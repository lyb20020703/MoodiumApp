using Moodium.Guide;
using Moodium.Squeeze;
using NUnit.Framework;
using UnityEngine;

/// <summary>"Ask when you need it" tutorial: the ? button, the docked card and the quiet 100% hint.</summary>
public sealed class GuideHelpTests
{
    [Test]
    public void HelpButtonSitsAtTheRightEndOfTheRowAndTheCardStandsBehindIt()
    {
        var half = new Vector2(.125f, .085f);
        var button = HelpPuck.LocalPosition(half);
        Assert.That(button.x, Is.EqualTo(-.17f).Within(1e-6f), "screen-right of the breath button (−0.085)");
        Assert.That(button.z, Is.EqualTo(-half.y - .13f).Within(1e-6f), "same row as the mode buttons");
        var dock = HelpPuck.CardDockPosition(half);
        Assert.That(dock.x, Is.EqualTo(button.x));
        Assert.That(dock.z, Is.EqualTo(button.z - HelpPuck.CardGap).Within(1e-6f), "behind the button, away from the viewer");
        Assert.That(HelpPuck.Size, Is.LessThan(.08f), "one size smaller than a mode button");
    }

    [Test]
    public void FirstVisitPulseIsTwoRingsAndThenStops()
    {
        Assert.That(HelpPuck.PulsePhase(-.1f), Is.EqualTo(-1f));
        Assert.That(HelpPuck.PulsePhase(0f), Is.EqualTo(0f));
        Assert.That(HelpPuck.PulsePhase(HelpPuck.PulseSeconds / 2 - .01f), Is.GreaterThan(.95f));
        Assert.That(HelpPuck.PulsePhase(HelpPuck.PulseSeconds / 2 + .01f), Is.LessThan(.05f), "second ring starts");
        Assert.That(HelpPuck.PulsePhase(HelpPuck.PulseSeconds), Is.EqualTo(-1f));
        Assert.That(HelpPuck.PulseSeconds, Is.EqualTo(2.4f));
    }

    [Test]
    public void ExploreHintFadesInHoldsAndFadesOutAfterSixSeconds()
    {
        Assert.That(SqueezeTableView.HintAlpha(0f), Is.EqualTo(0f));
        Assert.That(SqueezeTableView.HintAlpha(.3f), Is.EqualTo(1f).Within(1e-5f));
        Assert.That(SqueezeTableView.HintAlpha(3f), Is.EqualTo(1f));
        Assert.That(SqueezeTableView.HintAlpha(5.7f), Is.EqualTo(.5f).Within(1e-4f));
        Assert.That(SqueezeTableView.HintAlpha(6.01f), Is.EqualTo(0f));
    }

    [Test]
    public void DockedCardIsThirtyCentimetresAndStandsOnTheTableLeaningBack()
    {
        Assert.That(WorldGuideCards.CardSize.x * WorldGuideCards.MetersPerDesignPixel * WorldGuideCards.DockedScale,
            Is.EqualTo(.30f).Within(1e-5f));
        var p = WorldGuideCards.DockedLocalPosition();
        var half = WorldGuideCards.CardSize.y * WorldGuideCards.MetersPerDesignPixel * WorldGuideCards.DockedScale * .5f;
        var tilt = WorldGuideCards.DockedTiltDegrees * Mathf.Deg2Rad;
        Assert.That(p.y - half * Mathf.Cos(tilt), Is.EqualTo(WorldGuideCards.DockedLift).Within(1e-5f), "bottom edge 2 cm above the table");
        var r = WorldGuideCards.DockedLocalRotation();
        Assert.That(Vector3.Dot(r * Vector3.forward, Vector3.back), Is.GreaterThan(.97f), "+Z away from the viewer (dock +Z faces the viewer)");
        Assert.That((r * Vector3.up).z, Is.LessThan(0f), "top leans away from the viewer");
        // The bottom edge sits right above the dock point, so the card never slides over the ? button.
        var bottom = p + r * (Vector3.down * half);
        Assert.That(bottom.z, Is.EqualTo(0f).Within(1e-4f));
    }

    [Test]
    public void PagesStartWithExploreOnlyAfterFullEnergyAndModeButtonsMapToTheirPage()
    {
        Assert.That(WorldGuideCards.DockedPageCount(false), Is.EqualTo(4));
        Assert.That(WorldGuideCards.DockedPageCount(true), Is.EqualTo(5));
        Assert.That(WorldGuideCards.DockedPageIndex(GuideCard.Press, false), Is.EqualTo(0));
        Assert.That(WorldGuideCards.DockedPageIndex(GuideCard.Rhythm, false), Is.EqualTo(2));
        Assert.That(WorldGuideCards.DockedPageIndex(GuideCard.Rhythm, true), Is.EqualTo(3));
        Assert.That(GuideHelpController.CardFor(SqueezeMode.Free), Is.EqualTo(GuideCard.Free));
        Assert.That(GuideHelpController.CardFor(SqueezeMode.Rhythm), Is.EqualTo(GuideCard.Rhythm));
        Assert.That(GuideHelpController.CardFor(SqueezeMode.Breath), Is.EqualTo(GuideCard.Breath));
    }

    [Test]
    public void FirstVisitIsCountedOncePerWorld()
    {
        PlayerPrefs.DeleteKey(GuideHelpController.FirstVisitKey(false));
        PlayerPrefs.DeleteKey(GuideHelpController.FirstVisitKey(true));
        try
        {
            Assert.That(GuideHelpController.ConsumeFirstVisit(false), Is.True);
            Assert.That(GuideHelpController.ConsumeFirstVisit(false), Is.False);
            Assert.That(GuideHelpController.ConsumeFirstVisit(true), Is.True, "each world has its own first visit");
        }
        finally
        {
            PlayerPrefs.DeleteKey(GuideHelpController.FirstVisitKey(false));
            PlayerPrefs.DeleteKey(GuideHelpController.FirstVisitKey(true));
        }
    }

    [Test]
    public void DockedCardOpensOnTheTableAndClosesOnce()
    {
        var root = new GameObject("Guide host");
        var dock = new GameObject("Dock");
        try
        {
            dock.transform.SetPositionAndRotation(new Vector3(.2f, .75f, .5f), Quaternion.Euler(0, 30, 0));
            var guide = root.AddComponent<WorldGuideCards>();
            guide.ShowDocked(dock.transform, false, false, true);
            Assert.That(guide.IsDockedOpen, Is.True);
            Assert.That(guide.PageCount, Is.EqualTo(5));
            var panel = (Transform)typeof(WorldGuideCards).GetField("m_Root", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(guide);
            Assert.That(panel.parent, Is.SameAs(dock.transform), "the card belongs to the table, not to the viewer");
            var face = panel.GetComponentInChildren<SpriteRenderer>();
            Assert.That(face.sprite != null && face.sprite.name == "guide_explore_candy_zh", Is.True, "explore page first after 100%");
            var closed = 0;
            guide.DockedClosed += () => closed++;
            guide.CloseDocked();
            guide.CloseDocked();
            Assert.That(closed, Is.EqualTo(1));
            Assert.That(guide.IsDockedOpen, Is.False);
            guide.Hide(true);
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(dock);
        }
    }

    [Test]
    public void NewSpritesAreInResources()
    {
        foreach (var name in new[] { "mode_icon_help", "mode_label_help_cn", "mode_label_help_en",
                     "help_hint_explore_candy_cn", "help_hint_explore_candy_en", "help_hint_explore_nature_cn", "help_hint_explore_nature_en" })
            Assert.That(Resources.Load<Sprite>("MoodiumSqueezeModes/" + name), Is.Not.Null,
                $"{name}: copy into Assets/UI/Moodium/SqueezeModes/Resources/MoodiumSqueezeModes and run Moodium ▸ Setup Squeeze Modes");
        Assert.That(Resources.Load<Sprite>(WorldGuideCards.ResourceFolder + WorldGuideCards.CloseSpriteName), Is.Not.Null);
        foreach (var world in new[] { GuideWorld.Candy, GuideWorld.Nature })
        foreach (var english in new[] { false, true })
        {
            var sprite = Resources.Load<Sprite>(WorldGuideCards.ResourceFolder + WorldGuideCards.ActionButtonSpriteName("gotit", world, english));
            Assert.That(sprite, Is.Not.Null, WorldGuideCards.ActionButtonSpriteName("gotit", world, english));
            Assert.That(sprite.pixelsPerUnit, Is.EqualTo(WorldGuideCards.PixelsPerUnit).Within(.5f));
        }
    }
}
