using System.Collections.Generic;
using UnityEngine;

namespace Moodium.NatureWorld
{
    /// <summary>Nature-only completion atmosphere. Leaves begin after the bag reaches 100%.</summary>
    [DisallowMultipleComponent]
    public sealed class NatureLeafParticleController : MonoBehaviour
    {
        [SerializeField] ParticleSystem m_Leaves;
        [SerializeField] Texture2D[] m_LeafTextures;
        [SerializeField, Min(0f)] float m_CompletionRate = 18f;
        [SerializeField, Min(0f)] float m_EmissionHeightAboveViewer = 2.4f;
        [Header("Leaf motion")]
        [SerializeField, Min(0.01f)] float m_MinFallSpeed = .08f;
        [SerializeField, Min(0.01f)] float m_MaxFallSpeed = .22f;
        [SerializeField, Min(0f)] float m_SwayAmplitude = .12f;
        [SerializeField, Min(0f)] float m_SwayFrequency = .55f;
        [SerializeField, Min(0f)] float m_HorizontalDrift = .035f;
        [SerializeField, Min(0f)] float m_DepthDrift = .018f;
        [SerializeField] Vector2 m_RollSpeedX = new(-.55f, .55f);
        [SerializeField] Vector2 m_RollSpeedY = new(-.7f, .7f);
        [SerializeField] Vector2 m_RollSpeedZ = new(-.5f, .5f);
        [SerializeField] Vector2 m_SizeMultiplier = new(.7f, 1.25f);

        struct LeafMotion
        {
            public float Phase;
            public float FallSpeed;
            public float DriftSign;
            public float RollX;
            public float RollY;
            public float RollZ;
            public float SizeMultiplier;
            public float InitialRotationX;
            public float InitialRotationY;
            public float InitialRotationZ;
            public bool SizeApplied;
            public bool RotationApplied;
        }

        readonly Dictionary<uint, LeafMotion> m_MotionBySeed = new();
        readonly List<ParticleSystem> m_LeafVariants = new();
        readonly List<Material> m_RuntimeMaterials = new();
        ParticleSystem.Particle[] m_ParticleBuffer;
        bool m_HasOrigin;
        bool m_Emitting;
        float m_EmissionRate;
        float m_EmissionStartedAt;
        Vector3 m_EmissionBox = new(5.5f, .6f, 5.5f);

        public static bool ShouldEmitLeaves(float normalizedProgress) => normalizedProgress >= .999f;
        public static bool NeedsVariantInitialization(int variantCount) => variantCount == 0;
        public static int GetLeafSystemCount(int textureCount) => Mathf.Max(1, textureCount);
        public static float GetEmissionRatePerVariant(float totalRate, int variantCount) =>
            variantCount > 0 ? Mathf.Max(0f, totalRate) / variantCount : 0f;
        public static float GetIndependentEmissionRate(float totalRate, int count, int index)
        {
            if (count <= 0 || index < 0 || index >= count) return 0f;
            // Unequal native rates prevent cloned emitters producing matched triplets.
            var weight = 1f + index * .271f;
            var sum = count + .271f * count * (count - 1) * .5f;
            return Mathf.Max(0f, totalRate) * weight / sum;
        }
        public static int GetInitialEmissionCount(int totalCount, int variantCount, int variantIndex)
        {
            if (totalCount <= 0 || variantCount <= 0 || variantIndex < 0 || variantIndex >= variantCount)
                return 0;
            return totalCount / variantCount + (variantIndex < totalCount % variantCount ? 1 : 0);
        }

        bool m_Suspended;
        readonly System.Collections.Generic.List<ParticleSystem> m_ResumeParticles = new();
        public void SetSuspended(bool value)
        {
            if (m_Suspended == value) return;
            m_Suspended = value;
            if (value)
            {
                m_ResumeParticles.Clear();
                foreach (var ps in m_LeafVariants) if (ps != null) { if (ps.isPlaying) m_ResumeParticles.Add(ps); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
            }
            else { foreach (var ps in m_ResumeParticles) if (ps != null) ps.Play(true); m_ResumeParticles.Clear(); }
        }
        public void Configure(ParticleSystem leaves)
        {
            m_Leaves = leaves;
            EnsureLeafVariants();
            StopLeaves();
        }

        // The atmosphere prefab is instantiated by CandyWorldManager rather than
        // created through Configure. Initialize lazily so that path uses the same
        // serialized particle system and texture variants.
        void EnsureLeafVariants()
        {
            if (!NeedsVariantInitialization(m_LeafVariants.Count))
                return;
            BuildLeafVariants();
        }

        void BuildLeafVariants()
        {
            m_LeafVariants.Clear();
            if (m_Leaves == null)
                return;

            m_Leaves.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            m_EmissionBox = m_Leaves.shape.scale;
            m_LeafVariants.Add(m_Leaves);
            var sourceRenderer = m_Leaves.GetComponent<ParticleSystemRenderer>();
            var sourceMaterial = sourceRenderer != null ? sourceRenderer.sharedMaterial : null;
            var count = GetLeafSystemCount(m_LeafTextures != null ? m_LeafTextures.Length : 0);
            for (var i = 0; i < count; i++)
            {
                var leaves = i == 0 ? m_Leaves : Instantiate(m_Leaves, m_Leaves.transform.parent);
                leaves.name = "Falling Leaves Variant " + (i + 1);
                leaves.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                // Distinct seeds give each texture its own positions and trajectories.
                // Fixed random seeds are not supported by native property replication.
                leaves.useAutoRandomSeed = true;
                var main = leaves.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                // At .08 m/s, a leaf needs 30 seconds just to reach eye level
                // from the 2.4 m overhead emitter. The prefab's 5.5–8.5 s
                // lifetime expired before the leaves could enter the view.
                main.startLifetime = new ParticleSystem.MinMaxCurve(35f, 42f);
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                main.maxParticles = Mathf.Max(main.maxParticles, 256);
                var emission = leaves.emission;
                emission.enabled = false;
                var shape = leaves.shape;
                shape.enabled = true;
                // Native ReplicateProperties emits from this volume. Do not rely
                // on ParticleSystem.Emit, which only changes Unity's simulation.
                shape.scale = new Vector3(m_EmissionBox.x * (1f + i * .137f),
                    3f + i * .23f, m_EmissionBox.z * (1f + i * .193f));
                shape.rotation = new Vector3(0f, i * 73f, 0f);
                shape.position = new Vector3((i - (count - 1) * .5f) * .61f, -1.5f, i * .43f);
                emission.SetBursts(System.Array.Empty<ParticleSystem.Burst>());
                main.startDelay = 0f;
                main.startRotation3D = true;
                main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                var renderer = leaves.GetComponent<ParticleSystemRenderer>();
                if (renderer != null) renderer.alignment = ParticleSystemRenderSpace.World;
                if (sourceMaterial != null && m_LeafTextures != null && i < m_LeafTextures.Length && m_LeafTextures[i] != null)
                {
                    var material = new Material(sourceMaterial) { name = "Nature Leaf Variant " + (i + 1) };
                    if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", m_LeafTextures[i]);
                    if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", m_LeafTextures[i]);
                    renderer.sharedMaterial = material;
                    m_RuntimeMaterials.Add(material);
                }
                if (i > 0) m_LeafVariants.Add(leaves);
            }
        }

        public void StartLeaves()
        {
            m_HasOrigin = false;
            m_Emitting = false;
            if (m_Leaves == null)
                return;
            EnsureLeafVariants();
            foreach (var leaves in m_LeafVariants)
                leaves.Clear(true);
            m_MotionBySeed.Clear();
            foreach (var leaves in m_LeafVariants)
                leaves.Play(true);
            SetEmissionRate(0f);
        }

        public void StopLeaves()
        {
            m_HasOrigin = false;
            m_Emitting = false;
            m_EmissionRate = 0f;
            foreach (var leaves in m_LeafVariants)
                leaves.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void SetOrigin(Vector3 fallbackPosition)
        {
            if (m_HasOrigin)
                return;

            var camera = Camera.main;
            if (camera != null)
            {
                transform.position = camera.transform.position + Vector3.up * m_EmissionHeightAboveViewer;
            }
            else
            {
                transform.position = fallbackPosition + Vector3.up * m_EmissionHeightAboveViewer;
            }
            m_HasOrigin = true;
        }

        public void UpdateLeafIntensity(float normalizedProgress)
        {
            if (!m_HasOrigin || m_LeafVariants.Count == 0)
                return;
            var shouldEmit = ShouldEmitLeaves(normalizedProgress);
            if (shouldEmit == m_Emitting)
                return;
            m_Emitting = shouldEmit;
            SetEmissionRate(m_Emitting ? m_CompletionRate : 0f);
        }

        void LateUpdate()
        {
            // World-space particles retain their existing motion, but each new leaf
            // is emitted above the current viewer position. This keeps the effect
            // broad around someone who walks, rather than leaving one small patch.
            if (!m_Emitting || !m_HasOrigin || Camera.main == null)
                return;
            var cameraPosition = Camera.main.transform.position;
            transform.position = new Vector3(cameraPosition.x, cameraPosition.y + m_EmissionHeightAboveViewer, cameraPosition.z);
        }

        void Update()
        {
            if (m_Suspended) return;
            if (!m_Emitting || m_LeafVariants.Count == 0)
                return;
            for (var variant = 0; variant < m_LeafVariants.Count; variant++)
            {
                var emission = m_LeafVariants[variant].emission;
                // Delay module is not supported by native replication. Enable
                // independent emitters on separate frames instead.
                if (!emission.enabled && Time.time - m_EmissionStartedAt >= variant * .317f)
                    emission.enabled = true;
                UpdateVariantMotion(m_LeafVariants[variant], variant);
            }
        }

        void UpdateVariantMotion(ParticleSystem leaves, int variant)
        {
            var count = leaves.particleCount;
            if (count <= 0)
                return;
            if (m_ParticleBuffer == null || m_ParticleBuffer.Length < count)
                m_ParticleBuffer = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(count)];

            count = leaves.GetParticles(m_ParticleBuffer);
            var now = Time.time;
            var delta = Mathf.Max(0f, Time.deltaTime);
            for (var i = 0; i < count; i++)
            {
                var particle = m_ParticleBuffer[i];
                var seed = particle.randomSeed != 0 ? particle.randomSeed : (uint)(i + 1);
                var key = seed ^ (uint)(variant * 100003);
                if (!m_MotionBySeed.TryGetValue(key, out var motion))
                {
                    motion = CreateMotion(key);
                    m_MotionBySeed[key] = motion;
                }

                if (!motion.SizeApplied)
                {
                    particle.startSize3D *= motion.SizeMultiplier;
                    motion.SizeApplied = true;
                }
                if (!motion.RotationApplied)
                {
                    particle.rotation3D = new Vector3(motion.InitialRotationX, motion.InitialRotationY, motion.InitialRotationZ);
                    motion.RotationApplied = true;
                }

                var sway = Mathf.Sin(now * m_SwayFrequency + motion.Phase) * m_SwayAmplitude;
                particle.velocity = new Vector3(
                    motion.DriftSign * m_HorizontalDrift + sway,
                    -motion.FallSpeed,
                    Mathf.Cos(now * m_SwayFrequency * .73f + motion.Phase) * m_DepthDrift);
                particle.rotation3D += new Vector3(motion.RollX, motion.RollY, motion.RollZ) * (Mathf.Rad2Deg * delta);
                m_ParticleBuffer[i] = particle;
                m_MotionBySeed[key] = motion;
            }
            leaves.SetParticles(m_ParticleBuffer, count);
        }

        LeafMotion CreateMotion(uint seed)
        {
            var random = new System.Random(unchecked((int)seed));
            return new LeafMotion
            {
                Phase = (float)(random.NextDouble() * Mathf.PI * 2f),
                FallSpeed = Mathf.Lerp(m_MinFallSpeed, m_MaxFallSpeed, (float)random.NextDouble()),
                DriftSign = random.Next(0, 2) == 0 ? -1f : 1f,
                RollX = Mathf.Lerp(m_RollSpeedX.x, m_RollSpeedX.y, (float)random.NextDouble()),
                RollY = Mathf.Lerp(m_RollSpeedY.x, m_RollSpeedY.y, (float)random.NextDouble()),
                RollZ = Mathf.Lerp(m_RollSpeedZ.x, m_RollSpeedZ.y, (float)random.NextDouble()),
                SizeMultiplier = Mathf.Lerp(m_SizeMultiplier.x, m_SizeMultiplier.y, (float)random.NextDouble()),
                InitialRotationX = (float)(random.NextDouble() * 360f),
                InitialRotationY = (float)(random.NextDouble() * 360f),
                InitialRotationZ = (float)(random.NextDouble() * 360f)
            };
        }

        void SetEmissionRate(float rate)
        {
            if (m_Leaves == null)
                return;
            m_EmissionRate = Mathf.Max(0f, rate);
            m_EmissionStartedAt = Time.time;
            for (var i = 0; i < m_LeafVariants.Count; i++)
            {
                var leaves = m_LeafVariants[i];
                var variantEmission = leaves.emission;
                // ReplicateProperties copies emission settings to RealityKit;
                // manual Emit calls are not a native emission source.
                variantEmission.rateOverTime = GetIndependentEmissionRate(m_EmissionRate, m_LeafVariants.Count, i);
                variantEmission.enabled = m_EmissionRate > 0f && i == 0;
            }
        }

        void OnDestroy()
        {
            foreach (var material in m_RuntimeMaterials)
                if (material != null) Destroy(material);
        }

    }
}
