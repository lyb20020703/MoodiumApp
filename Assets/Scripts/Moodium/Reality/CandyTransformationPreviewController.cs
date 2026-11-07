using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Moodium.Reality
{
    public sealed class CandyTransformationPreviewController : MonoBehaviour
    {
        static readonly int CandyProgressId = Shader.PropertyToID("_CandyProgress");
        static readonly int CandyOriginId = Shader.PropertyToID("_CandyOrigin");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        [SerializeField] Renderer m_TestRenderer;
        [SerializeField] Slider m_ProgressSlider;
        [SerializeField] TMP_Text m_PercentageLabel;
        [SerializeField] Transform m_TransformationOrigin;
        [SerializeField, Range(0f, 1f)] float m_CandyProgress;
        [SerializeField] bool m_PlaceInFrontOfCamera = true;
        [SerializeField, Min(0.5f)] float m_PreviewDistance = 1f;
        [SerializeField] bool m_AutoAnimateOnStart = true;
        [SerializeField, Min(2f)] float m_AutoCycleDuration = 8f;
        [SerializeField] Color m_RealityColor = new(0.38f, 0.4f, 0.46f, 1f);
        [SerializeField] Color m_CandyPink = new(1f, 0.18f, 0.58f, 0.42f);
        [SerializeField] Color m_CandyViolet = new(0.55f, 0.24f, 1f, 0.42f);

        Material m_RuntimeMaterial;
        bool m_AutoAnimate;

        public float CandyProgress
        {
            get => m_CandyProgress;
            set
            {
                m_CandyProgress = Mathf.Clamp01(value);
                ApplyProgress();
            }
        }

        void Awake()
        {
            m_AutoAnimate = m_AutoAnimateOnStart;
            if (m_PlaceInFrontOfCamera)
                PlaceInFrontOfCamera();
            if (m_TestRenderer != null)
                m_RuntimeMaterial = m_TestRenderer.material;
            if (m_ProgressSlider != null)
            {
                m_ProgressSlider.minValue = 0f;
                m_ProgressSlider.maxValue = 1f;
                m_ProgressSlider.SetValueWithoutNotify(m_CandyProgress);
                m_ProgressSlider.onValueChanged.AddListener(SetProgress);
            }
            ApplyProgress();
        }

        void Update()
        {
            if (!m_AutoAnimate)
                return;
            m_CandyProgress = Mathf.PingPong(Time.time * 2f / m_AutoCycleDuration, 1f);
            ApplyProgress();
        }

        void OnDestroy()
        {
            if (m_ProgressSlider != null)
                m_ProgressSlider.onValueChanged.RemoveListener(SetProgress);
            if (m_RuntimeMaterial != null)
                Destroy(m_RuntimeMaterial);
        }

        public void SetProgress(float progress)
        {
            m_AutoAnimate = false;
            CandyProgress = progress;
        }

        void PlaceInFrontOfCamera()
        {
            var camera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
            if (camera == null)
                return;
            var forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f)
                forward = camera.transform.forward.normalized;
            transform.SetPositionAndRotation(
                camera.transform.position + forward * m_PreviewDistance - Vector3.up * 0.04f,
                Quaternion.LookRotation(forward, Vector3.up));
        }

        void ApplyProgress()
        {
            if (m_RuntimeMaterial == null && m_TestRenderer != null)
                m_RuntimeMaterial = m_TestRenderer.material;
            if (m_RuntimeMaterial != null)
            {
                if (m_RuntimeMaterial.HasProperty(CandyProgressId))
                    m_RuntimeMaterial.SetFloat(CandyProgressId, m_CandyProgress);
                var origin = m_TransformationOrigin != null ? m_TransformationOrigin.position : transform.position;
                if (m_RuntimeMaterial.HasProperty(CandyOriginId))
                    m_RuntimeMaterial.SetVector(CandyOriginId, origin);

                // URP/Lit properties are supported by PolySpatial and converted reliably on visionOS.
                var candyColor = Color.Lerp(m_CandyPink, m_CandyViolet, Mathf.SmoothStep(0f, 1f, m_CandyProgress));
                var color = Color.Lerp(m_RealityColor, candyColor, m_CandyProgress);
                color.a = Mathf.Lerp(1f, 0.42f, m_CandyProgress);
                if (m_RuntimeMaterial.HasProperty(BaseColorId))
                    m_RuntimeMaterial.SetColor(BaseColorId, color);
                if (m_RuntimeMaterial.HasProperty(SmoothnessId))
                    m_RuntimeMaterial.SetFloat(SmoothnessId, Mathf.Lerp(0.32f, 0.94f, m_CandyProgress));
                if (m_RuntimeMaterial.HasProperty(EmissionColorId))
                {
                    m_RuntimeMaterial.EnableKeyword("_EMISSION");
                    m_RuntimeMaterial.SetColor(EmissionColorId, candyColor * (0.08f + m_CandyProgress * 0.72f));
                }
            }
            if (m_ProgressSlider != null && !Mathf.Approximately(m_ProgressSlider.value, m_CandyProgress))
                m_ProgressSlider.SetValueWithoutNotify(m_CandyProgress);
            if (m_PercentageLabel != null)
                m_PercentageLabel.text = $"{Mathf.RoundToInt(m_CandyProgress * 100f)}%";
        }
    }
}
