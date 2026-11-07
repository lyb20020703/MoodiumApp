using System;
using DG.Tweening;
using UnityEngine;
using VFXViewer;

namespace Interaction
{
    public partial class InteractionModule
    {
        private void OnTriggerFired(ITrigger trigger, bool isStart)
        {
            LogInteractionFile(
                "InteractionTrigger",
                $"trigger_fired source={(trigger != null ? trigger.GetType().Name : "null")}, isStart={isStart}");
            HandleTrigger(isStart);
        }

        private void HandleTrigger(bool isStart)
        {
            bool hasListEntries = (appearEffectEntries != null && appearEffectEntries.Count > 0)
                || (disappearEffectEntries != null && disappearEffectEntries.Count > 0);
            bool useListMode = hasListEntries
                && (_contentHandle == null || _appearEffect == null || _disappearEffect == null);

            LogInteractionFile(
                "InteractionTrigger",
                $"handle_trigger isStart={isStart}, useListMode={useListMode}, hasListEntries={hasListEntries}");

            if (useListMode)
            {
                if (isStart)
                {
                    if (_currentPhase == InteractionPhase.Initial)
                    {
                        _pendingStartWhileInitial = true;
                        LogInteractionFile("InteractionTrigger", "start_trigger_pending_during_initial_phase");
                        return;
                    }
                    OnTriggerFiredListAppear();
                }
                else
                {
                    // 若结束触发器在「出现中」阶段触发，先记录为挂起，等开始效果列表全部完成并进入进行阶段后再真正执行结束效果列表
                    if (_currentPhase == InteractionPhase.Appearing)
                    {
                        // 结束触发器提前触发属于正常流程，默认不输出
                        _pendingEndWhileAppearing = true;
                        LogInteractionFile("InteractionTrigger", "end_trigger_pending_during_appearing_phase");
                    }
                    else
                    {
                        OnTriggerFiredListDisappear();
                    }
                }
                return;
            }

            if (_contentHandle == null || _appearEffect == null || _disappearEffect == null)
            {
                LogInteractionFile("InteractionTrigger", "ignore_trigger reason=missing_content_or_effect");
                return;
            }

            if (isStart)
            {
                if (_currentPhase == InteractionPhase.Initial)
                {
                    _pendingStartWhileInitial = true;
                    LogInteractionFile("InteractionTrigger", "start_trigger_pending_during_initial_phase");
                    return;
                }
                if (_currentPhase != InteractionPhase.Start)
                {
                    LogInteractionFile(
                        "InteractionTrigger",
                        $"ignore_start_trigger reason=phase_not_start current={_currentPhase}");
                    return;
                }
                SetPhase(InteractionPhase.InProgress);
                NotifyEnteredInProgress();
                _appearedContents.Add(_contentHandle);
                if (_contentHandle?.Root != null) _contentHandle.Root.SetActive(true);
                float d = _appearEffect is CommonShowEffect ? 0f : DefaultDurationWhenZero;
                _appearEffect.Appear(_contentHandle, d, () =>
                {
                    if (endTriggerType == TriggerType.Time && startTriggerType != TriggerType.Time)
                    {
                        BeginEndCountdownIfAny();
                    }
                });
            }
            else
            {
                if (_currentPhase != InteractionPhase.InProgress)
                {
                    LogInteractionFile(
                        "InteractionTrigger",
                        $"ignore_end_trigger reason=phase_not_in_progress current={_currentPhase}");
                    return;
                }
                SetPhase(InteractionPhase.End);
                int endVersion = _activeEndPhaseVersion;
                _appearedContents.Remove(_contentHandle);
                float d = _disappearEffect is CommonHideEffect ? 0f : DefaultDurationWhenZero;
                Action onDone = () =>
                {
                    if (_contentHandle?.Root != null) _contentHandle.Root.SetActive(false);
                    TryCompleteEndPhase(endVersion, "single_disappear_completed");
                };
                _disappearEffect.Disappear(_contentHandle, d, onDone);
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Debug/Trigger Start")]
        public void DebugTriggerStart()
        {
            if (!Application.isPlaying) return;
            bool activatedForDebug = !gameObject.activeSelf;
            if (!CanRunEditorDebugTrigger()) return;

            if (activatedForDebug)
            {
                StartCoroutine(DebugTriggerAfterActivation(true));
                return;
            }

            HandleTrigger(true);
        }

        [ContextMenu("Debug/Trigger End")]
        public void DebugTriggerEnd()
        {
            if (!Application.isPlaying) return;
            bool activatedForDebug = !gameObject.activeSelf;
            if (!CanRunEditorDebugTrigger()) return;

            if (activatedForDebug)
            {
                StartCoroutine(DebugTriggerAfterActivation(false));
                return;
            }

            HandleTrigger(false);
        }

        private System.Collections.IEnumerator DebugTriggerAfterActivation(bool isStart)
        {
            // OnEnable/Start 会在下一帧重新应用配置；等配置完成后再触发，
            // 避免初始化阶段清掉刚刚记录的调试请求。
            yield return null;
            yield return null;

            if (this != null && gameObject.activeInHierarchy)
                HandleTrigger(isStart);
        }

        private bool CanRunEditorDebugTrigger()
        {
            var scene = gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning(
                    $"[{nameof(InteractionModule)}] 调试触发仅支持已加载场景中的模块实例，不能直接在 Prefab 资源上执行：{name}",
                    this);
                return false;
            }

            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            if (!gameObject.activeInHierarchy)
            {
                Debug.LogWarning(
                    $"[{nameof(InteractionModule)}] 模块的父级未激活，无法执行调试触发：{name}",
                    this);
                return false;
            }

            return true;
        }
#endif

        private void OnTriggerFiredListAppear()
        {
            if (_currentPhase != InteractionPhase.Start)
            {
                // 非开始阶段的开始触发属于正常防抖，默认不输出
                LogInteractionFile(
                    "InteractionTrigger",
                    $"ignore_list_appear reason=phase_not_start current={_currentPhase}");
                return;
            }
            // 进入出现中阶段默认不输出
            SetPhase(InteractionPhase.Appearing);

            _currentAppearHandles.Clear();
            int validCount = 0;
            foreach (var e in appearEffectEntries)
            {
                if (e == null) continue;
                if (HasValidTarget(e))
                    validCount++;
            }
            LogInteractionFile("InteractionPhase", $"list_appear_started validTargetCount={validCount}");
            if (validCount == 0)
            {
                LogInteractionFile("InteractionPhase", "list_appear_aborted reason=no_valid_targets");
                ResetToStartPhase();
                return;
            }

            foreach (var e in appearEffectEntries)
            {
                if (e == null) continue;
                if (e.targetType == EffectTargetType.Guide) continue;
                if (IsStageTarget(e.targetType)) continue;
                if (IsSystemTarget(e.targetType)) continue;
                if (!TryResolveTargetRoot(e, out var root) || root == null) continue;
                IContentHandle handle;
                try { handle = GetOrCreateHandle(root); }
                catch
                {
                    validCount--;
                    continue;
                }
                if (e.effectType != AppearEffectType.Hide)
                {
                    _currentAppearHandles.Add(handle);
                    _appearedContents.Add(handle);
                }
            }

            if (validCount == 0)
            {
                for (int i = 0; i < _currentAppearHandles.Count; i++)
                    _appearedContents.Remove(_currentAppearHandles[i]);
                _currentAppearHandles.Clear();
                ResetToStartPhase();
                return;
            }

            int pending = validCount;
            Action onAllAppearComplete = () =>
            {
                pending--;
                if (pending > 0) return;
                // 进入进行阶段默认不输出
                SetPhase(InteractionPhase.InProgress);
                NotifyEnteredInProgress();
                LogInteractionFile("InteractionPhase", "list_appear_completed");
                // 结束=时间：结束延时从「开始效果全部完成」这一刻起算
                if (endTriggerType == TriggerType.Time)
                {
                    // 结束计时启动默认不输出
                    BeginEndCountdownIfAny();
                }
                // 若在「出现中」阶段已有挂起的结束触发，则此时立即执行结束效果列表
                if (_pendingEndWhileAppearing)
                {
                    _pendingEndWhileAppearing = false;
                    // 挂起结束触发的恢复默认不输出
                    LogInteractionFile("InteractionTrigger", "resume_pending_end_trigger_after_appear");
                    OnTriggerFiredListDisappear();
                }
            };

            foreach (var e in appearEffectEntries)
            {
                var entry = e;
                if (entry == null) continue;
                IContentHandle handle;
                float entryDelay = entry.delay;

                if (entry.targetType == EffectTargetType.Guide)
                {
                    void RunGuideOne()
                    {
                        ExecuteGuideActionEntry(entry, onAllAppearComplete);
                    }

                    if (entryDelay > 0f)
                        TrackDelayedCall(DOVirtual.DelayedCall(entryDelay, RunGuideOne));
                    else
                        RunGuideOne();
                    continue;
                }

                if (IsSystemTarget(entry.targetType))
                {
                    void RunSystemOne()
                    {
                        ExecuteSystemAction(entry, onAllAppearComplete);
                    }

                    if (entryDelay > 0f)
                        TrackDelayedCall(DOVirtual.DelayedCall(entryDelay, RunSystemOne));
                    else
                        RunSystemOne();
                    continue;
                }

                if (IsStageTarget(entry.targetType))
                {
                    void RunStageOne()
                    {
                        ExecuteStageAction(entry, onAllAppearComplete);
                    }

                    if (entryDelay > 0f)
                        TrackDelayedCall(DOVirtual.DelayedCall(entryDelay, RunStageOne));
                    else
                        RunStageOne();
                    continue;
                }

                if (!TryResolveTargetRoot(entry, out var root) || root == null)
                {
                    onAllAppearComplete();
                    continue;
                }

                try { handle = GetOrCreateHandle(root); }
                catch { onAllAppearComplete(); continue; }
                float entryDuration = entry.duration > 0f ? entry.duration : DefaultDurationWhenZero;
                if (entry.effectType == AppearEffectType.Show || entry.effectType == AppearEffectType.Hide) entryDuration = 0f;
                var effect = GetOrAddAppearEffect(entry.effectType);
                if (effect == null) { onAllAppearComplete(); continue; }

                // 仅 VFX 使用「显示」时用时长表示「播放完成」的等待时间
                bool showNeedsDuration = entry.effectType == AppearEffectType.Show && handle.Type == ContentType.VFX;
                float appearCompleteDelay = showNeedsDuration ? (entry.duration > 0f ? entry.duration : DefaultDurationWhenZero) : 0f;
                Action onOneComplete = appearCompleteDelay > 0f
                    ? () => TrackDelayedCall(DOVirtual.DelayedCall(appearCompleteDelay, () => onAllAppearComplete()))
                    : onAllAppearComplete;

                void RunOne()
                {
                    if (root == null) { onAllAppearComplete(); return; }
                    ApplyAppearConfig(entry, effect);
                    if (handle.Type == ContentType.Video)
                        AVProMediaDiagnostics.EnsureForHierarchy(
                            root,
                            GetModuleDebugName(),
                            $"InteractionModule.RunAppearList.before_set_active effectType={entry.effectType}");
                    if (entry.effectType != AppearEffectType.Hide)
                        root.SetActive(true);
                    // Video 内容：无论使用哪种开始效果，进入开始阶段时都应播放
                    if (handle.Type == ContentType.Video && entry.effectType != AppearEffectType.Show && entry.effectType != AppearEffectType.Hide)
                        CommonShowEffect.PlayVideo(handle);
                    effect.Appear(handle, entryDuration, onOneComplete);
                }
                if (entryDelay > 0f)
                    TrackDelayedCall(DOVirtual.DelayedCall(entryDelay, RunOne));
                else
                    RunOne();
            }
        }

        private void BeginInitialPhaseIfNeeded()
        {
            _pendingStartWhileInitial = false;
            _pendingEndWhileAppearing = false;

            if (initialEffectEntries == null || initialEffectEntries.Count == 0 || !HasValidInitialEntry())
            {
                LogInteractionFile("InteractionPhase", "skip_initial_phase reason=no_valid_initial_entries");
                SetPhase(InteractionPhase.Start);
                return;
            }

            LogInteractionFile("InteractionPhase", $"begin_initial_phase validInitialEntries={initialEffectEntries.Count}");
            SetPhase(InteractionPhase.Initial);
            SetRegisteredTriggersEnabledForInitialPhase();
            ExecuteInitialEffectEntries();
        }

        private bool HasValidInitialEntry()
        {
            if (initialEffectEntries == null)
                return false;

            for (int i = 0; i < initialEffectEntries.Count; i++)
            {
                var entry = initialEffectEntries[i];
                if (entry == null)
                    continue;
                if (HasValidTarget(entry))
                    return true;
            }

            return false;
        }

        private void ExecuteInitialEffectEntries()
        {
            if (initialEffectEntries == null || initialEffectEntries.Count == 0)
            {
                LogInteractionFile("InteractionPhase", "complete_initial_phase_immediately reason=no_initial_entries");
                CompleteInitialPhase();
                return;
            }

            int validCount = 0;
            foreach (var e in initialEffectEntries)
            {
                if (e == null) continue;
                if (HasValidTarget(e))
                    validCount++;
            }

            if (validCount == 0)
            {
                LogInteractionFile("InteractionPhase", "complete_initial_phase_immediately reason=no_valid_initial_targets");
                CompleteInitialPhase();
                return;
            }

            LogInteractionFile("InteractionPhase", $"execute_initial_effects validTargetCount={validCount}");

            int pending = validCount;
            Action onAllInitialComplete = () =>
            {
                pending--;
                if (pending > 0) return;
                CompleteInitialPhase();
            };

            foreach (var e in initialEffectEntries)
            {
                var entry = e;
                if (entry == null) continue;
                IContentHandle handle;
                float entryDelay = entry.delay;

                if (entry.targetType == EffectTargetType.Guide)
                {
                    void RunGuideOne()
                    {
                        ExecuteGuideActionEntry(entry, onAllInitialComplete);
                    }

                    if (entryDelay > 0f)
                        TrackDelayedCall(DOVirtual.DelayedCall(entryDelay, RunGuideOne));
                    else
                        RunGuideOne();
                    continue;
                }

                if (IsSystemTarget(entry.targetType))
                {
                    void RunSystemOne()
                    {
                        ExecuteSystemAction(entry, onAllInitialComplete);
                    }

                    if (entryDelay > 0f)
                        TrackDelayedCall(DOVirtual.DelayedCall(entryDelay, RunSystemOne));
                    else
                        RunSystemOne();
                    continue;
                }

                if (IsStageTarget(entry.targetType))
                {
                    void RunStageOne()
                    {
                        ExecuteStageAction(entry, onAllInitialComplete);
                    }

                    if (entryDelay > 0f)
                        TrackDelayedCall(DOVirtual.DelayedCall(entryDelay, RunStageOne));
                    else
                        RunStageOne();
                    continue;
                }

                if (!TryResolveTargetRoot(entry, out var root) || root == null)
                {
                    onAllInitialComplete();
                    continue;
                }

                try { handle = GetOrCreateHandle(root); }
                catch { onAllInitialComplete(); continue; }
                float entryDuration = entry.duration > 0f ? entry.duration : DefaultDurationWhenZero;
                if (entry.effectType == AppearEffectType.Show || entry.effectType == AppearEffectType.Hide) entryDuration = 0f;
                var effect = GetOrAddAppearEffect(entry.effectType);
                if (effect == null) { onAllInitialComplete(); continue; }

                bool showNeedsDuration = entry.effectType == AppearEffectType.Show && handle.Type == ContentType.VFX;
                float appearCompleteDelay = showNeedsDuration ? (entry.duration > 0f ? entry.duration : DefaultDurationWhenZero) : 0f;
                Action onOneComplete = appearCompleteDelay > 0f
                    ? () => TrackDelayedCall(DOVirtual.DelayedCall(appearCompleteDelay, () => onAllInitialComplete()))
                    : onAllInitialComplete;

                void RunOne()
                {
                    if (root == null) { onAllInitialComplete(); return; }
                    ApplyAppearConfig(entry, effect);
                    if (handle.Type == ContentType.Video)
                        AVProMediaDiagnostics.EnsureForHierarchy(
                            root,
                            GetModuleDebugName(),
                            $"InteractionModule.RunInitialList.before_set_active effectType={entry.effectType}");
                    if (entry.effectType != AppearEffectType.Hide)
                        root.SetActive(true);
                    if (handle.Type == ContentType.Video && entry.effectType != AppearEffectType.Show && entry.effectType != AppearEffectType.Hide)
                        CommonShowEffect.PlayVideo(handle);
                    effect.Appear(handle, entryDuration, onOneComplete);
                    if (entry.effectType == AppearEffectType.Hide)
                        _appearedContents.Remove(handle);
                    else
                        _appearedContents.Add(handle);
                }
                if (entryDelay > 0f)
                    TrackDelayedCall(DOVirtual.DelayedCall(entryDelay, RunOne));
                else
                    RunOne();
            }
        }

        private void CompleteInitialPhase()
        {
            LogModuleState($"CompleteInitialPhase pendingStartWhileInitial={_pendingStartWhileInitial}");
            LogInteractionFile(
                "InteractionPhase",
                $"complete_initial_phase pendingStartWhileInitial={_pendingStartWhileInitial}");
            SetRegisteredTriggersEnabled(true);
            NotifyBackToStart();
            SetPhase(InteractionPhase.Start);

            if (_pendingStartWhileInitial)
            {
                _pendingStartWhileInitial = false;
                HandleTrigger(true);
            }
        }

        private void OnTriggerFiredListDisappear()
        {
            if (_currentPhase != InteractionPhase.InProgress)
            {
                // 非进行阶段的结束触发属于正常防抖，默认不输出
                LogInteractionFile(
                    "InteractionTrigger",
                    $"ignore_list_disappear reason=phase_not_in_progress current={_currentPhase}");
                return;
            }
            // 进入结束阶段默认不输出
            SetPhase(InteractionPhase.End);
            int endVersion = _activeEndPhaseVersion;
            CancelDisappearCompletionFallback();

            foreach (var h in _currentAppearHandles)
                _appearedContents.Remove(h);
            _currentAppearHandles.Clear();

            int validCount = 0;
            foreach (var e in disappearEffectEntries)
            {
                if (e == null) continue;
                if (HasValidTarget(e))
                    validCount++;
            }
            LogInteractionFile("InteractionPhase", $"list_disappear_started validTargetCount={validCount}");
            if (validCount == 0)
            {
                TryCompleteEndPhase(endVersion, "list_disappear_completed_without_targets");
                return;
            }

            int pending = validCount;
            void CompleteDisappearPhase(string reason)
            {
                TryCompleteEndPhase(endVersion, reason);
            }

            Action onAllDisappearComplete = () =>
            {
                pending--;
                if (pending > 0) return;
                CompleteDisappearPhase("list_disappear_completed");
            };

            ScheduleDisappearCompletionFallback(
                GetDisappearCompletionFallbackDelay(),
                () =>
                {
                    LogWarning($"结束效果回调超时，已强制完成模块：{GetModuleDebugName()}");
                    CompleteDisappearPhase("list_disappear_timeout_force_complete");
                });

            foreach (var e in disappearEffectEntries)
            {
                var entry = e;
                if (entry == null) continue;
                IContentHandle handle;
                float entryDelay = entry.delay;

                if (entry.targetType == EffectTargetType.Guide)
                {
                    void RunGuideOne()
                    {
                        ExecuteGuideActionEntry(entry, onAllDisappearComplete);
                    }

                    if (entryDelay > 0f)
                        TrackDelayedCall(DOVirtual.DelayedCall(entryDelay, RunGuideOne));
                    else
                        RunGuideOne();
                    continue;
                }

                if (IsSystemTarget(entry.targetType))
                {
                    void RunSystemOne()
                    {
                        ExecuteSystemAction(entry, onAllDisappearComplete);
                    }

                    if (entryDelay > 0f)
                        TrackDelayedCall(DOVirtual.DelayedCall(entryDelay, RunSystemOne));
                    else
                        RunSystemOne();
                    continue;
                }

                if (IsStageTarget(entry.targetType))
                {
                    void RunStageOne()
                    {
                        ExecuteStageAction(entry, onAllDisappearComplete);
                    }

                    if (entryDelay > 0f)
                        TrackDelayedCall(DOVirtual.DelayedCall(entryDelay, RunStageOne));
                    else
                        RunStageOne();
                    continue;
                }

                if (!TryResolveTargetRoot(entry, out var root) || root == null)
                {
                    onAllDisappearComplete();
                    continue;
                }

                try { handle = GetOrCreateHandle(root); }
                catch { onAllDisappearComplete(); continue; }

                if (entry.effectType == DisappearEffectType.Show)
                {
                    float showDuration = 0f;

                    var showEffect = GetOrAddAppearEffect(AppearEffectType.Show);
                    if (showEffect == null) { onAllDisappearComplete(); continue; }

                    void RunShowOne()
                    {
                        if (root == null)
                        {
                            onAllDisappearComplete();
                            return;
                        }

                        showEffect.Appear(handle, showDuration, onAllDisappearComplete);
                    }

                    if (entryDelay > 0f)
                        TrackDelayedCall(DOVirtual.DelayedCall(entryDelay, RunShowOne));
                    else
                        RunShowOne();

                    continue;
                }

                float entryDuration;
                if (entry.effectType == DisappearEffectType.Hide)
                {
                    entryDuration = (handle.Type == ContentType.VFX) ? (entry.duration > 0f ? entry.duration : DefaultDurationWhenZero) : 0f;
                }
                else
                    entryDuration = entry.duration > 0f ? entry.duration : DefaultDurationWhenZero;
                var effect = GetOrAddDisappearEffect(entry.effectType);
                if (effect == null) { onAllDisappearComplete(); continue; }

                bool suspendVideoAfterDisappear =
                    handle.Type == ContentType.Video &&
                    entry.effectType != DisappearEffectType.Hide;

                Action onOneComplete = () =>
                {
                    if (suspendVideoAfterDisappear)
                        CommonHideEffect.SuspendVideo(handle);

                    if (root != null) root.SetActive(false);
                    onAllDisappearComplete();
                };

                void RunOne()
                {
                    if (root == null) { onOneComplete(); return; }
                    ApplyDisappearConfig(entry, effect);
                    // Video 内容：带结束动画时先暂停保持最后一帧，结束后再真正释放。
                    if (suspendVideoAfterDisappear)
                        CommonHideEffect.PauseVideo(handle);
                    effect.Disappear(handle, entryDuration, onOneComplete);
                }
                if (entryDelay > 0f)
                    TrackDelayedCall(DOVirtual.DelayedCall(entryDelay, RunOne));
                else
                    RunOne();
            }
        }
    }
}
