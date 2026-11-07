using System;
using Moodium.Flow;
using Unity.PolySpatial;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;
namespace Moodium.Squeeze
{
    /// <summary>
    /// The "?" (How to play) button at the right end of the mode row. Same glass puck as the mode buttons, one size
    /// smaller and half a step further out so it reads as a different kind of button. It stays on the table after the
    /// mode buttons fade out at 100%.
    /// States: idle · first-visit pulse (two soft rings) · open (selected glow) · new content (accent dot).
    /// </summary>
    public sealed class HelpPuck
    {
        /// <summary>Screen-right is local −X (the row's +Z points at the viewer); the breath button sits at −0.085.</summary>
        public const float OffsetX = -.17f;
        public const float Size = .06f;
        /// <summary>The docked help card stands this far behind the button.</summary>
        public const float CardGap = .10f;
        public const float PulseSeconds = 2.4f;
        public const int PulseCount = 2;
        /// <summary>Buttons fade in over the first 1.4 s; a pulse requested earlier waits for that.</summary>
        public const float ReadyAfter = 1.4f;

        readonly TableVisuals v;
        readonly Transform node;
        readonly SpriteRenderer puck, glow, ring, icon, label, dot;
        readonly BoxCollider collider;
        readonly float born;
        float pressedAt = -100;
        bool touching;
        XROrigin xrOrigin;

        public event Action Pressed;
        public Transform Node => node;
        public float ReadyAt => born + ReadyAfter;

        public static Vector3 LocalPosition(Vector2 halfSize) => new(OffsetX, 0, -halfSize.y - .13f);
        public static Vector3 CardDockPosition(Vector2 halfSize) => LocalPosition(halfSize) + new Vector3(0, 0, -CardGap);

        /// <summary>Ring progress 0..1 of the current pulse, or −1 outside the pulse window.</summary>
        public static float PulsePhase(float secondsSinceStart)
        {
            if (secondsSinceStart < 0 || secondsSinceStart >= PulseSeconds) return -1;
            var each = PulseSeconds / PulseCount;
            return secondsSinceStart % each / each;
        }

        public HelpPuck(TableVisuals visuals, Transform parent)
        {
            v = visuals; born = Time.unscaledTime;
            node = v.Node(parent, "How to play (?)", LocalPosition(v.HalfSize)).transform;
            ring = v.Sprite(node, "First-visit Ring", "mode_btn_press_ripple", new Vector3(0, .0015f, 0), Size * 1.6f, 15, v.Soft);
            glow = v.Sprite(node, "Open Glow", "mode_btn_selected_glow", new Vector3(0, .0017f, 0), Size, 17, v.Accent);
            puck = v.Sprite(node, "Glass Puck", "mode_btn_base", new Vector3(0, .002f, 0), Size, 20);
            icon = v.Sprite(node, "Icon", "mode_icon_help", new Vector3(0, .0026f, 0), .024f, 26);
            label = v.Sprite(node, "Label", "mode_label_help_" + v.Language, new Vector3(0, .002f, .045f), .06f, 20);
            dot = v.Sprite(node, "New Dot", "rhythm_combo_bead_full", new Vector3(-.021f, .003f, -.019f), .011f, 27, v.Accent);
            collider = node.gameObject.AddComponent<BoxCollider>(); collider.size = new Vector3(.055f, .02f, .055f);
            node.gameObject.AddComponent<MoodiumSpatialButton>().Configure(null, null, Press);
            node.gameObject.AddComponent<VisionOSHoverEffect>().Type = VisionOSHoverEffect.EffectType.Highlight;
            v.Register(node.gameObject, 30);
            ring.gameObject.SetActive(false);
        }

        public void Press()
        {
            if (Time.unscaledTime - born < 1 || Time.unscaledTime - pressedAt < .35f) return;
            pressedAt = Time.unscaledTime;
            Pressed?.Invoke();
        }

        /// <param name="open">The help card is showing.</param>
        /// <param name="newContent">Show the accent dot (explore page unlocked, not opened yet).</param>
        /// <param name="pulseStartedAt">unscaledTime of the last pulse request (−100 = none).</param>
        public void Tick(bool open, bool newContent, float pulseStartedAt)
        {
            var now = Time.unscaledTime; var age = now - born;
            var alpha = Mathf.SmoothStep(0, 1, (age - 1) / .4f);   // same fade-in as the mode buttons; never fades out
            var phase = pulseStartedAt < 0 ? -1 : PulsePhase(now - Mathf.Max(pulseStartedAt, ReadyAt));
            var pulsing = phase >= 0;
            var pressAge = now - pressedAt;

            var pos = node.localPosition;
            pos.y = (open ? .003f : 0) - Mathf.Max(0, 1 - pressAge / .12f) * .003f - (1 - Mathf.Clamp01((age - 1) / .4f)) * .01f;
            node.localPosition = pos;
            node.localScale = Vector3.one * (open ? 1.04f : 1);

            puck.color = new Color(1, 1, 1, alpha);
            glow.color = WithAlpha(v.Accent, open ? alpha : 0);
            icon.color = WithAlpha(open ? v.Accent : Color.white, alpha * (open ? 1 : pulsing ? .95f : newContent ? .8f : .55f));
            label.color = WithAlpha(open ? v.Accent : new Color(.85f, .83f, .89f), alpha * (open || pulsing ? 1 : .6f));
            dot.color = WithAlpha(v.Accent, newContent && !open ? alpha : 0);
            ring.gameObject.SetActive(pulsing);
            if (pulsing && ring.sprite != null)
            {
                ring.color = WithAlpha(v.Accent, (1 - phase) * .85f * alpha);
                ring.transform.localScale = Vector3.one * (Size * 1.6f / ring.sprite.bounds.size.x) * Mathf.Lerp(.75f, 1.45f, phase);
            }

            collider.enabled = alpha > .5f;
            var subsystem = XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRHandSubsystem>();
            var touch = collider.enabled && subsystem != null && (Touches(subsystem.leftHand) || Touches(subsystem.rightHand));
            if (touch && !touching) Press();
            touching = touch;
        }

        bool Touches(XRHand hand)
        {
            if (!hand.isTracked || !hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var tip)) return false;
            if (xrOrigin == null) xrOrigin = UnityEngine.Object.FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
            return ModeButtonBar.TouchesPoint(tip.position, collider, xrOrigin != null ? xrOrigin.Origin.transform : null);
        }

        static Color WithAlpha(Color c, float a) { c.a = a; return c; }
    }
}
