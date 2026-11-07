using System;
using System.Collections;
using Moodium.Flow;
using Moodium.Interaction;
using Unity.PolySpatial;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

namespace Moodium.CandyWorld
{
    /// <summary>
    /// Floating world dock: two liquid-glass buttons stacked in a column, [Candy Shower | Meadow Shower] above
    /// [Exit World], that hover at the user's right hand during the room stage of Candy / Nature world. Replaces the
    /// wrist band (CandyEnergyWristUI), which first-time users did not find and found hard to press.
    ///
    /// Layout of the view: the squeeze model keeps the centre, the bubble-world orb sits on the left
    /// (CandyWorldManager.BubbleHover) and this dock on the right, so the two never cover each other.
    /// Buttons carry their title only: no sub-label, no progress line, no toasts.
    ///
    /// Placement: 40° to the right of the facing direction, 0.48 m out and 0.17 m below the eyes (about 20° down),
    /// turned to face the eyes — inside arm's reach for a right-hand poke, out of the centre of the view and clear of
    /// the right edge of the tabletop "How to play" card (≈ 28°; at 30° the two overlapped and the card's buttons were
    /// pressed through the dock). While that card is open the dock also steps back (dimmed, no input). It follows
    /// lazily: only when the head has turned more than 35° left, or 65° right (past the dock), for 0.4 s, or the user
    /// walked 0.3 m, does it glide (0.6 s) to the new front-right. It never moves while a fingertip is within 12 cm
    /// of it or within 1 s after a press, so a poke target does not slide away.
    ///
    /// Input: each button is a capsule mesh with a BoxCollider + MoodiumSpatialButton, so the shared
    /// MoodiumSpatialInteractionController presses it for gaze + pinch (IndirectPinch), direct pinch and poke
    /// (Touch) alike. The capsule also carries VisionOSHoverEffect, shaped like the glass (not a rectangle).
    ///
    /// Same public API as the wrist UI for the calls CandyWorldManager makes, so the swap is mechanical.
    /// </summary>
    public sealed class WorldActionDock : MonoBehaviour
    {
        public const string Folder = "MoodiumWorldDock/";

        // Layout, metres / degrees. Distance is horizontal; SideDegrees turns the dock to the user's right.
        public const float Distance = .48f, Drop = .17f, SideDegrees = 40f;
        public const float ButtonWidth = .10f, ButtonHeight = .037f;
        /// <summary>Centre-to-centre spacing of the stacked buttons (2.1 cm of air between the glass capsules).</summary>
        public const float ButtonPitch = .058f;
        /// <summary>Pressable margin on every side of a capsule, and the collider depth a poking finger enters.</summary>
        public const float HitPad = .006f, HitDepth = .03f;

        // Lazy follow. The dock already sits 40° right, so looking at it must not count as turning away:
        // a right turn has to go 25° past it before the dock follows.
        public const float RecenterLeftDegrees = 35f, RecenterRightDegrees = SideDegrees + 25f, RecenterDelay = .4f, GlideSeconds = .6f;
        /// <summary>Alpha while the tabletop "How to play" card is open: visible, clearly not the thing to press now.</summary>
        public const float YieldAlpha = .35f;
        public const float WalkRecenterMetres = .30f, HeightRecenterMetres = .15f;
        public const float HandNearMetres = .12f, PressHoldsFollowSeconds = 1f;

        // Input safety.
        public const float InputSettleSeconds = .5f, PressCooldown = .4f, ExitConfirmSeconds = 3f;
        /// <summary>Wait before the Shower button's one "now available" hop, so it does not land on Moodi's thank-you.</summary>
        public const float ReadyPulseDelay = 2.5f;

        [Tooltip("Exit needs a second press within 3 s. Off by default: the user chose one-press exit for the wrist; " +
                 "turn on only if real-device testing shows accidental exits.")]
        [SerializeField] bool m_ConfirmExit;
        [Tooltip("System gaze highlight on the capsule-shaped hit mesh. Turn off if it looks wrong on device.")]
        [SerializeField] bool m_SystemHover = true;

        public enum ShowerLook { Locked, Ready, On }

        public event Action ReleasePressed;
        public event Action ExitPressed;

        /// <summary>Hidden (fades out, no input) while true — set by CandyWorldManager while in a bubble world.</summary>
        bool m_Suppressed;
        public bool Suppressed { get => m_Suppressed; set { m_Suppressed=value; RefreshInput(); } }
        /// <summary>
        /// Dimmed to <see cref="YieldAlpha"/> with no input while true — set by CandyWorldManager while the tabletop
        /// "How to play" card is open, so a hand reaching for that card's buttons cannot press the dock on the way.
        /// </summary>
        bool m_Yielding;
        public bool Yielding { get => m_Yielding; set { if (m_Yielding == value) return; m_Yielding = value; RefreshInput(); } }
        public bool Visible => m_Alpha > .5f;
        /// <summary>Where collected energy can fly to (the Shower button).</summary>
        public Transform ReleaseAnchor => m_Shower != null ? m_Shower.transform : transform;

        Camera m_Camera;
        bool m_English, m_Nature, m_Built;
        bool m_ReleaseAvailable, m_ReleaseOn, m_ExitAvailable, m_RoomInteraction = true;
        bool m_ReadyAnnounced, m_Placed, m_Gliding;
        float m_AvailableAt = -100f, m_Alpha, m_ShownAt = -100f, m_LastPress = -100f, m_OffAxisFor, m_GlideStart, m_GlideEndAt = -100f;
        Vector3 m_GlideFrom, m_GlideTo, m_AnchorEye, m_AnchorForward = Vector3.forward;
        WorldDockButton m_Shower, m_Exit;
        readonly WristExitConfirmation m_Confirm = new(ExitConfirmSeconds);
        XRHandSubsystem m_Hands;
        XROrigin m_Origin;

        // ---------- pure helpers (EditMode tests) ----------

        public static Vector3 FlatForward(Vector3 forward)
        {
            var f = Vector3.ProjectOnPlane(forward, Vector3.up);
            return f.sqrMagnitude < 1e-6f ? Vector3.forward : f.normalized;
        }

        /// <summary>Front-right of the facing direction: SideDegrees to the right, Distance out, Drop below the eyes.</summary>
        public static Vector3 TargetPosition(Vector3 eye, Vector3 headForward) =>
            eye + Quaternion.Euler(0f, SideDegrees, 0f) * FlatForward(headForward) * Distance + Vector3.down * Drop;

        /// <summary>Turned to the eyes. Project convention: the panel's +Z points away from the viewer.</summary>
        public static Quaternion FaceEye(Vector3 position, Vector3 eye)
        {
            var away = position - eye;
            return away.sqrMagnitude < 1e-6f ? Quaternion.identity : Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        /// <summary>Head yaw, measured flat, relative to the facing direction the dock was last placed for. + = turned right.</summary>
        public static float HeadYawFromAnchor(Vector3 headForward, Vector3 anchorForward) =>
            Vector3.SignedAngle(FlatForward(anchorForward), FlatForward(headForward), Vector3.up);

        /// <summary>Turned far enough to follow: 35° to the left, or 55° to the right (the dock's own side).</summary>
        public static bool TurnedAway(float headYaw) => headYaw < -RecenterLeftDegrees || headYaw > RecenterRightDegrees;

        /// <summary>Glide when the head has been turned away long enough, or the user walked / stood up / sat down.</summary>
        public static bool ShouldRecenter(float headYaw, float turnedSeconds, Vector3 eye, Vector3 anchorEye)
        {
            var flat = Vector3.ProjectOnPlane(eye - anchorEye, Vector3.up).magnitude;
            return (TurnedAway(headYaw) && turnedSeconds >= RecenterDelay) ||
                   flat > WalkRecenterMetres || Mathf.Abs(eye.y - anchorEye.y) > HeightRecenterMetres;
        }

        public static bool FollowHeld(float fingertipDistance, float sincePress) =>
            fingertipDistance < HandNearMetres || sincePress < PressHoldsFollowSeconds;

        public static bool CanPress(bool visible, float sinceShown, float sinceGlideEnd, bool gliding, float sinceLastPress) =>
            visible && !gliding && sinceShown >= InputSettleSeconds && sinceGlideEnd >= InputSettleSeconds && sinceLastPress >= PressCooldown;

        public static ShowerLook ShowerState(bool available, bool on) => !available ? ShowerLook.Locked : on ? ShowerLook.On : ShowerLook.Ready;

        public static string ShowerSprite(ShowerLook look, bool nature, bool english, string state) =>
            look == ShowerLook.Locked ? $"{Folder}dock_shower_locked_{(nature ? "nature" : "candy")}_{Lang(english)}"
            : $"{Folder}dock_{(look == ShowerLook.On ? "stop" : "shower")}_{(nature ? "nature" : "candy")}_{Lang(english)}_{state}";

        public static string ExitSprite(bool confirming, bool english, string state) =>
            $"{Folder}dock_exit{(confirming ? "_confirm" : "")}_{Lang(english)}_{state}";

        /// <summary>Button centres along the dock's local Y: Shower on top, Exit below (further from the eye line).</summary>
        public static float ButtonY(bool exit) => (exit ? -1f : 1f) * ButtonPitch / 2f;

        static string Lang(bool english) => english ? "en" : "zh";

        // ---------- setup (same calls as the wrist UI) ----------

        public void Build(Camera camera, bool english)
        {
            m_Camera = camera; m_English = english;
            if (m_Built) { RefreshAll(); return; }
            m_Built = true;
            Moodium.UI.UIRefreshVisuals.UnitScale(transform);   // 1 local unit = 1 m for every offset below
            if (GetComponent<VisionOSSortingGroup>() == null) gameObject.AddComponent<VisionOSSortingGroup>();
            m_Shower = WorldDockButton.Create(transform, "Shower", new Vector3(0, ButtonY(false), 0), m_SystemHover, PressShower);
            m_Exit = WorldDockButton.Create(transform, "Exit World", new Vector3(0, ButtonY(true), 0), m_SystemHover, PressExit);
            SetAlpha(0f);
            RefreshAll();
        }

        public void SetLanguage(bool english) { m_English = english; RefreshAll(); }

        public void SetWorldTheme(bool natureWorld)
        {
            m_Nature = natureWorld; m_ReadyAnnounced = false; m_Confirm.Reset();
            RefreshAll();
        }

        public void SetReleaseAvailable(bool available)
        {
            if (available && !m_ReleaseAvailable) m_AvailableAt = Time.time;
            m_ReleaseAvailable = available;
            if (!available) m_ReleaseOn = false;
            RefreshAll();
        }

        public void SetReleaseToggleState(bool on) { m_ReleaseOn = on; RefreshAll(); }

        public void SetExitAvailable(bool available) { m_ExitAvailable = available; if (!available) m_Confirm.Reset(); RefreshAll(); RefreshInput(); }

        public void SetRoomInteractionEnabled(bool enabled) { m_RoomInteraction = enabled; RefreshInput(); }

        // ---------- per frame ----------

        System.Func<Bounds?> m_TableBag;
        Vector3 m_TableRight;
        public void UseTableBesideBag(System.Func<Bounds?> bag) { m_TableBag = bag; m_TableRight = Vector3.zero; }

        void LateUpdate()
        {
            if(m_Built&&m_Camera!=null)Tick(Time.time,Time.deltaTime,NearestFingertip());
        }

        // Clock and fingertip distance are explicit so the real follow state machine can be tested deterministically.
        void Tick(float now,float dt,float fingertipDistance)
        {
            if (!m_Built || m_Camera == null) return;
            var eye = m_Camera.transform.position;
            var forward = m_Camera.transform.forward;
            var want = !Suppressed && m_RoomInteraction && m_ExitAvailable;
            if (want && !m_Placed) { Snap(eye, forward,now); }
            if (want && m_Alpha <= 0f) m_ShownAt = now;
            var full = m_Yielding ? YieldAlpha : 1f;
            SetAlpha(Mathf.MoveTowards(m_Alpha, want ? full : 0f, dt / (want ? .25f : .18f)));
            if (!want) { if (m_Alpha <= 0f) m_Placed = false; return; }
            if (m_Confirm.IsArmed(now) == false && m_Exit != null && m_Exit.Confirming) RefreshAll(now);
            // Energy full: the Shower button hops once per world visit (no toast), once the dock is shown and settled.
            if (m_ReleaseAvailable && !m_ReadyAnnounced && !m_Yielding && m_Alpha >= 1f && now - m_ShownAt >= InputSettleSeconds &&
                now - m_AvailableAt >= ReadyPulseDelay)
            {
                m_ReadyAnnounced = true; m_Shower.Pulse();
                Debug.Log("[World Dock] energy full, shower ready");
            }

            if (m_TableBag != null)
            {
                var bag = m_TableBag();
                if (bag.HasValue)
                {
                    if (m_TableRight.sqrMagnitude < .01f)
                    {
                        var toward = Vector3.ProjectOnPlane(bag.Value.center - eye, Vector3.up).normalized;
                        m_TableRight = Vector3.Cross(Vector3.up, toward.sqrMagnitude > .01f ? toward : FlatForward(forward));
                    }
                    // Fixed tabletop dock beside the bag; no head-follow translation.
                    transform.position = bag.Value.center + m_TableRight * (Mathf.Max(bag.Value.extents.x, bag.Value.extents.z) + .18f)
                        + Vector3.up * .07f;
                    transform.rotation = FaceEye(transform.position, eye);
                    m_Gliding = false;
                }
                return;
            }

            var held = FollowHeld(fingertipDistance, now - m_LastPress);
            if(held)
            {
                // Stop even a glide already in flight: the target must not move away from a reaching finger.
                if(m_Gliding){m_Gliding=false;m_GlideEndAt=now;}
                m_OffAxisFor=0;return;
            }
            if (m_Gliding)
            {
                var t = Mathf.Clamp01((now - m_GlideStart) / GlideSeconds);
                transform.position = Vector3.Lerp(m_GlideFrom, m_GlideTo, t * t * (3f - 2f * t));
                if (t >= 1f) { m_Gliding = false; m_GlideEndAt = now; }
            }
            else
            {
                var yaw = HeadYawFromAnchor(forward, m_AnchorForward);
                m_OffAxisFor = TurnedAway(yaw) ? m_OffAxisFor + dt : 0f;
                if (!held && ShouldRecenter(yaw, m_OffAxisFor, eye, m_AnchorEye))
                {
                    m_Gliding = true; m_GlideStart = now; m_OffAxisFor = 0f; m_Confirm.Reset();
                    m_GlideFrom = transform.position; m_GlideTo = TargetPosition(eye, forward);
                    m_AnchorEye = eye; m_AnchorForward = FlatForward(forward);
                    Debug.Log($"[World Dock] glide to front-right (head {yaw:+0;-0}°)");
                }
            }
            transform.rotation = FaceEye(transform.position, eye);
        }

        void Snap(Vector3 eye, Vector3 forward,float now)
        {
            transform.position = TargetPosition(eye, forward);
            transform.rotation = FaceEye(transform.position, eye);
            m_AnchorEye = eye; m_AnchorForward = FlatForward(forward); m_Placed = true; m_Gliding = false; m_GlideEndAt = now;
        }

        // ---------- presses ----------

        bool Pressable(float now) => isActiveAndEnabled&&!Suppressed&&!m_Yielding&&m_RoomInteraction&&m_ExitAvailable&&CanPress(Visible, now - m_ShownAt, now - m_GlideEndAt, m_Gliding, now - m_LastPress);

        void PressShower()=>PressShowerAt(Time.time);
        void PressShowerAt(float now)
        {
            if (!Pressable(now) || !m_RoomInteraction) return;
            m_LastPress = now;
            if (!m_ReleaseAvailable)
            {
                // Locked: a small shake so the press is not dead; no text.
                m_Shower.Shake();
                Debug.Log("[World Dock] shower pressed while locked");
                return;
            }
            m_Shower.Flash();
            Debug.Log("[World Dock] shower " + (m_ReleaseOn ? "stop" : "start"));
            ReleasePressed?.Invoke();
        }

        void PressExit()=>PressExitAt(Time.time);
        void PressExitAt(float now)
        {
            if (!Pressable(now) || !m_ExitAvailable) return;
            m_LastPress = now;
            m_Exit.Flash();
            if (m_ConfirmExit && !m_Confirm.Press(now)) { RefreshAll(now); Debug.Log("[World Dock] exit armed"); return; }
            Debug.Log("[World Dock] exit");
            ExitPressed?.Invoke();
        }

        // ---------- visuals ----------

        void RefreshAll(float? at=null)
        {
            if (!m_Built) return;
            var look = ShowerState(m_ReleaseAvailable, m_ReleaseOn);
            m_Shower.SetSprites(state => ShowerSprite(look, m_Nature, m_English, state), look == ShowerLook.Locked);
            var confirming = m_ConfirmExit && m_Confirm.IsArmed(at??Time.time);
            m_Exit.Confirming = confirming;
            m_Exit.SetSprites(state => ExitSprite(confirming, m_English, state), false);
        }

        void SetAlpha(float a)
        {
            m_Alpha = a;
            m_Shower?.SetAlpha(a); m_Exit?.SetAlpha(a);
            RefreshInput();
        }

        void RefreshInput()
        {
            bool interactive=isActiveAndEnabled&&!Suppressed&&!m_Yielding&&m_RoomInteraction&&m_ExitAvailable&&m_Alpha>.9f;
            m_Shower?.SetInteractive(interactive);m_Exit?.SetInteractive(interactive);
        }
        void OnDisable()
        {
            m_Placed=m_Gliding=false;m_OffAxisFor=0;m_Confirm.Reset();
            SetAlpha(0);
        }

        float NearestFingertip()
        {
            if (m_Hands == null || !m_Hands.running) m_Hands = XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRHandSubsystem>();
            if (m_Hands == null || !m_Hands.running) return float.PositiveInfinity;
            if (m_Origin == null) m_Origin = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
            var origin = m_Origin != null ? m_Origin.Origin.transform : null;
            var best = float.PositiveInfinity;
            foreach (var hand in new[] { m_Hands.leftHand, m_Hands.rightHand })
            {
                if (!hand.isTracked || !hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var tip)) continue;
                var world = PalmPressGestureController.TransformTrackingPose(tip, origin).position;
                best = Mathf.Min(best, m_Shower.DistanceTo(world),m_Exit.DistanceTo(world));
            }
            return best;
        }
    }
}
