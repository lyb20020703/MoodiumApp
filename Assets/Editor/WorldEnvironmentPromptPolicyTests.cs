using Moodium.Reality;
using NUnit.Framework;

public sealed class WorldEnvironmentPromptPolicyTests
{
    [Test]
    public void CandyPrompt_AppearsWhenWrapperBecomesInteractive()
    {
        Assert.That(WorldEnvironmentPromptPolicy.ShouldShow(false, .19f, false), Is.False);
        Assert.That(WorldEnvironmentPromptPolicy.ShouldShow(false, .20f, false), Is.True);
    }

    [Test]
    public void NaturePrompt_AppearsOnlyWhenEnvironmentPinchBecomesAvailable()
    {
        Assert.That(WorldEnvironmentPromptPolicy.ShouldShow(true, 0f, false), Is.False);
        Assert.That(WorldEnvironmentPromptPolicy.ShouldShow(true, .99f, true), Is.False);
        Assert.That(WorldEnvironmentPromptPolicy.ShouldShow(true, 1f, true), Is.True);
    }

    [Test]
    public void Prompt_UsesTheSelectedLanguage()
    {
        Assert.That(WorldEnvironmentPromptPolicy.Message(false, false), Does.Contain("糖纸"));
        Assert.That(WorldEnvironmentPromptPolicy.Message(true, true), Does.Contain("Pinch"));
    }
}
