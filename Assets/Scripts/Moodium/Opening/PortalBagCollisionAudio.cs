using UnityEngine;

namespace Moodium.Opening
{
    [DisallowMultipleComponent]
    public sealed class PortalBagCollisionAudio : MonoBehaviour
    {
        static float s_NextPlayTime;
        AudioClip m_Clip;
        AudioSource m_Source;

        public void Configure(AudioClip clip)
        {
            m_Clip = clip;
            // UnityEngine.Object overloads == to treat a destroyed component as
            // null.  Do not use ?? here: it only performs a CLR null check, so a
            // stale AudioSource reference can survive and throw below.
            m_Source = GetComponent<AudioSource>();
            if (m_Source == null)
                m_Source = gameObject.AddComponent<AudioSource>();

            if (m_Source == null)
                return;
            m_Source.playOnAwake = false;
            m_Source.spatialBlend = 0f;
            m_Source.volume = 0.22f;
        }

        void OnCollisionEnter(Collision collision)
        {
            if (m_Clip == null || m_Source == null || collision == null ||
                collision.relativeVelocity.magnitude < 0.16f || Time.unscaledTime < s_NextPlayTime)
                return;
            if (collision.collider == null || collision.collider.GetComponentInParent<PortalBagCollisionAudio>() == null)
                return;
            s_NextPlayTime = Time.unscaledTime + 0.12f;
            m_Source.pitch = Random.Range(0.92f, 1.08f);
            m_Source.PlayOneShot(m_Clip, Random.Range(0.7f, 1f));
        }
    }
}
