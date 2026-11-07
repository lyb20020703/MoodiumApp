using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;
using Unity.XR.CoreUtils;
using Moodium.Interaction;

namespace Moodium.Flow
{
    /// <summary>
    /// Gives the Moodi model shown above a tracked sticker entry a small idle bob,
    /// looping idle animation, and alternating fingertip reactions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MoodiSelectionMascotController : MonoBehaviour
    {
        const string AnimationConfigResourcePath = "MoodiumSelection/MoodiSelectionAnimationConfig";
        const string MotionRootName = "Moodi Selection Motion Root";

        static readonly XRHandJointID[] ContactJointIds =
        {
            XRHandJointID.Wrist,
            XRHandJointID.Palm,
            XRHandJointID.ThumbTip,
            XRHandJointID.IndexTip,
            XRHandJointID.MiddleTip,
            XRHandJointID.RingTip,
            XRHandJointID.LittleTip,
            XRHandJointID.IndexMetacarpal,
            XRHandJointID.MiddleMetacarpal,
            XRHandJointID.RingMetacarpal,
            XRHandJointID.LittleMetacarpal
        };

        [SerializeField, Min(0f)] float m_BobAmplitude = .008f;
        [SerializeField, Min(0f)] float m_BobFrequency = 1.15f;
        [SerializeField, Min(.001f)] float m_FingerContactRadius = .018f;
        [SerializeField, Min(0f)] float m_ReactionCooldownSeconds = .65f;
        [SerializeField] bool m_DebugLogging;

        readonly List<Material> m_RuntimeEmissionMaterials = new();
        Animator m_Animator;
        AnimationClip m_IdleBlink;
        AnimationClip m_ShySmile;
        AnimationClip m_Wink;
        PlayableGraph m_AnimationGraph;
        AnimationClipPlayable m_ClipPlayable;
        BoxCollider m_InteractionCollider;
        XRHandSubsystem m_HandSubsystem;
        XROrigin m_XROrigin;
        Vector3 m_AuthoredLocalPosition;
        float m_IdleStartedAt;
        float m_ReactionEndsAt;
        float m_NextReactionAllowedAt;
        int m_LastReactionIndex = -1;
        bool m_PlayingReaction;
        bool m_LoggedMissingHandSubsystem;
        bool m_EmissionConfigured;

        public static int GetNextReactionIndex(int lastReactionIndex) => (lastReactionIndex + 1) % 2;

        public static Animator FindMascotAnimator(GameObject entryRoot)
        {
            if (entryRoot == null)
                return null;

            // The rigged companion owns its Animator. Never create a legacy wrapper
            // or PlayableGraph for it, including during its first appearance frame.
            if(entryRoot.GetComponentInChildren<Moodium.Characters.MoodiCompanionAnimator>(true)!=null)
                return null;

            var animators = entryRoot.GetComponentsInChildren<Animator>(true);
            foreach (var animator in animators)
                if (animator != null && animator.transform.name == "IP_Unity_3Clips")
                    return animator;
            return animators.Length > 0 ? animators[0] : null;
        }

        public static bool IsPointWithinContactRadius(Collider collider, Vector3 worldPosition, float radius)
        {
            if (collider == null)
                return false;
            var closestPoint = collider.ClosestPoint(worldPosition);
            return (closestPoint - worldPosition).sqrMagnitude <= radius * radius;
        }

        public static Transform EnsureMotionRoot(Transform animatedRoot)
        {
            if (animatedRoot == null)
                return null;
            if (animatedRoot.parent != null && animatedRoot.parent.name == MotionRootName)
                return animatedRoot.parent;

            var authoredParent = animatedRoot.parent;
            var authoredSiblingIndex = animatedRoot.GetSiblingIndex();
            var authoredPosition = animatedRoot.localPosition;
            var authoredRotation = animatedRoot.localRotation;
            var authoredScale = animatedRoot.localScale;

            var motionRoot = new GameObject(MotionRootName).transform;
            motionRoot.SetParent(authoredParent, false);
            motionRoot.SetSiblingIndex(authoredSiblingIndex);
            motionRoot.localPosition = authoredPosition;
            motionRoot.localRotation = authoredRotation;
            motionRoot.localScale = authoredScale;

            animatedRoot.SetParent(motionRoot, false);
            animatedRoot.localPosition = Vector3.zero;
            animatedRoot.localRotation = Quaternion.identity;
            animatedRoot.localScale = Vector3.one;
            return motionRoot;
        }

        public static MoodiSelectionMascotController AttachTo(GameObject entryRoot)
        {
            if (entryRoot == null)
                return null;

            // The rigged Moodi (v2) has its own animator controller; the legacy clips here would freeze it, so it
            // answers touches itself. Give it the selection-screen touch reaction instead (Happy / Wave in turn).
            var rigged = entryRoot.GetComponentsInChildren<Moodium.Characters.MoodiCompanionAnimator>(true);
            foreach (var moodi in rigged) moodi.ConfigureSelectionTouch();
            if (rigged.Length > 0) return null;

            var animator = FindMascotAnimator(entryRoot);
            if (animator == null)
            {
                Debug.LogWarning("[Moodium Selection Moodi] No Animator was found in the world-entry UI prefab.");
                return null;
            }

            var motionRoot = EnsureMotionRoot(animator.transform);
            var controller = motionRoot.GetComponent<MoodiSelectionMascotController>();
            if (controller == null)
                controller = motionRoot.gameObject.AddComponent<MoodiSelectionMascotController>();
            controller.Configure(animator, Resources.Load<MoodiSelectionAnimationConfig>(AnimationConfigResourcePath));
            return controller;
        }

        void Configure(Animator animator, MoodiSelectionAnimationConfig config)
        {
            m_Animator = animator;
            if (config == null)
            {
                Debug.LogWarning($"[Moodium Selection Moodi] Missing animation config in Resources/{AnimationConfigResourcePath}.");
                return;
            }

            m_IdleBlink = config.IdleBlink;
            m_ShySmile = config.ShySmile;
            m_Wink = config.Wink;
            m_AuthoredLocalPosition = transform.localPosition;
            EnsureInteractionCollider();
            ConfigureSubtleEmission();
            PlayIdle();
            LogDebug($"Configured. Idle={m_IdleBlink?.name}, Shy={m_ShySmile?.name}, Wink={m_Wink?.name}, Collider={m_InteractionCollider != null}.");
        }

        void OnEnable()
        {
            m_AuthoredLocalPosition = transform.localPosition;
            TrySubscribeToHands();
            if (m_Animator != null && m_IdleBlink != null && !m_AnimationGraph.IsValid())
                PlayIdle();
        }

        void Update()
        {
            if (m_HandSubsystem == null)
                TrySubscribeToHands();

            transform.localPosition = m_AuthoredLocalPosition + Vector3.up *
                (Mathf.Sin(Time.unscaledTime * m_BobFrequency * Mathf.PI * 2f) * m_BobAmplitude);

            if (m_PlayingReaction && Time.unscaledTime >= m_ReactionEndsAt)
                PlayIdle();
            else if (!m_PlayingReaction && m_IdleBlink != null &&
                     Time.unscaledTime - m_IdleStartedAt >= m_IdleBlink.length)
                RestartIdleLoop();
        }

        void OnDisable()
        {
            if (m_HandSubsystem != null)
                m_HandSubsystem.updatedHands -= OnHandsUpdated;
            m_HandSubsystem = null;
            StopAnimationGraph();
            m_PlayingReaction = false;
        }

        void OnDestroy()
        {
            StopAnimationGraph();
            foreach (var material in m_RuntimeEmissionMaterials)
                if (material != null)
                    Destroy(material);
            m_RuntimeEmissionMaterials.Clear();
        }

        void TrySubscribeToHands()
        {
            var subsystem = XRGeneralSettings.Instance?.Manager?.activeLoader
                ?.GetLoadedSubsystem<XRHandSubsystem>();
            if (subsystem == null)
            {
                if (!m_LoggedMissingHandSubsystem)
                {
                    LogDebug("XRHandSubsystem is not available yet.");
                    m_LoggedMissingHandSubsystem = true;
                }
                return;
            }
            if (subsystem == m_HandSubsystem)
                return;

            if (m_HandSubsystem != null)
                m_HandSubsystem.updatedHands -= OnHandsUpdated;
            m_HandSubsystem = subsystem;
            m_HandSubsystem.updatedHands += OnHandsUpdated;
            m_LoggedMissingHandSubsystem = false;
            LogDebug("Subscribed to XR hand updates.");
        }

        void OnHandsUpdated(XRHandSubsystem subsystem, XRHandSubsystem.UpdateSuccessFlags updateSuccessFlags,
            XRHandSubsystem.UpdateType updateType)
        {
            if (updateType != XRHandSubsystem.UpdateType.Dynamic ||
                Time.unscaledTime < m_NextReactionAllowedAt || m_InteractionCollider == null)
                return;

            if (HandTouchesMoodi(subsystem.leftHand) || HandTouchesMoodi(subsystem.rightHand))
                TriggerNextReaction();
        }

        bool HandTouchesMoodi(XRHand hand)
        {
            if (!hand.isTracked)
                return false;

            foreach (var jointId in ContactJointIds)
            {
                var joint = hand.GetJoint(jointId);
                if (!joint.TryGetPose(out var trackingPose))
                    continue;
                if (m_XROrigin == null)
                    m_XROrigin = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
                var origin = m_XROrigin != null ? m_XROrigin.Origin.transform : null;
                var worldPosition = PalmPressGestureController.TransformTrackingPose(trackingPose, origin).position;
                if (IsPointWithinContactRadius(m_InteractionCollider, worldPosition, m_FingerContactRadius))
                {
                    LogDebug($"Contact detected from {jointId} at {worldPosition}.");
                    return true;
                }
            }
            return false;
        }

        void TriggerNextReaction()
        {
            m_LastReactionIndex = GetNextReactionIndex(m_LastReactionIndex);
            var reaction = m_LastReactionIndex == 0 ? m_ShySmile : m_Wink;
            if (reaction == null)
                return;

            PlayClip(reaction);
            m_PlayingReaction = true;
            m_ReactionEndsAt = Time.unscaledTime + reaction.length;
            m_NextReactionAllowedAt = m_ReactionEndsAt + m_ReactionCooldownSeconds;
            LogDebug($"Playing reaction '{reaction.name}'.");
        }

        void PlayIdle()
        {
            if (m_IdleBlink == null)
                return;
            PlayClip(m_IdleBlink);
            m_PlayingReaction = false;
            m_IdleStartedAt = Time.unscaledTime;
        }

        void RestartIdleLoop()
        {
            if (m_ClipPlayable.IsValid())
                m_ClipPlayable.SetTime(0d);
            m_IdleStartedAt = Time.unscaledTime;
        }

        void PlayClip(AnimationClip clip)
        {
            StopAnimationGraph();
            if (m_Animator == null || clip == null)
                return;
            m_Animator.enabled = true;
            m_AnimationGraph = PlayableGraph.Create($"Moodium_Selection_{clip.name}");
            m_AnimationGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            m_ClipPlayable = AnimationClipPlayable.Create(m_AnimationGraph, clip);
            m_ClipPlayable.SetApplyFootIK(false);
            var output = AnimationPlayableOutput.Create(m_AnimationGraph, "Moodi Selection Animation", m_Animator);
            output.SetSourcePlayable(m_ClipPlayable);
            m_AnimationGraph.Play();
        }

        void StopAnimationGraph()
        {
            if (m_AnimationGraph.IsValid())
                m_AnimationGraph.Destroy();
        }

        void EnsureInteractionCollider()
        {
            if (m_InteractionCollider != null)
                return;
            var renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return;
            var worldBounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
                worldBounds.Encapsulate(renderers[index].bounds);

            m_InteractionCollider = gameObject.AddComponent<BoxCollider>();
            m_InteractionCollider.isTrigger = true;
            m_InteractionCollider.center = transform.InverseTransformPoint(worldBounds.center);
            var localSize = transform.InverseTransformVector(worldBounds.size);
            m_InteractionCollider.size = new Vector3(
                Mathf.Max(.01f, Mathf.Abs(localSize.x)),
                Mathf.Max(.01f, Mathf.Abs(localSize.y)),
                Mathf.Max(.01f, Mathf.Abs(localSize.z)));
        }

        void ConfigureSubtleEmission()
        {
            if (m_EmissionConfigured)
                return;
            m_EmissionConfigured = true;

            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                var sourceMaterials = renderer.sharedMaterials;
                var runtimeMaterials = new Material[sourceMaterials.Length];
                for (var index = 0; index < sourceMaterials.Length; index++)
                {
                    var source = sourceMaterials[index];
                    if (source == null)
                        continue;

                    var runtime = new Material(source)
                    {
                        name = source.name + " (Selection Moodi Emission)"
                    };
                    var baseColor = runtime.HasProperty("_BaseColor")
                        ? runtime.GetColor("_BaseColor")
                        : runtime.HasProperty("_Color") ? runtime.GetColor("_Color") : Color.white;
                    var emission = Color.Lerp(Color.white, baseColor, .35f) * .12f;
                    if (runtime.HasProperty("_EmissionColor"))
                    {
                        runtime.EnableKeyword("_EMISSION");
                        runtime.SetColor("_EmissionColor", emission);
                    }
                    if (runtime.HasProperty("_EmissiveColor"))
                        runtime.SetColor("_EmissiveColor", emission);
                    runtimeMaterials[index] = runtime;
                    m_RuntimeEmissionMaterials.Add(runtime);
                }
                renderer.sharedMaterials = runtimeMaterials;
            }
        }

        void LogDebug(string message)
        {
            if (m_DebugLogging)
                Debug.Log($"[Moodium Selection Moodi] {message}", this);
        }
    }
}
