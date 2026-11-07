using System;
using System.Collections.Generic;
using Moodium.Flow;
using TMPro;
using Unity.PolySpatial;
using UnityEngine;

namespace Moodium.MoodCheck
{
    public enum MoodCheckState { Hidden, Dragging, Confirming, Recommending, Comparing, Done }

    [DisallowMultipleComponent]
    public sealed class MoodCheckController : MonoBehaviour
    {
        [SerializeField] GameObject m_PanelPrefab;
        [SerializeField] Camera m_Camera;
        [SerializeField] Vector2 m_ComparisonTarget = new(.5f, -.1f);

        GameObject m_PanelInstance;
        MoodCheckAvailability m_Availability;
        MoodCheckPhase m_Phase;
        MoodInputMethod m_InputMethod;
        MoodCheckSample m_PreSample;
        MoodVisitMetrics m_Metrics;
        readonly Dictionary<MoodKind, MoodiumSpatialButton> m_MoodChipButtons = new();
        bool m_English;
        bool m_CompletionSent;
        bool m_SkipSent;
        float m_ConfirmShownAt;
        VisionOSSortingGroup m_Sorting;
        readonly List<Renderer> m_SortRenderers = new();
        int m_SortedRendererCount = -1;

        public event Action<MoodCheckSample> Completed;
        public event Action Skipped;

        public MoodCheckState State { get; private set; } = MoodCheckState.Hidden;
        public MoodCheckPlate Plate { get; private set; }
        public MoodCheckSample PendingSample { get; private set; }
        public Transform PanelTransform => m_PanelInstance != null ? m_PanelInstance.transform : null;
        public bool IsVisible => m_PanelInstance != null && m_PanelInstance.activeSelf;
        public bool ShowRecommendCard { get; set; } = true;

        public void Configure(GameObject panelPrefab, Camera camera)
        {
            m_PanelPrefab = panelPrefab;
            m_Camera = camera;
        }

        public void BeginPreCheck(MoodCheckAvailability availability, bool english)
        {
            Begin(MoodCheckPhase.Pre, english);
            m_Availability = availability;
            m_PreSample = null;
            m_Metrics = default;
            Plate.SetNormalizedPosition(Vector2.zero, true);
            SetTitle("plate.title.pre");
            ShowDraggingFooter();
        }

        public void BeginPostCheck(MoodCheckSample pre, MoodVisitMetrics metrics, bool english)
        {
            Begin(MoodCheckPhase.Post, english);
            m_PreSample = pre;
            m_Metrics = metrics;
            Plate.SetNormalizedPosition(pre != null ? pre.Position : Vector2.zero, true);
            SetTitle("plate.title.post");
            ShowDraggingFooter();
        }

        public void QuickSelect(MoodKind mood)
        {
            if (State is not (MoodCheckState.Dragging or MoodCheckState.Confirming))
                return;
            m_InputMethod = MoodInputMethod.Quick;
            Plate.SetNormalizedPosition(MoodClassifier.Preset(mood), false);
            EnterConfirming();
        }

        public void Adjust()
        {
            if (State != MoodCheckState.Confirming)
                return;
            State = MoodCheckState.Dragging;
            ShowDraggingFooter();
        }

        public void Confirm()
        {
            if (State != MoodCheckState.Confirming)
                return;
            PendingSample = BuildSample();
            if (m_Phase == MoodCheckPhase.Post)
            {
                State = MoodCheckState.Comparing;
                ShowComparison();
            }
            else if (ShowRecommendCard)
            {
                State = MoodCheckState.Recommending;
                ShowRecommendation(false);
            }
            else
            {
                State = MoodCheckState.Done;
                DismissPanel();
                SendCompleted();
                Debug.Log("[Sensory Bag Branch] mode=none step=mood-confirmed-without-sticker-card");
            }
        }

        public void Retry()
        {
            if (State != MoodCheckState.Recommending)
                return;
            PendingSample = null;
            State = MoodCheckState.Dragging;
            ShowDraggingFooter();
        }

        public void AcceptRecommendation()
        {
            if (State != MoodCheckState.Recommending || m_CompletionSent)
                return;
            State = MoodCheckState.Done;
            SendCompleted();
            // The recommendation card is the final mood-check surface. Once the
            // user accepts it, return to world selection instead of leaving a
            // third compact recommendation-summary panel on screen.
            DismissImmediate();
        }

        public void ContinueComparison()
        {
            if (State != MoodCheckState.Comparing || m_CompletionSent)
                return;
            State = MoodCheckState.Done;
            SendCompleted();
        }

        public void Skip()
        {
            if (State == MoodCheckState.Hidden || m_SkipSent || m_CompletionSent)
                return;
            m_SkipSent = true;
            DismissPanel();
            State = MoodCheckState.Hidden;
            Skipped?.Invoke();
        }

        public void DismissImmediate()
        {
            DismissPanel();
            State = MoodCheckState.Hidden;
            PendingSample = null;
        }

        void Begin(MoodCheckPhase phase, bool english)
        {
            DismissPanel();
            m_Phase = phase;
            m_English = english;
            m_InputMethod = MoodInputMethod.Plate;
            m_CompletionSent = false;
            m_SkipSent = false;
            PendingSample = null;
            if (m_PanelPrefab == null)
                m_PanelPrefab = Resources.Load<GameObject>("Moodium/MoodCheck/MoodCheckPanel");
            m_PanelInstance = m_PanelPrefab != null ? Instantiate(m_PanelPrefab) : BuildFallbackPanel();
            m_PanelInstance.name = phase == MoodCheckPhase.Pre ? "Mood Check - Pre" : "Mood Check - Post";
            m_PanelInstance.transform.SetParent(transform, true);
            PlacePanel();
            var plateRoot = m_PanelInstance.transform.Find("Plate");
            Plate = plateRoot != null ? plateRoot.GetComponent<MoodCheckPlate>() : null;
            if (Plate == null && plateRoot != null)
            {
                Plate = plateRoot.gameObject.AddComponent<MoodCheckPlate>();
                Plate.Configure(.112f);
            }
            var handle = plateRoot != null ? plateRoot.GetComponent<MoodCheckPlateHandle>() : null;
            if (handle == null && plateRoot != null)
                handle = plateRoot.gameObject.AddComponent<MoodCheckPlateHandle>();
            if (handle != null)
            {
                handle.Configure(Plate);
                handle.Released += OnPlateReleased;
            }
            if (Plate != null)
            {
                Plate.Changed += OnPlateChanged;
                OnPlateChanged(Plate.NormalizedPosition, Plate.CurrentMood);
            }
            WireButtons();
            ApplyLanguage();
            SyncNativeSorting(true);
            State = MoodCheckState.Dragging;
        }

        void OnPlateReleased(Vector2 _)
        {
            if (State != MoodCheckState.Dragging)
                return;
            m_InputMethod = MoodInputMethod.Plate;
            EnterConfirming();
        }

        void OnPlateChanged(Vector2 _, MoodKind mood)
        {
            var copy = MoodCheckCopy.Get("mood." + mood.ToString().ToLowerInvariant(), m_English);
            var separator = copy.IndexOf(" · ", StringComparison.Ordinal);
            SetText("Readout/MoodName", separator >= 0 ? copy.Substring(0, separator) : copy);
            SetText("Readout/MoodDescription", separator >= 0 ? copy.Substring(separator + 3) : string.Empty);
            UpdateMoodChipSelection(mood);
            LayoutMoodReadout();
        }

        void LayoutMoodReadout()
        {
            if (m_PanelInstance == null) return;
            var name = m_PanelInstance.transform.Find("Readout/MoodName")?.GetComponent<TMP_Text>();
            var description = m_PanelInstance.transform.Find("Readout/MoodDescription")?.GetComponent<TMP_Text>();
            var dot = m_PanelInstance.transform.Find("Readout/MoodDot");
            if (name == null || description == null || dot == null) return;
            var nameWidth = name.preferredWidth * name.transform.localScale.x;
            var descriptionWidth = description.preferredWidth * description.transform.localScale.x;
            var start = -(.010f + .007f + nameWidth + .006f + descriptionWidth) * .5f;
            dot.localPosition = new Vector3(start + .005f, .1815f, -.006f);
            var dotRenderer = dot.GetComponent<SpriteRenderer>();
            if (dotRenderer != null && (State == MoodCheckState.Hidden || !Application.isPlaying))
                dotRenderer.color = MoodCheckPlate.Tint(Plate != null ? Plate.CurrentMood : MoodKind.Neutral);
            name.transform.localPosition = new Vector3(start + .017f + nameWidth * .5f, .1815f, -.004f);
            description.transform.localPosition = new Vector3(start + .017f + nameWidth + .006f + descriptionWidth * .5f, .1815f, -.004f);
        }

        void LateUpdate()
        {
            // New TMP fallback-font sub-meshes, the recommendation card and footer swaps add renderers.
            SyncNativeSorting();
            if (m_PanelInstance == null || Plate == null ||
                State is not (MoodCheckState.Dragging or MoodCheckState.Confirming)) return;
            var dot = m_PanelInstance.transform.Find("Readout/MoodDot")?.GetComponent<SpriteRenderer>();
            if (dot != null) dot.color = Color.Lerp(dot.color, MoodCheckPlate.Tint(Plate.CurrentMood),
                1f - Mathf.Exp(-10f * Time.deltaTime));
            if (State == MoodCheckState.Confirming)
            {
                var t = Mathf.SmoothStep(0f, 1f, (Time.time - m_ConfirmShownAt) / .25f);
                foreach (var node in new[] { "Footer/ConfirmTitle", "Footer/ConfirmButton" })
                {
                    var target = m_PanelInstance.transform.Find(node);
                    if (target == null) continue;
                    var position = target.localPosition;
                    position.y = (node.EndsWith("ConfirmTitle") ? -.2105f : -.247f) - .006f * (1f - t);
                    target.localPosition = position;
                    var label = target.GetComponent<TMP_Text>() ?? target.Find("Label")?.GetComponent<TMP_Text>();
                    if (label != null) { var color = label.color; color.a = t; label.color = color; }
                }
            }
        }

        void EnterConfirming()
        {
            State = MoodCheckState.Confirming;
            m_ConfirmShownAt = Time.time;
            SetActive("Footer/Hint", false);
            SetActive("Footer/ConfirmTitle", true);
            SetActive("Footer/SkipButton", false);
            SetActive("Footer/AdjustButton", false);
            SetActive("Footer/ConfirmButton", true);
            SetText("Footer/ConfirmTitle", MoodCheckCopy.Format("confirm.title", m_English,
                ("mood", MoodName(Plate.CurrentMood))));
            UpdateMoodChipSelection(Plate.CurrentMood);
        }

        MoodCheckSample BuildSample()
        {
            var position = Plate.NormalizedPosition;
            var mood = MoodClassifier.Classify(position.x, position.y);
            return new MoodCheckSample
            {
                phase = m_Phase,
                valence = position.x,
                arousal = position.y,
                mood = mood,
                intensity = MoodClassifier.Intensity(position.x, position.y),
                inputMethod = m_InputMethod,
                recommendedWorldId = m_Phase == MoodCheckPhase.Pre
                    ? MoodClassifier.Recommend(mood, m_Availability.CandyAvailable, m_Availability.NatureAvailable)
                    : m_PreSample?.recommendedWorldId,
                timestampUtc = DateTime.UtcNow.ToString("O"),
                language = m_English ? "en" : "zh-CN",
                durationInWorldSeconds = m_Phase == MoodCheckPhase.Post ? m_Metrics.DurationInWorldSeconds : 0f,
                pinchCount = m_Phase == MoodCheckPhase.Post ? m_Metrics.PinchCount : 0
            };
        }

        void SendCompleted()
        {
            if (m_CompletionSent || PendingSample == null)
                return;
            m_CompletionSent = true;
            Completed?.Invoke(PendingSample);
        }

        void ShowDraggingFooter()
        {
            SetActive("PanelGlass", true);
            SetCoreVisible(true);
            SetActive("RecommendationCard", false);
            SetActive("ComparisonCard", false);
            SetActive("Footer/Hint", true);
            SetActive("Footer/ConfirmTitle", false);
            SetActive("Footer/RecommendationSummary", false);
            SetActive("Footer/SkipButton", false);
            SetActive("Footer/AdjustButton", false);
            SetActive("Footer/ConfirmButton", false);
            SetActive("Footer/RetryButton", false);
            SetActive("Footer/ContinueButton", false);
            if (Plate != null) UpdateMoodChipSelection(Plate.CurrentMood);
        }

        void ShowRecommendation(bool compact)
        {
            SetActive("PanelGlass", false);
            SetCoreVisible(false);
            SetActive("RecommendationCard", !compact);
            SetActive("Footer/Hint", false);
            SetActive("Footer/ConfirmTitle", false);
            SetActive("Footer/RecommendationSummary", compact);
            SetActive("Footer/SkipButton", false);
            SetActive("Footer/AdjustButton", false);
            SetActive("Footer/ConfirmButton", false);
            SetActive("Footer/RetryButton", false);
            SetActive("Footer/ContinueButton", false);
            SetText("Footer/ContinueButton/Label", MoodCheckCopy.Get("rec.cta", m_English));
            SetText("RecommendationCard/Badge", MoodCheckCopy.Format("rec.today", m_English,
                ("mood", MoodName(PendingSample.mood))));
            SetText("RecommendationCard/WorldName", WorldName(PendingSample.recommendedWorldId));
            SetText("RecommendationCard/Recommendation",
                MoodCheckCopy.Get("rec." + PendingSample.mood.ToString().ToLowerInvariant(), m_English));
            SetText("RecommendationCard/StickerText", MoodCheckCopy.Get("rec.sticker", m_English));
            SetText("RecommendationCard/Other", MoodCheckCopy.Get("rec.other", m_English));
            SetText("Footer/RecommendationSummary",
                $"Moodi 推荐 · {WorldName(PendingSample?.recommendedWorldId)}\n{MoodCheckCopy.Get("rec." + PendingSample?.mood.ToString().ToLowerInvariant(), m_English)}");
            var card = m_PanelInstance.transform.Find("RecommendationCard");
            if (card != null)
            {
                var view = card.GetComponent<MoodRecommendationView>() ?? card.gameObject.AddComponent<MoodRecommendationView>();
                var title = m_PanelInstance.transform.Find("Header/Title")?.GetComponent<TMP_Text>();
                view.Show(PendingSample, m_English, title != null ? title.font : TMP_Settings.defaultFontAsset,
                    AcceptRecommendation, Retry);
            }
            // The card now has its own group; take its layers out of the panel's group.
            SyncNativeSorting(true);
        }

        void ShowComparison()
        {
            SetCoreVisible(false);
            SetActive("ComparisonCard", true);
            SetActive("Footer/Hint", false);
            SetActive("Footer/ConfirmTitle", false);
            SetActive("Footer/SkipButton", false);
            SetActive("Footer/AdjustButton", false);
            SetActive("Footer/ConfirmButton", false);
            SetActive("Footer/RetryButton", false);
            SetActive("Footer/ContinueButton", true);
            var comparison = MoodClassifier.Compare(m_PreSample != null ? m_PreSample.Position : Vector2.zero,
                PendingSample.Position, m_ComparisonTarget);
            SetText("ComparisonCard/Result", MoodCheckCopy.Get("compare." + comparison.ToString().ToLowerInvariant(), m_English));
            SetText("ComparisonCard/MoodTransition",
                $"{MoodCheckCopy.Get("compare.before", m_English)} {MoodName(m_PreSample?.mood ?? MoodKind.Neutral)} → " +
                $"{MoodCheckCopy.Get("compare.now", m_English)} {MoodName(PendingSample.mood)}");
            SetText("ComparisonCard/Metrics",
                $"{MoodCheckCopy.Get("compare.time", m_English)} {Mathf.Max(0f, m_Metrics.DurationInWorldSeconds) / 60f:0.#} min · " +
                $"{MoodCheckCopy.Get("compare.squeezes", m_English)} {m_Metrics.PinchCount}");
            SetText("ComparisonCard/Privacy", MoodCheckCopy.Get("compare.privacy", m_English));
        }

        void SetCoreVisible(bool visible)
        {
            SetActive("Header", visible);
            SetActive("Readout", visible);
            SetActive("Plate", visible);
            SetActive("AxisLabels", visible);
            SetActive("QuickChips", visible);
        }

        void PlacePanel()
        {
            var camera = m_Camera != null ? m_Camera : Camera.main;
            if (camera == null)
                return;
            var horizontal = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
            if (horizontal.sqrMagnitude < .0001f)
                horizontal = Vector3.forward;
            horizontal.Normalize();
            m_PanelInstance.transform.position = camera.transform.position + horizontal * .75f + Vector3.down * .12f;
            m_PanelInstance.transform.rotation = Quaternion.LookRotation(horizontal, Vector3.up);
        }

        void WireButtons()
        {
            SetActive("Footer/SkipButton", false);
            SetActive("Footer/AdjustButton", false);
            ConfigureButton("Footer/ConfirmButton", Confirm);
            ConfigureButton("Footer/ContinueButton", m_Phase == MoodCheckPhase.Post ? ContinueComparison : AcceptRecommendation);
            var moods = new[] { MoodKind.Happy, MoodKind.Calm, MoodKind.Neutral, MoodKind.Low, MoodKind.Tense };
            foreach (var mood in moods)
            {
                var captured = mood;
                ConfigureButton($"QuickChips/{mood}Chip", () => QuickSelect(captured));
                var chip = m_PanelInstance.transform.Find($"QuickChips/{mood}Chip")?.GetComponent<MoodiumSpatialButton>();
                if (chip != null)
                    m_MoodChipButtons[mood] = chip;
            }
            UpdateMoodChipSelection(Plate != null ? Plate.CurrentMood : MoodKind.Neutral);
        }

        void UpdateMoodChipSelection(MoodKind selected)
        {
            foreach (var pair in m_MoodChipButtons)
            {
                var button = pair.Value;
                if (button == null) continue;
                button.SetSelected(pair.Key == selected);
                var selectedBackground = button.transform.Find("Selected Background");
                if (selectedBackground != null) selectedBackground.gameObject.SetActive(pair.Key == selected);
                var alpha = State == MoodCheckState.Confirming && pair.Key != selected ? .55f : 1f;
                if (button.Label != null) { var color = button.Label.color; color.a = alpha; button.Label.color = color; }
                var dot = button.transform.Find("Dot")?.GetComponent<SpriteRenderer>();
                if (dot != null) { var color = MoodCheckPlate.Tint(pair.Key); color.a = alpha; dot.color = color; }
                var background = button.transform.Find("Background")?.GetComponent<Renderer>();
                if (background is SpriteRenderer sprite)
                    sprite.color = new Color(1f, 1f, 1f, alpha);
                else if (background != null && Application.isPlaying)
                {
                    var material = background.material;
                    var property = material.HasProperty("_Tint") ? "_Tint" : material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
                    if (material.HasProperty(property))
                    { var color = material.GetColor(property); color.a *= alpha; material.SetColor(property, color); }
                }
            }
        }

        void ConfigureButton(string path, Action action)
        {
            var target = m_PanelInstance.transform.Find(path);
            var button = target != null ? target.GetComponent<MoodiumSpatialButton>() : null;
            if (button == null) return;
            var label = target.Find("Label")?.GetComponent<TMP_Text>();
            var background = target.Find("Background")?.GetComponent<Renderer>();
            // Sprite skins are selected by swapping their explicit layers. Avoid
            // cloning Sprite/Default materials or the generic press flash resetting
            // their selected appearance, especially in editor previews and tests.
            button.Configure(label, background is SpriteRenderer ? null : background, action);
        }

        void ApplyLanguage()
        {
            var fonts = m_PanelInstance.GetComponentInChildren<MoodCheckFontReferences>(true);
            var font = m_English ? fonts?.English : fonts?.Chinese;
                foreach (var label in m_PanelInstance.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (font != null) label.font = font;
                    MoodTextMaterial.Apply(label);
                    // The panel is also used over white recommendation cards. A thick dark
                    // TMP outline/underlay becomes a black rectangle on visionOS, so keep all
                    // labels clean and let the panel glass provide contrast.
                    label.fontSharedMaterial.SetFloat("_OutlineWidth", 0f);
                    label.fontSharedMaterial.SetColor("_OutlineColor", Color.clear);
                    label.fontSharedMaterial.SetColor("_UnderlayColor", Color.clear);
                    label.fontSharedMaterial.SetFloat("_UnderlayDilate", 0f);
                    label.fontSharedMaterial.SetFloat("_UnderlaySoftness", 0f);
                }
            SetText("Header/Kicker", MoodCheckCopy.Get("plate.kicker", m_English));
            SetText("Footer/Hint", MoodCheckCopy.Get("plate.hint", m_English));
            SetText("Footer/SkipButton/Label", MoodCheckCopy.Get("skip", m_English));
            SetText("Footer/AdjustButton/Label", MoodCheckCopy.Get("confirm.adjust", m_English));
            SetText("Footer/ConfirmButton/Label", MoodCheckCopy.Get("confirm.yes", m_English));
            SetText("Footer/ContinueButton/Label", MoodCheckCopy.Get("compare.continue", m_English));
            SetText("AxisLabels/Up", MoodCheckCopy.Get("axis.up", m_English));
            SetText("AxisLabels/Down", MoodCheckCopy.Get("axis.down", m_English));
            SetText("AxisLabels/Left", MoodCheckCopy.Get("axis.left", m_English));
            SetText("AxisLabels/Right", MoodCheckCopy.Get("axis.right", m_English));
            foreach (var mood in new[] { MoodKind.Happy, MoodKind.Calm, MoodKind.Neutral, MoodKind.Low, MoodKind.Tense })
            {
                var chip = m_PanelInstance.transform.Find($"QuickChips/{mood}Chip");
                var label = chip != null ? chip.Find("Label")?.GetComponent<TMP_Text>() : null;
                var dot = chip != null ? chip.Find("Dot") : null;
                if (label == null || dot == null) continue;
                label.text = MoodName(mood);
                var width = label.preferredWidth * label.transform.localScale.x;
                dot.localPosition = new Vector3(-(.007f + .005f + width) * .5f + .0035f, 0f, -.005f);
                label.transform.localPosition = new Vector3(.006f, 0f, -.003f);
            }
            var confirm = m_PanelInstance.transform.Find("Footer/ConfirmButton");
            var confirmLabel = confirm != null ? confirm.Find("Label")?.GetComponent<TMP_Text>() : null;
            if (confirmLabel != null)
            {
                var width = Mathf.Max(.12f, confirmLabel.preferredWidth * confirmLabel.transform.localScale.x + .04f);
                confirm.GetComponent<BoxCollider>().size = new Vector3(width, .045f, .02f);
                confirm.Find("Background").localScale = new Vector3(width / .11f, 1f, 1f);
                confirm.GetComponent<MoodiumSpatialButton>()?.SetSelected(true);
            }
            LayoutMoodReadout();
        }

        void SetTitle(string key) => SetText("Header/Title", MoodCheckCopy.Get(key, m_English));

        void SetText(string path, string value)
        {
            var target = m_PanelInstance != null ? m_PanelInstance.transform.Find(path) : null;
            var text = target != null ? target.GetComponent<TMP_Text>() : null;
            if (text != null) text.text = value ?? string.Empty;
        }

        void SetActive(string path, bool active)
        {
            var target = m_PanelInstance != null ? m_PanelInstance.transform.Find(path) : null;
            if (target != null) target.gameObject.SetActive(active);
        }

        string MoodName(MoodKind mood)
        {
            var value = MoodCheckCopy.Get("mood." + mood.ToString().ToLowerInvariant(), m_English);
            var separator = value.IndexOf(" · ", StringComparison.Ordinal);
            return separator >= 0 ? value.Substring(0, separator) : value;
        }

        string WorldName(string worldId)
        {
            if (string.IsNullOrEmpty(worldId)) return string.Empty;
            if (m_English) return worldId == "candy" ? "Candy World" : "Nature World";
            return worldId == "candy" ? "糖果世界" : "自然世界";
        }

        GameObject BuildFallbackPanel()
        {
            Debug.LogWarning("[Mood Check] Panel prefab is missing; using a functional fallback.");
            var panel = new GameObject("MoodCheckPanel Fallback");
            var plate = new GameObject("Plate");
            plate.transform.SetParent(panel.transform, false);
            return panel;
        }

        /// <summary>
        /// visionOS ignores Unity's sortingOrder unless a VisionOSSortingGroup exists. Without one, RealityKit sorts
        /// every glass layer, sprite and text label of this panel on its own by distance. Next to another transparent
        /// object (the opening video, a world model) that order flips, and the text's glyph rectangles are drawn
        /// before the glass behind them: they show as dark blocks behind the letters. Same fix as GlassSlideToEnter
        /// and MoodRecommendationView: one group, PostPass, ordered by the sortingOrder the prefab already has
        /// (glass 0–2, plate 10–23, text 30).
        /// </summary>
        void SyncNativeSorting(bool force = false)
        {
            if (m_PanelInstance == null) return;
            if (force)
                foreach (var label in m_PanelInstance.GetComponentsInChildren<TMP_Text>(true))
                    if (label.isActiveAndEnabled) label.ForceMeshUpdate();
            m_PanelInstance.GetComponentsInChildren(true, m_SortRenderers);
            if (!force && m_SortRenderers.Count == m_SortedRendererCount) return;
            if (m_Sorting == null)
            {
                m_Sorting = m_PanelInstance.GetComponent<VisionOSSortingGroup>() ??
                    m_PanelInstance.AddComponent<VisionOSSortingGroup>();
                m_Sorting.DepthPass = VisionOSSortingGroup.DepthPassType.PostPass;
            }
            m_Sorting.Renderers.Clear();
            foreach (var renderer in m_SortRenderers)
            {
                // A sub-panel with its own group (the recommendation card) keeps its own order.
                if (renderer.GetComponentInParent<VisionOSSortingGroup>(true) != m_Sorting) continue;
                m_Sorting.Renderers.Add(new VisionOSSortingGroup.RendererSorting
                    { Renderer = renderer.gameObject, Order = NativeSortOrder(renderer), ApplyToDescendants = false });
            }
            m_SortedRendererCount = m_SortRenderers.Count;
        }

        /// <summary>Order inside the panel's sorting group. TMP fallback-font glyphs draw with their label.</summary>
        public static int NativeSortOrder(Renderer renderer)
        {
            var sub = renderer.GetComponent<TMP_SubMesh>();
            var owner = sub != null && sub.textComponent != null ? sub.textComponent.GetComponent<Renderer>() : null;
            return (owner != null ? owner : renderer).sortingOrder;
        }

        void DismissPanel()
        {
            if (m_PanelInstance != null)
            {
                m_PanelInstance.SetActive(false);
                if (Application.isPlaying) Destroy(m_PanelInstance);
                else DestroyImmediate(m_PanelInstance);
            }
            m_PanelInstance = null;
            Plate = null;
            m_Sorting = null;
            m_SortedRendererCount = -1;
        }

        void OnDestroy() => DismissPanel();
    }
}
