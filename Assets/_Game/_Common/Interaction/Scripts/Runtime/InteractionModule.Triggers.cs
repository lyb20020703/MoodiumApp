using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Interaction
{
    public partial class InteractionModule
    {
        private void ApplyTriggerConfig(ITrigger trigger, bool isStartSlot, bool isEndSlot)
        {
            if (trigger is TimeTrigger tt)
            {
                float startD = isStartSlot ? startTimeDelay : 0f;
                float endD = isEndSlot ? endTimeDelay : 0f;
                tt.SetConfig(startD, endD, false, isStartSlot, isEndSlot);
                // 列表模式 + 开始/结束都为时间：禁用 TimeTrigger 自带的「开始→结束」路径，只由模块在开始效果完成后启动结束计时
                bool listMode = (appearEffectEntries != null && appearEffectEntries.Count > 0 && disappearEffectEntries != null && disappearEffectEntries.Count > 0)
                                && (_contentHandle == null || _appearEffect == null || _disappearEffect == null);
                if (listMode && startTriggerType == TriggerType.Time && endTriggerType == TriggerType.Time && isEndSlot)
                {
                    tt.DisableSelfTimedEnd();
                }
                // 触发器参数日志默认不输出
            }
            else if (trigger is PositionTrigger pt)
                pt.SetConfig(enterRegionCollider, leaveRegionCollider, startTriggerType == TriggerType.Leave, endTriggerType == TriggerType.Leave);
            else if (trigger is TouchTrigger tct)
            {
                bool useGuideTouch = trigger is GuideTouchTrigger || UsesGuideTouchTrigger(isStartSlot, isEndSlot);
                bool useHover = ShouldUseHoverTouchInCurrentEnvironment();
                Action touchEnterCallback = useGuideTouch ? (Action)PlayGuideTouchAnimationIfNeeded : null;
                var resolvedTouchInteractable = useGuideTouch
                    ? ResolveGuideTouchInteractable()
                    : ResolveLocalTouchInteractable(touchInteractable);
                if (resolvedTouchInteractable == null)
                {
                    if (useGuideTouch)
                    {
                        string guideName = string.IsNullOrWhiteSpace(globalGuideObjectName) ? "Guider" : globalGuideObjectName;
                        LogWarning($"使用触摸导游触发器但未找到导游交互体：{guideName}（需要 XRBaseInteractable）。");
                    }
                    else if (touchInteractable == null)
                    {
                        LogWarning("使用触摸触发器但未绑定 XRSimpleInteractable，点击将无反应。");
                    }
                    else
                    {
                        LogWarning($"触摸交互体必须属于当前模块节点：{touchInteractable.name} 已被忽略。");
                    }
                }
                tct.SetConfig(resolvedTouchInteractable, isStartSlot, isEndSlot, useHover, touchEnterCallback);
            }
            else if (trigger is LongPressTrigger lpt)
                lpt.SetConfig(startLongPressInteractable, endLongPressInteractable, startLongPressHoldSec, endLongPressHoldSec, isStartSlot, isEndSlot, ShouldUseHoverTouchInCurrentEnvironment());
        }

        private bool ShouldUseHoverTouchInCurrentEnvironment()
        {
            return !Application.isEditor;
        }

        private bool UsesGuideTouchTrigger(bool isStartSlot, bool isEndSlot)
        {
            bool startUsesGuide = isStartSlot && startTriggerType == TriggerType.TouchGuide;
            bool endUsesGuide = isEndSlot && endTriggerType == TriggerType.TouchGuide;
            return startUsesGuide || endUsesGuide;
        }

        private XRBaseInteractable ResolveGuideTouchInteractable()
        {
            var interactable = FindInteractableOnGuide(globalGuide != null ? globalGuide.transform : null);
            if (interactable != null)
                return interactable;

            string guideName = string.IsNullOrWhiteSpace(globalGuideObjectName) ? "Guider" : globalGuideObjectName;
            var scene = gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded)
                scene = SceneManager.GetActiveScene();

            if (!scene.IsValid() || !scene.isLoaded)
                return null;

            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                var root = roots[i];
                if (root == null)
                    continue;

                var guideRoot = FindChildByNameRecursive(root.transform, guideName);
                interactable = FindInteractableOnGuide(guideRoot);
                if (interactable != null)
                    return interactable;
            }

            return null;
        }

        private XRBaseInteractable ResolveLocalTouchInteractable(XRBaseInteractable interactable)
        {
            if (interactable == null)
                return null;

            var touchTransform = interactable.transform;
            if (touchTransform == null)
                return null;

            if (touchTransform == transform || touchTransform.IsChildOf(transform))
                return interactable;

            return null;
        }

        private static XRBaseInteractable FindInteractableOnGuide(Transform guideRoot)
        {
            if (guideRoot == null)
                return null;

            var self = guideRoot.GetComponent<XRBaseInteractable>();
            if (self != null)
                return self;

            return guideRoot.GetComponentInChildren<XRBaseInteractable>(true);
        }

        private ITrigger GetOrAddTrigger(TriggerType type)
        {
            switch (type)
            {
                case TriggerType.Time: return GetOrAddComponent<TimeTrigger>();
                case TriggerType.Enter:
                case TriggerType.Leave:
                    if (GetComponent<Collider>() == null) gameObject.AddComponent<BoxCollider>().isTrigger = true;
                    return GetOrAddComponent<PositionTrigger>();
                case TriggerType.Touch: return GetOrAddComponent<TouchTrigger>();
                case TriggerType.LongPress: return GetOrAddComponent<LongPressTrigger>();
                case TriggerType.TouchGuide: return GetOrAddComponent<GuideTouchTrigger>();
                default: return null;
            }
        }
    }
}
