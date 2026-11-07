using System;
using System.Collections.Generic;
using Autohand;
using Autohand.Demo;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace VFXViewer
{
    /// <summary>
    /// Activates a trimmed AutoHand OpenXR rig in experience mode and wires it to
    /// AutoHand's official XR Hands tracking/grabbing stack.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AutoHandVisionOSBridge : MonoBehaviour
    {
        private const string GrabbableLayerName = "Grabbable";
        private const string GrabbingLayerName = "Grabbing";
        private const string HandPlayerLayerName = "HandPlayer";
        private const string HandLayerName = "Hand";

        private static readonly string[] s_DisabledObjectNames =
        {
            "FingerBending",
            "Finger Bending Scripts",
            "UIPointer",
            "WristEvent",
        };

        [Header("Experience Rig")]
        [SerializeField] private GameObject xrPlayerPrefab;
        [SerializeField] private Transform experienceRigParent;
        [SerializeField] private ChapterPlacementDirector placementDirector;
        [SerializeField] private GameObject handVisualizer;
        [SerializeField] private bool hideHandVisualizerAlways = true;

        private GameObject m_RuntimeRig;
        private Hand m_LeftHand;
        private Hand m_RightHand;
        private XRHandTrackingEvents m_LeftTrackingEvents;
        private XRHandTrackingEvents m_RightTrackingEvents;
        private OpenXRAutoHandTracking m_LeftTracking;
        private OpenXRAutoHandTracking m_RightTracking;
        private OpenXRAutoHandTrackingGrabber m_LeftTrackingGrabber;
        private OpenXRAutoHandTrackingGrabber m_RightTrackingGrabber;
        private Renderer[] m_LeftHandRenderers = Array.Empty<Renderer>();
        private Renderer[] m_RightHandRenderers = Array.Empty<Renderer>();
        private bool m_ExperienceModeActive;
        private bool m_LoggedMissingLayers;
        private bool m_LoggedMissingRig;
        private bool m_LeftTrackedLastFrame;
        private bool m_RightTrackedLastFrame;
        private void Awake()
        {
            if (placementDirector == null)
                placementDirector = FindFirstObjectByType<ChapterPlacementDirector>(FindObjectsInactive.Include);

            if (experienceRigParent == null)
                experienceRigParent = ResolveRigParent();

            if (handVisualizer == null)
                handVisualizer = FindSceneObjectByName("Hand Visualizer");

            SyncSceneTrackingHandsVisibility();
        }

        private void OnEnable()
        {
            if (placementDirector != null)
                placementDirector.AppModeChanged += OnAppModeChanged;

            HideHandVisualizerIfNeeded();
            ApplyAppMode(placementDirector != null ? placementDirector.CurrentAppMode : ExhibitAppMode.Placement);
        }

        private void Start()
        {
            if (placementDirector == null)
                placementDirector = FindFirstObjectByType<ChapterPlacementDirector>(FindObjectsInactive.Include);

            ApplyAppMode(placementDirector != null ? placementDirector.CurrentAppMode : ExhibitAppMode.Placement);
        }

        private void OnDisable()
        {
            if (placementDirector != null)
                placementDirector.AppModeChanged -= OnAppModeChanged;

            SetExperienceModeActive(false);
            HideHandVisualizerIfNeeded();
        }

        private void Update()
        {
            HideHandVisualizerIfNeeded();
            SyncSceneTrackingHandsVisibility();

            if (!m_ExperienceModeActive)
                return;

            if (!HasRequiredLayers())
                return;

            EnsureRuntimeRig();
            UpdateTrackedHandState(
                handLabel: "Left",
                hand: m_LeftHand,
                trackingEvents: m_LeftTrackingEvents,
                renderers: m_LeftHandRenderers,
                wasTrackedLastFrame: ref m_LeftTrackedLastFrame);
            UpdateTrackedHandState(
                handLabel: "Right",
                hand: m_RightHand,
                trackingEvents: m_RightTrackingEvents,
                renderers: m_RightHandRenderers,
                wasTrackedLastFrame: ref m_RightTrackedLastFrame);

        }

        private void OnAppModeChanged(ExhibitAppMode mode)
        {
            ApplyAppMode(mode);
        }

        private void ApplyAppMode(ExhibitAppMode mode)
        {
            SetExperienceModeActive(mode == ExhibitAppMode.Experience);
        }

        private void SetExperienceModeActive(bool active)
        {
            m_ExperienceModeActive = active;
            HideHandVisualizerIfNeeded();

            if (!active)
            {
                ReleaseAllHands();
                if (m_RuntimeRig != null)
                    m_RuntimeRig.SetActive(false);
                m_LeftTrackedLastFrame = false;
                m_RightTrackedLastFrame = false;
                return;
            }

            EnsureRuntimeRig();
            if (m_RuntimeRig != null && !m_RuntimeRig.activeSelf)
                m_RuntimeRig.SetActive(true);

        }

        private void EnsureRuntimeRig()
        {
            if (m_RuntimeRig == null)
            {
                if (xrPlayerPrefab == null)
                {
                    if (!m_LoggedMissingRig)
                    {
                        Debug.LogError("AutoHandVisionOSBridge is missing the XRPlayer prefab reference.", this);
                        m_LoggedMissingRig = true;
                    }

                    return;
                }

                if (experienceRigParent == null)
                    experienceRigParent = ResolveRigParent();

                m_RuntimeRig = Instantiate(xrPlayerPrefab, experienceRigParent, false);
                m_RuntimeRig.name = "ExperienceXRPlayer";
                m_RuntimeRig.SetActive(false);
                TrimRuntimeRig(m_RuntimeRig);
                CacheHands(m_RuntimeRig);
                ConfigureOfficialTracking();
                m_RuntimeRig.SetActive(m_ExperienceModeActive);
                return;
            }

            if (m_LeftHand == null || m_RightHand == null)
                CacheHands(m_RuntimeRig);

            ConfigureOfficialTracking();
        }

        private void TrimRuntimeRig(GameObject rigRoot)
        {
            if (rigRoot == null)
                return;

            DisableDemoBehaviours(rigRoot);
            DisableTrackedPoseBehaviours(rigRoot);
            DisableProjectionHandsAndProjectors(rigRoot);
            DisableNamedObjects(rigRoot);
            DisableRigCamera(rigRoot);
            DisableAutoHandPlayerBody(rigRoot);
            StripOpenXRInputComponents(rigRoot);
        }

        private static void DisableDemoBehaviours(GameObject rigRoot)
        {
            MonoBehaviour[] behaviours = rigRoot.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                string fullName = behaviour.GetType().FullName;
                if (string.IsNullOrEmpty(fullName))
                    continue;

                if (fullName.StartsWith("Autohand.Demo.", StringComparison.Ordinal) &&
                    !string.Equals(fullName, "Autohand.Demo.OpenXRHandControllerLink", StringComparison.Ordinal) &&
                    !string.Equals(fullName, "Autohand.Demo.OpenXRHandPointGrabLink", StringComparison.Ordinal))
                    behaviour.enabled = false;
            }
        }

        private static void DisableTrackedPoseBehaviours(GameObject rigRoot)
        {
            MonoBehaviour[] behaviours = rigRoot.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                string fullName = behaviour.GetType().FullName;
                if (string.IsNullOrEmpty(fullName))
                    continue;

                if (fullName.Contains("TrackedPoseDriver", StringComparison.Ordinal))
                    behaviour.enabled = false;
            }
        }

        private static void DisableProjectionHandsAndProjectors(GameObject rigRoot)
        {
            HandProjector[] projectors = rigRoot.GetComponentsInChildren<HandProjector>(true);
            for (int i = 0; i < projectors.Length; i++)
            {
                if (projectors[i] != null)
                    projectors[i].enabled = false;
            }

            Hand[] hands = rigRoot.GetComponentsInChildren<Hand>(true);
            for (int i = 0; i < hands.Length; i++)
            {
                Hand hand = hands[i];
                if (hand == null)
                    continue;

                if (!hand.gameObject.name.Contains("Projection", StringComparison.OrdinalIgnoreCase))
                    continue;

                hand.enableMovement = false;
                hand.usingHighlight = false;
                hand.gameObject.SetActive(false);
            }
        }

        private static void DisableNamedObjects(GameObject rigRoot)
        {
            Transform[] transforms = rigRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform child = transforms[i];
                if (child == null)
                    continue;

                for (int nameIndex = 0; nameIndex < s_DisabledObjectNames.Length; nameIndex++)
                {
                    if (!string.Equals(child.name, s_DisabledObjectNames[nameIndex], StringComparison.Ordinal))
                        continue;

                    child.gameObject.SetActive(false);
                    break;
                }
            }
        }

        private static void DisableRigCamera(GameObject rigRoot)
        {
            Camera[] cameras = rigRoot.GetComponentsInChildren<Camera>(true);
            for (int i = 0; i < cameras.Length; i++)
            {
                cameras[i].enabled = false;
                cameras[i].tag = "Untagged";
            }

            AudioListener[] listeners = rigRoot.GetComponentsInChildren<AudioListener>(true);
            for (int i = 0; i < listeners.Length; i++)
                listeners[i].enabled = false;
        }

        private static void DisableAutoHandPlayerBody(GameObject rigRoot)
        {
            AutoHandPlayer autoHandPlayer = rigRoot.GetComponentInChildren<AutoHandPlayer>(true);
            if (autoHandPlayer == null)
                return;

            autoHandPlayer.enabled = false;

            if (autoHandPlayer.TryGetComponent(out Rigidbody body))
            {
                body.isKinematic = true;
                body.detectCollisions = false;
                body.useGravity = false;
            }

            if (autoHandPlayer.TryGetComponent(out Collider bodyCollider))
                bodyCollider.enabled = false;
        }

        private static void StripOpenXRInputComponents(GameObject rigRoot)
        {
            OpenXRHandPlayerControllerLink[] playerLinks = rigRoot.GetComponentsInChildren<OpenXRHandPlayerControllerLink>(true);
            for (int i = 0; i < playerLinks.Length; i++)
            {
                if (playerLinks[i] != null)
                    DestroyImmediate(playerLinks[i]);
            }

            OpenXRAutoHandAxisFingerBender[] fingerBenders = rigRoot.GetComponentsInChildren<OpenXRAutoHandAxisFingerBender>(true);
            for (int i = 0; i < fingerBenders.Length; i++)
            {
                if (fingerBenders[i] != null)
                    DestroyImmediate(fingerBenders[i]);
            }
        }

        private void CacheHands(GameObject rigRoot)
        {
            m_LeftHand = null;
            m_RightHand = null;
            m_LeftHandRenderers = Array.Empty<Renderer>();
            m_RightHandRenderers = Array.Empty<Renderer>();

            Hand[] hands = rigRoot.GetComponentsInChildren<Hand>(true);
            var runtimeHands = new List<Hand>(hands.Length);
            for (int i = 0; i < hands.Length; i++)
            {
                Hand hand = hands[i];
                if (!IsRuntimeDrivenHand(hand))
                    continue;

                runtimeHands.Add(hand);

                if (hand.left)
                {
                    if (IsPreferredRuntimeHand(hand, m_LeftHand))
                        m_LeftHand = hand;
                }
                else
                {
                    if (IsPreferredRuntimeHand(hand, m_RightHand))
                        m_RightHand = hand;
                }
            }

            DisableUnusedRuntimeHands(runtimeHands);

            if (m_LeftHand != null)
            {
                m_LeftHand.enableMovement = true;
                ApplyRuntimeHandGrabMask(m_LeftHand);
                m_LeftHandRenderers = m_LeftHand.GetComponentsInChildren<Renderer>(true);
            }

            if (m_RightHand != null)
            {
                m_RightHand.enableMovement = true;
                ApplyRuntimeHandGrabMask(m_RightHand);
                m_RightHandRenderers = m_RightHand.GetComponentsInChildren<Renderer>(true);
            }
        }

        private void ConfigureOfficialTracking()
        {
            ConfigureTrackedHand(
                hand: m_LeftHand,
                handedness: Handedness.Left,
                trackingEvents: ref m_LeftTrackingEvents,
                tracking: ref m_LeftTracking,
                trackingGrabber: ref m_LeftTrackingGrabber);
            ConfigureTrackedHand(
                hand: m_RightHand,
                handedness: Handedness.Right,
                trackingEvents: ref m_RightTrackingEvents,
                tracking: ref m_RightTracking,
                trackingGrabber: ref m_RightTrackingGrabber);
        }

        private void ConfigureTrackedHand(
            Hand hand,
            Handedness handedness,
            ref XRHandTrackingEvents trackingEvents,
            ref OpenXRAutoHandTracking tracking,
            ref OpenXRAutoHandTrackingGrabber trackingGrabber)
        {
            if (hand == null)
                return;

            bool restoreActive = false;
            bool shouldSetupWhileInactive =
                hand.gameObject.activeSelf &&
                (trackingEvents == null || tracking == null);

            if (shouldSetupWhileInactive)
            {
                hand.gameObject.SetActive(false);
                restoreActive = true;
            }

            trackingEvents = hand.GetComponent<XRHandTrackingEvents>();
            if (trackingEvents == null)
                trackingEvents = hand.gameObject.AddComponent<XRHandTrackingEvents>();

            trackingEvents.handedness = handedness;
            trackingEvents.updateType = XRHandTrackingEvents.UpdateTypes.BeforeRender;

            tracking = hand.GetComponent<OpenXRAutoHandTracking>();
            if (tracking == null)
                tracking = hand.gameObject.AddComponent<OpenXRAutoHandTracking>();

            tracking.hand = hand;
            tracking.controllerLink = null;
            tracking.drawGizmos = false;
            ApplyOfficialTrackingTuning(handedness, tracking);

            trackingGrabber = hand.GetComponent<OpenXRAutoHandTrackingGrabber>();
            if (trackingGrabber == null)
                trackingGrabber = hand.gameObject.AddComponent<OpenXRAutoHandTrackingGrabber>();

            ApplyOfficialGrabberTuning(trackingGrabber, tracking);

            OpenXRHandControllerLink[] controllerLinks = hand.GetComponentsInChildren<OpenXRHandControllerLink>(true);
            for (int i = 0; i < controllerLinks.Length; i++)
            {
                if (controllerLinks[i] != null)
                    controllerLinks[i].enabled = true;
            }

            if (restoreActive)
                hand.gameObject.SetActive(true);

        }

        private void ApplyOfficialTrackingTuning(Handedness handedness, OpenXRAutoHandTracking tracking)
        {
            if (tracking == null)
                return;

            tracking.forwardAxis = AxisEnum.up;
            tracking.handPoseSmoothingSpeed = 0.03f;
            tracking.followPositionSmoothing = 0.333333f;
            tracking.followRotationSmoothing = 0.5f;

            if (handedness == Handedness.Left)
            {
                tracking.upAxis = AxisEnum.left;
                tracking.handOffset = new Vector3(-0.01f, 0f, 0.1f);
                tracking.handRotationOffset = new Vector3(0f, 0f, -90f);
            }
            else
            {
                tracking.upAxis = AxisEnum.right;
                tracking.handOffset = new Vector3(0.01f, 0f, 0.1f);
                tracking.handRotationOffset = new Vector3(0f, 0f, 90f);
            }
        }

        private static void ApplyOfficialGrabberTuning(OpenXRAutoHandTrackingGrabber trackingGrabber, OpenXRAutoHandTracking tracking)
        {
            if (trackingGrabber == null)
                return;

            trackingGrabber.handTracker = tracking;
            trackingGrabber.allowHeldFingerMovement = true;
            trackingGrabber.releaseGrabDelay = 0.35f;
            trackingGrabber.fingerTipRadiusMultiplier = 2f;
            trackingGrabber.useFingerTouchGrabbing = true;
            trackingGrabber.useFingerTouchReleasing = true;
            trackingGrabber.useTouchHoldingWithHeldPose = true;
            trackingGrabber.usePoseGrabbing = false;
            trackingGrabber.minPoseGrabCloseness = 0.25f;
            trackingGrabber.maxPoseGrabCloseness = 0.9f;
            trackingGrabber.minDeltaPoseActivation = 0.01f;
            trackingGrabber.maxDeltaPoseActivation = 0.035f;
            trackingGrabber.usePoseRelease = true;
            trackingGrabber.minPoseReleaseOpenness = 0f;
            trackingGrabber.maxPoseReleaseOpenness = 0.4f;
            trackingGrabber.requiredDeltaPoseReleaseOpenness = 0.15f;
            trackingGrabber.usePoseSqueezing = true;
            trackingGrabber.squeezeUnsqueezeDelay = 0.5f;
            trackingGrabber.squeezePoseSensitvityMultiplier = 1.6f;
            trackingGrabber.enabled = true;
        }

        private void UpdateTrackedHandState(
            string handLabel,
            Hand hand,
            XRHandTrackingEvents trackingEvents,
            Renderer[] renderers,
            ref bool wasTrackedLastFrame)
        {
            if (hand == null || trackingEvents == null)
                return;

            bool isTracked = trackingEvents.handIsTracked;
            if (!isTracked)
            {
                wasTrackedLastFrame = false;
                hand.enableMovement = false;
                ReleaseHand(hand);
                SetRenderersEnabled(renderers, false);
                return;
            }

            wasTrackedLastFrame = true;
            hand.enableMovement = true;
            ApplyRuntimeHandGrabMask(hand);

            SetRenderersEnabled(renderers, true);
        }

        private static void ApplyRuntimeHandGrabMask(Hand hand)
        {
            if (hand == null)
                return;

            int grabbableLayer = LayerMask.NameToLayer(GrabbableLayerName);
            if (grabbableLayer < 0)
            {
                hand.usingHighlight = false;
                hand.highlightLayers = 0;
                return;
            }

            hand.usingHighlight = false;
            hand.highlightLayers = 1 << grabbableLayer;
            if (hand.highlighter != null)
                hand.highlighter.ClearHighlights();
        }

        private void DisableUnusedRuntimeHands(List<Hand> runtimeHands)
        {
            for (int i = 0; i < runtimeHands.Count; i++)
            {
                Hand hand = runtimeHands[i];
                if (hand == null || hand == m_LeftHand || hand == m_RightHand)
                    continue;

                hand.enableMovement = false;
                hand.gameObject.SetActive(false);

                Renderer[] renderers = hand.GetComponentsInChildren<Renderer>(true);
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                    renderers[rendererIndex].enabled = false;
            }
        }

        private static bool IsRuntimeDrivenHand(Hand hand)
        {
            if (hand == null)
                return false;

            string handName = hand.gameObject.name;
            if (handName.Contains("Projection", StringComparison.OrdinalIgnoreCase))
                return false;

            return hand.follow != null || hand.palmTransform != null;
        }

        private static bool IsPreferredRuntimeHand(Hand candidate, Hand current)
        {
            if (candidate == null)
                return false;

            if (current == null)
                return true;

            return GetRuntimeHandPriority(candidate) > GetRuntimeHandPriority(current);
        }

        private static int GetRuntimeHandPriority(Hand hand)
        {
            int priority = 0;
            string handName = hand.gameObject.name;
            string followName = hand.follow != null ? hand.follow.name : string.Empty;

            if (hand.gameObject.activeSelf)
                priority += 100;
            if (hand.gameObject.activeInHierarchy)
                priority += 20;
            if (handName.Contains("RobotHand", StringComparison.OrdinalIgnoreCase))
                priority += 1000;
            if (followName.Contains("OpenXR", StringComparison.OrdinalIgnoreCase))
                priority += 300;
            if (handName.Contains("Classic Hand", StringComparison.OrdinalIgnoreCase))
                priority -= 100;

            return priority;
        }

        private static void ReleaseHand(Hand hand)
        {
            if (hand == null)
                return;

            if (hand.IsGrabbing() || hand.holdingObj != null)
                hand.ForceReleaseGrab();

            hand.SetGrip(0f, 0f);
            hand.gripOffset = 0f;
        }

        private void ReleaseAllHands()
        {
            ReleaseHand(m_LeftHand);
            ReleaseHand(m_RightHand);
            SetRenderersEnabled(m_LeftHandRenderers, false);
            SetRenderersEnabled(m_RightHandRenderers, false);
        }

        private static void SetRenderersEnabled(Renderer[] renderers, bool enabled)
        {
            if (renderers == null)
                return;

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = enabled;
            }
        }

        private bool HasRequiredLayers()
        {
            bool hasLayers =
                LayerMask.NameToLayer(GrabbableLayerName) >= 0 &&
                LayerMask.NameToLayer(GrabbingLayerName) >= 0 &&
                LayerMask.NameToLayer(HandPlayerLayerName) >= 0 &&
                LayerMask.NameToLayer(HandLayerName) >= 0;

            if (!hasLayers && !m_LoggedMissingLayers)
            {
                Debug.LogError(
                    $"AutoHandVisionOSBridge requires the layers '{GrabbableLayerName}', '{GrabbingLayerName}', '{HandPlayerLayerName}' and '{HandLayerName}'.",
                    this);
                m_LoggedMissingLayers = true;
            }

            return hasLayers;
        }

        private Transform ResolveRigParent()
        {
            Transform cameraOffset = transform.Find("Camera Offset");
            return cameraOffset != null ? cameraOffset : transform;
        }

        private void HideHandVisualizerIfNeeded()
        {
            if (!hideHandVisualizerAlways || handVisualizer == null)
                return;

            if (handVisualizer.activeSelf)
                handVisualizer.SetActive(false);
        }

        private void SyncSceneTrackingHandsVisibility()
        {
            bool showSceneTrackingHands = !m_ExperienceModeActive;
            SetSceneTrackingHandVisible("Left Hand Tracking", showSceneTrackingHands);
            SetSceneTrackingHandVisible("Right Hand Tracking", showSceneTrackingHands);
        }

        private static void SetSceneTrackingHandVisible(string objectName, bool visible)
        {
            GameObject trackingRoot = FindSceneObjectByName(objectName);
            if (trackingRoot == null)
                return;

            Renderer[] renderers = trackingRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].enabled = visible;

            MonoBehaviour[] behaviours = trackingRoot.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                if (behaviour.GetType().Name.Contains("XRHandMeshController", StringComparison.Ordinal))
                    behaviour.enabled = visible;
            }
        }

        private static GameObject FindSceneObjectByName(string targetName)
        {
            Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform item = transforms[i];
                if (item != null && string.Equals(item.name, targetName, StringComparison.Ordinal))
                    return item.gameObject;
            }

            return null;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
                return "<null>";

            string path = transform.name;
            Transform current = transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }
    }
}
