using System;
using System.Collections.Generic;
using Autohand;
using UnityEngine;
using VFXViewer;

namespace Interaction
{
    [DisallowMultipleComponent]
    public sealed class AutoHandTouchRelay : MonoBehaviour
    {
        public event Action<AutoHandTouchRelay, Hand> TouchStarted;
        public event Action<AutoHandTouchRelay, Hand> TouchEnded;

        private HandTouchEvent m_HandTouchEvent;
        private HandTriggerAreaEvents m_TriggerAreaEvents;
        private bool m_UseCollisionTouchSource;
        private bool m_UseTriggerTouchSource;
        private readonly List<Collider> m_TargetColliders = new List<Collider>();
        private readonly List<bool> m_OriginalTriggerStates = new List<bool>();
        private bool m_CapturedColliderState;
        private bool m_HandTouchEventWasEnabled = true;
        private int m_ExperienceTouchUsers;

        public static AutoHandTouchRelay Ensure(GameObject target)
        {
            if (target == null)
                return null;

            var relay = target.GetComponent<AutoHandTouchRelay>();
            if (relay == null)
                relay = target.AddComponent<AutoHandTouchRelay>();

            relay.RefreshSources();
            relay.LogRelay("ensure");
            return relay;
        }

        private void Awake()
        {
            RefreshSources();
        }

        private void OnEnable()
        {
            RefreshSources();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            RestoreExperienceTouchModeIfNeeded();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            RestoreExperienceTouchModeIfNeeded();
        }

        public void AcquireExperienceTouchMode()
        {
            m_ExperienceTouchUsers++;
            if (m_ExperienceTouchUsers > 1)
            {
                LogRelay("acquire_experience_touch_mode_shared");
                return;
            }

            CaptureColliderStateIfNeeded();
            DetachFromGrabbableChild();
            ApplyTriggerOnlyTouchMode();
            RefreshSources();
            Subscribe();
            LogRelay("acquire_experience_touch_mode");
        }

        public void ReleaseExperienceTouchMode()
        {
            if (m_ExperienceTouchUsers <= 0)
                return;

            m_ExperienceTouchUsers--;
            if (m_ExperienceTouchUsers > 0)
            {
                LogRelay("release_experience_touch_mode_shared");
                return;
            }

            RestoreOriginalColliderState();
            RefreshSources();
            Subscribe();
            LogRelay("release_experience_touch_mode");
        }

        public void RefreshSources()
        {
            m_UseCollisionTouchSource = false;
            m_UseTriggerTouchSource = false;

            if (m_ExperienceTouchUsers > 0)
            {
                EnsureTriggerAreaEvents();
                if (m_HandTouchEvent != null)
                    m_HandTouchEvent.enabled = false;

                m_UseTriggerTouchSource = true;
                LogRelay("refresh_sources_experience_mode");
                return;
            }

            var colliders = GetComponents<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled)
                    continue;

                if (collider.isTrigger)
                    m_UseTriggerTouchSource = true;
                else
                    m_UseCollisionTouchSource = true;
            }

            if (m_UseCollisionTouchSource)
            {
                m_HandTouchEvent = GetComponent<HandTouchEvent>();
                if (m_HandTouchEvent == null)
                    m_HandTouchEvent = gameObject.AddComponent<HandTouchEvent>();

                m_HandTouchEvent.enabled = true;
                m_HandTouchEvent.oneHanded = false;
                m_HandTouchEvent.handType = HandType.both;
            }

            if (m_UseTriggerTouchSource)
            {
                EnsureTriggerAreaEvents();
                m_TriggerAreaEvents.oneHanded = false;
                m_TriggerAreaEvents.handType = HandType.both;
            }

            LogRelay("refresh_sources");
        }

        private void CaptureColliderStateIfNeeded()
        {
            if (m_CapturedColliderState)
                return;

            m_TargetColliders.Clear();
            m_OriginalTriggerStates.Clear();

            var colliders = GetComponents<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null)
                    continue;

                m_TargetColliders.Add(collider);
                m_OriginalTriggerStates.Add(collider.isTrigger);
            }

            m_CapturedColliderState = true;
        }

        private void ApplyTriggerOnlyTouchMode()
        {
            CaptureColliderStateIfNeeded();

            for (int i = 0; i < m_TargetColliders.Count; i++)
            {
                Collider collider = m_TargetColliders[i];
                if (collider != null)
                    collider.isTrigger = true;
            }

            m_HandTouchEvent = GetComponent<HandTouchEvent>();
            if (m_HandTouchEvent != null)
            {
                m_HandTouchEventWasEnabled = m_HandTouchEvent.enabled;
                m_HandTouchEvent.enabled = false;
            }

            EnsureTriggerAreaEvents();
        }

        private void RestoreOriginalColliderState()
        {
            for (int i = 0; i < m_TargetColliders.Count && i < m_OriginalTriggerStates.Count; i++)
            {
                Collider collider = m_TargetColliders[i];
                if (collider != null)
                    collider.isTrigger = m_OriginalTriggerStates[i];
            }

            if (m_HandTouchEvent != null)
                m_HandTouchEvent.enabled = m_HandTouchEventWasEnabled;
        }

        private void RestoreExperienceTouchModeIfNeeded()
        {
            if (m_ExperienceTouchUsers <= 0)
                return;

            m_ExperienceTouchUsers = 0;
            RestoreOriginalColliderState();
            RefreshSources();
        }

        private void DetachFromGrabbableChild()
        {
            GrabbableChild grabChild = GetComponent<GrabbableChild>();
            if (grabChild == null)
                return;

            if (grabChild.grabParent != null)
            {
                var colliders = GetComponents<Collider>();
                for (int i = 0; i < colliders.Length; i++)
                {
                    Collider collider = colliders[i];
                    if (collider != null)
                        grabChild.grabParent.grabColliders.Remove(collider);
                }
            }

            Destroy(grabChild);
        }

        private void EnsureTriggerAreaEvents()
        {
            m_TriggerAreaEvents = GetComponent<HandTriggerAreaEvents>();
            if (m_TriggerAreaEvents == null)
                m_TriggerAreaEvents = gameObject.AddComponent<HandTriggerAreaEvents>();

            m_TriggerAreaEvents.oneHanded = false;
            m_TriggerAreaEvents.handType = HandType.both;
        }

        private void Subscribe()
        {
            Unsubscribe();

            if (m_UseCollisionTouchSource && m_HandTouchEvent != null)
            {
                m_HandTouchEvent.HandStartTouchEvent += OnHandStartTouch;
                m_HandTouchEvent.HandStopTouchEvent += OnHandStopTouch;
            }

            if (m_UseTriggerTouchSource && m_TriggerAreaEvents != null)
            {
                m_TriggerAreaEvents.HandEnterEvent += OnHandEnter;
                m_TriggerAreaEvents.HandExitEvent += OnHandExit;
            }

            LogRelay("subscribe");
        }

        private void Unsubscribe()
        {
            if (m_HandTouchEvent != null)
            {
                m_HandTouchEvent.HandStartTouchEvent -= OnHandStartTouch;
                m_HandTouchEvent.HandStopTouchEvent -= OnHandStopTouch;
            }

            if (m_TriggerAreaEvents != null)
            {
                m_TriggerAreaEvents.HandEnterEvent -= OnHandEnter;
                m_TriggerAreaEvents.HandExitEvent -= OnHandExit;
            }

            LogRelay("unsubscribe");
        }

        private void OnHandStartTouch(Hand hand)
        {
            LogRelay("touch_started", $"source=collision, hand={DescribeHand(hand)}");
            TouchStarted?.Invoke(this, hand);
        }

        private void OnHandStopTouch(Hand hand)
        {
            LogRelay("touch_ended", $"source=collision, hand={DescribeHand(hand)}");
            TouchEnded?.Invoke(this, hand);
        }

        private void OnHandEnter(Hand hand, HandTriggerAreaEvents area)
        {
            LogRelay("touch_started", $"source=trigger, hand={DescribeHand(hand)}");
            TouchStarted?.Invoke(this, hand);
        }

        private void OnHandExit(Hand hand, HandTriggerAreaEvents area)
        {
            LogRelay("touch_ended", $"source=trigger, hand={DescribeHand(hand)}");
            TouchEnded?.Invoke(this, hand);
        }

        private void LogRelay(string stage, string extra = null)
        {
            if (!PlacementDebugFileLogger.IsLoggingEnabled)
                return;

            GetColliderStats(out int totalColliders, out int enabledColliders, out int triggerColliders);
            string suffix = string.IsNullOrWhiteSpace(extra) ? string.Empty : $", {extra}";
            PlacementDebugFileLogger.Log(
                $"[AutoHandRelay] target={gameObject.name}, stage={stage}, experienceUsers={m_ExperienceTouchUsers}, " +
                $"useCollisionSource={m_UseCollisionTouchSource}, useTriggerSource={m_UseTriggerTouchSource}, " +
                $"totalColliders={totalColliders}, enabledColliders={enabledColliders}, " +
                $"triggerColliders={triggerColliders}{suffix}");
        }

        private void GetColliderStats(out int total, out int enabled, out int triggers)
        {
            total = 0;
            enabled = 0;
            triggers = 0;

            var colliders = GetComponents<Collider>();
            if (colliders == null)
                return;

            total = colliders.Length;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null)
                    continue;

                if (collider.enabled)
                    enabled++;

                if (collider.isTrigger)
                    triggers++;
            }
        }

        private static string DescribeHand(Hand hand)
        {
            if (hand == null)
                return "<null>";

            return $"{hand.name}#{hand.GetInstanceID()}";
        }
    }
}
