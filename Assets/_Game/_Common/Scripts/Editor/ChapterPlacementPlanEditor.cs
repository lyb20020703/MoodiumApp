using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VFXViewer.Editor
{
    [CustomEditor(typeof(ChapterPlacementPlan))]
    public class ChapterPlacementPlanEditor : UnityEditor.Editor
    {
        private SerializedProperty m_RootSettingsProp;
        private SerializedProperty m_ChaptersProp;
        private SerializedProperty m_StageModulesProp;
        private readonly List<bool> m_ChapterFoldouts = new List<bool>();
        private GUIStyle m_GroupBoxStyle;
        private Texture2D m_GroupBoxBackground;

        private void OnEnable()
        {
            m_RootSettingsProp = serializedObject.FindProperty("rootSettings");
            m_ChaptersProp = serializedObject.FindProperty("chapters");
            m_StageModulesProp = serializedObject.FindProperty("stageModules");
        }

        private void OnDisable()
        {
            if (m_GroupBoxBackground != null)
            {
                DestroyImmediate(m_GroupBoxBackground);
                m_GroupBoxBackground = null;
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (m_ChaptersProp == null)
            {
                DrawDefaultInspector();
                return;
            }

            EnsureChapterFoldoutsCapacity();

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Chapter Placement Plan", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            DrawModuleIdValidation();
            DrawBaselineSection();
            DrawExhibitSection();
            DrawStageSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawModuleIdValidation()
        {
            var duplicates = CollectDuplicateModuleIds();
            if (duplicates.Count == 0)
                return;

            EditorGUILayout.HelpBox(
                $"moduleId 重复：{string.Join("、", duplicates)}\n展台与章节步骤的 moduleId 必须全局唯一。",
                MessageType.Error);
            EditorGUILayout.Space(6f);
        }

        private void DrawChapter(int chapterIndex)
        {
            var chapterProp = m_ChaptersProp.GetArrayElementAtIndex(chapterIndex);
            if (chapterProp == null) return;

            var chapterIdProp = chapterProp.FindPropertyRelative("chapterId");
            var chapterNameProp = chapterProp.FindPropertyRelative("chapterName");
            var stepsProp = chapterProp.FindPropertyRelative("steps");

            string chapterId = chapterIdProp != null ? chapterIdProp.stringValue : string.Empty;
            string chapterName = chapterNameProp != null ? chapterNameProp.stringValue : string.Empty;
            string chapterTitle = string.IsNullOrWhiteSpace(chapterName) ? chapterId : $"{chapterId} - {chapterName}";
            if (string.IsNullOrWhiteSpace(chapterTitle))
                chapterTitle = $"Chapter {chapterIndex + 1}";

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            m_ChapterFoldouts[chapterIndex] = EditorGUILayout.Foldout(
                m_ChapterFoldouts[chapterIndex],
                $"{chapterIndex + 1}. {chapterTitle}",
                true);

            if (m_ChapterFoldouts[chapterIndex])
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(chapterIdProp);
                EditorGUILayout.PropertyField(chapterNameProp);
                EditorGUILayout.Space(2f);

                DrawChapterStepsGrouped(stepsProp, chapterIndex);
                EditorGUILayout.Space(4f);

                if (GUILayout.Button("添加步骤"))
                {
                    int newStepIndex = stepsProp.arraySize;
                    stepsProp.InsertArrayElementAtIndex(newStepIndex);
                }

                if (GUILayout.Button("删除该章节"))
                {
                    m_ChaptersProp.DeleteArrayElementAtIndex(chapterIndex);
                    EditorGUI.indentLevel--;
                    EditorGUILayout.EndVertical();
                    return;
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawChapterStepsGrouped(SerializedProperty stepsProp, int chapterIndex)
        {
            if (stepsProp == null || !stepsProp.isArray) return;
            if (stepsProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox("该章节暂无步骤。", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("步骤列表", EditorStyles.boldLabel);
            EditorGUILayout.Space(2f);

            int i = 0;
            while (i < stepsProp.arraySize)
            {
                var stepProp = stepsProp.GetArrayElementAtIndex(i);
                if (stepProp == null)
                {
                    i++;
                    continue;
                }

                string groupKey = NormalizeGroupKey(stepProp.FindPropertyRelative("groupKey")?.stringValue);
                int groupStart = i;
                int groupEnd = i;
                if (!string.IsNullOrEmpty(groupKey))
                {
                    int j = i + 1;
                    while (j < stepsProp.arraySize)
                    {
                        var nextStep = stepsProp.GetArrayElementAtIndex(j);
                        if (nextStep == null) break;
                        string nextKey = NormalizeGroupKey(nextStep.FindPropertyRelative("groupKey")?.stringValue);
                        if (nextKey != groupKey) break;
                        groupEnd = j;
                        j++;
                    }
                }

                int count = groupEnd - groupStart + 1;
                bool isMultiStepGroup = count > 1;
                if (isMultiStepGroup)
                {
                    string groupLabel = string.IsNullOrEmpty(groupKey)
                        ? $"多步组 ({count})"
                        : $"组 {groupKey} ({count})";

                    EditorGUILayout.BeginVertical(GetGroupBoxStyle());
                    EditorGUILayout.LabelField(groupLabel, EditorStyles.boldLabel);

                    for (int idx = groupStart; idx <= groupEnd; idx++)
                        DrawStep(stepsProp, chapterIndex, idx, boxed: false);

                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space(2f);
                }
                else
                {
                    DrawStep(stepsProp, chapterIndex, groupStart, boxed: false);
                    EditorGUILayout.Space(2f);
                }

                i = groupEnd + 1;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawBaselineSection()
        {
            if (m_RootSettingsProp == null)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("定位", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_RootSettingsProp, includeChildren: true);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6f);
        }

        private void DrawExhibitSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("展品", EditorStyles.boldLabel);

            if (m_ChaptersProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox("未配置展品章节。", MessageType.Info);
            }
            else
            {
                for (int i = 0; i < m_ChaptersProp.arraySize; i++)
                {
                    DrawChapter(i);
                    EditorGUILayout.Space(6f);
                }
            }

            if (GUILayout.Button("添加章节"))
            {
                int newIndex = m_ChaptersProp.arraySize;
                m_ChaptersProp.InsertArrayElementAtIndex(newIndex);
                EnsureChapterFoldoutsCapacity();
                m_ChapterFoldouts[newIndex] = true;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6f);
        }

        private void DrawStageSection()
        {
            if (m_StageModulesProp == null || !m_StageModulesProp.isArray)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("展台", EditorStyles.boldLabel);

            if (m_StageModulesProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox("未配置展台。", MessageType.Info);
            }
            else
            {
                for (int i = 0; i < m_StageModulesProp.arraySize; i++)
                {
                    DrawStageModule(i);
                    EditorGUILayout.Space(2f);
                }
            }

            if (GUILayout.Button("添加展台"))
            {
                int newIndex = m_StageModulesProp.arraySize;
                m_StageModulesProp.InsertArrayElementAtIndex(newIndex);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6f);
        }

        private void DrawStep(SerializedProperty stepsProp, int chapterIndex, int stepIndex, bool boxed)
        {
            if (stepsProp == null || !stepsProp.isArray) return;
            if (stepIndex < 0 || stepIndex >= stepsProp.arraySize) return;

            var stepProp = stepsProp.GetArrayElementAtIndex(stepIndex);
            if (stepProp == null) return;

            var moduleIdProp = stepProp.FindPropertyRelative("moduleId");
            var moduleNameProp = stepProp.FindPropertyRelative("moduleName");
            var groupKeyProp = stepProp.FindPropertyRelative("groupKey");
            var prefabProp = stepProp.FindPropertyRelative("prefab");
            var spawnDistanceProp = stepProp.FindPropertyRelative("spawnDistance");
            var fixedInstanceIdProp = stepProp.FindPropertyRelative("fixedInstanceId");

            if (boxed)
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            string moduleId = moduleIdProp != null ? moduleIdProp.stringValue : string.Empty;
            EditorGUILayout.LabelField($"步骤 {stepIndex + 1}: {moduleId}", EditorStyles.miniBoldLabel);
            if (GUILayout.Button("删除", GUILayout.Width(56f)))
            {
                stepsProp.DeleteArrayElementAtIndex(stepIndex);
                EditorGUILayout.EndHorizontal();
                if (boxed)
                    EditorGUILayout.EndVertical();
                return;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(moduleIdProp, new GUIContent("Module Id"));
            EditorGUILayout.PropertyField(moduleNameProp, new GUIContent("Module Name"));
            EditorGUILayout.PropertyField(groupKeyProp);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(prefabProp);
            if (EditorGUI.EndChangeCheck() && moduleIdProp != null && prefabProp != null)
            {
                var prefab = prefabProp.objectReferenceValue as GameObject;
                if (prefab != null)
                    moduleIdProp.stringValue = prefab.name;
            }
            EditorGUILayout.PropertyField(spawnDistanceProp);
            if (fixedInstanceIdProp != null && !string.IsNullOrEmpty(fixedInstanceIdProp.stringValue))
                fixedInstanceIdProp.stringValue = string.Empty;

            string autoInstanceId = GetAutoInstanceIdByListOrder(chapterIndex, stepIndex);
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField(new GUIContent("fixedInstanceId (自动)", "按 ChapterPlacementPlan 列表顺序自动生成。首项为 0，其后递增。"), autoInstanceId);
            EditorGUI.EndDisabledGroup();
            if (boxed)
                EditorGUILayout.EndVertical();
        }

        private void DrawStageModule(int stageIndex)
        {
            if (m_StageModulesProp == null || !m_StageModulesProp.isArray)
                return;
            if (stageIndex < 0 || stageIndex >= m_StageModulesProp.arraySize)
                return;

            var stageProp = m_StageModulesProp.GetArrayElementAtIndex(stageIndex);
            if (stageProp == null)
                return;

            var moduleIdProp = stageProp.FindPropertyRelative("moduleId");
            var moduleNameProp = stageProp.FindPropertyRelative("moduleName");
            var prefabProp = stageProp.FindPropertyRelative("prefab");
            var spawnDistanceProp = stageProp.FindPropertyRelative("spawnDistance");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            string moduleId = moduleIdProp != null ? moduleIdProp.stringValue : string.Empty;
            EditorGUILayout.LabelField($"展台 {stageIndex + 1}: {moduleId}", EditorStyles.miniBoldLabel);
            if (GUILayout.Button("删除", GUILayout.Width(56f)))
            {
                m_StageModulesProp.DeleteArrayElementAtIndex(stageIndex);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(moduleIdProp, new GUIContent("StageId"));
            EditorGUILayout.PropertyField(moduleNameProp, new GUIContent("StageName"));
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(prefabProp);
            if (EditorGUI.EndChangeCheck() && moduleIdProp != null && prefabProp != null)
            {
                var prefab = prefabProp.objectReferenceValue as GameObject;
                if (prefab != null)
                    moduleIdProp.stringValue = prefab.name;
            }
            EditorGUILayout.PropertyField(spawnDistanceProp);
            EditorGUILayout.EndVertical();
        }

        private string GetAutoInstanceIdByListOrder(int chapterIndex, int stepIndex)
        {
            int flatIndex = 0;
            for (int i = 0; i < chapterIndex; i++)
            {
                var chapter = m_ChaptersProp.GetArrayElementAtIndex(i);
                var steps = chapter?.FindPropertyRelative("steps");
                if (steps != null && steps.isArray)
                    flatIndex += steps.arraySize;
            }

            flatIndex += stepIndex;
            if (flatIndex <= 0) return "0";
            return flatIndex.ToString();
        }

        private static string NormalizeGroupKey(string key)
        {
            return string.IsNullOrWhiteSpace(key) ? string.Empty : key.Trim();
        }

        private List<string> CollectDuplicateModuleIds()
        {
            var duplicates = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (m_ChaptersProp != null && m_ChaptersProp.isArray)
            {
                for (int chapterIndex = 0; chapterIndex < m_ChaptersProp.arraySize; chapterIndex++)
                {
                    var chapterProp = m_ChaptersProp.GetArrayElementAtIndex(chapterIndex);
                    var stepsProp = chapterProp?.FindPropertyRelative("steps");
                    if (stepsProp == null || !stepsProp.isArray)
                        continue;

                    for (int stepIndex = 0; stepIndex < stepsProp.arraySize; stepIndex++)
                    {
                        var stepProp = stepsProp.GetArrayElementAtIndex(stepIndex);
                        string moduleId = stepProp?.FindPropertyRelative("moduleId")?.stringValue;
                        TryRegisterDuplicateId(moduleId, seen, duplicates);
                    }
                }
            }

            if (m_StageModulesProp != null && m_StageModulesProp.isArray)
            {
                for (int stageIndex = 0; stageIndex < m_StageModulesProp.arraySize; stageIndex++)
                {
                    var stageProp = m_StageModulesProp.GetArrayElementAtIndex(stageIndex);
                    string moduleId = stageProp?.FindPropertyRelative("moduleId")?.stringValue;
                    TryRegisterDuplicateId(moduleId, seen, duplicates);
                }
            }

            return duplicates;
        }

        private static void TryRegisterDuplicateId(string moduleId, HashSet<string> seen, List<string> duplicates)
        {
            moduleId = string.IsNullOrWhiteSpace(moduleId) ? string.Empty : moduleId.Trim();
            if (string.IsNullOrEmpty(moduleId))
                return;

            if (seen.Add(moduleId))
                return;

            if (!duplicates.Exists(existing => string.Equals(existing, moduleId, StringComparison.OrdinalIgnoreCase)))
                duplicates.Add(moduleId);
        }

        private GUIStyle GetGroupBoxStyle()
        {
            if (m_GroupBoxStyle != null && m_GroupBoxBackground != null)
                return m_GroupBoxStyle;

            var style = new GUIStyle(EditorStyles.helpBox);
            var color = EditorGUIUtility.isProSkin
                ? new Color(0.17f, 0.22f, 0.32f, 0.95f)
                : new Color(0.84f, 0.91f, 0.98f, 1f);

            m_GroupBoxBackground = CreateColorTexture(color);
            style.normal.background = m_GroupBoxBackground;
            style.onNormal.background = m_GroupBoxBackground;
            m_GroupBoxStyle = style;
            return m_GroupBoxStyle;
        }

        private static Texture2D CreateColorTexture(Color color)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        private void EnsureChapterFoldoutsCapacity()
        {
            while (m_ChapterFoldouts.Count < m_ChaptersProp.arraySize)
                m_ChapterFoldouts.Add(true);
            while (m_ChapterFoldouts.Count > m_ChaptersProp.arraySize)
                m_ChapterFoldouts.RemoveAt(m_ChapterFoldouts.Count - 1);
        }
    }
}
