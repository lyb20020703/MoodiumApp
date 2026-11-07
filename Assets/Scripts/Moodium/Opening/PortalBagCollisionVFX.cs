using Moodium.Interaction;
using UnityEngine;

namespace Moodium.Opening
{
    [DisallowMultipleComponent]
    public sealed class PortalBagCollisionVFX : MonoBehaviour
    {
        // The shared opening-candy emitter can only show one burst at a time.
        // A global cooldown also prevents a settled pile from becoming a VFX source.
        static float s_NextGlobalBurstTime;

        [SerializeField, Min(0.01f)] float m_MinImpactSpeed = 0.16f;
        [SerializeField, Min(0.05f)] float m_GlobalCooldown = 0.18f;

        void OnCollisionEnter(Collision collision)
        {
            if (collision == null || collision.relativeVelocity.magnitude < m_MinImpactSpeed)
                return;

            if (collision.collider == null ||
                collision.collider.GetComponentInParent<PortalBagCollisionVFX>() == null)
                return;

            if (Time.time < s_NextGlobalBurstTime)
                return;

            s_NextGlobalBurstTime = Time.time + m_GlobalCooldown;
            var position = collision.contactCount > 0
                ? collision.GetContact(0).point
                : transform.position;
            MoodiumInteractionVFXManager.PlayInteractionEffect(position, Quaternion.identity, gameObject);
        }
    }
}
