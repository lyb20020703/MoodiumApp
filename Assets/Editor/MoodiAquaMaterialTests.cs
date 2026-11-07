using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class MoodiAquaMaterialTests
{
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const string MaterialX = "UnityEditor.ShaderGraph.MaterialX.";

    [Test]
    public void ConvertsToMaterialXWithoutUnsupportedNodesOrVertexDisplacement()
    {
        var importer = BubbleSplatGraphBuilder.T(MaterialX + "MtlxPostProcessor");
        var graph = importer.GetMethod("LoadGraphData", Static)
            .Invoke(null, new object[] { MoodiAquaGraphBuilder.GraphPath });
        var converted = importer.GetMethod("ProcessGraph", Static)
            .Invoke(null, new[] { graph, "MoodiAqua", (object)false });
        Assert.That(converted, Is.Not.Null);

        var adapter = BubbleSplatGraphBuilder.T(MaterialX + "AdapterMap");
        var nodes = (IEnumerable)graph.GetType().GetMethods(Instance)
            .Single(method => method.Name == "GetNodes" && method.IsGenericMethod)
            .MakeGenericMethod(BubbleSplatGraphBuilder.T("UnityEditor.ShaderGraph.AbstractMaterialNode"))
            .Invoke(graph, null);
        foreach (var node in nodes)
        {
            if (node.GetType().Name == "BlockNode") continue;
            Assert.That(adapter.GetMethod("IsNodeSupported", Static).Invoke(null, new[] { node }),
                Is.EqualTo(true), node.GetType().Name);
            Assert.That(adapter.GetMethod("GetSupportNotification", Static).Invoke(null, new[] { node }),
                Is.Null.Or.Empty, node.GetType().Name);
        }

        var processor = BubbleSplatGraphBuilder.T(MaterialX + "MtlxGraphProcessor");
        var xml = (string)processor.GetMethod("ProcessGraph", Instance)
            .Invoke(Activator.CreateInstance(processor, true), new[] { converted });
        var root = XDocument.Parse(xml).Root;
        var names = root.Descendants().Attributes("name").Select(name => name.Value).ToHashSet();
        foreach (var reference in root.Descendants().Attributes("nodename"))
            Assert.That(names, Does.Contain(reference.Value));
        // The converter can emit an empty geometry stage for an unconnected vertex context.
        Assert.That(root.Descendants("GeometryModification").Elements(), Is.Empty);
    }

    [Test]
    public void MaterialUsesOriginalAlbedoAndNormalAndIsOpaque()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MoodiAquaGraphBuilder.MaterialPath);
        Assert.That(material, Is.Not.Null);
        Assert.That(material.shader, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Shader>(MoodiAquaGraphBuilder.GraphPath)));
        Assert.That(ShaderUtil.ShaderHasError(material.shader), Is.False);
        Assert.That(material.GetTexture("_BaseMap"), Is.EqualTo(AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Materials/moodi/base_color.png")));
        Assert.That(material.GetTexture("_BumpMap"), Is.EqualTo(AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Materials/moodi/normal.png")));
        Assert.That(material.GetTexture("_Matcap"), Is.Not.Null);
        var flow = material.GetTexture("_FlowNormal");
        Assert.That(flow, Is.Not.Null);
        var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(flow));
        Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.NormalMap));
        Assert.That(material.renderQueue, Is.LessThan((int)RenderQueue.AlphaTest));
        Assert.That(material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), Is.False);
        var original = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/moodi/moodi.mat");
        Assert.That(original.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
    }

    [Test]
    public void PilotUsesAquaAndSharedMoodiRetainsOriginalUntilDeviceApproval()
    {
        AssertMaterial("Assets/Resources/MoodiEmergence/Moodi.prefab", MoodiAquaGraphBuilder.MaterialPath);
        AssertMaterial("Assets/prefab/Moodi meshy.prefab", "Assets/Materials/moodi/moodi.mat");
    }

    static void AssertMaterial(string prefabPath, string materialPath)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        var renderers = prefab.GetComponentsInChildren<Renderer>(true);
        Assert.That(renderers, Is.Not.Empty);
        foreach (var renderer in renderers)
            Assert.That(renderer.sharedMaterials, Is.All.EqualTo(material), renderer.name);
    }
}
