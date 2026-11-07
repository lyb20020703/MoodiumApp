using System.Collections.Generic;
using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

namespace Moodium.Interaction
{
    /// <summary>Detects either physical palm touching the tracked capsule collider.</summary>
    [DisallowMultipleComponent]
    public sealed class PalmPressGestureController : MonoBehaviour
    {
        [Header("Palm contact volume (metres)")]
        [SerializeField] Vector3 m_PalmColliderSize = new(0.09f, 0.06f, 0.105f);
        [Tooltip("Offset along the derived palm normal. Keep near zero to cover both palm and hand-back surfaces.")]
        [SerializeField, Range(-0.04f, 0.04f)] float m_PalmForwardOffset;
        [SerializeField, Min(0f)] float m_TargetPadding = 0.008f;
        [SerializeField, Min(0f)] float m_TriggerCooldown = 0.3f;

        [Header("Runtime diagnostics")]
        [SerializeField] bool m_InteractionEnabled;
        [SerializeField] bool m_LeftPalmTracked;
        [SerializeField] bool m_RightPalmTracked;
        [SerializeField] bool m_LeftPalmTouching;
        [SerializeField] bool m_RightPalmTouching;

        readonly Collider[] m_OverlapBuffer = new Collider[24];

        XRHandSubsystem m_HandSubsystem;
        XROrigin m_XROrigin;
        readonly HashSet<ChocolateCapsuleInteraction> m_Targets = new();
        readonly Dictionary<ChocolateCapsuleInteraction, BoxCollider> m_GeneratedTargetColliders = new();
        readonly Dictionary<ChocolateCapsuleInteraction, float> m_LastTriggerTimes = new();
        ChocolateCapsuleInteraction m_LeftContactTarget;
        ChocolateCapsuleInteraction m_RightContactTarget;
        bool m_LoggedSubsystemReady;
        bool m_LoggedSubsystemMissing;

        public bool LeftPalmTouching => m_LeftPalmTouching;
        public bool RightPalmTouching => m_RightPalmTouching;

        public void SetTarget(ChocolateCapsuleInteraction target)
        {
            ClearTargets();
            RegisterTarget(target);
        }

        public void RegisterTarget(ChocolateCapsuleInteraction target)
        {
            if (target == null || !m_Targets.Add(target))
                return;
            EnsureTargetCollider(target);
        }

        public void UnregisterTarget(ChocolateCapsuleInteraction target)
        {
            if (target == null || !m_Targets.Remove(target))
                return;
            if (m_LeftContactTarget == target) m_LeftContactTarget = null;
            if (m_RightContactTarget == target) m_RightContactTarget = null;
            target.PalmContactEnded();
            RemoveGeneratedTargetCollider(target);
            m_LastTriggerTimes.Remove(target);
        }

        public void ClearTargets()
        {
            EndCurrentContact();
            foreach (var target in new List<ChocolateCapsuleInteraction>(m_Targets))
                RemoveGeneratedTargetCollider(target);
            m_Targets.Clear();
            m_LastTriggerTimes.Clear();
        }

        public void SetInteractionEnabled(bool enabled)
        {
            if (m_InteractionEnabled == enabled)
                return;

            m_InteractionEnabled = enabled;
            if (!enabled)
                EndCurrentContact();
        }

        void Awake()
        {
        }

        void OnEnable()
        {
            TrySubscribeToHandSubsystem();
        }

        void Update()
        {
            if (m_HandSubsystem == null)
                TrySubscribeToHandSubsystem();
        }

        void OnDisable()
        {
            if (m_HandSubsystem != null)
                m_HandSubsystem.updatedHands -= OnHandsUpdated;
            m_HandSubsystem = null;
            EndCurrentContact();
        }

        void OnDestroy()
        {
            ClearTargets();
        }

        void OnValidate()
        {
            m_PalmColliderSize.x = Mathf.Max(0.01f, m_PalmColliderSize.x);
            m_PalmColliderSize.y = Mathf.Max(0.005f, m_PalmColliderSize.y);
            m_PalmColliderSize.z = Mathf.Max(0.01f, m_PalmColliderSize.z);
            if (Application.isPlaying)
                EndCurrentContact();
        }

        void TrySubscribeToHandSubsystem()
        {
            var subsystem = XRGeneralSettings.Instance?.Manager?.activeLoader
                ?.GetLoadedSubsystem<XRHandSubsystem>();
            if (subsystem == null)
            {
                if (!m_LoggedSubsystemMissing)
                {
                    m_LoggedSubsystemMissing = true;
                    Debug.LogWarning("[Moodium Palm Press] XRHandSubsystem is not available yet.");
                }
                return;
            }

            m_HandSubsystem = subsystem;
            m_HandSubsystem.updatedHands -= OnHandsUpdated;
            m_HandSubsystem.updatedHands += OnHandsUpdated;
            m_LoggedSubsystemMissing = false;
            if (!m_LoggedSubsystemReady)
            {
                m_LoggedSubsystemReady = true;
                Debug.Log("[Moodium Palm Press] XRHandSubsystem connected.");
            }
        }

        void OnHandsUpdated(
            XRHandSubsystem subsystem,
            XRHandSubsystem.UpdateSuccessFlags updateSuccessFlags,
            XRHandSubsystem.UpdateType updateType)
        {
            if (updateType != XRHandSubsystem.UpdateType.Dynamic)
                return;

            if (!m_InteractionEnabled || m_Targets.Count == 0)
            {
                EndCurrentContact();
                return;
            }

            var now = Time.unscaledTime;
            Physics.SyncTransforms();
            m_LeftPalmTracked = TryGetPalmBox(subsystem.leftHand, out var leftCenter, out var leftRotation);
            m_RightPalmTracked = TryGetPalmBox(subsystem.rightHand, out var rightCenter, out var rightRotation);
            var leftTarget = m_LeftPalmTracked ? FindTouchingTarget(leftCenter, leftRotation) : null;
            var rightTarget = m_RightPalmTracked ? FindTouchingTarget(rightCenter, rightRotation) : null;
            m_LeftPalmTouching = leftTarget != null;
            m_RightPalmTouching = rightTarget != null;
            if(m_LeftContactTarget!=null&&!m_LeftPalmTracked&&rightTarget!=m_LeftContactTarget)
                m_LeftContactTarget.NotifyTrackingLost();
            if(m_RightContactTarget!=null&&!m_RightPalmTracked&&leftTarget!=m_RightContactTarget)
                m_RightContactTarget.NotifyTrackingLost();
            UpdateHandContact(ref m_LeftContactTarget, leftTarget, m_RightContactTarget, "left", now);
            UpdateHandContact(ref m_RightContactTarget, rightTarget, m_LeftContactTarget, "right", now);
            if(m_LeftContactTarget!=null)m_LeftContactTarget.HandContactHeld(leftCenter);
            if(m_RightContactTarget!=null&&m_RightContactTarget!=m_LeftContactTarget)
                m_RightContactTarget.HandContactHeld(rightCenter);
        }

        bool TryGetPalmBox(XRHand hand, out Vector3 center, out Quaternion rotation)
        {
            center = default;
            rotation = Quaternion.identity;
            if (!hand.isTracked)
                return false;

            // Apple visionOS reports XRHandJointID.Palm as WillNeverBeValid.
            // Derive the palm plane from joints that visionOS actually supplies.
            var wrist = hand.GetJoint(XRHandJointID.Wrist);
            var indexMetacarpal = hand.GetJoint(XRHandJointID.IndexMetacarpal);
            var middleMetacarpal = hand.GetJoint(XRHandJointID.MiddleMetacarpal);
            var littleMetacarpal = hand.GetJoint(XRHandJointID.LittleMetacarpal);
            if (!wrist.TryGetPose(out var wristPose) ||
                !indexMetacarpal.TryGetPose(out var indexPose) ||
                !middleMetacarpal.TryGetPose(out var middlePose) ||
                !littleMetacarpal.TryGetPose(out var littlePose))
                return false;

            if (m_XROrigin == null)
                m_XROrigin = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
            var origin = m_XROrigin != null ? m_XROrigin.Origin.transform : null;
            wristPose = TransformTrackingPose(wristPose, origin);
            indexPose = TransformTrackingPose(indexPose, origin);
            middlePose = TransformTrackingPose(middlePose, origin);
            littlePose = TransformTrackingPose(littlePose, origin);
            if (!TryBuildPalmBox(
                    wristPose, indexPose, middlePose, littlePose, out center, out rotation))
                return false;
            center += rotation * Vector3.up * m_PalmForwardOffset;
            return true;
        }

        public static bool TryBuildPalmBox(
            Pose wrist,
            Pose indexMetacarpal,
            Pose middleMetacarpal,
            Pose littleMetacarpal,
            out Vector3 center,
            out Quaternion rotation)
        {
            center = (wrist.position + middleMetacarpal.position) * 0.5f;
            rotation = Quaternion.identity;
            var forward = middleMetacarpal.position - wrist.position;
            var across = littleMetacarpal.position - indexMetacarpal.position;
            if (forward.sqrMagnitude < 0.000001f || across.sqrMagnitude < 0.000001f)
                return false;

            forward.Normalize();
            var normal = Vector3.Cross(forward, across).normalized;
            if (normal.sqrMagnitude < 0.5f)
                return false;
            rotation = Quaternion.LookRotation(forward, normal);
            return true;
        }

        bool IsTouchingTarget(Vector3 center, Quaternion rotation)
        {
            return FindTouchingTarget(center, rotation) != null;
        }

        ChocolateCapsuleInteraction FindTouchingTarget(Vector3 center, Quaternion rotation)
        {
            var count = Physics.OverlapBoxNonAlloc(
                center,
                m_PalmColliderSize * 0.5f,
                m_OverlapBuffer,
                rotation,
                Physics.AllLayers,
                QueryTriggerInteraction.Collide);
            for (var i = 0; i < count; i++)
            {
                var candidate = m_OverlapBuffer[i];
                if (candidate == null)
                    continue;
                var target = candidate.GetComponentInParent<ChocolateCapsuleInteraction>();
                if (target != null && target.isActiveAndEnabled && m_Targets.Contains(target))
                    return target;
            }
            return null;
        }

        void EnsureTargetCollider(ChocolateCapsuleInteraction target)
        {
            if (target.GetComponentInChildren<Collider>(true) != null)
                return;

            var renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                Debug.LogWarning("[Moodium Palm Press] Capsule has no renderer for contact bounds.");
                return;
            }

            var root = target.transform;
            var localBounds = ToLocalBounds(root, renderers[0].bounds);
            for (var i = 1; i < renderers.Length; i++)
            {
                var rendererBounds = ToLocalBounds(root, renderers[i].bounds);
                localBounds.Encapsulate(rendererBounds.min);
                localBounds.Encapsulate(rendererBounds.max);
            }

            var generatedCollider = target.gameObject.AddComponent<BoxCollider>();
            generatedCollider.isTrigger = true;
            generatedCollider.center = localBounds.center;
            var scale = root.lossyScale;
            var localPadding = new Vector3(
                m_TargetPadding / Mathf.Max(Mathf.Abs(scale.x), 0.0001f),
                m_TargetPadding / Mathf.Max(Mathf.Abs(scale.y), 0.0001f),
                m_TargetPadding / Mathf.Max(Mathf.Abs(scale.z), 0.0001f));
            generatedCollider.size = localBounds.size + localPadding * 2f;
            m_GeneratedTargetColliders[target] = generatedCollider;
        }

        public static Pose TransformTrackingPose(Pose trackingPose, Transform xrOrigin)
        {
            if (xrOrigin == null)
                return trackingPose;
            return new Pose(
                xrOrigin.TransformPoint(trackingPose.position),
                xrOrigin.rotation * trackingPose.rotation);
        }

        static Bounds ToLocalBounds(Transform root, Bounds worldBounds)
        {
            var center = worldBounds.center;
            var extents = worldBounds.extents;
            var local = new Bounds(root.InverseTransformPoint(center), Vector3.zero);
            for (var x = -1; x <= 1; x += 2)
            for (var y = -1; y <= 1; y += 2)
            for (var z = -1; z <= 1; z += 2)
            {
                var corner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                local.Encapsulate(root.InverseTransformPoint(corner));
            }
            return local;
        }

        void EndCurrentContact()
        {
            if (m_LeftContactTarget != null)
                m_LeftContactTarget.PalmContactEnded();
            if (m_RightContactTarget != null && m_RightContactTarget != m_LeftContactTarget)
                m_RightContactTarget.PalmContactEnded();
            m_LeftContactTarget = null;
            m_RightContactTarget = null;
            m_LeftPalmTracked = false;
            m_RightPalmTracked = false;
            m_LeftPalmTouching = false;
            m_RightPalmTouching = false;
        }

        void UpdateHandContact(
            ref ChocolateCapsuleInteraction current,
            ChocolateCapsuleInteraction next,
            ChocolateCapsuleInteraction otherHand,
            string handName,
            float now)
        {
            if (current == next)
                return;
            var previous = current;
            current = next;
            if (previous != null && previous != otherHand)
                previous.PalmContactEnded();
            if (next == null || next == otherHand)
                return;
            if (m_LastTriggerTimes.TryGetValue(next, out var last) && now - last < m_TriggerCooldown)
                return;
            m_LastTriggerTimes[next] = now;
            next.PalmContactStarted();
            Debug.Log($"[Moodium Palm Press] Capsule pressed by {handName} palm.");
        }

        void RemoveGeneratedTargetCollider(ChocolateCapsuleInteraction target)
        {
            if (!m_GeneratedTargetColliders.Remove(target, out var generatedCollider) ||
                generatedCollider == null)
                return;
            if (Application.isPlaying)
                Destroy(generatedCollider);
            else
                DestroyImmediate(generatedCollider);
        }

    }
}
