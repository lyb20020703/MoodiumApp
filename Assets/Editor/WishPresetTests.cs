using System.Collections.Generic;
using System.IO;
using System.Linq;
using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;

public sealed class WishPresetTests
{
    static IEnumerable<WishPreset> All => WishPresetLibrary.Document.presets;

    // ---------- data ----------

    [Test]
    public void FivePresetsInBothLanguages()
    {
        Assert.That(WishPresetLibrary.Document.version, Is.EqualTo(1));
        foreach (var id in WishPresetLibrary.Ids)
            foreach (var lang in new[] { "zh", "en" })
                Assert.That(WishPresetLibrary.Find(id, lang), Is.Not.Null, id + "/" + lang);
        Assert.That(All.Count(), Is.EqualTo(10));
        Assert.That(WishPresetLibrary.Find("peace_joy", "zh").text, Is.EqualTo("平安喜乐"));
        Assert.That(WishPresetLibrary.Find("happy", "zh").text, Is.EqualTo("天天开心"));
        Assert.That(WishPresetLibrary.Find("good_vibes", "zh").text, Is.EqualTo("好事发生"));
        Assert.That(WishPresetLibrary.Find("stay_well", "zh").text, Is.EqualTo("身体健康"));
        Assert.That(WishPresetLibrary.Find("let_go", "zh").text, Is.EqualTo("烦恼退散"));
    }

    [Test]
    public void StrokesFitTheHandwritingRules()
    {
        foreach (var p in All)
        {
            Assert.That(p.kinds.Length, Is.EqualTo(p.strokes.Count), p.id);
            Assert.That(p.kinds[0], Is.EqualTo(2), p.id);
            Assert.That(WishChimeStore.CanRelease(p.strokes), Is.True, p.id);
            Assert.That(WishPresetLibrary.PointCount(p.strokes), Is.LessThan(4000 / 4), p.id + ": leaves room for a signature");
            for (int i = 0; i < p.strokes.Count; i++)
            {
                var s = p.strokes[i];
                Assert.That(s.Count, Is.InRange(2, 74), $"{p.id} stroke {i}");   // WishChime splits handwriting at 74
                Assert.That(p.kinds[i], Is.InRange(0, 2));
                foreach (var pt in s)
                {
                    Assert.That(pt.Length, Is.EqualTo(2));
                    Assert.That(pt[0], Is.InRange(0f, 1f)); Assert.That(pt[1], Is.InRange(0f, 1f));
                }
                if (p.kinds[i] == 0)
                {
                    var prev = p.strokes[i - 1][^1];
                    Assert.That(new Vector2(s[0][0] - prev[0], s[0][1] - prev[1]).magnitude, Is.LessThan(1e-3f), "a continued piece starts where the last ended");
                }
            }
        }
    }

    [Test]
    public void ChineseIsFourCharactersTopToBottomAndEnglishIsOneWordPerLine()
    {
        foreach (var p in All)
        {
            int starts = p.kinds.Count(k => k == 2);
            Assert.That(starts, Is.EqualTo(p.lang == "zh" ? p.text.Length : p.text.Split(' ').Length), p.id + "/" + p.lang);
            // Each new character / line begins lower on the paper than the one before (v = 1 is the top).
            float lastTop = 2f;
            for (int i = 0; i < p.strokes.Count; i++)
            {
                if (p.kinds[i] != 2) continue;
                int end = i + 1; while (end < p.strokes.Count && p.kinds[end] != 2) end++;
                float top = p.strokes.Skip(i).Take(end - i).SelectMany(s => s).Max(pt => pt[1]);
                Assert.That(top, Is.LessThan(lastTop), p.id + "/" + p.lang);
                lastTop = top;
            }
            // Clear of the hanging hook (top) and the nature leaf (bottom left).
            Assert.That(p.strokes.SelectMany(s => s).Max(pt => pt[1]), Is.LessThan(.93f));
            Assert.That(p.strokes.SelectMany(s => s).Min(pt => pt[1]), Is.GreaterThan(.08f));
        }
    }

    [Test]
    public void CopiesNeverShareArraysWithTheLibrary()
    {
        var p = WishPresetLibrary.Find("peace_joy", "zh");
        var copy = WishPresetLibrary.CopyStrokes(p, 3);
        Assert.That(copy.Count, Is.EqualTo(3));
        copy[0][0][0] = 99f;
        Assert.That(p.strokes[0][0][0], Is.Not.EqualTo(99f));
        Assert.That(WishPresetLibrary.CopyStrokes(p).Count, Is.EqualTo(p.strokes.Count));
    }

    [Test]
    public void RecordKeepsThePresetAndOldFilesStillLoad()
    {
        var path = Path.Combine(Path.GetTempPath(), "wish_presets_test_" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var strokes = WishPresetLibrary.CopyStrokes(WishPresetLibrary.Find("let_go", "en"));
            var record = new WishChimeRecord { id = "a", world = "candy", folder = "f", createdAt = "now", position = new[] { 0f, 1f, 2f }, strokes = strokes, preset = "let_go" };
            Assert.That(WishChimeStore.Append(path, record), Is.True);
            var loaded = WishChimeStore.Load(path).chimes.Single();
            Assert.That(loaded.preset, Is.EqualTo("let_go"));
            Assert.That(loaded.strokes.Count, Is.EqualTo(strokes.Count));
            // A file written before presets existed has no "preset" field.
            File.WriteAllText(path, "{\"version\":1,\"chimes\":[{\"id\":\"b\",\"world\":\"nature\",\"folder\":\"f\",\"createdAt\":\"x\",\"layer\":0,\"position\":[0,1,2],\"yaw\":0,\"strokes\":[[[0.1,0.1],[0.2,0.2]]]}]}");
            var old = WishChimeStore.Load(path).chimes.Single();
            Assert.That(old.preset, Is.Null);
        }
        finally { if (File.Exists(path)) File.Delete(path); if (File.Exists(path + ".bak")) File.Delete(path + ".bak"); }
    }

    // ---------- writing animation ----------

    [Test]
    public void EveryPresetWritesInTwoToFourSeconds()
    {
        foreach (var p in All)
        {
            var t = new WishWritingTimeline(p.strokes, p.kinds);
            Assert.That(t.Total, Is.InRange(WishWritingTimeline.MinSeconds - .01f, WishWritingTimeline.MaxSeconds + .01f), p.id + "/" + p.lang);
        }
    }

    [Test]
    public void SamplingDrawsForwardAndEndsWithEverything()
    {
        foreach (var p in All)
        {
            var t = new WishWritingTimeline(p.strokes, p.kinds);
            var start = t.Sample(0f);
            Assert.That(start.Done, Is.Zero);
            int lastDone = 0; float lastProgress = -1f;
            for (float time = 0; time < t.Total; time += 1f / 90f)
            {
                var f = t.Sample(time);
                Assert.That(f.Done, Is.GreaterThanOrEqualTo(lastDone), p.id);
                if (f.Done != lastDone) lastProgress = -1f;
                lastDone = f.Done;
                if (!f.PenMoving) continue;
                var stroke = p.strokes[f.Done];
                Assert.That(f.Partial.Count, Is.InRange(1, stroke.Count - 1));
                for (int i = 0; i < f.Partial.Count; i++) Assert.That(f.Partial[i], Is.SameAs(stroke[i]), "partial is a prefix of the stroke");
                // The tip lies on the segment after the partial points.
                var a = new Vector2(stroke[f.Partial.Count - 1][0], stroke[f.Partial.Count - 1][1]);
                var b = new Vector2(stroke[f.Partial.Count][0], stroke[f.Partial.Count][1]);
                var along = Vector2.Dot(f.Tip.Value - a, b - a) / Mathf.Max(1e-9f, (b - a).sqrMagnitude);
                Assert.That(along, Is.InRange(-1e-3f, 1.001f));
                var progress = f.Partial.Count + along;
                Assert.That(progress, Is.GreaterThanOrEqualTo(lastProgress - 1e-4f), "pen never moves backwards");
                lastProgress = progress;
            }
            var end = t.Sample(t.Total + .01f);
            Assert.That(end.Done, Is.EqualTo(p.strokes.Count));
            Assert.That(end.PenMoving, Is.False);
        }
    }

    [Test]
    public void PausesFollowTheStrokeKinds()
    {
        var p = WishPresetLibrary.Find("stay_well", "zh");
        var t = new WishWritingTimeline(p.strokes, p.kinds);
        Assert.That(t.Groups[0].Start, Is.Zero);
        for (int i = 1; i < t.Groups.Count; i++)
        {
            var g = t.Groups[i];
            var expected = (p.kinds[g.First] == 2 ? WishWritingTimeline.GlyphGap : WishWritingTimeline.StrokeGap) * t.Scale;
            Assert.That(g.Start - (t.Groups[i - 1].Start + t.Groups[i - 1].Duration), Is.EqualTo(expected).Within(1e-4f));
            for (int k = g.First + 1; k < g.First + g.Count; k++) Assert.That(p.kinds[k], Is.Zero, "continued pieces share one pen movement");
        }
    }

    [Test]
    public void EaseAlwaysMovesForward()
    {
        Assert.That(WishWritingTimeline.EaseAt(0f), Is.EqualTo(0f).Within(1e-6f));
        Assert.That(WishWritingTimeline.EaseAt(1f), Is.EqualTo(1f).Within(1e-6f));
        float last = 0f;
        for (int i = 1; i <= 200; i++)
        {
            var v = WishWritingTimeline.EaseAt(i / 200f);
            Assert.That(v, Is.GreaterThan(last));
            last = v;
        }
        Assert.That(WishWritingTimeline.EaseAt(.1f), Is.LessThan(.1f), "slow start");
    }

    // ---------- chips and layout ----------

    [Test]
    public void ChipStatePriority()
    {
        Assert.That(WishPresetLibrary.ChipSuffix("happy", true, true, true, true), Is.EqualTo("_pressed"));
        Assert.That(WishPresetLibrary.ChipSuffix("happy", false, true, true, true), Is.EqualTo("_selected_candy"));
        Assert.That(WishPresetLibrary.ChipSuffix("happy", false, true, false, false), Is.EqualTo("_selected_nature"));
        Assert.That(WishPresetLibrary.ChipSuffix("happy", false, false, true, true), Is.EqualTo("_gaze"));
        Assert.That(WishPresetLibrary.ChipSuffix(WishPresetLibrary.WriteOwnId, false, true, false, true), Is.EqualTo("_idle"));
    }

    [Test]
    public void AllChipSpritesImportAtTheRightSize()
    {
        int count = 0;
        foreach (var lang in new[] { "zh", "en" })
        {
            foreach (var id in WishPresetLibrary.Ids)
                foreach (var state in new[] { "idle", "gaze", "pressed", "selected_candy", "selected_nature" })
                { AssertChip(id, lang, state); count++; }
            foreach (var state in new[] { "idle", "gaze", "pressed" })
            { AssertChip(WishPresetLibrary.WriteOwnId, lang, state); count++; }
        }
        Assert.That(count, Is.EqualTo(56));
    }

    static void AssertChip(string id, string lang, string state)
    {
        var sprite = Resources.Load<Sprite>(WishPresetLibrary.ChipPrefix(id, lang) + "_" + state);
        Assert.That(sprite, Is.Not.Null, id + "_" + lang + "_" + state);
        // 948 x 438 px at 7333.33 px/m = 0.1293 x 0.0597 m; the capsule inside is 0.0933 x 0.0237 m.
        Assert.That(sprite.bounds.size.x, Is.EqualTo(.1293f).Within(.002f));
        Assert.That(sprite.bounds.size.y, Is.EqualTo(.0597f).Within(.002f));
    }

    [Test]
    public void ChipColumnSitsBesideThePaperWithoutOverlaps()
    {
        const float stripHalfWidth = .085f / 2f, stripHalfHeight = .275f / 2f;
        Assert.That(-WishPresetPanel.ColumnX - WishPresetPanel.ChipWidth / 2f - stripHalfWidth, Is.GreaterThanOrEqualTo(.025f), "gap to the paper");
        for (int i = 1; i < WishPresetPanel.ChipCount; i++)
        {
            var gap = WishPresetPanel.ChipY(i - 1) - WishPresetPanel.ChipY(i) - (WishPresetPanel.ChipHeight + WishPresetPanel.ChipPad);
            Assert.That(gap, Is.GreaterThan(0f), "pressable areas must not overlap");
        }
        Assert.That(WishPresetPanel.ChipY(0), Is.EqualTo(-WishPresetPanel.ChipY(WishPresetPanel.ChipCount - 1)).Within(1e-5f), "centred on the paper");
        Assert.That(WishPresetPanel.ChipY(0) + WishPresetPanel.ChipHeight / 2f, Is.LessThan(stripHalfHeight));
        // Release over Rewrite on the right: their pressable capsules (height + 1 cm) do not touch.
        Assert.That(2f * WishPresetPanel.ButtonY - (WishActionButton.Height + .01f), Is.GreaterThan(.005f));
    }

    [TestCase("Candy")]
    [TestCase("Nature")]
    public void PresetInkIsThinner(string world)
    {
        var root = new GameObject("paper");
        try
        {
            var go = new GameObject("ink"); var ink = go.AddComponent<WishInkMesh>();
            var frame = Resources.Load<WishPaperFrame>("MoodiumBubblePlay/PaperFrame_" + world);
            ink.Initialize(root.transform, frame, world == "Candy");
            var line = new List<List<float[]>> { new() { new[] { .2f, .5f }, new[] { .8f, .5f } } };
            float Width() { ink.Completed(line); var m = go.transform.Find("Completed Ink").GetComponent<MeshFilter>().sharedMesh; var v = m.vertices; return Vector3.Distance(v[0], v[1]); }
            var hand = Width();
            ink.SetWidth(WishPresetLibrary.InkWidth, WishPresetLibrary.GlowWidth);
            Assert.That(Width(), Is.EqualTo(hand * WishPresetLibrary.InkWidth).Within(hand * .05f));
            ink.SetWidth(1, 1);
            Assert.That(Width(), Is.EqualTo(hand).Within(hand * .02f));
        }
        finally { Object.DestroyImmediate(root); }
    }
}
