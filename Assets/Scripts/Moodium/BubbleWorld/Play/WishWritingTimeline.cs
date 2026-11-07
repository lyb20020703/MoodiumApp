using System.Collections.Generic;
using UnityEngine;

namespace Moodium.NatureWorld
{
    /// <summary>
    /// Timing of the "the wish writes itself" animation for a preset. Pure: no scene objects.
    /// Mirrors buildTimeline / sample in Source/preview/stage.html, so the preview videos show exactly this timing.
    ///
    /// Strokes are grouped into pen movements (a kind-0 piece continues the previous piece without lifting the pen).
    /// Before each movement there is a short pause: StrokeGap inside a character / word, GlyphGap before a new
    /// character (Chinese) or line (English). The whole wish takes BaseSeconds + pen length / CmPerSecond,
    /// clamped to [MinSeconds, MaxSeconds]; the pen time is shared out by length, so long strokes take longer.
    /// If very short strokes (MinStroke) push the total past MaxSeconds, all pauses and strokes are scaled down evenly.
    /// Inside a movement the pen eases in and out (EaseAt).
    /// </summary>
    public sealed class WishWritingTimeline
    {
        /// <summary>Paper size while writing (strip shown 2.5x), used to measure pen length in real centimetres.</summary>
        public const float PaperWidthCm = 8.5f, PaperHeightCm = 27.5f;
        public const float StrokeGap = .03f, GlyphGap = .14f, MinStroke = .05f;
        public const float BaseSeconds = 1.4f, CmPerSecond = 45f, MinSeconds = 2.2f, MaxSeconds = 3.4f;
        /// <summary>0 = constant speed; 0.8 = slow start and finish, fast middle (still always moving forward).</summary>
        public const float Ease = .8f;

        public struct Group
        {
            public int First, Count;
            public float Length, Gap, Start, Duration;
        }

        /// <summary>One animation frame. <see cref="Partial"/> is a reused buffer: copy it if you keep it.</summary>
        public readonly struct Frame
        {
            /// <summary>Strokes [0, Done) are finished.</summary>
            public readonly int Done;
            /// <summary>Points of stroke Done already passed by the pen (without the tip), or null between movements.</summary>
            public readonly List<float[]> Partial;
            /// <summary>Pen position on the paper, or null between movements.</summary>
            public readonly Vector2? Tip;
            public bool PenMoving => Tip.HasValue;

            public Frame(int done, List<float[]> partial, Vector2? tip)
            {
                Done = done; Partial = partial; Tip = tip;
            }
        }

        readonly List<List<float[]>> m_Strokes;
        readonly List<Group> m_Groups = new();
        readonly List<float[]> m_Partial = new();

        public float Total { get; }
        /// <summary>1, or below 1 when the wish was sped up to stay within MaxSeconds.</summary>
        public float Scale { get; }
        public IReadOnlyList<Group> Groups => m_Groups;
        public int StrokeCount => m_Strokes.Count;

        public WishWritingTimeline(List<List<float[]>> strokes, int[] kinds)
        {
            m_Strokes = strokes;
            float pen = 0, pauses = 0;
            for (int i = 0; i < strokes.Count; i++)
            {
                int kind = kinds != null && i < kinds.Length ? kinds[i] : 1;
                if (i == 0 || kind != 0)
                {
                    var gap = i == 0 ? 0 : kind == 2 ? GlyphGap : StrokeGap;
                    m_Groups.Add(new Group { First = i, Gap = gap });
                    pauses += gap;
                }
                var g = m_Groups[^1];
                g.Count++;
                var length = Length(strokes[i]);
                g.Length += length;
                pen += length;
                m_Groups[^1] = g;
            }
            var target = Mathf.Clamp(BaseSeconds + pen / CmPerSecond, MinSeconds, MaxSeconds);
            var penTime = Mathf.Max(target - pauses, m_Groups.Count * MinStroke);
            float raw = 0;
            for (int i = 0; i < m_Groups.Count; i++)
            {
                var g = m_Groups[i];
                g.Duration = Mathf.Max(MinStroke, g.Length / Mathf.Max(pen, 1e-6f) * penTime);
                raw += g.Gap + g.Duration;
                m_Groups[i] = g;
            }
            // Many tiny strokes (dots) can push past the target through MinStroke: then everything speeds up evenly.
            Scale = raw > target ? target / raw : 1f;
            float t = 0;
            for (int i = 0; i < m_Groups.Count; i++)
            {
                var g = m_Groups[i];
                g.Gap *= Scale; g.Duration *= Scale;
                t += g.Gap;
                g.Start = t;
                t += g.Duration;
                m_Groups[i] = g;
            }
            Total = t;
        }

        /// <summary>Stroke length in centimetres on the writing-size paper.</summary>
        public static float Length(List<float[]> stroke)
        {
            float d = 0;
            for (int i = 1; i < stroke.Count; i++) d += Segment(stroke[i - 1], stroke[i]);
            return d;
        }

        static float Segment(float[] a, float[] b) =>
            new Vector2((b[0] - a[0]) * PaperWidthCm, (b[1] - a[1]) * PaperHeightCm).magnitude;

        /// <summary>Monotonic ease: derivative 1 - Ease * cos(2 pi s) stays above zero for Ease below 1.</summary>
        public static float EaseAt(float s)
        {
            s = Mathf.Clamp01(s);
            return s - Ease * Mathf.Sin(2 * Mathf.PI * s) / (2 * Mathf.PI);
        }

        public Frame Sample(float time)
        {
            foreach (var g in m_Groups)
            {
                if (time < g.Start) return new Frame(g.First, null, null);
                if (time >= g.Start + g.Duration) continue;
                var d = EaseAt((time - g.Start) / g.Duration) * g.Length;
                for (int i = g.First; i < g.First + g.Count; i++)
                {
                    var s = m_Strokes[i];
                    for (int j = 1; j < s.Count; j++)
                    {
                        var seg = Segment(s[j - 1], s[j]);
                        if (d <= seg)
                        {
                            var f = seg > 0 ? d / seg : 0;
                            var tip = new Vector2(Mathf.Lerp(s[j - 1][0], s[j][0], f), Mathf.Lerp(s[j - 1][1], s[j][1], f));
                            m_Partial.Clear();
                            for (int k = 0; k < j; k++) m_Partial.Add(s[k]);
                            return new Frame(i, m_Partial, tip);
                        }
                        d -= seg;
                    }
                }
                return new Frame(g.First + g.Count, null, null);
            }
            return new Frame(m_Strokes.Count, null, null);
        }
    }
}
