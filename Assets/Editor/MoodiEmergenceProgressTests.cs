using Moodium.Reality;
using NUnit.Framework;
using UnityEngine;

public sealed class MoodiEmergenceProgressTests
{
    [Test]
    public void Progress_MapsLinearlyAcrossTheWholeAnimationClip()
    {
        const float clipLength = 4f;

        Assert.That(MoodiEmergenceProgress.ToAnimationTime(0f, clipLength), Is.EqualTo(0f));
        Assert.That(MoodiEmergenceProgress.ToAnimationTime(.25f, clipLength), Is.EqualTo(1f));
        Assert.That(MoodiEmergenceProgress.ToAnimationTime(.5f, clipLength), Is.EqualTo(2f));
        Assert.That(MoodiEmergenceProgress.ToAnimationTime(1f, clipLength), Is.EqualTo(4f));
    }

    [Test]
    public void Visibility_StaysHiddenAtZeroAndAppearsAfterProgressBegins()
    {
        Assert.That(MoodiEmergenceProgress.ShouldBeVisible(0f), Is.False);
        Assert.That(MoodiEmergenceProgress.ShouldBeVisible(.001f), Is.True);
    }

    [Test]
    public void Emergence_UsesTheVisualPrefabOrientation_NotTheTrackingAnchor()
    {
        var visualOrientation = Quaternion.identity;

        Assert.That(
            Quaternion.Angle(MoodiEmergenceProgress.ResolveVisualOrientation(visualOrientation), Quaternion.identity),
            Is.LessThan(.01f));
    }

    [Test]
    public void Emergence_UsesTheApprovedUprightLocalRotation()
    {
        Assert.That(
            Quaternion.Angle(
                MoodiEmergenceProgress.AuthoredLocalRotation,
                Quaternion.Euler(-90f, 0f, 180f)),
            Is.LessThan(.01f));
    }

    [Test]
    public void Emergence_UsesUniformOverallBagScale_WithoutMascotDistortion()
    {
        // A calibrated bag can have independent face dimensions. Moodi should
        // grow/shrink with the overall bag size, but must not inherit that
        // anisotropy and squash its face.
        Assert.That(
            MoodiEmergenceProgress.CalculateUniformBagScale(new Vector3(4f, 2f, 1f)),
            Is.EqualTo(2f).Within(.0001f));
    }

    [Test]
    public void Emergence_RisesFromInsideTheBagAcrossTheConfiguredProgressWindow()
    {
        const float liftDistance = .12f;

        Assert.That(MoodiEmergenceProgress.CalculateVerticalLift(0f, .08f, .92f, liftDistance),
            Is.EqualTo(0f).Within(.0001f));
        Assert.That(MoodiEmergenceProgress.CalculateVerticalLift(.08f, .08f, .92f, liftDistance),
            Is.EqualTo(0f).Within(.0001f));
        Assert.That(MoodiEmergenceProgress.CalculateVerticalLift(.5f, .08f, .92f, liftDistance),
            Is.GreaterThan(0f).And.LessThan(liftDistance));
        Assert.That(MoodiEmergenceProgress.CalculateVerticalLift(1f, .08f, .92f, liftDistance),
            Is.EqualTo(liftDistance).Within(.0001f));
    }

    [Test]
    public void Emergence_LiftIsLimitedByTheSoftsVerticalThickness_NotItsPlanarSize()
    {
        const float softThickness = .08f;

        Assert.That(MoodiEmergenceProgress.CalculateSurfaceLimitedLift(softThickness, .45f),
            Is.EqualTo(.036f).Within(.0001f));
        Assert.That(MoodiEmergenceProgress.CalculateSurfaceLimitedLift(softThickness, 2f),
            Is.EqualTo(softThickness * .5f).Within(.0001f));
    }

    [Test]
    public void PreviewLoop_AdvancesFromHiddenToFullyEmergedBeforeRestarting()
    {
        Assert.That(MoodiEmergencePreviewController.AutoPreviewProgress(0f, 8f), Is.EqualTo(0f));
        Assert.That(MoodiEmergencePreviewController.AutoPreviewProgress(2f, 8f), Is.EqualTo(.25f));
        Assert.That(MoodiEmergencePreviewController.AutoPreviewProgress(8f, 8f), Is.EqualTo(0f));
    }

    [Test]
    public void Preview_UsesAnIndependentMotionRootSoTheSoftNeverRisesWithMoodi()
    {
        var bagRoot = new GameObject("Bag Root").transform;
        var motionRoot = MoodiEmergencePreviewController.CreateEmergenceMotionRoot(bagRoot);

        Assert.That(motionRoot.parent, Is.EqualTo(bagRoot));
        Assert.That(motionRoot.localPosition, Is.EqualTo(Vector3.zero));
        Assert.That(motionRoot.localRotation, Is.EqualTo(Quaternion.identity));

        Object.DestroyImmediate(bagRoot.gameObject);
    }
}
