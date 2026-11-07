using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using RenderHeads.Media.AVProVideo;
using UnityEngine;

namespace VFXViewer
{
    [DisallowMultipleComponent]
    public sealed class AVProSafePlayback : MonoBehaviour
    {
        private const int MaxOpenAttempts = 2;
        private const float MetadataTimeoutSeconds = 4.0f;
        private const float FirstFrameTimeoutSeconds = 3.0f;
        private const float RetryDelaySeconds = 0.25f;
        private const float MatrixEpsilon = 0.0001f;

        private readonly List<RendererState> m_HiddenRenderers = new List<RendererState>();
        private Coroutine m_PlayRoutine;
        private int m_PlayToken;

        public static void Play(MediaPlayer mediaPlayer, GameObject renderRoot, string owner, string source)
        {
            if (mediaPlayer == null)
                return;

            ConfigureManualOpen(mediaPlayer);

            AVProSafePlayback playback = mediaPlayer.GetComponent<AVProSafePlayback>();
            if (playback == null)
                playback = mediaPlayer.gameObject.AddComponent<AVProSafePlayback>();

            playback.BeginPlay(mediaPlayer, renderRoot, owner, source);
        }

        private void OnDisable()
        {
            RestoreHiddenRenderers();
        }

        private void OnDestroy()
        {
            RestoreHiddenRenderers();
        }

        private void BeginPlay(MediaPlayer mediaPlayer, GameObject renderRoot, string owner, string source)
        {
            m_PlayToken++;

            if (m_PlayRoutine != null)
            {
                StopCoroutine(m_PlayRoutine);
                m_PlayRoutine = null;
            }

            RestoreHiddenRenderers();

            if (!isActiveAndEnabled)
            {
                Log("cannot_start_coroutine", mediaPlayer, owner, source, "reason=helper_inactive");
                return;
            }

            m_PlayRoutine = StartCoroutine(PlayRoutine(mediaPlayer, renderRoot, owner, source, m_PlayToken));
        }

        private IEnumerator PlayRoutine(MediaPlayer mediaPlayer, GameObject renderRoot, string owner, string source, int token)
        {
            Log("begin", mediaPlayer, owner, source, DescribeMediaState(mediaPlayer));
            HideVideoRenderers(mediaPlayer, renderRoot);
            AVProMediaDiagnostics.EnsureForMediaPlayer(mediaPlayer, owner, source + ".safe_begin");

            string metadataReason = string.Empty;
            string textureReason = string.Empty;

            for (int attempt = 1; attempt <= MaxOpenAttempts; attempt++)
            {
                if (token != m_PlayToken)
                    yield break;

                ConfigureManualOpen(mediaPlayer);

                if (mediaPlayer.MediaOpened)
                {
                    AVProMediaDiagnostics.LogCloseRequest(mediaPlayer, owner, source + $".safe_close_before_open.attempt{attempt}");
                    mediaPlayer.CloseMedia();
                    AVProMediaDiagnostics.LogCloseResult(mediaPlayer, owner, source + $".safe_close_before_open.attempt{attempt}");
                    yield return null;
                    yield return null;
                }

                ConfigureManualOpen(mediaPlayer);

                bool opened = mediaPlayer.OpenMedia(autoPlay: false);

                if (!opened)
                {
                    Log("open_failed", mediaPlayer, owner, source, $"attempt={attempt}, {DescribeMediaState(mediaPlayer)}");
                    yield return new WaitForSecondsRealtime(RetryDelaySeconds);
                    continue;
                }

                bool metadataReady = false;
                float metadataStart = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - metadataStart < MetadataTimeoutSeconds)
                {
                    if (TryValidateMetadata(mediaPlayer, out metadataReason))
                    {
                        metadataReady = true;
                        break;
                    }

                    yield return null;
                }

                if (!metadataReady)
                {
                    Log("invalid_metadata", mediaPlayer, owner, source, $"attempt={attempt}, reason={metadataReason}, {DescribeMediaState(mediaPlayer)}");
                    yield return new WaitForSecondsRealtime(RetryDelaySeconds);
                    continue;
                }

                mediaPlayer.Play();

                bool textureReady = false;
                float frameStart = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - frameStart < FirstFrameTimeoutSeconds)
                {
                    if (TryValidateTexture(mediaPlayer, out textureReason))
                    {
                        textureReady = true;
                        break;
                    }

                    yield return null;
                }

                if (textureReady)
                {
                    ForceApplyTargets(mediaPlayer, renderRoot);
                    yield return null;
                    ForceApplyTargets(mediaPlayer, renderRoot);
                    RestoreHiddenRenderers();
                    Log("ready", mediaPlayer, owner, source, $"attempt={attempt}, {DescribeMediaState(mediaPlayer)}");
                    m_PlayRoutine = null;
                    yield break;
                }

                Log("invalid_texture", mediaPlayer, owner, source, $"attempt={attempt}, reason={textureReason}, {DescribeMediaState(mediaPlayer)}");
                mediaPlayer.Pause();
                yield return new WaitForSecondsRealtime(RetryDelaySeconds);
            }

            AVProMediaDiagnostics.LogCloseRequest(mediaPlayer, owner, source + ".safe_failed_close");
            mediaPlayer.CloseMedia();
            AVProMediaDiagnostics.LogCloseResult(mediaPlayer, owner, source + ".safe_failed_close");

            string message = $"[AVProSafePlayback] failed owner={owner}, source={source}, metadataReason={metadataReason}, textureReason={textureReason}";
            PlacementDebugFileLogger.Log(message);
            Debug.LogWarning(message, this);
            m_PlayRoutine = null;
        }

        private static void ConfigureManualOpen(MediaPlayer mediaPlayer)
        {
            if (mediaPlayer == null)
                return;

            mediaPlayer.AutoOpen = false;
            mediaPlayer.AutoStart = false;
        }

        private void HideVideoRenderers(MediaPlayer mediaPlayer, GameObject renderRoot)
        {
            GameObject root = renderRoot != null ? renderRoot : (mediaPlayer != null ? mediaPlayer.gameObject : null);
            if (root == null || mediaPlayer == null)
                return;

            ApplyToMesh[] applyTargets = root.GetComponentsInChildren<ApplyToMesh>(true);
            if (applyTargets == null)
                return;

            for (int i = 0; i < applyTargets.Length; i++)
            {
                ApplyToMesh apply = applyTargets[i];
                if (apply == null || apply.Player != mediaPlayer)
                    continue;

                Renderer renderer = apply.MeshRenderer;
                if (renderer == null || ContainsRenderer(renderer))
                    continue;

                m_HiddenRenderers.Add(new RendererState(renderer, renderer.enabled));
                renderer.enabled = false;
            }
        }

        private bool ContainsRenderer(Renderer renderer)
        {
            for (int i = 0; i < m_HiddenRenderers.Count; i++)
            {
                if (m_HiddenRenderers[i].Renderer == renderer)
                    return true;
            }

            return false;
        }

        private void RestoreHiddenRenderers()
        {
            for (int i = 0; i < m_HiddenRenderers.Count; i++)
            {
                RendererState state = m_HiddenRenderers[i];
                if (state.Renderer != null)
                    state.Renderer.enabled = state.Enabled;
            }

            m_HiddenRenderers.Clear();
        }

        private static void ForceApplyTargets(MediaPlayer mediaPlayer, GameObject renderRoot)
        {
            GameObject root = renderRoot != null ? renderRoot : (mediaPlayer != null ? mediaPlayer.gameObject : null);
            if (root == null || mediaPlayer == null)
                return;

            ApplyToBase[] applyTargets = root.GetComponentsInChildren<ApplyToBase>(true);
            if (applyTargets == null)
                return;

            for (int i = 0; i < applyTargets.Length; i++)
            {
                ApplyToBase apply = applyTargets[i];
                if (apply != null && apply.Player == mediaPlayer)
                    apply.ForceUpdate();
            }
        }

        private static bool TryValidateMetadata(MediaPlayer mediaPlayer, out string reason)
        {
            reason = string.Empty;

            if (mediaPlayer == null)
            {
                reason = "mediaPlayer=null";
                return false;
            }

            if (!mediaPlayer.MediaOpened)
            {
                reason = "mediaOpened=false";
                return false;
            }

            if (mediaPlayer.Info == null)
            {
                reason = "info=null";
                return false;
            }

            if (!SafeBool(() => mediaPlayer.Info.HasVideo()))
            {
                reason = "hasVideo=false";
                return false;
            }

            int width = SafeInt(() => mediaPlayer.Info.GetVideoWidth());
            int height = SafeInt(() => mediaPlayer.Info.GetVideoHeight());
            if (width <= 0 || height <= 0)
            {
                reason = $"videoSize={width}x{height}";
                return false;
            }

            if (mediaPlayer.Control == null)
            {
                reason = "control=null";
                return false;
            }

            if (!SafeBool(() => mediaPlayer.Control.CanPlay()))
            {
                reason = "canPlay=false";
                return false;
            }

            return true;
        }

        private static bool TryValidateTexture(MediaPlayer mediaPlayer, out string reason)
        {
            reason = string.Empty;

            if (mediaPlayer == null || mediaPlayer.TextureProducer == null)
            {
                reason = "textureProducer=null";
                return false;
            }

            int textureCount = SafeInt(() => mediaPlayer.TextureProducer.GetTextureCount());
            if (textureCount <= 0)
            {
                reason = $"textureCount={textureCount}";
                return false;
            }

            Texture texture = null;
            try
            {
                texture = mediaPlayer.TextureProducer.GetTexture(0);
            }
            catch (Exception ex)
            {
                reason = "textureError=" + ex.GetType().Name;
                return false;
            }

            if (texture == null)
            {
                reason = "texture=null";
                return false;
            }

            if (texture.width <= 16 || texture.height <= 16)
            {
                reason = $"textureSize={texture.width}x{texture.height}";
                return false;
            }

            Matrix4x4 matrix;
            try
            {
                matrix = mediaPlayer.TextureProducer.GetTextureMatrix();
            }
            catch (Exception ex)
            {
                reason = "textureMatrixError=" + ex.GetType().Name;
                return false;
            }

            if (IsDegenerateTextureMatrix(matrix))
            {
                reason = "textureMatrixDegenerate=" + FormatMatrix2x3(matrix);
                return false;
            }

            return true;
        }

        private static bool IsDegenerateTextureMatrix(Matrix4x4 matrix)
        {
            float basisMagnitude =
                Mathf.Abs(matrix.m00) +
                Mathf.Abs(matrix.m01) +
                Mathf.Abs(matrix.m10) +
                Mathf.Abs(matrix.m11);
            return basisMagnitude <= MatrixEpsilon;
        }

        private static string DescribeMediaState(MediaPlayer mediaPlayer)
        {
            if (mediaPlayer == null)
                return "mediaPlayer=null";

            var sb = new StringBuilder(256);
            sb.Append("mediaOpened=").Append(SafeString(() => mediaPlayer.MediaOpened.ToString()));
            sb.Append(", autoOpen=").Append(SafeString(() => mediaPlayer.AutoOpen.ToString()));
            sb.Append(", autoStart=").Append(SafeString(() => mediaPlayer.AutoStart.ToString()));
            sb.Append(", canPlay=").Append(SafeString(() => mediaPlayer.Control != null ? mediaPlayer.Control.CanPlay().ToString() : "control=null"));
            sb.Append(", isPlaying=").Append(SafeString(() => mediaPlayer.Control != null ? mediaPlayer.Control.IsPlaying().ToString() : "control=null"));
            sb.Append(", videoSize=").Append(SafeString(() => mediaPlayer.Info != null ? mediaPlayer.Info.GetVideoWidth() + "x" + mediaPlayer.Info.GetVideoHeight() : "info=null"));
            sb.Append(", fps=").Append(SafeString(() => mediaPlayer.Info != null ? mediaPlayer.Info.GetVideoFrameRate().ToString("F3") : "info=null"));
            sb.Append(", textureCount=").Append(SafeString(() => mediaPlayer.TextureProducer != null ? mediaPlayer.TextureProducer.GetTextureCount().ToString() : "producer=null"));
            sb.Append(", textureMatrix=").Append(SafeString(() => mediaPlayer.TextureProducer != null ? FormatMatrix2x3(mediaPlayer.TextureProducer.GetTextureMatrix()) : "producer=null"));
            return sb.ToString();
        }

        private static string FormatMatrix2x3(Matrix4x4 matrix)
        {
            return matrix.m00.ToString("F4") + "/" +
                   matrix.m01.ToString("F4") + "/" +
                   matrix.m03.ToString("F4") + "/" +
                   matrix.m10.ToString("F4") + "/" +
                   matrix.m11.ToString("F4") + "/" +
                   matrix.m13.ToString("F4");
        }

        private static void Log(string eventName, MediaPlayer mediaPlayer, string owner, string source, string details)
        {
            string playerPath = mediaPlayer != null ? GetHierarchyPath(mediaPlayer.transform) : "<null>";
            PlacementDebugFileLogger.Log(
                $"[AVProSafePlayback] event={eventName}, owner={owner}, source={source}, player={playerPath}, {details}");
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
                return "<null>";

            var sb = new StringBuilder(transform.name);
            Transform current = transform.parent;
            while (current != null)
            {
                sb.Insert(0, current.name + "/");
                current = current.parent;
            }

            return sb.ToString();
        }

        private static bool SafeBool(Func<bool> getter)
        {
            try
            {
                return getter();
            }
            catch
            {
                return false;
            }
        }

        private static int SafeInt(Func<int> getter)
        {
            try
            {
                return getter();
            }
            catch
            {
                return 0;
            }
        }

        private static string SafeString(Func<string> getter)
        {
            try
            {
                return getter();
            }
            catch (Exception ex)
            {
                return "err=" + ex.GetType().Name;
            }
        }

        private struct RendererState
        {
            public readonly Renderer Renderer;
            public readonly bool Enabled;

            public RendererState(Renderer renderer, bool enabled)
            {
                Renderer = renderer;
                Enabled = enabled;
            }
        }
    }
}
