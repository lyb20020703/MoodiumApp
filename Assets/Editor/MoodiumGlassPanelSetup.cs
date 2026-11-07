using System.Collections.Generic;
using System.IO;
using Moodium.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class MoodiumGlassPanelSetup
{
    const string ScenePath = "Assets/Samples/PolySpatial/Scenes/Meshing 1.unity";
    const string PrefabFolder = "Assets/Prefab/UI";
    const string PrefabPath = PrefabFolder + "/Moodium_GlassPanel.prefab";
    const string MeshFolder = PrefabFolder + "/Meshes";
    const string SurfaceMeshPath = MeshFolder + "/Moodium_GlassPanel_Surface.asset";
    const string BorderMeshPath = MeshFolder + "/Moodium_GlassPanel_Border.asset";
    const string MaterialFolder = "Assets/Materials";
    const string GlassMaterialPath = MaterialFolder + "/Moodium_Glass_UI.mat";
    const string EdgeMaterialPath = MaterialFolder + "/Moodium_Glass_UI_Edge.mat";
    const string BlurShaderPath = "Assets/Samples/PolySpatial/HoverComponent/ShaderGraphs/BlurredBackground.shadergraph";
    const string FontAssetPath = "Assets/UI/Moodium/Fonts/Inter-Regular SDF.asset";
    const string PreviewName = "Moodium Glass Panel - Visual Test";

    [MenuItem("Moodium/Setup Glass Panel Test")]
    public static void Setup()
    {
        EnsureFolder(PrefabFolder);
        EnsureFolder(MeshFolder);
        EnsureFolder(MaterialFolder);

        var surfaceMesh = ReplaceMeshAsset(SurfaceMeshPath, CreateRoundedRectMesh(0.72f, 0.43f, 0.055f, 10));
        var borderMesh = ReplaceMeshAsset(BorderMeshPath, CreateRoundedBorderMesh(0.72f, 0.43f, 0.055f, 0.006f, 10));
        var glassMaterial = CreateGlassMaterial();
        var edgeMaterial = CreateEdgeMaterial();
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (font == null)
            throw new FileNotFoundException("Moodium TMP font asset is missing. Run Moodium/Setup Flow Font first.", FontAssetPath);

        var prefabRoot = BuildPrefab(surfaceMesh, borderMesh, glassMaterial, edgeMaterial, font);
        PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
        Object.DestroyImmediate(prefabRoot);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            throw new FileNotFoundException("Moodium Glass Panel Prefab could not be loaded.", PrefabPath);

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var flow = Object.FindFirstObjectByType<Moodium.Flow.MoodiumAppFlowController>();
        if (flow == null)
            throw new System.InvalidOperationException("MoodiumAppFlowController was not found in Meshing 1.");

        var serializedFlow = new SerializedObject(flow);
        serializedFlow.FindProperty("m_GlassPanelPrefab").objectReferenceValue = prefab;
        serializedFlow.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(flow);

        var existing = GameObject.Find(PreviewName);
        if (existing != null)
            Object.DestroyImmediate(existing);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"MOODIUM_GLASS_UI_APPLIED={PrefabPath}; material={GlassMaterialPath}; font={font.name}");
    }

    static GameObject BuildPrefab(Mesh surfaceMesh, Mesh borderMesh, Material glass, Material edge, TMP_FontAsset font)
    {
        var root = new GameObject("Moodium_GlassPanel");

        var surface = new GameObject("Glass Surface");
        surface.transform.SetParent(root.transform, false);
        surface.AddComponent<MeshFilter>().sharedMesh = surfaceMesh;
        var surfaceRenderer = surface.AddComponent<MeshRenderer>();
        surfaceRenderer.sharedMaterial = glass;
        surfaceRenderer.shadowCastingMode = ShadowCastingMode.Off;
        surfaceRenderer.receiveShadows = false;

        var edgeObject = new GameObject("Soft Edge Highlight");
        edgeObject.transform.SetParent(root.transform, false);
        edgeObject.transform.localPosition = new Vector3(0f, 0f, -0.001f);
        edgeObject.AddComponent<MeshFilter>().sharedMesh = borderMesh;
        var edgeRenderer = edgeObject.AddComponent<MeshRenderer>();
        edgeRenderer.sharedMaterial = edge;
        edgeRenderer.shadowCastingMode = ShadowCastingMode.Off;
        edgeRenderer.receiveShadows = false;
        edgeRenderer.sortingOrder = 2;

        CreateText(root.transform, "Moodium", new Vector3(0f, 0.055f, -0.006f), 4.6f, font, new Color(0.97f, 0.98f, 1f, 1f));
        CreateText(root.transform, "Candy World", new Vector3(0f, -0.055f, -0.006f), 2.65f, font, new Color(0.84f, 0.88f, 1f, 1f));
        return root;
    }

    static void CreateText(Transform parent, string value, Vector3 position, float size, TMP_FontAsset font, Color color)
    {
        var textObject = new GameObject("Text - " + value);
        textObject.transform.SetParent(parent, false);
        textObject.transform.localPosition = position;
        textObject.transform.localRotation = Quaternion.identity;
        textObject.transform.localScale = Vector3.one * 0.1f;
        var text = textObject.AddComponent<TextMeshPro>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.fontStyle = value == "Moodium" ? FontStyles.Bold : FontStyles.Normal;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.rectTransform.sizeDelta = new Vector2(6.2f, 0.8f);
        text.renderer.sortingOrder = 10;
    }

    static Material CreateGlassMaterial()
    {
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(BlurShaderPath);
        if (shader == null)
            throw new FileNotFoundException("PolySpatial background blur Shader Graph was not found.", BlurShaderPath);

        var material = AssetDatabase.LoadAssetAtPath<Material>(GlassMaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "Moodium_Glass_UI" };
            AssetDatabase.CreateAsset(material, GlassMaterialPath);
        }
        else
            material.shader = shader;

        material.SetColor("_Tint", new Color(0.56f, 0.67f, 1f, 0.28f));
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", 0.92f);
        material.renderQueue = 3000;
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material CreateEdgeMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            throw new System.InvalidOperationException("URP Lit shader was not found.");

        var material = AssetDatabase.LoadAssetAtPath<Material>(EdgeMaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "Moodium_Glass_UI_Edge" };
            AssetDatabase.CreateAsset(material, EdgeMaterialPath);
        }
        else
            material.shader = shader;

        var color = new Color(0.58f, 0.72f, 1f, 0.58f);
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        material.SetColor("_EmissionColor", new Color(0.18f, 0.28f, 0.65f, 1f));
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", 0.95f);
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_EMISSION");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = 3001;
        EditorUtility.SetDirty(material);
        return material;
    }

    static Mesh ReplaceMeshAsset(string path, Mesh mesh)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        EditorUtility.CopySerialized(mesh, existing);
        Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    static Mesh CreateRoundedRectMesh(float width, float height, float radius, int segments)
    {
        var perimeter = BuildPerimeter(width, height, radius, segments);
        var vertices = new List<Vector3> { Vector3.zero };
        vertices.AddRange(perimeter);
        var triangles = new List<int>();
        for (var i = 0; i < perimeter.Count; i++)
        {
            triangles.Add(0);
            triangles.Add(1 + (i + 1) % perimeter.Count);
            triangles.Add(1 + i);
        }
        return CreateMesh("Moodium Glass Rounded Surface", vertices, triangles);
    }

    static Mesh CreateRoundedBorderMesh(float width, float height, float radius, float thickness, int segments)
    {
        var outer = BuildPerimeter(width, height, radius, segments);
        var inner = BuildPerimeter(width - thickness * 2f, height - thickness * 2f, Mathf.Max(0.001f, radius - thickness), segments);
        var vertices = new List<Vector3>();
        vertices.AddRange(outer);
        vertices.AddRange(inner);
        var triangles = new List<int>();
        var count = outer.Count;
        for (var i = 0; i < count; i++)
        {
            var next = (i + 1) % count;
            triangles.Add(i);
            triangles.Add(next);
            triangles.Add(count + i);
            triangles.Add(next);
            triangles.Add(count + next);
            triangles.Add(count + i);
        }
        return CreateMesh("Moodium Glass Soft Border", vertices, triangles);
    }

    static List<Vector3> BuildPerimeter(float width, float height, float radius, int segments)
    {
        var points = new List<Vector3>(segments * 4);
        var centers = new[]
        {
            new Vector2(width * 0.5f - radius, height * 0.5f - radius),
            new Vector2(-width * 0.5f + radius, height * 0.5f - radius),
            new Vector2(-width * 0.5f + radius, -height * 0.5f + radius),
            new Vector2(width * 0.5f - radius, -height * 0.5f + radius)
        };
        var starts = new[] { 0f, 90f, 180f, 270f };
        for (var corner = 0; corner < 4; corner++)
        for (var step = 0; step < segments; step++)
        {
            var angle = (starts[corner] + step * 90f / segments) * Mathf.Deg2Rad;
            var p = centers[corner] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            points.Add(new Vector3(p.x, p.y, 0f));
        }
        return points;
    }

    static Mesh CreateMesh(string name, List<Vector3> vertices, List<int> triangles)
    {
        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        var normals = new Vector3[vertices.Count];
        for (var i = 0; i < normals.Length; i++)
            normals[i] = Vector3.back;
        mesh.normals = normals;
        mesh.RecalculateBounds();
        return mesh;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
