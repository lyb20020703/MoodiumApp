using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using RenderHeads.Media.AVProVideo;

namespace VFXViewer
{
    [DisallowMultipleComponent]
    public class PlacementRootImageAutoDeployer : MonoBehaviour
    {
        private enum AutoDeployDebugState
        {
            None,
            MissingReferences,
            UnsupportedMode,
            TrackingManagerDisabled,
            WaitingForRecognitionZoneEntry,
            Cooldown,
            NoMatchingImage,
            WaitingForImageRemoval,
            WaitingForStableTracking,
            WaitingForRootAnchor,
            RootOnlyCompleted,
            RootPlacementTriggered,
            DeployStarting,
            DeployInvoked,
            RootPlacementFailed,
            RootAnchorTimeout
        }

        private struct RootImageSample
        {
            public float timestamp;
            public TrackableId trackableId;
            public Pose rootPose;
            public float distanceMeters;
            public float viewAngleDegrees;
        }

        [Header("核心引用")]
        [SerializeField] private ChapterPlacementDirector m_PlacementDirector;
        [SerializeField] private ExhibitPlacementManager m_PlacementManager;
        [SerializeField] private ARTrackedImageManager m_TrackedImageManager;

        [Header("Root 识别图片")]
        [SerializeField] private string m_RootReferenceImageName = "RootCard";
        [SerializeField] private float m_SamplingWindowSeconds = 1f;
        [SerializeField] private int m_MinValidSampleCount = 8;
        [SerializeField] private float m_MinDistanceMeters = 0.5f;
        [SerializeField] private float m_MaxDistanceMeters = 3f;
        [SerializeField] private float m_MaxRootPositionSpreadMeters = 0.03f;
        [SerializeField] private float m_MaxRootYawSpreadDegrees = 4f;
        [SerializeField] private float m_MaxViewAngleDegrees = 15f;
        [SerializeField] private float m_TriggerCooldownSeconds = 5f;
        [SerializeField] private bool m_RequireImageLostBeforeNextTrigger = true;

        [Header("体验模式识别入口")]
        [SerializeField] private bool m_RequireEnterRecognitionZoneInExperienceMode = true;
        [SerializeField] private bool m_UseTrackedRootPoseAsRecognitionZoneOrigin = true;
        [SerializeField] private GameObject m_RecognitionZonePrefab;
        [SerializeField] private float m_ExperienceAdvanceDistanceMeters = 0.8f;
        [SerializeField] private float m_ExperienceRootCardRecognitionDistanceMeters = 1.2f;
        [SerializeField] private float m_RecognitionZoneMinVisualScale = 0.15f;
        [SerializeField] private float m_RecognitionZoneMaxVisualScale = 0.5f;

        [Header("Root 放置")]
        [SerializeField] private Vector3 m_RootImageLocalOffset = Vector3.zero;
        [SerializeField] private Vector3 m_RootRotationOffsetEuler = Vector3.zero;
        [SerializeField] private float m_WaitForRootAnchorSeconds = 8f;
        [SerializeField] private float m_RootAnchorSettleSeconds = 0.5f;

        [Header("识别成功音效")]
        [SerializeField] private AudioClip m_RecognitionSuccessAudioClip;
        [SerializeField] [Range(0f, 1f)] private float m_RecognitionSuccessAudioVolume = 1f;

        [Header("调试")]
        [SerializeField] private bool m_LogEvents = true;

        private readonly List<RootImageSample> m_RootImageSamples = new List<RootImageSample>();
        private float m_SamplingStartedAt = -1f;
        private float m_LastTriggerAt = -100f;
        private TrackableId m_SampledTrackableId = TrackableId.invalidId;
        private TrackableId m_ConsumedTrackableId = TrackableId.invalidId;
        private Coroutine m_WaitForRootAnchorCoroutine;
        private AutoDeployDebugState m_LastDebugState = AutoDeployDebugState.None;
        private string m_LastDebugDetail = string.Empty;
        private bool m_HasEnteredRecognitionZoneThisExperienceSession;
        private bool m_WasExperienceModeLastFrame;
        private string m_LastStatusMessage = string.Empty;
        private GameObject m_RecognitionZoneInstance;
        private Vector3 m_RecognitionZoneInstanceBaseScale = Vector3.one;
        private Collider[] m_RecognitionZoneColliders = Array.Empty<Collider>();
        private RecognitionZoneVisualAnchor m_RecognitionZoneVisualAnchor;
        private bool m_HasStartedRecognitionZoneMediaPlayback;
        private bool m_HasRecognitionZonePoseResolvedThisExperienceSession;
        private string m_LastRecognitionZonePoseDetail = string.Empty;
        private AudioSource m_RecognitionSuccessAudioSource;

        private void Awake()
        {
            if (m_TrackedImageManager == null)
                m_TrackedImageManager = GetComponent<ARTrackedImageManager>();

            if (m_PlacementDirector == null)
                m_PlacementDirector = FindFirstObjectByType<ChapterPlacementDirector>(FindObjectsInactive.Include);

            if (m_PlacementManager == null)
                m_PlacementManager = FindFirstObjectByType<ExhibitPlacementManager>(FindObjectsInactive.Include);
        }

        private void OnDisable()
        {
            ResetSamplingWindow();
            if (m_WaitForRootAnchorCoroutine != null)
            {
                StopCoroutine(m_WaitForRootAnchorCoroutine);
                m_WaitForRootAnchorCoroutine = null;
            }

            m_HasEnteredRecognitionZoneThisExperienceSession = false;
            m_WasExperienceModeLastFrame = false;
            m_LastStatusMessage = string.Empty;
            m_HasRecognitionZonePoseResolvedThisExperienceSession = false;
            m_LastRecognitionZonePoseDetail = string.Empty;
            SetRecognitionZoneVisualVisible(false);
        }

        private void Update()
        {
            if (m_PlacementDirector == null || m_PlacementManager == null || m_TrackedImageManager == null)
            {
                PublishDebugState(
                    AutoDeployDebugState.MissingReferences,
                    $"placementDirector={(m_PlacementDirector != null)}, placementManager={(m_PlacementManager != null)}, trackedImageManager={(m_TrackedImageManager != null)}");
                ResetSamplingWindow();
                UpdateConsumptionState(null);
                return;
            }

            SyncRecognitionZoneSessionState();

            if (!m_PlacementDirector.IsPlacementMode && !m_PlacementDirector.IsExperienceMode)
            {
                PublishDebugState(
                    AutoDeployDebugState.UnsupportedMode,
                    $"currentMode={m_PlacementDirector.CurrentAppMode}");
                ResetSamplingWindow();
                SetRecognitionZoneVisualVisible(false);
                return;
            }

            if (!m_TrackedImageManager.isActiveAndEnabled)
            {
                PublishDebugState(AutoDeployDebugState.TrackingManagerDisabled, "ARTrackedImageManager is disabled.");
                ResetSamplingWindow();
                UpdateConsumptionState(null);
                SetRecognitionZoneVisualVisible(false);
                return;
            }

            if (m_PlacementDirector.IsPlacementMode &&
                !m_PlacementDirector.CanAcceptRootCardPlacementUpdate())
            {
                PublishDebugState(
                    AutoDeployDebugState.NoMatchingImage,
                    "当前布展步骤不是“定位点”，已忽略 RootCard 识别。");
                ResetSamplingWindow();
                UpdateConsumptionState(null);
                SetRecognitionZoneVisualVisible(false);
                return;
            }

            if (m_PlacementDirector.ShouldIgnoreExperienceRootCardDetections)
            {
                PublishDebugState(
                    AutoDeployDebugState.NoMatchingImage,
                    "当前体验会话已完成 RootCard 识别，已忽略后续 RootCard 检测。");
                ResetSamplingWindow();
                UpdateConsumptionState(null);
                SetRecognitionZoneVisualVisible(m_PlacementDirector.ShouldKeepRecognitionZoneVisibleAfterExperienceAcceptance);
                return;
            }

            UpdateRecognitionZoneGuidance();

            bool hasMatchedImage = TryGetTrackedRootImage(out ARTrackedImage matchedImage, out string matchedImageName, out string trackedImageSummary);
            UpdateConsumptionState(matchedImage);

            if (m_WaitForRootAnchorCoroutine != null)
            {
                PublishDebugState(
                    AutoDeployDebugState.WaitingForRootAnchor,
                    $"Matched {matchedImageName}, waiting for Root anchor.");
                return;
            }

            if (Time.unscaledTime < m_LastTriggerAt + Mathf.Max(0f, m_TriggerCooldownSeconds))
            {
                PublishDebugState(AutoDeployDebugState.Cooldown, "Auto deploy cooldown active.");
                return;
            }

            if (!hasMatchedImage)
            {
                PublishDebugState(AutoDeployDebugState.NoMatchingImage, trackedImageSummary);
                ResetSamplingWindow();
                return;
            }

            if (m_PlacementDirector.IsExperienceMode &&
                ShouldWaitForExperienceAdvanceDistance(matchedImage, out string experienceAdvanceDetail))
            {
                PublishDebugState(AutoDeployDebugState.WaitingForStableTracking, experienceAdvanceDetail);
                PublishStatusOnce($"请靠近海报至 {m_ExperienceRootCardRecognitionDistanceMeters:0.#}m 内，开始下一步体验。");
                ResetSamplingWindow();
                return;
            }

            if (ShouldRequireImageLostBeforeNextTrigger() &&
                m_ConsumedTrackableId != TrackableId.invalidId &&
                matchedImage != null &&
                matchedImage.trackableId == m_ConsumedTrackableId)
            {
                PublishDebugState(
                    AutoDeployDebugState.WaitingForImageRemoval,
                    "Placement root image is still visible from the previous trigger. Move it out of view before triggering again.");
                ResetSamplingWindow();
                return;
            }

            if (!TryComputeSamplingMetrics(matchedImage, out Pose candidateRootPose, out float distanceMeters, out float viewAngleDegrees, out string samplingIssue))
            {
                PublishDebugState(AutoDeployDebugState.RootPlacementFailed, samplingIssue);
                ResetSamplingWindow();
                return;
            }

            if (m_SampledTrackableId != matchedImage.trackableId)
            {
                ResetSamplingWindow();
                m_SampledTrackableId = matchedImage.trackableId;
                m_SamplingStartedAt = Time.unscaledTime;
                LogRootImageEvent(
                    "image_detected",
                    $"matchedImage={matchedImageName}, trackableId={matchedImage.trackableId}, distance={distanceMeters:F3}, " +
                    $"viewAngle={viewAngleDegrees:F2}, {BuildTrackedImagePoseDebugSummary(matchedImage, candidateRootPose)}");
                LogRootImageEvent(
                    "image_sampling_started",
                    $"matchedImage={matchedImageName}, trackableId={matchedImage.trackableId}, samplingWindowSeconds={m_SamplingWindowSeconds:0.##}, minValidSampleCount={m_MinValidSampleCount}");
            }

            m_RootImageSamples.Add(new RootImageSample
            {
                timestamp = Time.unscaledTime,
                trackableId = matchedImage.trackableId,
                rootPose = candidateRootPose,
                distanceMeters = distanceMeters,
                viewAngleDegrees = viewAngleDegrees
            });

            float samplingDuration = Time.unscaledTime - m_SamplingStartedAt;
            if (samplingDuration < Mathf.Max(0.01f, m_SamplingWindowSeconds) ||
                m_RootImageSamples.Count < Mathf.Max(1, m_MinValidSampleCount))
            {
                PublishDebugState(
                    AutoDeployDebugState.WaitingForStableTracking,
                    $"Matched {matchedImageName}, sampling {samplingDuration:0.##}/{m_SamplingWindowSeconds:0.##}s, validSamples={m_RootImageSamples.Count}/{m_MinValidSampleCount}.");
                return;
            }

            if (!TryAggregateStableRootPose(
                    out Pose acceptedRootPose,
                    out float positionSpreadMeters,
                    out float yawSpreadDegrees,
                    out float minDistanceMeters,
                    out float maxDistanceMeters,
                    out string aggregateSummary))
            {
                LogRootImageEvent(
                    "image_sampling_rejected_unstable",
                    $"matchedImage={matchedImageName}, duration={samplingDuration:0.##}, validSamples={m_RootImageSamples.Count}, " +
                    $"distanceRange={minDistanceMeters:F3}-{maxDistanceMeters:F3}, positionSpread={positionSpreadMeters:F4}, " +
                    $"yawSpread={yawSpreadDegrees:F2}, reason={aggregateSummary}");
                PublishDebugState(AutoDeployDebugState.WaitingForStableTracking, aggregateSummary);
                ResetSamplingWindow();
                return;
            }

            int acceptedSampleCount = m_RootImageSamples.Count;
            m_LastTriggerAt = Time.unscaledTime;
            m_ConsumedTrackableId = matchedImage.trackableId;
            ResetSamplingWindow();

            string acceptedSummary =
                $"matchedImage={matchedImageName}, duration={samplingDuration:0.##}, validSamples={acceptedSampleCount}, " +
                $"distanceRange={minDistanceMeters:F3}-{maxDistanceMeters:F3}, positionSpread={positionSpreadMeters:F4}, " +
                $"yawSpread={yawSpreadDegrees:F2}, acceptedRootPose={FormatPoseSummary(acceptedRootPose)}, " +
                $"{BuildTrackedImagePoseDebugSummary(matchedImage, acceptedRootPose)}";
            LogRootImageEvent("image_sampling_accepted", acceptedSummary);

            PublishDebugState(
                AutoDeployDebugState.RootPlacementTriggered,
                $"Matched {matchedImageName}, computedRootPose={FormatPoseSummary(acceptedRootPose)}");

            if (m_PlacementDirector.IsPlacementMode)
            {
                if (!m_PlacementManager.TryPlaceRootMarkerAtPose(acceptedRootPose))
                {
                    PublishDebugState(
                        AutoDeployDebugState.RootPlacementFailed,
                        $"Matched {matchedImageName}, failed to place Root at {FormatPoseSummary(acceptedRootPose)}");
                    return;
                }

                LogRootImageEvent(
                    "root_anchor_waiting",
                    $"matchedImage={matchedImageName}, pose={FormatPoseSummary(acceptedRootPose)}, waitTimeoutSeconds={m_WaitForRootAnchorSeconds:0.##}");
                m_WaitForRootAnchorCoroutine = StartCoroutine(WaitForRootAnchorAndDeployLayout(matchedImageName, FormatPoseSummary(acceptedRootPose)));
                return;
            }

            if (!m_PlacementDirector.TryHandleExperienceRootCardPose(acceptedRootPose, matchedImageName, FormatPoseSummary(acceptedRootPose)))
            {
                PublishDebugState(
                    AutoDeployDebugState.RootPlacementFailed,
                    $"Matched {matchedImageName}, failed to bootstrap experience Root at {FormatPoseSummary(acceptedRootPose)}");
                return;
            }

            PlayRecognitionSuccessAudio();
            PublishDebugState(
                AutoDeployDebugState.DeployInvoked,
                $"Matched {matchedImageName}, experience bootstrap invoked. {FormatPoseSummary(acceptedRootPose)}");
        }

        private void SyncRecognitionZoneSessionState()
        {
            if (m_PlacementDirector == null)
                return;

            bool isExperienceMode = m_PlacementDirector.IsExperienceMode;
            if (isExperienceMode && !m_WasExperienceModeLastFrame)
            {
                m_HasEnteredRecognitionZoneThisExperienceSession = false;
                m_LastStatusMessage = string.Empty;
                m_HasRecognitionZonePoseResolvedThisExperienceSession = false;
                m_LastRecognitionZonePoseDetail = string.Empty;
            }
            else if (!isExperienceMode && m_WasExperienceModeLastFrame)
            {
                m_HasEnteredRecognitionZoneThisExperienceSession = false;
                m_LastStatusMessage = string.Empty;
                m_HasRecognitionZonePoseResolvedThisExperienceSession = false;
                m_LastRecognitionZonePoseDetail = string.Empty;
                SetRecognitionZoneVisualVisible(false);
            }

            m_WasExperienceModeLastFrame = isExperienceMode;
        }

        private void UpdateRecognitionZoneGuidance()
        {
            if (m_PlacementDirector == null || !m_PlacementDirector.IsExperienceMode)
            {
                SetRecognitionZoneVisualVisible(false);
                return;
            }

            if (!m_RequireEnterRecognitionZoneInExperienceMode ||
                m_PlacementDirector.ShouldIgnoreExperienceRootCardDetections)
            {
                SetRecognitionZoneVisualVisible(m_PlacementDirector != null &&
                    m_PlacementDirector.ShouldKeepRecognitionZoneVisibleAfterExperienceAcceptance);
                return;
            }

            if (!TryGetRecognitionZoneWorldPose(out _, out _))
            {
                SetRecognitionZoneVisualVisible(false);
                return;
            }

            bool isInsideRecognitionZone = TryIsMainCameraInsideRecognitionZone(out float planarDistanceMeters, out string entryMode);
            if (!m_HasRecognitionZonePoseResolvedThisExperienceSession)
            {
                m_HasRecognitionZonePoseResolvedThisExperienceSession = true;
                LogRootImageEvent(
                    "recognition_zone_available",
                    $"entryMode={entryMode}, insideAtResolve={isInsideRecognitionZone}");
            }

            bool enteredRecognitionZoneThisFrame = isInsideRecognitionZone && !m_HasEnteredRecognitionZoneThisExperienceSession;
            m_HasEnteredRecognitionZoneThisExperienceSession = isInsideRecognitionZone;
            SetRecognitionZoneVisualVisible(true);
            if (enteredRecognitionZoneThisFrame)
            {
                PublishStatusOnce(
                    $"已进入识别光圈，请将 RootCard 保持在 {m_MinDistanceMeters:0.#}m~{m_MaxDistanceMeters:0.#}m 范围内稳定识别约 1 秒。");
                LogRootImageEvent(
                    "recognition_zone_entered",
                    $"entryMode={entryMode}, planarDistance={planarDistanceMeters:0.###}");
            }
        }

        private bool TryIsMainCameraInsideRecognitionZone(out float planarDistanceMeters, out string entryMode)
        {
            planarDistanceMeters = 0f;
            entryMode = "none";

            Camera mainCamera = Camera.main;
            if (mainCamera == null)
                return true;

            Vector3 cameraPosition = mainCamera.transform.position;

            if (EnsureRecognitionZoneVisualInstance() &&
                m_RecognitionZoneVisualAnchor != null)
            {
                entryMode = "visual_anchor";
                return m_RecognitionZoneVisualAnchor.TryContainsPlanarPoint(cameraPosition, out planarDistanceMeters);
            }

            if (TryGetRecognitionZoneWorldPose(out Vector3 zonePosition, out _) &&
                EnsureRecognitionZoneVisualInstance() &&
                m_RecognitionZoneColliders != null &&
                m_RecognitionZoneColliders.Length > 0)
            {
                Vector3 planarSamplePoint = new Vector3(cameraPosition.x, zonePosition.y, cameraPosition.z);
                entryMode = "collider";
                for (int i = 0; i < m_RecognitionZoneColliders.Length; i++)
                {
                    Collider zoneCollider = m_RecognitionZoneColliders[i];
                    if (zoneCollider == null || !zoneCollider.enabled || !zoneCollider.gameObject.activeInHierarchy)
                        continue;

                    Vector3 closestPoint = zoneCollider.ClosestPoint(planarSamplePoint);
                    Vector2 planarOffset = new Vector2(
                        planarSamplePoint.x - closestPoint.x,
                        planarSamplePoint.z - closestPoint.z);
                    planarDistanceMeters = planarOffset.magnitude;
                    if (planarOffset.sqrMagnitude <= 0.0001f)
                        return true;
                }
            }

            if (!TryGetRecognitionZoneWorldPose(out zonePosition, out _))
                return false;

            entryMode = "zone_center";
            Vector2 cameraPlanar = new Vector2(cameraPosition.x, cameraPosition.z);
            Vector2 zonePlanar = new Vector2(zonePosition.x, zonePosition.z);
            planarDistanceMeters = Vector2.Distance(cameraPlanar, zonePlanar);
            float radiusMeters = 1f;
            return planarDistanceMeters <= radiusMeters;
        }

        private bool TryGetRecognitionZoneWorldPose(out Vector3 worldPosition, out Quaternion worldRotation)
        {
            if (TryGetTrackedRecognitionZonePose(out worldPosition, out worldRotation))
                return true;

            if (m_UseTrackedRootPoseAsRecognitionZoneOrigin)
            {
                worldPosition = Vector3.zero;
                worldRotation = Quaternion.identity;
                return false;
            }

            Transform zoneCenter = transform;
            if (zoneCenter == null)
            {
                worldPosition = Vector3.zero;
                worldRotation = Quaternion.identity;
                return false;
            }

            worldPosition = zoneCenter.position;
            worldRotation = zoneCenter.rotation;
            return true;
        }

        private bool ShouldWaitForExperienceAdvanceDistance(ARTrackedImage matchedImage, out string detail)
        {
            detail = string.Empty;

            if (matchedImage == null || Camera.main == null)
                return false;

            Transform imageTransform = matchedImage.transform;
            if (imageTransform == null)
                return false;

            float requiredDistanceMeters = Mathf.Max(0.05f, m_ExperienceRootCardRecognitionDistanceMeters);
            float currentDistanceMeters = Vector3.Distance(Camera.main.transform.position, imageTransform.position);
            if (currentDistanceMeters <= requiredDistanceMeters)
                return false;

            detail =
                $"waiting_for_experience_advance_distance distance={currentDistanceMeters:F3}, " +
                $"required<={requiredDistanceMeters:F3}";
            return true;
        }

        private bool TryGetTrackedRecognitionZonePose(out Vector3 worldPosition, out Quaternion worldRotation)
        {
            worldPosition = Vector3.zero;
            worldRotation = Quaternion.identity;

            if (!m_UseTrackedRootPoseAsRecognitionZoneOrigin ||
                m_PlacementDirector == null ||
                !m_PlacementDirector.IsExperienceMode)
            {
                return false;
            }

            if (!TryGetTrackedRootImage(out ARTrackedImage matchedImage, out _, out _))
            {
                m_LastRecognitionZonePoseDetail = "Waiting for RootCard pose to resolve before showing the recognition zone.";
                return false;
            }

            Transform imageTransform = matchedImage.transform;
            if (imageTransform == null)
            {
                m_LastRecognitionZonePoseDetail = "Waiting for RootCard pose to resolve before showing the recognition zone.";
                return false;
            }

            worldPosition = imageTransform.position;
            worldRotation = imageTransform.rotation;
            m_LastRecognitionZonePoseDetail = "Recognition zone follows the current RootCard tracked image pose.";
            return true;
        }

        private bool EnsureRecognitionZoneVisualInstance()
        {
            Transform trackedZoneParent = GetTrackedRecognitionZoneParent();
            if (m_RecognitionZoneInstance != null)
            {
                if (trackedZoneParent != null && m_RecognitionZoneInstance.transform.parent != trackedZoneParent)
                    m_RecognitionZoneInstance.transform.SetParent(trackedZoneParent, worldPositionStays: false);

                UpdateRecognitionZoneVisualTransform();
                return true;
            }

            if (m_RecognitionZonePrefab == null)
                return false;

            if (trackedZoneParent == null && !TryGetRecognitionZoneWorldPose(out _, out _))
                return false;

            m_RecognitionZoneInstance = trackedZoneParent != null
                ? Instantiate(m_RecognitionZonePrefab, trackedZoneParent, false)
                : Instantiate(m_RecognitionZonePrefab);
            m_RecognitionZoneInstance.name = $"{m_RecognitionZonePrefab.name}_RecognitionZone";
            m_RecognitionZoneInstanceBaseScale = m_RecognitionZoneInstance.transform.localScale;
            m_RecognitionZoneColliders = m_RecognitionZoneInstance.GetComponentsInChildren<Collider>(includeInactive: true);
            m_RecognitionZoneVisualAnchor = m_RecognitionZoneInstance.GetComponentInChildren<RecognitionZoneVisualAnchor>(includeInactive: true);
            UpdateRecognitionZoneVisualTransform();
            return true;
        }

        private void UpdateRecognitionZoneVisualTransform()
        {
            if (m_RecognitionZoneInstance == null)
                return;

            Transform visualTransform = m_RecognitionZoneInstance.transform;

            Transform trackedZoneParent = GetTrackedRecognitionZoneParent();
            if (trackedZoneParent != null)
            {
                if (visualTransform.parent != trackedZoneParent)
                    visualTransform.SetParent(trackedZoneParent, worldPositionStays: false);

                visualTransform.localPosition = Vector3.zero;
                visualTransform.localRotation = Quaternion.identity;
                visualTransform.localScale = m_RecognitionZoneInstanceBaseScale;
                UpdateRecognitionZoneVisualScale();
                return;
            }

            if (!TryGetRecognitionZoneWorldPose(out Vector3 worldPosition, out Quaternion worldRotation))
                return;

            visualTransform.SetPositionAndRotation(worldPosition, worldRotation);
            visualTransform.localScale = m_RecognitionZoneInstanceBaseScale;
            UpdateRecognitionZoneVisualScale();
        }

        private void UpdateRecognitionZoneVisualScale()
        {
            if (m_RecognitionZoneVisualAnchor == null)
                return;

            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                m_RecognitionZoneVisualAnchor.ResetVisualScale();
                return;
            }

            float minVisualScale = Mathf.Max(0.01f, m_RecognitionZoneMinVisualScale);
            float maxVisualScale = Mathf.Max(minVisualScale, m_RecognitionZoneMaxVisualScale);
            if (m_PlacementDirector != null &&
                m_PlacementDirector.ShouldKeepRecognitionZoneVisibleAfterExperienceAcceptance)
            {
                m_RecognitionZoneVisualAnchor.ApplyTriggeredVisualScale(minVisualScale);
                return;
            }

            float nearDistanceMeters = m_PlacementDirector != null && m_PlacementDirector.IsExperienceMode
                ? Mathf.Max(m_MinDistanceMeters, m_ExperienceAdvanceDistanceMeters)
                : m_MinDistanceMeters;
            m_RecognitionZoneVisualAnchor.UpdateVisualScaleByPlanarDistance(
                mainCamera.transform.position,
                nearDistanceMeters,
                m_MaxDistanceMeters,
                minVisualScale,
                maxVisualScale);
        }

        private void PlayRecognitionSuccessAudio()
        {
            if (m_RecognitionSuccessAudioClip == null)
                return;

            AudioSource source = EnsureRecognitionSuccessAudioSource();
            if (source == null)
                return;

            source.volume = 1f;
            source.PlayOneShot(m_RecognitionSuccessAudioClip, Mathf.Clamp01(m_RecognitionSuccessAudioVolume));
        }

        private AudioSource EnsureRecognitionSuccessAudioSource()
        {
            if (m_RecognitionSuccessAudioSource == null)
            {
                var sourceObject = new GameObject("RecognitionSuccessAudioSource");
                sourceObject.transform.SetParent(transform, false);
                m_RecognitionSuccessAudioSource = sourceObject.AddComponent<AudioSource>();
            }

            ConfigureRecognitionSuccessAudioSource(m_RecognitionSuccessAudioSource);
            return m_RecognitionSuccessAudioSource;
        }

        private static void ConfigureRecognitionSuccessAudioSource(AudioSource source)
        {
            if (source == null)
                return;

            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.panStereo = 0f;
        }

        private void SetRecognitionZoneVisualVisible(bool visible)
        {
            if (!visible)
            {
                if (m_RecognitionZoneInstance != null &&
                    (m_RecognitionZoneInstance.activeSelf || m_HasStartedRecognitionZoneMediaPlayback))
                {
                    CloseRecognitionZoneMediaPlayers();
                }

                if (m_RecognitionZoneInstance != null && m_RecognitionZoneInstance.activeSelf)
                    m_RecognitionZoneInstance.SetActive(false);
                m_HasStartedRecognitionZoneMediaPlayback = false;
                return;
            }

            if (!EnsureRecognitionZoneVisualInstance() || m_RecognitionZoneInstance == null)
                return;

            UpdateRecognitionZoneVisualTransform();
            if (!m_RecognitionZoneInstance.activeSelf)
                m_RecognitionZoneInstance.SetActive(true);

            TryPlayRecognitionZoneMediaPlayer();
        }

        private void TryPlayRecognitionZoneMediaPlayer()
        {
            if (m_HasStartedRecognitionZoneMediaPlayback || m_RecognitionZoneInstance == null)
                return;

            var mediaPlayer = m_RecognitionZoneInstance.GetComponentInChildren<MediaPlayer>(includeInactive: true);
            if (mediaPlayer == null)
                return;

            GameObject mediaPlayerObject = mediaPlayer.gameObject;
            if (mediaPlayerObject != null && !mediaPlayerObject.activeSelf)
                mediaPlayerObject.SetActive(true);

            mediaPlayer.PlaybackRate = 1f;

            AVProSafePlayback.Play(
                mediaPlayer,
                m_RecognitionZoneInstance,
                "RootAreaRecognition",
                "PlacementRootImageAutoDeployer.TryPlayRecognitionZoneMediaPlayer");

            m_HasStartedRecognitionZoneMediaPlayback = true;
        }

        private void CloseRecognitionZoneMediaPlayers()
        {
            if (m_RecognitionZoneInstance == null)
                return;

            var mediaPlayers = m_RecognitionZoneInstance.GetComponentsInChildren<MediaPlayer>(includeInactive: true);
            if (mediaPlayers == null || mediaPlayers.Length == 0)
                return;

            int closedCount = 0;
            for (int i = 0; i < mediaPlayers.Length; i++)
            {
                MediaPlayer mediaPlayer = mediaPlayers[i];
                if (mediaPlayer == null || !mediaPlayer.MediaOpened)
                    continue;

                AVProMediaDiagnostics.LogCloseRequest(
                    mediaPlayer,
                    "RootAreaRecognition",
                    "PlacementRootImageAutoDeployer.CloseRecognitionZoneMediaPlayers");
                mediaPlayer.CloseMedia();
                AVProMediaDiagnostics.LogCloseResult(
                    mediaPlayer,
                    "RootAreaRecognition",
                    "PlacementRootImageAutoDeployer.CloseRecognitionZoneMediaPlayers");
                closedCount++;
            }

            if (closedCount > 0)
                LogRootImageEvent("recognition_zone_media_closed", $"count={closedCount}");
        }

        private void PublishStatusOnce(string message)
        {
            if (m_PlacementManager == null || string.IsNullOrWhiteSpace(message) || string.Equals(m_LastStatusMessage, message, StringComparison.Ordinal))
                return;

            m_LastStatusMessage = message;
            m_PlacementManager.onStatusMessage?.Invoke(message);
        }

        private bool TryGetTrackedRootImage(out ARTrackedImage matchedImage, out string matchedImageName, out string trackedImageSummary)
        {
            matchedImage = null;
            matchedImageName = string.Empty;
            trackedImageSummary = string.Empty;

            if (string.IsNullOrWhiteSpace(m_RootReferenceImageName))
            {
                trackedImageSummary = "Root reference image name is empty.";
                return false;
            }

            int seenTrackables = 0;
            ARTrackedImage fallbackMatchedImage = null;
            string fallbackMatchedImageName = string.Empty;
            foreach (ARTrackedImage trackedImage in m_TrackedImageManager.trackables)
            {
                if (trackedImage == null)
                    continue;

                seenTrackables++;
                string referenceImageName = trackedImage.referenceImage.name;
                trackedImageSummary = $"Seen {referenceImageName} ({trackedImage.trackingState}). Expecting {m_RootReferenceImageName}.";

                if (!IsMatchingReferenceImageName(referenceImageName))
                    continue;

                if (trackedImage.trackingState != TrackingState.Tracking)
                    continue;

                if (fallbackMatchedImage == null)
                {
                    fallbackMatchedImage = trackedImage;
                    fallbackMatchedImageName = referenceImageName;
                }

                // 优先选择“不是上一张已消费图片”的 tracking 图，避免旧 trackable 还残留在管理器里时卡死后续识别。
                if (m_ConsumedTrackableId != TrackableId.invalidId &&
                    trackedImage.trackableId == m_ConsumedTrackableId)
                {
                    continue;
                }

                matchedImage = trackedImage;
                matchedImageName = referenceImageName;
                trackedImageSummary = $"Matched {referenceImageName} with tracking state {trackedImage.trackingState} and trackableId {trackedImage.trackableId}.";
                return true;
            }

            if (fallbackMatchedImage != null)
            {
                matchedImage = fallbackMatchedImage;
                matchedImageName = fallbackMatchedImageName;
                trackedImageSummary = $"Matched {fallbackMatchedImageName} with tracking state {fallbackMatchedImage.trackingState} and trackableId {fallbackMatchedImage.trackableId}.";
                return true;
            }

            if (seenTrackables == 0)
                trackedImageSummary = $"No tracked images. Expecting {m_RootReferenceImageName}.";

            return false;
        }

        private bool TryComputeSamplingMetrics(
            ARTrackedImage matchedImage,
            out Pose rootPose,
            out float distanceMeters,
            out float viewAngleDegrees,
            out string issue)
        {
            rootPose = default;
            distanceMeters = 0f;
            viewAngleDegrees = 0f;
            issue = string.Empty;

            if (!TryComputeRootPose(matchedImage, out rootPose, out string poseSummary))
            {
                issue = poseSummary;
                return false;
            }

            if (Camera.main == null)
            {
                issue = "Main camera is missing.";
                return false;
            }

            Transform imageTransform = matchedImage != null ? matchedImage.transform : null;
            if (imageTransform == null)
            {
                issue = "Tracked image transform is missing.";
                return false;
            }

            distanceMeters = Vector3.Distance(Camera.main.transform.position, imageTransform.position);
            if (distanceMeters < m_MinDistanceMeters || distanceMeters > m_MaxDistanceMeters)
            {
                issue =
                    $"distance_out_of_range distance={distanceMeters:F3}, allowed={m_MinDistanceMeters:F3}-{m_MaxDistanceMeters:F3}, pose={poseSummary}";
                LogRootImageEvent("image_sampling_rejected_distance", issue);
                return false;
            }

            viewAngleDegrees = ComputeHorizontalViewAngleDegrees(imageTransform, Camera.main.transform);
            if (viewAngleDegrees > m_MaxViewAngleDegrees)
            {
                issue =
                    $"horizontal_view_angle_out_of_range viewAngle={viewAngleDegrees:F2}, allowed<={m_MaxViewAngleDegrees:F2}, pose={poseSummary}";
                LogRootImageEvent("image_sampling_rejected_angle", issue);
                return false;
            }

            return true;
        }

        private bool TryAggregateStableRootPose(
            out Pose aggregatedRootPose,
            out float positionSpreadMeters,
            out float yawSpreadDegrees,
            out float minDistanceMeters,
            out float maxDistanceMeters,
            out string summary)
        {
            aggregatedRootPose = default;
            positionSpreadMeters = 0f;
            yawSpreadDegrees = 0f;
            minDistanceMeters = 0f;
            maxDistanceMeters = 0f;
            summary = string.Empty;

            if (m_RootImageSamples.Count < Mathf.Max(1, m_MinValidSampleCount))
            {
                summary = $"insufficient_samples count={m_RootImageSamples.Count}, required={m_MinValidSampleCount}";
                return false;
            }

            List<RootImageSample> filteredSamples = FilterOutlierSamples(m_RootImageSamples);
            if (filteredSamples.Count < Mathf.Max(1, m_MinValidSampleCount))
            {
                summary = $"insufficient_filtered_samples count={filteredSamples.Count}, required={m_MinValidSampleCount}";
                return false;
            }

            aggregatedRootPose = AggregateRootPose(filteredSamples);
            minDistanceMeters = float.MaxValue;
            maxDistanceMeters = 0f;

            for (int i = 0; i < filteredSamples.Count; i++)
            {
                RootImageSample sample = filteredSamples[i];
                positionSpreadMeters = Mathf.Max(
                    positionSpreadMeters,
                    Vector3.Distance(sample.rootPose.position, aggregatedRootPose.position));
                yawSpreadDegrees = Mathf.Max(
                    yawSpreadDegrees,
                    Mathf.Abs(Mathf.DeltaAngle(
                        sample.rootPose.rotation.eulerAngles.y,
                        aggregatedRootPose.rotation.eulerAngles.y)));
                minDistanceMeters = Mathf.Min(minDistanceMeters, sample.distanceMeters);
                maxDistanceMeters = Mathf.Max(maxDistanceMeters, sample.distanceMeters);
            }

            if (positionSpreadMeters > m_MaxRootPositionSpreadMeters ||
                yawSpreadDegrees > m_MaxRootYawSpreadDegrees)
            {
                summary =
                    $"spread_out_of_range positionSpread={positionSpreadMeters:F4}, allowed<={m_MaxRootPositionSpreadMeters:F4}, " +
                    $"yawSpread={yawSpreadDegrees:F2}, allowed<={m_MaxRootYawSpreadDegrees:F2}, filteredSamples={filteredSamples.Count}";
                return false;
            }

            summary =
                $"filteredSamples={filteredSamples.Count}, positionSpread={positionSpreadMeters:F4}, " +
                $"yawSpread={yawSpreadDegrees:F2}, acceptedRootPose={FormatPoseSummary(aggregatedRootPose)}";
            return true;
        }

        private List<RootImageSample> FilterOutlierSamples(List<RootImageSample> sourceSamples)
        {
            Pose provisionalPose = AggregateRootPose(sourceSamples);
            float maxPositionDeviation = Mathf.Max(m_MaxRootPositionSpreadMeters * 2f, 0.001f);
            float maxYawDeviation = Mathf.Max(m_MaxRootYawSpreadDegrees * 2f, 0.5f);

            var filteredSamples = new List<RootImageSample>(sourceSamples.Count);
            for (int i = 0; i < sourceSamples.Count; i++)
            {
                RootImageSample sample = sourceSamples[i];
                float positionDeviation = Vector3.Distance(sample.rootPose.position, provisionalPose.position);
                float yawDeviation = Mathf.Abs(Mathf.DeltaAngle(
                    sample.rootPose.rotation.eulerAngles.y,
                    provisionalPose.rotation.eulerAngles.y));
                if (positionDeviation > maxPositionDeviation || yawDeviation > maxYawDeviation)
                    continue;

                filteredSamples.Add(sample);
            }

            return filteredSamples.Count > 0 ? filteredSamples : new List<RootImageSample>(sourceSamples);
        }

        private static Pose AggregateRootPose(List<RootImageSample> samples)
        {
            Vector3 positionSum = Vector3.zero;
            float sinYaw = 0f;
            float cosYaw = 0f;

            for (int i = 0; i < samples.Count; i++)
            {
                Pose pose = samples[i].rootPose;
                positionSum += pose.position;
                float yawRadians = pose.rotation.eulerAngles.y * Mathf.Deg2Rad;
                sinYaw += Mathf.Sin(yawRadians);
                cosYaw += Mathf.Cos(yawRadians);
            }

            Vector3 averagedPosition = positionSum / Mathf.Max(1, samples.Count);
            float averagedYaw = Mathf.Atan2(sinYaw, cosYaw) * Mathf.Rad2Deg;
            Quaternion averagedRotation = Quaternion.Euler(0f, averagedYaw, 0f);
            return new Pose(averagedPosition, averagedRotation);
        }

        private void ResetSamplingWindow()
        {
            m_RootImageSamples.Clear();
            m_SamplingStartedAt = -1f;
            m_SampledTrackableId = TrackableId.invalidId;
        }

        private Transform GetTrackedRecognitionZoneParent()
        {
            if (!m_UseTrackedRootPoseAsRecognitionZoneOrigin)
                return null;

            if (!TryGetTrackedRootImage(out ARTrackedImage matchedImage, out _, out _) || matchedImage == null)
                return null;

            return matchedImage.transform;
        }

        private void UpdateConsumptionState(ARTrackedImage matchedImage)
        {
            if (m_ConsumedTrackableId == TrackableId.invalidId)
                return;

            bool consumedTrackableStillTracking = false;
            if (m_TrackedImageManager != null)
            {
                foreach (ARTrackedImage trackedImage in m_TrackedImageManager.trackables)
                {
                    if (trackedImage == null || trackedImage.trackableId != m_ConsumedTrackableId)
                        continue;

                    if (trackedImage.trackingState == TrackingState.Tracking)
                    {
                        consumedTrackableStillTracking = true;
                        break;
                    }
                }
            }

            if (!consumedTrackableStillTracking)
                m_ConsumedTrackableId = TrackableId.invalidId;
        }

        private bool ShouldRequireImageLostBeforeNextTrigger()
        {
            if (!m_RequireImageLostBeforeNextTrigger)
                return false;

            if (m_PlacementDirector != null && m_PlacementDirector.IsExperienceMode)
                return false;

            // 布展模式下，当前步骤就是定位点时允许同一张 RootCard 在冷却后重复更新 Root。
            if (m_PlacementDirector != null &&
                m_PlacementDirector.IsPlacementMode &&
                m_PlacementDirector.CanAcceptRootCardPlacementUpdate())
            {
                return false;
            }

            return true;
        }

        private bool IsMatchingReferenceImageName(string candidateName)
        {
            if (string.IsNullOrWhiteSpace(candidateName) || string.IsNullOrWhiteSpace(m_RootReferenceImageName))
                return false;

            string trimmedCandidateName = candidateName.Trim();
            string[] configuredNames = m_RootReferenceImageName.Split(new[] { ',', ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (configuredNames.Length == 0)
                return false;

            for (int i = 0; i < configuredNames.Length; i++)
            {
                string configuredName = configuredNames[i].Trim();
                if (string.IsNullOrWhiteSpace(configuredName))
                    continue;

                if (string.Equals(trimmedCandidateName, configuredName, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (trimmedCandidateName.StartsWith(configuredName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private bool TryComputeRootPose(ARTrackedImage matchedImage, out Pose rootPose, out string poseSummary)
        {
            rootPose = default;
            poseSummary = string.Empty;

            if (matchedImage == null)
            {
                poseSummary = "matchedImage is null.";
                return false;
            }

            Transform imageTransform = matchedImage.transform;
            if (imageTransform == null)
            {
                poseSummary = "tracked image transform is missing.";
                return false;
            }

            Vector3 worldPosition = imageTransform.TransformPoint(m_RootImageLocalOffset);
            Quaternion worldRotation = ComputePlanarRotationFromImageUp(
                imageTransform,
                Quaternion.Euler(m_RootRotationOffsetEuler));

            rootPose = new Pose(worldPosition, worldRotation);
            poseSummary =
                $"rootPos={FormatVector3(worldPosition)}, rootRot={FormatVector3(worldRotation.eulerAngles)}, " +
                $"imagePos={FormatVector3(imageTransform.position)}, imageRot={FormatVector3(imageTransform.rotation.eulerAngles)}";
            return true;
        }

        private Quaternion ComputePlanarRotationFromImageUp(Transform imageTransform, Quaternion localRotationOffset)
        {
            Vector3 planarNormal = Vector3.ProjectOnPlane(imageTransform.up, Vector3.up);
            if (planarNormal.sqrMagnitude <= 0.000001f)
                planarNormal = Vector3.ProjectOnPlane(imageTransform.forward, Vector3.up);
            if (planarNormal.sqrMagnitude <= 0.000001f)
                return Quaternion.Euler(0f, imageTransform.rotation.eulerAngles.y, 0f);

            Quaternion baseRotation = Quaternion.LookRotation(planarNormal.normalized, Vector3.up);
            Vector3 forward = Vector3.ProjectOnPlane(baseRotation * localRotationOffset * Vector3.forward, Vector3.up);
            if (forward.sqrMagnitude <= 0.000001f)
                forward = planarNormal;

            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        private static string BuildTrackedImagePoseDebugSummary(ARTrackedImage matchedImage, Pose rootPose)
        {
            if (matchedImage == null || matchedImage.transform == null)
                return "imagePoseDebug=unavailable";

            Transform imageTransform = matchedImage.transform;
            Transform cameraTransform = Camera.main != null ? Camera.main.transform : null;

            Vector3 toCamera = cameraTransform != null
                ? cameraTransform.position - imageTransform.position
                : Vector3.zero;
            string cameraSummary = cameraTransform != null
                ? $", cameraPos={FormatVector3(cameraTransform.position)}, toCameraYaw={FormatPlanarYaw(toCamera)}, normalDotToCamera={Vector3.Dot(imageTransform.up.normalized, toCamera.normalized):F3}"
                : ", cameraPose=unavailable";
            string rightAxisSummary = FormatAxisDebug("right", imageTransform.right);
            string upAxisSummary = FormatAxisDebug("up", imageTransform.up);
            string forwardAxisSummary = FormatAxisDebug("forward", imageTransform.forward);

            return
                $"imagePoseDebug=trackingState={matchedImage.trackingState}, reference={matchedImage.referenceImage.name}, " +
                $"referenceSize=({matchedImage.referenceImage.size.x:F3}, {matchedImage.referenceImage.size.y:F3}), " +
                $"imagePos={FormatVector3(imageTransform.position)}, imageRot={FormatVector3(imageTransform.rotation.eulerAngles)}, " +
                $"rootYaw={rootPose.rotation.eulerAngles.y:F3}, currentYawSource=image_up_flattened, " +
                $"{rightAxisSummary}, " +
                $"{upAxisSummary}, " +
                $"{forwardAxisSummary}, " +
                $"imageUpYaw={FormatPlanarYaw(imageTransform.up)}" +
                cameraSummary;
        }

        private static string FormatAxisDebug(string axisName, Vector3 axis)
        {
            Vector3 planar = Vector3.ProjectOnPlane(axis, Vector3.up);
            return
                $"{axisName}={FormatVector3(axis)}, " +
                $"{axisName}PlanarMag={planar.magnitude:F4}, " +
                $"{axisName}PlanarYaw={FormatPlanarYaw(axis)}";
        }

        private static string FormatPlanarYaw(Vector3 direction)
        {
            Vector3 planar = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (planar.sqrMagnitude <= 0.000001f)
                return "n/a";

            return Quaternion.LookRotation(planar.normalized, Vector3.up).eulerAngles.y.ToString("F3");
        }

        private static float ComputeHorizontalViewAngleDegrees(Transform imageTransform, Transform cameraTransform)
        {
            Vector3 toCamera = cameraTransform.position - imageTransform.position;
            // ARTrackedImage in this project follows the AR Foundation convention where local Y
            // points along the image surface normal and local Z points toward the top of the image.
            Vector3 surfaceNormal = imageTransform.up;
            if (surfaceNormal.sqrMagnitude <= 0.000001f)
                surfaceNormal = imageTransform.forward;

            if (Vector3.Dot(surfaceNormal, toCamera) < 0f)
                surfaceNormal = -surfaceNormal;

            // Keep only left/right yaw by projecting both vectors onto the horizontal plane.
            Vector3 horizontalToCamera = Vector3.ProjectOnPlane(toCamera, Vector3.up);
            Vector3 horizontalSurfaceNormal = Vector3.ProjectOnPlane(surfaceNormal, Vector3.up);

            if (horizontalToCamera.sqrMagnitude <= 0.000001f || horizontalSurfaceNormal.sqrMagnitude <= 0.000001f)
                return 0f;

            return Vector3.Angle(horizontalSurfaceNormal, horizontalToCamera);
        }

        private IEnumerator WaitForRootAnchorAndDeployLayout(string matchedImageName, string poseSummary)
        {
            float timeoutSeconds = Mathf.Max(0.5f, m_WaitForRootAnchorSeconds);
            float deadline = Time.unscaledTime + timeoutSeconds;
            float rootAnchorDetectedAt = -1f;
            float settleSeconds = Mathf.Max(0f, m_RootAnchorSettleSeconds);

            while (Time.unscaledTime < deadline)
            {
                if (m_PlacementDirector == null || m_PlacementManager == null)
                {
                    PublishDebugState(
                        AutoDeployDebugState.RootPlacementFailed,
                        "Placement references became unavailable while waiting for Root anchor.");
                    m_WaitForRootAnchorCoroutine = null;
                    yield break;
                }

                if (!m_PlacementDirector.IsPlacementMode)
                {
                    PublishDebugState(
                        AutoDeployDebugState.RootPlacementFailed,
                        $"Placement mode exited before Root anchor completed. currentMode={m_PlacementDirector.CurrentAppMode}");
                    m_WaitForRootAnchorCoroutine = null;
                    yield break;
                }

                if (m_PlacementManager.HasSavedRootAnchor)
                {
                    if (rootAnchorDetectedAt < 0f)
                    {
                        rootAnchorDetectedAt = Time.unscaledTime;
                        PublishDebugState(
                            AutoDeployDebugState.WaitingForRootAnchor,
                            $"Matched {matchedImageName}, Root anchor detected. Settling for {settleSeconds:0.##}s before deploy.");
                    }

                    if (Time.unscaledTime - rootAnchorDetectedAt < settleSeconds)
                    {
                        yield return null;
                        continue;
                    }

                    LogRootImageEvent(
                        "root_anchor_ready",
                        $"matchedImage={matchedImageName}, pose={poseSummary}, settleSeconds={settleSeconds:0.##}");

                    if (!m_PlacementManager.HasUsableLayoutSnapshot())
                    {
                        PublishDebugState(
                            AutoDeployDebugState.RootOnlyCompleted,
                            $"Matched {matchedImageName}, Root anchored successfully, but no layout is available. {poseSummary}");
                        m_WaitForRootAnchorCoroutine = null;
                        yield break;
                    }

                    PublishDebugState(
                        AutoDeployDebugState.DeployStarting,
                        $"Matched {matchedImageName}, Root anchor settled and one-click deploy is starting. {poseSummary}");
                    m_PlacementDirector.OneClickDeployFromLayout();
                    PublishDebugState(
                        AutoDeployDebugState.DeployInvoked,
                        $"Matched {matchedImageName}, Root anchored and one-click deploy invoked. {poseSummary}");
                    m_WaitForRootAnchorCoroutine = null;
                    yield break;
                }

                rootAnchorDetectedAt = -1f;
                yield return null;
            }

            LogRootImageEvent(
                "root_anchor_timeout",
                $"matchedImage={matchedImageName}, waitTimeoutSeconds={timeoutSeconds:0.##}, pose={poseSummary}");
            PublishDebugState(
                AutoDeployDebugState.RootAnchorTimeout,
                $"Timed out waiting {timeoutSeconds:0.##}s for Root anchor after matching {matchedImageName}. {poseSummary}");
            m_WaitForRootAnchorCoroutine = null;
        }

        private void PublishDebugState(AutoDeployDebugState state, string detail)
        {
            if (!m_LogEvents)
                return;

            detail ??= string.Empty;
            if (m_LastDebugState == state && string.Equals(m_LastDebugDetail, detail, StringComparison.Ordinal))
                return;

            m_LastDebugState = state;
            m_LastDebugDetail = detail;

            string message = string.IsNullOrWhiteSpace(detail)
                ? $"[PlacementRootImageAutoDeployer] {state}"
                : $"[PlacementRootImageAutoDeployer] {state}: {detail}";

            Debug.Log(message, this);
            PlacementDebugFileLogger.Log(message);
        }

        private void LogRootImageEvent(string eventName, string detail)
        {
            if (!m_LogEvents)
                return;

            string message = string.IsNullOrWhiteSpace(detail)
                ? $"[RootCard] event={eventName}"
                : $"[RootCard] event={eventName}, {detail}";
            Debug.Log(message, this);
            PlacementDebugFileLogger.Log(message);
        }

        private static string FormatVector3(Vector3 value)
        {
            return $"({value.x:F3}, {value.y:F3}, {value.z:F3})";
        }

        private static string FormatPoseSummary(Pose pose)
        {
            return $"pos={FormatVector3(pose.position)}, rot={FormatVector3(pose.rotation.eulerAngles)}";
        }
    }
}
