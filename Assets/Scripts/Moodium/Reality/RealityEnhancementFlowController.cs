using System.Collections;
using System.Collections.Generic;
using Moodium.Flow;
using Moodium.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Moodium.Reality
{
    public enum RealityEnhancementFlowState
    {
        Idle,
        ObjectDetected,
        Analyzing,
        Recommend,
        Generating,
        Interactive
    }

    /// <summary>Shows the initial guidance, then hands the tracked object directly to the enhancement.</summary>
    public sealed class RealityEnhancementFlowController : MonoBehaviour
    {
        [Header("Timing")]
        [SerializeField, Min(2f)] float m_GuideDuration = 4f;
        [SerializeField, Min(0.2f)] float m_GenerationDuration = 0.5f;
        [SerializeField] bool m_ShowLegacyInitialGuide;

        TissueObjectTrackingSpawner m_Spawner;
        ImageTrackingCapsuleSpawner m_ImageSpawner;
        Camera m_Camera;
        GameObject m_GlassPanelPrefab;
        TMP_FontAsset m_Font;
        TMP_FontAsset m_ChineseFont;
        GameObject m_PushLeftHandPrefab;
        GameObject m_PushRightHandPrefab;
        RealityEnhancementFlowState m_State;
        GameObject m_Guide;
        Coroutine m_GuideRoutine;
        readonly HashSet<ChocolateCapsuleInteraction> m_InteractiveCapsules = new();
        readonly HashSet<PushHandInteractionGuide> m_PushHandGuides = new();
        readonly Dictionary<ChocolateCapsuleInteraction, Coroutine> m_PresentationRoutines = new();
        bool m_Running;

        public RealityEnhancementFlowState State => m_State;
        public int InteractiveCapsuleCount => m_InteractiveCapsules.Count;
        public bool ShowLegacyInitialGuide => m_ShowLegacyInitialGuide;

        public void Configure(
            TissueObjectTrackingSpawner spawner,
            ImageTrackingCapsuleSpawner imageSpawner,
            Camera camera,
            GameObject glassPanelPrefab,
            TMP_FontAsset font,
            TMP_FontAsset chineseFont,
            GameObject pushLeftHandPrefab = null,
            GameObject pushRightHandPrefab = null)
        {
            m_Spawner = spawner;
            m_ImageSpawner = imageSpawner;
            m_Camera = camera;
            m_GlassPanelPrefab = glassPanelPrefab;
            m_Font = font;
            m_ChineseFont = chineseFont;
            m_PushLeftHandPrefab = pushLeftHandPrefab;
            m_PushRightHandPrefab = pushRightHandPrefab;
            var legacyThumbBend = GetComponent<BimanualThumbBendGestureController>();
            if (legacyThumbBend != null)
            {
                legacyThumbBend.SetGestureEnabled(false);
                legacyThumbBend.SetTarget(null);
                legacyThumbBend.enabled = false;
            }
        }

        public void StartFlow()
        {
            StopFlow(false);
            if (m_Spawner == null && m_ImageSpawner == null)
            {
                Debug.LogError("[Reality Flow] No tracked-source Capsule spawner was found.");
                return;
            }
            m_Running = true;
            m_State = RealityEnhancementFlowState.Idle;
            if (m_Spawner != null)
            {
                m_Spawner.SetDeferredSpawning(true);
                m_Spawner.TissueDetected += OnTissueDetected;
                m_Spawner.CapsuleAvailable += OnCapsuleAvailable;
                m_Spawner.CapsuleUnavailable += OnCapsuleUnavailable;
            }
            if (m_ImageSpawner != null)
            {
                m_ImageSpawner.CapsuleAvailable += OnCapsuleAvailable;
                m_ImageSpawner.CapsuleUnavailable += OnCapsuleUnavailable;
            }
            // The experience now begins through the sticker-selection / bag-calibration
            // flow. Keep this legacy object-tracking card off by default so it does not
            // overlap the current Candy World entry UI.
            if (m_ShowLegacyInitialGuide)
                m_GuideRoutine = StartCoroutine(ShowInitialGuide());
            Debug.Log("[Reality Flow] Waiting for an object or reference image.");
        }

        public void StopFlow() => StopFlow(true);

        void StopFlow(bool clearTrackedVisuals)
        {
            if (m_Spawner != null)
            {
                m_Spawner.TissueDetected -= OnTissueDetected;
                m_Spawner.CapsuleAvailable -= OnCapsuleAvailable;
                m_Spawner.CapsuleUnavailable -= OnCapsuleUnavailable;
                m_Spawner.SetDeferredSpawning(false);
                if (clearTrackedVisuals)
                    m_Spawner.ClearRuntimeInstances();
            }
            if (m_ImageSpawner != null)
            {
                m_ImageSpawner.CapsuleAvailable -= OnCapsuleAvailable;
                m_ImageSpawner.CapsuleUnavailable -= OnCapsuleUnavailable;
                if (clearTrackedVisuals)
                    m_ImageSpawner.ClearRuntimeInstances();
            }
            m_Running = false;
            m_State = RealityEnhancementFlowState.Idle;
            if (m_GuideRoutine != null) StopCoroutine(m_GuideRoutine);
            foreach (var routine in m_PresentationRoutines.Values)
                if (routine != null) StopCoroutine(routine);
            m_GuideRoutine = null;
            m_PresentationRoutines.Clear();
            m_InteractiveCapsules.Clear();
            DestroyPushHandGuides();
            DestroyPresentationObjects();
        }

        void OnDestroy()
        {
            StopFlow(false);
        }

        void Update()
        {
            if (m_Guide != null && m_Camera != null)
            {
                var forward = HorizontalForward(m_Camera.transform);
                m_Guide.transform.SetPositionAndRotation(
                    m_Camera.transform.position + forward * 0.92f + Vector3.up * 0.035f,
                    Quaternion.LookRotation(forward, Vector3.up));
            }

        }

        IEnumerator ShowInitialGuide()
        {
            m_Guide = CreateGlassCard(
                "Reality Initial Guidance",
                "拿起身边的日常物品\n将它放在面前，开始感官增强体验\n\n" +
                "Pick up an everyday object\nPlace it in front of you to begin",
                new Vector2(0.56f, 0.28f),
                TextAlignmentOptions.Center);
            yield return AnimateScaleIn(m_Guide, 0.35f);
            yield return new WaitForSeconds(Mathf.Max(0f, m_GuideDuration - 0.8f));
            yield return AnimateScaleOut(m_Guide, 0.4f);
            DestroySafe(ref m_Guide);
            m_GuideRoutine = null;
        }

        void OnTissueDetected(ARTrackedObject trackedObject)
        {
            if (!m_Running || trackedObject == null)
                return;
            DismissGuide();
            StartCoroutine(RunDetectionFlow(trackedObject));
        }

        IEnumerator RunDetectionFlow(ARTrackedObject trackedObject)
        {
            m_State = RealityEnhancementFlowState.ObjectDetected;
            if (!CanContinue(trackedObject))
            {
                CancelDetection(trackedObject, "Tracked tissue was removed before generation.");
                yield break;
            }

            m_State = RealityEnhancementFlowState.Generating;
            var capsule = m_Spawner.SpawnDeferred(trackedObject);
            if (capsule == null)
            {
                CancelDetection(trackedObject, "Chocolate Capsule could not be generated.");
                yield break;
            }
            yield return null;
        }

        bool CanContinue(ARTrackedObject trackedObject)
        {
            return m_Running && trackedObject != null &&
                   trackedObject.trackingState != TrackingState.None;
        }

        void CancelDetection(ARTrackedObject trackedObject, string reason)
        {
            Debug.LogWarning($"[Reality Flow] {reason}");
            m_Spawner?.ReleaseDeferredDetection(trackedObject);
            if (m_InteractiveCapsules.Count == 0)
                m_State = RealityEnhancementFlowState.Idle;
        }

        void OnCapsuleAvailable(ChocolateCapsuleInteraction capsule)
        {
            if (!m_Running || capsule == null || m_InteractiveCapsules.Contains(capsule) ||
                m_PresentationRoutines.ContainsKey(capsule))
                return;
            DismissGuide();
            m_State = RealityEnhancementFlowState.Generating;
            m_PresentationRoutines[capsule] = StartCoroutine(PresentCapsule(capsule));
        }

        IEnumerator PresentCapsule(ChocolateCapsuleInteraction capsule)
        {
            yield return AnimateCapsuleIn(capsule.gameObject, m_GenerationDuration);
            m_PresentationRoutines.Remove(capsule);
            if (!m_Running || capsule == null || !capsule.isActiveAndEnabled)
                yield break;
            RegisterAvailableCapsule(capsule);
            Debug.Log("[Reality Flow] Interactive tracked Candy Capsule ready.");
        }

        void RegisterAvailableCapsule(ChocolateCapsuleInteraction capsule)
        {
            if (capsule == null || !m_InteractiveCapsules.Add(capsule))
                return;
            if (m_PushLeftHandPrefab != null || m_PushRightHandPrefab != null)
            {
                var guide = new GameObject("Push Hand Interaction Guide").AddComponent<PushHandInteractionGuide>();
                guide.Configure(capsule, m_PushLeftHandPrefab, m_PushRightHandPrefab);
                m_PushHandGuides.Add(guide);
            }
            m_State = RealityEnhancementFlowState.Interactive;
        }

        void OnCapsuleUnavailable(ChocolateCapsuleInteraction capsule)
        {
            if (capsule == null)
                return;
            if (m_PresentationRoutines.Remove(capsule, out var routine) && routine != null)
                StopCoroutine(routine);
            if (m_InteractiveCapsules.Remove(capsule))
            if (m_InteractiveCapsules.Count == 0)
                m_State = RealityEnhancementFlowState.Idle;
        }

        void DismissGuide()
        {
            if (m_GuideRoutine != null) StopCoroutine(m_GuideRoutine);
            m_GuideRoutine = null;
            DestroySafe(ref m_Guide);
        }

        void DestroyPushHandGuides()
        {
            foreach (var guide in m_PushHandGuides)
                if (guide != null)
                    Destroy(guide.gameObject);
            m_PushHandGuides.Clear();
        }

        GameObject CreateGlassCard(string name, string text, Vector2 size, TextAlignmentOptions alignment)
        {
            var root = new GameObject(name);
            root.transform.localScale = Vector3.zero;
            if (m_GlassPanelPrefab != null)
            {
                var glass = Instantiate(m_GlassPanelPrefab, root.transform, false);
                glass.name = "Soft VisionOS Glass";
                glass.transform.localScale = new Vector3(size.x / 0.72f, size.y / 0.43f, 1f);
                foreach (var oldText in glass.GetComponentsInChildren<TMP_Text>(true))
                    oldText.gameObject.SetActive(false);
            }

            var textObject = new GameObject("AI Perception Text");
            textObject.transform.SetParent(root.transform, false);
            textObject.transform.localPosition = new Vector3(0f, 0f, -0.035f);
            textObject.transform.localScale = Vector3.one * 0.1f;
            var tmp = textObject.AddComponent<TextMeshPro>();
            tmp.font = m_ChineseFont != null ? m_ChineseFont : m_Font;
            tmp.text = text;
            tmp.fontSize = 2.25f;
            tmp.color = new Color(0.96f, 0.97f, 1f, 0.98f);
            tmp.alignment = alignment;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.rectTransform.sizeDelta = new Vector2(size.x * 8.5f, size.y * 8.1f);
            tmp.renderer.sortingOrder = 80;
            return root;
        }

        static IEnumerator AnimateScaleIn(GameObject target, float duration)
        {
            if (target == null) yield break;
            var elapsed = 0f;
            while (elapsed < duration && target != null)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var eased = 1f - Mathf.Pow(1f - t, 3f);
                target.transform.localScale = Vector3.one * eased;
                yield return null;
            }
            if (target != null) target.transform.localScale = Vector3.one;
        }

        static IEnumerator AnimateScaleOut(GameObject target, float duration)
        {
            if (target == null) yield break;
            var start = target.transform.localScale;
            var elapsed = 0f;
            while (elapsed < duration && target != null)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                target.transform.localScale = Vector3.Lerp(start, Vector3.zero, t * t);
                yield return null;
            }
        }

        static IEnumerator AnimateCapsuleIn(GameObject capsule, float duration)
        {
            if (capsule == null) yield break;
            var targetScale = capsule.transform.localScale;
            capsule.transform.localScale = targetScale * 0.04f;
            var elapsed = 0f;
            while (elapsed < duration && capsule != null)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var back = 1f + 0.1f * Mathf.Sin(t * Mathf.PI);
                capsule.transform.localScale = Vector3.LerpUnclamped(targetScale * 0.04f, targetScale, t) * back;
                yield return null;
            }
            if (capsule != null) capsule.transform.localScale = targetScale;
        }

        void DestroyPresentationObjects()
        {
            DestroySafe(ref m_Guide);
        }

        static void DestroySafe(ref GameObject value)
        {
            if (value != null) Destroy(value);
            value = null;
        }

        static Vector3 HorizontalForward(Transform cameraTransform)
        {
            var forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            return forward.sqrMagnitude > 0.001f ? forward : cameraTransform.forward.normalized;
        }
    }
}
