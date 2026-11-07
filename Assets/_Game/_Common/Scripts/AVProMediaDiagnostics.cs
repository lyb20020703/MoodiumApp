using System;
using System.Text;
using RenderHeads.Media.AVProVideo;
using UnityEngine;

namespace VFXViewer
{
    [DisallowMultipleComponent]
    public sealed class AVProMediaDiagnostics : MonoBehaviour
    {
        private static float s_LastCloseRealtime = -1f;
        private static int s_LastCloseFrame = -1;
        private static string s_LastCloseOwner = string.Empty;
        private static string s_LastClosePlayer = string.Empty;

        private MediaPlayer m_MediaPlayer;
        private string m_Owner = string.Empty;
        private bool m_Registered;

        public static void EnsureForHierarchy(GameObject root, string owner, string source)
        {
            if (root == null)
                return;

            MediaPlayer[] mediaPlayers = root.GetComponentsInChildren<MediaPlayer>(true);
            if (mediaPlayers == null)
                return;

            for (int i = 0; i < mediaPlayers.Length; i++)
                EnsureForMediaPlayer(mediaPlayers[i], owner, source);
        }

        public static AVProMediaDiagnostics EnsureForMediaPlayer(MediaPlayer mediaPlayer, string owner, string source)
        {
            if (mediaPlayer == null)
                return null;

            AVProMediaDiagnostics diagnostics = mediaPlayer.GetComponent<AVProMediaDiagnostics>();
            if (diagnostics == null)
                diagnostics = mediaPlayer.gameObject.AddComponent<AVProMediaDiagnostics>();

            diagnostics.Bind(mediaPlayer, owner);
            return diagnostics;
        }

        public static void LogPlayRequest(MediaPlayer mediaPlayer, string owner, string source)
        {
            EnsureForMediaPlayer(mediaPlayer, owner, source);
        }

        public static void LogPlayResult(MediaPlayer mediaPlayer, string owner, string source)
        {
            EnsureForMediaPlayer(mediaPlayer, owner, source);
        }

        public static void LogCloseRequest(MediaPlayer mediaPlayer, string owner, string source)
        {
            AVProMediaDiagnostics diagnostics = EnsureForMediaPlayer(mediaPlayer, owner, source);
            diagnostics?.MarkCloseBoundary();
        }

        public static void LogCloseResult(MediaPlayer mediaPlayer, string owner, string source)
        {
            EnsureForMediaPlayer(mediaPlayer, owner, source);
        }

        private void Awake()
        {
            if (m_MediaPlayer == null)
                m_MediaPlayer = GetComponent<MediaPlayer>();

            RegisterEvents();
        }

        private void OnEnable()
        {
            RegisterEvents();
        }

        private void OnDisable()
        {
            UnregisterEvents();
        }

        private void OnDestroy()
        {
            UnregisterEvents();
        }

        private void Bind(MediaPlayer mediaPlayer, string owner)
        {
            if (m_MediaPlayer != mediaPlayer)
            {
                UnregisterEvents();
                m_MediaPlayer = mediaPlayer;
            }

            if (!string.IsNullOrWhiteSpace(owner))
                m_Owner = owner;

            RegisterEvents();
        }

        private void RegisterEvents()
        {
            if (m_Registered || m_MediaPlayer == null)
                return;

            m_MediaPlayer.Events.AddListener(OnMediaPlayerEvent);
            m_Registered = true;
        }

        private void UnregisterEvents()
        {
            if (!m_Registered || m_MediaPlayer == null)
                return;

            m_MediaPlayer.Events.RemoveListener(OnMediaPlayerEvent);
            m_Registered = false;
        }

        private void OnMediaPlayerEvent(MediaPlayer mediaPlayer, MediaPlayerEvent.EventType eventType, ErrorCode errorCode)
        {
            if (eventType == MediaPlayerEvent.EventType.Closing)
            {
                MarkCloseBoundary();
                return;
            }

            if (eventType != MediaPlayerEvent.EventType.Error &&
                eventType != MediaPlayerEvent.EventType.Stalled)
            {
                return;
            }

            string message = BuildCompactSnapshot($"avpro_abnormal_event eventType={eventType}, errorCode={errorCode}");
            PlacementDebugFileLogger.Log(message);
            Debug.LogWarning(message, this);
        }

        private void MarkCloseBoundary()
        {
            s_LastCloseRealtime = Time.realtimeSinceStartup;
            s_LastCloseFrame = Time.frameCount;
            s_LastCloseOwner = m_Owner;
            s_LastClosePlayer = GetHierarchyPath(m_MediaPlayer != null ? m_MediaPlayer.transform : transform);
        }

        private string BuildCompactSnapshot(string source)
        {
            var sb = new StringBuilder(512);
            MediaPlayer mediaPlayer = m_MediaPlayer != null ? m_MediaPlayer : GetComponent<MediaPlayer>();

            sb.Append("[AVProDiag] source=").Append(source);
            sb.Append(", owner=").Append(string.IsNullOrWhiteSpace(m_Owner) ? "<none>" : m_Owner);
            sb.Append(", frame=").Append(Time.frameCount);
            sb.Append(", player=").Append(GetHierarchyPath(mediaPlayer != null ? mediaPlayer.transform : transform));

            if (s_LastCloseRealtime >= 0f)
            {
                sb.Append(", lastCloseDeltaMs=").Append(((Time.realtimeSinceStartup - s_LastCloseRealtime) * 1000f).ToString("F1"));
                sb.Append(", lastCloseFrameDelta=").Append(Time.frameCount - s_LastCloseFrame);
                sb.Append(", lastCloseOwner=").Append(string.IsNullOrWhiteSpace(s_LastCloseOwner) ? "<none>" : s_LastCloseOwner);
                sb.Append(", lastClosePlayer=").Append(string.IsNullOrWhiteSpace(s_LastClosePlayer) ? "<none>" : s_LastClosePlayer);
            }

            if (mediaPlayer == null)
            {
                sb.Append(", mediaPlayer=null");
                return sb.ToString();
            }

            sb.Append(", mediaOpened=").Append(Safe(() => mediaPlayer.MediaOpened.ToString(), "err"));
            sb.Append(", mediaPath=").Append(Safe(() => GetMediaPath(mediaPlayer), "err"));
            sb.Append(", canPlay=").Append(Safe(() => mediaPlayer.Control != null ? mediaPlayer.Control.CanPlay().ToString() : "control=null", "err"));
            sb.Append(", isPlaying=").Append(Safe(() => mediaPlayer.Control != null ? mediaPlayer.Control.IsPlaying().ToString() : "control=null", "err"));
            sb.Append(", hasVideo=").Append(Safe(() => mediaPlayer.Info != null ? mediaPlayer.Info.HasVideo().ToString() : "info=null", "err"));
            sb.Append(", videoSize=").Append(Safe(() => mediaPlayer.Info != null ? mediaPlayer.Info.GetVideoWidth() + "x" + mediaPlayer.Info.GetVideoHeight() : "info=null", "err"));
            sb.Append(", videoFps=").Append(Safe(() => mediaPlayer.Info != null ? mediaPlayer.Info.GetVideoFrameRate().ToString("F3") : "info=null", "err"));
            sb.Append(", stalled=").Append(Safe(() => mediaPlayer.Info != null ? mediaPlayer.Info.IsPlaybackStalled().ToString() : "info=null", "err"));
            sb.Append(", textureCount=").Append(Safe(() => mediaPlayer.TextureProducer != null ? mediaPlayer.TextureProducer.GetTextureCount().ToString() : "producer=null", "err"));
            sb.Append(", textureFrameCount=").Append(Safe(() => mediaPlayer.TextureProducer != null ? mediaPlayer.TextureProducer.GetTextureFrameCount().ToString() : "producer=null", "err"));
            sb.Append(", textureMatrix=").Append(Safe(() => mediaPlayer.TextureProducer != null ? FormatMatrix2x3(mediaPlayer.TextureProducer.GetTextureMatrix()) : "producer=null", "err"));
            sb.Append(", texture0=").Append(Safe(() => DescribeTexture(mediaPlayer), "err"));
            return sb.ToString();
        }

        private static string GetMediaPath(MediaPlayer mediaPlayer)
        {
            MediaReference mediaReference = mediaPlayer.MediaReference;
            if (mediaReference == null)
                return "<null>";

            MediaReference currentReference = mediaReference.GetCurrentPlatformMediaReference();
            MediaPath mediaPath = currentReference != null ? currentReference.MediaPath : mediaReference.MediaPath;
            return mediaPath != null ? mediaPath.Path : "<null>";
        }

        private static string DescribeTexture(MediaPlayer mediaPlayer)
        {
            Texture texture = mediaPlayer.TextureProducer.GetTexture(0);
            if (texture == null)
                return "null";

            IntPtr nativePtr = texture.GetNativeTexturePtr();
            string ptrText = nativePtr == IntPtr.Zero ? "0" : "0x" + nativePtr.ToInt64().ToString("X");
            return texture.width + "x" + texture.height +
                ",id=" + texture.GetInstanceID() +
                ",ptr=" + ptrText;
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

        private static string Safe(Func<string> valueFactory, string fallback)
        {
            try
            {
                return valueFactory != null ? valueFactory() : fallback;
            }
            catch (Exception ex)
            {
                return fallback + ":" + ex.GetType().Name;
            }
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
                return "<null>";

            var sb = new StringBuilder(transform.name);
            Transform current = transform.parent;
            int depth = 0;
            while (current != null && depth++ < 8)
            {
                sb.Insert(0, current.name + "/");
                current = current.parent;
            }

            if (current != null)
                sb.Insert(0, ".../");

            return sb.ToString();
        }
    }
}
