using Moodium.Flow;
using Moodium.MoodCheck;
using TMPro;
using Unity.PolySpatial;
using UnityEngine;

namespace Moodium.UI
{
    /// <summary>Authored composite sprites keep the glass chip to one transparent surface.</summary>
    public sealed class MoodEntryChip : MonoBehaviour
    {
        SpriteRenderer m_Visual;
        Sprite m_Default;
        Sprite m_Hover;
        StickerWorldSelectionController m_Owner;
        MoodiumSpatialButton m_Button;
        float m_Started;
        float m_Opacity;
        bool m_Hinted;
        float m_HintStarted = -100f;
        public bool AllowIdleHint { get; set; } = true;

        public MoodiumSpatialButton Initialize(StickerWorldSelectionController owner, TMP_FontAsset font,
            System.Action pressed)
        {
            m_Owner = owner;
            m_Started = Time.unscaledTime;
            m_Visual = gameObject.AddComponent<SpriteRenderer>();
            m_Visual.color = new Color(1f, 1f, 1f, 0f);
            var collider = gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(.27f, .056f, .02f);
            // Keep the semantic label available to existing callers; the supplied
            // composite already includes typography, so it is not rendered twice.
            var labelObject = new GameObject("Label", typeof(TextMeshPro));
            labelObject.transform.SetParent(transform, false);
            var label = labelObject.GetComponent<TextMeshPro>();
            label.font = font;
            label.renderer.enabled = false;
            m_Button = gameObject.AddComponent<MoodiumSpatialButton>();
            m_Button.Configure(label, null, pressed, animateTransform: true);
            return m_Button;
        }

        public void SetContent(bool english, MoodCheckSample sample)
        {
            var state = sample == null ? "default" : sample.mood.ToString().ToLowerInvariant();
            var language = english ? "en" : "cn";
            m_Default = Resources.Load<Sprite>($"MoodiumMoodEntry/mood_chip_{state}_{language}");
            m_Hover = sample == null
                ? Resources.Load<Sprite>($"MoodiumMoodEntry/mood_chip_hover_{language}") : m_Default;
            m_Visual.sprite = m_Default;
        }

        void Update()
        {
            var elapsed = Time.unscaledTime - m_Started;
            var tracked = m_Owner != null && m_Owner.HasVisibleEntry;
            if (AllowIdleHint && elapsed >= 12f && !m_Hinted)
            {
                m_Hinted = true;
                if (!tracked) m_HintStarted = Time.unscaledTime;
            }
            var hovered = false;
#if UNITY_EDITOR
            var mouse = UnityEngine.InputSystem.Mouse.current;
            var camera = Camera.main;
            if (mouse != null && camera != null && Physics.Raycast(
                camera.ScreenPointToRay(mouse.position.ReadValue()), out var hit, 20f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
                hovered = hit.collider.gameObject == gameObject;
            m_Button.SetHovered(hovered);
#endif
            var hint = Time.unscaledTime - m_HintStarted;
            m_Visual.sprite = hovered || (hint >= 0f && hint < 1.6f && Mathf.Sin(hint / 1.6f * Mathf.PI) > .4f)
                ? m_Hover : m_Default;
            var appeared = Mathf.Clamp01((elapsed - 1.5f) / .6f);
            m_Opacity = Mathf.MoveTowards(m_Opacity, tracked ? .55f : 1f, Time.unscaledDeltaTime / .4f);
            m_Visual.color = new Color(1f, 1f, 1f, appeared * m_Opacity);
            GetComponent<BoxCollider>().enabled = appeared > .1f;
        }
    }
}
