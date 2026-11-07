using TMPro;
using UnityEngine;

namespace VFXViewer
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TMP_Text))]
    public sealed class FrameRateTextDisplay : MonoBehaviour
    {
        private const float SampleIntervalSeconds = 0.25f;
        private const float SmoothingFactor = 0.35f;

        private TMP_Text m_TargetText;
        private float m_AccumulatedTime;
        private int m_AccumulatedFrames;
        private float m_SmoothedFps;

        private void Awake()
        {
            m_TargetText = GetComponent<TMP_Text>();
            ResetSampling();
            UpdateText(0f);
        }

        private void OnEnable()
        {
            ResetSampling();
            UpdateText(0f);
        }

        private void Update()
        {
            if (m_TargetText == null)
                return;

            float deltaTime = Time.unscaledDeltaTime;
            if (deltaTime <= Mathf.Epsilon)
                return;

            m_AccumulatedTime += deltaTime;
            m_AccumulatedFrames++;
            if (m_AccumulatedTime < SampleIntervalSeconds)
                return;

            float currentFps = m_AccumulatedFrames / m_AccumulatedTime;
            m_SmoothedFps = m_SmoothedFps <= Mathf.Epsilon
                ? currentFps
                : Mathf.Lerp(m_SmoothedFps, currentFps, SmoothingFactor);

            UpdateText(m_SmoothedFps);
            ResetSampling();
        }

        private void ResetSampling()
        {
            m_AccumulatedTime = 0f;
            m_AccumulatedFrames = 0;
        }

        private void UpdateText(float fps)
        {
            m_TargetText.text = fps <= Mathf.Epsilon
                ? "FPS --"
                : $"FPS {fps:0.0}";
        }
    }
}
