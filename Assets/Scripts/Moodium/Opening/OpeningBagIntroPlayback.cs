using System.Collections;
using UnityEngine;
using UnityEngine.Video;

namespace Moodium.Opening
{
    /// <summary>Small, independent one-shot panel displayed beside the opening video.</summary>
    public sealed class OpeningBagIntroPlayback : MonoBehaviour
    {
        readonly OpeningBagIntroPlaybackGate m_Gate = new();

        VideoPlayer m_Player;
        RenderTexture m_RenderTexture;
        Material m_Material;
        bool m_CleanedUp;

        public static OpeningBagIntroPlayback CreateAndPlay(VideoClip clip, Transform mainVideoDisplay)
        {
            if (clip == null || mainVideoDisplay == null)
                return null;

            var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panel.name = "Moodium Bag Intro Video Display";
            // Deliberately overlap the left edge of the main video so the two
            // tutorial clips read as one paired composition rather than cards.
            panel.transform.position = mainVideoDisplay.TransformPoint(new Vector3(-0.48f, 0f, -0.015f));
            panel.transform.rotation = mainVideoDisplay.rotation;
            panel.transform.localScale = new Vector3(0.56f, 0.315f, 1f);
            Destroy(panel.GetComponent<Collider>());

            var playback = panel.AddComponent<OpeningBagIntroPlayback>();
            playback.Begin(clip);
            return playback;
        }

        void Begin(VideoClip clip)
        {
            if (!m_Gate.TryBegin(hasHardware: true))
                return;

            m_RenderTexture = new RenderTexture(640, 360, 0, RenderTextureFormat.ARGB32)
            {
                name = "Moodium Bag Intro Video Render Texture"
            };
            m_RenderTexture.Create();

            m_Material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture"));
            if (m_Material.HasProperty("_BaseMap"))
                m_Material.SetTexture("_BaseMap", m_RenderTexture);
            if (m_Material.HasProperty("_MainTex"))
                m_Material.SetTexture("_MainTex", m_RenderTexture);
            OpeningVideoSurfaceSettings.TransparentAlpha.ApplyTo(m_Material);
            GetComponent<Renderer>().sharedMaterial = m_Material;

            gameObject.AddComponent<PolySpatialVideoRenderTextureUpdater>().Configure(m_RenderTexture);
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

        IEnumerator PrepareAndPlay()
        {
            m_Player.Prepare();
            var deadline = Time.realtimeSinceStartup + 15f;
            while (!m_Player.isPrepared && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (m_Player == null || !m_Player.isPrepared)
            {
                Debug.LogWarning("[Moodium Opening] BagIntro.mp4 could not be prepared; hiding its panel.");
                FinishAndDestroy();
                yield break;
            }

            Debug.Log("[Moodium Opening] BagIntro one-shot started.");
            m_Player.Play();
        }

        void OnPlaybackFinished(VideoPlayer _)
        {
            Debug.Log("[Moodium Opening] BagIntro one-shot finished.");
            FinishAndDestroy();
        }

        void FinishAndDestroy()
        {
            if (m_CleanedUp)
                return;
            m_CleanedUp = true;
            m_Gate.Complete();
            ReleaseResources();
            gameObject.SetActive(false);
            Destroy(gameObject);
        }

        void ReleaseResources()
        {
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
            if (m_Material != null)
                Destroy(m_Material);
        }

        void OnDestroy()
        {
            if (m_CleanedUp)
                return;
            m_CleanedUp = true;
            m_Gate.Complete();
            ReleaseResources();
        }
    }

    /// <summary>Testable lifecycle guard for the optional hardware-only one-shot.</summary>
    public sealed class OpeningBagIntroPlaybackGate
    {
        bool m_HasStarted;
        public bool IsVisible { get; private set; }

        public bool TryBegin(bool hasHardware)
        {
            if (!hasHardware || m_HasStarted)
                return false;
            m_HasStarted = true;
            IsVisible = true;
            return true;
        }

        public void Complete()
        {
            IsVisible = false;
        }
    }
}
