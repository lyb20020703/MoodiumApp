using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;

public sealed class WishChimePolishTests
{
    static readonly Vector3 Eye = new Vector3(0f, 1.5f, 0f);

    // ---------- A. Buttons ----------

    [Test]
    public void ButtonSpritePathsFollowLanguageAndWorld()
    {
        Assert.That(WishActionButton.Prefix(false, true, false), Is.EqualTo("MoodiumBubblePlay/WishButtons/wish_btn_rewrite_zh"));
        Assert.That(WishActionButton.Prefix(false, false, true), Is.EqualTo("MoodiumBubblePlay/WishButtons/wish_btn_rewrite_en"));
        Assert.That(WishActionButton.Prefix(true, true, false), Is.EqualTo("MoodiumBubblePlay/WishButtons/wish_btn_release_candy_zh"));
        Assert.That(WishActionButton.Prefix(true, false, true), Is.EqualTo("MoodiumBubblePlay/WishButtons/wish_btn_release_nature_en"));
    }

    [Test]
    public void ButtonStateSuffixPrefersDisabledThenPressed()
    {
        Assert.That(WishActionButton.Suffix(false, true, true), Is.EqualTo("_disabled"));
        Assert.That(WishActionButton.Suffix(true, true, true), Is.EqualTo("_pressed"));
        Assert.That(WishActionButton.Suffix(true, false, true), Is.EqualTo("_gaze"));
        Assert.That(WishActionButton.Suffix(true, false, false), Is.EqualTo("_idle"));
    }

    [TestCase("rewrite_zh", false)]
    [TestCase("rewrite_en", false)]
    [TestCase("release_candy_zh", true)]
    [TestCase("release_candy_en", true)]
    [TestCase("release_nature_zh", true)]
    [TestCase("release_nature_en", true)]
    public void AllButtonSpritesImportAtTheRightSize(string content, bool hasDisabled)
    {
        foreach (var state in hasDisabled ? new[] { "idle", "gaze", "pressed", "disabled" } : new[] { "idle", "gaze", "pressed" })
        {
            var sprite = Resources.Load<Sprite>(WishActionButton.Folder + "wish_btn_" + content + "_" + state);
            Assert.That(sprite, Is.Not.Null, content + "_" + state);
            // 996 x 564 px at 7333.33 PPU = 0.1358 x 0.0769 m; the capsule inside is 0.090 x 0.031 m.
            Assert.That(sprite.bounds.size.x, Is.EqualTo(.1358f).Within(.002f));
            Assert.That(sprite.bounds.size.y, Is.EqualTo(.0769f).Within(.002f));
        }
    }

    [Test]
    public void ButtonsLeaveAGapBesideTheEnlargedStrip()
    {
        const float stripHalfWidth = .034f * 2.5f / 2f;
        var gap = WishActionButton.Offset - WishActionButton.Width / 2f - stripHalfWidth;
        Assert.That(gap, Is.GreaterThanOrEqualTo(.03f));
    }

    // ---------- B. Follow bubbles yield to the chime ----------

    [Test]
    public void ParksOnTheSideOfTheFreeHand()
    {
        Assert.That(ChimeFocus.ParkYaw(0f, 1), Is.EqualTo(-50f));  // right hand writes -> park left
        Assert.That(ChimeFocus.ParkYaw(0f, 0), Is.EqualTo(50f));   // left hand writes -> park right
        Assert.That(ChimeFocus.ParkYaw(0f, -1), Is.EqualTo(-50f)); // unknown -> assume right-handed
        var p = ChimeFocus.ParkPosition(Eye, 0f, 1);
        Assert.That(p.x, Is.LessThan(0f));
        Assert.That(ChimeFocus.HorizontalDistance(p, Eye), Is.EqualTo(ChimeFocus.ParkDistance).Within(.001f));
        Assert.That(p.y, Is.EqualTo(Eye.y - ChimeFocus.ParkDrop).Within(.001f));
    }

    [Test]
    public void ParkedBubbleIsClearOfTheWritingStripAndButtons()
    {
        var stripCentre = Eye + Vector3.forward * .4f - Vector3.up * .08f;
        foreach (var hand in new[] { 0, 1 })
        {
            var parked = ChimeFocus.ParkPosition(Eye, 0f, hand);
            Assert.That(ChimeFocus.HorizontalDistance(parked, stripCentre), Is.GreaterThan(ChimeFocus.KeepOutRadius + .08f * ChimeFocus.ParkScale));
            foreach (var side in new[] { -1f, 1f })
            {
                var button = stripCentre + Vector3.right * side * WishActionButton.Offset;
                Assert.That(Vector3.Distance(parked, button), Is.GreaterThan(.2f));
            }
        }
    }

    [Test]
    public void DefaultBubbleSpotOverlapsTheBlankChimeAndIsPushedAside()
    {
        // Today: blank chime 0.55 m ahead, 0.25 m down; follow bubble 0.6 m ahead, 0.25 m down, radius 0.08 m.
        var chime = Eye + Vector3.forward * .55f - Vector3.up * .25f;
        var bubble = Eye + Vector3.forward * .6f - Vector3.up * .25f;
        Assert.That(ChimeFocus.HorizontalDistance(bubble, chime), Is.LessThan(ChimeFocus.KeepOutRadius));
        var moved = ChimeFocus.AvoidKeepOut(Eye, bubble, chime, ChimeFocus.KeepOutRadius, .08f);
        Assert.That(ChimeFocus.HorizontalDistance(moved, chime), Is.GreaterThanOrEqualTo(ChimeFocus.KeepOutRadius + .08f - .001f));
        Assert.That(ChimeFocus.HorizontalDistance(moved, Eye), Is.EqualTo(.6f).Within(.001f));
        Assert.That(moved.y, Is.EqualTo(bubble.y).Within(.001f));
    }

    [Test]
    public void ClearTargetIsNotMoved()
    {
        var chime = Eye + Vector3.forward * .55f;
        var target = Eye + Vector3.left * .6f;
        Assert.That(ChimeFocus.AvoidKeepOut(Eye, target, chime, ChimeFocus.KeepOutRadius, .08f), Is.EqualTo(target));
    }

    [Test]
    public void AvoidingKeepsTheSideTheBubbleIsAlreadyOn()
    {
        var chime = Eye + Vector3.forward * .55f;
        var slightlyRight = Eye + Quaternion.Euler(0f, 10f, 0f) * Vector3.forward * .6f;
        Assert.That(ChimeFocus.AvoidKeepOut(Eye, slightlyRight, chime, ChimeFocus.KeepOutRadius, .08f).x, Is.GreaterThan(0f));
        var slightlyLeft = Eye + Quaternion.Euler(0f, -10f, 0f) * Vector3.forward * .6f;
        Assert.That(ChimeFocus.AvoidKeepOut(Eye, slightlyLeft, chime, ChimeFocus.KeepOutRadius, .08f).x, Is.LessThan(0f));
    }

    [Test]
    public void FocusStartsForWritingFlyingOrReachingAndHasATail()
    {
        Assert.That(ChimeFocus.RawFocus(true, false, false, 9f), Is.True);
        Assert.That(ChimeFocus.RawFocus(false, true, false, 9f), Is.True);
        Assert.That(ChimeFocus.RawFocus(false, false, true, .2f), Is.True);
        Assert.That(ChimeFocus.RawFocus(false, false, true, .3f), Is.False);
        Assert.That(ChimeFocus.RawFocus(false, false, false, .1f), Is.False);

        var until = ChimeFocus.NextFocusUntil(true, 10f, 0f);
        Assert.That(ChimeFocus.Active(false, 11f, until), Is.True);
        Assert.That(ChimeFocus.Active(false, 11.3f, until), Is.False);
    }

    [Test]
    public void HoldGateNeedsAnUnbrokenHold()
    {
        var gate = new BubbleHoldGate();
        Assert.That(gate.Update(true, .3f), Is.False);
        Assert.That(gate.Progress, Is.EqualTo(.5f).Within(.01f));
        Assert.That(gate.Update(false, .02f), Is.False);   // finger left: start over
        Assert.That(gate.Progress, Is.EqualTo(0f));
        Assert.That(gate.Update(true, .3f), Is.False);
        Assert.That(gate.Update(true, .31f), Is.True);     // completes once
        Assert.That(gate.Update(true, .5f), Is.False);
    }
}
