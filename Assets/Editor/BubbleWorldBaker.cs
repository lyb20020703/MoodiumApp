using System.IO;
using Moodium.NatureWorld;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Moodium ▸ Bubble World ▸ Bake Meshes: for every world folder (Resources/MoodiumBubbleWorld/ for Nature,
/// Resources/MoodiumBubbleWorld/Candy/ for Candy) turns points.bytes into 20 point-layer mesh assets plus the sky sphere
/// under that folder's Meshes/. PolySpatial mirrors imported mesh assets reliably on device; runtime-built meshes are
/// only a fallback. Re-run after replacing a points.bytes.
/// </summary>
public static class BubbleWorldBaker
{
    static readonly string[] Folders = { BubbleWorldData.NatureFolder, BubbleWorldData.CandyFolder,
        BubbleWorldData.NestedNatureFolder, BubbleWorldData.NestedCandyFolder, BubbleWorldData.MapleNatureFolder };

    [MenuItem("Moodium/Bubble World/Bake Both Nested Worlds")]
    public static void BakeBothNested()
    {
        // Re-exporting nested frames must never rebuild the accepted first-layer assets.
        BakeNestedNature();
        BakeNestedCandy();
    }

    [MenuItem("Moodium/Bubble World/Bake Candy Meshes")]
    public static void BakeCandy()
    {
        try
        {
            if (!BakeFolder("Assets/Resources/" + BubbleWorldData.CandyFolder))
                throw new System.InvalidOperationException("Candy point cloud could not be baked.");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    [MenuItem("Moodium/Bubble World/Bake Nature Meshes")]
    public static void BakeNature()
    {
        try
        {
            if (!BakeFolder("Assets/Resources/" + BubbleWorldData.NatureFolder))
                throw new System.InvalidOperationException("Nature point cloud could not be baked.");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    [MenuItem("Moodium/Bubble World/Bake Nested Nature Meshes")]
    public static void BakeNestedNature()
    {
        try
        {
            if (!BakeFolder("Assets/Resources/" + BubbleWorldData.NestedNatureFolder))
                throw new System.InvalidOperationException("Nested Nature point cloud could not be baked.");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    [MenuItem("Moodium/Bubble World/Bake Maple Nature Meshes")]
    public static void BakeMapleNature()
    {
        try
        {
            if (!BakeFolder("Assets/Resources/" + BubbleWorldData.MapleNatureFolder))
                throw new System.InvalidOperationException("Maple Nature point cloud could not be baked.");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    [MenuItem("Moodium/Bubble World/Bake Nested Candy Meshes")]
    public static void BakeNestedCandy()
    {
        try
        {
            if (!BakeFolder("Assets/Resources/" + BubbleWorldData.NestedCandyFolder))
                throw new System.InvalidOperationException("Nested Candy point cloud could not be baked.");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    [MenuItem("Moodium/Bubble World/Bake Meshes")]
    public static void Bake()
    {
        var baked = 0;
        try
        {
            foreach (var folder in Folders)
                if (BakeFolder("Assets/Resources/" + folder)) baked++;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        if (baked == 0)
            EditorUtility.DisplayDialog("Bubble World", "No points.bytes found under Assets/Resources/MoodiumBubbleWorld/ or .../Candy/.", "OK");
    }

    static bool BakeFolder(string folder)
    {
        var points = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "points.bytes");
        if (points == null) return false;
        if (!BubbleWorldData.TryParse(points.bytes, out var cloud))
        {
            Debug.LogError($"[Bubble World] {folder}points.bytes is not a valid MSPC v1 file.");
            return false;
        }
        var meshFolder = folder + "Meshes/";
        Directory.CreateDirectory(meshFolder);
        var atlas = BubbleWorldData.BuildRoundAtlas(cloud);
        var atlasPath = folder + "point_colors_round.png";
        File.WriteAllBytes(atlasPath, atlas.EncodeToPNG());
        Object.DestroyImmediate(atlas);
        AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceSynchronousImport);
        for (var layer = 0; layer < BubbleWorldData.LayerCount; layer++)
        {
            EditorUtility.DisplayProgressBar("Bubble World", $"{folder} · layer {layer + 1}/{BubbleWorldData.LayerCount}",
                layer / (float)BubbleWorldData.LayerCount);
            BubbleWorldData.LayerRange(layer, cloud.Count, BubbleWorldData.LayerCount, out var s, out var e);
            var mesh = BubbleWorldData.BuildLayerMesh(cloud, s, e, BubbleWorldData.LayerMeshName(layer),
                candy: folder == "Assets/Resources/" + BubbleWorldData.CandyFolder || folder == "Assets/Resources/" + BubbleWorldData.NestedCandyFolder);
            Save(mesh, meshFolder + mesh.name + ".asset");
        }
        Save(BubbleWorldData.BuildPanoramaSphere(), meshFolder + BubbleWorldData.PanoramaMeshName + ".asset");
        var radii = BubbleWorldData.LayerOuterRadii(cloud, BubbleWorldData.LayerCount);
        Debug.Log($"[Bubble World] {folder}: baked {cloud.Count} points into {BubbleWorldData.LayerCount} layers " +
                  $"(the small bubble shows {BubbleWorldController.MiniLayerCount(radii, BubbleWorldController.MiniWorldRadius)} layers, " +
                  $"farthest {cloud.MaxRadius:0.0} m, eye height {cloud.EyeHeight:0.00} m).");
        return true;
    }

    static void Save(Mesh mesh, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
        }
        else
        {
            AssetDatabase.CreateAsset(mesh, path);
        }
    }
}
