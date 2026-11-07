using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Moodium.NatureWorld
{
    /// <summary>
    /// One preset wish in one language. <see cref="strokes"/> use the handwriting format of
    /// <see cref="WishChimeRecord.strokes"/> ([u, v] on the paper, v = 1 at the top), so a preset renders, saves
    /// and flies exactly like handwriting. <see cref="kinds"/>: 0 = pen keeps moving (long stroke split at 74 points),
    /// 1 = new stroke, 2 = new character / line. Built by Source/tools/build_presets.py.
    /// </summary>
    [Serializable]
    public sealed class WishPreset
    {
        public string id, lang, text, chip;
        public int[] kinds;
        public List<List<float[]>> strokes;
    }

    [Serializable]
    public sealed class WishPresetDocument
    {
        public int version;
        public float[] paperCm;
        public List<WishPreset> presets = new();
    }

    public static class WishPresetLibrary
    {
        public const string DataPath = "MoodiumBubblePlay/WishPresets/wish_presets";
        public const string ChipFolder = "MoodiumBubblePlay/WishPresets/Chips/";
        /// <summary>Chip order, top to bottom. "write_own" is the sixth chip, below a slightly larger gap.</summary>
        public static readonly string[] Ids = { "peace_joy", "happy", "good_vibes", "stay_well", "let_go" };
        public const string WriteOwnId = "write_own";

        /// <summary>Presets draw thinner ink than finger writing: the glyphs are denser than handwriting.</summary>
        public const float InkWidth = .7f, GlowWidth = .5f;

        static WishPresetDocument s_Document;

        public static WishPresetDocument Document
        {
            get
            {
                if (s_Document != null) return s_Document;
                var asset = Resources.Load<TextAsset>(DataPath);
                if (asset == null)
                {
                    Debug.LogWarning("[Wish Preset] Missing Resources/" + DataPath + ".json");
                    return s_Document = new WishPresetDocument();
                }
                s_Document = JsonConvert.DeserializeObject<WishPresetDocument>(asset.text) ?? new WishPresetDocument();
                Resources.UnloadAsset(asset);
                return s_Document;
            }
        }

        public static string Language(bool english) => english ? "en" : "zh";

        public static WishPreset Find(string id, string lang)
        {
            foreach (var p in Document.presets)
                if (p.id == id && p.lang == lang) return p;
            return null;
        }

        /// <summary>Deep copy, so a chime's record never shares point arrays with the cached library.</summary>
        public static List<List<float[]>> CopyStrokes(WishPreset preset, int count = int.MaxValue)
        {
            var copy = new List<List<float[]>>();
            if (preset?.strokes == null) return copy;
            for (int i = 0; i < preset.strokes.Count && i < count; i++)
            {
                var s = new List<float[]>(preset.strokes[i].Count);
                foreach (var p in preset.strokes[i]) s.Add(new[] { p[0], p[1] });
                copy.Add(s);
            }
            return copy;
        }

        public static int PointCount(List<List<float[]>> strokes)
        {
            int n = 0;
            foreach (var s in strokes) n += s.Count;
            return n;
        }

        public static string ChipPrefix(string id, string lang) => ChipFolder + "wish_chip_" + id + "_" + lang;

        /// <summary>pressed &gt; selected &gt; gaze &gt; idle. "write_own" has no selected art.</summary>
        public static string ChipSuffix(string id, bool pressed, bool selected, bool hover, bool candy) =>
            pressed ? "_pressed" : selected && id != WriteOwnId ? (candy ? "_selected_candy" : "_selected_nature") : hover ? "_gaze" : "_idle";
    }
}
