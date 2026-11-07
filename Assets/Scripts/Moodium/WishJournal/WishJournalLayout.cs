using System;
using System.Globalization;
using Moodium.MoodCheck;
using UnityEngine;

namespace Moodium.WishJournal
{
    /// <summary>
    /// Numbers, colours and copy of the Wish Journal (design: Moodium-WishJournal-Design-1101). Pure.
    /// Design pixels: the panel is 700 x 500, origin at its top-left, y down; 1 px = 0.42 / 520 m like all Moodium UI.
    /// </summary>
    public static class WishJournalLayout
    {
        public const float Mpx = 0.42f / 520f;
        public const float PanelW = 700f, PanelH = 500f;
        public const string Folder = "MoodiumWishJournal/";

        /// <summary>Design pixel (top-left origin, y down) → panel-local metres (centre origin, y up), on the panel plane.</summary>
        public static Vector3 Px(float x, float y, float z = 0f) => new((x - PanelW / 2f) * Mpx, (PanelH / 2f - y) * Mpx, z);

        // ---------- day page ----------
        public const int StripsPerScreen = 5;
        /// <summary>Strip spacing: 160 px for a few strips, closing up to 107.5 px so five fit the rail (60..560).</summary>
        public const float MaxStripPitch = 160f, StripSpan = 430f, StripWidthPx = 66f, StripTopPx = 222f, RailY = 206f;
        /// <summary>Real width of the paper on the day page and in the detail view (the writing size).</summary>
        public const float DayStripWidth = StripWidthPx * Mpx, DetailStripWidth = .085f;
        public const float StripHitPad = .006f;
        public const int MaxMoodRows = 2;

        /// <summary>Centre x (design px) of strip <paramref name="slot"/> of <paramref name="count"/> on screen, centred on the rail (60..560).</summary>
        public static float StripX(int slot, int count)
        {
            count = Mathf.Clamp(count, 1, StripsPerScreen);
            return 310f + (slot - (count - 1) / 2f) * StripPitch(count);
        }

        public static float StripPitch(int count) => count <= 1 ? 0f : Mathf.Min(MaxStripPitch, StripSpan / (count - 1));

        /// <summary>Strips shown on rail page <paramref name="page"/>: [first, first + count).</summary>
        public static (int first, int count) RailPage(int total, int page)
        {
            var pages = RailPages(total);
            page = Mathf.Clamp(page, 0, pages - 1);
            var first = page * StripsPerScreen;
            return (first, Mathf.Clamp(total - first, 0, StripsPerScreen));
        }

        public static int RailPages(int total) => Mathf.Max(1, Mathf.CeilToInt(total / (float)StripsPerScreen));

        // ---------- month page ----------
        public const float CellW = 80f, CellH = 54f, CellPitchX = 90f, CellPitchY = 60f, GridLeft = 40f, GridTop = 108f;

        /// <summary>Monday-first column 0..6 of a date.</summary>
        public static int Column(DateTime date) => ((int)date.DayOfWeek + 6) % 7;

        /// <summary>Top-left (design px) of the cell of <paramref name="date"/> in its month grid.</summary>
        public static Vector2 Cell(DateTime date)
        {
            var first = new DateTime(date.Year, date.Month, 1);
            var index = Column(first) + date.Day - 1;
            return new Vector2(GridLeft + index % 7 * CellPitchX, GridTop + index / 7 * CellPitchY);
        }

        public static int Rows(int year, int month) =>
            Mathf.CeilToInt((Column(new DateTime(year, month, 1)) + DateTime.DaysInMonth(year, month)) / 7f);

        // ---------- detail ----------
        /// <summary>Paper and card in front of the dimmed page (z negative = toward the viewer).</summary>
        public static readonly Vector3 DetailPaperTop = Px(205f, 62f, -.08f);
        /// <summary>Card 360 x 400 px (x 320..680, y 55..455); its content starts at x 344.</summary>
        public static readonly Vector3 DetailCard = Px(500f, 255f, -.06f);
        public const float DimmedPage = .35f;
        public const float OpenSeconds = .3f, CloseSeconds = .2f, PageSeconds = .2f, LiftSeconds = .4f;
        public const float SwayDegrees = 1.5f, SwayPeriod = 2.4f;

        // ---------- colours ----------
        public static readonly Color Candy = new Color32(0xF2, 0x7D, 0xB6, 0xFF);
        public static readonly Color Nature = new Color32(0x55, 0xC4, 0x7F, 0xFF);
        public static readonly Color Accent = new Color32(0xFF, 0xD6, 0x78, 0xFF);
        public static Color World(string world) => world == "candy" ? Candy : Nature;
        public static Color KnotColor(string world) => world == "candy" ? new Color32(0xFF, 0xB3, 0xD1, 0xFF) : new Color32(0xE8, 0xFF, 0xF3, 0xFF);

        public static Color Mood(MoodKind mood) => mood switch
        {
            MoodKind.Happy => new Color32(0xFF, 0xC6, 0x5C, 0xFF),
            MoodKind.Calm => new Color32(0x6F, 0xD3, 0xC4, 0xFF),
            MoodKind.Low => new Color32(0x8E, 0xA6, 0xEC, 0xFF),
            MoodKind.Tense => new Color32(0xF2, 0x93, 0x7B, 0xFF),
            _ => new Color32(0xC7, 0xC0, 0xDA, 0xFF),
        };

        public static string Key(MoodKind mood) => mood.ToString().ToLowerInvariant();

        // ---------- copy ----------
        public static string Lang(bool en) => en ? "en" : "zh";

        public static string Date(DateTime d, bool en) => en ? d.ToString("MMM d", CultureInfo.InvariantCulture) : $"{d.Month}月{d.Day}日";

        public static string Weekday(DateTime d, DateTime today, bool en)
        {
            var w = en ? d.ToString("dddd", CultureInfo.InvariantCulture) : "周" + "日一二三四五六"[(int)d.DayOfWeek];
            var rel = d.Date == today.Date ? (en ? "Today" : "今天") : d.Date == today.Date.AddDays(-1) ? (en ? "Yesterday" : "昨天") : null;
            if (d.Year != today.Year) w = (en ? d.Year + " · " : d.Year + "年 · ") + w;
            return rel == null ? w : w + " · " + rel;
        }

        public static string Month(int year, int month, bool en) =>
            en ? new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture) : $"{year} 年 {month} 月";

        public static string Time(DateTime t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);

        public static string FamilyName(string world, bool en) =>
            world == "candy" ? (en ? "Candy World" : "糖果世界") : (en ? "Nature World" : "自然世界");

        /// <summary>
        /// The world's name, the same as under its bubble: 棉花糖世界 / 薄荷巧克力世界 / 森林世界 / 雪原世界 / 枫林世界
        /// (<paramref name="maple"/>, 2026-11-03). "deep" means the
        /// Candy/Nested or NestedNature folder; since 泡泡世界 2.0 either world of a family can be entered first, so the
        /// folder names the world, not the layer (see LayerNote).
        /// </summary>
        public static string WorldName(string world, bool deep, bool en, bool maple = false) => world == "candy"
            ? (deep ? (en ? "Mint Chocolate World" : "薄荷巧克力世界") : (en ? "Marshmallow World" : "棉花糖世界"))
            : maple ? (en ? "Maple World" : "枫林世界")
            : (deep ? (en ? "Snow World" : "雪原世界") : (en ? "Forest World" : "森林世界"));

        /// <summary>Under a paper (five papers are 107 px apart): the same name; in English without "World" so it fits.</summary>
        public static string ShortWorldName(string world, bool deep, bool en, bool maple = false) => en
            ? (world == "candy" ? (deep ? "Mint Chocolate" : "Marshmallow") : maple ? "Maple" : (deep ? "Snow" : "Forest"))
            : WorldName(world, deep, false, maple);

        /// <summary>Which layer the wish was written in, from the record (layer 0 = the world entered first).</summary>
        public static string LayerNote(int layer, bool en) =>
            layer > 0 ? (en ? " (next layer)" : "（第二层）") : (en ? " (first layer)" : "（第一层）");

        /// <summary>"糖果世界 · 20:12 进入 · 26 分钟" — minutes are left out when the visit has no leave line.</summary>
        public static string VisitLine(JournalVisit v, bool en)
        {
            var s = FamilyName(v.World, en) + " · " + Time(v.Entered) + (en ? "" : " 进入");
            if (v.Minutes >= 0f) s += " · " + Mathf.Max(1, Mathf.RoundToInt(v.Minutes)) + (en ? " min" : " 分钟");
            return s;
        }

        public static string CheckInLine(DateTime t, bool en) => (en ? "Check-in · " : "签到 · ") + Time(t);

        public static string Stay(JournalVisit v, bool en)
        {
            if (v == null || v.Minutes < 0f) return null;
            var s = Mathf.Max(1, Mathf.RoundToInt(v.Minutes)) + (en ? " min" : " 分钟");
            if (v.Pinches > 0) s += en ? $" · {v.Pinches} squeezes" : $" · 捏了 {v.Pinches} 下";
            return s;
        }

        public static string MonthSummary(int wishes, int days, bool en) =>
            en ? $"{wishes} wishes · {days} days this month" : $"这个月写下 {wishes} 个心愿 · 去过 {days} 天";

        /// <summary>The preset's own text in the current language ("平安喜乐"), or null for handwriting.</summary>
        public static string PresetName(string preset, bool en)
        {
            if (string.IsNullOrEmpty(preset)) return null;
            var p = Moodium.NatureWorld.WishPresetLibrary.Find(preset, en ? "en" : "zh");
            return p != null ? p.chip : preset;
        }
    }
}
