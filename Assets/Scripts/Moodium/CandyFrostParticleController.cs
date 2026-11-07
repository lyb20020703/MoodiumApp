using UnityEngine;

namespace Moodium.CandyWorld
{
    [DisallowMultipleComponent]
    public sealed class CandyFrostParticleController : MonoBehaviour
    {
        [SerializeField] ParticleSystem m_SugarDust;
        [SerializeField] ParticleSystem m_Sparkles;
        [SerializeField, Min(0f)] float m_MaxDustRate = 34f;
        [SerializeField, Min(0f)] float m_MaxSparkleRate = 5f;
        float m_TargetIntensity;
        float m_DisplayedIntensity;
        bool m_HasOrigin;
        bool m_Completed;

        public float Intensity => m_DisplayedIntensity;
        public bool HasOrigin => m_HasOrigin;

        bool m_Suspended;
        readonly System.Collections.Generic.List<ParticleSystem> m_ResumeParticles = new();
        public void SetSuspended(bool value)
        {
            if (m_Suspended == value) return;
            m_Suspended = value;
            if (value)
            {
                m_ResumeParticles.Clear();
                foreach (var ps in new ParticleSystem[] { m_SugarDust, m_Sparkles }) if (ps != null) { if (ps.isPlaying) m_ResumeParticles.Add(ps); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
            }
            else { foreach (var ps in m_ResumeParticles) if (ps != null) ps.Play(true); m_ResumeParticles.Clear(); }
        }
        public void Configure(ParticleSystem sugarDust, ParticleSystem sparkles)
        {
            m_SugarDust = sugarDust;
            m_Sparkles = sparkles;
            ApplyIntensity(0f);
        }

        public void StartFrost()
        {
            m_Completed = false;
            m_TargetIntensity = 0f;
            m_DisplayedIntensity = 0f;
            m_HasOrigin = false;
            ClearAndPlay(m_SugarDust);
            ClearAndPlay(m_Sparkles);
            ApplyIntensity(0f);
        }

        public void StopFrost()
        {
            m_TargetIntensity = 0f;
            m_DisplayedIntensity = 0f;
            m_HasOrigin = false;
            StopAndClear(m_SugarDust);
            StopAndClear(m_Sparkles);
        }

        public void SetOrigin(Vector3 worldPosition)
        {
            if (m_HasOrigin)
                return;

            // The emission volume follows translation, while existing particles
            // stay in world space and continue their natural downward trajectory.
            var camera = Camera.main;
            if (camera != null)
            {
                transform.SetPositionAndRotation(camera.transform.position - Vector3.up * .18f, Quaternion.identity);
            }
            else
            {
                transform.position = worldPosition - Vector3.up * 0.35f;
            }
            m_HasOrigin = true;
            Debug.Log("[Candy Frost] Emission volume follows the viewer; falling particles remain in world space.");
        }

        void LateUpdate()
        {
            if (!m_HasOrigin) return;
            var camera = Camera.main;
            if (camera != null)
                transform.SetPositionAndRotation(camera.transform.position - Vector3.up * .18f, Quaternion.identity);
        }

        public void UpdateFrostIntensity(float energy, float normalizedEnergy)
        {
            m_TargetIntensity = Mathf.Clamp01(normalizedEnergy);
            if (!m_Completed && m_TargetIntensity >= 0.999f)
            {
                m_Completed = true;
                EmitCompletionBurst();
                Debug.Log("[Candy Frost] Candy world unlocked frost burst.");
            }
        }

        void Update()
        {
            if (m_Suspended) return;
            if (!m_HasOrigin)
                return;

            m_DisplayedIntensity = Mathf.MoveTowards(m_DisplayedIntensity, m_TargetIntensity, Time.deltaTime * 0.22f);
            ApplyIntensity(m_DisplayedIntensity);
        }

        void ApplyIntensity(float intensity)
        {
            // 0-20%: nearly invisible; 20-50%: soft dust; 50-100%: an atmospheric snowfall.
            var dustCurve = intensity < 0.2f
                ? Mathf.InverseLerp(0f, 0.2f, intensity) * 0.035f
                : Mathf.Lerp(0.035f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 1f, intensity)));
            var sparkleCurve = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1f, intensity));
            SetEmissionRate(m_SugarDust, m_MaxDustRate * dustCurve);
            SetEmissionRate(m_Sparkles, m_MaxSparkleRate * sparkleCurve);
            ApplyDownfallMotion(intensity);
        }

        void ApplyDownfallMotion(float intensity)
        {
            if (m_SugarDust == null)
                return;
            var fullAwakening = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.82f, 1f, intensity));
            var emission = m_SugarDust.emission;
            // At 100%, switch from sparse floating dust to a continuous sugar snowfall.
            emission.rateOverTime = Mathf.Lerp(emission.rateOverTime.constant, 115f, fullAwakening);
            var main = m_SugarDust.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                Mathf.Lerp(4.2f, 3.2f, fullAwakening),
                Mathf.Lerp(6.8f, 5.2f, fullAwakening));
            main.startSize = new ParticleSystem.MinMaxCurve(0.007f, Mathf.Lerp(0.016f, 0.022f, fullAwakening));
            main.gravityModifier = Mathf.Lerp(0f, 0.035f, fullAwakening);
            var velocity = m_SugarDust.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.035f, 0.035f);
            velocity.y = new ParticleSystem.MinMaxCurve(
                Mathf.Lerp(-0.095f, -0.30f, fullAwakening),
                Mathf.Lerp(-0.025f, -0.16f, fullAwakening));
            velocity.z = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
        }

        void EmitCompletionBurst()
        {
            if (!m_HasOrigin)
                return;
            m_SugarDust?.Emit(38);
            m_Sparkles?.Emit(16);
        }

        static void SetEmissionRate(ParticleSystem particles, float rate)
        {
            if (particles == null)
                return;
            var emission = particles.emission;
            emission.rateOverTime = rate;
        }

        static void ClearAndPlay(ParticleSystem particles)
        {
            if (particles == null)
                return;
            var main = particles.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            particles.Clear(true);
            particles.Play(true);
        }

        static void StopAndClear(ParticleSystem particles)
        {
            if (particles != null)
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
