using System;
using Moodium.UI;
using UnityEngine;

namespace Moodium.Flow
{
    /// <summary>
    /// Rules for the sticker-alignment guide on the 「请对准贴纸」 step. The sticker is a 10 × 8 cm oval. What worked best in
    /// testing: look at it so it sits exactly in a frame, then step back a little until it is recognised, then slide the
    /// knob of the world entry. Pure helpers (EditMode tests). Seconds, metres, degrees.
    /// </summary>
    public static class StickerAlignRules
    {
        public const string Folder = "MoodiumStickerGuide/";
        public const float StickerWidth = .10f, StickerHeight = .08f;
        /// <summary>The frame floats this far in front of the eyes, at the centre of the view.</summary>
        public const float FrameDistance = .6f;
        /// <summary>The frame first matches the sticker seen from this close, then shrinks to how it looks from this far.</summary>
        public const float CloseDistance = .35f, BackDistance = .7f;
        /// <summary>The oval in align_frame / align_frame_solid is 600 px wide at 1 px = 0.25 mm.</summary>
        public const float SpriteOvalWidth = .15f;
        /// <summary>Lazy follow: how fast the frame catches up with the head (1/s).</summary>
        public const float FollowRate = 7f;
        public const float SteadyDegreesPerSecond = 8f, SteadyMetresPerSecond = .05f;
        /// <summary>Align: at least this long; step back once the head has been steady this long, or after the max anyway.</summary>
        public const float MinAlignSeconds = 2.5f, SteadySeconds = 1.2f, MaxAlignSeconds = 7f;
        /// <summary>Step back: the frame shrinks over this long, then waits; with no sticker found, it starts over.</summary>
        public const float ShrinkSeconds = 2.5f, RetrySeconds = 10f;
        public const float FadeSeconds = .35f, FoundSeconds = 1f, SlideLoop = 3.2f;
        public const float TextScale = .8f, TextGap = .02f, HandScale = .6f;
        /// <summary>The slide hint ends once the knob has moved this far (or is being dragged).</summary>
        public const float SliderTouched = .02f;
        /// <summary>Fingertip offset from the hand sprite's centre, metres at scale 1 (same art as the wish-chime hint).</summary>
        public static readonly Vector2 HandTip = new(-.0178f, .0244f);

        public enum Phase { Hidden, Align, StepBack, Found, Slide, Done }

        /// <summary>Width of a frame at <see cref="FrameDistance"/> that covers the sticker seen from <paramref name="stickerDistance"/>.</summary>
        public static float FrameWidth(float stickerDistance) => StickerWidth * FrameDistance / Mathf.Max(.05f, stickerDistance);

        /// <summary>Sticker distance the frame shows <paramref name="sinceStepBack"/> seconds into the step-back phase.</summary>
        public static float StepBackDistance(float sinceStepBack)
        {
            var t = Mathf.Clamp01(sinceStepBack / ShrinkSeconds);
            return Mathf.Lerp(CloseDistance, BackDistance, t * t * (3f - 2f * t));
        }

        public static bool Steady(float degreesPerSecond, float metresPerSecond) =>
            degreesPerSecond < SteadyDegreesPerSecond && metresPerSecond < SteadyMetresPerSecond;

        public static bool ReadyToStepBack(float sinceAlign, float steadyFor) =>
            sinceAlign >= MaxAlignSeconds || (sinceAlign >= MinAlignSeconds && steadyFor >= SteadySeconds);

        public static string TextSprite(Phase phase, bool english)
        {
            var key = phase switch
            {
                Phase.Align => "put",
                Phase.StepBack => "back",
                Phase.Found => "found",
                _ => null,   // the slide demo has no words: the hand shows it
            };
            return key == null ? null : Folder + "align_text_" + key + "_" + (english ? "en" : "zh");
        }

        /// <summary>
        /// Next phase. <paramref name="found"/>: a world entry is visible; <paramref name="sliderTouched"/>: its knob moved or is held.
        /// </summary>
        public static Phase Next(Phase phase, float sincePhase, float steadyFor, bool found, bool sliderTouched)
        {
            switch (phase)
            {
                case Phase.Hidden:
                    return found ? Phase.Slide : Phase.Align;
                case Phase.Align:
                    if (found) return Phase.Found;
                    return Phase.Align; // Keep the frame fixed until recognition; no step-back prompt.
                case Phase.StepBack:
                    if (found) return Phase.Found;
                    return sincePhase >= ShrinkSeconds + RetrySeconds ? Phase.Align : Phase.StepBack;
                case Phase.Found:
                    if (!found) return Phase.Align;
                    return sincePhase >= FoundSeconds ? Phase.Slide : Phase.Found;
                case Phase.Slide:
                    if (!found) return Phase.Align;
                    return sliderTouched ? Phase.Done : Phase.Slide;
                case Phase.Done:
                    return found ? Phase.Done : Phase.Align;
            }
            return phase;
        }

        /// <summary>Knob progress (0..1) the demo hand is at, and its opacity, <paramref name="t"/> seconds into the loop.</summary>
        public static (float progress, float alpha) SlideDemo(float t)
        {
            t = Mathf.Repeat(t, SlideLoop);
            if (t < .35f) return (0f, t / .35f);
            if (t < 1.65f) { var k = (t - .35f) / 1.3f; return (.85f * k * k * (3f - 2f * k), 1f); }
            if (t < 2.15f) return (.85f, 1f - (t - 1.65f) / .5f);
            return (0f, 0f);
        }
    }

    /// <summary>
    /// The guide on the sticker step: a dashed 10 : 8 oval floating at the centre of the view (lazily following the head)
    /// with 「把贴纸放进框里」; once the head is steady it shrinks with 「慢慢往后退一两步」; when the sticker is recognised it turns
    /// warm, says 「找到啦」 and fades; then a translucent hand drags the knob of the world entry's slider (no words), until
    /// the user touches the knob. Nothing here is pressable. Created by
    /// StickerWorldSelectionController.
    /// </summary>
    public sealed class StickerAlignGuide : MonoBehaviour
    {
        static readonly Color Warm = new(1f, .85f, .55f, 1f);

        Camera m_Viewer;
        Func<GlassSlideToEnter> m_Slider;
        bool m_English, m_Suspended;
        StickerAlignRules.Phase m_Phase = StickerAlignRules.Phase.Hidden;
        float m_PhaseAt = -1f, m_SteadyFor, m_FrameAlpha, m_TextAlpha, m_HandAlpha, m_FrameWidth;
        Vector3 m_FramePosition, m_LastEye, m_LastForward;
        bool m_HasPose;
        SpriteRenderer m_Frame, m_FrameSolid, m_Text, m_Hand;

        public StickerAlignRules.Phase CurrentPhase => m_Phase;
        public SpriteRenderer Frame => m_Frame;
        public SpriteRenderer FrameSolid => m_FrameSolid;
        public SpriteRenderer Text => m_Text;
        public SpriteRenderer Hand => m_Hand;
        public float FrameWorldWidth => m_FrameWidth;

        public static StickerAlignGuide Create(Camera viewer, bool english, Func<GlassSlideToEnter> visibleSlider)
        {
            var go = new GameObject("Sticker Align Guide");
            var guide = go.AddComponent<StickerAlignGuide>();
            guide.m_Viewer = viewer; guide.m_English = english; guide.m_Slider = visibleSlider;
            guide.m_Frame = guide.Sprite("Frame", StickerAlignRules.Folder + "align_frame");
            guide.m_FrameSolid = guide.Sprite("Frame Found", StickerAlignRules.Folder + "align_frame_solid");
            guide.m_Text = guide.Sprite("Text", null);
            guide.m_Hand = guide.Sprite("Slide Hand", StickerAlignRules.Folder + "align_hand");
            guide.m_FrameWidth = StickerAlignRules.FrameWidth(StickerAlignRules.CloseDistance);
            return guide;
        }

        SpriteRenderer Sprite(string name, string id)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var r = go.AddComponent<SpriteRenderer>();
            if (id != null) r.sprite = UIRefreshVisuals.Load(id);
            r.color = new Color(1f, 1f, 1f, 0f);
            go.SetActive(false);
            return r;
        }

        /// <summary>The mood check panel is open: hide everything; resume from the alignment step.</summary>
        public void SetSuspended(bool suspended)
        {
            m_Suspended = suspended;
            if (suspended) { m_Phase = StickerAlignRules.Phase.Hidden; m_FrameAlpha = m_TextAlpha = m_HandAlpha = 0f; Show(m_Frame, 0f); Show(m_FrameSolid, 0f); Show(m_Text, 0f); Show(m_Hand, 0f); }
        }

        void LateUpdate()
        {
            if (m_Viewer == null) m_Viewer = Camera.main;
            if (m_Viewer == null) return;
            var slider = m_Slider?.Invoke();
            Step(Time.time, Time.deltaTime, m_Viewer.transform.position, m_Viewer.transform.forward, slider != null ? slider.transform : null,
                slider != null ? slider.Width : 0f, slider != null ? slider.Height : 0f, slider != null && (slider.Progress > StickerAlignRules.SliderTouched || slider.IsDragging));
        }

        /// <summary>One frame. Public so EditMode tests can drive it without a camera or tracked sticker.</summary>
        public void Step(float now, float dt, Vector3 eye, Vector3 forward, Transform slider, float sliderWidth, float sliderHeight, bool sliderTouched)
        {
            if (m_Suspended) return;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            forward.Normalize();

            // Head steadiness (the user is holding the sticker in the frame).
            if (m_HasPose && dt > 0f)
            {
                var degrees = Vector3.Angle(m_LastForward, forward) / dt;
                var metres = Vector3.Distance(m_LastEye, eye) / dt;
                m_SteadyFor = StickerAlignRules.Steady(degrees, metres) ? m_SteadyFor + dt : 0f;
            }
            m_LastEye = eye; m_LastForward = forward;

            var found = slider != null;
            if (m_PhaseAt < 0f) m_PhaseAt = now;
            var next = StickerAlignRules.Next(m_Phase, now - m_PhaseAt, m_SteadyFor, found, sliderTouched);
            if (next != m_Phase)
            {
                if (next == StickerAlignRules.Phase.Align && m_Phase != StickerAlignRules.Phase.Hidden && m_Phase != StickerAlignRules.Phase.StepBack)
                    Debug.Log("[Sticker Guide] sticker lost: back to the frame");
                if (next == StickerAlignRules.Phase.Found) Debug.Log("[Sticker Guide] sticker found");
                m_Phase = next; m_PhaseAt = now; m_SteadyFor = 0f;
                var textId = StickerAlignRules.TextSprite(next, m_English);
                if (textId != null) m_Text.sprite = UIRefreshVisuals.Load(textId);   // otherwise the last words just fade out
                if (!m_HasPose) m_FramePosition = eye + forward * StickerAlignRules.FrameDistance;
            }
            m_HasPose = true;
            var since = now - m_PhaseAt;

            // Frame: lazy-follows the centre of the view.
            var target = eye + forward * StickerAlignRules.FrameDistance;
            m_FramePosition = Vector3.Lerp(m_FramePosition, target, 1f - Mathf.Exp(-StickerAlignRules.FollowRate * dt));
            var look = m_FramePosition - eye;
            var facing = look.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(look, Vector3.up) : Quaternion.identity;
            var aligning = m_Phase == StickerAlignRules.Phase.Align || m_Phase == StickerAlignRules.Phase.StepBack;
            var width = m_Phase == StickerAlignRules.Phase.StepBack
                ? StickerAlignRules.FrameWidth(StickerAlignRules.StepBackDistance(since))
                : StickerAlignRules.FrameWidth(StickerAlignRules.CloseDistance);
            if (m_Phase == StickerAlignRules.Phase.Align) width = Mathf.Lerp(m_FrameWidth, width, 1f - Mathf.Exp(-6f * dt));   // grows back gently after a retry
            if (m_Phase == StickerAlignRules.Phase.Found) width = m_FrameWidth;
            m_FrameWidth = width;
            var fade = dt / StickerAlignRules.FadeSeconds;
            m_FrameAlpha = Mathf.MoveTowards(m_FrameAlpha, aligning ? 1f : 0f, fade);
            var foundK = m_Phase == StickerAlignRules.Phase.Found ? Mathf.Clamp01(since / StickerAlignRules.FoundSeconds) : 1f;
            var solidAlpha = m_Phase == StickerAlignRules.Phase.Found ? 1f - foundK * foundK : 0f;
            var frameScale = width / StickerAlignRules.SpriteOvalWidth;
            PlaceFrame(m_Frame, m_FramePosition, facing, frameScale, Color.white, m_FrameAlpha * .9f);
            PlaceFrame(m_FrameSolid, m_FramePosition, facing, frameScale * (1f + .12f * foundK), Warm, solidAlpha);

            // Text: under the frame while aligning or found. The slide demo has none.
            var textAlpha = m_Phase == StickerAlignRules.Phase.Found ? 1f - foundK * foundK * foundK : aligning ? 1f : 0f;
            m_TextAlpha = Mathf.MoveTowards(m_TextAlpha, textAlpha, fade);
            var t = m_Text.transform;
            t.localScale = Vector3.one * StickerAlignRules.TextScale;
            var textHeight = m_Text.sprite != null ? m_Text.sprite.bounds.size.y * StickerAlignRules.TextScale : .03f;
            var textCentre = m_FramePosition - facing * Vector3.up * (width * StickerAlignRules.StickerHeight / StickerAlignRules.StickerWidth * .5f + StickerAlignRules.TextGap + textHeight * .5f);
            var textLook = textCentre - eye;
            t.SetPositionAndRotation(textCentre, textLook.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(textLook, Vector3.up) : facing);
            Show(m_Text, m_TextAlpha);

            // Slide demo: a translucent hand drags from the knob towards the end of the slider, over and over.
            var demo = StickerAlignRules.SlideDemo(since);
            // Disabled after device feedback: the sprite displayed a black rectangular background.
            m_HandAlpha = 0f;
            if (slider != null && m_Phase == StickerAlignRules.Phase.Slide)
            {
                var knob = slider.TransformPoint(new Vector3(GlassSlideToEnter.KnobCenterX(demo.progress, sliderWidth, sliderHeight), 0f, -.02f));
                var h = m_Hand.transform;
                h.localScale = Vector3.one * StickerAlignRules.HandScale;
                var handLook = knob - eye;
                var handRotation = handLook.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(handLook, Vector3.up) : facing;
                h.SetPositionAndRotation(knob - handRotation * (Vector3)(StickerAlignRules.HandTip * StickerAlignRules.HandScale), handRotation);
            }
            Show(m_Hand, m_HandAlpha);
        }

        static void PlaceFrame(SpriteRenderer frame, Vector3 position, Quaternion rotation, float scale, Color tint, float alpha)
        {
            frame.transform.SetPositionAndRotation(position, rotation);
            frame.transform.localScale = Vector3.one * scale;
            tint.a = alpha;
            frame.color = tint;
            Show(frame, alpha);
        }

        /// <summary>Nearly transparent quads still draw on visionOS: switch them off instead.</summary>
        static void Show(SpriteRenderer sprite, float alpha)
        {
            if (sprite == null) return;
            var c = sprite.color; c.a = Mathf.Clamp01(alpha); sprite.color = c;
            var on = alpha > .01f && sprite.sprite != null;
            if (sprite.gameObject.activeSelf != on) sprite.gameObject.SetActive(on);
        }
    }
}
