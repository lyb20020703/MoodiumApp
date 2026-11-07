using TMPro;
using UnityEngine;

namespace Moodium.Opening
{
    public sealed class OpeningPromptView : MonoBehaviour
    {
        [SerializeField] TMP_Text[] m_Texts;
        [SerializeField, Range(0f, 1f)] float m_MinAlpha = 0.5f;
        [SerializeField, Min(0.1f)] float m_BreathSpeed = 1.4f;
        [SerializeField, Min(0f)] float m_FloatAmplitude = 0.008f;

        Vector3 m_Origin;

        void Awake() => m_Origin = transform.localPosition;

        void Update()
        {
            var wave = (Mathf.Sin(Time.unscaledTime * m_BreathSpeed) + 1f) * 0.5f;
            var alpha = Mathf.Lerp(m_MinAlpha, 1f, wave);
            transform.localPosition = m_Origin + Vector3.up * ((wave - 0.5f) * 2f * m_FloatAmplitude);
            if (m_Texts == null)
                return;
            foreach (var text in m_Texts)
            {
                if (text == null)
                    continue;
                var color = text.color;
                color.a = alpha;
                text.color = color;
            }
        }
    }
}
