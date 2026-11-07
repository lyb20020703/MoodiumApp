using System;
using System.Collections.Generic;
using UnityEngine;

namespace Interaction
{
    public partial class InteractionModule
    {
        private void ApplyConfigInternal()
        {
            if (!Enum.IsDefined(typeof(TriggerType), startTriggerType))
                startTriggerType = TriggerType.Time;
            if (!Enum.IsDefined(typeof(TriggerType), endTriggerType))
                endTriggerType = TriggerType.Time;

            if (initialEffectEntries == null) initialEffectEntries = new List<AppearEffectEntry>();
            if (appearEffectEntries == null) appearEffectEntries = new List<AppearEffectEntry>();
            if (disappearEffectEntries == null) disappearEffectEntries = new List<DisappearEffectEntry>();

            int validInitial = 0, validAppear = 0, validDisappear = 0;
            for (int i = 0; i < initialEffectEntries.Count; i++)
            {
                var e = initialEffectEntries[i];
                if (e == null) continue;
                if (e.targetType == EffectTargetType.Guide)
                {
                    if (e.guideAction == GuideListActionType.PlayVoice && e.guideVoiceClip == null)
                    {
                        LogWarning($"初始效果列表第 {i + 1} 条导游动作为“播放音频”但未设置语音片段，已忽略该条。");
                        continue;
                    }
                    if (e.guideAction == GuideListActionType.PlayAnimation &&
                        string.IsNullOrWhiteSpace(e.guideAnimationStateOrTrigger))
                    {
                        LogWarning($"初始效果列表第 {i + 1} 条导游动作为“播放动画”但未填写动画状态或 Trigger，已忽略该条。");
                        continue;
                    }
                    validInitial++;
                    continue;
                }

                if (IsSystemTarget(e.targetType))
                {
                    if (TryValidateSystemTarget(e))
                        validInitial++;
                    continue;
                }

                if (IsStageTarget(e.targetType))
                {
                    if (!HasValidTarget(e)) continue;
                    TryValidateStageTarget(e);
                    validInitial++;
                    continue;
                }

                if (e.contentRoot == null) continue;
                var contentType = ContentHandleFactory.DetectType(e.contentRoot);
                ValidateAppearEntryForContent(contentType, ref e.effectType);
                validInitial++;
            }
            for (int i = 0; i < appearEffectEntries.Count; i++)
            {
                var e = appearEffectEntries[i];
                if (e == null) continue;
                if (e.targetType == EffectTargetType.Guide)
                {
                    if (e.guideAction == GuideListActionType.PlayVoice && e.guideVoiceClip == null)
                    {
                        LogWarning($"开始效果列表第 {i + 1} 条导游动作为“播放音频”但未设置语音片段，已忽略该条。");
                        continue;
                    }
                    if (e.guideAction == GuideListActionType.PlayAnimation &&
                        string.IsNullOrWhiteSpace(e.guideAnimationStateOrTrigger))
                    {
                        LogWarning($"开始效果列表第 {i + 1} 条导游动作为“播放动画”但未填写动画状态或 Trigger，已忽略该条。");
                        continue;
                    }
                    validAppear++;
                    continue;
                }

                if (IsSystemTarget(e.targetType))
                {
                    if (TryValidateSystemTarget(e))
                        validAppear++;
                    continue;
                }

                if (IsStageTarget(e.targetType))
                {
                    if (!HasValidTarget(e)) continue;
                    TryValidateStageTarget(e);
                    validAppear++;
                    continue;
                }

                if (e.contentRoot == null) continue;
                var contentType = ContentHandleFactory.DetectType(e.contentRoot);
                ValidateAppearEntryForContent(contentType, ref e.effectType);
                validAppear++;
            }
            for (int i = 0; i < disappearEffectEntries.Count; i++)
            {
                var e = disappearEffectEntries[i];
                if (e == null) continue;
                if (e.targetType == EffectTargetType.Guide)
                {
                    if (e.guideAction == GuideListActionType.PlayVoice && e.guideVoiceClip == null)
                    {
                        LogWarning($"结束效果列表第 {i + 1} 条导游动作为“播放音频”但未设置语音片段，已忽略该条。");
                        continue;
                    }
                    if (e.guideAction == GuideListActionType.PlayAnimation &&
                        string.IsNullOrWhiteSpace(e.guideAnimationStateOrTrigger))
                    {
                        LogWarning($"结束效果列表第 {i + 1} 条导游动作为“播放动画”但未填写动画状态或 Trigger，已忽略该条。");
                        continue;
                    }
                    validDisappear++;
                    continue;
                }

                if (IsSystemTarget(e.targetType))
                {
                    if (TryValidateSystemTarget(e))
                        validDisappear++;
                    continue;
                }

                if (IsStageTarget(e.targetType))
                {
                    if (!HasValidTarget(e)) continue;
                    TryValidateStageTarget(e);
                    validDisappear++;
                    continue;
                }

                if (e.contentRoot == null) continue;
                var contentType = ContentHandleFactory.DetectType(e.contentRoot);
                ValidateDisappearEntryForContent(contentType, ref e.effectType);
                    validDisappear++;
            }

            LogInteractionFile(
                "InteractionConfig",
                $"applyConfigInternal validInitial={validInitial} validAppear={validAppear} " +
                $"validDisappear={validDisappear}, {DescribeTriggerSummary()}");

            if (validInitial == 0 && validAppear == 0 && validDisappear == 0)
            {
                LogInteractionFile("InteractionConfig", "skip_config reason=no_valid_entries");
                LogWarning("初始/开始/结束效果列表为空或内容根未设置，已跳过配置。可通过 RegisterTrigger API 接线。");
                return;
            }

            var appearTypes = new HashSet<AppearEffectType>();
            var disappearTypes = new HashSet<DisappearEffectType>();
            foreach (var e in initialEffectEntries)
            {
                if (e != null && e.targetType != EffectTargetType.Guide && !IsStageTarget(e.targetType) && !IsSystemTarget(e.targetType) && HasValidTarget(e))
                    appearTypes.Add(e.effectType);
            }
            foreach (var e in appearEffectEntries)
            {
                if (e != null && e.targetType != EffectTargetType.Guide && !IsStageTarget(e.targetType) && !IsSystemTarget(e.targetType) && HasValidTarget(e))
                    appearTypes.Add(e.effectType);
            }
            foreach (var e in disappearEffectEntries)
            {
                if (e != null && e.targetType != EffectTargetType.Guide && !IsStageTarget(e.targetType) && !IsSystemTarget(e.targetType) && HasValidTarget(e))
                    disappearTypes.Add(e.effectType);
            }
            foreach (var t in appearTypes)
            {
                if (GetOrAddAppearEffect(t) == null)
                    LogWarning($"无法创建开始效果组件：{t}，已跳过。");
            }
            foreach (var t in disappearTypes)
            {
                if (GetOrAddDisappearEffect(t) == null)
                    LogWarning($"无法创建结束效果组件：{t}，已跳过。");
            }

            foreach (var e in initialEffectEntries)
            {
                if (e == null || e.targetType == EffectTargetType.Guide || IsStageTarget(e.targetType) || IsSystemTarget(e.targetType))
                    continue;
                if (e.contentRoot == null) continue;
                if (e.initialHidden)
                {
                    if (e.effectType == AppearEffectType.Transparency)
                        SetContentAlpha(e.contentRoot, e.appearInitialTransparency);
                    e.contentRoot.SetActive(false);
                }
            }

            foreach (var e in appearEffectEntries)
            {
                if (e == null || e.targetType == EffectTargetType.Guide || IsStageTarget(e.targetType) || IsSystemTarget(e.targetType))
                    continue;
                if (e.contentRoot == null) continue;
                if (e.initialHidden)
                {
                    if (e.effectType == AppearEffectType.Transparency)
                        SetContentAlpha(e.contentRoot, e.appearInitialTransparency);
                    e.contentRoot.SetActive(false);
                }
            }

            // 触发器注册日志过于频繁，默认不输出
            var addedTriggers = new HashSet<ITrigger>();
            void AddTrigger(TriggerType type, bool isStartSlot, bool isEndSlot)
            {
                ITrigger t = null;
                try { t = GetOrAddTrigger(type); }
                catch (Exception ex)
                {
                    LogError($"GetOrAddTrigger({type}) 异常: {ex.Message}\n{ex.StackTrace}");
                    throw;
                }
                if (t == null || !addedTriggers.Add(t)) return;
                try { ApplyTriggerConfig(t, isStartSlot, isEndSlot); }
                catch (Exception ex)
                {
                    LogError($"ApplyTriggerConfig({type}) 异常: {ex.Message}\n{ex.StackTrace}");
                    throw;
                }
                _triggers.Add(t);
                RegisterTriggerInstance(t);
                t.Triggered -= OnTriggerFired;
                t.Triggered += OnTriggerFired;
                try { t.Enable(); }
                catch (Exception ex)
                {
                    LogError($"Trigger.Enable({type}) 异常: {ex.Message}\n{ex.StackTrace}");
                    throw;
                }
                LogInteractionFile(
                    "InteractionTrigger",
                    $"configure triggerType={type}, component={t.GetType().Name}, " +
                    $"isStartSlot={isStartSlot}, isEndSlot={isEndSlot}, {DescribeTouchInteractableBinding()}");
            }
            AddTrigger(startTriggerType, true, startTriggerType == endTriggerType);
            if (endTriggerType != startTriggerType)
                AddTrigger(endTriggerType, false, true);

            LogInteractionFile(
                "InteractionTrigger",
                $"trigger_registration_complete registered={_triggers.Count}, {DescribeTriggerSummary()}");

            // 触发器注册摘要默认不输出

            if (dragReleaseType != DragReleaseType.None)
            {
                var behavior = GetOrAddDragReleaseBehavior(dragReleaseType);
                if (behavior != null)
                {
                    ApplyDragReleaseConfig(behavior);
                    var seen = new HashSet<int>();
                    foreach (var e in initialEffectEntries)
                    {
                        if (e == null || e.targetType == EffectTargetType.Guide || IsStageTarget(e.targetType) || IsSystemTarget(e.targetType))
                            continue;
                        if (e.contentRoot == null) continue;
                        int id = e.contentRoot.GetInstanceID();
                        if (!seen.Add(id)) continue;
                        try
                        {
                            var handle = GetOrCreateHandle(e.contentRoot);
                            RegisterDragReleaseBehavior(handle, behavior);
                        }
                        catch (Exception ex)
                        {
                            LogWarning($"注册拖拽释放行为失败: {ex.Message}");
                        }
                    }
                    foreach (var e in appearEffectEntries)
                    {
                        if (e == null || e.targetType == EffectTargetType.Guide || IsStageTarget(e.targetType) || IsSystemTarget(e.targetType))
                            continue;
                        if (e.contentRoot == null) continue;
                        int id = e.contentRoot.GetInstanceID();
                        if (!seen.Add(id)) continue;
                        try
                        {
                            var handle = GetOrCreateHandle(e.contentRoot);
                            RegisterDragReleaseBehavior(handle, behavior);
                        }
                        catch (Exception ex)
                        {
                            LogWarning($"注册拖拽释放行为失败: {ex.Message}");
                        }
                    }
                }
            }

            BeginInitialPhaseIfNeeded();
        }
    }
}
