using Moodium.Calibration;
using Moodium.Interaction;
using Moodium.UI;
using Unity.XR.CoreUtils;
using UnityEngine;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

namespace Moodium.Flow
{
    /// <summary>Scoped fingertip bridge for the branch UI; all business actions use the same pointer contracts.</summary>
    /// <remarks>Runs before MoodiumSpatialInteractionController: when a system touch begins, this bridge cancels its own
    /// drag first, so the router's PointerStarted for the same slider / swipe / handle is not rejected as a second pointer.</remarks>
    [DefaultExecutionOrder(-50)]
    public sealed class SensoryBagDirectTouchInput : MonoBehaviour
    {
        XRHandSubsystem hands;
        XROrigin origin;
        Collider captured;
        int activePointer;
        WorldCarouselSwipeHandle swipe;
        GlassSlideToEnter slider;
        SensoryBagCalibrationHandle calibration;
        MoodiumSpatialButton button;
        bool capturing;

        void Update()
        {
            // Spatial pinch owns that gesture; don't produce a simultaneous physical tap.
            if (UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count > 0) { Cancel(); return; }
            if (hands == null || !hands.running)
                hands = XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRHandSubsystem>();
            if (hands == null || !hands.running) { Cancel(); return; }
            if (origin == null) origin = FindFirstObjectByType<XROrigin>();
            UpdateHand(hands.leftHand, -1001); UpdateHand(hands.rightHand, -1002);
        }

        void UpdateHand(XRHand hand, int id)
        {
            if (!hand.isTracked || !hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var pose))
            { if (capturing && activePointer == id) Cancel(); return; }
            ProcessContact(id, PalmPressGestureController.TransformTrackingPose(pose, origin != null ? origin.Origin.transform : null).position);
        }

        public void ProcessContact(int id, Vector3 world)
        {
            if (capturing)
            {
                if (id != activePointer) return;
                if (captured == null || !captured.enabled || !captured.gameObject.activeInHierarchy) { Cancel(); return; }
                if (Vector3.Distance(captured.ClosestPoint(world), world) > (calibration != null && calibration.IsDragHandle ? .15f : .03f))
                { Release(); return; }
                swipe?.PointerMoved(id, world); slider?.PointerMoved(id, world); calibration?.PointerMoved(id, world);
                return;
            }
            Collider nearest = null;
            var best = .012f;
            var priority = -1;
            foreach (var collider in GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || !collider.gameObject.activeInHierarchy) continue;
                var c = collider.GetComponentInParent<SensoryBagCalibrationHandle>();
                var s = collider.GetComponentInParent<GlassSlideToEnter>();
                var b = collider.GetComponentInParent<MoodiumSpatialButton>();
                var w = collider.GetComponentInParent<WorldCarouselSwipeHandle>();
                if (c == null && s == null && b == null && w == null) continue;
                if (c != null && !c.CanInteract || w != null && !w.CanInteract) continue;
                var distance = Vector3.Distance(collider.ClosestPoint(world), world);
                var p = c != null || s != null || b != null ? 1 : 0;
                if (distance > .012f || nearest != null && (p < priority || p == priority && distance >= best)) continue;
                nearest = collider; best = distance; priority = p;
            }
            if (nearest == null) return;
            capturing = true; activePointer = id; captured = nearest;
            calibration = nearest.GetComponentInParent<SensoryBagCalibrationHandle>();
            slider = nearest.GetComponentInParent<GlassSlideToEnter>();
            button = nearest.GetComponentInParent<MoodiumSpatialButton>();
            swipe = nearest.GetComponentInParent<WorldCarouselSwipeHandle>();
            if (calibration != null) calibration.PointerStarted(id, world);
            else if (slider != null) slider.PointerStarted(id, world);
            else if (button != null) button.Press();
            else if (swipe != null) swipe.PointerStarted(id, world);
        }
        public void Release()
        {
            if (!capturing) return;
            var w = swipe; var s = slider; var c = calibration; var id = activePointer;
            Clear();
            w?.PointerEnded(id); s?.PointerEnded(id); c?.PointerEnded(id);
        }
        public void Cancel()
        {
            if (!capturing) return;
            var w = swipe; var s = slider; var c = calibration; var id = activePointer;
            Clear();
            w?.CancelPointer(id); s?.CancelPointer(id); c?.PointerEnded(id);
        }
        void Clear() { capturing = false; captured = null; swipe = null; slider = null; calibration = null; button = null; }
        void OnDisable() => Cancel();
    }
}
