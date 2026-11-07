using System;
using System.Collections.Generic;
using Interaction;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VFXViewer
{
    [Serializable]
    public class RootSessionData
    {
        public string trackableId;
        public string originalTrackableId;
        public string fallbackTrackableId;
    }

    public partial class ExhibitPlacementManager
    {
        [Header("定位点")]
        [SerializeField] private string m_RootObjectName = "PlacementRoot";
        [SerializeField] private GameObject m_RootMarkerPrefab;
        [SerializeField] private float m_RootSpawnDistance = 1.2f;

        private TrackableId m_RootSessionTrackableId = TrackableId.invalidId;
        private string m_RootSessionOriginalTrackableId = string.Empty;
        private string m_RootSessionFallbackTrackableId = string.Empty;
        private bool m_RootVisibleInPlacementMode = true;
        private static readonly string[] s_RootPlacementVisualNames = { "X", "Y", "Z" };
        private const int DefaultRenderLayer = 0;
        private static readonly bool EnableRootVisibilityDiagnostics = false;
        private static readonly bool EnableRootBootstrapDiagnostics = false;

        public bool HasRootMarker => GetRootTransform() != null;
        public bool IsRootMarkerVisible => GetRootMarker() != null && GetRootMarker().IsPresentationVisible;
        public bool IsRootPresentationVisibleForPlayback => GetRootMarker() != null && GetRootMarker().IsPresentationVisible;
        public bool HasRestoredRootAnchor => m_HasRestoredRootExhibit;
        public bool HasSavedRootAnchor => GetRootAnchor() != null;
        public int RestoredSessionAnchorCount => RestoredSessionExhibitCount + (m_HasResolvedRootForPlayback ? 1 : 0);
        public int ExpectedSessionAnchorCount => ExpectedSessionExhibitCount + (HasSavedRootSessionTrackableId() ? 1 : 0);
        public bool HasResolvedAllSessionAnchorsForPlayback =>
            IsRootReadyForPlayback;

        public bool TryCreateRootMarkerAtDefaultPose()
        {
            if (m_CurrentAppMode != ExhibitAppMode.Placement)
            {
                onStatusMessage?.Invoke("当前处于体验模式，无法生成定位点。");
                return false;
            }

            if (GetRootTransform() != null)
            {
                m_RootVisibleInPlacementMode = true;
                SetRootMarkerVisible(true);
                onStatusMessage?.Invoke("定位点已存在。");
                return true;
            }

            // 用户显式新建 Root 时，以当前操作为准，不再等待旧 session 里的 Root 恢复。
            m_RootSessionTrackableId = TrackableId.invalidId;
            SetRootSessionTrackableIds(string.Empty);
            m_HasRestoredRootExhibit = false;
            m_HasResolvedRootForPlayback = false;

            CreateAndPersistDefaultRootMarker();
            return GetRootTransform() != null;
        }

        public bool TryDeleteRootMarker()
        {
            if (m_CurrentAppMode != ExhibitAppMode.Placement)
            {
                onStatusMessage?.Invoke("当前处于体验模式，无法删除定位点。");
                return false;
            }

            Transform rootTransform = GetRootTransform();
            if (rootTransform == null)
            {
                onStatusMessage?.Invoke("当前没有定位点可删除。");
                return false;
            }

            var placedExhibits = CaptureRegisteredExhibitSnapshot();
            if (placedExhibits.Count > 0)
            {
                onStatusMessage?.Invoke("请先删除所有展台和展品，再删除定位点。");
                return false;
            }

            CancelPendingAnchorAttachmentForTarget(rootTransform);
            GlobalGuideRootBinder.DetachGuidesFromRoot(rootTransform);

            ARAnchor parentAnchor = rootTransform.GetComponentInParent<ARAnchor>();
            if (parentAnchor != null)
                QueueAnchorNodeForDestruction(parentAnchor.gameObject);
            else
                Destroy(rootTransform.gameObject);

            m_RuntimeRootTrans = null;
            m_RootSessionTrackableId = TrackableId.invalidId;
            SetRootSessionTrackableIds(string.Empty);
            m_HasRestoredRootExhibit = false;
            m_HasResolvedRootForPlayback = false;
            m_RootVisibleInPlacementMode = true;
            ClearManualSelection(refreshUi: true);
            QueueSaveToFiles(saveLayout: true, suppressStatusMessage: false);
            RefreshPlacementProgressInDirector();
            onStatusMessage?.Invoke("已删除定位点。");
            return true;
        }

        public bool TrySetRootPresentationVisibleForPlayback(bool visible)
        {
            var rootMarker = GetRootMarker();
            if (rootMarker == null)
                return false;

            if (!rootMarker.gameObject.activeSelf)
                rootMarker.gameObject.SetActive(true);

            SyncRootPlacementVisualsForCurrentMode();

            if (rootMarker.IsPresentationVisible == visible)
                return true;

            rootMarker.SetPresentationVisible(visible);
            return true;
        }

        public void NotifyRootMarkerGrabStarted(Transform rootTransform)
        {
            if (rootTransform == null)
                return;

            ParentLooseRootToModuleContainer(rootTransform);
            m_RuntimeRootTrans = rootTransform;
            onSelectionChanged?.Invoke("当前选中: 定位点");
            onStatusMessage?.Invoke("定位点已脱离锚点，请调整后松手保存。");
        }

        public bool TryDetachRootFromCurrentAnchor(Transform rootTransform)
        {
            if (rootTransform == null)
                return false;

            if (CancelPendingAnchorAttachmentForTarget(rootTransform))
            {
                ParentLooseRootToModuleContainer(rootTransform);
                m_RuntimeRootTrans = rootTransform;
                m_RootSessionTrackableId = TrackableId.invalidId;
                SetRootSessionTrackableIds(string.Empty);
                m_HasRestoredRootExhibit = false;
                m_HasResolvedRootForPlayback = false;
                RefreshPlacementProgressInDirector();
                return true;
            }

            ARAnchor parentAnchor = rootTransform.GetComponentInParent<ARAnchor>();
            if (parentAnchor == null)
                return false;

            GameObject anchorNode = parentAnchor.gameObject;
            rootTransform.SetParent(null, true);
            ParentLooseRootToModuleContainer(rootTransform);

            m_RuntimeRootTrans = rootTransform;
            m_RootSessionTrackableId = TrackableId.invalidId;
            SetRootSessionTrackableIds(string.Empty);
            m_HasRestoredRootExhibit = false;
            m_HasResolvedRootForPlayback = false;

            QueueAnchorNodeForDestruction(anchorNode);
            CleanupOrphanRootAnchors();
            RefreshPlacementProgressInDirector();
            return true;
        }

        public void NotifyRootMarkerReleased(Transform rootTransform)
        {
            if (rootTransform == null)
                return;

            ParentLooseRootToModuleContainer(rootTransform);
            m_RuntimeRootTrans = rootTransform;

            if (!EnsureAnchorForRoot(rootTransform))
                return;

            onStatusMessage?.Invoke("定位点位置已更新，正在重新创建空间锚点。");
        }

        private void EnsureRootStateForCurrentMode()
        {
            SyncRootInteractionModulesForCurrentMode();
            SyncRootPlacementVisualsForCurrentMode();
            SetRootMarkerVisible(ShouldShowRootMarkerInCurrentMode());
            LogRootVisibilityDiagnostics("ensure_root_state_for_current_mode");
        }

        private bool ShouldShowRootMarkerInCurrentMode()
        {
            if (GetRootTransform() == null)
                return false;

            if (m_CurrentAppMode != ExhibitAppMode.Placement)
                return false;

            return m_RootVisibleInPlacementMode;
        }

        private void CreateAndPersistDefaultRootMarker()
        {
            Transform rootMarker = CreateRootMarker(GetDefaultRootPose());
            SetRootMarkerVisible(true);
            LogRootVisibilityDiagnostics("create_and_persist_default_root_marker");
            if (!EnsureAnchorForRoot(rootMarker))
                return;

            onStatusMessage?.Invoke("已创建定位点，正在创建空间锚点。");
        }

        public bool TryPlaceRootMarkerAtPose(Pose pose, bool selectRoot = true)
        {
            if (m_CurrentAppMode != ExhibitAppMode.Placement)
            {
                onStatusMessage?.Invoke("当前处于体验模式，无法根据识别图片放置定位点。");
                return false;
            }

            Transform rootTransform = GetRootTransform();
            if (rootTransform != null)
            {
                TryDetachRootFromCurrentAnchor(rootTransform);
                CancelPendingAnchorAttachmentForTarget(rootTransform);
            }
            else
            {
                rootTransform = CreateRootMarker(pose);
            }

            if (rootTransform == null)
            {
                onStatusMessage?.Invoke("根据识别图片放置定位点失败。");
                return false;
            }

            ParentLooseRootToModuleContainer(rootTransform);
            rootTransform.SetPositionAndRotation(pose.position, pose.rotation);

            m_RuntimeRootTrans = rootTransform;
            m_RootVisibleInPlacementMode = true;
            m_RootSessionTrackableId = TrackableId.invalidId;
            SetRootSessionTrackableIds(string.Empty);
            m_HasRestoredRootExhibit = false;
            m_HasResolvedRootForPlayback = false;

            SetRootMarkerVisible(true);
            LogRootVisibilityDiagnostics("try_place_root_marker_at_pose");
            if (selectRoot)
                SetManualSelection(rootTransform);

            RefreshPlacementProgressInDirector();

            if (!EnsureAnchorForRoot(rootTransform))
            {
                onStatusMessage?.Invoke("已更新定位点位置，但重新创建空间锚点失败。");
                return false;
            }

            onStatusMessage?.Invoke("已根据识别图片更新定位点，正在创建空间锚点。");
            return true;
        }

        private Pose GetDefaultRootPose()
        {
            if (m_MainCamera == null)
                m_MainCamera = Camera.main;

            if (m_MainCamera == null)
                return new Pose(Vector3.zero, Quaternion.identity);

            Transform cameraTrans = m_MainCamera.transform;
            float distance = m_RootSpawnDistance <= 0.1f ? 1.2f : m_RootSpawnDistance;
            float configuredDistance = ResolveRootSpawnDistance();
            if (configuredDistance > 0.1f)
                distance = configuredDistance;
            Vector3 position = cameraTrans.position + (cameraTrans.forward * distance);
            position.y -= 0.25f;

            Vector3 lookDirection = cameraTrans.position - position;
            lookDirection.y = 0f;
            Quaternion rotation = lookDirection.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(lookDirection.normalized)
                : Quaternion.identity;

            return new Pose(position, rotation);
        }

        private Transform CreateRootMarker(Pose pose, bool suppressInteractionModuleAutoStart = false)
        {
            GameObject marker = CreateRootMarkerVisual(suppressInteractionModuleAutoStart);
            marker.name = m_RootObjectName;
            marker.transform.SetPositionAndRotation(pose.position, pose.rotation);
            EnsureRootMarkerInteractionComponents(marker);

            ParentLooseRootToModuleContainer(marker.transform);
            m_RuntimeRootTrans = marker.transform;
            m_RootVisibleInPlacementMode = true;
            SyncRootInteractionModulesForCurrentMode();
            SyncRootPlacementVisualsForCurrentMode();
            SetRootMarkerVisible(ShouldShowRootMarkerInCurrentMode());
            LogRootVisibilityDiagnostics("create_root_marker");
            return marker.transform;
        }

        private void SyncRootInteractionModulesForCurrentMode()
        {
            if (m_CurrentAppMode != ExhibitAppMode.Placement)
                return;

            Transform rootTransform = GetRootTransform();
            if (rootTransform == null)
                return;

            var modules = rootTransform.GetComponentsInChildren<InteractionModule>(true);
            for (int i = 0; i < modules.Length; i++)
            {
                InteractionModule module = modules[i];
                if (module == null)
                    continue;

                module.StopAllCoroutines();
                module.ForceResetToStart();
                module.enabled = false;
            }
        }

        private void SyncRootPlacementVisualsForCurrentMode()
        {
            Transform rootTransform = GetRootTransform();
            if (rootTransform == null)
                return;

            ApplyRootPlacementGrabConfiguration(rootTransform.gameObject);

            bool showPlacementVisuals = m_CurrentAppMode == ExhibitAppMode.Placement;
            for (int i = 0; i < s_RootPlacementVisualNames.Length; i++)
            {
                Transform child = rootTransform.Find(s_RootPlacementVisualNames[i]);
                if (child == null || child.gameObject.activeSelf == showPlacementVisuals)
                    continue;

                child.gameObject.SetActive(showPlacementVisuals);
            }

            LogRootVisibilityDiagnostics($"sync_root_placement_visuals showPlacementVisuals={showPlacementVisuals}");
        }

        public bool TrySetRootMarkerVisible(bool visible, bool notifyUI = true)
        {
            if (m_CurrentAppMode != ExhibitAppMode.Placement)
            {
                onStatusMessage?.Invoke("当前处于体验模式，无法切换定位点显示。");
                return false;
            }

            PlacementRootMarker rootMarker = GetRootMarker();
            if (rootMarker == null)
            {
                onStatusMessage?.Invoke("当前没有定位点可切换显示。");
                return false;
            }

            m_RootVisibleInPlacementMode = visible;
            SyncRootPlacementVisualsForCurrentMode();
            SetRootMarkerVisible(visible);
            LogRootVisibilityDiagnostics($"try_set_root_marker_visible visible={visible}");

            if (visible)
                SelectRootMarker(notifyUI: false);

            onStatusMessage?.Invoke(visible ? "已显示定位点。" : "已隐藏定位点。");

            if (notifyUI)
                NotifyUIUpdate();

            return true;
        }

        private GameObject CreateRootMarkerVisual(bool suppressInteractionModuleAutoStart = false)
        {
            GameObject configuredPrefab = ResolveRootMarkerPrefabFromPlan();
            if (configuredPrefab != null)
            {
                if (suppressInteractionModuleAutoStart)
                    return InstantiateRootMarkerVisualWithoutAutoStartingModules(configuredPrefab);

                return Instantiate(configuredPrefab);
            }

            if (m_RootMarkerPrefab != null)
            {
                if (suppressInteractionModuleAutoStart)
                    return InstantiateRootMarkerVisualWithoutAutoStartingModules(m_RootMarkerPrefab);

                return Instantiate(m_RootMarkerPrefab);
            }

            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.transform.localScale = new Vector3(0.12f, 0.01f, 0.12f);
            return marker;
        }

        private GameObject InstantiateRootMarkerVisualWithoutAutoStartingModules(GameObject prefab)
        {
            if (prefab == null)
                return null;

            var stagingRoot = new GameObject("__RootBootstrapStaging")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            stagingRoot.SetActive(false);

            GameObject marker = null;
            try
            {
                marker = Instantiate(prefab, stagingRoot.transform, false);
                DisableRootInteractionModulesForBootstrap(marker);
                marker.transform.SetParent(null, false);
                return marker;
            }
            finally
            {
                Destroy(stagingRoot);
            }
        }

        private static void DisableRootInteractionModulesForBootstrap(GameObject marker)
        {
            if (marker == null)
                return;

            var modules = marker.GetComponentsInChildren<InteractionModule>(true);
            for (int i = 0; i < modules.Length; i++)
            {
                InteractionModule module = modules[i];
                if (module == null)
                    continue;

                module.enabled = false;
                if (EnableRootBootstrapDiagnostics)
                {
                    PlacementDebugFileLogger.Log(
                        $"[PlaybackFlow] bootstrap_root_module_pre_disabled module={module.name} moduleId={module.ModuleId}");
                }
            }
        }

        private GameObject ResolveRootMarkerPrefabFromPlan()
        {
            ChapterPlacementPlan plan = ResolvePlacementPlan();
            if (plan == null || plan.rootSettings == null)
                return null;

            return plan.rootSettings.markerPrefab;
        }

        private float ResolveRootSpawnDistance()
        {
            ChapterPlacementPlan plan = ResolvePlacementPlan();
            if (plan == null || plan.rootSettings == null)
                return m_RootSpawnDistance;

            return plan.rootSettings.spawnDistance;
        }

        private Vector3 GetExpectedRootMarkerLocalScale()
        {
            PlacementRootMarker runtimeRootMarker = GetRootMarker();
            if (runtimeRootMarker != null && IsMeaningfulRootMarkerScale(runtimeRootMarker.transform.localScale))
                return runtimeRootMarker.transform.localScale;

            GameObject configuredPrefab = ResolveRootMarkerPrefabFromPlan();
            if (configuredPrefab == null)
                configuredPrefab = m_RootMarkerPrefab;

            if (configuredPrefab != null && IsMeaningfulRootMarkerScale(configuredPrefab.transform.localScale))
                return configuredPrefab.transform.localScale;

            return new Vector3(0.12f, 0.01f, 0.12f);
        }

        private static bool IsMeaningfulRootMarkerScale(Vector3 scale)
        {
            return Mathf.Abs(scale.x) > 0.0001f ||
                   Mathf.Abs(scale.y) > 0.0001f ||
                   Mathf.Abs(scale.z) > 0.0001f;
        }

        private void EnsureRootMarkerInteractionComponents(GameObject marker)
        {
            if (marker == null)
                return;

            ApplyRootPlacementGrabConfiguration(marker);

            if (marker.GetComponent<Rigidbody>() == null)
                marker.AddComponent<Rigidbody>();

            XRGrabInteractable interactable = marker.GetComponent<XRGrabInteractable>();
            if (interactable == null)
                interactable = marker.AddComponent<XRGrabInteractable>();

            ConfigureRootGrabInteractable(marker.transform, interactable);

            if (marker.GetComponent<PlacementRootMarker>() == null)
                marker.AddComponent<PlacementRootMarker>();

            if (marker.GetComponent<SpectatorPeopleOcclusionRendererAdapter>() == null)
                marker.AddComponent<SpectatorPeopleOcclusionRendererAdapter>();
        }

        private void ApplyRootPlacementGrabConfiguration(GameObject marker)
        {
            if (marker == null)
                return;

            marker.layer = DefaultRenderLayer;
            RestoreRootVisualLayers(marker.transform);
            EnsureRootPlacementGrabCollider(marker.transform);

            XRGrabInteractable interactable = marker.GetComponent<XRGrabInteractable>();
            if (interactable != null)
                ConfigureRootGrabInteractable(marker.transform, interactable);

            LogRootVisibilityDiagnostics("apply_root_placement_grab_configuration");
        }

        private void ConfigureRootGrabInteractable(Transform rootTransform, XRGrabInteractable interactable)
        {
            if (rootTransform == null || interactable == null)
                return;

            BoxCollider placementGrabCollider = EnsureRootPlacementGrabCollider(rootTransform);
            if (placementGrabCollider == null)
                return;

            interactable.useDynamicAttach = true;
            interactable.movementType = XRBaseInteractable.MovementType.Instantaneous;
            interactable.throwOnDetach = false;
            interactable.attachTransform = placementGrabCollider.transform;
            interactable.colliders.Clear();
            interactable.colliders.Add(placementGrabCollider);
        }

        private BoxCollider EnsureRootPlacementGrabCollider(Transform rootTransform)
        {
            if (rootTransform == null)
                return null;

            Transform colliderTransform = rootTransform.Find(PlacementGrabColliderName);
            if (colliderTransform == null)
            {
                var colliderObject = new GameObject(PlacementGrabColliderName);
                colliderTransform = colliderObject.transform;
                colliderTransform.SetParent(rootTransform, false);
                colliderTransform.localPosition = Vector3.zero;
                colliderTransform.localRotation = Quaternion.identity;
                colliderTransform.localScale = Vector3.one;
            }

            colliderTransform.gameObject.layer = TryResolvePlacementGrabLayer(out int placementGrabLayer)
                ? placementGrabLayer
                : DefaultRenderLayer;

            BoxCollider boxCollider = colliderTransform.GetComponent<BoxCollider>();
            if (boxCollider == null)
                boxCollider = colliderTransform.gameObject.AddComponent<BoxCollider>();

            Bounds localBounds = CalculateLocalRendererBounds(rootTransform);
            colliderTransform.localRotation = Quaternion.identity;
            colliderTransform.localScale = Vector3.one;
            if (localBounds.size.sqrMagnitude <= 0.000001f)
            {
                colliderTransform.localPosition = Vector3.zero;
                boxCollider.center = Vector3.zero;
                boxCollider.size = new Vector3(0.15f, 0.08f, 0.15f);
            }
            else
            {
                Vector3 size = localBounds.size;
                size.x = Mathf.Max(size.x, 0.12f);
                size.y = Mathf.Max(size.y, 0.08f);
                size.z = Mathf.Max(size.z, 0.12f);
                colliderTransform.localPosition = localBounds.center;
                boxCollider.center = Vector3.zero;
                boxCollider.size = size;
            }

            boxCollider.isTrigger = false;
            boxCollider.enabled = true;
            return boxCollider;
        }

        private static void RestoreRootVisualLayers(Transform root)
        {
            if (root == null)
                return;

            for (int i = 0; i < root.childCount; i++)
                SetLayerRecursively(root.GetChild(i), DefaultRenderLayer);
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            if (root == null)
                return;

            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
                SetLayerRecursively(root.GetChild(i), layer);
        }

        private static Bounds CalculateLocalRendererBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return default;

            bool hasBounds = false;
            Bounds localBounds = default;
            Matrix4x4 worldToLocal = root.worldToLocalMatrix;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                Bounds rendererBounds = renderer.bounds;
                Vector3 min = rendererBounds.min;
                Vector3 max = rendererBounds.max;
                Vector3[] corners =
                {
                    new Vector3(min.x, min.y, min.z),
                    new Vector3(min.x, min.y, max.z),
                    new Vector3(min.x, max.y, min.z),
                    new Vector3(min.x, max.y, max.z),
                    new Vector3(max.x, min.y, min.z),
                    new Vector3(max.x, min.y, max.z),
                    new Vector3(max.x, max.y, min.z),
                    new Vector3(max.x, max.y, max.z),
                };

                for (int j = 0; j < corners.Length; j++)
                {
                    Vector3 localPoint = worldToLocal.MultiplyPoint3x4(corners[j]);
                    if (!hasBounds)
                    {
                        localBounds = new Bounds(localPoint, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(localPoint);
                    }
                }
            }

            return localBounds;
        }

        private void ParentLooseRootToModuleContainer(Transform rootTransform)
        {
            if (rootTransform == null)
                return;

            var containerRoot = ResolveModuleContainerRoot();
            if (containerRoot == null)
                return;

            rootTransform.SetParent(containerRoot, true);
        }

        private void SetRootMarkerVisible(bool visible)
        {
            var rootMarker = GetRootMarker();
            if (rootMarker == null)
                return;

            if (!rootMarker.gameObject.activeSelf)
                rootMarker.gameObject.SetActive(true);

            if (rootMarker.IsPresentationVisible == visible)
            {
                LogRootVisibilityDiagnostics($"set_root_marker_visible skipped visible={visible}");
                return;
            }

            rootMarker.SetPresentationVisible(visible);
            LogRootVisibilityDiagnostics($"set_root_marker_visible applied visible={visible}");
        }

        private void LogRootVisibilityDiagnostics(string reason)
        {
            if (!EnableRootVisibilityDiagnostics)
                return;

            Transform rootTransform = GetRootTransform();
            PlacementRootMarker rootMarker = GetRootMarker();
            if (rootTransform == null || rootMarker == null)
            {
                LogAnchorLifecycle($"[RootVisibility] reason={reason}, rootExists={(rootTransform != null)}, markerExists={(rootMarker != null)}");
                return;
            }

            var renderers = rootTransform.GetComponentsInChildren<Renderer>(true);
            int enabledRendererCount = 0;
            int disabledRendererCount = 0;
            int forceRenderingOffCount = 0;
            var rendererStates = new List<string>(renderers.Length);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                if (renderer.enabled)
                    enabledRendererCount++;
                else
                    disabledRendererCount++;

                if (renderer.forceRenderingOff)
                    forceRenderingOffCount++;

                rendererStates.Add(
                    $"{GetRelativeTransformPath(rootTransform, renderer.transform)}:enabled={renderer.enabled},forceOff={renderer.forceRenderingOff}");
            }

            var placementVisualStates = new List<string>(s_RootPlacementVisualNames.Length);
            for (int i = 0; i < s_RootPlacementVisualNames.Length; i++)
            {
                Transform child = rootTransform.Find(s_RootPlacementVisualNames[i]);
                placementVisualStates.Add(child == null
                    ? $"{s_RootPlacementVisualNames[i]}:missing"
                    : $"{s_RootPlacementVisualNames[i]}:activeSelf={child.gameObject.activeSelf},activeInHierarchy={child.gameObject.activeInHierarchy},layer={LayerMask.LayerToName(child.gameObject.layer)}({child.gameObject.layer})");
            }

            Transform placementGrabTransform = rootTransform.Find(PlacementGrabColliderName);
            string placementGrabState = placementGrabTransform == null
                ? "missing"
                : $"activeSelf={placementGrabTransform.gameObject.activeSelf},layer={LayerMask.LayerToName(placementGrabTransform.gameObject.layer)}({placementGrabTransform.gameObject.layer}),colliderEnabled={placementGrabTransform.GetComponent<Collider>()?.enabled}";

            LogAnchorLifecycle(
                $"[RootVisibility] reason={reason}, appMode={m_CurrentAppMode}, shouldShow={ShouldShowRootMarkerInCurrentMode()}, " +
                $"placementVisibleFlag={m_RootVisibleInPlacementMode}, presentationVisible={rootMarker.IsPresentationVisible}, " +
                $"rootActiveSelf={rootTransform.gameObject.activeSelf}, rootActiveInHierarchy={rootTransform.gameObject.activeInHierarchy}, " +
                $"rootLayer={LayerMask.LayerToName(rootTransform.gameObject.layer)}({rootTransform.gameObject.layer}), " +
                $"renderersEnabled={enabledRendererCount}, renderersDisabled={disabledRendererCount}, forceRenderingOff={forceRenderingOffCount}, " +
                $"placementVisuals=[{string.Join(", ", placementVisualStates)}], placementGrab={placementGrabState}, " +
                $"rendererStates=[{string.Join(", ", rendererStates)}], {DescribeTransformPose(rootTransform)}");
        }

        private static string GetRelativeTransformPath(Transform root, Transform target)
        {
            if (root == null || target == null)
                return "<null>";

            if (root == target)
                return root.name;

            var segments = new List<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                segments.Add(current.name);
                current = current.parent;
            }

            if (current == root)
                segments.Add(root.name);

            segments.Reverse();
            return string.Join("/", segments);
        }

        private PlacementRootMarker GetRootMarker()
        {
            if (m_RuntimeRootTrans != null)
            {
                var runtimeMarker = m_RuntimeRootTrans.GetComponent<PlacementRootMarker>();
                if (runtimeMarker != null)
                    return runtimeMarker;
            }

            var markers = FindObjectsByType<PlacementRootMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (markers == null || markers.Length == 0)
                return null;

            m_RuntimeRootTrans = markers[0].transform;
            return markers[0];
        }

        private ARAnchor GetRootAnchor()
        {
            Transform root = GetRootTransform();
            return root != null ? root.GetComponentInParent<ARAnchor>() : null;
        }

        private bool EnsureAnchorForRoot(Transform rootTransform)
        {
            if (rootTransform == null)
            {
                onStatusMessage?.Invoke("保存定位点失败：未找到定位点。");
                return false;
            }

            ARAnchor existingAnchor = rootTransform.GetComponentInParent<ARAnchor>();
            if (existingAnchor != null)
            {
                m_RuntimeRootTrans = rootTransform;
                m_RootSessionTrackableId = existingAnchor.trackableId;
                if (m_CurrentAppMode == ExhibitAppMode.Placement)
                    SetRootSessionTrackableIds(existingAnchor.trackableId.ToString());
                return true;
            }

            m_RuntimeRootTrans = rootTransform;
            return TryBeginAnchorAttachment(
                rootTransform,
                new Pose(rootTransform.position, rootTransform.rotation),
                "Anchor_Root",
                isRoot: true,
                saveLayout: HasAnySceneExhibits(),
                suppressStatusMessage: false,
                displayName: "定位点");
        }

        private void SaveRootStateToFiles()
        {
            SaveRootStateToFiles(null, HasAnySceneExhibits());
        }

        private void SaveRootStateToFiles(string fallbackTrackableId)
        {
            SaveRootStateToFiles(fallbackTrackableId, HasAnySceneExhibits());
        }

        private void SaveRootStateToFiles(string fallbackTrackableId, bool saveLayout)
        {
            ARAnchor rootAnchor = GetRootAnchor();
            if (rootAnchor != null)
            {
                m_RootSessionTrackableId = rootAnchor.trackableId;
                if (m_CurrentAppMode == ExhibitAppMode.Experience && !string.IsNullOrWhiteSpace(fallbackTrackableId))
                {
                    string originalTrackableId = string.IsNullOrWhiteSpace(GetOriginalRootCandidateTrackableId())
                        ? rootAnchor.trackableId.ToString()
                        : GetOriginalRootCandidateTrackableId();
                    SetRootSessionTrackableIds(originalTrackableId, fallbackTrackableId);
                }
                else
                {
                    SetRootSessionTrackableIds(rootAnchor.trackableId.ToString());
                }
            }

            if (saveLayout)
                MarkAllRegisteredExhibitPersistenceDirty(markSession: false, markLayout: true);
            SaveAllToFiles(saveLayout, suppressStatusMessage: true);
            onStatusMessage?.Invoke("定位点已固定。");
        }

        private void SaveRootStateToFilesPreservingCandidates(bool saveLayout)
        {
            ARAnchor rootAnchor = GetRootAnchor();
            if (rootAnchor != null)
                m_RootSessionTrackableId = rootAnchor.trackableId;

            if (saveLayout)
                MarkAllRegisteredExhibitPersistenceDirty(markSession: false, markLayout: true);

            SaveAllToFiles(saveLayout, suppressStatusMessage: true);
        }

        private bool HasAnySceneExhibits()
        {
            return HasRegisteredSceneExhibits();
        }

        private bool TryRestoreRootMarkerOnAnchor(ARAnchor anchor)
        {
            if (anchor == null ||
                (!HasSavedRootSessionTrackableId() &&
                 m_RootSessionTrackableId == TrackableId.invalidId) ||
                (!RootSessionContainsTrackableId(anchor.trackableId) &&
                 anchor.trackableId != m_RootSessionTrackableId))
            {
                return false;
            }

            bool hasSamples = TryEvaluateRootPoseConfidence(
                new Pose(anchor.transform.position, anchor.transform.rotation),
                out RestoreConfidence candidateConfidence,
                out string candidateDiagnostics,
                out _,
                out float candidateMaxPositionErrorMeters,
                out float candidateMaxRotationErrorDegrees);

            LogAnchorLifecycle(
                $"restore_attempt_root: {DescribeRootRestoreSource(anchor.trackableId.ToString())}, " +
                $"candidateConfidence={FormatRestoreConfidence(candidateConfidence)}, sampleBased={hasSamples}, " +
                $"maxPositionError={candidateMaxPositionErrorMeters:F4}, maxRotationError={candidateMaxRotationErrorDegrees:F2}, " +
                $"{DescribeAnchorState(anchor)}");
            if (hasSamples && candidateConfidence == RestoreConfidence.Unsafe)
            {
                LogAnchorLifecycle(
                    $"restore_rejected_inconsistent_root: trackableId={anchor.trackableId}, " +
                    $"maxPositionError={candidateMaxPositionErrorMeters:F4}, maxRotationError={candidateMaxRotationErrorDegrees:F2}, diagnostics={candidateDiagnostics}");
                return false;
            }

            PlacementRootMarker existingRootMarker = GetRootMarker();
            if (existingRootMarker != null)
            {
                ARAnchor existingAnchor = existingRootMarker.GetComponentInParent<ARAnchor>();
                if (existingAnchor != null && existingAnchor != anchor)
                {
                    bool hasCurrentSamples = TryEvaluateRootPoseConfidence(
                        new Pose(existingRootMarker.transform.position, existingRootMarker.transform.rotation),
                        out RestoreConfidence currentConfidence,
                        out _,
                        out _,
                        out _,
                        out _);
                    bool shouldSwitch = hasCurrentSamples && currentConfidence == RestoreConfidence.Unsafe
                        ? candidateConfidence != RestoreConfidence.Unsafe
                        : ShouldPreferRootRestoreCandidate(anchor.trackableId, existingAnchor.trackableId);
                    if (!shouldSwitch)
                    {
                        m_RuntimeRootTrans = existingRootMarker.transform;
                        m_RootSessionTrackableId = existingAnchor.trackableId;
                        m_HasRestoredRootExhibit = true;
                        m_HasResolvedRootForPlayback = true;
                        SetRootRestoreRuntimeState(
                            GetAnchoredRootRestoreState(existingAnchor.trackableId),
                            hasCurrentSamples ? currentConfidence : RestoreConfidence.Weak,
                            string.Empty,
                            "restore_root_kept_current",
                            existingRootMarker.transform,
                            existingAnchor);
                        SetRootMarkerVisible(ShouldShowRootMarkerInCurrentMode());
                        LogAnchorLifecycle(
                            $"restore root skipped late anchor after anchored restore: " +
                            $"activeAnchor={existingAnchor.trackableId}, activeSource={DescribeRootRestoreSource(existingAnchor.trackableId.ToString())}, " +
                            $"lateAnchor={anchor.trackableId}, lateSource={DescribeRootRestoreSource(anchor.trackableId.ToString())}, " +
                            $"activeConfidence={FormatRestoreConfidence(hasCurrentSamples ? currentConfidence : RestoreConfidence.Weak)}, " +
                            $"candidateConfidence={FormatRestoreConfidence(candidateConfidence)}");
                        return true;
                    }

                    if (anchor.transform.childCount > 0 && !existingRootMarker.transform.IsChildOf(anchor.transform))
                    {
                        LogAnchorLifecycle(
                            $"restore root skipped preferred late anchor occupied: activeAnchor={existingAnchor.trackableId}, " +
                            $"activeSource={DescribeRootRestoreSource(existingAnchor.trackableId.ToString())}, " +
                            $"preferredAnchor={anchor.trackableId}, preferredSource={DescribeRootRestoreSource(anchor.trackableId.ToString())}, {DescribeAnchorState(anchor)}");
                        return true;
                    }

                    existingRootMarker.transform.SetParent(anchor.transform, false);
                    existingRootMarker.transform.localPosition = Vector3.zero;
                    existingRootMarker.transform.localRotation = Quaternion.identity;
                    m_RuntimeRootTrans = existingRootMarker.transform;
                    m_RootSessionTrackableId = anchor.trackableId;
                    m_HasRestoredRootExhibit = true;
                    m_HasResolvedRootForPlayback = true;
                    SetRootRestoreRuntimeState(
                        GetAnchoredRootRestoreState(anchor.trackableId),
                        candidateConfidence,
                        string.Empty,
                        "late_original_takeover_root",
                        existingRootMarker.transform,
                        anchor);
                    CleanupOrphanRootAnchors(existingRootMarker.transform);
                    SetRootMarkerVisible(ShouldShowRootMarkerInCurrentMode());
                    LogAnchorLifecycle(
                        $"restore root switched to preferred late anchor: previousAnchor={existingAnchor.trackableId}, " +
                        $"previousSource={DescribeRootRestoreSource(existingAnchor.trackableId.ToString())}, " +
                        $"preferredAnchor={anchor.trackableId}, preferredSource={DescribeRootRestoreSource(anchor.trackableId.ToString())}, {DescribeAnchorState(anchor)}, " +
                        $"{DescribeTransformPose(existingRootMarker.transform)}");
                    return true;
                }
            }

            TryParentAnchorToModuleContainer(anchor.transform);

            PlacementRootMarker rootMarker = anchor.GetComponentInChildren<PlacementRootMarker>(true);
            if (rootMarker == null && existingRootMarker != null && existingRootMarker.GetComponentInParent<ARAnchor>() == null)
            {
                CancelPendingAnchorAttachmentForTarget(existingRootMarker.transform);
                if (anchor.transform.childCount == 0 || existingRootMarker.transform.IsChildOf(anchor.transform))
                {
                    existingRootMarker.transform.SetParent(anchor.transform, false);
                    existingRootMarker.transform.localPosition = Vector3.zero;
                    existingRootMarker.transform.localRotation = Quaternion.identity;
                    rootMarker = existingRootMarker;
                }
            }

            if (rootMarker == null)
            {
                Transform rootTransform = CreateRootMarker(new Pose(anchor.transform.position, anchor.transform.rotation));
                rootTransform.SetParent(anchor.transform, false);
                rootTransform.localPosition = Vector3.zero;
                rootTransform.localRotation = Quaternion.identity;
                rootMarker = rootTransform.GetComponent<PlacementRootMarker>();
            }

            m_RuntimeRootTrans = rootMarker.transform;
            m_RootSessionTrackableId = anchor.trackableId;
            m_HasRestoredRootExhibit = true;
            m_HasResolvedRootForPlayback = true;
            SetRootRestoreRuntimeState(
                GetAnchoredRootRestoreState(anchor.trackableId),
                hasSamples ? candidateConfidence : RestoreConfidence.Weak,
                string.Empty,
                string.Equals(
                    ClassifyRestoreTrackableSource(
                        GetOriginalRootCandidateTrackableId(),
                        GetFallbackRootCandidateTrackableId(),
                        anchor.trackableId.ToString()),
                    "fallback",
                    StringComparison.Ordinal)
                    ? "restore_bound_fallback_root"
                    : "restore_bound_original_root",
                rootMarker.transform,
                anchor);
            CleanupOrphanRootAnchors(rootMarker.transform);
            SetRootMarkerVisible(ShouldShowRootMarkerInCurrentMode());
            LogAnchorLifecycle(
                $"restore root success: {DescribeRootRestoreSource(anchor.trackableId.ToString())}, " +
                $"{DescribeAnchorState(anchor)}, {DescribeTransformPose(rootMarker.transform)}");
            if (string.IsNullOrWhiteSpace(GetOriginalRootCandidateTrackableId()))
            {
                SetRootSessionTrackableIds(anchor.trackableId.ToString());
                QueueSaveToFiles(saveLayout: false, suppressStatusMessage: true);
            }
            onStatusMessage?.Invoke("自动识别: 定位点");
            return true;
        }

        private RootSessionData BuildCurrentRootSessionData(ARAnchor rootAnchor)
        {
            var rootSessionData = new RootSessionData();
            if (rootAnchor == null)
                return rootSessionData;

            rootSessionData.trackableId = rootAnchor.trackableId.ToString();
            if (m_CurrentAppMode == ExhibitAppMode.Experience && HasSavedRootSessionTrackableId())
            {
                string originalTrackableId = GetOriginalRootCandidateTrackableId();
                string fallbackTrackableId = GetFallbackRootCandidateTrackableId();

                if (string.IsNullOrWhiteSpace(originalTrackableId))
                    originalTrackableId = rootAnchor.trackableId.ToString();
                SetRestoreTrackableIds(rootSessionData, originalTrackableId, fallbackTrackableId);
                return rootSessionData;
            }

            SetRestoreTrackableIds(rootSessionData, rootAnchor.trackableId.ToString());
            return rootSessionData;
        }

        private void CleanupOrphanRootAnchors(Transform activeRootTransform = null)
        {
            var anchors = FindObjectsByType<ARAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < anchors.Length; i++)
            {
                ARAnchor anchor = anchors[i];
                if (anchor == null || !string.Equals(anchor.name, "Anchor_Root", StringComparison.Ordinal))
                    continue;

                var rootMarker = anchor.GetComponentInChildren<PlacementRootMarker>(true);
                if (rootMarker != null && rootMarker.transform == activeRootTransform)
                    continue;

                if (rootMarker != null)
                    continue;

                QueueAnchorNodeForDestruction(anchor.gameObject);
            }
        }

        private bool TryFindLegacyRootTransform(out Transform rootTransform)
        {
            rootTransform = null;
            return TryGetRegisteredExhibitByInstanceId("0", out var info) &&
                   (rootTransform = info != null ? info.transform : null) != null;
        }
    }
}
