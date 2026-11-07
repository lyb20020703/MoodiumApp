using System;
using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;
using UnityEngine;
using VFXViewer;

namespace Interaction
{
    [Serializable]
    public struct GuideMoveRequest
    {
        public bool immediate;
        public bool keepHeight;
        public bool rotateTowardsTarget;
        public float moveDuration;
        public float arriveDistance;
        public float rotateSpeedDeg;
        public GuideMoveStyle moveStyle;
        public Transform pathRoot;
        public GuidePathInterpolation pathInterpolation;
    }

    /// <summary>
    /// 全局导游执行器：负责显示隐藏与移动。
    /// 由 InteractionModule.Guide 执行动作调用，不包含流程编排逻辑。
    /// </summary>
    public class GlobalGuideRuntime : MonoBehaviour
    {
        private static readonly bool EnableGuideRuntimeDiagnostics = false;
        private const string GuideAnimationNodeName = "Animation";
        private const string GuideAppearEffectName = "Appear";
        private const string GuideDisappearEffectName = "Disappear";
        private const string VoiceTalkingBoolParameterName = "IsTalking";

        [Header("导游语音联动")]
        [SerializeField] private bool autoPlayVoiceAnimation = true;
        [SerializeField] private string voiceTalkingAnimationStateOrTrigger = "Ani_Water_Talk";
        [SerializeField] private string voiceIdleAnimationStateOrTrigger = "Ani_WaterBall_Idle";

        [Header("导游语音空间音频")]
        [SerializeField, Range(0f, 1f)] private float voiceSpatialBlend = 1f;
        [SerializeField] private bool spatializeVoiceIfPluginAvailable = true;
        [SerializeField] private AudioRolloffMode voiceRolloffMode = AudioRolloffMode.Logarithmic;
        [SerializeField, Min(0f)] private float voiceDopplerLevel = 0f;
        [SerializeField, Min(0.01f)] private float voiceMinDistance = 1f;
        [SerializeField, Min(0.01f)] private float voiceMaxDistance = 15f;

        private Tween m_MoveTween;
        private Action m_MoveCompletion;
        private Transform m_InitialParent;
        private Vector3 m_InitialLocalPosition;
        private Quaternion m_InitialLocalRotation;
        private Vector3 m_InitialWorldPosition;
        private Quaternion m_InitialWorldRotation;
        private bool m_InitialActiveSelf;
        private bool m_InitialPoseCaptured;
        private AudioSource m_VoiceAudioSource;
        private KeyframeAnimationRunner m_GuideAnimationRunner;
        private GuideAnimationEventRelay m_GuideAnimationRelay;
        private bool m_RestoreIdleAfterVoice;
        private bool m_HasObservedVoicePlayback;
        private string m_PendingVoiceIdleAnimationStateOrTrigger;
        private bool m_UseVoiceTalkingBoolParameter;
        private Transform m_FollowTarget;
        private GuideMoveRequest m_FollowTargetRequest;
        private Transform m_FollowReferenceFrame;
        private Vector3 m_LastFollowTargetPosition;
        private Quaternion m_LastFollowTargetRotation = Quaternion.identity;
        private Vector3 m_LastFollowGuidePosition;
        private Quaternion m_LastFollowGuideRotation = Quaternion.identity;

        public bool IsVisible => gameObject.activeSelf;
        public bool HasTrackedMoveTarget => m_FollowTarget != null;

        public void SetVisible(bool visible)
        {
            CaptureInitialPoseIfNeeded();
            CancelGuideAnimationCallbacks();

            if (gameObject.activeSelf == visible)
                return;

            gameObject.SetActive(visible);
        }

        public void PlayAppearAnimationEffect()
        {
            CancelVoicePlaybackTracking();
            PlayGuideAnimationEffect(GuideAppearEffectName, waitForCompletion: false, null);
        }

        public void PlayDisappearAnimationEffect(Action onComplete)
        {
            CancelVoicePlaybackTracking();
            PlayGuideAnimationEffect(GuideDisappearEffectName, waitForCompletion: true, onComplete);
        }

        public void PlayAnimation(string stateOrTrigger, bool waitForCompletion, Action onComplete)
        {
            CancelVoicePlaybackTracking();

            if (string.IsNullOrWhiteSpace(stateOrTrigger))
            {
                onComplete?.Invoke();
                return;
            }

            PlayGuideAnimationEffect(stateOrTrigger, waitForCompletion, onComplete);
        }

        public void PlayTouchAnimation(string stateOrTrigger)
        {
            if (string.IsNullOrWhiteSpace(stateOrTrigger))
                return;

            TryPlayGuideAnimationWithoutCallbacks(stateOrTrigger);
        }

        public float EstimateAnimationDuration(string stateOrTrigger)
        {
            if (string.IsNullOrWhiteSpace(stateOrTrigger))
                return 0f;

            if (!TryResolveGuideAnimationAnimator(out var animator))
                return 0f;

            return ResolveGuideAnimationDuration(animator, stateOrTrigger);
        }

        public void PlayVoice(
            AudioClip clip,
            float volume = 1f,
            bool? playVoiceAnimation = null,
            string talkingAnimationStateOrTrigger = null,
            string idleAnimationStateOrTrigger = null)
        {
            if (clip == null)
                return;

            AudioSource source = EnsureVoiceAudioSource();
            if (source == null)
                return;

            CancelVoicePlaybackTracking();

            if (source.isPlaying)
                source.Stop();

            source.clip = clip;
            source.volume = Mathf.Clamp01(volume);
            source.Play();

            bool shouldPlayVoiceAnimation = playVoiceAnimation ?? autoPlayVoiceAnimation;
            if (!shouldPlayVoiceAnimation)
                return;

            string talkingState = string.IsNullOrWhiteSpace(talkingAnimationStateOrTrigger)
                ? voiceTalkingAnimationStateOrTrigger
                : talkingAnimationStateOrTrigger;
            string idleState = string.IsNullOrWhiteSpace(idleAnimationStateOrTrigger)
                ? voiceIdleAnimationStateOrTrigger
                : idleAnimationStateOrTrigger;

            m_HasObservedVoicePlayback = source.isPlaying;
            m_UseVoiceTalkingBoolParameter = TrySetGuideAnimationBool(VoiceTalkingBoolParameterName, true);
            m_PendingVoiceIdleAnimationStateOrTrigger = idleState;
            m_RestoreIdleAfterVoice = m_UseVoiceTalkingBoolParameter || !string.IsNullOrWhiteSpace(idleState);

            if (!m_UseVoiceTalkingBoolParameter)
                TryPlayGuideAnimationWithoutCallbacks(talkingState);
        }

        public void MoveTo(Transform target, GuideMoveRequest request, Action onArrived = null)
        {
            CaptureInitialPoseIfNeeded();
            CompleteInterruptedMove("superseded by a new move request");

            if (target == null)
            {
                ClearFollowTarget();
                Debug.LogWarning("[GuideRuntime] MoveTo target is null. Completing immediately.", this);
                onArrived?.Invoke();
                return;
            }

            SetFollowTarget(target, request);

            if (request.immediate)
            {
                SnapTo(target, request);
                RefreshFollowTargetSnapshots();
                onArrived?.Invoke();
                return;
            }

            if (!gameObject.activeInHierarchy)
            {
                // 导游不可见或不在激活层级时，无法执行 Tween，退化为直接到位。
                SnapTo(target, request);
                RefreshFollowTargetSnapshots();
                onArrived?.Invoke();
                return;
            }

            float fixedY = transform.position.y;
            Vector3 destination = ResolveDestination(target, request.keepHeight, fixedY);
            if (IsArrived(destination, request.arriveDistance))
            {
                var endRotation = ComputeTargetRotation(destination, request.rotateTowardsTarget);
                SetPose(destination, endRotation, request.rotateTowardsTarget);
                RefreshFollowTargetSnapshots();
                onArrived?.Invoke();
                return;
            }

            Tween tween = BuildMoveTween(destination, request, fixedY);
            if (tween == null)
            {
                var endRotation = ComputeTargetRotation(destination, request.rotateTowardsTarget);
                SetPose(destination, endRotation, request.rotateTowardsTarget);
                RefreshFollowTargetSnapshots();
                onArrived?.Invoke();
                return;
            }

            PlayMoveTween(tween, destination, request, onArrived);
        }

        public void ResetToInitialPose()
        {
            CaptureInitialPoseIfNeeded();

            CancelMoveWithoutCompleting("reset to initial placement pose");
            CancelGuideAnimationCallbacks();
            StopVoicePlayback();
            ClearFollowTarget();

            bool shouldTemporarilyActivateForReset = !gameObject.activeSelf && m_InitialActiveSelf;
            if (shouldTemporarilyActivateForReset)
                gameObject.SetActive(true);

            ResetGuideAnimationState();
            bool usedWorldPoseFallback = RestoreInitialTransformPose();
            if (gameObject.activeSelf != m_InitialActiveSelf)
                gameObject.SetActive(m_InitialActiveSelf);

            string message =
                $"[GuideRuntime] reset_to_initial name={name} activeSelf={m_InitialActiveSelf} " +
                $"usedWorldFallback={usedWorldPoseFallback} " +
                $"currentParent={GetTransformPath(transform.parent)} initialParent={GetTransformPath(m_InitialParent)} " +
                $"worldPos={FormatVector3(transform.position)} localPos={FormatVector3(transform.localPosition)} " +
                $"worldScale={FormatVector3(transform.lossyScale)} localScale={FormatVector3(transform.localScale)}";
            LogGuideRuntime(message);
        }

        public void PrepareForAppModeTransition(bool visible)
        {
            CaptureInitialPoseIfNeeded();

            CancelMoveWithoutCompleting("prepare for app mode transition");
            CancelGuideAnimationCallbacks();
            StopVoicePlayback();
            ClearFollowTarget();

            if (gameObject.activeSelf != visible)
                gameObject.SetActive(visible);

            string message =
                $"[GuideRuntime] prepare_for_mode_transition name={name} visible={visible} " +
                $"parent={GetTransformPath(transform.parent)} " +
                $"worldPos={FormatVector3(transform.position)} localPos={FormatVector3(transform.localPosition)} " +
                $"worldScale={FormatVector3(transform.lossyScale)} localScale={FormatVector3(transform.localScale)}";
            LogGuideRuntime(message);
        }

        private void StopVoicePlayback()
        {
            AudioSource source = m_VoiceAudioSource;
            if (source != null)
            {
                if (source.isPlaying)
                    source.Stop();
                source.clip = null;
            }

            CancelVoicePlaybackTracking();
        }

        private void ResetGuideAnimationState()
        {
            if (!TryResolveGuideAnimationAnimator(out var animator) || animator == null)
                return;

            if (!animator.enabled)
                animator.enabled = true;

            if (!animator.gameObject.activeInHierarchy)
                return;

            animator.Rebind();
            animator.Update(0f);
        }

        private AudioSource EnsureVoiceAudioSource()
        {
            if (m_VoiceAudioSource != null)
            {
                ConfigureVoiceAudioSource(m_VoiceAudioSource);
                return m_VoiceAudioSource;
            }

            m_VoiceAudioSource = GetComponent<AudioSource>();
            if (m_VoiceAudioSource == null)
                m_VoiceAudioSource = gameObject.AddComponent<AudioSource>();

            ConfigureVoiceAudioSource(m_VoiceAudioSource);

            return m_VoiceAudioSource;
        }

        private void ConfigureVoiceAudioSource(AudioSource source)
        {
            if (source == null)
                return;

            source.playOnAwake = false;
            source.loop = false;
            source.panStereo = 0f;
            source.spatialBlend = Mathf.Clamp01(voiceSpatialBlend);
            source.rolloffMode = voiceRolloffMode;
            source.dopplerLevel = Mathf.Max(0f, voiceDopplerLevel);
            source.minDistance = Mathf.Max(0.01f, voiceMinDistance);
            source.maxDistance = Mathf.Max(source.minDistance, voiceMaxDistance);

            bool canSpatialize = spatializeVoiceIfPluginAvailable && HasSpatializerPlugin();
            source.spatialize = canSpatialize;
            source.spatializePostEffects = canSpatialize;
        }

        private static bool HasSpatializerPlugin()
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;

            MethodInfo getPluginNamesMethod = typeof(AudioSettings).GetMethod("GetSpatializerPluginNames", flags);
            if (getPluginNamesMethod != null)
            {
                string[] pluginNames = getPluginNamesMethod.Invoke(null, null) as string[];
                return pluginNames != null && pluginNames.Length > 0;
            }

            MethodInfo getPluginNameMethod = typeof(AudioSettings).GetMethod("GetSpatializerPluginName", flags);
            if (getPluginNameMethod != null)
            {
                string pluginName = getPluginNameMethod.Invoke(null, null) as string;
                return !string.IsNullOrWhiteSpace(pluginName);
            }

            return false;
        }

        private void Update()
        {
            MonitorVoicePlayback();
            FollowTrackedTargetIfNeeded();
        }

        private void PlayGuideAnimationEffect(string effectName, bool waitForCompletion, Action onComplete)
        {
            if (string.IsNullOrWhiteSpace(effectName))
            {
                onComplete?.Invoke();
                return;
            }

            CancelGuideAnimationCallbacks();

            if (!TryResolveGuideAnimationAnimator(out var animator))
            {
                onComplete?.Invoke();
                return;
            }

            if (!animator.enabled)
                animator.enabled = true;

            if (!animator.gameObject.activeInHierarchy)
            {
                onComplete?.Invoke();
                return;
            }

            GuideAnimationEventRelay relay = null;
            if (waitForCompletion)
            {
                relay = EnsureGuideAnimationRelay(animator.gameObject);
                relay.Begin(effectName, onComplete);
            }

            if (!TryTriggerGuideAnimation(animator, effectName))
            {
                relay?.Cancel();
                onComplete?.Invoke();
                return;
            }

            if (!waitForCompletion)
            {
                onComplete?.Invoke();
                return;
            }

            float clipLength = ResolveGuideAnimationDuration(animator, effectName);
            if (clipLength > 0f)
                EnsureGuideAnimationRunner(animator.gameObject).InvokeAfter(clipLength, relay.CompletePending);
        }

        private bool TryPlayGuideAnimationWithoutCallbacks(string effectName)
        {
            if (string.IsNullOrWhiteSpace(effectName))
                return false;

            if (!TryResolveGuideAnimationAnimator(out var animator))
                return false;

            if (!animator.enabled)
                animator.enabled = true;

            if (!animator.gameObject.activeInHierarchy)
                return false;

            return TryTriggerGuideAnimation(animator, effectName);
        }

        private bool TrySetGuideAnimationBool(string parameterName, bool value)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
                return false;

            if (!TryResolveGuideAnimationAnimator(out var animator))
                return false;

            if (!animator.enabled)
                animator.enabled = true;

            if (!animator.gameObject.activeInHierarchy)
                return false;

            var parameters = animator.parameters;
            if (parameters == null)
                return false;

            for (int i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                if (!string.Equals(parameter.name, parameterName, StringComparison.Ordinal))
                    continue;

                if (parameter.type != AnimatorControllerParameterType.Bool)
                    return false;

                animator.SetBool(parameterName, value);
                return true;
            }

            return false;
        }

        private void MonitorVoicePlayback()
        {
            if (!m_RestoreIdleAfterVoice)
                return;

            AudioSource source = m_VoiceAudioSource;
            if (source == null)
            {
                CancelVoicePlaybackTracking();
                return;
            }

            if (source.isPlaying)
            {
                m_HasObservedVoicePlayback = true;
                return;
            }

            if (!m_HasObservedVoicePlayback)
                return;

            bool useTalkingBoolParameter = m_UseVoiceTalkingBoolParameter;
            string idleState = m_PendingVoiceIdleAnimationStateOrTrigger;
            CancelVoicePlaybackTracking(resetTalkingAnimation: false);

            if (useTalkingBoolParameter)
            {
                TrySetGuideAnimationBool(VoiceTalkingBoolParameterName, false);
                return;
            }

            TryPlayGuideAnimationWithoutCallbacks(idleState);
        }

        private void CancelVoicePlaybackTracking(bool resetTalkingAnimation = true)
        {
            bool useTalkingBoolParameter = m_UseVoiceTalkingBoolParameter;
            m_RestoreIdleAfterVoice = false;
            m_HasObservedVoicePlayback = false;
            m_PendingVoiceIdleAnimationStateOrTrigger = null;
            m_UseVoiceTalkingBoolParameter = false;

            if (resetTalkingAnimation && useTalkingBoolParameter)
                TrySetGuideAnimationBool(VoiceTalkingBoolParameterName, false);
        }

        private void CancelGuideAnimationCallbacks()
        {
            if (m_GuideAnimationRunner != null)
                m_GuideAnimationRunner.Cancel();
            if (m_GuideAnimationRelay != null)
                m_GuideAnimationRelay.Cancel();
        }

        private bool TryResolveGuideAnimationAnimator(out Animator animator)
        {
            animator = null;

            Transform animationRoot = FindChildByNameRecursive(transform, GuideAnimationNodeName);
            if (animationRoot == null)
                return false;

            animator = animationRoot.GetComponent<Animator>();
            if (animator == null)
                animator = animationRoot.GetComponentInChildren<Animator>(true);

            return animator != null;
        }

        private static bool TryTriggerGuideAnimation(Animator animator, string effectName)
        {
            if (animator == null || string.IsNullOrEmpty(effectName))
                return false;

            var parameters = animator.parameters;
            if (parameters != null)
            {
                for (int i = 0; i < parameters.Length; i++)
                {
                    var parameter = parameters[i];
                    if (!string.Equals(parameter.name, effectName, StringComparison.Ordinal))
                        continue;

                    if (parameter.type == AnimatorControllerParameterType.Trigger)
                    {
                        animator.ResetTrigger(effectName);
                        animator.SetTrigger(effectName);
                        return true;
                    }

                    break;
                }
            }

            animator.Play(effectName, 0, 0f);
            return true;
        }

        private static float ResolveGuideAnimationDuration(Animator animator, string effectName)
        {
            if (animator == null || animator.runtimeAnimatorController == null || string.IsNullOrEmpty(effectName))
                return 0f;

            float partialMatchLength = 0f;
            var clips = animator.runtimeAnimatorController.animationClips;
            if (clips == null)
                return 0f;

            for (int i = 0; i < clips.Length; i++)
            {
                var clip = clips[i];
                if (clip == null)
                    continue;

                if (string.Equals(clip.name, effectName, StringComparison.OrdinalIgnoreCase))
                    return clip.length;

                if (clip.name.IndexOf(effectName, StringComparison.OrdinalIgnoreCase) >= 0)
                    partialMatchLength = Mathf.Max(partialMatchLength, clip.length);
            }

            return partialMatchLength;
        }

        private KeyframeAnimationRunner EnsureGuideAnimationRunner(GameObject owner)
        {
            if (m_GuideAnimationRunner == null || m_GuideAnimationRunner.gameObject != owner)
                m_GuideAnimationRunner = owner.GetComponent<KeyframeAnimationRunner>() ?? owner.AddComponent<KeyframeAnimationRunner>();
            return m_GuideAnimationRunner;
        }

        private GuideAnimationEventRelay EnsureGuideAnimationRelay(GameObject owner)
        {
            if (m_GuideAnimationRelay == null || m_GuideAnimationRelay.gameObject != owner)
                m_GuideAnimationRelay = owner.GetComponent<GuideAnimationEventRelay>() ?? owner.AddComponent<GuideAnimationEventRelay>();
            return m_GuideAnimationRelay;
        }

        private static Transform FindChildByNameRecursive(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrEmpty(targetName))
                return null;

            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (string.Equals(child.name, targetName, StringComparison.Ordinal))
                    return child;

                var nested = FindChildByNameRecursive(child, targetName);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private Tween BuildMoveTween(Vector3 destination, GuideMoveRequest request, float fixedY)
        {
            float duration = Mathf.Max(0.01f, request.moveDuration);

            switch (request.moveStyle)
            {
                case GuideMoveStyle.Waypoints:
                {
                    var points = BuildWaypointPath(destination, request, fixedY);
                    if (points.Count <= 0)
                        return transform.DOMove(destination, duration).SetEase(Ease.Linear);
                    if (points.Count == 1)
                        return transform.DOMove(points[0], duration).SetEase(Ease.Linear);
                    var pathType = request.pathInterpolation == GuidePathInterpolation.Smooth
                        ? PathType.CatmullRom
                        : PathType.Linear;
                    return transform.DOPath(points.ToArray(), duration, pathType, PathMode.Full3D)
                        .SetEase(Ease.Linear);
                }

                default:
                    return transform.DOMove(destination, duration).SetEase(Ease.Linear);
            }
        }

        private void PlayMoveTween(Tween tween, Vector3 destination, GuideMoveRequest request, Action onArrived)
        {
            bool completed = false;
            float rotateSpeed = Mathf.Max(1f, request.rotateSpeedDeg);
            Vector3 prevPosition = transform.position;

            if (request.rotateTowardsTarget)
            {
                tween.OnUpdate(() =>
                {
                    Vector3 current = transform.position;
                    Vector3 moveDirection = current - prevPosition;
                    if (moveDirection.sqrMagnitude < 0.000001f)
                        moveDirection = destination - current;

                    moveDirection.y = 0f;
                    if (moveDirection.sqrMagnitude >= 0.0001f)
                    {
                        Quaternion targetRotation = Quaternion.LookRotation(moveDirection.normalized, Vector3.up);
                        transform.rotation = Quaternion.RotateTowards(
                            transform.rotation,
                            targetRotation,
                            rotateSpeed * Time.deltaTime);
                    }

                    prevPosition = current;
                });
            }

            tween.OnComplete(() =>
            {
                completed = true;
                m_MoveTween = null;
                var completion = m_MoveCompletion;
                m_MoveCompletion = null;
                var endRotation = ComputeTargetRotation(destination, request.rotateTowardsTarget);
                SetPose(destination, endRotation, request.rotateTowardsTarget);
                RefreshFollowTargetSnapshots();
                completion?.Invoke();
            });

            tween.OnKill(() =>
            {
                if (!completed && m_MoveTween == tween)
                    m_MoveTween = null;
            });

            m_MoveTween = tween;
            m_MoveCompletion = onArrived;
        }

        private List<Vector3> BuildWaypointPath(Vector3 destination, GuideMoveRequest request, float fixedY)
        {
            var points = new List<Vector3>(8);
            Vector3 last = transform.position;

            if (request.pathRoot != null)
            {
                for (int i = 0; i < request.pathRoot.childCount; i++)
                {
                    var child = request.pathRoot.GetChild(i);
                    if (child == null)
                        continue;

                    Vector3 p = child.position;
                    if (request.keepHeight)
                        p.y = fixedY;

                    if ((p - last).sqrMagnitude < 0.000001f)
                        continue;

                    points.Add(p);
                    last = p;
                }
            }

            if ((destination - last).sqrMagnitude >= 0.000001f)
                points.Add(destination);

            return points;
        }

        private void SnapTo(Transform target, GuideMoveRequest request)
        {
            Vector3 destination = ResolveDestination(target, request.keepHeight, transform.position.y);
            Quaternion destinationRotation = ComputeTargetRotation(destination, request.rotateTowardsTarget);
            SetPose(destination, destinationRotation, request.rotateTowardsTarget);
        }

        private void SetFollowTarget(Transform target, GuideMoveRequest request)
        {
            if (target == null)
            {
                ClearFollowTarget();
                return;
            }

            m_FollowTarget = target;
            m_FollowTargetRequest = request;
            RefreshFollowTargetSnapshots();
        }

        private void ClearFollowTarget()
        {
            m_FollowTarget = null;
            m_FollowReferenceFrame = null;
        }

        private void RefreshFollowTargetSnapshots()
        {
            if (m_FollowTarget == null)
                return;

            Transform referenceFrame = transform.parent;
            m_FollowReferenceFrame = referenceFrame;
            GetTargetPoseInFollowFrame(m_FollowTarget, referenceFrame, out m_LastFollowTargetPosition, out m_LastFollowTargetRotation);
            GetGuidePoseInFollowFrame(referenceFrame, out m_LastFollowGuidePosition, out m_LastFollowGuideRotation);
        }

        private void FollowTrackedTargetIfNeeded()
        {
            if (m_FollowTarget == null)
                return;

            if (!m_FollowTarget.gameObject.scene.IsValid())
            {
                ClearFollowTarget();
                return;
            }

            Transform referenceFrame = transform.parent;
            if (referenceFrame != m_FollowReferenceFrame)
            {
                RefreshFollowTargetSnapshots();
                return;
            }

            GetTargetPoseInFollowFrame(m_FollowTarget, referenceFrame, out Vector3 currentTargetPosition, out Quaternion currentTargetRotation);
            GetGuidePoseInFollowFrame(referenceFrame, out Vector3 currentGuidePosition, out Quaternion currentGuideRotation);
            bool targetChanged = HasPoseChanged(
                currentTargetPosition,
                currentTargetRotation,
                m_LastFollowTargetPosition,
                m_LastFollowTargetRotation);
            bool guideChanged = HasPoseChanged(
                currentGuidePosition,
                currentGuideRotation,
                m_LastFollowGuidePosition,
                m_LastFollowGuideRotation);

            if (!targetChanged)
            {
                if (guideChanged)
                    RefreshFollowTargetSnapshots();
                return;
            }

            Quaternion deltaRotation = currentTargetRotation * Quaternion.Inverse(m_LastFollowTargetRotation);
            Vector3 correctedGuidePosition = currentTargetPosition + deltaRotation * (currentGuidePosition - m_LastFollowTargetPosition);
            Quaternion correctedGuideRotation = deltaRotation * currentGuideRotation;

            bool hadActiveMoveTween = m_MoveTween != null && m_MoveTween.IsActive();
            Action pendingCompletion = m_MoveCompletion;
            if (hadActiveMoveTween)
            {
                Tween activeTween = m_MoveTween;
                m_MoveTween = null;
                if (activeTween != null && activeTween.IsActive())
                    activeTween.Kill();
            }

            SetGuidePoseInFollowFrame(referenceFrame, correctedGuidePosition, correctedGuideRotation);
            RefreshFollowTargetSnapshots();

            if (!hadActiveMoveTween)
                return;

            Vector3 destination = ResolveDestination(m_FollowTarget, m_FollowTargetRequest.keepHeight, transform.position.y);
            if (IsArrived(destination, m_FollowTargetRequest.arriveDistance))
            {
                Quaternion endRotation = ComputeTargetRotation(destination, m_FollowTargetRequest.rotateTowardsTarget);
                SetPose(destination, endRotation, m_FollowTargetRequest.rotateTowardsTarget);
                RefreshFollowTargetSnapshots();
                m_MoveCompletion = null;
                pendingCompletion?.Invoke();
                return;
            }

            Tween restartedTween = BuildMoveTween(destination, m_FollowTargetRequest, transform.position.y);
            if (restartedTween == null)
            {
                Quaternion endRotation = ComputeTargetRotation(destination, m_FollowTargetRequest.rotateTowardsTarget);
                SetPose(destination, endRotation, m_FollowTargetRequest.rotateTowardsTarget);
                RefreshFollowTargetSnapshots();
                m_MoveCompletion = null;
                pendingCompletion?.Invoke();
                return;
            }

            PlayMoveTween(restartedTween, destination, m_FollowTargetRequest, pendingCompletion);
        }

        private static void GetTargetPoseInFollowFrame(
            Transform target,
            Transform referenceFrame,
            out Vector3 position,
            out Quaternion rotation)
        {
            if (referenceFrame == null)
            {
                position = target.position;
                rotation = target.rotation;
                return;
            }

            position = referenceFrame.InverseTransformPoint(target.position);
            rotation = Quaternion.Inverse(referenceFrame.rotation) * target.rotation;
        }

        private void GetGuidePoseInFollowFrame(
            Transform referenceFrame,
            out Vector3 position,
            out Quaternion rotation)
        {
            if (referenceFrame == null)
            {
                position = transform.position;
                rotation = transform.rotation;
                return;
            }

            position = transform.localPosition;
            rotation = transform.localRotation;
        }

        private void SetGuidePoseInFollowFrame(
            Transform referenceFrame,
            Vector3 position,
            Quaternion rotation)
        {
            if (referenceFrame == null)
            {
                transform.SetPositionAndRotation(position, rotation);
                return;
            }

            transform.SetLocalPositionAndRotation(position, rotation);
        }

        private static bool HasPoseChanged(
            Vector3 position,
            Quaternion rotation,
            Vector3 previousPosition,
            Quaternion previousRotation)
        {
            return (position - previousPosition).sqrMagnitude > 0.000001f ||
                   Quaternion.Angle(rotation, previousRotation) > 0.01f;
        }

        private static Vector3 ResolveDestination(Transform target, bool keepHeight, float fixedY)
        {
            Vector3 destination = target.position;
            if (keepHeight)
                destination.y = fixedY;
            return destination;
        }

        private bool IsArrived(Vector3 destination, float arriveDistance)
        {
            float threshold = Mathf.Max(0.001f, arriveDistance);
            return (transform.position - destination).sqrMagnitude <= threshold * threshold;
        }

        private void SetPose(Vector3 position, Quaternion rotation, bool applyRotation)
        {
            transform.position = position;
            if (applyRotation)
                transform.rotation = rotation;
        }

        private Quaternion ComputeTargetRotation(Vector3 destination, bool rotateTowardsTarget)
        {
            if (!rotateTowardsTarget)
                return transform.rotation;

            Vector3 forward = destination - transform.position;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                return transform.rotation;

            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        private void OnDisable()
        {
            KillMoveTween();
            m_MoveCompletion = null;
            CancelGuideAnimationCallbacks();
            StopVoicePlayback();
            ClearFollowTarget();
        }

        private void CaptureInitialPoseIfNeeded()
        {
            if (m_InitialPoseCaptured)
                return;

            SnapshotCurrentPoseAsInitial();

            LogGuideRuntime(
                $"[GuideRuntime] capture_initial name={name} activeSelf={m_InitialActiveSelf} " +
                $"parent={GetTransformPath(m_InitialParent)} " +
                $"worldPos={FormatVector3(m_InitialWorldPosition)} localPos={FormatVector3(m_InitialLocalPosition)} " +
                $"worldScale={FormatVector3(transform.lossyScale)} localScale={FormatVector3(transform.localScale)}");
        }

        public void RebaseInitialPoseToCurrentTransform()
        {
            SnapshotCurrentPoseAsInitial();

            LogGuideRuntime(
                $"[GuideRuntime] rebase_initial name={name} activeSelf={m_InitialActiveSelf} " +
                $"parent={GetTransformPath(m_InitialParent)} " +
                $"worldPos={FormatVector3(m_InitialWorldPosition)} localPos={FormatVector3(m_InitialLocalPosition)} " +
                $"worldScale={FormatVector3(transform.lossyScale)} localScale={FormatVector3(transform.localScale)}");
        }

        private void SnapshotCurrentPoseAsInitial()
        {
            m_InitialParent = transform.parent;
            m_InitialLocalPosition = transform.localPosition;
            m_InitialLocalRotation = transform.localRotation;
            m_InitialWorldPosition = transform.position;
            m_InitialWorldRotation = transform.rotation;
            m_InitialActiveSelf = gameObject.activeSelf;
            m_InitialPoseCaptured = true;
        }

        private bool RestoreInitialTransformPose()
        {
            if (transform.parent == m_InitialParent)
            {
                transform.SetLocalPositionAndRotation(m_InitialLocalPosition, m_InitialLocalRotation);
                return false;
            }

            transform.SetPositionAndRotation(m_InitialWorldPosition, m_InitialWorldRotation);
            return true;
        }

        private void LogGuideRuntime(string message)
        {
            if (!EnableGuideRuntimeDiagnostics || string.IsNullOrEmpty(message))
                return;

            Debug.Log(message, this);
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

        private void KillMoveTween()
        {
            if (m_MoveTween != null && m_MoveTween.IsActive())
                m_MoveTween.Kill();
            m_MoveTween = null;
        }

        private void CancelMoveWithoutCompleting(string reason)
        {
            var tween = m_MoveTween;
            bool hadPendingMove = tween != null || m_MoveCompletion != null;
            if (!hadPendingMove)
                return;

            m_MoveTween = null;
            m_MoveCompletion = null;

            if (tween != null && tween.IsActive())
                tween.Kill();

            Debug.Log($"[GuideRuntime] Move cancelled: {reason}", this);
        }

        private void CompleteInterruptedMove(string reason)
        {
            var tween = m_MoveTween;
            var completion = m_MoveCompletion;

            if (tween == null && completion == null)
                return;

            m_MoveTween = null;
            m_MoveCompletion = null;

            if (tween != null && tween.IsActive())
                tween.Kill();

            if (completion != null)
            {
                Debug.Log($"[GuideRuntime] Previous move interrupted: {reason}", this);
                completion.Invoke();
            }
        }
    }

    internal sealed class GuideAnimationEventRelay : MonoBehaviour
    {
        private string _expectedEffectName;
        private Action _onComplete;

        public bool HasPendingCallback => _onComplete != null;

        public void Begin(string effectName, Action onComplete)
        {
            _expectedEffectName = effectName;
            _onComplete = onComplete;
        }

        public void Cancel()
        {
            _expectedEffectName = null;
            _onComplete = null;
        }

        public void CompletePending()
        {
            Complete(_expectedEffectName);
        }

        public void OnAnimationFinished()
        {
            CompletePending();
        }

        public void OnAnimationFinished(string effectName)
        {
            Complete(effectName);
        }

        public void OnAppearFinished()
        {
            Complete("Appear");
        }

        public void OnDisappearFinished()
        {
            Complete("Disappear");
        }

        private void Complete(string effectName)
        {
            if (_onComplete == null)
                return;

            if (!string.IsNullOrEmpty(_expectedEffectName) &&
                !string.Equals(_expectedEffectName, effectName, StringComparison.OrdinalIgnoreCase))
                return;

            var callback = _onComplete;
            _expectedEffectName = null;
            _onComplete = null;
            callback?.Invoke();
        }
    }
}
