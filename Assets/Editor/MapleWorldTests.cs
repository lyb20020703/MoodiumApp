using System.IO;
using System.Linq;
using Moodium.Characters;
using Moodium.MoodCheck;
using Moodium.NatureWorld;
using Moodium.WishJournal;
using NUnit.Framework;
using UnityEngine;
using L = Moodium.WishJournal.WishJournalLayout;

/// <summary>
/// 枫林世界 (2026-11-03): the third Nature world. The door's arrows go through the seasons (森林 → 枫林 → 雪原), the world
/// has its own name sprites, Moodi's spoken welcome (W3n), falling maple leaves to catch with their own sounds, and its
/// name in the wish journal. Candy and Nature still never mix.
/// </summary>
public sealed class MapleWorldTests
{
    const string Folder = BubbleWorldData.MapleNatureFolder;

    [Test]
    public void TheDoorGoesThroughTheSeasonsAndNeverReachesCandy()
    {
        var nature = new[] { BubbleWorldData.NatureFolder, BubbleWorldData.MapleNatureFolder, BubbleWorldData.NestedNatureFolder };
        foreach (var world in nature) Assert.That(DoorSwitchRules.Family(world), Is.EqualTo(nature), world);
        Assert.That(DoorSwitchRules.Next(nature, Folder, 1), Is.EqualTo(BubbleWorldData.NestedNatureFolder), "autumn → winter");
        Assert.That(DoorSwitchRules.Next(nature, Folder, -1), Is.EqualTo(BubbleWorldData.NatureFolder), "autumn → summer");
        Assert.That(DoorSwitchRules.Next(nature, BubbleWorldData.NestedNatureFolder, 1), Is.EqualTo(BubbleWorldData.NatureFolder), "and round again");
        Assert.That(DoorSwitchRules.Family(BubbleWorldData.CandyFolder), Does.Not.Contain(Folder));
        Assert.That(BubbleWorldData.IsCandy(Folder), Is.False);
        Assert.That(BubbleWorldData.IsNature(Folder), Is.True);
    }

    [Test]
    public void TheWorldHasItsKeyNameSpritesAndAMoodPick()
    {
        Assert.That(BubbleChoiceRules.Key(Folder), Is.EqualTo("maple"));
        foreach (var lang in new[] { "zh", "en" })
        foreach (var kind in new[] { "world", "next" })
        {
            var path = BubbleChoiceRules.SpriteFolder + $"choice_{kind}_maple_{lang}";
            var sprite = Resources.Load<Sprite>(path);
            Assert.That(sprite, Is.Not.Null, path);
            Assert.That(sprite.pixelsPerUnit, Is.EqualTo(4000f), path + ": same scale as the other names");
        }
        Assert.That(BubbleChoiceRules.Recommend(false, MoodKind.Low), Is.EqualTo(Folder), "low → warm autumn leaves to catch and let go");
        Assert.That(BubbleChoiceRules.Recommend(true, MoodKind.Low), Is.EqualTo(BubbleWorldData.CandyFolder), "candy unchanged");
        Assert.That(BubbleChoiceRules.Recommend(false, MoodKind.Happy), Is.EqualTo(BubbleWorldData.NatureFolder));
        Assert.That(BubbleChoiceRules.Recommend(false, MoodKind.Calm), Is.EqualTo(BubbleWorldData.NestedNatureFolder));
    }

    [Test]
    public void TheWorldDataIsCompleteAndLevelled()
    {
        var bytes = File.ReadAllBytes("Assets/Resources/" + Folder + "points.bytes");
        Assert.That(BubbleWorldData.TryParse(bytes, out var cloud), Is.True);
        Assert.That(cloud.Count, Is.EqualTo(500000));
        Assert.That(cloud.EyeHeight, Is.EqualTo(.313f).Within(1e-5f));
        Assert.That(cloud.MaxRadius, Is.LessThan(BubbleWorldController.SkyMaxRadius), "every point inside the sky");
        Assert.That(BubbleWorldController.HasData(Folder), Is.True);
        Assert.That(Resources.Load<Texture2D>(BubbleWorldData.RoundColorsResource(Folder)), Is.Not.Null);
        Assert.That(Resources.Load<Texture2D>(BubbleWorldData.PanoramaResource(Folder)), Is.Not.Null);
        Assert.That(Resources.Load<Texture2D>(BubbleWorldData.SoftPanoramaResource(Folder)), Is.Not.Null);
        var frame = File.ReadAllText("Assets/Resources/" + Folder + "frame.json");
        Assert.That(frame, Does.Contain("\"name\": \"MapleNature\""));
        Assert.That(frame, Does.Contain("levels the far horizon"), "no source panorama: how up was found is recorded");
        // Upright: straight under the viewer (within 0.5 m) is ground, below the eye. Not wider: the capture point stands on
        // a ~17° slope, so 2 m uphill the ground is already above the eye.
        int below = 0, above = 0;
        for (var i = 0; i < cloud.Count; i++)
        {
            var p = cloud.Positions[i];
            if (new Vector2(p.x, p.z).magnitude > .5f) continue;
            if (p.y < 0f) below++; else above++;
        }
        Assert.That(below, Is.GreaterThan(1000));
        Assert.That(above * 20, Is.LessThan(below), "the ground is under the feet");
    }

    [Test]
    public void MoodiWelcomesYouInBothLanguages()
    {
        Assert.That(BubbleWorldNarrator.IntroId(Folder), Is.EqualTo("W3n"));
        Assert.That(BubbleWorldNarrator.AllIds, Does.Contain("W3n"));
        foreach (var lang in new[] { "zh", "en" })
        {
            var clip = Resources.Load<AudioClip>(MoodiVoice.ClipResourcePath("W3n", lang));
            Assert.That(clip, Is.Not.Null, lang);
            Assert.That(clip.length, Is.InRange(10f, 35f), lang);
        }
    }

    [Test]
    public void LeavesFallToBeCaughtOnlyInTheMapleWorld()
    {
        Assert.That(BubbleWorldPlay.CatchKindFor(Folder), Is.EqualTo(CatchKind.Maple));
        Assert.That(BubbleWorldPlay.CatchKindFor(BubbleWorldData.NestedNatureFolder), Is.EqualTo(CatchKind.Snow));
        Assert.That(BubbleWorldPlay.CatchKindFor(BubbleWorldData.NestedCandyFolder), Is.EqualTo(CatchKind.Candy));
        Assert.That(BubbleWorldPlay.CatchKindFor(BubbleWorldData.NatureFolder), Is.Null);
        Assert.That(BubbleWorldPlay.CatchKindFor(BubbleWorldData.CandyFolder), Is.Null);
        for (var v = 0; v < 3; v++)
        {
            Assert.That(BubbleCatchFall.CatchClip(CatchKind.Maple, v), Is.EqualTo("catch_maple_" + v));
            var clip = BubblePlayVisuals.Clip(BubbleCatchFall.CatchClip(CatchKind.Maple, v));
            Assert.That(clip, Is.Not.Null, "catch_maple_" + v);
            Assert.That(clip.length, Is.EqualTo(.7f).Within(.02f), "as short as the other catch sounds");
        }
        Assert.That(BubbleCatchFall.CatchClip(CatchKind.Snow, 1), Is.EqualTo("catch_snow_1"), "snow unchanged");
        Assert.That(BubbleCatchFall.CatchClip(CatchKind.Candy, 2), Is.EqualTo("catch_choco_2"), "candy unchanged");
    }

    [Test]
    public void ALeafSwingsAsItFallsRestsOnThePalmThenDriftsAway()
    {
        for (var t = 0f; t < 5f; t += .1f)
        {
            var (offset, tilt) = MapleLeafFall.Swing(t, .3f);
            Assert.That(Mathf.Abs(offset), Is.LessThanOrEqualTo(MapleLeafFall.SwingMetres + 1e-5f));
            Assert.That(Mathf.Abs(tilt), Is.LessThanOrEqualTo(MapleLeafFall.Tilt + 1e-3f));
            Assert.That(offset * tilt, Is.LessThanOrEqualTo(1e-6f), "it tilts into the swing");
        }
        var away = new Vector3(.3f, -.2f, .4f);
        var (start, full) = MapleLeafFall.Drift(0f, away);
        Assert.That(start.magnitude, Is.LessThan(1e-5f));
        Assert.That(full, Is.EqualTo(1f));
        var (end, gone) = MapleLeafFall.Drift(MapleLeafFall.DriftSeconds, away);
        Assert.That(end.y, Is.EqualTo(MapleLeafFall.DriftUp).Within(1e-4f), "lifted by the wind");
        Assert.That(Vector3.Dot(new Vector3(end.x, 0f, end.z).normalized, new Vector3(away.x, 0f, away.z).normalized), Is.GreaterThan(.999f), "away from the viewer");
        Assert.That(gone, Is.EqualTo(0f).Within(1e-5f), "and gone");
        Assert.That(MapleLeafFall.Gone(MapleLeafFall.RestSeconds + MapleLeafFall.DriftSeconds), Is.True);
        Assert.That(MapleLeafFall.Gone(MapleLeafFall.RestSeconds), Is.False);
        Assert.That(MapleLeafFall.MaxSpeed, Is.LessThan(.12f + 1e-6f), "slower than snow (0.12–0.22 m/s)");
    }

    [Test]
    public void TheLeavesAreTexturedQuadsWithoutColliders()
    {
        var host = new GameObject("Maple Catch");
        var camera = new GameObject("Eye").AddComponent<Camera>();
        try
        {
            var fall = host.AddComponent<BubbleCatchFall>();
            fall.Initialize(new BubblePlayHands(), camera, CatchKind.Maple);
            Assert.That(fall.Kind, Is.EqualTo(CatchKind.Maple));
            Assert.That(fall.Count, Is.EqualTo(MapleLeafFall.LeafCount));
            var leaves = host.GetComponentsInChildren<MeshRenderer>(true);
            Assert.That(leaves.Length, Is.EqualTo(MapleLeafFall.LeafCount));
            Assert.That(leaves.Select(r => r.sharedMaterial.mainTexture != null ? r.sharedMaterial.mainTexture.name : null).Distinct().Count(), Is.EqualTo(2), "red and orange");
            Assert.That(host.GetComponentsInChildren<Collider>(true), Is.Empty, "a leaf never takes a pinch");
            Assert.That(leaves.All(r => !r.gameObject.activeSelf), Is.True, "nothing falls until the world is entered");
        }
        finally { Object.DestroyImmediate(host); Object.DestroyImmediate(camera.gameObject); }
    }

    [Test]
    public void TheJournalNamesTheMapleWorld()
    {
        var record = new WishChimeRecord { folder = Folder, world = "nature", layer = 1 };
        Assert.That(WishJournalData.Place(record), Is.EqualTo(("nature", true)));
        Assert.That(WishJournalData.IsMaple(record), Is.True);
        Assert.That(WishJournalData.IsMaple(new WishChimeRecord { folder = BubbleWorldData.NestedNatureFolder }), Is.False);
        Assert.That(L.WorldName("nature", true, false, true), Is.EqualTo("枫林世界"));
        Assert.That(L.WorldName("nature", true, true, true), Is.EqualTo("Maple World"));
        Assert.That(L.ShortWorldName("nature", true, true, true), Is.EqualTo("Maple"));
        Assert.That(L.WorldName("nature", true, false), Is.EqualTo("雪原世界"), "snow unchanged");
    }
}
