using System;
using TMPro;
using UnityEngine;

namespace Moodium.Flow
{
    public sealed class MoodiumSpatialButton : MonoBehaviour
    {
        [SerializeField] TMP_Text m_Label;
        [SerializeField] Renderer m_Background;

        Action m_OnPressed;
        Color m_BaseColor;
        string m_ColorProperty;
        Vector3 m_BaseScale;
        bool m_AnimateTransform;
        bool m_Hovered;
        bool m_Pressing;
        float m_LastPress = float.NegativeInfinity;

        /// <summary>
        /// One poke can arrive twice: SensoryBagDirectTouchInput presses when the fingertip is 12 mm away, then
        /// visionOS sends its own Touch Began for the same contact. Presses closer together than this are one press.
        /// </summary>
        public const float PressCooldown = .3f;
        public static bool IsRepeat(float lastPress, float now) => now - lastPress < PressCooldown;

        public TMP_Text Label => m_Label;

        public void Configure(TMP_Text label, Renderer background, Action onPressed, bool animateTransform = false)
        {
            m_Label = label;
            m_Background = background;
            m_OnPressed = onPressed;
            m_AnimateTransform = animateTransform;
            m_BaseScale = transform.localScale;
            if (m_Background != null)
            {
                var material = m_Background.sharedMaterial;
                m_ColorProperty = material.HasProperty("_Tint") ? "_Tint" :
                    material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
                m_BaseColor = material.HasProperty(m_ColorProperty)
                    ? material.GetColor(m_ColorProperty)
                    : Color.white;
            }
        }

        public void SetHovered(bool hovered)
        {
            m_Hovered = hovered;
            GetComponent<Moodium.UI.UIRefreshSpriteButton>()?.SetHovered(hovered);
        }

        public void SetSelected(bool selected)
        {
            if (m_Label != null)
                m_Label.color = selected ? new Color(.05f, .05f, .08f, 1f) : Color.white;
            if (m_Background == null)
                return;
            var material = m_Background.material;
            if (material.HasProperty(m_ColorProperty))
                material.SetColor(m_ColorProperty, selected ? new Color(1f, 1f, 1f, .96f) : m_BaseColor);
        }

        void Update()
        {
            if (!m_AnimateTransform || m_Pressing)
                return;
            var target = m_Hovered ? m_BaseScale * 1.025f : m_BaseScale;
            transform.localScale = Vector3.Lerp(transform.localScale, target, 1f - Mathf.Exp(-12f * Time.deltaTime));
        }

        public void Press()
        {
            var now = Time.realtimeSinceStartup;
            if (IsRepeat(m_LastPress, now)) return;
            m_LastPress = now;
            Debug.Log($"[Moodium Flow] Button pressed: {(m_Label != null ? m_Label.text : name)}");
            if (m_AnimateTransform)
                StartCoroutine(PressCard());
            else
            {
                m_OnPressed?.Invoke();
                if (m_Background != null && Application.isPlaying)
                    StartCoroutine(Flash());
            }
        }

        System.Collections.IEnumerator PressCard()
        {
            if (m_Pressing)
                yield break;
            m_Pressing = true;
            yield return AnimateScale(m_BaseScale * 0.97f, 0.055f);
            yield return AnimateScale(m_BaseScale * 1.018f, 0.07f);
            yield return AnimateScale(m_BaseScale, 0.055f);
            m_Pressing = false;
            m_OnPressed?.Invoke();
        }

        System.Collections.IEnumerator AnimateScale(Vector3 target, float duration)
        {
            var start = transform.localScale;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var eased = t * t * (3f - 2f * t);
                transform.localScale = Vector3.LerpUnclamped(start, target, eased);
                yield return null;
            }
            transform.localScale = target;
        }

        System.Collections.IEnumerator Flash()
        {
            var material = m_Background.material;
            if (material.HasProperty(m_ColorProperty))
                material.SetColor(m_ColorProperty, Color.Lerp(m_BaseColor, Color.white, 0.28f));
            yield return new WaitForSeconds(0.12f);
            if (material.HasProperty(m_ColorProperty))
                material.SetColor(m_ColorProperty, m_BaseColor);
        }
    }
}
