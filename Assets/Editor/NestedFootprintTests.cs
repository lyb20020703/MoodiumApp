using System;
using System.IO;
using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The second-layer worlds used to be exported with a point-size rule that made their points about 2x larger in view
/// than the first layer's (tools/convert_gaussian_to_bubble_world.py --footprint legacy). These keep the two layers matched.
/// </summary>
public sealed class NestedFootprintTests
{
    const float StandingEye = 1.5f;

    static BubbleWorldData.Cloud Load(string folder)
    {
        var path = "Assets/Resources/" + folder + "points.bytes";
        Assert.That(BubbleWorldData.TryParse(File.ReadAllBytes(path), out var cloud), Is.True, path);
        return cloud;
    }

    /// <summary>Median point size in arcminutes, seen from a standing eye above the world's floor.</summary>
    static float MedianArcminutes(BubbleWorldData.Cloud cloud)
    {
        var eye = new Vector3(0f, StandingEye - cloud.EyeHeight, 0f);
        var angles = new float[cloud.Count];
        for (var i = 0; i < cloud.Count; i++)
            angles[i] = cloud.Sizes[i] / Mathf.Max(.01f, (cloud.Positions[i] - eye).magnitude) * Mathf.Rad2Deg * 60f;
        Array.Sort(angles);
        return angles[angles.Length / 2];
    }

    [TestCase(BubbleWorldData.NatureFolder, BubbleWorldData.NestedNatureFolder)]
    [TestCase(BubbleWorldData.CandyFolder, BubbleWorldData.NestedCandyFolder)]
    [TestCase(BubbleWorldData.NatureFolder, BubbleWorldData.MapleNatureFolder)]
    public void SecondLayerPointsLookAsFineAsTheFirstLayer(string first, string second)
    {
        var a = MedianArcminutes(Load(first));
        var b = MedianArcminutes(Load(second));
        Assert.That(b, Is.LessThan(a * 1.25f), $"first layer {a:0.0}′, second layer {b:0.0}′");
    }

    [TestCase(BubbleWorldData.NestedNatureFolder)]
    [TestCase(BubbleWorldData.NestedCandyFolder)]
    [TestCase(BubbleWorldData.MapleNatureFolder)]
    public void BakedMeshesMatchTheExportedPoints(string folder)
    {
        // Points are sorted near → far and layer 00 starts at point 0; its first quad's edge is that point's size.
        var cloud = Load(folder);
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Resources/" + BubbleWorldData.MeshResource(folder, BubbleWorldData.LayerMeshName(0)) + ".asset");
        Assert.That(mesh, Is.Not.Null);
        var v = mesh.vertices;
        Assert.That((v[1] - v[0]).magnitude, Is.EqualTo(cloud.Sizes[0]).Within(1e-4f),
            "points.bytes changed but the meshes were not re-baked: Moodium > Bubble World > Bake Both Nested Worlds");
        Assert.That(((v[0] + v[2]) * .5f - cloud.Positions[0]).magnitude, Is.LessThan(1e-4f));
    }
}
