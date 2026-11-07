using System.Collections.Generic;
using UnityEngine;

namespace Moodium.Reality
{
    /// <summary>Drives the lightweight, Vision Pro-safe candy material properties.</summary>
    public sealed class CandyEnvironmentShaderController : MonoBehaviour
    {
        static readonly int CandyProgress = Shader.PropertyToID("_CandyProgress");
        static readonly int CandyOrigin = Shader.PropertyToID("_CandyOrigin");
        static readonly int IridescenceStrength = Shader.PropertyToID("_IridescenceStrength");
        static readonly int SparkleStrength = Shader.PropertyToID("_SparkleStrength");
        static readonly int RimStrength = Shader.PropertyToID("_RimStrength");
        static readonly int AwakeningFlash = Shader.PropertyToID("_AwakeningFlash");
        static readonly int ShaderTime = Shader.PropertyToID("_ShaderTime");
        static readonly int RipplePosition = Shader.PropertyToID("_RipplePosition");
        static readonly int RippleAge = Shader.PropertyToID("_RippleAge");
        static readonly int RippleStrength = Shader.PropertyToID("_RippleStrength");

        [Header("Energy appearance")]
        [SerializeField, Min(0f)] float m_BaseRimStrength = 1.15f;
        [SerializeField, Min(0f)] float m_MaxRimStrength = 2.75f;
        [SerializeField, Min(0f)] float m_MaxIridescence = 1.25f;
        [SerializeField, Min(0f)] float m_MaxSparkle = 1.4f;
        [SerializeField, Min(0.1f)] float m_AwakeningDuration = 1.35f;

        [Header("Candy glass ripple")]
        [SerializeField, Min(0.05f)] float m_RippleDuration = 1.5f;
        [SerializeField, Range(0f, 3f)] float m_RippleIntensity = 1.35f;

        readonly List<Material> m_Materials = new();
        float m_Energy;
        float m_AwakeningStart = -100f;
        float m_RippleStart = -100f;
        bool m_Active;

        public void SetMaterials(IEnumerable<Material> materials)
        {
            m_Materials.Clear();
            if (materials != null)
                foreach (var material in materials)
                    if (material != null)
                        m_Materials.Add(material);
            ApplyStaticProperties();
        }

        public void SetActive(bool active)
        {
            m_Active = active;
            enabled = active;
            if (!active)
            {
                SetAll(AwakeningFlash, 0f);
                SetAll(RippleStrength, 0f);
            }
        }

        public void SetOrigin(Vector3 worldPosition) => SetAll(CandyOrigin, worldPosition);

        public void SetEnergyProgress(float energy, float normalizedProgress)
        {
            var previous = m_Energy;
            m_Energy = Mathf.Clamp01(normalizedProgress);
            ApplyStaticProperties();
            if (previous < 0.999f && m_Energy >= 0.999f)
                m_AwakeningStart = Time.time;
        }

        public void TriggerRipple(Vector3 worldPosition)
        {
            SetAll(RipplePosition, worldPosition);
            m_RippleStart = Time.time;
            Debug.Log($"[Candy Environment] Spatial Mesh ripple at {worldPosition}");
        }

        void Update()
        {
            if (!m_Active)
                return;

            SetAll(ShaderTime, Time.time);
            var awakeningT = Mathf.Clamp01((Time.time - m_AwakeningStart) / m_AwakeningDuration);
            var flash = awakeningT < 1f ? Mathf.Sin(awakeningT * Mathf.PI) : 0f;
            SetAll(AwakeningFlash, flash);

            var rippleAge = Time.time - m_RippleStart;
            var rippleFade = rippleAge >= 0f && rippleAge < m_RippleDuration
                ? (1f - rippleAge / m_RippleDuration) * m_RippleIntensity : 0f;
            SetAll(RippleAge, Mathf.Max(0f, rippleAge));
            SetAll(RippleStrength, rippleFade);
        }

        void ApplyStaticProperties()
        {
            var iridescence = Mathf.SmoothStep(0f, m_MaxIridescence, Mathf.InverseLerp(0.15f, 1f, m_Energy));
            var sparkle = Mathf.SmoothStep(0f, m_MaxSparkle, Mathf.InverseLerp(0.3f, 1f, m_Energy));
            SetAll(CandyProgress, m_Energy);
            SetAll(IridescenceStrength, iridescence);
            SetAll(SparkleStrength, sparkle);
            SetAll(RimStrength, Mathf.Lerp(m_BaseRimStrength, m_MaxRimStrength, m_Energy));
        }

        void SetAll(int property, float value)
        {
            foreach (var material in m_Materials)
                if (material != null && material.HasProperty(property)) material.SetFloat(property, value);
        }

        void SetAll(int property, Vector3 value)
        {
            foreach (var material in m_Materials)
                if (material != null && material.HasProperty(property)) material.SetVector(property, value);
        }
    }
}
