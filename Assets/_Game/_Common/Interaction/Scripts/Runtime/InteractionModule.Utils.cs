using DG.Tweening;
using UnityEngine;
using System.Collections.Generic;
using VFXViewer;

namespace Interaction
{
    public partial class InteractionModule
    {
        private Tween _disappearCompletionFallbackTween;
        private Tween _phaseTimeoutWatchdogTween;
        private int _phaseTimeoutWatchdogVersion;
        private int _activeEndPhaseVersion;
        private bool _endPhaseCompletionPending;
        private bool _phaseTimingCycleActive;
        private int _phaseTimingCycleIndex;
        private InteractionPhase _phaseTimingTrackedPhase;
        private float _phaseTimingCycleStartedAt;
        private float _phaseTimingPhaseStartedAt;
        private float _phaseTimingInitialDuration;
        private float _phaseTimingStartDuration;
        private float _phaseTimingAppearingDuration;
        private float _phaseTimingInProgressDuration;
        private float _phaseTimingEndDuration;
#if UNITY_EDITOR
        private ExhibitInfo _guidePreviewInfoCache;
        private string _guidePreviewLabelCache;
        private string _guidePreviewLabelModuleIdCache;
        private string _guidePreviewLabelDisplayNameCache;
#endif

        private void CleanupRuntimeState()
        {
            FinalizePhaseTimingCycleIfActive("module_disabled");
            CancelPhaseTimeoutWatchdog();
            CancelDisappearCompletionFallback();
            for (int i = 0; i < _triggers.Count; i++)
                _triggers[i].Triggered -= OnTriggerFired;
            _triggers.Clear();
            ClearTriggerCaches();
            _dragReleaseBehaviors.Clear();
            _contentHandleCache.Clear();
            KillDelayedCalls();
            s_instances.Remove(this);
        }

        private void RegisterTriggerInstance(ITrigger trigger)
        {
            if (trigger is TouchTrigger tct && !_touchTriggers.Contains(tct)) _touchTriggers.Add(tct);
            if (trigger is LongPressTrigger lpt && !_longPressTriggers.Contains(lpt)) _longPressTriggers.Add(lpt);
            if (trigger is TimeTrigger tt && !_timeTriggers.Contains(tt)) _timeTriggers.Add(tt);
            if (trigger != null)
                LogInteractionFile("InteractionTrigger", $"register triggerType={trigger.GetType().Name}");
        }

        private void UnregisterTriggerInstance(ITrigger trigger)
        {
            if (trigger is TouchTrigger tct) _touchTriggers.Remove(tct);
            if (trigger is LongPressTrigger lpt) _longPressTriggers.Remove(lpt);
            if (trigger is TimeTrigger tt) _timeTriggers.Remove(tt);
        }

        private void ClearTriggerCaches()
        {
            _touchTriggers.Clear();
            _longPressTriggers.Clear();
            _timeTriggers.Clear();
        }

        private void SetRegisteredTriggersEnabled(bool enabled)
        {
            int affectedCount = 0;
            for (int i = 0; i < _triggers.Count; i++)
            {
                var trigger = _triggers[i];
                if (trigger == null)
                    continue;

                affectedCount++;

                if (enabled)
                    trigger.Enable();
                else
                    trigger.Disable();
            }

            LogModuleState($"triggerPolicy=all enabled={enabled} triggerCount={affectedCount}");
            LogInteractionFile("InteractionTrigger", $"set_all_triggers_enabled enabled={enabled} affected={affectedCount}");
        }

        private void SetRegisteredTriggersEnabledForInitialPhase()
        {
            int touchEnabledCount = 0;
            int otherDisabledCount = 0;
            for (int i = 0; i < _triggers.Count; i++)
            {
                var trigger = _triggers[i];
                if (trigger == null)
                    continue;

                // 初始阶段允许 Touch/TouchGuide 先记录开始请求，等初始效果结束后统一进入开始流程。
                if (trigger is TouchTrigger)
                {
                    trigger.Enable();
                    touchEnabledCount++;
                }
                else
                {
                    trigger.Disable();
                    otherDisabledCount++;
                }
            }

            LogModuleState($"triggerPolicy=initial touchEnabled={touchEnabledCount} otherDisabled={otherDisabledCount}");
            LogInteractionFile(
                "InteractionTrigger",
                $"set_initial_phase_trigger_policy touchEnabled={touchEnabledCount} otherDisabled={otherDisabledCount}");
        }

        private void NotifyEnteredInProgress()
        {
            for (int i = 0; i < _touchTriggers.Count; i++) _touchTriggers[i].NotifyModuleEnteredInProgress();
            for (int i = 0; i < _longPressTriggers.Count; i++) _longPressTriggers[i].NotifyModuleEnteredInProgress();
            LogInteractionFile("InteractionPhase", "notify_entered_in_progress");
        }

        private void NotifyBackToStart()
        {
            for (int i = 0; i < _touchTriggers.Count; i++) _touchTriggers[i].NotifyModuleBackToStartPhase();
            for (int i = 0; i < _longPressTriggers.Count; i++) _longPressTriggers[i].NotifyModuleBackToStartPhase();
            for (int i = 0; i < _timeTriggers.Count; i++) _timeTriggers[i].ResetForNextCycle();
            LogInteractionFile("InteractionPhase", "notify_back_to_start");
        }

        private void BeginEndCountdownIfAny()
        {
            for (int i = 0; i < _timeTriggers.Count; i++) _timeTriggers[i].BeginEndDelayCountdown();
        }

        private IContentHandle GetOrCreateHandle(GameObject root)
        {
            if (root == null) return null;
            int id = root.GetInstanceID();
            if (_contentHandleCache.TryGetValue(id, out var cached) && cached != null) return cached;
            var handle = ContentHandleFactory.Create(root);
            _contentHandleCache[id] = handle;
            return handle;
        }

        private void TrackDelayedCall(Tween tween)
        {
            if (tween == null) return;
            _delayedCalls.Add(tween);
        }

        private void KillDelayedCalls()
        {
            if (_delayedCalls.Count == 0) return;
            for (int i = 0; i < _delayedCalls.Count; i++)
            {
                var t = _delayedCalls[i];
                if (t != null && t.IsActive()) t.Kill();
            }
            _delayedCalls.Clear();
        }

        private void ScheduleDisappearCompletionFallback(float delay, System.Action onTimeout)
        {
            CancelDisappearCompletionFallback();
            if (onTimeout == null)
                return;

            if (delay <= 0f)
            {
                onTimeout();
                return;
            }

            _disappearCompletionFallbackTween =
                DOVirtual.DelayedCall(delay, () =>
                {
                    _disappearCompletionFallbackTween = null;
                    onTimeout();
                })
                .SetTarget(this);
            TrackDelayedCall(_disappearCompletionFallbackTween);
        }

        private void CancelDisappearCompletionFallback()
        {
            if (_disappearCompletionFallbackTween != null && _disappearCompletionFallbackTween.IsActive())
                _disappearCompletionFallbackTween.Kill();

            _disappearCompletionFallbackTween = null;
        }

        private void SchedulePhaseTimeoutWatchdogForCurrentPhase()
        {
            CancelPhaseTimeoutWatchdog();

            if (!Application.isPlaying ||
                !isActiveAndEnabled ||
                _isPlacementPreviewActive ||
                _remoteControlEnabled ||
                !enablePhaseTimeoutAutoAdvance)
                return;

            float timeout = GetPhaseTimeoutDelay(_currentPhase);
            if (timeout <= 0f)
                return;

            int version = ++_phaseTimeoutWatchdogVersion;
            InteractionPhase phaseSnapshot = _currentPhase;
            _phaseTimeoutWatchdogTween = DOVirtual.DelayedCall(timeout, () =>
                {
                    _phaseTimeoutWatchdogTween = null;
                    if (!Application.isPlaying || !isActiveAndEnabled)
                        return;

                    if (version != _phaseTimeoutWatchdogVersion || _currentPhase != phaseSnapshot)
                        return;

                    LogInteractionFile(
                        "InteractionWatchdog",
                        $"phase_timeout_triggered phase={phaseSnapshot}, timeout={timeout:F2}s");
                    AdvancePhaseByWatchdog(phaseSnapshot);
                })
                .SetTarget(this);
            TrackDelayedCall(_phaseTimeoutWatchdogTween);
            LogInteractionFile(
                "InteractionWatchdog",
                $"phase_timeout_scheduled phase={_currentPhase}, timeout={timeout:F2}s");
        }

        private void CancelPhaseTimeoutWatchdog()
        {
            _phaseTimeoutWatchdogVersion++;
            if (_phaseTimeoutWatchdogTween != null && _phaseTimeoutWatchdogTween.IsActive())
                _phaseTimeoutWatchdogTween.Kill();

            _phaseTimeoutWatchdogTween = null;
        }

        private void AdvancePhaseByWatchdog(InteractionPhase phase)
        {
            switch (phase)
            {
                case InteractionPhase.Initial:
                    LogInteractionFile("InteractionWatchdog", "advance Initial -> Start");
                    CompleteInitialPhase();
                    break;
                case InteractionPhase.Start:
                    LogInteractionFile("InteractionWatchdog", "advance Start -> next via start trigger");
                    TriggerOrForceAdvanceStartPhaseByWatchdog();
                    break;
                case InteractionPhase.Appearing:
                    LogInteractionFile("InteractionWatchdog", "advance Appearing -> InProgress");
                    ForceCompleteAppearingPhaseByWatchdog();
                    break;
                case InteractionPhase.InProgress:
                    LogInteractionFile("InteractionWatchdog", "advance InProgress -> End");
                    TriggerOrForceAdvanceEndPhaseByWatchdog();
                    break;
                case InteractionPhase.End:
                    LogInteractionFile("InteractionWatchdog", "advance End -> completed");
                    TryCompleteCurrentEndPhase("watchdog_timeout_end");
                    break;
            }
        }

        private void TriggerOrForceAdvanceStartPhaseByWatchdog()
        {
            if (_currentPhase != InteractionPhase.Start)
                return;

            InteractionPhase phaseBefore = _currentPhase;
            TryTriggerStart();
            if (_currentPhase != phaseBefore)
                return;

            LogInteractionFile("InteractionWatchdog", "force_advance_start_to_in_progress");
            SetPhase(InteractionPhase.InProgress);
            NotifyEnteredInProgress();
            if (endTriggerType == TriggerType.Time)
                BeginEndCountdownIfAny();
        }

        private void TriggerOrForceAdvanceEndPhaseByWatchdog()
        {
            if (_currentPhase != InteractionPhase.InProgress)
                return;

            InteractionPhase phaseBefore = _currentPhase;
            TryTriggerEnd();
            if (_currentPhase != phaseBefore)
                return;

            LogInteractionFile("InteractionWatchdog", "force_advance_in_progress_to_end");
            SetPhase(InteractionPhase.End);
            TryCompleteCurrentEndPhase("watchdog_force_complete_without_end_trigger");
        }

        private void ForceCompleteAppearingPhaseByWatchdog()
        {
            if (_currentPhase != InteractionPhase.Appearing)
                return;

            SetPhase(InteractionPhase.InProgress);
            NotifyEnteredInProgress();
            if (endTriggerType == TriggerType.Time)
                BeginEndCountdownIfAny();

            if (_pendingEndWhileAppearing)
            {
                _pendingEndWhileAppearing = false;
                LogInteractionFile("InteractionWatchdog", "resume_pending_end_trigger_after_appearing_timeout");
                OnTriggerFiredListDisappear();
            }
        }

        private bool TryCompleteCurrentEndPhase(string reason)
        {
            return TryCompleteEndPhase(_activeEndPhaseVersion, reason);
        }

        private bool TryCompleteEndPhase(int endVersion, string reason)
        {
            if (_currentPhase != InteractionPhase.End)
                return false;

            if (!_endPhaseCompletionPending || endVersion != _activeEndPhaseVersion)
                return false;

            _endPhaseCompletionPending = false;
            CancelPhaseTimeoutWatchdog();
            CancelDisappearCompletionFallback();
            LogInteractionFile("InteractionPhase", $"complete_end_phase reason={reason}");
            NotifyCompleted();
            if (allowRestartAfterEnd)
                ResetToStartPhase();
            return true;
        }

        private void BeginPhaseTimingCycle(InteractionPhase entryPhase, string reason)
        {
            float now = GetPhaseTimingNow();
            _phaseTimingCycleActive = true;
            _phaseTimingCycleIndex++;
            _phaseTimingTrackedPhase = entryPhase;
            _phaseTimingCycleStartedAt = now;
            _phaseTimingPhaseStartedAt = now;
            _phaseTimingInitialDuration = 0f;
            _phaseTimingStartDuration = 0f;
            _phaseTimingAppearingDuration = 0f;
            _phaseTimingInProgressDuration = 0f;
            _phaseTimingEndDuration = 0f;
            LogInteractionFile(
                "InteractionTiming",
                $"cycle_started cycle={_phaseTimingCycleIndex}, entryPhase={entryPhase}, reason={reason}");
        }

        private void TrackPhaseTimingTransition(InteractionPhase nextPhase, string reason)
        {
            if (!_phaseTimingCycleActive)
            {
                BeginPhaseTimingCycle(nextPhase, reason);
                return;
            }

            if (_phaseTimingTrackedPhase == nextPhase)
                return;

            float now = GetPhaseTimingNow();
            float duration = Mathf.Max(0f, now - _phaseTimingPhaseStartedAt);
            AddPhaseTimingDuration(_phaseTimingTrackedPhase, duration);
            LogInteractionFile(
                "InteractionTiming",
                $"phase_duration cycle={_phaseTimingCycleIndex}, completedPhase={_phaseTimingTrackedPhase}, " +
                $"duration={duration:F3}s, nextPhase={nextPhase}, cycleElapsed={Mathf.Max(0f, now - _phaseTimingCycleStartedAt):F3}s, reason={reason}");
            _phaseTimingTrackedPhase = nextPhase;
            _phaseTimingPhaseStartedAt = now;
        }

        private void FinalizePhaseTimingCycleIfActive(string reason)
        {
            if (!_phaseTimingCycleActive)
                return;

            float now = GetPhaseTimingNow();
            float duration = Mathf.Max(0f, now - _phaseTimingPhaseStartedAt);
            AddPhaseTimingDuration(_phaseTimingTrackedPhase, duration);
            float total = Mathf.Max(0f, now - _phaseTimingCycleStartedAt);
            LogInteractionFile(
                "InteractionTiming",
                $"phase_duration cycle={_phaseTimingCycleIndex}, completedPhase={_phaseTimingTrackedPhase}, " +
                $"duration={duration:F3}s, nextPhase=<cycle_end>, cycleElapsed={total:F3}s, reason={reason}");
            LogInteractionFile(
                "InteractionTiming",
                $"cycle_summary cycle={_phaseTimingCycleIndex}, reason={reason}, total={total:F3}s, " +
                $"Initial={_phaseTimingInitialDuration:F3}s, Start={_phaseTimingStartDuration:F3}s, " +
                $"Appearing={_phaseTimingAppearingDuration:F3}s, InProgress={_phaseTimingInProgressDuration:F3}s, " +
                $"End={_phaseTimingEndDuration:F3}s");
            _phaseTimingCycleActive = false;
            _phaseTimingPhaseStartedAt = 0f;
            _phaseTimingCycleStartedAt = 0f;
        }

        private void AddPhaseTimingDuration(InteractionPhase phase, float duration)
        {
            switch (phase)
            {
                case InteractionPhase.Initial:
                    _phaseTimingInitialDuration += duration;
                    break;
                case InteractionPhase.Start:
                    _phaseTimingStartDuration += duration;
                    break;
                case InteractionPhase.Appearing:
                    _phaseTimingAppearingDuration += duration;
                    break;
                case InteractionPhase.InProgress:
                    _phaseTimingInProgressDuration += duration;
                    break;
                case InteractionPhase.End:
                    _phaseTimingEndDuration += duration;
                    break;
            }
        }

        private float GetPhaseTimingNow()
        {
            return Application.isPlaying ? Time.realtimeSinceStartup : 0f;
        }

        private float GetPhaseTimeoutDelay(InteractionPhase phase)
        {
            switch (phase)
            {
                case InteractionPhase.Initial:
                    return Mathf.Max(initialPhaseTimeoutSeconds, GetInitialPhaseExpectedDuration()) + PhaseTimeoutSafetyBufferSeconds;
                case InteractionPhase.Start:
                {
                    float timeout = startPhaseTimeoutSeconds;
                    if (startTriggerType == TriggerType.Time)
                        timeout = Mathf.Max(timeout, Mathf.Max(0f, startTimeDelay));
                    return timeout + PhaseTimeoutSafetyBufferSeconds;
                }
                case InteractionPhase.Appearing:
                    return Mathf.Max(appearingPhaseTimeoutSeconds, GetAppearPhaseExpectedDuration()) + PhaseTimeoutSafetyBufferSeconds;
                case InteractionPhase.InProgress:
                {
                    float timeout = inProgressPhaseTimeoutSeconds;
                    if (endTriggerType == TriggerType.Time)
                        timeout = Mathf.Max(timeout, Mathf.Max(0f, endTimeDelay));
                    return timeout + PhaseTimeoutSafetyBufferSeconds;
                }
                case InteractionPhase.End:
                    return Mathf.Max(endPhaseTimeoutSeconds, GetEndPhaseExpectedDuration()) + PhaseTimeoutSafetyBufferSeconds;
                default:
                    return 0f;
            }
        }

        private float GetInitialPhaseExpectedDuration()
        {
            return GetAppearEntriesExpectedDuration(initialEffectEntries);
        }

        private float GetAppearPhaseExpectedDuration()
        {
            return GetAppearEntriesExpectedDuration(appearEffectEntries);
        }

        private float GetEndPhaseExpectedDuration()
        {
            return GetDisappearCompletionFallbackDelay();
        }

        private float GetAppearEntriesExpectedDuration(IList<AppearEffectEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return 0f;

            float maxDelay = 0f;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || !HasValidTarget(entry))
                    continue;

                float candidate = Mathf.Max(0f, entry.delay) + EstimateAppearEntryDuration(entry);
                if (candidate > maxDelay)
                    maxDelay = candidate;
            }

            return maxDelay;
        }

        private float EstimateAppearEntryDuration(AppearEffectEntry entry)
        {
            if (entry == null)
                return 0f;

            if (entry.targetType == EffectTargetType.Guide)
            {
                if (entry.guideAction == GuideListActionType.MoveToWaypoint ||
                    entry.guideAction == GuideListActionType.ShowAndMoveToWaypoint)
                {
                    return Mathf.Max(0f, entry.guideMoveDuration);
                }

                if (entry.guideAction == GuideListActionType.PlayAnimation)
                {
                    if (!entry.guideAnimationWaitForCompletion)
                        return 0f;

                    float duration = 0f;
                    if (!string.IsNullOrWhiteSpace(entry.guideAnimationStateOrTrigger))
                    {
                        var runtime = ResolveGuideRuntime();
                        if (runtime != null)
                            duration = runtime.EstimateAnimationDuration(entry.guideAnimationStateOrTrigger);
                    }

                    return duration > 0f ? duration : DefaultDurationWhenZero;
                }

                if (entry.guideAction == GuideListActionType.PlayVoice)
                {
                    if (entry.guideVoiceClip != null)
                        return entry.guideVoiceClip.length;
                    return 0f;
                }

                return entry.guideUseAnimationEffect ? DefaultDurationWhenZero : 0f;
            }

            if (IsStageTarget(entry.targetType))
            {
                if (entry.stageActionType == StageActionType.PlaySdfEffect)
                    return Mathf.Max(entry.duration > 0f ? entry.duration : DefaultDurationWhenZero, 0f) + 0.1f;

                return 0f;
            }

            if (IsSystemTarget(entry.targetType))
                return 0f;

            if (entry.effectType == AppearEffectType.Show ||
                entry.effectType == AppearEffectType.Hide)
            {
                if (entry.contentRoot == null)
                    return 0f;

                return ContentHandleFactory.DetectType(entry.contentRoot) == ContentType.VFX
                    ? Mathf.Max(entry.duration > 0f ? entry.duration : DefaultDurationWhenZero, 0f)
                    : 0f;
            }

            return Mathf.Max(entry.duration > 0f ? entry.duration : DefaultDurationWhenZero, 0f);
        }

        private float GetDisappearCompletionFallbackDelay()
        {
            if (disappearEffectEntries == null || disappearEffectEntries.Count == 0)
                return DefaultDurationWhenZero + 0.25f;

            float maxDelay = 0f;
            for (int i = 0; i < disappearEffectEntries.Count; i++)
            {
                var entry = disappearEffectEntries[i];
                if (entry == null)
                    continue;

                float candidate = Mathf.Max(0f, entry.delay) + EstimateDisappearEntryDuration(entry);
                if (candidate > maxDelay)
                    maxDelay = candidate;
            }

            return maxDelay + 0.25f;
        }

        private float EstimateDisappearEntryDuration(DisappearEffectEntry entry)
        {
            if (entry == null)
                return 0f;

            if (entry.targetType == EffectTargetType.Guide)
            {
                if (entry.guideAction == GuideListActionType.MoveToWaypoint ||
                    entry.guideAction == GuideListActionType.ShowAndMoveToWaypoint)
                {
                    return Mathf.Max(0f, entry.guideMoveDuration);
                }

                if (entry.guideAction == GuideListActionType.PlayAnimation)
                {
                    if (!entry.guideAnimationWaitForCompletion)
                        return 0f;

                    float duration = 0f;
                    if (!string.IsNullOrWhiteSpace(entry.guideAnimationStateOrTrigger))
                    {
                        var runtime = ResolveGuideRuntime();
                        if (runtime != null)
                            duration = runtime.EstimateAnimationDuration(entry.guideAnimationStateOrTrigger);
                    }

                    return duration > 0f ? duration : DefaultDurationWhenZero;
                }

                return entry.guideUseAnimationEffect ? DefaultDurationWhenZero : 0f;
            }

            if (IsStageTarget(entry.targetType))
            {
                if (entry.stageActionType == StageActionType.PlaySdfEffect)
                    return Mathf.Max(entry.duration > 0f ? entry.duration : DefaultDurationWhenZero, 0f) + 0.1f;

                return 0f;
            }

            if (IsSystemTarget(entry.targetType))
                return 0f;

            if (entry.effectType == DisappearEffectType.Show)
                return 0f;

            if (entry.effectType == DisappearEffectType.Hide)
            {
                if (entry.contentRoot == null)
                    return 0f;

                return ContentHandleFactory.DetectType(entry.contentRoot) == ContentType.VFX
                    ? Mathf.Max(entry.duration > 0f ? entry.duration : DefaultDurationWhenZero, 0f)
                    : 0f;
            }

            return Mathf.Max(entry.duration > 0f ? entry.duration : DefaultDurationWhenZero, 0f);
        }

        private void LogInfo(string message)
        {
            if (enableVerboseLogging)
                Debug.Log(message, this);
        }

        private void LogModuleState(string message)
        {
            if (enableVerboseLogging)
            {
                Debug.Log(
                    $"[ModuleState] module={GetModuleDebugName()} activeSelf={gameObject.activeSelf} " +
                    $"activeInHierarchy={gameObject.activeInHierarchy} enabled={enabled} phase={_currentPhase} {message}",
                    this);
            }
        }

        private string GetModuleDebugName()
        {
            var info = GetComponentInParent<ExhibitInfo>(true);
            if (info != null && !string.IsNullOrWhiteSpace(info.moduleId))
                return info.moduleId.Trim();

            return string.IsNullOrWhiteSpace(gameObject.name) ? "unnamed" : gameObject.name.Trim();
        }

        private void LogWarning(string message)
        {
            if (PlacementDebugFileLogger.IsLoggingEnabled)
                PlacementDebugFileLogger.Log(
                    $"[InteractionWarning] module={GetModuleDebugName()}, phase={_currentPhase}, message={message}");
            Debug.LogWarning(message, this);
        }

        private void LogError(string message)
        {
            if (PlacementDebugFileLogger.IsLoggingEnabled)
                PlacementDebugFileLogger.Log(
                    $"[InteractionError] module={GetModuleDebugName()}, phase={_currentPhase}, message={message}");
            Debug.LogError(message, this);
        }

        private void LogInteractionFile(string category, string message)
        {
            if (!PlacementDebugFileLogger.IsLoggingEnabled)
                return;

            PlacementDebugFileLogger.Log(
                $"[{category}] module={GetModuleDebugName()}, phase={_currentPhase}, " +
                $"activeSelf={gameObject.activeSelf}, activeInHierarchy={gameObject.activeInHierarchy}, " +
                $"triggerCount={_triggers.Count}, touchTriggers={_touchTriggers.Count}, " +
                $"longPressTriggers={_longPressTriggers.Count}, timeTriggers={_timeTriggers.Count}, " +
                $"playbackTarget={DescribePlaybackTargetState()}, {message}");
        }

        private string DescribeTriggerSummary()
        {
            return
                $"startTrigger={startTriggerType}, endTrigger={endTriggerType}, " +
                $"initialEntries={InitialEntryCount}, appearEntries={AppearEntryCount}, " +
                $"disappearEntries={DisappearEntryCount}, allowRestartAfterEnd={allowRestartAfterEnd}, " +
                $"phaseTimeoutAutoAdvance={enablePhaseTimeoutAutoAdvance}, " +
                $"initialTimeout={initialPhaseTimeoutSeconds:F1}, startTimeout={startPhaseTimeoutSeconds:F1}, " +
                $"appearingTimeout={appearingPhaseTimeoutSeconds:F1}, inProgressTimeout={inProgressPhaseTimeoutSeconds:F1}, " +
                $"endTimeout={endPhaseTimeoutSeconds:F1}, " +
                $"{DescribeTouchInteractableBinding()}";
        }

        private string DescribeTouchInteractableBinding()
        {
            if (touchInteractable == null)
                return "touchInteractable=<none>";

            Transform transform = touchInteractable.transform;
            int explicitColliderCount = touchInteractable.colliders != null ? touchInteractable.colliders.Count : 0;
            int resolvedColliderCount = 0;
            var colliders = touchInteractable.GetComponentsInChildren<Collider>(true);
            if (colliders != null)
                resolvedColliderCount = colliders.Length;

            return
                $"touchInteractable={touchInteractable.gameObject.name}" +
                $"[activeSelf={touchInteractable.gameObject.activeSelf}," +
                $"activeInHierarchy={touchInteractable.gameObject.activeInHierarchy}," +
                $"explicitColliders={explicitColliderCount},resolvedColliders={resolvedColliderCount}," +
                $"worldPos=({transform.position.x:F3},{transform.position.y:F3},{transform.position.z:F3})," +
                $"worldRot=({transform.eulerAngles.x:F3},{transform.eulerAngles.y:F3},{transform.eulerAngles.z:F3})]";
        }

        private string DescribePlaybackTargetState()
        {
            var target = PlaybackTarget;
            if (target == null)
                return "<none>";

            Transform transform = target.transform;
            return
                $"{target.name}[activeSelf={target.activeSelf},activeInHierarchy={target.activeInHierarchy}," +
                $"worldPos=({transform.position.x:F3},{transform.position.y:F3},{transform.position.z:F3})," +
                $"worldRot=({transform.eulerAngles.x:F3},{transform.eulerAngles.y:F3},{transform.eulerAngles.z:F3})]";
        }

        private float GetDefaultDuration<T>(IList<T> entries) where T : class
        {
            if (entries == null) return DefaultDurationWhenZero;
            for (int i = 0; i < entries.Count; i++)
            {
                switch (entries[i])
                {
                    case AppearEffectEntry appear when appear.contentRoot != null && appear.duration > 0f:
                        return appear.duration;
                    case DisappearEffectEntry disappear when disappear.contentRoot != null && disappear.duration > 0f:
                        return disappear.duration;
                }
            }
            return DefaultDurationWhenZero;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            DrawColliderGizmo(enterRegionCollider, new Color(0.1f, 0.8f, 0.3f, 0.5f));
            DrawColliderGizmo(leaveRegionCollider, new Color(0.9f, 0.6f, 0.1f, 0.5f));
            DrawGuidePathGizmos(false);
        }

        public void DrawGuidePathGizmosForEditor()
        {
            DrawGuidePathGizmos(true);
        }

        public bool DrawGuidePathGizmosForEditor(Vector3 start, out Vector3 end)
        {
            return DrawGuidePathGizmos(true, out end, true, start);
        }

        public bool TryGetGuidePreviewStartForEditor(out Vector3 position)
        {
            return TryResolveGuidePreviewStartPosition(out position);
        }

        private static void DrawColliderGizmo(Collider collider, Color color)
        {
            if (collider == null) return;
            Gizmos.color = color;
            var bounds = collider.bounds;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }

        private void DrawGuidePathGizmos(bool useHandles)
        {
            DrawGuidePathGizmos(useHandles, out _, false, Vector3.zero);
        }

        private bool DrawGuidePathGizmos(bool useHandles, out Vector3 end, bool hasForcedStart, Vector3 forcedStart)
        {
            string moduleLabel = GetGuidePreviewLabel();
            Color phaseColor = GetPhaseColor();
            DrawGuidePreviewMarker(transform.position, phaseColor, moduleLabel, useHandles);
            Vector3 current;
            if (hasForcedStart)
                current = forcedStart;
            else if (!TryResolveGuidePreviewStartPosition(out current))
            {
                end = transform.position;
                return false;
            }

            bool hasListGuideMoves = HasGuideMoveEntries();

            if (!hasListGuideMoves)
            {
                end = current;
                return true;
            }

            // 效果列表中的 Guide 条目预览（初始/开始/结束）
            current = DrawInitialGuideEntryPaths(current, useHandles);
            current = DrawAppearGuideEntryPaths(current, useHandles);
            current = DrawDisappearGuideEntryPaths(current, useHandles);
            end = current;
            return true;
        }

        private Vector3 DrawInitialGuideEntryPaths(Vector3 start, bool useHandles)
        {
            if (initialEffectEntries == null || initialEffectEntries.Count == 0)
                return start;

            Vector3 current = start;

            for (int i = 0; i < initialEffectEntries.Count; i++)
            {
                var entry = initialEffectEntries[i];
                if (entry == null || entry.targetType != EffectTargetType.Guide)
                    continue;
                if (!IsGuideActionMove(entry.guideAction))
                    continue;

                Transform target = ResolveGuideTarget(entry.guideWaypoint);
                current = DrawGuidePathPreview(
                    current,
                    target,
                    entry.guideMoveStyle,
                    entry.guidePathRoot,
                    entry.guidePathInterpolation,
                    entry.guideKeepHeight,
                    new Color(0.2f, 0.9f, 0.35f, 0.9f),
                    useHandles);
            }

            return current;
        }

        private Vector3 DrawAppearGuideEntryPaths(Vector3 start, bool useHandles)
        {
            if (appearEffectEntries == null || appearEffectEntries.Count == 0)
                return start;

            Vector3 current = start;

            for (int i = 0; i < appearEffectEntries.Count; i++)
            {
                var entry = appearEffectEntries[i];
                if (entry == null || entry.targetType != EffectTargetType.Guide)
                    continue;
                if (!IsGuideActionMove(entry.guideAction))
                    continue;

                Transform target = ResolveGuideTarget(entry.guideWaypoint);
                current = DrawGuidePathPreview(
                    current,
                    target,
                    entry.guideMoveStyle,
                    entry.guidePathRoot,
                    entry.guidePathInterpolation,
                    entry.guideKeepHeight,
                    new Color(0.2f, 0.9f, 0.35f, 0.9f),
                    useHandles);
            }

            return current;
        }

        private Vector3 DrawDisappearGuideEntryPaths(Vector3 start, bool useHandles)
        {
            if (disappearEffectEntries == null || disappearEffectEntries.Count == 0)
                return start;

            Vector3 current = start;

            for (int i = 0; i < disappearEffectEntries.Count; i++)
            {
                var entry = disappearEffectEntries[i];
                if (entry == null || entry.targetType != EffectTargetType.Guide)
                    continue;
                if (!IsGuideActionMove(entry.guideAction))
                    continue;

                Transform target = ResolveGuideTarget(entry.guideWaypoint);
                current = DrawGuidePathPreview(
                    current,
                    target,
                    entry.guideMoveStyle,
                    entry.guidePathRoot,
                    entry.guidePathInterpolation,
                    entry.guideKeepHeight,
                    new Color(0.2f, 0.9f, 0.35f, 0.9f),
                    useHandles);
            }

            return current;
        }

        private string GetGuidePreviewLabel()
        {
            var info = _guidePreviewInfoCache;
            if (info == null)
                info = _guidePreviewInfoCache = GetComponent<ExhibitInfo>();

            if (info != null)
            {
                string moduleId = string.IsNullOrWhiteSpace(info.moduleId) ? string.Empty : info.moduleId.Trim();
                string displayName = string.IsNullOrWhiteSpace(info.displayName) ? string.Empty : info.displayName.Trim();
                if (_guidePreviewLabelCache != null &&
                    string.Equals(_guidePreviewLabelModuleIdCache, moduleId, System.StringComparison.Ordinal) &&
                    string.Equals(_guidePreviewLabelDisplayNameCache, displayName, System.StringComparison.Ordinal))
                {
                    return _guidePreviewLabelCache;
                }

                string moduleCode = ToModuleCode(moduleId);

                if (!string.IsNullOrWhiteSpace(moduleCode) && !string.IsNullOrWhiteSpace(displayName))
                    _guidePreviewLabelCache = $"{moduleCode}_{displayName}";
                else if (!string.IsNullOrWhiteSpace(moduleCode))
                    _guidePreviewLabelCache = moduleCode;
                else if (!string.IsNullOrWhiteSpace(displayName))
                    _guidePreviewLabelCache = displayName;
                else
                    _guidePreviewLabelCache = gameObject.name;

                _guidePreviewLabelModuleIdCache = moduleId;
                _guidePreviewLabelDisplayNameCache = displayName;
                return _guidePreviewLabelCache;
            }

            return gameObject.name;
        }

        private static string ToModuleCode(string moduleId)
        {
            if (string.IsNullOrWhiteSpace(moduleId))
                return string.Empty;

            string trimmed = moduleId.Trim();
            int firstSeparator = trimmed.IndexOf('_');
            if (firstSeparator <= 0 || firstSeparator >= trimmed.Length - 1)
                return trimmed;

            int secondSeparator = trimmed.IndexOf('_', firstSeparator + 1);
            if (secondSeparator < 0)
                return trimmed;

            string first = trimmed.Substring(0, firstSeparator);
            string second = trimmed.Substring(firstSeparator + 1, secondSeparator - firstSeparator - 1);
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
                return trimmed;

            return first + "_" + second;
        }

        private Color GetPhaseColor()
        {
            switch (_currentPhase)
            {
                case InteractionPhase.Initial:
                    return new Color(0.6f, 0.75f, 1f, 0.95f);
                case InteractionPhase.Appearing:
                    return new Color(0.2f, 0.75f, 1f, 0.95f);
                case InteractionPhase.InProgress:
                    return new Color(0.2f, 0.9f, 0.35f, 0.95f);
                case InteractionPhase.End:
                    return new Color(1f, 0.55f, 0.2f, 0.95f);
                default:
                    return new Color(0.75f, 0.75f, 0.75f, 0.9f);
            }
        }

        private Vector3 DrawGuidePathPreview(
            Vector3 start,
            Transform target,
            GuideMoveStyle moveStyle,
            Transform pathRoot,
            GuidePathInterpolation pathInterpolation,
            bool keepHeight,
            Color color,
            bool useHandles)
        {
            if (target == null)
                return start;

            var points = BuildGuidePreviewPoints(start, target.position, moveStyle, pathRoot, pathInterpolation, keepHeight);
            if (points.Count == 0)
                return start;

            Vector3 end = points[points.Count - 1];
            if (points.Count < 2)
                return end;

            if (useHandles)
            {
                var oldHandleColor = UnityEditor.Handles.color;
                UnityEditor.Handles.color = color;
                UnityEditor.Handles.DrawAAPolyLine(4f, points.ToArray());
                UnityEditor.Handles.color = oldHandleColor;
            }
            else
            {
                Gizmos.color = color;
                for (int i = 1; i < points.Count; i++)
                    Gizmos.DrawLine(points[i - 1], points[i]);
            }

            return end;
        }

        private void DrawGuidePreviewMarker(Vector3 point, Color color, string label, bool useHandles)
        {
            float size = GetMarkerSize(point);
            if (useHandles)
            {
                var oldHandleColor = UnityEditor.Handles.color;
                UnityEditor.Handles.color = color;
                UnityEditor.Handles.SphereHandleCap(0, point, Quaternion.identity, size * 2f, EventType.Repaint);
                UnityEditor.Handles.Label(point + Vector3.up * (size * 1.8f), label);
                UnityEditor.Handles.color = oldHandleColor;
                return;
            }

            Gizmos.color = color;
            Gizmos.DrawSphere(point, size * 0.8f);
            var oldColor = UnityEditor.Handles.color;
            UnityEditor.Handles.color = color;
            UnityEditor.Handles.Label(point + Vector3.up * (size * 1.8f), label);
            UnityEditor.Handles.color = oldColor;
        }

        private bool HasGuideMoveEntries()
        {
            if (initialEffectEntries != null)
            {
                for (int i = 0; i < initialEffectEntries.Count; i++)
                {
                    var entry = initialEffectEntries[i];
                    if (entry != null && entry.targetType == EffectTargetType.Guide && IsGuideActionMove(entry.guideAction))
                        return true;
                }
            }

            if (appearEffectEntries != null)
            {
                for (int i = 0; i < appearEffectEntries.Count; i++)
                {
                    var entry = appearEffectEntries[i];
                    if (entry != null && entry.targetType == EffectTargetType.Guide && IsGuideActionMove(entry.guideAction))
                        return true;
                }
            }

            if (disappearEffectEntries != null)
            {
                for (int i = 0; i < disappearEffectEntries.Count; i++)
                {
                    var entry = disappearEffectEntries[i];
                    if (entry != null && entry.targetType == EffectTargetType.Guide && IsGuideActionMove(entry.guideAction))
                        return true;
                }
            }

            return false;
        }

        private static List<Vector3> BuildGuidePreviewPoints(
            Vector3 start,
            Vector3 targetPos,
            GuideMoveStyle moveStyle,
            Transform pathRoot,
            GuidePathInterpolation pathInterpolation,
            bool keepHeight)
        {
            float fixedY = start.y;
            Vector3 destination = targetPos;
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
                            Vector3 p = child.position;
                            if (keepHeight)
                                p.y = fixedY;
                            AddPointIfFar(waypointPoints, p);
                        }
                    }
                    AddPointIfFar(waypointPoints, destination);

                    if (pathInterpolation == GuidePathInterpolation.Smooth)
                        return BuildCatmullRomPreviewPoints(waypointPoints, 10);

                    points = waypointPoints;
                    break;
                }

                default:
                    AddPointIfFar(points, destination);
                    break;
            }

            return points;
        }

        private static List<Vector3> BuildCatmullRomPreviewPoints(List<Vector3> controlPoints, int segmentsPerSpan)
        {
            if (controlPoints == null || controlPoints.Count <= 2)
                return controlPoints ?? new List<Vector3>();

            int seg = Mathf.Clamp(segmentsPerSpan, 4, 24);
            var result = new List<Vector3>(controlPoints.Count * seg);
            AddPointIfFar(result, controlPoints[0]);

            for (int i = 0; i < controlPoints.Count - 1; i++)
            {
                Vector3 p0 = controlPoints[Mathf.Max(i - 1, 0)];
                Vector3 p1 = controlPoints[i];
                Vector3 p2 = controlPoints[i + 1];
                Vector3 p3 = controlPoints[Mathf.Min(i + 2, controlPoints.Count - 1)];

                for (int s = 1; s <= seg; s++)
                {
                    float t = s / (float)seg;
                    Vector3 sample = CatmullRom(p0, p1, p2, p3, t);
                    AddPointIfFar(result, sample);
                }
            }

            return result;
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        private static void AddPointIfFar(List<Vector3> points, Vector3 point)
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

        private bool TryResolveGuidePreviewStartPosition(out Vector3 position)
        {
            if (initialEffectEntries != null)
            {
                for (int i = 0; i < initialEffectEntries.Count; i++)
                {
                    if (TryResolveGuideEntryPosition(initialEffectEntries[i], requireRevealAction: true, out position))
                        return true;
                }
            }

            if (appearEffectEntries != null)
            {
                for (int i = 0; i < appearEffectEntries.Count; i++)
                {
                    if (TryResolveGuideEntryPosition(appearEffectEntries[i], requireRevealAction: true, out position))
                        return true;
                }
            }

            if (disappearEffectEntries != null)
            {
                for (int i = 0; i < disappearEffectEntries.Count; i++)
                {
                    if (TryResolveGuideEntryPosition(disappearEffectEntries[i], requireRevealAction: true, out position))
                        return true;
                }
            }

            // 兜底：兼容“先显示后移动”或仅配置移动动作的历史数据。
            if (initialEffectEntries != null)
            {
                for (int i = 0; i < initialEffectEntries.Count; i++)
                {
                    if (TryResolveGuideEntryPosition(initialEffectEntries[i], requireRevealAction: false, out position))
                        return true;
                }
            }

            if (appearEffectEntries != null)
            {
                for (int i = 0; i < appearEffectEntries.Count; i++)
                {
                    if (TryResolveGuideEntryPosition(appearEffectEntries[i], requireRevealAction: false, out position))
                        return true;
                }
            }

            if (disappearEffectEntries != null)
            {
                for (int i = 0; i < disappearEffectEntries.Count; i++)
                {
                    if (TryResolveGuideEntryPosition(disappearEffectEntries[i], requireRevealAction: false, out position))
                        return true;
                }
            }

            position = Vector3.zero;
            return false;
        }

        private bool TryResolveGuideEntryPosition(AppearEffectEntry entry, bool requireRevealAction, out Vector3 position)
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

        private bool TryResolveGuideEntryPosition(DisappearEffectEntry entry, bool requireRevealAction, out Vector3 position)
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

        private static float GetMarkerSize(Vector3 worldPos)
        {
            float handleSize = UnityEditor.HandleUtility.GetHandleSize(worldPos);
            return Mathf.Max(0.01f, handleSize * 0.04f);
        }
#endif
    }
}
