using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

namespace Moodium.Interaction
{
    /// <summary>
    /// Self-contained hand/capsule contact detection. Each tracked Capsule owns one
    /// instance, so object and image tracked Capsules can collide independently.
    /// </summary>
    /// <remarks>
    /// Only the fingertips, the last finger joints and the palm count (not the wrist or the bones inside the hand), so
    /// a wrist resting on the table beside the bag no longer holds the contact. A contact starts within
    /// <see cref="DefaultContactRadius"/> of the bag and ends once every one of those joints is more than
    /// <see cref="DefaultReleaseRadius"/> away: lifting about two centimetres is enough. While a hand stays on the bag,
    /// pressing down or closing the fingers again counts as a new squeeze (<see cref="HandSqueezePulse"/>). The player's
    /// sensitivity setting (<see cref="SqueezeSensitivity"/>) sets both the release distance and the squeeze thresholds.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TrackedCapsuleHandCollision : MonoBehaviour
    {
        /// <summary>A joint this close to the bag's collider starts a contact.</summary>
        public const float DefaultContactRadius = .012f;
        /// <summary>The contact ends once every contact joint is further than this from the collider (sensitivity 3).</summary>
        public const float DefaultReleaseRadius = .02f;
        /// <summary>Releasing always needs at least this much more distance than touching, against jitter.</summary>
        public const float MinReleaseMargin = .003f;
        /// <summary>Shortest time between two squeezes, whether by touching again or squeezing in place.</summary>
        public const float DefaultCooldownSeconds = .35f;

        [SerializeField, Min(0.005f)] float m_ContactRadius = DefaultContactRadius;
        [SerializeField, Min(0f)] float m_ContactReleaseGraceSeconds = 0.18f;
        [SerializeField, Min(0f)] float m_InteractionCooldownSeconds = DefaultCooldownSeconds;
        [SerializeField] bool m_SqueezeInPlace = true;

        /// <summary>The joints that touch the bag when someone presses or squeezes it.</summary>
        public static readonly XRHandJointID[] ContactJointIds =
        {
            XRHandJointID.Palm,
            XRHandJointID.ThumbDistal, XRHandJointID.ThumbTip,
            XRHandJointID.IndexDistal, XRHandJointID.IndexTip,
            XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip,
            XRHandJointID.RingDistal, XRHandJointID.RingTip,
            XRHandJointID.LittleDistal, XRHandJointID.LittleTip
        };

        static readonly XRHandJointID[] TipIds =
        {
            XRHandJointID.ThumbTip, XRHandJointID.IndexTip, XRHandJointID.MiddleTip,
            XRHandJointID.RingTip, XRHandJointID.LittleTip
        };

        readonly Collider[] m_HitBuffer = new Collider[12];
        readonly HandSqueezePulse m_LeftPulse = new();
        readonly HandSqueezePulse m_RightPulse = new();
        ChocolateCapsuleInteraction m_Capsule;
        XRHandSubsystem m_HandSubsystem;
        XROrigin m_XROrigin;
        bool m_Touching;
        bool m_LoggedMissingSubsystem;
        float m_LastSampleTime = float.NegativeInfinity;
        float m_ReleaseRadius = DefaultReleaseRadius;
        HandContactStabilityGate m_ContactGate;

        public bool IsTouching => m_Touching;
        public bool SqueezeInPlace { get => m_SqueezeInPlace; set => m_SqueezeInPlace = value; }

        void Awake()
        {
            m_Capsule = GetComponent<ChocolateCapsuleInteraction>();
            m_ContactGate = new HandContactStabilityGate(
                m_ContactReleaseGraceSeconds, m_InteractionCooldownSeconds);
        }

        void OnEnable()
        {
            TrySubscribe();
        }

        void Update()
        {
            if (m_HandSubsystem == null)
                TrySubscribe();
        }

        void OnDisable()
        {
            if (m_HandSubsystem != null)
                m_HandSubsystem.updatedHands -= OnHandsUpdated;
            m_HandSubsystem = null;
            ForceRelease();
        }

        void TrySubscribe()
        {
            var subsystem = XRGeneralSettings.Instance?.Manager?.activeLoader
                ?.GetLoadedSubsystem<XRHandSubsystem>();
            if (subsystem == null)
            {
                if (!m_LoggedMissingSubsystem)
                {
                    m_LoggedMissingSubsystem = true;
                    Debug.LogWarning("[Moodium Capsule Collision] XRHandSubsystem is not available yet.");
                }
                return;
            }

            m_HandSubsystem = subsystem;
            m_HandSubsystem.updatedHands -= OnHandsUpdated;
            m_HandSubsystem.updatedHands += OnHandsUpdated;
            m_LoggedMissingSubsystem = false;
        }

        void OnHandsUpdated(
            XRHandSubsystem subsystem,
            XRHandSubsystem.UpdateSuccessFlags updateSuccessFlags,
            XRHandSubsystem.UpdateType updateType)
        {
            if (updateType != XRHandSubsystem.UpdateType.Dynamic ||
                m_Capsule == null || !m_Capsule.InteractionEnabled || !isActiveAndEnabled)
            {
                ApplyStableContact(false, default);
                ResetPulses();
                return;
            }

            ApplySensitivity(SqueezeSensitivity.Current);
            // Hysteresis: once touching, a joint has to move further away before the contact ends.
            var radius = m_Touching ? m_ReleaseRadius : m_ContactRadius;
            var left = HandTouchesCapsule(subsystem.leftHand, radius, out var leftContact);
            var right = HandTouchesCapsule(subsystem.rightHand, radius, out var rightContact);
            var wasTouching = m_Touching;
            ApplyStableContact(left || right, left ? leftContact : rightContact);

            var now = Time.unscaledTime;
            var dt = Mathf.Clamp(now - m_LastSampleTime, 0f, .1f);
            m_LastSampleTime = now;
            if (!m_SqueezeInPlace || !wasTouching || !m_Touching)
            {
                // Pulses only count inside a contact that was already running last frame; a new contact starts fresh.
                ResetPulses();
                return;
            }
            var pulsed = StepPulse(subsystem.leftHand, left, m_LeftPulse, dt, "left", out var leftReason) |
                         StepPulse(subsystem.rightHand, right, m_RightPulse, dt, "right", out var rightReason);
            if (!pulsed || !m_ContactGate.TryPulse(now)) return;
            m_Capsule.HandSqueezeInPlace(left ? leftContact : rightContact);
            Debug.Log($"[Moodium Capsule Collision] Squeeze in place ({leftReason ?? rightReason}).");
        }

        bool StepPulse(XRHand hand, bool touching, HandSqueezePulse pulse, float dt, string side, out string reason)
        {
            reason = null;
            if (!touching || !TrySample(hand, out var sample))
            {
                pulse.Reset();
                return false;
            }
            if (!pulse.Update(sample, dt, out var why, out var dip)) return false;
            reason = $"{side} {why} {dip * 1000f:0}mm";
            return true;
        }

        /// <summary>Takes the player's sensitivity level (cheap when it has not changed).</summary>
        public void ApplySensitivity(SqueezeSensitivity.Profile profile)
        {
            m_ReleaseRadius = ReleaseRadiusFor(profile, m_ContactRadius);
            m_LeftPulse.Configure(profile);
            m_RightPulse.Configure(profile);
        }

        public static float ReleaseRadiusFor(SqueezeSensitivity.Profile profile, float contactRadius) =>
            Mathf.Max(profile.ReleaseRadius, contactRadius + MinReleaseMargin);

        public float ReleaseRadius => m_ReleaseRadius;

        void ResetPulses()
        {
            m_LeftPulse.Reset();
            m_RightPulse.Reset();
        }

        Transform TrackingOrigin()
        {
            if (m_XROrigin == null)
                m_XROrigin = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
            return m_XROrigin != null ? m_XROrigin.Origin.transform : null;
        }

        /// <summary>
        /// Height uses world up: the bag lies on a table, and the image anchor's own axes are not a reliable "up".
        /// </summary>
        bool TrySample(XRHand hand, out HandSqueezeSample sample)
        {
            sample = default;
            var origin = TrackingOrigin();
            if (!TryJoint(hand, XRHandJointID.Palm, origin, out var palm)) return false;
            var height = palm.y;
            var spread = 0f;
            foreach (var id in TipIds)
            {
                if (!TryJoint(hand, id, origin, out var tip)) return false;
                spread += Vector3.Distance(tip, palm) / TipIds.Length;
                if (id == XRHandJointID.IndexTip || id == XRHandJointID.MiddleTip || id == XRHandJointID.RingTip)
                    height += tip.y;
            }
            sample = new HandSqueezeSample(height / 4f, spread);
            return true;
        }

        static bool TryJoint(XRHand hand, XRHandJointID id, Transform origin, out Vector3 position)
        {
            position = default;
            if (!hand.GetJoint(id).TryGetPose(out var pose)) return false;
            position = PalmPressGestureController.TransformTrackingPose(pose, origin).position;
            return true;
        }

        bool HandTouchesCapsule(XRHand hand, float radius, out Vector3 contactPosition)
        {
            contactPosition = default;
            if (!hand.isTracked)
                return false;

            foreach (var jointId in ContactJointIds)
                if (JointTouchesCapsule(hand.GetJoint(jointId), radius, out contactPosition))
                    return true;
            return false;
        }

        bool JointTouchesCapsule(XRHandJoint joint, float radius, out Vector3 contactPosition)
        {
            contactPosition = default;
            if (!joint.TryGetPose(out var trackingPose))
                return false;

            var worldPose = PalmPressGestureController.TransformTrackingPose(trackingPose, TrackingOrigin());
            var count = Physics.OverlapSphereNonAlloc(
                worldPose.position,
                radius,
                m_HitBuffer,
                Physics.AllLayers,
                QueryTriggerInteraction.Collide);
            for (var i = 0; i < count; i++)
            {
                var hit = m_HitBuffer[i];
                if (hit != null && hit.GetComponentInParent<ChocolateCapsuleInteraction>() == m_Capsule)
                {
                    contactPosition = hit.ClosestPoint(worldPose.position);
                    return true;
                }
            }
            return false;
        }

        void ApplyStableContact(bool touching, Vector3 contactPosition)
        {
            m_ContactGate ??= new HandContactStabilityGate(
                m_ContactReleaseGraceSeconds, m_InteractionCooldownSeconds);
            var wasTouching = m_Touching;
            var triggerFeedback = m_ContactGate.Update(touching, Time.unscaledTime);
            m_Touching = m_ContactGate.IsContactActive;

            if (!wasTouching && m_Touching)
            {
                if (triggerFeedback)
                    m_Capsule?.HandContactStarted(contactPosition);
                return;
            }
            if (wasTouching && !m_Touching)
            {
                m_Capsule?.PalmContactEnded();
                return;
            }
            if (m_Touching)
                m_Capsule?.HandContactHeld(contactPosition);
        }

        void ForceRelease()
        {
            m_ContactGate?.Reset();
            ResetPulses();
            if (!m_Touching)
                return;
            m_Touching = false;
            m_Capsule?.PalmContactEnded();
        }
    }

    /// <summary>Keeps a physical hand contact stable through brief tracking gaps.</summary>
    public sealed class HandContactStabilityGate
    {
        readonly float m_ReleaseGraceSeconds;
        readonly float m_CooldownSeconds;
        float m_LastTouchTime = float.NegativeInfinity;
        float m_NextAllowedStartTime = float.NegativeInfinity;

        public bool IsContactActive { get; private set; }

        public HandContactStabilityGate(float releaseGraceSeconds, float cooldownSeconds)
        {
            m_ReleaseGraceSeconds = Mathf.Max(0f, releaseGraceSeconds);
            m_CooldownSeconds = Mathf.Max(0f, cooldownSeconds);
        }

        /// <returns>True only when this contact edge may trigger feedback.</returns>
        public bool Update(bool isTouching, float currentTime)
        {
            if (isTouching)
            {
                m_LastTouchTime = currentTime;
                if (IsContactActive)
                    return false;
                IsContactActive = true;
                if (currentTime < m_NextAllowedStartTime)
                    return false;
                m_NextAllowedStartTime = currentTime + m_CooldownSeconds;
                return true;
            }

            if (IsContactActive && currentTime - m_LastTouchTime >= m_ReleaseGraceSeconds)
                IsContactActive = false;
            return false;
        }

        /// <summary>
        /// A squeeze inside a running contact (the hand never left). It shares the cooldown with new contacts, so one
        /// squeeze is never counted twice by both routes.
        /// </summary>
        /// <returns>True when this squeeze may trigger feedback.</returns>
        public bool TryPulse(float currentTime)
        {
            if (!IsContactActive || currentTime < m_NextAllowedStartTime)
                return false;
            m_NextAllowedStartTime = currentTime + m_CooldownSeconds;
            return true;
        }

        public void Reset()
        {
            IsContactActive = false;
            m_LastTouchTime = float.NegativeInfinity;
        }
    }
}
