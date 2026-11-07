using System;
using UnityEngine;

namespace Moodium.MoodCheck
{
    [DisallowMultipleComponent]
    public sealed class MoodCheckPlate : MonoBehaviour
    {
        [SerializeField] float m_EffectiveRadius = .112f;
        [SerializeField] Transform m_Token;
        [SerializeField] Transform m_TokenShadow;
        [SerializeField] Transform m_PlateGlow;
        [SerializeField] Transform m_Trail;
        [SerializeField] SpriteRenderer m_MoodiSprite;
        [SerializeField] SpriteRenderer m_TintBase;
        [SerializeField] SpriteRenderer m_GlowRenderer;
        [SerializeField] SpriteRenderer[] m_QuadrantIcons;
        [SerializeField] Sprite[] m_MoodSprites;

        Vector2 m_NormalizedPosition;
        Color m_TargetTint = new(.84f, .86f, .91f, .1f);
        float m_LastVisualIntensity = -1f;
        MoodKind m_LastVisualMood = (MoodKind)(-1);
        Vector3 m_TokenBaseScale = Vector3.one;
        Vector3 m_ShadowBaseScale = Vector3.one;
        SpriteRenderer m_Halo;
        Vector3 m_HaloBaseScale;
        Vector3[] m_IconBaseScales;

        public event Action<Vector2, MoodKind> Changed;
        public Vector2 NormalizedPosition => m_NormalizedPosition;
        public MoodKind CurrentMood { get; private set; } = MoodKind.Neutral;
        public bool IsDragging { get; private set; }
        public float EffectiveRadius => m_EffectiveRadius;

        public void Configure(float effectiveRadius)
        {
            m_EffectiveRadius = Mathf.Max(.001f, effectiveRadius);
            ResolveVisuals();
            ApplyPosition(true);
        }

        public void ConfigureVisuals(Sprite[] moodSprites)
        {
            m_MoodSprites = moodSprites;
            ResolveVisuals();
            UpdateMoodVisuals(true);
        }

        public Vector2 WorldToNormalized(Vector3 worldPosition)
        {
            var local = transform.InverseTransformPoint(worldPosition);
            return Vector2.ClampMagnitude(new Vector2(local.x, local.y) / m_EffectiveRadius, 1f);
        }

        public void SetFromWorldPosition(Vector3 worldPosition) =>
            SetNormalizedPosition(WorldToNormalized(worldPosition), true);

        public void SetNormalizedPosition(Vector2 position, bool immediate)
        {
            m_NormalizedPosition = Vector2.ClampMagnitude(position, 1f);
            CurrentMood = MoodClassifier.Classify(m_NormalizedPosition.x, m_NormalizedPosition.y);
            ApplyPosition(immediate);
            UpdateMoodVisuals(immediate);
            Changed?.Invoke(m_NormalizedPosition, CurrentMood);
        }

        public void BeginDrag()
        {
            ResolveVisuals();
            IsDragging = true;
            if (m_Token != null)
            {
                m_Token.localScale = m_TokenBaseScale * 1.12f;
                var position = m_Token.localPosition;
                position.z -= .008f;
                m_Token.localPosition = position;
            }
            if (m_TokenShadow != null)
                m_TokenShadow.localScale = m_ShadowBaseScale * 1.25f;
        }

        public void EndDrag()
        {
            IsDragging = false;
            if (m_Token != null)
            {
                m_Token.localScale = m_TokenBaseScale;
                var position = m_Token.localPosition;
                position.z = -.012f;
                m_Token.localPosition = position;
            }
            if (m_TokenShadow != null)
                m_TokenShadow.localScale = m_ShadowBaseScale;
        }

        void Awake()
        {
            ResolveVisuals();
            ApplyPosition(true);
        }

        void Update()
        {
            if (m_TintBase == null || m_GlowRenderer == null)
                return;
            var blend = 1f - Mathf.Exp(-10f * Time.deltaTime);
            m_TintBase.color = new Color(1f, 1f, 1f, .92f);
            m_GlowRenderer.color = Color.Lerp(m_GlowRenderer.color, new Color(1f, 1f, 1f, IsDragging ? .42f : .30f), blend);
            if (m_Halo != null)
            {
                m_Halo.color = Color.Lerp(m_Halo.color, m_TargetTint, blend);
                // Token itself already grows by 1.12 during drag.
                var factor = IsDragging ? 1.15f / 1.12f : 1f;
                m_Halo.transform.localScale = Vector3.Lerp(m_Halo.transform.localScale, m_HaloBaseScale * factor, blend);
            }
            UpdateIconEmphasis(blend);
        }

        void ResolveVisuals()
        {
            m_Token ??= transform.Find("Token");
            m_TokenShadow ??= transform.Find("TokenShadow");
            m_PlateGlow ??= transform.Find("PlateGlow");
            m_Trail ??= transform.Find("Trail");
            m_MoodiSprite ??= transform.Find("Token/MoodiSprite")?.GetComponent<SpriteRenderer>();
            m_TintBase ??= transform.Find("PlateTintBase")?.GetComponent<SpriteRenderer>();
            m_GlowRenderer ??= m_PlateGlow != null ? m_PlateGlow.GetComponent<SpriteRenderer>() : null;
            if (m_Halo == null)
            {
                m_Halo = transform.Find("Token/Halo")?.GetComponent<SpriteRenderer>();
                if (m_Halo != null) m_HaloBaseScale = m_Halo.transform.localScale;
            }
            if (m_QuadrantIcons == null || m_QuadrantIcons.Length == 0)
            {
                var root = transform.Find("QuadrantIcons");
                m_QuadrantIcons = root != null ? root.GetComponentsInChildren<SpriteRenderer>(true) : Array.Empty<SpriteRenderer>();
            }
            if (m_IconBaseScales == null || m_IconBaseScales.Length != m_QuadrantIcons.Length)
            {
                m_IconBaseScales = new Vector3[m_QuadrantIcons.Length];
                for (var i = 0; i < m_IconBaseScales.Length; i++)
                    m_IconBaseScales[i] = m_QuadrantIcons[i] != null ? m_QuadrantIcons[i].transform.localScale : Vector3.one;
            }
            if (m_Token != null && m_TokenBaseScale == Vector3.one)
                m_TokenBaseScale = m_Token.localScale;
            if (m_TokenShadow != null && m_ShadowBaseScale == Vector3.one)
                m_ShadowBaseScale = m_TokenShadow.localScale;
        }

        void ApplyPosition(bool immediate)
        {
            ResolveVisuals();
            var position = new Vector3(m_NormalizedPosition.x * m_EffectiveRadius,
                m_NormalizedPosition.y * m_EffectiveRadius, -.012f);
            if (IsDragging)
                position.z -= .008f;
            if (m_Token != null)
                m_Token.localPosition = position;
            if (m_TokenShadow != null)
                m_TokenShadow.localPosition = new Vector3(position.x, position.y - .027f, -.001f);
            if (m_PlateGlow != null)
                m_PlateGlow.localPosition = new Vector3(position.x, position.y, -.002f);
            if (m_Trail != null)
            {
                var intensity = m_NormalizedPosition.magnitude;
                m_Trail.gameObject.SetActive(false);
                m_Trail.localPosition = new Vector3(position.x * .5f, position.y * .5f, -.004f);
                m_Trail.localScale = new Vector3(.0005f, intensity * m_EffectiveRadius, 1f);
                if (intensity > 0f)
                    m_Trail.localRotation = Quaternion.Euler(0f, 0f,
                        -Mathf.Atan2(position.x, position.y) * Mathf.Rad2Deg);
            }
        }

        void UpdateMoodVisuals(bool immediate)
        {
            var intensity = MoodClassifier.Intensity(m_NormalizedPosition.x, m_NormalizedPosition.y);
            if (!immediate && CurrentMood == m_LastVisualMood && Mathf.Abs(intensity - m_LastVisualIntensity) <= .02f)
                return;
            m_LastVisualMood = CurrentMood;
            m_LastVisualIntensity = intensity;
            var tint = Tint(CurrentMood);
            m_TargetTint = new Color(tint.r, tint.g, tint.b, .95f);
            if (immediate)
            {
                if (m_TintBase != null)
                    m_TintBase.color = new Color(1f, 1f, 1f, .92f);
                if (m_GlowRenderer != null)
                    m_GlowRenderer.color = new Color(1f, 1f, 1f, IsDragging ? .42f : .30f);
                if (m_Halo != null) m_Halo.color = m_TargetTint;
            }
            if (m_MoodiSprite != null && m_MoodSprites != null && (int)CurrentMood < m_MoodSprites.Length)
                m_MoodiSprite.sprite = m_MoodSprites[(int)CurrentMood];
            if (immediate) UpdateIconEmphasis(1f);
        }

        void UpdateIconEmphasis(float blend)
        {
            for (var i = 0; i < m_QuadrantIcons.Length; i++)
            {
                var renderer = m_QuadrantIcons[i];
                if (renderer == null) continue;
                var active = (CurrentMood == MoodKind.Happy && i == 0) ||
                             (CurrentMood == MoodKind.Calm && i == 1) ||
                             (CurrentMood == MoodKind.Low && i == 2) ||
                             (CurrentMood == MoodKind.Tense && i == 3);
                var color = renderer.color;
                color.a = Mathf.Lerp(color.a, active ? .95f : .45f, blend);
                renderer.color = color;
                renderer.transform.localScale = Vector3.Lerp(renderer.transform.localScale,
                    m_IconBaseScales[i] * (active ? 1.15f : 1f), blend);
            }
        }

        public static Color Tint(MoodKind mood) => mood switch
        {
            MoodKind.Happy => new Color(1f, 207f / 255f, 110f / 255f),
            MoodKind.Calm => new Color(127f / 255f, 221f / 255f, 190f / 255f),
            MoodKind.Low => new Color(142f / 255f, 168f / 255f, 230f / 255f),
            MoodKind.Tense => new Color(240f / 255f, 134f / 255f, 118f / 255f),
            _ => new Color(228f / 255f, 230f / 255f, 240f / 255f)
        };
    }
}
