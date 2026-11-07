using Moodium.Reality;
using NUnit.Framework;
using UnityEngine;

public sealed class CandyWrapperCrackMathTests
{
    [Test]
    public void PalmOrThreeTips_ProducesLargeCluster()
    {
        var large = CandyWrapperCrackMath.IsLargeCluster(
            contactCount: 3,
            hasPalm: false,
            minimumJointCountForLarge: 3);

        Assert.That(large, Is.True);
        Assert.That(CandyWrapperCrackMath.IsLargeCluster(1, true, 3), Is.True);
    }

    [Test]
    public void NearbyRecentSpawn_IsDeduplicated()
    {
        Assert.That(
            CandyWrapperCrackMath.IsDuplicateSpawn(
                new Vector3(.02f, 0f, 0f), Vector3.zero,
                now: .12f, lastSpawnTime: 0f,
                minimumDistance: .06f, minimumTime: .2f),
            Is.True);
    }

    [Test]
    public void ApproachVelocity_UsesOnlyMotionTowardSurface()
    {
        var toward = CandyWrapperCrackMath.TowardSurfaceVelocity(
            new Vector3(0f, 0f, -0.2f), Vector3.forward);
        var parallel = CandyWrapperCrackMath.TowardSurfaceVelocity(
            Vector3.right * .5f, Vector3.forward);

        Assert.That(toward, Is.EqualTo(.2f).Within(.0001f));
        Assert.That(parallel, Is.Zero);
    }

    [Test]
    public void SurfacePose_IsStoredInTheSpatialMeshLocalSpace()
    {
        var surface = new GameObject("Spatial Mesh").transform;
        surface.SetPositionAndRotation(new Vector3(2f, 1f, -3f), Quaternion.Euler(15f, 35f, 0f));
        var point = surface.TransformPoint(new Vector3(.3f, -.2f, 0f));
        var normal = surface.forward;

        var pose = CandyWrapperCrackMath.ToSurfaceLocalPose(surface, point, normal, .002f, 0f);

        Assert.That(pose.LocalPosition.z, Is.EqualTo(.002f).Within(.0001f));
        Assert.That(Vector3.Dot(pose.LocalRotation * Vector3.forward, Vector3.forward), Is.GreaterThan(.999f));
        Object.DestroyImmediate(surface.gameObject);
    }

    [Test]
    public void SurfaceNormalProbe_StartsOutsideTheSurfaceAndTravelsBackToIt()
    {
        var probe = CandyWrapperCrackMath.BuildSurfaceNormalProbe(
            surfacePoint: new Vector3(0f, 2f, 3f),
            pointOutsideSurface: new Vector3(0f, 2f, 3.02f),
            minimumOffset: .01f);

        Assert.That(probe.origin.x, Is.EqualTo(0f).Within(.0001f));
        Assert.That(probe.origin.y, Is.EqualTo(2f).Within(.0001f));
        Assert.That(probe.origin.z, Is.EqualTo(3.03f).Within(.0001f));
        Assert.That(Vector3.Dot(probe.direction, Vector3.back), Is.GreaterThan(.999f));
    }
}
