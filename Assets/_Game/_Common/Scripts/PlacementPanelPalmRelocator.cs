using System.Collections;
using UnityEngine;
#if INCLUDE_UNITY_XR_HANDS
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Gestures;
#endif

namespace VFXViewer
{
    public class PlacementPanelPalmRelocator : MonoBehaviour
    {
        private enum HandPoseSource
        {
            Palm,
            Wrist,
            Root,
            EditorDebug
        }

        [Header("运行时引用")]
        [SerializeField] private ChapterPlacementDirector m_PlacementDirector;
        [SerializeField] private ExhibitControlPanel m_ControlPanel;
        [SerializeField] private Transform m_TargetRoot;

#if INCLUDE_UNITY_XR_HANDS
        [SerializeField] private XRHandPose m_LeftPalmUpPose;
        [SerializeField] private XRHandTrackingEvents m_LeftHandTrackingEvents;
#endif

        [Header("触发条件")]
        [SerializeField] private float m_PalmUpHoldDuration = 0.3f;
        [SerializeField] private float m_RelocationCooldown = 1f;

        [Header("面板落点")]
        [SerializeField] private Vector3 m_PalmLocalOffset = new Vector3(0f, 0.08f, 0.12f);
        [SerializeField] private float m_RelocationDuration = 0.18f;
        [SerializeField] private bool m_FaceCameraHorizontally = true;
        [SerializeField] private Vector3 m_AdditionalRotationOffsetEuler = Vector3.zero;

        private bool m_WaitingForPalmReset;
        private float m_PalmUpElapsed;
        private float m_NextAllowedRelocationTime;
        private Coroutine m_RelocationCoroutine;
        private Vector3 m_InitialScale = Vector3.one;
        private bool m_HasCapturedRotationOffset;
        private Quaternion m_CapturedRotationOffset = Quaternion.identity;
        private bool m_HasLoggedPalmRecallReadyState;
        private bool m_HasLoggedPalmHoldStarted;
        private bool m_HasLoggedGestureNotMatched;
        private string m_LastUnavailableReason;
        private HandPoseSource? m_LastLoggedPoseSource;

#if INCLUDE_UNITY_XR_HANDS
        private bool m_IsPalmUpGestureDetected;
        private bool m_HasTrackedPlacementPose;
        private Pose m_LastPlacementPose;
        private bool m_HasSubscribedToHandEvents;
#endif

        private void Awake()
        {
            ResolveRuntimeReferences();
            CaptureInitialScale();
        }

        private void OnEnable()
        {
            ResolveRuntimeReferences();
            CaptureInitialScale();
            CaptureRotationOffsetIfNeeded();
#if INCLUDE_UNITY_XR_HANDS
            EnsureHandTrackingEvents();
            SubscribeToHandTrackingEvents();
#endif
            LogPalmRecallBootstrap();
        }

        private void OnDisable()
        {
            ResetGestureState();
#if INCLUDE_UNITY_XR_HANDS
            UnsubscribeFromHandTrackingEvents();
            m_IsPalmUpGestureDetected = false;
            m_HasTrackedPlacementPose = false;
#endif

            if (m_RelocationCoroutine != null)
            {
                StopCoroutine(m_RelocationCoroutine);
                m_RelocationCoroutine = null;
            }
        }

        private void OnValidate()
        {
            ResolveRuntimeReferences();

            if (m_PalmUpHoldDuration < 0.05f)
                m_PalmUpHoldDuration = 0.05f;

            if (m_RelocationCooldown < 0f)
                m_RelocationCooldown = 0f;

            if (m_RelocationDuration < 0f)
                m_RelocationDuration = 0f;
        }

        private void Update()
        {
            if (!IsPlacementModeActive() || IsPlacementManipulationBusy())
            {
                ResetGestureState();
                return;
            }

            CaptureRotationOffsetIfNeeded();

#if !INCLUDE_UNITY_XR_HANDS
            LogUnavailableState("xrHandsDefineMissing");
            return;
#else
            EnsureHandTrackingEvents();

            if (m_LeftPalmUpPose == null)
            {
                LogUnavailableState("gesturePoseMissing");
                ResetGestureState();
                return;
            }

            if (m_LeftHandTrackingEvents == null)
            {
                LogUnavailableState("trackingEventsMissing");
                ResetGestureState();
                return;
            }

            if (!m_LeftHandTrackingEvents.handIsTracked)
            {
                LogUnavailableState("leftHandNotTracked");
                ResetGestureState();
                return;
            }

            if (!m_HasTrackedPlacementPose)
            {
                LogUnavailableState("palmAndWristPoseUnavailable");
                ResetGestureState();
                return;
            }

            if (!m_IsPalmUpGestureDetected)
            {
                LogGestureNotMatchedOnce();
                m_HasLoggedPalmHoldStarted = false;
                m_PalmUpElapsed = 0f;
                m_WaitingForPalmReset = false;
                return;
            }

            ClearUnavailableState();
            m_HasLoggedGestureNotMatched = false;

            if (m_WaitingForPalmReset || Time.unscaledTime < m_NextAllowedRelocationTime)
                return;

            if (!m_HasLoggedPalmHoldStarted)
            {
                m_HasLoggedPalmHoldStarted = true;
                LogPalmRecall(
                    $"[PalmRecall] holdStarted panel={name} pose={m_LeftPalmUpPose.name} " +
                    $"hold={m_PalmUpHoldDuration:F2} poseSource={m_LastLoggedPoseSource}");
            }

            m_PalmUpElapsed += Time.unscaledDeltaTime;
            if (m_PalmUpElapsed < m_PalmUpHoldDuration)
                return;

            m_PalmUpElapsed = 0f;
            m_HasLoggedPalmHoldStarted = false;
            m_WaitingForPalmReset = true;
            m_NextAllowedRelocationTime = Time.unscaledTime + m_RelocationCooldown;
            RelocatePanelToPalm(m_LastPlacementPose);
#endif
        }

        private void ResolveRuntimeReferences()
        {
            if (m_PlacementDirector == null)
                m_PlacementDirector = FindFirstObjectByType<ChapterPlacementDirector>(FindObjectsInactive.Include);

            if (m_ControlPanel == null)
                m_ControlPanel = GetComponent<ExhibitControlPanel>();

            if (m_TargetRoot == null)
                m_TargetRoot = transform;
        }

        private void CaptureInitialScale()
        {
            var target = GetTargetRoot();
            if (target != null)
                m_InitialScale = target.localScale;
        }

        private Transform GetTargetRoot()
        {
            return m_TargetRoot != null ? m_TargetRoot : transform;
        }

        private bool IsPlacementModeActive()
        {
            return m_PlacementDirector != null && m_PlacementDirector.IsPlacementMode;
        }

        private static bool IsPlacementManipulationBusy()
        {
            return MovableExhibit.IsAnyGrabInProgress || PlacementRootMarker.IsAnyGrabInProgress;
        }

        private void ResetGestureState()
        {
            m_PalmUpElapsed = 0f;
            m_WaitingForPalmReset = false;
            m_HasLoggedPalmHoldStarted = false;
        }

#if INCLUDE_UNITY_XR_HANDS
        private void EnsureHandTrackingEvents()
        {
            if (m_LeftHandTrackingEvents == null)
                m_LeftHandTrackingEvents = GetComponent<XRHandTrackingEvents>();

            if (m_LeftHandTrackingEvents == null)
                m_LeftHandTrackingEvents = gameObject.AddComponent<XRHandTrackingEvents>();

            m_LeftHandTrackingEvents.handedness = Handedness.Left;
            m_LeftHandTrackingEvents.updateType =
                XRHandTrackingEvents.UpdateTypes.Dynamic |
                XRHandTrackingEvents.UpdateTypes.BeforeRender;
        }

        private void SubscribeToHandTrackingEvents()
        {
            if (m_HasSubscribedToHandEvents || m_LeftHandTrackingEvents == null)
                return;

            m_LeftHandTrackingEvents.jointsUpdated.AddListener(OnLeftHandJointsUpdated);
            m_LeftHandTrackingEvents.trackingAcquired.AddListener(OnLeftTrackingAcquired);
            m_LeftHandTrackingEvents.trackingLost.AddListener(OnLeftTrackingLost);
            m_HasSubscribedToHandEvents = true;
        }

        private void UnsubscribeFromHandTrackingEvents()
        {
            if (!m_HasSubscribedToHandEvents || m_LeftHandTrackingEvents == null)
                return;

            m_LeftHandTrackingEvents.jointsUpdated.RemoveListener(OnLeftHandJointsUpdated);
            m_LeftHandTrackingEvents.trackingAcquired.RemoveListener(OnLeftTrackingAcquired);
            m_LeftHandTrackingEvents.trackingLost.RemoveListener(OnLeftTrackingLost);
            m_HasSubscribedToHandEvents = false;
        }

        private void OnLeftTrackingAcquired()
        {
            ClearUnavailableState();
            LogPalmRecall($"[PalmRecall] trackingAcquired panel={name}");
        }

        private void OnLeftTrackingLost()
        {
            m_IsPalmUpGestureDetected = false;
            m_HasTrackedPlacementPose = false;
            LogUnavailableState("leftHandNotTracked");
        }

        private void OnLeftHandJointsUpdated(XRHandJointsUpdatedEventArgs eventArgs)
        {
            var hand = eventArgs.hand;
            if (!hand.isTracked)
            {
                m_IsPalmUpGestureDetected = false;
                m_HasTrackedPlacementPose = false;
                LogUnavailableState("leftHandNotTracked");
                return;
            }

            if (TryGetPlacementPose(hand, out var placementPose, out var poseSource))
            {
                m_LastPlacementPose = placementPose;
                m_HasTrackedPlacementPose = true;
                ClearUnavailableState();
                LogPoseSourceIfChanged(poseSource);
            }
            else
            {
                m_HasTrackedPlacementPose = false;
                LogUnavailableState("palmAndWristPoseUnavailable");
            }

            m_IsPalmUpGestureDetected = m_LeftPalmUpPose != null && m_LeftPalmUpPose.CheckConditions(eventArgs);
            if (m_IsPalmUpGestureDetected)
                m_HasLoggedGestureNotMatched = false;
        }

        private static bool TryGetPlacementPose(XRHand hand, out Pose pose, out HandPoseSource poseSource)
        {
            pose = default;
            poseSource = HandPoseSource.Root;

            var palmJoint = hand.GetJoint(XRHandJointID.Palm);
            if (palmJoint.TryGetPose(out pose))
            {
                poseSource = HandPoseSource.Palm;
                return true;
            }

            var wristJoint = hand.GetJoint(XRHandJointID.Wrist);
            if (wristJoint.TryGetPose(out pose))
            {
                poseSource = HandPoseSource.Wrist;
                return true;
            }

            pose = hand.rootPose;
            poseSource = HandPoseSource.Root;
            return pose.rotation != Quaternion.identity || pose.position != Vector3.zero;
        }
#endif

        private void CaptureRotationOffsetIfNeeded()
        {
            if (m_HasCapturedRotationOffset)
                return;

            var target = GetTargetRoot();
            if (target == null)
                return;

            var cameraTransform = GetReferenceCameraTransform();
            if (cameraTransform == null)
                return;

            Quaternion baseRotation = ComputeFacingRotation(target.position, cameraTransform.position);
            m_CapturedRotationOffset = Quaternion.Inverse(baseRotation) * target.rotation;
            m_HasCapturedRotationOffset = true;
        }

        private Transform GetReferenceCameraTransform()
        {
            return Camera.main != null ? Camera.main.transform : null;
        }

        private void RelocatePanelToPalm(Pose placementPose)
        {
            var target = GetTargetRoot();
            if (target == null)
                return;

            Vector3 targetPosition = placementPose.position + placementPose.rotation * m_PalmLocalOffset;
            Quaternion targetRotation = ComputeTargetRotation(targetPosition);
            bool useTransitionRecall = m_ControlPanel != null && m_ControlPanel.IsPanelVisibleForPlacementRecall();

            LogPalmRecall(
                $"[PalmRecall] trigger panel={name} target={target.name} poseSource={m_LastLoggedPoseSource} " +
                $"targetPos={FormatVector3(targetPosition)} handPos={FormatVector3(placementPose.position)} " +
                $"offset={FormatVector3(m_PalmLocalOffset)} duration={m_RelocationDuration:F2} " +
                $"mode={(useTransitionRecall ? "transition" : "move")}");

            if (m_RelocationCoroutine != null)
                StopCoroutine(m_RelocationCoroutine);

            if (useTransitionRecall)
            {
                m_RelocationCoroutine = StartCoroutine(RelocateWithRecallTransitionCoroutine(target, targetPosition, targetRotation));
                return;
            }

            m_ControlPanel?.ShowPanelForPlacementRecall();
            m_RelocationCoroutine = StartCoroutine(RelocateCoroutine(target, targetPosition, targetRotation));
        }

        public bool TriggerRecallForEditorDebug()
        {
            ResolveRuntimeReferences();

            if (!IsPlacementModeActive())
            {
                LogPalmRecall($"[PalmRecall] editorDebugBlocked panel={name} reason=notPlacementMode");
                return false;
            }

            if (IsPlacementManipulationBusy())
            {
                return false;
            }

            var cameraTransform = GetReferenceCameraTransform();
            if (cameraTransform == null)
            {
                LogPalmRecall($"[PalmRecall] editorDebugBlocked panel={name} reason=missingCamera");
                return false;
            }

            m_LastLoggedPoseSource = HandPoseSource.EditorDebug;
            var debugPose = new Pose(cameraTransform.position, cameraTransform.rotation);
            RelocatePanelToPalm(debugPose);
            return true;
        }

        private Quaternion ComputeTargetRotation(Vector3 targetPosition)
        {
            var cameraTransform = GetReferenceCameraTransform();
            if (cameraTransform == null)
                return GetTargetRoot() != null ? GetTargetRoot().rotation : Quaternion.identity;

            Quaternion baseRotation = ComputeFacingRotation(targetPosition, cameraTransform.position);
            Quaternion additionalOffset = Quaternion.Euler(m_AdditionalRotationOffsetEuler);
            if (m_HasCapturedRotationOffset)
                return baseRotation * m_CapturedRotationOffset * additionalOffset;

            return baseRotation * additionalOffset;
        }

        private Quaternion ComputeFacingRotation(Vector3 position, Vector3 cameraPosition)
        {
            Vector3 forward = cameraPosition - position;
            if (m_FaceCameraHorizontally)
                forward.y = 0f;

            if (forward.sqrMagnitude <= 0.000001f)
                forward = GetTargetRoot() != null ? GetTargetRoot().forward : Vector3.forward;

            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        private IEnumerator RelocateCoroutine(Transform target, Vector3 targetPosition, Quaternion targetRotation)
        {
            Vector3 startPosition = target.position;
            Quaternion startRotation = target.rotation;

            if (m_RelocationDuration <= 0.0001f)
            {
                target.SetPositionAndRotation(targetPosition, targetRotation);
                target.localScale = m_InitialScale;
                LogPalmRecall(
                    $"[PalmRecall] relocatedInstantly panel={name} target={target.name} " +
                    $"worldPos={FormatVector3(target.position)}");
                m_RelocationCoroutine = null;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < m_RelocationDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / m_RelocationDuration);
                float easedT = 1f - Mathf.Pow(1f - t, 3f);
                target.SetPositionAndRotation(
                    Vector3.Lerp(startPosition, targetPosition, easedT),
                    Quaternion.Slerp(startRotation, targetRotation, easedT));
                target.localScale = m_InitialScale;
                yield return null;
            }

            target.SetPositionAndRotation(targetPosition, targetRotation);
            target.localScale = m_InitialScale;
            LogPalmRecall(
                $"[PalmRecall] relocated panel={name} target={target.name} " +
                $"worldPos={FormatVector3(target.position)}");
            m_RelocationCoroutine = null;
        }

        private IEnumerator RelocateWithRecallTransitionCoroutine(Transform target, Vector3 targetPosition, Quaternion targetRotation)
        {
            bool waitingForDisappear = m_ControlPanel != null && m_ControlPanel.BeginPanelRelocationTransition();
            if (waitingForDisappear)
            {
                LogPalmRecall($"[PalmRecall] transitionDisappearStarted panel={name} target={target.name}");

                const float maxWaitSeconds = 2f;
                float elapsed = 0f;
                while (elapsed < maxWaitSeconds &&
                       m_ControlPanel != null &&
                       !m_ControlPanel.IsPanelRelocationTransitionComplete())
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }

                bool transitionCompleted = m_ControlPanel != null && m_ControlPanel.IsPanelRelocationTransitionComplete();
                if (!transitionCompleted && m_ControlPanel != null)
                {
                    LogPalmRecall(
                        $"[PalmRecall] transitionDisappearTimedOut panel={name} target={target.name} " +
                        $"waited={elapsed:F2}");
                    m_ControlPanel.ForceCompletePanelRelocationTransitionForRecall();
                }

                LogPalmRecall(
                    $"[PalmRecall] transitionDisappearFinished panel={name} target={target.name} " +
                    $"waited={elapsed:F2}");
            }

            target.SetPositionAndRotation(targetPosition, targetRotation);
            target.localScale = m_InitialScale;

            LogPalmRecall(
                $"[PalmRecall] relocatedByTransition panel={name} target={target.name} " +
                $"worldPos={FormatVector3(target.position)}");

            m_ControlPanel?.PreparePanelForPlacementRecall();
            yield return null;
            m_ControlPanel?.PlayPanelAppearAnimationForPlacementRecall();
            m_RelocationCoroutine = null;
        }

        private void LogPalmRecallBootstrap()
        {
            if (m_HasLoggedPalmRecallReadyState)
                return;

#if INCLUDE_UNITY_XR_HANDS
            const string xrHandsState = "enabled";
            string gestureName = m_LeftPalmUpPose != null ? m_LeftPalmUpPose.name : "<null>";
#else
            const string xrHandsState = "disabled";
            const string gestureName = "<unavailable>";
#endif
            var target = GetTargetRoot();
            LogPalmRecall(
                $"[PalmRecall] bootstrap panel={name} xrHands={xrHandsState} " +
                $"hasDirector={(m_PlacementDirector != null)} hasControlPanel={(m_ControlPanel != null)} " +
                $"target={(target != null ? target.name : "<null>")} gesture={gestureName}");
            m_HasLoggedPalmRecallReadyState = true;
        }

        private void LogUnavailableState(string reason)
        {
            if (m_LastUnavailableReason == reason)
                return;

            LogPalmRecall($"[PalmRecall] unavailable panel={name} reason={reason}");
            m_LastUnavailableReason = reason;
            m_HasLoggedGestureNotMatched = false;
        }

        private void ClearUnavailableState()
        {
            m_LastUnavailableReason = null;
        }

        private void LogPoseSourceIfChanged(HandPoseSource poseSource)
        {
            if (m_LastLoggedPoseSource == poseSource)
                return;

            LogPalmRecall($"[PalmRecall] poseSource panel={name} source={poseSource}");
            m_LastLoggedPoseSource = poseSource;
        }

        private void LogGestureNotMatchedOnce()
        {
            if (m_HasLoggedGestureNotMatched)
                return;

#if INCLUDE_UNITY_XR_HANDS
            string gestureName = m_LeftPalmUpPose != null ? m_LeftPalmUpPose.name : "<null>";
#else
            const string gestureName = "<unavailable>";
#endif
            LogPalmRecall(
                $"[PalmRecall] blocked panel={name} reason=gestureNotMatched " +
                $"gesture={gestureName} poseSource={m_LastLoggedPoseSource}");
            m_HasLoggedGestureNotMatched = true;
        }

        private static string FormatVector3(Vector3 value)
        {
            return $"({value.x:F3}, {value.y:F3}, {value.z:F3})";
        }

        private void LogPalmRecall(string message)
        {
            Debug.Log(message, this);
        }
    }
}
