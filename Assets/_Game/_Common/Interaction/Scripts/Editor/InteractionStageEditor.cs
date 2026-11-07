using UnityEditor;
using UnityEngine;
using Interaction;

namespace Interaction.Editor
{
    [CustomEditor(typeof(InteractionStage))]
    public class InteractionStageEditor : UnityEditor.Editor
    {
        private SerializedProperty _stageModuleId;
        private SerializedProperty _stageEffectRoot;
        private SerializedProperty _stageSdfAnchor;
        private SerializedProperty _stageSdfSwitchTransitionDurationSeconds;
        private SerializedProperty _stageSdfSwitchTurbulenceEnabled;
        private SerializedProperty _stageSdfSwitchTurbulenceDistanceFactor;
        private SerializedProperty _stageSdfSwitchTurbulenceMaxOffset;
        private SerializedProperty _stageSdfSwitchTurbulenceFrequency;
        private SerializedProperty _stageSdfSwitchTurbulenceRotationDegrees;

        private void OnEnable()
        {
            _stageModuleId = serializedObject.FindProperty("stageModuleId");
            _stageEffectRoot = serializedObject.FindProperty("stageEffectRoot");
            _stageSdfAnchor = serializedObject.FindProperty("stageSdfAnchor");
            _stageSdfSwitchTransitionDurationSeconds = serializedObject.FindProperty("stageSdfSwitchTransitionDurationSeconds");
            _stageSdfSwitchTurbulenceEnabled = serializedObject.FindProperty("stageSdfSwitchTurbulenceEnabled");
            _stageSdfSwitchTurbulenceDistanceFactor = serializedObject.FindProperty("stageSdfSwitchTurbulenceDistanceFactor");
            _stageSdfSwitchTurbulenceMaxOffset = serializedObject.FindProperty("stageSdfSwitchTurbulenceMaxOffset");
            _stageSdfSwitchTurbulenceFrequency = serializedObject.FindProperty("stageSdfSwitchTurbulenceFrequency");
            _stageSdfSwitchTurbulenceRotationDegrees = serializedObject.FindProperty("stageSdfSwitchTurbulenceRotationDegrees");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("展台绑定", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("展台请挂载 InteractionStage，并在这里指定被外部模块驱动的内容根。", MessageType.Info);

            EditorGUILayout.PropertyField(
                _stageModuleId,
                new GUIContent("展台ID", "可选。为空时会优先读取父级 ExhibitInfo.moduleId。"));
            EditorGUILayout.PropertyField(
                _stageEffectRoot,
                new GUIContent("展台内容", "普通模块以“展台”为目标时，效果会作用到这个内容。"));

            if (_stageEffectRoot != null && _stageEffectRoot.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("未配置展台内容。外部模块虽然能找到该展台，但不会有可执行的展台效果目标。", MessageType.Warning);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("SDF特效", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("“播放SDF特效”只会查找并播放你已经挂好的 SDF_LineModel / VisualEffect，不会自动创建。", MessageType.Info);

            EditorGUILayout.PropertyField(
                _stageSdfAnchor,
                new GUIContent("SDF挂点", "可选。优先从这个节点及其子节点查找现成的 SDF_LineModel；为空时默认在“展台内容”下查找。"));
            EditorGUILayout.HelpBox("请把 VFX Graph_SDF Effect 插件里的 SDF_LineModel 直接挂在展台层级里，并在那个 VisualEffect 组件上完成 SDF 资源和参数配置。运行时首次启用展台时，展台内容会先隐藏；SDF 节点在每次启用展台时都会先被自动隐藏，等收到“播放SDF特效”再显示。SDF 的位置/旋转/缩放请在普通模块的那条“播放SDF特效”动作里配置。", MessageType.None);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("SDF切换过渡", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(
                _stageSdfSwitchTransitionDurationSeconds,
                new GUIContent("过渡时长", "SDF 在两个目标之间切换时的平滑移动时长。"));
            EditorGUILayout.PropertyField(
                _stageSdfSwitchTurbulenceEnabled,
                new GUIContent("启用湍流", "只在 SDF 切换目标的移动期间叠加湍流扰动。"));

            if (_stageSdfSwitchTurbulenceEnabled != null && _stageSdfSwitchTurbulenceEnabled.boolValue)
            {
                EditorGUILayout.PropertyField(
                    _stageSdfSwitchTurbulenceDistanceFactor,
                    new GUIContent("摆动比例", "湍流横向摆动相对于移动距离的比例。"));
                EditorGUILayout.PropertyField(
                    _stageSdfSwitchTurbulenceMaxOffset,
                    new GUIContent("最大偏移", "湍流横向摆动的最大世界坐标偏移。"));
                EditorGUILayout.PropertyField(
                    _stageSdfSwitchTurbulenceFrequency,
                    new GUIContent("扰动速度", "湍流扰动随时间变化的速度。"));
                EditorGUILayout.PropertyField(
                    _stageSdfSwitchTurbulenceRotationDegrees,
                    new GUIContent("旋转晃动", "湍流带来的额外旋转晃动角度。"));
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
