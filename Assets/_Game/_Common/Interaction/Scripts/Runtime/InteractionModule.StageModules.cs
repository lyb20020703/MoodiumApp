using System;
using UnityEngine;
using VFXViewer;

namespace Interaction
{
    public partial class InteractionModule
    {
        private static bool IsStageTarget(EffectTargetType targetType)
        {
            return targetType == EffectTargetType.StageModule;
        }

        private static bool IsSystemTarget(EffectTargetType targetType)
        {
            return targetType == EffectTargetType.System;
        }

        private static string NormalizeStageModuleId(string stageModuleId)
        {
            return string.IsNullOrWhiteSpace(stageModuleId) ? string.Empty : stageModuleId.Trim();
        }

        private bool HasValidTarget(AppearEffectEntry entry)
        {
            if (entry == null)
                return false;

            if (entry.targetType == EffectTargetType.Guide)
                return true;

            if (IsSystemTarget(entry.targetType))
                return true;

            if (IsStageTarget(entry.targetType))
                return !string.IsNullOrWhiteSpace(entry.stageModuleId);

            return entry.contentRoot != null;
        }

        private bool HasValidTarget(DisappearEffectEntry entry)
        {
            if (entry == null)
                return false;

            if (entry.targetType == EffectTargetType.Guide)
                return true;

            if (IsSystemTarget(entry.targetType))
                return true;

            if (IsStageTarget(entry.targetType))
                return !string.IsNullOrWhiteSpace(entry.stageModuleId);

            return entry.contentRoot != null;
        }

        private bool TryResolveTargetRoot(AppearEffectEntry entry, out GameObject root)
        {
            root = null;
            if (entry == null)
                return false;

            if (IsSystemTarget(entry.targetType))
                return false;

            if (IsStageTarget(entry.targetType))
                return TryResolveStageEffectRoot(entry.stageModuleId, out root);

            root = entry.contentRoot;
            return root != null;
        }

        private bool TryResolveTargetRoot(DisappearEffectEntry entry, out GameObject root)
        {
            root = null;
            if (entry == null)
                return false;

            if (IsSystemTarget(entry.targetType))
                return false;

            if (IsStageTarget(entry.targetType))
                return TryResolveStageEffectRoot(entry.stageModuleId, out root);

            root = entry.contentRoot;
            return root != null;
        }

        private bool TryResolveStageEffectRoot(string stageModuleId, out GameObject root)
        {
            return TryResolveStageEffectRoot(stageModuleId, out root, attemptRestore: true);
        }

        private bool TryResolveStageEffectRoot(string stageModuleId, out GameObject root, bool attemptRestore)
        {
            root = null;
            if (!TryResolveStage(stageModuleId, out var stage, attemptRestore) || stage == null)
                return false;

            return TryResolveStageEffectRootFromStage(stage, out root);
        }

        private bool TryResolveStage(string stageModuleId, out InteractionStage stage, bool attemptRestore)
        {
            stage = null;
            stageModuleId = NormalizeStageModuleId(stageModuleId);
            if (string.IsNullOrEmpty(stageModuleId))
                return false;

            if (InteractionStage.TryFindByModuleId(stageModuleId, out stage) && stage != null)
                return true;

            ExhibitPlacementManager spawner = ExhibitPlacementManager.Instance;
            if (!attemptRestore || spawner == null || !spawner.TryEnsureSessionPlacementRestoredByModuleId(stageModuleId))
                return false;

            return InteractionStage.TryFindByModuleId(stageModuleId, out stage) && stage != null;
        }

        private bool TryResolveStageEffectRootFromStage(InteractionStage stage, out GameObject root)
        {
            root = null;
            if (stage == null)
                return false;

            if (stage.StageEffectRoot == null)
            {
                LogWarning($"展台未配置 InteractionStage.stageEffectRoot：{stage.ModuleId}");
                return false;
            }

            root = stage.StageEffectRoot;
            return root != null;
        }

        private bool TryValidateStageTarget(AppearEffectEntry entry)
        {
            if (entry == null || !IsStageTarget(entry.targetType) || string.IsNullOrWhiteSpace(entry.stageModuleId))
                return false;

            ValidateStageAction(ref entry.stageActionType);

            if (!TryResolveStage(entry.stageModuleId, out var stage, attemptRestore: false) || stage == null)
                return false;

            if (entry.stageActionType == StageActionType.PlaySdfEffect)
                ValidateStageSdfConfig(stage);
            return true;
        }

        private bool TryValidateSystemTarget(AppearEffectEntry entry)
        {
            if (entry == null || !IsSystemTarget(entry.targetType))
                return false;

            ValidateSystemAction(ref entry.systemActionType, SystemActionType.PlayBgm);
            if (SystemActionNeedsAudioClip(entry.systemActionType) && entry.systemAudioClip == null)
            {
                LogWarning($"系统动作“{GetSystemActionDisplayName(entry.systemActionType)}”缺少音频片段，已忽略该条。");
                return false;
            }

            return true;
        }

        private bool TryValidateStageTarget(DisappearEffectEntry entry)
        {
            if (entry == null || !IsStageTarget(entry.targetType) || string.IsNullOrWhiteSpace(entry.stageModuleId))
                return false;

            ValidateStageAction(ref entry.stageActionType);

            if (!TryResolveStage(entry.stageModuleId, out var stage, attemptRestore: false) || stage == null)
                return false;

            if (entry.stageActionType == StageActionType.PlaySdfEffect)
                ValidateStageSdfConfig(stage);
            return true;
        }

        private bool TryValidateSystemTarget(DisappearEffectEntry entry)
        {
            if (entry == null || !IsSystemTarget(entry.targetType))
                return false;

            ValidateSystemAction(ref entry.systemActionType, SystemActionType.StopBgm);
            if (SystemActionNeedsAudioClip(entry.systemActionType) && entry.systemAudioClip == null)
            {
                LogWarning($"系统动作“{GetSystemActionDisplayName(entry.systemActionType)}”缺少音频片段，已忽略该条。");
                return false;
            }

            return true;
        }

        private static void ValidateStageAction(ref StageActionType stageActionType)
        {
            if (!Enum.IsDefined(typeof(StageActionType), stageActionType))
            {
                Debug.LogWarning($"展台动作 {stageActionType} 无效，已自动替换为 {StageActionType.Show}。");
                stageActionType = StageActionType.Show;
            }
        }

        private static void ValidateSystemAction(ref SystemActionType systemActionType, SystemActionType fallback)
        {
            if (!Enum.IsDefined(typeof(SystemActionType), systemActionType))
            {
                Debug.LogWarning($"系统动作“{systemActionType}”无效，已自动替换为“{GetSystemActionDisplayName(fallback)}”。");
                systemActionType = fallback;
            }
        }

        private static bool SystemActionNeedsAudioClip(SystemActionType actionType)
        {
            return actionType == SystemActionType.PlayBgm || actionType == SystemActionType.PlaySfx;
        }

        private static string GetSystemActionDisplayName(SystemActionType actionType)
        {
            return actionType switch
            {
                SystemActionType.PlayBgm => "播放背景音乐",
                SystemActionType.StopBgm => "停止背景音乐",
                SystemActionType.PlaySfx => "播放音效",
                _ => actionType.ToString()
            };
        }

        private static void ValidateStageSdfConfig(InteractionStage stage)
        {
            if (stage == null)
                return;

            if (stage.HasValidSdfEffectConfig(out var issue))
                return;

            Debug.LogWarning($"展台 {stage.ModuleId} 无法播放 SDF 特效：{issue}");
        }

        private void ExecuteStageAction(StageActionType actionType, string stageModuleId, float duration, Action onComplete)
        {
            if (!TryResolveStage(stageModuleId, out var stage, attemptRestore: true) || stage == null)
            {
                onComplete?.Invoke();
                return;
            }

            switch (actionType)
            {
                case StageActionType.Show:
                    ExecuteStageShow(stage, onComplete);
                    return;
                case StageActionType.Hide:
                    ExecuteStageHide(stage, onComplete);
                    return;
                case StageActionType.PlaySdfEffect:
                    ExecuteStagePlaySdfEffect(stage, duration, null, false, null, false, Vector3.zero, false, Vector3.zero, false, Vector3.one, onComplete);
                    return;
                default:
                    onComplete?.Invoke();
                    return;
            }
        }

        private void ExecuteStageAction(AppearEffectEntry entry, Action onComplete)
        {
            if (entry == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (!TryResolveStage(entry.stageModuleId, out var stage, attemptRestore: true) || stage == null)
            {
                onComplete?.Invoke();
                return;
            }

            switch (entry.stageActionType)
            {
                case StageActionType.Show:
                    ExecuteStageShow(stage, onComplete);
                    return;
                case StageActionType.Hide:
                    ExecuteStageHide(stage, onComplete);
                    return;
                case StageActionType.PlaySdfEffect:
                    ExecuteStagePlaySdfEffect(
                        stage,
                        entry.duration,
                        entry.stageSdfTransformSource,
                        entry.stageSdfFollowTransformSourceDuringPlayback,
                        entry.stageSdfTexture,
                        entry.stageSdfOverrideLocalPosition,
                        entry.stageSdfLocalPosition,
                        entry.stageSdfOverrideLocalRotation,
                        entry.stageSdfLocalEulerAngles,
                        entry.stageSdfOverrideLocalScale,
                        entry.stageSdfLocalScale,
                        onComplete);
                    return;
                default:
                    onComplete?.Invoke();
                    return;
            }
        }

        private void ExecuteStageAction(DisappearEffectEntry entry, Action onComplete)
        {
            if (entry == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (!TryResolveStage(entry.stageModuleId, out var stage, attemptRestore: true) || stage == null)
            {
                onComplete?.Invoke();
                return;
            }

            switch (entry.stageActionType)
            {
                case StageActionType.Show:
                    ExecuteStageShow(stage, onComplete);
                    return;
                case StageActionType.Hide:
                    ExecuteStageHide(stage, onComplete);
                    return;
                case StageActionType.PlaySdfEffect:
                    ExecuteStagePlaySdfEffect(
                        stage,
                        entry.duration,
                        entry.stageSdfTransformSource,
                        entry.stageSdfFollowTransformSourceDuringPlayback,
                        entry.stageSdfTexture,
                        entry.stageSdfOverrideLocalPosition,
                        entry.stageSdfLocalPosition,
                        entry.stageSdfOverrideLocalRotation,
                        entry.stageSdfLocalEulerAngles,
                        entry.stageSdfOverrideLocalScale,
                        entry.stageSdfLocalScale,
                        onComplete);
                    return;
                default:
                    onComplete?.Invoke();
                    return;
            }
        }

        private void ExecuteStageShow(InteractionStage stage, Action onComplete)
        {
            if (stage == null)
            {
                onComplete?.Invoke();
                return;
            }

            stage.TrySetStageContentVisible(true);
            onComplete?.Invoke();
        }

        private void ExecuteStageHide(InteractionStage stage, Action onComplete)
        {
            var root = stage != null ? stage.StageEffectRoot : null;
            if (root == null)
            {
                stage?.StopSdfEffect();
                onComplete?.Invoke();
                return;
            }

            try
            {
                var handle = GetOrCreateHandle(root);
                var hideEffect = GetOrAddDisappearEffect(DisappearEffectType.Hide);
                if (hideEffect != null)
                {
                    hideEffect.Disappear(handle, 0f, () =>
                    {
                        stage?.StopSdfEffect();
                        if (root != null && root.activeSelf)
                            root.SetActive(false);
                        onComplete?.Invoke();
                    });
                    return;
                }
            }
            catch
            {
            }

            stage?.StopSdfEffect();
            if (root.activeSelf)
                root.SetActive(false);
            onComplete?.Invoke();
        }

        private void ExecuteStagePlaySdfEffect(
            InteractionStage stage,
            float duration,
            Transform transformSource,
            bool followTransformSourceDuringPlayback,
            Texture3D sdfTexture,
            bool overrideLocalPosition,
            Vector3 localPosition,
            bool overrideLocalRotation,
            Vector3 localEulerAngles,
            bool overrideLocalScale,
            Vector3 localScale,
            Action onComplete)
        {
            if (stage == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (stage.StageEffectRoot != null && !stage.StageEffectRoot.activeSelf)
                stage.StageEffectRoot.SetActive(true);

            stage.TryPlaySdfEffect(
                duration > 0f ? duration : DefaultDurationWhenZero,
                transformSource,
                followTransformSourceDuringPlayback,
                sdfTexture,
                overrideLocalPosition,
                localPosition,
                overrideLocalRotation,
                localEulerAngles,
                overrideLocalScale,
                localScale,
                onComplete);
        }

        private void ExecuteSystemAction(AppearEffectEntry entry, Action onComplete)
        {
            ExecuteSystemActionCore(
                entry == null ? SystemActionType.PlayBgm : entry.systemActionType,
                entry == null ? null : entry.systemAudioClip,
                entry == null ? 1f : entry.systemAudioVolume,
                entry == null ? 0f : entry.systemFadeSeconds,
                SystemActionType.PlayBgm,
                onComplete);
        }

        private void ExecuteSystemAction(DisappearEffectEntry entry, Action onComplete)
        {
            ExecuteSystemActionCore(
                entry == null ? SystemActionType.StopBgm : entry.systemActionType,
                entry == null ? null : entry.systemAudioClip,
                entry == null ? 1f : entry.systemAudioVolume,
                entry == null ? 0f : entry.systemFadeSeconds,
                SystemActionType.StopBgm,
                onComplete);
        }

        private void ExecuteSystemActionCore(
            SystemActionType actionType,
            AudioClip audioClip,
            float audioVolume,
            float fadeSeconds,
            SystemActionType fallbackAction,
            Action onComplete)
        {
            ValidateSystemAction(ref actionType, fallbackAction);
            if (SystemActionNeedsAudioClip(actionType) && audioClip == null)
            {
                LogWarning($"系统动作“{GetSystemActionDisplayName(actionType)}”缺少音频片段，已跳过。");
                onComplete?.Invoke();
                return;
            }

            var runtime = actionType == SystemActionType.StopBgm
                ? GlobalSystemRuntime.GetExistingOrNull()
                : GlobalSystemRuntime.GetOrCreate();
            if (runtime == null)
            {
                onComplete?.Invoke();
                return;
            }

            runtime.Execute(new SystemActionRequest
            {
                actionType = actionType,
                audioClip = audioClip,
                audioVolume = audioVolume,
                fadeSeconds = fadeSeconds,
                context = this
            });

            onComplete?.Invoke();
        }
    }
}
