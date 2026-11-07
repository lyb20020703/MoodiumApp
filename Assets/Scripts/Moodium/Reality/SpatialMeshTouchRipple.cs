using System.Collections.Generic;
using Moodium.CandyWorld;
using Moodium.Interaction;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

namespace Moodium.Reality
{
    /// <summary>Turns direct index-tip contact with AR mesh colliders into a candy-glass ripple.</summary>
    public sealed class SpatialMeshTouchRipple : MonoBehaviour
    {
        [SerializeField, Min(0.005f)] float m_TouchRadius = 0.028f;
        [SerializeField, Min(0.05f)] float m_TriggerCooldown = 0.22f;
        [SerializeField] LayerMask m_CollisionLayers = ~0;

        readonly Collider[] m_OverlapResults = new Collider[24];
        readonly HashSet<Collider> m_SpatialColliders = new();
        ARMeshManager m_MeshManager;
        CandyEnvironmentShaderController m_ShaderController;
        CandyEnergyController m_EnergyController;
        float m_LastRefresh;
        float m_LastTrigger = -100f;
        XRHandSubsystem m_HandSubsystem;
        bool m_InteractionEnabled;
        readonly bool[] m_HandTouching = new bool[2];
        CandySpatialPaintTrail m_PaintTrail;

        public void Configure(
            ARMeshManager meshManager,
            CandyEnergyController energyController,
            CandyEnvironmentShaderController shaderController = null)
        {
            m_MeshManager = meshManager;
            m_EnergyController = energyController;
            m_ShaderController = shaderController;
            m_PaintTrail = GetComponent<CandySpatialPaintTrail>();
            if (m_PaintTrail == null)
                m_PaintTrail = gameObject.AddComponent<CandySpatialPaintTrail>();
            RefreshSpatialColliders();
        }

        public void SetInteractionEnabled(bool enabled)
        {
            m_InteractionEnabled = enabled;
            m_LastTrigger = -100f;
            m_HandTouching[0] = false;
            m_HandTouching[1] = false;
            m_PaintTrail?.SetPaintEnabled(enabled);
            if (enabled)
                RefreshSpatialColliders();
        }

        void Update()
        {
            if (!m_InteractionEnabled || m_MeshManager == null || m_EnergyController == null ||
                m_EnergyController.NormalizedEnergy < 0.999f)
                return;
            if (Time.time - m_LastRefresh > 1f)
                RefreshSpatialColliders();
            if (!TryEnsureHands())
                return;
            m_HandSubsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
            CheckHand(0, m_HandSubsystem.leftHand);
            CheckHand(1, m_HandSubsystem.rightHand);
        }

        bool TryEnsureHands()
        {
            if (m_HandSubsystem != null)
                return true;
            m_HandSubsystem = XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRHandSubsystem>();
            return m_HandSubsystem != null;
        }

        void CheckHand(int handIndex, XRHand hand)
        {
            if (!hand.isTracked)
            {
                EndSurfaceStroke(handIndex);
                return;
            }
            var joint = hand.GetJoint(XRHandJointID.IndexTip);
            if (!joint.TryGetPose(out var pose))
            {
                EndSurfaceStroke(handIndex);
                return;
            }
            var count = Physics.OverlapSphereNonAlloc(pose.position, m_TouchRadius, m_OverlapResults,
                m_CollisionLayers, QueryTriggerInteraction.Ignore);
            var foundSpatialSurface = false;
            for (var i = 0; i < count; i++)
            {
                var collider = m_OverlapResults[i];
                if (collider == null || !m_SpatialColliders.Contains(collider))
                    continue;
                foundSpatialSurface = true;
                var touchPoint = collider.ClosestPoint(pose.position);
                var surfaceNormal = pose.position - touchPoint;
                if (surfaceNormal.sqrMagnitude < 0.000001f)
                    surfaceNormal = Camera.main != null ? -Camera.main.transform.forward : Vector3.up;
                surfaceNormal.Normalize();

                m_PaintTrail?.Paint(handIndex, touchPoint, surfaceNormal);
                if (!m_HandTouching[handIndex] && Time.time - m_LastTrigger >= m_TriggerCooldown)
                {
                    m_LastTrigger = Time.time;
                    m_ShaderController?.TriggerRipple(touchPoint);
                    MoodiumInteractionVFXManager.PlayInteractionEffect(
                        touchPoint,
                        Quaternion.identity);
                    Debug.Log(
                        $"[Candy Spatial Touch] Energy 100% touch burst and paint started at {touchPoint}; " +
                        $"collider={collider.name}");
                    Debug.Log($"Spatial hit detected: collider={collider.name}, point={touchPoint}");
                    Debug.Log($"Particle spawned: spatial point={touchPoint}");
                }
                m_HandTouching[handIndex] = true;
                break;
            }
            if (!foundSpatialSurface)
                EndSurfaceStroke(handIndex);
        }

        void EndSurfaceStroke(int handIndex)
        {
            m_HandTouching[handIndex] = false;
            m_PaintTrail?.EndStroke(handIndex);
        }

        void RefreshSpatialColliders()
        {
            m_LastRefresh = Time.time;
            m_SpatialColliders.Clear();
            if (m_MeshManager == null)
                return;
            foreach (var filter in m_MeshManager.GetComponentsInChildren<MeshFilter>(true))
                foreach (var collider in filter.GetComponentsInChildren<Collider>(true))
                    m_SpatialColliders.Add(collider);
        }
    }

    /// <summary>VisionOS-safe paint using persistent, world-space round particles.</summary>
    [DisallowMultipleComponent]
    public sealed class CandySpatialPaintTrail : MonoBehaviour
    {
        [SerializeField, Range(0.02f, 0.12f)] float m_BrushSize = 0.055f;
        [SerializeField, Range(0.05f, 0.75f)] float m_BrushOpacity = 0.34f;
        bool m_Enabled;

        public void SetPaintEnabled(bool enabled)
        {
            m_Enabled = enabled;
            if (!enabled)
                MoodiumInteractionVFXManager.ClearSpatialPaint();
        }

        public void Paint(int handIndex, Vector3 point, Vector3 normal)
        {
            if (!m_Enabled)
                return;
            handIndex = Mathf.Clamp(handIndex, 0, 1);
            MoodiumInteractionVFXManager.UpdateSpatialPaint(
                handIndex,
                point + normal * 0.003f,
                m_BrushSize,
                new Color(0.94f, 0.48f, 1f, m_BrushOpacity));
        }

        public void EndStroke(int handIndex)
        {
            MoodiumInteractionVFXManager.StopSpatialPaint(handIndex);
        }

        void OnDestroy()
        {
            MoodiumInteractionVFXManager.ClearSpatialPaint();
        }
    }

    /// <summary>
    /// Detects a deliberate fist-to-open-hand gesture at full Candy Energy and
    /// releases the existing visionOS-safe white burst from the palm.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CandyHandOpenBurstGesture : MonoBehaviour
    {
        [SerializeField, Range(0.04f, 0.1f)] float m_FistTipDistance = 0.064f;
        [SerializeField, Range(0.07f, 0.16f)] float m_OpenTipDistance = 0.084f;
        [SerializeField, Range(0.05f, 0.5f)] float m_FistHoldDuration = 0.16f;
        [SerializeField, Range(0.5f, 4f)] float m_OpenGestureWindow = 2.8f;
        [SerializeField, Range(0.2f, 2f)] float m_TriggerCooldown = 0.85f;
        [Header("Pinch-release fallback")]
        [SerializeField, Range(0.015f, 0.045f)] float m_PinchDistance = 0.027f;
        [SerializeField, Range(0.03f, 0.08f)] float m_PinchReleaseDistance = 0.047f;
        [SerializeField, Range(0.2f, 1.2f)] float m_PinchHoldDuration = 0.5f;

        readonly float[] m_FistStartedAt = { -1f, -1f };
        readonly float[] m_ArmedAt = { -1f, -1f };
        readonly float[] m_LastTriggeredAt = { -100f, -100f };
        readonly float[] m_PinchStartedAt = { -1f, -1f };
        readonly bool[] m_PinchArmed = new bool[2];
        readonly bool[] m_HandWasDetected = new bool[2];
        CandyEnergyController m_Energy;
        XRHandSubsystem m_Hands;
        bool m_Enabled;

        public void Configure(CandyEnergyController energy) => m_Energy = energy;

        public void SetGestureEnabled(bool enabled)
        {
            m_Enabled = enabled;
            ResetHand(0);
            ResetHand(1);
        }

        void Update()
        {
            if (!m_Enabled || m_Energy == null || m_Energy.NormalizedEnergy < 0.999f)
            {
                ResetHand(0);
                ResetHand(1);
                return;
            }
            if (!TryEnsureHands())
                return;
            m_Hands.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
            CheckHand(0, m_Hands.leftHand);
            CheckHand(1, m_Hands.rightHand);
        }

        bool TryEnsureHands()
        {
            if (m_Hands == null)
                m_Hands = XRGeneralSettings.Instance?.Manager?.activeLoader
                    ?.GetLoadedSubsystem<XRHandSubsystem>();
            return m_Hands != null && m_Hands.running;
        }

        void CheckHand(int handIndex, XRHand hand)
        {
            if (!hand.isTracked || !TryGetHandState(
                    hand,
                    out var palmPose,
                    out var distances,
                    out var pinchDistance,
                    out var palmDiameter))
            {
                m_HandWasDetected[handIndex] = false;
                ResetHand(handIndex);
                return;
            }
            var handName = handIndex == 0 ? "Left" : "Right";
            if (!m_HandWasDetected[handIndex])
            {
                m_HandWasDetected[handIndex] = true;
                Debug.Log($"Hand detected: {handName}");
            }

            if (UpdatePinchReleaseGesture(
                    handIndex,
                    handName,
                    pinchDistance,
                    palmPose,
                    palmDiameter))
                return;

            var closedFingerCount = 0;
            var openFingerCount = 0;
            // Index, middle, ring and little fingertips provide a stable fist/open
            // distinction; thumb placement varies too much between users.
            for (var i = 0; i < distances.Length; i++)
            {
                if (distances[i] <= m_FistTipDistance) closedFingerCount++;
                if (distances[i] >= m_OpenTipDistance) openFingerCount++;
            }
            var isFist = closedFingerCount >= 3;
            // Three clearly extended fingers are enough; requiring all four made
            // the gesture unreliable for smaller hands and a naturally bent little finger.
            var isOpen = openFingerCount >= 3;

            if (m_ArmedAt[handIndex] < 0f)
            {
                if (!isFist)
                {
                    m_FistStartedAt[handIndex] = -1f;
                    return;
                }
                if (m_FistStartedAt[handIndex] < 0f)
                    m_FistStartedAt[handIndex] = Time.time;
                if (Time.time - m_FistStartedAt[handIndex] >= m_FistHoldDuration)
                {
                    m_ArmedAt[handIndex] = Time.time;
                    Debug.Log($"Gesture state: {handName} fist armed");
                }
                return;
            }

            if (Time.time - m_ArmedAt[handIndex] > m_OpenGestureWindow)
            {
                ResetHand(handIndex);
                return;
            }
            if (!isOpen || Time.time - m_LastTriggeredAt[handIndex] < m_TriggerCooldown)
                return;

            MoodiumInteractionVFXManager.PlayHandInteractionEffect(
                palmPose.position,
                palmPose.rotation,
                palmDiameter);
            m_LastTriggeredAt[handIndex] = Time.time;
            m_FistStartedAt[handIndex] = -1f;
            m_ArmedAt[handIndex] = -1f;
            Debug.Log(
                $"Gesture state: {handName} fist opened; " +
                $"white palm burst at {palmPose.position}.");
            Debug.Log($"Particle spawned: palm={palmPose.position}, palmScale={palmDiameter:0.000}");
        }

        bool UpdatePinchReleaseGesture(
            int handIndex,
            string handName,
            float pinchDistance,
            Pose palmPose,
            float palmDiameter)
        {
            if (!m_PinchArmed[handIndex])
            {
                if (pinchDistance <= m_PinchDistance)
                {
                    if (m_PinchStartedAt[handIndex] < 0f)
                    {
                        m_PinchStartedAt[handIndex] = Time.time;
                        Debug.Log($"Gesture state: {handName} pinch holding");
                    }
                    if (Time.time - m_PinchStartedAt[handIndex] >= m_PinchHoldDuration)
                    {
                        m_PinchArmed[handIndex] = true;
                        Debug.Log($"Gesture state: {handName} pinch armed after {m_PinchHoldDuration:0.0}s");
                    }
                }
                else
                {
                    m_PinchStartedAt[handIndex] = -1f;
                }
                return false;
            }

            if (pinchDistance < m_PinchReleaseDistance ||
                Time.time - m_LastTriggeredAt[handIndex] < m_TriggerCooldown)
                return false;

            MoodiumInteractionVFXManager.PlayHandInteractionEffect(
                palmPose.position,
                palmPose.rotation,
                palmDiameter);
            m_LastTriggeredAt[handIndex] = Time.time;
            m_PinchStartedAt[handIndex] = -1f;
            m_PinchArmed[handIndex] = false;
            m_FistStartedAt[handIndex] = -1f;
            m_ArmedAt[handIndex] = -1f;
            Debug.Log($"Gesture state: {handName} pinch released");
            Debug.Log($"Particle spawned: palm={palmPose.position}, palmScale={palmDiameter:0.000}");
            return true;
        }

        static bool TryGetHandState(
            XRHand hand,
            out Pose palmPose,
            out float[] distances,
            out float pinchDistance,
            out float palmDiameter)
        {
            distances = null;
            pinchDistance = 1f;
            palmDiameter = 0.12f;
            var palm = hand.GetJoint(XRHandJointID.Palm);
            if (!palm.TryGetPose(out palmPose))
                return false;
            var ids = new[]
            {
                XRHandJointID.IndexTip,
                XRHandJointID.MiddleTip,
                XRHandJointID.RingTip,
                XRHandJointID.LittleTip
            };
            distances = new float[ids.Length];
            for (var i = 0; i < ids.Length; i++)
            {
                var joint = hand.GetJoint(ids[i]);
                if (!joint.TryGetPose(out var tipPose))
                    return false;
                distances[i] = Vector3.Distance(palmPose.position, tipPose.position);
            }
            var thumb = hand.GetJoint(XRHandJointID.ThumbTip);
            var index = hand.GetJoint(XRHandJointID.IndexTip);
            if (!thumb.TryGetPose(out var thumbPose) || !index.TryGetPose(out var indexPose))
                return false;
            pinchDistance = Vector3.Distance(thumbPose.position, indexPose.position);
            var wrist = hand.GetJoint(XRHandJointID.Wrist);
            if (wrist.TryGetPose(out var wristPose))
                palmDiameter = Mathf.Clamp(
                    Vector3.Distance(wristPose.position, palmPose.position) * 2.7f,
                    0.08f,
                    0.18f);
            return true;
        }

        void ResetHand(int index)
        {
            m_FistStartedAt[index] = -1f;
            m_ArmedAt[index] = -1f;
            m_PinchStartedAt[index] = -1f;
            m_PinchArmed[index] = false;
        }
    }
}
