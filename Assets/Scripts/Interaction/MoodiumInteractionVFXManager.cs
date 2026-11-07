using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Moodium.Interaction
{
    [DisallowMultipleComponent]
    public sealed class MoodiumInteractionVFXManager : MonoBehaviour
    {
        [SerializeField] GameObject m_TouchBurstPrefab;
        [Header("Direct visionOS visibility test")]
        [SerializeField] GameObject m_HitDustExplosionTestPrefab;
        [SerializeField] bool m_UseDirectHitDustTest;
        [SerializeField, Range(0.1f, 1f)] float m_TouchBurstScale = 0.45f;
        [SerializeField, Range(0.5f, 3f)] float m_MaximumLifetime = 1.4f;

        ParticleSystem m_SharedParticleSystem;
        GameObject m_SharedParticleRoot;
        readonly ParticleSystem[] m_HandTrailSystems = new ParticleSystem[2];
        readonly GameObject[] m_HandTrailRoots = new GameObject[2];
        readonly float[] m_LastHandTrailUpdate = new float[2];
        readonly ParticleSystem[] m_SpatialPaintSystems = new ParticleSystem[2];
        readonly GameObject[] m_SpatialPaintRoots = new GameObject[2];
        Coroutine m_BurstEmissionRoutine;

        public static MoodiumInteractionVFXManager Instance { get; private set; }
        public static Material SharedRoundParticleMaterial
        {
            get
            {
                if (Instance == null || Instance.m_TouchBurstPrefab == null)
                    return null;
                return Instance.m_TouchBurstPrefab
                    .GetComponentInChildren<ParticleSystemRenderer>(true)?.sharedMaterial;
            }
        }

        public static bool IsSharedParticleReady =>
            Instance != null && Instance.m_SharedParticleSystem != null;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            BuildPersistentParticleSystem();
            BuildPersistentHandTrailSystems();
            BuildPersistentSpatialPaintSystems();
        }

        void Update()
        {
            for (var i = 0; i < m_HandTrailSystems.Length; i++)
            {
                var particles = m_HandTrailSystems[i];
                if (particles == null || Time.time - m_LastHandTrailUpdate[i] <= 0.1f)
                    continue;
                var emission = particles.emission;
                emission.rateOverTime = 0f;
            }
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public static GameObject PlayInteractionEffect(
            Vector3 position,
            Quaternion rotation,
            GameObject interactionOwner = null)
        {
            return Instance != null
                ? Instance.Play(position, rotation, interactionOwner)
                : null;
        }

        public static GameObject PlayHandInteractionEffect(
            Vector3 position,
            Quaternion rotation,
            float palmDiameter)
        {
            return Instance != null
                ? Instance.Play(position, rotation, null, Mathf.Clamp(palmDiameter, 0.07f, 0.2f))
                : null;
        }

        /// <summary>
        /// Emits Opening hand-trail dust through the same persistent, prefab-authored
        /// ParticleSystem used for touch bursts. Keeping this renderer alive gives
        /// PolySpatial time to mirror it before any short-lived particles are emitted.
        /// </summary>
        public static void EmitHandTrail(
            int handIndex,
            Vector3 position,
            Vector3 velocity,
            float speed01,
            Color color)
        {
            if (Instance == null)
                return;
            Instance.UpdateHandTrailEmitter(handIndex, position, velocity, speed01, color);
        }

        public static void UpdateSpatialPaint(int handIndex, Vector3 position, float size, Color color)
        {
            if (Instance == null)
                return;
            Instance.UpdateSpatialPaintEmitter(handIndex, position, size, color);
        }

        public static void StopSpatialPaint(int handIndex)
        {
            if (Instance == null)
                return;
            handIndex = Mathf.Clamp(handIndex, 0, 1);
            var particles = Instance.m_SpatialPaintSystems[handIndex];
            if (particles == null)
                return;
            var emission = particles.emission;
            emission.rateOverTime = 0f;
        }

        public static void ClearSpatialPaint()
        {
            if (Instance == null)
                return;
            for (var i = 0; i < Instance.m_SpatialPaintSystems.Length; i++)
            {
                var particles = Instance.m_SpatialPaintSystems[i];
                if (particles == null)
                    continue;
                var emission = particles.emission;
                emission.rateOverTime = 0f;
                particles.Clear(true);
            }
        }

        void UpdateSpatialPaintEmitter(int handIndex, Vector3 position, float size, Color color)
        {
            BuildPersistentSpatialPaintSystems();
            handIndex = Mathf.Clamp(handIndex, 0, 1);
            var particles = m_SpatialPaintSystems[handIndex];
            var root = m_SpatialPaintRoots[handIndex];
            if (particles == null || root == null)
                return;
            root.transform.position = position;
            var main = particles.main;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.82f, size * 1.12f);
            main.startColor = color;
            var emission = particles.emission;
            emission.rateOverTime = 78f;
            if (!particles.isPlaying)
                particles.Play(true);
        }

        void UpdateHandTrailEmitter(
            int handIndex,
            Vector3 position,
            Vector3 velocity,
            float speed01,
            Color color)
        {
            BuildPersistentHandTrailSystems();
            handIndex = Mathf.Clamp(handIndex, 0, 1);
            var particles = m_HandTrailSystems[handIndex];
            var root = m_HandTrailRoots[handIndex];
            if (particles == null || root == null)
                return;

            root.transform.position = position;
            var main = particles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.65f, 1.25f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.002f, Mathf.Lerp(0.006f, 0.018f, speed01));
            main.startSize = new ParticleSystem.MinMaxCurve(
                Mathf.Lerp(0.0028f, 0.0038f, speed01),
                Mathf.Lerp(0.0055f, 0.0085f, speed01));
            main.startColor = color;
            var velocityModule = particles.velocityOverLifetime;
            velocityModule.enabled = true;
            velocityModule.space = ParticleSystemSimulationSpace.World;
            velocityModule.x = new ParticleSystem.MinMaxCurve(velocity.x - 0.003f, velocity.x + 0.003f);
            velocityModule.y = new ParticleSystem.MinMaxCurve(velocity.y - 0.002f, velocity.y + 0.005f);
            velocityModule.z = new ParticleSystem.MinMaxCurve(velocity.z - 0.003f, velocity.z + 0.003f);
            var emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = Mathf.Lerp(110f, 320f, speed01);
            if (!particles.isPlaying)
                particles.Play(true);
            m_LastHandTrailUpdate[handIndex] = Time.time;
        }

        GameObject Play(
            Vector3 position,
            Quaternion rotation,
            GameObject owner,
            float visualDiameterOverride = -1f)
        {
            // The old direct Epic Toon FX diagnostic is intentionally not used here.
            // Reality interactions must share the persistent Sugar Snow-compatible
            // renderer that has already been verified on Vision Pro.

            BuildPersistentParticleSystem();
            if (m_SharedParticleSystem == null)
                return null;

            var visualDiameter = visualDiameterOverride > 0f
                ? visualDiameterOverride
                : GetVisualDiameter(owner);
            var sizeMultiplier = Mathf.Clamp(visualDiameter / 0.12f, 0.8f, 2.5f);
            var particleSize = Mathf.Clamp(0.055f * sizeMultiplier, 0.045f, 0.12f);
            var travelSpeed = Mathf.Clamp(0.42f * sizeMultiplier, 0.32f, 0.9f);
            var spawnPosition = owner != null ? owner.transform.position : position;

            m_SharedParticleRoot.transform.position = spawnPosition;
            var main = m_SharedParticleSystem.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(travelSpeed * 0.65f, travelSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(particleSize * 0.65f, particleSize);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 1f, 1f, 0.95f),
                new Color(0.91f, 0.78f, 1f, 0.85f));

            var shape = m_SharedParticleSystem.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Clamp(visualDiameter * 0.12f, 0.01f, 0.05f);
            shape.radiusThickness = 1f;

            m_SharedParticleSystem.Clear(true);
            if (!m_SharedParticleSystem.isPlaying)
                m_SharedParticleSystem.Play(true);
            var emission = m_SharedParticleSystem.emission;
            emission.enabled = true;
            // PolySpatial mirrors rateOverTime changes (the exact route used by
            // the device-visible Sugar Snow), whereas manual Emit/Burst events
            // were not reaching RealityKit on the tested build.
            emission.rateOverTime = 190f;
            if (m_BurstEmissionRoutine != null)
                StopCoroutine(m_BurstEmissionRoutine);
            m_BurstEmissionRoutine = StartCoroutine(StopBurstEmissionAfter(0.42f));

            Debug.Log(
                $"[ParticleTest] Native Burst Started | owner={owner?.name ?? "<null>"} | " +
                $"object position={spawnPosition} | emitter position={m_SharedParticleRoot.transform.position} | " +
                $"size={particleSize:F3} | speed={travelSpeed:F3} | emission=190/s");
            return m_SharedParticleRoot;
        }

        IEnumerator StopBurstEmissionAfter(float duration)
        {
            yield return new WaitForSeconds(duration);
            if (m_SharedParticleSystem != null)
            {
                var emission = m_SharedParticleSystem.emission;
                emission.rateOverTime = 0f;
            }
            m_BurstEmissionRoutine = null;
        }

        GameObject SpawnDirectHitDustTest(GameObject owner, Vector3 fallbackPosition)
        {
            var objectPosition = owner != null ? owner.transform.position : fallbackPosition;
            var effect = Instantiate(
                m_HitDustExplosionTestPrefab,
                objectPosition,
                Quaternion.identity);
            effect.name = "[ParticleTest] HitDustExplosion";

            // HitDust was authored at a much larger world scale than Moodium's
            // candy models. Bounds-derived scaling keeps it visibly outside the
            // model while preventing an excessively large effect.
            var visualDiameter = GetVisualDiameter(owner);
            var testScale = Mathf.Clamp(visualDiameter * 1.5f, 0.15f, 0.8f);
            effect.transform.localScale = Vector3.one * testScale;

            foreach (var particles in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particles.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                particles.Play(true);
            }

            var particlePosition = effect.transform.position;
            Debug.Log(
                $"[ParticleTest] Touch Triggered | owner={owner?.name ?? "<null>"} | " +
                $"object position={objectPosition} | particle position={particlePosition} | " +
                $"particle scale={effect.transform.localScale} | " +
                $"systems={effect.GetComponentsInChildren<ParticleSystem>(true).Length}");

            Destroy(effect, 5f);
            return effect;
        }

        void BuildPersistentParticleSystem()
        {
            if (m_SharedParticleSystem != null || m_TouchBurstPrefab == null)
                return;

            m_SharedParticleRoot = Instantiate(m_TouchBurstPrefab, transform);
            m_SharedParticleRoot.name = "Moodium Persistent Sugar Particle System";
            m_SharedParticleRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            m_SharedParticleRoot.transform.localScale = Vector3.one;
            foreach (var source in m_SharedParticleRoot.GetComponentsInChildren<AudioSource>(true))
                source.enabled = false;

            m_SharedParticleSystem = m_SharedParticleRoot.GetComponentInChildren<ParticleSystem>(true);
            if (m_SharedParticleSystem == null)
                return;

            var main = m_SharedParticleSystem.main;
            main.playOnAwake = false;
            // Keep the prefab-authored renderer alive for the whole app session.
            // Sugar Snow works on device for the same reason: its ParticleSystem is
            // already mirrored before particles are injected.
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.maxParticles = 600;
            main.stopAction = ParticleSystemStopAction.None;

            var emission = m_SharedParticleSystem.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(System.Array.Empty<ParticleSystem.Burst>());
            var shape = m_SharedParticleSystem.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.02f;

            var renderer = m_SharedParticleSystem.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.enabled = true;
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sortMode = ParticleSystemSortMode.Distance;
                renderer.sortingOrder = 40;
            }

            m_SharedParticleSystem.Clear(true);
            m_SharedParticleSystem.Play(true);
            Debug.Log("[Moodium Interaction VFX] Persistent Sugar Snow-compatible particle renderer prepared for visionOS.");
        }

        void BuildPersistentHandTrailSystems()
        {
            if (m_TouchBurstPrefab == null)
                return;
            for (var i = 0; i < m_HandTrailSystems.Length; i++)
            {
                if (m_HandTrailSystems[i] != null)
                    continue;
                var root = Instantiate(m_TouchBurstPrefab, transform);
                root.name = i == 0
                    ? "Moodium Persistent Left Hand Sugar Trail"
                    : "Moodium Persistent Right Hand Sugar Trail";
                root.transform.localScale = Vector3.one;
                foreach (var source in root.GetComponentsInChildren<AudioSource>(true))
                    source.enabled = false;
                var particles = root.GetComponentInChildren<ParticleSystem>(true);
                if (particles == null)
                {
                    Destroy(root);
                    continue;
                }
                var main = particles.main;
                main.loop = true;
                main.playOnAwake = false;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                main.maxParticles = 720;
                var emission = particles.emission;
                emission.enabled = true;
                emission.rateOverTime = 0f;
                emission.SetBursts(System.Array.Empty<ParticleSystem.Burst>());
                var shape = particles.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.0015f;
                var renderer = particles.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                {
                    renderer.enabled = true;
                    renderer.renderMode = ParticleSystemRenderMode.Billboard;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.sortMode = ParticleSystemSortMode.Distance;
                    renderer.sortingOrder = 41;
                }
                particles.Clear(true);
                particles.Play(true);
                m_HandTrailRoots[i] = root;
                m_HandTrailSystems[i] = particles;
            }
        }

        void BuildPersistentSpatialPaintSystems()
        {
            if (m_TouchBurstPrefab == null)
                return;
            for (var i = 0; i < m_SpatialPaintSystems.Length; i++)
            {
                if (m_SpatialPaintSystems[i] != null)
                    continue;
                var root = Instantiate(m_TouchBurstPrefab, transform);
                root.name = i == 0
                    ? "Moodium Persistent Left Spatial Paint"
                    : "Moodium Persistent Right Spatial Paint";
                root.transform.localScale = Vector3.one;
                foreach (var source in root.GetComponentsInChildren<AudioSource>(true))
                    source.enabled = false;
                var particles = root.GetComponentInChildren<ParticleSystem>(true);
                if (particles == null)
                {
                    Destroy(root);
                    continue;
                }
                var main = particles.main;
                main.loop = true;
                main.playOnAwake = false;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                main.maxParticles = 1400;
                main.startLifetime = new ParticleSystem.MinMaxCurve(32f, 48f);
                main.startSpeed = 0f;
                main.gravityModifier = 0f;
                var emission = particles.emission;
                emission.enabled = true;
                emission.rateOverTime = 0f;
                emission.SetBursts(System.Array.Empty<ParticleSystem.Burst>());
                var shape = particles.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.0025f;
                var velocity = particles.velocityOverLifetime;
                velocity.enabled = false;
                var noise = particles.noise;
                noise.enabled = false;
                var renderer = particles.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                {
                    renderer.enabled = true;
                    renderer.renderMode = ParticleSystemRenderMode.Billboard;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.sortMode = ParticleSystemSortMode.Distance;
                    renderer.sortingOrder = 42;
                }
                particles.Clear(true);
                particles.Play(true);
                m_SpatialPaintRoots[i] = root;
                m_SpatialPaintSystems[i] = particles;
            }
        }

        static float GetVisualDiameter(GameObject owner)
        {
            if (owner == null)
                return 0.12f;
            var renderers = owner.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return Mathf.Max(owner.transform.lossyScale.x, owner.transform.lossyScale.y, owner.transform.lossyScale.z) * 0.12f;
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        }
    }
}
