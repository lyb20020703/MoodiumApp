using System.IO;
using Moodium.Flow;
using NUnit.Framework;
using UnityEngine;
using Phase = Moodium.Flow.StickerAlignRules.Phase;

/// <summary>The 「请对准贴纸」 guide: frame in view → step back → found → slide the knob.</summary>
public sealed class StickerAlignGuideTests
{
    [Test]
    public void TheFrameMatchesTheStickerSeenFromCloseThenFromFurtherBack()
    {
        var close = StickerAlignRules.FrameWidth(StickerAlignRules.CloseDistance);
        var back = StickerAlignRules.FrameWidth(StickerAlignRules.BackDistance);
        Assert.That(close, Is.EqualTo(.10f * .6f / .35f).Within(1e-4f));
        Assert.That(back / close, Is.EqualTo(StickerAlignRules.CloseDistance / StickerAlignRules.BackDistance).Within(1e-4f));
        Assert.That(StickerAlignRules.StepBackDistance(0f), Is.EqualTo(StickerAlignRules.CloseDistance));
        Assert.That(StickerAlignRules.StepBackDistance(StickerAlignRules.ShrinkSeconds), Is.EqualTo(StickerAlignRules.BackDistance).Within(1e-5f));
        Assert.That(StickerAlignRules.StepBackDistance(1f), Is.InRange(StickerAlignRules.CloseDistance, StickerAlignRules.BackDistance));
        Assert.That(StickerAlignRules.StickerWidth / StickerAlignRules.StickerHeight, Is.EqualTo(1.25f).Within(1e-4f), "10 × 8 cm oval");
    }

    [Test]
    public void StepBackComesOnceTheHeadIsSteadyOrAfterAWhile()
    {
        Assert.That(StickerAlignRules.ReadyToStepBack(1f, 5f), Is.False, "too soon, even if steady");
        Assert.That(StickerAlignRules.ReadyToStepBack(3f, .5f), Is.False, "still moving the head");
        Assert.That(StickerAlignRules.ReadyToStepBack(3f, 1.3f), Is.True);
        Assert.That(StickerAlignRules.ReadyToStepBack(StickerAlignRules.MaxAlignSeconds, 0f), Is.True);
        Assert.That(StickerAlignRules.Steady(3f, .01f), Is.True);
        Assert.That(StickerAlignRules.Steady(20f, .01f), Is.False);
        Assert.That(StickerAlignRules.Steady(3f, .2f), Is.False);
    }

    [Test]
    public void ThePhasesFollowTheSticker()
    {
        Assert.That(StickerAlignRules.Next(Phase.Hidden, 0f, 0f, false, false), Is.EqualTo(Phase.Align));
        Assert.That(StickerAlignRules.Next(Phase.Align, 1f, 0f, true, false), Is.EqualTo(Phase.Found));
        Assert.That(StickerAlignRules.Next(Phase.StepBack, 1f, 0f, true, false), Is.EqualTo(Phase.Found));
        Assert.That(StickerAlignRules.Next(Phase.StepBack, StickerAlignRules.ShrinkSeconds + StickerAlignRules.RetrySeconds, 0f, false, false), Is.EqualTo(Phase.Align), "start over");
        Assert.That(StickerAlignRules.Next(Phase.Found, .5f, 0f, true, false), Is.EqualTo(Phase.Found));
        Assert.That(StickerAlignRules.Next(Phase.Found, StickerAlignRules.FoundSeconds, 0f, true, false), Is.EqualTo(Phase.Slide));
        Assert.That(StickerAlignRules.Next(Phase.Slide, 5f, 0f, true, false), Is.EqualTo(Phase.Slide));
        Assert.That(StickerAlignRules.Next(Phase.Slide, 5f, 0f, true, true), Is.EqualTo(Phase.Done), "the user took the knob");
        Assert.That(StickerAlignRules.Next(Phase.Slide, 5f, 0f, false, false), Is.EqualTo(Phase.Align), "sticker lost");
        Assert.That(StickerAlignRules.Next(Phase.Done, 5f, 0f, false, false), Is.EqualTo(Phase.Align));
    }

    [Test]
    public void TheHandDragsTheKnobAndRests()
    {
        Assert.That(StickerAlignRules.SlideDemo(0f).progress, Is.Zero);
        Assert.That(StickerAlignRules.SlideDemo(0f).alpha, Is.Zero);
        Assert.That(StickerAlignRules.SlideDemo(1.65f).progress, Is.EqualTo(.85f).Within(1e-4f));
        Assert.That(StickerAlignRules.SlideDemo(1.65f).alpha, Is.EqualTo(1f).Within(1e-4f));
        Assert.That(StickerAlignRules.SlideDemo(3f).alpha, Is.Zero);
        Assert.That(StickerAlignRules.SlideDemo(StickerAlignRules.SlideLoop + 1.65f).progress, Is.EqualTo(.85f).Within(1e-4f), "loops");
    }

    [Test]
    public void ArtIsInResources()
    {
        foreach (var name in new[] { "align_frame", "align_frame_solid", "align_hand" })
            AssertSprite(StickerAlignRules.Folder + name);
        foreach (var en in new[] { false, true })
            foreach (var p in new[] { Phase.Align, Phase.StepBack, Phase.Found })
                AssertSprite(StickerAlignRules.TextSprite(p, en));
        Assert.That(StickerAlignRules.TextSprite(Phase.Slide, false), Is.Null, "the slide demo has no words");
        Assert.That(StickerAlignRules.TextSprite(Phase.Done, false), Is.Null);
        var frame = Resources.Load<Sprite>(StickerAlignRules.Folder + "align_frame");
        Assert.That(frame.bounds.size.x, Is.EqualTo(.17f).Within(.001f), "680 px canvas; the oval itself is 600 px = SpriteOvalWidth");
    }

    static void AssertSprite(string id)
    {
        var sprite = Resources.Load<Sprite>(id);
        Assert.That(sprite, Is.Not.Null, id);
        Assert.That(sprite.pixelsPerUnit, Is.EqualTo(4000f).Within(.01f), id);
    }

    [Test]
    public void TheGuideWalksThroughFrameStepBackFoundAndSlide()
    {
        var guide = StickerAlignGuide.Create(null, false, () => null);
        var sliderGo = new GameObject("slider");
        try
        {
            var eye = new Vector3(0f, 1.5f, 0f);
            var forward = Vector3.forward;
            float now = 0f;
            void Run(float seconds, Transform slider = null, bool touched = false)
            {
                for (var t = 0f; t < seconds; t += .05f) { now += .05f; guide.Step(now, .05f, eye, forward, slider, .28f, .068f, touched); }
            }

            Run(.5f);
            Assert.That(guide.CurrentPhase, Is.EqualTo(Phase.Align));
            Assert.That(guide.Frame.gameObject.activeSelf, Is.True);
            Assert.That(guide.Text.sprite.name, Is.EqualTo("align_text_put_zh"));
            Assert.That(Vector3.Distance(guide.Frame.transform.position, eye + forward * StickerAlignRules.FrameDistance), Is.LessThan(.01f), "centre of the view");
            Assert.That(guide.FrameWorldWidth, Is.EqualTo(StickerAlignRules.FrameWidth(StickerAlignRules.CloseDistance)).Within(.002f));
            Assert.That(guide.Text.transform.position.y, Is.LessThan(guide.Frame.transform.position.y - .04f), "text under the frame");
            Assert.That(guide.GetComponentsInChildren<Collider>(true), Is.Empty, "nothing pressable");

            Run(3f);   // steady head
            Assert.That(guide.CurrentPhase, Is.EqualTo(Phase.Align));
            Assert.That(guide.Text.sprite.name, Is.EqualTo("align_text_put_zh"));
            Run(StickerAlignRules.ShrinkSeconds + .2f);
            Assert.That(guide.FrameWorldWidth, Is.EqualTo(StickerAlignRules.FrameWidth(StickerAlignRules.CloseDistance)).Within(.002f), "frame stays fixed");

            sliderGo.transform.position = new Vector3(0f, 1.2f, .8f);
            Run(.1f, sliderGo.transform);
            Assert.That(guide.CurrentPhase, Is.EqualTo(Phase.Found));
            Assert.That(guide.FrameSolid.gameObject.activeSelf, Is.True, "warm frame");
            Assert.That(guide.Text.sprite.name, Is.EqualTo("align_text_found_zh"));
            Run(StickerAlignRules.FoundSeconds + .2f, sliderGo.transform);
            Assert.That(guide.CurrentPhase, Is.EqualTo(Phase.Slide));
            Run(1.2f, sliderGo.transform);   // demo hand mid-drag
            Assert.That(guide.Frame.gameObject.activeSelf, Is.False);
            Assert.That(guide.Text.gameObject.activeSelf, Is.False, "no words for the slide demo, only the hand");
            Assert.That(guide.Hand.gameObject.activeSelf, Is.False, "hand demo disabled after device black-background feedback");
            Assert.That(Vector3.Distance(guide.Hand.transform.position, sliderGo.transform.position), Is.LessThan(.2f), "on the slider");

            Run(.6f, sliderGo.transform, true);
            Assert.That(guide.CurrentPhase, Is.EqualTo(Phase.Done));
            Assert.That(guide.Hand.gameObject.activeSelf, Is.False);
            Assert.That(guide.Text.gameObject.activeSelf, Is.False);

            Run(.5f);   // sticker lost
            Assert.That(guide.CurrentPhase, Is.EqualTo(Phase.Align));
            Assert.That(guide.Frame.gameObject.activeSelf, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(guide.gameObject);
            Object.DestroyImmediate(sliderGo);
        }
    }

    [Test]
    public void TheMoodCheckPanelHidesTheGuide()
    {
        var guide = StickerAlignGuide.Create(null, true, () => null);
        try
        {
            for (var t = .05f; t < 1f; t += .05f) guide.Step(t, .05f, Vector3.zero, Vector3.forward, null, 0f, 0f, false);
            Assert.That(guide.Frame.gameObject.activeSelf, Is.True);
            Assert.That(guide.Text.sprite.name, Is.EqualTo("align_text_put_en"));
            guide.SetSuspended(true);
            guide.Step(1.1f, .05f, Vector3.zero, Vector3.forward, null, 0f, 0f, false);
            Assert.That(guide.Frame.gameObject.activeSelf, Is.False);
            Assert.That(guide.Text.gameObject.activeSelf, Is.False);
            guide.SetSuspended(false);
            for (var t = 1.2f; t < 2f; t += .05f) guide.Step(t, .05f, Vector3.zero, Vector3.forward, null, 0f, 0f, false);
            Assert.That(guide.CurrentPhase, Is.EqualTo(Phase.Align));
            Assert.That(guide.Frame.gameObject.activeSelf, Is.True);
        }
        finally { Object.DestroyImmediate(guide.gameObject); }
    }

    [Test]
    public void TheSelectionStepOwnsTheGuide()
    {
        var source = File.ReadAllText("Assets/Scripts/Moodium/Flow/StickerWorldSelectionController.cs");
        Assert.That(source, Does.Contain("StickerAlignGuide.Create(Camera.main, m_English, VisibleSlider)"));
        Assert.That(source, Does.Contain("m_Guide?.SetSuspended(suspended)"));
        Assert.That(source, Does.Contain("SensoryBagBranchUI.Dispose(m_Guide.gameObject)"));
        var go = new GameObject("selection");
        try { Assert.That(go.AddComponent<StickerWorldSelectionController>().VisibleSlider(), Is.Null, "no sticker yet"); }
        finally { Object.DestroyImmediate(go); }
    }
}
