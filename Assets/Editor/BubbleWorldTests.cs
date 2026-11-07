using System.IO;
using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;

public sealed class BubbleWorldTests
{
    [Test]
    public void VisibleArrivingBubbleRemembersTheFirstPress()
    {
        var go=new GameObject("arriving bubble");
        try
        {
            var controller=go.AddComponent<BubbleWorldController>();
            var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
            typeof(BubbleWorldController).GetField("m_Phase",flags).SetValue(controller,BubbleWorldController.Phase.Rising);
            controller.Touch();controller.Touch();
            Assert.That(typeof(BubbleWorldController).GetField("m_TouchRequested",flags).GetValue(controller),Is.True);
            Assert.That(typeof(BubbleWorldController).GetField("m_Phase",flags).GetValue(controller),Is.EqualTo(BubbleWorldController.Phase.Rising));
        }
        finally {Object.DestroyImmediate(go);}
    }
    [Test]
    public void BreathingKeepsSkyAtFixedWorldRadius()
    {
        foreach (var scale in new[] { 1f, 1.0075f, 1.015f })
            Assert.That(BubbleWorldController.SkyScaleForBreath(scale) * scale,
                Is.EqualTo(BubbleWorldController.SkyMaxRadius).Within(.0001f));
    }

    [Test]
    public void RoundPointFadeUsesActualAlphaBlendingWithoutDepthOcclusion()
    {
        var material = BubbleWorldData.CreateUnlit("fade test", null, true,
            UnityEngine.Rendering.CullMode.Off, Color.white);
        try
        {
            BubbleWorldData.ConfigureRoundPoints(material);
            BubbleWorldController.ConfigureTransparentPoints(material);
            BubbleWorldData.SetAlpha(material, .25f);
            Assert.That(material.GetFloat("_Surface"), Is.EqualTo(1));
            Assert.That(material.GetFloat("_SrcBlend"), Is.EqualTo((float)UnityEngine.Rendering.BlendMode.SrcAlpha));
            Assert.That(material.GetFloat("_DstBlend"), Is.EqualTo((float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            Assert.That(material.GetFloat("_ZWrite"), Is.Zero);
            Assert.That(material.renderQueue, Is.EqualTo((int)UnityEngine.Rendering.RenderQueue.Transparent));
            Assert.That(material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), Is.True);
            Assert.That(material.GetColor("_BaseColor").a, Is.EqualTo(.25f));
        }
        finally { Object.DestroyImmediate(material); }
    }

    [Test]
    public void StopClearsBreathAndWanderAndIsIdempotent()
    {
        var go = new GameObject("cleanup test");
        try
        {
            var controller = go.AddComponent<BubbleWorldController>();
            controller.SetBreath(1); controller.SetWanderFade(.25f);
            controller.StopAndClear(); controller.StopAndClear();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            Assert.That(typeof(BubbleWorldController).GetField("m_Breath", flags).GetValue(controller), Is.EqualTo(0f));
            Assert.That(typeof(BubbleWorldController).GetField("m_WanderTarget", flags).GetValue(controller), Is.EqualTo(1f));
            Assert.That(controller.CurrentPhase, Is.EqualTo(BubbleWorldController.Phase.Idle));
        }
        finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void SummonArcAndReturnRadiusHaveExactEndpoints()
    {
        var from = new Vector3(1, 0, 2); var hover = new Vector3(1, 1, 2);
        Assert.That(Vector3.Distance(BubbleWorldController.SummonPosition(from, hover, 0), from), Is.LessThan(.00001f));
        Assert.That(Vector3.Distance(BubbleWorldController.SummonPosition(from, hover, 1), hover), Is.LessThan(.00001f));
        Assert.That(BubbleWorldController.SummonPosition(from, hover, .5f).y, Is.GreaterThan(.5f));
        Assert.That(BubbleWorldController.LeaveRadius(0), Is.EqualTo(10.5f).Within(.0001f));
        Assert.That(BubbleWorldController.LeaveRadius(1), Is.EqualTo(.15f).Within(.0001f));
    }

    [Test]
    public void LayersCollapseFromFarToNearAndWanderUsesHysteresis()
    {
        Assert.That(BubbleWorldController.LayerCollapseTime(4, 5), Is.Zero);
        Assert.That(BubbleWorldController.LayerCollapseTime(0, 5), Is.EqualTo(1));
        Assert.That(BubbleWorldController.WanderState(false, .8f), Is.False);
        Assert.That(BubbleWorldController.WanderState(false, .81f), Is.True);
        Assert.That(BubbleWorldController.WanderState(true, .7f), Is.True);
        Assert.That(BubbleWorldController.WanderState(true, .6f), Is.False);
    }

    [Test]
    public void WarmupBuildsOnlyTheMiniWorldBeforeOuterLayers()
    {
        Assert.That(BubbleWorldController.WarmupLayerCount(4, 20), Is.EqualTo(4));
        Assert.That(BubbleWorldController.WarmupLayerCount(0, 20), Is.EqualTo(1));
        Assert.That(BubbleWorldController.WarmupLayerCount(25, 20), Is.EqualTo(20));
    }

    [Test]
    public void AllWorldsUseCanonicalSourceFrames()
    {
        Assert.That(Quaternion.Angle(BubbleWorldData.SourceFrameRotation(BubbleWorldData.NatureFolder), Quaternion.identity),
            Is.LessThan(.001f));
        Assert.That(Quaternion.Angle(BubbleWorldData.SourceFrameRotation(BubbleWorldData.CandyFolder), Quaternion.identity),
            Is.LessThan(.001f));
        Assert.That(Quaternion.Angle(BubbleWorldData.SourceFrameRotation(BubbleWorldData.NestedNatureFolder), Quaternion.identity),
            Is.LessThan(.001f));
        Assert.That(Quaternion.Angle(BubbleWorldData.SourceFrameRotation(BubbleWorldData.NestedCandyFolder), Quaternion.identity),
            Is.LessThan(.001f));
        Assert.That(Quaternion.Angle(BubbleWorldData.SourceFrameRotation(BubbleWorldData.MapleNatureFolder), Quaternion.identity),
            Is.LessThan(.001f));
    }

    [TestCase(BubbleWorldData.NestedNatureFolder, .369f)]
    [TestCase(BubbleWorldData.NestedCandyFolder, .56f)]
    [TestCase(BubbleWorldData.MapleNatureFolder, .313f)]
    public void NestedBakedLayersMatchExportedCloud(string folder, float measuredEye)
    {
        var bytes = File.ReadAllBytes("Assets/Resources/" + folder + "points.bytes");
        Assert.That(BubbleWorldData.TryParse(bytes, out var cloud), Is.True);
        Assert.That(cloud.Count, Is.EqualTo(500000));
        Assert.That(cloud.EyeHeight, Is.InRange(.2f, 2f));
        Assert.That(cloud.EyeHeight, Is.EqualTo(measuredEye).Within(.00001f));
        for (var layer = 0; layer < BubbleWorldData.LayerCount; layer++)
        {
            var mesh = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Resources/" + folder +
                "Meshes/" + BubbleWorldData.LayerMeshName(layer) + ".asset");
            Assert.That(mesh, Is.Not.Null, folder + layer);
            BubbleWorldData.LayerRange(layer, cloud.Count, BubbleWorldData.LayerCount, out var start, out var end);
            var vertices = mesh.vertices;
            Assert.That(vertices.Length, Is.EqualTo((end - start) * 4));
            for (var sample = 0; sample < 17; sample++)
            {
                var index = sample * (end - start - 1) / 16;
                var centre = (vertices[index * 4] + vertices[index * 4 + 2]) * .5f;
                Assert.That(Vector3.Distance(centre, cloud.Positions[start + index]), Is.LessThan(.00002f),
                    $"{folder} layer {layer} point {index}");
            }
        }
    }

    [Test]
    public void FullProgressDoesNotStartASummonAndInvalidLeaveDoesNotEmit()
    {
        var go = new GameObject("bubble lifecycle test");
        try
        {
            var controller = go.AddComponent<BubbleWorldController>();
            typeof(BubbleWorldController).GetField("m_Phase", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(controller, BubbleWorldController.Phase.Armed);
            var left = 0; controller.Left += _ => left++;
            controller.SetProgress(1f);
            controller.Leave(LeaveTarget.Exit);
            controller.Dismiss();
            Assert.That(controller.CurrentPhase, Is.EqualTo(BubbleWorldController.Phase.Armed));
            Assert.That(left, Is.Zero);
        }
        finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void RoundAtlasKeepsPointColoursAndFadesEdgesWithoutNeighbourBleed()
    {
        BubbleWorldData.TryParse(MakeCloud(Spiral(8, 3f)), out var cloud);
        var atlas = BubbleWorldData.BuildRoundAtlas(cloud);
        Material material = null;
        try
        {
            Assert.That(atlas.GetPixel(1, 1).a, Is.EqualTo(1f).Within(.005f));
            Assert.That(atlas.GetPixel(0, 0).a, Is.LessThan(.1f));
            Assert.That(atlas.GetPixel(0, 1).a, Is.LessThan(.5f));
            Assert.That(((Color32)atlas.GetPixel(5, 1)).r, Is.EqualTo(cloud.Colors[1].r));
            material = BubbleWorldData.CreateUnlit("Test round points", atlas, false,
                UnityEngine.Rendering.CullMode.Off, Color.white);
            BubbleWorldData.ConfigureRoundPoints(material);
            Assert.That(material.GetFloat("_AlphaClip"), Is.EqualTo(1f));
            Assert.That(material.GetFloat("_Cutoff"), Is.Zero);
            Assert.That(material.IsKeywordEnabled("_ALPHATEST_ON"), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(atlas);
            if (material != null) Object.DestroyImmediate(material);
        }
    }

    static byte[] MakeCloud(Vector3[] positions, float size = .05f, float eye = 1.3f)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(new[] { (byte)'M', (byte)'S', (byte)'P', (byte)'C' });
        writer.Write(1);
        writer.Write(positions.Length);
        writer.Write(eye);
        var max = 0f;
        foreach (var p in positions) max = Mathf.Max(max, p.magnitude);
        writer.Write(max);
        for (var i = 0; i < positions.Length; i++)
        {
            writer.Write(positions[i].x); writer.Write(positions[i].y); writer.Write(positions[i].z);
            writer.Write(Mathf.FloatToHalf(size));
            writer.Write((byte)(i % 256)); writer.Write((byte)20); writer.Write((byte)30); writer.Write((byte)255);
        }
        return stream.ToArray();
    }

    /// <summary>Points sorted near → far along a spiral, like the converter writes them.</summary>
    static Vector3[] Spiral(int n, float maxRadius)
    {
        var points = new Vector3[n];
        for (var i = 0; i < n; i++)
        {
            var r = .3f + (maxRadius - .3f) * i / (n - 1f);
            var a = i * 2.39996f;
            var y = Mathf.Sin(i * .7f) * .5f;
            var h = Mathf.Sqrt(1f - y * y);
            points[i] = new Vector3(Mathf.Sin(a) * h, y, Mathf.Cos(a) * h) * r;
        }
        return points;
    }

    [Test]
    public void ParsesTheConverterFormat()
    {
        var points = Spiral(37, 12f);
        Assert.That(BubbleWorldData.TryParse(MakeCloud(points, .08f, 1.34f), out var cloud), Is.True);
        Assert.That(cloud.Count, Is.EqualTo(37));
        Assert.That(cloud.EyeHeight, Is.EqualTo(1.34f).Within(1e-5f));
        Assert.That(cloud.MaxRadius, Is.EqualTo(12f).Within(1e-3f));
        Assert.That(Vector3.Distance(cloud.Positions[5], points[5]), Is.LessThan(1e-5f));
        Assert.That(cloud.Sizes[3], Is.EqualTo(.08f).Within(1e-3f));
        Assert.That(cloud.Colors[7].r, Is.EqualTo(7));
    }

    [Test]
    public void RejectsWrongMagicVersionOrTruncatedData()
    {
        var bytes = MakeCloud(Spiral(10, 5f));
        Assert.That(BubbleWorldData.TryParse(null, out _), Is.False);
        var wrongMagic = (byte[])bytes.Clone(); wrongMagic[0] = (byte)'X';
        Assert.That(BubbleWorldData.TryParse(wrongMagic, out _), Is.False);
        var wrongVersion = (byte[])bytes.Clone(); wrongVersion[4] = 2;
        Assert.That(BubbleWorldData.TryParse(wrongVersion, out _), Is.False);
        var truncated = new byte[bytes.Length - 1];
        System.Array.Copy(bytes, truncated, truncated.Length);
        Assert.That(BubbleWorldData.TryParse(truncated, out _), Is.False);
        Assert.That(BubbleWorldData.TryReadSummary(truncated, 4, out _, out _, out _), Is.False);
    }

    [Test]
    public void SummaryMatchesFullParseWithoutReadingEveryPoint()
    {
        var bytes = MakeCloud(Spiral(103, 40f));
        BubbleWorldData.TryParse(bytes, out var cloud);
        Assert.That(BubbleWorldData.TryReadSummary(bytes, 20, out var count, out var eye, out var radii), Is.True);
        Assert.That(count, Is.EqualTo(103));
        Assert.That(eye, Is.EqualTo(cloud.EyeHeight));
        var expected = BubbleWorldData.LayerOuterRadii(cloud, 20);
        for (var i = 0; i < 20; i++) Assert.That(radii[i], Is.EqualTo(expected[i]).Within(1e-4f));
    }

    [Test]
    public void LayersCoverEveryPointExactlyOnce()
    {
        var next = 0;
        for (var layer = 0; layer < 20; layer++)
        {
            BubbleWorldData.LayerRange(layer, 200003, 20, out var start, out var end);
            Assert.That(start, Is.EqualTo(next));
            Assert.That(end - start, Is.InRange(10000, 10001));
            next = end;
        }
        Assert.That(next, Is.EqualTo(200003));
    }

    [Test]
    public void ColourUvHitsTheTexelCentreWithRowZeroAtTheTop()
    {
        Assert.That(BubbleWorldData.AtlasSizeFor(200000), Is.EqualTo(512));
        var first = BubbleWorldData.ColorUV(0, 512);
        Assert.That(first.x, Is.EqualTo(.5f / 512f).Within(1e-7f));
        Assert.That(first.y, Is.EqualTo(1f - .5f / 512f).Within(1e-7f));
        var secondRow = BubbleWorldData.ColorUV(513, 512);
        Assert.That(secondRow.x, Is.EqualTo(1.5f / 512f).Within(1e-7f));
        Assert.That(secondRow.y, Is.EqualTo(1f - 1.5f / 512f).Within(1e-7f));
    }

    [Test]
    public void LayerMeshIsOneCentreFacingQuadPerPoint()
    {
        BubbleWorldData.TryParse(MakeCloud(Spiral(64, 9f), .1f), out var cloud);
        var mesh = BubbleWorldData.BuildLayerMesh(cloud, 16, 48, "test");
        try
        {
            Assert.That(mesh.vertexCount, Is.EqualTo(32 * 4));
            Assert.That(mesh.triangles.Length, Is.EqualTo(32 * 6));
            Assert.That(mesh.indexFormat, Is.EqualTo(UnityEngine.Rendering.IndexFormat.UInt16));
            var uvs = mesh.uv;
            var vertices = mesh.vertices;
            for (var q = 0; q < 32; q++)
            {
                Assert.That(uvs[q * 4 + 3].y, Is.GreaterThan(uvs[q * 4].y));
                Assert.That(uvs[q * 4 + 2].x, Is.GreaterThan(uvs[q * 4].x));
                var centre = (vertices[q * 4] + vertices[q * 4 + 2]) * .5f;
                Assert.That(Vector3.Distance(centre, cloud.Positions[16 + q]), Is.LessThan(1e-4f));
                Assert.That(Vector3.Distance(vertices[q * 4], vertices[q * 4 + 2]), Is.EqualTo(.1f * Mathf.Sqrt(2f)).Within(2e-3f));
                Assert.That(BubbleWorldData.FacesTowards(mesh, q * 2, -cloud.Positions[16 + q]), Is.True, $"quad {q}");
            }
        }
        finally { Object.DestroyImmediate(mesh); }
    }

    [Test]
    public void SkySphereFacesInwardAndUsesTheConverterEquirectMapping()
    {
        var mesh = BubbleWorldData.BuildPanoramaSphere(32, 16);
        try
        {
            var tris = mesh.triangles;
            var vertices = mesh.vertices;
            var inward = 0;
            for (var t = 0; t < tris.Length / 3; t++)
            {
                var a = vertices[tris[t * 3]]; var b = vertices[tris[t * 3 + 1]]; var c = vertices[tris[t * 3 + 2]];
                var normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude < 1e-12f) continue; // pole slivers
                if (Vector3.Dot(normal, -(a + b + c)) > 0f) inward++;
                else Assert.Fail($"triangle {t} faces outward");
            }
            Assert.That(inward, Is.GreaterThan(0));

            var uvs = mesh.uv;
            for (var i = 0; i < vertices.Length; i += 7)
            {
                var v = vertices[i];
                if (Mathf.Abs(v.y) > .999f) continue; // longitude is undefined at the poles
                var u = .5f + Mathf.Atan2(v.x, v.z) / (2f * Mathf.PI);
                var vv = .5f + Mathf.Asin(Mathf.Clamp(v.y, -1f, 1f)) / Mathf.PI;
                var du = Mathf.Abs(uvs[i].x - u);
                Assert.That(Mathf.Min(du, 1f - du), Is.LessThan(1e-4f), $"u at {i}");
                Assert.That(uvs[i].y, Is.EqualTo(vv).Within(1e-4f), $"v at {i}");
            }
        }
        finally { Object.DestroyImmediate(mesh); }
    }

    [Test]
    public void BubbleAppearsOnlyAtFullEnergy()
    {
        Assert.That(BubbleWorldController.ShouldAppear(.998f), Is.False);
        Assert.That(BubbleWorldController.ShouldAppear(1f), Is.True);
    }

    [Test]
    public void GrowthCurvesStartAndEndOnTheDesignedSizes()
    {
        Assert.That(BubbleWorldController.ApproachRadius(0f, .15f, .6f), Is.EqualTo(.15f).Within(1e-5f));
        Assert.That(BubbleWorldController.ApproachRadius(1f, .15f, .6f), Is.EqualTo(.6f).Within(1e-5f));
        Assert.That(BubbleWorldController.EnvelopRadius(0f, .6f, 7f), Is.EqualTo(.6f).Within(1e-5f));
        Assert.That(BubbleWorldController.EnvelopRadius(1f, .6f, 10.5f), Is.EqualTo(10.5f).Within(1e-4f));
        var last = 0f;
        for (var t = 0f; t <= 1f; t += .05f)
        {
            var r = BubbleWorldController.EnvelopRadius(t, .6f, 7f);
            Assert.That(r, Is.GreaterThanOrEqualTo(last));
            last = r;
        }
        Assert.That(BubbleWorldController.LayerGrowScale(0f), Is.EqualTo(.9f).Within(1e-5f));
        Assert.That(BubbleWorldController.LayerGrowScale(1f), Is.EqualTo(1f).Within(1e-5f));
    }

    [Test]
    public void FilmFadesOnceTheViewerIsInside()
    {
        Assert.That(BubbleWorldController.MembraneAlpha(.15f, .85f), Is.EqualTo(.85f).Within(1e-5f));
        Assert.That(BubbleWorldController.MembraneAlpha(1f, .85f), Is.EqualTo(.85f).Within(1e-5f));
        Assert.That(BubbleWorldController.MembraneAlpha(2.5f, .85f), Is.EqualTo(0f).Within(1e-5f));
    }

    [Test]
    public void MiniBubbleKeepsOnlyLayersThatFitAndItsSkyFadesWhileSwelling()
    {
        var radii = new[] { 1f, 3f, 6.9f, 7.2f, 20f, 72f };
        Assert.That(BubbleWorldController.MiniLayerCount(radii, 7f), Is.EqualTo(3));
        Assert.That(BubbleWorldController.LayerRevealTime(0, 3), Is.EqualTo(0f));
        Assert.That(BubbleWorldController.LayerRevealTime(2, 3), Is.EqualTo(2f / 3f).Within(1e-6f));
        // The bubble's sky is solid while it is small, gone once it has swelled past the viewer.
        Assert.That(BubbleWorldController.SkyAlphaWhileEnveloping(.6f), Is.EqualTo(1f));
        Assert.That(BubbleWorldController.SkyAlphaWhileEnveloping(1.2f), Is.EqualTo(.5f).Within(1e-5f));
        Assert.That(BubbleWorldController.SkyAlphaWhileEnveloping(1.6f), Is.EqualTo(0f));
    }

    [Test]
    public void ScanGroundLandsOnTheRealFloorWhenHeadHeightIsPlausible()
    {
        var standing = BubbleWorldController.FinalCenter(new Vector3(1f, 1.62f, 2f), 0f, 1.34f, true);
        Assert.That(standing, Is.EqualTo(new Vector3(1f, 1.34f, 2f)));
        var oddOrigin = BubbleWorldController.FinalCenter(new Vector3(1f, .1f, 2f), 0f, 1.34f, true);
        Assert.That(oddOrigin, Is.EqualTo(new Vector3(1f, .1f, 2f)));
        var off = BubbleWorldController.FinalCenter(new Vector3(1f, 1.62f, 2f), 0f, 1.34f, false);
        Assert.That(off.y, Is.EqualTo(1.62f));
    }

    [Test]
    public void FingertipTouchesWithinRadiusPlusPadding()
    {
        var c = new Vector3(0f, 1f, 1f);
        Assert.That(BubbleWorldController.IsTouching(c + Vector3.left * .155f, c, .15f, .01f), Is.True);
        Assert.That(BubbleWorldController.IsTouching(c + Vector3.left * .17f, c, .15f, .01f), Is.False);
    }

    [Test]
    public void ScanForwardTurnsToTheGaze()
    {
        Assert.That(BubbleWorldController.GazeYaw(Vector3.forward), Is.EqualTo(0f).Within(1e-4f));
        Assert.That(BubbleWorldController.GazeYaw(Vector3.right), Is.EqualTo(90f).Within(1e-4f));
        Assert.That(BubbleWorldController.GazeYaw(new Vector3(0f, -1f, 0f)), Is.EqualTo(0f));
        var yaw = Quaternion.Euler(0f, BubbleWorldController.GazeYaw(new Vector3(-1f, .4f, 1f)), 0f);
        Assert.That(Vector3.Angle(yaw * Vector3.forward, new Vector3(-1f, 0f, 1f)), Is.LessThan(.01f));
    }

    [Test]
    public void EachWorldHasItsOwnResourceFolder()
    {
        Assert.That(BubbleWorldData.FolderFor(false), Is.EqualTo("MoodiumBubbleWorld/"));
        Assert.That(BubbleWorldData.FolderFor(true), Is.EqualTo("MoodiumBubbleWorld/Candy/"));
        Assert.That(BubbleWorldData.PointsResource(BubbleWorldData.CandyFolder), Is.EqualTo("MoodiumBubbleWorld/Candy/points"));
        Assert.That(BubbleWorldData.MeshResource(BubbleWorldData.CandyFolder, BubbleWorldData.LayerMeshName(3)),
            Is.EqualTo("MoodiumBubbleWorld/Candy/Meshes/BubbleWorld_Layer_03"));
    }

    [TestCase(BubbleWorldData.NatureFolder)]
    [TestCase(BubbleWorldData.CandyFolder)]
    public void DeviceMeshesAreBakedAndColourAtlasUsesExactSampling(string folder)
    {
        const int expectedCount = 500000;
        var points = Resources.Load<TextAsset>(BubbleWorldData.PointsResource(folder));
        Assert.That(points, Is.Not.Null);
        Assert.That(BubbleWorldData.TryReadSummary(points.bytes, BubbleWorldData.LayerCount,
            out var count, out _, out _), Is.True);
        Assert.That(count, Is.EqualTo(expectedCount));
        for (var layer = 0; layer < BubbleWorldData.LayerCount; layer++)
        {
            var mesh = Resources.Load<Mesh>(BubbleWorldData.MeshResource(folder, BubbleWorldData.LayerMeshName(layer)));
            Assert.That(mesh, Is.Not.Null, $"{folder} layer {layer}: run Bake Meshes before device builds.");
            Assert.That(mesh.vertexCount, Is.EqualTo(expectedCount / BubbleWorldData.LayerCount * 4));
            if (mesh.vertexCount > 65535)
                Assert.That(mesh.indexFormat, Is.EqualTo(UnityEngine.Rendering.IndexFormat.UInt32));
        }
        Assert.That(Resources.Load<Mesh>(BubbleWorldData.MeshResource(folder, BubbleWorldData.PanoramaMeshName)), Is.Not.Null);
        var texture = Resources.Load<Texture2D>(BubbleWorldData.RoundColorsResource(folder));
        Assert.That(texture, Is.Not.Null);
        Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Bilinear));
        Assert.That(texture.width, Is.EqualTo(BubbleWorldData.RoundAtlasSizeFor(expectedCount)));
        Assert.That(texture.mipmapCount, Is.EqualTo(1));
    }

    [TestCase(BubbleWorldData.NatureFolder)]
    [TestCase(BubbleWorldData.CandyFolder)]
    public void ShippedPointCloudIsValidWhenPresent(string folder)
    {
        var points = Resources.Load<TextAsset>(BubbleWorldData.PointsResource(folder));
        if (points == null) Assert.Ignore($"Resources/{folder}points.bytes is not in this project yet.");
        Assert.That(BubbleWorldData.TryReadSummary(points.bytes, BubbleWorldData.LayerCount, out var count, out var eye, out var radii), Is.True);
        Assert.That(count, Is.GreaterThan(0));
        Assert.That(eye, Is.InRange(.5f, 2.5f), "scan centre should sit near standing eye height (see splat_convert.py --eye)");
        var mini = BubbleWorldController.MiniLayerCount(radii, BubbleWorldController.MiniWorldRadius);
        Assert.That(mini, Is.InRange(1, BubbleWorldData.LayerCount - 1), "some layers inside the bubble, some revealed later");
        for (var i = 1; i < radii.Length; i++) Assert.That(radii[i], Is.GreaterThanOrEqualTo(radii[i - 1]), "points must be sorted near → far");
        Assert.That(Resources.Load<Texture2D>(BubbleWorldData.ColorsResource(folder)), Is.Not.Null);
        Assert.That(Resources.Load<Texture2D>(BubbleWorldData.PanoramaResource(folder)), Is.Not.Null);
    }
}
