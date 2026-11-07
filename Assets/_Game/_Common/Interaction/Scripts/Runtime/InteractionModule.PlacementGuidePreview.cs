using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VFXViewer;

namespace Interaction
{
    public partial class InteractionModule
    {
        private const string PlacementGuidePreviewRootName = "__PlacementGuidePathPreview";
        private const string PlacementGuidePreviewLineNamePrefix = "__GuidePathLine_";
        private const float PlacementGuidePreviewLineWidth = 0.005f;
        private const float PlacementGuidePreviewLineAlpha = 0.45f;
        private const bool EnablePlacementGuidePreviewLogs = false;

        private static readonly int s_PlacementGuidePreviewSurfaceShaderId = Shader.PropertyToID("_Surface");
        private static readonly int s_PlacementGuidePreviewSurfaceTypeShaderId = Shader.PropertyToID("_SurfaceType");
        private static readonly int s_PlacementGuidePreviewBlendShaderId = Shader.PropertyToID("_Blend");
        private static readonly int s_PlacementGuidePreviewBaseColorShaderId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_PlacementGuidePreviewColorShaderId = Shader.PropertyToID("_Color");
        private static readonly int s_PlacementGuidePreviewSrcBlendShaderId = Shader.PropertyToID("_SrcBlend");
        private static readonly int s_PlacementGuidePreviewDstBlendShaderId = Shader.PropertyToID("_DstBlend");
        private static readonly int s_PlacementGuidePreviewZWriteShaderId = Shader.PropertyToID("_ZWrite");
        private static readonly int s_PlacementGuidePreviewAlphaClipShaderId = Shader.PropertyToID("_AlphaClip");
        private static readonly int s_PlacementGuidePreviewModeShaderId = Shader.PropertyToID("_Mode");
        private static Material s_PlacementGuidePreviewMaterial;
        private static readonly List<InteractionModule> s_PlacementGuidePreviewSortedModulesCache = new List<InteractionModule>();
        private static bool s_PlacementGuidePreviewSortedModulesCacheValid;
        private static bool s_PlacementGuidePreviewRefreshForced = true;
        private static int s_PlacementGuidePreviewRefreshFrame = -1;
        private const float PlacementGuidePreviewPositionEpsilonSqr = 0.000001f;

        private Transform _placementGuidePreviewRoot;
        private readonly List<LineRenderer> _placementGuidePreviewLines = new List<LineRenderer>();
        private int _placementGuidePreviewRouteSignature;
        private bool _placementGuidePreviewRouteSignatureValid;
        private bool _placementGuidePreviewRouteChangedThisFrame;
        private bool _placementGuidePreviewCachedEffectiveStartValid;
        private Vector3 _placementGuidePreviewCachedEffectiveStart;
        private bool _placementGuidePreviewCachedOutgoingChainValid;
        private Vector3 _placementGuidePreviewCachedOutgoingChain;
        private bool _placementGuidePreviewCachedHasVisibleSegments;

        private static void LogPlacementGuidePreviewStatic(string message, Object context = null)
        {
            if (!EnablePlacementGuidePreviewLogs)
                return;

            Debug.Log($"[GuidePreview] {message}", context);
        }

        private void LogPlacementGuidePreview(string message)
        {
            if (!EnablePlacementGuidePreviewLogs)
                return;

            Debug.Log($"[GuidePreview] module={GetModuleDebugName()} {message}", this);
        }

        private static string DescribePlacementGuidePreviewPosition(Vector3 position)
        {
            return $"({position.x:F3}, {position.y:F3}, {position.z:F3})";
        }

        private static string DescribePlacementGuidePreviewTransform(Transform target)
        {
            if (target == null)
                return "<null>";

            return $"{target.name} pos={DescribePlacementGuidePreviewPosition(target.position)}";
        }

        private void EnsurePlacementGuidePathPreview()
        {
            RefreshPlacementGuidePathPreviewsOncePerFrame();
        }

        public static void NotifyPlacementGuidePreviewExhibitMoved(Transform exhibitRoot)
        {
            if (!IsPlacementGuidePreviewRuntimeEnabled())
            {
                LogPlacementGuidePreviewStatic(
                    $"skip moved refresh: runtimeDisabled exhibit={DescribePlacementGuidePreviewTransform(exhibitRoot)}");
                return;
            }

            if (TryRunPendingPlacementGuidePreviewFrameRefresh(
                    "moved refresh preflight",
                    exhibitRoot))
            {
                return;
            }

            var sortedModules = GetPlacementGuidePreviewSortedModules();
            if (!TryGetPlacementGuidePreviewStartIndexForExhibit(sortedModules, exhibitRoot, out int startIndex))
            {
                LogPlacementGuidePreviewStatic(
                    $"moved refresh fallback to full refresh: exhibit={DescribePlacementGuidePreviewTransform(exhibitRoot)}");
                ForceRefreshPlacementGuidePathPreviews();
                return;
            }

            var startModule = sortedModules[startIndex];
            int refreshStartIndex = Mathf.Max(0, startIndex - 1);
            var refreshStartModule = sortedModules[refreshStartIndex];
            if (startModule != null)
            {
                startModule._placementGuidePreviewRouteSignatureValid = false;
                startModule._placementGuidePreviewRouteChangedThisFrame = true;
            }
            LogPlacementGuidePreviewStatic(
                $"moved refresh: exhibit={DescribePlacementGuidePreviewTransform(exhibitRoot)}, startIndex={startIndex}, startModule={(startModule != null ? startModule.GetModuleDebugName() : "<null>")}, refreshStartIndex={refreshStartIndex}, refreshStartModule={(refreshStartModule != null ? refreshStartModule.GetModuleDebugName() : "<null>")}, sortedModuleCount={sortedModules.Count}",
                startModule);
            RefreshPlacementGuidePathPreviewsFromIndex(
                sortedModules,
                refreshStartIndex,
                startIndex,
                allowEarlyOut: true);
        }

        public static void NotifyPlacementGuidePreviewExhibitPlaced(Transform exhibitRoot)
        {
            if (!IsPlacementGuidePreviewRuntimeEnabled())
            {
                LogPlacementGuidePreviewStatic(
                    $"skip placed refresh: runtimeDisabled exhibit={DescribePlacementGuidePreviewTransform(exhibitRoot)}");
                return;
            }

            if (TryRunPendingPlacementGuidePreviewFrameRefresh(
                    "placed refresh preflight",
                    exhibitRoot))
            {
                return;
            }

            var sortedModules = GetPlacementGuidePreviewSortedModules();
            if (!TryGetPlacementGuidePreviewStartIndexForExhibit(sortedModules, exhibitRoot, out int startIndex))
            {
                LogPlacementGuidePreviewStatic(
                    $"placed refresh fallback to full refresh: exhibit={DescribePlacementGuidePreviewTransform(exhibitRoot)}");
                ForceRefreshPlacementGuidePathPreviews();
                return;
            }

            var startModule = sortedModules[startIndex];
            int refreshStartIndex = Mathf.Max(0, startIndex - 1);
            var refreshStartModule = sortedModules[refreshStartIndex];
            if (startModule != null)
            {
                startModule._placementGuidePreviewRouteSignatureValid = false;
                startModule._placementGuidePreviewRouteChangedThisFrame = true;
            }
            LogPlacementGuidePreviewStatic(
                $"placed refresh: exhibit={DescribePlacementGuidePreviewTransform(exhibitRoot)}, startIndex={startIndex}, startModule={(startModule != null ? startModule.GetModuleDebugName() : "<null>")}, refreshStartIndex={refreshStartIndex}, refreshStartModule={(refreshStartModule != null ? refreshStartModule.GetModuleDebugName() : "<null>")}, sortedModuleCount={sortedModules.Count}",
                startModule);
            RefreshPlacementGuidePathPreviewsFromIndex(
                sortedModules,
                refreshStartIndex,
                startIndex,
                allowEarlyOut: true);
        }

        public static void ForceRefreshPlacementGuidePathPreviews()
        {
            MarkPlacementGuidePathPreviewDirty(invalidateSortedModulesCache: true);
            if (!IsPlacementGuidePreviewRuntimeEnabled())
            {
                LogPlacementGuidePreviewStatic("skip full refresh: runtimeDisabled");
                return;
            }

            var sortedModules = GetPlacementGuidePreviewSortedModules();
            for (int i = 0; i < sortedModules.Count; i++)
            {
                var module = sortedModules[i];
                if (module == null)
                    continue;

                module._placementGuidePreviewRouteSignatureValid = false;
                module._placementGuidePreviewRouteChangedThisFrame = true;
            }

            LogPlacementGuidePreviewStatic($"force full refresh: moduleCount={sortedModules.Count}");
            RefreshPlacementGuidePathPreviewsFromIndex(
                sortedModules,
                0,
                sortedModules.Count - 1,
                allowEarlyOut: false);
            RefreshPlacementGuidePreviewVisibilityForActiveModules(sortedModules);
        }

        private static bool TryRunPendingPlacementGuidePreviewFrameRefresh(
            string reason,
            Transform exhibitRoot)
        {
            if (!s_PlacementGuidePreviewRefreshForced &&
                s_PlacementGuidePreviewSortedModulesCacheValid)
            {
                return false;
            }

            LogPlacementGuidePreviewStatic(
                $"{reason}: exhibit={DescribePlacementGuidePreviewTransform(exhibitRoot)}, refreshForced={s_PlacementGuidePreviewRefreshForced}, sortedModulesCacheValid={s_PlacementGuidePreviewSortedModulesCacheValid}");
            RefreshPlacementGuidePathPreviewsOncePerFrame();
            return true;
        }

        private static void RefreshPlacementGuidePathPreviewsOncePerFrame()
        {
            if (!IsPlacementGuidePreviewRuntimeEnabled())
                return;

            if (s_PlacementGuidePreviewRefreshFrame == Time.frameCount)
                return;

            s_PlacementGuidePreviewRefreshFrame = Time.frameCount;

            var sortedModules = GetPlacementGuidePreviewSortedModules();
            if (s_PlacementGuidePreviewRefreshForced)
            {
                s_PlacementGuidePreviewRefreshForced = false;
                LogPlacementGuidePreviewStatic(
                    $"frame refresh forced: frame={Time.frameCount}, moduleCount={sortedModules.Count}");
                RefreshPlacementGuidePathPreviewsFromIndex(
                    sortedModules,
                    0,
                    sortedModules.Count - 1,
                    allowEarlyOut: false);
                return;
            }

            int firstChangedIndex = -1;
            int lastChangedIndex = -1;

            for (int i = 0; i < sortedModules.Count; i++)
            {
                var module = sortedModules[i];
                if (module == null || !module._isPlacementPreviewActive)
                    continue;

                int routeSignature = module.ComputePlacementGuidePreviewRouteSignature();
                if (!module._placementGuidePreviewRouteSignatureValid || module._placementGuidePreviewRouteSignature != routeSignature)
                {
                    module._placementGuidePreviewRouteSignature = routeSignature;
                    module._placementGuidePreviewRouteSignatureValid = true;
                    module._placementGuidePreviewRouteChangedThisFrame = true;
                    if (firstChangedIndex < 0)
                        firstChangedIndex = i;
                    lastChangedIndex = i;
                }
            }

            if (firstChangedIndex < 0)
                return;

            LogPlacementGuidePreviewStatic(
                $"frame refresh changed-range: frame={Time.frameCount}, firstChangedIndex={firstChangedIndex}, lastChangedIndex={lastChangedIndex}, moduleCount={sortedModules.Count}");
            RefreshPlacementGuidePathPreviewsFromIndex(
                sortedModules,
                firstChangedIndex,
                lastChangedIndex,
                allowEarlyOut: true);
        }

        private static void RefreshPlacementGuidePathPreviewsForModuleId(string moduleId, bool forceVisualRefresh)
        {
            if (!IsPlacementGuidePreviewRuntimeEnabled())
                return;

            moduleId = NormalizePlacementGuidePreviewModuleId(moduleId);
            if (string.IsNullOrEmpty(moduleId))
            {
                LogPlacementGuidePreviewStatic("skip module refresh: moduleId empty");
                return;
            }

            var sortedModules = GetPlacementGuidePreviewSortedModules();
            if (!TryGetPlacementGuidePreviewStartIndex(sortedModules, moduleId, out int startIndex))
            {
                LogPlacementGuidePreviewStatic($"skip module refresh: moduleId={moduleId} not found in sorted modules");
                return;
            }

            var module = sortedModules[startIndex];
            if (module == null)
            {
                LogPlacementGuidePreviewStatic($"skip module refresh: moduleId={moduleId} resolved null module at index={startIndex}");
                return;
            }

            int refreshStartIndex = Mathf.Max(0, startIndex - 1);
            var refreshStartModule = sortedModules[refreshStartIndex];
            module._placementGuidePreviewRouteSignatureValid = false;
            module._placementGuidePreviewRouteChangedThisFrame = forceVisualRefresh;
            module.LogPlacementGuidePreview(
                $"module refresh requested: startIndex={startIndex}, refreshStartIndex={refreshStartIndex}, refreshStartModule={(refreshStartModule != null ? refreshStartModule.GetModuleDebugName() : "<null>")}, forceVisualRefresh={forceVisualRefresh}");
            RefreshPlacementGuidePathPreviewsFromIndex(
                sortedModules,
                refreshStartIndex,
                startIndex,
                allowEarlyOut: true);
        }

        private static void RefreshPlacementGuidePreviewVisibilityForActiveModules(List<InteractionModule> sortedModules)
        {
            if (sortedModules == null)
                return;

            for (int i = 0; i < sortedModules.Count; i++)
            {
                var module = sortedModules[i];
                if (module == null || !module._isPlacementPreviewActive || module._placementGuidePreviewRoot == null)
                    continue;

                var targets = module.CollectPlacementPreviewTargets();
                module.ApplyPlacementPreviewVisibilityMask(targets);
                module.ForcePlacementPreviewTargetVisible(module._placementGuidePreviewRoot.gameObject);
                module.LogPlacementGuidePreview(
                    $"visibility refreshed for route root: targetCount={targets.Count}, root={module._placementGuidePreviewRoot.name}");
            }
        }

        private static bool IsPlacementGuidePreviewRuntimeEnabled()
        {
            var spawner = FindFirstObjectByType<ExhibitPlacementManager>(FindObjectsInactive.Include);
            return spawner != null && spawner.CurrentAppMode == ExhibitAppMode.Placement;
        }

        private static void RefreshPlacementGuidePathPreviewsFromIndex(
            List<InteractionModule> sortedModules,
            int startIndex,
            int lastChangedIndex,
            bool allowEarlyOut)
        {
            if (sortedModules == null || sortedModules.Count == 0)
                return;

            startIndex = Mathf.Clamp(startIndex, 0, sortedModules.Count - 1);
            lastChangedIndex = Mathf.Clamp(lastChangedIndex, startIndex, sortedModules.Count - 1);

            bool hasChainStart = false;
            Vector3 chainStart = Vector3.zero;
            if (startIndex > 0)
            {
                var previousModule = sortedModules[startIndex - 1];
                if (previousModule != null && previousModule._placementGuidePreviewCachedOutgoingChainValid)
                {
                    hasChainStart = true;
                    chainStart = previousModule._placementGuidePreviewCachedOutgoingChain;
                }
            }

            LogPlacementGuidePreviewStatic(
                $"refresh range: startIndex={startIndex}, lastChangedIndex={lastChangedIndex}, moduleCount={sortedModules.Count}, allowEarlyOut={allowEarlyOut}, hasInitialChain={hasChainStart}, initialChain={(hasChainStart ? DescribePlacementGuidePreviewPosition(chainStart) : "<none>")}");

            for (int i = startIndex; i < sortedModules.Count; i++)
            {
                var module = sortedModules[i];
                if (module == null)
                    continue;

                bool forceVisualRefresh = module._placementGuidePreviewRouteChangedThisFrame;
                module.RefreshPlacementGuidePathPreviewForCurrentState(
                    hasChainStart,
                    chainStart,
                    forceVisualRefresh,
                    out bool hasOutgoingChain,
                    out Vector3 outgoingChain,
                    out bool outgoingStateChanged);

                module._placementGuidePreviewRouteChangedThisFrame = false;
                hasChainStart = hasOutgoingChain;
                chainStart = outgoingChain;

                if (allowEarlyOut && i >= lastChangedIndex && !outgoingStateChanged)
                {
                    module.LogPlacementGuidePreview(
                        $"refresh early-out at index={i}, lastChangedIndex={lastChangedIndex}, outgoingStateChanged={outgoingStateChanged}");
                    break;
                }
            }
        }

        private static void MarkPlacementGuidePathPreviewDirty(bool invalidateSortedModulesCache)
        {
            s_PlacementGuidePreviewRefreshForced = true;
            s_PlacementGuidePreviewRefreshFrame = -1;
            if (invalidateSortedModulesCache)
                s_PlacementGuidePreviewSortedModulesCacheValid = false;
        }

        private void ResetPlacementGuidePathPreviewTracking()
        {
            _placementGuidePreviewRouteSignature = 0;
            _placementGuidePreviewRouteSignatureValid = false;
            _placementGuidePreviewRouteChangedThisFrame = false;
            _placementGuidePreviewCachedEffectiveStartValid = false;
            _placementGuidePreviewCachedEffectiveStart = Vector3.zero;
            _placementGuidePreviewCachedOutgoingChainValid = false;
            _placementGuidePreviewCachedOutgoingChain = Vector3.zero;
            _placementGuidePreviewCachedHasVisibleSegments = false;
        }

        private void RefreshPlacementGuidePathPreviewForCurrentState(
            bool hasIncomingChain,
            Vector3 incomingChain,
            bool forceVisualRefresh,
            out bool hasOutgoingChain,
            out Vector3 outgoingChain,
            out bool outgoingStateChanged)
        {
            bool hasEffectiveStart = hasIncomingChain;
            Vector3 effectiveStart = incomingChain;
            List<List<Vector3>> segments = null;
            bool hasVisibleSegments = false;

            if (_isPlacementPreviewActive)
            {
                bool hadOwnStart = TryResolvePlacementGuidePreviewStartPosition(out var ownStart);
                if (!hasEffectiveStart && hadOwnStart)
                {
                    effectiveStart = ownStart;
                    hasEffectiveStart = true;
                }

                if (hasEffectiveStart)
                {
                    segments = BuildPlacementGuidePreviewSegments(effectiveStart);
                    hasVisibleSegments = segments != null && segments.Count > 0;
                }

                LogPlacementGuidePreview(
                    $"refresh current state: hasIncomingChain={hasIncomingChain}, incomingChain={DescribePlacementGuidePreviewPosition(incomingChain)}, hadOwnStart={hadOwnStart}, ownStart={(hadOwnStart ? DescribePlacementGuidePreviewPosition(ownStart) : "<none>")}, hasEffectiveStart={hasEffectiveStart}, effectiveStart={(hasEffectiveStart ? DescribePlacementGuidePreviewPosition(effectiveStart) : "<none>")}, segmentCount={(segments != null ? segments.Count : 0)}, forceVisualRefresh={forceVisualRefresh}");
            }
            else if (_placementGuidePreviewRoot != null)
            {
                LogPlacementGuidePreview("preview inactive during refresh, destroy existing route root");
                DestroyPlacementGuidePathPreview();
            }

            hasOutgoingChain = hasEffectiveStart;
            outgoingChain = effectiveStart;
            if (_isPlacementPreviewActive && hasEffectiveStart && TryResolvePlacementGuidePreviewEndPosition(effectiveStart, out var moduleEnd))
                outgoingChain = moduleEnd;

            bool previewStateChanged = HasPlacementGuidePreviewVisualStateChanged(
                hasEffectiveStart,
                effectiveStart,
                hasVisibleSegments);
            outgoingStateChanged = HasPlacementGuidePreviewOutgoingStateChanged(
                hasOutgoingChain,
                outgoingChain);

            if (forceVisualRefresh || previewStateChanged)
                ApplyPlacementGuidePathPreviewSegments(hasVisibleSegments ? segments : null);

            LogPlacementGuidePreview(
                $"refresh result: previewStateChanged={previewStateChanged}, outgoingStateChanged={outgoingStateChanged}, hasOutgoingChain={hasOutgoingChain}, outgoingChain={(hasOutgoingChain ? DescribePlacementGuidePreviewPosition(outgoingChain) : "<none>")}, hasVisibleSegments={hasVisibleSegments}");

            _placementGuidePreviewCachedEffectiveStartValid = hasEffectiveStart;
            _placementGuidePreviewCachedEffectiveStart = effectiveStart;
            _placementGuidePreviewCachedOutgoingChainValid = hasOutgoingChain;
            _placementGuidePreviewCachedOutgoingChain = outgoingChain;
            _placementGuidePreviewCachedHasVisibleSegments = hasVisibleSegments;
        }

        private bool HasPlacementGuidePreviewVisualStateChanged(
            bool hasEffectiveStart,
            Vector3 effectiveStart,
            bool hasVisibleSegments)
        {
            if (_placementGuidePreviewCachedHasVisibleSegments != hasVisibleSegments)
                return true;
            if (_placementGuidePreviewCachedEffectiveStartValid != hasEffectiveStart)
                return true;
            if (hasEffectiveStart &&
                !ArePlacementGuidePreviewPositionsEqual(_placementGuidePreviewCachedEffectiveStart, effectiveStart))
            {
                return true;
            }

            return false;
        }

        private bool HasPlacementGuidePreviewOutgoingStateChanged(bool hasOutgoingChain, Vector3 outgoingChain)
        {
            if (_placementGuidePreviewCachedOutgoingChainValid != hasOutgoingChain)
                return true;
            if (hasOutgoingChain &&
                !ArePlacementGuidePreviewPositionsEqual(_placementGuidePreviewCachedOutgoingChain, outgoingChain))
            {
                return true;
            }

            return false;
        }

        private static bool ArePlacementGuidePreviewPositionsEqual(Vector3 a, Vector3 b)
        {
            return (a - b).sqrMagnitude <= PlacementGuidePreviewPositionEpsilonSqr;
        }

        private void ApplyPlacementGuidePathPreviewSegments(List<List<Vector3>> segments)
        {
            if (!_isPlacementPreviewActive)
            {
                LogPlacementGuidePreview("apply segments skipped: preview inactive, destroy route root");
                DestroyPlacementGuidePathPreview();
                return;
            }

            if (segments == null || segments.Count == 0)
            {
                LogPlacementGuidePreview("apply segments skipped: no visible segments, destroy route root");
                DestroyPlacementGuidePathPreview();
                return;
            }

            var root = GetOrCreatePlacementGuidePreviewRoot();
            root.gameObject.SetActive(true);
            root.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.localScale = Vector3.one;

            EnsurePlacementGuidePreviewLineCount(segments.Count);

            for (int i = 0; i < _placementGuidePreviewLines.Count; i++)
            {
                var line = _placementGuidePreviewLines[i];
                if (line == null)
                    continue;

                bool shouldShow = i < segments.Count;
                line.gameObject.SetActive(shouldShow);
                if (!shouldShow)
                    continue;

                ConfigurePlacementGuidePreviewLine(line);

                var points = segments[i];
                line.positionCount = points.Count;
                for (int pointIndex = 0; pointIndex < points.Count; pointIndex++)
                    line.SetPosition(pointIndex, points[pointIndex]);
            }

            LogPlacementGuidePreview(
                $"applied route segments: segmentCount={segments.Count}, lineCount={_placementGuidePreviewLines.Count}, pointCounts={DescribePlacementGuidePreviewPointCounts(segments)}");
        }

        private void AddPlacementGuidePreviewTarget(List<GameObject> targets, HashSet<int> seen)
        {
            if (_placementGuidePreviewRoot == null || targets == null || seen == null)
                return;

            int id = _placementGuidePreviewRoot.gameObject.GetInstanceID();
            if (seen.Add(id))
                targets.Add(_placementGuidePreviewRoot.gameObject);
        }

        private void DestroyPlacementGuidePathPreview()
        {
            if (_placementGuidePreviewRoot != null)
            {
                LogPlacementGuidePreview(
                    $"destroy route root: name={_placementGuidePreviewRoot.name}, lineCount={_placementGuidePreviewLines.Count}");
            }

            if (_placementGuidePreviewRoot != null)
            {
                if (Application.isPlaying)
                    Destroy(_placementGuidePreviewRoot.gameObject);
                else
                    DestroyImmediate(_placementGuidePreviewRoot.gameObject);
            }

            _placementGuidePreviewRoot = null;
            _placementGuidePreviewLines.Clear();
        }

        private Transform GetOrCreatePlacementGuidePreviewRoot()
        {
            if (_placementGuidePreviewRoot != null)
                return _placementGuidePreviewRoot;

            var existing = transform.Find(PlacementGuidePreviewRootName);
            if (existing != null)
            {
                _placementGuidePreviewRoot = existing;
                RebuildPlacementGuidePreviewLineCache();
                return _placementGuidePreviewRoot;
            }

            var rootObject = new GameObject(PlacementGuidePreviewRootName)
            {
                hideFlags = HideFlags.DontSave
            };
            rootObject.transform.SetParent(transform, false);
            rootObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            rootObject.transform.localScale = Vector3.one;
            _placementGuidePreviewRoot = rootObject.transform;
            _placementGuidePreviewRoot.SetAsLastSibling();
            LogPlacementGuidePreview($"created route root: name={_placementGuidePreviewRoot.name}");
            return _placementGuidePreviewRoot;
        }

        private void RebuildPlacementGuidePreviewLineCache()
        {
            _placementGuidePreviewLines.Clear();
            if (_placementGuidePreviewRoot == null)
                return;

            var lines = _placementGuidePreviewRoot.GetComponentsInChildren<LineRenderer>(true);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] != null)
                    _placementGuidePreviewLines.Add(lines[i]);
            }
        }

        private void EnsurePlacementGuidePreviewLineCount(int requiredCount)
        {
            if (_placementGuidePreviewRoot == null)
                return;

            if (_placementGuidePreviewLines.Count > requiredCount)
                return;

            for (int i = _placementGuidePreviewLines.Count; i < requiredCount; i++)
            {
                var lineObject = new GameObject($"{PlacementGuidePreviewLineNamePrefix}{i + 1}")
                {
                    hideFlags = HideFlags.DontSave
                };
                lineObject.transform.SetParent(_placementGuidePreviewRoot, false);
                lineObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                lineObject.transform.localScale = Vector3.one;

                var line = lineObject.AddComponent<LineRenderer>();
                ConfigurePlacementGuidePreviewLine(line);
                _placementGuidePreviewLines.Add(line);
            }
        }

        private static void ConfigurePlacementGuidePreviewLine(LineRenderer line)
        {
            if (line == null)
                return;

            Color color = new Color(0.2f, 0.9f, 0.35f, PlacementGuidePreviewLineAlpha);

            line.useWorldSpace = true;
            line.loop = false;
            line.alignment = LineAlignment.View;
            line.widthMultiplier = PlacementGuidePreviewLineWidth;
            line.numCapVertices = 6;
            line.numCornerVertices = 6;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            line.sharedMaterial = GetOrCreatePlacementGuidePreviewMaterial(color);
            line.startColor = color;
            line.endColor = color;
        }

        private static Material GetOrCreatePlacementGuidePreviewMaterial(Color color)
        {
            if (s_PlacementGuidePreviewMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                    shader = Shader.Find("Unlit/Color");
                if (shader == null)
                    shader = Shader.Find("Sprites/Default");
                if (shader == null)
                    shader = Shader.Find("Standard");

                s_PlacementGuidePreviewMaterial = new Material(shader)
                {
                    name = "InteractionModule_PlacementGuidePreview"
                };
            }

            ConfigurePlacementGuidePreviewMaterialTransparency(s_PlacementGuidePreviewMaterial);

            if (s_PlacementGuidePreviewMaterial.HasProperty(s_PlacementGuidePreviewBaseColorShaderId))
                s_PlacementGuidePreviewMaterial.SetColor(s_PlacementGuidePreviewBaseColorShaderId, color);
            if (s_PlacementGuidePreviewMaterial.HasProperty(s_PlacementGuidePreviewColorShaderId))
                s_PlacementGuidePreviewMaterial.SetColor(s_PlacementGuidePreviewColorShaderId, color);

            return s_PlacementGuidePreviewMaterial;
        }

        private static void ConfigurePlacementGuidePreviewMaterialTransparency(Material material)
        {
            if (material == null)
                return;

            if (material.HasProperty(s_PlacementGuidePreviewSurfaceShaderId))
                material.SetFloat(s_PlacementGuidePreviewSurfaceShaderId, 1f);
            if (material.HasProperty(s_PlacementGuidePreviewSurfaceTypeShaderId))
                material.SetFloat(s_PlacementGuidePreviewSurfaceTypeShaderId, 1f);
            if (material.HasProperty(s_PlacementGuidePreviewBlendShaderId))
                material.SetFloat(s_PlacementGuidePreviewBlendShaderId, 0f);
            if (material.HasProperty(s_PlacementGuidePreviewAlphaClipShaderId))
                material.SetFloat(s_PlacementGuidePreviewAlphaClipShaderId, 0f);
            if (material.HasProperty(s_PlacementGuidePreviewModeShaderId))
                material.SetFloat(s_PlacementGuidePreviewModeShaderId, 3f);
            if (material.HasProperty(s_PlacementGuidePreviewSrcBlendShaderId))
                material.SetFloat(s_PlacementGuidePreviewSrcBlendShaderId, (float)BlendMode.SrcAlpha);
            if (material.HasProperty(s_PlacementGuidePreviewDstBlendShaderId))
                material.SetFloat(s_PlacementGuidePreviewDstBlendShaderId, (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty(s_PlacementGuidePreviewZWriteShaderId))
                material.SetFloat(s_PlacementGuidePreviewZWriteShaderId, 0f);

            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
        }

        private List<List<Vector3>> BuildPlacementGuidePreviewSegments()
        {
            var segments = new List<List<Vector3>>();
            if (!TryResolvePlacementGuidePreviewChainStart(out var current))
                return segments;

            return BuildPlacementGuidePreviewSegments(current);
        }

        private List<List<Vector3>> BuildPlacementGuidePreviewSegments(Vector3 current)
        {
            var segments = new List<List<Vector3>>();

            AppendPlacementGuidePreviewSegments(initialEffectEntries, ref current, segments);
            AppendPlacementGuidePreviewSegments(appearEffectEntries, ref current, segments);
            AppendPlacementGuidePreviewSegments(disappearEffectEntries, ref current, segments);
            LogPlacementGuidePreview(
                $"built route segments: segmentCount={segments.Count}, finalEnd={DescribePlacementGuidePreviewPosition(current)}");
            return segments;
        }

        private bool TryResolvePlacementGuidePreviewChainStart(out Vector3 position)
        {
            position = Vector3.zero;
            var sortedModules = GetPlacementGuidePreviewSortedModules();
            bool hasChainStart = false;
            Vector3 chainStart = Vector3.zero;

            for (int i = 0; i < sortedModules.Count; i++)
            {
                var module = sortedModules[i];
                if (module == null)
                    continue;

                bool hasOwnStart = module.TryResolvePlacementGuidePreviewStartPosition(out var ownStart);
                if (ReferenceEquals(module, this))
                {
                    if (hasChainStart)
                    {
                        position = chainStart;
                        LogPlacementGuidePreview(
                            $"resolved chain start from previous module: {DescribePlacementGuidePreviewPosition(position)}");
                        return true;
                    }

                    if (hasOwnStart)
                    {
                        position = ownStart;
                        LogPlacementGuidePreview(
                            $"resolved own start: {DescribePlacementGuidePreviewPosition(position)}");
                        return true;
                    }

                    LogPlacementGuidePreview("failed to resolve chain start: no previous chain and no own start");
                    return false;
                }

                if (!hasOwnStart)
                    continue;

                if (!hasChainStart)
                {
                    chainStart = ownStart;
                    hasChainStart = true;
                }

                if (!module.TryResolvePlacementGuidePreviewEndPosition(chainStart, out var moduleEnd))
                    continue;

                chainStart = moduleEnd;
            }

            bool resolved = TryResolvePlacementGuidePreviewStartPosition(out position);
            if (resolved)
            {
                LogPlacementGuidePreview(
                    $"resolved fallback start: {DescribePlacementGuidePreviewPosition(position)}");
            }
            else
            {
                LogPlacementGuidePreview("failed to resolve fallback start");
            }

            return resolved;
        }

        private void AppendPlacementGuidePreviewSegments(
            List<AppearEffectEntry> entries,
            ref Vector3 current,
            List<List<Vector3>> segments)
        {
            if (entries == null)
                return;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.targetType != EffectTargetType.Guide || !IsGuideActionMove(entry.guideAction))
                    continue;

                var target = ResolveGuideTarget(entry.guideWaypoint);
                if (target == null)
                    continue;

                var points = BuildPlacementGuidePreviewPoints(
                    current,
                    target.position,
                    entry.guideMoveStyle,
                    entry.guidePathRoot,
                    entry.guidePathInterpolation,
                    entry.guideKeepHeight);
                if (points.Count >= 2)
                    segments.Add(points);
                if (points.Count > 0)
                    current = points[points.Count - 1];
            }
        }

        private void AppendPlacementGuidePreviewSegments(
            List<DisappearEffectEntry> entries,
            ref Vector3 current,
            List<List<Vector3>> segments)
        {
            if (entries == null)
                return;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.targetType != EffectTargetType.Guide || !IsGuideActionMove(entry.guideAction))
                    continue;

                var target = ResolveGuideTarget(entry.guideWaypoint);
                if (target == null)
                    continue;

                var points = BuildPlacementGuidePreviewPoints(
                    current,
                    target.position,
                    entry.guideMoveStyle,
                    entry.guidePathRoot,
                    entry.guidePathInterpolation,
                    entry.guideKeepHeight);
                if (points.Count >= 2)
                    segments.Add(points);
                if (points.Count > 0)
                    current = points[points.Count - 1];
            }
        }

        private bool TryResolvePlacementGuidePreviewStartPosition(out Vector3 position)
        {
            if (initialEffectEntries != null)
            {
                for (int i = 0; i < initialEffectEntries.Count; i++)
                {
                    if (TryResolvePlacementGuidePreviewEntryPosition(initialEffectEntries[i], true, out position))
                        return true;
                }
            }

            if (appearEffectEntries != null)
            {
                for (int i = 0; i < appearEffectEntries.Count; i++)
                {
                    if (TryResolvePlacementGuidePreviewEntryPosition(appearEffectEntries[i], true, out position))
                        return true;
                }
            }

            if (disappearEffectEntries != null)
            {
                for (int i = 0; i < disappearEffectEntries.Count; i++)
                {
                    if (TryResolvePlacementGuidePreviewEntryPosition(disappearEffectEntries[i], true, out position))
                        return true;
                }
            }

            if (initialEffectEntries != null)
            {
                for (int i = 0; i < initialEffectEntries.Count; i++)
                {
                    if (TryResolvePlacementGuidePreviewEntryPosition(initialEffectEntries[i], false, out position))
                        return true;
                }
            }

            if (appearEffectEntries != null)
            {
                for (int i = 0; i < appearEffectEntries.Count; i++)
                {
                    if (TryResolvePlacementGuidePreviewEntryPosition(appearEffectEntries[i], false, out position))
                        return true;
                }
            }

            if (disappearEffectEntries != null)
            {
                for (int i = 0; i < disappearEffectEntries.Count; i++)
                {
                    if (TryResolvePlacementGuidePreviewEntryPosition(disappearEffectEntries[i], false, out position))
                        return true;
                }
            }

            position = Vector3.zero;
            return false;
        }

        private bool TryResolvePlacementGuidePreviewEntryPosition(
            AppearEffectEntry entry,
            bool requireRevealAction,
            out Vector3 position)
        {
            if (entry == null || entry.targetType != EffectTargetType.Guide)
            {
                position = Vector3.zero;
                return false;
            }

            bool actionMatched = requireRevealAction
                ? entry.guideAction == GuideListActionType.ShowAndMoveToWaypoint
                : IsGuideActionMove(entry.guideAction);
            if (!actionMatched)
            {
                position = Vector3.zero;
                return false;
            }

            var target = ResolveGuideTarget(entry.guideWaypoint);
            if (target == null)
            {
                position = Vector3.zero;
                return false;
            }

            position = target.position;
            return true;
        }

        private bool TryResolvePlacementGuidePreviewEntryPosition(
            DisappearEffectEntry entry,
            bool requireRevealAction,
            out Vector3 position)
        {
            if (entry == null || entry.targetType != EffectTargetType.Guide)
            {
                position = Vector3.zero;
                return false;
            }

            bool actionMatched = requireRevealAction
                ? entry.guideAction == GuideListActionType.ShowAndMoveToWaypoint
                : IsGuideActionMove(entry.guideAction);
            if (!actionMatched)
            {
                position = Vector3.zero;
                return false;
            }

            var target = ResolveGuideTarget(entry.guideWaypoint);
            if (target == null)
            {
                position = Vector3.zero;
                return false;
            }

            position = target.position;
            return true;
        }

        private static List<Vector3> BuildPlacementGuidePreviewPoints(
            Vector3 start,
            Vector3 targetPosition,
            GuideMoveStyle moveStyle,
            Transform pathRoot,
            GuidePathInterpolation pathInterpolation,
            bool keepHeight)
        {
            float fixedY = start.y;
            Vector3 destination = targetPosition;
            if (keepHeight)
                destination.y = fixedY;

            var points = new List<Vector3>(16) { start };

            switch (moveStyle)
            {
                case GuideMoveStyle.Waypoints:
                {
                    var waypointPoints = new List<Vector3>(16) { start };
                    if (pathRoot != null)
                    {
                        for (int i = 0; i < pathRoot.childCount; i++)
                        {
                            var child = pathRoot.GetChild(i);
                            if (child == null)
                                continue;

                            Vector3 point = child.position;
                            if (keepHeight)
                                point.y = fixedY;
                            AddPlacementGuidePreviewPointIfFar(waypointPoints, point);
                        }
                    }

                    AddPlacementGuidePreviewPointIfFar(waypointPoints, destination);
                    if (pathInterpolation == GuidePathInterpolation.Smooth)
                        return BuildPlacementGuidePreviewCatmullRomPoints(waypointPoints, 10);

                    return waypointPoints;
                }

                default:
                    AddPlacementGuidePreviewPointIfFar(points, destination);
                    return points;
            }
        }

        private static List<Vector3> BuildPlacementGuidePreviewCatmullRomPoints(List<Vector3> controlPoints, int segmentsPerSpan)
        {
            if (controlPoints == null || controlPoints.Count <= 2)
                return controlPoints ?? new List<Vector3>();

            int segments = Mathf.Clamp(segmentsPerSpan, 4, 24);
            var result = new List<Vector3>(controlPoints.Count * segments);
            AddPlacementGuidePreviewPointIfFar(result, controlPoints[0]);

            for (int i = 0; i < controlPoints.Count - 1; i++)
            {
                Vector3 p0 = controlPoints[Mathf.Max(i - 1, 0)];
                Vector3 p1 = controlPoints[i];
                Vector3 p2 = controlPoints[i + 1];
                Vector3 p3 = controlPoints[Mathf.Min(i + 2, controlPoints.Count - 1)];

                for (int segmentIndex = 1; segmentIndex <= segments; segmentIndex++)
                {
                    float t = segmentIndex / (float)segments;
                    Vector3 sample = EvaluatePlacementGuidePreviewCatmullRom(p0, p1, p2, p3, t);
                    AddPlacementGuidePreviewPointIfFar(result, sample);
                }
            }

            return result;
        }

        private static Vector3 EvaluatePlacementGuidePreviewCatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        private static void AddPlacementGuidePreviewPointIfFar(List<Vector3> points, Vector3 point)
        {
            if (points == null || points.Count == 0)
            {
                points?.Add(point);
                return;
            }

            if ((points[points.Count - 1] - point).sqrMagnitude < 0.000001f)
                return;

            points.Add(point);
        }

        private bool TryResolvePlacementGuidePreviewEndPosition(Vector3 start, out Vector3 position)
        {
            position = start;
            if (!HasPlacementGuidePreviewMoveEntries())
                return true;

            AdvancePlacementGuidePreviewPosition(initialEffectEntries, ref position);
            AdvancePlacementGuidePreviewPosition(appearEffectEntries, ref position);
            AdvancePlacementGuidePreviewPosition(disappearEffectEntries, ref position);
            return true;
        }

        private int ComputePlacementGuidePreviewRouteSignature()
        {
            unchecked
            {
                int hash = 17;
                AppendPlacementGuidePreviewRouteSignature(initialEffectEntries, ref hash);
                AppendPlacementGuidePreviewRouteSignature(appearEffectEntries, ref hash);
                AppendPlacementGuidePreviewRouteSignature(disappearEffectEntries, ref hash);
                return hash;
            }
        }

        private void AppendPlacementGuidePreviewRouteSignature(List<AppearEffectEntry> entries, ref int hash)
        {
            if (entries == null)
                return;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.targetType != EffectTargetType.Guide || !IsGuideActionMove(entry.guideAction))
                    continue;

                hash = (hash * 31) + (int)entry.guideAction;
                hash = (hash * 31) + (int)entry.guideMoveStyle;
                hash = (hash * 31) + (int)entry.guidePathInterpolation;
                hash = (hash * 31) + (entry.guideKeepHeight ? 1 : 0);
                AppendPlacementGuidePreviewTransformSignature(entry.guideWaypoint, ref hash);
                AppendPlacementGuidePreviewPathRootSignature(entry.guidePathRoot, ref hash);
            }
        }

        private void AppendPlacementGuidePreviewRouteSignature(List<DisappearEffectEntry> entries, ref int hash)
        {
            if (entries == null)
                return;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.targetType != EffectTargetType.Guide || !IsGuideActionMove(entry.guideAction))
                    continue;

                hash = (hash * 31) + (int)entry.guideAction;
                hash = (hash * 31) + (int)entry.guideMoveStyle;
                hash = (hash * 31) + (int)entry.guidePathInterpolation;
                hash = (hash * 31) + (entry.guideKeepHeight ? 1 : 0);
                AppendPlacementGuidePreviewTransformSignature(entry.guideWaypoint, ref hash);
                AppendPlacementGuidePreviewPathRootSignature(entry.guidePathRoot, ref hash);
            }
        }

        private static void AppendPlacementGuidePreviewTransformSignature(Transform target, ref int hash)
        {
            if (target == null)
            {
                hash *= 31;
                return;
            }

            Vector3 position = target.position;
            hash = (hash * 31) + position.GetHashCode();
        }

        private static void AppendPlacementGuidePreviewPathRootSignature(Transform pathRoot, ref int hash)
        {
            if (pathRoot == null)
            {
                hash *= 31;
                return;
            }

            hash = (hash * 31) + pathRoot.childCount;
            for (int i = 0; i < pathRoot.childCount; i++)
            {
                var child = pathRoot.GetChild(i);
                if (child == null)
                {
                    hash *= 31;
                    continue;
                }

                hash = (hash * 31) + child.position.GetHashCode();
            }
        }

        private bool HasPlacementGuidePreviewMoveEntries()
        {
            if (HasPlacementGuidePreviewMoveEntries(initialEffectEntries))
                return true;
            if (HasPlacementGuidePreviewMoveEntries(appearEffectEntries))
                return true;
            return HasPlacementGuidePreviewMoveEntries(disappearEffectEntries);
        }

        private bool HasPlacementGuidePreviewMoveEntries(List<AppearEffectEntry> entries)
        {
            if (entries == null)
                return false;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry != null && entry.targetType == EffectTargetType.Guide && IsGuideActionMove(entry.guideAction))
                    return true;
            }

            return false;
        }

        private bool HasPlacementGuidePreviewMoveEntries(List<DisappearEffectEntry> entries)
        {
            if (entries == null)
                return false;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry != null && entry.targetType == EffectTargetType.Guide && IsGuideActionMove(entry.guideAction))
                    return true;
            }

            return false;
        }

        private void AdvancePlacementGuidePreviewPosition(List<AppearEffectEntry> entries, ref Vector3 current)
        {
            if (entries == null)
                return;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.targetType != EffectTargetType.Guide || !IsGuideActionMove(entry.guideAction))
                    continue;

                var target = ResolveGuideTarget(entry.guideWaypoint);
                if (target == null)
                    continue;

                var points = BuildPlacementGuidePreviewPoints(
                    current,
                    target.position,
                    entry.guideMoveStyle,
                    entry.guidePathRoot,
                    entry.guidePathInterpolation,
                    entry.guideKeepHeight);
                if (points.Count > 0)
                    current = points[points.Count - 1];
            }
        }

        private void AdvancePlacementGuidePreviewPosition(List<DisappearEffectEntry> entries, ref Vector3 current)
        {
            if (entries == null)
                return;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.targetType != EffectTargetType.Guide || !IsGuideActionMove(entry.guideAction))
                    continue;

                var target = ResolveGuideTarget(entry.guideWaypoint);
                if (target == null)
                    continue;

                var points = BuildPlacementGuidePreviewPoints(
                    current,
                    target.position,
                    entry.guideMoveStyle,
                    entry.guidePathRoot,
                    entry.guidePathInterpolation,
                    entry.guideKeepHeight);
                if (points.Count > 0)
                    current = points[points.Count - 1];
            }
        }

        private string GetPlacementGuidePreviewModuleId()
        {
            var info = GetComponentInParent<ExhibitInfo>(true);
            if (info != null && !string.IsNullOrWhiteSpace(info.moduleId))
                return info.moduleId.Trim();

            return string.Empty;
        }

        private static List<InteractionModule> GetPlacementGuidePreviewSortedModules()
        {
            if (s_PlacementGuidePreviewSortedModulesCacheValid)
                return s_PlacementGuidePreviewSortedModulesCache;

            var modules = FindObjectsByType<InteractionModule>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            s_PlacementGuidePreviewSortedModulesCache.Clear();
            for (int i = 0; i < modules.Length; i++)
            {
                var module = modules[i];
                if (IsPlacementGuidePreviewDrawableModule(module))
                    s_PlacementGuidePreviewSortedModulesCache.Add(module);
            }

            var director = FindFirstObjectByType<ChapterPlacementDirector>(FindObjectsInactive.Include);
            s_PlacementGuidePreviewSortedModulesCache.Sort((a, b) => ComparePlacementGuidePreviewModuleOrder(a, b, director));
            s_PlacementGuidePreviewSortedModulesCacheValid = true;
            return s_PlacementGuidePreviewSortedModulesCache;
        }

        private static bool IsPlacementGuidePreviewDrawableModule(InteractionModule module)
        {
            if (module == null)
                return false;

            var go = module.gameObject;
            if (go == null)
                return false;

            var scene = go.scene;
            return scene.IsValid() && scene.isLoaded;
        }

        private static int ComparePlacementGuidePreviewModuleHierarchyOrder(InteractionModule a, InteractionModule b)
        {
            if (ReferenceEquals(a, b))
                return 0;
            if (a == null)
                return -1;
            if (b == null)
                return 1;

            var aScene = a.gameObject.scene;
            var bScene = b.gameObject.scene;
            int sceneCompare = aScene.handle.CompareTo(bScene.handle);
            if (sceneCompare != 0)
                return sceneCompare;

            return ComparePlacementGuidePreviewTransformOrder(a.transform, b.transform);
        }

        private static int ComparePlacementGuidePreviewModuleOrder(
            InteractionModule a,
            InteractionModule b,
            ChapterPlacementDirector director)
        {
            if (ReferenceEquals(a, b))
                return 0;
            if (a == null)
                return -1;
            if (b == null)
                return 1;

            bool hasAPlanIndex = TryGetPlacementGuidePreviewPlanIndex(director, a, out int aPlanIndex);
            bool hasBPlanIndex = TryGetPlacementGuidePreviewPlanIndex(director, b, out int bPlanIndex);

            if (hasAPlanIndex && hasBPlanIndex)
            {
                int planCompare = aPlanIndex.CompareTo(bPlanIndex);
                if (planCompare != 0)
                    return planCompare;
            }
            else if (hasAPlanIndex != hasBPlanIndex)
            {
                return hasAPlanIndex ? -1 : 1;
            }

            return ComparePlacementGuidePreviewModuleHierarchyOrder(a, b);
        }

        private static bool TryGetPlacementGuidePreviewPlanIndex(
            ChapterPlacementDirector director,
            InteractionModule module,
            out int flatIndex)
        {
            flatIndex = -1;
            if (director == null || module == null)
                return false;

            string moduleId = module.GetPlacementGuidePreviewModuleId();
            if (string.IsNullOrEmpty(moduleId))
                return false;

            return director.TryGetGuidePreviewFlatIndex(moduleId, out flatIndex);
        }

        private static bool TryGetPlacementGuidePreviewStartIndex(
            List<InteractionModule> sortedModules,
            string moduleId,
            out int startIndex)
        {
            startIndex = -1;
            if (sortedModules == null || string.IsNullOrEmpty(moduleId))
                return false;

            for (int i = 0; i < sortedModules.Count; i++)
            {
                var module = sortedModules[i];
                if (module == null)
                    continue;

                if (!string.Equals(module.GetPlacementGuidePreviewModuleId(), moduleId, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                startIndex = i;
                return true;
            }

            return false;
        }

        private static bool TryGetPlacementGuidePreviewStartIndexForExhibit(
            List<InteractionModule> sortedModules,
            Transform exhibitRoot,
            out int startIndex)
        {
            startIndex = -1;
            if (sortedModules == null || sortedModules.Count == 0 || exhibitRoot == null)
                return false;

            var modules = exhibitRoot.GetComponentsInChildren<InteractionModule>(true);
            int bestIndex = int.MaxValue;
            bool found = false;

            for (int i = 0; i < modules.Length; i++)
            {
                var module = modules[i];
                if (module == null)
                    continue;

                string moduleId = NormalizePlacementGuidePreviewModuleId(module.GetPlacementGuidePreviewModuleId());
                if (string.IsNullOrEmpty(moduleId))
                    continue;

                if (!TryGetPlacementGuidePreviewStartIndex(sortedModules, moduleId, out int moduleIndex))
                    continue;

                if (moduleIndex < bestIndex)
                {
                    bestIndex = moduleIndex;
                    found = true;
                }
            }

            if (found)
            {
                startIndex = bestIndex;
                LogPlacementGuidePreviewStatic(
                    $"resolved exhibit start index from child modules: exhibit={DescribePlacementGuidePreviewTransform(exhibitRoot)}, startIndex={startIndex}, moduleCount={modules.Length}");
                return true;
            }

            if (!TryGetPlacementGuidePreviewModuleId(exhibitRoot, out string exhibitModuleId))
            {
                LogPlacementGuidePreviewStatic(
                    $"failed to resolve exhibit moduleId: exhibit={DescribePlacementGuidePreviewTransform(exhibitRoot)}");
                return false;
            }

            bool resolvedByRootId = TryGetPlacementGuidePreviewStartIndex(sortedModules, exhibitModuleId, out startIndex);
            LogPlacementGuidePreviewStatic(
                $"resolved exhibit start index from root moduleId: exhibit={DescribePlacementGuidePreviewTransform(exhibitRoot)}, moduleId={exhibitModuleId}, startIndex={startIndex}, resolved={resolvedByRootId}");
            return resolvedByRootId;
        }

        private static string DescribePlacementGuidePreviewPointCounts(List<List<Vector3>> segments)
        {
            if (segments == null || segments.Count == 0)
                return "[]";

            var counts = new System.Text.StringBuilder();
            counts.Append('[');
            for (int i = 0; i < segments.Count; i++)
            {
                if (i > 0)
                    counts.Append(',');

                counts.Append(segments[i] != null ? segments[i].Count : 0);
            }

            counts.Append(']');
            return counts.ToString();
        }

        private static bool TryGetPlacementGuidePreviewModuleId(Transform exhibitRoot, out string moduleId)
        {
            moduleId = string.Empty;
            if (exhibitRoot == null)
                return false;

            var info = exhibitRoot.GetComponentInParent<ExhibitInfo>(true);
            if (info == null)
                return false;

            moduleId = NormalizePlacementGuidePreviewModuleId(info.moduleId);
            return !string.IsNullOrEmpty(moduleId);
        }

        private static string NormalizePlacementGuidePreviewModuleId(string moduleId)
        {
            return string.IsNullOrWhiteSpace(moduleId) ? string.Empty : moduleId.Trim();
        }

        private static int ComparePlacementGuidePreviewTransformOrder(Transform a, Transform b)
        {
            if (a == b)
                return 0;

            int aDepth = GetPlacementGuidePreviewDepth(a);
            int bDepth = GetPlacementGuidePreviewDepth(b);
            int maxDepth = Mathf.Max(aDepth, bDepth);

            for (int level = 0; level < maxDepth; level++)
            {
                int aIndex = GetPlacementGuidePreviewSiblingIndexAtLevel(a, aDepth, level);
                int bIndex = GetPlacementGuidePreviewSiblingIndexAtLevel(b, bDepth, level);
                int compare = aIndex.CompareTo(bIndex);
                if (compare != 0)
                    return compare;
            }

            return aDepth.CompareTo(bDepth);
        }

        private static int GetPlacementGuidePreviewDepth(Transform transform)
        {
            int depth = 0;
            while (transform != null)
            {
                depth++;
                transform = transform.parent;
            }

            return depth;
        }

        private static int GetPlacementGuidePreviewSiblingIndexAtLevel(Transform transform, int depth, int level)
        {
            int fromLeaf = depth - level - 1;
            while (fromLeaf-- > 0 && transform != null)
                transform = transform.parent;

            return transform == null ? -1 : transform.GetSiblingIndex();
        }
    }
}
