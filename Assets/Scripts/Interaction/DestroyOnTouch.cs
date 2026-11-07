using System.Collections;
using Moodium.Flow;
using UnityEngine;
using UnityEngine.Scripting;

namespace Moodium.Interaction
{
    /// <summary>Hand-only pop interaction for falling Spatial Physics objects.</summary>
    [Preserve]
    public sealed class DestroyOnTouch : MonoBehaviour
    {
        [SerializeField, Range(0.08f, 0.6f)] float m_DisappearDuration = 0.22f;
        [SerializeField, Min(0f)] float m_ArmingDelay = 0.15f;

        bool m_Disappearing;
        bool m_AlwaysSpawnCircularBurst;
        float m_ReadyAt;
        ChocolateCapsuleInteraction m_ShellCrackCapsule;

        /// <summary>
        /// Enables the compact circular pop in addition to the object's normal touch feedback.
        /// Used only by the full-energy reward rain.
        /// </summary>
        public void ConfigureRewardBurstFeedback() => m_AlwaysSpawnCircularBurst = true;

        /// <summary>Changes this release reward's hand-contact response to its authored ShellCrack animation.</summary>
        public void ConfigureShellCrackFeedback(ChocolateCapsuleInteraction capsule) =>
            m_ShellCrackCapsule = capsule;

        /// <summary>Allows a reward lifecycle to dismiss an untouched grounded object.</summary>
        public void DisappearAutomatically() => BeginDisappear();

        void OnEnable()
        {
            m_ReadyAt = Time.time + m_ArmingDelay;
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!CanReact(collision.gameObject))
                return;
            BeginDisappear();
        }

        void OnTriggerEnter(Collider other)
        {
            if (CanReact(other.gameObject))
                BeginDisappear();
        }

        bool CanReact(GameObject other)
        {
            if (m_Disappearing || Time.time < m_ReadyAt || other == null)
                return false;

            var current = other.transform;
            while (current != null)
            {
                if (current.name.StartsWith("HandCollider", System.StringComparison.Ordinal))
                    return true;
                current = current.parent;
            }
            return false;
        }

        void BeginDisappear()
        {
            if (m_Disappearing)
                return;
            m_Disappearing = true;

            var touchAudio = GetComponent<MoodiumTouchAudio>();
            if (touchAudio != null)
                touchAudio.Play();

            var spawnedCircularBurst = false;
            var particleFeedback = GetComponent<TouchParticleFeedback>();
            if (particleFeedback != null && particleFeedback.HasParticle)
                particleFeedback.Play();
            else
            {
                var effect = MoodiumInteractionVFXManager.PlayInteractionEffect(
                    GetVisualCenter(),
                    transform.rotation,
                    gameObject);
                if (effect == null)
                {
                    effect = ParticleBurstEffect.Spawn(GetVisualCenter());
                    MoodiumRuntimeObjectRegistry.RegisterCreative(effect);
                    spawnedCircularBurst = true;
                }
            }

            if (m_AlwaysSpawnCircularBurst && !spawnedCircularBurst)
            {
                var circularBurst = ParticleBurstEffect.Spawn(GetVisualCenter());
                MoodiumRuntimeObjectRegistry.RegisterCreative(circularBurst);
            }

            foreach (var collider in GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            var body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.useGravity = false;
                body.isKinematic = true;
            }

            if (m_ShellCrackCapsule != null &&
                m_ShellCrackCapsule.PlayShellCrackForReward(() => StartCoroutine(Disappear())))
                return;

            StartCoroutine(Disappear());
            Debug.Log($"[Moodium Physics] Hand touched falling object: {name}");
        }

        Vector3 GetVisualCenter()
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return transform.position;
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds.center;
        }

        IEnumerator Disappear()
        {
            var startScale = transform.localScale;
            var elapsed = 0f;
            while (elapsed < m_DisappearDuration)
            {
                elapsed += Time.deltaTime;
                var progress = Mathf.Clamp01(elapsed / m_DisappearDuration);
                var eased = progress * progress * (3f - 2f * progress);
                transform.localScale = Vector3.LerpUnclamped(startScale, Vector3.zero, eased);
                yield return null;
            }
            transform.localScale = Vector3.zero;
            Destroy(gameObject);
        }
    }
}
