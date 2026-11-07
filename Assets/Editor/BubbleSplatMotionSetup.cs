using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using UnityEngine;

/// <summary>Batchmode entry points for motion graph generation and conversion verification.</summary>
public static class BubbleSplatMotionSetup
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static Type TypeOf(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
    public static void Build() => BubbleSplatGraphBuilder.Build();
    public static void ValidateConversion()
    {
        var type = TypeOf("UnityEditor.ShaderGraph.MaterialX.MtlxPostProcessor");
        var graph = type.GetMethod("LoadGraphData", Flags).Invoke(null, new object[] { "Assets/Shaders/BubbleSplatMotion.shadergraph" });
        var converted = type.GetMethod("ProcessGraph", Flags).Invoke(null, new[] { graph, "BubbleSplatMotion", (object)false });
        if (converted == null) throw new InvalidOperationException("MaterialX conversion returned no graph.");
        var adapter = TypeOf("UnityEditor.ShaderGraph.MaterialX.AdapterMap");
        foreach (var path in new[] { "Assets/Shaders/BubbleSplatMotion.shadergraph", "Assets/Shaders/HandDisturb.shadersubgraph", "Assets/Shaders/SplatHash.shadersubgraph" })
        {
            var data = type.GetMethod("LoadGraphData", Flags).Invoke(null, new object[] { path });
            var nodes = (IEnumerable)data.GetType().GetMethods(Instance).First(m => m.Name == "GetNodes" && m.IsGenericMethod)
                .MakeGenericMethod(TypeOf("UnityEditor.ShaderGraph.AbstractMaterialNode")).Invoke(data, null);
            foreach (var node in nodes)
            {
                if (node.GetType().Name == "BlockNode") continue; // context adapter validates these below
                if (!(bool)adapter.GetMethod("IsNodeSupported", Flags).Invoke(null, new[] { node }))
                    throw new InvalidOperationException("Unsupported MaterialX node: " + node.GetType().Name);
                var warning = (string)adapter.GetMethod("GetSupportNotification", Flags).Invoke(null, new[] { node });
                if (!string.IsNullOrEmpty(warning)) throw new InvalidOperationException(node.GetType().Name + ": " + warning);
                if (node.GetType().Name != "Vector1Node") continue;
                var expected = (float)node.GetType().GetField("m_Value", Instance).GetValue(node);
                var slot = node.GetType().GetMethods(Instance).First(m => m.Name == "FindSlot" && m.IsGenericMethod && m.GetParameters().Length == 1)
                    .MakeGenericMethod(TypeOf("UnityEditor.ShaderGraph.Vector1MaterialSlot")).Invoke(node, new object[] { 1 });
                if ((float)slot.GetType().GetProperty("value", Instance).GetValue(slot) != expected)
                    throw new InvalidOperationException("Constant input slot differs from its declared value: " + expected);
            }
        }
        var processorType = TypeOf("UnityEditor.ShaderGraph.MaterialX.MtlxGraphProcessor");
        var xml = (string)processorType.GetMethod("ProcessGraph", Instance).Invoke(Activator.CreateInstance(processorType, true), new[] { converted });
        var root = XDocument.Parse(xml).Root;
        if (!root.Elements("GeometryModification").Any() || !root.Elements("surfacematerial").Elements("input").Any(e => (string)e.Attribute("name") == "vertexshader"))
            throw new InvalidOperationException("Converted material is missing the vertex displacement output.");
        var names = root.Elements().Select(e => (string)e.Attribute("name")).ToHashSet();
        foreach (var reference in root.Descendants("input").Attributes("nodename"))
            if (!names.Contains(reference.Value)) throw new InvalidOperationException("Dangling MaterialX connection: " + reference.Value);
        Debug.Log("[Bubble Motion] MaterialX graph conversion completed (device rendering not yet verified).");
    }
}
