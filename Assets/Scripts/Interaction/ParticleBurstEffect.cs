using UnityEngine;
using UnityEngine.Rendering;

namespace Moodium.Interaction
{
    /// <summary>Creates a short, lightweight white candy-pop particle burst.</summary>
    public sealed class ParticleBurstEffect : MonoBehaviour
    {
        ParticleSystem m_Particles;
        Material m_RuntimeMaterial;
        Texture2D m_RuntimeCircleTexture;

        public static GameObject Spawn(Vector3 position)
        {
            if(!Application.isPlaying)return null;
            var effectObject = new GameObject("Moodium White Candy Burst");
            effectObject.transform.position = position;
            effectObject.AddComponent<ParticleBurstEffect>().BuildAndPlay();
            return effectObject;
        }

        void BuildAndPlay()
        {
            m_Particles = gameObject.AddComponent<ParticleSystem>();
            var main = m_Particles.main;
            main.duration = 0.12f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.26f, 0.46f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.018f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 1f, 1f, 0.95f),
                new Color(0.76f, 0.86f, 1f, 0.8f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 24;
            main.stopAction = ParticleSystemStopAction.None;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            var emission = m_Particles.emission;
            emission.rateOverTime = 140f;
            emission.SetBursts(System.Array.Empty<ParticleSystem.Burst>());

            var shape = m_Particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.015f;
            shape.radiusThickness = 0.2f;

            var velocity = m_Particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.radial = new ParticleSystem.MinMaxCurve(0.12f, 0.25f);

            var colorOverLifetime = m_Particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(0.7f, 0.82f, 1f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = gradient;

            var renderer = m_Particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
            {
                m_RuntimeMaterial = new Material(shader) { name = "Moodium Burst Material (Runtime)" };
                m_RuntimeCircleTexture = CreateRuntimeCircleTexture(64);
                var color = Color.white;
                if (m_RuntimeMaterial.HasProperty("_BaseMap"))
                    m_RuntimeMaterial.SetTexture("_BaseMap", m_RuntimeCircleTexture);
                if (m_RuntimeMaterial.HasProperty("_MainTex"))
                    m_RuntimeMaterial.SetTexture("_MainTex", m_RuntimeCircleTexture);
                if (m_RuntimeMaterial.HasProperty("_BaseColor"))
                    m_RuntimeMaterial.SetColor("_BaseColor", color);
                if (m_RuntimeMaterial.HasProperty("_Color"))
                    m_RuntimeMaterial.SetColor("_Color", color);
                m_RuntimeMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m_RuntimeMaterial.SetOverrideTag("RenderType", "Transparent");
                m_RuntimeMaterial.renderQueue = (int)RenderQueue.Transparent;
                if (m_RuntimeMaterial.HasProperty("_Surface"))
                    m_RuntimeMaterial.SetFloat("_Surface", 1f);
                if (m_RuntimeMaterial.HasProperty("_Blend"))
                    m_RuntimeMaterial.SetFloat("_Blend", 1f);
                if (m_RuntimeMaterial.HasProperty("_ZWrite"))
                    m_RuntimeMaterial.SetFloat("_ZWrite", 0f);
                renderer.sharedMaterial = m_RuntimeMaterial;
            }

            m_Particles.Play();
            Destroy(gameObject, 1.25f);
        }

        /// <summary>
        /// Creates the same soft circular alpha mask as the authored Moodium particle texture,
        /// without relying on an editor-only asset lookup at runtime.
        /// </summary>
        public static Texture2D CreateRuntimeCircleTexture(int resolution)
        {
            resolution = Mathf.Max(4, resolution);
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
            {
                name = "Moodium Runtime White Circle",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var radius = (resolution - 1) * .5f;
            var center = radius;
            for (var y = 0; y < resolution; y++)
            for (var x = 0; x < resolution; x++)
            {
                var distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center)) / radius;
                var alpha = Mathf.Clamp01((1f - distance) / .12f);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
            texture.Apply(false, true);
            return texture;
        }

        void OnDestroy()
        {
            if (m_RuntimeMaterial != null)
                Destroy(m_RuntimeMaterial);
            if (m_RuntimeCircleTexture != null)
                Destroy(m_RuntimeCircleTexture);
        }
    }
}
