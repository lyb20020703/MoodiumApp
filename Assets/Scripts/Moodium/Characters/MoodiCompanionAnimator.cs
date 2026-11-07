using System;
using Moodium.Flow;
using Moodium.Interaction;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

namespace Moodium.Characters
{
    /// <summary>
    /// Drives the rigged Moodi (Moodi_Rigged.fbx) through the MoodiCompanion animator controller.
    ///
    /// Works without any calls from other scripts:
    /// - Idle loops (each instance starts at a random phase, so two Moodis never move in lockstep);
    /// - Wave plays once the first time this Moodi appears;
    /// - Walk plays while the object moves horizontally, and Moodi turns to face where it is going;
    ///   once it stops after walking it turns back towards the viewer;
    /// - Touched plays when a fingertip or palm reaches its body.
    /// Flows can also call PlayHappy / PlayWave / PlayTouched / SetWalking.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MoodiCompanionAnimator : MonoBehaviour
    {
        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int TouchedId = Animator.StringToHash("Touched");
        static readonly int HappyId = Animator.StringToHash("Happy");
        static readonly int WaveId = Animator.StringToHash("Wave");
        const float WalkSpeed = .08f; // must match the Idle→Walk transition in the controller
        static readonly XRHandJointID[] TouchJoints={XRHandJointID.IndexTip,XRHandJointID.MiddleTip,XRHandJointID.Palm};

        [SerializeField] Animator m_Animator;

        [Header("Body (set by the setup tool, local to this object)")]
        [SerializeField] Vector3 m_BodyCenterLocal;
        [SerializeField, Min(0f)] float m_TouchRadiusLocal = .07f;

        [Header("Appear")]
        [Tooltip("Play Wave once, the first time this Moodi becomes visible. Skipped if a flow already asked for another reaction.")]
        [SerializeField] bool m_WaveOnFirstAppear = true;
        [SerializeField] bool m_RandomizeIdlePhase = true;

        [Header("Walk")]
        [Tooltip("Play the hop while this object moves horizontally faster than 0.08 m/s.")]
        [SerializeField] bool m_AutoWalkFromMovement = true;
        [SerializeField, Range(1f, 20f)] float m_SpeedSmoothing = 8f;
        [Tooltip("Turn Moodi to face the direction it is walking.")]
        [SerializeField] bool m_TurnTowardWalkDirection = true;
        [Tooltip("After it has walked and stopped, turn back to face the viewer.")]
        [SerializeField] bool m_FaceViewerWhenStopped = true;
        [SerializeField, Range(30f, 720f)] float m_TurnSpeed = 300f;

        [Header("Touch")]
        [SerializeField] bool m_DetectHandTouch = true;
        [Tooltip("Extra reach beyond the body surface, in metres.")]
        [SerializeField, Range(0f, .05f)] float m_TouchMargin = .01f;
        [SerializeField, Range(.2f, 3f)] float m_TouchCooldown = 1f;

        Vector3 m_LastPosition;
        Vector3 m_Velocity;
        bool m_ManualWalk;
        bool m_ManualWalkValue;
        int m_SupportHand = -1;
        bool m_LeftWasInside, m_RightWasInside;
        bool m_TouchFlourish;
        int m_Flourishes;
        bool m_HasWalked;
        bool m_Greeted;
        bool m_StartPending;
        float m_NextTouchTime;
        bool m_HandWasInside;
        Vector3 m_FaceInModel; // Moodi's face direction, in the animated model's local space
        XRHandSubsystem m_Hands;
        XROrigin m_XROrigin;
        Transform m_FacingViewer;

        /// <summary>Raised whenever Moodi reacts to a touch (hand or code).</summary>
        public event Action Touched;

        public Animator Animator => m_Animator;
        public void ResumeAfterRescue()
        {
            enabled=true;m_StartPending=false;m_Greeted=true;m_LastPosition=transform.position;
            m_Velocity=Vector3.zero;m_HasWalked=false;
        }
        public Vector3 BodyCenterLocal => m_BodyCenterLocal;
        public float CurrentSpeed => m_Velocity.magnitude;

        /// <summary>Keep a walking companion facing this viewer without changing its route or animation.</summary>
        public void SetFacingViewer(Transform viewer) => m_FacingViewer = viewer;

        /// <summary>Signed yaw (degrees) to turn from one horizontal direction towards another,
        /// limited to maxStep and ignoring differences below the dead zone.</summary>
        public static float YawStep(Vector3 from, Vector3 to, float maxStep, float deadZone = 2f)
        {
            from.y = 0f; to.y = 0f;
            if (from.sqrMagnitude < 1e-8f || to.sqrMagnitude < 1e-8f) return 0f;
            var angle = Vector3.SignedAngle(from, to, Vector3.up);
            return Mathf.Abs(angle) < deadZone ? 0f : Mathf.Clamp(angle, -maxStep, maxStep);
        }

        public void Configure(Vector3 bodyCenterLocal, float touchRadiusLocal)
        {
            m_BodyCenterLocal = bodyCenterLocal;
            m_TouchRadiusLocal = Mathf.Max(0f, touchRadiusLocal);
        }

        public void PlayTouched()
        {
            if (Time.time < m_NextTouchTime || m_Animator == null) return;
            m_NextTouchTime = Time.time + m_TouchCooldown;
            m_Greeted = true;
            m_Animator.ResetTrigger(HappyId);
            m_Animator.ResetTrigger(WaveId);
            if (m_TouchFlourish)
            {
                m_Animator.ResetTrigger(TouchedId);
                m_Animator.SetTrigger((m_Flourishes++ & 1) == 0 ? HappyId : WaveId);
            }
            else m_Animator.SetTrigger(TouchedId);
            Touched?.Invoke();
        }

        public void PlayHappy()
        {
            if (m_Animator == null) return;
            m_Greeted = true; // a flow reaction replaces the automatic first-appear wave
            m_Animator.ResetTrigger(TouchedId);
            m_Animator.ResetTrigger(WaveId);
            m_Animator.SetTrigger(HappyId);
        }

        public void PlayWave()
        {
            if (m_Animator == null) return;
            m_Greeted = true;
            m_Animator.ResetTrigger(TouchedId);
            m_Animator.ResetTrigger(HappyId);
            m_Animator.SetTrigger(WaveId);
        }

        /// <summary>Force walking on/off (ignores movement detection until ClearManualWalk).</summary>
        public void SetWalking(bool walking)
        {
            m_ManualWalk = true;
            m_ManualWalkValue = walking;
        }

        public void ClearManualWalk() => m_ManualWalk = false;

        /// <summary>
        /// The hand Moodi is sitting on (0 left, 1 right, −1 none). Its own fingertips are always close to Moodi's
        /// body, so it never counts as a touch; only the other hand pats. Each hand has its own touch edge, so a
        /// resting hand can no longer hold the shared "inside" flag and block every later touch.
        /// </summary>
        public void SetSupportHand(int hand)
        {
            m_SupportHand = hand;
            m_LeftWasInside = m_RightWasInside = false;
        }
        public int SupportHand => m_SupportHand;

        /// <summary>
        /// Selection screens: Moodi stands on a moving model. Keep Idle (no walk from the carousel's motion), widen
        /// the reach a little for the small model, and answer touches with a clear Happy / Wave in turn
        /// (the old ShySmile / Wink alternation).
        /// </summary>
        public void ConfigureSelectionTouch(float margin = .03f)
        {
            SetWalking(false);
            m_TouchMargin = Mathf.Max(m_TouchMargin, margin);
            m_TouchFlourish = true;
        }
        public bool TouchFlourish => m_TouchFlourish;

        /// <summary>Pure: which hands start a touch this frame (rising edge per hand, support hand ignored).</summary>
        public static bool TouchStarts(bool leftInside, bool rightInside, bool leftWas, bool rightWas, int supportHand)
        {
            var l = supportHand != 0 && leftInside && !leftWas;
            var r = supportHand != 1 && rightInside && !rightWas;
            return l || r;
        }

        void Awake()
        {
            if (m_Animator == null) m_Animator = GetComponentInChildren<Animator>(true);
            m_LastPosition = transform.position;
            m_FaceInModel = FindFaceDirection();
        }

        void Start()
        {
            // The sticker selection screen attaches MoodiSelectionMascotController to the first Animator
            // it finds in CandyWorldUI / NatureWorldUI. It drives that Animator with clips made for an older
            // model, which would freeze this rig, so hand the Animator back to the companion controller.
            var legacy = GetComponentInChildren<MoodiSelectionMascotController>(true);
            if (legacy != null)
            {
                legacy.enabled = false;
                if (m_Animator != null) m_Animator.Rebind();
            }
        }

        void OnEnable()
        {
            m_LastPosition = transform.position;
            m_Velocity = Vector3.zero;
            m_StartPending = true;
            m_HandWasInside=false;
        }

        void Update()
        {
            if (m_Animator == null||!m_Animator.isActiveAndEnabled||m_Animator.runtimeAnimatorController==null) return;
            var dt = Mathf.Max(Time.deltaTime, 1e-4f);

            if (m_StartPending && m_Animator.isActiveAndEnabled && m_Animator.runtimeAnimatorController != null)
            {
                m_StartPending = false;
                // Entries/guides are instantiated before their final world placement.
                // The first observed position is a baseline, never a walking impulse.
                m_LastPosition=transform.position;m_Velocity=Vector3.zero;
                if (m_RandomizeIdlePhase) m_Animator.Play("Idle", 0, UnityEngine.Random.value);
                if (m_WaveOnFirstAppear && !m_Greeted) PlayWave();
            }

            var delta = transform.position - m_LastPosition;
            m_LastPosition = transform.position;
            delta.y = 0f;
            m_Velocity = Vector3.Lerp(m_Velocity, delta / dt, 1f - Mathf.Exp(-m_SpeedSmoothing * dt));
            var speed = m_Velocity.magnitude;
            if (m_ManualWalk) m_Animator.SetFloat(SpeedId, m_ManualWalkValue ? .3f : 0f);
            else if (m_AutoWalkFromMovement) m_Animator.SetFloat(SpeedId, speed);

            UpdateFacing(speed, dt);
            if (m_DetectHandTouch) CheckHandTouch();
        }

        void UpdateFacing(float speed, float dt)
        {
            Vector3 target;
            if (m_FacingViewer != null)
                target = m_FacingViewer.position - transform.TransformPoint(m_BodyCenterLocal);
            else if (m_TurnTowardWalkDirection && speed > WalkSpeed)
            {
                target = m_Velocity;
                m_HasWalked = true;
            }
            else if (m_FaceViewerWhenStopped && m_HasWalked && TryGetViewer(out var viewer))
                target = viewer.position - transform.TransformPoint(m_BodyCenterLocal);
            else
                return;

            // Turn the animated model (not this root) around the body centre: other scripts own
            // this root's position (walk guide, breathing float), nobody else moves the model.
            var model = m_Animator.transform;
            var step = YawStep(model.TransformDirection(m_FaceInModel), target, m_TurnSpeed * dt);
            if (step != 0f) model.RotateAround(transform.TransformPoint(m_BodyCenterLocal), Vector3.up, step);
        }

        Vector3 FindFaceDirection()
        {
            if (m_Animator == null) return Vector3.back;
            Transform body = null, upper = null, stem = null, eyeL = null, eyeR = null;
            foreach (var t in m_Animator.GetComponentsInChildren<Transform>(true))
                switch (t.name)
                {
                    case "Body": body = t; break;
                    case "Body_Upper": upper = t; break;
                    case "Sprout_Stem": stem = t; break;
                    case "Eye_L": eyeL = t; break;
                    case "Eye_R": eyeR = t; break;
                }
            if (body == null || upper == null || stem == null || eyeL == null || eyeR == null)
                return m_Animator.transform.InverseTransformDirection(-transform.forward);
            var up = (stem.position - body.position).normalized;
            var face = Vector3.ProjectOnPlane((eyeL.position + eyeR.position) * .5f - upper.position, up);
            return m_Animator.transform.InverseTransformDirection(face.normalized);
        }

        bool TryGetViewer(out Transform viewer)
        {
            if (m_XROrigin == null) m_XROrigin = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
            viewer = m_XROrigin != null && m_XROrigin.Camera != null ? m_XROrigin.Camera.transform
                : Camera.main != null ? Camera.main.transform : null;
            return viewer != null;
        }

        void CheckHandTouch()
        {
            if (DetectHandTouch(out _)) PlayTouched();
        }

        /// <summary>
        /// Hand-touch check for a display that has taken over this Moodi and disabled this component
        /// (MoodiRiseDisplay while Moodi sits on the soft model). Returns true on the frame a hand starts touching
        /// Moodi's body; <paramref name="handOnMoodi"/> is true while any free hand is inside its reach.
        /// It does not play anything: the caller chooses the reaction. <paramref name="extraMargin"/> widens the
        /// reach (metres) for a small Moodi.
        /// </summary>
        public bool PollHandTouch(out bool handOnMoodi, float extraMargin = 0f)
        {
            handOnMoodi = false;
            return m_DetectHandTouch && DetectHandTouch(out handOnMoodi, extraMargin);
        }

        bool DetectHandTouch(out bool handOnMoodi, float extraMargin = 0f)
        {
            handOnMoodi = false;
            if (m_Hands == null)
                m_Hands = XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRHandSubsystem>();
            if (m_Hands == null || !m_Hands.running){m_HandWasInside=m_LeftWasInside=m_RightWasInside=false;return false;}
            m_Hands.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
            if (m_XROrigin == null) m_XROrigin = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
            var origin = m_XROrigin != null ? m_XROrigin.Origin.transform : null;

            var center = transform.TransformPoint(m_BodyCenterLocal);
            var radius = m_TouchRadiusLocal * Mathf.Abs(transform.lossyScale.x) + m_TouchMargin + Mathf.Max(0f, extraMargin);
            var left = IsInside(m_Hands.leftHand, origin, center, radius);
            var right = IsInside(m_Hands.rightHand, origin, center, radius);
            var started = TouchStarts(left, right, m_LeftWasInside, m_RightWasInside, m_SupportHand);
            m_LeftWasInside = left;
            m_RightWasInside = right;
            m_HandWasInside = (left && m_SupportHand != 0) || (right && m_SupportHand != 1);
            handOnMoodi = m_HandWasInside;
            return started;
        }

        static bool IsInside(XRHand hand, Transform origin, Vector3 center, float radius)
        {
            if (!hand.isTracked) return false;
            foreach (var id in TouchJoints)
            {
                if (!hand.GetJoint(id).TryGetPose(out var pose)) continue;
                var world = PalmPressGestureController.TransformTrackingPose(pose, origin).position;
                if ((world - center).sqrMagnitude <= radius * radius) return true;
            }
            return false;
        }
    }
}
