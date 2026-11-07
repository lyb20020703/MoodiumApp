using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Moodium.NatureWorld;
using UnityEditor;
using UnityEngine;

public static class BubbleSplatBakeValidation
{
    static readonly string[] Folders = { BubbleWorldData.NatureFolder, BubbleWorldData.CandyFolder,
        BubbleWorldData.NestedNatureFolder, BubbleWorldData.NestedCandyFolder, BubbleWorldData.MapleNatureFolder };
    static string GeometryHash(Mesh mesh)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        foreach (var p in mesh.vertices) { writer.Write(p.x); writer.Write(p.y); writer.Write(p.z); }
        foreach (var uv in mesh.uv) { writer.Write(uv.x); writer.Write(uv.y); }
        foreach (var index in mesh.triangles) writer.Write(index);
        writer.Flush();
        using var sha = SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(stream.ToArray()));
    }
    static string Path(string folder, int layer) => "Assets/Resources/" + BubbleWorldData.MeshResource(folder, BubbleWorldData.LayerMeshName(layer)) + ".asset";
    public static void BakeAndValidate()
    {
        var previous = new Dictionary<string, (string guid, string hash)>();
        foreach (var folder in Folders)
        for (var layer = 0; layer < BubbleWorldData.LayerCount; layer++)
        {
            var path = Path(folder, layer); var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) throw new InvalidOperationException("Missing existing layer: " + path);
            previous[path] = (AssetDatabase.AssetPathToGUID(path), GeometryHash(mesh));
        }
        BubbleWorldBaker.Bake();
        foreach (var pair in previous)
        {
            if (AssetDatabase.AssetPathToGUID(pair.Key) != pair.Value.guid ||
                GeometryHash(AssetDatabase.LoadAssetAtPath<Mesh>(pair.Key)) != pair.Value.hash)
                throw new InvalidOperationException("Bake changed original geometry, UV0, indices or GUID: " + pair.Key);
        }
        Validate();
        Debug.Log("[Bubble Motion] All 80 layers rebaked; original positions, UV0, indices and GUIDs preserved.");
    }
    public static void Validate()
    {
        long total = 0;
        foreach (var folder in Folders)
        {
            var bytes = Resources.Load<TextAsset>(BubbleWorldData.PointsResource(folder));
            if (bytes == null || !BubbleWorldData.TryParse(bytes.bytes, out var cloud)) throw new InvalidOperationException(folder);
            var candy = BubbleWorldData.IsCandy(folder);
            for (var layer = 0; layer < BubbleWorldData.LayerCount; layer++)
            {
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Path(folder, layer));
                BubbleWorldData.LayerRange(layer, cloud.Count, BubbleWorldData.LayerCount, out var start, out var end);
                var uv = mesh.uv2; var colors = mesh.colors; var vertices = mesh.vertices;
                if (vertices.Length != (end - start) * 4 || uv.Length != vertices.Length || colors.Length != vertices.Length)
                    throw new InvalidOperationException("Incomplete motion channels: " + mesh.name);
                var raw = new Bounds(vertices[0], Vector3.zero);
                for (var i = 0; i < vertices.Length; i++)
                {
                    raw.Encapsulate(vertices[i]); var point = start + i / 4;
                    var expected = new Vector2(BubbleWorldData.MotionSeed(point), BubbleSplatMotionMath.Sway(cloud.Positions[point], cloud.Colors[point], cloud.EyeHeight, candy));
                    if ((uv[i] - expected).sqrMagnitude > 1e-10f || colors[i] != colors[i / 4 * 4] ||
                        Vector3.Distance(BubbleSplatMotionMath.DecodeCenter(colors[i]), cloud.Positions[point]) > .0001f)
                        throw new InvalidOperationException("Invalid per-splat data: " + mesh.name + " vertex " + i);
                }
                if (Vector3.Distance(mesh.bounds.extents, raw.extents + Vector3.one * .8f) > .0001f ||
                    Vector3.Distance(mesh.bounds.center, raw.center) > .0001f)
                    throw new InvalidOperationException("Incorrect expanded bounds: " + mesh.name);
                total += end - start;
            }
        }
        Debug.Log("[Bubble Motion] Validated 80 mesh assets, " + total + " splats: UV1, float center colors and 0.8 m bounds padding.");
    }
}
