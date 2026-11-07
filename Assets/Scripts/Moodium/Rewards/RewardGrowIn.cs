using UnityEngine;

namespace Moodium.Rewards
{
    [DisallowMultipleComponent]
    public sealed class RewardGrowIn : MonoBehaviour
    {
        const float Duration = 1.3f;
        Vector3 m_FinalPosition;
        Vector3 m_FinalScale;
        float m_TargetHeight;
        float m_StartedAt;
        bool m_Begun;

        public void Begin(float surfaceY, float targetHeight)
        {
            m_TargetHeight = Mathf.Max(.01f, targetHeight);
            SnapBaseToSurface(gameObject, surfaceY);
            m_FinalPosition = transform.position;
            m_FinalScale = transform.localScale;
            transform.position = m_FinalPosition - Vector3.up * (m_TargetHeight * .6f);
            transform.localScale = m_FinalScale * .3f;
            m_StartedAt = Time.unscaledTime;
            m_Begun = true;
        }

        public static void SnapBaseToSurface(GameObject target, float surfaceY)
        {
            if (target == null)
                return;
            var found = false;
            var lowest = float.PositiveInfinity;
            foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
                if (renderer.enabled)
                {
                    lowest = Mathf.Min(lowest, renderer.bounds.min.y);
                    found = true;
                }
            if (found)
                target.transform.position += Vector3.up * (surfaceY - lowest);
        }

        public static float EvaluateScale(float normalizedTime)
        {
            var t = Mathf.Clamp01(normalizedTime);
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            var eased = 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
            return Mathf.LerpUnclamped(.3f, 1f, eased);
        }

        void Update()
        {
            if (!m_Begun)
                return;
            var t = Mathf.Clamp01((Time.unscaledTime - m_StartedAt) / Duration);
            transform.position = Vector3.LerpUnclamped(m_FinalPosition - Vector3.up * (m_TargetHeight * .6f), m_FinalPosition, t);
            transform.localScale = m_FinalScale * EvaluateScale(t);
            if (t >= 1f)
                enabled = false;
        }
    }
}
