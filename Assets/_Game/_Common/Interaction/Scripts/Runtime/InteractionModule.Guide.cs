using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using VFXViewer;

namespace Interaction
{
    public partial class InteractionModule
    {
        [Header("导游运行时")]
        [SerializeField] private GlobalGuideRuntime globalGuide;
        [SerializeField] private string globalGuideObjectName = "Guider";
        [SerializeField] private bool autoAddGuideRuntimeIfMissing = true;
        private Transform _cachedGuideWaypoint;

        public string GlobalGuideObjectName =>
            string.IsNullOrWhiteSpace(globalGuideObjectName) ? "Guider" : globalGuideObjectName;

        private Transform ResolveGuideTarget(Transform overrideWaypoint)
        {
            if (overrideWaypoint != null)
                return overrideWaypoint;

            if (_cachedGuideWaypoint != null)
                return _cachedGuideWaypoint;

            var waypoint = GetComponentInChildren<GuideWaypoint>(true);
            if (waypoint != null)
            {
                _cachedGuideWaypoint = waypoint.transform;
                return _cachedGuideWaypoint;
            }

            var named = transform.Find("GuideWaypoint");
            if (named != null)
            {
                _cachedGuideWaypoint = named;
                return _cachedGuideWaypoint;
            }

            return transform;
        }

        private GuideMoveRequest BuildGuideMoveRequest(
            bool immediate,
            bool keepHeight,
            bool rotateTowardsTarget,
            GuideMoveStyle moveStyle,
            Transform pathRoot,
            GuidePathInterpolation pathInterpolation,
            float moveDuration,
            float arriveDistance,
            float rotateSpeedDeg)
        {
            return new GuideMoveRequest
            {
                immediate = immediate,
                keepHeight = keepHeight,
                rotateTowardsTarget = rotateTowardsTarget,
                moveStyle = moveStyle,
                pathRoot = pathRoot,
                pathInterpolation = pathInterpolation,
                moveDuration = moveDuration,
                arriveDistance = arriveDistance,
                rotateSpeedDeg = rotateSpeedDeg
            };
        }

        private bool IsGuideActionMove(GuideListActionType action)
        {
            return action == GuideListActionType.MoveToWaypoint || action == GuideListActionType.ShowAndMoveToWaypoint;
        }

        private static bool IsGuideActionVoiceOnly(GuideListActionType action)
        {
            return action == GuideListActionType.PlayVoice;
        }

        private static bool IsGuideActionAnimationOnly(GuideListActionType action)
        {
            return action == GuideListActionType.PlayAnimation;
        }

        private void ExecuteGuideActionEntry(AppearEffectEntry entry, Action onDone)
        {
            if (entry == null)
            {
                onDone?.Invoke();
                return;
            }

            ExecuteGuideActionCore(
                entry.guideAction,
                entry.guideUseAnimationEffect,
                entry.guideAnimationStateOrTrigger,
                entry.guideAnimationWaitForCompletion,
                entry.guideWaypoint,
                entry.guideMoveImmediate,
                entry.guideKeepHeight,
                entry.guideRotateTowardsTarget,
                entry.guideMoveStyle,
                entry.guidePathRoot,
                entry.guidePathInterpolation,
                entry.guideMoveDuration,
                entry.guideArriveDistance,
                entry.guideRotateSpeedDeg,
                entry.guideVoiceClip,
                entry.guideVoiceVolume,
                entry.guideVoiceOverrideAnimationSettings,
                entry.guideVoicePlayAnimation,
                entry.guideVoiceAnimationStateOrTrigger,
                entry.guideVoiceIdleAnimationStateOrTrigger,
                onDone);
        }

        private void ExecuteGuideActionEntry(DisappearEffectEntry entry, Action onDone)
        {
            if (entry == null)
            {
                onDone?.Invoke();
                return;
            }

            ExecuteGuideActionCore(
                entry.guideAction,
                entry.guideUseAnimationEffect,
                entry.guideAnimationStateOrTrigger,
                entry.guideAnimationWaitForCompletion,
                entry.guideWaypoint,
                entry.guideMoveImmediate,
                entry.guideKeepHeight,
                entry.guideRotateTowardsTarget,
                entry.guideMoveStyle,
                entry.guidePathRoot,
                entry.guidePathInterpolation,
                entry.guideMoveDuration,
                entry.guideArriveDistance,
                entry.guideRotateSpeedDeg,
                entry.guideVoiceClip,
                entry.guideVoiceVolume,
                entry.guideVoiceOverrideAnimationSettings,
                entry.guideVoicePlayAnimation,
                entry.guideVoiceAnimationStateOrTrigger,
                entry.guideVoiceIdleAnimationStateOrTrigger,
                onDone);
        }

        private void ExecuteGuideActionCore(
            GuideListActionType action,
            bool useAnimationEffect,
            string animationStateOrTrigger,
            bool animationWaitForCompletion,
            Transform waypoint,
            bool immediate,
            bool keepHeight,
            bool rotateTowardsTarget,
            GuideMoveStyle moveStyle,
            Transform pathRoot,
            GuidePathInterpolation pathInterpolation,
            float moveDuration,
            float arriveDistance,
            float rotateSpeedDeg,
            AudioClip voiceClip,
            float voiceVolume,
            bool voiceOverrideAnimationSettings,
            bool voicePlayAnimation,
            string voiceAnimationStateOrTrigger,
            string voiceIdleAnimationStateOrTrigger,
            Action onDone)
        {
            if (!ChapterPlacementDirector.TryAuthorizeGlobalGuideCommand(this, out string blockReason))
            {
                string moduleId = ModuleId;
                GameObject playbackTarget = PlaybackTarget;
                PlacementDebugFileLogger.Log(
                    $"[GuideActionGate] blocked module={name} moduleId={moduleId} action={action} " +
                    $"reason={blockReason} target={(playbackTarget != null ? playbackTarget.name : "<null>")} " +
                    $"targetActiveSelf={(playbackTarget != null && playbackTarget.activeSelf)} " +
                    $"targetActiveInHierarchy={(playbackTarget != null && playbackTarget.activeInHierarchy)}");
                onDone?.Invoke();
                return;
            }

            var runtime = ResolveGuideRuntime();
            if (runtime == null)
            {
                PlacementDebugFileLogger.Log(
                    $"[GuideAction] module={name} action={action} runtime=<null> useAnimationEffect={useAnimationEffect}");
                onDone?.Invoke();
                return;
            }

            PlacementDebugFileLogger.Log(
                $"[GuideAction] module={name} action={action} useAnimationEffect={useAnimationEffect} " +
                $"runtime={runtime.name} activeSelf={runtime.gameObject.activeSelf} activeInHierarchy={runtime.gameObject.activeInHierarchy} " +
                $"worldPos=({runtime.transform.position.x:F3},{runtime.transform.position.y:F3},{runtime.transform.position.z:F3})");

            if (IsGuideActionVoiceOnly(action))
            {
                PlayGuideVoiceIfNeeded(
                    runtime,
                    voiceClip,
                    voiceVolume,
                    voiceOverrideAnimationSettings,
                    voicePlayAnimation,
                    voiceAnimationStateOrTrigger,
                    voiceIdleAnimationStateOrTrigger);
                onDone?.Invoke();
                return;
            }

            if (IsGuideActionAnimationOnly(action))
            {
                runtime.PlayAnimation(animationStateOrTrigger, animationWaitForCompletion, onDone);
                return;
            }

            if (action == GuideListActionType.Show || action == GuideListActionType.ShowAndMoveToWaypoint)
            {
                runtime.SetVisible(true);
                if (useAnimationEffect && action == GuideListActionType.Show)
                    runtime.PlayAppearAnimationEffect();
            }
            else if (action == GuideListActionType.Hide)
            {
                if (useAnimationEffect)
                {
                    runtime.PlayDisappearAnimationEffect(() =>
                    {
                        runtime.SetVisible(false);
                        onDone?.Invoke();
                    });
                    return;
                }

                runtime.SetVisible(false);
                onDone?.Invoke();
                return;
            }

            if (!IsGuideActionMove(action))
            {
                onDone?.Invoke();
                return;
            }

            var target = ResolveGuideTarget(waypoint);
            var request = BuildGuideMoveRequest(
                immediate,
                keepHeight,
                rotateTowardsTarget,
                moveStyle,
                pathRoot,
                pathInterpolation,
                moveDuration,
                arriveDistance,
                rotateSpeedDeg);
            runtime.MoveTo(target, request, onDone);
        }

        private static void PlayGuideVoiceIfNeeded(
            GlobalGuideRuntime runtime,
            AudioClip clip,
            float volume,
            bool overrideAnimationSettings,
            bool playAnimation,
            string animationStateOrTrigger,
            string idleAnimationStateOrTrigger)
        {
            if (runtime == null || clip == null)
                return;

            bool? playVoiceAnimation = overrideAnimationSettings ? (bool?)playAnimation : null;
            string talkingState = overrideAnimationSettings ? animationStateOrTrigger : null;
            string idleState = overrideAnimationSettings ? idleAnimationStateOrTrigger : null;

            runtime.PlayVoice(
                clip,
                volume,
                playVoiceAnimation,
                talkingState,
                idleState);
        }

        private void PlayGuideTouchAnimationIfNeeded()
        {
            if (!guideTouchPlayAnimationOnTouch || string.IsNullOrWhiteSpace(guideTouchAnimationStateOrTrigger))
                return;

            var runtime = ResolveGuideRuntime();
            if (runtime == null)
                return;

            runtime.PlayTouchAnimation(guideTouchAnimationStateOrTrigger);
        }

        private GlobalGuideRuntime ResolveGuideRuntime()
        {
            if (globalGuide != null)
            {
                PlacementDebugFileLogger.Log(
                    $"[GuideResolve] module={name} source=cached runtime={globalGuide.name} " +
                    $"activeSelf={globalGuide.gameObject.activeSelf} activeInHierarchy={globalGuide.gameObject.activeInHierarchy} " +
                    $"worldPos=({globalGuide.transform.position.x:F3},{globalGuide.transform.position.y:F3},{globalGuide.transform.position.z:F3})");
                return globalGuide;
            }

            var scene = gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded)
                scene = SceneManager.GetActiveScene();

            if (scene.IsValid() && scene.isLoaded)
            {
                var roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    var root = roots[i];
                    if (root == null)
                        continue;

                    Transform found = FindChildByNameRecursive(root.transform, globalGuideObjectName);
                    if (found == null)
                        continue;

                    globalGuide = found.GetComponent<GlobalGuideRuntime>();
                    if (globalGuide == null && autoAddGuideRuntimeIfMissing)
                        globalGuide = found.gameObject.AddComponent<GlobalGuideRuntime>();

                    if (globalGuide != null)
                    {
                        PlacementDebugFileLogger.Log(
                            $"[GuideResolve] module={name} source=scene-search runtime={globalGuide.name} " +
                            $"activeSelf={globalGuide.gameObject.activeSelf} activeInHierarchy={globalGuide.gameObject.activeInHierarchy} " +
                            $"worldPos=({globalGuide.transform.position.x:F3},{globalGuide.transform.position.y:F3},{globalGuide.transform.position.z:F3})");
                        return globalGuide;
                    }
                }
            }

            globalGuide = FindFirstObjectByType<GlobalGuideRuntime>(FindObjectsInactive.Include);
            if (globalGuide != null)
            {
                PlacementDebugFileLogger.Log(
                    $"[GuideResolve] module={name} source=fallback runtime={globalGuide.name} " +
                    $"activeSelf={globalGuide.gameObject.activeSelf} activeInHierarchy={globalGuide.gameObject.activeInHierarchy} " +
                    $"worldPos=({globalGuide.transform.position.x:F3},{globalGuide.transform.position.y:F3},{globalGuide.transform.position.z:F3})");
                return globalGuide;
            }

            Debug.LogWarning($"[InteractionModule] 未找到全局导游对象：{globalGuideObjectName}。", this);
            return null;
        }

        private static Transform FindChildByNameRecursive(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrEmpty(targetName))
                return null;

            if (string.Equals(root.name, targetName, StringComparison.Ordinal))
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                var found = FindChildByNameRecursive(child, targetName);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
