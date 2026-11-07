using Moodium.NatureWorld;
using NUnit.Framework;

public sealed class WalkTrailGuideControllerTests
{
    [TestCase(0f,1.65f)]
    [TestCase(-.3f,1.2f)]
    public void GuideTracksTargetHorizontallyButStaysAtCompanionHeight(float groundY,float viewerY)
    {
        var target=new UnityEngine.Vector3(2,groundY,3);
        var viewer=new UnityEngine.Vector3(0,viewerY,0);
        var hovering=WalkTrailGuideController.GuideHoverPoint(target,viewer,0);
        Assert.That(hovering.x,Is.EqualTo(target.x));
        Assert.That(hovering.z,Is.EqualTo(target.z));
        Assert.That(hovering.y,Is.EqualTo(WalkTrailGuideController.CompanionFollowPoint(viewer,UnityEngine.Vector3.forward).y).Within(.0001f));
        Assert.That(hovering.y,Is.GreaterThan(groundY+.5f));
        Assert.That(WalkTrailGuideController.GuideHoverPoint(target,viewer,1).y-hovering.y,Is.LessThanOrEqualTo(.0151f));
    }
    [Test]
    public void CompanionFollowsViewerInsteadOfReturningToAFixedBagPosition()
    {
        var first = WalkTrailGuideController.CompanionFollowPoint(UnityEngine.Vector3.zero, UnityEngine.Vector3.forward);
        var movement = new UnityEngine.Vector3(2f, 0f, -1f);
        var next = WalkTrailGuideController.CompanionFollowPoint(movement, UnityEngine.Vector3.forward);
        Assert.That(UnityEngine.Vector3.Distance(next - first, movement), Is.LessThan(.0001f));
        var intermediate = WalkTrailGuideController.SmoothGuidePosition(first, next, .016f);
        Assert.That(UnityEngine.Vector3.Distance(intermediate, first), Is.GreaterThan(0f));
        Assert.That(UnityEngine.Vector3.Distance(intermediate, next), Is.GreaterThan(.1f));
    }
    [Test]
    public void GuideAdvancesWithoutViewerMovementButWaitsWhenTooFarAhead()
    {
        Assert.That(WalkTrailGuideController.AdvanceGuideDistance(.3f, 1.3f, .3f, 1f), Is.GreaterThan(.3f));
        Assert.That(WalkTrailGuideController.AdvanceGuideDistance(.6f, 1.3f, 1.4f, 1f), Is.EqualTo(.6f));
        Assert.That(WalkTrailGuideController.AdvanceGuideDistance(1.2f, 1.3f, .3f, 1f), Is.EqualTo(1.3f));
    }
    [Test]
    public void WalkingInvitationWaitsTwentySecondsWithoutPalmCycle()
    {
        Assert.That(WalkTrailGuideController.ShouldOfferWalk(false, false, -1f, 19.99f), Is.False);
        Assert.That(WalkTrailGuideController.ShouldOfferWalk(false, false, -1f, 20f), Is.True);
        Assert.That(WalkTrailGuideController.ShouldOfferWalk(true, true, 3f, 30f), Is.False);
    }

    [Test]
    public void MelodyClimbsThenLoopsFromFourthNote()
    {
        Assert.That(WalkTrailGuideController.NextNoteIndex(0), Is.EqualTo(1));
        Assert.That(WalkTrailGuideController.NextNoteIndex(6), Is.EqualTo(7));
        Assert.That(WalkTrailGuideController.NextNoteIndex(7), Is.EqualTo(3));
    }
}
