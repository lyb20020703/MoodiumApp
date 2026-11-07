using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Moodium.MoodCheck;
using Moodium.NatureWorld;
using UnityEngine;

namespace Moodium.WishJournal
{
    /// <summary>One visit to a world, from the visit log, with the mood check-ins around it.</summary>
    public sealed class JournalVisit
    {
        public string VisitId, World;
        public DateTime Entered;
        public DateTime? Left;
        /// <summary>Minutes in the world, or -1 when the leave line is missing (app closed inside the world).</summary>
        public float Minutes = -1f;
        public int Pinches = -1;
        public MoodKind? Before, After;
    }

    /// <summary>One line of a day's mood: a visit (world, time, minutes), or a check-in that did not lead into a world.</summary>
    public sealed class JournalRow
    {
        public DateTime Time;
        public JournalVisit Visit;
        public MoodKind? Before, After;
    }

    public sealed class JournalEntry
    {
        public WishChimeRecord Record;
        public DateTime Time;
        public string World;
        /// <summary>Written in the second (nested) layer.</summary>
        public bool Deep;
        /// <summary>Written in 枫林世界 (World is "nature" and Deep is true, like 雪原; this picks the name).</summary>
        public bool Maple;
        public string Preset;
        public JournalVisit Visit;
        public MoodKind? Before, After;
        public bool Candy => World == "candy";
    }

    public sealed class JournalDay
    {
        public DateTime Date;
        public readonly List<JournalEntry> Entries = new();
        public readonly List<JournalRow> Rows = new();
        /// <summary>The last mood checked in that day (month view dot), if any.</summary>
        public MoodKind? LastMood;
    }

    /// <summary>
    /// Builds the Wish Journal from what is already on the device: the wish chimes (wish_chimes.json), the mood
    /// check-ins (moodium_mood_log.json, one JSON object per line) and the visit log written by
    /// <see cref="WishJournalVisitLog"/>. Pure apart from <see cref="LoadAsync"/>; all times are shown in
    /// <c>zone</c> (the device's local time in the app).
    /// </summary>
    public static class WishJournalData
    {
        /// <summary>A check-in this long before entering a world counts as that visit's "before" mood.</summary>
        public static readonly TimeSpan CheckInBeforeVisit = TimeSpan.FromMinutes(30);
        /// <summary>Without a leave line, a wish this long after entering still belongs to the visit.</summary>
        public static readonly TimeSpan OpenVisitWindow = TimeSpan.FromHours(3);

        [Serializable]
        sealed class MoodLine
        {
            public string visitId, phase, mood, timestampUtc, enteredWorldId;
            public float durationInWorldSeconds;
            public int pinchCount;
        }

        sealed class MoodPoint { public DateTime Time; public MoodKind Mood; public bool Post; public string VisitId; public bool Used; }

        public static Task<List<JournalDay>> LoadAsync(string wishPath = null, string moodPath = null, string visitPath = null)
        {
            wishPath ??= WishChimeStore.DefaultPath;
            moodPath ??= MoodCheckLog.DefaultPath;
            visitPath ??= WishJournalVisitLog.DefaultPath;
            return Task.Run(() => Build(WishChimeStore.Load(wishPath), ReadLines(moodPath), ReadLines(visitPath), TimeZoneInfo.Local));
        }

        static IEnumerable<string> ReadLines(string path)
        {
            try { return File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>(); }
            catch (Exception e) { Debug.LogWarning("[Wish Journal] could not read " + Path.GetFileName(path) + ": " + e.Message); return Array.Empty<string>(); }
        }

        public static bool TryTime(string text, TimeZoneInfo zone, out DateTime local)
        {
            local = default;
            if (string.IsNullOrEmpty(text) || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time))
                return false;
            local = TimeZoneInfo.ConvertTime(time, zone).DateTime;
            return true;
        }

        public static bool TryMood(string text, out MoodKind mood) => Enum.TryParse(text, true, out mood) && Enum.IsDefined(typeof(MoodKind), mood);

        /// <summary>Which world and layer a chime hangs in, from its folder (the record's own world as a fallback).</summary>
        public static (string world, bool deep) Place(WishChimeRecord record)
        {
            switch (record?.folder)
            {
                case BubbleWorldData.CandyFolder: return ("candy", false);
                case BubbleWorldData.NestedCandyFolder: return ("candy", true);
                case BubbleWorldData.NatureFolder: return ("nature", false);
                case BubbleWorldData.NestedNatureFolder: return ("nature", true);
                // 枫林 is not the family's first world either; IsMaple tells it apart from 雪原 for its name.
                case BubbleWorldData.MapleNatureFolder: return ("nature", true);
            }
            return (record?.world == "candy" ? "candy" : "nature", record != null && record.layer > 0);
        }

        /// <summary>The chime hangs in 枫林 (the third Nature world).</summary>
        public static bool IsMaple(WishChimeRecord record) => record?.folder == BubbleWorldData.MapleNatureFolder;

        public static List<JournalDay> Build(WishChimeDocument wishes, IEnumerable<string> moodLines, IEnumerable<string> visitLines, TimeZoneInfo zone)
        {
            zone ??= TimeZoneInfo.Local;
            var moods = new List<MoodPoint>();
            foreach (var line in moodLines ?? Array.Empty<string>())
            {
                var m = Parse<MoodLine>(line);
                if (m == null || !TryTime(m.timestampUtc, zone, out var t) || !TryMood(m.mood, out var kind)) continue;
                moods.Add(new MoodPoint { Time = t, Mood = kind, Post = string.Equals(m.phase, "post", StringComparison.OrdinalIgnoreCase), VisitId = m.visitId });
            }
            moods.Sort((a, b) => a.Time.CompareTo(b.Time));

            var visits = new Dictionary<string, JournalVisit>();
            foreach (var line in visitLines ?? Array.Empty<string>())
            {
                var v = Parse<WishJournalVisitLog.Line>(line);
                if (v == null || string.IsNullOrEmpty(v.visitId) || !TryTime(v.timestampUtc, zone, out var t)) continue;
                if (!visits.TryGetValue(v.visitId, out var visit)) visits[v.visitId] = visit = new JournalVisit { VisitId = v.visitId, Entered = t };
                if (v.@event == WishJournalVisitLog.Enter) { visit.Entered = t; visit.World = v.world; }
                else if (v.@event == WishJournalVisitLog.Leave)
                {
                    visit.Left = t; visit.Minutes = Mathf.Max(0f, v.seconds) / 60f; visit.Pinches = Mathf.Max(0, v.pinches);
                    if (string.IsNullOrEmpty(visit.World)) visit.World = v.world;
                }
            }
            var visitList = new List<JournalVisit>(visits.Values);
            visitList.Sort((a, b) => a.Entered.CompareTo(b.Entered));
            foreach (var visit in visitList)
            {
                // Before: the latest pre check-in shortly before entering. After: a post check-in of this visit.
                MoodPoint before = null;
                foreach (var m in moods)
                    if (!m.Post && !m.Used && m.Time <= visit.Entered && visit.Entered - m.Time <= CheckInBeforeVisit) before = m;
                if (before != null) { before.Used = true; visit.Before = before.Mood; }
                foreach (var m in moods)
                    if (m.Post && !m.Used && (m.VisitId == visit.VisitId || (string.IsNullOrEmpty(m.VisitId) && m.Time >= visit.Entered && visit.Left.HasValue && m.Time <= visit.Left.Value.AddMinutes(10))))
                    { m.Used = true; visit.After = m.Mood; break; }
            }

            var days = new SortedDictionary<DateTime, JournalDay>();
            JournalDay Day(DateTime t) { if (!days.TryGetValue(t.Date, out var d)) days[t.Date] = d = new JournalDay { Date = t.Date }; return d; }

            foreach (var record in wishes?.chimes ?? new List<WishChimeRecord>())
            {
                if (record == null || !WishChimeStore.CanRelease(record.strokes) || !TryTime(record.createdAt, zone, out var t)) continue;
                var (world, deep) = Place(record);
                var entry = new JournalEntry { Record = record, Time = t, World = world, Deep = deep, Maple = IsMaple(record), Preset = string.IsNullOrEmpty(record.preset) ? null : record.preset };
                foreach (var visit in visitList)
                    if ((visit.World ?? world) == world && t >= visit.Entered && t <= (visit.Left ?? visit.Entered + OpenVisitWindow)) entry.Visit = visit;
                if (entry.Visit != null) { entry.Before = entry.Visit.Before; entry.After = entry.Visit.After; }
                else
                {
                    // Older data has no visits: the day's last check-in before the wish, within the open-visit window.
                    foreach (var m in moods) if (!m.Post && m.Time <= t && t - m.Time <= OpenVisitWindow && m.Time.Date == t.Date) entry.Before = m.Mood;
                }
                Day(t).Entries.Add(entry);
            }
            foreach (var visit in visitList) Day(visit.Entered).Rows.Add(new JournalRow { Time = visit.Entered, Visit = visit, Before = visit.Before, After = visit.After });
            foreach (var m in moods)
            {
                if (!m.Used) Day(m.Time).Rows.Add(m.Post ? new JournalRow { Time = m.Time, After = m.Mood } : new JournalRow { Time = m.Time, Before = m.Mood });
                Day(m.Time).LastMood = m.Mood;   // moods are in time order: the last one wins
            }
            var result = new List<JournalDay>(days.Values);
            foreach (var d in result)
            {
                d.Entries.Sort((a, b) => a.Time.CompareTo(b.Time));
                d.Rows.Sort((a, b) => a.Time.CompareTo(b.Time));
            }
            return result;
        }

        static T Parse<T>(string line) where T : class
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            try { return JsonUtility.FromJson<T>(line); } catch { return null; }
        }

        /// <summary>Index of the day to open on: the last day with something on it (the list is in time order).</summary>
        public static int OpeningIndex(IReadOnlyList<JournalDay> days) => days == null || days.Count == 0 ? -1 : days.Count - 1;

        /// <summary>Index of <paramref name="date"/> in <paramref name="days"/>, or -1.</summary>
        public static int IndexOf(IReadOnlyList<JournalDay> days, DateTime date)
        {
            for (int i = 0; i < days.Count; i++) if (days[i].Date == date.Date) return i;
            return -1;
        }

        public static (int wishes, int days) MonthSummary(IReadOnlyList<JournalDay> days, int year, int month)
        {
            int w = 0, d = 0;
            foreach (var day in days)
                if (day.Date.Year == year && day.Date.Month == month) { d++; w += day.Entries.Count; }
            return (w, d);
        }

        /// <summary>The newest wish's createdAt (for the "new wish" dot), or null.</summary>
        public static string Newest(WishChimeDocument wishes)
        {
            string newest = null; DateTimeOffset best = DateTimeOffset.MinValue;
            foreach (var r in wishes?.chimes ?? new List<WishChimeRecord>())
                if (r != null && DateTimeOffset.TryParse(r.createdAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) && t > best) { best = t; newest = r.createdAt; }
            return newest;
        }

        /// <summary>True when <paramref name="newest"/> is later than what was seen when the journal was last opened.</summary>
        public static bool HasNew(string newest, string seenUntil)
        {
            if (string.IsNullOrEmpty(newest)) return false;
            if (string.IsNullOrEmpty(seenUntil)) return true;
            return DateTimeOffset.TryParse(newest, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var a) &&
                   (!DateTimeOffset.TryParse(seenUntil, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var b) || a > b);
        }

        /// <summary>
        /// Writing order for a replay: a handwritten stroke that was split at 74 points continues the previous one
        /// (it starts where that one ended) and must not pause; every other stroke is a new pen-down.
        /// </summary>
        public static int[] ReplayKinds(List<List<float[]>> strokes)
        {
            var kinds = new int[strokes?.Count ?? 0];
            for (int i = 1; i < kinds.Length; i++)
            {
                var prev = strokes[i - 1]; var cur = strokes[i];
                var joined = prev != null && cur != null && prev.Count > 0 && cur.Count > 0 &&
                             Mathf.Approximately(prev[^1][0], cur[0][0]) && Mathf.Approximately(prev[^1][1], cur[0][1]);
                kinds[i] = joined ? 0 : 1;
            }
            return kinds;
        }
    }

    /// <summary>
    /// Tiny append-only log of world visits (enter / leave), one JSON object per line, next to the mood log.
    /// The mood log itself only holds the optional check-ins; this is what lets the journal say which world,
    /// when, and for how long. Never throws.
    /// </summary>
    public static class WishJournalVisitLog
    {
        public const string Enter = "enter", Leave = "leave";

        [Serializable]
        public sealed class Line
        {
            public string visitId, world, @event, timestampUtc;
            public float seconds;
            public int pinches;
        }

        public static string DefaultPath => Path.Combine(Application.persistentDataPath, "moodium_visits.json");

        public static string Build(string visitId, string world, string kind, DateTime utc, float seconds = 0f, int pinches = 0) =>
            JsonUtility.ToJson(new Line { visitId = visitId, world = world, @event = kind, timestampUtc = utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), seconds = seconds, pinches = pinches });

        public static bool TryAppend(string json, string path = null)
        {
            try
            {
                path ??= DefaultPath;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(path, json + "\n");
                return true;
            }
            catch (Exception e) { Debug.LogWarning("[Wish Journal] could not write visit log: " + e.Message); return false; }
        }
    }
}
