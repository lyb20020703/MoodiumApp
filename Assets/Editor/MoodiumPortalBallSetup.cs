using UnityEditor;
using UnityEngine;
using Moodium.Opening;

public static class MoodiumPortalBallSetup
{
    const string Folder = "Assets/prefab";
    const string PortalPrefab = "Assets/prefab/MoodiumOpening/MoodiumPortalEntrance.prefab";

    [MenuItem("Moodium/Setup Portal Sensory Balls")]
    public static void Setup()
    {
        var elastic = CreateBall("MoodiumElasticBall", new Color(0.86f, 0.28f, 0.82f, 1f), 0.82f, 0.05f, false);
        var soft = CreateBall("MoodiumSoftGelBall", new Color(0.96f, 0.98f, 1f, 0.52f), 0.7f, 0f, true);
        var root = PrefabUtility.LoadPrefabContents(PortalPrefab);
        var controller = root.GetComponentInChildren<PortalFallingBagController>(true);
        var serialized = new SerializedObject(controller);
        var prefabs = serialized.FindProperty("m_BagPrefabs");
        var old = prefabs.arraySize;
        prefabs.arraySize = 41;
        var cursor = 0;
        for (var bag = 0; bag < 15; bag++)
        {
            prefabs.GetArrayElementAtIndex(cursor++).objectReferenceValue =
                prefabs.GetArrayElementAtIndex(bag % 3).objectReferenceValue;
            prefabs.GetArrayElementAtIndex(cursor++).objectReferenceValue =
                (bag % 2 == 0) ? elastic : soft;
            if (bag < 11)
                prefabs.GetArrayElementAtIndex(cursor++).objectReferenceValue =
                    (bag % 2 == 0) ? soft : elastic;
        }
        serialized.FindProperty("m_ConcurrentCount").intValue = 41;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(root, PortalPrefab);
        PrefabUtility.UnloadPrefabContents(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    static GameObject CreateBall(string name, Color color, float smoothness, float metallic, bool transparent)
    {
        var path = $"{Folder}/{name}.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
            return existing;
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader) { name = name + " Material" };
        material.SetColor(material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (transparent && material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.renderQueue = 3000;
        }
        var materialPath = $"{Folder}/{name}_Mat.mat";
        AssetDatabase.CreateAsset(material, materialPath);
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = name;
        // PortalFallingBagController applies its common 0.15–0.18 scale range.
        // A 0.4m source sphere therefore lands around 6–7cm in the Portal.
        ball.transform.localScale = Vector3.one * 0.7f;
        ball.GetComponent<Renderer>().sharedMaterial = material;
        var collider = ball.GetComponent<SphereCollider>();
        collider.sharedMaterial = new PhysicsMaterial(name + " Physics") { bounciness = 0.45f, dynamicFriction = 0.25f, staticFriction = 0.25f, frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Maximum };
        var prefab = PrefabUtility.SaveAsPrefabAsset(ball, path);
        Object.DestroyImmediate(ball);
        return prefab;
    }
}
