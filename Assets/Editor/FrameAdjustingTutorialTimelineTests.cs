using Moodium.Calibration;
using NUnit.Framework;

public sealed class FrameAdjustingTutorialTimelineTests
{
    [Test]
    public void BothLanguages_UseTheConfirmedSubtitleFrameRange()
    {
        var expectedStart = FrameAdjustingTutorialTimeline.ToFrame(0, 2, 8);
        var expectedEnd = FrameAdjustingTutorialTimeline.ToFrame(0, 5, 11);

        Assert.That(FrameAdjustingTutorialTimeline.ChineseSubtitle.StartFrame, Is.EqualTo(expectedStart));
        Assert.That(FrameAdjustingTutorialTimeline.ChineseSubtitle.EndFrame, Is.EqualTo(expectedEnd));
        Assert.That(FrameAdjustingTutorialTimeline.EnglishSubtitle.StartFrame, Is.EqualTo(expectedStart));
        Assert.That(FrameAdjustingTutorialTimeline.EnglishSubtitle.EndFrame, Is.EqualTo(expectedEnd));
    }
}
