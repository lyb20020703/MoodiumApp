using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;
using Unity.XR.CoreUtils;
using Moodium.Interaction;

namespace Moodium.NatureWorld
{
    /// <summary>Only updates material parameters. Meshes remain shared, immutable assets.</summary>
    public sealed class BubbleSplatMotion : MonoBehaviour
    {
        public enum Stage { Dormant, Miniature, Inside, HandingOff, Leaving }
        [Header("Motion")]
        [Min(0)] public float WindStrength = 1;
        [Min(0)] public float FloatStrength = .8f;
        [Min(0)] public float SelfAmount = 1;
        [Min(.01f)] public float EnterSeconds = 1.5f, LeaveSeconds = .5f;
        [Range(0, 1)] public float MiniatureStrength = .3f;
        [Header("Hands (metres / seconds)")]
        [Min(0)] public float ForwardDistance = .25f, OpenDistance = .06f, PinchDistance = .035f;
        [Min(0)] public float PinchCooldown = .8f, TriggerDelay = .15f;
        [Min(.01f)] public float RiseTime = .35f, FallTime = 1, PositionTime = .08f, HandoffTime = .3f;
        [Min(.01f)] public float Radius = .25f, RadiusGrowth = .1f, GrowthSeconds = 3, GrowthDecay = 2;
        [Min(0)] public float TrackingGrace = .3f;
        [Range(1, 30)] public float InnerConeDegrees = 4, OuterConeDegrees = 9;
        [Header("Device diagnostics")]
        [Range(0, 8)] public float Boost = 1;
        public bool DebugTint;
        public bool StaticValidation;

        public sealed class HandState
        {
            public Vector3 Position;
            public float Amount, Growth, EligibleTime;
            public float LastSeen = float.NegativeInfinity, LastPinch = float.NegativeInfinity;
            public bool HasPosition;
            bool wasEligible;
            public void Step(bool tracked, Vector3 position, float fingerDistance, bool forward,
                bool enabled, bool handoff, float now, float dt, BubbleSplatMotion settings)
            {
                if (tracked)
                {
                    if (!HasPosition || now - LastSeen > settings.TrackingGrace) Position = position;
                    else Position = Vector3.Lerp(Position, position, BubbleSplatMotionMath.Blend(dt, settings.PositionTime));
                    HasPosition = true; LastSeen = now;
                    if (fingerDistance <= settings.PinchDistance) LastPinch = now;
                    wasEligible = forward && fingerDistance > settings.OpenDistance;
                }
                var eligible = enabled && HasPosition && now - LastSeen <= settings.TrackingGrace &&
                    wasEligible && now - LastPinch >= settings.PinchCooldown;
                EligibleTime = eligible ? EligibleTime + dt : 0;
                var active = eligible && EligibleTime >= settings.TriggerDelay;
                Growth = active ? Mathf.MoveTowards(Growth, 1, dt / Mathf.Max(.01f, settings.GrowthSeconds)) :
                    Mathf.Lerp(Growth, 0, BubbleSplatMotionMath.Blend(dt, settings.GrowthDecay));
                Amount = handoff ? Mathf.MoveTowards(Amount, 0, dt / Mathf.Max(.01f, settings.HandoffTime)) :
                    Mathf.Lerp(Amount, active ? 1 : 0, BubbleSplatMotionMath.Blend(dt, active ? settings.RiseTime : settings.FallTime));
                if (!active && Amount < .003f) Amount = 0;
            }
        }
        readonly HandState[] states = { new HandState(), new HandState() };
        Material material;
        Transform world;
        Camera viewer;
        XROrigin origin;
        XRHandSubsystem hands;
        Stage stage;
        float wind, fromWind, stageTime;
        public float CurrentWind => wind;
        public Stage CurrentStage => stage;

        public void Initialize(Material target, Transform pointRoot, Camera camera)
        {
            material = target; world = pointRoot; viewer = camera;
            origin = FindFirstObjectByType<XROrigin>();
            stage = Stage.Dormant; wind = 0;
        }
        public void SetStage(Stage next)
        {
            if (stage == next) return;
            fromWind = wind; stage = next; stageTime = 0;
        }
        public static Vector4 LocalHand(Transform root, Vector3 worldPosition, float worldRadius)
        {
            var p = root.InverseTransformPoint(worldPosition);
            // Point roots use uniform breathing/miniature scale; keep the radius in physical metres.
            var scale = Mathf.Max(.0001f, Mathf.Abs(root.lossyScale.x));
            return new Vector4(p.x, p.y, p.z, worldRadius / scale);
        }
        public float AdvanceWind(float dt)
        {
            stageTime += Mathf.Max(0, dt);
            switch (stage)
            {
                case Stage.Dormant: wind = 0; break;
                case Stage.Miniature: wind = MiniatureStrength; break;
                case Stage.Inside: wind = Mathf.Lerp(fromWind, WindStrength, Mathf.SmoothStep(0, 1, stageTime / Mathf.Max(.01f, EnterSeconds))); break;
                case Stage.Leaving: wind = Mathf.Lerp(fromWind, 0, stageTime / Mathf.Max(.01f, LeaveSeconds)); break;
                case Stage.HandingOff: wind = fromWind; break;
            }
            return wind;
        }
        void LateUpdate()
        {
            if (material == null || world == null) return;
            var dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
            AdvanceWind(dt);
            if (viewer == null) viewer = Camera.main;
            if (hands == null || !hands.running)
                hands = XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRHandSubsystem>();
            if (origin == null) origin = FindFirstObjectByType<XROrigin>();
            for (var i = 0; i < 2; i++)
            {
                var position = states[i].Position; var distance = 0f;
                var tracked = hands != null && hands.running && viewer != null &&
                    Sample(i == 0 ? hands.leftHand : hands.rightHand, out position, out distance);
                var forward = viewer != null && Vector3.Dot(position - viewer.transform.position, viewer.transform.forward) > ForwardDistance;
                states[i].Step(tracked, position, distance, forward, stage == Stage.Inside && !StaticValidation,
                    stage == Stage.HandingOff, Time.unscaledTime, dt, this);
            }
            var up = world.InverseTransformDirection(Vector3.up).normalized;
            var direction = world.InverseTransformDirection(new Vector3(.8f, 0, .6f));
            direction = Vector3.ProjectOnPlane(direction, up).normalized;
            SetVector("_Up", up); SetVector("_WindDir", direction);
            if (viewer != null) SetVector("_Eye", world.InverseTransformPoint(viewer.transform.position));
            SetVector("_HandA", LocalHand(world, states[0].Position, Radius + RadiusGrowth * states[0].Growth));
            SetVector("_HandB", LocalHand(world, states[1].Position, Radius + RadiusGrowth * states[1].Growth));
            SetVector("_HandAmount", StaticValidation ? Vector4.zero : new Vector4(states[0].Amount, states[1].Amount, 0, 0));
            SetFloat("_WindStrength", StaticValidation ? 0 : wind);
            SetFloat("_SelfAmount", SelfAmount); SetFloat("_FloatStrength", FloatStrength);
            SetFloat("_Boost", Boost); SetFloat("_DebugTint", DebugTint ? 1 : 0);
            SetFloat("_InnerCos", Mathf.Cos(Mathf.Min(InnerConeDegrees, OuterConeDegrees - .1f) * Mathf.Deg2Rad));
            SetFloat("_OuterCos", Mathf.Cos(OuterConeDegrees * Mathf.Deg2Rad));
        }
        bool Sample(XRHand hand, out Vector3 position, out float fingerDistance)
        {
            position = Vector3.zero; fingerDistance = 0;
            if (!hand.isTracked || !hand.GetJoint(XRHandJointID.Palm).TryGetPose(out var palm) ||
                !hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out var thumb) ||
                !hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var index)) return false;
            var tracking = origin != null ? origin.Origin.transform : null;
            position = PalmPressGestureController.TransformTrackingPose(palm, tracking).position;
            fingerDistance = Vector3.Distance(PalmPressGestureController.TransformTrackingPose(thumb, tracking).position,
                PalmPressGestureController.TransformTrackingPose(index, tracking).position);
            return true;
        }
        void SetFloat(string property, float value)
        {
            var previous = material.GetFloat(property);
            if ((value == 0 && previous != 0) || Mathf.Abs(previous - value) > .0001f) material.SetFloat(property, value);
        }
        void SetVector(string property, Vector4 value)
        {
            var previous = material.GetVector(property);
            if ((value.sqrMagnitude == 0 && previous.sqrMagnitude > 0) || (previous - value).sqrMagnitude > .00000001f)
                material.SetVector(property, value);
        }
        void OnDisable() => ResetMotion();
        public void ResetMotion()
        {
            if (material != null)
            {
                material.SetFloat("_WindStrength", 0); material.SetVector("_HandAmount", Vector4.zero);
            }
            wind = 0; fromWind = 0; stageTime = 0; stage = Stage.Dormant;
            for (var i = 0; i < states.Length; i++) states[i] = new HandState();
        }
    }
}
