using System;
using TMPro;
using Unity.PolySpatial;
using UnityEngine;
using UnityEngine.Rendering;

namespace Moodium.UI
{
    /// <summary>
    /// Liquid-glass "slide to enter" control for world entry (e.g. 进入自然世界 / 进入糖果世界).
    ///
    /// Layers, back to front: soft world-coloured glow, frosted glass pill (blurred background
    /// material), world tint, a light trail that follows the knob, the glass rim highlight, the
    /// label and chevron hint, the knob glow, the glossy knob and its arrow/check icon.
    ///
    /// Drag the knob past <see cref="CompleteThreshold"/> and release to fire <see cref="Completed"/>.
    /// Releasing earlier springs the knob back. Everything is built at runtime; textures load from
    /// Resources/MoodiumGlassSlider unless assigned in the inspector.
    ///
    /// Orientation: like Unity UI and the project's world-space TextMeshPro, the control's +Z
    /// points away from the viewer. Its parent is responsible for facing the user.
    ///
    /// Input: MoodiumSpatialInteractionController should route spatial-pointer Began/Moved/Ended to
    /// PointerStarted/PointerMoved/PointerEnded, exactly like SensoryBagCalibrationHandle.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GlassSlideToEnter : MonoBehaviour
    {
        public enum Theme { Nature, Candy }

        const string ResourceFolder = "MoodiumGlassSlider/";
        const float KnobScale = 0.84f;
        // Millimetre-scale separation is too small for stable visionOS transparent sorting.
        const float GlowZ = 0.008f;
        const float TintZ = -0.003f;
        const float FillZ = -0.006f;
        const float RimZ = -0.009f;
        const float LabelZ = -0.012f;
        const float ChevronZ = -0.014f;
        const float KnobGlowZ = 0.006f;
        const float KnobZ = -0.018f;
        const float IconZ = -0.021f;

        [Header("Size (metres)")]
        [SerializeField, Min(0.1f)] float m_Width = 0.28f;
        [SerializeField, Min(0.03f)] float m_Height = 0.068f;
        [SerializeField, Range(0.6f, 0.99f)] float m_CompleteThreshold = 0.9f;
        [Tooltip("World-space cap height of the label, in metres.")]
        [SerializeField, Range(0.008f, 0.03f)] float m_LabelHeight = 0.017f;

        [Header("Feel")]
        [SerializeField, Range(1f, 8f)] float m_ReturnFrequency = 3.2f;
        [SerializeField, Range(0.1f, 1f)] float m_ReturnDamping = 0.55f;
        [SerializeField, Min(0.5f)] float m_NudgeInterval = 3.2f;
        [SerializeField, Range(0f, 0.02f)] float m_NudgeDistance = 0.006f;
        [SerializeField, Range(0.1f, 1f)] float m_CompleteDelay = 0.35f;

        [Header("Optional: assign, or leave empty to load from Resources/MoodiumGlassSlider")]
        [SerializeField] Material m_GlassMaterial;
        [SerializeField] Texture2D m_TrackTint;
        [SerializeField] Texture2D m_TrackRim;
        [SerializeField] Texture2D m_TrackGlow;
        [SerializeField] Texture2D m_Fill;
        [SerializeField] Texture2D m_KnobNature;
        [SerializeField] Texture2D m_KnobCandy;
        [SerializeField] Texture2D m_KnobGlow;
        [SerializeField] Texture2D m_IconArrow;
        [SerializeField] Texture2D m_IconCheck;
        [SerializeField] Texture2D m_IconChevrons;
        [SerializeField] AudioClip m_ThresholdClip;
        [SerializeField] AudioClip m_CompleteClip;

        public event Action Completed;

        public float CompleteThreshold => m_CompleteThreshold;
        public float Progress => m_Progress;
        /// <summary>Track size in metres (local), for guides that point at the knob (StickerAlignGuide).</summary>
        public float Width => m_Width;
        public float Height => m_Height;
        public bool IsDragging => m_Dragging;
        public bool IsCompleted => m_Completed;

        Theme m_Theme = Theme.Nature;
        string m_Label = "进入自然世界";
        string m_CompletedLabel = "正在进入…";
        TMP_FontAsset m_Font;
        bool m_Built;

        Transform m_Knob;
        Transform m_KnobGlowT;
        Transform m_Icon;
        Transform m_FillT;
        Transform m_Chevrons;
        Mesh m_FillMesh;
        Mesh m_GlassMesh;
        float m_FillBuiltWidth = -1f;
        Material m_GlowMat, m_TintMat, m_FillMat, m_RimMat, m_ChevronMat, m_KnobGlowMat, m_KnobMat, m_IconMat;
        TextMeshPro m_LabelText;
        AudioSource m_Audio;

        float m_Progress;
        float m_Velocity;
        bool m_Dragging;
        bool m_Returning;
        bool m_Completed;
        bool m_CompletedFired;
        float m_CompleteTime;
        bool m_PastThreshold;
        float m_ThresholdPulse;
        int m_ActivePointer = -1;
        float m_GrabOffset;
        float m_Hover;
        bool m_Hovered;
        float m_IdleTimer;

        // ---------- Public API ----------

        public void Configure(string label, Theme theme, TMP_FontAsset font, Material glassMaterial = null,
            string completedLabel = null)
        {
            m_Label = string.IsNullOrEmpty(label) ? m_Label : label;
            m_Font = font;
            if (glassMaterial != null) m_GlassMaterial = glassMaterial;
            if (!string.IsNullOrEmpty(completedLabel)) m_CompletedLabel = completedLabel;
            m_Theme = theme;
            Build();
            // Re-apply in case the control was already built (reused for another world).
            if (m_Font != null) m_LabelText.font = m_Font;
            SetTheme(theme);
            ResetSlider();
        }

        public void ResetSlider()
        {
            m_Progress = 0f;
            m_Velocity = 0f;
            m_Dragging = m_Returning = m_Completed = m_CompletedFired = m_PastThreshold = false;
            m_ActivePointer = -1;
            m_IdleTimer = 0f;
            if (m_Built)
            {
                m_IconMat.SetTexture("_BaseMap", Load(ref m_IconArrow, "slider_icon_arrow"));
                m_LabelText.text = m_Label;
                ApplyVisuals(0f);
            }
        }

        public void SetHovered(bool hovered) => m_Hovered = hovered;

        public void PointerStarted(int pointerId, Vector3 worldPosition)
        {
            if (!m_Built || m_Completed || m_ActivePointer != -1)
                return;
            m_ActivePointer = pointerId;
            m_Dragging = true;
            m_Returning = false;
            m_Velocity = 0f;
            var x = transform.InverseTransformPoint(worldPosition).x;
            var knobX = KnobCenterX(m_Progress, m_Width, m_Height);
            m_GrabOffset = Mathf.Abs(x - knobX) < KnobDiameter(m_Height) * 0.8f ? knobX - x : 0f;
            m_Progress = ComputeProgress(x + m_GrabOffset, m_Width, m_Height);
        }

        public void PointerMoved(int pointerId, Vector3 worldPosition)
        {
            if (pointerId != m_ActivePointer || !m_Dragging)
                return;
            var x = transform.InverseTransformPoint(worldPosition).x;
            m_Progress = ComputeProgress(x + m_GrabOffset, m_Width, m_Height);
            var past = m_Progress >= m_CompleteThreshold;
            if (past && !m_PastThreshold)
            {
                m_ThresholdPulse = 1f;
                Play(m_ThresholdClip);
            }
            m_PastThreshold = past;
        }

        public void PointerEnded(int pointerId)
        {
            if (pointerId != m_ActivePointer)
                return;
            m_ActivePointer = -1;
            m_Dragging = false;
            if (m_Progress >= m_CompleteThreshold)
                BeginComplete();
            else
            {
                m_Returning = true;
                m_Velocity = 0f;
            }
            m_PastThreshold = false;
        }

        public void CancelPointer(int pointerId)
        {
            if (pointerId != m_ActivePointer) return;
            m_ActivePointer = -1; m_Dragging = false; m_Returning = true;
            m_Velocity = 0; m_PastThreshold = false;
        }

        // ---------- Pure helpers (EditMode-testable) ----------

        public static float KnobDiameter(float height) => height * KnobScale;

        static float Margin(float height) => (height - KnobDiameter(height)) * 0.5f;

        public static float Travel(float width, float height) => width - 2f * Margin(height) - KnobDiameter(height);

        /// <summary>Local x of the knob centre for a progress value.</summary>
        public static float KnobCenterX(float progress, float width, float height) =>
            -width * 0.5f + Margin(height) + KnobDiameter(height) * 0.5f + Travel(width, height) * progress;

        /// <summary>Progress (0..1) for a desired knob-centre local x.</summary>
        public static float ComputeProgress(float knobCenterX, float width, float height)
        {
            var travel = Travel(width, height);
            if (travel <= 0f)
                return 0f;
            var start = KnobCenterX(0f, width, height);
            return Mathf.Clamp01((knobCenterX - start) / travel);
        }

        // ---------- Frame update ----------

        // Start (not Awake) so that Configure, called right after AddComponent, wins.
        void Start()
        {
            if (!m_Built)
            {
                Build();
                ResetSlider();
            }
        }

        void Update()
        {
            if (!m_Built)
                return;
            var dt = Time.deltaTime;
            m_Hover = Mathf.MoveTowards(m_Hover, m_Hovered || m_Dragging ? 1f : 0f, dt / 0.2f);
            m_ThresholdPulse = Mathf.MoveTowards(m_ThresholdPulse, 0f, dt / 0.25f);

            if (m_Returning)
            {
                StepSpring(ref m_Progress, ref m_Velocity, 0f, m_ReturnFrequency, m_ReturnDamping, dt);
                m_Progress = Mathf.Max(m_Progress, -0.03f);
                if (Mathf.Abs(m_Progress) < 0.001f && Mathf.Abs(m_Velocity) < 0.01f)
                {
                    m_Progress = 0f;
                    m_Returning = false;
                }
            }

            if (m_Completed)
            {
                m_Progress = Mathf.MoveTowards(m_Progress, 1f, dt / 0.15f);
                if (!m_CompletedFired && Time.time - m_CompleteTime >= m_CompleteDelay)
                {
                    m_CompletedFired = true;
                    Completed?.Invoke();
                }
            }

            var nudge = 0f;
            var resting = !m_Dragging && !m_Returning && !m_Completed && m_Progress <= 0.001f;
            if (resting)
            {
                m_IdleTimer += dt;
                var cycle = m_IdleTimer % m_NudgeInterval;
                if (m_IdleTimer > m_NudgeInterval && cycle < 0.6f)
                    nudge = Mathf.Sin(cycle / 0.6f * Mathf.PI) * m_NudgeDistance;
            }
            else
            {
                m_IdleTimer = 0f;
            }
            ApplyVisuals(nudge);
        }

        void BeginComplete()
        {
            m_Completed = true;
            m_CompleteTime = Time.time;
            m_IconMat.SetTexture("_BaseMap", Load(ref m_IconCheck, "slider_icon_check"));
            m_LabelText.text = m_CompletedLabel;
            m_ThresholdPulse = 1f;
            Play(m_CompleteClip);
        }

        void ApplyVisuals(float nudge)
        {
            var knob = KnobDiameter(m_Height);
            var margin = Margin(m_Height);
            var p = Mathf.Max(0f, m_Progress);
            var knobX = KnobCenterX(m_Progress, m_Width, m_Height) + nudge;
            var themeGlow = ThemeGlow();
            var themeTint = ThemeTint();
            var themeFill = ThemeFill();

            // Knob, its glow and icon.
            var knobScale = (m_Dragging ? 1.06f : 1f) + m_ThresholdPulse * 0.08f;
            m_Knob.localPosition = new Vector3(knobX, 0f, KnobZ);
            m_Knob.localScale = Vector3.one * knob * knobScale;
            m_Icon.localPosition = new Vector3(knobX, 0f, IconZ);
            m_Icon.localScale = Vector3.one * knob * 0.42f * knobScale;
            m_KnobGlowT.localPosition = new Vector3(knobX, 0f, KnobGlowZ);
            m_KnobGlowT.localScale = Vector3.one * knob * 1.7f;
            SetColor(m_KnobGlowMat, themeGlow, 0.35f + 0.5f * p + 0.2f * m_ThresholdPulse);

            // Trail from the left end to the knob's right edge; fills the whole pill when done.
            var left = -m_Width * 0.5f + margin;
            var right = m_Completed ? m_Width * 0.5f - margin : knobX + knob * 0.5f;
            var fillWidth = Mathf.Max(knob, right - left);
            if (Mathf.Abs(fillWidth - m_FillBuiltWidth) > 0.0005f)
            {
                BuildRoundedRect(m_FillMesh, fillWidth, knob, knob * 0.5f, 10);
                m_FillBuiltWidth = fillWidth;
            }
            m_FillT.localPosition = new Vector3(left + fillWidth * 0.5f, 0f, FillZ);
            SetColor(m_FillMat, themeFill, m_Completed ? 1f : Mathf.Clamp01(p * 3f));

            // Track layers.
            var glowAlpha = m_Completed ? 0.75f : 0.28f + 0.22f * m_Hover + 0.35f * p;
            SetColor(m_GlowMat, themeGlow, glowAlpha);
            SetColor(m_TintMat, themeTint, 0.32f + 0.10f * m_Hover);
            SetColor(m_RimMat, Color.white, 0.8f + 0.2f * m_Hover);

            // Label: sits right of the resting knob, fades as the knob moves over it.
            var labelLeft = -m_Width * 0.5f + margin + knob;
            var labelRight = m_Width * 0.5f - margin;
            var labelCenter = m_Completed ? 0f : (labelLeft + labelRight) * 0.5f;
            m_LabelText.transform.localPosition = new Vector3(labelCenter, 0f, LabelZ);
            var labelColor = Color.white;
            labelColor.a = m_Completed ? 1f : Mathf.Clamp01(1f - p * 1.6f);
            m_LabelText.color = labelColor;

            // Chevron hint only while resting.
            SetColor(m_ChevronMat, Color.white, m_Completed ? 0f : Mathf.Clamp01(0.6f - p * 12f));
        }

        // ---------- Construction ----------

        void Build()
        {
            if (m_Built)
                return;
            m_Built = true;
            var knob = KnobDiameter(m_Height);
            var margin = Margin(m_Height);

            CreateQuad("Glow", Load(ref m_TrackGlow, "slider_track_glow"), new Vector2(m_Width * 1.25f, m_Height * 2.06f), GlowZ, 0, out m_GlowMat);

            if (m_GlassMaterial != null)
            {
                var glass = new GameObject("Glass");
                glass.transform.SetParent(transform, false);
                m_GlassMesh = new Mesh { name = "Glass Slider Pill", hideFlags = HideFlags.DontSave };
                BuildRoundedRect(m_GlassMesh, m_Width, m_Height, m_Height * 0.5f, 14);
                glass.AddComponent<MeshFilter>().sharedMesh = m_GlassMesh;
                var glassRenderer = glass.AddComponent<MeshRenderer>();
                glassRenderer.sharedMaterial = m_GlassMaterial;
                glassRenderer.sortingOrder = 1;
                PrepareRenderer(glassRenderer);
            }

            CreateQuad("Tint", Load(ref m_TrackTint, "slider_track_tint"), new Vector2(m_Width, m_Height), TintZ, 2, out m_TintMat);

            var fill = new GameObject("Trail");
            fill.transform.SetParent(transform, false);
            m_FillT = fill.transform;
            m_FillMesh = new Mesh { name = "Glass Slider Trail", hideFlags = HideFlags.DontSave };
            fill.AddComponent<MeshFilter>().sharedMesh = m_FillMesh;
            var fillRenderer = fill.AddComponent<MeshRenderer>();
            m_FillMat = CreateUnlitMaterial("Glass Slider Trail", Load(ref m_Fill, "slider_fill"), 3);
            fillRenderer.sharedMaterial = m_FillMat;
            fillRenderer.sortingOrder = 3;
            PrepareRenderer(fillRenderer);

            CreateQuad("Rim", Load(ref m_TrackRim, "slider_track_rim"), new Vector2(m_Width, m_Height), RimZ, 4, out m_RimMat);

            var labelObject = new GameObject("Label", typeof(TextMeshPro));
            labelObject.transform.SetParent(transform, false);
            labelObject.transform.localScale = Vector3.one * 0.03f;
            m_LabelText = labelObject.GetComponent<TextMeshPro>();
            if (m_Font != null) m_LabelText.font = m_Font;
            m_LabelText.text = m_Label;
            m_LabelText.fontSize = m_LabelHeight / 0.0029f;
            m_LabelText.fontStyle = FontStyles.Bold;
            m_LabelText.characterSpacing = 4f;
            m_LabelText.alignment = TextAlignmentOptions.Center;
            m_LabelText.rectTransform.sizeDelta = new Vector2((m_Width - 2f * margin) / 0.03f, m_Height / 0.03f);
            m_LabelText.color = Color.white;
            m_LabelText.transform.localPosition = new Vector3(0f, 0f, LabelZ);
            m_LabelText.renderer.sortingOrder = 5;

            m_Chevrons = CreateQuad("Chevrons", Load(ref m_IconChevrons, "slider_icon_chevrons"),
                new Vector2(knob * 0.75f, knob * 0.25f), ChevronZ, 5, out m_ChevronMat).transform;
            m_Chevrons.localPosition = new Vector3(m_Width * 0.5f - margin - knob * 0.45f, 0f, ChevronZ);

            // Keep the soft halo behind the glass pill. At the foreground depth it
            // visually covers the capsule with a circular translucent patch.
            m_KnobGlowT = CreateQuad("Knob Glow", Load(ref m_KnobGlow, "slider_knob_glow"), Vector2.one, KnobGlowZ, 1, out m_KnobGlowMat).transform;
            var knobTexture = m_Theme == Theme.Candy ? Load(ref m_KnobCandy, "slider_knob_candy") : Load(ref m_KnobNature, "slider_knob_nature");
            var knobRenderer = CreateQuad("Knob", knobTexture, Vector2.one, KnobZ, 7, out m_KnobMat);
            m_Knob = knobRenderer.transform;
            m_Icon = CreateQuad("Icon", Load(ref m_IconArrow, "slider_icon_arrow"), Vector2.one, IconZ, 8, out m_IconMat).transform;

            // Whole-track collider so a drag can start anywhere on the pill.
            var trackCollider = gameObject.AddComponent<BoxCollider>();
            trackCollider.size = new Vector3(m_Width, m_Height, 0.02f);
            // Knob collider with the system gaze highlight.
            var knobCollider = knobRenderer.gameObject.AddComponent<SphereCollider>();
            knobCollider.radius = 0.5f;

            m_Audio = gameObject.AddComponent<AudioSource>();
            m_Audio.playOnAwake = false;
            m_Audio.spatialBlend = 1f;
            m_Audio.minDistance = 0.3f;

            ApplyVisuals(0f);
            ConfigureNativeSorting();
        }

        void ConfigureNativeSorting()
        {
            // RealityKit needs a native sorting group: Unity sortingOrder/renderQueue
            // alone does not establish a stable order for overlapping transparent UI.
            // Build TMP fallback-font submeshes before registering its descendants.
            m_LabelText.ForceMeshUpdate();
            var group = GetComponent<VisionOSSortingGroup>() ?? gameObject.AddComponent<VisionOSSortingGroup>();
            group.DepthPass = VisionOSSortingGroup.DepthPassType.PostPass;
            group.Renderers.Clear();
            var names = new[] { "Glow", "Knob Glow", "Glass", "Tint", "Trail", "Rim", "Label", "Chevrons", "Knob", "Icon" };
            for (var order = 0; order < names.Length; order++)
            {
                var layer = transform.Find(names[order]);
                if (layer == null) continue;
                group.Renderers.Add(new VisionOSSortingGroup.RendererSorting
                {
                    Renderer = layer.gameObject,
                    Order = order,
                    ApplyToDescendants = true
                });
            }
        }

        /// <summary>Swaps the knob colours, e.g. when the same control is reused for another world.</summary>
        public void SetTheme(Theme theme)
        {
            m_Theme = theme;
            if (!m_Built) return;
            m_KnobMat.SetTexture("_BaseMap", theme == Theme.Candy
                ? Load(ref m_KnobCandy, "slider_knob_candy")
                : Load(ref m_KnobNature, "slider_knob_nature"));
        }

        MeshRenderer CreateQuad(string objectName, Texture2D texture, Vector2 size, float z, int order, out Material material)
        {
            var quad = new GameObject(objectName);
            quad.transform.SetParent(transform, false);
            quad.transform.localPosition = new Vector3(0f, 0f, z);
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);
            quad.AddComponent<MeshFilter>().sharedMesh = SharedQuad();
            var renderer = quad.AddComponent<MeshRenderer>();
            material = CreateUnlitMaterial("Glass Slider " + objectName, texture, order);
            renderer.sharedMaterial = material;
            renderer.sortingOrder = order;
            PrepareRenderer(renderer);
            return renderer;
        }

        static Material CreateUnlitMaterial(string materialName, Texture2D texture, int order)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var material = new Material(shader) { name = materialName, hideFlags = HideFlags.DontSave };
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_SrcBlendAlpha")) material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            if (material.HasProperty("_DstBlendAlpha")) material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.renderQueue = (int)RenderQueue.Transparent + order;
            if (texture != null) material.SetTexture("_BaseMap", texture);
            return material;
        }

        static void PrepareRenderer(MeshRenderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        static void SetColor(Material material, Color color, float alpha)
        {
            if (material == null) return;
            color.a = Mathf.Clamp01(alpha);
            material.SetColor("_BaseColor", color);
        }

        Color ThemeTint() => m_Theme == Theme.Candy ? new Color(1f, 0.67f, 0.84f) : new Color(0.67f, 0.9f, 0.75f);
        Color ThemeGlow() => m_Theme == Theme.Candy ? new Color(1f, 0.55f, 0.78f) : new Color(0.47f, 0.86f, 0.59f);
        Color ThemeFill() => m_Theme == Theme.Candy ? new Color(0.96f, 0.51f, 0.75f) : new Color(0.38f, 0.77f, 0.47f);

        static Texture2D Load(ref Texture2D field, string resourceName)
        {
            if (field == null)
            {
                field = Resources.Load<Texture2D>(ResourceFolder + resourceName);
                if (field == null)
                    Debug.LogWarning($"[Glass Slider] Missing texture Resources/{ResourceFolder}{resourceName}.png");
            }
            return field;
        }

        void Play(AudioClip clip)
        {
            if (clip != null && m_Audio != null)
                m_Audio.PlayOneShot(clip);
        }

        static Mesh s_Quad;

        /// <summary>Unit quad facing -Z (towards the viewer), same winding as Unity's built-in Quad.</summary>
        static Mesh SharedQuad()
        {
            if (s_Quad != null) return s_Quad;
            s_Quad = new Mesh { name = "Glass Slider Quad", hideFlags = HideFlags.DontSave };
            s_Quad.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
            };
            s_Quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            s_Quad.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            s_Quad.triangles = new[] { 0, 3, 1, 3, 0, 2 };
            s_Quad.RecalculateBounds();
            return s_Quad;
        }

        /// <summary>
        /// Rounded rectangle centred on the origin, facing -Z. UVs span the rectangle (0..1),
        /// so a horizontal gradient texture stretches across the full width.
        /// </summary>
        public static void BuildRoundedRect(Mesh mesh, float width, float height, float radius, int segmentsPerCorner)
        {
            radius = Mathf.Clamp(radius, 0f, Mathf.Min(width, height) * 0.5f);
            var hw = width * 0.5f;
            var hh = height * 0.5f;
            var corners = new[]
            {
                new Vector2(hw - radius, hh - radius),
                new Vector2(-hw + radius, hh - radius),
                new Vector2(-hw + radius, -hh + radius),
                new Vector2(hw - radius, -hh + radius)
            };
            var perimeterCount = 4 * (segmentsPerCorner + 1);
            var vertices = new Vector3[perimeterCount + 1];
            var uvs = new Vector2[perimeterCount + 1];
            var normals = new Vector3[perimeterCount + 1];
            vertices[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);
            normals[0] = Vector3.back;
            var index = 1;
            for (var c = 0; c < 4; c++)
            {
                for (var s = 0; s <= segmentsPerCorner; s++)
                {
                    var angle = (c * 90f + s * 90f / segmentsPerCorner) * Mathf.Deg2Rad;
                    var point = corners[c] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    vertices[index] = new Vector3(point.x, point.y, 0f);
                    uvs[index] = new Vector2((point.x + hw) / Mathf.Max(width, 0.0001f), (point.y + hh) / Mathf.Max(height, 0.0001f));
                    normals[index] = Vector3.back;
                    index++;
                }
            }
            // Perimeter runs counter-clockwise in XY; (centre, next, current) is clockwise when
            // seen from -Z, which is Unity's front face for a viewer looking along +Z.
            var triangles = new int[perimeterCount * 3];
            for (var i = 0; i < perimeterCount; i++)
            {
                var current = 1 + i;
                var next = 1 + (i + 1) % perimeterCount;
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = next;
                triangles[i * 3 + 2] = current;
            }
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }

        static void StepSpring(ref float value, ref float velocity, float target, float frequency, float damping, float dt)
        {
            if (dt <= 0f) return;
            var omega = 2f * Mathf.PI * frequency;
            var steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.008f));
            var h = dt / steps;
            for (var i = 0; i < steps; i++)
            {
                var acceleration = omega * omega * (target - value) - 2f * damping * omega * velocity;
                velocity += acceleration * h;
                value += velocity * h;
            }
        }

        void OnDestroy()
        {
            foreach (var material in new[] { m_GlowMat, m_TintMat, m_FillMat, m_RimMat, m_ChevronMat, m_KnobGlowMat, m_KnobMat, m_IconMat })
                if (material != null) Destroy(material);
            if (m_FillMesh != null) Destroy(m_FillMesh);
            if (m_GlassMesh != null) Destroy(m_GlassMesh);
        }
    }
}
