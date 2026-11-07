using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Moodium.Flow;
using Moodium.NatureWorld;
using NUnit.Framework;
using Unity.PolySpatial;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Wish chime × (put away), stale sorting entries, and the first-entry shell warm-up (2026-11-01).</summary>
public sealed class BubbleComfort1101Tests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    static object Read(object o, string name) => o.GetType().GetField(name, Flags).GetValue(o);
    static void Set(object o, string name, object value) => o.GetType().GetField(name, Flags).SetValue(o, value);
    static void Call(object o, string name) => o.GetType().GetMethod(name, Flags).Invoke(o, null);

    sealed class Fixture : IDisposable
    {
        public readonly GameObject Root; public readonly WishChime Chime; public readonly WishChimeGarden Garden;
        public readonly string DirectoryPath; public readonly Vector3 HangingAt;
        public Fixture()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "wish-putaway-" + Guid.NewGuid().ToString("N"));
            Root = new GameObject("Put-away fixture"); var camera = Root.AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 1.5f, 0);
            Garden = Root.AddComponent<WishChimeGarden>();
            Garden.Initialize(BubbleWorldData.CandyFolder, Root.transform, camera, new BubblePlayHands(), null, Path.Combine(DirectoryPath, "wish_chimes.json"));
            Set(Garden, "taught", true);
            var go = new GameObject("Wish"); go.transform.SetParent(Root.transform, false); go.transform.position = new Vector3(0, 1.25f, .55f);
            Chime = go.AddComponent<WishChime>();
            Chime.Initialize(Garden, new WishChimeRecord { id = "fixture", position = new[] { 0f, 1.25f, .55f } });
            go.transform.position = new Vector3(0, 1.25f, .55f);
            Set(Chime, "<State>k__BackingField", WishChime.Stage.Blank);
            HangingAt = go.transform.position;
            Chime.BeginWrite();
        }
        public void Write() => ((List<List<float[]>>)Read(Chime, "strokes")).Add(new List<float[]> { new[] { .3f, .4f }, new[] { .6f, .45f } });
        public void Dispose() { Object.DestroyImmediate(Root); if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true); }
    }

    // ---------- 3. × while writing ----------

    [Test]
    public void WritingHasACloseButtonBesidesReleaseAndRewrite()
    {
        using var f = new Fixture();
        var close = f.Chime.GetComponentsInChildren<SpriteRenderer>(true).Single(r => r.name == "Put Away Glass");
        Assert.That(close.sprite.name, Is.EqualTo("settings_close"), "same glass × as the settings panel");
        Assert.That(close.GetComponent<MoodiumSpatialButton>(), Is.Not.Null);
        Assert.That(close.GetComponent<BoxCollider>(), Is.Not.Null);
        // Its pressable square stays clear of Release's (top of the right column).
        var closeBottom = WishChime.CloseOffset.y - WishChime.CloseSize / 2f;
        var releaseTop = Moodium.NatureWorld.WishPresetPanel.ButtonY + (WishActionButton.Height + .01f) / 2f;
        Assert.That(closeBottom, Is.GreaterThan(releaseTop));
        Assert.That(WishChime.CloseOffset.x, Is.InRange(WishActionButton.Offset - WishActionButton.Width / 2f, WishActionButton.Offset + WishActionButton.Width / 2f));
    }

    [Test]
    public void PutAwayKeepsTheInkHangsTheChimeBackAndEndsWriting()
    {
        using var f = new Fixture(); f.Write();
        var stripScale = (Vector3)Read(f.Chime, "stripScale");
        Assert.That(f.Garden.IsWriting, Is.True);
        f.Chime.PutAway();
        Assert.That(f.Chime.State, Is.EqualTo(WishChime.Stage.Blank));
        Assert.That(f.Garden.IsWriting, Is.False, "writing focus ends, so the follow bubbles can unlock");
        Assert.That(f.Garden.TryKeepOut(out var keep), Is.True); Assert.That(Vector3.Distance(keep, f.HangingAt), Is.LessThan(.001f), "still the garden's blank chime");
        Assert.That(Vector3.Distance(f.Chime.transform.position, f.HangingAt), Is.LessThan(.0001f), "back where it hung, not in front of the eyes");
        Assert.That(f.Chime.Strip.localScale, Is.EqualTo(stripScale), "paper back to its hanging size");
        Assert.That(f.Chime.HasInk, Is.True, "nothing written is lost");
        Assert.That(f.Chime.transform.Find("Wish Actions"), Is.Null, "buttons and chips are gone");
    }

    [Test]
    public void ReopeningWaitsAMomentThenRestoresTheButtonsWithTheSameInk()
    {
        using var f = new Fixture(); f.Write(); f.Chime.PutAway();
        f.Chime.BeginWrite();
        Assert.That(f.Chime.State, Is.EqualTo(WishChime.Stage.Blank), "the pinch that pressed × must not reopen it");
        Set(f.Chime, "putAwayAt", Time.time - WishChime.PutAwayReopenSeconds - .01f);
        f.Chime.BeginWrite();
        Assert.That(f.Chime.State, Is.EqualTo(WishChime.Stage.Writing));
        Assert.That(((WishActionButton)Read(f.Chime, "releaseButton")).Interactable, Is.True, "the kept ink can be released");
        Assert.That(f.Chime.GetComponentsInChildren<SpriteRenderer>(true).Count(r => r.name == "Put Away Glass"), Is.EqualTo(1));
    }

    [Test]
    public void PutAwayDuringAPresetFinishesItAndKeepsItsId()
    {
        using var f = new Fixture();
        f.Chime.GetType().GetMethod("PickPreset", Flags).Invoke(f.Chime, new object[] { "happy" });
        Assert.That(f.Chime.PresetWriting, Is.True);
        f.Chime.PutAway();
        Assert.That(f.Chime.PresetWriting, Is.False); Assert.That(f.Chime.HasInk, Is.True); Assert.That(f.Chime.PresetId, Is.EqualTo("happy"));
    }

    // ---------- 2. stale sorting entries ----------

    [Test]
    public void PruneSortingDropsDestroyedAndLeavingEntriesOnly()
    {
        var root = new GameObject("Group");
        try
        {
            var group = root.AddComponent<VisionOSSortingGroup>();
            GameObject Child(string name, Transform parent) { var g = new GameObject(name); g.transform.SetParent(parent, false); group.Renderers.Add(new VisionOSSortingGroup.RendererSorting { Renderer = g, Order = 5 }); return g; }
            var keep = Child("keep", root.transform);
            var gone = Child("gone", root.transform);
            var leaving = new GameObject("leaving"); leaving.transform.SetParent(root.transform, false);
            Child("leaving child", leaving.transform);
            Object.DestroyImmediate(gone);
            Assert.That(BubblePlayVisuals.PruneSorting(group, leaving.transform), Is.EqualTo(2));
            Assert.That(group.Renderers.Select(e => e.Renderer), Is.EquivalentTo(new[] { keep }));
            Assert.That(BubblePlayVisuals.PruneSorting(null), Is.Zero);
        }
        finally { Object.DestroyImmediate(root); }
    }

    [Test]
    public void ChimeDestroyersTakeTheirEntriesOut()
    {
        var garden = File.ReadAllText("Assets/Scripts/Moodium/BubbleWorld/Play/WishChimeGarden.cs");
        Assert.That(garden, Does.Contain("PruneSorting(sorting,c.transform)"), "Leave");
        var chime = File.ReadAllText("Assets/Scripts/Moodium/BubbleWorld/Play/WishChime.cs");
        Assert.That(chime, Does.Contain("PruneSorting(Sorting,model.transform)"), "lite → full rebuild");
        Assert.That(chime, Does.Contain("SortingChanged(sorting)"), "new wish UI is pushed to PolySpatial");
        var world = File.ReadAllText("Assets/Scripts/Moodium/BubbleWorld/BubbleWorldController.cs");
        Assert.That(world, Does.Contain("PruneSorting(m_WorldSorting,m_HomeHoldRing.transform)"));
    }

    // ---------- 4. first-entry warm-up ----------

    sealed class World : IDisposable
    {
        public readonly GameObject Go; public readonly BubbleWorldController C; public readonly List<GameObject> Layers;
        public World(int layers, int mini)
        {
            Go = new GameObject("Warm-up world"); C = Go.AddComponent<BubbleWorldController>();
            Layers = (List<GameObject>)Read(C, "m_Layers");
            for (int i = 0; i < layers; i++) { var l = new GameObject("Layer " + i); l.transform.SetParent(Go.transform, false); l.SetActive(i < mini); Layers.Add(l); }
            Set(C, "m_MiniLayerCount", mini); Set(C, "m_AllLayersReady", true);
        }
        public void Phase(BubbleWorldController.Phase p) => Set(C, "m_Phase", p);
        public void Step(int frames = 1) { for (int i = 0; i < frames; i++) Call(C, "StepOuterWarmup"); }
        public void Dispose() => Object.DestroyImmediate(Go);
    }

    [Test]
    public void WarmupOnlyWhileTheBubbleWaits()
    {
        Assert.That(BubbleWorldController.WarmupWindow(BubbleWorldController.Phase.Floating, true), Is.True);
        Assert.That(BubbleWorldController.WarmupWindow(BubbleWorldController.Phase.Portal, true), Is.True);
        Assert.That(BubbleWorldController.WarmupWindow(BubbleWorldController.Phase.Floating, false), Is.False, "meshes still loading");
        foreach (var p in new[] { BubbleWorldController.Phase.Armed, BubbleWorldController.Phase.Approach, BubbleWorldController.Phase.Reveal, BubbleWorldController.Phase.Inside })
            Assert.That(BubbleWorldController.WarmupWindow(p, true), Is.False, p.ToString());
    }

    [Test]
    public void EachOuterShellIsShownCollapsedForTwoFramesThenHiddenAtFullSize()
    {
        using var w = new World(6, 2); w.Phase(BubbleWorldController.Phase.Floating);
        var seen = new HashSet<int>();
        for (int frame = 0; frame < 60 && !w.C.OuterLayersWarm; frame++)
        {
            w.Step();
            for (int i = 0; i < w.Layers.Count; i++)
            {
                if (i < 2) { Assert.That(w.Layers[i].activeSelf && w.Layers[i].transform.localScale == Vector3.one, Is.True, "mini shells untouched"); continue; }
                if (!w.Layers[i].activeSelf) { Assert.That(w.Layers[i].transform.localScale, Is.EqualTo(Vector3.one)); continue; }
                Assert.That(w.Layers[i].transform.localScale.x, Is.EqualTo(BubbleWorldController.WarmCollapsedScale), "collapsed to a point while shown");
                seen.Add(i);
            }
            Assert.That(w.Layers.Skip(2).Count(l => l.activeSelf), Is.LessThanOrEqualTo(1), "one shell at a time");
        }
        Assert.That(w.C.OuterLayersWarm, Is.True);
        Assert.That(seen, Is.EquivalentTo(new[] { 2, 3, 4, 5 }));
        Assert.That(w.Layers.Skip(2).All(l => !l.activeSelf), Is.True, "all hidden again for the reveal");
    }

    [Test]
    public void TouchingMidWayPutsTheShellBackAndRetriesItLater()
    {
        using var w = new World(4, 1); w.Phase(BubbleWorldController.Phase.Floating);
        w.Step();
        Assert.That(w.Layers[1].activeSelf, Is.True);
        w.Phase(BubbleWorldController.Phase.Approach); w.Step();
        Assert.That(w.Layers[1].activeSelf, Is.False); Assert.That(w.Layers[1].transform.localScale, Is.EqualTo(Vector3.one));
        Assert.That(w.C.OuterLayersWarm, Is.False);
        w.Phase(BubbleWorldController.Phase.Floating); w.Step(60);
        Assert.That(w.C.OuterLayersWarm, Is.True, "finished on the next wait");
    }
}
