using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Moodium.MoodCheck;
using Moodium.NatureWorld;
using Moodium.WishJournal;
using NUnit.Framework;
using TMPro;
using Unity.PolySpatial;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using L = Moodium.WishJournal.WishJournalLayout;

public sealed class WishJournalDataTests
{
    static readonly TimeZoneInfo Beijing = TimeZoneInfo.CreateCustomTimeZone("UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");
    static readonly TimeZoneInfo NewYork = TimeZoneInfo.CreateCustomTimeZone("UTC-5", TimeSpan.FromHours(-5), "UTC-5", "UTC-5");

    static List<List<float[]>> Ink() => new() { new() { new[] { .3f, .6f }, new[] { .6f, .62f } } };
    static WishChimeRecord Wish(string createdAt, string folder = BubbleWorldData.CandyFolder, string world = "candy", string preset = null) =>
        new() { id = Guid.NewGuid().ToString("N"), createdAt = createdAt, folder = folder, world = world, position = new[] { 0f, 0f, 0f }, strokes = Ink(), preset = preset };
    static WishChimeDocument Doc(params WishChimeRecord[] r) => new() { chimes = r.ToList() };
    static string Mood(string utc, string mood, string phase = "pre", string visit = "") =>
        $"{{\"sessionId\":\"s\",\"visitId\":\"{visit}\",\"phase\":\"{phase}\",\"mood\":\"{mood}\",\"timestampUtc\":\"{utc}\"}}";
    static string Visit(string id, string world, string kind, string utc, float seconds = 0, int pinches = 0) =>
        WishJournalVisitLog.Build(id, world, kind, DateTimeOffset.Parse(utc).UtcDateTime, seconds, pinches);

    [Test]
    public void DaysAreLocalDates()
    {
        // 01:10 on Nov 2 in Beijing is still Nov 1 (12:10) in New York.
        var doc = Doc(Wish("2026-11-02T01:10:00+08:00"));
        Assert.That(WishJournalData.Build(doc, null, null, Beijing).Single().Date, Is.EqualTo(new DateTime(2026, 11, 2)));
        var ny = WishJournalData.Build(doc, null, null, NewYork).Single();
        Assert.That(ny.Date, Is.EqualTo(new DateTime(2026, 11, 1)));
        Assert.That(L.Time(ny.Entries[0].Time), Is.EqualTo("12:10"));
    }

    [Test]
    public void WishInsideAVisitGetsItsWorldTimeAndMoods()
    {
        // Beijing: check-in 20:05 (tense), enter candy 20:12, leave 20:38 after 26 min / 148 pinches, wish 20:30.
        var days = WishJournalData.Build(Doc(Wish("2026-11-01T20:30:00+08:00")),
            new[] { Mood("2026-11-01T12:05:00Z", "tense"), Mood("2026-11-01T12:39:00Z", "calm", "post", "v1") },
            new[] { Visit("v1", "candy", WishJournalVisitLog.Enter, "2026-11-01T12:12:00Z"), Visit("v1", "candy", WishJournalVisitLog.Leave, "2026-11-01T12:38:00Z", 1560, 148) }, Beijing);
        var day = days.Single(); var e = day.Entries.Single();
        Assert.That(e.Visit, Is.Not.Null);
        Assert.That((e.Before, e.After), Is.EqualTo(((MoodKind?)MoodKind.Tense, (MoodKind?)MoodKind.Calm)));
        Assert.That(e.Visit.Minutes, Is.EqualTo(26f).Within(.01f)); Assert.That(e.Visit.Pinches, Is.EqualTo(148));
        Assert.That(day.Rows.Single().Visit, Is.SameAs(e.Visit), "the check-in became the visit's 'before', not a row of its own");
        Assert.That(L.VisitLine(e.Visit, false), Is.EqualTo("糖果世界 · 20:12 进入 · 26 分钟"));
        Assert.That(L.Stay(e.Visit, false), Is.EqualTo("26 分钟 · 捏了 148 下"));
        Assert.That(day.LastMood, Is.EqualTo(MoodKind.Calm));
    }

    [Test]
    public void VisitMatchingRespectsWorldAndTime()
    {
        var visits = new[] { Visit("v", "candy", WishJournalVisitLog.Enter, "2026-11-01T12:00:00Z") };   // no leave line
        var days = WishJournalData.Build(Doc(
                Wish("2026-11-01T22:30:00+08:00"),                                            // 2.5 h after entering: inside
                Wish("2026-11-01T23:30:00+08:00"),                                            // 3.5 h: outside
                Wish("2026-11-01T21:00:00+08:00", BubbleWorldData.NatureFolder, "nature")),   // other world
            null, visits, Beijing);
        var entries = days.Single().Entries;
        Assert.That(entries.Single(x => L.Time(x.Time) == "22:30").Visit, Is.Not.Null);
        Assert.That(entries.Single(x => L.Time(x.Time) == "23:30").Visit, Is.Null);
        Assert.That(entries.Single(x => x.World == "nature").Visit, Is.Null);
        Assert.That(days[0].Rows.Single().Visit.Minutes, Is.EqualTo(-1f), "no leave line: no minutes");
        Assert.That(L.VisitLine(days[0].Rows[0].Visit, true), Is.EqualTo("Candy World · 20:00"));
    }

    [Test]
    public void OlderWishesFallBackToThatDaysCheckIn()
    {
        var days = WishJournalData.Build(Doc(Wish("2026-10-20T21:00:00+08:00"), Wish("2026-10-21T09:00:00+08:00")),
            new[] { Mood("2026-10-20T11:00:00Z", "low") }, null, Beijing);   // 19:00 local on the 20th
        Assert.That(days[0].Entries[0].Before, Is.EqualTo(MoodKind.Low));
        Assert.That(days[1].Entries[0].Before, Is.Null, "a check-in from the day before does not count");
        Assert.That(days[0].Rows.Single().Visit, Is.Null, "a check-in without a visit is a row of its own");
        Assert.That(L.CheckInLine(days[0].Rows[0].Time, false), Is.EqualTo("签到 · 19:00"));
    }

    [Test]
    public void MoodOnlyAndVisitOnlyDaysArePages()
    {
        var days = WishJournalData.Build(new WishChimeDocument(),
            new[] { Mood("2026-10-03T02:00:00Z", "happy") },
            new[] { Visit("a", "nature", WishJournalVisitLog.Enter, "2026-10-05T02:00:00Z") }, Beijing);
        Assert.That(days.Select(d => d.Date.Day), Is.EqualTo(new[] { 3, 5 }));
        Assert.That(days.All(d => d.Entries.Count == 0), Is.True);
    }

    [Test]
    public void BrokenLinesAreSkipped()
    {
        var days = WishJournalData.Build(Doc(Wish("not a date"), Wish("2026-11-01T10:00:00+08:00")),
            new[] { "", "{", Mood("2026-11-01T01:00:00Z", "furious"), Mood("bad", "calm"), "{\"mood\":\"calm\"}" },
            new[] { "garbage", "{\"visitId\":\"\"}" }, Beijing);
        Assert.That(days.Single().Entries.Count, Is.EqualTo(1));
        Assert.That(days[0].Rows, Is.Empty);
        var blank = Wish("2026-11-01T11:00:00+08:00"); blank.strokes = new List<List<float[]>> { new() { new[] { .5f, .5f } } };
        Assert.That(WishJournalData.Build(Doc(blank), null, null, Beijing), Is.Empty, "a paper with no real stroke is not a wish");
    }

    [Test]
    public void PlaceComesFromTheFolder()
    {
        Assert.That(WishJournalData.Place(Wish("x", BubbleWorldData.NestedCandyFolder)), Is.EqualTo(("candy", true)));
        Assert.That(WishJournalData.Place(Wish("x", BubbleWorldData.NestedNatureFolder, "nature")), Is.EqualTo(("nature", true)));
        Assert.That(WishJournalData.Place(Wish("x", BubbleWorldData.NatureFolder, "nature")), Is.EqualTo(("nature", false)));
        Assert.That(L.WorldName("nature", true, false), Is.EqualTo("雪原世界"));
        Assert.That(L.WorldName("candy", false, false), Is.EqualTo("棉花糖世界"));
        Assert.That(L.ShortWorldName("candy", true, true), Is.EqualTo("Mint Chocolate"));
        Assert.That(L.LayerNote(0, false), Is.EqualTo("（第一层）"));
        Assert.That(L.LayerNote(1, false), Is.EqualTo("（第二层）"));
    }

    [Test]
    public void ReplayKeepsSplitStrokesContinuous()
    {
        var strokes = new List<List<float[]>>
        {
            new() { new[] { .1f, .1f }, new[] { .2f, .2f } },
            new() { new[] { .2f, .2f }, new[] { .3f, .3f } },   // continues the first (split at 74 points)
            new() { new[] { .6f, .6f }, new[] { .7f, .7f } },   // new pen-down
        };
        Assert.That(WishJournalData.ReplayKinds(strokes), Is.EqualTo(new[] { 0, 0, 1 }));
        var preset = WishPresetLibrary.Find("happy", "zh");
        var entry = new JournalEntry { Record = new WishChimeRecord { strokes = WishPresetLibrary.CopyStrokes(preset) }, Preset = "happy" };
        Assert.That(WishJournalStrip.Kinds(entry), Is.EqualTo(preset.kinds), "an untouched preset replays with its own pauses");
    }

    [Test]
    public void NewWishDot()
    {
        Assert.That(WishJournalData.Newest(Doc(Wish("2026-11-01T10:00:00+08:00"), Wish("2026-11-01T12:00:00+08:00"))), Is.EqualTo("2026-11-01T12:00:00+08:00"));
        Assert.That(WishJournalData.HasNew("2026-11-01T12:00:00+08:00", null), Is.True);
        Assert.That(WishJournalData.HasNew("2026-11-01T12:00:00+08:00", "2026-11-01T12:00:00+08:00"), Is.False);
        Assert.That(WishJournalData.HasNew("2026-11-01T12:00:01+08:00", "2026-11-01T12:00:00+08:00"), Is.True);
        Assert.That(WishJournalData.HasNew(null, null), Is.False);
    }

    [Test]
    public void VisitLogRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "visits-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Assert.That(WishJournalVisitLog.TryAppend(Visit("v", "nature", WishJournalVisitLog.Enter, "2026-11-01T13:05:00Z"), path), Is.True);
            Assert.That(WishJournalVisitLog.TryAppend(Visit("v", "nature", WishJournalVisitLog.Leave, "2026-11-01T13:19:00Z", 840, 9), path), Is.True);
            var visit = WishJournalData.Build(null, null, File.ReadAllLines(path), Beijing).Single().Rows.Single().Visit;
            Assert.That((visit.World, visit.Minutes, visit.Pinches), Is.EqualTo(("nature", 14f, 9)));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Test]
    public void MonthSummaryAndOpeningDay()
    {
        var days = WishJournalData.Build(Doc(Wish("2026-10-02T10:00:00+08:00"), Wish("2026-10-02T11:00:00+08:00"), Wish("2026-11-01T10:00:00+08:00")), null, null, Beijing);
        Assert.That(WishJournalData.MonthSummary(days, 2026, 10), Is.EqualTo((2, 1)));
        Assert.That(WishJournalData.OpeningIndex(days), Is.EqualTo(1), "opens on the latest day");
        Assert.That(WishJournalData.IndexOf(days, new DateTime(2026, 10, 2, 18, 0, 0)), Is.Zero);
    }
}

public sealed class WishJournalLayoutTests
{
    [Test]
    public void PapersFitTheRailAndTheirPressAreasDoNotTouch()
    {
        var pad = WishJournalLayout.StripHitPad / L.Mpx;
        for (int count = 1; count <= L.StripsPerScreen; count++)
        {
            Assert.That(L.StripX(0, count) - L.StripWidthPx / 2f, Is.GreaterThanOrEqualTo(60f));
            Assert.That(L.StripX(count - 1, count) + L.StripWidthPx / 2f, Is.LessThanOrEqualTo(560f));
            if (count > 1) Assert.That(L.StripPitch(count), Is.GreaterThan(L.StripWidthPx + 2f * pad), $"{count} papers");
        }
        Assert.That(L.StripPitch(3), Is.EqualTo(160f), "three papers keep the design spacing");
        Assert.That(L.StripTopPx + L.DayStripWidth * 27.5f / 8.5f / L.Mpx, Is.LessThan(440f), "papers end above the time labels");
    }

    [Test]
    public void RailPages()
    {
        Assert.That(L.RailPages(0), Is.EqualTo(1)); Assert.That(L.RailPages(5), Is.EqualTo(1)); Assert.That(L.RailPages(12), Is.EqualTo(3));
        Assert.That(L.RailPage(12, 2), Is.EqualTo((10, 2))); Assert.That(L.RailPage(12, 9), Is.EqualTo((10, 2)));
    }

    [Test]
    public void MonthGrid()
    {
        Assert.That(L.Column(new DateTime(2026, 10, 1)), Is.EqualTo(3), "Thursday");
        Assert.That(L.Cell(new DateTime(2026, 10, 1)), Is.EqualTo(new Vector2(L.GridLeft + 3 * L.CellPitchX, L.GridTop)));
        Assert.That(L.Rows(2026, 11), Is.EqualTo(6), "Nov 2026 starts on a Sunday");
        Assert.That(L.GridTop + (L.Rows(2026, 11) - 1) * L.CellPitchY + L.CellH, Is.LessThanOrEqualTo(470f), "six rows fit the panel");
    }

    [Test]
    public void Copy()
    {
        var today = new DateTime(2026, 11, 1);
        Assert.That(L.Date(today, false), Is.EqualTo("11月1日")); Assert.That(L.Date(today, true), Is.EqualTo("Nov 1"));
        Assert.That(L.Weekday(today, today, false), Is.EqualTo("周日 · 今天"));
        Assert.That(L.Weekday(today.AddDays(-1), today, true), Is.EqualTo("Saturday · Yesterday"));
        Assert.That(L.Weekday(new DateTime(2025, 12, 31), today, false), Is.EqualTo("2025年 · 周三"));
        Assert.That(L.Month(2026, 10, false), Is.EqualTo("2026 年 10 月"));
        Assert.That(L.MonthSummary(12, 8, false), Is.EqualTo("这个月写下 12 个心愿 · 去过 8 天"));
        Assert.That(L.PresetName("peace_joy", false), Is.EqualTo("平安喜乐"));
        Assert.That(L.PresetName(null, false), Is.Null);
    }
}

public sealed class WishJournalPanelTests
{
    GameObject root;
    static TMP_FontAsset Font(string guid) => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
    static readonly DateTime Today = new(2026, 11, 1);

    [SetUp] public void Setup() => root = new GameObject("Journal test");
    [TearDown] public void Cleanup() => Object.DestroyImmediate(root);

    static JournalEntry Entry(DateTime t, string preset = "peace_joy", string world = "candy") => new()
    {
        Time = t, World = world, Preset = preset,
        Record = new WishChimeRecord { createdAt = t.ToString("O"), strokes = WishPresetLibrary.CopyStrokes(WishPresetLibrary.Find(preset ?? "happy", "zh")), folder = world == "candy" ? BubbleWorldData.CandyFolder : BubbleWorldData.NatureFolder },
    };

    static List<JournalDay> Days(int wishesOnLastDay = 3)
    {
        var a = new JournalDay { Date = Today.AddDays(-12) };
        a.Entries.Add(Entry(a.Date.AddHours(20), "let_go", "nature"));
        a.Rows.Add(new JournalRow { Time = a.Date.AddHours(19), Before = MoodKind.Low }); a.LastMood = MoodKind.Low;
        var b = new JournalDay { Date = Today };
        var visit = new JournalVisit { World = "candy", Entered = Today.AddHours(20), Minutes = 26, Pinches = 148, Before = MoodKind.Tense, After = MoodKind.Calm };
        b.Rows.Add(new JournalRow { Time = visit.Entered, Visit = visit, Before = visit.Before, After = visit.After });
        for (int i = 0; i < wishesOnLastDay; i++) { var e = Entry(Today.AddHours(20).AddMinutes(10 + i), i % 2 == 0 ? "peace_joy" : null); e.Visit = visit; e.Before = visit.Before; e.After = visit.After; b.Entries.Add(e); }
        b.LastMood = MoodKind.Calm;
        return new List<JournalDay> { a, b };
    }

    WishJournalPanel Open(List<JournalDay> days, bool english = false)
    {
        var panel = root.AddComponent<WishJournalPanel>();
        panel.Initialize(english, Font("87c6dc9cf9eb7482f92d732e550a4b92"), Font("5d946757aa5b8410aba5b97ed2216bfa"), () => { }, days, Today);
        return panel;
    }

    static bool AnyInput(Transform t) => t != null && t.GetComponentsInChildren<Collider>(true).Any(c => c.enabled);
    static bool AnyDrawn(Transform t) => t != null && t.GetComponentsInChildren<Renderer>(true).Any(r => r.enabled);

    [Test]
    public void AllSpritesImportAtDesignScale()
    {
        var sprites = Resources.LoadAll<Sprite>("MoodiumWishJournal");
        Assert.That(sprites.Length, Is.EqualTo(81));
        foreach (var s in sprites) Assert.That(s.pixelsPerUnit, Is.EqualTo(UIRefreshAssetPostprocessor.PixelsPerUnit).Within(.01f), s.name);
        Assert.That(Resources.Load<Sprite>("MoodiumWishJournal/journal_panel").bounds.size.x / L.Mpx, Is.EqualTo(780f).Within(.5f), "panel kept at full resolution");
    }

    [Test]
    public void EmptyJournal()
    {
        var panel = Open(new List<JournalDay>());
        Assert.That(panel.Current, Is.EqualTo(WishJournalPanel.View.Empty));
        Assert.That(panel.PageRoot.GetComponentsInChildren<SpriteRenderer>().Any(r => r.sprite != null && r.sprite.name == "journal_label_empty_title_zh"), Is.True);
    }

    [Test]
    public void OpensOnTheLatestDayWithItsPapers()
    {
        var panel = Open(Days());
        Assert.That(panel.Current, Is.EqualTo(WishJournalPanel.View.Day));
        Assert.That(panel.DayIndex, Is.EqualTo(1));
        Assert.That(panel.PageRoot.GetComponentsInChildren<WishJournalStrip>().Length, Is.EqualTo(3));
        var next = panel.PageRoot.Find("Next Day").GetComponent<JournalSpriteButton>();
        Assert.That(next.Interactable, Is.False, "today is the last page");
        Assert.That(panel.PageRoot.GetComponentsInChildren<TextMeshPro>().Any(t => t.text == "11月1日"), Is.True);
        Assert.That(panel.PageRoot.GetComponentsInChildren<TextMeshPro>().Any(t => t.text == "糖果世界 · 20:00 进入 · 26 分钟"), Is.True);
        panel.PreviousDay();
        Assert.That(panel.DayIndex, Is.Zero);
        Assert.That(panel.PageRoot.GetComponentsInChildren<WishJournalStrip>().Single().Entry.World, Is.EqualTo("nature"));
    }

    [Test]
    public void MoreThanFivePapersPageAlongTheRail()
    {
        var panel = Open(Days(7));
        Assert.That(panel.PageRoot.GetComponentsInChildren<WishJournalStrip>().Length, Is.EqualTo(5));
        panel.ShowRailPage(1);
        Assert.That(panel.PageRoot.GetComponentsInChildren<WishJournalStrip>().Length, Is.EqualTo(2));
        Assert.That(panel.DayIndex, Is.EqualTo(1), "still the same day");
    }

    [Test]
    public void DetailDimsTheDayTakesItsInputAndReplays()
    {
        var panel = Open(Days());
        var entry = panel.CurrentDay.Entries[0];
        panel.OpenDetail(entry);
        Assert.That(panel.DetailOpen, Is.True);
        Assert.That(AnyInput(panel.PageRoot), Is.False, "the dimmed day takes no input");
        Assert.That(AnyInput(panel.DetailRoot), Is.True);
        Assert.That(panel.DetailStrip.Width, Is.EqualTo(.085f), "real writing size");
        Assert.That(panel.DetailStrip.Replaying, Is.True);
        panel.DetailStrip.FinishReplay();
        Assert.That(panel.DetailStrip.Replaying, Is.False);
        var texts = panel.DetailRoot.GetComponentsInChildren<TextMeshPro>().Select(t => t.text).ToArray();
        Assert.That(texts, Does.Contain("预设心愿「平安喜乐」"));
        Assert.That(texts, Does.Contain("26 分钟 · 捏了 148 下"));
        panel.CloseDetail(false);
        Assert.That(panel.DetailOpen, Is.False);
        Assert.That(AnyInput(panel.PageRoot), Is.True);
    }

    [Test]
    public void MonthViewJumpsToADay()
    {
        var panel = Open(Days());
        panel.OpenMonth();
        Assert.That(panel.Current, Is.EqualTo(WishJournalPanel.View.Month));
        Assert.That(panel.MonthShown, Is.EqualTo((2026, 11)));
        Assert.That(panel.MonthAllowed(2026, 10), Is.True); Assert.That(panel.MonthAllowed(2026, 12), Is.False); Assert.That(panel.MonthAllowed(2026, 9), Is.False);
        panel.ShiftMonth(-1);
        Assert.That(panel.MonthShown, Is.EqualTo((2026, 10)));
        Assert.That(panel.PageRoot.Find("Day 20"), Is.Not.Null, "Oct 20 has a wish");
        Assert.That(panel.PageRoot.Find("Day 21"), Is.Null);
        panel.JumpTo(new DateTime(2026, 10, 20));
        Assert.That((panel.Current, panel.DayIndex), Is.EqualTo((WishJournalPanel.View.Day, 0)));
    }

    [Test]
    public void EveryRendererIsInTheSortingGroupAndCloseHidesNothingTwice()
    {
        var panel = Open(Days(), english: true);
        panel.OpenDetail(panel.CurrentDay.Entries[1]);
        var group = root.GetComponent<VisionOSSortingGroup>();
        Assert.That(group.DepthPass, Is.EqualTo(VisionOSSortingGroup.DepthPassType.PostPass));
        var registered = new HashSet<GameObject>(group.Renderers.Select(e => e.Renderer));
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            Assert.That(registered.Contains(r.gameObject), Is.True, "not sorted: " + r.name);
        var detailOrders = group.Renderers.Where(e => e.Renderer.transform.IsChildOf(panel.DetailRoot)).Select(e => e.Order);
        var pageOrders = group.Renderers.Where(e => e.Renderer.transform.IsChildOf(panel.PageRoot)).Select(e => e.Order);
        Assert.That(detailOrders.Min(), Is.GreaterThan(pageOrders.Max()), "the detail draws over the dimmed day");
        panel.ShowDay(0);
        Assert.That(group.Renderers.All(e => e.Renderer != null && e.Renderer.activeInHierarchy), Is.True, "rebuilt pages leave no stale entries");
    }

    [Test]
    public void EntryButtonDotAndOpenState()
    {
        var b = WishJournalButton.Create(root.transform, Vector3.zero, () => { });
        Assert.That(b.DotVisible, Is.False);
        b.SetDot(true); Assert.That(b.DotVisible, Is.True);
        b.SetOpen(true);
        Assert.That(b.Button.GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo("journal_btn_open"));
        Assert.That(b.GetComponent<VisionOSSortingGroup>(), Is.Not.Null);
    }

    [Test]
    public void FlowWritesVisitsAndKeepsOnePanelAtATime()
    {
        var flow = File.ReadAllText("Assets/Scripts/Moodium/MoodiumAppFlowController.cs");
        Assert.That(flow, Does.Contain("WishJournalVisitLog.Enter"));
        Assert.That(flow, Does.Contain("WishJournalVisitLog.Leave"));
        Assert.That(flow, Does.Contain("if (m_JournalPanel != null) return; // Panels are mutually exclusive."));
        Assert.That(flow, Does.Contain("if (m_SelectionSettingsPanel != null) return; // Panels are mutually exclusive."));
        Assert.That(flow.Split("DisposeJournal();").Length - 1, Is.GreaterThanOrEqualTo(3), "selection restart, entering a world, destroy");
    }
}
