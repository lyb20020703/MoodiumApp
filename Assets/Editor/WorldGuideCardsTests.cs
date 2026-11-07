using Moodium.Guide;
using NUnit.Framework;
using UnityEngine;

public sealed class WorldGuideCardsTests
{
    static readonly GuideWorld[] Worlds = { GuideWorld.Candy, GuideWorld.Nature };
    static readonly GuideCard[] Cards = { GuideCard.Press, GuideCard.Free, GuideCard.Rhythm, GuideCard.Breath };
    static readonly bool[] Languages = { false, true };

    [Test]
    public void SpriteNames_FollowTheConvention()
    {
        Assert.That(WorldGuideCards.CardSpriteName(GuideWorld.Candy, GuideCard.Rhythm, false), Is.EqualTo("guide_card_candy_rhythm_zh"));
        Assert.That(WorldGuideCards.CardSpriteName(GuideWorld.Nature, GuideCard.Press, true), Is.EqualTo("guide_card_nature_press_en"));
        Assert.That(WorldGuideCards.ExploreSpriteName(GuideWorld.Nature, false), Is.EqualTo("guide_explore_nature_zh"));
        Assert.That(WorldGuideCards.ActionButtonSpriteName("start", GuideWorld.Candy, true), Is.EqualTo("guide_btn_start_candy_en"));
        Assert.That(WorldGuideCards.SkipSpriteName(false), Is.EqualTo("guide_btn_skip_zh"));
    }

    [Test]
    public void EverySpriteIsInResourcesAtRealSize()
    {
        foreach (var world in Worlds)
        foreach (var english in Languages)
        {
            foreach (var card in Cards)
                AssertSprite(WorldGuideCards.CardSpriteName(world, card, english), .42f);
            AssertSprite(WorldGuideCards.ExploreSpriteName(world, english), .42f * 640f / 520f);
            foreach (var kind in new[] { "next", "start", "explore" })
                AssertSprite(WorldGuideCards.ActionButtonSpriteName(kind, world, english), -1f);
        }
        foreach (var english in Languages)
            AssertSprite(WorldGuideCards.SkipSpriteName(english), -1f);
    }

    static void AssertSprite(string name, float expectedWidthMeters)
    {
        var sprite = Resources.Load<Sprite>(WorldGuideCards.ResourceFolder + name);
        Assert.That(sprite, Is.Not.Null, $"Missing Resources/{WorldGuideCards.ResourceFolder}{name}.png");
        Assert.That(sprite.pixelsPerUnit, Is.EqualTo(WorldGuideCards.PixelsPerUnit).Within(.5f),
            $"{name}: re-import the folder so WorldGuideAssetPostprocessor applies its Pixels Per Unit");
        if (expectedWidthMeters > 0f)
            Assert.That(sprite.bounds.size.x, Is.EqualTo(expectedWidthMeters).Within(.002f), name);
    }

    [TestCase(-.08f, 1)]
    [TestCase(.08f, -1)]
    [TestCase(.02f, 0)]
    [TestCase(-.049f, 0)]
    public void DragLeftTurnsToTheNextCard(float drag, int expected)
    {
        Assert.That(WorldGuideCards.SwipeStep(drag, .05f), Is.EqualTo(expected));
    }

    [Test]
    public void ButtonsSitOnTheRightEdgeOfTheCard()
    {
        // A 140 px wide "Next" button whose right edge is at 488 px on a 520 x 660 card.
        var width = 140f * WorldGuideCards.MetersPerDesignPixel;
        var p = WorldGuideCards.PlaceByRightEdge(WorldGuideCards.CardSize, 488f, 605f, width);
        Assert.That(p.x + width / 2f, Is.EqualTo((488f - 260f) * WorldGuideCards.MetersPerDesignPixel).Within(1e-5f));
        Assert.That(p.y, Is.EqualTo((330f - 605f) * WorldGuideCards.MetersPerDesignPixel).Within(1e-5f));
        Assert.That(p.z, Is.LessThan(0f), "buttons must sit in front of the card (towards the viewer)");
    }

    [Test]
    public void CardIsFortyTwoCentimetresWide()
    {
        Assert.That(WorldGuideCards.CardSize.x * WorldGuideCards.MetersPerDesignPixel, Is.EqualTo(.42f).Within(1e-5f));
    }
}
