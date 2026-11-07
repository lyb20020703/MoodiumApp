using Moodium.Interaction;
using NUnit.Framework;

public sealed class HandContactStabilityGateTests
{
    [Test]
    public void ShortTrackingGap_DoesNotEndTheSameContact()
    {
        var gate = new HandContactStabilityGate(releaseGraceSeconds: .18f, cooldownSeconds: .35f);

        Assert.That(gate.Update(isTouching: true, currentTime: 0f), Is.True);
        Assert.That(gate.IsContactActive, Is.True);
        Assert.That(gate.Update(isTouching: false, currentTime: .08f), Is.False);
        Assert.That(gate.IsContactActive, Is.True);
    }

    [Test]
    public void RecontactAfterRealRelease_RespectsCooldown()
    {
        var gate = new HandContactStabilityGate(releaseGraceSeconds: .18f, cooldownSeconds: .35f);

        Assert.That(gate.Update(isTouching: true, currentTime: 0f), Is.True);
        gate.Update(isTouching: false, currentTime: .20f);
        Assert.That(gate.IsContactActive, Is.False);
        Assert.That(gate.Update(isTouching: true, currentTime: .25f), Is.False);
        gate.Update(isTouching: false, currentTime: .50f);
        Assert.That(gate.Update(isTouching: true, currentTime: .60f), Is.True);
    }
}
