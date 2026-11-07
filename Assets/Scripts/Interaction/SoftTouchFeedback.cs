using System.Collections;
using UnityEngine;

namespace Moodium.Interaction
{
    /// <summary>Configurable squash, overshoot and settle feedback that preserves edited scale.</summary>
    public sealed class SoftTouchFeedback : MonoBehaviour
    {
        [SerializeField, Range(0.5f, 1f)] float m_SquashScale = 0.9f;
        [SerializeField, Range(0f, 0.25f)] float m_StretchAmount = 0.06f;
        [SerializeField, Range(0f, 0.3f)] float m_BounceAmount = 0.05f;
        [SerializeField, Range(0.12f, 0.8f)] float m_AnimationDuration = 0.3f;

        Vector3 m_BaseScale;
        Coroutine m_Animation;

        public bool FeedbackEnabled { get; private set; }
        public bool IsPlaying => m_Animation != null;
        public float SquashScale => m_SquashScale;
        public float BounceAmount => m_BounceAmount;
        public float StretchAmount => m_StretchAmount;
        public float AnimationDuration => m_AnimationDuration;

        void Awake()
        {
            m_BaseScale = transform.localScale;
        }

        public void SetFeedbackEnabled(bool enabled)
        {
            if (FeedbackEnabled == enabled)
                return;

            FeedbackEnabled = enabled;
            if (enabled)
                m_BaseScale = transform.localScale;
            else
                StopFeedback(true);
        }

        public void TriggerFeedback()
        {
            if (!FeedbackEnabled || !isActiveAndEnabled)
                return;

            StopFeedback(true);
            m_Animation = StartCoroutine(Animate());
            Debug.Log($"[Moodium Soft Touch] Triggered: {name}, baseScale={m_BaseScale}");
        }

        IEnumerator Animate()
        {
            var squashDuration = m_AnimationDuration * 0.27f;
            var bounceDuration = m_AnimationDuration * 0.33f;
            var settleDuration = Mathf.Max(0.01f, m_AnimationDuration - squashDuration - bounceDuration);

            var squash = Vector3.Scale(
                m_BaseScale,
                new Vector3(1f + m_StretchAmount, m_SquashScale, 1f + m_StretchAmount));
            yield return ScaleTo(squash, squashDuration, EaseOut);
            yield return ScaleTo(m_BaseScale * (1f + m_BounceAmount), bounceDuration, EaseOut);
            yield return ScaleTo(m_BaseScale, settleDuration, EaseInOut);
            transform.localScale = m_BaseScale;
            m_Animation = null;
        }

        IEnumerator ScaleTo(Vector3 target, float duration, System.Func<float, float> easing)
        {
            var start = transform.localScale;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var progress = Mathf.Clamp01(elapsed / duration);
                transform.localScale = Vector3.LerpUnclamped(start, target, easing(progress));
                yield return null;
            }
            transform.localScale = target;
        }

        void StopFeedback(bool restoreScale)
        {
            if (m_Animation != null)
            {
                StopCoroutine(m_Animation);
                m_Animation = null;
            }
            if (restoreScale)
                transform.localScale = m_BaseScale;
        }

        static float EaseOut(float value) => 1f - Mathf.Pow(1f - value, 3f);

        static float EaseInOut(float value) =>
            value < 0.5f
                ? 4f * value * value * value
                : 1f - Mathf.Pow(-2f * value + 2f, 3f) * 0.5f;
    }
}
