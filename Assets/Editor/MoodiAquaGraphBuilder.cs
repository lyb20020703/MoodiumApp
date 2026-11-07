using System;
using UnityEditor;
using UnityEngine;
using static BubbleSplatGraphBuilder;

public static class MoodiAquaGraphBuilder
{
    internal const string GraphPath = "Assets/Shaders/MoodiAqua.shadergraph";
    internal const string MaterialPath = "Assets/Materials/moodi/Moodi_Aqua.mat";
    const string Slime = "Assets/Plugins/Vefects/Slime VFX URP/Slime/";

    [MenuItem("Moodium/Moodi/Build Aqua Material")]
    public static void Build()
    {
        var graph = new Builder();
        E Float(string name, float value) => graph.Property("Vector1", name, value);
        E ColorProperty(string name, Color value, bool hdr = false) =>
            graph.Property("Color", name, value, hdr);
        E Vector(string name, Vector3 value) => graph.Property("Vector3", name, value);
        E Spatial(string node, string space)
        {
            var input = graph.Node(node);
            var field = input.GetType().BaseType.GetField("m_Space", Flags);
            field.SetValue(input, Enum.Parse(field.FieldType, space));
            return new E(input, 0);
        }
        E Sample(E texture, E uv, bool normal = false)
        {
            var sample = graph.Node("SampleTexture2DNode", uv.Depth + 1);
            if (normal) Set(sample, "textureType", 1);
            graph.Connect(texture, sample, 1);
            graph.Connect(uv, sample, 2);
            return new E(sample, 0, uv.Depth + 1);
        }
        E Luma(E rgb) => graph.Dot(graph.XYZ(rgb), graph.V3(.299f, .587f, .114f));
        E Clamp01(E value) => graph.Op("SaturateNode", 1, value);
        E Power(E value, E exponent) => graph.Op("PowerNode", 2, value, exponent);
        E Cross(E left, E right) => graph.Op("CrossProductNode", 2, left, right);

        var baseMap = graph.Property("Texture2D", "_BaseMap");
        var bumpMap = graph.Property("Texture2D", "_BumpMap");
        var matcap = graph.Property("Texture2D", "_Matcap");
        var flowMap = graph.Property("Texture2D", "_FlowNormal");
        var uv = graph.UV(0);
        var albedo = Sample(baseMap, uv);
        var baseColor = graph.XYZ(albedo * ColorProperty("_BaseColor", Color.white));
        // The mask thresholds describe the authored sRGB image. Use the untinted map so tint cannot expose the eyes.
        var keep = graph.Smooth(Float("_EyeLumaLow", .22f), Float("_EyeLumaHigh", .32f),
            Power(Clamp01(Luma(albedo)), 1f / 2.2f));

        var time = new E(graph.Node("MaterialX.PolySpatialTimeNode"), 0);
        var p = Spatial("PositionNode", "Object") * Vector("_FlowTile", new Vector3(5, 1.3f, 5))
            + time * Vector("_FlowSpeed", new Vector3(.1f, .03f, -.1f));
        var weights = graph.Op("AbsoluteNode", 1, Spatial("NormalVectorNode", "Object"));
        weights /= graph.Max(.0001f, graph.X(weights) + graph.Y(weights) + graph.Z(weights));
        var xy = graph.XYZ(Sample(flowMap, graph.V2(graph.X(p), graph.Y(p)), true));
        var yz = graph.XYZ(Sample(flowMap, graph.V2(graph.Y(p), graph.Z(p)), true));
        var xz = graph.XYZ(Sample(flowMap, graph.V2(graph.X(p), graph.Z(p)), true));
        var flow = xy * graph.Z(weights) + yz * graph.X(weights) + xz * graph.Y(weights);
        var transform = graph.Op("TransformNode", 1, flow);
        Set(transform.Node, "conversionType", 1); // Object → World direction.
        var normal = graph.Norm(Spatial("NormalVectorNode", "World") + transform * Float("_FlowIntensity", .25f));

        var view = graph.Norm(Spatial("ViewDirectionNode", "World"));
        var right = graph.Norm(Cross(view, graph.V3(0, 1, 0)));
        var up = Cross(right, view);
        var lookup = graph.V2(graph.Dot(normal, right), graph.Dot(normal, up)) * .5f + .5f;
        var shade = Luma(Sample(matcap, lookup));
        var floor = Float("_MatcapFloor", .45f);
        var highlight = Power(Clamp01((shade - floor) / graph.Max(.001f, 1 - floor)), Float("_MatcapContrast", 2.5f));
        var rim = Float("_RimScale", .9f) * Power(1 - Clamp01(graph.Dot(normal, view)), Float("_RimPower", 3.5f));
        var rimColor = graph.Lerp(ColorProperty("_RimColorA", new Color(.85f, .45f, 1), true),
            ColorProperty("_RimColorB", new Color(.45f, .75f, 1), true), Clamp01(graph.X(flow) * .5f + .5f));
        var glow = Float("_GlowMultiply", 1) * keep;
        var emission = (highlight * ColorProperty("_MatcapTint", new Color(1, .93f, 1)) * Float("_MatcapStrength", .8f)
            + rim * rimColor) * glow + ColorProperty("_EmissionColor", Color.black, true);
        var shaded = baseColor * graph.Lerp(1, shade * 1.3f, Float("_MatcapShade", .25f) * glow);
        var bump = graph.Op("NormalStrengthNode", 2, Sample(bumpMap, uv, true), Float("_BumpScale", 1));
        var smoothness = graph.Lerp(.5f, Float("_Smoothness", .75f), glow);
        graph.Outputs(true, ("BaseColor", shaded), ("NormalTS", bump), ("Smoothness", smoothness),
            ("Emission", emission), ("Metallic", (E)0), ("Occlusion", (E)1));
        graph.Save(GraphPath);

        var shader = AssetDatabase.LoadAssetAtPath<Shader>(GraphPath);
        if (shader == null || ShaderUtil.ShaderHasError(shader))
            throw new InvalidOperationException("Moodi Aqua shader import failed.");
        if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) != null) return;
        var material = new Material(shader);
        void Texture(string name, string path)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) throw new InvalidOperationException("Missing Moodi Aqua texture " + path);
            material.SetTexture(name, texture);
        }
        Texture("_BaseMap", "Assets/Materials/moodi/base_color.png");
        Texture("_BumpMap", "Assets/Materials/moodi/normal.png");
        Texture("_Matcap", Slime + "Textures/Matcap/T_VFX_Vefects_Slime_Matcap_04.tga");
        Texture("_FlowNormal", Slime + "Shared/Textures/Noises/T_VFX_Perlin_Noise_01_N.tga");
        AssetDatabase.CreateAsset(material, MaterialPath);
    }
}
