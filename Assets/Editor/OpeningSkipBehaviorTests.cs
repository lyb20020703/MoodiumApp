using System.IO;
using NUnit.Framework;
using Moodium.MoodCheck;

public sealed class OpeningSkipBehaviorTests
{
    [Test]
    public void Skip_RemainsAvailableDuringTheHardwareTutorial()
    {
        var source = File.ReadAllText("Assets/Scripts/Moodium/Opening/OpeningManager.cs");

        StringAssert.Contains("System.Func<bool> skipRequested", source);
        StringAssert.Contains("SkipToStickerSelection", source);
        StringAssert.Contains("HideIntroGuidance", source);
        StringAssert.Contains("while (CurrentFrame(player) < branch.EndFrame", source);
        StringAssert.Contains("if (skipRequested())", source);
    }

    [Test]
    public void MoodCheckWait_StopsImmediatelyWhenTutorialSkipIsRequested()
    {
        Assert.That(OpeningMoodCheckPolicy.ShouldWait(true, false), Is.True);
        Assert.That(OpeningMoodCheckPolicy.ShouldWait(true, true), Is.False);
        Assert.That(OpeningMoodCheckPolicy.ShouldWait(false, false), Is.False);
    }

    [Test]
    public void OpeningVideo_CreatesItsWindowHandleBeforeTheTutorialBegins()
    {
        var source = File.ReadAllText("Assets/Scripts/Moodium/Opening/OpeningManager.cs");
        var displayCreated = source.IndexOf("display.name = \"Moodium Opening Video Display\"", System.StringComparison.Ordinal);
        var handleCreated = source.IndexOf("EnsureOpeningWindowHandle(display.transform)", System.StringComparison.Ordinal);
        var tutorialBegins = source.IndexOf("PlayHardwareTutorial(", System.StringComparison.Ordinal);

        Assert.That(displayCreated, Is.GreaterThanOrEqualTo(0));
        Assert.That(handleCreated, Is.GreaterThan(displayCreated));
        Assert.That(handleCreated, Is.LessThan(tutorialBegins));
    }
}
