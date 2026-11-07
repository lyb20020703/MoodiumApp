using Moodium.CandyWorld;
using NUnit.Framework;
using UnityEngine;

public sealed class CandyProjectileVideoExclusionTests
{
    [Test]
    public void FastProjectileCrossingVideoIsDetectedEvenWhenBothEndpointsAreOutside()
    {
        Assert.That(CandyProjectile.CrossesProtectedSphere(Vector3.left, Vector3.right, .05f), Is.True);
    }

    [Test]
    public void ProjectilePassingBesideVideoIsKept()
    {
        Assert.That(CandyProjectile.CrossesProtectedSphere(
            Vector3.left + Vector3.up, Vector3.right + Vector3.up, .05f), Is.False);
    }

    [Test]
    public void StationaryProjectileInsideVideoIsRemoved()
    {
        Assert.That(CandyProjectile.CrossesProtectedSphere(Vector3.zero, Vector3.zero, .05f), Is.True);
    }
}
