using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace VFXViewer
{
    [DisallowMultipleComponent]
    public class AdminAccessCardUnlocker : MonoBehaviour
    {
        private const string InterfaceAnimManagerTypeName = "InterfaceAnimManager";

        private enum UnlockDebugState
        {
            None,
            MissingReferences,
            NotExperienceMode,
            TrackingManagerDisabled,
            Cooldown,
            NoMatchingImage,
            WaitingForCardRemoval,
            WaitingForStableTracking,
            UnlockTriggered
        }

        [Header("核心引用")]
        [SerializeField] private ChapterPlacementDirector m_PlacementDirector;
        [SerializeField] private ARTrackedImageManager m_TrackedImageManager;

        [Header("管理员识别卡")]
        [SerializeField] private string m_AdminCardReferenceImageName = "AdminAccessCard";
        [SerializeField] private float m_MinTrackingDurationSeconds = 0f;
        [SerializeField] private float m_UnlockCooldownSeconds = 5f;
        [SerializeField] private bool m_OnlyUnlockFromExperienceMode = true;
        [SerializeField] private bool m_RequireCardLostBeforeNextUnlock = true;
        [SerializeField] private bool m_LogUnlockEvents = true;

        [Header("切换提示")]
        [SerializeField] private GameObject m_PlacementModeHintUiPrefab;
        [SerializeField] private Vector3 m_PlacementModeHintImageLocalOffset = Vector3.zero;
        [SerializeField] private Vector3 m_PlacementModeHintRotationOffsetEuler = Vector3.zero;
        [SerializeField] private bool m_ShowPlacementModeHintOnUnlock = true;
        [SerializeField] private float m_PlacementModeHintVisibleSeconds = 2f;

        private float m_TrackingStartedAt = -1f;
        private float m_LastUnlockAt = -100f;
        private UnlockDebugState m_LastDebugState = UnlockDebugState.None;
        private string m_LastDebugDetail = string.Empty;
        private TrackableId m_ConsumedTrackableId = TrackableId.invalidId;
        private Coroutine m_PlacementModeHintRoutine;
        private GameObject m_RuntimePlacementModeHintInstance;

        private void Awake()
        {
            if (m_TrackedImageManager == null)
                m_TrackedImageManager = GetComponent<ARTrackedImageManager>();

            if (m_PlacementDirector == null)
                m_PlacementDirector = FindFirstObjectByType<ChapterPlacementDirector>(FindObjectsInactive.Include);
        }

        private void OnDisable()
        {
            if (m_PlacementModeHintRoutine != null)
            {
                StopCoroutine(m_PlacementModeHintRoutine);
                m_PlacementModeHintRoutine = null;
            }

            if (m_RuntimePlacementModeHintInstance != null)
            {
                Destroy(m_RuntimePlacementModeHintInstance);
                m_RuntimePlacementModeHintInstance = null;
            }
        }

        private void Update()
        {
            if (m_PlacementDirector == null || m_TrackedImageManager == null)
            {
                PublishDebugState(UnlockDebugState.MissingReferences,
                    $"placementDirector={(m_PlacementDirector != null)}, trackedImageManager={(m_TrackedImageManager != null)}");
                UpdateCardConsumptionState(null);
                ResetTrackingWindow();
                return;
            }

            if (!m_TrackedImageManager.isActiveAndEnabled)
            {
                PublishDebugState(UnlockDebugState.TrackingManagerDisabled, "ARTrackedImageManager is disabled.");
                UpdateCardConsumptionState(null);
                ResetTrackingWindow();
                return;
            }

            bool hasMatchedCard = TryGetTrackedAdminCard(out ARTrackedImage matchedImage, out string matchedImageName, out string trackedImageSummary);
            UpdateCardConsumptionState(matchedImage);

            if (m_OnlyUnlockFromExperienceMode && !m_PlacementDirector.IsExperienceMode)
            {
                PublishDebugState(UnlockDebugState.NotExperienceMode,
                    $"currentMode={m_PlacementDirector.CurrentAppMode}");
                ResetTrackingWindow();
                return;
            }

            if (Time.unscaledTime < m_LastUnlockAt + Mathf.Max(0f, m_UnlockCooldownSeconds))
            {
                PublishDebugState(UnlockDebugState.Cooldown, "Unlock cooldown active.");
                return;
            }

            if (!hasMatchedCard)
            {
                PublishDebugState(UnlockDebugState.NoMatchingImage, trackedImageSummary);
                ResetTrackingWindow();
                return;
            }

            if (m_RequireCardLostBeforeNextUnlock &&
                m_HasConsumedCurrentCardSight &&
                matchedImage != null &&
                matchedImage.trackableId == m_ConsumedTrackableId)
            {
                PublishDebugState(UnlockDebugState.WaitingForCardRemoval,
                    "Admin card is still visible from the previous unlock. Move it out of view before unlocking again.");
                ResetTrackingWindow();
                return;
            }

            if (m_TrackingStartedAt < 0f)
            {
                m_TrackingStartedAt = Time.unscaledTime;
                if (Mathf.Max(0f, m_MinTrackingDurationSeconds) > 0f)
                {
                    PublishDebugState(UnlockDebugState.WaitingForStableTracking,
                        $"Matched {matchedImageName}, waiting {m_MinTrackingDurationSeconds:0.##}s for stable tracking.");
                    return;
                }
            }

            if (Time.unscaledTime - m_TrackingStartedAt < Mathf.Max(0f, m_MinTrackingDurationSeconds))
            {
                PublishDebugState(UnlockDebugState.WaitingForStableTracking,
                    $"Matched {matchedImageName}, waiting {m_MinTrackingDurationSeconds:0.##}s for stable tracking.");
                return;
            }

            m_LastUnlockAt = Time.unscaledTime;
            m_ConsumedTrackableId = matchedImage != null ? matchedImage.trackableId : TrackableId.invalidId;
            ResetTrackingWindow();
            PublishDebugState(UnlockDebugState.UnlockTriggered, $"Matched {matchedImageName}, switching to placement mode.");

            if (m_LogUnlockEvents)
                Debug.Log($"Admin access card detected: {matchedImageName}. Switching to placement mode.", this);

            bool shouldShowPlacementModeHint = m_ShowPlacementModeHintOnUnlock && m_PlacementModeHintUiPrefab != null;
            Vector3 hintPosition = default;
            Quaternion hintRotation = Quaternion.identity;
            bool hasHintPose = shouldShowPlacementModeHint &&
                               TryComputePlacementModeHintSpawnPose(matchedImage, out hintPosition, out hintRotation);

            m_PlacementDirector.EnterPlacementMode();

            if (hasHintPose)
                ShowPlacementModeHint(hintPosition, hintRotation);
        }

        private bool TryGetTrackedAdminCard(out ARTrackedImage matchedImage, out string matchedImageName, out string trackedImageSummary)
        {
            matchedImage = null;
            matchedImageName = string.Empty;
            trackedImageSummary = string.Empty;

            if (string.IsNullOrWhiteSpace(m_AdminCardReferenceImageName))
            {
                trackedImageSummary = "Admin card reference image name is empty.";
                return false;
            }

            int seenTrackables = 0;

            foreach (ARTrackedImage trackedImage in m_TrackedImageManager.trackables)
            {
                if (trackedImage == null)
                    continue;

                seenTrackables++;
                string referenceImageName = trackedImage.referenceImage.name;
                trackedImageSummary = $"Seen {referenceImageName} ({trackedImage.trackingState}). Expecting {m_AdminCardReferenceImageName}.";

                if (!IsMatchingAdminCardName(referenceImageName))
                    continue;

                if (trackedImage.trackingState == TrackingState.None)
                    continue;

                matchedImage = trackedImage;
                matchedImageName = referenceImageName;
                trackedImageSummary = $"Matched {referenceImageName} with tracking state {trackedImage.trackingState} and trackableId {trackedImage.trackableId}.";
                return true;
            }

            if (seenTrackables == 0)
                trackedImageSummary = $"No tracked images. Expecting {m_AdminCardReferenceImageName}.";

            return false;
        }

        private void ResetTrackingWindow()
        {
            m_TrackingStartedAt = -1f;
        }

        private bool m_HasConsumedCurrentCardSight => m_ConsumedTrackableId != TrackableId.invalidId;

        private void UpdateCardConsumptionState(ARTrackedImage matchedImage)
        {
            if (matchedImage != null)
                return;

            m_ConsumedTrackableId = TrackableId.invalidId;
        }

        private bool IsMatchingAdminCardName(string candidateName)
        {
            if (string.IsNullOrWhiteSpace(candidateName) || string.IsNullOrWhiteSpace(m_AdminCardReferenceImageName))
                return false;

            return candidateName.IndexOf(m_AdminCardReferenceImageName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void PublishDebugState(UnlockDebugState state, string detail)
        {
            if (!m_LogUnlockEvents)
                return;

            detail ??= string.Empty;
            if (m_LastDebugState == state && string.Equals(m_LastDebugDetail, detail, StringComparison.Ordinal))
                return;

            m_LastDebugState = state;
            m_LastDebugDetail = detail;

            if (string.IsNullOrWhiteSpace(detail))
            {
                Debug.Log($"[AdminCardUnlocker] {state}", this);
                return;
            }

            Debug.Log($"[AdminCardUnlocker] {state}: {detail}", this);
        }

        private void ShowPlacementModeHint(Vector3 spawnPosition, Quaternion spawnRotation)
        {
            if (m_PlacementModeHintRoutine != null)
            {
                StopCoroutine(m_PlacementModeHintRoutine);
                m_PlacementModeHintRoutine = null;
            }

            if (m_RuntimePlacementModeHintInstance != null)
            {
                Destroy(m_RuntimePlacementModeHintInstance);
                m_RuntimePlacementModeHintInstance = null;
            }

            if (m_PlacementModeHintUiPrefab == null)
                return;

            m_RuntimePlacementModeHintInstance = Instantiate(m_PlacementModeHintUiPrefab, spawnPosition, spawnRotation);
            m_PlacementModeHintRoutine = StartCoroutine(ShowPlacementModeHintUiCoroutine(
                m_RuntimePlacementModeHintInstance,
                Mathf.Max(0f, m_PlacementModeHintVisibleSeconds)));
        }

        private bool TryComputePlacementModeHintSpawnPose(ARTrackedImage matchedImage, out Vector3 spawnPosition, out Quaternion spawnRotation)
        {
            if (matchedImage == null)
            {
                spawnPosition = Vector3.zero;
                spawnRotation = Quaternion.identity;
                return false;
            }

            Transform imageTransform = matchedImage.transform;
            spawnPosition = imageTransform.TransformPoint(m_PlacementModeHintImageLocalOffset);
            spawnRotation = ComputeFacingRotation(spawnPosition) * Quaternion.Euler(m_PlacementModeHintRotationOffsetEuler);
            return true;
        }

        private Quaternion ComputeFacingRotation(Vector3 worldPosition)
        {
            Transform reference = Camera.main != null ? Camera.main.transform : null;
            if (reference == null)
                return Quaternion.identity;

            Vector3 forward = reference.position - worldPosition;
            if (forward.sqrMagnitude <= 0.000001f)
                forward = -reference.forward;

            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        private IEnumerator ShowPlacementModeHintUiCoroutine(GameObject uiRoot, float visibleSeconds)
        {
            if (uiRoot == null)
            {
                m_PlacementModeHintRoutine = null;
                yield break;
            }

            var animManager = ResolveInterfaceAnimManager(uiRoot);
            uiRoot.SetActive(true);

            if (animManager != null)
            {
                InvokeAnimManagerMethod(animManager, "startDisappear", true);
                InvokeAnimManagerMethod(animManager, "startAppear", false);
                yield return WaitForAnimManagerState(animManager, "appeared", 2f);
            }

            yield return new WaitForSecondsRealtime(visibleSeconds);

            if (animManager != null)
            {
                InvokeAnimManagerMethod(animManager, "startDisappear", false);
                yield return WaitForAnimManagerState(animManager, "disappeared", 2f);
            }

            if (uiRoot != null)
                Destroy(uiRoot);

            m_RuntimePlacementModeHintInstance = null;
            m_PlacementModeHintRoutine = null;
        }

        private static MonoBehaviour ResolveInterfaceAnimManager(GameObject uiRoot)
        {
            if (uiRoot == null)
                return null;

            var managerOnRoot = uiRoot.GetComponent(InterfaceAnimManagerTypeName) as MonoBehaviour;
            if (managerOnRoot != null)
                return managerOnRoot;

            var managersInChildren = uiRoot.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < managersInChildren.Length; i++)
            {
                var manager = managersInChildren[i];
                if (manager != null && manager.GetType().Name == InterfaceAnimManagerTypeName)
                    return manager;
            }

            return null;
        }

        private static void InvokeAnimManagerMethod(MonoBehaviour animManager, string methodName, bool direct)
        {
            if (animManager == null)
                return;

            MethodInfo method = animManager.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
            if (method == null)
                return;

            method.Invoke(animManager, new object[] { direct });
        }

        private static string GetAnimManagerStateName(MonoBehaviour animManager)
        {
            if (animManager == null)
                return string.Empty;

            FieldInfo field = animManager.GetType().GetField("currentState", BindingFlags.Instance | BindingFlags.Public);
            object stateValue = field != null ? field.GetValue(animManager) : null;
            return stateValue != null ? stateValue.ToString() : string.Empty;
        }

        private static IEnumerator WaitForAnimManagerState(MonoBehaviour animManager, string expectedState, float timeoutSeconds)
        {
            float elapsed = 0f;
            while (elapsed < timeoutSeconds)
            {
                if (string.Equals(GetAnimManagerStateName(animManager), expectedState, StringComparison.OrdinalIgnoreCase))
                    yield break;

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }
    }
}
