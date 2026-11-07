using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Moodium.MoodCheck;
using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;

/// <summary>泡泡世界 2.0 P0: choose one of the family's two worlds in the room; home + next layer inside the first one.</summary>
public sealed class BubbleChoiceTests
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly string[] Folders = { BubbleWorldData.CandyFolder, BubbleWorldData.NestedCandyFolder, BubbleWorldData.NatureFolder, BubbleWorldData.NestedNatureFolder };

    [Test]
    public void EachWorldPairsWithTheOtherWorldOfItsFamilyOnly()
    {
        Assert.That(BubbleChoiceRules.Partner(BubbleWorldData.CandyFolder), Is.EqualTo(BubbleWorldData.NestedCandyFolder));
        Assert.That(BubbleChoiceRules.Partner(BubbleWorldData.NestedCandyFolder), Is.EqualTo(BubbleWorldData.CandyFolder));
        Assert.That(BubbleChoiceRules.Partner(BubbleWorldData.NatureFolder), Is.EqualTo(BubbleWorldData.NestedNatureFolder));
        Assert.That(BubbleChoiceRules.Partner(BubbleWorldData.NestedNatureFolder), Is.EqualTo(BubbleWorldData.NatureFolder));
        foreach (var f in Folders)
            Assert.That(BubbleWorldData.IsCandy(BubbleChoiceRules.Partner(f)), Is.EqualTo(BubbleWorldData.IsCandy(f)), "candy and nature never mix");
        Assert.That(BubbleChoiceRules.Partner("Elsewhere/"), Is.Null);
        Assert.That(Folders.Select(BubbleChoiceRules.Key), Is.EqualTo(new[] { "candy", "mint", "forest", "snow" }));
    }

    [TestCase(MoodKind.Tense, "mint", "snow")]
    [TestCase(MoodKind.Low, "candy", "maple")]
    [TestCase(MoodKind.Neutral, "candy", "forest")]
    [TestCase(MoodKind.Calm, "mint", "snow")]
    [TestCase(MoodKind.Happy, "candy", "forest")]
    public void TheMoodPicksWithinTheFamily(MoodKind mood, string candy, string nature)
    {
        Assert.That(BubbleChoiceRules.Key(BubbleChoiceRules.Recommend(true, mood)), Is.EqualTo(candy));
        Assert.That(BubbleChoiceRules.Key(BubbleChoiceRules.Recommend(false, mood)), Is.EqualTo(nature));
    }

    [Test]
    public void NoCheckInNoPick()
    {
        Assert.That(BubbleChoiceRules.Recommend(true, null), Is.Null);
        Assert.That(BubbleChoiceRules.Recommend(false, null), Is.Null);
    }

    [Test]
    public void OnlyAFreshPreCheckInCounts()
    {
        var now = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        MoodCheckSample Sample(double minutesAgo, MoodCheckPhase phase = MoodCheckPhase.Pre) => new()
            { phase = phase, mood = MoodKind.Low, timestampUtc = now.AddMinutes(-minutesAgo).ToString("O") };
        Assert.That(BubbleChoiceRules.FreshMood(Sample(5), now), Is.EqualTo(MoodKind.Low));
        Assert.That(BubbleChoiceRules.FreshMood(Sample(59), now), Is.EqualTo(MoodKind.Low));
        Assert.That(BubbleChoiceRules.FreshMood(Sample(120), now), Is.Null, "an old check-in from an earlier visit");
        Assert.That(BubbleChoiceRules.FreshMood(Sample(5, MoodCheckPhase.Post), now), Is.Null);
        Assert.That(BubbleChoiceRules.FreshMood(null, now), Is.Null);
        Assert.That(BubbleChoiceRules.FreshMood(new MoodCheckSample { phase = MoodCheckPhase.Pre, timestampUtc = "soon" }, now), Is.Null);
    }

    [Test]
    public void TheOtherBubbleFloatsUpperRightAndThePickIsCloserAndBigger()
    {
        var hover = new Vector3(0f, 1.2f, .8f);
        var forward = Vector3.forward;
        var other = BubbleChoiceRules.PartnerPosition(hover, forward, false, true);
        Assert.That(other.x, Is.EqualTo(BubbleChoiceRules.PartnerSide).Within(1e-4f), "right of the real bubble");
        Assert.That(other.y, Is.EqualTo(hover.y + BubbleChoiceRules.PartnerLift).Within(1e-4f), "a little higher, above Moodi");
        Assert.That(other.z, Is.GreaterThan(hover.z), "the real bubble is the pick: the other one sits further away");
        var picked = BubbleChoiceRules.PartnerPosition(hover, forward, true, false);
        Assert.That(picked.z, Is.LessThan(hover.z), "the other bubble is the pick: closer");
        var neutral = BubbleChoiceRules.PartnerPosition(hover, forward, false, false);
        Assert.That(neutral.z, Is.EqualTo(hover.z).Within(1e-4f));
        Assert.That(BubbleChoiceRules.EntryRadius(.15f, BubbleWorldData.CandyFolder, BubbleWorldData.CandyFolder), Is.EqualTo(.15f));
        Assert.That(BubbleChoiceRules.EntryRadius(.15f, BubbleWorldData.CandyFolder, BubbleWorldData.NestedCandyFolder), Is.EqualTo(.15f * BubbleChoiceRules.OtherScale).Within(1e-5f));
        Assert.That(BubbleChoiceRules.EntryRadius(.15f, null, BubbleWorldData.NestedCandyFolder), Is.EqualTo(.15f), "no pick: both the same");
    }

    [Test]
    public void FirstLayerShowsHomeLeftAndNextLayerRight()
    {
        var (home, next) = BubbleChoiceRules.Pair(new Vector3(0f, 1f, 1f), Vector3.forward);
        Assert.That(home.x, Is.EqualTo(-BubbleChoiceRules.PairHalf).Within(1e-4f));
        Assert.That(next.x, Is.EqualTo(BubbleChoiceRules.PairHalf).Within(1e-4f));
        Assert.That(Vector3.Distance(home, next), Is.EqualTo(.3f).Within(1e-4f));
        Assert.That(BubbleChoiceRules.PortalRadius * 2f, Is.EqualTo(.22f).Within(1e-4f), "next layer is 22 cm (was 16)");
        Assert.That(BubbleChoiceRules.TagVisible(1f, 9f), Is.True, "just appeared");
        Assert.That(BubbleChoiceRules.TagVisible(8f, 9f), Is.False);
        Assert.That(BubbleChoiceRules.TagVisible(8f, .2f), Is.True, "a fingertip is close");
    }

    [Test]
    public void EveryTagAndTheRingAreInResources()
    {
        foreach (var en in new[] { false, true })
        {
            foreach (var f in Folders)
            {
                AssertSprite(BubbleChoiceRules.WorldLabel(f, en));
                AssertSprite(BubbleChoiceRules.NextLabel(f, en));
            }
            AssertSprite(BubbleChoiceRules.HomeLabel(en));
        }
        AssertSprite(BubbleChoiceRules.RingSprite);
        Assert.That(BubbleChoiceRules.WorldLabel(BubbleWorldData.NestedCandyFolder, false), Does.EndWith("choice_world_mint_zh"));
        Assert.That(BubbleChoiceRules.WorldLabel("Elsewhere/", false), Is.Null);
    }

    static void AssertSprite(string id)
    {
        var sprite = Resources.Load<Sprite>(id);
        Assert.That(sprite, Is.Not.Null, id);
        Assert.That(sprite.pixelsPerUnit, Is.EqualTo(4000f).Within(.01f), id);
    }

    [Test]
    public void TheEntryChoiceShowsTheOtherWorldTagsAndRing()
    {
        var viewerGo = new GameObject("viewer");
        var choice = BubbleEntryChoice.Create();
        try
        {
            var viewer = viewerGo.AddComponent<Camera>();
            viewer.transform.position = new Vector3(0f, 1.5f, 0f);
            var realCentre = new Vector3(0f, 1.2f, .8f);
            var touching = true;   // a hand already resting where the bubble rises
            choice.Show(viewer, BubbleWorldData.CandyFolder, BubbleWorldData.CandyFolder, realCentre - Vector3.up * .35f, realCentre, .15f,
                () => realCentre, () => .15f, (c, r) => touching, Color.white, .85f);
            Assert.That(choice.PartnerFolder, Is.EqualTo(BubbleWorldData.NestedCandyFolder));
            Assert.That(choice.PartnerBubble, Is.Not.Null);
            var inner = choice.PartnerBubble.GetComponentsInChildren<MeshRenderer>().First(r => r.name == "Panorama");
            Assert.That(inner.sharedMaterial.mainTexture, Is.Not.Null, "the other world's panorama inside its bubble");
            Assert.That(choice.PartnerBubble.GetComponentInChildren<Collider>(), Is.Not.Null, "look + pinch works too");

            for (float t = 0f; t <= 1.2f; t += .1f) choice.Step(t, .1f);   // rise, tags fade in
            Assert.That(choice.PartnerRadius, Is.EqualTo(.15f * BubbleChoiceRules.OtherScale).Within(1e-4f));
            Assert.That(choice.PartnerCentre.x, Is.GreaterThan(realCentre.x + .3f), "to the right");
            Assert.That(choice.RealLabel.Renderer.gameObject.activeSelf, Is.True);
            Assert.That(choice.RealLabel.Renderer.sprite.name, Does.StartWith("choice_world_candy_"), "just the world's name");
            Assert.That(choice.PartnerLabel.Renderer.sprite.name, Does.StartWith("choice_world_mint_"));
            Assert.That(choice.GetComponentsInChildren<SpriteRenderer>(true).Count(r => r.name.EndsWith("Label")), Is.EqualTo(2), "one tag per bubble, nothing else");
            Assert.That(choice.Ring.gameObject.activeSelf, Is.True, "ring around the pick");
            Assert.That(Vector3.Distance(choice.Ring.transform.position, realCentre), Is.LessThan(.02f));
            Assert.That(choice.RealLabel.Renderer.bounds.max.y, Is.LessThan(realCentre.y - .15f), "tag under the bubble");

            // Fingertip on the other bubble: a hand that was already there does not count until it has been away once.
            choice.Step(1.3f, .1f);
            Assert.That(choice.PartnerTouched, Is.False);
            touching = false; choice.Step(1.4f, .1f);
            touching = true; choice.Step(1.5f, .1f);
            Assert.That(choice.PartnerTouched, Is.True);

            choice.Dismiss();
            choice.Step(1.6f, .1f);
            Assert.That(choice.Visible, Is.True, "sinking back");
            choice.Step(1.6f + BubbleEntryChoice.SinkSeconds + .1f, .1f);
            Assert.That(choice.Visible, Is.False);
            Assert.That(choice.PartnerBubble, Is.Null);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(choice.gameObject);
            UnityEngine.Object.DestroyImmediate(viewerGo);
        }
    }

    [Test]
    public void WithoutACheckInBothBubblesAreTheSameAndUnringed()
    {
        var viewerGo = new GameObject("viewer");
        var choice = BubbleEntryChoice.Create();
        try
        {
            var viewer = viewerGo.AddComponent<Camera>();
            var real = Vector3.forward + Vector3.up * .3f;
            choice.Show(viewer, BubbleWorldData.NatureFolder, null, Vector3.forward, real, .15f, () => real, () => .15f, (c, r) => false, Color.white, .85f);
            for (float t = 0f; t <= 1.5f; t += .1f) choice.Step(t, .1f);
            Assert.That(choice.PartnerFolder, Is.EqualTo(BubbleWorldData.NestedNatureFolder));
            Assert.That(choice.PartnerLabel.Renderer.sprite.name, Does.StartWith("choice_world_snow_"));
            Assert.That(choice.Ring.gameObject.activeSelf, Is.False, "no check-in: no ring");
            Assert.That(choice.PartnerRadius, Is.EqualTo(.15f), "no pick: same size");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(choice.gameObject);
            UnityEngine.Object.DestroyImmediate(viewerGo);
        }
    }

    [Test]
    public void TheNextLayerIsTheWorldNotChosen()
    {
        var go = new GameObject("bubble");
        try
        {
            var controller = go.AddComponent<BubbleWorldController>();
            typeof(BubbleWorldController).GetField("m_Folder", Private).SetValue(controller, BubbleWorldData.NestedCandyFolder);
            typeof(BubbleWorldController).GetField("m_NatureWorldDepth", Private).SetValue(controller, 0);
            // The next layer is off since the door entry (2026-11-02 b); the old rule still holds when it is switched on.
            typeof(BubbleWorldController).GetField("m_NextLayer", Private).SetValue(controller, true);
            typeof(BubbleWorldController).GetMethod("PrepareNested", Private).Invoke(controller, null);
            var nested = (BubbleWorldController)typeof(BubbleWorldController).GetField("m_Nested", Private).GetValue(controller);
            Assert.That(nested, Is.Not.Null);
            Assert.That(nested.Folder, Is.EqualTo(BubbleWorldData.CandyFolder), "entered 薄荷巧克力 first: the next layer is 棉花糖");
            // The production controller cleans up with Destroy; take the nested one out by hand in EditMode.
            nested.StopAllCoroutines();
            typeof(BubbleWorldController).GetField("m_Nested", Private).SetValue(controller, null);
            UnityEngine.Object.DestroyImmediate(nested.gameObject);
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [Test]
    public void WishesRememberTheLayerTheyWereWrittenIn()
    {
        var root = new GameObject("garden");
        try
        {
            var camera = root.AddComponent<Camera>();
            var garden = root.AddComponent<WishChimeGarden>();
            garden.Initialize(BubbleWorldData.CandyFolder, root.transform, camera, new BubblePlayHands(), null, null, 1);
            Assert.That(garden.Layer, Is.EqualTo(1), "棉花糖 entered as the next layer");
            garden.Initialize(BubbleWorldData.NestedCandyFolder, root.transform, camera, new BubblePlayHands(), null, null, 0);
            Assert.That(garden.Layer, Is.EqualTo(0), "薄荷巧克力 entered first");
            garden.Initialize(BubbleWorldData.NestedNatureFolder, root.transform, camera, new BubblePlayHands(), null);
            Assert.That(garden.Layer, Is.EqualTo(1), "old callers keep the fixed order");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [Test]
    public void TheRoomEntryIsWiredToTheMood()
    {
        var manager = File.ReadAllText("Assets/Scripts/Moodium/CandyWorldManager.cs");
        Assert.That(manager, Does.Contain("BubbleChoiceRules.Recommend(!m_NatureWorldActive, m_EntryMood)"));
        Assert.That(manager, Does.Contain("m_BubbleWorld.ConfigureEntry(entryPick)"));
        var flow = File.ReadAllText("Assets/Scripts/Moodium/MoodiumAppFlowController.cs");
        Assert.That(flow.IndexOf("SetEntryMood(", StringComparison.Ordinal), Is.LessThan(flow.IndexOf("m_CandyWorldManager.StartExperience(world)", StringComparison.Ordinal)));
    }
}
