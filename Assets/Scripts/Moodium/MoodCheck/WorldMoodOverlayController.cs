using System.Collections;
using UnityEngine;

namespace Moodium.MoodCheck
{
    [DisallowMultipleComponent]
    public sealed class WorldMoodOverlayController : MonoBehaviour
    {
        const float BaseEmissionRate = 90f;
        bool m_Enabled = true;
        float m_InitialIntensity;
        bool m_ClearingPlayed;
        ParticleSystem m_Particles;
        GameObject m_ClearingLight;

        public bool IsActive { get; private set; }
        public float CurrentIntensity { get; private set; }
        public int ClearingPlayCount { get; private set; }
        public ParticleSystem ParticleSystem => m_Particles;

        public void Configure(bool enabled) => m_Enabled = enabled;

        public void Begin(MoodCheckSample sample, Transform cameraRoot)
        {
            Stop();
            m_ClearingPlayed = false;
            ClearingPlayCount = 0;
            if (!m_Enabled || sample == null || sample.mood is not (MoodKind.Low or MoodKind.Tense))
                return;
            if (cameraRoot == null)
            {
                Debug.LogWarning("[Mood Check] Mood overlay skipped because the camera root is missing.");
                return;
            }

            m_InitialIntensity = Mathf.Clamp01(sample.intensity);
            var root = new GameObject("Mood Atmosphere");
            root.transform.SetParent(cameraRoot, false);
            root.transform.localPosition = new Vector3(0f, 1.1f, .25f);
            m_Particles = root.AddComponent<ParticleSystem>();
            var main = m_Particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.8f, 1.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.1f, 1.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(.002f, .0045f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(.72f, .82f, 1f, .12f), new Color(.88f, .93f, 1f, .28f));
            main.gravityModifier = .12f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 180;
            var shape = m_Particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(2.4f, .15f, 2.4f);
            var emission = m_Particles.emission;
            emission.rateOverTime = BaseEmissionRate * m_InitialIntensity;
            var renderer = m_Particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 2.5f;
            renderer.velocityScale = .08f;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                         Shader.Find("Particles/Standard Unlit");
            if (shader != null)
            {
                var material = new Material(shader) { name = "Mood Atmosphere Runtime" };
                material.SetColor("_BaseColor", new Color(.75f, .84f, 1f, .22f));
                material.SetColor("_Color", new Color(.75f, .84f, 1f, .22f));
                renderer.material = material;
            }
            CurrentIntensity = m_InitialIntensity;
            IsActive = CurrentIntensity > 0f;
            if (Application.isPlaying)
                m_Particles.Play();
        }

        public void SetNormalizedProgress(float normalizedProgress)
        {
            if (m_Particles == null || m_ClearingPlayed)
                return;
            var progress = Mathf.Clamp01(normalizedProgress);
            CurrentIntensity = m_InitialIntensity * (1f - progress);
            var emission = m_Particles.emission;
            emission.rateOverTime = BaseEmissionRate * CurrentIntensity;
            IsActive = CurrentIntensity > 0f;
            if (progress < 1f)
                return;
            emission.enabled = false;
            if (Application.isPlaying)
                m_Particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            PlayClearingOnce();
        }

        public void Stop()
        {
            IsActive = false;
            CurrentIntensity = 0f;
            if (m_Particles != null)
            {
                var root = m_Particles.gameObject;
                m_Particles = null;
                if (Application.isPlaying) Destroy(root);
                else DestroyImmediate(root);
            }
            if (m_ClearingLight != null)
            {
                if (Application.isPlaying) Destroy(m_ClearingLight);
                else DestroyImmediate(m_ClearingLight);
                m_ClearingLight = null;
            }
        }

        void PlayClearingOnce()
        {
            if (m_ClearingPlayed)
                return;
            m_ClearingPlayed = true;
            ClearingPlayCount++;
            var parent = m_Particles != null ? m_Particles.transform.parent : transform;
            m_ClearingLight = new GameObject("Mood Clearing Light");
            m_ClearingLight.transform.SetParent(parent, false);
            var light = m_ClearingLight.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, .94f, .78f);
            light.range = 2.5f;
            light.intensity = 1.2f;
            if (Application.isPlaying)
                StartCoroutine(FadeClearing(light));
        }

        IEnumerator FadeClearing(Light light)
        {
            var elapsed = 0f;
            while (elapsed < .8f && light != null)
            {
                elapsed += Time.deltaTime;
                light.intensity = Mathf.Lerp(1.2f, 0f, elapsed / .8f);
                yield return null;
            }
            if (m_ClearingLight != null)
                Destroy(m_ClearingLight);
            m_ClearingLight = null;
        }

        void OnDestroy() => Stop();
    }
}
