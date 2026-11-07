using Moodium.Flow;
using NUnit.Framework;

/// <summary>Moodi's spoken invitation on the sticker world entries (CandyWorldUI / NatureWorldUI).</summary>
public sealed class MoodiStickerInviteTests
{
    [Test]
    public void EachWorldHasItsOwnLineAndTheRecommendedWorldGetsTheRecommendation()
    {
        Assert.That(StickerWorldSelectionController.MoodiInviteLine("candy", null), Is.EqualTo("S1c"));
        Assert.That(StickerWorldSelectionController.MoodiInviteLine("nature", ""), Is.EqualTo("S1n"));
        Assert.That(StickerWorldSelectionController.MoodiInviteLine("Candy", "candy"), Is.EqualTo("S2"));
        Assert.That(StickerWorldSelectionController.MoodiInviteLine("nature", "candy"), Is.EqualTo("S1n"));
    }

    [Test]
    public void MoodisDoNotTalkOverEachOtherOrRepeatTooOften()
    {
        Assert.That(StickerWorldSelectionController.CanMoodiInvite(10f, -100f, -100f), Is.True);
        Assert.That(StickerWorldSelectionController.CanMoodiInvite(10f, 8f, -100f), Is.False, "another Moodi spoke 2 s ago");
        Assert.That(StickerWorldSelectionController.CanMoodiInvite(30f, 20f, 15f), Is.False, "same world spoke 15 s ago");
        Assert.That(StickerWorldSelectionController.CanMoodiInvite(30f, 20f, 15f, true), Is.True, "recommendation ignores the world cooldown");
        Assert.That(StickerWorldSelectionController.CanMoodiInvite(40f, 20f, 15f), Is.True);
    }
}
