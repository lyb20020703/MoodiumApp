using Moodium.MoodCheck;
using NUnit.Framework;

public sealed class MoodCheckAutoPromptPolicyTests
{
    [Test]
    public void Tick_RequiresTwelveContinuousSecondsWithoutEntries()
    {
        var policy = new MoodCheckAutoPromptPolicy();
        Assert.That(policy.Tick(11.99f, 0, false, false), Is.False);
        Assert.That(policy.Tick(.01f, 0, false, false), Is.True);
    }

    [Test]
    public void ActiveEntry_ResetsTimerAndLaterEmptyIntervalCanTrigger()
    {
        var policy = new MoodCheckAutoPromptPolicy();
        Assert.That(policy.Tick(8f, 0, false, false), Is.False);
        Assert.That(policy.Tick(1f, 1, false, false), Is.False);
        Assert.That(policy.Tick(11f, 0, false, false), Is.False);
        Assert.That(policy.Tick(1f, 0, false, false), Is.True);
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void ExistingSampleOrAlreadyShown_SuppressesAndResets(bool hasSample, bool alreadyShown)
    {
        var policy = new MoodCheckAutoPromptPolicy();
        Assert.That(policy.Tick(20f, 0, hasSample, alreadyShown), Is.False);
        Assert.That(policy.ElapsedWithoutEntries, Is.Zero);
    }
}
