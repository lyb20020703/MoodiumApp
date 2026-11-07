using System;
using System.Collections;
using System.Collections.Generic;
using Unity.PolySpatial;
using UnityEngine;
namespace Moodium.UI
{
    /// <summary>
    /// Settings panel above the selection gear. Two pages of the same size in one panel:
    ///  - main:  Language [中文 | English], Sensory bag [有 | 没有], Intro [去设置 ›], Sensitivity [− ●●●○○ +],
    ///           Pace [标准 | 轻快 | 速览]
    ///  - Intro: Ask language [每次问 | 不用问], Ask about the bag [每次问 | 不用问], with a back chevron.
    /// Rows 0–1 sit on the main page, rows 2–3 on the Intro page. Every choice applies at once (no Save).
    /// Sensitivity is a stepper (levels 1–5, default 3, see Moodium.Interaction.SqueezeSensitivity), not a segmented row.
    /// Pace is a three-option segmented row (0–2, default 0, see Moodium.CandyWorld.ProgressPace).
    /// Pages cross-fade; a page that is not shown has no enabled renderer or collider.
    /// </summary>
    public sealed class SelectionSettingsVisual : MonoBehaviour
    {
        public const int LanguageRow = 0, BagRow = 1, AskLanguageRow = 2, AskBagRow = 3, RowCount = 4;
        public const float PageFadeSeconds = .2f;
        public const int SensitivityLevels = 5;
        public const int PaceOptions = 3;

        SpriteRenderer m_Panel, m_IntroPanel, m_LinkSprite;
        UIRefreshSpriteButton m_Link;
        readonly SpriteRenderer[] m_Thumbs = new SpriteRenderer[RowCount];
        readonly SpriteRenderer[,] m_Labels = new SpriteRenderer[RowCount, 2];
        Action<bool, bool> m_Apply, m_ApplyIntro; Action m_Close; Action<int> m_ApplySensitivity;
        SpriteRenderer m_Minus, m_Plus;
        readonly SpriteRenderer[] m_Dots = new SpriteRenderer[SensitivityLevels];
        int m_Sensitivity = 3;
        SpriteRenderer m_PaceThumb;
        readonly SpriteRenderer[] m_PaceLabels = new SpriteRenderer[PaceOptions];
        int m_Pace; Action<int> m_ApplyPace;
        Coroutine m_PaceSlide;
        bool m_English, m_HasBag, m_AskLanguage = true, m_AskBag = true, m_Closing;
        Coroutine m_Transition, m_Show, m_PageFade;
        Transform m_Content, m_MainPage, m_IntroPage;
        float m_Alpha = 1f, m_PageMix;   // m_PageMix: 0 = main page shown, 1 = Intro page shown
        readonly List<SpriteRenderer> m_Ghosts = new();

        public bool IntroPageOpen { get; private set; }
        /// <summary>Squeeze sensitivity shown on the main page, 1–5.</summary>
        public int Sensitivity => m_Sensitivity;
        /// <summary>Progress pace shown on the main page, 0–2 (标准 / 轻快 / 速览).</summary>
        public int Pace => m_Pace;

        public void Initialize(bool english, bool hasBag, Action<bool, bool> apply, Action close) =>
            Initialize(english, hasBag, true, true, apply, null, close);

        /// <param name="applyIntro">(askLanguage, askBag) whenever a row of the Intro page changes.</param>
        /// <param name="embeddedGear">Draw an "open" gear under the panel (the carousel's gear sits there). False when the
        /// caller's own gear stays visible elsewhere (video selection: top-right corner).</param>
        /// <param name="sensitivity">Squeeze sensitivity to show, 1–5.</param>
        /// <param name="applySensitivity">The new level whenever − or + changes it.</param>
        /// <param name="pace">Progress pace to show, 0–2.</param>
        /// <param name="applyPace">The new pace whenever an option is chosen.</param>
        public void Initialize(bool english, bool hasBag, bool askLanguage, bool askBag,
            Action<bool, bool> apply, Action<bool, bool> applyIntro, Action close, bool embeddedGear = true,
            int sensitivity = 3, Action<int> applySensitivity = null, int pace = 0, Action<int> applyPace = null)
        {
            m_Pace = Mathf.Clamp(pace, 0, PaceOptions - 1); m_ApplyPace = applyPace;
            m_English = english; m_HasBag = hasBag; m_AskLanguage = askLanguage; m_AskBag = askBag;
            m_Apply = apply; m_ApplyIntro = applyIntro; m_Close = close;
            m_Sensitivity = Mathf.Clamp(sensitivity, 1, SensitivityLevels); m_ApplySensitivity = applySensitivity;
            gameObject.AddComponent<VisionOSSortingGroup>();
            m_Content = new GameObject("Settings Panel Content").transform; m_Content.SetParent(transform, false);
            m_MainPage = new GameObject("Settings Main Page").transform; m_MainPage.SetParent(m_Content, false);
            m_IntroPage = new GameObject("Settings Intro Page").transform; m_IntroPage.SetParent(m_Content, false);

            m_Panel = UIRefreshVisuals.Sprite(m_MainPage, "Settings Panel", PanelId(false), Vector3.zero, 20);
            m_IntroPanel = UIRefreshVisuals.Sprite(m_IntroPage, "Settings Intro Panel", PanelId(true), Vector3.zero, 20);
            // One close button for both pages.
            var closeSprite = UIRefreshVisuals.Sprite(m_Content, "Settings Close", "settings_close", MoodiumUIRefreshLayout.SettingsClose, 23);
            UIRefreshVisuals.Button(closeSprite, Vector2.one * (44 * MoodiumUIRefreshLayout.DesignPxToMeters), Close);
            var backSprite = UIRefreshVisuals.Sprite(m_IntroPage, "Settings Back", "settings_back", MoodiumUIRefreshLayout.SettingsBack, 23);
            UIRefreshVisuals.Button(backSprite, Vector2.one * (44 * MoodiumUIRefreshLayout.DesignPxToMeters), () => ShowIntroPage(false));

            m_LinkSprite = UIRefreshVisuals.Sprite(m_MainPage, "Settings Intro Link", LinkPrefix() + "_idle", MoodiumUIRefreshLayout.SettingsIntroLink, 22);
            m_Link = m_LinkSprite.gameObject.AddComponent<UIRefreshSpriteButton>();
            m_Link.Configure(LinkPrefix(), () => ShowIntroPage(true));
            UIRefreshVisuals.Button(m_LinkSprite, MoodiumUIRefreshLayout.SettingsIntroLinkHit, m_Link.Press);

            if (embeddedGear)
            {
                // The open gear under the panel, where the selection gear was pressed (carousel).
                var gear = UIRefreshVisuals.Gear(transform, new Vector3(0, -MoodiumUIRefreshLayout.SettingsPanelAboveKey, -.002f), Close);
                gear.SetOpen(true); gear.ShowPressedFeedback();
            }
            for (int row = 0; row < RowCount; row++)
            {
                var page = row < AskLanguageRow ? m_MainPage : m_IntroPage;
                var centre = TrackCentre(row);
                UIRefreshVisuals.Sprite(page, "Settings Track " + row, "settings_seg_track", centre, 21);
                m_Thumbs[row] = UIRefreshVisuals.Sprite(page, "Settings Thumb " + row, "settings_seg_thumb", MoodiumUIRefreshLayout.SettingsOption(centre, Selected(row)), 22);
                for (int col = 0; col < 2; col++)
                {
                    int r = row, c = col;
                    var label = UIRefreshVisuals.Sprite(page, "Settings Option " + row + "-" + col, LabelId(row, col), MoodiumUIRefreshLayout.SettingsOption(centre, col), 23);
                    m_Labels[row, col] = label; label.color = MoodiumUIRefreshLayout.SettingsLabelColor(col == Selected(row));
                    UIRefreshVisuals.Button(label, new Vector2(.081f, .036f), () => Choose(r, c));
                }
            }
            BuildSensitivity();
            BuildPace();
            gameObject.AddComponent<Moodium.Flow.SensoryBagDirectTouchInput>();
            SetAlpha(1f); SetPageInput(true);
            if (Application.isPlaying) m_Show = StartCoroutine(Show());
        }

        public static Vector2 TrackCentre(int row) =>
            row % 2 == 0 ? MoodiumUIRefreshLayout.SettingsLanguageTrack : MoodiumUIRefreshLayout.SettingsBagTrack;

        int Selected(int row) => row switch
        {
            LanguageRow => m_English ? 1 : 0,
            BagRow => m_HasBag ? 0 : 1,
            AskLanguageRow => m_AskLanguage ? 0 : 1,
            _ => m_AskBag ? 0 : 1,
        };

        string Lang => m_English ? "en" : "zh";
        string PanelId(bool intro) => (intro ? "settings_intro_panel_" : "settings_panel_") + Lang;
        string LinkPrefix() => "settings_link_" + Lang;
        string LabelId(int row, int col) => row switch
        {
            LanguageRow => col == 0 ? "settings_label_lang_zh" : "settings_label_lang_en",
            BagRow => "settings_label_bag_" + (col == 0 ? "has_" : "none_") + Lang,
            _ => "settings_label_" + (col == 0 ? "ask_" : "skip_") + Lang,
        };

        // ---------- squeeze sensitivity ----------

        void BuildSensitivity()
        {
            var track = MoodiumUIRefreshLayout.SettingsSensitivityTrack;
            UIRefreshVisuals.Sprite(m_MainPage, "Settings Sensitivity Track", "settings_seg_track", track, 21);
            for (int i = 0; i < SensitivityLevels; i++)
                m_Dots[i] = UIRefreshVisuals.Sprite(m_MainPage, "Settings Sensitivity Dot " + (i + 1), "settings_step_dot",
                    MoodiumUIRefreshLayout.SettingsSensitivityDot(i), 22);
            var x = new Vector2(MoodiumUIRefreshLayout.SettingsStepperButtonX, 0f);
            m_Minus = UIRefreshVisuals.Sprite(m_MainPage, "Settings Sensitivity Minus", "settings_step_minus", track - x, 23);
            m_Plus = UIRefreshVisuals.Sprite(m_MainPage, "Settings Sensitivity Plus", "settings_step_plus", track + x, 23);
            UIRefreshVisuals.Button(m_Minus, MoodiumUIRefreshLayout.SettingsStepperHit, () => StepSensitivity(-1));
            UIRefreshVisuals.Button(m_Plus, MoodiumUIRefreshLayout.SettingsStepperHit, () => StepSensitivity(1));
        }

        /// <summary>− (−1) or + (+1). Stops at 1 and 5; applies at once.</summary>
        public void StepSensitivity(int delta)
        {
            if (m_Closing) return;
            var level = Mathf.Clamp(m_Sensitivity + delta, 1, SensitivityLevels);
            if (level == m_Sensitivity) return;
            m_Sensitivity = level;
            SetAlpha(m_Alpha);
            m_ApplySensitivity?.Invoke(level);
            Debug.Log("[Settings] squeeze sensitivity " + level);
        }

        // ---------- progress pace ----------

        string PaceLabelId(int option) => "settings_label_pace_" + option + "_" + Lang;

        void BuildPace()
        {
            UIRefreshVisuals.Sprite(m_MainPage, "Settings Pace Track", "settings_seg3_track", MoodiumUIRefreshLayout.SettingsPaceTrack, 21);
            m_PaceThumb = UIRefreshVisuals.Sprite(m_MainPage, "Settings Pace Thumb", "settings_seg3_thumb",
                MoodiumUIRefreshLayout.SettingsPaceOption(m_Pace), 22);
            for (int i = 0; i < PaceOptions; i++)
            {
                int option = i;
                m_PaceLabels[i] = UIRefreshVisuals.Sprite(m_MainPage, "Settings Pace Option " + i, PaceLabelId(i),
                    MoodiumUIRefreshLayout.SettingsPaceOption(i), 23);
                UIRefreshVisuals.Button(m_PaceLabels[i], MoodiumUIRefreshLayout.SettingsPaceOptionHit, () => ChoosePace(option));
            }
        }

        /// <summary>标准 (0), 轻快 (1) or 速览 (2). Applies at once; the thumb slides like the two-option rows.</summary>
        public void ChoosePace(int option)
        {
            option = Mathf.Clamp(option, 0, PaceOptions - 1);
            if (m_Closing || option == m_Pace) return;
            m_Pace = option;
            if (m_PaceSlide != null) StopCoroutine(m_PaceSlide);
            if (Application.isPlaying) m_PaceSlide = StartCoroutine(SlidePaceThumb());
            else m_PaceThumb.transform.localPosition = MoodiumUIRefreshLayout.SettingsPaceOption(m_Pace);
            SetAlpha(m_Alpha);   // label colours: the chosen one turns Ink
            m_ApplyPace?.Invoke(option);
            Debug.Log("[Settings] progress pace " + option);
        }

        IEnumerator SlidePaceThumb()
        {
            var from = m_PaceThumb.transform.localPosition;
            for (float e = 0; e < MoodiumUIRefreshLayout.SettingsThumbSlideSeconds; e += Time.unscaledDeltaTime)
            {
                m_PaceThumb.transform.localPosition = Vector3.Lerp(from, MoodiumUIRefreshLayout.SettingsPaceOption(m_Pace),
                    UIRefreshVisuals.Ease(e / MoodiumUIRefreshLayout.SettingsThumbSlideSeconds));
                yield return null;
            }
            m_PaceThumb.transform.localPosition = MoodiumUIRefreshLayout.SettingsPaceOption(m_Pace);
            m_PaceSlide = null;
        }

        /// <summary>The colour a pace label has at full panel opacity, or null for other sprites.</summary>
        Color? PaceColor(SpriteRenderer r)
        {
            for (int i = 0; i < PaceOptions; i++)
                if (m_PaceLabels[i] == r) return MoodiumUIRefreshLayout.SettingsLabelColor(i == m_Pace);
            return null;
        }

        /// <summary>The colour a sensitivity sprite has at full panel opacity, or null for other sprites.</summary>
        Color? SensitivityColor(SpriteRenderer r)
        {
            for (int i = 0; i < SensitivityLevels; i++)
                if (m_Dots[i] == r) return i < m_Sensitivity ? MoodiumUIRefreshLayout.SettingsDotOn : MoodiumUIRefreshLayout.SettingsDotOff;
            if (r == m_Minus) return new Color(1, 1, 1, m_Sensitivity > 1 ? 1f : MoodiumUIRefreshLayout.SettingsStepperDisabledAlpha);
            if (r == m_Plus) return new Color(1, 1, 1, m_Sensitivity < SensitivityLevels ? 1f : MoodiumUIRefreshLayout.SettingsStepperDisabledAlpha);
            return null;
        }

        // ---------- choices ----------

        public void Choose(int row, int option)
        {
            if (m_Closing || Selected(row) == option) return;
            switch (row)
            {
                case LanguageRow: m_English = option == 1; break;
                case BagRow: m_HasBag = option == 0; break;
                case AskLanguageRow: m_AskLanguage = option == 0; break;
                default: m_AskBag = option == 0; break;
            }
            var languageChanged = row == LanguageRow;
            if (m_Show != null) { StopCoroutine(m_Show); m_Show = null; m_Content.localScale = Vector3.one; SetAlpha(1); }
            if (m_Transition != null) StopCoroutine(m_Transition);
            foreach (var ghost in m_Ghosts) if (ghost != null) DestroyNow(ghost.gameObject);
            m_Ghosts.Clear();
            if (Application.isPlaying) m_Transition = StartCoroutine(Change(row, languageChanged)); else ApplyVisuals();
            if (row < AskLanguageRow) m_Apply?.Invoke(m_English, m_HasBag);
            else m_ApplyIntro?.Invoke(m_AskLanguage, m_AskBag);
        }

        void ApplyVisuals()
        {
            m_Panel.sprite = UIRefreshVisuals.Load(PanelId(false));
            m_IntroPanel.sprite = UIRefreshVisuals.Load(PanelId(true));
            m_Link.Configure(LinkPrefix(), () => ShowIntroPage(true));
            for (int i = 0; i < PaceOptions; i++) m_PaceLabels[i].sprite = UIRefreshVisuals.Load(PaceLabelId(i));
            for (int r = 0; r < RowCount; r++)
            {
                m_Thumbs[r].transform.localPosition = MoodiumUIRefreshLayout.SettingsOption(TrackCentre(r), Selected(r));
                for (int c = 0; c < 2; c++)
                {
                    m_Labels[r, c].sprite = UIRefreshVisuals.Load(LabelId(r, c));
                    m_Labels[r, c].color = MoodiumUIRefreshLayout.SettingsLabelColor(c == Selected(r));
                }
            }
            m_Panel.color = m_IntroPanel.color = m_LinkSprite.color = Color.white;
            SetAlpha(m_Alpha);   // re-applies the page fade and the hidden page's disabled renderers
        }

        IEnumerator Change(int changedRow, bool language)
        {
            // Only the shown page animates; the hidden page is brought up to date at the end.
            var first = changedRow < AskLanguageRow ? LanguageRow : AskLanguageRow;
            var starts = new Vector3[RowCount];
            var colors = new Color[RowCount, 2];
            for (int r = first; r < first + 2; r++)
            {
                starts[r] = m_Thumbs[r].transform.localPosition;
                for (int c = 0; c < 2; c++) colors[r, c] = m_Labels[r, c].color;
            }
            // Keep the old baked language visible while the new sprites fade in.
            var faded = new[] { m_Panel, m_Labels[BagRow, 0], m_Labels[BagRow, 1], m_LinkSprite };
            if (language)
                foreach (var original in faded)
                {
                    var ghost = UIRefreshVisuals.Sprite(m_MainPage, "Language Crossfade", "settings_panel_zh", original.transform.localPosition, original.sortingOrder);
                    ghost.sprite = original.sprite; ghost.color = original.color; m_Ghosts.Add(ghost);
                }
            m_Panel.sprite = UIRefreshVisuals.Load(PanelId(false));
            m_Link.Configure(LinkPrefix(), () => ShowIntroPage(true));
            for (int c = 0; c < 2; c++) m_Labels[BagRow, c].sprite = UIRefreshVisuals.Load(LabelId(BagRow, c));
            // The pace labels are words too: swap them with the panel (no crossfade; they are small).
            if (language) for (int i = 0; i < PaceOptions; i++) m_PaceLabels[i].sprite = UIRefreshVisuals.Load(PaceLabelId(i));
            float elapsed = 0;
            while (elapsed < MoodiumUIRefreshLayout.SettingsThumbSlideSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = UIRefreshVisuals.Ease(elapsed / MoodiumUIRefreshLayout.SettingsThumbSlideSeconds); var fade = Mathf.Clamp01(elapsed / .2f);
                for (int r = first; r < first + 2; r++)
                {
                    m_Thumbs[r].transform.localPosition = Vector3.Lerp(starts[r], MoodiumUIRefreshLayout.SettingsOption(TrackCentre(r), Selected(r)), t);
                    for (int c = 0; c < 2; c++)
                    {
                        var color = Color.Lerp(colors[r, c], MoodiumUIRefreshLayout.SettingsLabelColor(c == Selected(r)), fade);
                        if (language && r == BagRow) color.a *= fade;
                        m_Labels[r, c].color = color;
                    }
                }
                if (language)
                {
                    m_Panel.color = m_LinkSprite.color = new Color(1, 1, 1, fade);
                    foreach (var ghost in m_Ghosts) { var color = ghost.color; color.a = 1 - fade; ghost.color = color; }
                }
                yield return null;
            }
            foreach (var ghost in m_Ghosts) DestroyNow(ghost.gameObject);
            m_Ghosts.Clear(); ApplyVisuals(); m_Transition = null;
        }

        // ---------- pages ----------

        /// <summary>Opens the Intro page ("Set up ›") or returns to the main page (back chevron).</summary>
        public void ShowIntroPage(bool open)
        {
            if (m_Closing || IntroPageOpen == open) return;
            IntroPageOpen = open;
            SetPageInput(false);
            if (m_PageFade != null) StopCoroutine(m_PageFade);
            if (Application.isPlaying) m_PageFade = StartCoroutine(FadePage(open ? 1f : 0f));
            else { m_PageMix = open ? 1f : 0f; SetAlpha(m_Alpha); SetPageInput(true); }
            Debug.Log("[Settings] " + (open ? "intro page" : "main page"));
        }

        IEnumerator FadePage(float target)
        {
            var from = m_PageMix;
            for (float e = 0; e < PageFadeSeconds; e += Time.unscaledDeltaTime)
            {
                m_PageMix = Mathf.Lerp(from, target, UIRefreshVisuals.Ease(e / PageFadeSeconds));
                SetAlpha(m_Alpha);
                yield return null;
            }
            m_PageMix = target; SetAlpha(m_Alpha); SetPageInput(true); m_PageFade = null;
        }

        /// <summary>Only the settled, shown page takes input; nothing does during a fade or while closing.</summary>
        void SetPageInput(bool settled)
        {
            foreach (var c in m_MainPage.GetComponentsInChildren<Collider>(true)) c.enabled = settled && !IntroPageOpen && !m_Closing;
            foreach (var c in m_IntroPage.GetComponentsInChildren<Collider>(true)) c.enabled = settled && IntroPageOpen && !m_Closing;
        }

        float PageAlpha(Transform t) =>
            t.IsChildOf(m_IntroPage) ? m_PageMix : t.IsChildOf(m_MainPage) ? 1f - m_PageMix : 1f;

        // ---------- show / hide ----------

        IEnumerator Show()
        {
            float e = 0;
            while (e < .3f)
            {
                e += Time.unscaledDeltaTime; float t = UIRefreshVisuals.Ease(e / .3f);
                m_Content.localScale = Vector3.one * Mathf.Lerp(.92f, 1, t); SetAlpha(t);
                yield return null;
            }
            m_Content.localScale = Vector3.one; SetAlpha(1);
        }

        void SetAlpha(float a)
        {
            m_Alpha = a;
            foreach (var r in m_Content.GetComponentsInChildren<SpriteRenderer>(true))
            {
                var c = r.color; c.a = a * PageAlpha(r.transform);
                var own = SensitivityColor(r) ?? PaceColor(r);
                if (own.HasValue) c = new Color(own.Value.r, own.Value.g, own.Value.b, c.a * own.Value.a);
                for (int row = 0; row < RowCount; row++)
                    for (int col = 0; col < 2; col++)
                        if (m_Labels[row, col] == r) c.a *= MoodiumUIRefreshLayout.SettingsLabelColor(col == Selected(row)).a;
                r.color = c;
                // A fully transparent sprite is still a quad on visionOS: draw nothing for the hidden page.
                r.enabled = c.a > .01f;
            }
        }

        public void Close()
        {
            if (m_Closing) return;
            m_Closing = true;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            StopAllCoroutines(); m_Show = m_Transition = m_PageFade = m_PaceSlide = null;
            if (Application.isPlaying) StartCoroutine(Hide()); else m_Close?.Invoke();
        }

        IEnumerator Hide()
        {
            float e = 0;
            while (e < .2f) { e += Time.unscaledDeltaTime; SetAlpha(1 - UIRefreshVisuals.Ease(e / .2f)); yield return null; }
            m_Close?.Invoke();
        }

        static void DestroyNow(GameObject go) { if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }
    }
}
