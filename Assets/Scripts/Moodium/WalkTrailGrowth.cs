using UnityEngine;

namespace Moodium.NatureWorld
{
    public sealed class WalkTrailGrowth : MonoBehaviour
    {
        Vector3 m_Scale;
        Vector3 m_Position;
        Vector3 m_Ground;
        Quaternion m_Rotation;
        float m_Born;
        float m_Retire = -1f;
        float m_Wave = -100f;

        public void Initialize(Vector3 ground)
        {
            m_Scale = transform.localScale;
            m_Position = transform.position;
            m_Rotation = transform.rotation;
            m_Ground = ground;
            m_Born = Time.time;
            ApplyScale(0f);
        }

        public void Retire() => m_Retire = Time.time;
        public void Wave(float delay) => m_Wave = Time.time + delay;

        void Update()
        {
            var t = Mathf.Clamp01((Time.time - m_Born) / .4f);
            var scale = t < .7f ? Mathf.SmoothStep(0f, 1.12f, t / .7f)
                : Mathf.Lerp(1.12f, 1f, Mathf.SmoothStep(0f, 1f, (t - .7f) / .3f));
            if (m_Retire >= 0f)
            {
                scale *= 1f - Mathf.Clamp01((Time.time - m_Retire) / .3f);
                if (Time.time - m_Retire >= .3f) { Destroy(gameObject); return; }
            }
            ApplyScale(scale);
            var waveTime = Time.time - m_Wave;
            var tilt = waveTime >= 0f && waveTime <= 1.2f ? Mathf.Sin(waveTime / 1.2f * Mathf.PI * 2f) * 5f : 0f;
            transform.rotation = m_Rotation * Quaternion.Euler(0f, 0f, tilt);
        }

        void ApplyScale(float scale)
        {
            transform.localScale = m_Scale * scale;
            transform.position = m_Ground + (m_Position - m_Ground) * scale;
        }
    }
}
