using UnityEngine;

namespace Moodium.MoodCheck
{
    public sealed class MoodCheckAutoPromptPolicy
    {
        public const float DelaySeconds = 12f;
        public float ElapsedWithoutEntries { get; private set; }

        public bool Tick(float deltaSeconds, int activeEntryCount, bool hasSample, bool alreadyShown)
        {
            if (hasSample || alreadyShown || activeEntryCount > 0)
            {
                ResetNoEntryTimer();
                return false;
            }
            ElapsedWithoutEntries += Mathf.Max(0f, deltaSeconds);
            return ElapsedWithoutEntries >= DelaySeconds;
        }

        public void ResetNoEntryTimer() => ElapsedWithoutEntries = 0f;
    }

    public sealed class MoodRecommendationTagPulse : MonoBehaviour
    {
        Vector3 m_BaseScale;
        void Awake() => m_BaseScale = transform.localScale;
        void OnEnable()
        {
            if (m_BaseScale == Vector3.zero) m_BaseScale = transform.localScale;
        }
        void Update()
        {
            var scale = 1f + .025f * (1f + Mathf.Sin(Time.unscaledTime * Mathf.PI));
            transform.localScale = m_BaseScale * scale;
        }
    }
}
