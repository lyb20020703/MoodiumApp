using System.Collections.Generic;
using Interaction;
using UnityEngine;
using VFXViewer;

namespace VFXViewer
{
    [DefaultExecutionOrder(10000)]
    public sealed class GlobalGuideRootBinder : MonoBehaviour
    {
        private static readonly bool EnableGuideRootBindingDiagnostics = false;
        private static GlobalGuideRootBinder s_Instance;

        private sealed class GuideBindingState
        {
            public bool initialized;
            public Transform originalParent;
            public int originalSiblingIndex;
            public int boundRootInstanceId;
        }

        private readonly Dictionary<int, GuideBindingState> m_StateByGuideInstanceId =
            new Dictionary<int, GuideBindingState>();

        private ExhibitPlacementManager m_PlacementManager;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<GlobalGuideRootBinder>(FindObjectsInactive.Include) != null)
                return;

            var binderObject = new GameObject("GlobalGuideRootBinder");
            binderObject.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(binderObject);
            binderObject.AddComponent<GlobalGuideRootBinder>();
        }

        private void Awake()
        {
            s_Instance = this;
        }

        private void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;
        }

        public static void DetachGuidesFromRoot(Transform rootTransform)
        {
            if (rootTransform == null)
                return;

            GlobalGuideRootBinder binder = s_Instance;
            if (binder == null)
                binder = FindFirstObjectByType<GlobalGuideRootBinder>(FindObjectsInactive.Include);
            if (binder == null)
                return;

            binder.DetachGuidesFromRootInternal(rootTransform);
        }

        private void LateUpdate()
        {
            if (m_PlacementManager == null)
                m_PlacementManager = ExhibitPlacementManager.Instance ?? FindFirstObjectByType<ExhibitPlacementManager>(FindObjectsInactive.Include);

            Transform rootTransform = null;
            bool shouldBindGuideToRoot =
                m_PlacementManager != null &&
                m_PlacementManager.CurrentAppMode == ExhibitAppMode.Experience &&
                m_PlacementManager.TryGetRootMarkerTransform(out rootTransform);

            var guideRuntimes = FindObjectsByType<GlobalGuideRuntime>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var seenGuideIds = new HashSet<int>();

            for (int i = 0; i < guideRuntimes.Length; i++)
            {
                GlobalGuideRuntime runtime = guideRuntimes[i];
                if (runtime == null)
                    continue;

                Transform guideTransform = runtime.transform;
                int guideInstanceId = guideTransform.GetInstanceID();
                seenGuideIds.Add(guideInstanceId);

                if (!m_StateByGuideInstanceId.TryGetValue(guideInstanceId, out GuideBindingState state))
                {
                    state = new GuideBindingState();
                    m_StateByGuideInstanceId.Add(guideInstanceId, state);
                }

                if (!shouldBindGuideToRoot && runtime.IsVisible)
                    runtime.SetVisible(false);

                SyncGuideParent(state, runtime, guideTransform, rootTransform);
            }

            RemoveStaleStates(seenGuideIds);
        }

        private void SyncGuideParent(
            GuideBindingState state,
            GlobalGuideRuntime runtime,
            Transform guideTransform,
            Transform rootTransform)
        {
            if (state == null || guideTransform == null)
                return;

            if (!state.initialized)
            {
                state.originalParent = guideTransform.parent;
                state.originalSiblingIndex = guideTransform.GetSiblingIndex();
                state.initialized = true;
            }

            if (rootTransform == null)
            {
                state.boundRootInstanceId = 0;
                RestoreOriginalParent(state, guideTransform);
                return;
            }

            if (guideTransform.parent == rootTransform)
                return;

            bool isNewRootBinding = state.boundRootInstanceId != rootTransform.GetInstanceID();
            Vector3 worldScaleBeforeRebind = guideTransform.lossyScale;
            Vector3 worldPositionBeforeRebind = guideTransform.position;
            Quaternion worldRotationBeforeRebind = guideTransform.rotation;

            guideTransform.SetParent(rootTransform, true);
            PreserveWorldScale(guideTransform, worldScaleBeforeRebind);

            if (isNewRootBinding)
            {
                guideTransform.localPosition = Vector3.zero;
                runtime?.RebaseInitialPoseToCurrentTransform();

                LogGuideBinding(
                    "bind_to_root",
                    runtime,
                    guideTransform,
                    rootTransform,
                    worldPositionBeforeRebind,
                    worldRotationBeforeRebind,
                    worldScaleBeforeRebind,
                    "snap_to_root_on_new_binding");
            }

            state.boundRootInstanceId = rootTransform.GetInstanceID();
        }

        private void DetachGuidesFromRootInternal(Transform rootTransform)
        {
            if (rootTransform == null)
                return;

            var guideRuntimes = FindObjectsByType<GlobalGuideRuntime>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < guideRuntimes.Length; i++)
            {
                GlobalGuideRuntime runtime = guideRuntimes[i];
                if (runtime == null)
                    continue;

                Transform guideTransform = runtime.transform;
                if (guideTransform.parent != rootTransform)
                    continue;

                int guideInstanceId = guideTransform.GetInstanceID();
                if (!m_StateByGuideInstanceId.TryGetValue(guideInstanceId, out GuideBindingState state))
                {
                    state = new GuideBindingState
                    {
                        initialized = true,
                        originalParent = null,
                        originalSiblingIndex = 0
                    };
                    m_StateByGuideInstanceId.Add(guideInstanceId, state);
                }

                RestoreOriginalParent(state, guideTransform);
            }
        }

        private static void RestoreOriginalParent(GuideBindingState state, Transform guideTransform)
        {
            if (guideTransform == null)
                return;

            Transform targetParent = state != null ? state.originalParent : null;
            if (guideTransform.parent == targetParent)
                return;

            Vector3 worldScaleBeforeRestore = guideTransform.lossyScale;
            guideTransform.SetParent(targetParent, true);
            PreserveWorldScale(guideTransform, worldScaleBeforeRestore);

            if (targetParent == null)
                return;

            int maxSiblingIndex = Mathf.Max(0, targetParent.childCount - 1);
            int siblingIndex = Mathf.Clamp(state.originalSiblingIndex, 0, maxSiblingIndex);
            guideTransform.SetSiblingIndex(siblingIndex);
        }

        private static void PreserveWorldScale(Transform target, Vector3 desiredWorldScale)
        {
            if (target == null)
                return;

            Vector3 parentScale = target.parent != null ? target.parent.lossyScale : Vector3.one;
            target.localScale = new Vector3(
                SafeDivide(desiredWorldScale.x, parentScale.x),
                SafeDivide(desiredWorldScale.y, parentScale.y),
                SafeDivide(desiredWorldScale.z, parentScale.z));
        }

        private static float SafeDivide(float numerator, float denominator)
        {
            return Mathf.Abs(denominator) > 0.0001f ? numerator / denominator : numerator;
        }

        private static void LogGuideBinding(
            string reason,
            GlobalGuideRuntime runtime,
            Transform guideTransform,
            Transform rootTransform,
            Vector3 previousWorldPosition,
            Quaternion previousWorldRotation,
            Vector3 previousWorldScale,
            string bindingMode)
        {
            if (!EnableGuideRootBindingDiagnostics || guideTransform == null)
                return;

            string message =
                $"[GuideRootBind] reason={reason} bindingMode={bindingMode} " +
                $"guide={guideTransform.name} parent={GetTransformPath(guideTransform.parent)} root={GetTransformPath(rootTransform)} " +
                $"runtimeVisible={(runtime != null && runtime.IsVisible)} " +
                $"prevWorldPos={FormatVector3(previousWorldPosition)} prevWorldRot={FormatVector3(previousWorldRotation.eulerAngles)} prevWorldScale={FormatVector3(previousWorldScale)} " +
                $"currentWorldPos={FormatVector3(guideTransform.position)} currentWorldRot={FormatVector3(guideTransform.rotation.eulerAngles)} currentWorldScale={FormatVector3(guideTransform.lossyScale)} " +
                $"currentLocalPos={FormatVector3(guideTransform.localPosition)} currentLocalScale={FormatVector3(guideTransform.localScale)}";
            Debug.Log(message, guideTransform);
            PlacementDebugFileLogger.Log(message);
        }

        private static string FormatVector3(Vector3 value)
        {
            return $"({value.x:F3},{value.y:F3},{value.z:F3})";
        }

        private static string GetTransformPath(Transform target)
        {
            if (target == null)
                return "<null>";

            var parts = new List<string>(8);
            Transform current = target;
            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        private void RemoveStaleStates(HashSet<int> seenGuideIds)
        {
            if (seenGuideIds == null)
                return;

            var staleGuideIds = ListPool<int>.Get();
            foreach (var pair in m_StateByGuideInstanceId)
            {
                if (!seenGuideIds.Contains(pair.Key))
                    staleGuideIds.Add(pair.Key);
            }

            for (int i = 0; i < staleGuideIds.Count; i++)
                m_StateByGuideInstanceId.Remove(staleGuideIds[i]);

            ListPool<int>.Release(staleGuideIds);
        }

        private static class ListPool<T>
        {
            private static readonly Stack<List<T>> Pool = new Stack<List<T>>();

            public static List<T> Get()
            {
                return Pool.Count > 0 ? Pool.Pop() : new List<T>();
            }

            public static void Release(List<T> list)
            {
                if (list == null)
                    return;

                list.Clear();
                Pool.Push(list);
            }
        }
    }
}
