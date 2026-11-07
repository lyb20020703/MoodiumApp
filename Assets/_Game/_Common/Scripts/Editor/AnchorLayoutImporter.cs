using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using UnityEngine.XR.ARFoundation;
using VFXViewer; // 引用命名空间

public class AnchorLayoutImporter : EditorWindow
{
    [MenuItem("工具/导入布局（预览）")]
    public static void ShowWindow()
    {
        GetWindow<AnchorLayoutImporter>("Layout Importer");
    }

    private string jsonFilePath = "";
    private ChapterPlacementPlan placementPlan;
    private readonly Dictionary<string, ChapterPlacementStep> stepByModuleId = new Dictionary<string, ChapterPlacementStep>();

    void OnGUI()
    {
        GUILayout.Label("博物馆布局预览 (相对坐标)", EditorStyles.boldLabel);
        GUILayout.Space(10);

        if (GUILayout.Button("选择 museum_layout.json"))
        {
            jsonFilePath = EditorUtility.OpenFilePanel("Select Layout", Application.persistentDataPath, "json");
        }
        GUILayout.Label("路径: " + (string.IsNullOrEmpty(jsonFilePath) ? "未选择" : "..." + Path.GetFileName(jsonFilePath)));

        GUILayout.Space(10);
        placementPlan = (ChapterPlacementPlan)EditorGUILayout.ObjectField("布展清单", placementPlan, typeof(ChapterPlacementPlan), false);
        GUILayout.Space(20);

        EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(jsonFilePath));
        if (GUILayout.Button("生成预览场景")) ImportLayout();
        EditorGUI.EndDisabledGroup();
    }

    void ImportLayout()
    {
        RebuildStepLookup();

        string json = File.ReadAllText(jsonFilePath);
        LayoutDataList data = JsonUtility.FromJson<LayoutDataList>(json);

        if (data == null || data.items == null) return;

        string rootName = "--- Museum Preview ---";
        GameObject container = GameObject.Find(rootName);
        if (container != null) DestroyImmediate(container);
        container = new GameObject(rootName);
        
        GameObject editorRootObj = new GameObject("Root Reference");
        editorRootObj.transform.SetParent(container.transform);

        foreach (var item in data.items)
        {
            GameObject child = SpawnObj(item, editorRootObj.transform, false);
            child.transform.localPosition = item.relativePosition;
            child.transform.localRotation = item.relativeRotation;
            child.transform.localScale = item.scale;
        }

        Selection.activeGameObject = container;
        Debug.Log($"导入成功：{data.items.Count} 个展品");
    }

    GameObject SpawnObj(LayoutData item, Transform parent, bool isRoot)
    {
        GameObject obj = null;
        if (stepByModuleId.TryGetValue(NormalizeModuleId(item.moduleId), out var step) && step != null && step.prefab != null)
        {
            obj = (GameObject)PrefabUtility.InstantiatePrefab(step.prefab);
        }
        if (obj == null) {
            obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.transform.localScale = Vector3.one * 0.2f;
        }
        
        obj.transform.SetParent(parent);
        obj.name = $"[{item.instanceID}] {item.displayName}" + (isRoot ? " [ROOT]" : "");
        
        var anchor = obj.GetComponent<ARAnchor>();
        if (anchor) DestroyImmediate(anchor);
        
        return obj;
    }

    private void RebuildStepLookup()
    {
        stepByModuleId.Clear();
        if (placementPlan == null || placementPlan.chapters == null) return;

        foreach (var chapter in placementPlan.chapters)
        {
            if (chapter == null || chapter.steps == null) continue;
            foreach (var step in chapter.steps)
            {
                if (step == null || step.prefab == null) continue;
                string moduleId = NormalizeModuleId(step.moduleId);
                if (string.IsNullOrEmpty(moduleId) || stepByModuleId.ContainsKey(moduleId)) continue;
                stepByModuleId.Add(moduleId, step);
            }
        }
    }

    private static string NormalizeModuleId(string moduleId)
    {
        return string.IsNullOrWhiteSpace(moduleId) ? string.Empty : moduleId.Trim();
    }
}
