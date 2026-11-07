using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;
using UnityEngine;
using UnityEngine.VFX;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VFXViewer;

namespace Interaction
{
    public partial class InteractionModule
    {
        private struct PlacementPreviewRendererState
        {
            public Renderer renderer;
            public bool forceRenderingOff;
        }

        private struct PlacementPreviewCanvasState
        {
            public Canvas canvas;
            public bool enabled;
        }

        private struct PlacementPreviewVisualEffectState
        {
            public VisualEffect visualEffect;
            public bool enabled;
        }

        private struct PlacementPreviewTransformState
        {
            public Transform transform;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
            public bool activeSelf;
        }

        private struct PlacementPreviewAnimatorState
        {
            public Animator animator;
            public bool enabled;
        }

        private struct PlacementPreviewTeleportFxState
        {
            public Behaviour behaviour;
            public bool enabled;
            public string stateName;
        }

        private sealed class PlacementPreviewRootCache
        {
            public readonly GameObject root;
            public readonly MonoBehaviour[] behaviours;
            public readonly Renderer[] renderers;
            public readonly Canvas[] canvases;
            public readonly VisualEffect[] visualEffects;
            public readonly bool hasPreviewVfx;
            public readonly bool isVideoRoot;
            public readonly bool isPointCloudRoot;

            public PlacementPreviewRootCache(GameObject root)
            {
                this.root = root;
                behaviours = root != null ? root.GetComponentsInChildren<MonoBehaviour>(true) : new MonoBehaviour[0];
                renderers = root != null ? root.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
                canvases = root != null ? root.GetComponentsInChildren<Canvas>(true) : new Canvas[0];
                visualEffects = root != null ? root.GetComponentsInChildren<VisualEffect>(true) : new VisualEffect[0];
                hasPreviewVfx = visualEffects.Length > 0;
                isVideoRoot = root != null && ContentHandleFactory.DetectType(root) == ContentType.Video;

                for (int i = 0; i < behaviours.Length; i++)
                {
                    if (behaviours[i] is PointCloudController)
                    {
                        isPointCloudRoot = true;
                        break;
                    }
                }
            }
        }

        private const string PlacementPreviewTeleportFxTypeName = "TeleportFX.KriptoFX_Teleportation";
        private const string PlacementPreviewTeleportFxDefaultEnabledStateName = "DefaultEnabled";
        private const BindingFlags PlacementPreviewReflectionFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private bool _isPlacementPreviewActive;
        private Coroutine _placementPreviewCoroutine;
        private bool _placementPreviewVisibilityCaptured;
        private bool _placementPreviewRuntimeCaptured;
        private readonly List<PlacementPreviewRendererState> _placementPreviewRendererStates = new List<PlacementPreviewRendererState>();
        private readonly List<PlacementPreviewCanvasState> _placementPreviewCanvasStates = new List<PlacementPreviewCanvasState>();
        private readonly List<PlacementPreviewVisualEffectState> _placementPreviewVisualEffectStates = new List<PlacementPreviewVisualEffectState>();
        private readonly List<PlacementPreviewTransformState> _placementPreviewTransformStates = new List<PlacementPreviewTransformState>();
        private readonly List<PlacementPreviewAnimatorState> _placementPreviewAnimatorStates = new List<PlacementPreviewAnimatorState>();
        private readonly List<PlacementPreviewTeleportFxState> _placementPreviewTeleportFxStates = new List<PlacementPreviewTeleportFxState>();
        private readonly List<GameObject> _placementPreviewContentRootsBuffer = new List<GameObject>();
        private readonly HashSet<int> _placementPreviewContentRootIdsBuffer = new HashSet<int>();
        private readonly List<GameObject> _placementPreviewTargetsBuffer = new List<GameObject>();
        private readonly HashSet<int> _placementPreviewTargetIdsBuffer = new HashSet<int>();
        private readonly HashSet<int> _placementPreviewTargetTransformIdsBuffer = new HashSet<int>();
        private readonly Dictionary<int, PlacementPreviewRootCache> _placementPreviewRootCaches = new Dictionary<int, PlacementPreviewRootCache>();

        public bool IsPlacementPreviewActive => _isPlacementPreviewActive;

        public void EnterPlacementPreview()
        {
            _isPlacementPreviewActive = true;
            ClearPlacementPreviewRootCaches();
            CancelPlacementPreviewRuntimeOperations();
            ResetPlacementGuidePathPreviewTracking();
            MarkPlacementGuidePathPreviewDirty(invalidateSortedModulesCache: true);
            RestorePlacementPreviewRuntimeState(placementMode: true);
            CapturePlacementPreviewVisibilityStates();
            KillDelayedCalls();
            SetRegisteredTriggersEnabled(false);
            _pendingStartWhileInitial = false;
            _pendingEndWhileAppearing = false;
            _appearedContents.Clear();
            _currentAppearHandles.Clear();
            SetPhase(InteractionPhase.Start);
            ApplyPlacementPreviewVisibility();
            SchedulePlacementPreviewRefreshNextFrame();
        }

        public void ExitPlacementPreview()
        {
            if (!_isPlacementPreviewActive)
                return;

            _isPlacementPreviewActive = false;
            ResetPlacementGuidePathPreviewTracking();
            MarkPlacementGuidePathPreviewDirty(invalidateSortedModulesCache: true);
            StopPlacementPreviewCoroutine();
            DestroyPlacementGuidePathPreview();
            RestorePlacementPreviewVisibilityStates();
            RestorePlacementPreviewRuntimeState(placementMode: false);
            RebuildRuntimeStateFromConfig();
            ClearPlacementPreviewRootCaches();
        }

        private void SchedulePlacementPreviewRefreshNextFrame()
        {
            StopPlacementPreviewCoroutine();
            if (!isActiveAndEnabled)
                return;

            _placementPreviewCoroutine = StartCoroutine(ApplyPlacementPreviewNextFrame());
        }

        private IEnumerator ApplyPlacementPreviewNextFrame()
        {
            yield return null;
            if (!_isPlacementPreviewActive)
            {
                _placementPreviewCoroutine = null;
                yield break;
            }

            ApplyPlacementPreviewVisibility();

            yield return new WaitForEndOfFrame();
            if (!_isPlacementPreviewActive)
            {
                _placementPreviewCoroutine = null;
                yield break;
            }

            ApplyPlacementPreviewVisibility();

            yield return null;
            _placementPreviewCoroutine = null;

            if (!_isPlacementPreviewActive)
                yield break;

            ApplyPlacementPreviewVisibility();
        }

        private void StopPlacementPreviewCoroutine()
        {
            if (_placementPreviewCoroutine == null)
                return;

            StopCoroutine(_placementPreviewCoroutine);
            _placementPreviewCoroutine = null;
        }

        private void CancelPlacementPreviewRuntimeOperations()
        {
            StopPlacementPreviewCoroutine();
            StopAllCoroutines();
            CancelPlacementPreviewEffectRunners();
            KillPlacementPreviewTweens();
            KillDelayedCalls();
        }

        private void ApplyPlacementPreviewVisibility()
        {
            if (gameObject != null && !gameObject.activeSelf)
                gameObject.SetActive(true);

            EnsurePlacementGuidePathPreview();
            InvalidatePlacementPreviewRootCache(gameObject);
            var contentRoots = CollectPlacementContentRootsNonAlloc();
            var targets = CollectPlacementPreviewTargetsNonAlloc();
            BuildPlacementPreviewTargetTransformIds(targets);
            ApplyPlacementPreviewVisibilityMask();

            for (int i = 0; i < targets.Count; i++)
            {
                var root = targets[i];
                if (root == null)
                    continue;

                bool hasPreviewVfx = HasPlacementPreviewVfxCached(root);
                bool shouldActivateVideo = ShouldActivatePlacementVideoPreviewContent(root);
                bool shouldActivatePointCloud = ShouldActivatePlacementPointCloudPreviewContent(root);
                bool isPointCloudRoot = IsPlacementPointCloudRootCached(root);

                ForcePlacementPreviewTargetVisible(root);
                SetContentAlpha(root, 1f);

                if (!hasPreviewVfx && shouldActivateVideo)
                    ForcePreviewShowForContentRoot(root);

                if (!isPointCloudRoot || shouldActivatePointCloud)
                    ForcePlacementPreviewVfxVisible(root);
            }

            ApplyPlacementPreviewHeavyContentSuppression(contentRoots);
        }

        private List<GameObject> CollectPlacementPreviewTargetsNonAlloc()
        {
            _placementPreviewTargetsBuffer.Clear();
            _placementPreviewTargetIdsBuffer.Clear();

            void AddTarget(GameObject root)
            {
                if (root == null)
                    return;

                int id = root.GetInstanceID();
                if (_placementPreviewTargetIdsBuffer.Add(id))
                    _placementPreviewTargetsBuffer.Add(root);
            }

            void AddContentTarget(GameObject root)
            {
                if (!ShouldIncludePlacementPreviewContentRoot(root))
                    return;

                AddTarget(root);
            }

            if (initialEffectEntries != null)
            {
                for (int i = 0; i < initialEffectEntries.Count; i++)
                {
                    var entry = initialEffectEntries[i];
                    if (entry == null)
                        continue;

                    if (entry.targetType == EffectTargetType.Guide)
                    {
                        AddLocalPlacementPreviewTarget(entry.guideWaypoint, _placementPreviewTargetsBuffer, _placementPreviewTargetIdsBuffer);
                        AddLocalPlacementPreviewTarget(entry.guidePathRoot, _placementPreviewTargetsBuffer, _placementPreviewTargetIdsBuffer);
                        continue;
                    }

                    if (entry.targetType == EffectTargetType.System)
                        continue;

                    AddContentTarget(entry.contentRoot);
                }
            }

            if (appearEffectEntries != null)
            {
                for (int i = 0; i < appearEffectEntries.Count; i++)
                {
                    var entry = appearEffectEntries[i];
                    if (entry == null)
                        continue;

                    if (entry.targetType == EffectTargetType.Guide)
                    {
                        AddLocalPlacementPreviewTarget(entry.guideWaypoint, _placementPreviewTargetsBuffer, _placementPreviewTargetIdsBuffer);
                        AddLocalPlacementPreviewTarget(entry.guidePathRoot, _placementPreviewTargetsBuffer, _placementPreviewTargetIdsBuffer);
                        continue;
                    }

                    if (entry.targetType == EffectTargetType.System)
                        continue;

                    AddContentTarget(entry.contentRoot);
                }
            }

            if (disappearEffectEntries != null)
            {
                for (int i = 0; i < disappearEffectEntries.Count; i++)
                {
                    var entry = disappearEffectEntries[i];
                    if (entry == null)
                        continue;

                    if (entry.targetType == EffectTargetType.Guide)
                    {
                        AddLocalPlacementPreviewTarget(entry.guideWaypoint, _placementPreviewTargetsBuffer, _placementPreviewTargetIdsBuffer);
                        AddLocalPlacementPreviewTarget(entry.guidePathRoot, _placementPreviewTargetsBuffer, _placementPreviewTargetIdsBuffer);
                        continue;
                    }

                    if (entry.targetType == EffectTargetType.System)
                        continue;

                    AddContentTarget(entry.contentRoot);
                }
            }

            AddLocalPlacementPreviewTarget(ResolveLocalTouchInteractable(touchInteractable), _placementPreviewTargetsBuffer, _placementPreviewTargetIdsBuffer);
            AddLocalPlacementPreviewTarget(startLongPressInteractable, _placementPreviewTargetsBuffer, _placementPreviewTargetIdsBuffer);
            AddLocalPlacementPreviewTarget(endLongPressInteractable, _placementPreviewTargetsBuffer, _placementPreviewTargetIdsBuffer);
            AddLocalPlacementPreviewTarget(enterRegionCollider, _placementPreviewTargetsBuffer, _placementPreviewTargetIdsBuffer);
            AddLocalPlacementPreviewTarget(leaveRegionCollider, _placementPreviewTargetsBuffer, _placementPreviewTargetIdsBuffer);
            AddPlacementGuidePreviewTarget(_placementPreviewTargetsBuffer, _placementPreviewTargetIdsBuffer);

            if (_placementPreviewTargetsBuffer.Count == 0)
                AddTarget(gameObject);

            return _placementPreviewTargetsBuffer;
        }

        private List<GameObject> CollectPlacementPreviewTargets()
        {
            return CollectPlacementPreviewTargetsNonAlloc();
        }

        private List<GameObject> CollectPlacementContentRootsNonAlloc()
        {
            _placementPreviewContentRootsBuffer.Clear();
            _placementPreviewContentRootIdsBuffer.Clear();
            CollectPlacementContentRoots(_placementPreviewContentRootsBuffer, _placementPreviewContentRootIdsBuffer);
            return _placementPreviewContentRootsBuffer;
        }

        private void ApplyPlacementPreviewHeavyContentSuppression(List<GameObject> contentRoots)
        {
            if (contentRoots == null)
                return;

            for (int i = 0; i < contentRoots.Count; i++)
            {
                var root = contentRoots[i];
                if (root == null)
                    continue;

                bool isVideoRoot = IsPlacementVideoRootCached(root);
                bool isPointCloudRoot = IsPlacementPointCloudRootCached(root);
                if (!isVideoRoot && !isPointCloudRoot)
                    continue;

                bool isTargetRoot = _placementPreviewTargetIdsBuffer.Contains(root.GetInstanceID());
                bool shouldKeepVideoActive = isVideoRoot &&
                                             ShouldActivatePlacementVideoPreviewContent(root) &&
                                             isTargetRoot;
                bool shouldKeepPointCloudActive = isPointCloudRoot &&
                                                  ShouldActivatePlacementPointCloudPreviewContent(root) &&
                                                  isTargetRoot;

                if (isVideoRoot && !shouldKeepVideoActive)
                    SuspendPlacementPreviewVideo(root);

                if (isPointCloudRoot && !shouldKeepPointCloudActive)
                    HidePlacementPreviewPointCloud(root);
            }
        }

        private void SuspendPlacementPreviewVideo(GameObject root)
        {
            if (root == null)
                return;

            var handle = GetOrCreateHandle(root);
            if (handle == null || handle.Type != ContentType.Video)
                return;

            CommonHideEffect.SuspendPlacementPreviewVideo(handle);
        }

        private void HidePlacementPreviewPointCloud(GameObject root)
        {
            if (root == null)
                return;

            var behaviours = GetPlacementPreviewRootCache(root).behaviours;
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IPlacementPreviewVFXController previewController)
                    previewController.HideForPlacementPreview();
            }
        }

        private void AddLocalPlacementPreviewTarget(Component component, List<GameObject> targets, HashSet<int> seen)
        {
            if (component == null || targets == null || seen == null)
                return;

            Transform targetTransform = component.transform;
            if (targetTransform == null)
                return;

            if (targetTransform != transform && !targetTransform.IsChildOf(transform))
                return;

            int id = component.gameObject.GetInstanceID();
            if (seen.Add(id))
                targets.Add(component.gameObject);
        }

        private void EnsurePlacementPreviewHierarchyActive(Transform target)
        {
            Transform current = target;
            while (current != null)
            {
                if (!current.gameObject.activeSelf)
                    current.gameObject.SetActive(true);

                if (current == transform)
                    break;

                current = current.parent;
            }
        }

        private void ForcePreviewShowForContentRoot(GameObject root)
        {
            if (root == null)
                return;

            var handle = GetOrCreateHandle(root);
            var showEffect = GetOrAddAppearEffect(AppearEffectType.Show);
            if (handle == null || showEffect == null)
                return;

            showEffect.Appear(handle, 0f, null);
        }

        private void ForcePlacementPreviewVfxVisible(GameObject root)
        {
            if (root == null || VFXRuntimeGuard.DisableUnsupportedVFX(root, this))
                return;

            var cache = GetPlacementPreviewRootCache(root);
            bool invokedController = false;
            int previewControllerCount = 0;
            int vfxControllerCount = 0;
            var behaviours = cache.behaviours;
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IPlacementPreviewVFXController previewController)
                {
                    previewControllerCount++;
                    previewController.ShowForPlacementPreview();
                    invokedController = true;
                    continue;
                }

                if (behaviours[i] is not IVFXController controller)
                    continue;

                vfxControllerCount++;
                controller.Appear(0f, null);
                invokedController = true;
            }

            var visualEffects = cache.visualEffects;
            if (PlacementDebugFileLogger.IsLoggingEnabled &&
                (previewControllerCount > 0 || vfxControllerCount > 0 || visualEffects.Length > 0))
            {
                PlacementDebugFileLogger.Log(
                    $"[PlacementVFX] module={GetModuleDebugName()} root={root.name} activeSelf={root.activeSelf} " +
                    $"activeInHierarchy={root.activeInHierarchy} previewControllers={previewControllerCount} " +
                    $"vfxControllers={vfxControllerCount} visualEffects={visualEffects.Length} invokedController={invokedController}");
            }

            for (int i = 0; i < visualEffects.Length; i++)
            {
                var visualEffect = visualEffects[i];
                if (visualEffect == null)
                    continue;

                visualEffect.enabled = true;
                if (!invokedController)
                {
                    visualEffect.Reinit();
                    visualEffect.Play();
                }

                if (PlacementDebugFileLogger.IsLoggingEnabled)
                {
                    PlacementDebugFileLogger.Log(
                        $"[PlacementVFX] module={GetModuleDebugName()} root={root.name} effect={visualEffect.name} " +
                        $"enabled={visualEffect.enabled} aliveParticles={visualEffect.aliveParticleCount}");

                    var renderer = visualEffect.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        Bounds bounds = renderer.bounds;
                        PlacementDebugFileLogger.Log(
                            $"[PlacementVFX] module={GetModuleDebugName()} root={root.name} renderer={renderer.GetType().Name} " +
                            $"enabled={renderer.enabled} forceRenderingOff={renderer.forceRenderingOff} isVisible={renderer.isVisible} " +
                            $"boundsCenter={bounds.center} boundsSize={bounds.size}");
                    }

                    bool? culled = TryReadVisualEffectBoolProperty(visualEffect, "culled");
                    bool? paused = TryReadVisualEffectBoolProperty(visualEffect, "pause");
                    PlacementDebugFileLogger.Log(
                        $"[PlacementVFX] module={GetModuleDebugName()} root={root.name} effectState culled={FormatNullableBool(culled)} " +
                        $"pause={FormatNullableBool(paused)}");
                }
            }
        }

        private static bool? TryReadVisualEffectBoolProperty(VisualEffect visualEffect, string propertyName)
        {
            if (visualEffect == null || string.IsNullOrWhiteSpace(propertyName))
                return null;

            try
            {
                var property = typeof(VisualEffect).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                if (property == null || property.PropertyType != typeof(bool))
                    return null;

                return (bool)property.GetValue(visualEffect);
            }
            catch
            {
                return null;
            }
        }

        private static string FormatNullableBool(bool? value)
        {
            return value.HasValue ? value.Value.ToString() : "n/a";
        }

        private void CancelPlacementPreviewEffectRunners()
        {
            var shaderRunners = GetComponentsInChildren<ShaderTweenRunner>(true);
            for (int i = 0; i < shaderRunners.Length; i++)
            {
                var runner = shaderRunners[i];
                if (runner == null)
                    continue;

                runner.Cancel();
            }

            var keyframeRunners = GetComponentsInChildren<KeyframeAnimationRunner>(true);
            for (int i = 0; i < keyframeRunners.Length; i++)
            {
                var runner = keyframeRunners[i];
                if (runner == null)
                    continue;

                runner.Cancel();
            }

            var vfxRunners = GetComponentsInChildren<VFXEffectRunner>(true);
            for (int i = 0; i < vfxRunners.Length; i++)
            {
                var runner = vfxRunners[i];
                if (runner == null)
                    continue;

                runner.Cancel();
            }

            var particleRunners = GetComponentsInChildren<ParticleAppearRunner>(true);
            for (int i = 0; i < particleRunners.Length; i++)
            {
                var runner = particleRunners[i];
                if (runner == null)
                    continue;

                runner.Cancel();
            }
        }

        private void KillPlacementPreviewTweens()
        {
            var transforms = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                var targetTransform = transforms[i];
                if (targetTransform == null)
                    continue;

                targetTransform.DOKill();
                DOTween.Kill(targetTransform.gameObject);
            }

            var canvasGroups = GetComponentsInChildren<CanvasGroup>(true);
            for (int i = 0; i < canvasGroups.Length; i++)
            {
                var canvasGroup = canvasGroups[i];
                if (canvasGroup == null)
                    continue;

                canvasGroup.DOKill();
                DOTween.Kill(canvasGroup);
            }

            var renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                var materials = renderer.materials;
                for (int j = 0; j < materials.Length; j++)
                {
                    var material = materials[j];
                    if (material == null)
                        continue;

                    DOTween.Kill(material);
                }
            }
        }

        private void ForcePlacementPreviewTargetVisible(GameObject root)
        {
            if (root == null)
                return;

            if (!root.activeSelf)
                root.SetActive(true);

            EnsurePlacementPreviewHierarchyActive(root.transform);

            var cache = GetPlacementPreviewRootCache(root);
            var renderers = cache.renderers;
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                renderer.enabled = true;
                renderer.forceRenderingOff = false;
            }

            var canvases = cache.canvases;
            for (int i = 0; i < canvases.Length; i++)
            {
                var canvas = canvases[i];
                if (canvas != null)
                    canvas.enabled = true;
            }

            var visualEffects = cache.visualEffects;
            for (int i = 0; i < visualEffects.Length; i++)
            {
                var visualEffect = visualEffects[i];
                if (visualEffect != null)
                    visualEffect.enabled = true;
            }
        }

        private void ApplyPlacementPreviewVisibilityMask()
        {
            var cache = GetPlacementPreviewRootCache(gameObject);
            var renderers = cache.renderers;
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                renderer.forceRenderingOff = !IsPlacementPreviewTarget(renderer.transform);
            }

            var canvases = cache.canvases;
            for (int i = 0; i < canvases.Length; i++)
            {
                var canvas = canvases[i];
                if (canvas == null)
                    continue;

                canvas.enabled = IsPlacementPreviewTarget(canvas.transform);
            }

            var visualEffects = cache.visualEffects;
            for (int i = 0; i < visualEffects.Length; i++)
            {
                var visualEffect = visualEffects[i];
                if (visualEffect == null)
                    continue;

                visualEffect.enabled = IsPlacementPreviewTarget(visualEffect.transform);
            }
        }

        private void ApplyPlacementPreviewVisibilityMask(List<GameObject> targets)
        {
            BuildPlacementPreviewTargetTransformIds(targets);
            ApplyPlacementPreviewVisibilityMask();
        }

        private bool IsPlacementPreviewTarget(Transform candidate)
        {
            if (candidate == null || _placementPreviewTargetTransformIdsBuffer.Count == 0)
                return false;

            for (Transform current = candidate; current != null; current = current.parent)
            {
                if (_placementPreviewTargetTransformIdsBuffer.Contains(current.GetInstanceID()))
                    return true;

                if (current == transform)
                    break;
            }

            return false;
        }

        private void CapturePlacementPreviewVisibilityStates()
        {
            if (_placementPreviewVisibilityCaptured)
                return;

            _placementPreviewRendererStates.Clear();
            _placementPreviewCanvasStates.Clear();
            _placementPreviewVisualEffectStates.Clear();

            var cache = GetPlacementPreviewRootCache(gameObject);
            var renderers = cache.renderers;
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                _placementPreviewRendererStates.Add(new PlacementPreviewRendererState
                {
                    renderer = renderer,
                    forceRenderingOff = renderer.forceRenderingOff
                });
            }

            var canvases = cache.canvases;
            for (int i = 0; i < canvases.Length; i++)
            {
                var canvas = canvases[i];
                if (canvas == null)
                    continue;

                _placementPreviewCanvasStates.Add(new PlacementPreviewCanvasState
                {
                    canvas = canvas,
                    enabled = canvas.enabled
                });
            }

            var visualEffects = cache.visualEffects;
            for (int i = 0; i < visualEffects.Length; i++)
            {
                var visualEffect = visualEffects[i];
                if (visualEffect == null)
                    continue;

                _placementPreviewVisualEffectStates.Add(new PlacementPreviewVisualEffectState
                {
                    visualEffect = visualEffect,
                    enabled = visualEffect.enabled
                });
            }

            _placementPreviewVisibilityCaptured = true;
        }

        private void CapturePlacementPreviewRuntimeStateIfNeeded()
        {
            if (_placementPreviewRuntimeCaptured)
                return;

            _placementPreviewTransformStates.Clear();
            _placementPreviewAnimatorStates.Clear();
            _placementPreviewTeleportFxStates.Clear();

            var transforms = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                var targetTransform = transforms[i];
                if (targetTransform == null)
                    continue;

                _placementPreviewTransformStates.Add(new PlacementPreviewTransformState
                {
                    transform = targetTransform,
                    localPosition = targetTransform.localPosition,
                    localRotation = targetTransform.localRotation,
                    localScale = targetTransform.localScale,
                    activeSelf = targetTransform.gameObject.activeSelf
                });
            }

            var animators = GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                if (animator == null)
                    continue;

                _placementPreviewAnimatorStates.Add(new PlacementPreviewAnimatorState
                {
                    animator = animator,
                    enabled = animator.enabled
                });
            }

            var behaviours = GetComponentsInChildren<Behaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (!TryReadPlacementPreviewTeleportFxState(behaviour, out var stateName))
                    continue;

                _placementPreviewTeleportFxStates.Add(new PlacementPreviewTeleportFxState
                {
                    behaviour = behaviour,
                    enabled = behaviour.enabled,
                    stateName = stateName
                });
            }

            _placementPreviewRuntimeCaptured = true;
        }

        private void RestorePlacementPreviewRuntimeState(bool placementMode)
        {
            CapturePlacementPreviewRuntimeStateIfNeeded();

            for (int i = 0; i < _placementPreviewTransformStates.Count; i++)
            {
                var state = _placementPreviewTransformStates[i];
                var targetTransform = state.transform;
                if (targetTransform == null)
                    continue;

                targetTransform.DOKill();

                if (targetTransform.gameObject.activeSelf != state.activeSelf)
                    targetTransform.gameObject.SetActive(state.activeSelf);

                targetTransform.localPosition = state.localPosition;
                targetTransform.localRotation = state.localRotation;
                targetTransform.localScale = state.localScale;
            }

            for (int i = 0; i < _placementPreviewAnimatorStates.Count; i++)
            {
                var state = _placementPreviewAnimatorStates[i];
                var animator = state.animator;
                if (animator == null)
                    continue;

                if (animator.gameObject.activeInHierarchy)
                {
                    animator.enabled = true;
                    animator.Rebind();
                    animator.Update(0f);
                }

                animator.enabled = placementMode ? false : state.enabled;
            }

            ApplyPlacementPreviewTeleportFxStates(placementMode);

            var targets = CollectPlacementPreviewTargetsNonAlloc();
            for (int i = 0; i < targets.Count; i++)
            {
                var root = targets[i];
                if (root == null)
                    continue;

                if (placementMode)
                    ForcePlacementPreviewTargetVisible(root);
                SetContentAlpha(root, 1f);

                if (!placementMode)
                    RestorePlacementPreviewVfxRuntime(root);
            }
        }

        private static void RestorePlacementPreviewVfxRuntime(GameObject root)
        {
            if (root == null)
                return;

            var behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IPlacementPreviewVFXController previewController)
                    previewController.RestoreAfterPlacementPreview();
            }
        }

        private void ApplyPlacementPreviewTeleportFxStates(bool placementMode)
        {
            for (int i = 0; i < _placementPreviewTeleportFxStates.Count; i++)
            {
                var state = _placementPreviewTeleportFxStates[i];
                var behaviour = state.behaviour;
                if (behaviour == null)
                    continue;

                string targetStateName = placementMode
                    ? PlacementPreviewTeleportFxDefaultEnabledStateName
                    : state.stateName;
                TrySetPlacementPreviewTeleportFxState(behaviour, targetStateName);

                if (!placementMode && behaviour.enabled != state.enabled)
                {
                    behaviour.enabled = state.enabled;
                }

                if (behaviour.isActiveAndEnabled)
                    TryInvokePlacementPreviewTeleportFxManualUpdate(behaviour);
            }
        }

        private static bool TryReadPlacementPreviewTeleportFxState(Behaviour behaviour, out string stateName)
        {
            stateName = null;

            if (!TryGetPlacementPreviewTeleportFxStateField(behaviour, out var stateField))
                return false;

            stateName = stateField.GetValue(behaviour)?.ToString();
            return !string.IsNullOrWhiteSpace(stateName);
        }

        private static bool TrySetPlacementPreviewTeleportFxState(Behaviour behaviour, string stateName)
        {
            if (string.IsNullOrWhiteSpace(stateName) ||
                !TryGetPlacementPreviewTeleportFxStateField(behaviour, out var stateField))
            {
                return false;
            }

            try
            {
                var enumValue = System.Enum.Parse(stateField.FieldType, stateName);
                stateField.SetValue(behaviour, enumValue);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetPlacementPreviewTeleportFxStateField(Behaviour behaviour, out FieldInfo stateField)
        {
            stateField = null;
            if (behaviour == null)
                return false;

            var type = behaviour.GetType();
            if (type.FullName != PlacementPreviewTeleportFxTypeName)
                return false;

            stateField = type.GetField("TeleportationState", PlacementPreviewReflectionFlags);
            return stateField != null;
        }

        private static void TryInvokePlacementPreviewTeleportFxManualUpdate(Behaviour behaviour)
        {
            if (behaviour == null)
                return;

            var method = behaviour.GetType().GetMethod("ManualUpdate", PlacementPreviewReflectionFlags);
            if (method == null || method.GetParameters().Length != 0)
                return;

            try
            {
                method.Invoke(behaviour, null);
            }
            catch
            {
                // Ignore plugin-specific invocation failures and keep placement preview functional.
            }
        }

        private void RestorePlacementPreviewVisibilityStates()
        {
            if (!_placementPreviewVisibilityCaptured)
                return;

            for (int i = 0; i < _placementPreviewRendererStates.Count; i++)
            {
                var state = _placementPreviewRendererStates[i];
                if (state.renderer != null)
                    state.renderer.forceRenderingOff = state.forceRenderingOff;
            }

            for (int i = 0; i < _placementPreviewCanvasStates.Count; i++)
            {
                var state = _placementPreviewCanvasStates[i];
                if (state.canvas != null)
                    state.canvas.enabled = state.enabled;
            }

            for (int i = 0; i < _placementPreviewVisualEffectStates.Count; i++)
            {
                var state = _placementPreviewVisualEffectStates[i];
                if (state.visualEffect != null)
                    state.visualEffect.enabled = state.enabled;
            }

            _placementPreviewRendererStates.Clear();
            _placementPreviewCanvasStates.Clear();
            _placementPreviewVisualEffectStates.Clear();
            _placementPreviewVisibilityCaptured = false;
        }

        private void RebuildRuntimeStateFromConfig()
        {
            for (int i = 0; i < _triggers.Count; i++)
            {
                var trigger = _triggers[i];
                if (trigger == null)
                    continue;

                trigger.Triggered -= OnTriggerFired;
                trigger.Disable();
            }

            _triggers.Clear();
            ClearTriggerCaches();
            _dragReleaseBehaviors.Clear();
            _contentHandleCache.Clear();
            _appearedContents.Clear();
            _currentAppearHandles.Clear();
            _contentHandle = null;
            _appearEffect = null;
            _disappearEffect = null;
            ClearPlacementPreviewRootCaches();
            KillDelayedCalls();
            _pendingStartWhileInitial = false;
            _pendingEndWhileAppearing = false;
            SetPhase(InteractionPhase.Start);

            if (!s_instances.Contains(this))
                s_instances.Add(this);

            ApplyConfig();
        }

        private void BuildPlacementPreviewTargetTransformIds(List<GameObject> targets)
        {
            _placementPreviewTargetTransformIdsBuffer.Clear();
            if (targets == null)
                return;

            for (int i = 0; i < targets.Count; i++)
            {
                var root = targets[i];
                if (root == null || root.transform == null)
                    continue;

                _placementPreviewTargetTransformIdsBuffer.Add(root.transform.GetInstanceID());
            }
        }

        private PlacementPreviewRootCache GetPlacementPreviewRootCache(GameObject root)
        {
            if (root == null)
                return new PlacementPreviewRootCache(null);

            int id = root.GetInstanceID();
            if (_placementPreviewRootCaches.TryGetValue(id, out var cache) && cache.root == root)
                return cache;

            cache = new PlacementPreviewRootCache(root);
            _placementPreviewRootCaches[id] = cache;
            return cache;
        }

        private void ClearPlacementPreviewRootCaches()
        {
            _placementPreviewRootCaches.Clear();
            _placementPreviewContentRootsBuffer.Clear();
            _placementPreviewContentRootIdsBuffer.Clear();
            _placementPreviewTargetsBuffer.Clear();
            _placementPreviewTargetIdsBuffer.Clear();
            _placementPreviewTargetTransformIdsBuffer.Clear();
        }

        private void InvalidatePlacementPreviewRootCache(GameObject root)
        {
            if (root == null)
                return;

            _placementPreviewRootCaches.Remove(root.GetInstanceID());
        }

        private bool HasPlacementPreviewVfxCached(GameObject root)
        {
            return root != null && GetPlacementPreviewRootCache(root).hasPreviewVfx;
        }

        private bool IsPlacementVideoRootCached(GameObject root)
        {
            return root != null && GetPlacementPreviewRootCache(root).isVideoRoot;
        }

        private bool IsPlacementPointCloudRootCached(GameObject root)
        {
            return root != null && GetPlacementPreviewRootCache(root).isPointCloudRoot;
        }
    }
}
