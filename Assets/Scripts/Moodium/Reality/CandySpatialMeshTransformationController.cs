using System.Collections.Generic;
using TMPro;
using Moodium.Interaction;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

namespace Moodium.Reality
{
    /// <summary>
    /// Adds a transparent visual-only layer to AR meshes. The source MeshRenderer,
    /// MeshFilter and MeshCollider are never modified, so tracking and physics stay isolated.
    /// </summary>
    public sealed class CandySpatialMeshTransformationController : MonoBehaviour
    {
        const int MaterialLevels = 8;
        const float ActivationProgress = 0.2f;
        static readonly int CandyProgressProperty = Shader.PropertyToID("_CandyProgress");
        static readonly int CandyOriginProperty = Shader.PropertyToID("_CandyOrigin");
        static readonly int TransparencyProperty = Shader.PropertyToID("_Transparency");
        static readonly int RimStrengthProperty = Shader.PropertyToID("_RimStrength");
        static readonly int IridescenceProperty = Shader.PropertyToID("_IridescenceStrength");
        static readonly int SparkleProperty = Shader.PropertyToID("_SparkleStrength");
        static readonly int ShaderTimeProperty = Shader.PropertyToID("_ShaderTime");
        static readonly int ShellThicknessProperty = Shader.PropertyToID("_ShellThickness");
        static readonly int PressRadiusProperty = Shader.PropertyToID("_PressRadius");
        static readonly int PressPointAProperty = Shader.PropertyToID("_PressPointA");
        static readonly int PressPointBProperty = Shader.PropertyToID("_PressPointB");

        sealed class OverlayEntry
        {
            public MeshFilter Source;
            public GameObject VisualObject;
            public MeshFilter VisualFilter;
            public MeshRenderer VisualRenderer;
            public Collider[] SurfaceColliders;
        }

        [SerializeField] ARMeshManager m_MeshManager;
        [SerializeField] Material m_CandyMaterialTemplate;
        [SerializeField, Min(1f)] float m_MaxSpreadDistance = 2.5f;
        [SerializeField] Color m_CandyPink = new(1f, 0.58f, 0.78f, 1f);
        [SerializeField] Color m_CandyViolet = new(0.68f, 0.55f, 1f, 1f);
        [SerializeField, Range(0.05f, 0.3f)] float m_MaximumAlpha = 0.12f;

        [Header("Soft Candy Wrapper")]
        [Tooltip("Small outward normal offset that gives the transparent wrapper a soft shell presence.")]
        [SerializeField, Range(0f, 0.02f)] float m_ShellThickness = 0.006f;
        [Tooltip("World-space radius of one hand press impression.")]
        [SerializeField, Range(0.04f, 0.3f)] float m_PressRadius = 0.14f;
        [Tooltip("Maximum inward displacement at the center of a direct press.")]
        [SerializeField, Range(0f, 0.06f)] float m_MaxPressDepth = 0.024f;
        [Tooltip("A fingertip must be this close to a real spatial-mesh collider to press the wrapper.")]
        [SerializeField, Range(0.005f, 0.12f)] float m_HandContactDistance = 0.045f;
        [SerializeField, Range(1f, 30f)] float m_PressReturnSpeed = 12f;

        readonly Dictionary<MeshFilter, OverlayEntry> m_Overlays = new();
        Material[] m_LevelMaterials;
        Color[] m_LevelColors;
        float m_CompletionFlashUntil;
        Vector3 m_Origin;
        float m_Progress;
        bool m_Running;
        readonly List<Vector3> m_UIVisibilityPoints = new();
        float m_NextUIVisibilityCheck;
        CandyWrapperContactFeedback m_ContactFeedback;
        Vector3 m_ContactNormalA = Vector3.up;
        Vector3 m_ContactNormalB = Vector3.up;
        XRHandSubsystem m_HandSubsystem;
        XROrigin m_XROrigin;
        Vector4 m_CurrentPressA = new(0f, -1000f, 0f, 0f);
        Vector4 m_CurrentPressB = new(0f, -1000f, 0f, 0f);

        public float Progress => m_Progress;
        public Vector3 Origin => m_Origin;
        /// <summary>True once the Candy wrapper has reached its interactive visual phase.</summary>
        public bool IsInteractionReady => m_Running && m_Progress >= ActivationProgress;

        /// <summary>
        /// Returns true only when the AR mesh owning this collider currently has a visible
        /// Candy overlay. This lets surface interactions follow the actual visual spread.
        /// </summary>
        public bool IsOverlayActive(Collider collider)
        {
            if (!IsInteractionReady || collider == null)
                return false;
            var source = collider.GetComponentInParent<MeshFilter>();
            if (source != null && m_Overlays.TryGetValue(source, out var directEntry))
                return directEntry.VisualRenderer != null && directEntry.VisualRenderer.enabled;
            // AR Default Mesh normally hosts filter and collider together, but retain a
            // conservative fallback for provider prefabs that place their collider below it.
            foreach (var pair in m_Overlays)
            {
                var entry = pair.Value;
                if (entry.Source != null && (collider.transform == entry.Source.transform ||
                                             collider.transform.IsChildOf(entry.Source.transform)))
                    return entry.VisualRenderer != null && entry.VisualRenderer.enabled;
            }
            return false;
        }

        public void Configure(ARMeshManager meshManager, Material candyMaterialTemplate)
        {
            m_MeshManager = meshManager;
            m_CandyMaterialTemplate = candyMaterialTemplate;
        }

        public void StartTransformation()
        {
            if (m_Running || m_MeshManager == null || m_CandyMaterialTemplate == null)
                return;
            m_Running = true;
            m_CompletionFlashUntil = 0f;
            m_Progress = 0f;
            m_Origin = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            BuildLevelMaterials();
            // Keep the candy surface material, but no longer create the separate
            // hand-following foil crease geometry. Clear legacy scene components too.
            m_ContactFeedback = GetComponent<CandyWrapperContactFeedback>();
            if (m_ContactFeedback != null)
            {
                m_ContactFeedback.ClearFeedback();
                m_ContactFeedback.enabled = false;
            }
            m_MeshManager.meshesChanged += OnMeshesChanged;
            foreach (var filter in m_MeshManager.GetComponentsInChildren<MeshFilter>(true))
                Register(filter);
            DisableAllOverlays();
            Debug.Log("[Candy Spatial Mesh] Visual overlay ready but disabled at 0% Energy. Tracking mesh is untouched.");
        }

        public void StopTransformation()
        {
            if (!m_Running)
                return;
            m_Running = false;
            if (m_ContactFeedback != null)
            {
                m_ContactFeedback.ClearFeedback();
                m_ContactFeedback.enabled = false;
            }
            if (m_MeshManager != null)
                m_MeshManager.meshesChanged -= OnMeshesChanged;
            DestroyAllOverlays();
            DestroyLevelMaterials();
            Debug.Log("[Candy Spatial Mesh] Transparent visual overlays removed. Tracking mesh remained unchanged.");
        }

        public void SetOrigin(Vector3 worldPosition)
        {
            m_Origin = worldPosition;
            UpdateMaterialGlobals();
            if (m_Progress >= ActivationProgress)
                ApplyToAllMeshes();
        }

        public void SetProgress(float energy, float normalizedProgress)
        {
            if (m_Running && m_Progress < .999f && normalizedProgress >= .999f)
                m_CompletionFlashUntil = Time.time + .65f;
            m_Progress = Mathf.Clamp01(normalizedProgress);
            UpdateMaterialGlobals();
            if (m_Progress < ActivationProgress)
                DisableAllOverlays();
            else
                ApplyToAllMeshes();
            Debug.Log($"[Candy Spatial Mesh] Transparent overlay progress={m_Progress:0.00}, origin={m_Origin}");
        }

        /// <summary>
        /// Returns the normal-direction displacement for one soft press. Negative values
        /// move the visual wrapper inward. Kept public for a small regression test and so
        /// press falloff remains independent from XR input availability.
        /// </summary>
        public static float ComputeSoftPressDisplacement(float distance, float radius, float strength)
        {
            if (radius <= 0f || strength <= 0f || distance >= radius)
                return 0f;
            var t = 1f - Mathf.Clamp01(distance / radius);
            var smoothFalloff = t * t * (3f - 2f * t);
            return -strength * smoothFalloff;
        }

        void OnDestroy() => StopTransformation();

        void Update()
        {
            if (!m_Running || m_LevelMaterials == null)
                return;
            UpdateSoftShellPresses();
            // Native transparent sorting groups cannot order against other groups.
            // Protect UI sightlines by hiding only the obstructing visual mesh chunks.
            if (Time.unscaledTime >= m_NextUIVisibilityCheck)
            {
                m_NextUIVisibilityCheck = Time.unscaledTime + .2f;
                RefreshUIVisibilityPoints();
                ApplyToAllMeshes();
            }
            if (m_CompletionFlashUntil > 0f && m_LevelColors != null)
            {
                var remaining = Mathf.Clamp01((m_CompletionFlashUntil - Time.time) / .65f);
                var flash = Mathf.Sin(remaining * Mathf.PI);
                for (var i = 0; i < m_LevelMaterials.Length; i++)
                {
                    var color = Color.Lerp(m_LevelColors[i], new Color(1f, .94f, .78f, m_LevelColors[i].a), flash * .25f);
                    color.a = m_LevelColors[i].a + flash * .025f;
                    if (m_LevelMaterials[i].HasProperty("_BaseColor"))
                        m_LevelMaterials[i].SetColor("_BaseColor", color);
                }
                if (Time.time >= m_CompletionFlashUntil) m_CompletionFlashUntil = 0f;
            }
            foreach (var material in m_LevelMaterials)
                if (material != null && material.HasProperty(ShaderTimeProperty))
                    material.SetFloat(ShaderTimeProperty, Time.time);
        }

        void OnMeshesChanged(ARMeshesChangedEventArgs args)
        {
            foreach (var filter in args.added) Register(filter);
            foreach (var filter in args.updated) Register(filter);
            foreach (var filter in args.removed) Unregister(filter);
            if (m_Progress >= ActivationProgress)
                ApplyToAllMeshes();
        }

        void Register(MeshFilter source)
        {
            if (source == null || source.sharedMesh == null)
                return;
            if (m_Overlays.TryGetValue(source, out var existing))
            {
                existing.VisualFilter.sharedMesh = source.sharedMesh;
                existing.SurfaceColliders = source.GetComponentsInChildren<Collider>(true);
                return;
            }

            var visual = new GameObject("Moodium Candy Visual Overlay");
            visual.transform.SetParent(source.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            var visualFilter = visual.AddComponent<MeshFilter>();
            visualFilter.sharedMesh = source.sharedMesh;
            var visualRenderer = visual.AddComponent<MeshRenderer>();
            visualRenderer.shadowCastingMode = ShadowCastingMode.Off;
            visualRenderer.receiveShadows = false;
            visualRenderer.lightProbeUsage = LightProbeUsage.Off;
            visualRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            visualRenderer.enabled = false;
            m_Overlays.Add(source, new OverlayEntry
            {
                Source = source,
                VisualObject = visual,
                VisualFilter = visualFilter,
                VisualRenderer = visualRenderer,
                SurfaceColliders = source.GetComponentsInChildren<Collider>(true)
            });
        }

        void Unregister(MeshFilter source)
        {
            if (source == null || !m_Overlays.Remove(source, out var entry))
                return;
            if (entry.VisualObject != null)
                Destroy(entry.VisualObject);
        }

        void ApplyToAllMeshes()
        {
            if (!m_Running || m_LevelMaterials == null || m_Progress < ActivationProgress)
                return;
            var effectProgress = Mathf.InverseLerp(ActivationProgress, 1f, m_Progress);
            var stale = new List<MeshFilter>();
            foreach (var pair in m_Overlays)
            {
                var entry = pair.Value;
                if (entry.Source == null || entry.VisualRenderer == null)
                {
                    stale.Add(pair.Key);
                    continue;
                }
                entry.VisualFilter.sharedMesh = entry.Source.sharedMesh;
                var distance01 = Mathf.Clamp01(
                    Vector3.Distance(entry.Source.transform.position, m_Origin) / m_MaxSpreadDistance);
                var start = distance01 * 0.65f;
                var localProgress = Mathf.SmoothStep(
                    0f, 1f, Mathf.InverseLerp(start, start + 0.35f, effectProgress));
                var level = Mathf.Clamp(
                    Mathf.RoundToInt(localProgress * (MaterialLevels - 1)), 0, MaterialLevels - 1);
                entry.VisualRenderer.sharedMaterial = m_LevelMaterials[level];
                entry.VisualRenderer.enabled = localProgress > 0.001f && !ObscuresUI(entry);
            }
            foreach (var source in stale)
                m_Overlays.Remove(source);
        }

        void RefreshUIVisibilityPoints()
        {
            m_UIVisibilityPoints.Clear();
            foreach (var text in FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
            {
                if (!text.isActiveAndEnabled || text.color.a <= .01f || string.IsNullOrWhiteSpace(text.text))
                    continue;
                // Rect corners protect the full label, not just its pivot. Include
                // Canvas-backed and 3D TMP labels used by the existing spatial UI.
                var corners = new Vector3[4];
                text.rectTransform.GetWorldCorners(corners);
                var center = (corners[0] + corners[2]) * .5f;
                m_UIVisibilityPoints.Add(center);
                foreach (var corner in corners)
                    m_UIVisibilityPoints.Add(Vector3.LerpUnclamped(center, corner, 1.15f));
            }
        }

        bool ObscuresUI(OverlayEntry entry)
        {
            var camera = Camera.main;
            if (camera == null || entry.SurfaceColliders == null) return false;
            foreach (var point in m_UIVisibilityPoints)
            {
                if (Vector3.Dot(camera.transform.forward, point - camera.transform.position) <= 0f)
                    continue;
                foreach (var surface in entry.SurfaceColliders)
                    if (BlocksUIPoint(surface, camera.transform.position, point)) return true;
            }
            return false;
        }

        /// <summary>Only suppress visual foil in front of UI; never modify the source collider.</summary>
        public static bool BlocksUIPoint(Collider surface, Vector3 viewer, Vector3 uiPoint)
        {
            if (surface == null || !surface.enabled || !surface.gameObject.activeInHierarchy) return false;
            var offset = uiPoint - viewer;
            var distance = offset.magnitude;
            return distance > .025f && surface.Raycast(new Ray(viewer, offset / distance), out _, distance + .015f);
        }

        void DisableAllOverlays()
        {
            foreach (var entry in m_Overlays.Values)
                if (entry.VisualRenderer != null)
                    entry.VisualRenderer.enabled = false;
        }

        void BuildLevelMaterials()
        {
            DestroyLevelMaterials();
            var stableShader = Shader.Find("Universal Render Pipeline/Lit");
            m_LevelMaterials = new Material[MaterialLevels];
            m_LevelColors = new Color[MaterialLevels];
            for (var i = 0; i < MaterialLevels; i++)
            {
                var progress = i / (float)(MaterialLevels - 1);
                // Prefer the existing transparent Candy shader for Fresnel/iridescence.
                // It is still rendered on a separate visual-only layer; AR mesh data and
                // colliders are never replaced. URP/Lit remains the safe fallback.
                var material = m_CandyMaterialTemplate != null
                    ? new Material(m_CandyMaterialTemplate)
                    : new Material(stableShader);
                material.name = $"Candy Transparent Overlay {i} (Runtime)";
                material.hideFlags = HideFlags.DontSave;
                ConfigureTransparentMaterial(material);
                var color = Color.Lerp(m_CandyPink, m_CandyViolet, progress * .45f);
                color = Color.Lerp(Color.white, color, .45f);
                color.a = Mathf.Lerp(0.025f, m_MaximumAlpha, progress);
                m_LevelColors[i] = color;
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                if (material.HasProperty("_Color")) material.SetColor("_Color", color);
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", Mathf.Lerp(0.78f, 0.96f, progress));
                if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", Mathf.Lerp(0.01f, 0.07f, progress));
                if (material.HasProperty(TransparencyProperty))
                    material.SetFloat(TransparencyProperty, Mathf.Lerp(0.07f, m_MaximumAlpha, progress));
                if (material.HasProperty(CandyProgressProperty))
                    material.SetFloat(CandyProgressProperty, Mathf.Lerp(ActivationProgress, 1f, progress));
                if (material.HasProperty(RimStrengthProperty))
                    material.SetFloat(RimStrengthProperty, Mathf.Lerp(0.45f, 2.15f, progress));
                if (material.HasProperty(IridescenceProperty))
                    material.SetFloat(IridescenceProperty, Mathf.Lerp(0f, 0.65f, progress));
                if (material.HasProperty(SparkleProperty))
                    material.SetFloat(SparkleProperty, Mathf.Lerp(0f, 0.48f, progress));
                if (material.HasProperty("_EmissionColor"))
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", color * .012f);
                }
                if (material.HasProperty(ShellThicknessProperty))
                    material.SetFloat(ShellThicknessProperty, m_ShellThickness);
                if (material.HasProperty(PressRadiusProperty))
                    material.SetFloat(PressRadiusProperty, m_PressRadius);
                m_LevelMaterials[i] = material;
            }
            UpdateMaterialGlobals();
        }

        void UpdateMaterialGlobals()
        {
            if (m_LevelMaterials == null)
                return;
            foreach (var material in m_LevelMaterials)
            {
                if (material == null)
                    continue;
                if (material.HasProperty(CandyOriginProperty))
                    material.SetVector(CandyOriginProperty, m_Origin);
                if (material.HasProperty(PressPointAProperty))
                    material.SetVector(PressPointAProperty, m_CurrentPressA);
                if (material.HasProperty(PressPointBProperty))
                    material.SetVector(PressPointBProperty, m_CurrentPressB);
            }
        }

        void UpdateSoftShellPresses()
        {
            var targetA = Vector4.zero;
            var targetB = Vector4.zero;
            if (m_Progress >= ActivationProgress && TryEnsureHands())
            {
                m_HandSubsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
                TryGetHandPress(m_HandSubsystem.leftHand, out targetA, out m_ContactNormalA);
                TryGetHandPress(m_HandSubsystem.rightHand, out targetB, out m_ContactNormalB);
            }

            var lerp = 1f - Mathf.Exp(-m_PressReturnSpeed * Time.deltaTime);
            m_CurrentPressA = Vector4.Lerp(m_CurrentPressA, targetA, lerp);
            m_CurrentPressB = Vector4.Lerp(m_CurrentPressB, targetB, lerp);
            if (targetA.w <= 0.0001f && m_CurrentPressA.w <= 0.0001f)
                m_CurrentPressA = new Vector4(0f, -1000f, 0f, 0f);
            if (targetB.w <= 0.0001f && m_CurrentPressB.w <= 0.0001f)
                m_CurrentPressB = new Vector4(0f, -1000f, 0f, 0f);
            UpdateMaterialGlobals();
        }

        bool TryEnsureHands()
        {
            if (m_HandSubsystem == null)
                m_HandSubsystem = XRGeneralSettings.Instance?.Manager?.activeLoader
                    ?.GetLoadedSubsystem<XRHandSubsystem>();
            return m_HandSubsystem != null && m_HandSubsystem.running;
        }

        bool TryGetHandPress(XRHand hand, out Vector4 press, out Vector3 normal)
        {
            press = Vector4.zero;
            normal = Vector3.up;
            if (!hand.isTracked)
                return false;
            var indexTip = hand.GetJoint(XRHandJointID.IndexTip);
            if (!indexTip.TryGetPose(out var trackingPose))
                return false;
            if (m_XROrigin == null)
                m_XROrigin = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
            var origin = m_XROrigin != null ? m_XROrigin.Origin.transform : null;
            var handPosition = PalmPressGestureController.TransformTrackingPose(trackingPose, origin).position;
            if (!TryFindClosestOverlaySurface(handPosition, out var surfacePoint, out var distance))
                return false;
            normal = handPosition - surfacePoint;
            if (normal.sqrMagnitude < .000001f) normal = Vector3.up;
            else normal.Normalize();

            var contact01 = 1f - Mathf.Clamp01(distance / Mathf.Max(.0001f, m_HandContactDistance));
            var strength = m_MaxPressDepth * (contact01 * contact01 * (3f - 2f * contact01));
            if (strength <= .00001f)
                return false;
            press = new Vector4(surfacePoint.x, surfacePoint.y, surfacePoint.z, strength);
            return true;
        }

        bool TryFindClosestOverlaySurface(Vector3 handPosition, out Vector3 surfacePoint, out float distance)
        {
            surfacePoint = default;
            distance = m_HandContactDistance;
            var bestDistance = m_HandContactDistance;
            foreach (var entry in m_Overlays.Values)
            {
                if (entry.Source == null || entry.VisualRenderer == null || !entry.VisualRenderer.enabled)
                    continue;
                var colliders = entry.SurfaceColliders;
                if (colliders == null)
                    continue;
                foreach (var collider in colliders)
                {
                    if (collider == null || !collider.enabled)
                        continue;
                    var point = collider.ClosestPoint(handPosition);
                    var candidateDistance = Vector3.Distance(handPosition, point);
                    if (candidateDistance > bestDistance)
                        continue;
                    bestDistance = candidateDistance;
                    surfacePoint = point;
                }
            }
            if (bestDistance >= m_HandContactDistance)
                return false;
            distance = bestDistance;
            return true;
        }

        static void ConfigureTransparentMaterial(Material material)
        {
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_SrcBlendAlpha")) material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            if (material.HasProperty("_DstBlendAlpha")) material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        }

        void DestroyAllOverlays()
        {
            foreach (var entry in m_Overlays.Values)
                if (entry.VisualObject != null)
                    Destroy(entry.VisualObject);
            m_Overlays.Clear();
        }

        void DestroyLevelMaterials()
        {
            if (m_LevelMaterials == null)
                return;
            foreach (var material in m_LevelMaterials)
                if (material != null)
                    Destroy(material);
            m_LevelMaterials = null;
            m_LevelColors = null;
        }
    }
}
