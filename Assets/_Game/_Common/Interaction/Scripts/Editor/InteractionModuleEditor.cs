using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Interaction;
using VFXViewer;

namespace Interaction.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(InteractionModule))]
    public class InteractionModuleEditor : UnityEditor.Editor
    {
        private static readonly Dictionary<int, ContentType> s_ContentTypeCache = new Dictionary<int, ContentType>();
        private static readonly List<StageModuleOption> s_CachedStageModuleOptions = new List<StageModuleOption>();
        private static readonly HashSet<string> s_CachedStageModuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool s_EditorCachesInitialized;
        private static bool s_StageModuleOptionsDirty = true;
        private static GUIStyle s_RuntimeStatusLabelStyle;

        private readonly struct StageModuleOption
        {
            public StageModuleOption(string id, string label)
            {
                Id = id ?? string.Empty;
                Label = string.IsNullOrWhiteSpace(label) ? Id : label;
            }

            public string Id { get; }
            public string Label { get; }
        }

        private static TriggerType ReadTriggerType(SerializedProperty triggerTypeProp)
        {
            return triggerTypeProp == null ? TriggerType.Time : (TriggerType)triggerTypeProp.intValue;
        }

        static InteractionModuleEditor()
        {
            EnsureEditorCachesInitialized();
        }

        private SerializedProperty _allowRestartAfterEnd;
        private SerializedProperty _startTriggerType;
        private SerializedProperty _endTriggerType;
        private SerializedProperty _initialEffectEntries;
        private SerializedProperty _appearEffectEntries;
        private SerializedProperty _disappearEffectEntries;
        private SerializedProperty _dragReleaseType;

        private SerializedProperty _startTimeDelay;
        private SerializedProperty _enterRegionCollider;
        private SerializedProperty _touchInteractable;
        private SerializedProperty _startLongPressInteractable;
        private SerializedProperty _startLongPressHoldSec;
        private SerializedProperty _endTimeDelay;
        private SerializedProperty _leaveRegionCollider;
        private SerializedProperty _endLongPressInteractable;
        private SerializedProperty _endLongPressHoldSec;
        private SerializedProperty _guideTouchPlayAnimationOnTouch;
        private SerializedProperty _guideTouchAnimationStateOrTrigger;

        private SerializedProperty _dragReturnHomeRecordOnRelease;
        private SerializedProperty _dragSnapToGrid;
        private SerializedProperty _dragSnapGridSize;
        private SerializedProperty _dragSnapZeroVelocity;
        private SerializedProperty _dragPhysicsAddRigidbodyIfMissing;
        private SerializedProperty _dragPhysicsUseGravity;

        private ReorderableList _initialList;
        private ReorderableList _appearList;
        private ReorderableList _disappearList;

        private static void EnsureEditorCachesInitialized()
        {
            if (s_EditorCachesInitialized)
                return;

            s_EditorCachesInitialized = true;
            EditorApplication.hierarchyChanged += InvalidateEditorCaches;
            EditorApplication.projectChanged += InvalidateEditorCaches;
        }

        private static void InvalidateEditorCaches()
        {
            s_ContentTypeCache.Clear();
            s_StageModuleOptionsDirty = true;
            s_CachedStageModuleOptions.Clear();
            s_CachedStageModuleIds.Clear();
        }

        private static ContentType GetCachedContentType(GameObject root)
        {
            if (root == null)
                return ContentType.Model;

            int instanceId = root.GetInstanceID();
            if (s_ContentTypeCache.TryGetValue(instanceId, out var cachedType))
                return cachedType;

            cachedType = ContentHandleFactory.DetectType(root);
            s_ContentTypeCache[instanceId] = cachedType;
            return cachedType;
        }

        private void OnEnable()
        {
            EnsureEditorCachesInitialized();
            _allowRestartAfterEnd = serializedObject.FindProperty("allowRestartAfterEnd");
            _startTriggerType = serializedObject.FindProperty("startTriggerType");
            _endTriggerType = serializedObject.FindProperty("endTriggerType");
            _initialEffectEntries = serializedObject.FindProperty("initialEffectEntries");
            _appearEffectEntries = serializedObject.FindProperty("appearEffectEntries");
            _disappearEffectEntries = serializedObject.FindProperty("disappearEffectEntries");
            _dragReleaseType = serializedObject.FindProperty("dragReleaseType");

            _startTimeDelay = serializedObject.FindProperty("startTimeDelay");
            _enterRegionCollider = serializedObject.FindProperty("enterRegionCollider");
            _touchInteractable = serializedObject.FindProperty("touchInteractable");
            _startLongPressInteractable = serializedObject.FindProperty("startLongPressInteractable");
            _startLongPressHoldSec = serializedObject.FindProperty("startLongPressHoldSec");
            _endTimeDelay = serializedObject.FindProperty("endTimeDelay");
            _leaveRegionCollider = serializedObject.FindProperty("leaveRegionCollider");
            _endLongPressInteractable = serializedObject.FindProperty("endLongPressInteractable");
            _endLongPressHoldSec = serializedObject.FindProperty("endLongPressHoldSec");
            _guideTouchPlayAnimationOnTouch = serializedObject.FindProperty("guideTouchPlayAnimationOnTouch");
            _guideTouchAnimationStateOrTrigger = serializedObject.FindProperty("guideTouchAnimationStateOrTrigger");

            _dragReturnHomeRecordOnRelease = serializedObject.FindProperty("dragReturnHomeRecordOnRelease");
            _dragSnapToGrid = serializedObject.FindProperty("dragSnapToGrid");
            _dragSnapGridSize = serializedObject.FindProperty("dragSnapGridSize");
            _dragSnapZeroVelocity = serializedObject.FindProperty("dragSnapZeroVelocity");
            _dragPhysicsAddRigidbodyIfMissing = serializedObject.FindProperty("dragPhysicsAddRigidbodyIfMissing");
            _dragPhysicsUseGravity = serializedObject.FindProperty("dragPhysicsUseGravity");

            _initialList = new ReorderableList(serializedObject, _initialEffectEntries, true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "初始效果列表"),
                elementHeightCallback = idx => GetAppearElementHeight(_initialEffectEntries.GetArrayElementAtIndex(idx)),
                drawElementCallback = (rect, index, active, focused) => DrawInitialElement(rect, index)
            };
            _appearList = new ReorderableList(serializedObject, _appearEffectEntries, true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "开始效果列表"),
                elementHeightCallback = idx => GetAppearElementHeight(_appearEffectEntries.GetArrayElementAtIndex(idx)),
                drawElementCallback = (rect, index, active, focused) => DrawAppearElement(rect, index)
            };
            _disappearList = new ReorderableList(serializedObject, _disappearEffectEntries, true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "结束效果列表"),
                elementHeightCallback = idx => GetDisappearElementHeight(_disappearEffectEntries.GetArrayElementAtIndex(idx)),
                drawElementCallback = (rect, index, active, focused) => DrawDisappearElement(rect, index)
            };
        }

        private static float LineHeight => EditorGUIUtility.singleLineHeight + 2f;

        private static EffectTargetType ReadEffectTargetType(SerializedProperty elem)
        {
            var targetTypeProp = elem?.FindPropertyRelative("targetType");
            return targetTypeProp == null ? EffectTargetType.Content : (EffectTargetType)targetTypeProp.enumValueIndex;
        }

        private static bool GuideActionNeedsMove(GuideListActionType action)
        {
            return action == GuideListActionType.MoveToWaypoint || action == GuideListActionType.ShowAndMoveToWaypoint;
        }

        private static GuideMoveStyle ReadGuideMoveStyle(SerializedProperty elem)
        {
            var styleProp = elem?.FindPropertyRelative("guideMoveStyle");
            return styleProp == null ? GuideMoveStyle.Straight : (GuideMoveStyle)styleProp.enumValueIndex;
        }

        private static bool IsStageTarget(SerializedProperty elem)
        {
            return ReadEffectTargetType(elem) == EffectTargetType.StageModule;
        }

        private static bool IsSystemTarget(SerializedProperty elem)
        {
            return ReadEffectTargetType(elem) == EffectTargetType.System;
        }

        private static StageActionType ReadStageActionType(SerializedProperty actionProp, StageActionType fallback)
        {
            return actionProp == null ? fallback : (StageActionType)actionProp.enumValueIndex;
        }

        private static SystemActionType ReadSystemActionType(SerializedProperty actionProp, SystemActionType fallback)
        {
            if (actionProp == null)
                return fallback;

            var action = (SystemActionType)actionProp.enumValueIndex;
            return Enum.IsDefined(typeof(SystemActionType), action) ? action : fallback;
        }

        private static float GetGuideMoveParamLineCount(SerializedProperty elem)
        {
            float lines = 7f; // waypoint, moveStyle, immediate+keepHeight, rotateTowards, moveDuration, arriveDistance, rotateSpeed
            var moveStyle = ReadGuideMoveStyle(elem);
            if (moveStyle == GuideMoveStyle.Waypoints)
                lines += 2f; // pathRoot + interpolation
            return lines;
        }

        private static float GetGuideVoiceParamLineCount(SerializedProperty elem, GuideListActionType action)
        {
            if (action != GuideListActionType.PlayVoice)
                return 0f;

            float lines = 3f; // clip, volume, override toggle
            if (!ReadBoolProperty(elem, "guideVoiceOverrideAnimationSettings"))
                return lines;

            lines += 1f; // play animation toggle
            if (ReadBoolProperty(elem, "guideVoicePlayAnimation"))
                lines += 2f; // talk + idle states
            return lines;
        }

        private static float GetGuideAnimationParamLineCount(GuideListActionType action)
        {
            if (action == GuideListActionType.Show || action == GuideListActionType.Hide)
                return 1f;

            return action == GuideListActionType.PlayAnimation ? 2f : 0f;
        }

        private static float GetSystemParamLineCount(SystemActionType action)
        {
            switch (action)
            {
                case SystemActionType.PlayBgm:
                    return 3f;
                case SystemActionType.StopBgm:
                    return 1f;
                case SystemActionType.PlaySfx:
                    return 2f;
                default:
                    return 0f;
            }
        }

        private static float GetAppearElementHeight(SerializedProperty elem)
        {
            if (elem == null) return LineHeight;
            if (!elem.isExpanded) return 2f * LineHeight;

            var targetType = ReadEffectTargetType(elem);
            if (targetType == EffectTargetType.Guide)
            {
                float guideLines = 3f; // header+delay+action
                var guideActionProp = elem.FindPropertyRelative("guideAction");
                var guideAction = guideActionProp == null
                    ? GuideListActionType.ShowAndMoveToWaypoint
                    : (GuideListActionType)guideActionProp.enumValueIndex;
                if (GuideActionNeedsMove(guideAction))
                    guideLines += GetGuideMoveParamLineCount(elem);
                guideLines += GetGuideAnimationParamLineCount(guideAction);
                guideLines += GetGuideVoiceParamLineCount(elem, guideAction);
                return guideLines * LineHeight;
            }

            if (targetType == EffectTargetType.System)
            {
                var systemActionProp = elem.FindPropertyRelative("systemActionType");
                var systemAction = ReadSystemActionType(systemActionProp, SystemActionType.PlayBgm);
                float systemLines = 3f;
                systemLines += GetSystemParamLineCount(systemAction);
                return systemLines * LineHeight;
            }

            if (targetType == EffectTargetType.StageModule)
            {
                var stageActionProp = elem.FindPropertyRelative("stageActionType");
                var stageAction = ReadStageActionType(stageActionProp, StageActionType.Show);
                float stageLines = 3f; // targetRef, delay+目标, 类型
                if (StageActionNeedsDuration(stageAction))
                    stageLines += 1f + GetStageSdfConfigLineCount(elem);
                return stageLines * LineHeight;
            }

            var typeProp = elem.FindPropertyRelative("effectType");
            var type = typeProp != null ? (AppearEffectType)typeProp.enumValueIndex : AppearEffectType.Show;
            var rootProp = elem.FindPropertyRelative("contentRoot");
            var contentType = IsStageTarget(elem)
                ? ContentType.Model
                : GetCachedContentType(rootProp?.objectReferenceValue as GameObject);
            float lines = 4f; // targetRef, delay+目标, initialHidden, type
            if (AppearTypeNeedsDuration(type, contentType)) lines += 1f;
            switch (type)
            {
                case AppearEffectType.Hide: break;
                case AppearEffectType.Transparency: lines += 2; break;
                case AppearEffectType.Shader: lines += 3; break;
                case AppearEffectType.Transform: lines += 8; break;
                case AppearEffectType.KeyframeAnimation: lines += 1; break;
            }
            return lines * LineHeight;
        }

        private static float GetDisappearElementHeight(SerializedProperty elem)
        {
            if (elem == null) return LineHeight;
            if (!elem.isExpanded) return 2f * LineHeight;

            var targetType = ReadEffectTargetType(elem);
            if (targetType == EffectTargetType.Guide)
            {
                float guideLines = 3f; // header+delay+action
                var guideActionProp = elem.FindPropertyRelative("guideAction");
                var guideAction = guideActionProp == null
                    ? GuideListActionType.Hide
                    : (GuideListActionType)guideActionProp.enumValueIndex;
                if (GuideActionNeedsMove(guideAction))
                    guideLines += GetGuideMoveParamLineCount(elem);
                guideLines += GetGuideAnimationParamLineCount(guideAction);
                guideLines += GetGuideVoiceParamLineCount(elem, guideAction);
                return guideLines * LineHeight;
            }

            if (targetType == EffectTargetType.System)
            {
                var systemActionProp = elem.FindPropertyRelative("systemActionType");
                var systemAction = ReadSystemActionType(systemActionProp, SystemActionType.StopBgm);
                float systemLines = 3f;
                systemLines += GetSystemParamLineCount(systemAction);
                return systemLines * LineHeight;
            }

            if (targetType == EffectTargetType.StageModule)
            {
                var stageActionProp = elem.FindPropertyRelative("stageActionType");
                var stageAction = ReadStageActionType(stageActionProp, StageActionType.Hide);
                float stageLines = 3f; // targetRef, delay+目标, 类型
                if (StageActionNeedsDuration(stageAction))
                    stageLines += 1f + GetStageSdfConfigLineCount(elem);
                return stageLines * LineHeight;
            }

            var typeProp = elem.FindPropertyRelative("effectType");
            var type = typeProp != null ? (DisappearEffectType)typeProp.enumValueIndex : DisappearEffectType.Hide;
            var rootProp = elem.FindPropertyRelative("contentRoot");
            var contentType = IsStageTarget(elem)
                ? ContentType.Model
                : GetCachedContentType(rootProp?.objectReferenceValue as GameObject);
            float lines = 3f; // targetRef, delay+目标, type
            if (DisappearTypeNeedsDuration(type, contentType)) lines += 1f;
            switch (type)
            {
                case DisappearEffectType.Show: break;
                case DisappearEffectType.Transparency: lines += 1; break;
                case DisappearEffectType.Shader: lines += 3; break;
                case DisappearEffectType.Transform: lines += 8; break;
                case DisappearEffectType.KeyframeAnimation: lines += 1; break;
            }
            return lines * LineHeight;
        }

        private void DrawInitialElement(Rect rect, int index)
        {
            DrawAppearElementInternal(_initialEffectEntries, rect, index);
        }

        private void DrawAppearElement(Rect rect, int index)
        {
            DrawAppearElementInternal(_appearEffectEntries, rect, index);
        }

        private void DrawAppearElementInternal(SerializedProperty listProp, Rect rect, int index)
        {
            var elem = listProp.GetArrayElementAtIndex(index);
            var targetTypeProp = elem.FindPropertyRelative("targetType");
            var contentRoot = elem.FindPropertyRelative("contentRoot");
            var stageModuleId = elem.FindPropertyRelative("stageModuleId");
            var stageAction = elem.FindPropertyRelative("stageActionType");
            var delay = elem.FindPropertyRelative("delay");
            var initialHidden = elem.FindPropertyRelative("initialHidden");
            var duration = elem.FindPropertyRelative("duration");
            var effectType = elem.FindPropertyRelative("effectType");
            var guideAction = elem.FindPropertyRelative("guideAction");
            var targetType = ReadEffectTargetType(elem);

            float y = rect.y;
            rect.height = EditorGUIUtility.singleLineHeight;

            elem.isExpanded = EditorGUI.Foldout(new Rect(rect.x, y, 20, rect.height), elem.isExpanded, GUIContent.none);
            var contentRect = new Rect(rect.x + 18, y, rect.width - 18, rect.height);
            if (targetType == EffectTargetType.Guide)
                EditorGUI.LabelField(contentRect, "导游动作");
            else if (targetType == EffectTargetType.System)
                EditorGUI.LabelField(contentRect, "系统动作");
            else if (targetType == EffectTargetType.StageModule)
                DrawStageModuleIdField(contentRect, stageModuleId);
            else
            {
                var fieldRect = DrawContentTypeBadge(contentRect, contentRoot);
                EditorGUI.PropertyField(fieldRect, contentRoot, GUIContent.none);
            }
            y += LineHeight;
            DrawDelayAndTargetRow(rect, y, delay, targetTypeProp);
            y += LineHeight;

            if (!elem.isExpanded) return;

            if (ReadEffectTargetType(elem) == EffectTargetType.Guide)
            {
                DrawGuideActionPopup(new Rect(rect.x, y, rect.width, rect.height), guideAction, "导游动作");
                y += LineHeight;
                DrawGuideMoveParams(rect, ref y, elem);
                DrawGuideAnimationParams(rect, ref y, elem);
                DrawGuideVoiceParams(rect, ref y, elem);
                return;
            }

            if (ReadEffectTargetType(elem) == EffectTargetType.System)
            {
                DrawSystemActionPopup(new Rect(rect.x, y, rect.width, rect.height), elem.FindPropertyRelative("systemActionType"), "类型");
                y += LineHeight;
                DrawSystemActionParams(rect, ref y, elem, SystemActionType.PlayBgm);
                return;
            }

            if (ReadEffectTargetType(elem) == EffectTargetType.StageModule)
            {
                DrawStageActionPopup(new Rect(rect.x, y, rect.width, rect.height), stageAction, "类型");
                y += LineHeight;
                var action = ReadStageActionType(stageAction, StageActionType.Show);
                if (StageActionNeedsDuration(action))
                {
                    EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, rect.height), duration, new GUIContent("时长(秒)", "用于等待 SDF 特效播放完成，0 则使用默认 0.5 秒"));
                    y += LineHeight;
                    DrawStageSdfConfigFields(rect, ref y, elem);
                }
                return;
            }

            EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, rect.height), initialHidden, new GUIContent("初始隐藏"));
            y += LineHeight;
            DrawAppearTypePopup(new Rect(rect.x, y, rect.width, rect.height), contentRoot, effectType, targetType == EffectTargetType.StageModule);
            y += LineHeight;
            var type = (AppearEffectType)effectType.enumValueIndex;
            var contentType = targetType == EffectTargetType.StageModule
                ? ContentType.Model
                : GetCachedContentType(contentRoot?.objectReferenceValue as GameObject);
            if (AppearTypeNeedsDuration(type, contentType))
            {
                var tip = (type == AppearEffectType.Show && contentType == ContentType.VFX)
                    ? "视为播放完成的等待时长，0 则使用默认 0.5 秒"
                    : "0 则使用默认 0.5 秒";
                EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, rect.height), duration, new GUIContent("时长(秒)", tip));
                y += LineHeight;
            }
            DrawAppearEntryParams(rect, ref y, elem, type);
        }

        private static bool AppearTypeNeedsDuration(AppearEffectType type, ContentType contentType)
        {
            if (type == AppearEffectType.Hide)
                return false;
            if (type != AppearEffectType.Show && type != AppearEffectType.KeyframeAnimation)
                return true;
            // 仅 VFX 使用「显示」时也需要时长，用于控制何时视为播放完成
            return type == AppearEffectType.Show && contentType == ContentType.VFX;
        }

        private static bool StageActionNeedsDuration(StageActionType actionType)
        {
            return actionType == StageActionType.PlaySdfEffect;
        }

        private static float GetStageSdfConfigLineCount(SerializedProperty elem)
        {
            if (elem == null)
                return 0f;

            float lines = 5f; // align target + optional follow + texture + 3 offset toggles
            bool hasTransformSource = HasStageSdfTransformSource(elem);
            if (hasTransformSource)
                lines += 1f; // follow toggle

            if (ReadBoolProperty(elem, "stageSdfOverrideLocalPosition"))
                lines += 1f;
            if (ReadBoolProperty(elem, "stageSdfOverrideLocalRotation"))
                lines += 1f;
            if (ReadBoolProperty(elem, "stageSdfOverrideLocalScale"))
                lines += 1f;
            return lines;
        }

        private static bool HasStageSdfTransformSource(SerializedProperty elem)
        {
            var prop = elem?.FindPropertyRelative("stageSdfTransformSource");
            return prop != null && prop.objectReferenceValue != null;
        }

        private static bool ReadBoolProperty(SerializedProperty elem, string propertyName)
        {
            var prop = elem?.FindPropertyRelative(propertyName);
            return prop != null && prop.boolValue;
        }

        private static void DrawStageSdfConfigFields(Rect rect, ref float y, SerializedProperty elem)
        {
            if (elem == null)
                return;

            var transformSourceProp = elem.FindPropertyRelative("stageSdfTransformSource");
            if (transformSourceProp != null)
            {
                var lineRect = new Rect(rect.x, y, rect.width, rect.height);
                EditorGUI.PropertyField(lineRect, transformSourceProp, new GUIContent("目标点", "播放时先把 SDF_LineModel 的世界位置、旋转和缩放对齐到这个 Transform；若启用了位置、旋转或缩放偏移，会在对齐结果基础上叠加偏移"));
                y += LineHeight;
            }

            bool hasTransformSource = transformSourceProp != null && transformSourceProp.objectReferenceValue != null;
            if (hasTransformSource)
            {
                var followProp = elem.FindPropertyRelative("stageSdfFollowTransformSourceDuringPlayback");
                if (followProp != null)
                {
                    var lineRect = new Rect(rect.x, y, rect.width, rect.height);
                    EditorGUI.PropertyField(lineRect, followProp, new GUIContent("播放期间持续跟随", "勾选后，SDF 特效播放期间会持续跟随目标点的位置、朝向和缩放。适用于 FaceMainCamera 等运行时持续变化的对象"));
                    y += LineHeight;
                }
            }

            var textureProp = elem.FindPropertyRelative("stageSdfTexture");
            if (textureProp != null)
            {
                var lineRect = new Rect(rect.x, y, rect.width, rect.height);
                EditorGUI.PropertyField(lineRect, textureProp, new GUIContent("SDF资源"));
                y += LineHeight;
            }

            DrawStageSdfOverrideField(
                rect,
                ref y,
                elem.FindPropertyRelative("stageSdfOverrideLocalPosition"),
                elem.FindPropertyRelative("stageSdfLocalPosition"),
                "启用位置偏移",
                "位置偏移");

            DrawStageSdfOverrideField(
                rect,
                ref y,
                elem.FindPropertyRelative("stageSdfOverrideLocalRotation"),
                elem.FindPropertyRelative("stageSdfLocalEulerAngles"),
                "启用旋转偏移",
                "旋转偏移");

            DrawStageSdfOverrideField(
                rect,
                ref y,
                elem.FindPropertyRelative("stageSdfOverrideLocalScale"),
                elem.FindPropertyRelative("stageSdfLocalScale"),
                "启用缩放偏移",
                "缩放偏移");
        }

        private static void DrawStageSdfOverrideField(
            Rect rect,
            ref float y,
            SerializedProperty enabledProp,
            SerializedProperty valueProp,
            string enabledLabel,
            string valueLabel)
        {
            if (enabledProp == null)
                return;

            var lineRect = new Rect(rect.x, y, rect.width, rect.height);
            EditorGUI.PropertyField(lineRect, enabledProp, new GUIContent(enabledLabel));
            y += LineHeight;

            if (!enabledProp.boolValue || valueProp == null)
                return;

            EditorGUI.indentLevel++;
            lineRect.y = y;
            EditorGUI.PropertyField(lineRect, valueProp, new GUIContent(valueLabel));
            EditorGUI.indentLevel--;
            y += LineHeight;
        }

        private void DrawAppearEntryParams(Rect rect, ref float y, SerializedProperty elem, AppearEffectType type)
        {
            rect.y = y; rect.height = EditorGUIUtility.singleLineHeight;
            switch (type)
            {
                case AppearEffectType.Show:
                    break;
                case AppearEffectType.Hide:
                    break;
                case AppearEffectType.Transparency:
                    EditorGUI.PropertyField(rect, elem.FindPropertyRelative("appearInitialTransparency"), new GUIContent("初始透明度(0-1)"));
                    y += LineHeight; rect.y = y;
                    EditorGUI.PropertyField(rect, elem.FindPropertyRelative("appearTransparencyTarget"), new GUIContent("目标透明度(0-1)"));
                    y += LineHeight;
                    break;
                case AppearEffectType.Shader:
                    EditorGUI.PropertyField(rect, elem.FindPropertyRelative("shaderAppearPropertyName"), new GUIContent("参数名"));
                    y += LineHeight; rect.y = y;
                    EditorGUI.PropertyField(rect, elem.FindPropertyRelative("shaderAppearFrom"), new GUIContent("起始值"));
                    y += LineHeight; rect.y = y;
                    EditorGUI.PropertyField(rect, elem.FindPropertyRelative("shaderAppearTo"), new GUIContent("结束值"));
                    y += LineHeight;
                    break;
                case AppearEffectType.Transform:
                    DrawTransformAppearParams(rect, ref y, elem);
                    break;
                case AppearEffectType.KeyframeAnimation:
                    EditorGUI.PropertyField(rect, elem.FindPropertyRelative("keyframeAppearStateOrTrigger"), new GUIContent("状态名"));
                    y += LineHeight;
                    break;
            }
        }

        private void DrawTransformAppearParams(Rect rect, ref float y, SerializedProperty elem)
        {
            rect.height = EditorGUIUtility.singleLineHeight;
            var animPosProp = elem.FindPropertyRelative("transformAppearAnimatePosition");
            var animRotProp = elem.FindPropertyRelative("transformAppearAnimateRotation");
            var animScaleProp = elem.FindPropertyRelative("transformAppearAnimateScale");

            EditorGUI.PropertyField(rect, animPosProp, new GUIContent("动画位置"));
            bool animPos = animPosProp.boolValue;
            y += LineHeight; rect.y = y;
            if (animPos)
            {
                EditorGUI.PropertyField(rect, elem.FindPropertyRelative("transformAppearPositionTo"), new GUIContent("结束位置"));
                y += LineHeight; rect.y = y;
            }

            EditorGUI.PropertyField(rect, animRotProp, new GUIContent("动画旋转"));
            bool animRot = animRotProp.boolValue;
            y += LineHeight; rect.y = y;
            if (animRot)
            {
                EditorGUI.PropertyField(rect, elem.FindPropertyRelative("transformAppearRotationTo"), new GUIContent("结束旋转(欧拉)"));
                y += LineHeight; rect.y = y;
            }

            EditorGUI.PropertyField(rect, animScaleProp, new GUIContent("动画缩放"));
            bool animScale = animScaleProp.boolValue;
            y += LineHeight; rect.y = y;
            if (animScale)
            {
                EditorGUI.PropertyField(rect, elem.FindPropertyRelative("transformAppearScaleTo"), new GUIContent("结束缩放"));
                y += LineHeight; rect.y = y;
            }
            DrawEasePopup(rect, elem.FindPropertyRelative("transformAppearEase"), new GUIContent("缓动类型"));
            y += LineHeight;
        }

        private void DrawDisappearElement(Rect rect, int index)
        {
            var elem = _disappearEffectEntries.GetArrayElementAtIndex(index);
            var targetTypeProp = elem.FindPropertyRelative("targetType");
            var contentRoot = elem.FindPropertyRelative("contentRoot");
            var stageModuleId = elem.FindPropertyRelative("stageModuleId");
            var stageAction = elem.FindPropertyRelative("stageActionType");
            var delay = elem.FindPropertyRelative("delay");
            var duration = elem.FindPropertyRelative("duration");
            var effectType = elem.FindPropertyRelative("effectType");
            var guideAction = elem.FindPropertyRelative("guideAction");
            var targetType = ReadEffectTargetType(elem);

            float y = rect.y;
            rect.height = EditorGUIUtility.singleLineHeight;

            elem.isExpanded = EditorGUI.Foldout(new Rect(rect.x, y, 20, rect.height), elem.isExpanded, GUIContent.none);
            var contentRect = new Rect(rect.x + 18, y, rect.width - 18, rect.height);
            if (targetType == EffectTargetType.Guide)
                EditorGUI.LabelField(contentRect, "导游动作");
            else if (targetType == EffectTargetType.System)
                EditorGUI.LabelField(contentRect, "系统动作");
            else if (targetType == EffectTargetType.StageModule)
                DrawStageModuleIdField(contentRect, stageModuleId);
            else
            {
                var fieldRect = DrawContentTypeBadge(contentRect, contentRoot);
                EditorGUI.PropertyField(fieldRect, contentRoot, GUIContent.none);
            }
            y += LineHeight;
            DrawDelayAndTargetRow(rect, y, delay, targetTypeProp);
            y += LineHeight;

            if (!elem.isExpanded) return;

            if (ReadEffectTargetType(elem) == EffectTargetType.Guide)
            {
                DrawGuideActionPopup(new Rect(rect.x, y, rect.width, rect.height), guideAction, "导游动作");
                y += LineHeight;
                DrawGuideMoveParams(rect, ref y, elem);
                DrawGuideAnimationParams(rect, ref y, elem);
                DrawGuideVoiceParams(rect, ref y, elem);
                return;
            }

            if (ReadEffectTargetType(elem) == EffectTargetType.System)
            {
                DrawSystemActionPopup(new Rect(rect.x, y, rect.width, rect.height), elem.FindPropertyRelative("systemActionType"), "类型");
                y += LineHeight;
                DrawSystemActionParams(rect, ref y, elem, SystemActionType.StopBgm);
                return;
            }

            if (ReadEffectTargetType(elem) == EffectTargetType.StageModule)
            {
                DrawStageActionPopup(new Rect(rect.x, y, rect.width, rect.height), stageAction, "类型");
                y += LineHeight;
                var action = ReadStageActionType(stageAction, StageActionType.Hide);
                if (StageActionNeedsDuration(action))
                {
                    EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, rect.height), duration, new GUIContent("时长(秒)", "用于等待 SDF 特效播放完成，0 则使用默认 0.5 秒"));
                    y += LineHeight;
                    DrawStageSdfConfigFields(rect, ref y, elem);
                }
                return;
            }

            DrawDisappearTypePopup(new Rect(rect.x, y, rect.width, rect.height), contentRoot, effectType, targetType == EffectTargetType.StageModule);
            y += LineHeight;
            var type = (DisappearEffectType)effectType.enumValueIndex;
            var contentType = targetType == EffectTargetType.StageModule
                ? ContentType.Model
                : GetCachedContentType(contentRoot?.objectReferenceValue as GameObject);
            if (DisappearTypeNeedsDuration(type, contentType))
            {
                EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, rect.height), duration, new GUIContent("时长(秒)", type == DisappearEffectType.Hide && contentType == ContentType.VFX ? "先执行收尾再隐藏，等待此时长后隐藏" : "0 则使用默认 0.5 秒"));
                y += LineHeight;
            }
            DrawDisappearEntryParams(rect, ref y, elem, type);
        }

        private static bool DisappearTypeNeedsDuration(DisappearEffectType type, ContentType contentType)
        {
            if (type == DisappearEffectType.Show)
                return false;
            // 直接隐藏：仅当内容为 VFX 时显示时长（先执行收尾工作再隐藏）；其他类型不显示
            if (type == DisappearEffectType.Hide)
                return contentType == ContentType.VFX;
            return true;
        }

        private void DrawDisappearEntryParams(Rect rect, ref float y, SerializedProperty elem, DisappearEffectType type)
        {
            rect.y = y; rect.height = EditorGUIUtility.singleLineHeight;
            switch (type)
            {
                case DisappearEffectType.Show:
                    break;
                case DisappearEffectType.Hide:
                    break;
                case DisappearEffectType.Transparency:
                    EditorGUI.PropertyField(rect, elem.FindPropertyRelative("endTransparencyTarget"), new GUIContent("目标透明度(0-1)"));
                    y += LineHeight;
                    break;
                case DisappearEffectType.Shader:
                    EditorGUI.PropertyField(rect, elem.FindPropertyRelative("shaderDisappearPropertyName"), new GUIContent("参数名"));
                    y += LineHeight; rect.y = y;
                    EditorGUI.PropertyField(rect, elem.FindPropertyRelative("shaderDisappearFrom"), new GUIContent("起始值"));
                    y += LineHeight; rect.y = y;
                    EditorGUI.PropertyField(rect, elem.FindPropertyRelative("shaderDisappearTo"), new GUIContent("结束值"));
                    y += LineHeight;
                    break;
                case DisappearEffectType.Transform:
                    DrawTransformDisappearParams(rect, ref y, elem);
                    break;
                case DisappearEffectType.KeyframeAnimation:
                    EditorGUI.PropertyField(rect, elem.FindPropertyRelative("keyframeDisappearStateOrTrigger"), new GUIContent("状态名"));
                    y += LineHeight;
                    break;
            }
        }

        private void DrawGuideMoveParams(Rect rect, ref float y, SerializedProperty elem)
        {
            var actionProp = elem.FindPropertyRelative("guideAction");
            if (actionProp == null)
                return;

            var action = (GuideListActionType)actionProp.enumValueIndex;
            if (!GuideActionNeedsMove(action))
                return;

            rect.height = EditorGUIUtility.singleLineHeight;
            rect.y = y;
            EditorGUI.PropertyField(rect, elem.FindPropertyRelative("guideWaypoint"), new GUIContent("目标点(可选)"));
            y += LineHeight;
            rect.y = y;

            var moveStyleProp = elem.FindPropertyRelative("guideMoveStyle");
            if (moveStyleProp != null)
            {
                EditorGUI.PropertyField(rect, moveStyleProp, new GUIContent("移动轨迹"));
                y += LineHeight;
                rect.y = y;

                var moveStyle = (GuideMoveStyle)moveStyleProp.enumValueIndex;
                if (moveStyle == GuideMoveStyle.Waypoints)
                {
                    EditorGUI.PropertyField(rect, elem.FindPropertyRelative("guidePathRoot"), new GUIContent("路径点容器(可选)"));
                    y += LineHeight;
                    rect.y = y;
                    EditorGUI.PropertyField(rect, elem.FindPropertyRelative("guidePathInterpolation"), new GUIContent("路径插值"));
                    y += LineHeight;
                    rect.y = y;
                }
            }

            var immediateProp = elem.FindPropertyRelative("guideMoveImmediate");
            var keepHeightProp = elem.FindPropertyRelative("guideKeepHeight");
            EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width * 0.5f, rect.height), immediateProp, new GUIContent("瞬移"));
            EditorGUI.PropertyField(new Rect(rect.x + rect.width * 0.5f, rect.y, rect.width * 0.5f, rect.height), keepHeightProp, new GUIContent("保持高度"));
            y += LineHeight;
            rect.y = y;

            EditorGUI.PropertyField(rect, elem.FindPropertyRelative("guideRotateTowardsTarget"), new GUIContent("朝向目标"));
            y += LineHeight;
            rect.y = y;

            EditorGUI.PropertyField(rect, elem.FindPropertyRelative("guideMoveDuration"), new GUIContent("移动时长(秒)"));
            y += LineHeight;
            rect.y = y;

            EditorGUI.PropertyField(rect, elem.FindPropertyRelative("guideArriveDistance"), new GUIContent("到达阈值"));
            y += LineHeight;
            rect.y = y;

            EditorGUI.PropertyField(rect, elem.FindPropertyRelative("guideRotateSpeedDeg"), new GUIContent("旋转速度(度/秒)"));
            y += LineHeight;
        }

        private void DrawGuideVoiceParams(Rect rect, ref float y, SerializedProperty elem)
        {
            var actionProp = elem.FindPropertyRelative("guideAction");
            if (actionProp == null)
                return;

            var action = (GuideListActionType)actionProp.enumValueIndex;
            if (action != GuideListActionType.PlayVoice)
                return;

            rect.height = EditorGUIUtility.singleLineHeight;
            rect.y = y;
            EditorGUI.PropertyField(rect, elem.FindPropertyRelative("guideVoiceClip"), new GUIContent("语音片段"));
            y += LineHeight;
            rect.y = y;
            var volumeProp = elem.FindPropertyRelative("guideVoiceVolume");
            if (volumeProp != null)
                EditorGUI.Slider(rect, volumeProp, 0f, 1f, new GUIContent("语音音量"));
            y += LineHeight;

            var overrideProp = elem.FindPropertyRelative("guideVoiceOverrideAnimationSettings");
            if (overrideProp == null)
                return;

            rect.y = y;
            EditorGUI.PropertyField(rect, overrideProp, new GUIContent("覆盖语音联动"));
            y += LineHeight;

            if (!overrideProp.boolValue)
                return;

            var playAnimationProp = elem.FindPropertyRelative("guideVoicePlayAnimation");
            if (playAnimationProp == null)
                return;

            rect.y = y;
            EditorGUI.PropertyField(rect, playAnimationProp, new GUIContent("语音时自动播放动画"));
            y += LineHeight;

            if (!playAnimationProp.boolValue)
                return;

            rect.y = y;
            EditorGUI.PropertyField(rect, elem.FindPropertyRelative("guideVoiceAnimationStateOrTrigger"), new GUIContent("语音动画状态/Trigger"));
            y += LineHeight;

            rect.y = y;
            EditorGUI.PropertyField(rect, elem.FindPropertyRelative("guideVoiceIdleAnimationStateOrTrigger"), new GUIContent("语音结束回到状态/Trigger"));
            y += LineHeight;
        }

        private void DrawGuideAnimationParams(Rect rect, ref float y, SerializedProperty elem)
        {
            var actionProp = elem.FindPropertyRelative("guideAction");
            if (actionProp == null)
                return;

            var action = (GuideListActionType)actionProp.enumValueIndex;
            rect.height = EditorGUIUtility.singleLineHeight;

            if (action == GuideListActionType.Show || action == GuideListActionType.Hide)
            {
                var animationProp = elem.FindPropertyRelative("guideUseAnimationEffect");
                if (animationProp == null)
                    return;

                rect.y = y;
                EditorGUI.PropertyField(rect, animationProp, new GUIContent("动画效果"));
                y += LineHeight;
                return;
            }

            if (action != GuideListActionType.PlayAnimation)
                return;

            var animationNameProp = elem.FindPropertyRelative("guideAnimationStateOrTrigger");
            var waitProp = elem.FindPropertyRelative("guideAnimationWaitForCompletion");
            if (animationNameProp == null || waitProp == null)
                return;

            rect.y = y;
            EditorGUI.PropertyField(rect, animationNameProp, new GUIContent("动画状态/Trigger"));
            y += LineHeight;
            rect.y = y;
            EditorGUI.PropertyField(rect, waitProp, new GUIContent("等待动画完成"));
            y += LineHeight;
        }

        private static void DrawGuideActionPopup(Rect rect, SerializedProperty actionProp, string label)
        {
            if (actionProp == null)
                return;

            var values = new[]
            {
                GuideListActionType.Show,
                GuideListActionType.Hide,
                GuideListActionType.MoveToWaypoint,
                GuideListActionType.ShowAndMoveToWaypoint,
                GuideListActionType.PlayAnimation,
                GuideListActionType.PlayVoice
            };
            var names = new[]
            {
                "显示导游",
                "隐藏导游",
                "移动到目标点",
                "显示并移动到目标点",
                "播放动画",
                "播放音频"
            };

            var current = (GuideListActionType)actionProp.enumValueIndex;
            int currentIndex = Array.IndexOf(values, current);
            if (currentIndex < 0) currentIndex = 0;

            int newIndex = EditorGUI.Popup(rect, label, currentIndex, names);
            if (newIndex >= 0 && newIndex < values.Length)
                actionProp.enumValueIndex = (int)values[newIndex];
        }

        private static void DrawStageActionPopup(Rect rect, SerializedProperty actionProp, string label)
        {
            if (actionProp == null)
                return;

            var values = new[]
            {
                StageActionType.Show,
                StageActionType.Hide,
                StageActionType.PlaySdfEffect
            };
            var names = new[]
            {
                "显示",
                "消失",
                "播放SDF特效"
            };

            var current = (StageActionType)actionProp.enumValueIndex;
            int currentIndex = Array.IndexOf(values, current);
            if (currentIndex < 0)
                currentIndex = 0;

            int newIndex = EditorGUI.Popup(rect, label, currentIndex, names);
            if (newIndex >= 0 && newIndex < values.Length)
                actionProp.enumValueIndex = (int)values[newIndex];
        }

        private static void DrawSystemActionPopup(Rect rect, SerializedProperty actionProp, string label)
        {
            if (actionProp == null)
                return;

            var values = new[]
            {
                SystemActionType.PlayBgm,
                SystemActionType.StopBgm,
                SystemActionType.PlaySfx
            };
            var names = new[]
            {
                "播放背景音乐",
                "停止背景音乐",
                "播放音效"
            };

            var current = (SystemActionType)actionProp.enumValueIndex;
            int currentIndex = Array.IndexOf(values, current);
            if (currentIndex < 0)
                currentIndex = 0;

            int newIndex = EditorGUI.Popup(rect, label, currentIndex, names);
            if (newIndex >= 0 && newIndex < values.Length)
                actionProp.enumValueIndex = (int)values[newIndex];
        }

        private static void DrawSystemActionParams(Rect rect, ref float y, SerializedProperty elem, SystemActionType fallback)
        {
            if (elem == null)
                return;

            var action = ReadSystemActionType(elem.FindPropertyRelative("systemActionType"), fallback);
            rect.height = EditorGUIUtility.singleLineHeight;
            rect.y = y;

            switch (action)
            {
                case SystemActionType.PlayBgm:
                    EditorGUI.PropertyField(rect, elem.FindPropertyRelative("systemAudioClip"), new GUIContent("音频片段"));
                    y += LineHeight;
                    rect.y = y;
                    DrawSystemAudioVolumeField(rect, elem.FindPropertyRelative("systemAudioVolume"));
                    y += LineHeight;
                    rect.y = y;
                    EditorGUI.PropertyField(rect, elem.FindPropertyRelative("systemFadeSeconds"), new GUIContent("淡入淡出时长(秒)"));
                    y += LineHeight;
                    return;

                case SystemActionType.StopBgm:
                    EditorGUI.PropertyField(rect, elem.FindPropertyRelative("systemFadeSeconds"), new GUIContent("淡出时长(秒)"));
                    y += LineHeight;
                    return;

                case SystemActionType.PlaySfx:
                    EditorGUI.PropertyField(rect, elem.FindPropertyRelative("systemAudioClip"), new GUIContent("音频片段"));
                    y += LineHeight;
                    rect.y = y;
                    DrawSystemAudioVolumeField(rect, elem.FindPropertyRelative("systemAudioVolume"));
                    y += LineHeight;
                    return;
            }
        }

        private static void DrawSystemAudioVolumeField(Rect rect, SerializedProperty volumeProp)
        {
            if (volumeProp == null)
                return;

            EditorGUI.Slider(rect, volumeProp, 0f, 1f, new GUIContent("音量"));
        }

        private static void DrawStageModuleIdField(Rect rect, SerializedProperty stageModuleIdProp)
        {
            if (stageModuleIdProp == null)
                return;

            var options = CollectStageModuleOptions(stageModuleIdProp.stringValue);
            string currentValue = string.IsNullOrWhiteSpace(stageModuleIdProp.stringValue)
                ? string.Empty
                : stageModuleIdProp.stringValue.Trim();
            int firstConfiguredIndex = -1;
            for (int i = 0; i < options.Count; i++)
            {
                if (string.IsNullOrEmpty(options[i].Id))
                    continue;

                firstConfiguredIndex = i;
                break;
            }

            if (string.IsNullOrEmpty(currentValue) && firstConfiguredIndex >= 0)
            {
                currentValue = options[firstConfiguredIndex].Id;
                stageModuleIdProp.stringValue = currentValue;
            }

            int selectedIndex = 0;
            var labels = new string[options.Count];
            for (int i = 0; i < options.Count; i++)
            {
                labels[i] = options[i].Label;
                if (string.Equals(options[i].Id, currentValue, StringComparison.OrdinalIgnoreCase))
                    selectedIndex = i;
            }

            int newIndex = EditorGUI.Popup(rect, "展台ID", selectedIndex, labels);
            if (newIndex >= 0 && newIndex < options.Count)
                stageModuleIdProp.stringValue = options[newIndex].Id;
        }

        private static List<StageModuleOption> CollectStageModuleOptions(string currentValue)
        {
            EnsureStageModuleOptionsCache();

            var options = new List<StageModuleOption>(s_CachedStageModuleOptions);

            currentValue = string.IsNullOrWhiteSpace(currentValue) ? string.Empty : currentValue.Trim();
            if (!string.IsNullOrEmpty(currentValue) && !s_CachedStageModuleIds.Contains(currentValue))
                options.Add(new StageModuleOption(currentValue, $"当前值: {currentValue}"));

            return options;
        }

        private static void EnsureStageModuleOptionsCache()
        {
            if (!s_StageModuleOptionsDirty)
                return;

            s_CachedStageModuleOptions.Clear();
            s_CachedStageModuleIds.Clear();
            s_CachedStageModuleOptions.Add(new StageModuleOption(string.Empty, string.Empty));
            s_CachedStageModuleIds.Add(string.Empty);

            var plans = new List<ChapterPlacementPlan>();
            var directors = UnityEngine.Object.FindObjectsByType<ChapterPlacementDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < directors.Length; i++)
            {
                var plan = directors[i] != null ? directors[i].Plan : null;
                if (plan != null && !plans.Contains(plan))
                    plans.Add(plan);
            }

            string[] planGuids = AssetDatabase.FindAssets("t:ChapterPlacementPlan");
            for (int i = 0; i < planGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(planGuids[i]);
                var plan = AssetDatabase.LoadAssetAtPath<ChapterPlacementPlan>(path);
                if (plan != null && !plans.Contains(plan))
                    plans.Add(plan);
            }

            for (int i = 0; i < plans.Count; i++)
                AppendStageModuleOptions(plans[i], s_CachedStageModuleOptions, s_CachedStageModuleIds);

            s_StageModuleOptionsDirty = false;
        }

        private static void AppendStageModuleOptions(
            ChapterPlacementPlan plan,
            List<StageModuleOption> options,
            HashSet<string> addedIds)
        {
            if (plan == null || plan.stageModules == null)
                return;

            for (int i = 0; i < plan.stageModules.Count; i++)
            {
                var stage = plan.stageModules[i];
                if (stage == null)
                    continue;

                string id = string.IsNullOrWhiteSpace(stage.moduleId) ? string.Empty : stage.moduleId.Trim();
                if (string.IsNullOrEmpty(id) || !addedIds.Add(id))
                    continue;

                string label = string.IsNullOrWhiteSpace(stage.moduleName)
                    ? id
                    : $"{stage.moduleName} ({id})";
                options.Add(new StageModuleOption(id, label));
            }
        }

        private void DrawDelayAndTargetRow(Rect rect, float y, SerializedProperty delayProp, SerializedProperty targetTypeProp)
        {
            rect.height = EditorGUIUtility.singleLineHeight;
            const float gap = 4f;
            float delayWidth = rect.width * 0.48f;
            var delayRect = new Rect(rect.x, y, delayWidth, rect.height);
            var rightRect = new Rect(rect.x + delayWidth + gap, y, rect.width - delayWidth - gap, rect.height);
            const float delayLabelWidth = 52f;
            var delayLabelRect = new Rect(delayRect.x, delayRect.y, delayLabelWidth, delayRect.height);
            var delayValueRect = new Rect(delayLabelRect.xMax + 2f, delayRect.y, delayRect.width - delayLabelWidth - 2f, delayRect.height);
            EditorGUI.LabelField(delayLabelRect, "延时(秒)");
            EditorGUI.PropertyField(delayValueRect, delayProp, GUIContent.none);

            if (targetTypeProp == null)
                return;

            const float labelWidth = 28f;
            var labelRect = new Rect(rightRect.x, rightRect.y, labelWidth, rightRect.height);
            var popupRect = new Rect(labelRect.xMax + 2f, rightRect.y, rightRect.width - labelWidth - 2f, rightRect.height);
            EditorGUI.LabelField(labelRect, "目标");
            DrawEffectTargetTypePopup(popupRect, targetTypeProp);
        }

        private static void DrawEffectTargetTypePopup(Rect rect, SerializedProperty targetTypeProp)
        {
            if (targetTypeProp == null)
                return;

            var values = new[] { EffectTargetType.Content, EffectTargetType.Guide, EffectTargetType.StageModule, EffectTargetType.System };
            var names = new[] { "内容", "导游", "展台", "系统" };

            var current = (EffectTargetType)targetTypeProp.enumValueIndex;
            int currentIndex = Array.IndexOf(values, current);
            if (currentIndex < 0)
                currentIndex = 0;

            int selected = EditorGUI.Popup(rect, currentIndex, names);
            if (selected >= 0 && selected < values.Length)
                targetTypeProp.enumValueIndex = (int)values[selected];
        }

        private void DrawTransformDisappearParams(Rect rect, ref float y, SerializedProperty elem)
        {
            rect.height = EditorGUIUtility.singleLineHeight;
            var animPosProp = elem.FindPropertyRelative("transformDisappearAnimatePosition");
            var animRotProp = elem.FindPropertyRelative("transformDisappearAnimateRotation");
            var animScaleProp = elem.FindPropertyRelative("transformDisappearAnimateScale");

            EditorGUI.PropertyField(rect, animPosProp, new GUIContent("动画位置"));
            bool animPos = animPosProp.boolValue;
            y += LineHeight; rect.y = y;
            if (animPos)
            {
                EditorGUI.PropertyField(rect, elem.FindPropertyRelative("transformDisappearPositionTo"), new GUIContent("结束位置"));
                y += LineHeight; rect.y = y;
            }

            EditorGUI.PropertyField(rect, animRotProp, new GUIContent("动画旋转"));
            bool animRot = animRotProp.boolValue;
            y += LineHeight; rect.y = y;
            if (animRot)
            {
                EditorGUI.PropertyField(rect, elem.FindPropertyRelative("transformDisappearRotationTo"), new GUIContent("结束旋转(欧拉)"));
                y += LineHeight; rect.y = y;
            }

            EditorGUI.PropertyField(rect, animScaleProp, new GUIContent("动画缩放"));
            bool animScale = animScaleProp.boolValue;
            y += LineHeight; rect.y = y;
            if (animScale)
            {
                EditorGUI.PropertyField(rect, elem.FindPropertyRelative("transformDisappearScaleTo"), new GUIContent("结束缩放"));
                y += LineHeight; rect.y = y;
            }
            DrawEasePopup(rect, elem.FindPropertyRelative("transformDisappearEase"), new GUIContent("缓动类型"));
            y += LineHeight;
        }

        private void DrawAppearTypePopup(Rect rect, SerializedProperty contentRootProp, SerializedProperty effectTypeProp, bool forceModelType = false)
        {
            GameObject root = contentRootProp?.objectReferenceValue as GameObject;
            var contentType = forceModelType ? ContentType.Model : GetCachedContentType(root);
            var allowed = GetAllowedAppearEffects(contentType);
            DrawEnumPopupRect(rect, effectTypeProp, allowed, "类型", GetLocalizedAppearName);
        }

        private void DrawDisappearTypePopup(Rect rect, SerializedProperty contentRootProp, SerializedProperty effectTypeProp, bool forceModelType = false)
        {
            GameObject root = contentRootProp?.objectReferenceValue as GameObject;
            var contentType = forceModelType ? ContentType.Model : GetCachedContentType(root);
            var allowed = GetAllowedDisappearEffects(contentType);
            DrawEnumPopupRect(rect, effectTypeProp, allowed, "类型", GetLocalizedDisappearName);
        }

        private static AppearEffectType[] GetAllowedAppearEffects(ContentType type)
        {
            return InteractionModule.GetAllowedAppearTypes(type);
        }

        private static DisappearEffectType[] GetAllowedDisappearEffects(ContentType type)
        {
            return InteractionModule.GetAllowedDisappearTypes(type);
        }

        private static string GetLocalizedAppearName(AppearEffectType type)
        {
            switch (type)
            {
                case AppearEffectType.Show: return "直接显示";
                case AppearEffectType.Hide: return "直接隐藏";
                case AppearEffectType.Transparency: return "透明度渐变";
                case AppearEffectType.Shader: return "材质动画";
                case AppearEffectType.Transform: return "程序化动画";
                case AppearEffectType.KeyframeAnimation: return "关键帧动画";
                default: return type.ToString();
            }
        }

        private static string GetLocalizedDisappearName(DisappearEffectType type)
        {
            switch (type)
            {
                case DisappearEffectType.Show: return "直接显示";
                case DisappearEffectType.Hide: return "直接隐藏";
                case DisappearEffectType.Transparency: return "透明度渐变";
                case DisappearEffectType.Shader: return "材质动画";
                case DisappearEffectType.Transform: return "程序化动画";
                case DisappearEffectType.KeyframeAnimation: return "关键帧动画";
                default: return type.ToString();
            }
        }

        private static void DrawEnumPopupRect(Rect rect, SerializedProperty enumProp, AppearEffectType[] allowedValues, string label, Func<AppearEffectType, string> getName)
        {
            if (allowedValues == null || allowedValues.Length == 0) return;
            var currentValue = (AppearEffectType)enumProp.enumValueIndex;
            int currentIndex = Array.IndexOf(allowedValues, currentValue);
            if (currentIndex < 0) currentIndex = 0;
            var names = new string[allowedValues.Length];
            for (int i = 0; i < allowedValues.Length; i++) names[i] = getName(allowedValues[i]);
            int newIndex = EditorGUI.Popup(rect, label, currentIndex, names);
            if (newIndex >= 0 && newIndex < allowedValues.Length)
                enumProp.enumValueIndex = (int)allowedValues[newIndex];
        }

        private static void DrawEnumPopupRect(Rect rect, SerializedProperty enumProp, DisappearEffectType[] allowedValues, string label, Func<DisappearEffectType, string> getName)
        {
            if (allowedValues == null || allowedValues.Length == 0) return;
            var currentValue = (DisappearEffectType)enumProp.enumValueIndex;
            int currentIndex = Array.IndexOf(allowedValues, currentValue);
            if (currentIndex < 0) currentIndex = 0;
            var names = new string[allowedValues.Length];
            for (int i = 0; i < allowedValues.Length; i++) names[i] = getName(allowedValues[i]);
            int newIndex = EditorGUI.Popup(rect, label, currentIndex, names);
            if (newIndex >= 0 && newIndex < allowedValues.Length)
                enumProp.enumValueIndex = (int)allowedValues[newIndex];
        }

        private static Rect DrawContentTypeBadge(Rect contentRect, SerializedProperty contentRoot)
        {
            const float badgeWidth = 56f;
            const float gap = 2f;
            const float labelWidth = 32f;
            const float labelGap = 4f;
            var fieldRect = new Rect(contentRect.x + badgeWidth + gap, contentRect.y, contentRect.width - badgeWidth - gap, contentRect.height);
            var labelRect = new Rect(fieldRect.x, fieldRect.y, labelWidth, fieldRect.height);
            var objectRect = new Rect(fieldRect.x + labelWidth + labelGap, fieldRect.y, fieldRect.width - labelWidth - labelGap, fieldRect.height);
            if (contentRoot == null || contentRoot.objectReferenceValue == null) return fieldRect;
            var root = contentRoot.objectReferenceValue as GameObject;
            var contentType = GetCachedContentType(root);
            string label = GetLocalizedContentTypeLabel(contentType);

            var badgeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.85f, 0.92f, 1f, 0.95f) }
            };
            var badgeRect = new Rect(contentRect.x, contentRect.y, badgeWidth, contentRect.height);
            var bg = EditorGUIUtility.isProSkin ? new Color(0.2f, 0.28f, 0.38f, 0.9f) : new Color(0.75f, 0.82f, 0.9f, 0.9f);
            EditorGUI.DrawRect(badgeRect, bg);
            EditorGUI.LabelField(new Rect(badgeRect.x + 4f, badgeRect.y, badgeRect.width - 6f, badgeRect.height), label, badgeStyle);
            EditorGUI.LabelField(labelRect, "内容");
            return objectRect;
        }

        private static string GetLocalizedContentTypeLabel(ContentType type)
        {
            switch (type)
            {
                case ContentType.Model: return "模型";
                case ContentType.Video: return "视频";
                case ContentType.Particle: return "粒子";
                case ContentType.VFX: return "特效";
                default: return type.ToString();
            }
        }


        private void DrawEasePopup(Rect rect, SerializedProperty easeIntProp, GUIContent label)
        {
            var values = (Ease[])Enum.GetValues(typeof(Ease));
            var names = new List<string>();
            var valueList = new List<int>();
            for (int i = 0; i < values.Length; i++)
            {
                string n = values[i].ToString();
                if (n.StartsWith("INTERNAL_", StringComparison.Ordinal)) continue;
                names.Add(n);
                valueList.Add((int)values[i]);
            }
            int currentValue = easeIntProp.intValue;
            int currentIndex = valueList.IndexOf(currentValue);
            if (currentIndex < 0) currentIndex = 0;
            int newIndex = EditorGUI.Popup(rect, label.text, currentIndex, names.ToArray());
            if (newIndex >= 0 && newIndex < valueList.Count)
                easeIntProp.intValue = valueList[newIndex];
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (serializedObject.isEditingMultipleObjects)
            {
                EditorGUILayout.HelpBox(
                    "InteractionModule 自定义面板暂不支持多对象编辑。请单独选择 prefab 再修改，避免把触发器等配置意外写回到其他 prefab。",
                    MessageType.Warning);
                return;
            }

            DrawRuntimeStatus();
            DrawValidation();
            DrawContentSetup();
            DrawInitialPhase();
            DrawStartPhase();
            // 进行阶段暂不显示
            DrawEndPhase();

            serializedObject.ApplyModifiedProperties();
        }


        private void DrawRuntimeStatus()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawSectionTitle("运行状态");

            var module = target as InteractionModule;
            if (!Application.isPlaying || module == null)
            {
                EditorGUILayout.HelpBox("进入 Play 模式后显示运行时状态与调试按钮。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.LabelField(
                "当前阶段",
                GetPhaseLabel(module.CurrentPhase),
                GetRuntimeStatusLabelStyle());

            var moduleScene = module.gameObject.scene;
            bool isLoadedSceneInstance = moduleScene.IsValid() && moduleScene.isLoaded;

            if (!isLoadedSceneInstance)
            {
                EditorGUILayout.HelpBox(
                    "当前检查的是 Prefab 资源。请在 Hierarchy 中选择已加载的场景实例后再测试交互。",
                    MessageType.Warning);
            }
            else if (!module.gameObject.activeInHierarchy)
            {
                EditorGUILayout.HelpBox(
                    "当前模块未激活。点击调试按钮时会先临时激活这个场景实例，再运行交互动画。",
                    MessageType.Info);
            }

            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(!isLoadedSceneInstance))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("触发开始"))
                    module.DebugTriggerStart();
                if (GUILayout.Button("触发结束"))
                    module.DebugTriggerEnd();
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawValidation()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawSectionTitle("校验");

            var issues = new List<string>();
            var startType = ReadTriggerType(_startTriggerType);
            var endType = ReadTriggerType(_endTriggerType);
            bool touchRequired = startType == TriggerType.Touch || endType == TriggerType.Touch;
            bool hasExternalTouchInteractable = HasExternalTouchInteractableBinding();

            if (touchRequired && _touchInteractable.objectReferenceValue == null)
                issues.Add("触摸触发器缺少 XRSimpleInteractable 绑定。");
            if (touchRequired && hasExternalTouchInteractable)
                issues.Add("触摸交互体必须属于当前模块节点或其子节点。");
            if (startType == TriggerType.LongPress && _startLongPressInteractable.objectReferenceValue == null)
                issues.Add("开始长按触发器未绑定交互体。");
            if (endType == TriggerType.LongPress && _endLongPressInteractable.objectReferenceValue == null)
                issues.Add("结束长按触发器未绑定交互体。");
            if ((startType == TriggerType.Enter || startType == TriggerType.Leave) && _enterRegionCollider.objectReferenceValue == null)
                issues.Add("开始进入/离开触发器未绑定进入区域碰撞体。");
            if ((endType == TriggerType.Enter || endType == TriggerType.Leave) && _leaveRegionCollider.objectReferenceValue == null)
                issues.Add("结束进入/离开触发器未绑定离开区域碰撞体。");

            int nullInitial = CountNullEntries(_initialEffectEntries);
            int nullAppear = CountNullEntries(_appearEffectEntries);
            int nullDisappear = CountNullEntries(_disappearEffectEntries);
            if (nullInitial > 0) issues.Add($"初始效果列表存在 {nullInitial} 个空内容条目。");
            if (nullAppear > 0) issues.Add($"开始效果列表存在 {nullAppear} 个空内容条目。");
            if (nullDisappear > 0) issues.Add($"结束效果列表存在 {nullDisappear} 个空内容条目。");

            bool hasInvalidInitialType = HasIncompatibleAppearTypes(_initialEffectEntries);
            bool hasInvalidAppearType = HasIncompatibleAppearTypes(_appearEffectEntries);
            bool hasInvalidDisappearType = HasIncompatibleDisappearTypes(_disappearEffectEntries);
            if (hasInvalidInitialType) issues.Add("初始效果列表存在与内容类型不兼容的效果类型。");
            if (hasInvalidAppearType) issues.Add("开始效果列表存在与内容类型不兼容的效果类型。");
            if (hasInvalidDisappearType) issues.Add("结束效果列表存在与内容类型不兼容的效果类型。");

            int missingGuideVoiceInitial = CountMissingGuideVoiceClipEntries(_initialEffectEntries);
            int missingGuideVoiceAppear = CountMissingGuideVoiceClipEntries(_appearEffectEntries);
            int missingGuideVoiceDisappear = CountMissingGuideVoiceClipEntries(_disappearEffectEntries);
            if (missingGuideVoiceInitial > 0)
                issues.Add($"初始效果列表存在 {missingGuideVoiceInitial} 条“播放音频”导游动作未设置语音片段。");
            if (missingGuideVoiceAppear > 0)
                issues.Add($"开始效果列表存在 {missingGuideVoiceAppear} 条“播放音频”导游动作未设置语音片段。");
            if (missingGuideVoiceDisappear > 0)
                issues.Add($"结束效果列表存在 {missingGuideVoiceDisappear} 条“播放音频”导游动作未设置语音片段。");

            int missingGuideAnimationInitial = CountMissingGuideAnimationEntries(_initialEffectEntries);
            int missingGuideAnimationAppear = CountMissingGuideAnimationEntries(_appearEffectEntries);
            int missingGuideAnimationDisappear = CountMissingGuideAnimationEntries(_disappearEffectEntries);
            if (missingGuideAnimationInitial > 0)
                issues.Add($"初始效果列表存在 {missingGuideAnimationInitial} 条“播放动画”导游动作未填写动画状态或 Trigger。");
            if (missingGuideAnimationAppear > 0)
                issues.Add($"开始效果列表存在 {missingGuideAnimationAppear} 条“播放动画”导游动作未填写动画状态或 Trigger。");
            if (missingGuideAnimationDisappear > 0)
                issues.Add($"结束效果列表存在 {missingGuideAnimationDisappear} 条“播放动画”导游动作未填写动画状态或 Trigger。");

            int missingSystemClipInitial = CountMissingSystemAudioClipEntries(_initialEffectEntries);
            int missingSystemClipAppear = CountMissingSystemAudioClipEntries(_appearEffectEntries);
            int missingSystemClipDisappear = CountMissingSystemAudioClipEntries(_disappearEffectEntries);
            if (missingSystemClipInitial > 0)
                issues.Add($"初始效果列表存在 {missingSystemClipInitial} 条系统播放动作未设置音频片段。");
            if (missingSystemClipAppear > 0)
                issues.Add($"开始效果列表存在 {missingSystemClipAppear} 条系统播放动作未设置音频片段。");
            if (missingSystemClipDisappear > 0)
                issues.Add($"结束效果列表存在 {missingSystemClipDisappear} 条系统播放动作未设置音频片段。");

            bool usesGuideTouch = startType == TriggerType.TouchGuide || endType == TriggerType.TouchGuide;
            if (usesGuideTouch
                && _guideTouchPlayAnimationOnTouch != null
                && _guideTouchPlayAnimationOnTouch.boolValue
                && _guideTouchAnimationStateOrTrigger != null
                && string.IsNullOrWhiteSpace(_guideTouchAnimationStateOrTrigger.stringValue))
            {
                issues.Add("已启用“触摸导游时播放动画”，但未填写触摸动画状态或 Trigger。");
            }

            int missingStageInitial = CountMissingStageModuleEntries(_initialEffectEntries);
            int missingStageAppear = CountMissingStageModuleEntries(_appearEffectEntries);
            int missingStageDisappear = CountMissingStageModuleEntries(_disappearEffectEntries);
            if (missingStageInitial > 0)
                issues.Add($"初始效果列表存在 {missingStageInitial} 个展台目标未填写展台ID。");
            if (missingStageAppear > 0)
                issues.Add($"开始效果列表存在 {missingStageAppear} 个展台目标未填写展台ID。");
            if (missingStageDisappear > 0)
                issues.Add($"结束效果列表存在 {missingStageDisappear} 个展台目标未填写展台ID。");

            if (issues.Count == 0)
                EditorGUILayout.HelpBox("未发现明显配置问题。", MessageType.Info);
            else
                EditorGUILayout.HelpBox(string.Join("\n", issues), MessageType.Warning);

            EditorGUILayout.BeginHorizontal();
            if ((nullInitial > 0 || nullAppear > 0 || nullDisappear > 0) && GUILayout.Button("移除空条目"))
            {
                RemoveNullEntries(_initialEffectEntries);
                RemoveNullEntries(_appearEffectEntries);
                RemoveNullEntries(_disappearEffectEntries);
            }
            if ((hasInvalidInitialType || hasInvalidAppearType || hasInvalidDisappearType) && GUILayout.Button("修正不兼容类型"))
            {
                FixIncompatibleAppearTypes(_initialEffectEntries);
                FixIncompatibleAppearTypes(_appearEffectEntries);
                FixIncompatibleDisappearTypes(_disappearEffectEntries);
            }
            if (touchRequired && hasExternalTouchInteractable && GUILayout.Button("清空无效触摸交互体"))
            {
                _touchInteractable.objectReferenceValue = null;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private bool HasExternalTouchInteractableBinding()
        {
            var module = target as InteractionModule;
            var interactable = _touchInteractable?.objectReferenceValue as Component;
            return IsComponentOutsideModuleHierarchy(module, interactable);
        }

        private static bool IsComponentOutsideModuleHierarchy(InteractionModule module, Component component)
        {
            if (module == null || component == null)
                return false;

            Transform moduleRoot = module.transform;
            Transform targetTransform = component.transform;
            if (moduleRoot == null || targetTransform == null)
                return false;

            return targetTransform != moduleRoot && !targetTransform.IsChildOf(moduleRoot);
        }

        private static int CountNullEntries(SerializedProperty listProp)
        {
            if (listProp == null || !listProp.isArray) return 0;
            int count = 0;
            for (int i = 0; i < listProp.arraySize; i++)
            {
                var elem = listProp.GetArrayElementAtIndex(i);
                var targetTypeProp = elem.FindPropertyRelative("targetType");
                var targetType = targetTypeProp == null ? EffectTargetType.Content : (EffectTargetType)targetTypeProp.enumValueIndex;
                if (targetType == EffectTargetType.Guide) continue;
                if (targetType == EffectTargetType.System) continue;
                if (targetType == EffectTargetType.StageModule)
                {
                    var stageModuleId = elem.FindPropertyRelative("stageModuleId");
                    if (stageModuleId == null || string.IsNullOrWhiteSpace(stageModuleId.stringValue))
                        count++;
                    continue;
                }
                var root = elem.FindPropertyRelative("contentRoot");
                if (root == null || root.objectReferenceValue == null) count++;
            }
            return count;
        }

        private static int CountMissingSystemAudioClipEntries(SerializedProperty listProp)
        {
            if (listProp == null || !listProp.isArray)
                return 0;

            int count = 0;
            for (int i = 0; i < listProp.arraySize; i++)
            {
                var elem = listProp.GetArrayElementAtIndex(i);
                var targetTypeProp = elem.FindPropertyRelative("targetType");
                var targetType = targetTypeProp == null ? EffectTargetType.Content : (EffectTargetType)targetTypeProp.enumValueIndex;
                if (targetType != EffectTargetType.System)
                    continue;

                var actionProp = elem.FindPropertyRelative("systemActionType");
                var action = ReadSystemActionType(actionProp, SystemActionType.PlayBgm);
                if (action != SystemActionType.PlayBgm && action != SystemActionType.PlaySfx)
                    continue;

                var clipProp = elem.FindPropertyRelative("systemAudioClip");
                if (clipProp == null || clipProp.objectReferenceValue == null)
                    count++;
            }

            return count;
        }

        private static int CountMissingStageModuleEntries(SerializedProperty listProp)
        {
            if (listProp == null || !listProp.isArray)
                return 0;

            int count = 0;
            for (int i = 0; i < listProp.arraySize; i++)
            {
                var elem = listProp.GetArrayElementAtIndex(i);
                var targetTypeProp = elem.FindPropertyRelative("targetType");
                var targetType = targetTypeProp == null ? EffectTargetType.Content : (EffectTargetType)targetTypeProp.enumValueIndex;
                if (targetType != EffectTargetType.StageModule)
                    continue;

                var stageModuleId = elem.FindPropertyRelative("stageModuleId");
                if (stageModuleId == null || string.IsNullOrWhiteSpace(stageModuleId.stringValue))
                    count++;
            }

            return count;
        }

        private static int CountMissingGuideVoiceClipEntries(SerializedProperty listProp)
        {
            if (listProp == null || !listProp.isArray)
                return 0;

            int count = 0;
            for (int i = 0; i < listProp.arraySize; i++)
            {
                var elem = listProp.GetArrayElementAtIndex(i);
                var targetTypeProp = elem.FindPropertyRelative("targetType");
                var targetType = targetTypeProp == null ? EffectTargetType.Content : (EffectTargetType)targetTypeProp.enumValueIndex;
                if (targetType != EffectTargetType.Guide)
                    continue;

                var actionProp = elem.FindPropertyRelative("guideAction");
                var action = actionProp == null ? GuideListActionType.Hide : (GuideListActionType)actionProp.enumValueIndex;
                if (action != GuideListActionType.PlayVoice)
                    continue;

                var clipProp = elem.FindPropertyRelative("guideVoiceClip");
                if (clipProp == null || clipProp.objectReferenceValue == null)
                    count++;
            }

            return count;
        }

        private static int CountMissingGuideAnimationEntries(SerializedProperty listProp)
        {
            if (listProp == null || !listProp.isArray)
                return 0;

            int count = 0;
            for (int i = 0; i < listProp.arraySize; i++)
            {
                var elem = listProp.GetArrayElementAtIndex(i);
                var targetTypeProp = elem.FindPropertyRelative("targetType");
                var targetType = targetTypeProp == null ? EffectTargetType.Content : (EffectTargetType)targetTypeProp.enumValueIndex;
                if (targetType != EffectTargetType.Guide)
                    continue;

                var actionProp = elem.FindPropertyRelative("guideAction");
                var action = actionProp == null ? GuideListActionType.Hide : (GuideListActionType)actionProp.enumValueIndex;
                if (action != GuideListActionType.PlayAnimation)
                    continue;

                var animationNameProp = elem.FindPropertyRelative("guideAnimationStateOrTrigger");
                if (animationNameProp == null || string.IsNullOrWhiteSpace(animationNameProp.stringValue))
                    count++;
            }

            return count;
        }

        private static void RemoveNullEntries(SerializedProperty listProp)
        {
            if (listProp == null || !listProp.isArray) return;
            for (int i = listProp.arraySize - 1; i >= 0; i--)
            {
                var elem = listProp.GetArrayElementAtIndex(i);
                var targetTypeProp = elem.FindPropertyRelative("targetType");
                var targetType = targetTypeProp == null ? EffectTargetType.Content : (EffectTargetType)targetTypeProp.enumValueIndex;
                if (targetType == EffectTargetType.Guide) continue;
                if (targetType == EffectTargetType.System) continue;
                if (targetType == EffectTargetType.StageModule)
                {
                    var stageModuleId = elem.FindPropertyRelative("stageModuleId");
                    if (stageModuleId == null || string.IsNullOrWhiteSpace(stageModuleId.stringValue))
                        listProp.DeleteArrayElementAtIndex(i);
                    continue;
                }
                var root = elem.FindPropertyRelative("contentRoot");
                if (root == null || root.objectReferenceValue == null)
                    listProp.DeleteArrayElementAtIndex(i);
            }
        }

        private static bool HasIncompatibleAppearTypes(SerializedProperty listProp)
        {
            if (listProp == null || !listProp.isArray) return false;
            for (int i = 0; i < listProp.arraySize; i++)
            {
                var elem = listProp.GetArrayElementAtIndex(i);
                var targetTypeProp = elem.FindPropertyRelative("targetType");
                var targetType = targetTypeProp == null ? EffectTargetType.Content : (EffectTargetType)targetTypeProp.enumValueIndex;
                if (targetType == EffectTargetType.Guide || targetType == EffectTargetType.StageModule || targetType == EffectTargetType.System) continue;
                var root = elem.FindPropertyRelative("contentRoot");
                if (root == null || root.objectReferenceValue == null) continue;
                var effectProp = elem.FindPropertyRelative("effectType");
                if (effectProp == null) continue;
                var allowed = GetAllowedAppearEffects(GetCachedContentType(root.objectReferenceValue as GameObject));
                var current = (AppearEffectType)effectProp.enumValueIndex;
                if (Array.IndexOf(allowed, current) < 0) return true;
            }
            return false;
        }

        private static bool HasIncompatibleDisappearTypes(SerializedProperty listProp)
        {
            if (listProp == null || !listProp.isArray) return false;
            for (int i = 0; i < listProp.arraySize; i++)
            {
                var elem = listProp.GetArrayElementAtIndex(i);
                var targetTypeProp = elem.FindPropertyRelative("targetType");
                var targetType = targetTypeProp == null ? EffectTargetType.Content : (EffectTargetType)targetTypeProp.enumValueIndex;
                if (targetType == EffectTargetType.Guide || targetType == EffectTargetType.StageModule || targetType == EffectTargetType.System) continue;
                var root = elem.FindPropertyRelative("contentRoot");
                if (root == null || root.objectReferenceValue == null) continue;
                var effectProp = elem.FindPropertyRelative("effectType");
                if (effectProp == null) continue;
                var allowed = GetAllowedDisappearEffects(GetCachedContentType(root.objectReferenceValue as GameObject));
                var current = (DisappearEffectType)effectProp.enumValueIndex;
                if (Array.IndexOf(allowed, current) < 0) return true;
            }
            return false;
        }

        private static void FixIncompatibleAppearTypes(SerializedProperty listProp)
        {
            if (listProp == null || !listProp.isArray) return;
            for (int i = 0; i < listProp.arraySize; i++)
            {
                var elem = listProp.GetArrayElementAtIndex(i);
                var targetTypeProp = elem.FindPropertyRelative("targetType");
                var targetType = targetTypeProp == null ? EffectTargetType.Content : (EffectTargetType)targetTypeProp.enumValueIndex;
                if (targetType == EffectTargetType.Guide || targetType == EffectTargetType.StageModule || targetType == EffectTargetType.System) continue;
                var root = elem.FindPropertyRelative("contentRoot");
                if (root == null || root.objectReferenceValue == null) continue;
                var effectProp = elem.FindPropertyRelative("effectType");
                if (effectProp == null) continue;
                var allowed = GetAllowedAppearEffects(GetCachedContentType(root.objectReferenceValue as GameObject));
                var current = (AppearEffectType)effectProp.enumValueIndex;
                if (Array.IndexOf(allowed, current) < 0)
                    effectProp.enumValueIndex = (int)allowed[0];
            }
        }

        private static void FixIncompatibleDisappearTypes(SerializedProperty listProp)
        {
            if (listProp == null || !listProp.isArray) return;
            for (int i = 0; i < listProp.arraySize; i++)
            {
                var elem = listProp.GetArrayElementAtIndex(i);
                var targetTypeProp = elem.FindPropertyRelative("targetType");
                var targetType = targetTypeProp == null ? EffectTargetType.Content : (EffectTargetType)targetTypeProp.enumValueIndex;
                if (targetType == EffectTargetType.Guide || targetType == EffectTargetType.StageModule || targetType == EffectTargetType.System) continue;
                var root = elem.FindPropertyRelative("contentRoot");
                if (root == null || root.objectReferenceValue == null) continue;
                var effectProp = elem.FindPropertyRelative("effectType");
                if (effectProp == null) continue;
                var allowed = GetAllowedDisappearEffects(GetCachedContentType(root.objectReferenceValue as GameObject));
                var current = (DisappearEffectType)effectProp.enumValueIndex;
                if (Array.IndexOf(allowed, current) < 0)
                    effectProp.enumValueIndex = (int)allowed[0];
            }
        }

        private static string GetPhaseLabel(InteractionPhase phase)
        {
            switch (phase)
            {
                case InteractionPhase.Initial:
                    return "初始";
                case InteractionPhase.Start:
                    return "开始";
                case InteractionPhase.Appearing:
                    return "出现中";
                case InteractionPhase.InProgress:
                    return "进行中";
                case InteractionPhase.End:
                    return "结束";
                default:
                    return phase.ToString();
            }
        }

        private static GUIStyle GetRuntimeStatusLabelStyle()
        {
            if (s_RuntimeStatusLabelStyle != null)
                return s_RuntimeStatusLabelStyle;

            s_RuntimeStatusLabelStyle = new GUIStyle(EditorStyles.label);
            var projectChineseFont = AssetDatabase.LoadAssetAtPath<Font>(
                "Assets/_Game/_Common/Font/LXGWWenKaiMono-Regular.ttf");
            if (projectChineseFont != null)
                s_RuntimeStatusLabelStyle.font = projectChineseFont;

            return s_RuntimeStatusLabelStyle;
        }

        private static void DrawSectionTitle(string title)
        {
            var rect = EditorGUILayout.GetControlRect(false, 22);
            var bg = EditorGUIUtility.isProSkin ? new Color(0.28f, 0.28f, 0.28f, 1f) : new Color(0.7f, 0.7f, 0.7f, 1f);
            EditorGUI.DrawRect(rect, bg);
            var style = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(8, 0, 0, 0), fontSize = 11 };
            EditorGUI.LabelField(rect, title, style);
            EditorGUILayout.Space(2);
        }

        private void DrawContentSetup()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawSectionTitle("内容");
            EditorGUILayout.PropertyField(_allowRestartAfterEnd, new GUIContent("循环交互", "勾选：开始→结束→再开始→… 可循环；不勾选：仅一次 开始→结束，之后不再响应开始。"));

            if (((InteractionModule)target).GetComponent<InteractionStage>() != null)
            {
                EditorGUILayout.HelpBox("该对象同时挂有 InteractionStage。展台内容请在 InteractionStage 组件上配置，而不是在 InteractionModule 上配置。", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawInitialPhase()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawSectionTitle("初始阶段");

            EditorGUILayout.HelpBox("模块激活后自动执行一次初始效果列表；执行完成后才进入开始阶段。", MessageType.Info);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("初始效果", EditorStyles.miniBoldLabel);
            _initialList.DoLayoutList();

            EditorGUILayout.EndVertical();
        }

        private void DrawStartPhase()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawSectionTitle("开始阶段");

            EditorGUILayout.LabelField("触发器", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(_startTriggerType, new GUIContent("类型"));
            var startType = ReadTriggerType(_startTriggerType);
            EditorGUI.indentLevel++;
            DrawSingleTriggerParams(startType, true);
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("开始效果", EditorStyles.miniBoldLabel);
            _appearList.DoLayoutList();

            EditorGUILayout.EndVertical();
        }

        private void DrawInProgressPhase()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawSectionTitle("进行阶段");

            EditorGUILayout.LabelField("拖拽释放", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(_dragReleaseType, new GUIContent("行为"));
            var dragType = (DragReleaseType)_dragReleaseType.enumValueIndex;
            if (dragType != DragReleaseType.None)
            {
                EditorGUI.indentLevel++;
                DrawDragReleaseParamsBody(dragType);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawEndPhase()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawSectionTitle("结束阶段");

            EditorGUILayout.LabelField("触发器", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(_endTriggerType, new GUIContent("类型"));
            var endType = ReadTriggerType(_endTriggerType);
            EditorGUI.indentLevel++;
            DrawSingleTriggerParams(endType, false);
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("结束效果", EditorStyles.miniBoldLabel);
            _disappearList.DoLayoutList();

            EditorGUILayout.EndVertical();
        }

        private void DrawSingleTriggerParams(TriggerType type, bool isStart)
        {
            if (type == TriggerType.Time)
            {
                if (isStart)
                    EditorGUILayout.PropertyField(_startTimeDelay, new GUIContent("延时(秒)"));
                else
                    EditorGUILayout.PropertyField(_endTimeDelay, new GUIContent("延时(秒)"));
            }
            else if (type == TriggerType.Enter || type == TriggerType.Leave)
            {
                if (type == TriggerType.Enter)
                {
                    if (isStart)
                        EditorGUILayout.PropertyField(_enterRegionCollider, new GUIContent("进入区域(碰撞体)", "Main Camera 的 XZ 在此碰撞体内时触发出现"));
                    else
                        EditorGUILayout.PropertyField(_leaveRegionCollider, new GUIContent("结束区域(碰撞体)", "Main Camera 的 XZ 在此碰撞体内时触发结束"));
                }
                else
                {
                    if (isStart)
                        EditorGUILayout.PropertyField(_enterRegionCollider, new GUIContent("离开区域(碰撞体)", "Main Camera 的 XZ 离开此碰撞体时触发出现"));
                    else
                        EditorGUILayout.PropertyField(_leaveRegionCollider, new GUIContent("离开区域(碰撞体)", "Main Camera 的 XZ 离开此碰撞体时触发结束"));
                }
            }
            else if (type == TriggerType.Touch)
            {
                EditorGUILayout.PropertyField(_touchInteractable, new GUIContent("触摸交互体 (XRBaseInteractable)", "拖入 XRBaseInteractable。布局模式走 XRI Select/Hover；体验模式会复用这组 Collider 自动切到 AutoHand 真手触摸。"));
                if (HasExternalTouchInteractableBinding())
                {
                    EditorGUILayout.HelpBox("触摸交互体必须属于当前模块节点或其子节点。", MessageType.Warning);
                    if (GUILayout.Button("清空无效触摸交互体"))
                        _touchInteractable.objectReferenceValue = null;
                }
            }
            else if (type == TriggerType.TouchGuide)
            {
                EditorGUILayout.HelpBox("自动使用全局导游（默认对象名 Guider）上的 XRBaseInteractable 作为触摸触发源。", MessageType.Info);

                bool drawSharedGuideTouchConfig = isStart || ReadTriggerType(_startTriggerType) != TriggerType.TouchGuide;
                if (drawSharedGuideTouchConfig && _guideTouchPlayAnimationOnTouch != null)
                {
                    EditorGUILayout.PropertyField(_guideTouchPlayAnimationOnTouch, new GUIContent("触摸导游时播放动画"));
                    if (_guideTouchPlayAnimationOnTouch.boolValue)
                        EditorGUILayout.PropertyField(_guideTouchAnimationStateOrTrigger, new GUIContent("触摸动画状态/Trigger"));
                }
                else
                {
                    EditorGUILayout.HelpBox("触摸导游动画配置与开始阶段共用。", MessageType.None);
                }
            }
            else if (type == TriggerType.LongPress)
            {
                if (isStart)
                {
                    EditorGUILayout.PropertyField(_startLongPressInteractable, new GUIContent("开始长按交互体", "按住此交互体满「按住时长」后触发开始，松开可触发结束（若结束也为长按且同一交互体）"));
                    EditorGUILayout.PropertyField(_startLongPressHoldSec, new GUIContent("按住时长(秒)"));
                }
                else
                {
                    EditorGUILayout.PropertyField(_endLongPressInteractable, new GUIContent("结束长按交互体", "开始触发后按住此交互体满「按住时长」即触发结束"));
                    EditorGUILayout.PropertyField(_endLongPressHoldSec, new GUIContent("按住时长(秒)", "按住结束交互体达到此时长后触发结束"));
                }
            }
        }

        private void DrawDragReleaseParamsBody(DragReleaseType type)
        {
            switch (type)
            {
                case DragReleaseType.ReturnHome:
                    EditorGUILayout.PropertyField(_dragReturnHomeRecordOnRelease, new GUIContent("释放时重记锚点"));
                    break;
                case DragReleaseType.Snap:
                    EditorGUILayout.PropertyField(_dragSnapToGrid, new GUIContent("对齐网格"));
                    EditorGUILayout.PropertyField(_dragSnapGridSize, new GUIContent("网格大小"));
                    EditorGUILayout.PropertyField(_dragSnapZeroVelocity, new GUIContent("清零速度"));
                    break;
                case DragReleaseType.PhysicsCollision:
                    EditorGUILayout.PropertyField(_dragPhysicsAddRigidbodyIfMissing, new GUIContent("无刚体时添加"));
                    EditorGUILayout.PropertyField(_dragPhysicsUseGravity, new GUIContent("启用重力"));
                    break;
            }
        }

    }
}
