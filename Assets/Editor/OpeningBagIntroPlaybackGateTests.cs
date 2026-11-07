using Moodium.Opening;
using NUnit.Framework;

public sealed class OpeningBagIntroPlaybackGateTests
{
    [Test]
    public void YesSelection_StartsTheOneShotPanelOnce()
    {
        var gate = new OpeningBagIntroPlaybackGate();

        Assert.That(gate.TryBegin(hasHardware: true), Is.True);
        Assert.That(gate.IsVisible, Is.True);
        Assert.That(gate.TryBegin(hasHardware: true), Is.False);
    }

    [Test]
    public void NoSelection_DoesNotStartTheOneShotPanel()
    {
        var gate = new OpeningBagIntroPlaybackGate();

        Assert.That(gate.TryBegin(hasHardware: false), Is.False);
        Assert.That(gate.IsVisible, Is.False);
    }

    [Test]
    public void Completion_HidesThePanelWithoutReplaying()
    {
        var gate = new OpeningBagIntroPlaybackGate();
        gate.TryBegin(hasHardware: true);

        gate.Complete();

        Assert.That(gate.IsVisible, Is.False);
        Assert.That(gate.TryBegin(hasHardware: true), Is.False);
    }
}
