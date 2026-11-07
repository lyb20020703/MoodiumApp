using Moodium.CandyWorld;
using NUnit.Framework;

public sealed class WristExitConfirmationTests
{
    // This catches the regression where a single accidental wrist contact immediately leaves the world.
    [Test]
    public void SecondPressInsideTheConfirmationWindowConfirmsExit()
    {
        var confirmation = new WristExitConfirmation(2f);

        Assert.That(confirmation.Press(10f), Is.False);
        Assert.That(confirmation.Press(11.9f), Is.True);
    }

    // This catches the regression where a stale first touch can later exit the world without re-confirming.
    [Test]
    public void PressAfterTheConfirmationWindowArmsANewConfirmationInsteadOfExiting()
    {
        var confirmation = new WristExitConfirmation(2f);

        Assert.That(confirmation.Press(10f), Is.False);
        Assert.That(confirmation.Press(12.01f), Is.False);
    }
}
