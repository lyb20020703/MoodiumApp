using System;
using System.Collections.Generic;
using Interaction;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace VFXViewer
{
    /// <summary>
    /// 场景内 InteractionModule 注册与事件转发中心。
    /// </summary>
    public class InteractionModuleManager : MonoBehaviour
    {
        private readonly Dictionary<string, InteractionModule> m_ModulesById =
            new Dictionary<string, InteractionModule>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, InteractionStage> m_StagesById =
            new Dictionary<string, InteractionStage>(StringComparer.OrdinalIgnoreCase);

        public event Action<InteractionModule, string> ModuleCompleted;
        public event Action<InteractionModule, string, InteractionPhase, InteractionPhase> ModulePhaseChanged;
        public event Action RegistryChanged;

        private void OnEnable()
        {
            ApplyRuntimeInitialChildVisibility();
            RefreshBindings();
        }

        private void OnDisable()
        {
            UnsubscribeAll();
        }

        public bool TryGetModuleById(string moduleId, out InteractionModule module)
        {
            return m_ModulesById.TryGetValue(NormalizeModuleId(moduleId), out module);
        }

        public bool TryGetStageById(string moduleId, out InteractionStage stage)
        {
            return m_StagesById.TryGetValue(NormalizeModuleId(moduleId), out stage);
        }

        public void RefreshRegistry()
        {
            RefreshBindings();
        }

        private void RefreshBindings()
        {
            var modules = GetModuleCandidates();
            var refreshed = new Dictionary<string, InteractionModule>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < modules.Length; i++)
            {
                var module = modules[i];
                if (module == null) continue;
                if (!TryResolveModuleId(module, out var moduleId)) continue;
                if (string.IsNullOrEmpty(moduleId)) continue;
                if (!refreshed.TryGetValue(moduleId, out var existing))
                {
                    refreshed[moduleId] = module;
                    continue;
                }

                if (ShouldPreferModuleCandidate(existing, module))
                    refreshed[moduleId] = module;
            }

            var stages = GetStageCandidates();
            var refreshedStages = new Dictionary<string, InteractionStage>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < stages.Length; i++)
            {
                var stage = stages[i];
                if (stage == null) continue;
                if (!TryResolveStageId(stage, out var moduleId)) continue;
                if (string.IsNullOrEmpty(moduleId)) continue;
                if (!refreshedStages.TryGetValue(moduleId, out var existing))
                {
                    refreshedStages[moduleId] = stage;
                    continue;
                }

                if (ShouldPreferStageCandidate(existing, stage))
                    refreshedStages[moduleId] = stage;
            }

            bool changed = false;

            foreach (var kv in m_ModulesById)
            {
                if (kv.Value == null) continue;
                if (refreshed.TryGetValue(kv.Key, out var latest) && latest == kv.Value) continue;
                kv.Value.Completed -= OnModuleCompleted;
                kv.Value.PhaseChanged -= OnModulePhaseChanged;
                changed = true;
            }

            foreach (var kv in refreshed)
            {
                if (kv.Value == null) continue;
                if (m_ModulesById.TryGetValue(kv.Key, out var old) && old == kv.Value) continue;
                kv.Value.Completed -= OnModuleCompleted;
                kv.Value.Completed += OnModuleCompleted;
                kv.Value.PhaseChanged -= OnModulePhaseChanged;
                kv.Value.PhaseChanged += OnModulePhaseChanged;
                changed = true;
            }

            m_ModulesById.Clear();
            foreach (var kv in refreshed)
                m_ModulesById[kv.Key] = kv.Value;

            if (!changed)
                changed = HaveStageBindingsChanged(refreshedStages);

            m_StagesById.Clear();
            foreach (var kv in refreshedStages)
                m_StagesById[kv.Key] = kv.Value;

            if (changed)
                RegistryChanged?.Invoke();
        }

        private InteractionModule[] GetModuleCandidates()
        {
            // 测试场景里沿用“Manager 的子节点就是模块”；
            // 主场景运行时若 Manager 是动态创建的空节点，则改为全场景扫描，
            // 以便接管恢复到各个 Anchor 下的模块。
            if (transform.childCount > 0)
                return GetComponentsInChildren<InteractionModule>(true);

            return FindObjectsByType<InteractionModule>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        private InteractionStage[] GetStageCandidates()
        {
            if (transform.childCount > 0)
                return GetComponentsInChildren<InteractionStage>(true);

            return FindObjectsByType<InteractionStage>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        private void OnModuleCompleted(InteractionModule module)
        {
            if (module == null) return;
            if (!TryResolveModuleId(module, out var moduleId)) return;
            ModuleCompleted?.Invoke(module, moduleId);
        }

        private void OnModulePhaseChanged(InteractionModule module, InteractionPhase previous, InteractionPhase current)
        {
            if (module == null) return;
            if (!TryResolveModuleId(module, out var moduleId)) return;
            ModulePhaseChanged?.Invoke(module, moduleId, previous, current);
        }

        private void UnsubscribeAll()
        {
            foreach (var kv in m_ModulesById)
            {
                if (kv.Value != null)
                {
                    kv.Value.Completed -= OnModuleCompleted;
                    kv.Value.PhaseChanged -= OnModulePhaseChanged;
                }
            }
            m_ModulesById.Clear();
            m_StagesById.Clear();
        }

        private void ApplyRuntimeInitialChildVisibility()
        {
            if (!Application.isEditor || !Application.isPlaying)
                return;

            // 由 ChapterPlacementDirector 接管分组显隐时，不在 Manager 里强行只显示首子节点，
            // 否则会导致同组模块只激活第一个。
            if (FindFirstObjectByType<ChapterPlacementDirector>(FindObjectsInactive.Include) != null)
                return;

            int childCount = transform.childCount;
            if (childCount <= 0)
                return;

            bool firstModuleShown = false;
            for (int i = 0; i < childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child == null)
                    continue;

                bool hasStage = child.GetComponentInChildren<InteractionStage>(true) != null;
                bool hasModule = child.GetComponentInChildren<InteractionModule>(true) != null;
                if (hasStage && !hasModule)
                {
                    child.gameObject.SetActive(true);
                    continue;
                }

                if (!hasModule)
                    continue;

                child.gameObject.SetActive(!firstModuleShown);
                firstModuleShown = true;
            }
        }

        private static bool TryResolveModuleId(InteractionModule module, out string moduleId)
        {
            moduleId = module != null ? module.ModuleId : string.Empty;
            return !string.IsNullOrEmpty(moduleId);
        }

        private static bool TryResolveStageId(InteractionStage stage, out string moduleId)
        {
            moduleId = stage != null ? stage.ModuleId : string.Empty;
            return !string.IsNullOrEmpty(moduleId);
        }

        private static string NormalizeModuleId(string moduleId)
        {
            return string.IsNullOrWhiteSpace(moduleId) ? string.Empty : moduleId.Trim();
        }

        private static bool ShouldPreferModuleCandidate(InteractionModule current, InteractionModule candidate)
        {
            return GetModuleCandidateScore(candidate) > GetModuleCandidateScore(current);
        }

        private static int GetModuleCandidateScore(InteractionModule module)
        {
            return InteractionBase.GetDefaultCandidateScore(module);
        }

        private bool HaveStageBindingsChanged(Dictionary<string, InteractionStage> refreshedStages)
        {
            if (m_StagesById.Count != refreshedStages.Count)
                return true;

            foreach (var kv in m_StagesById)
            {
                if (!refreshedStages.TryGetValue(kv.Key, out var latest) || latest != kv.Value)
                    return true;
            }

            return false;
        }

        private static bool ShouldPreferStageCandidate(InteractionStage current, InteractionStage candidate)
        {
            return GetStageCandidateScore(candidate) > GetStageCandidateScore(current);
        }

        private static int GetStageCandidateScore(InteractionStage stage)
        {
            return InteractionBase.GetDefaultCandidateScore(stage);
        }
    }
}
