using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Moodium.NatureWorld;
using UnityEditor;
using UnityEngine;

/// <summary>Version-pinned Shader Graph authoring API for Unity 6000.0 / SG 17.
/// Produces ordinary editable built-in nodes, never Custom Function/HLSL nodes.</summary>
public static class BubbleSplatGraphBuilder
{
    internal const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const string Prefix = "UnityEditor.ShaderGraph.";
    const string Folder = "Assets/Shaders/";
    internal static Type T(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
    static object New(string name) => Activator.CreateInstance(T(Prefix + name), true);
    static object Get(object o, string name) => o.GetType().GetProperty(name, Flags)?.GetValue(o) ?? o.GetType().GetField(name, Flags)?.GetValue(o);
    internal static void Set(object o, string name, object value)
    {
        var p = o.GetType().GetProperty(name, Flags);
        if (p.PropertyType == typeof(Vector4) && value is Vector3 vector) value = (Vector4)vector;
        p.SetValue(o, p.PropertyType.IsEnum ? Enum.ToObject(p.PropertyType, value) : value);
    }
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethods(Flags)
        .First(m => m.Name == name && !m.IsGenericMethod && m.GetParameters().Length == args.Length &&
            m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(x => x)).Invoke(o, args);
    static Builder current;
    internal struct E
    {
        public object Node; public int Slot, Depth;
        public E(object node, int slot, int depth = 0) { Node = node; Slot = slot; Depth = depth; }
        public static implicit operator E(float value) => current.Constant(value);
        public static E operator +(E a, E b) => current.Op("AddNode", 2, a, b);
        public static E operator -(E a, E b) => current.Op("SubtractNode", 2, a, b);
        public static E operator *(E a, E b) => current.Op("MultiplyNode", 2, a, b);
        public static E operator /(E a, E b) => current.Op("DivideNode", 2, a, b);
    }
    internal sealed class Sub
    {
        public string Path;
        public Dictionary<string, int> Inputs;
    }
    internal sealed class Builder
    {
        public readonly object Graph = New("GraphData");
        readonly Dictionary<float, E> constants = new Dictionary<float, E>();
        readonly Dictionary<int, int> rows = new Dictionary<int, int>();
        public readonly Dictionary<string, int> InputIds = new Dictionary<string, int>();
        public Builder(bool sub = false)
        {
            current = this; Set(Graph, "isSubGraph", sub); Call(Graph, "AddContexts"); Set(Graph, "path", "Moodium");
        }
        public object Node(string type, int depth = 0)
        {
            var n = New(type); Call(Graph, "AddNode", n);
            var state = Get(n, "drawState");
            rows.TryGetValue(depth, out var row); rows[depth] = row + 1;
            Set(state, "position", new Rect(depth * 260, row * 220, 200, 150));
            Set(n, "drawState", state);
            return n;
        }
        public void Connect(E a, object b, int input) => Call(Graph, "Connect", Call(a.Node, "GetSlotReference", a.Slot), Call(b, "GetSlotReference", input));
        public E Op(string type, int output, params E[] inputs)
        {
            var depth = inputs.Length == 0 ? 0 : inputs.Max(i => i.Depth) + 1;
            var n = Node(type, depth);
            for (var i = 0; i < inputs.Length; i++) Connect(inputs[i], n, i);
            return new E(n, output, depth);
        }
        public E Constant(float value)
        {
            if (constants.TryGetValue(value, out var e)) return e;
            var n = Node("Vector1Node");
            n.GetType().GetField("m_Value", Flags).SetValue(n, value);
            // AddSlot preserves an existing slot's value; update that input explicitly.
            var slot = n.GetType().GetMethods(Flags).First(m => m.Name == "FindSlot" && m.IsGenericMethod && m.GetParameters().Length == 1)
                .MakeGenericMethod(T(Prefix + "Vector1MaterialSlot")).Invoke(n, new object[] { 1 });
            Set(slot, "value", value);
            e = new E(n, 0); constants[value] = e; return e;
        }
        public E Property(string type, string name, object value = null, bool hdr = false)
        {
            var p = New("Internal." + type + "ShaderProperty");
            Set(p, "displayName", name); Set(p, "overrideReferenceName", name);
            if (value != null) Set(p, "value", value);
            if (hdr) Set(p, "colorMode", 1);
            Call(Graph, "AddGraphInput", p, -1);
            InputIds[name] = Get(p, "guid").GetHashCode();
            var n = Node("PropertyNode"); Set(n, "property", p); return new E(n, 0);
        }
        public E X(E v) => Split(v, 1);
        public E Y(E v) => Split(v, 2);
        public E Z(E v) => Split(v, 3);
        public E W(E v) => Split(v, 4);
        E Split(E v, int channel) => Op("SplitNode", channel, v);
        public E V2(E x, E y) => Op("CombineNode", 6, x, y);
        public E V3(E x, E y, E z) => Op("CombineNode", 5, x, y, z);
        public E XYZ(E v) => V3(X(v), Y(v), Z(v));
        public E Sin(E v) => Op("SineNode", 1, v);
        public E Cos(E v) => Op("CosineNode", 1, v);
        public E Frac(E v) => Op("FractionNode", 1, v);
        public E Len(E v) => Op("LengthNode", 1, v);
        public E Max(E a, E b) => Op("MaximumNode", 2, a, b);
        public E Min(E a, E b) => Op("MinimumNode", 2, a, b);
        public E Dot(E a, E b) => Op("DotProductNode", 2, a, b);
        public E Norm(E v) => v / Max(Len(v), .0001f);
        public E Smooth(E a, E b, E v) => Op("SmoothstepNode", 3, a, b, v);
        public E Lerp(E a, E b, E v) => Op("LerpNode", 3, a, b, v);
        public E Noise(E uv)
        {
            var e = Op("GradientNoiseNode", 2, uv, 1);
            Set(e.Node, "hashType", 1); return e;
        }
        public E UV(int channel)
        { var n = Node("UVNode"); Set(n, "uvChannel", channel); return new E(n, 0); }
        public E[] Use(Sub sub, params (string name, E value)[] args)
        {
            var n = Node("SubGraphNode", args.Max(a => a.value.Depth) + 1);
            Set(n, "asset", AssetDatabase.LoadAssetAtPath(sub.Path, T(Prefix + "SubGraphAsset")));
            foreach (var a in args) Connect(a.value, n, sub.Inputs[a.name]);
            return new[] { new E(n, 1, args.Max(a => a.value.Depth) + 1), new E(n, 2, args.Max(a => a.value.Depth) + 1) };
        }
        public Sub SaveSub(string name, params (string type, E value)[] outputs)
        {
            var n = Node("SubGraphOutputNode", outputs.Max(o => o.value.Depth) + 1);
            foreach (var output in outputs)
            {
                var slot = (int)Call(n, "AddSlot", Enum.Parse(T(Prefix + "ConcreteSlotValueType"), output.type));
                Connect(output.value, n, slot);
            }
            Set(Graph, "outputNode", n);
            var path = Folder + name + ".shadersubgraph"; Save(path);
            return new Sub { Path = path, Inputs = InputIds };
        }
        public void Save(string path)
        {
            Call(Graph, "ValidateGraph");
            var json = (string)T(Prefix + "Serialization.MultiJson").GetMethod("Serialize", Flags).Invoke(null, new[] { Graph });
            Directory.CreateDirectory(Folder); File.WriteAllText(path, json);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }
        public void Outputs(params (string name, E value)[] outputs)
            => Outputs(false, outputs);

        public void Outputs(bool opaqueLit, params (string name, E value)[] outputs)
        {
            var target = Activator.CreateInstance(T("UnityEditor.Rendering.Universal.ShaderGraph.UniversalTarget"), true);
            var subTarget = opaqueLit ? "UniversalLitSubTarget" : "UniversalUnlitSubTarget";
            Call(target, "TrySetActiveSubTarget", T("UnityEditor.Rendering.Universal.ShaderGraph." + subTarget));
            Set(target, "surfaceType", opaqueLit ? 0 : 1);
            Set(target, "renderFace", opaqueLit ? 2 : 0);
            Set(target, "zWriteControl", opaqueLit ? 1 : 2);
            Set(target, "alphaClip", !opaqueLit);
            var targets = Array.CreateInstance(T(Prefix + "Target"), 1); targets.SetValue(target, 0);
            var descriptors = Array.CreateInstance(T(Prefix + "BlockFieldDescriptor"), outputs.Length);
            for (var i = 0; i < outputs.Length; i++)
            {
                var vertex = outputs[i].name == "Position";
                descriptors.SetValue(T(Prefix + "BlockFields+" + (vertex ? "VertexDescription" : "SurfaceDescription"))
                    .GetField(outputs[i].name, Flags).GetValue(null), i);
            }
            Call(Graph, "InitializeOutputs", targets, descriptors);
            var blocks = (IEnumerable)Graph.GetType().GetMethods(Flags).First(m => m.Name == "GetNodes" && m.IsGenericMethod)
                .MakeGenericMethod(T(Prefix + "BlockNode")).Invoke(Graph, null);
            foreach (var block in blocks.Cast<object>().ToArray())
                foreach (var output in outputs)
                    if (((string)Get(block, "name")).EndsWith("." + output.name)) Connect(output.value, block, 0);
        }
    }

    [MenuItem("Moodium/Bubble World/Build GPU Motion Graphs")]
    public static void Build()
    {
        var b = new Builder(true);
        var seed = b.Property("Vector1", "Seed", 0f);
        var jit = b.V3(b.Frac(seed * 7.13f), b.Frac(seed * 3.71f), b.Frac(seed * 5.37f)) * 2 - 1;
        var hash = b.SaveSub("SplatHash", ("Vector3", jit));

        b = new Builder(true);
        var p = b.Property("Vector3", "Point", Vector3.zero);
        seed = b.Property("Vector1", "Seed", 0f);
        var t = b.Property("Vector1", "Time", 0f);
        var hand = b.Property("Vector4", "Hand", new Vector4(0,0,0,.3f));
        var amount = b.Property("Vector1", "Amount", 0f);
        var up = b.Property("Vector3", "Up", Vector3.up);
        var eye = b.Property("Vector3", "Eye", Vector3.zero);
        var boost = b.Property("Vector1", "Boost", 1f);
        var inner = b.Property("Vector1", "InnerCos", Mathf.Cos(4 * Mathf.Deg2Rad));
        var outer = b.Property("Vector1", "OuterCos", Mathf.Cos(9 * Mathf.Deg2Rad));
        var h = b.XYZ(hand); var radius = b.Max(b.W(hand), .0001f); var v = p - h;
        var near = 1 - b.Smooth(.6f * radius, radius, b.Len(v));
        var toP = p - eye; var toH = h - eye;
        var ray = b.Smooth(outer, inner, b.Dot(b.Norm(toP), b.Norm(toH))) * b.Smooth(b.Len(toH), b.Len(toH) + .4f, b.Len(toP));
        var k = b.Op("SaturateNode", 1, amount * b.Max(near, .85f * ray));
        var axis = b.Norm(toH);
        var perp = b.Norm(toP - b.Dot(toP, axis) * axis + .0001f);
        var outv = b.Norm(b.Lerp(perp, b.Norm(v + .0001f), near));
        jit = b.Use(hash, ("Seed", seed))[0];
        var dir = b.Norm(outv * .6f + up * .45f + jit * .35f);
        var mag = b.Min(.8f, k * k * (.05f + .012f * b.Len(p)) * (.6f + .4f * b.Sin(.7f * t + 6.283f * seed)));
        var disturb = b.SaveSub("HandDisturb", ("Vector3", dir * mag * boost), ("Vector1", k));

        b = new Builder();
        var texture = b.Property("Texture2D", "_BaseMap");
        var color = b.Property("Color", "_BaseColor", Color.white);
        up = b.XYZ(b.Property("Vector4", "_Up", new Vector4(0,1,0,0)));
        var wind = b.XYZ(b.Property("Vector4", "_WindDir", new Vector4(.8f,0,.6f,0)));
        b.Property("Vector1", "_EyeHeight", 1.3f); // baked sway already includes scan eye height
        var strength = b.Property("Vector1", "_WindStrength", 0f);
        var style = b.Property("Vector1", "_Style", 0f);
        var selfAmount = b.Property("Vector1", "_SelfAmount", 1f);
        var floatStrength = b.Property("Vector1", "_FloatStrength", .8f);
        var handA = b.Property("Vector4", "_HandA", new Vector4(0,0,0,.3f));
        var handB = b.Property("Vector4", "_HandB", new Vector4(0,0,0,.3f));
        var handAmount = b.Property("Vector4", "_HandAmount", Vector4.zero);
        eye = b.XYZ(b.Property("Vector4", "_Eye", Vector4.zero));
        boost = b.Property("Vector1", "_Boost", 1f);
        var debug = b.Property("Vector1", "_DebugTint", 0f);
        inner = b.Property("Vector1", "_InnerCos", Mathf.Cos(4 * Mathf.Deg2Rad));
        outer = b.Property("Vector1", "_OuterCos", Mathf.Cos(9 * Mathf.Deg2Rad));
        var position = b.Node("PositionNode");
        var spaceField = position.GetType().BaseType.GetField("m_Space", Flags);
        spaceField.SetValue(position, Enum.ToObject(spaceField.FieldType, 0));
        // Color carries a constant center per quad, independently of the displayed texture color.
        p = (b.XYZ(new E(b.Node("VertexColorNode"), 0)) - .5f) * BubbleSplatMotionMath.CenterEncodingScale;
        var uv = b.UV(1); seed = b.X(uv); var sway = b.Y(uv);
        t = new E(b.Node("MaterialX.PolySpatialTimeNode"), 0);
        var r = b.Len(p); var s = b.V2(b.X(p), b.Z(p)) / (1 + .15f * r) * 1.2f;
        var phase = b.Noise(s); var side = b.Norm(b.Op("CrossProductNode", 2, up, wind));
        var amplitude = boost * strength * b.Min(.25f, .012f + .0035f * r) * sway;
        var gust = .55f + .45f * (.5f + .5f * b.Sin(.23f * t) * b.Sin(.37f * t + 1.3f));
        var wave = b.Noise(s * .5f - t * .9f * b.V2(b.X(wind), b.Z(wind)));
        var sway2 = .5f * b.Sin(1.7f * t + 6.283f * phase) + .25f * b.Sin(2.9f * t + 10.68f * phase);
        var m = gust * (.6f * sway2 + .8f * (wave - .35f));
        var dWind = amplitude * (wind * m + side * (.15f * b.Sin(6 * t + 6.283f * seed)) + up * (.25f * b.Sin(4.1f * t + 18.85f * phase)));
        var dFloat = amplitude * floatStrength * (up * b.Sin(.9f * t + 6.283f * seed) + wind * (.35f * b.Sin(.6f * t + 6.283f * phase)) + side * (.35f * b.Cos(.7f * t + 6.283f * phase)));
        var dSelf = boost * selfAmount * strength * (.004f + .0012f * r) * b.V3(b.Sin(1.1f * t + 6.283f * seed), .6f * b.Sin(.8f * t + 12.57f * seed), b.Cos(1.3f * t + 18.85f * seed));
        E[] Hand(E hnd, E amt) => b.Use(disturb, ("Point", p), ("Seed", seed), ("Time", t), ("Hand", hnd),
            ("Amount", amt), ("Up", up), ("Eye", eye), ("Boost", boost), ("InnerCos", inner), ("OuterCos", outer));
        var a = Hand(handA, b.X(handAmount)); var c = Hand(handB, b.Y(handAmount));
        var delta = b.Lerp(dWind, dFloat, style) + dSelf + a[0] + c[0];
        delta *= b.Min(1, .8f / b.Max(b.Len(delta), .0001f));
        var sample = b.Node("SampleTexture2DNode"); b.Connect(texture, sample, 1);
        var col = new E(sample, 0) * color;
        var influence = b.Max(a[1], c[1]);
        var alpha = b.W(col) * (1 - .6f * b.Op("PowerNode", 2, influence, 1.5f));
        var rgb = b.Lerp(b.XYZ(col), b.V3(influence, sway, 0), .8f * b.Op("SaturateNode", 1, debug));
        b.Outputs(("Position", new E(position, 0) + delta), ("BaseColor", rgb), ("Alpha", alpha), ("AlphaClipThreshold", (E)0));
        b.Save(Folder + "BubbleSplatMotion.shadergraph");
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(Folder + "BubbleSplatMotion.shadergraph");
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Motion shader import failed.");
        const string materialPath = "Assets/Resources/MoodiumBubbleWorld/BubbleSplatMotion.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath); }
        material.shader = shader; material.SetColor("_BaseColor", Color.white); material.SetFloat("_WindStrength", 0);
        material.SetFloat("_Boost", 1); material.SetFloat("_DebugTint", 0); material.SetFloat("_SelfAmount", 1);
        material.SetFloat("_FloatStrength", .8f); material.SetFloat("_InnerCos", Mathf.Cos(4 * Mathf.Deg2Rad));
        material.SetFloat("_OuterCos", Mathf.Cos(9 * Mathf.Deg2Rad)); material.SetVector("_HandAmount", Vector4.zero);
        material.renderQueue = 3000; EditorUtility.SetDirty(material); AssetDatabase.SaveAssets();
        BubbleSplatMotionSetup.ValidateConversion();
        Debug.Log("[Bubble Motion] GPU graph and both subgraphs generated; device validation pending.");
    }
}
