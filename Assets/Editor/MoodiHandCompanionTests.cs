using Moodium.Characters;
using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;

/// <summary>Gesture math for the walking Moodi's hand interactions, and the new walk-guide helpers.</summary>
public sealed class MoodiHandCompanionTests
{
    // Hands held flat in front, fingers pointing +Z. Palm up: the right thumb side is +X, the left is -X.
    static readonly Vector3 Wrist = Vector3.zero;
    static readonly Vector3 Middle = new(0f, 0f, .035f);

    [Test]
    public void PalmFacingPointsOutOfThePalmForBothHands()
    {
        var rightUp = MoodiHandCompanion.PalmFacing(Wrist, new Vector3(.02f, 0f, .03f), Middle, new Vector3(-.02f, 0f, .03f), true);
        var leftUp = MoodiHandCompanion.PalmFacing(Wrist, new Vector3(-.02f, 0f, .03f), Middle, new Vector3(.02f, 0f, .03f), false);
        var rightDown = MoodiHandCompanion.PalmFacing(Wrist, new Vector3(-.02f, 0f, .03f), Middle, new Vector3(.02f, 0f, .03f), true);
        Assert.That(Vector3.Dot(rightUp, Vector3.up), Is.EqualTo(1f).Within(.001f));
        Assert.That(Vector3.Dot(leftUp, Vector3.up), Is.EqualTo(1f).Within(.001f));
        Assert.That(Vector3.Dot(rightDown, Vector3.up), Is.EqualTo(-1f).Within(.001f));
        Assert.That(MoodiHandCompanion.PalmFacing(Wrist, Wrist, Wrist, Wrist, true), Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void OpenAndCurledFingers()
    {
        var knuckle = new Vector3(0f, 0f, .09f);
        Assert.That(MoodiHandCompanion.FingerOpen(Wrist, knuckle, new Vector3(0f, 0f, .17f)), Is.True);
        Assert.That(MoodiHandCompanion.FingerOpen(Wrist, knuckle, new Vector3(0f, -.02f, .06f)), Is.False);
    }

    [Test]
    public void PalmUpToleratesATiltButNotASidewaysHand()
    {
        Assert.That(MoodiHandCompanion.IsPalmUp(Vector3.up, 55f), Is.True);
        Assert.That(MoodiHandCompanion.IsPalmUp(new Vector3(1f, 1f, 0f).normalized, 55f), Is.True);
        Assert.That(MoodiHandCompanion.IsPalmUp(new Vector3(1f, .3f, 0f).normalized, 55f), Is.False);
        Assert.That(MoodiHandCompanion.IsPalmUp(Vector3.zero, 55f), Is.False);
    }

    [Test]
    public void SummonZoneIsInFrontBelowTheEyesAndWithinReach()
    {
        var head = new Vector3(0f, 1.6f, 0f);
        var reach = new Vector2(.2f, .7f);
        Assert.That(MoodiHandCompanion.IsInSummonZone(new Vector3(.1f, 1.25f, .4f), head, Vector3.forward, reach), Is.True);
        Assert.That(MoodiHandCompanion.IsInSummonZone(new Vector3(0f, 1.25f, -.4f), head, Vector3.forward, reach), Is.False, "behind");
        Assert.That(MoodiHandCompanion.IsInSummonZone(new Vector3(0f, 1.7f, .4f), head, Vector3.forward, reach), Is.False, "above the eyes");
        Assert.That(MoodiHandCompanion.IsInSummonZone(new Vector3(0f, 1.2f, 1f), head, Vector3.forward, reach), Is.False, "too far");
        Assert.That(MoodiHandCompanion.IsInSummonZone(new Vector3(0f, .7f, .3f), head, Vector3.forward, reach), Is.False, "hanging down");
    }

    [Test]
    public void FlightArcLandsOnTheTargetAndSquashSettles()
    {
        var a = new Vector3(0f, 1f, 1f);
        var b = new Vector3(.3f, 1.2f, .4f);
        Assert.That(Vector3.Distance(MoodiHandCompanion.ArcPoint(a, b, 0f, .1f), a), Is.LessThan(1e-5f));
        Assert.That(Vector3.Distance(MoodiHandCompanion.ArcPoint(a, b, 1f, .1f), b), Is.LessThan(1e-5f));
        Assert.That(MoodiHandCompanion.ArcPoint(a, b, .5f, .1f).y, Is.EqualTo((a.y + b.y) * .5f + .1f).Within(1e-4f));
        Assert.That(MoodiHandCompanion.Squash(0f).y, Is.EqualTo(.85f).Within(1e-4f));
        Assert.That(Vector3.Distance(MoodiHandCompanion.Squash(1f), Vector3.one), Is.LessThan(1e-4f));
        var perch = MoodiHandCompanion.PerchPoint(Vector3.one, Vector3.up, .022f);
        Assert.That(perch.y - 1f, Is.EqualTo(.022f).Within(1e-5f));
    }

    [Test]
    public void TurningThePalmOverReleases()
    {
        Assert.That(MoodiHandCompanion.IsFlipped(Vector3.up, .2f), Is.False);
        Assert.That(MoodiHandCompanion.IsFlipped(Vector3.right, .2f), Is.True);
        Assert.That(MoodiHandCompanion.IsFlipped(Vector3.down, .2f), Is.True);
    }

    [Test]
    public void RestingViewerGetsMoodiOnTheRightAndAwayMeansOutOfView()
    {
        var viewer = new Vector3(0f, 1.6f, 0f);
        var approach = WalkTrailGuideController.ApproachPoint(viewer, Vector3.forward);
        Assert.That(approach.z, Is.EqualTo(.5f).Within(1e-4f));
        Assert.That(approach.y, Is.LessThan(viewer.y));
        Assert.That(approach.x, Is.EqualTo(.22f).Within(1e-4f));
        Assert.That(WalkTrailGuideController.IsOutOfView(Vector3.forward, new Vector3(.2f, 0f, 1f)), Is.False);
        Assert.That(WalkTrailGuideController.IsOutOfView(Vector3.forward, Vector3.right), Is.True);
        Assert.That(WalkTrailGuideController.IsOutOfView(Vector3.forward, Vector3.back), Is.True);
    }
}
