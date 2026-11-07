using System;
using System.Collections.Generic;
using System.Reflection;
using Autohand;
using Interaction;
using UnityEngine;
using UnityEngine.Rendering;

namespace VFXViewer
{
    public sealed class SpectatorContentRuntime : MonoBehaviour
    {
        private const string PeopleOcclusionLitReplacementShaderName = "Occlusion/OcclusionLitFast";
        private const string PeopleOcclusionEffectsReplacementShaderName = "Occlusion/OcclusionUnlitTransparent";
        private const string IosHumanOcclusionOpaqueShaderResourcePath = "Shaders/SpectatorHumanOcclusionOpaque";
        private const string IosHumanOcclusionOpaqueShaderName = "VFXViewer/SpectatorHumanOcclusionOpaque";
        private const string IosHumanOcclusionTransparentShaderResourcePath = "Shaders/SpectatorHumanOcclusionTransparent";
        private const string IosHumanOcclusionTransparentShaderName = "VFXViewer/SpectatorHumanOcclusionTransparent";
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
        private static readonly int AlphaClipId = Shader.PropertyToID("_AlphaClip");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int BlendId = Shader.PropertyToID("_Blend");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int SrcBlendAlphaId = Shader.PropertyToID("_SrcBlendAlpha");
        private static readonly int DstBlendAlphaId = Shader.PropertyToID("_DstBlendAlpha");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        private static readonly int CullId = Shader.PropertyToID("_Cull");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int SpecColorId = Shader.PropertyToID("_SpecColor");
        private static readonly int SpecGlossMapId = Shader.PropertyToID("_SpecGlossMap");
        private static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
        private static readonly int BumpScaleId = Shader.PropertyToID("_BumpScale");
        private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int UseVertexColorId = Shader.PropertyToID("_UseVertexColor");

        private sealed class SpawnedExhibit
        {
            public string instanceId;
            public string moduleId;
            public GameObject rootObject;
            public ExhibitInfo exhibitInfo;
        }

        private sealed class RemoteHandHandle
        {
            public Hand hand;
            public Renderer[] renderers = Array.Empty<Renderer>();
            public string heldObjectInstanceId = string.Empty;
        }

        private struct ModuleRemoteStateFingerprint
        {
            public int phase;
            public int phaseSequence;
            public bool gameObjectActive;
            public bool pendingStartWhileInitial;
            public bool pendingEndWhileAppearing;

            public bool Equals(ModuleRemoteStateFingerprint other)
            {
                return phase == other.phase &&
                       phaseSequence == other.phaseSequence &&
                       gameObjectActive == other.gameObjectActive &&
                       pendingStartWhileInitial == other.pendingStartWhileInitial &&
                       pendingEndWhileAppearing == other.pendingEndWhileAppearing;
            }
        }

        private readonly struct PeopleOcclusionMaterialKey : IEquatable<PeopleOcclusionMaterialKey>
        {
            private readonly Material m_SourceMaterial;
            private readonly Shader m_ReplacementShader;

            public PeopleOcclusionMaterialKey(Material sourceMaterial, Shader replacementShader)
            {
                m_SourceMaterial = sourceMaterial;
                m_ReplacementShader = replacementShader;
            }

            public bool Equals(PeopleOcclusionMaterialKey other)
            {
                return ReferenceEquals(m_SourceMaterial, other.m_SourceMaterial) &&
                       ReferenceEquals(m_ReplacementShader, other.m_ReplacementShader);
            }

            public override bool Equals(object obj)
            {
                return obj is PeopleOcclusionMaterialKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                int sourceHash = m_SourceMaterial != null ? m_SourceMaterial.GetInstanceID() : 0;
                int shaderHash = m_ReplacementShader != null ? m_ReplacementShader.GetInstanceID() : 0;
                return (sourceHash * 397) ^ shaderHash;
            }
        }

        private readonly Dictionary<string, GameObject> m_PrefabByModuleId =
            new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, SpawnedExhibit> m_ExhibitsByInstanceId =
            new Dictionary<string, SpawnedExhibit>(StringComparer.Ordinal);

        private readonly HashSet<string> m_SeenInstanceIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<InteractionModule> m_ModuleScratch = new List<InteractionModule>();
        private readonly Dictionary<string, ModuleRemoteStateFingerprint> m_LastAppliedModuleStateById =
            new Dictionary<string, ModuleRemoteStateFingerprint>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<PeopleOcclusionMaterialKey, Material> m_PeopleOcclusionMaterialCache =
            new Dictionary<PeopleOcclusionMaterialKey, Material>();
        private readonly HashSet<string> m_LoggedPeopleOcclusionMessages =
            new HashSet<string>(StringComparer.Ordinal);

        private Transform m_RootTransform;
        private Transform m_ContentRoot;
        private InteractionModuleManager m_ModuleManager;
        private GameObject m_RemoteHandsRig;
        private RemoteHandHandle m_LeftHand;
        private RemoteHandHandle m_RightHand;
        private bool m_RemoteHandsInitialized;
        private ExhibitAppMode m_CurrentAppMode = ExhibitAppMode.Placement;
        private Shader m_PeopleOcclusionLitReplacementShader;
        private Shader m_PeopleOcclusionEffectsReplacementShader;
        private Shader m_IosHumanOcclusionOpaqueShader;
        private Shader m_IosHumanOcclusionTransparentShader;

        public void Initialize(Transform rootTransform)
        {
            m_RootTransform = rootTransform;
            EnsureContentRoot();
            BuildPrefabCatalog();
            EnsureModuleManager();
        }

        public void SetRootTransform(Transform rootTransform)
        {
            m_RootTransform = rootTransform;
            EnsureContentRoot();
            if (m_ContentRoot != null && m_RootTransform != null)
                m_ContentRoot.SetParent(m_RootTransform, false);
        }

        public void ClearRuntimeContent()
        {
            foreach (var pair in m_ExhibitsByInstanceId)
            {
                SpawnedExhibit exhibit = pair.Value;
                if (exhibit?.rootObject != null)
                    Destroy(exhibit.rootObject);
            }

            m_ExhibitsByInstanceId.Clear();
            m_SeenInstanceIds.Clear();
            m_ModuleScratch.Clear();
            m_LastAppliedModuleStateById.Clear();
            m_PrefabByModuleId.Clear();
            ReleasePeopleOcclusionMaterials();

            if (m_RemoteHandsRig != null)
                Destroy(m_RemoteHandsRig);

            if (m_ContentRoot != null)
                Destroy(m_ContentRoot.gameObject);

            m_RemoteHandsRig = null;
            m_LeftHand = null;
            m_RightHand = null;
            m_RemoteHandsInitialized = false;
            m_ModuleManager = null;
            m_ContentRoot = null;
            m_RootTransform = null;
            m_CurrentAppMode = ExhibitAppMode.Placement;
        }

        public void ApplyFullSnapshot(FullSceneSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            ApplyLayoutSnapshot(snapshot.sharedLayout);
            ApplyPlaybackSnapshot(snapshot.playbackSnapshot);
            ApplyRemoteHands(snapshot.remoteHands);
        }

        public void ApplyDeltaEvent(DeltaEvent deltaEvent)
        {
            if (deltaEvent == null)
                return;

            SpectatorDeltaKind kind = (SpectatorDeltaKind)deltaEvent.kind;
            switch (kind)
            {
                case SpectatorDeltaKind.LayoutChanged:
                    ApplyLayoutSnapshot(SpectatorProtocol.DeserializePayload<SharedLayoutSnapshot>(deltaEvent.payloadJson));
                    break;

                case SpectatorDeltaKind.PlaybackChanged:
                case SpectatorDeltaKind.ModeChanged:
                case SpectatorDeltaKind.GroupChanged:
                case SpectatorDeltaKind.ModuleTriggerStart:
                case SpectatorDeltaKind.ModuleTriggerEnd:
                case SpectatorDeltaKind.ModulePhaseCorrection:
                    ApplyPlaybackSnapshot(SpectatorProtocol.DeserializePayload<PlaybackSnapshot>(deltaEvent.payloadJson));
                    break;

                case SpectatorDeltaKind.HandStateChanged:
                    var handBatch = SpectatorProtocol.DeserializePayload<RemoteHandBatch>(deltaEvent.payloadJson);
                    ApplyRemoteHands(handBatch.remoteHands);
                    break;
            }
        }

        private void BuildPrefabCatalog()
        {
            m_PrefabByModuleId.Clear();

            ChapterPlacementDirector director = FindFirstObjectByType<ChapterPlacementDirector>(FindObjectsInactive.Include);
            ChapterPlacementPlan plan = director != null ? director.Plan : null;
            if (plan == null)
                return;

            if (plan.chapters != null)
            {
                for (int c = 0; c < plan.chapters.Count; c++)
                {
                    ChapterPlacementChapter chapter = plan.chapters[c];
                    if (chapter == null || chapter.steps == null)
                        continue;

                    for (int s = 0; s < chapter.steps.Count; s++)
                    {
                        ChapterPlacementStep step = chapter.steps[s];
                        RegisterPrefab(step != null ? step.moduleId : null, step != null ? step.prefab : null);
                    }
                }
            }

            if (plan.stageModules != null)
            {
                for (int i = 0; i < plan.stageModules.Count; i++)
                {
                    ChapterPlacementStageModule stage = plan.stageModules[i];
                    RegisterPrefab(stage != null ? stage.moduleId : null, stage != null ? stage.prefab : null);
                }
            }
        }

        private void RegisterPrefab(string moduleId, GameObject prefab)
        {
            moduleId = NormalizeModuleId(moduleId);
            if (string.IsNullOrEmpty(moduleId) || prefab == null)
                return;

            if (!m_PrefabByModuleId.ContainsKey(moduleId))
                m_PrefabByModuleId[moduleId] = prefab;
        }

        private static string NormalizeModuleId(string moduleId)
        {
            return string.IsNullOrWhiteSpace(moduleId) ? string.Empty : moduleId.Trim();
        }

        private void EnsureContentRoot()
        {
            if (m_RootTransform == null)
                return;

            if (m_ContentRoot != null)
                return;

            var contentRoot = new GameObject("SpectatorContentRoot");
            m_ContentRoot = contentRoot.transform;
            m_ContentRoot.SetParent(m_RootTransform, false);
        }

        private void EnsureModuleManager()
        {
            if (m_ContentRoot == null)
                return;

            if (m_ModuleManager == null)
                m_ModuleManager = m_ContentRoot.GetComponent<InteractionModuleManager>() ?? m_ContentRoot.gameObject.AddComponent<InteractionModuleManager>();
        }

        private void ApplyLayoutSnapshot(SharedLayoutSnapshot snapshot)
        {
            if (snapshot == null || m_ContentRoot == null)
                return;

            m_SeenInstanceIds.Clear();
            SharedLayoutItem[] items = snapshot.items ?? Array.Empty<SharedLayoutItem>();
            for (int i = 0; i < items.Length; i++)
            {
                SharedLayoutItem item = items[i];
                if (item == null)
                    continue;

                string instanceId = string.IsNullOrWhiteSpace(item.instanceId) ? $"{item.moduleId}::{i}" : item.instanceId.Trim();
                m_SeenInstanceIds.Add(instanceId);

                if (!m_ExhibitsByInstanceId.TryGetValue(instanceId, out SpawnedExhibit exhibit))
                    exhibit = CreateExhibit(instanceId, item);

                if (exhibit == null || exhibit.rootObject == null)
                    continue;

                exhibit.moduleId = item.moduleId;
                exhibit.rootObject.transform.localPosition = item.relativePosition.ToVector3();
                exhibit.rootObject.transform.localRotation = item.relativeRotation.ToQuaternion();
                exhibit.rootObject.transform.localScale = item.scale.ToVector3();

                if (exhibit.exhibitInfo != null)
                {
                    exhibit.exhibitInfo.moduleId = item.moduleId;
                    exhibit.exhibitInfo.displayName = item.displayName;
                    exhibit.exhibitInfo.instanceID = instanceId;
                    exhibit.exhibitInfo.placementContentHidden = item.placementContentHidden;
                }

                bool shouldBeActive = m_CurrentAppMode == ExhibitAppMode.Experience || !item.placementContentHidden;
                exhibit.rootObject.SetActive(shouldBeActive);
                ConfigureRemoteModules(exhibit.rootObject);
            }

            RemoveStaleExhibits();
            m_ModuleManager?.RefreshRegistry();
        }

        private SpawnedExhibit CreateExhibit(string instanceId, SharedLayoutItem item)
        {
            string moduleId = NormalizeModuleId(item.moduleId);
            if (!m_PrefabByModuleId.TryGetValue(moduleId, out GameObject prefab) || prefab == null)
            {
                Debug.LogWarning($"[Spectator] Missing prefab for moduleId={moduleId}");
                return null;
            }

            GameObject instance = Instantiate(prefab, m_ContentRoot, false);
            instance.name = $"Spectator_{moduleId}_{instanceId}";
            DisableHostOnlyComponentsRecursive(instance);
            AdaptMaterialsForPeopleOcclusion(instance);

            ExhibitInfo exhibitInfo = instance.GetComponent<ExhibitInfo>();
            if (exhibitInfo == null)
                exhibitInfo = instance.AddComponent<ExhibitInfo>();

            exhibitInfo.moduleId = moduleId;
            exhibitInfo.displayName = item.displayName;
            exhibitInfo.instanceID = instanceId;
            exhibitInfo.placementContentHidden = item.placementContentHidden;

            var exhibit = new SpawnedExhibit
            {
                instanceId = instanceId,
                moduleId = moduleId,
                rootObject = instance,
                exhibitInfo = exhibitInfo,
            };
            m_ExhibitsByInstanceId[instanceId] = exhibit;
            return exhibit;
        }

        private void RemoveStaleExhibits()
        {
            if (m_ExhibitsByInstanceId.Count == 0)
                return;

            var staleKeys = ListPool<string>.Get();
            try
            {
                foreach (var pair in m_ExhibitsByInstanceId)
                {
                    if (!m_SeenInstanceIds.Contains(pair.Key))
                        staleKeys.Add(pair.Key);
                }

                for (int i = 0; i < staleKeys.Count; i++)
                {
                    string key = staleKeys[i];
                    if (!m_ExhibitsByInstanceId.TryGetValue(key, out SpawnedExhibit exhibit))
                        continue;

                    if (exhibit.rootObject != null)
                        Destroy(exhibit.rootObject);
                    ForgetAppliedRemoteState(exhibit.moduleId);
                    m_ExhibitsByInstanceId.Remove(key);
                }
            }
            finally
            {
                ListPool<string>.Release(staleKeys);
            }
        }

        private static void DisableHostOnlyComponentsRecursive(GameObject root)
        {
            if (root == null)
                return;

            var placementManagers = root.GetComponentsInChildren<ExhibitPlacementManager>(true);
            for (int i = 0; i < placementManagers.Length; i++)
                placementManagers[i].enabled = false;

            var directors = root.GetComponentsInChildren<ChapterPlacementDirector>(true);
            for (int i = 0; i < directors.Length; i++)
                directors[i].enabled = false;

            var handBridges = root.GetComponentsInChildren<AutoHandVisionOSBridge>(true);
            for (int i = 0; i < handBridges.Length; i++)
                handBridges[i].enabled = false;

            var controlPanels = root.GetComponentsInChildren<ExhibitControlPanel>(true);
            for (int i = 0; i < controlPanels.Length; i++)
            {
                ExhibitControlPanel panel = controlPanels[i];
                if (panel == null)
                    continue;

                panel.enabled = false;
                panel.gameObject.SetActive(false);
            }

            var palmRelocators = root.GetComponentsInChildren<PlacementPanelPalmRelocator>(true);
            for (int i = 0; i < palmRelocators.Length; i++)
            {
                PlacementPanelPalmRelocator relocator = palmRelocators[i];
                if (relocator == null)
                    continue;

                relocator.enabled = false;
                relocator.gameObject.SetActive(false);
            }

            DisableCamerasAndAudioListeners(root);
        }

        private void AdaptMaterialsForPeopleOcclusion(GameObject root)
        {
            if (root == null)
                return;

            if (Application.isEditor)
            {
                LogPeopleOcclusionMessageOnce(
                    "editor-spectator-occlusion-bypass",
                    "[SpectatorOcclusion] Skipping spectator content material adaptation in the editor. " +
                    "The direct iOS human-occlusion shader path only runs on-device.");
                return;
            }

            if (PlatformRuntime.IsIOSButNotVisionOS)
            {
                LogPeopleOcclusionMessageOnce(
                    "ios-direct-human-occlusion-route",
                    "[SpectatorOcclusion] iOS spectator content will use the direct human-occlusion shader path.");
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            int adaptedRendererCount = 0;
            int adaptedMaterialCount = 0;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!TryResolvePeopleOcclusionReplacementShader(renderer, out Shader replacementShader))
                    continue;

                Material[] sharedMaterials = GetRendererMaterialsForOcclusion(renderer);
                if (sharedMaterials == null || sharedMaterials.Length == 0)
                {
                    LogPeopleOcclusionMessageOnce(
                        $"missing-renderer-materials::{renderer.GetType().Name}",
                        $"[SpectatorOcclusion] Renderer '{renderer.name}' of type '{renderer.GetType().Name}' does not expose materials for spectator occlusion adaptation.");
                    continue;
                }

                bool changed = false;
                for (int j = 0; j < sharedMaterials.Length; j++)
                {
                    Material sourceMaterial = sharedMaterials[j];
                    Material adaptedMaterial = GetOrCreatePeopleOcclusionMaterial(sourceMaterial, replacementShader);
                    if (adaptedMaterial == null || ReferenceEquals(adaptedMaterial, sourceMaterial))
                        continue;

                    sharedMaterials[j] = adaptedMaterial;
                    adaptedMaterialCount++;
                    changed = true;
                }

                if (!changed)
                    continue;

                renderer.sharedMaterials = sharedMaterials;
                adaptedRendererCount++;
            }

            if (adaptedMaterialCount > 0)
            {
                Debug.Log(
                    $"[SpectatorOcclusion] Adapted '{root.name}' for People Occlusion. renderers={adaptedRendererCount}, materials={adaptedMaterialCount}");
            }
        }

        private bool TryResolvePeopleOcclusionReplacementShader(Renderer renderer, out Shader replacementShader)
        {
            replacementShader = null;
            if (renderer == null)
                return false;

            bool useIosDirectHumanOcclusion = PlatformRuntime.IsIOSButNotVisionOS;
            if (renderer is MeshRenderer)
            {
                replacementShader = useIosDirectHumanOcclusion
                    ? ResolveIosHumanOcclusionOpaqueShader()
                    : ResolvePeopleOcclusionLitReplacementShader();
                return replacementShader != null;
            }

            if (renderer is SkinnedMeshRenderer)
            {
                replacementShader = useIosDirectHumanOcclusion
                    ? ResolveIosHumanOcclusionOpaqueShader()
                    : ResolvePeopleOcclusionLitReplacementShader();
                return replacementShader != null;
            }

            string rendererType = renderer.GetType().Name;
            if (renderer is ParticleSystemRenderer ||
                renderer is TrailRenderer ||
                renderer is LineRenderer ||
                rendererType == nameof(SpriteRenderer) ||
                string.Equals(rendererType, "VFXRenderer", StringComparison.Ordinal))
            {
                replacementShader = useIosDirectHumanOcclusion
                    ? ResolveIosHumanOcclusionTransparentShader()
                    : ResolvePeopleOcclusionEffectsReplacementShader();
                return replacementShader != null;
            }

            return false;
        }

        private static Material[] GetRendererMaterialsForOcclusion(Renderer renderer)
        {
            if (renderer == null)
                return Array.Empty<Material>();

            Material[] materials = renderer.sharedMaterials;
            if (materials != null && materials.Length > 0)
                return materials;

            Material sharedMaterial = renderer.sharedMaterial;
            if (sharedMaterial == null)
                return Array.Empty<Material>();

            return new[] { sharedMaterial };
        }

        private Material GetOrCreatePeopleOcclusionMaterial(Material sourceMaterial, Shader replacementShader)
        {
            if (sourceMaterial == null || replacementShader == null)
                return sourceMaterial;

            if (ReferenceEquals(sourceMaterial.shader, replacementShader) ||
                string.Equals(sourceMaterial.shader != null ? sourceMaterial.shader.name : string.Empty, replacementShader.name, StringComparison.Ordinal))
            {
                return sourceMaterial;
            }

            var cacheKey = new PeopleOcclusionMaterialKey(sourceMaterial, replacementShader);
            if (m_PeopleOcclusionMaterialCache.TryGetValue(cacheKey, out Material cachedMaterial) && cachedMaterial != null)
                return cachedMaterial;

            Material adaptedMaterial = new Material(replacementShader)
            {
                name = $"{sourceMaterial.name} [Spectator People Occlusion]"
            };

            bool useVertexColor = IsEffectsRendererShader(replacementShader) || IsIosHumanOcclusionTransparentShader(replacementShader);
            CopyCommonSurfaceProperties(
                sourceMaterial,
                adaptedMaterial,
                useVertexColor,
                forceTransparentBlend: !IsIosHumanOcclusionOpaqueShader(replacementShader));
            m_PeopleOcclusionMaterialCache[cacheKey] = adaptedMaterial;

            LogPeopleOcclusionMessageOnce(
                $"shader-swap::{sourceMaterial.shader?.name}=>{replacementShader.name}",
                $"[SpectatorOcclusion] Replacing unsupported shader '{sourceMaterial.shader?.name}' with '{replacementShader.name}' for iPhone spectator rendering.");
            return adaptedMaterial;
        }

        private Shader ResolvePeopleOcclusionLitReplacementShader()
        {
            if (m_PeopleOcclusionLitReplacementShader != null)
                return m_PeopleOcclusionLitReplacementShader;

            m_PeopleOcclusionLitReplacementShader = Shader.Find(PeopleOcclusionLitReplacementShaderName);
            if (m_PeopleOcclusionLitReplacementShader == null)
            {
                LogPeopleOcclusionMessageOnce(
                    "missing-lit-replacement-shader",
                    $"[SpectatorOcclusion] Failed to find replacement shader '{PeopleOcclusionLitReplacementShaderName}'.");
            }

            return m_PeopleOcclusionLitReplacementShader;
        }

        private Shader ResolvePeopleOcclusionEffectsReplacementShader()
        {
            if (m_PeopleOcclusionEffectsReplacementShader != null)
                return m_PeopleOcclusionEffectsReplacementShader;

            m_PeopleOcclusionEffectsReplacementShader = Shader.Find(PeopleOcclusionEffectsReplacementShaderName);
            if (m_PeopleOcclusionEffectsReplacementShader == null)
            {
                LogPeopleOcclusionMessageOnce(
                    "missing-effects-replacement-shader",
                    $"[SpectatorOcclusion] Failed to find replacement shader '{PeopleOcclusionEffectsReplacementShaderName}'.");
            }

            return m_PeopleOcclusionEffectsReplacementShader;
        }

        private Shader ResolveIosHumanOcclusionOpaqueShader()
        {
            if (m_IosHumanOcclusionOpaqueShader != null)
                return m_IosHumanOcclusionOpaqueShader;

            m_IosHumanOcclusionOpaqueShader = Resources.Load<Shader>(IosHumanOcclusionOpaqueShaderResourcePath);
            if (m_IosHumanOcclusionOpaqueShader == null)
                m_IosHumanOcclusionOpaqueShader = Shader.Find(IosHumanOcclusionOpaqueShaderName);

            if (m_IosHumanOcclusionOpaqueShader == null)
            {
                LogPeopleOcclusionMessageOnce(
                    "missing-ios-opaque-human-occlusion-shader",
                    $"[SpectatorOcclusion] Failed to find iOS human-occlusion shader '{IosHumanOcclusionOpaqueShaderName}'.");
            }

            return m_IosHumanOcclusionOpaqueShader;
        }

        private Shader ResolveIosHumanOcclusionTransparentShader()
        {
            if (m_IosHumanOcclusionTransparentShader != null)
                return m_IosHumanOcclusionTransparentShader;

            m_IosHumanOcclusionTransparentShader = Resources.Load<Shader>(IosHumanOcclusionTransparentShaderResourcePath);
            if (m_IosHumanOcclusionTransparentShader == null)
                m_IosHumanOcclusionTransparentShader = Shader.Find(IosHumanOcclusionTransparentShaderName);

            if (m_IosHumanOcclusionTransparentShader == null)
            {
                LogPeopleOcclusionMessageOnce(
                    "missing-ios-transparent-human-occlusion-shader",
                    $"[SpectatorOcclusion] Failed to find iOS human-occlusion shader '{IosHumanOcclusionTransparentShaderName}'.");
            }

            return m_IosHumanOcclusionTransparentShader;
        }

        private static bool IsEffectsRendererShader(Shader shader)
        {
            return shader != null &&
                   string.Equals(shader.name, PeopleOcclusionEffectsReplacementShaderName, StringComparison.Ordinal);
        }

        private static bool IsIosHumanOcclusionOpaqueShader(Shader shader)
        {
            return shader != null &&
                   string.Equals(shader.name, IosHumanOcclusionOpaqueShaderName, StringComparison.Ordinal);
        }

        private static bool IsIosHumanOcclusionTransparentShader(Shader shader)
        {
            return shader != null &&
                   string.Equals(shader.name, IosHumanOcclusionTransparentShaderName, StringComparison.Ordinal);
        }

        private void ReleasePeopleOcclusionMaterials()
        {
            foreach (var pair in m_PeopleOcclusionMaterialCache)
            {
                if (pair.Value != null)
                    Destroy(pair.Value);
            }

            m_PeopleOcclusionMaterialCache.Clear();
            m_LoggedPeopleOcclusionMessages.Clear();
            m_PeopleOcclusionLitReplacementShader = null;
            m_PeopleOcclusionEffectsReplacementShader = null;
            m_IosHumanOcclusionOpaqueShader = null;
            m_IosHumanOcclusionTransparentShader = null;
        }

        private void LogPeopleOcclusionMessageOnce(string key, string message)
        {
            if (!m_LoggedPeopleOcclusionMessages.Add(key))
                return;

            Debug.Log(message);
        }

        private static void CopyCommonSurfaceProperties(Material source, Material target, bool useVertexColor, bool forceTransparentBlend)
        {
            if (source == null || target == null)
                return;

            CopyTextureWithScaleAndOffset(
                source,
                target,
                BaseMapId,
                "_BaseMap",
                "_MainTex",
                "_BaseColorMap",
                "_UnlitColorMap",
                "_ColorMap",
                "_BaseColorTexture");

            Color baseColor = TryGetColor(source, "_BaseColor", out Color sourceBaseColor)
                ? sourceBaseColor
                : TryGetColor(source, "_Color", out Color sourceColor)
                    ? sourceColor
                    : TryGetColor(source, "_TintColor", out Color tintColor)
                        ? tintColor
                        : TryGetColor(source, "_UnlitColor", out Color unlitColor)
                            ? unlitColor
                            : source.color;

            if (target.HasProperty(BaseColorId))
                target.SetColor(BaseColorId, baseColor);

            if (target.HasProperty(UseVertexColorId))
                target.SetFloat(UseVertexColorId, useVertexColor ? 1f : 0f);

            if (target.HasProperty(SmoothnessId) && TryGetFloat(source, "_Smoothness", out float smoothness))
                target.SetFloat(SmoothnessId, smoothness);

            if (target.HasProperty(SpecColorId) && TryGetColor(source, "_SpecColor", out Color specColor))
                target.SetColor(SpecColorId, specColor);

            if (CopyTexture(source, target, SpecGlossMapId, "_SpecGlossMap"))
                target.EnableKeyword("_SPECGLOSSMAP");

            if (CopyTexture(source, target, BumpMapId, "_BumpMap"))
            {
                target.EnableKeyword("_NORMALMAP");
                if (target.HasProperty(BumpScaleId) && TryGetFloat(source, "_BumpScale", out float bumpScale))
                    target.SetFloat(BumpScaleId, bumpScale);
            }

            bool hasEmission = false;
            if (CopyTexture(source, target, EmissionMapId, "_EmissionMap"))
                hasEmission = true;

            if (target.HasProperty(EmissionColorId) && TryGetColor(source, "_EmissionColor", out Color emissionColor))
            {
                target.SetColor(EmissionColorId, emissionColor);
                if (emissionColor.maxColorComponent > 0.001f)
                    hasEmission = true;
            }

            if (hasEmission)
                target.EnableKeyword("_EMISSION");

            bool alphaClipEnabled = false;
            if (target.HasProperty(CutoffId) && TryGetFloat(source, "_Cutoff", out float cutoff))
            {
                target.SetFloat(CutoffId, cutoff);
                alphaClipEnabled = cutoff > 0.001f;
            }

            if (TryGetFloat(source, "_AlphaClip", out float alphaClip))
                alphaClipEnabled |= alphaClip > 0.5f;

            if (source.IsKeywordEnabled("_ALPHATEST_ON"))
                alphaClipEnabled = true;

            if (target.HasProperty(AlphaClipId))
                target.SetFloat(AlphaClipId, alphaClipEnabled ? 1f : 0f);
            if (alphaClipEnabled && target.HasProperty(AlphaClipId))
                target.EnableKeyword("_ALPHATEST_ON");

            if (target.HasProperty(CullId) && TryGetFloat(source, "_Cull", out float cullMode))
                target.SetFloat(CullId, cullMode);

            ConfigureBlendMode(source, target, baseColor.a, alphaClipEnabled, forceTransparentBlend);
        }

        private static void ConfigureBlendMode(Material source, Material target, float alpha, bool alphaClipEnabled, bool forceTransparentBlend)
        {
            bool hasExplicitSrcBlend = TryGetFloat(source, "_SrcBlend", out float srcBlend);
            bool hasExplicitDstBlend = TryGetFloat(source, "_DstBlend", out float dstBlend);
            bool hasExplicitSrcBlendAlpha = TryGetFloat(source, "_SrcBlendAlpha", out float srcBlendAlpha);
            bool hasExplicitDstBlendAlpha = TryGetFloat(source, "_DstBlendAlpha", out float dstBlendAlpha);
            bool hasExplicitSurface = TryGetFloat(source, "_Surface", out float sourceSurface);
            bool hasExplicitBlendMode = TryGetFloat(source, "_Blend", out float sourceBlendMode);

            if (forceTransparentBlend)
            {
                if (target.HasProperty(SurfaceId))
                    target.SetFloat(SurfaceId, hasExplicitSurface && sourceSurface > 0.5f ? sourceSurface : 1f);
                if (target.HasProperty(BlendId))
                    target.SetFloat(BlendId, hasExplicitBlendMode ? sourceBlendMode : 0f);
                if (target.HasProperty(SrcBlendId))
                    target.SetFloat(SrcBlendId, hasExplicitSrcBlend ? srcBlend : (float)BlendMode.SrcAlpha);
                if (target.HasProperty(DstBlendId))
                    target.SetFloat(DstBlendId, hasExplicitDstBlend ? dstBlend : (float)BlendMode.OneMinusSrcAlpha);
                if (target.HasProperty(SrcBlendAlphaId))
                    target.SetFloat(SrcBlendAlphaId, hasExplicitSrcBlendAlpha ? srcBlendAlpha : (float)BlendMode.One);
                if (target.HasProperty(DstBlendAlphaId))
                    target.SetFloat(DstBlendAlphaId, hasExplicitDstBlendAlpha ? dstBlendAlpha : (float)BlendMode.OneMinusSrcAlpha);
                if (target.HasProperty(ZWriteId))
                    target.SetFloat(ZWriteId, 0f);
                target.renderQueue = source.renderQueue >= (int)RenderQueue.Transparent
                    ? source.renderQueue
                    : (int)RenderQueue.Transparent;
                target.SetOverrideTag("RenderType", "Transparent");
                return;
            }

            if (target.HasProperty(SurfaceId))
                target.SetFloat(SurfaceId, hasExplicitSurface ? sourceSurface : 0f);
            if (target.HasProperty(BlendId))
                target.SetFloat(BlendId, hasExplicitBlendMode ? sourceBlendMode : 0f);
            if (target.HasProperty(SrcBlendId))
                target.SetFloat(SrcBlendId, hasExplicitSrcBlend ? srcBlend : (float)BlendMode.One);
            if (target.HasProperty(DstBlendId))
                target.SetFloat(DstBlendId, hasExplicitDstBlend ? dstBlend : (float)BlendMode.Zero);
            if (target.HasProperty(SrcBlendAlphaId))
                target.SetFloat(SrcBlendAlphaId, hasExplicitSrcBlendAlpha ? srcBlendAlpha : (float)BlendMode.One);
            if (target.HasProperty(DstBlendAlphaId))
                target.SetFloat(DstBlendAlphaId, hasExplicitDstBlendAlpha ? dstBlendAlpha : (float)BlendMode.Zero);
            if (target.HasProperty(ZWriteId))
            {
                if (TryGetFloat(source, "_ZWrite", out float sourceZWrite))
                    target.SetFloat(ZWriteId, sourceZWrite);
                else
                    target.SetFloat(ZWriteId, 1f);
            }

            target.renderQueue = source.renderQueue >= 0
                ? source.renderQueue
                : alphaClipEnabled
                    ? (int)RenderQueue.AlphaTest
                    : (int)RenderQueue.Geometry;
            target.SetOverrideTag("RenderType", alphaClipEnabled ? "TransparentCutout" : "Opaque");
        }

        private static void CopyTextureWithScaleAndOffset(Material source, Material target, int targetPropertyId, params string[] sourcePropertyNames)
        {
            if (source == null || target == null || sourcePropertyNames == null || !target.HasProperty(targetPropertyId))
                return;

            for (int i = 0; i < sourcePropertyNames.Length; i++)
            {
                string propertyName = sourcePropertyNames[i];
                if (string.IsNullOrEmpty(propertyName) || !source.HasProperty(propertyName))
                    continue;

                Texture texture = source.GetTexture(propertyName);
                if (texture == null)
                    continue;

                target.SetTexture(targetPropertyId, texture);
                target.SetTextureScale(BaseMapId, source.GetTextureScale(propertyName));
                target.SetTextureOffset(BaseMapId, source.GetTextureOffset(propertyName));
                return;
            }
        }

        private static bool CopyTexture(Material source, Material target, int targetPropertyId, string sourcePropertyName)
        {
            if (source == null ||
                target == null ||
                !target.HasProperty(targetPropertyId) ||
                string.IsNullOrEmpty(sourcePropertyName) ||
                !source.HasProperty(sourcePropertyName))
            {
                return false;
            }

            Texture texture = source.GetTexture(sourcePropertyName);
            if (texture == null)
                return false;

            target.SetTexture(targetPropertyId, texture);
            return true;
        }

        private static bool TryGetFloat(Material material, string propertyName, out float value)
        {
            if (material != null && !string.IsNullOrEmpty(propertyName) && material.HasProperty(propertyName))
            {
                value = material.GetFloat(propertyName);
                return true;
            }

            value = 0f;
            return false;
        }

        private static bool TryGetColor(Material material, string propertyName, out Color value)
        {
            if (material != null && !string.IsNullOrEmpty(propertyName) && material.HasProperty(propertyName))
            {
                value = material.GetColor(propertyName);
                return true;
            }

            value = default;
            return false;
        }

        private void ConfigureRemoteModules(GameObject exhibitRoot)
        {
            if (exhibitRoot == null)
                return;

            InteractionModule[] modules = exhibitRoot.GetComponentsInChildren<InteractionModule>(true);
            for (int i = 0; i < modules.Length; i++)
                modules[i].SetRemoteControlEnabled(true);
        }

        private void ApplyPlaybackSnapshot(PlaybackSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            m_CurrentAppMode = (ExhibitAppMode)snapshot.appMode;
            EnsureModuleManager();
            m_ModuleManager?.RefreshRegistry();

            m_ModuleScratch.Clear();
            InteractionModule[] modules = m_ContentRoot != null
                ? m_ContentRoot.GetComponentsInChildren<InteractionModule>(true)
                : Array.Empty<InteractionModule>();
            for (int i = 0; i < modules.Length; i++)
                m_ModuleScratch.Add(modules[i]);

            ModuleRemoteStateSnapshot[] states = snapshot.moduleStates ?? Array.Empty<ModuleRemoteStateSnapshot>();
            for (int i = 0; i < m_ModuleScratch.Count; i++)
            {
                InteractionModule module = m_ModuleScratch[i];
                if (module == null)
                    continue;

                module.SetRemoteControlEnabled(true);
                ModuleRemoteStateSnapshot state = FindState(states, module.ModuleId);
                if (state != null)
                {
                    if (ShouldApplyRemotePlaybackState(state))
                        module.ApplyRemotePlaybackState(state);

                    if (!module.gameObject.activeSelf && state.gameObjectActive)
                        module.gameObject.SetActive(true);
                }
                else
                {
                    ForgetAppliedRemoteState(module.ModuleId);
                    module.ForceResetToStart();
                }
            }

            foreach (var pair in m_ExhibitsByInstanceId)
            {
                SpawnedExhibit exhibit = pair.Value;
                if (exhibit == null || exhibit.rootObject == null || exhibit.exhibitInfo == null)
                    continue;

                bool shouldBeActive = m_CurrentAppMode == ExhibitAppMode.Experience || !exhibit.exhibitInfo.placementContentHidden;
                exhibit.rootObject.SetActive(shouldBeActive);
                AdaptMaterialsForPeopleOcclusion(exhibit.rootObject);
            }
        }

        private static ModuleRemoteStateSnapshot FindState(ModuleRemoteStateSnapshot[] states, string moduleId)
        {
            if (states == null || string.IsNullOrWhiteSpace(moduleId))
                return null;

            for (int i = 0; i < states.Length; i++)
            {
                ModuleRemoteStateSnapshot candidate = states[i];
                if (candidate == null)
                    continue;
                if (string.Equals(candidate.moduleId, moduleId, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }

            return null;
        }

        private bool ShouldApplyRemotePlaybackState(ModuleRemoteStateSnapshot state)
        {
            if (state == null)
                return false;

            string moduleId = NormalizeModuleId(state.moduleId);
            if (string.IsNullOrEmpty(moduleId))
                return true;

            ModuleRemoteStateFingerprint next = CreateFingerprint(state);
            if (m_LastAppliedModuleStateById.TryGetValue(moduleId, out ModuleRemoteStateFingerprint current) &&
                current.Equals(next))
            {
                return false;
            }

            m_LastAppliedModuleStateById[moduleId] = next;
            return true;
        }

        private void ForgetAppliedRemoteState(string moduleId)
        {
            moduleId = NormalizeModuleId(moduleId);
            if (string.IsNullOrEmpty(moduleId))
                return;

            m_LastAppliedModuleStateById.Remove(moduleId);
        }

        private static ModuleRemoteStateFingerprint CreateFingerprint(ModuleRemoteStateSnapshot state)
        {
            return new ModuleRemoteStateFingerprint
            {
                phase = state.phase,
                phaseSequence = state.phaseSequence,
                gameObjectActive = state.gameObjectActive,
                pendingStartWhileInitial = state.pendingStartWhileInitial,
                pendingEndWhileAppearing = state.pendingEndWhileAppearing,
            };
        }

        private void ApplyRemoteHands(RemoteHandState[] remoteHands)
        {
            if (remoteHands == null || remoteHands.Length == 0)
                return;

            EnsureRemoteHands();
            if (!m_RemoteHandsInitialized)
                return;

            for (int i = 0; i < remoteHands.Length; i++)
            {
                RemoteHandState state = remoteHands[i];
                if (state == null)
                    continue;

                RemoteHandHandle handle = (SpectatorHandedness)state.handedness == SpectatorHandedness.Left
                    ? m_LeftHand
                    : m_RightHand;
                ApplyRemoteHand(handle, state);
            }
        }

        private void EnsureRemoteHands()
        {
            if (m_RemoteHandsInitialized)
                return;

            GameObject xrPlayerPrefab = ResolveRemoteHandRigPrefab();
            if (xrPlayerPrefab == null)
            {
                Debug.LogWarning("[SpectatorContent] Remote hand rig prefab was not found. AutoHand remote hands will stay disabled.", this);
                return;
            }

            m_RemoteHandsRig = Instantiate(xrPlayerPrefab, transform, false);
            m_RemoteHandsRig.name = "SpectatorRemoteHandsRig";
            DisableAllBehaviours(m_RemoteHandsRig);
            DisablePhysics(m_RemoteHandsRig);
            DisableCamerasAndAudioListeners(m_RemoteHandsRig);

            Hand[] hands = m_RemoteHandsRig.GetComponentsInChildren<Hand>(true);
            for (int i = 0; i < hands.Length; i++)
            {
                Hand hand = hands[i];
                if (!IsRuntimeDrivenHand(hand))
                    continue;

                var handle = new RemoteHandHandle
                {
                    hand = hand,
                    renderers = hand.GetComponentsInChildren<Renderer>(true),
                };

                if (hand.left)
                {
                    if (IsPreferredRuntimeHand(handle, m_LeftHand))
                        m_LeftHand = handle;
                }
                else
                {
                    if (IsPreferredRuntimeHand(handle, m_RightHand))
                        m_RightHand = handle;
                }
            }

            m_RemoteHandsInitialized = m_LeftHand != null || m_RightHand != null;
            Debug.Log(
                $"[SpectatorContent] Remote hand rig initialized. success={m_RemoteHandsInitialized} left={(m_LeftHand != null ? m_LeftHand.hand.name : "<missing>")} right={(m_RightHand != null ? m_RightHand.hand.name : "<missing>")}",
                this);
        }

        private static void DisableAllBehaviours(GameObject root)
        {
            if (root == null)
                return;

            MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour != null)
                    behaviour.enabled = false;
            }
        }

        private static void DisablePhysics(GameObject root)
        {
            if (root == null)
                return;

            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;

            Rigidbody[] rigidbodies = root.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rigidbodies.Length; i++)
            {
                rigidbodies[i].isKinematic = true;
                rigidbodies[i].detectCollisions = false;
            }
        }

        private static void DisableCamerasAndAudioListeners(GameObject root)
        {
            if (root == null)
                return;

            Camera[] cameras = root.GetComponentsInChildren<Camera>(true);
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera cameraComponent = cameras[i];
                if (cameraComponent == null)
                    continue;

                cameraComponent.enabled = false;
                if (cameraComponent.CompareTag("MainCamera"))
                    cameraComponent.tag = "Untagged";
            }

            AudioListener[] listeners = root.GetComponentsInChildren<AudioListener>(true);
            for (int i = 0; i < listeners.Length; i++)
            {
                AudioListener listener = listeners[i];
                if (listener != null)
                    listener.enabled = false;
            }
        }

        private GameObject ResolveRemoteHandRigPrefab()
        {
            AutoHandVisionOSBridge bridge = FindFirstObjectByType<AutoHandVisionOSBridge>(FindObjectsInactive.Include);
            if (bridge == null)
                return null;

            FieldInfo field = typeof(AutoHandVisionOSBridge).GetField("xrPlayerPrefab", BindingFlags.Instance | BindingFlags.NonPublic);
            return field != null ? field.GetValue(bridge) as GameObject : null;
        }

        private void ApplyRemoteHand(RemoteHandHandle handle, RemoteHandState state)
        {
            if (handle == null || handle.hand == null)
                return;

            bool visible = state.isTracked;
            if (!handle.hand.gameObject.activeSelf)
                handle.hand.gameObject.SetActive(true);

            handle.hand.transform.SetPositionAndRotation(
                state.rootPose.position.ToVector3(),
                state.rootPose.rotation.ToQuaternion());

            float[] curls = state.fingerCurls ?? Array.Empty<float>();
            Finger[] fingers = handle.hand.fingers ?? Array.Empty<Finger>();
            for (int i = 0; i < fingers.Length; i++)
            {
                Finger finger = fingers[i];
                if (finger == null || finger.poseData == null || finger.poseData.Length < 2)
                    continue;

                int fingerIndex = (int)finger.fingerType;
                if (fingerIndex < 0 || fingerIndex >= curls.Length)
                    continue;

                float curl = Mathf.Clamp01(curls[fingerIndex]);
                if (state.isGrabbing)
                    curl = Mathf.Max(curl, 0.65f);

                FingerPoseData fromPose = finger.poseData[(int)FingerPoseEnum.Open];
                FingerPoseData toPose = finger.poseData[(int)FingerPoseEnum.Closed];
                FingerPoseData pose = finger.poseDataNonAlloc;
                pose.LerpData(ref fromPose, ref toPose, curl, false);
                pose.SetFingerPose(finger, handle.hand.transform.rotation, finger.knuckleJoint, finger.middleJoint, finger.distalJoint);
            }

            for (int i = 0; i < handle.renderers.Length; i++)
            {
                Renderer renderer = handle.renderers[i];
                if (renderer == null)
                    continue;

                if (!renderer.gameObject.activeSelf)
                    renderer.gameObject.SetActive(true);

                renderer.enabled = visible;
            }

            handle.heldObjectInstanceId = string.IsNullOrWhiteSpace(state.heldObjectInstanceId)
                ? string.Empty
                : state.heldObjectInstanceId.Trim();

            ApplyHeldObjectPose(state);
        }

        private void ApplyHeldObjectPose(RemoteHandState state)
        {
            if (state == null || !state.isGrabbing || !state.hasHeldObjectRelativePose)
                return;

            string instanceId = string.IsNullOrWhiteSpace(state.heldObjectInstanceId)
                ? string.Empty
                : state.heldObjectInstanceId.Trim();
            if (string.IsNullOrEmpty(instanceId))
                return;

            if (!m_ExhibitsByInstanceId.TryGetValue(instanceId, out SpawnedExhibit exhibit))
                return;

            if (exhibit == null || exhibit.rootObject == null)
                return;

            Transform exhibitTransform = exhibit.rootObject.transform;
            exhibitTransform.localPosition = state.heldObjectRelativePose.position.ToVector3();
            exhibitTransform.localRotation = state.heldObjectRelativePose.rotation.ToQuaternion();
        }

        private static bool IsRuntimeDrivenHand(Hand hand)
        {
            if (hand == null)
                return false;

            string handName = hand.gameObject.name;
            if (handName.Contains("Projection", StringComparison.OrdinalIgnoreCase))
                return false;

            return hand.follow != null || hand.palmTransform != null;
        }

        private static bool IsPreferredRuntimeHand(RemoteHandHandle candidate, RemoteHandHandle current)
        {
            if (candidate == null || candidate.hand == null)
                return false;

            if (current == null || current.hand == null)
                return true;

            return GetRuntimeHandPriority(candidate.hand) > GetRuntimeHandPriority(current.hand);
        }

        private static int GetRuntimeHandPriority(Hand hand)
        {
            if (hand == null)
                return int.MinValue;

            int priority = 0;
            string handName = hand.gameObject.name;
            string followName = hand.follow != null ? hand.follow.name : string.Empty;

            if (hand.gameObject.activeSelf)
                priority += 100;
            if (hand.gameObject.activeInHierarchy)
                priority += 20;
            if (handName.Contains("RobotHand", StringComparison.OrdinalIgnoreCase))
                priority += 1000;
            if (followName.Contains("OpenXR", StringComparison.OrdinalIgnoreCase))
                priority += 300;
            if (handName.Contains("Classic Hand", StringComparison.OrdinalIgnoreCase))
                priority -= 100;

            return priority;
        }

        [Serializable]
        private sealed class RemoteHandBatch
        {
            public RemoteHandState[] remoteHands = Array.Empty<RemoteHandState>();
        }

        private static class ListPool<T>
        {
            private static readonly Stack<List<T>> s_Pool = new Stack<List<T>>();

            public static List<T> Get()
            {
                return s_Pool.Count > 0 ? s_Pool.Pop() : new List<T>();
            }

            public static void Release(List<T> list)
            {
                if (list == null)
                    return;

                list.Clear();
                s_Pool.Push(list);
            }
        }
    }
}
