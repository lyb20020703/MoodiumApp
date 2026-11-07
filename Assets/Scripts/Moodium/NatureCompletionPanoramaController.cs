using UnityEngine;
using UnityEngine.Rendering;

namespace Moodium.NatureWorld
{
    /// <summary>
    /// Shows Nature World's completion panorama as an inward-facing background sphere.
    /// It deliberately renders before world geometry so trees, leaves and reward objects
    /// continue to draw in front of it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NatureCompletionPanoramaController : MonoBehaviour
    {
        const float CompletedProgress = .999f;
        public const int BackgroundRenderQueue = 1000;
        public const string DomeMeshResourcePath = "Moodium/NaturePanoramaDome";
        // PolySpatial reliably mirrors this built-in shader on visionOS. The custom
        // transparent feather shader imported in the Editor but caused both worlds'
        // panoramas to disappear on device, so the panorama intentionally uses the
        // same opaque, background-first path as the previously working full sphere.
        public const string PanoramaFeatherShaderName = "Universal Render Pipeline/Unlit";

        [SerializeField, Min(2f)] float m_Radius = 12f;
        [SerializeField] float m_HeightOffset = -0.5f;

        Camera m_Viewer;
        Texture2D m_PanoramaTexture;
        GameObject m_PanoramaInstance;
        Material m_PanoramaMaterial;
        bool m_Started;

        public static bool ShouldDisplay(float normalizedProgress) => normalizedProgress >= CompletedProgress;

        public static Quaternion GetDomeRotation(Bounds bounds)
        {
            var size = bounds.size;
            var cutAxis = 0;
            if (size.y < size.x && size.y <= size.z)
                cutAxis = 1;
            else if (size.z < size.x && size.z < size.y)
                cutAxis = 2;

            var centerComponent = cutAxis == 0 ? bounds.center.x :
                cutAxis == 1 ? bounds.center.y : bounds.center.z;
            var domeDirection = cutAxis == 0 ? Vector3.right :
                cutAxis == 1 ? Vector3.up : Vector3.forward;
            if (centerComponent < 0f)
                domeDirection = -domeDirection;

            return Quaternion.FromToRotation(domeDirection, Vector3.up);
        }

        public static float GetDomeUniformScale(Bounds bounds, float targetRadius)
        {
            var diameter = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            var importedRadius = Mathf.Max(.0001f, diameter * .5f);
            return Mathf.Max(0f, targetRadius) / importedRadius;
        }

        public static float GetEdgeFeatherAlpha(float heightAboveCutEdge, float featherHeight) =>
            Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(heightAboveCutEdge / Mathf.Max(.0001f, featherHeight)));

        public void Configure(Camera viewer, Texture2D panoramaTexture)
        {
            m_Viewer = viewer;
            m_PanoramaTexture = panoramaTexture;
        }

        public void StartDisplay()
        {
            StopAndClear();
            m_Started = true;
        }

        public void SetProgress(float normalizedProgress)
        {
            if (!m_Started || m_PanoramaInstance != null || !ShouldDisplay(normalizedProgress))
                return;
            CreatePanorama();
        }

        void CreatePanorama()
        {
            if (m_Viewer == null || m_PanoramaTexture == null)
            {
                Debug.LogWarning("[Nature Panorama] Cannot display the completion panorama: camera or texture is missing.");
                return;
            }

            var shader = Shader.Find(PanoramaFeatherShaderName) ?? Shader.Find("Unlit/Texture");
            if (shader == null)
            {
                Debug.LogWarning("[Nature Panorama] No supported unlit shader was found.");
                return;
            }

            m_PanoramaMaterial = new Material(shader)
            {
                name = "Nature Completion Panorama Background",
                renderQueue = BackgroundRenderQueue
            };
            if (m_PanoramaMaterial.HasProperty("_BaseMap"))
                m_PanoramaMaterial.SetTexture("_BaseMap", m_PanoramaTexture);
            if (m_PanoramaMaterial.HasProperty("_MainTex"))
                m_PanoramaMaterial.SetTexture("_MainTex", m_PanoramaTexture);
            if (m_PanoramaMaterial.HasProperty("_Surface"))
                m_PanoramaMaterial.SetFloat("_Surface", 0f);
            if (m_PanoramaMaterial.HasProperty("_ZWrite"))
                m_PanoramaMaterial.SetFloat("_ZWrite", 1f);
            if (m_PanoramaMaterial.HasProperty("_Cull"))
                m_PanoramaMaterial.SetFloat("_Cull", (float)CullMode.Front);

            // PolySpatial reliably mirrors imported mesh assets. The prior runtime-created
            // mesh rendered in the Editor but could be absent on device, so this is an
            // equivalent Blender-exported, upper-only dome stored under Resources.
            var domeMesh = Resources.Load<Mesh>(DomeMeshResourcePath);
            if (domeMesh == null)
            {
                Debug.LogWarning($"[Nature Panorama] Missing dome mesh resource at Resources/{DomeMeshResourcePath}.");
                Destroy(m_PanoramaMaterial);
                m_PanoramaMaterial = null;
                return;
            }

            m_PanoramaInstance = new GameObject("Nature Completion Panorama Background");
            m_PanoramaInstance.name = "Nature Completion Panorama Background";
            m_PanoramaInstance.transform.position = m_Viewer.transform.position +
                Vector3.up * m_HeightOffset;
            // Detect the imported half-sphere's short (cut) axis from its real mesh
            // bounds. FBX axis/unit conversion differs between Blender and Unity, so
            // hard-coded identity rotation and scale can turn the dome sideways and
            // shrink it into a foreground object on device.
            // The creation position is intentionally retained for the lifetime of this
            // instance: moving the headset must not drag the world-space sky with it.
            m_PanoramaInstance.transform.rotation = GetDomeRotation(domeMesh.bounds);
            m_PanoramaInstance.transform.localScale = Vector3.one *
                GetDomeUniformScale(domeMesh.bounds, m_Radius);

            var filter = m_PanoramaInstance.AddComponent<MeshFilter>();
            filter.sharedMesh = domeMesh;
            var renderer = m_PanoramaInstance.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = m_PanoramaMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        public void StopAndClear()
        {
            m_Started = false;
            if (m_PanoramaInstance != null)
                Destroy(m_PanoramaInstance);
            m_PanoramaInstance = null;
            if (m_PanoramaMaterial != null)
                Destroy(m_PanoramaMaterial);
            m_PanoramaMaterial = null;
        }

        void OnDisable() => StopAndClear();
    }
}
