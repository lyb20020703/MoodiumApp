using UnityEngine;
using Moodium.Flow;

namespace Moodium.Interaction
{
    /// <summary>Spawns a configured one-shot effect at the model's center point.</summary>
    public sealed class TouchParticleFeedback : MonoBehaviour
    {
        [SerializeField] Transform m_ParticleSpawnPoint;
        [SerializeField] GameObject m_ParticlePrefab;
        [SerializeField, Min(0.1f)] float m_MaxLifetime = 5f;

        [Header("visionOS particle adaptation")]
        [SerializeField] GameObject m_NativeBaselinePrefab;
        [SerializeField] bool m_SpawnNativeBaseline = true;
        [SerializeField, Min(0.01f)] float m_EffectScaleMultiplier = 0.35f;

        public Transform SpawnPoint => m_ParticleSpawnPoint != null ? m_ParticleSpawnPoint : transform;
        public GameObject ParticlePrefab => m_ParticlePrefab;
        public GameObject NativeBaselinePrefab => m_NativeBaselinePrefab;
        public bool HasParticle => m_ParticlePrefab != null || (m_SpawnNativeBaseline && m_NativeBaselinePrefab != null);

        public void Configure(Transform spawnPoint, GameObject particlePrefab)
        {
            m_ParticleSpawnPoint = spawnPoint;
            m_ParticlePrefab = particlePrefab;
        }

        public void ConfigureVisionOS(GameObject nativeBaselinePrefab, bool spawnNativeBaseline, float effectScale)
        {
            m_NativeBaselinePrefab = nativeBaselinePrefab;
            m_SpawnNativeBaseline = spawnNativeBaseline;
            m_EffectScaleMultiplier = Mathf.Max(0.01f, effectScale);
        }

        public GameObject Play()
        {
            var point = SpawnPoint;
            var managedEffect = MoodiumInteractionVFXManager.PlayInteractionEffect(
                point.position,
                point.rotation,
                gameObject);
            if (managedEffect != null)
                return managedEffect;

            if (!HasParticle)
                return ParticleBurstEffect.Spawn(point.position);

            GameObject effect = null;
            if (m_ParticlePrefab != null)
            {
                effect = Instantiate(m_ParticlePrefab, point.position, point.rotation);
                RegisterCreativeEffectIfNeeded(effect);
                effect.name = $"{m_ParticlePrefab.name} (Moodium Touch)";
                PrepareForVisionOS(effect);
                var cleanup = effect.GetComponent<TouchParticleAutoDestroy>();
                if (cleanup == null)
                    cleanup = effect.AddComponent<TouchParticleAutoDestroy>();
                cleanup.Configure(m_MaxLifetime);

                foreach (var particles in effect.GetComponentsInChildren<ParticleSystem>(true))
                    particles.Play(true);

                var renderers = effect.GetComponentsInChildren<ParticleSystemRenderer>(true);
                Debug.Log(
                    $"[Moodium Touch Particle] Spawned configured effect: {m_ParticlePrefab.name}, " +
                    $"position={point.position}, scale={effect.transform.lossyScale}, systems={renderers.Length}");
            }

            if (m_SpawnNativeBaseline && m_NativeBaselinePrefab != null)
                effect = SpawnNativeBaseline(point) ?? effect;
            return effect;
        }

        void PrepareForVisionOS(GameObject effect)
        {
            effect.transform.localScale *= m_EffectScaleMultiplier;
            foreach (var particles in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particles.main;
                main.cullingMode = ParticleSystemCullingMode.Automatic;

                var renderer = particles.GetComponent<ParticleSystemRenderer>();
                if (renderer == null)
                    continue;
                renderer.enabled = true;
                if (renderer.renderMode == ParticleSystemRenderMode.Stretch)
                    renderer.renderMode = ParticleSystemRenderMode.Billboard;
            }
        }

        GameObject SpawnNativeBaseline(Transform point)
        {
            var baseline = Instantiate(m_NativeBaselinePrefab, point.position, point.rotation);
            RegisterCreativeEffectIfNeeded(baseline);
            baseline.name = "Moodium Soft Round Particle (Touch)";
            var cleanup = baseline.GetComponent<TouchParticleAutoDestroy>();
            if (cleanup == null)
                cleanup = baseline.AddComponent<TouchParticleAutoDestroy>();
            cleanup.Configure(1f);
            foreach (var particles in baseline.GetComponentsInChildren<ParticleSystem>(true))
                particles.Play(true);
            Debug.Log($"[Moodium Touch Particle] Small round feedback spawned at {point.position}");
            return baseline;
        }

        void RegisterCreativeEffectIfNeeded(GameObject effect)
        {
            if (effect == null)
                return;
            var owner = transform.root != null ? transform.root.gameObject : gameObject;
            if (MoodiumRuntimeObjectRegistry.IsCreativeObject(owner))
                MoodiumRuntimeObjectRegistry.RegisterCreative(effect);
        }

    }

    public sealed class TouchParticleAutoDestroy : MonoBehaviour
    {
        float m_DestroyAt;

        public void Configure(float maximumLifetime)
        {
            var calculatedLifetime = 0f;
            foreach (var particles in GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particles.main;
                var lifetime = main.startDelay.constantMax + main.duration + main.startLifetime.constantMax;
                calculatedLifetime = Mathf.Max(calculatedLifetime, lifetime);
            }
            m_DestroyAt = Time.time + Mathf.Min(maximumLifetime, Mathf.Max(0.2f, calculatedLifetime + 0.15f));
        }

        void Update()
        {
            if (Time.time >= m_DestroyAt)
                Destroy(gameObject);
        }
    }
}
