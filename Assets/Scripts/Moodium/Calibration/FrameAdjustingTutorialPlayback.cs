using System.Collections;
using Moodium.Opening;
using TMPro;
using UnityEngine;
using UnityEngine.Video;

namespace Moodium.Calibration
{
    /// <summary>One-shot calibration tutorial displayed at the opening video's established pose.</summary>
    [DisallowMultipleComponent]
    public sealed class FrameAdjustingTutorialPlayback : MonoBehaviour
    {
        VideoPlayer m_Player;
        RenderTexture m_RenderTexture;
        Material m_Material;
        TextMeshPro m_Subtitle;
        bool m_English;
        bool m_CleanedUp;

        public static FrameAdjustingTutorialPlayback CreateAndPlay(
            VideoClip clip, Transform openingDisplay, bool english, TMP_FontAsset font)
        {
            if (clip == null || openingDisplay == null)
                return null;

            var display = GameObject.CreatePrimitive(PrimitiveType.Quad);
            display.name = "Frame Adjusting Tutorial Video Display";
            display.transform.SetPositionAndRotation(openingDisplay.position, openingDisplay.rotation);
            display.transform.localScale = openingDisplay.localScale;
            Destroy(display.GetComponent<Collider>());

            var playback = display.AddComponent<FrameAdjustingTutorialPlayback>();
            playback.Begin(clip, english, font);
            return playback;
        }

        void Begin(VideoClip clip, bool english, TMP_FontAsset font)
        {
            m_English = english;
            m_RenderTexture = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32)
            {
                name = "Moodium Frame Adjusting Tutorial Render Texture"
            };
            m_RenderTexture.Create();

            m_Material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture"));
            if (m_Material.HasProperty("_BaseMap")) m_Material.SetTexture("_BaseMap", m_RenderTexture);
            if (m_Material.HasProperty("_MainTex")) m_Material.SetTexture("_MainTex", m_RenderTexture);
            OpeningVideoSurfaceSettings.TransparentAlpha.ApplyTo(m_Material);
            GetComponent<Renderer>().sharedMaterial = m_Material;

            gameObject.AddComponent<PolySpatialVideoRenderTextureUpdater>().Configure(m_RenderTexture);
            CreateSubtitle(english, font);

            m_Player = gameObject.AddComponent<VideoPlayer>();
            m_Player.playOnAwake = false;
            m_Player.isLooping = false;
            m_Player.source = VideoSource.VideoClip;
            m_Player.clip = clip;
            m_Player.renderMode = VideoRenderMode.RenderTexture;
            m_Player.targetTexture = m_RenderTexture;
            m_Player.loopPointReached += OnPlaybackFinished;
            StartCoroutine(PrepareAndPlay());
        }

        void CreateSubtitle(bool english, TMP_FontAsset font)
        {
            var subtitleRoot = new GameObject("Frame Adjusting Tutorial Subtitle");
            subtitleRoot.transform.SetParent(transform, false);
            subtitleRoot.transform.localPosition = new Vector3(0f, -0.39f, -0.03f);
            subtitleRoot.transform.localRotation = Quaternion.identity;
            subtitleRoot.transform.localScale = new Vector3(
                SafeScaleDivide(0.045f, transform.localScale.x),
                SafeScaleDivide(0.045f, transform.localScale.y),
                SafeScaleDivide(0.045f, transform.localScale.z));
            m_Subtitle = subtitleRoot.AddComponent<TextMeshPro>();
            m_Subtitle.font = font;
            m_Subtitle.fontSize = 7f;
            m_Subtitle.alignment = TextAlignmentOptions.Center;
            m_Subtitle.rectTransform.sizeDelta = new Vector2(24f, 4f);
            m_Subtitle.text = english ? "fit the frame around your sensory bag" : "把边框调整到和感官袋一样大吧";
            m_Subtitle.color = Color.white;
            m_Subtitle.outlineWidth = .08f;
            m_Subtitle.outlineColor = new Color(.08f, .02f, .16f, .85f);
            m_Subtitle.enabled = false;
        }

        IEnumerator PrepareAndPlay()
        {
            m_Player.Prepare();
            var deadline = Time.realtimeSinceStartup + 15f;
            while (m_Player != null && !m_Player.isPrepared && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (m_Player == null || !m_Player.isPrepared)
            {
                Debug.LogWarning("[Sensory Bag Calibration] Frame-adjusting tutorial could not be prepared.");
                StopAndDestroy();
                yield break;
            }
            m_Player.Play();
        }

        void Update()
        {
            if (m_Player == null || m_Subtitle == null)
                return;
            var frame = m_Player.frame >= 0
                ? m_Player.frame
                : (long)System.Math.Round(m_Player.time * FrameAdjustingTutorialTimeline.FramesPerSecond);
            var segment = FrameAdjustingTutorialTimeline.ForLanguage(m_English);
            m_Subtitle.enabled = frame >= segment.StartFrame && frame < segment.EndFrame;
        }

        void OnPlaybackFinished(VideoPlayer _) => StopAndDestroy();

        public void StopAndDestroy()
        {
            if (m_CleanedUp) return;
            m_CleanedUp = true;
            if (m_Player != null)
            {
                m_Player.loopPointReached -= OnPlaybackFinished;
                m_Player.Stop();
                m_Player.targetTexture = null;
            }
            if (m_RenderTexture != null)
            {
                m_RenderTexture.Release();
                Destroy(m_RenderTexture);
            }
            if (m_Material != null) Destroy(m_Material);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (m_CleanedUp) return;
            m_CleanedUp = true;
            if (m_Player != null)
            {
                m_Player.loopPointReached -= OnPlaybackFinished;
                m_Player.Stop();
                m_Player.targetTexture = null;
            }
            if (m_RenderTexture != null)
                m_RenderTexture.Release();
            if (m_Material != null)
                Destroy(m_Material);
        }

        static float SafeScaleDivide(float value, float divisor) =>
            Mathf.Abs(divisor) > .0001f ? value / divisor : value;
    }

    public static class FrameAdjustingTutorialTimeline
    {
        public const int FramesPerSecond = 30;
        public readonly struct Segment
        {
            public Segment(long startFrame, long endFrame) { StartFrame = startFrame; EndFrame = endFrame; }
            public long StartFrame { get; }
            public long EndFrame { get; }
        }

        public static readonly Segment ChineseSubtitle = new(ToFrame(0, 2, 8), ToFrame(0, 5, 11));
        public static readonly Segment EnglishSubtitle = new(ToFrame(0, 2, 8), ToFrame(0, 5, 11));
        public static Segment ForLanguage(bool english) => english ? EnglishSubtitle : ChineseSubtitle;
        public static long ToFrame(int minutes, int seconds, int frames) =>
            ((long)minutes * 60L + seconds) * FramesPerSecond + frames;
    }
}
