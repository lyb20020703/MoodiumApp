using Moodium.Flow;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MoodiumWorldSetup
{
    const string ScenePath = "Assets/Samples/PolySpatial/Scenes/Meshing 1.unity";
    const string WorldFolder = "Assets/Data/Moodium/Worlds";
    const string CandyWorldPath = WorldFolder + "/CandyWorld.asset";

    static readonly string[] CandyPrefabPaths =
    {
        "Assets/prefab/ChocolatePrefab.prefab",
        "Assets/prefab/Chocolate_Capsule.prefab",
        "Assets/prefab/CookiePrefab.prefab",
        "Assets/prefab/HeartPrefab.prefab",
        "Assets/prefab/Macaron.prefab",
        "Assets/prefab/StarPrefab.prefab"
    };

    [MenuItem("Moodium/Setup Candy World")]
    public static void Setup()
    {
        EnsureFolder("Assets/Data");
        EnsureFolder("Assets/Data/Moodium");
        EnsureFolder(WorldFolder);

        var candyWorld = AssetDatabase.LoadAssetAtPath<MoodiumWorldDefinition>(CandyWorldPath);
        if (candyWorld == null)
        {
            candyWorld = ScriptableObject.CreateInstance<MoodiumWorldDefinition>();
            AssetDatabase.CreateAsset(candyWorld, CandyWorldPath);
        }

        var serializedWorld = new SerializedObject(candyWorld);
        serializedWorld.FindProperty("m_WorldId").stringValue = "candy";
        serializedWorld.FindProperty("m_DisplayName").stringValue = "Candy World";
        serializedWorld.FindProperty("m_Description").stringValue = "A sweet, light sensory experience";
        serializedWorld.FindProperty("m_Icon").objectReferenceValue = null;

        var prefabs = serializedWorld.FindProperty("m_Prefabs");
        prefabs.arraySize = CandyPrefabPaths.Length;
        for (var i = 0; i < CandyPrefabPaths.Length; i++)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CandyPrefabPaths[i]);
            if (prefab == null)
                throw new System.IO.FileNotFoundException("Candy World prefab was not found.", CandyPrefabPaths[i]);
            prefabs.GetArrayElementAtIndex(i).objectReferenceValue = prefab;
        }

        serializedWorld.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(candyWorld);

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var flow = Object.FindFirstObjectByType<MoodiumAppFlowController>();
        if (flow == null)
            throw new System.InvalidOperationException("MoodiumAppFlowController was not found in Meshing 1.");

        var serializedFlow = new SerializedObject(flow);
        var worlds = serializedFlow.FindProperty("m_Worlds");
        worlds.arraySize = 1;
        worlds.GetArrayElementAtIndex(0).objectReferenceValue = candyWorld;
        serializedFlow.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(flow);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"MOODIUM_CANDY_WORLD_READY={CandyWorldPath}; prefabs={CandyPrefabPaths.Length}");
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        var slash = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
