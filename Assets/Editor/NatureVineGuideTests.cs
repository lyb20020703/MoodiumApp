using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Artngame.TreeGEN.ProceduralIvy;
using Moodium.Characters;
using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Light vine guidance and grow sound (2026-11-03): Moodi says a tip if no vine was planted for a while (touch first,
/// then look + pinch), a soft spot marks a reachable wall or table, and the first vine gets a happy line.
/// Every vine that starts growing plays one of three light sounds.
/// </summary>
public sealed class NatureVineGuideTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    GameObject m_Root, m_GeneratorObject, m_Eye;
    readonly List<GameObject> m_Surfaces = new();

    [TearDown]
    public void TearDown()
    {
        foreach (var surface in m_Surfaces) if (surface != null) Object.DestroyImmediate(surface);
        m_Surfaces.Clear();
        if (m_Root != null) Object.DestroyImmediate(m_Root);
        if (m_Eye != null) Object.DestroyImmediate(m_Eye);
    }

    // ---------- when Moodi speaks ----------

    static string Run(NatureVineGuide guide, float seconds, bool plantable = true, int vines = 0, bool busy = false)
    {
        string said = null;
        for (var t = 0f; t < seconds && said == null; t += .1f) said = guide.Tick(.1f, plantable, vines, busy);
        return said;
    }

    [Test]
    public void TheTouchTipComesAfterTwentyPlantableSecondsThenThePinchTip()
    {
        var guide = new NatureVineGuide();
        Assert.That(Run(guide, NatureVineGuide.TouchDelay - .5f), Is.Null);
        Assert.That(Run(guide, 1f), Is.EqualTo(NatureVineGuide.TouchLine));
        guide.Said(NatureVineGuide.TouchLine);
        Assert.That(Run(guide, NatureVineGuide.PinchDelay - .5f), Is.Null);
        Assert.That(Run(guide, 1f), Is.EqualTo(NatureVineGuide.PinchLine));
        guide.Said(NatureVineGuide.PinchLine);
        Assert.That(Run(guide, 300f), Is.Null, "two tips at most");
    }

    [Test]
    public void TipsWaitForQuietAndOnlyCountPlantableTime()
    {
        var guide = new NatureVineGuide();
        Assert.That(Run(guide, 60f, plantable: false), Is.Null, "inside a door world or before 100% nothing counts");
        Assert.That(Run(guide, 30f, busy: true), Is.Null, "never over Moodi, the narrator or the help card");
        Assert.That(Run(guide, NatureVineGuide.QuietSeconds - .3f), Is.Null, "a few quiet seconds first");
        Assert.That(Run(guide, 1f), Is.EqualTo(NatureVineGuide.TouchLine));
    }

    [Test]
    public void TheFirstVineGetsOneHappyLineAndNoTips()
    {
        var guide = new NatureVineGuide();
        Assert.That(guide.Tick(.1f, true, 1, false), Is.EqualTo(NatureVineGuide.FirstVineLine));
        guide.Said(NatureVineGuide.FirstVineLine);
        Assert.That(Run(guide, 120f, vines: 1), Is.Null, "no tips once the user planted");
        Assert.That(guide.Step, Is.Zero);
    }

    [Test]
    public void TheHappyLineIsDroppedIfMoodiIsBusyTooLong()
    {
        var guide = new NatureVineGuide();
        Assert.That(Run(guide, NatureVineGuide.PraiseWindow + 1f, vines: 1, busy: true), Is.Null);
        Assert.That(Run(guide, 10f, vines: 1), Is.Null, "a late cheer would not match anything");
    }

    [Test]
    public void VineLinesAreMoodiLinesAndOnlyTheTipsTeach()
    {
        foreach (var id in new[] { "V1", "V2", "V3" })
            Assert.That(System.Array.IndexOf(MoodiVoice.AllIds, id), Is.GreaterThanOrEqualTo(0), id);
        Assert.That(MoodiVoice.IsTeaching(NatureVineGuide.TouchLine), Is.True);
        Assert.That(MoodiVoice.IsTeaching(NatureVineGuide.PinchLine), Is.True);
        Assert.That(MoodiVoice.IsTeaching(NatureVineGuide.FirstVineLine), Is.False);
    }

    [Test]
    public void EveryVineLineHasAShortMonoClipInBothLanguages()
    {
        foreach (var language in new[] { "zh", "en" })
        foreach (var id in new[] { "V1", "V2", "V3" })
        {
            var path = MoodiVoice.ClipResourcePath(id, language);
            var clip = Resources.Load<AudioClip>(path);
            Assert.That(clip, Is.Not.Null, path);
            Assert.That(clip.length, Is.InRange(.6f, 6f), path);
            Assert.That(clip.channels, Is.EqualTo(1), path + ": reimport the folder");
        }
    }

    // ---------- grow sound ----------

    [Test]
    public void GrowSoundsExistAreShortAndNeverRepeatBackToBack()
    {
        for (var i = 0; i < NatureVineSound.Clips.Length; i++)
        {
            var clip = Resources.Load<AudioClip>(NatureVineSound.Resource(i));
            Assert.That(clip, Is.Not.Null, NatureVineSound.Resource(i));
            Assert.That(clip.length, Is.InRange(1f, 2.5f), clip.name);
            Assert.That(clip.channels, Is.EqualTo(1), clip.name);
        }
        for (var last = -1; last < NatureVineSound.Clips.Length; last++)
            foreach (var random in new[] { 0.0, .34, .5, .67, .999, 1.0 })
            {
                var pick = NatureVineSound.Pick(last, random);
                Assert.That(pick, Is.InRange(0, NatureVineSound.Clips.Length - 1));
                Assert.That(pick, Is.Not.EqualTo(last));
            }
        Assert.That(NatureVineSound.CanPlay(10f, 10f - NatureVineSound.MinGap * .5f), Is.False, "the completion ring shares sounds");
        Assert.That(NatureVineSound.CanPlay(10f, 10f - NatureVineSound.MinGap), Is.True);
        Assert.That(NatureVineSound.Volume, Is.LessThan(.7f), "quieter than a tree growing in");
    }

    [Test]
    public void EveryRequestedVinePlaysTheSoundAndTheManagerDrivesTheGuide()
    {
        var controller = File.ReadAllText("Assets/Scripts/Moodium/NatureIvySpatialGrowthController.cs");
        var request = controller.Substring(controller.IndexOf("bool RequestIvy(", System.StringComparison.Ordinal));
        request = request.Substring(0, request.IndexOf("List<Collider> CollidersWithin", System.StringComparison.Ordinal));
        Assert.That(request, Does.Contain("PlayGrowSound(hit.point);"));
        var manager = File.ReadAllText("Assets/Scripts/Moodium/CandyWorldManager.cs");
        Assert.That(manager, Does.Contain("UpdateVineGuide();"));
        Assert.That(manager, Does.Contain("m_VineGuide = new NatureVineGuide();"));
        Assert.That(manager, Does.Contain("m_WalkTrailGuide.TrySay(line)"));
    }

    // ---------- guide spot ----------

    [Test]
    public void SpotFadesInBreathesAndFadesOut()
    {
        Assert.That(NatureVineGuideSpot.Alpha(0f, -1f), Is.Zero);
        Assert.That(NatureVineGuideSpot.Alpha(NatureVineGuideSpot.FadeIn, -1f), Is.GreaterThan(.6f));
        for (var t = 0f; t < NatureVineGuideSpot.Seconds; t += .05f)
            Assert.That(NatureVineGuideSpot.Alpha(t, -1f), Is.InRange(0f, 1f));
        Assert.That(NatureVineGuideSpot.Alpha(NatureVineGuideSpot.Seconds, -1f), Is.Zero);
        Assert.That(NatureVineGuideSpot.Alpha(3f, NatureVineGuideSpot.Cancel), Is.Zero, "planting a vine clears it quickly");
        Assert.That(NatureVineGuideSpot.Finished(3f, NatureVineGuideSpot.Cancel), Is.True);
        Assert.That(NatureVineGuideSpot.Finished(3f, -1f), Is.False);
    }

    NatureIvySpatialGrowthController Planter(Vector3 eye, Vector3 forward, out HashSet<Collider> room)
    {
        m_Root = new GameObject("Nature Vine Guide Test Root");
        m_GeneratorObject = new GameObject("Ivy Generator");
        m_GeneratorObject.transform.SetParent(m_Root.transform);
        m_GeneratorObject.AddComponent<MeshFilter>();
        m_GeneratorObject.AddComponent<MeshRenderer>();
        var generator = m_GeneratorObject.AddComponent<IvyGeneratorTREANT>();
        generator.addLeaves = false;
        generator.growIvy = false;
        var controller = m_Root.AddComponent<NatureIvySpatialGrowthController>();
        controller.Configure(generator, 10);
        controller.StartGrowth();
        m_Root.AddComponent<Moodium.Flow.WorldJourneyDirector>().Rescued();
        controller.SetProgress(1f);
        m_Eye = new GameObject("Eye");
        m_Eye.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(forward));
        var camera = m_Eye.AddComponent<Camera>();
        typeof(NatureIvySpatialGrowthController).GetField("m_Camera", Flags).SetValue(controller, camera);
        room = (HashSet<Collider>)typeof(NatureIvySpatialGrowthController).GetField("m_SpatialColliders", Flags).GetValue(controller);
        return controller;
    }

    Collider Surface(Vector3 position, Vector3 size)
    {
        var surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
        surface.layer = 4;
        surface.transform.position = position;
        surface.transform.localScale = size;
        m_Surfaces.Add(surface);
        Physics.SyncTransforms();
        return surface.GetComponent<Collider>();
    }

    [Test]
    public void SpotLandsOnTheWallAheadFacingTheViewer()
    {
        var controller = Planter(new Vector3(0f, 1.4f, 0f), Vector3.forward, out var room);
        room.Add(Surface(new Vector3(0f, 1.4f, 1.05f), new Vector3(3f, 3f, .1f)));   // wall face at z = 1
        Assert.That(controller.ShowGuideSpot(), Is.True);
        var spot = controller.GuideSpot;
        Assert.That(spot, Is.Not.Null);
        Assert.That(spot.transform.position.z, Is.EqualTo(1f - NatureVineGuideSpot.Lift).Within(.002f));
        Assert.That(Vector3.Dot(spot.transform.forward, Vector3.forward), Is.GreaterThan(.99f), "front faces the viewer");
        Assert.That(spot.sprite, Is.Not.Null);
        Assert.That(spot.bounds.size.x, Is.EqualTo(NatureVineGuideSpot.Size).Within(.01f));
        Assert.That(spot.GetComponent<Collider>(), Is.Null, "the spot must never catch a pinch");
    }

    [Test]
    public void TheFloorIsNeverMarkedButATableIs()
    {
        var controller = Planter(new Vector3(0f, 1.6f, 0f), Vector3.forward, out var room);
        room.Add(Surface(new Vector3(0f, -.05f, 1f), new Vector3(4f, .1f, 4f)));     // floor top at y = 0
        Assert.That(controller.ShowGuideSpot(), Is.False);
        Assert.That(controller.GuideSpot, Is.Null);
        room.Add(Surface(new Vector3(0f, .7f, 1.1f), new Vector3(1.2f, .1f, .8f)));   // table top at y = 0.75
        Assert.That(controller.ShowGuideSpot(), Is.True);
        Assert.That(controller.GuideSpot.transform.position.y, Is.EqualTo(.75f + NatureVineGuideSpot.Lift).Within(.002f));
    }

    [Test]
    public void PlantingAVineOrStoppingClearsTheSpot()
    {
        var controller = Planter(new Vector3(0f, 1.4f, 0f), Vector3.forward, out var room);
        room.Add(Surface(new Vector3(0f, 1.4f, 1.05f), new Vector3(3f, 3f, .1f)));
        var update = typeof(NatureIvySpatialGrowthController).GetMethod("UpdateGuideSpot", Flags);
        Assert.That(controller.ShowGuideSpot(), Is.True);
        var start = (float)typeof(NatureIvySpatialGrowthController).GetField("m_SpotStart", Flags).GetValue(controller);
        typeof(NatureIvySpatialGrowthController).GetField("m_UserVines", Flags).SetValue(controller, 1);
        update.Invoke(controller, new object[] { true, start + 1f });
        Assert.That(controller.GuideSpot, Is.Not.Null, "fades out first");
        update.Invoke(controller, new object[] { true, start + 1f + NatureVineGuideSpot.Cancel + .01f });
        Assert.That(controller.GuideSpot, Is.Null);

        typeof(NatureIvySpatialGrowthController).GetField("m_UserVines", Flags).SetValue(controller, 0);
        Assert.That(controller.ShowGuideSpot(), Is.True);
        controller.SetSuspended(true);
        Assert.That(controller.GuideSpot, Is.Null, "walking into a door world removes it at once");
    }
}
