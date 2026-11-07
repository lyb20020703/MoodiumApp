using System;
using Moodium.Interaction;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

namespace Moodium.Characters
{
    /// <summary>
    /// Hand interactions for the walking Moodi (after the world reaches 100%).
    ///
    /// - Summon: hold a hand out, palm up, fingers open, for 0.5 s. A ring fills on the palm, Moodi
    ///   says B1, flies over in an arc and lands with a small squash (B2).
    /// - Perched: Moodi rides on the palm. A fingertip of the other hand pats it (C2, every 3rd pat
    ///   Happy + C3). Lifting the palm to the face makes it wave (C4). Shaking the hand tilts it (C5).
    /// - Release: turn the palm over or toss it forward (D1). Walking with Moodi on the palm releases
    ///   it after 1.5 s (D2) so the eyes go back to the path. Losing the hand releases it quietly.
    ///
    /// The component sits on the walk guide's root ("Walking Light Guide"). While HasControl is true
    /// the walk guide must not move that transform; after release the guide takes it back and flies it
    /// to its follow or route point as before.
    /// Hand data comes from XRHandSubsystem, like PalmPressGestureController (visionOS has no Palm
    /// joint, so the palm is derived from the wrist, metacarpals and middle proximal joint).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MoodiHandCompanion : MonoBehaviour
    {
        public enum State { Free, Coming, Perched, Leaving }

        [Header("Summon")]
        [Tooltip("Max angle between the palm and straight up.")]
        [SerializeField, Range(10f, 90f)] float m_PalmUpAngle = 55f;
        [SerializeField, Range(.1f, 1.5f)] float m_SummonHold = .5f;
        [Tooltip("Palm distance from the head, metres.")]
        [SerializeField] Vector2 m_Reach = new(.2f, .7f);
        [SerializeField, Range(.05f, 1f)] float m_MaxHandSpeed = .3f;
        [SerializeField, Range(0f, 5f)] float m_SummonCooldown = 1f;
        [Tooltip("Turn on if, on device, Moodi answers the back of the hand instead of the palm.")]
        [SerializeField] bool m_InvertPalmFacing;

        [Header("Perch")]
        [Tooltip("Palm surface to Moodi's body centre, metres.")]
        [SerializeField, Range(0f, .15f)] float m_PerchHeight = .075f;
        [SerializeField, Range(.15f, .6f)] float m_LiftDistance = .35f;
        [SerializeField, Range(0f, 30f)] float m_MaxTilt = 15f;

        [Header("Release")]
        [SerializeField, Range(-1f, 1f)] float m_FlipDot = .2f;
        [SerializeField, Range(.05f, 1f)] float m_FlipHold = .25f;
        [SerializeField, Range(.3f, 3f)] float m_TossSpeed = .8f;
        [SerializeField, Range(.1f, 2f)] float m_LostHold = .4f;
        [Tooltip("After a pat, or while the other hand is at Moodi: no flip/toss release, and the holding hand may " +
                 "drop out of tracking this long before Moodi leaves the palm.")]
        [SerializeField, Range(.1f, 3f)] float m_LostHoldWhilePatting = 1.5f;
        [SerializeField, Range(0f, 3f)] float m_PatGrace = 1.2f;
        [Tooltip("Other hand's palm within this distance of Moodi's body counts as patting (m).")]
        [SerializeField, Range(.05f, .4f)] float m_PatReach = .16f;
        float m_TouchedAt = -10f;
        [SerializeField, Range(.1f, 2f)] float m_WalkSpeed = .4f;
        [SerializeField, Range(.2f, 5f)] float m_WalkHold = 1.5f;

        [Header("Teaching (seconds)")]
        [SerializeField] float m_RemindSummonAfter = 10f;
        [SerializeField] float m_RemindHandAfter = 90f;

        public event Action Summoned;
        public event Action Landed;
        public event Action Released;

        public State Current => m_State;
        /// <summary>While true the walk guide must not move the guide transform.</summary>
        public bool HasControl => m_State != State.Free;
        /// <summary>0..1 while a palm is held up and the ring is filling.</summary>
        public float SummonProgress => m_Progress;

        struct Hand
        {
            public bool Valid, Right;
            public Vector3 Palm, Facing, Velocity;
            public int Extended;
        }

        Transform m_Guide, m_Visual, m_Viewer;
        MoodiCompanionAnimator m_Animator;
        MoodiVoice m_Voice;
        XRHandSubsystem m_Hands;
        XROrigin m_XROrigin;
        PalmPressGestureController m_Press;
        bool m_SearchedPress;
        SpriteRenderer m_Ring;
        Color m_Tint = Color.white;
        Vector3 m_VisualScale = Vector3.one;

        Hand m_Left, m_Right;
        Vector3 m_PrevLeft, m_PrevRight, m_PrevHead, m_PrevPerchVelocity;
        bool m_HadLeft, m_HadRight, m_HadHead;
        float m_HeadSpeed;

        State m_State;
        bool m_SummonRight, m_Charging, m_SaidNotice, m_Lifted, m_EverSummoned;
        float m_Progress, m_NextSummon, m_LostFor, m_FlipFor, m_WalkFor, m_LiftFor, m_ShakeFor;
        float m_FlightT, m_FlightTime, m_LandedAt = -1f, m_InvitedAt = -1f;
        int m_Pats;
        Vector3 m_FlightStart, m_LeaveStart, m_LeaveEnd, m_Accel;
        Quaternion m_Tilt = Quaternion.identity;

        // ---------- setup ----------

        public void Initialize(Transform guide, Transform visual, MoodiCompanionAnimator animator, MoodiVoice voice,
            Transform viewer, Color tint)
        {
            m_Guide = guide;
            m_Visual = visual;
            m_Viewer = viewer;
            m_Tint = tint;
            m_Voice = voice;
            if (m_Animator != null) m_Animator.Touched -= OnTouched;
            m_Animator = animator;
            if (m_Animator != null) m_Animator.Touched += OnTouched;
            m_VisualScale = visual != null ? visual.localScale : Vector3.one;
            ResetToFree();
        }

        /// <summary>The walk guide invited the user to walk (A1/A2 were said). Starts the summon reminders.</summary>
        public void NotifyInvited() => m_InvitedAt = Time.time;

        void OnDestroy()
        {
            if (m_Animator != null) m_Animator.Touched -= OnTouched;
            if (m_Ring != null) Destroy(m_Ring.gameObject);
        }

        void OnDisable() => ResetToFree();

        // ---------- pure helpers (tested) ----------

        /// <summary>Direction the palm faces, from joints visionOS supplies. Zero if the hand is degenerate.</summary>
        public static Vector3 PalmFacing(Vector3 wrist, Vector3 indexMetacarpal, Vector3 middleMetacarpal,
            Vector3 littleMetacarpal, bool rightHand)
        {
            var forward = middleMetacarpal - wrist;
            var across = littleMetacarpal - indexMetacarpal;
            if (forward.sqrMagnitude < 1e-8f || across.sqrMagnitude < 1e-8f) return Vector3.zero;
            var normal = Vector3.Cross(forward.normalized, across.normalized);
            if (normal.sqrMagnitude < 1e-6f) return Vector3.zero;
            // Same cross product as PalmPressGestureController.TryBuildPalmBox: it points out of the back of
            // the right hand and out of the palm of the left hand.
            return (rightHand ? -normal : normal).normalized;
        }

        /// <summary>A finger counts as open when its tip is well past its knuckle, seen from the wrist.</summary>
        public static bool FingerOpen(Vector3 wrist, Vector3 proximal, Vector3 tip) =>
            Vector3.Distance(wrist, tip) > 1.45f * Vector3.Distance(wrist, proximal);

        public static bool IsPalmUp(Vector3 facing, float maxAngle) =>
            facing.sqrMagnitude > .5f && Vector3.Dot(facing.normalized, Vector3.up) >= Mathf.Cos(maxAngle * Mathf.Deg2Rad);

        /// <summary>Palm held out in front of the face, below the eyes, above the waist, within reach.</summary>
        public static bool IsInSummonZone(Vector3 palm, Vector3 head, Vector3 headForward, Vector2 reach)
        {
            var forward = Vector3.ProjectOnPlane(headForward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) return false;
            var offset = palm - head;
            var distance = offset.magnitude;
            if (distance < reach.x || distance > reach.y) return false;
            if (Vector3.Dot(Vector3.ProjectOnPlane(offset, Vector3.up), forward.normalized) < .12f) return false;
            return offset.y < -.05f && offset.y > -.75f;
        }

        public static Vector3 PerchPoint(Vector3 palm, Vector3 facing, float height) => palm + facing.normalized * height;

        /// <summary>Point on a lifted arc from a to b (t 0..1).</summary>
        public static Vector3 ArcPoint(Vector3 a, Vector3 b, float t, float height)
        {
            t = Mathf.Clamp01(t);
            return Vector3.Lerp(a, b, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * height);
        }

        /// <summary>Landing squash then a small stretch, settling at 1 (t 0..1 over 0.25 s).</summary>
        public static Vector3 Squash(float t)
        {
            t = Mathf.Clamp01(t);
            var a = .15f * (1f - t) * Mathf.Cos(t * Mathf.PI * 1.5f);
            return new Vector3(1f + a, 1f - a, 1f + a);
        }

        public static bool IsFlipped(Vector3 facing, float dot) => Vector3.Dot(facing, Vector3.up) < dot;

        // ---------- frame ----------

        void Update()
        {
            if (m_Guide == null || m_Viewer == null) return;
            var dt = Time.deltaTime;
            if (dt <= 0f) return;
            ReadHead(dt);
            ReadHands(dt);
            switch (m_State)
            {
                case State.Free: UpdateFree(dt); break;
                case State.Coming: UpdateComing(dt); break;
                case State.Perched: UpdatePerched(dt); break;
                case State.Leaving: UpdateLeaving(dt); break;
            }
            UpdateReminders();
        }

        void UpdateFree(float dt)
        {
            var hand = PickSummonHand();
            if (!hand.Valid || Time.time < m_NextSummon)
            {
                m_Progress = Mathf.MoveTowards(m_Progress, 0f, dt * 4f);
                if (m_Progress <= 0f) { m_Charging = false; m_SaidNotice = false; ShowRing(false, default, 0f); }
                else if (m_Charging && CurrentHand().Valid) ShowRing(true, CurrentHand(), m_Progress);
                else ShowRing(false, default, 0f);
                return;
            }
            if (!m_Charging || hand.Right != m_SummonRight) { m_Charging = true; m_SummonRight = hand.Right; }
            m_Progress = Mathf.Min(1f, m_Progress + dt / m_SummonHold);
            ShowRing(true, hand, m_Progress);
            if (!m_SaidNotice && m_Progress > .3f)
            {
                m_SaidNotice = true;
                if (m_Voice != null) m_Voice.SayOneOf("B1");
            }
            if (m_Progress >= 1f) StartComing(hand);
        }

        void StartComing(Hand hand)
        {
            m_State = State.Coming;
            m_FlightStart = m_Guide.position;
            m_FlightT = 0f;
            m_FlightTime = Mathf.Clamp(Vector3.Distance(m_FlightStart, PerchTarget(hand)) / .9f, .6f, .9f);
            m_LostFor = 0f;
            m_EverSummoned = true;
            MoodiVoice.MarkLearned("A3");
            MoodiVoice.MarkLearned("E2");
            Summoned?.Invoke();
        }

        void UpdateComing(float dt)
        {
            var hand = CurrentHand();
            if (!hand.Valid)
            {
                m_LostFor += dt;
                if (m_LostFor > m_LostHold) Release(false, true);
                return;
            }
            m_LostFor = 0f;
            m_FlightT = Mathf.Min(1f, m_FlightT + dt / m_FlightTime);
            var t = Mathf.SmoothStep(0f, 1f, m_FlightT);
            m_Guide.position = ArcPoint(m_FlightStart, PerchTarget(hand), t, .1f);
            ShowRing(true, hand, 1f);
            if (m_FlightT >= 1f) Land();
        }

        void Land()
        {
            m_State = State.Perched;
            m_LandedAt = Time.time;
            m_Pats = 0;
            m_Lifted = false;
            m_FlipFor = m_WalkFor = m_LiftFor = m_ShakeFor = 0f;
            m_PrevPerchVelocity = CurrentHand().Velocity;
            m_Accel = Vector3.zero;
            m_TouchedAt = -10f;
            if (m_Animator != null)
            {
                // Riding the palm moves the root; without this the movement read as walking and Idle never played.
                m_Animator.SetWalking(false);
                m_Animator.SetSupportHand(m_SummonRight ? 1 : 0);
            }
            ShowRing(false, default, 0f);
            if (m_Voice != null) m_Voice.Say("B2");
            Landed?.Invoke();
        }

        void UpdatePerched(float dt)
        {
            var hand = CurrentHand();
            // A pat from the other hand often hides the holding hand from tracking for a moment, or makes its joints
            // jump. Right after a touch (or while the other hand is at Moodi) Moodi stays put instead of falling off.
            var patting = Time.time - m_TouchedAt < m_PatGrace || OtherHandNear();
            if (!hand.Valid)
            {
                m_LostFor += dt;
                if (m_LostFor > (patting ? m_LostHoldWhilePatting : m_LostHold)) Release(false, true);
                return;
            }
            m_LostFor = 0f;

            // Ride the palm.
            m_Guide.position = Vector3.Lerp(m_Guide.position, PerchTarget(hand), 1f - Mathf.Exp(-30f * dt));

            // Landing squash.
            var sinceLanding = Time.time - m_LandedAt;
            if (m_Visual != null)
                m_Visual.localScale = Vector3.Scale(m_VisualScale, sinceLanding < .25f ? Squash(sinceLanding / .25f) : Vector3.one);

            // Lean against the hand's sideways acceleration.
            var accel = (hand.Velocity - m_PrevPerchVelocity) / dt;
            m_PrevPerchVelocity = hand.Velocity;
            m_Accel = Vector3.Lerp(m_Accel, Vector3.ProjectOnPlane(accel, Vector3.up), 1f - Mathf.Exp(-10f * dt));
            var lean = Mathf.Clamp(m_Accel.magnitude * 2.5f, 0f, m_MaxTilt);
            var axis = Vector3.Cross(Vector3.up, m_Accel);
            var tilt = axis.sqrMagnitude > 1e-6f ? Quaternion.AngleAxis(-lean, axis.normalized) : Quaternion.identity;
            m_Tilt = Quaternion.Slerp(m_Tilt, tilt, 1f - Mathf.Exp(-8f * dt));
            m_Guide.rotation = m_Tilt;
            m_ShakeFor = m_Accel.magnitude > 4f ? m_ShakeFor + dt : Mathf.Max(0f, m_ShakeFor - dt);
            if (m_ShakeFor > .2f) { m_ShakeFor = 0f; if (m_Voice != null) m_Voice.Say("C5"); }

            // Lift to the face → wave.
            var toHead = Vector3.Distance(hand.Palm, m_Viewer.position);
            if (toHead < m_LiftDistance)
            {
                m_LiftFor += dt;
                if (!m_Lifted && m_LiftFor > .4f)
                {
                    m_Lifted = true;
                    if (m_Animator != null) m_Animator.PlayWave();
                    if (m_Voice != null) m_Voice.Say("C4");
                }
            }
            else
            {
                m_LiftFor = 0f;
                if (toHead > m_LiftDistance + .1f) m_Lifted = false;
            }

            // Teaching, once per session (MoodiVoice drops repeats).
            if (m_Voice != null)
            {
                if (sinceLanding > 1f) m_Voice.Say("C1");
                if (sinceLanding > 6f || m_Pats >= 3) m_Voice.Say("C6");
            }

            // Release: palm turned over, a toss, or walking off with Moodi in hand.
            m_FlipFor = IsFlipped(hand.Facing, m_FlipDot) && !patting ? m_FlipFor + dt : 0f;
            if (m_FlipFor >= m_FlipHold) { Release(false, false); return; }
            var forward = Vector3.ProjectOnPlane(m_Viewer.forward, Vector3.up).normalized;
            if (!patting && Vector3.Dot(hand.Velocity, (forward + Vector3.up).normalized) > m_TossSpeed && sinceLanding > .4f)
            { Release(false, false); return; }
            m_WalkFor = m_HeadSpeed > m_WalkSpeed ? m_WalkFor + dt : 0f;
            if (m_WalkFor >= m_WalkHold) Release(true, false);
        }

        void Release(bool walking, bool quiet)
        {
            m_State = State.Leaving;
            m_LeaveStart = m_Guide.position;
            var forward = Vector3.ProjectOnPlane(m_Viewer.forward, Vector3.up);
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            m_LeaveEnd = m_LeaveStart + forward * .12f + Vector3.up * .05f;
            m_FlightT = 0f;
            m_NextSummon = Time.time + m_SummonCooldown;
            m_Progress = 0f;
            m_Charging = false;
            m_SaidNotice = false;
            ShowRing(false, default, 0f);
            if (m_Visual != null) m_Visual.localScale = m_VisualScale;
            if (m_Animator != null) { m_Animator.SetSupportHand(-1); m_Animator.ClearManualWalk(); }
            if (!quiet && m_Voice != null) m_Voice.Say(walking ? "D2" : "D1");
            MoodiVoice.MarkLearned("C6");
            Released?.Invoke();
        }

        void UpdateLeaving(float dt)
        {
            m_FlightT = Mathf.Min(1f, m_FlightT + dt / .45f);
            m_Guide.position = ArcPoint(m_LeaveStart, m_LeaveEnd, Mathf.SmoothStep(0f, 1f, m_FlightT), .04f);
            m_Tilt = Quaternion.Slerp(m_Tilt, Quaternion.identity, 1f - Mathf.Exp(-12f * dt));
            m_Guide.rotation = m_Tilt;
            if (m_FlightT >= 1f) ResetToFree();
        }

        void ResetToFree()
        {
            m_State = State.Free;
            m_Progress = 0f;
            m_Charging = false;
            m_SaidNotice = false;
            m_Tilt = Quaternion.identity;
            if (m_Guide != null) m_Guide.rotation = Quaternion.identity;
            if (m_Visual != null) m_Visual.localScale = m_VisualScale;
            if (m_Animator != null) { m_Animator.SetSupportHand(-1); m_Animator.ClearManualWalk(); }
            if (m_Ring != null) m_Ring.gameObject.SetActive(false);
        }

        void UpdateReminders()
        {
            if (m_Voice == null || m_EverSummoned || m_InvitedAt < 0f || m_State != State.Free) return;
            var since = Time.time - m_InvitedAt;
            if (since > m_RemindSummonAfter) m_Voice.Say("A3");
            if (since > m_RemindHandAfter) m_Voice.Say("E2");
        }

        void OnTouched()
        {
            if (m_Voice == null && m_Animator == null) return;
            MoodiVoice.MarkLearned("C1");
            m_TouchedAt = Time.time;
            if (m_State != State.Perched)
            {
                if (m_Voice != null) m_Voice.SayOneOf("C2");
                return;
            }
            m_Pats++;
            if (m_Pats % 3 == 0)
            {
                if (m_Animator != null) m_Animator.PlayHappy();
                if (m_Voice != null) m_Voice.Say("C3");
            }
            else if (m_Voice != null) m_Voice.SayOneOf("C2");
        }

        // ---------- hands ----------

        Hand CurrentHand() => m_SummonRight ? m_Right : m_Left;
        Hand OtherHand() => m_SummonRight ? m_Left : m_Right;

        /// <summary>The free hand's palm is within reach of Moodi's body (about to pat, or patting).</summary>
        bool OtherHandNear()
        {
            var other = OtherHand();
            if (!other.Valid || m_Guide == null) return false;
            var body = m_Animator != null ? m_Animator.transform.TransformPoint(m_Animator.BodyCenterLocal) : m_Guide.position;
            return (other.Palm - body).sqrMagnitude < m_PatReach * m_PatReach;
        }

        Hand PickSummonHand()
        {
            var right = CanSummon(m_Right);
            var left = CanSummon(m_Left);
            if (right && left) return m_SummonRight ? m_Right : m_Left;
            return right ? m_Right : left ? m_Left : default;
        }

        bool CanSummon(Hand hand) =>
            hand.Valid && hand.Extended >= 3 && hand.Velocity.magnitude < m_MaxHandSpeed &&
            IsPalmUp(hand.Facing, m_PalmUpAngle) && IsInSummonZone(hand.Palm, m_Viewer.position, m_Viewer.forward, m_Reach) &&
            !IsPressingBag(hand.Right);

        bool IsPressingBag(bool right)
        {
            if (!m_SearchedPress) { m_SearchedPress = true; m_Press = FindFirstObjectByType<PalmPressGestureController>(); }
            return m_Press != null && (right ? m_Press.RightPalmTouching : m_Press.LeftPalmTouching);
        }

        Vector3 PerchTarget(Hand hand)
        {
            var bodyOffset = Vector3.zero;
            if (m_Animator != null) bodyOffset = m_Animator.transform.TransformPoint(m_Animator.BodyCenterLocal) - m_Guide.position;
            // Keep old serialized 2.2 cm settings clear of the hand as well.
            var lift = Mathf.Max(.075f, m_PerchHeight);
            var bob = Mathf.Sin(Time.unscaledTime * (Mathf.PI * 2f / 3.6f)) * .004f;
            return PerchPoint(hand.Palm, hand.Facing, lift) + Vector3.up * bob - bodyOffset;
        }

        void ReadHead(float dt)
        {
            var head = m_Viewer.position;
            if (m_HadHead)
            {
                var speed = Vector3.ProjectOnPlane(head - m_PrevHead, Vector3.up).magnitude / dt;
                m_HeadSpeed = Mathf.Lerp(m_HeadSpeed, speed, 1f - Mathf.Exp(-6f * dt));
            }
            m_PrevHead = head;
            m_HadHead = true;
        }

        void ReadHands(float dt)
        {
            if (m_Hands == null || !m_Hands.running)
                m_Hands = XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRHandSubsystem>();
            if (m_Hands == null || !m_Hands.running)
            {
                m_Left = m_Right = default;
                m_HadLeft = m_HadRight = false;
                return;
            }
            if (m_XROrigin == null) m_XROrigin = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
            var origin = m_XROrigin != null ? m_XROrigin.Origin.transform : null;
            m_Left = Sample(m_Hands.leftHand, false, origin, dt, ref m_PrevLeft, ref m_HadLeft, m_Left.Velocity);
            m_Right = Sample(m_Hands.rightHand, true, origin, dt, ref m_PrevRight, ref m_HadRight, m_Right.Velocity);
        }

        Hand Sample(XRHand hand, bool right, Transform origin, float dt, ref Vector3 previous, ref bool hadPrevious,
            Vector3 lastVelocity)
        {
            var result = new Hand { Right = right };
            if (!hand.isTracked ||
                !TryJoint(hand, XRHandJointID.Wrist, origin, out var wrist) ||
                !TryJoint(hand, XRHandJointID.IndexMetacarpal, origin, out var indexMeta) ||
                !TryJoint(hand, XRHandJointID.MiddleMetacarpal, origin, out var middleMeta) ||
                !TryJoint(hand, XRHandJointID.LittleMetacarpal, origin, out var littleMeta))
            {
                hadPrevious = false;
                return result;
            }
            var facing = PalmFacing(wrist, indexMeta, middleMeta, littleMeta, right);
            if (facing == Vector3.zero) { hadPrevious = false; return result; }
            if (m_InvertPalmFacing) facing = -facing;

            var palm = TryJoint(hand, XRHandJointID.MiddleProximal, origin, out var middleKnuckle)
                ? Vector3.Lerp(wrist, middleKnuckle, .7f)
                : (wrist + middleMeta) * .5f;

            var open = 0;
            if (IsOpen(hand, XRHandJointID.IndexProximal, XRHandJointID.IndexTip, wrist, origin)) open++;
            if (IsOpen(hand, XRHandJointID.MiddleProximal, XRHandJointID.MiddleTip, wrist, origin)) open++;
            if (IsOpen(hand, XRHandJointID.RingProximal, XRHandJointID.RingTip, wrist, origin)) open++;
            if (IsOpen(hand, XRHandJointID.LittleProximal, XRHandJointID.LittleTip, wrist, origin)) open++;

            var velocity = hadPrevious ? (palm - previous) / dt : Vector3.zero;
            result.Valid = true;
            result.Palm = palm;
            result.Facing = facing;
            result.Extended = open;
            result.Velocity = Vector3.Lerp(lastVelocity, velocity, 1f - Mathf.Exp(-12f * dt));
            previous = palm;
            hadPrevious = true;
            return result;
        }

        static bool IsOpen(XRHand hand, XRHandJointID proximal, XRHandJointID tip, Vector3 wrist, Transform origin) =>
            TryJoint(hand, proximal, origin, out var p) && TryJoint(hand, tip, origin, out var t) && FingerOpen(wrist, p, t);

        static bool TryJoint(XRHand hand, XRHandJointID id, Transform origin, out Vector3 position)
        {
            position = default;
            if (!hand.GetJoint(id).TryGetPose(out var pose)) return false;
            position = PalmPressGestureController.TransformTrackingPose(pose, origin).position;
            return true;
        }

        // ---------- summon ring ----------

        void ShowRing(bool show, Hand hand, float progress)
        {
            if (!show)
            {
                if (m_Ring != null) m_Ring.gameObject.SetActive(false);
                return;
            }
            if (m_Ring == null)
            {
                var go = new GameObject("Moodi Summon Ring");
                go.transform.SetParent(m_Guide.parent, false);
                m_Ring = go.AddComponent<SpriteRenderer>();
                m_Ring.sprite = Resources.Load<Sprite>("MoodiumWalkTrail/walk_floor_ring");
            }
            if (m_Ring.sprite == null) return;
            m_Ring.gameObject.SetActive(true);
            var size = Mathf.Lerp(.05f, .085f, progress);
            m_Ring.transform.localScale = Vector3.one * (size / Mathf.Max(1e-4f, m_Ring.sprite.bounds.size.x));
            var up = Mathf.Abs(Vector3.Dot(hand.Facing, Vector3.up)) > .9f ? Vector3.forward : Vector3.up;
            m_Ring.transform.SetPositionAndRotation(hand.Palm + hand.Facing * .006f, Quaternion.LookRotation(hand.Facing, up));
            m_Ring.color = new Color(m_Tint.r, m_Tint.g, m_Tint.b, Mathf.Lerp(.25f, 1f, progress));
        }
    }
}
