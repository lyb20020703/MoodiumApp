using Moodium.Opening;
using NUnit.Framework;

public sealed class OpeningVideoTimelineTests
{
    [Test]
    public void Timeline_ConvertsThirtyFpsTimecodesToExactFrames()
    {
        Assert.That(OpeningVideoTimeline.ToFrame(0, 24, 14), Is.EqualTo(734));
        Assert.That(OpeningVideoTimeline.ToFrame(0, 32, 8), Is.EqualTo(968));
        Assert.That(OpeningVideoTimeline.ToFrame(2, 10, 29), Is.EqualTo(3929));
    }

    [Test]
    public void Timeline_UsesConfirmedLanguageAndHardwareSegments()
    {
        Assert.That(OpeningVideoTimeline.LanguageLoop.StartFrame, Is.EqualTo(734));
        Assert.That(OpeningVideoTimeline.LanguageLoop.EndFrame, Is.EqualTo(968));

        Assert.That(OpeningVideoTimeline.Chinese.QuestionLoop.StartFrame, Is.EqualTo(1240));
        Assert.That(OpeningVideoTimeline.Chinese.QuestionLoop.EndFrame, Is.EqualTo(1410));
        Assert.That(OpeningVideoTimeline.Chinese.Yes.StartFrame, Is.EqualTo(1410));
        Assert.That(OpeningVideoTimeline.Chinese.Yes.EndFrame, Is.EqualTo(1873));
        Assert.That(OpeningVideoTimeline.Chinese.No.StartFrame, Is.EqualTo(2058));
        Assert.That(OpeningVideoTimeline.Chinese.No.EndFrame, Is.EqualTo(2409));

        Assert.That(OpeningVideoTimeline.English.EntryFrame, Is.EqualTo(2507));
        Assert.That(OpeningVideoTimeline.English.QuestionLoop.StartFrame, Is.EqualTo(2811));
        Assert.That(OpeningVideoTimeline.English.QuestionLoop.EndFrame, Is.EqualTo(2959));
        Assert.That(OpeningVideoTimeline.English.Yes.StartFrame, Is.EqualTo(2959));
        Assert.That(OpeningVideoTimeline.English.Yes.EndFrame, Is.EqualTo(3417));
        Assert.That(OpeningVideoTimeline.English.No.StartFrame, Is.EqualTo(3567));
        Assert.That(OpeningVideoTimeline.English.No.EndFrame, Is.EqualTo(3929));
    }

    [Test]
    public void Timeline_ChineseContinuesAtLanguageLoopEnd()
    {
        Assert.That(OpeningVideoTimeline.Chinese.EntryFrame,
            Is.EqualTo(OpeningVideoTimeline.LanguageLoop.EndFrame));
    }

    [Test]
    public void Timeline_DoesNotTreatUnknownRuntimeFrameCountAsTooShort()
    {
        Assert.That(OpeningVideoTimeline.HasKnownInsufficientFrameCount(
            0, OpeningVideoTimeline.English.No.EndFrame), Is.False);
        Assert.That(OpeningVideoTimeline.HasKnownInsufficientFrameCount(
            3000, OpeningVideoTimeline.English.No.EndFrame), Is.True);
        Assert.That(OpeningVideoTimeline.HasKnownInsufficientFrameCount(
            4026, OpeningVideoTimeline.English.No.EndFrame), Is.False);
    }

    [Test]
    public void Timeline_UsesThirtySecondChineseFallbackWhileDebugging()
    {
        Assert.That(OpeningVideoTimeline.LanguageAutoSelectTimeoutSeconds, Is.EqualTo(30f));
    }
}
