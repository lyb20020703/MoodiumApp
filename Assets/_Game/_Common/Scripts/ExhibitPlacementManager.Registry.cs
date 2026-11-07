using System;
using System.Collections.Generic;
using UnityEngine;

namespace VFXViewer
{
    public partial class ExhibitPlacementManager
    {
        private struct ExhibitRegistryEntry
        {
            public ExhibitInfo info;
            public string moduleId;
            public string instanceId;
        }

        private readonly Dictionary<int, ExhibitRegistryEntry> m_ExhibitRegistryByTransformId =
            new Dictionary<int, ExhibitRegistryEntry>();

        private readonly Dictionary<string, HashSet<int>> m_ExhibitTransformIdsByModuleId =
            new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, HashSet<int>> m_ExhibitTransformIdsByInstanceId =
            new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);

        private readonly List<int> m_StaleRegisteredExhibitIds = new List<int>();

        private void SubscribeExhibitRegistry()
        {
            ExhibitInfo.Registered += OnExhibitInfoRegistered;
            ExhibitInfo.Unregistered += OnExhibitInfoUnregistered;
            ExhibitInfo.Changed += OnExhibitInfoChanged;
            RebuildExhibitRegistry();
        }

        private void UnsubscribeExhibitRegistry()
        {
            ExhibitInfo.Registered -= OnExhibitInfoRegistered;
            ExhibitInfo.Unregistered -= OnExhibitInfoUnregistered;
            ExhibitInfo.Changed -= OnExhibitInfoChanged;
            ClearExhibitRegistry();
        }

        private void OnExhibitInfoRegistered(ExhibitInfo info)
        {
            RegisterOrUpdateExhibitInfo(info);
        }

        private void OnExhibitInfoChanged(ExhibitInfo info)
        {
            RegisterOrUpdateExhibitInfo(info);
            MarkExhibitPersistenceDirty(info, markSession: true, markLayout: true);
        }

        private void OnExhibitInfoUnregistered(ExhibitInfo info)
        {
            UnregisterExhibitInfo(info);
        }

        private void RebuildExhibitRegistry()
        {
            ClearExhibitRegistry();

            var exhibits = FindObjectsByType<ExhibitInfo>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < exhibits.Length; i++)
                RegisterOrUpdateExhibitInfo(exhibits[i]);
        }

        private void ClearExhibitRegistry()
        {
            m_ExhibitRegistryByTransformId.Clear();
            m_ExhibitTransformIdsByModuleId.Clear();
            m_ExhibitTransformIdsByInstanceId.Clear();
            m_StaleRegisteredExhibitIds.Clear();
            ClearExhibitPlacementPreviewCaches();
        }

        private void RegisterOrUpdateExhibitInfo(ExhibitInfo info)
        {
            if (info == null || info.transform == null)
                return;

            if (ShouldSkipExhibitRegistry(info))
            {
                RemoveRegisteredExhibitByTransformId(info.transform.GetInstanceID());
                return;
            }

            int transformId = info.transform.GetInstanceID();
            if (m_ExhibitRegistryByTransformId.TryGetValue(transformId, out var existingEntry))
                RemoveExhibitRegistryIndices(transformId, existingEntry.moduleId, existingEntry.instanceId);

            InvalidateExhibitPlacementPreviewCache(transformId);

            string moduleId = NormalizeModuleId(info.moduleId);
            string instanceId = string.IsNullOrWhiteSpace(info.instanceID) ? string.Empty : info.instanceID.Trim();

            m_ExhibitRegistryByTransformId[transformId] = new ExhibitRegistryEntry
            {
                info = info,
                moduleId = moduleId,
                instanceId = instanceId
            };

            AddExhibitRegistryIndex(m_ExhibitTransformIdsByModuleId, moduleId, transformId);
            AddExhibitRegistryIndex(m_ExhibitTransformIdsByInstanceId, instanceId, transformId);
        }

        private void UnregisterExhibitInfo(ExhibitInfo info)
        {
            if (info == null)
                return;

            if (info.transform != null)
            {
                RemoveRegisteredExhibitByTransformId(info.transform.GetInstanceID());
                return;
            }

            foreach (var pair in m_ExhibitRegistryByTransformId)
            {
                if (pair.Value.info != info)
                    continue;

                RemoveRegisteredExhibitByTransformId(pair.Key);
                return;
            }
        }

        private void RemoveRegisteredExhibitHierarchy(Transform root)
        {
            if (root == null)
                return;

            var exhibits = root.GetComponentsInChildren<ExhibitInfo>(true);
            for (int i = 0; i < exhibits.Length; i++)
                UnregisterExhibitInfo(exhibits[i]);
        }

        private void RemoveRegisteredExhibitByTransformId(int transformId)
        {
            if (!m_ExhibitRegistryByTransformId.TryGetValue(transformId, out var entry))
                return;

            RemoveExhibitRegistryIndices(transformId, entry.moduleId, entry.instanceId);
            m_ExhibitRegistryByTransformId.Remove(transformId);
            InvalidateExhibitPlacementPreviewCache(transformId);
        }

        private void CleanupStaleRegisteredExhibits()
        {
            if (m_ExhibitRegistryByTransformId.Count == 0)
                return;

            m_StaleRegisteredExhibitIds.Clear();
            foreach (var pair in m_ExhibitRegistryByTransformId)
            {
                if (pair.Value.info == null || pair.Value.info.transform == null)
                    m_StaleRegisteredExhibitIds.Add(pair.Key);
            }

            for (int i = 0; i < m_StaleRegisteredExhibitIds.Count; i++)
                RemoveRegisteredExhibitByTransformId(m_StaleRegisteredExhibitIds[i]);

            m_StaleRegisteredExhibitIds.Clear();
        }

        private bool TryGetRegisteredExhibitInfo(Transform target, out ExhibitInfo info)
        {
            info = null;
            if (target == null)
                return false;

            CleanupStaleRegisteredExhibits();
            if (m_ExhibitRegistryByTransformId.TryGetValue(target.GetInstanceID(), out var entry) &&
                entry.info != null)
            {
                info = entry.info;
                return true;
            }

            info = target.GetComponent<ExhibitInfo>();
            if (info == null || ShouldSkipExhibitRegistry(info))
                return false;

            RegisterOrUpdateExhibitInfo(info);
            return true;
        }

        private IEnumerable<ExhibitInfo> EnumerateRegisteredExhibits()
        {
            CleanupStaleRegisteredExhibits();
            foreach (var entry in m_ExhibitRegistryByTransformId.Values)
            {
                if (entry.info != null)
                    yield return entry.info;
            }
        }

        private List<ExhibitInfo> CaptureRegisteredExhibitSnapshot()
        {
            CleanupStaleRegisteredExhibits();
            var snapshot = new List<ExhibitInfo>(m_ExhibitRegistryByTransformId.Count);
            foreach (var entry in m_ExhibitRegistryByTransformId.Values)
            {
                if (entry.info != null && !ShouldSkipExhibitRegistry(entry.info))
                    snapshot.Add(entry.info);
            }

            return snapshot;
        }

        private static bool ShouldSkipExhibitRegistry(ExhibitInfo info)
        {
            if (info == null)
                return true;

            if (info.GetComponent<PlacementRootMarker>() != null ||
                info.GetComponentInParent<PlacementRootMarker>() != null)
            {
                return true;
            }

            string moduleId = string.IsNullOrWhiteSpace(info.moduleId) ? string.Empty : info.moduleId.Trim();
            return string.Equals(moduleId, "RootStep", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryGetRegisteredExhibitByInstanceId(string instanceId, out ExhibitInfo info)
        {
            info = null;
            instanceId = string.IsNullOrWhiteSpace(instanceId) ? string.Empty : instanceId.Trim();
            if (string.IsNullOrEmpty(instanceId))
                return false;

            CleanupStaleRegisteredExhibits();
            if (!m_ExhibitTransformIdsByInstanceId.TryGetValue(instanceId, out var transformIds))
                return false;

            foreach (int transformId in transformIds)
            {
                if (!m_ExhibitRegistryByTransformId.TryGetValue(transformId, out var entry) || entry.info == null)
                    continue;

                info = entry.info;
                return true;
            }

            return false;
        }

        private bool TryGetSceneExhibitByInstanceId(string instanceId, out ExhibitInfo info)
        {
            if (TryGetRegisteredExhibitByInstanceId(instanceId, out info))
                return true;

            info = null;
            instanceId = string.IsNullOrWhiteSpace(instanceId) ? string.Empty : instanceId.Trim();
            if (string.IsNullOrEmpty(instanceId))
                return false;

            if (m_MainCamera == null)
                m_MainCamera = Camera.main;

            Transform cam = m_MainCamera != null ? m_MainCamera.transform : null;
            int bestPriority = int.MinValue;
            float bestDistance = float.MaxValue;
            var exhibits = FindObjectsByType<ExhibitInfo>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < exhibits.Length; i++)
            {
                ExhibitInfo candidate = exhibits[i];
                Transform candidateTransform = candidate != null ? candidate.transform : null;
                if (candidateTransform == null)
                    continue;

                string candidateInstanceId = string.IsNullOrWhiteSpace(candidate.instanceID)
                    ? string.Empty
                    : candidate.instanceID.Trim();
                if (!string.Equals(candidateInstanceId, instanceId, StringComparison.Ordinal))
                    continue;

                int priority = GetDeleteTargetPriority(candidate, candidateTransform);
                float distance = cam != null ? Vector3.Distance(cam.position, candidateTransform.position) : 0f;
                if (priority > bestPriority ||
                    (priority == bestPriority && distance < bestDistance))
                {
                    bestPriority = priority;
                    bestDistance = distance;
                    info = candidate;
                }
            }

            return info != null;
        }

        private bool TryGetRegisteredExhibitsByModuleId(string moduleId, List<ExhibitInfo> results)
        {
            if (results == null)
                return false;

            results.Clear();
            moduleId = NormalizeModuleId(moduleId);
            if (string.IsNullOrEmpty(moduleId))
                return false;

            CleanupStaleRegisteredExhibits();
            if (!m_ExhibitTransformIdsByModuleId.TryGetValue(moduleId, out var transformIds))
                return false;

            foreach (int transformId in transformIds)
            {
                if (!m_ExhibitRegistryByTransformId.TryGetValue(transformId, out var entry) || entry.info == null)
                    continue;

                results.Add(entry.info);
            }

            return results.Count > 0;
        }

        private void AppendSceneExhibitCandidatesByModuleId(string moduleId, List<ExhibitInfo> results)
        {
            if (results == null)
                return;

            moduleId = NormalizeModuleId(moduleId);
            if (string.IsNullOrEmpty(moduleId))
                return;

            var existingTransformIds = new HashSet<int>();
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i] != null && results[i].transform != null)
                    existingTransformIds.Add(results[i].transform.GetInstanceID());
            }

            var exhibits = FindObjectsByType<ExhibitInfo>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < exhibits.Length; i++)
            {
                ExhibitInfo candidate = exhibits[i];
                Transform candidateTransform = candidate != null ? candidate.transform : null;
                if (candidateTransform == null)
                    continue;

                if (!string.Equals(NormalizeModuleId(candidate.moduleId), moduleId, StringComparison.OrdinalIgnoreCase))
                    continue;

                int transformId = candidateTransform.GetInstanceID();
                if (!existingTransformIds.Add(transformId))
                    continue;

                results.Add(candidate);
            }
        }

        private bool HasRegisteredSceneExhibits()
        {
            CleanupStaleRegisteredExhibits();
            return m_ExhibitRegistryByTransformId.Count > 0;
        }

        private static void AddExhibitRegistryIndex(
            Dictionary<string, HashSet<int>> index,
            string key,
            int transformId)
        {
            if (string.IsNullOrEmpty(key))
                return;

            if (!index.TryGetValue(key, out var ids))
            {
                ids = new HashSet<int>();
                index.Add(key, ids);
            }

            ids.Add(transformId);
        }

        private static void RemoveExhibitRegistryIndex(
            Dictionary<string, HashSet<int>> index,
            string key,
            int transformId)
        {
            if (string.IsNullOrEmpty(key) || !index.TryGetValue(key, out var ids))
                return;

            ids.Remove(transformId);
            if (ids.Count == 0)
                index.Remove(key);
        }

        private void RemoveExhibitRegistryIndices(int transformId, string moduleId, string instanceId)
        {
            RemoveExhibitRegistryIndex(m_ExhibitTransformIdsByModuleId, moduleId, transformId);
            RemoveExhibitRegistryIndex(m_ExhibitTransformIdsByInstanceId, instanceId, transformId);
        }
    }
}
