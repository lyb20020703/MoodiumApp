using UnityEngine;

namespace Moodium.UI
{
    /// <summary>
    /// Layout numbers for the UI refresh, as pure functions so they can be unit-tested.
    /// All values match tokens/ui-refresh.tokens.json. Local positions are in metres,
    /// x right, y up, on the element's own plane.
    /// </summary>
    public static class MoodiumUIRefreshLayout
    {
        public const float DesignPxToMeters = 0.42f / 520f;

        public static readonly Color Candy = new Color32(0xF2, 0x7D, 0xB6, 0xFF);
        public static readonly Color Nature = new Color32(0x55, 0xC4, 0x7F, 0xFF);
        public static readonly Color Ink = new Color32(0x1E, 0x1A, 0x28, 0xFF);

        // ---------- energy ring ----------

        public const int EnergyDotCount = 40;
        public const float EnergyDotRadius = 54f * DesignPxToMeters;
        public const float EnergyGemRadius = 67f * DesignPxToMeters;
        public static readonly int[] EnergyGiftDots = { 7, 19, 31, 39 }; // 20 / 50 / 80 / 100 %

        /// <summary>Angle of dot i in degrees, counter-clockwise from +x. Dot 0 is one step clockwise from 12 o'clock; dot 39 sits at the top.</summary>
        public static float EnergyDotAngle(int i) => 90f - (i + 1) * (360f / EnergyDotCount);

        public static Vector2 EnergyDotPosition(int i) => OnCircle(EnergyDotAngle(i), EnergyDotRadius);

        public static Vector2 EnergyGemPosition(int giftIndex) => OnCircle(EnergyDotAngle(EnergyGiftDots[giftIndex]), EnergyGemRadius);

        public static int EnergyLitCount(float energy01) =>
            Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(energy01) * EnergyDotCount + 1e-4f), 0, EnergyDotCount);

        /// <summary>Index of the brighter leading dot, or -1 at 0% and 100%.</summary>
        public static int EnergyHeadIndex(float energy01)
        {
            var n = EnergyLitCount(energy01);
            return n > 0 && n < EnergyDotCount ? n - 1 : -1;
        }

        public static bool EnergyGiftReached(int giftIndex, float energy01) => EnergyLitCount(energy01) > EnergyGiftDots[giftIndex];

        public static Color EnergyHeadColor(Color world) => Color.Lerp(world, Color.white, 0.45f);

        // ---------- calibration rotate dial ----------

        public const float RotateCircleMargin = 0.05f;
        public const float RotateCircleSpriteRadius = 252f * DesignPxToMeters;
        public const float RotateKnobCentreAboveTable = 0.05f;
        public const float RotateSnapDegrees = 3f;

        public static float RotateCircleRadius(float frameWidth, float frameDepth) =>
            0.5f * Mathf.Sqrt(frameWidth * frameWidth + frameDepth * frameDepth) + RotateCircleMargin;

        public static float RotateCircleScale(float frameWidth, float frameDepth) =>
            RotateCircleRadius(frameWidth, frameDepth) / RotateCircleSpriteRadius;

        /// <summary>
        /// Knob position on the table plane relative to the frame centre (x = frame right, y = frame forward, away from the user).
        /// It sits on the rotation circle in the direction of the near-right corner, so it turns with the frame.
        /// </summary>
        public static Vector2 RotateKnobOnTable(float frameWidth, float frameDepth)
        {
            var dir = new Vector2(frameWidth * 0.5f, -frameDepth * 0.5f).normalized;
            return dir * RotateCircleRadius(frameWidth, frameDepth);
        }

        /// <summary>Snaps to 0 inside ±3°. Input and output in degrees, wrapped to (-180, 180].</summary>
        public static float SnapYaw(float yawDegrees)
        {
            var y = Mathf.DeltaAngle(0f, yawDegrees);
            return Mathf.Abs(y) <= RotateSnapDegrees ? 0f : y;
        }

        public static string FormatYaw(float yawDegrees)
        {
            var d = Mathf.RoundToInt(SnapYaw(yawDegrees));
            return d > 0 ? "+" + d + "°" : d < 0 ? "−" + (-d) + "°" : "0°";
        }

        // ---------- settings panel (relative to the panel centre) ----------
        // Panel art 360 x 440 design px, two pages of the same size: main (Language, Sensory bag, Intro → Set up,
        // Sensitivity, Pace) and Intro (ask the language / the sensory-bag question every time). Rows are 68 px apart.
        // The fourth row (2026-11-03) and the fifth row (Pace, 2026-11-04) each made the panel 68 px taller; every
        // earlier row moved up by 34 px each time.

        public const float SettingsPanelHalfHeightPx = 220f;
        public const float SettingsPanelAboveKey = (SettingsPanelHalfHeightPx + 28f) * DesignPxToMeters + .02f;

        public static readonly Vector2 SettingsClose = Px(139, 179);
        /// <summary>Back chevron of the Intro page, mirrored from the close button.</summary>
        public static readonly Vector2 SettingsBack = Px(-139, 179);
        /// <summary>First row (Language; on the Intro page: ask language).</summary>
        public static readonly Vector2 SettingsLanguageTrack = Px(56, 108);
        /// <summary>Second row (Sensory bag; on the Intro page: ask about the bag).</summary>
        public static readonly Vector2 SettingsBagTrack = Px(56, 40);
        /// <summary>Third row of the main page: the "Set up ›" pill that opens the Intro page.</summary>
        public static readonly Vector2 SettingsIntroLink = Px(56, -28);
        /// <summary>Fourth row of the main page: squeeze sensitivity, − ●●●○○ +.</summary>
        public static readonly Vector2 SettingsSensitivityTrack = Px(56, -96);
        /// <summary>The − and + buttons sit this far left and right of the sensitivity track's centre.</summary>
        public const float SettingsStepperButtonX = 75f * DesignPxToMeters;
        public static readonly Vector2 SettingsStepperHit = Px(48, 44);
        /// <summary>Spacing of the five level dots between − and +.</summary>
        public const float SettingsStepperDotSpacing = 21f * DesignPxToMeters;
        public static readonly Color SettingsDotOn = new(1f, 1f, 1f, .95f);
        public static readonly Color SettingsDotOff = new(1f, 1f, 1f, .28f);
        /// <summary>A − at level 1 or a + at level 5 is dimmed: it cannot go further.</summary>
        public const float SettingsStepperDisabledAlpha = .35f;

        /// <summary>Centre of level dot <paramref name="index"/> (0–4), relative to the panel centre.</summary>
        public static Vector2 SettingsSensitivityDot(int index) =>
            SettingsSensitivityTrack + new Vector2((index - 2) * SettingsStepperDotSpacing, 0f);

        /// <summary>Fifth row of the main page: progress pace, a three-option segmented control
        /// [标准 | 轻快 | 速览]. Wider than the two-option rows (234 x 44 px visible) and right-aligned with them.</summary>
        public static readonly Vector2 SettingsPaceTrack = Px(39, -164);
        /// <summary>Distance between the centres of two pace options (one segment, 78 px).</summary>
        public const float SettingsPaceOptionSpacing = 78f * DesignPxToMeters;
        public static readonly Vector2 SettingsPaceOptionHit = Px(76, 40);

        /// <summary>Centre of pace option <paramref name="option"/> (0–2), relative to the panel centre.</summary>
        public static Vector2 SettingsPaceOption(int option) =>
            SettingsPaceTrack + new Vector2((option - 1) * SettingsPaceOptionSpacing, 0f);

        public static readonly Vector2 SettingsIntroLinkHit = Px(200, 44);
        public const float SettingsOptionOffsetX = 48f * DesignPxToMeters;
        public const float SettingsThumbSlideSeconds = 0.25f;

        /// <summary>Centre of option 0 (left) or 1 (right) of a segmented row, relative to the panel centre.</summary>
        public static Vector2 SettingsOption(Vector2 trackCentre, int option) =>
            trackCentre + new Vector2(option == 0 ? -SettingsOptionOffsetX : SettingsOptionOffsetX, 0f);

        public static Color SettingsLabelColor(bool selected) => selected ? Ink : new Color(1f, 1f, 1f, 0.82f);

        static Vector2 OnCircle(float degrees, float radius)
        {
            var r = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * radius;
        }

        static Vector2 Px(float x, float y) => new Vector2(x, y) * DesignPxToMeters;
    }
}
