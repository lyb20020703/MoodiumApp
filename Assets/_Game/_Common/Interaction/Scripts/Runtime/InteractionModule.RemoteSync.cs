using UnityEngine;
using VFXViewer;

namespace Interaction
{
    public partial class InteractionModule
    {
        private bool _remoteControlEnabled;
        private int _phaseSequence;

        public bool IsRemoteControlEnabled => _remoteControlEnabled;
        public int PhaseSequence => _phaseSequence;
        public bool PendingStartWhileInitial => _pendingStartWhileInitial;
        public bool PendingEndWhileAppearing => _pendingEndWhileAppearing;

        public bool TryTriggerEnd()
        {
            if (!Application.isPlaying)
                return false;

            InteractionPhase phaseBefore = _currentPhase;
            bool pendingBefore = _pendingEndWhileAppearing;
            HandleTrigger(false);
            return phaseBefore != _currentPhase || pendingBefore != _pendingEndWhileAppearing;
        }

        public void ForceResetToStart()
        {
            if (!Application.isPlaying)
                return;

            FinalizePhaseTimingCycleIfActive("force_reset_to_start");
            KillDelayedCalls();
            CancelPhaseTimeoutWatchdog();
            CancelDisappearCompletionFallback();
            _currentAppearHandles.Clear();
            _appearedContents.Clear();
            _pendingStartWhileInitial = false;
            _pendingEndWhileAppearing = false;
            _endPhaseCompletionPending = false;
            SetPhase(InteractionPhase.Start);
            NotifyBackToStart();
            ApplyTriggerPolicyForRemoteControlState();
        }

        public void SetRemoteControlEnabled(bool enabled)
        {
            if (_remoteControlEnabled == enabled)
            {
                ApplyTriggerPolicyForRemoteControlState();
                return;
            }

            _remoteControlEnabled = enabled;
            ApplyTriggerPolicyForRemoteControlState();
        }

        public ModuleRemoteStateSnapshot CaptureRemotePlaybackState(long hostTimestamp)
        {
            return new ModuleRemoteStateSnapshot
            {
                moduleId = ModuleId,
                phase = (int)_currentPhase,
                phaseSequence = _phaseSequence,
                hostTimestamp = hostTimestamp,
                gameObjectActive = gameObject.activeSelf,
                pendingStartWhileInitial = _pendingStartWhileInitial,
                pendingEndWhileAppearing = _pendingEndWhileAppearing,
            };
        }

        public bool ApplyRemotePlaybackState(ModuleRemoteStateSnapshot snapshot)
        {
            if (snapshot == null)
                return false;

            if (!string.IsNullOrEmpty(snapshot.moduleId) &&
                !string.Equals(snapshot.moduleId, ModuleId, System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (!Application.isPlaying)
                return false;

            if (!gameObject.activeSelf && snapshot.gameObjectActive)
                gameObject.SetActive(true);

            SetRemoteControlEnabled(true);
            InteractionPhase targetPhase = (InteractionPhase)snapshot.phase;
            ForceResetToStart();
            ApplyCompletedInitialStateForRemote();
            if (targetPhase == InteractionPhase.Initial || targetPhase == InteractionPhase.Start)
            {
                if (snapshot.pendingStartWhileInitial)
                    TryTriggerStart();
                return true;
            }

            TryTriggerStart();

            if (targetPhase == InteractionPhase.End || snapshot.pendingEndWhileAppearing)
                TryTriggerEnd();

            return true;
        }

        private void ApplyCompletedInitialStateForRemote()
        {
            if (!Application.isPlaying || initialEffectEntries == null || initialEffectEntries.Count == 0)
                return;

            for (int i = 0; i < initialEffectEntries.Count; i++)
            {
                AppearEffectEntry entry = initialEffectEntries[i];
                if (entry == null || !HasValidTarget(entry))
                    continue;

                if (entry.targetType == EffectTargetType.Guide)
                {
                    ApplyCompletedGuideStateForRemote(entry);
                    continue;
                }

                if (IsSystemTarget(entry.targetType))
                    continue;

                if (IsStageTarget(entry.targetType))
                {
                    ExecuteStageAction(entry, null);
                    continue;
                }

                if (!TryResolveTargetRoot(entry, out GameObject root) || root == null)
                    continue;

                IContentHandle handle;
                try
                {
                    handle = GetOrCreateHandle(root);
                }
                catch
                {
                    continue;
                }

                IAppearEffect effect = GetOrAddAppearEffect(entry.effectType);
                if (effect == null)
                    continue;

                ApplyAppearConfig(entry, effect);
                if (entry.effectType != AppearEffectType.Hide)
                    root.SetActive(true);

                if (handle.Type == ContentType.Video &&
                    entry.effectType != AppearEffectType.Show &&
                    entry.effectType != AppearEffectType.Hide)
                {
                    CommonShowEffect.PlayVideo(handle);
                }

                effect.Appear(handle, 0f, null);
                if (entry.effectType == AppearEffectType.Hide)
                    _appearedContents.Remove(handle);
                else
                    _appearedContents.Add(handle);
            }
        }

        private void ApplyCompletedGuideStateForRemote(AppearEffectEntry entry)
        {
            if (entry == null)
                return;

            GlobalGuideRuntime runtime = ResolveGuideRuntime();
            if (runtime == null)
                return;

            GuideListActionType action = entry.guideAction;
            if (action == GuideListActionType.Show || action == GuideListActionType.ShowAndMoveToWaypoint)
                runtime.SetVisible(true);
            else if (action == GuideListActionType.Hide)
            {
                runtime.SetVisible(false);
                return;
            }

            if (!IsGuideActionMove(action))
                return;

            Transform target = ResolveGuideTarget(entry.guideWaypoint);
            GuideMoveRequest request = BuildGuideMoveRequest(
                immediate: true,
                keepHeight: entry.guideKeepHeight,
                rotateTowardsTarget: entry.guideRotateTowardsTarget,
                moveStyle: entry.guideMoveStyle,
                pathRoot: entry.guidePathRoot,
                pathInterpolation: entry.guidePathInterpolation,
                moveDuration: entry.guideMoveDuration,
                arriveDistance: entry.guideArriveDistance,
                rotateSpeedDeg: entry.guideRotateSpeedDeg);

            runtime.MoveTo(target, request, null);
        }

        private void ApplyTriggerPolicyForRemoteControlState()
        {
            if (_remoteControlEnabled)
            {
                SetRegisteredTriggersEnabled(false);
                return;
            }

            if (_currentPhase == InteractionPhase.Initial)
                SetRegisteredTriggersEnabledForInitialPhase();
            else
                SetRegisteredTriggersEnabled(true);
        }

        partial void NotifyEnabledForExtensions()
        {
            ApplyTriggerPolicyForRemoteControlState();
        }

        partial void NotifyPhaseChangedForExtensions(InteractionPhase previous, InteractionPhase current)
        {
            _phaseSequence++;
            ApplyTriggerPolicyForRemoteControlState();
        }
    }
}
