using UnityEngine;

namespace Moodium.Flow
{
    [DisallowMultipleComponent]
    public sealed class MoodiBreathingFloat : MonoBehaviour
    {
        const float MinimumPeriodSeconds = .001f;

        [SerializeField, Min(0f)] float m_VerticalAmplitude = .01f;
        [SerializeField, Min(MinimumPeriodSeconds)] float m_PeriodSeconds = 2.8f;
        [SerializeField, Min(0f)] float m_ScaleAmplitude = .01f;

        Vector3 m_BaselineLocalPosition;
        Vector3 m_BaselineLocalScale;
        float m_StartedAt;
        bool m_BaselineCaptured;

        public static float EvaluateOffset(float elapsedSeconds, float amplitude, float periodSeconds)
        {
            var safePeriod = SanitizePeriod(periodSeconds);
            return Mathf.Sin(elapsedSeconds / safePeriod * Mathf.PI * 2f) * amplitude;
        }

        static float SanitizePeriod(float periodSeconds)
        {
            if (float.IsNaN(periodSeconds) || float.IsInfinity(periodSeconds))
                return MinimumPeriodSeconds;

            return Mathf.Max(MinimumPeriodSeconds, periodSeconds);
        }

        public static Vector3 EvaluateScale(Vector3 baseline, float normalizedWave, float scaleAmplitude)
        {
            return baseline * (1f + normalizedWave * scaleAmplitude);
        }

        void OnEnable()
        {
            m_BaselineLocalPosition = transform.localPosition;
            m_BaselineLocalScale = transform.localScale;
            m_StartedAt = Time.unscaledTime;
            m_BaselineCaptured = true;
        }

        void Update()
        {
            var elapsed = Time.unscaledTime - m_StartedAt;
            var wave = EvaluateOffset(elapsed, 1f, m_PeriodSeconds);
            transform.localPosition = m_BaselineLocalPosition + Vector3.up * (wave * m_VerticalAmplitude);
            transform.localScale = EvaluateScale(m_BaselineLocalScale, wave, m_ScaleAmplitude);
        }

        void OnDisable()
        {
            if (!m_BaselineCaptured)
                return;

            transform.localPosition = m_BaselineLocalPosition;
            transform.localScale = m_BaselineLocalScale;
        }

        void OnValidate()
        {
            m_VerticalAmplitude = Mathf.Max(0f, m_VerticalAmplitude);
            m_PeriodSeconds = SanitizePeriod(m_PeriodSeconds);
            m_ScaleAmplitude = Mathf.Max(0f, m_ScaleAmplitude);
        }
    }
}
