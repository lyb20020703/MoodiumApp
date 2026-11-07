using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moodium.MoodCheck;
using Moodium.NatureWorld;
using TMPro;
using Unity.PolySpatial;
using UnityEngine;
using L = Moodium.WishJournal.WishJournalLayout;

namespace Moodium.WishJournal
{
    /// <summary>
    /// The Wish Journal panel (design: Moodium-WishJournal-Design-1101, boards 2–5).
    /// Views: Day (one day: mood rows + a rail of the day's wish papers), Month (calendar to jump), Empty, and the
    /// Detail of one paper (real size, the wish writes itself again) over the dimmed day.
    /// One VisionOSSortingGroup (PostPass) on the root. A view that is not shown has no enabled renderer and no
    /// enabled collider; anything destroyed is first taken out of the group.
    /// Orders: 0 panel · 1 lines · 2 chips/knots · 3 faces/dots · 4–6 day papers · 7 text/labels · 8 buttons ·
    /// detail 24–26 paper, 27 card, 28 card text, 29 card buttons.
    /// </summary>
    public sealed class WishJournalPanel : MonoBehaviour
    {
        public enum View { Loading, Empty, Day, Month }

        const string F = L.Folder;
        const int DetailOrder = 24;

        bool m_En, m_Busy, m_Closing;
        TMP_FontAsset m_Zh, m_EnFont;
        Action m_Closed;
        List<JournalDay> m_Days;
        DateTime m_Today;
        int m_DayIndex = -1, m_RailPage, m_Year, m_Month;
        VisionOSSortingGroup m_Group;
        Transform m_Content;
        Layer m_Chrome, m_Page, m_Detail;
        WishJournalStrip m_DetailStrip;
        AudioSource m_Audio;
        float m_Alpha;
        Task<List<JournalDay>> m_Load;

        public View Current { get; private set; } = View.Loading;
        public IReadOnlyList<JournalDay> Days => m_Days;
        public int DayIndex => m_DayIndex;
        public int RailPage => m_RailPage;
        public (int year, int month) MonthShown => (m_Year, m_Month);
        public bool DetailOpen => m_Detail != null;
        public WishJournalStrip DetailStrip => m_DetailStrip;
        public Transform PageRoot => m_Page?.Root;
        public Transform DetailRoot => m_Detail?.Root;

        // ---------- setup ----------

        /// <summary>Opens the panel. <paramref name="days"/> given (tests) = no file reading.</summary>
        public void Initialize(bool english, TMP_FontAsset chinese, TMP_FontAsset latin, Action closed, List<JournalDay> days = null, DateTime? today = null)
        {
            m_En = english; m_Zh = chinese; m_EnFont = latin != null ? latin : chinese; m_Closed = closed;
            m_Today = (today ?? DateTime.Now).Date;
            m_Group = GetComponent<VisionOSSortingGroup>();
            if (m_Group == null) m_Group = gameObject.AddComponent<VisionOSSortingGroup>();   // not ??: Unity's fake null
            m_Group.DepthPass = VisionOSSortingGroup.DepthPassType.PostPass;
            m_Content = new GameObject("Journal Content").transform; m_Content.SetParent(transform, false);
            Moodium.UI.UIRefreshVisuals.UnitScale(m_Content);
            m_Audio = gameObject.AddComponent<AudioSource>(); m_Audio.playOnAwake = false; m_Audio.spatialBlend = 1f; m_Audio.volume = .25f;
            BuildChrome();
            gameObject.AddComponent<Moodium.Flow.SensoryBagDirectTouchInput>();
            if (days != null) Loaded(days);
            else { m_Load = WishJournalData.LoadAsync(); Current = View.Loading; }
            SetAlpha(Application.isPlaying ? 0f : 1f);
            if (Application.isPlaying) StartCoroutine(Appear());
        }

        void Loaded(List<JournalDay> days)
        {
            m_Days = days ?? new List<JournalDay>();
            m_DayIndex = WishJournalData.OpeningIndex(m_Days);
            if (m_DayIndex < 0) Show(View.Empty); else Show(View.Day);
            Debug.Log($"[Wish Journal] opened: {m_Days.Count} days, {CountWishes()} wishes");
        }

        int CountWishes() { int n = 0; foreach (var d in m_Days) n += d.Entries.Count; return n; }

        void Update()
        {
            if (m_Load == null || !m_Load.IsCompleted) return;
            var load = m_Load; m_Load = null;
            if (load.Status != TaskStatus.RanToCompletion) Debug.LogWarning("[Wish Journal] could not load: " + load.Exception?.GetBaseException().Message);
            Loaded(load.Status == TaskStatus.RanToCompletion ? load.Result : null);
            SetAlpha(m_Alpha);
        }

        // ---------- navigation (public for tests and buttons) ----------

        public void ShowDay(int index)
        {
            if (m_Days == null || index < 0 || index >= m_Days.Count) return;
            m_DayIndex = index; m_RailPage = 0;
            Show(View.Day);
        }

        public void PreviousDay() { if (!m_Busy && m_DayIndex > 0) Transition(() => ShowDay(m_DayIndex - 1)); }
        public void NextDay() { if (!m_Busy && m_Days != null && m_DayIndex < m_Days.Count - 1) Transition(() => ShowDay(m_DayIndex + 1)); }

        public void ShowRailPage(int page)
        {
            var day = CurrentDay; if (day == null) return;
            var clamped = Mathf.Clamp(page, 0, L.RailPages(day.Entries.Count) - 1);
            if (clamped == m_RailPage) return;
            m_RailPage = clamped; Show(View.Day);
        }

        public void ShowMonth(int year, int month)
        {
            m_Year = year; m_Month = month;
            Show(View.Month);
        }

        public void OpenMonth()
        {
            if (m_Busy) return;
            var d = CurrentDay != null ? CurrentDay.Date : m_Today;
            Transition(() => ShowMonth(d.Year, d.Month));
        }

        public void ShiftMonth(int by)
        {
            if (m_Busy) return;
            var target = new DateTime(m_Year, m_Month, 1).AddMonths(by);
            if (!MonthAllowed(target.Year, target.Month)) return;
            Transition(() => ShowMonth(target.Year, target.Month));
        }

        /// <summary>Months between the first record's month and today's month can be shown.</summary>
        public bool MonthAllowed(int year, int month)
        {
            var m = new DateTime(year, month, 1);
            var last = new DateTime(m_Today.Year, m_Today.Month, 1);
            var first = m_Days != null && m_Days.Count > 0 ? new DateTime(m_Days[0].Date.Year, m_Days[0].Date.Month, 1) : last;
            return m >= first && m <= last;
        }

        public void JumpTo(DateTime date)
        {
            if (m_Busy || m_Days == null) return;
            var i = WishJournalData.IndexOf(m_Days, date);
            if (i >= 0) Transition(() => ShowDay(i));
        }

        public void BackToToday()
        {
            if (m_Busy) return;
            Transition(() => ShowMonth(m_Today.Year, m_Today.Month));
        }

        public JournalDay CurrentDay => m_Days != null && m_DayIndex >= 0 && m_DayIndex < m_Days.Count ? m_Days[m_DayIndex] : null;

        // ---------- open / close ----------

        public void Close()
        {
            if (m_Closing) return;
            m_Closing = true;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            if (Application.isPlaying && isActiveAndEnabled) StartCoroutine(Disappear()); else m_Closed?.Invoke();
        }

        IEnumerator Appear()
        {
            for (float t = 0; t < L.OpenSeconds; t += Time.unscaledDeltaTime)
            {
                var e = Moodium.UI.UIRefreshVisuals.Ease(t / L.OpenSeconds);
                m_Content.localScale = Vector3.one * Mathf.Lerp(.92f, 1f, e);
                SetAlpha(e); yield return null;
            }
            m_Content.localScale = Vector3.one; SetAlpha(1f);
        }

        IEnumerator Disappear()
        {
            var from = m_Alpha;
            for (float t = 0; t < L.CloseSeconds; t += Time.unscaledDeltaTime) { SetAlpha(from * (1f - t / L.CloseSeconds)); yield return null; }
            SetAlpha(0f);
            m_Closed?.Invoke();
        }

        /// <summary>Fade the page out, swap it, fade it in (0.2 s). Input is off meanwhile.</summary>
        void Transition(Action swap)
        {
            if (!Application.isPlaying || !isActiveAndEnabled) { swap(); return; }
            StartCoroutine(TransitionRoutine(swap));
        }

        IEnumerator TransitionRoutine(Action swap)
        {
            m_Busy = true;
            m_Page?.SetInput(false);
            var half = L.PageSeconds / 2f;
            for (float t = 0; t < half; t += Time.unscaledDeltaTime) { m_Page?.Apply(m_Alpha * (1f - t / half)); yield return null; }
            swap();
            m_Page?.SetInput(false);
            for (float t = 0; t < half; t += Time.unscaledDeltaTime) { m_Page?.Apply(m_Alpha * t / half); yield return null; }
            m_Page?.Apply(m_Alpha); m_Page?.SetInput(!DetailOpen);
            m_Busy = false;
        }

        void SetAlpha(float a)
        {
            m_Alpha = a;
            m_Chrome?.Apply(a);
            m_Page?.Apply(a * (DetailOpen ? L.DimmedPage : 1f));
            m_Detail?.Apply(a);
        }

        // ---------- views ----------

        void Show(View view)
        {
            CloseDetail(true);
            if (m_Page != null) { WishJournalSorting.Remove(m_Group, m_Page.Root); Release(m_Page.Root.gameObject); m_Page = null; }
            Current = view;
            var root = new GameObject(view + " Page").transform; root.SetParent(m_Content, false);
            m_Page = new Layer(root);
            switch (view)
            {
                case View.Empty: BuildEmpty(root); break;
                case View.Day: BuildDay(root, CurrentDay); break;
                case View.Month: BuildMonth(root, m_Year, m_Month); break;
            }
            Finish(m_Page);
            m_Page.Apply(m_Alpha);
            m_Page.SetInput(!m_Busy);
        }

        void BuildChrome()
        {
            var root = new GameObject("Journal Chrome").transform; root.SetParent(m_Content, false);
            m_Chrome = new Layer(root);
            Moodium.UI.UIRefreshVisuals.Sprite(root, "Journal Panel", F + "journal_panel", Vector3.zero, 0);
            Icon(root, "Close", "close", 650, 42, Close);
            Finish(m_Chrome);
        }

        void BuildEmpty(Transform root)
        {
            Label(root, "Empty Title", "label_title", 30, 40, 7, true);
            Sprite(root, "Empty Rail", F + "journal_rail", L.Px(350, 180), 1);
            Sprite(root, "Empty String", F + "journal_string", L.Px(350, 194), 2);
            Sprite(root, "Empty Moodi", F + "journal_empty_moodi", L.Px(350, 250), 3);
            Label(root, "Empty Line 1", "label_empty_title", 350, 330, 7, false);
            Label(root, "Empty Line 2", "label_empty_body", 350, 378, 7, false);
        }

        void BuildDay(Transform root, JournalDay day)
        {
            if (day == null) return;
            // header
            Icon(root, "Previous Day", "prev", 42, 42, PreviousDay, m_DayIndex > 0);
            Icon(root, "Next Day", "next", 490, 42, NextDay, m_DayIndex < m_Days.Count - 1);
            Icon(root, "Month", "cal", 596, 42, OpenMonth);
            var date = Text(root, "Date", L.Date(day.Date, m_En), 30, 76, 40, 7, true, Color.white, true);
            var dateWidth = date.GetPreferredValues(date.text).x * 10f;
            Text(root, "Weekday", L.Weekday(day.Date, m_Today, m_En), 15, 76 + dateWidth + 14, 42, 7, true, new Color(1, 1, 1, .66f));
            // mood rows
            if (day.Rows.Count > 0)
            {
                Label(root, "Mood Label", day.Date == m_Today ? "label_mood_today" : "label_mood_day", 30, 86, 7, true);
                var shown = Mathf.Min(L.MaxMoodRows, day.Rows.Count);
                // The newest rows: the end of the day is what the page is about.
                for (int i = 0; i < shown; i++) MoodRow(root, day.Rows[day.Rows.Count - shown + i], 119 + i * 40);
                if (day.Rows.Count > shown)
                    Text(root, "More Rows", (m_En ? "+" : "还有 ") + (day.Rows.Count - shown) + (m_En ? " more" : " 次"), 12, 640, 159, 7, false, new Color(1, 1, 1, .5f));
                Sprite(root, "Mood Divider", F + "journal_line", L.Px(350, 188), 1);
            }
            // rail and papers
            Sprite(root, "Rail", F + "journal_rail", L.Px(310, L.RailY), 1);
            var (first, count) = L.RailPage(day.Entries.Count, m_RailPage);
            for (int i = 0; i < count; i++) Paper(root, day.Entries[first + i], L.StripX(i, count));
            if (day.Entries.Count == 0) Label(root, "No Wish", "label_no_wish", 310, 320, 7, false);
            if (L.RailPages(day.Entries.Count) > 1)
            {
                SmallIcon(root, "Rail Previous", "prev", 34, L.RailY, () => ShowRailPage(m_RailPage - 1), m_RailPage > 0);
                SmallIcon(root, "Rail Next", "next", 586, L.RailY, () => ShowRailPage(m_RailPage + 1), m_RailPage < L.RailPages(day.Entries.Count) - 1);
                Text(root, "Rail Page", (m_RailPage + 1) + " / " + L.RailPages(day.Entries.Count), 12, 586, L.RailY + 28, 7, false, new Color(1, 1, 1, .5f));
            }
            if (day.Entries.Count > 0) Label(root, "Legend", "label_legend", 584, 240, 7, true);
        }

        void MoodRow(Transform root, JournalRow row, float y)
        {
            float x = 30f;
            var pair = row.Before.HasValue && row.After.HasValue;
            if (row.Before.HasValue || row.After.HasValue)
            {
                var chip = Sprite(root, "Mood Chip", F + (pair ? "journal_chip_pair" : "journal_chip_single"), L.Px(0, y), 2);
                AlignLeft(chip, x, y);
                x += 6f;
                if (row.Before.HasValue) x = MoodFace(root, row.Before.Value, x, y);
                if (pair) { var arrow = Sprite(root, "Mood Arrow", F + "journal_arrow", L.Px(0, y), 3); x = AlignLeft(arrow, x + 4f, y) + 4f; }
                if (row.After.HasValue) x = MoodFace(root, row.After.Value, x, y);
                x = 30f + (pair ? 214f : 112f) + 14f;
            }
            var line = row.Visit != null ? L.VisitLine(row.Visit, m_En) : L.CheckInLine(row.Time, m_En);
            if (row.Visit != null)
            {
                var dot = Sprite(root, "World Dot", F + "journal_dot_small", L.Px(x + 4f, y), 3);
                dot.color = L.World(row.Visit.World);
                x += 14f;
            }
            Text(root, "Mood Row", line, 13, x, y, 7, true, new Color(1, 1, 1, .78f));
        }

        float MoodFace(Transform root, MoodKind mood, float x, float y)
        {
            var face = Sprite(root, "Moodi " + mood, F + "journal_moodi_" + L.Key(mood), L.Px(0, y), 3);
            x = AlignLeft(face, x, y) + 4f;
            var name = Sprite(root, "Mood " + mood, F + "journal_mood_" + L.Key(mood) + "_" + L.Lang(m_En), L.Px(0, y), 7);
            name.color = L.Mood(mood);
            return AlignLeft(name, x, y) + 6f;
        }

        void Paper(Transform root, JournalEntry entry, float x)
        {
            var knot = Sprite(root, "Knot", F + "journal_knot", L.Px(x, L.RailY), 2);
            knot.color = L.KnotColor(entry.World);
            Sprite(root, "String", F + "journal_string", L.Px(x, L.RailY + 9f), 2);
            var strip = WishJournalStrip.Create(root, "Wish " + L.Time(entry.Time), entry, L.DayStripWidth, L.Px(x, L.StripTopPx, -.004f), 4, () => OpenDetail(entry));
            m_Page.Strips.Add(strip);
            Sprite(root, "Badge", F + (entry.Preset != null ? "journal_badge_preset" : "journal_badge_hand"), L.Px(x + 32f, 431f, -.006f), 7);
            Text(root, "Time", L.Time(entry.Time), 14, x, 450, 7, false, Color.white, true);
            var place = L.ShortWorldName(entry.World, entry.Deep, m_En, entry.Maple);
            var t = Text(root, "Place", place, 12, x + 6f, 470, 7, false, new Color(1, 1, 1, .62f));
            var w = t.GetPreferredValues(place).x * 10f;
            var dot = Sprite(root, "Place Dot", F + "journal_dot_small", L.Px(x + 6f - w / 2f - 8f, 470), 3);
            dot.color = L.World(entry.World);
        }

        void BuildMonth(Transform root, int year, int month)
        {
            Icon(root, "Previous Month", "prev", 42, 42, () => ShiftMonth(-1), MonthAllowed(new DateTime(year, month, 1).AddMonths(-1).Year, new DateTime(year, month, 1).AddMonths(-1).Month));
            var title = Text(root, "Month Title", L.Month(year, month, m_En), 28, 76, 40, 7, true, Color.white, true);
            var titleWidth = title.GetPreferredValues(title.text).x * 10f;
            Icon(root, "Next Month", "next", Mathf.Max(290f, 76f + titleWidth + 36f), 42, () => ShiftMonth(1), MonthAllowed(new DateTime(year, month, 1).AddMonths(1).Year, new DateTime(year, month, 1).AddMonths(1).Month));
            JournalSpriteButton.Create(root, "Back To Today", F + "journal_pill_today_" + L.Lang(m_En), L.Px(568, 42), 8, new Vector2(108f, 46f) * L.Mpx, BackToToday, () => m_Busy);
            Label(root, "Weekdays", "label_weekdays", 350, 92, 7, false);
            var days = DateTime.DaysInMonth(year, month);
            for (int d = 1; d <= days; d++)
            {
                var date = new DateTime(year, month, d);
                var cell = L.Cell(date);
                var i = WishJournalData.IndexOf(m_Days, date);
                var day = i >= 0 ? m_Days[i] : null;
                var future = date > m_Today;
                if (day != null)
                {
                    var bg = JournalSpriteButton.Create(root, "Day " + d, F + "journal_cell_has", L.Px(cell.x + L.CellW / 2f, cell.y + L.CellH / 2f), 1,
                        new Vector2(L.CellW, L.CellH) * L.Mpx, () => JumpTo(date), () => m_Busy);
                    bg.name = "Day " + d;
                    var wishes = Mathf.Min(3, day.Entries.Count);
                    for (int k = 0; k < wishes; k++)
                        Sprite(root, "Mini", F + "journal_mini_" + day.Entries[k].World, L.Px(cell.x + 40f + k * 10f, cell.y + 21f), 3);
                    if (day.Entries.Count > 3) Text(root, "More", "+" + (day.Entries.Count - 3), 10, cell.x + 64f, cell.y + 36f, 7, true, new Color(1, 1, 1, .7f));
                    if (day.LastMood.HasValue) Sprite(root, "Mood Dot", F + "journal_dot_small", L.Px(cell.x + 16f, cell.y + 38f), 3).color = L.Mood(day.LastMood.Value);
                }
                if (date == m_Today) Sprite(root, "Today", F + "journal_cell_today", L.Px(cell.x + L.CellW / 2f, cell.y + L.CellH / 2f), 2);
                var color = day != null ? Color.white : new Color(1, 1, 1, future ? .28f : .55f);
                Text(root, "Date " + d, d.ToString(), 15, cell.x + 10f, cell.y + 16f, 7, true, color, day != null);
            }
            var (w, n) = WishJournalData.MonthSummary(m_Days, year, month);
            var gridBottom = L.GridTop + L.Rows(year, month) * L.CellPitchY;
            Text(root, "Month Summary", L.MonthSummary(w, n, m_En), 12, 40, Mathf.Max(gridBottom + 12f, 448f), 7, true, new Color(1, 1, 1, .5f));
        }

        // ---------- detail ----------

        public void OpenDetail(JournalEntry entry)
        {
            if (m_Busy || m_Closing || entry == null) return;
            CloseDetail(true);
            var root = new GameObject("Wish Detail").transform; root.SetParent(m_Content, false);
            m_Detail = new Layer(root);
            Sprite(root, "Detail Knot", F + "journal_knot", L.DetailPaperTop + new Vector3(0f, .004f, 0f), DetailOrder).color = L.KnotColor(entry.World);
            m_DetailStrip = WishJournalStrip.Create(root, "Detail Paper", entry, L.DetailStripWidth, L.DetailPaperTop, DetailOrder, () => { if (m_DetailStrip != null && m_DetailStrip.Replaying) m_DetailStrip.FinishReplay(); });
            m_DetailStrip.SetSway(false);
            m_Detail.Strips.Add(m_DetailStrip);
            Sprite(root, "Detail Card", F + "journal_card", L.DetailCard, DetailOrder + 3);
            BuildCard(root, entry);
            Finish(m_Detail);
            m_Page?.SetInput(false);
            SetAlpha(m_Alpha);
            m_Detail.SetInput(true);
            Replay();
            Debug.Log($"[Wish Journal] detail {L.Time(entry.Time)} {entry.World}{(entry.Deep ? " deep" : "")} preset={entry.Preset ?? "-"}");
        }

        void BuildCard(Transform root, JournalEntry e)
        {
            const float left = 344f; const float z = -.062f; var o = DetailOrder + 4;
            Vector3 P(float x, float y) => L.Px(x, y, z);
            Text(root, "Card Date", L.Date(e.Time, m_En) + " · " + L.Time(e.Time), 26, left, 104, o, true, Color.white, true).transform.localPosition = P(left, 104);
            Text(root, "Card Weekday", L.Weekday(e.Time, m_Today, m_En), 14, left, 136, o, true, new Color(1, 1, 1, .62f)).transform.localPosition = P(left, 136);
            float y = 168f;
            LabelAt(root, "label_detail_wrote", left, y, o, z);
            var preset = L.PresetName(e.Preset, m_En);
            Sprite(root, "Card Badge", F + (preset != null ? "journal_badge_preset" : "journal_badge_hand"), P(left + 10f, y + 24f), o);
            Text(root, "Card Wrote", preset != null ? (m_En ? $"Preset “{preset}”" : $"预设心愿「{preset}」") : (m_En ? "Handwritten" : "亲手写的"), 16, left + 26f, y + 24f, o, true, Color.white, true).transform.localPosition = P(left + 26f, y + 24f);
            y += 58f;
            LabelAt(root, "label_detail_hung", left, y, o, z);
            Text(root, "Card Place", L.WorldName(e.World, e.Deep, m_En, e.Maple) + L.LayerNote(e.Record != null ? e.Record.layer : (e.Deep ? 1 : 0), m_En), 16, left, y + 24f, o, true, Color.white, true).transform.localPosition = P(left, y + 24f);
            y += 58f;
            if (e.Before.HasValue || e.After.HasValue)
            {
                LabelAt(root, "label_detail_mood", left, y, o, z);
                var row = new JournalRow { Before = e.Before, After = e.After };
                var x = left;
                var pair = row.Before.HasValue && row.After.HasValue;
                var chip = Sprite(root, "Card Chip", F + (pair ? "journal_chip_pair" : "journal_chip_single"), P(0, y + 26f), o); AlignLeft(chip, x, y + 26f, z);
                x += 6f;
                if (row.Before.HasValue) x = CardFace(root, row.Before.Value, x, y + 26f, o, z);
                if (pair) { var arrow = Sprite(root, "Card Arrow", F + "journal_arrow", P(0, y + 26f), o); x = AlignLeft(arrow, x + 4f, y + 26f, z) + 4f; }
                if (row.After.HasValue) CardFace(root, row.After.Value, x, y + 26f, o, z);
                y += 62f;
            }
            var stay = L.Stay(e.Visit, m_En);
            if (stay != null)
            {
                LabelAt(root, "label_detail_stay", left, y, o, z);
                Text(root, "Card Stay", stay, 16, left, y + 24f, o, true, Color.white, true).transform.localPosition = P(left, y + 24f);
            }
            JournalSpriteButton.Create(root, "Replay", F + "journal_pill_replay_" + L.Lang(m_En), P(left + 78f, 406f), o + 1, new Vector2(156f, 48f) * L.Mpx, Replay, () => m_Busy);
            JournalSpriteButton.Create(root, "Put Back", F + "journal_pill_putback_" + L.Lang(m_En), P(left + 246f, 406f), o + 1, new Vector2(156f, 48f) * L.Mpx, () => CloseDetail(false), () => m_Busy);
        }

        float CardFace(Transform root, MoodKind mood, float x, float y, int order, float z)
        {
            var face = Sprite(root, "Card Moodi", F + "journal_moodi_" + L.Key(mood), L.Px(0, y, z), order);
            x = AlignLeft(face, x, y, z) + 4f;
            var name = Sprite(root, "Card Mood", F + "journal_mood_" + L.Key(mood) + "_" + L.Lang(m_En), L.Px(0, y, z), order);
            name.color = L.Mood(mood);
            return AlignLeft(name, x, y, z) + 6f;
        }

        /// <summary>Writes the wish again in its stroke order (2.2–3.4 s, as preset wishes), then rings once.</summary>
        public void Replay()
        {
            if (m_DetailStrip == null) return;
            var candy = m_DetailStrip.Entry.Candy;
            var write = BubblePlayVisuals.Clip("chime_write_loop");
            if (Application.isPlaying && write != null) { m_Audio.clip = write; m_Audio.loop = true; m_Audio.volume = .22f; m_Audio.Play(); }
            m_DetailStrip.Replay(() =>
            {
                if (!Application.isPlaying) return;
                m_Audio.Stop(); m_Audio.loop = false; m_Audio.volume = .25f;
                var ring = BubblePlayVisuals.Clip("chime_ring_" + (candy ? "candy_" : "nature_") + UnityEngine.Random.Range(0, 3));
                if (ring != null) m_Audio.PlayOneShot(ring);
            });
        }

        public void CloseDetail(bool immediate)
        {
            if (m_Detail == null) return;
            if (Application.isPlaying) m_Audio.Stop();
            WishJournalSorting.Remove(m_Group, m_Detail.Root);
            Release(m_Detail.Root.gameObject);
            m_Detail = null; m_DetailStrip = null;
            SetAlpha(m_Alpha);
            if (!immediate) m_Page?.SetInput(!m_Busy);
        }

        // ---------- building blocks ----------

        SpriteRenderer Sprite(Transform parent, string name, string id, Vector3 position, int order) =>
            Moodium.UI.UIRefreshVisuals.Sprite(parent, name, id, position, order);

        /// <summary>Moves a sprite so its left edge is at design x, centred on design y. Returns its right edge (design px).</summary>
        static float AlignLeft(SpriteRenderer r, float leftPx, float yPx, float z = 0f)
        {
            var w = r.sprite != null ? r.sprite.bounds.size.x / L.Mpx : 0f;
            r.transform.localPosition = L.Px(leftPx + w / 2f, yPx, z);
            return leftPx + w;
        }

        /// <summary>A baked label sprite (journal_label_* in the current language); left-aligned or centred at design (x, y).</summary>
        SpriteRenderer Label(Transform parent, string name, string key, float x, float y, int order, bool left)
        {
            var r = Sprite(parent, name, F + "journal_" + key + "_" + L.Lang(m_En), L.Px(x, y), order);
            if (left) AlignLeft(r, x, y);
            return r;
        }

        void LabelAt(Transform parent, string key, float x, float y, int order, float z)
        {
            var r = Sprite(parent, key, F + "journal_" + key + "_" + L.Lang(m_En), L.Px(x, y, z), order);
            AlignLeft(r, x, y, z);
        }

        JournalSpriteButton Icon(Transform parent, string name, string icon, float x, float y, Action action, bool enabled = true)
        {
            var b = JournalSpriteButton.Create(parent, name, F + "journal_icon_" + icon, L.Px(x, y, -.002f), 8, Vector2.one * (54f * L.Mpx), action, () => m_Busy || m_Closing);
            b.SetInteractable(enabled);
            return b;
        }

        void SmallIcon(Transform parent, string name, string icon, float x, float y, Action action, bool enabled)
        {
            var b = Icon(parent, name, icon, x, y, action, enabled);
            b.transform.localScale = Vector3.one * .75f;
        }

        TextMeshPro Text(Transform parent, string name, string text, float px, float x, float y, int order, bool left, Color color, bool bold = false)
        {
            var t = Moodium.UI.UIRefreshVisuals.Text(parent, name, m_En ? m_EnFont : m_Zh, px, order);
            t.text = text; t.color = color;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.alignment = left ? TextAlignmentOptions.Left : TextAlignmentOptions.Center;
            t.rectTransform.pivot = new Vector2(left ? 0f : .5f, .5f);
            t.transform.localPosition = L.Px(x, y);
            t.raycastTarget = false;
            return t;
        }

        void Finish(Layer layer)
        {
            foreach (var t in layer.Root.GetComponentsInChildren<TMP_Text>(true)) t.ForceMeshUpdate();
            WishJournalSorting.RegisterSubMeshes(m_Group, layer.Root);
            layer.Capture();
        }

        static void Release(GameObject go)
        {
            if (go == null) return;
            go.SetActive(false);
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        void OnDestroy()
        {
            if (m_Group == null) return;
            WishJournalSorting.Remove(m_Group, transform);
        }

        /// <summary>
        /// A group of things that fade and take input together. Alpha multiplies each sprite's / text's own colour;
        /// at (near) zero renderers are switched off.
        /// </summary>
        sealed class Layer
        {
            public readonly Transform Root;
            public readonly List<WishJournalStrip> Strips = new();
            readonly List<(SpriteRenderer r, Color c)> m_Sprites = new();
            readonly List<(TMP_Text t, Color c)> m_Texts = new();
            readonly List<Renderer> m_SubMeshes = new();

            public Layer(Transform root) { Root = root; }

            public void Capture()
            {
                m_Sprites.Clear(); m_Texts.Clear(); m_SubMeshes.Clear();
                foreach (var r in Root.GetComponentsInChildren<SpriteRenderer>(true)) m_Sprites.Add((r, r.color));
                foreach (var t in Root.GetComponentsInChildren<TMP_Text>(true)) m_Texts.Add((t, t.color));
                foreach (var s in Root.GetComponentsInChildren<TMP_SubMesh>(true)) { var r = s.GetComponent<Renderer>(); if (r != null) m_SubMeshes.Add(r); }
            }

            public void Apply(float a)
            {
                var on = a > .01f;
                foreach (var (r, c) in m_Sprites)
                {
                    if (r == null) continue;
                    r.color = new Color(c.r, c.g, c.b, c.a * a);   // c.a already holds a disabled button's 38 %
                    r.enabled = on;
                }
                foreach (var (t, c) in m_Texts) { if (t == null) continue; t.color = new Color(c.r, c.g, c.b, c.a * a); t.GetComponent<Renderer>().enabled = on; }
                foreach (var r in m_SubMeshes) if (r != null) r.enabled = on;
                foreach (var s in Strips) if (s != null) s.SetAlpha(a);
            }

            public void SetInput(bool on)
            {
                foreach (var c in Root.GetComponentsInChildren<Collider>(true))
                {
                    var b = c.GetComponent<JournalSpriteButton>();
                    c.enabled = on && (b == null || b.Interactable);
                }
            }
        }
    }
}
