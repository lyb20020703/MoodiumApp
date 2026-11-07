using UnityEngine;

namespace Moodium.NatureWorld
{
    /// <summary>Short scale-in that makes each forest addition feel grown rather than popped in.</summary>
    public sealed class NatureTreeGrowIn : MonoBehaviour
    {
        const float Duration = 0.75f;
        Vector3 m_FinalScale;
        float m_Elapsed;

        void Awake()
        {
            m_FinalScale = transform.localScale;
            transform.localScale = m_FinalScale * 0.08f;
        }

        void Update()
        {
            m_Elapsed = Mathf.Min(Duration, m_Elapsed + Time.deltaTime);
            var t = 1f - Mathf.Pow(1f - m_Elapsed / Duration, 3f);
            transform.localScale = Vector3.LerpUnclamped(m_FinalScale * 0.08f, m_FinalScale, t);
            if (m_Elapsed >= Duration)
                enabled = false;
        }
    }
}
