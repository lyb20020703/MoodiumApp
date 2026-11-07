using Moodium.Interaction;
using UnityEngine;

namespace Moodium.Reality
{
    public sealed class CandyPhysicsObject : MonoBehaviour
    {
        [SerializeField, Min(0.05f)] float m_MinLandingSpeed = 0.18f;
        [SerializeField, Range(0.1f, 1f)] float m_LandingEffectScale = 0.42f;
        [SerializeField, Range(0f, 1f)] float m_Bounciness = 0.28f;

        GameObject m_LandingEffectPrefab;
        MoodiumTouchAudio m_TouchAudio;
        bool m_Landed;

        public void ConfigureFromPrefabFeedback()
        {
            var feedback = GetComponent<TouchParticleFeedback>();
            m_LandingEffectPrefab = feedback != null ? feedback.NativeBaselinePrefab : null;
            m_TouchAudio = GetComponent<MoodiumTouchAudio>();

            foreach (var collider in GetComponentsInChildren<Collider>(true))
            {
                var material = new PhysicsMaterial("Moodium Soft Candy Landing")
                {
                    bounciness = m_Bounciness,
                    dynamicFriction = 0.38f,
                    staticFriction = 0.44f,
                    bounceCombine = PhysicsMaterialCombine.Average,
                    frictionCombine = PhysicsMaterialCombine.Average
                };
                collider.material = material;
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            if (m_Landed || collision.relativeVelocity.magnitude < m_MinLandingSpeed ||
                collision.collider.GetComponentInParent<SpatialTableSurface>() == null)
                return;
            m_Landed = true;
            var point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            if (m_LandingEffectPrefab != null)
            {
                var effect = Instantiate(m_LandingEffectPrefab, point, Quaternion.identity);
                effect.name = "Moodium Soft Table Landing Feedback";
                effect.transform.localScale *= m_LandingEffectScale;
                foreach (var particles in effect.GetComponentsInChildren<ParticleSystem>(true))
                    particles.Play(true);
            }
            m_TouchAudio?.Play();
            Debug.Log($"[Moodium Candy Physics] Soft table landing: {name}, speed={collision.relativeVelocity.magnitude:0.###}");
        }
    }
}
