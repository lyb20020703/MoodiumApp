using System;
using System.Collections;
using TMPro;
using UnityEngine.UI;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

namespace Moodium.Opening
{
    [DisallowMultipleComponent]
    public sealed class MoodiumPortalEntranceController : MonoBehaviour
    {
        static readonly XRHandJointID[] ContactJoints =
        {
            XRHandJointID.ThumbTip,
            XRHandJointID.IndexTip,
            XRHandJointID.MiddleTip,
            XRHandJointID.RingTip,
            XRHandJointID.LittleTip,
            XRHandJointID.Wrist,
            XRHandJointID.IndexMetacarpal,
            XRHandJointID.MiddleMetacarpal,
            XRHandJointID.RingMetacarpal,
            XRHandJointID.LittleMetacarpal
        };

        [Header("Placement")]
        [SerializeField, Min(0.5f)] float m_DistanceFromUser = 1.35f;
        [SerializeField] float m_HeightOffset = -0.05f;

        [Header("Portal")]
        [SerializeField] GameObject m_VisualRoot;
        [SerializeField] Animator m_PortalAnimator;
        [SerializeField, Min(0.05f)] float m_OpeningDuration = 1.8f;
        [SerializeField, Min(0.05f)] float m_ExitDuration = 0.8f;

        [Header("Bag sequence")]
        [SerializeField] PortalFallingBagController m_BagRain;
        [SerializeField, Min(0f)] float m_BagBuildDuration = 5f;

        [Header("Entry object")]
        [SerializeField] Transform m_EntryModelRoot;
        [SerializeField] Animator m_EntryAnimator;
        [SerializeField] Vector3 m_EntryDeepLocalPosition = new(0f, -0.38f, 0.58f);
        [SerializeField, Min(0.05f)] float m_EntryEmergeDuration = 2.4f;
        [SerializeField, Min(0f)] float m_EntrySwayAmplitude = 0.08f;
        [SerializeField, Range(1, 4)] int m_EntrySwayCycles = 2;
        [SerializeField] Collider m_ManualContactCollider;
        [SerializeField] Collider m_EntryBagPusher;
        [SerializeField, Min(0f)] float m_ContactPadding = 0.035f;
        [SerializeField] GameObject m_EntryPrompt;
        [SerializeField] ParticleSystem m_EntryParticles;
        [SerializeField] AudioSource m_EntryAudio;
        [SerializeField] AudioClip m_PortalOpenClip;
        [SerializeField] AudioClip m_BagFallingClip;
        [SerializeField] AudioClip m_MoodiIntroVoiceClip;
        [SerializeField, Min(0f)] float m_MoodiIntroMinimumDuration = 3f;
        [SerializeField] TMP_FontAsset m_MoodiSubtitleFont;
        [SerializeField] Sprite m_LanguageButtonRoundedSprite;
        AudioSource m_PortalAudio;
        AudioSource m_MoodiVoiceAudio;
        [SerializeField] Texture2D m_TrailParticleTexture;

        readonly PortalEntranceStateMachine m_StateMachine = new();
        XRHandSubsystem m_HandSubsystem;
        Coroutine m_Sequence;
        Vector3 m_EntryModelInitialScale = Vector3.one;
        Vector3 m_EntryModelReadyLocalPosition;
        Transform[] m_PortalClouds = Array.Empty<Transform>();
        Vector3[] m_PortalCloudBasePositions = Array.Empty<Vector3>();
        ParticleSystem m_EntryTrailParticles;
        GameObject m_LanguageChoice;
        TMP_Text m_EntryPromptText;

        public event Action Completed;
        public PortalEntranceState State => m_StateMachine.State;

        void Awake()
        {
            ConfigureEntryTrail();
            ConfigureMoodiIntroSubtitle();
            ConfigureLanguageChoice();
            m_PortalAudio = gameObject.AddComponent<AudioSource>();
            m_PortalAudio.playOnAwake = false;
            m_PortalAudio.spatialBlend = 0f;
            m_PortalAudio.volume = 0.55f;
            m_MoodiVoiceAudio = gameObject.AddComponent<AudioSource>();
            m_MoodiVoiceAudio.playOnAwake = false;
            m_MoodiVoiceAudio.spatialBlend = 0f;
            m_MoodiVoiceAudio.volume = 0.8f;
            var cloudList = new System.Collections.Generic.List<Transform>();
            foreach (var child in GetComponentsInChildren<Transform>(true))
                if (child.name.StartsWith("PortalCloud_", StringComparison.Ordinal))
                    cloudList.Add(child);
            m_PortalClouds = cloudList.ToArray();
            m_PortalCloudBasePositions = new Vector3[m_PortalClouds.Length];
            for (var i = 0; i < m_PortalClouds.Length; i++)
                m_PortalCloudBasePositions[i] = m_PortalClouds[i].localPosition;
            if (m_EntryModelRoot != null)
            {
                m_EntryModelInitialScale = m_EntryModelRoot.localScale;
                m_EntryModelReadyLocalPosition = m_EntryModelRoot.localPosition;
            }
            if (m_EntryPrompt != null)
                m_EntryPrompt.SetActive(false);
            SetMoodiIntroVisible(false);
            SetLanguageChoiceVisible(false);
            if (m_BagRain != null)
                m_BagRain.enabled = false;
            SetBagPusherEnabled(false);
        }

        void Update()
        {
            for (var i = 0; i < m_PortalClouds.Length; i++)
            {
                if (m_PortalClouds[i] == null)
                    continue;
                var phase = i * 1.7f;
                var offset = Mathf.Sin(Time.unscaledTime * 0.8f + phase) * 0.012f;
                m_PortalClouds[i].localPosition = m_PortalCloudBasePositions[i] + Vector3.up * offset;
            }
            if (State != PortalEntranceState.Ready || !TryGetHandSubsystem())
                return;

            m_HandSubsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
            if (TryHandContact(m_HandSubsystem.leftHand) || TryHandContact(m_HandSubsystem.rightHand))
                return;

            if (m_EntryModelRoot != null)
            {
                var pulse = 1f + Mathf.Sin(Time.unscaledTime * 2.2f) * 0.035f;
                m_EntryModelRoot.localScale = m_EntryModelInitialScale * pulse;
            }
        }

        public void Show()
        {
            if (m_Sequence != null)
                StopCoroutine(m_Sequence);
            gameObject.SetActive(true);
            PlaceInFrontOfUser();
            m_StateMachine.Reset();
            if (m_VisualRoot != null)
                m_VisualRoot.SetActive(true);
            if (m_EntryPrompt != null)
                m_EntryPrompt.SetActive(false);
            SetMoodiIntroVisible(false);
            if (m_EntryModelRoot != null)
            {
                m_EntryModelRoot.gameObject.SetActive(false);
                m_EntryModelRoot.localPosition = m_EntryDeepLocalPosition;
                m_EntryModelRoot.localScale = m_EntryModelInitialScale * 0.5f;
            }
            if (m_BagRain != null)
                m_BagRain.enabled = false;
            SetBagPusherEnabled(false);
            m_PortalAnimator?.Rebind();
            if (m_PortalAnimator != null)
                m_PortalAnimator.speed = 1f;
            m_PortalAnimator?.Update(0f);
            m_StateMachine.BeginOpening();
            if (m_PortalOpenClip != null)
                m_PortalAudio.PlayOneShot(m_PortalOpenClip);
            m_Sequence = StartCoroutine(OpenSequence());
        }

        public void HideImmediate()
        {
            if (m_Sequence != null)
            {
                StopCoroutine(m_Sequence);
                m_Sequence = null;
            }
            m_StateMachine.Reset();
            if (m_VisualRoot != null)
                m_VisualRoot.SetActive(false);
            if (m_EntryPrompt != null)
                m_EntryPrompt.SetActive(false);
            if (m_BagRain != null)
                m_BagRain.enabled = false;
            SetBagPusherEnabled(false);
            gameObject.SetActive(false);
        }

        public bool TryAcceptContact(Vector3 worldPoint)
        {
            if (!ContainsContactPoint(worldPoint) || !m_StateMachine.TryBeginEntering())
                return false;

            if (m_EntryPrompt != null)
                m_EntryPrompt.SetActive(false);
            m_EntryParticles?.Play(true);
            m_EntryAudio?.Play();
            if (m_Sequence != null)
                StopCoroutine(m_Sequence);
            m_Sequence = StartCoroutine(ExitSequence());
            return true;
        }

        public bool TryGetEntryBounds(out Bounds bounds)
        {
            if (m_ManualContactCollider != null)
            {
                bounds = m_ManualContactCollider.bounds;
                bounds.Expand(m_ContactPadding * 2f);
                return true;
            }

            if (m_EntryModelRoot == null)
            {
                bounds = default;
                return false;
            }

            var renderers = m_EntryModelRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                bounds = new Bounds(m_EntryModelRoot.position, Vector3.one * 0.2f);
                bounds.Expand(m_ContactPadding * 2f);
                return true;
            }

            bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            bounds.Expand(m_ContactPadding * 2f);
            return true;
        }

        public void BeginOpeningForTests()
        {
            m_StateMachine.Reset();
            m_StateMachine.BeginOpening();
        }

        public void NotifyOpeningCompleteForTests()
        {
            m_StateMachine.MarkBagFalling();
            m_StateMachine.MarkEntryEmerging();
            MarkReady();
        }

        public void CompleteTransitionForTests() => FinishTransition();

        IEnumerator OpenSequence()
        {
            yield return new WaitForSeconds(m_OpeningDuration);
            m_StateMachine.MarkBagFalling();
            if (m_BagRain != null)
            {
                m_BagRain.enabled = true;
                if (m_BagFallingClip != null)
                {
                    m_PortalAudio.clip = m_BagFallingClip;
                    m_PortalAudio.loop = true;
                    m_PortalAudio.volume = 0.2f;
                    m_PortalAudio.Play();
                }
            }
            yield return new WaitForSeconds(m_BagBuildDuration);
            m_StateMachine.MarkEntryEmerging();
            yield return EmergeEntryModel();
            m_Sequence = null;
            MarkReady();
        }

        IEnumerator EmergeEntryModel()
        {
            if (m_EntryModelRoot == null)
                yield break;

            m_EntryModelRoot.gameObject.SetActive(true);
            if (m_EntryTrailParticles != null)
            {
                m_EntryTrailParticles.Clear(true);
                m_EntryTrailParticles.Play(true);
            }
            SetBagPusherEnabled(true);
            m_EntryAnimator?.Rebind();
            m_EntryAnimator?.Update(0f);
            var elapsed = 0f;
            while (elapsed < m_EntryEmergeDuration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.SmoothStep(0f, 1f, elapsed / m_EntryEmergeDuration);
                var position = Vector3.Lerp(m_EntryDeepLocalPosition, m_EntryModelReadyLocalPosition, t);
                var swayEnvelope = Mathf.Sin(Mathf.PI * t);
                position.x += Mathf.Sin(t * m_EntrySwayCycles * Mathf.PI * 2f) * m_EntrySwayAmplitude * swayEnvelope;
                m_EntryModelRoot.localPosition = position;
                m_EntryModelRoot.localScale = m_EntryModelInitialScale * Mathf.Lerp(0.5f, 1f, t);
                yield return null;
            }
            m_EntryModelRoot.localPosition = m_EntryModelReadyLocalPosition;
            m_EntryModelRoot.localScale = m_EntryModelInitialScale;
            SetBagPusherEnabled(false);
            m_EntryAnimator?.SetTrigger("Wink");
            PlayMoodiIntro();
            StartCoroutine(ShowLanguageChoiceAfterIntro());
            if (m_EntryTrailParticles != null)
                StartCoroutine(FadeEntryTrailParticles());
        }

        IEnumerator FadeEntryTrailParticles()
        {
            if (m_EntryTrailParticles == null)
                yield break;
            m_EntryTrailParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            yield return new WaitForSeconds(1.05f);
            m_EntryTrailParticles.Clear(true);
        }

        IEnumerator ExitSequence()
        {
            SetMoodiIntroVisible(false);
            SetLanguageChoiceVisible(false);
            if (m_EntryModelRoot != null)
            m_EntryModelRoot.localScale = Vector3.zero;
            if (m_PortalAudio != null)
            {
                m_PortalAudio.Stop();
                m_PortalAudio.clip = null;
                m_PortalAudio.loop = false;
            }
            if (m_EntryTrailParticles != null)
            {
                m_EntryTrailParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                m_EntryTrailParticles.Clear(true);
            }
            if (m_PortalAnimator != null)
            {
                if (m_VisualRoot != null)
                    m_VisualRoot.transform.localScale = Vector3.one;
                m_PortalAnimator.Play("Open", 0, 1f);
                m_PortalAnimator.speed = -1f;
                yield return new WaitForSeconds(m_OpeningDuration);
                m_PortalAnimator.speed = 1f;
            }
            m_Sequence = null;
            FinishTransition();
        }

        void MarkReady()
        {
            m_StateMachine.MarkReady();
            if (m_EntryPrompt != null)
                m_EntryPrompt.SetActive(true);
        }

        void ConfigureMoodiIntroSubtitle()
        {
            if (m_EntryPrompt == null)
                return;
            var subtitle = m_EntryPrompt.GetComponentInChildren<TMP_Text>(true);
            if (subtitle == null)
                return;
            subtitle.text = "嗨，我是 Moodi\nHi, I'm Moodi";
            subtitle.alignment = TextAlignmentOptions.Center;
            subtitle.fontSize = 0.3f;
            subtitle.lineSpacing = 12f;
            subtitle.font = m_MoodiSubtitleFont != null
                ? m_MoodiSubtitleFont
                : TMP_Settings.defaultFontAsset;
            subtitle.color = Color.white;
            subtitle.outlineWidth = 0.08f;
            subtitle.outlineColor = new Color(0.12f, 0.04f, 0.2f, 0.85f);
            var subtitleRenderer = subtitle.GetComponent<Renderer>();
            if (subtitleRenderer != null)
                subtitleRenderer.allowOcclusionWhenDynamic = false;
            subtitle.ForceMeshUpdate(ignoreActiveState: true);
            m_EntryPromptText = subtitle;
            var subtitleScale = subtitle.transform.localScale;
            subtitle.transform.localScale = new Vector3(-Mathf.Abs(subtitleScale.x), subtitleScale.y, subtitleScale.z);
        }

        void PlayMoodiIntro()
        {
            SetMoodiIntroVisible(true);
            if (m_MoodiIntroVoiceClip != null)
                m_MoodiVoiceAudio?.PlayOneShot(m_MoodiIntroVoiceClip);
        }

        void SetMoodiIntroVisible(bool visible)
        {
            if (m_EntryPrompt != null)
                m_EntryPrompt.SetActive(visible);
            if (visible)
                m_EntryPromptText?.ForceMeshUpdate(ignoreActiveState: true);
            if (!visible)
                m_MoodiVoiceAudio?.Stop();
        }

        IEnumerator ShowLanguageChoiceAfterIntro()
        {
            yield return new WaitForSeconds(Mathf.Max(
                m_MoodiIntroMinimumDuration,
                m_MoodiIntroVoiceClip != null ? m_MoodiIntroVoiceClip.length : 0f));
            if (m_EntryPromptText != null)
            {
                m_EntryPromptText.text = "你更希望 Moodi 用哪种语言陪你探索？\nWhich language would you like Moodi to use?";
                m_EntryPromptText.ForceMeshUpdate(ignoreActiveState: true);
            }
            SetLanguageChoiceVisible(true);
        }

        void ConfigureLanguageChoice()
        {
            if (m_EntryModelRoot == null) return;
            m_LanguageChoice = new GameObject("Moodi Language Choice");
            m_LanguageChoice.transform.SetParent(m_EntryPrompt != null ? m_EntryPrompt.transform : m_EntryModelRoot, false);
            m_LanguageChoice.transform.localPosition = Vector3.zero;
            m_LanguageChoice.transform.localRotation = Quaternion.identity;
            m_LanguageChoice.transform.localScale = Vector3.one * 0.0012f;
            var canvas = m_LanguageChoice.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            var scaler = m_LanguageChoice.AddComponent<CanvasScaler>(); scaler.dynamicPixelsPerUnit = 10f;
            var panel = new GameObject("Language Choice Buttons", typeof(RectTransform)); panel.transform.SetParent(m_LanguageChoice.transform, false);
            var panelRect = panel.GetComponent<RectTransform>(); panelRect.sizeDelta = new Vector2(360f, 80f); panelRect.anchoredPosition = new Vector2(0f, -48f);
            CreateLanguageButton(panel.transform, "中文", new Vector2(-142f, -70f), () => SelectLanguage("zh"));
            CreateLanguageButton(panel.transform, "English", new Vector2(138f, -70f), () => SelectLanguage("en"));
            m_LanguageChoice.SetActive(false);
        }

        void CreateChoiceText(Transform parent, string value, Vector2 position, float size)
        {
            var go = new GameObject("Language Choice Text", typeof(RectTransform)); go.transform.SetParent(parent, false); var rect = go.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(570f, 100f); rect.anchoredPosition = position;
            var text = go.AddComponent<TextMeshProUGUI>(); text.text = value; text.alignment = TextAlignmentOptions.Center; text.fontSize = size; text.color = Color.white; text.font = m_MoodiSubtitleFont != null ? m_MoodiSubtitleFont : TMP_Settings.defaultFontAsset;
        }

        void CreateLanguageButton(Transform parent, string label, Vector2 position, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(label == "中文" ? "Language Button - Chinese" : "Language Button - English",
                typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(200f, 50f);
            rect.anchoredPosition = position;
            var image = go.GetComponent<Image>();
            image.sprite = m_LanguageButtonRoundedSprite != null
                ? m_LanguageButtonRoundedSprite
                : Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            image.color = new Color(0.055f, 0.08f, 0.16f, 0.84f);
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0.42f, 0.58f, 0.95f, 0.55f);
            outline.effectDistance = new Vector2(1f, 1f);
            go.GetComponent<Button>().onClick.AddListener(action);
            CreateChoiceText(go.transform, label, Vector2.zero, 24f);
        }

        void SelectLanguage(string language) { PlayerPrefs.SetString("Moodium.Language", language); PlayerPrefs.Save(); SetLanguageChoiceVisible(false); }
        void SetLanguageChoiceVisible(bool visible) { if (m_LanguageChoice != null) m_LanguageChoice.SetActive(visible); }

        void FinishTransition()
        {
            if (State != PortalEntranceState.Entering)
                return;
            m_StateMachine.MarkCompleted();
            if (m_VisualRoot != null)
                m_VisualRoot.SetActive(false);
            Completed?.Invoke();
            gameObject.SetActive(false);
        }

        void SetBagPusherEnabled(bool enabled)
        {
            if (m_EntryBagPusher != null)
                m_EntryBagPusher.enabled = enabled;
        }

        void ConfigureEntryTrail()
        {
            if (m_EntryModelRoot == null)
                return;
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", new Color(1f, 0.32f, 0.8f, 0.5f));
            else if (material.HasProperty("_Color"))
                material.SetColor("_Color", new Color(1f, 0.32f, 0.8f, 0.5f));
            if (m_TrailParticleTexture != null)
            {
                if (material.HasProperty("_BaseMap"))
                    material.SetTexture("_BaseMap", m_TrailParticleTexture);
                if (material.HasProperty("_MainTex"))
                    material.SetTexture("_MainTex", m_TrailParticleTexture);
            }
            var particleObject = new GameObject("Moodi Loose Trail Particles");
            particleObject.transform.SetParent(m_EntryModelRoot, false);
            particleObject.transform.localPosition = Vector3.zero;
            m_EntryTrailParticles = particleObject.AddComponent<ParticleSystem>();
            var main = m_EntryTrailParticles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.65f, 1.25f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.006f, 0.018f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.004f, 0.012f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.56f, 0.4f, 0.98f, 0.07f),
                new Color(0.82f, 0.7f, 1f, 0.22f));
            var emission = m_EntryTrailParticles.emission;
            emission.rateOverTime = 8f;
            var shape = m_EntryTrailParticles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.006f;
            var noise = m_EntryTrailParticles.noise;
            noise.enabled = true;
            noise.strength = 0.035f;
            noise.frequency = 0.42f;
            noise.scrollSpeed = 0.18f;
            var renderer = particleObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.Facing;
            renderer.sharedMaterial = material;
            m_EntryTrailParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        bool TryGetHandSubsystem()
        {
            if (m_HandSubsystem == null)
                m_HandSubsystem = XRGeneralSettings.Instance?.Manager?.activeLoader
                    ?.GetLoadedSubsystem<XRHandSubsystem>();
            return m_HandSubsystem != null && m_HandSubsystem.running;
        }

        bool TryHandContact(XRHand hand)
        {
            if (!hand.isTracked)
                return false;
            foreach (var jointId in ContactJoints)
            {
                var joint = hand.GetJoint(jointId);
                if (joint.trackingState == XRHandJointTrackingState.None || !joint.TryGetPose(out var pose))
                    continue;
                if (TryAcceptContact(pose.position))
                    return true;
            }
            return false;
        }

        bool ContainsContactPoint(Vector3 worldPoint)
        {
            return TryGetEntryBounds(out var bounds) && bounds.Contains(worldPoint);
        }

        void PlaceInFrontOfUser()
        {
            var camera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
            if (camera == null)
                return;
            var forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f)
                forward = camera.transform.forward.normalized;
            transform.position = camera.transform.position + forward * m_DistanceFromUser + Vector3.up * m_HeightOffset;
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }
    }
}
