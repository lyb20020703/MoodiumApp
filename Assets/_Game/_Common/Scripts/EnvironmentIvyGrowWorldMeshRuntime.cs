using System;
using System.Collections.Generic;
using Dynamite3D.RealIvy;
using Interaction;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
#if UNITY_VISIONOS || UNITY_EDITOR
using UnityEngine.XR.VisionOS;
#endif
using VFXViewer;

[DefaultExecutionOrder(int.MinValue)]
[DisallowMultipleComponent]
[RequireComponent(typeof(ARMeshManager))]
public sealed class EnvironmentIvyGrowWorldMeshRuntime : MonoBehaviour
{
    const string k_InteractionBoxName = "EnvironmentIvyGrow Interaction Box";
    const string k_DebugRootName = "EnvironmentIvyGrow Debug";
    const string k_DebugBoxOutlineName = "Collision Box Outline";
    const string k_DebugSurfaceMarkerName = "Mesh Surface Marker";
    const string k_DebugSurfaceSamplesName = "Mesh Surface Samples";
    const string k_DebugSurfaceSamplePrefix = "Mesh Surface Sample ";
    const string k_LastTouchMarkerName = "Last Touch Marker";
    const int k_InteractionLayer = 3;
    const string k_IvyRuntimeConfigResourcePath = "EnvironmentIvyGrowIvyRuntimeConfig";
    const string k_WorldOcclusionShaderName = "AR/Basic Occlusion Fixed";
    const float k_PositionEpsilonSqr = 0.000001f;
    const float k_ClassificationRetryIntervalSeconds = 1f;
    const float k_DebugBoxLineWidth = 0.005f;
    const float k_DebugSurfaceMarkerScale = 0.025f;
    const float k_DebugSurfaceSampleScale = 0.02f;
    const float k_DebugSurfaceMarkerOffset = 0.004f;
    const int k_DebugSurfaceSampleCount = 12;
    static readonly Vector3 k_DefaultMinColliderSize = new Vector3(0.05f, 0.05f, 0.05f);
    static readonly Vector3 k_InvalidSpawnPoint = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
    static readonly int k_BaseColorShaderId = Shader.PropertyToID("_BaseColor");
    static readonly int k_ColorShaderId = Shader.PropertyToID("_Color");

    [SerializeField] ARMeshManager m_MeshManager;
    [SerializeField] Camera m_XRCamera;
    [SerializeField] bool m_FollowCamera = true;
    [SerializeField] bool m_ShowMeshVisuals;
    [SerializeField] bool m_EnableRealWorldOcclusion = true;
    [SerializeField] bool m_ShowDebugVisuals;
    [SerializeField] bool m_EnableMeshClassification = true;
    [SerializeField] Vector3 m_MinColliderSize = k_DefaultMinColliderSize;
    [SerializeField] EnvironmentIvyGrowIvyRuntimeConfig m_IvyRuntimeConfig;

    bool m_ClassificationConfigured;
    bool m_DeniedWarningLogged;
    bool m_IvyConfigMissingWarningLogged;
    bool m_IvyConfigInvalidWarningLogged;
    bool m_ClassificationRetryWarningLogged;
    float m_NextClassificationAttemptTime;
    float m_LastIvySpawnTime = float.NegativeInfinity;
    Vector3 m_LastIvySpawnPoint = k_InvalidSpawnPoint;

    readonly Dictionary<XRSimpleInteractable, InteractionBinding> m_InteractionBindings = new Dictionary<XRSimpleInteractable, InteractionBinding>();
    readonly Dictionary<ContactKey, TouchContactState> m_TouchContacts = new Dictionary<ContactKey, TouchContactState>();
    readonly Queue<IvyController> m_SpawnedIvies = new Queue<IvyController>();
    readonly Dictionary<MeshRenderer, Material[]> m_OriginalMeshRendererMaterials = new Dictionary<MeshRenderer, Material[]>();
    Transform m_RuntimeIvyRoot;
    Transform m_LastTouchMarker;

    static Material s_DebugBoxLineMaterial;
    static Material s_DebugSurfaceMarkerMaterial;
    static Material s_DebugSurfaceSampleMaterial;
    static Material s_LastTouchMarkerMaterial;
    static Material s_RuntimeWorldOcclusionMaterial;

    sealed class InteractionBinding
    {
        public MeshFilter meshFilter;
        public BoxCollider interactionCollider;
        public MeshCollider surfaceCollider;
        public UnityAction<HoverEnterEventArgs> hoverEntered;
        public UnityAction<HoverExitEventArgs> hoverExited;
        public UnityAction<SelectEnterEventArgs> selectEntered;
        public UnityAction<SelectExitEventArgs> selectExited;
    }

    readonly struct ContactKey : IEquatable<ContactKey>
    {
        readonly int m_InteractorId;
        readonly int m_ColliderId;

        public ContactKey(int interactorId, int colliderId)
        {
            m_InteractorId = interactorId;
            m_ColliderId = colliderId;
        }

        public bool MatchesCollider(BoxCollider collider)
        {
            return collider != null && m_ColliderId == collider.GetInstanceID();
        }

        public bool Equals(ContactKey other)
        {
            return m_InteractorId == other.m_InteractorId && m_ColliderId == other.m_ColliderId;
        }

        public override bool Equals(object obj)
        {
            return obj is ContactKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (m_InteractorId * 397) ^ m_ColliderId;
            }
        }
    }

    sealed class TouchContactState
    {
        public bool hoverActive;
        public bool selectActive;
        public bool hasSpawned;
    }

    public void Initialize(Camera xrCamera)
    {
        m_XRCamera = xrCamera;
        ResolveReferences();
        ResolveIvyRuntimeConfig();
        SyncBoundingVolumeToCamera();
        RefreshExistingMeshes();
    }

    void Reset()
    {
        m_MeshManager = GetComponent<ARMeshManager>();
    }

    void Awake()
    {
        ResolveReferences();
        ResolveIvyRuntimeConfig();
        SyncBoundingVolumeToCamera();
    }

    void OnEnable()
    {
        ResolveReferences();
        ResolveIvyRuntimeConfig();

        if (m_MeshManager != null)
            m_MeshManager.meshesChanged += OnMeshesChanged;

#if UNITY_VISIONOS || UNITY_EDITOR
        VisionOS.AuthorizationChanged += OnAuthorizationChanged;
#endif

        RefreshExistingMeshes();
        SyncBoundingVolumeToCamera();
    }

    void OnDisable()
    {
        if (m_MeshManager != null)
            m_MeshManager.meshesChanged -= OnMeshesChanged;

#if UNITY_VISIONOS || UNITY_EDITOR
        VisionOS.AuthorizationChanged -= OnAuthorizationChanged;
#endif

        UnbindAllInteractables();
        m_TouchContacts.Clear();
        m_OriginalMeshRendererMaterials.Clear();
    }

    void Update()
    {
        SyncBoundingVolumeToCamera();
        TryEnableMeshClassification();
    }

#if UNITY_VISIONOS || UNITY_EDITOR
    void OnAuthorizationChanged(VisionOSAuthorizationEventArgs args)
    {
        if (args.type != VisionOSAuthorizationType.WorldSensing)
            return;

        if (args.status == VisionOSAuthorizationStatus.Denied && !m_DeniedWarningLogged)
        {
            m_DeniedWarningLogged = true;
            Debug.LogWarning("[EnvironmentIvyGrow] World Sensing is denied. Environment mesh reconstruction and collision boxes will not update until permission is granted.", this);
            return;
        }

        if (args.status == VisionOSAuthorizationStatus.Allowed)
            m_DeniedWarningLogged = false;
    }
#endif

    void OnMeshesChanged(ARMeshesChangedEventArgs args)
    {
        if (args.added != null)
        {
            foreach (var meshFilter in args.added)
                ConfigureMesh(meshFilter);
        }

        if (args.updated != null)
        {
            foreach (var meshFilter in args.updated)
                ConfigureMesh(meshFilter);
        }

        if (args.removed != null)
        {
            foreach (var meshFilter in args.removed)
                TeardownMesh(meshFilter);
        }
    }

    void RefreshExistingMeshes()
    {
        if (m_MeshManager == null)
            return;

        foreach (var meshFilter in m_MeshManager.meshes)
            ConfigureMesh(meshFilter);
    }

    void ConfigureMesh(MeshFilter meshFilter)
    {
        if (meshFilter == null)
            return;

        var mesh = meshFilter.sharedMesh != null ? meshFilter.sharedMesh : meshFilter.mesh;
        var meshRenderer = meshFilter.GetComponent<MeshRenderer>();

        var interactionCollider = EnsureInteractionBoxCollider(meshFilter);
        var surfaceCollider = EnsureSurfaceCollider(meshFilter, mesh);

        ConfigureMeshRenderer(meshRenderer, mesh);
        UpdateCollisionBox(meshFilter, interactionCollider);
        UpdateSurfaceCollider(mesh, surfaceCollider);
        ConfigureInteractable(meshFilter, interactionCollider, surfaceCollider);
        UpdateDebugVisuals(meshFilter, interactionCollider, mesh);
    }

    void ConfigureMeshRenderer(MeshRenderer meshRenderer, Mesh mesh)
    {
        if (meshRenderer == null)
            return;

        if (!m_OriginalMeshRendererMaterials.ContainsKey(meshRenderer))
            m_OriginalMeshRendererMaterials.Add(meshRenderer, meshRenderer.sharedMaterials);

        var hasValidMesh = mesh != null && mesh.vertexCount > 0;
        if (!hasValidMesh)
        {
            meshRenderer.enabled = false;
            return;
        }

        if (m_ShowMeshVisuals)
        {
            meshRenderer.sharedMaterials = m_OriginalMeshRendererMaterials[meshRenderer];
            meshRenderer.enabled = true;
            return;
        }

        if (!m_EnableRealWorldOcclusion)
        {
            meshRenderer.enabled = false;
            return;
        }

        var occlusionMaterial = ResolveWorldOcclusionMaterial();
        if (occlusionMaterial == null)
        {
            meshRenderer.enabled = false;
            return;
        }

        meshRenderer.sharedMaterials = CreateMaterialArray(occlusionMaterial, Mathf.Max(1, mesh.subMeshCount));
        meshRenderer.enabled = true;
    }

    static Material[] CreateMaterialArray(Material material, int count)
    {
        var safeCount = Mathf.Max(1, count);
        var materials = new Material[safeCount];
        for (var i = 0; i < safeCount; i++)
            materials[i] = material;
        return materials;
    }

    Material ResolveWorldOcclusionMaterial()
    {
        var config = ResolveIvyRuntimeConfig();
        if (config != null && config.worldOcclusionMaterial != null)
            return config.worldOcclusionMaterial;

        if (s_RuntimeWorldOcclusionMaterial != null)
            return s_RuntimeWorldOcclusionMaterial;

        var shader = Shader.Find(k_WorldOcclusionShaderName);
        if (shader == null)
            return null;

        s_RuntimeWorldOcclusionMaterial = new Material(shader)
        {
            name = "EnvironmentIvyGrowRuntimeOcclusion",
            hideFlags = HideFlags.HideAndDontSave,
        };
        return s_RuntimeWorldOcclusionMaterial;
    }

    void UpdateCollisionBox(MeshFilter meshFilter, BoxCollider boxCollider)
    {
        var mesh = meshFilter.sharedMesh != null ? meshFilter.sharedMesh : meshFilter.mesh;
        if (mesh == null || mesh.vertexCount == 0)
        {
            boxCollider.enabled = false;
            return;
        }

        mesh.RecalculateBounds();
        var bounds = mesh.bounds;
        var colliderSize = ClampColliderSize(bounds.size);

        if (!IsFinite(bounds.center) || !IsFinite(colliderSize))
        {
            boxCollider.enabled = false;
            return;
        }

        boxCollider.isTrigger = false;
        boxCollider.center = bounds.center;
        boxCollider.size = colliderSize;
        boxCollider.enabled = true;
    }

    void ConfigureInteractable(MeshFilter meshFilter, BoxCollider interactionCollider, MeshCollider surfaceCollider)
    {
        if (meshFilter == null || interactionCollider == null)
            return;

        var interactable = meshFilter.GetComponent<XRSimpleInteractable>();
        if (interactable == null)
            interactable = meshFilter.gameObject.AddComponent<XRSimpleInteractable>();

        interactable.colliders.Clear();
        interactable.colliders.Add(interactionCollider);
        interactable.distanceCalculationMode = XRBaseInteractable.DistanceCalculationMode.ColliderVolume;

        VisionProHoverHelper.EnsurePokeFilterForVisionProHover(interactable);
        var pokeFilter = meshFilter.GetComponent<XRPokeFilter>();
        if (pokeFilter != null)
        {
            pokeFilter.pokeInteractable = interactable;
            pokeFilter.pokeCollider = interactionCollider;
        }

        if (m_InteractionBindings.TryGetValue(interactable, out var existingBinding))
        {
            existingBinding.meshFilter = meshFilter;
            existingBinding.interactionCollider = interactionCollider;
            existingBinding.surfaceCollider = surfaceCollider;
            return;
        }

        var binding = new InteractionBinding
        {
            meshFilter = meshFilter,
            interactionCollider = interactionCollider,
            surfaceCollider = surfaceCollider,
        };

        binding.hoverEntered = args => OnSurfaceHoverEntered(binding, args);
        binding.hoverExited = args => OnSurfaceHoverExited(binding, args);
        binding.selectEntered = args => OnSurfaceSelectEntered(binding, args);
        binding.selectExited = args => OnSurfaceSelectExited(binding, args);

        interactable.hoverEntered.AddListener(binding.hoverEntered);
        interactable.hoverExited.AddListener(binding.hoverExited);
        interactable.selectEntered.AddListener(binding.selectEntered);
        interactable.selectExited.AddListener(binding.selectExited);

        m_InteractionBindings.Add(interactable, binding);
    }

    BoxCollider EnsureInteractionBoxCollider(MeshFilter meshFilter)
    {
        var interactionTransform = FindOrCreateChild(meshFilter.transform, k_InteractionBoxName);
        interactionTransform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        interactionTransform.localScale = Vector3.one;
        interactionTransform.gameObject.layer = k_InteractionLayer;

        var interactionCollider = interactionTransform.GetComponent<BoxCollider>();
        if (interactionCollider == null)
            interactionCollider = interactionTransform.gameObject.AddComponent<BoxCollider>();

        interactionCollider.isTrigger = false;
        return interactionCollider;
    }

    MeshCollider EnsureSurfaceCollider(MeshFilter meshFilter, Mesh mesh)
    {
        if (meshFilter == null)
            return null;

        var surfaceCollider = meshFilter.GetComponent<MeshCollider>();
        if (surfaceCollider == null)
            surfaceCollider = meshFilter.gameObject.AddComponent<MeshCollider>();

        UpdateSurfaceCollider(mesh, surfaceCollider);
        return surfaceCollider;
    }

    void UpdateSurfaceCollider(Mesh mesh, MeshCollider surfaceCollider)
    {
        if (surfaceCollider == null)
            return;

        if (mesh == null || mesh.vertexCount == 0)
        {
            surfaceCollider.sharedMesh = null;
            surfaceCollider.enabled = false;
            return;
        }

        surfaceCollider.sharedMesh = null;
        surfaceCollider.sharedMesh = mesh;
        surfaceCollider.convex = false;
        surfaceCollider.enabled = true;
    }

    BoxCollider FindInteractionBoxCollider(MeshFilter meshFilter)
    {
        if (meshFilter == null)
            return null;

        var interactionTransform = meshFilter.transform.Find(k_InteractionBoxName);
        return interactionTransform != null ? interactionTransform.GetComponent<BoxCollider>() : null;
    }

    void UpdateDebugVisuals(MeshFilter meshFilter, BoxCollider interactionCollider, Mesh mesh)
    {
        if (meshFilter == null)
            return;

        var existingDebugRoot = meshFilter.transform.Find(k_DebugRootName);
        if (!m_ShowDebugVisuals)
        {
            if (existingDebugRoot != null)
                existingDebugRoot.gameObject.SetActive(false);

            return;
        }

        if (interactionCollider == null)
            return;

        var debugRoot = FindOrCreateChild(meshFilter.transform, k_DebugRootName);
        debugRoot.gameObject.SetActive(true);
        debugRoot.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        debugRoot.localScale = Vector3.one;

        var boxOutline = EnsureDebugBoxOutline(debugRoot);
        UpdateDebugBoxOutline(boxOutline, interactionCollider);

        var surfaceMarker = EnsureDebugSurfaceMarker(debugRoot);
        UpdateDebugSurfaceMarker(meshFilter, mesh, surfaceMarker);

        var surfaceSamplesRoot = EnsureDebugSurfaceSamplesRoot(debugRoot);
        UpdateDebugSurfaceSamples(mesh, surfaceSamplesRoot);
    }

    LineRenderer EnsureDebugBoxOutline(Transform debugRoot)
    {
        var outlineTransform = FindOrCreateChild(debugRoot, k_DebugBoxOutlineName);
        outlineTransform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        outlineTransform.localScale = Vector3.one;

        var lineRenderer = outlineTransform.GetComponent<LineRenderer>();
        if (lineRenderer == null)
            lineRenderer = outlineTransform.gameObject.AddComponent<LineRenderer>();

        lineRenderer.loop = false;
        lineRenderer.useWorldSpace = false;
        lineRenderer.alignment = LineAlignment.TransformZ;
        lineRenderer.positionCount = 16;
        lineRenderer.widthMultiplier = k_DebugBoxLineWidth;
        lineRenderer.numCapVertices = 0;
        lineRenderer.numCornerVertices = 0;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.material = GetOrCreateDebugMaterial(ref s_DebugBoxLineMaterial, new Color(0.15f, 0.95f, 1f, 1f));
        lineRenderer.startColor = new Color(0.15f, 0.95f, 1f, 1f);
        lineRenderer.endColor = new Color(0.15f, 0.95f, 1f, 1f);
        return lineRenderer;
    }

    Transform EnsureDebugSurfaceMarker(Transform debugRoot)
    {
        var markerTransform = debugRoot.Find(k_DebugSurfaceMarkerName);
        if (markerTransform == null)
        {
            var markerObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            markerObject.name = k_DebugSurfaceMarkerName;
            markerObject.transform.SetParent(debugRoot, false);
            markerTransform = markerObject.transform;

            var markerCollider = markerObject.GetComponent<Collider>();
            if (markerCollider != null)
                Destroy(markerCollider);
        }

        markerTransform.localScale = Vector3.one * k_DebugSurfaceMarkerScale;
        var markerRenderer = markerTransform.GetComponent<Renderer>();
        if (markerRenderer != null)
            markerRenderer.sharedMaterial = GetOrCreateDebugMaterial(ref s_DebugSurfaceMarkerMaterial, new Color(0.1f, 1f, 0.25f, 1f));

        return markerTransform;
    }

    Transform EnsureDebugSurfaceSamplesRoot(Transform debugRoot)
    {
        var samplesRoot = FindOrCreateChild(debugRoot, k_DebugSurfaceSamplesName);
        samplesRoot.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        samplesRoot.localScale = Vector3.one;
        return samplesRoot;
    }

    Transform EnsureDebugSurfaceSample(Transform samplesRoot, int index)
    {
        var sampleName = $"{k_DebugSurfaceSamplePrefix}{index + 1}";
        var sampleTransform = samplesRoot.Find(sampleName);
        if (sampleTransform == null)
        {
            var sampleObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sampleObject.name = sampleName;
            sampleObject.transform.SetParent(samplesRoot, false);
            sampleTransform = sampleObject.transform;

            var sampleCollider = sampleObject.GetComponent<Collider>();
            if (sampleCollider != null)
                Destroy(sampleCollider);
        }

        sampleTransform.localScale = Vector3.one * k_DebugSurfaceSampleScale;
        var sampleRenderer = sampleTransform.GetComponent<Renderer>();
        if (sampleRenderer != null)
            sampleRenderer.sharedMaterial = GetOrCreateDebugMaterial(ref s_DebugSurfaceSampleMaterial, new Color(1f, 0.82f, 0.18f, 1f));

        return sampleTransform;
    }

    void UpdateDebugBoxOutline(LineRenderer lineRenderer, BoxCollider boxCollider)
    {
        if (lineRenderer == null || boxCollider == null)
            return;

        var extents = boxCollider.size * 0.5f;
        var center = boxCollider.center;
        var corner000 = center + new Vector3(-extents.x, -extents.y, -extents.z);
        var corner001 = center + new Vector3(-extents.x, -extents.y, extents.z);
        var corner010 = center + new Vector3(-extents.x, extents.y, -extents.z);
        var corner011 = center + new Vector3(-extents.x, extents.y, extents.z);
        var corner100 = center + new Vector3(extents.x, -extents.y, -extents.z);
        var corner101 = center + new Vector3(extents.x, -extents.y, extents.z);
        var corner110 = center + new Vector3(extents.x, extents.y, -extents.z);
        var corner111 = center + new Vector3(extents.x, extents.y, extents.z);

        lineRenderer.SetPositions(new[]
        {
            corner000, corner001, corner011, corner010, corner000,
            corner100, corner101, corner001, corner101, corner111,
            corner011, corner111, corner110, corner010, corner110, corner100,
        });
    }

    void UpdateDebugSurfaceMarker(MeshFilter meshFilter, Mesh mesh, Transform markerTransform)
    {
        if (markerTransform == null)
            return;

        if (meshFilter == null || mesh == null)
        {
            markerTransform.gameObject.SetActive(false);
            return;
        }

        if (!TryGetMeshSurfacePointAndNormalLocal(mesh, mesh.bounds.center, out var localSurfacePoint, out var localSurfaceNormal))
        {
            markerTransform.gameObject.SetActive(false);
            return;
        }

        markerTransform.gameObject.SetActive(true);
        markerTransform.localPosition = localSurfacePoint + localSurfaceNormal * k_DebugSurfaceMarkerOffset;
        markerTransform.localRotation = Quaternion.LookRotation(localSurfaceNormal);
    }

    void UpdateDebugSurfaceSamples(Mesh mesh, Transform samplesRoot)
    {
        if (samplesRoot == null)
            return;

        if (mesh == null)
        {
            SetChildrenActive(samplesRoot, false);
            return;
        }

        var vertices = mesh.vertices;
        if (vertices == null || vertices.Length == 0)
        {
            SetChildrenActive(samplesRoot, false);
            return;
        }

        var normals = mesh.normals;
        var maxSamples = Mathf.Min(k_DebugSurfaceSampleCount, vertices.Length);
        for (var i = 0; i < maxSamples; i++)
        {
            var sampleTransform = EnsureDebugSurfaceSample(samplesRoot, i);
            var vertexIndex = maxSamples == 1
                ? 0
                : Mathf.Clamp(Mathf.RoundToInt(i * (vertices.Length - 1f) / (maxSamples - 1f)), 0, vertices.Length - 1);

            var localPoint = vertices[vertexIndex];
            var localNormal = normals != null && normals.Length == vertices.Length && normals[vertexIndex].sqrMagnitude > 0.000001f
                ? normals[vertexIndex].normalized
                : Vector3.up;

            sampleTransform.gameObject.SetActive(true);
            sampleTransform.localPosition = localPoint + localNormal * k_DebugSurfaceMarkerOffset;
            sampleTransform.localRotation = Quaternion.LookRotation(localNormal);
        }

        for (var i = maxSamples; i < samplesRoot.childCount; i++)
            samplesRoot.GetChild(i).gameObject.SetActive(false);
    }

    void SetChildrenActive(Transform parent, bool isActive)
    {
        if (parent == null)
            return;

        for (var i = 0; i < parent.childCount; i++)
            parent.GetChild(i).gameObject.SetActive(isActive);
    }

    Transform FindOrCreateChild(Transform parent, string childName)
    {
        var child = parent.Find(childName);
        if (child != null)
            return child;

        var childObject = new GameObject(childName);
        childObject.transform.SetParent(parent, false);
        return childObject.transform;
    }

    void TeardownMesh(MeshFilter meshFilter)
    {
        if (meshFilter == null)
            return;

        var meshRenderer = meshFilter.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
            m_OriginalMeshRendererMaterials.Remove(meshRenderer);

        var interactable = meshFilter.GetComponent<XRSimpleInteractable>();
        if (interactable == null)
            return;

        ClearTouchStatesForCollider(FindInteractionBoxCollider(meshFilter));
        UnbindInteractable(interactable);
    }

    void OnSurfaceHoverEntered(InteractionBinding binding, HoverEnterEventArgs args)
    {
        var config = ResolveIvyRuntimeConfig();
        if (config == null || !config.triggerOnHover)
            return;

        if (args.interactorObject == null)
            return;

        TryHandleTouchEnter(binding, args.interactorObject, isHoverEvent: true, config);
    }

    void OnSurfaceHoverExited(InteractionBinding binding, HoverExitEventArgs args)
    {
        if (binding.interactionCollider == null || args.interactorObject == null)
            return;

        UpdateTouchExitState(binding.interactionCollider, args.interactorObject, isHoverEvent: true);
    }

    void OnSurfaceSelectEntered(InteractionBinding binding, SelectEnterEventArgs args)
    {
        var config = ResolveIvyRuntimeConfig();
        if (config == null || !config.triggerOnSelect)
            return;

        if (args.interactorObject == null)
            return;

        TryHandleTouchEnter(binding, args.interactorObject, isHoverEvent: false, config);
    }

    void OnSurfaceSelectExited(InteractionBinding binding, SelectExitEventArgs args)
    {
        if (binding.interactionCollider == null || args.interactorObject == null)
            return;

        UpdateTouchExitState(binding.interactionCollider, args.interactorObject, isHoverEvent: false);
    }

    void TryHandleTouchEnter(InteractionBinding binding, UnityEngine.XR.Interaction.Toolkit.Interactors.IXRInteractor interactor, bool isHoverEvent, EnvironmentIvyGrowIvyRuntimeConfig config)
    {
        if (binding == null || binding.interactionCollider == null || interactor == null)
            return;

        var key = CreateContactKey(binding.interactionCollider, interactor);
        if (!m_TouchContacts.TryGetValue(key, out var state))
        {
            state = new TouchContactState();
            m_TouchContacts.Add(key, state);
        }

        if (isHoverEvent)
            state.hoverActive = true;
        else
            state.selectActive = true;

        if (state.hasSpawned)
            return;

        if (!TryResolveTouchReferencePoint(binding.meshFilter, interactor, out var touchReferencePoint))
            return;

        if (TryGrowIvy(binding, touchReferencePoint, config))
            state.hasSpawned = true;
    }

    void UpdateTouchExitState(BoxCollider surfaceCollider, UnityEngine.XR.Interaction.Toolkit.Interactors.IXRInteractor interactor, bool isHoverEvent)
    {
        var key = CreateContactKey(surfaceCollider, interactor);
        if (!m_TouchContacts.TryGetValue(key, out var state))
            return;

        if (isHoverEvent)
            state.hoverActive = false;
        else
            state.selectActive = false;

        if (!state.hoverActive && !state.selectActive)
            m_TouchContacts.Remove(key);
    }

    bool TryGrowIvy(InteractionBinding binding, Vector3 interactorPosition, EnvironmentIvyGrowIvyRuntimeConfig config)
    {
        if (!HasValidIvyConfig(config) || binding == null || binding.meshFilter == null || binding.surfaceCollider == null || !binding.surfaceCollider.enabled || !binding.surfaceCollider.gameObject.activeInHierarchy)
            return false;

        if (!TryGetPreferredSurfacePointAndNormal(binding, interactorPosition, out var touchPoint, out var surfaceNormal))
            return false;

        surfaceNormal.Normalize();
        UpdateLastTouchMarker(touchPoint, surfaceNormal);

        var spawnPosition = touchPoint + surfaceNormal * Mathf.Max(0f, config.spawnOffset);
        if (!CanSpawnIvyAt(config, spawnPosition))
            return false;

        var selectedPreset = SelectIvyPreset(config);
        if (selectedPreset == null || selectedPreset.ivyParameters == null)
            return false;

        EnforceIvyLimit(config.maxConcurrentIvies);

        var ivy = Instantiate(config.ivyPrefab, spawnPosition, CreateIvyRotation(surfaceNormal), EnsureRuntimeIvyRoot());
        ivy.transform.Rotate(Vector3.right, -90f, Space.Self);
        var ivyParameters = new IvyParameters(selectedPreset.ivyParameters);
        ivy.growthParameters = CloneGrowthParameters(ivy.growthParameters);
        ApplyGrowthTimingTuning(ivy, config);
        ivy.ivyParameters = ivyParameters;
        ivy.gameObject.SetActive(true);
        ivy.StartGrowth();

        m_SpawnedIvies.Enqueue(ivy);
        m_LastIvySpawnTime = Time.unscaledTime;
        m_LastIvySpawnPoint = spawnPosition;
        return true;
    }

    bool CanSpawnIvyAt(EnvironmentIvyGrowIvyRuntimeConfig config, Vector3 spawnPosition)
    {
        if (config.spawnCooldownSeconds > 0f && Time.unscaledTime - m_LastIvySpawnTime < config.spawnCooldownSeconds)
            return false;

        if (!IsFinite(m_LastIvySpawnPoint) || config.minSpawnDistance <= 0f)
            return true;

        var recentTouchWindow = Mathf.Max(config.spawnCooldownSeconds * 4f, 0.35f);
        if (Time.unscaledTime - m_LastIvySpawnTime > recentTouchWindow)
            return true;

        return (spawnPosition - m_LastIvySpawnPoint).sqrMagnitude >= config.minSpawnDistance * config.minSpawnDistance;
    }

    IvyPreset SelectIvyPreset(EnvironmentIvyGrowIvyRuntimeConfig config)
    {
        if (config == null)
            return null;

        if (config.ivyPresets != null && config.ivyPresets.Length > 0)
        {
            if (config.randomizePresets)
                return config.ivyPresets[UnityEngine.Random.Range(0, config.ivyPresets.Length)];

            return config.ivyPresets[0];
        }

        return config.ivyPreset;
    }

    static void ApplyGrowthTimingTuning(IvyController ivy, EnvironmentIvyGrowIvyRuntimeConfig config)
    {
        if (ivy == null || ivy.growthParameters == null || config == null)
            return;

        var growthDurationScale = Mathf.Clamp(config.growthDurationScale, 0.25f, 1f);
        ivy.growthParameters.lifetime = Mathf.Max(0.25f, ivy.growthParameters.lifetime * growthDurationScale);
    }

    static RuntimeGrowthParameters CloneGrowthParameters(RuntimeGrowthParameters source)
    {
        if (source == null)
            return new RuntimeGrowthParameters();

        return new RuntimeGrowthParameters
        {
            growthSpeed = source.growthSpeed,
            lifetime = source.lifetime,
            speedOverLifetimeEnabled = source.speedOverLifetimeEnabled,
            speedOverLifetimeCurve = source.speedOverLifetimeCurve != null
                ? new AnimationCurve(source.speedOverLifetimeCurve.keys)
                : new AnimationCurve(),
            delay = source.delay,
            startGrowthOnAwake = source.startGrowthOnAwake,
        };
    }

    void EnforceIvyLimit(int maxConcurrentIvies)
    {
        if (maxConcurrentIvies <= 0)
            return;

        while (m_SpawnedIvies.Count > 0 && m_SpawnedIvies.Peek() == null)
            m_SpawnedIvies.Dequeue();

        while (m_SpawnedIvies.Count >= maxConcurrentIvies)
        {
            var oldestIvy = m_SpawnedIvies.Dequeue();
            if (oldestIvy != null)
                Destroy(oldestIvy.gameObject);
        }
    }

    bool TryGetPreferredSurfacePointAndNormal(InteractionBinding binding, Vector3 worldReferencePoint, out Vector3 surfacePoint, out Vector3 surfaceNormal)
    {
        surfacePoint = default;
        surfaceNormal = default;

        if (binding != null && TryGetMeshSurfacePointAndNormal(binding.meshFilter, worldReferencePoint, out surfacePoint, out surfaceNormal))
            return true;

        return binding != null && TryGetBoxSurfacePointAndNormal(binding.interactionCollider, worldReferencePoint, out surfacePoint, out surfaceNormal);
    }

    bool TryGetBoxSurfacePointAndNormal(BoxCollider boxCollider, Vector3 worldReferencePoint, out Vector3 surfacePoint, out Vector3 surfaceNormal)
    {
        surfacePoint = default;
        surfaceNormal = default;

        if (boxCollider == null)
            return false;

        var localPoint = boxCollider.transform.InverseTransformPoint(worldReferencePoint) - boxCollider.center;
        var extents = boxCollider.size * 0.5f;

        var xDistance = Mathf.Abs(extents.x - Mathf.Abs(localPoint.x));
        var yDistance = Mathf.Abs(extents.y - Mathf.Abs(localPoint.y));
        var zDistance = Mathf.Abs(extents.z - Mathf.Abs(localPoint.z));

        Vector3 localNormal;
        if (xDistance <= yDistance && xDistance <= zDistance)
            localNormal = new Vector3(SignOrOne(localPoint.x), 0f, 0f);
        else if (yDistance <= zDistance)
            localNormal = new Vector3(0f, SignOrOne(localPoint.y), 0f);
        else
            localNormal = new Vector3(0f, 0f, SignOrOne(localPoint.z));

        var localSurfacePoint = new Vector3(
            Mathf.Clamp(localPoint.x, -extents.x, extents.x),
            Mathf.Clamp(localPoint.y, -extents.y, extents.y),
            Mathf.Clamp(localPoint.z, -extents.z, extents.z));

        if (Mathf.Abs(localNormal.x) > 0f)
            localSurfacePoint.x = extents.x * localNormal.x;
        else if (Mathf.Abs(localNormal.y) > 0f)
            localSurfacePoint.y = extents.y * localNormal.y;
        else
            localSurfacePoint.z = extents.z * localNormal.z;

        surfacePoint = boxCollider.transform.TransformPoint(localSurfacePoint + boxCollider.center);
        surfaceNormal = boxCollider.transform.TransformDirection(localNormal).normalized;
        return IsFinite(surfacePoint) && IsFinite(surfaceNormal) && surfaceNormal.sqrMagnitude > 0.0001f;
    }

    bool TryGetMeshSurfacePointAndNormal(MeshFilter meshFilter, Vector3 worldReferencePoint, out Vector3 surfacePoint, out Vector3 surfaceNormal)
    {
        surfacePoint = default;
        surfaceNormal = default;

        if (meshFilter == null)
            return false;

        var mesh = meshFilter.sharedMesh != null ? meshFilter.sharedMesh : meshFilter.mesh;
        if (mesh == null)
            return false;

        var localReferencePoint = meshFilter.transform.InverseTransformPoint(worldReferencePoint);
        if (!TryGetMeshSurfacePointAndNormalLocal(mesh, localReferencePoint, out var localSurfacePoint, out var localSurfaceNormal))
            return false;

        surfacePoint = meshFilter.transform.TransformPoint(localSurfacePoint);
        surfaceNormal = meshFilter.transform.TransformDirection(localSurfaceNormal).normalized;
        return IsFinite(surfacePoint) && IsFinite(surfaceNormal) && surfaceNormal.sqrMagnitude > 0.0001f;
    }

    bool TryGetMeshSurfacePointAndNormalLocal(Mesh mesh, Vector3 localReferencePoint, out Vector3 localSurfacePoint, out Vector3 localSurfaceNormal)
    {
        localSurfacePoint = default;
        localSurfaceNormal = Vector3.up;

        if (mesh == null)
            return false;

        var vertices = mesh.vertices;
        var triangles = mesh.triangles;
        if (vertices == null || triangles == null || vertices.Length == 0 || triangles.Length < 3)
            return false;

        var bestDistanceSqr = float.PositiveInfinity;
        for (var i = 0; i < triangles.Length; i += 3)
        {
            var vertexA = vertices[triangles[i]];
            var vertexB = vertices[triangles[i + 1]];
            var vertexC = vertices[triangles[i + 2]];

            var candidatePoint = ClosestPointOnTriangle(localReferencePoint, vertexA, vertexB, vertexC);
            var candidateDistanceSqr = (localReferencePoint - candidatePoint).sqrMagnitude;
            if (candidateDistanceSqr >= bestDistanceSqr)
                continue;

            var candidateNormal = Vector3.Cross(vertexB - vertexA, vertexC - vertexA);
            if (candidateNormal.sqrMagnitude < 0.000001f)
                continue;

            candidateNormal.Normalize();
            var towardReference = localReferencePoint - candidatePoint;
            if (towardReference.sqrMagnitude > 0.000001f && Vector3.Dot(candidateNormal, towardReference) < 0f)
                candidateNormal = -candidateNormal;

            bestDistanceSqr = candidateDistanceSqr;
            localSurfacePoint = candidatePoint;
            localSurfaceNormal = candidateNormal;
        }

        return bestDistanceSqr < float.PositiveInfinity && IsFinite(localSurfacePoint) && IsFinite(localSurfaceNormal);
    }

    bool TryResolveTouchReferencePoint(MeshFilter meshFilter, UnityEngine.XR.Interaction.Toolkit.Interactors.IXRInteractor interactor, out Vector3 referencePoint)
    {
        referencePoint = default;
        if (meshFilter == null || interactor == null)
            return false;

        if (TryGetPokeInteractionPoint(meshFilter.transform, interactor, out referencePoint))
            return true;

        if (TryGetInteractorAttachPoint(interactor, out referencePoint))
            return true;

        if (interactor.transform != null && IsFinite(interactor.transform.position))
        {
            referencePoint = interactor.transform.position;
            return true;
        }

        return false;
    }

    static ContactKey CreateContactKey(BoxCollider surfaceCollider, UnityEngine.XR.Interaction.Toolkit.Interactors.IXRInteractor interactor)
    {
        var interactorId = interactor != null && interactor.transform != null ? interactor.transform.GetInstanceID() : 0;
        var colliderId = surfaceCollider != null ? surfaceCollider.GetInstanceID() : 0;
        return new ContactKey(interactorId, colliderId);
    }

    static bool TryGetInteractorAttachPoint(UnityEngine.XR.Interaction.Toolkit.Interactors.IXRInteractor interactor, out Vector3 attachPoint)
    {
        attachPoint = default;
        if (interactor == null)
            return false;

        var attachTransform = interactor.GetAttachTransform(null);
        if (attachTransform == null)
            return false;

        var position = attachTransform.position;
        if (!IsFinite(position))
            return false;

        attachPoint = position;
        return true;
    }

    static bool TryGetPokeInteractionPoint(Transform expectedTarget, UnityEngine.XR.Interaction.Toolkit.Interactors.IXRInteractor interactor, out Vector3 pokePoint)
    {
        pokePoint = default;
        if (!(interactor is IPokeStateDataProvider pokeStateDataProvider) || pokeStateDataProvider.pokeStateData == null)
            return false;

        var pokeState = pokeStateDataProvider.pokeStateData.Value;
        if (expectedTarget != null && pokeState.target != null && pokeState.target != expectedTarget)
            return false;

        if (IsFinite(pokeState.pokeInteractionPoint))
        {
            pokePoint = pokeState.pokeInteractionPoint;
            return true;
        }

        if (IsFinite(pokeState.axisAlignedPokeInteractionPoint))
        {
            pokePoint = pokeState.axisAlignedPokeInteractionPoint;
            return true;
        }

        return false;
    }

    static Vector3 ClosestPointOnTriangle(Vector3 point, Vector3 vertexA, Vector3 vertexB, Vector3 vertexC)
    {
        var edgeAB = vertexB - vertexA;
        var edgeAC = vertexC - vertexA;
        var pointToA = point - vertexA;

        var dotABPoint = Vector3.Dot(edgeAB, pointToA);
        var dotACPoint = Vector3.Dot(edgeAC, pointToA);
        if (dotABPoint <= 0f && dotACPoint <= 0f)
            return vertexA;

        var pointToB = point - vertexB;
        var dotABPointB = Vector3.Dot(edgeAB, pointToB);
        var dotACPointB = Vector3.Dot(edgeAC, pointToB);
        if (dotABPointB >= 0f && dotACPointB <= dotABPointB)
            return vertexB;

        var vc = dotABPoint * dotACPointB - dotABPointB * dotACPoint;
        if (vc <= 0f && dotABPoint >= 0f && dotABPointB <= 0f)
        {
            var v = dotABPoint / (dotABPoint - dotABPointB);
            return vertexA + v * edgeAB;
        }

        var pointToC = point - vertexC;
        var dotABPointC = Vector3.Dot(edgeAB, pointToC);
        var dotACPointC = Vector3.Dot(edgeAC, pointToC);
        if (dotACPointC >= 0f && dotABPointC <= dotACPointC)
            return vertexC;

        var vb = dotABPointC * dotACPoint - dotABPoint * dotACPointC;
        if (vb <= 0f && dotACPoint >= 0f && dotACPointC <= 0f)
        {
            var w = dotACPoint / (dotACPoint - dotACPointC);
            return vertexA + w * edgeAC;
        }

        var va = dotABPointB * dotACPointC - dotABPointC * dotACPointB;
        if (va <= 0f && (dotACPointB - dotABPointB) >= 0f && (dotABPointC - dotACPointC) >= 0f)
        {
            var edgeBC = vertexC - vertexB;
            var w = (dotACPointB - dotABPointB) / ((dotACPointB - dotABPointB) + (dotABPointC - dotACPointC));
            return vertexB + w * edgeBC;
        }

        var denominator = 1f / (va + vb + vc);
        var barycentricV = vb * denominator;
        var barycentricW = vc * denominator;
        return vertexA + edgeAB * barycentricV + edgeAC * barycentricW;
    }

    Quaternion CreateIvyRotation(Vector3 surfaceNormal)
    {
        var forward = -surfaceNormal.normalized;
        var upward = m_XRCamera != null
            ? Vector3.ProjectOnPlane(m_XRCamera.transform.up, forward)
            : Vector3.ProjectOnPlane(Vector3.up, forward);

        if (upward.sqrMagnitude < 0.0001f)
            upward = Vector3.ProjectOnPlane(Vector3.right, forward);

        if (upward.sqrMagnitude < 0.0001f)
            upward = Vector3.up;

        return Quaternion.LookRotation(forward, upward.normalized);
    }

    Transform EnsureRuntimeIvyRoot()
    {
        if (m_RuntimeIvyRoot != null)
            return m_RuntimeIvyRoot;

        var rootObject = new GameObject("EnvironmentIvyGrow Runtime Ivy");
        SceneManager.MoveGameObjectToScene(rootObject, gameObject.scene);
        m_RuntimeIvyRoot = rootObject.transform;
        return m_RuntimeIvyRoot;
    }

    EnvironmentIvyGrowIvyRuntimeConfig ResolveIvyRuntimeConfig()
    {
        if (m_IvyRuntimeConfig == null)
            m_IvyRuntimeConfig = Resources.Load<EnvironmentIvyGrowIvyRuntimeConfig>(k_IvyRuntimeConfigResourcePath);

        if (m_IvyRuntimeConfig == null && !m_IvyConfigMissingWarningLogged)
        {
            m_IvyConfigMissingWarningLogged = true;
            Debug.LogWarning("[EnvironmentIvyGrow] Missing Resources/EnvironmentIvyGrowIvyRuntimeConfig asset. MR touch will not spawn ivy until the config asset is available.", this);
        }

        return m_IvyRuntimeConfig;
    }

    bool HasValidIvyConfig(EnvironmentIvyGrowIvyRuntimeConfig config)
    {
        if (config == null)
            return false;

        var hasSinglePreset = config.ivyPreset != null && config.ivyPreset.ivyParameters != null;
        var hasPresetArray = config.ivyPresets != null && config.ivyPresets.Length > 0;
        if (config.ivyPrefab != null && (hasSinglePreset || hasPresetArray))
            return true;

        if (!m_IvyConfigInvalidWarningLogged)
        {
            m_IvyConfigInvalidWarningLogged = true;
            Debug.LogWarning("[EnvironmentIvyGrow] Ivy runtime config is missing the Real Ivy prefab or ivy preset references. Touch events are active, but ivy spawning is disabled until those references are assigned.", this);
        }

        return false;
    }

    void UnbindAllInteractables()
    {
        if (m_InteractionBindings.Count == 0)
            return;

        var interactables = new List<XRSimpleInteractable>(m_InteractionBindings.Keys);
        foreach (var interactable in interactables)
            UnbindInteractable(interactable);
    }

    void UnbindInteractable(XRSimpleInteractable interactable)
    {
        if (interactable == null || !m_InteractionBindings.TryGetValue(interactable, out var binding))
            return;

        interactable.hoverEntered.RemoveListener(binding.hoverEntered);
        interactable.hoverExited.RemoveListener(binding.hoverExited);
        interactable.selectEntered.RemoveListener(binding.selectEntered);
        interactable.selectExited.RemoveListener(binding.selectExited);
        m_InteractionBindings.Remove(interactable);
    }

    void ClearTouchStatesForCollider(BoxCollider collider)
    {
        if (collider == null || m_TouchContacts.Count == 0)
            return;

        var keysToRemove = new List<ContactKey>();
        foreach (var pair in m_TouchContacts)
        {
            if (pair.Key.MatchesCollider(collider))
                keysToRemove.Add(pair.Key);
        }

        foreach (var key in keysToRemove)
            m_TouchContacts.Remove(key);
    }

    void UpdateLastTouchMarker(Vector3 surfacePoint, Vector3 surfaceNormal)
    {
        if (!m_ShowDebugVisuals)
            return;

        var marker = EnsureLastTouchMarker();
        if (marker == null)
            return;

        marker.gameObject.SetActive(true);
        marker.position = surfacePoint + surfaceNormal.normalized * k_DebugSurfaceMarkerOffset;
        marker.rotation = Quaternion.LookRotation(surfaceNormal.normalized);
    }

    Transform EnsureLastTouchMarker()
    {
        if (m_LastTouchMarker != null)
            return m_LastTouchMarker;

        var markerObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        markerObject.name = k_LastTouchMarkerName;
        markerObject.transform.SetParent(EnsureRuntimeIvyRoot(), false);
        markerObject.transform.localScale = Vector3.one * (k_DebugSurfaceMarkerScale * 1.2f);

        var markerCollider = markerObject.GetComponent<Collider>();
        if (markerCollider != null)
            Destroy(markerCollider);

        var markerRenderer = markerObject.GetComponent<Renderer>();
        if (markerRenderer != null)
            markerRenderer.sharedMaterial = GetOrCreateDebugMaterial(ref s_LastTouchMarkerMaterial, new Color(1f, 0.25f, 0.85f, 1f));

        m_LastTouchMarker = markerObject.transform;
        return m_LastTouchMarker;
    }

    static Material GetOrCreateDebugMaterial(ref Material material, Color color)
    {
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Standard");

            material = new Material(shader)
            {
                name = $"EnvironmentIvyGrowDebug_{color.r:0.00}_{color.g:0.00}_{color.b:0.00}"
            };
        }

        if (material.HasProperty(k_BaseColorShaderId))
            material.SetColor(k_BaseColorShaderId, color);
        if (material.HasProperty(k_ColorShaderId))
            material.SetColor(k_ColorShaderId, color);

        return material;
    }

    Vector3 ClampColliderSize(Vector3 sourceSize)
    {
        return new Vector3(
            Mathf.Max(Mathf.Abs(sourceSize.x), Mathf.Max(0.001f, m_MinColliderSize.x)),
            Mathf.Max(Mathf.Abs(sourceSize.y), Mathf.Max(0.001f, m_MinColliderSize.y)),
            Mathf.Max(Mathf.Abs(sourceSize.z), Mathf.Max(0.001f, m_MinColliderSize.z)));
    }

    static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    static float SignOrOne(float value)
    {
        return value < 0f ? -1f : 1f;
    }

    static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    void ResolveReferences()
    {
        if (m_MeshManager == null)
            m_MeshManager = GetComponent<ARMeshManager>();

        if (m_XRCamera != null)
            return;

        var xrOrigin = GetComponentInParent<XROrigin>();
        if (xrOrigin != null && xrOrigin.Camera != null)
        {
            m_XRCamera = xrOrigin.Camera;
            return;
        }

        m_XRCamera = Camera.main;
    }

    void SyncBoundingVolumeToCamera()
    {
        if (!m_FollowCamera || m_XRCamera == null)
            return;

        var parent = transform.parent;
        if (parent != null)
        {
            var targetLocalPosition = parent.InverseTransformPoint(m_XRCamera.transform.position);
            if ((transform.localPosition - targetLocalPosition).sqrMagnitude > k_PositionEpsilonSqr)
                transform.localPosition = targetLocalPosition;
        }
        else
        {
            var targetWorldPosition = m_XRCamera.transform.position;
            if ((transform.position - targetWorldPosition).sqrMagnitude > k_PositionEpsilonSqr)
                transform.position = targetWorldPosition;
        }
    }

    void TryEnableMeshClassification()
    {
        if (!m_EnableMeshClassification || m_ClassificationConfigured || m_MeshManager == null || Time.unscaledTime < m_NextClassificationAttemptTime)
            return;

#if UNITY_VISIONOS || UNITY_EDITOR
        var subsystem = m_MeshManager.subsystem;
        if (subsystem == null)
        {
            m_NextClassificationAttemptTime = Time.unscaledTime + k_ClassificationRetryIntervalSeconds;
            return;
        }

        try
        {
            if (!subsystem.GetClassificationEnabled())
                subsystem.SetClassificationEnabled(true);

            m_ClassificationConfigured = true;
            m_ClassificationRetryWarningLogged = false;
        }
        catch (Exception exception)
        {
            m_NextClassificationAttemptTime = Time.unscaledTime + k_ClassificationRetryIntervalSeconds;

            if (!m_ClassificationRetryWarningLogged)
            {
                m_ClassificationRetryWarningLogged = true;
                Debug.LogWarning($"[EnvironmentIvyGrow] Mesh classification is not ready yet: {exception.Message}. The system will keep retrying in the background.", this);
            }
        }
#else
        m_ClassificationConfigured = true;
#endif
    }
}

static class EnvironmentIvyGrowWorldMeshBootstrap
{
    const string k_TargetSceneName = "EvironmentIvyGrow";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;

        InstallForScene(SceneManager.GetActiveScene());
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        InstallForScene(scene);
    }

    static void InstallForScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded || scene.name != k_TargetSceneName)
            return;

        var sceneCamera = FindSceneCamera(scene);
        var meshManagers = UnityEngine.Object.FindObjectsByType<ARMeshManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var meshManager in meshManagers)
        {
            if (meshManager == null || meshManager.gameObject.scene != scene)
                continue;

            if (!meshManager.gameObject.activeSelf)
                meshManager.gameObject.SetActive(true);

            if (!meshManager.enabled)
                meshManager.enabled = true;

            var runtime = meshManager.GetComponent<EnvironmentIvyGrowWorldMeshRuntime>();
            if (runtime == null)
                runtime = meshManager.gameObject.AddComponent<EnvironmentIvyGrowWorldMeshRuntime>();

            runtime.Initialize(sceneCamera);
        }
    }

    static Camera FindSceneCamera(Scene scene)
    {
        var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var camera in cameras)
        {
            if (camera != null && camera.gameObject.scene == scene && camera.CompareTag("MainCamera"))
                return camera;
        }

        foreach (var camera in cameras)
        {
            if (camera != null && camera.gameObject.scene == scene)
                return camera;
        }

        return null;
    }
}
