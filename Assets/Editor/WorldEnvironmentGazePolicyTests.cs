using Moodium.Reality;
using NUnit.Framework;

public sealed class WorldEnvironmentGazePolicyTests
{
    [Test]
    public void GazeInteraction_UnlocksOnlyAtCompletedWorldProgress()
    {
        Assert.That(WorldEnvironmentGazePolicy.ShouldEnable(.999f), Is.False);
        Assert.That(WorldEnvironmentGazePolicy.ShouldEnable(1f), Is.True);
    }

    [Test]
    public void GazeInteraction_UsesCooldownBetweenActivations()
    {
        Assert.That(WorldEnvironmentGazePolicy.CanActivate(1f, 4.2f, 4f), Is.False);
        Assert.That(WorldEnvironmentGazePolicy.CanActivate(1f, 4.31f, 4f), Is.True);
    }
}
