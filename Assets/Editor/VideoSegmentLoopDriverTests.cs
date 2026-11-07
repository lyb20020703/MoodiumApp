using Moodium.Opening;
using NUnit.Framework;

public sealed class VideoSegmentLoopDriverTests
{
    const long Start = 1240, End = 1380;
    static VideoSegmentLoopDriver Seeking(float now = 0f)
    {
        var driver = new VideoSegmentLoopDriver(Start, End);
        driver.NotifySeekIssued(now);
        return driver;
    }

    [Test]
    public void StaleFramesAfterTheSeekAreIgnoredUntilTheSegmentStartIsSeen()
    {
        // Skipped from the tutorial: the player still reports frame 2600 and not playing for a while.
        var driver = Seeking();
        for (var t = .02f; t < 2f; t += .02f)
            Assert.That(driver.Tick(t, 2600, false), Is.EqualTo(VideoLoopAction.None), $"t={t}");
        Assert.That(driver.SeekPending, Is.True);
        Assert.That(driver.Tick(2.1f, Start + 1, true), Is.EqualTo(VideoLoopAction.None));
        Assert.That(driver.SeekPending, Is.False);
    }

    [Test]
    public void ALostSeekGetsPlayThenANewSeek()
    {
        var driver = Seeking();
        Assert.That(driver.Tick(2.6f, 500, false), Is.EqualTo(VideoLoopAction.Play));
        Assert.That(driver.Tick(3f, 500, true), Is.EqualTo(VideoLoopAction.None), "one stall window after Play");
        Assert.That(driver.Tick(3.9f, 500, true), Is.EqualTo(VideoLoopAction.Seek));
    }

    [Test]
    public void WrapsAtTheEndOnlyAfterTheSeekLanded()
    {
        var driver = Seeking();
        Assert.That(driver.Tick(.1f, End + 5, true), Is.EqualTo(VideoLoopAction.None), "stale end frame");
        driver.Tick(.2f, Start, true);
        var t = .2f;
        for (var f = Start + 1; f < End; f++)
        {
            t += 1f / 30f;
            Assert.That(driver.Tick(t, f, true), Is.EqualTo(VideoLoopAction.None), $"frame {f}");
        }
        Assert.That(driver.Tick(t + .03f, End, true), Is.EqualTo(VideoLoopAction.Seek));
    }

    [Test]
    public void BriefNotPlayingWaitsLongNotPlayingOnlyAsksForPlay()
    {
        var driver = Seeking();
        driver.Tick(.1f, Start, true);
        Assert.That(driver.Tick(.15f, Start + 2, false), Is.EqualTo(VideoLoopAction.None));
        Assert.That(driver.Tick(.4f, Start + 9, false), Is.EqualTo(VideoLoopAction.None), "frames advance: just lagging");
        Assert.That(driver.Tick(.7f, Start + 18, false), Is.EqualTo(VideoLoopAction.Play));
    }

    [Test]
    public void StalledFramesGetPlayThenSeek()
    {
        var driver = Seeking();
        driver.Tick(.1f, Start + 10, true);
        Assert.That(driver.Tick(1f, Start + 10, true), Is.EqualTo(VideoLoopAction.None));
        Assert.That(driver.Tick(1.35f, Start + 10, true), Is.EqualTo(VideoLoopAction.Play));
        Assert.That(driver.Tick(2f, Start + 10, true), Is.EqualTo(VideoLoopAction.None));
        Assert.That(driver.Tick(2.6f, Start + 10, true), Is.EqualTo(VideoLoopAction.Seek));
    }

    [Test]
    public void LeavingTheSegmentAfterLandingSeeksBack()
    {
        var driver = Seeking();
        driver.Tick(.1f, Start, true);
        Assert.That(driver.Tick(.2f, 300, true), Is.EqualTo(VideoLoopAction.Seek));
    }
}
