using Moodium.Opening;
using NUnit.Framework;

public sealed class OpeningVideoSubtitleTimelineTests
{
    [Test]
    public void SubtitleTimeline_ShowsMoodiGreetingBeforeLanguageChoice()
    {
        Assert.That(OpeningVideoSubtitleTimeline.GetTextAtFrame(OpeningVideoTimeline.ToFrame(0, 20, 28)),
            Is.EqualTo("Hi, I'm Moodi"));
        Assert.That(OpeningVideoSubtitleTimeline.GetTextAtFrame(OpeningVideoTimeline.ToFrame(0, 23, 12)),
            Is.EqualTo("Hi, I'm Moodi"));
        Assert.That(OpeningVideoSubtitleTimeline.GetTextAtFrame(OpeningVideoTimeline.ToFrame(0, 23, 13)),
            Is.Empty);
    }

    [Test]
    public void SubtitleTimeline_ShowsChineseCueInsideItsAuthoredFrameRange()
    {
        Assert.That(OpeningVideoSubtitleTimeline.GetTextAtFrame(OpeningVideoTimeline.ToFrame(0, 32, 19)),
            Is.EqualTo("欢迎来到 Moodium 的世界。"));
        Assert.That(OpeningVideoSubtitleTimeline.GetTextAtFrame(OpeningVideoTimeline.ToFrame(0, 34, 23)),
            Is.EqualTo("欢迎来到 Moodium 的世界。"));
    }

    [Test]
    public void SubtitleTimeline_HidesCueAtExclusiveEndAndDuringGaps()
    {
        Assert.That(OpeningVideoSubtitleTimeline.GetTextAtFrame(OpeningVideoTimeline.ToFrame(0, 34, 24)),
            Is.Empty);
        Assert.That(OpeningVideoSubtitleTimeline.GetTextAtFrame(OpeningVideoTimeline.ToFrame(0, 41, 10)),
            Is.Empty);
        Assert.That(OpeningVideoSubtitleTimeline.GetTextAtFrame(OpeningVideoTimeline.ToFrame(0, 45, 0)),
            Is.Empty);
    }

    [Test]
    public void SubtitleTimeline_FollowsEnglishAndHardwareBranchSeeks()
    {
        Assert.That(OpeningVideoSubtitleTimeline.GetTextAtFrame(OpeningVideoTimeline.ToFrame(1, 24, 14)),
            Is.EqualTo("Welcome to the world of Moodium."));
        Assert.That(OpeningVideoSubtitleTimeline.GetTextAtFrame(OpeningVideoTimeline.ToFrame(1, 39, 19)),
            Is.EqualTo("First, fill your sensory bag with a tactile material you’d like to use."));
        Assert.That(OpeningVideoSubtitleTimeline.GetTextAtFrame(OpeningVideoTimeline.ToFrame(2, 4, 23)),
            Is.EqualTo("Then place a Moodium sticker on it."));
    }
}
