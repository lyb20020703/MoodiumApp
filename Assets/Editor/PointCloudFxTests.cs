using Moodium.PointClouds;
using NUnit.Framework;
using UnityEngine;

public sealed class PointCloudFxTests
{
    [TestCase("Birch 1")]
    [TestCase("Oak 1")]
    public void BakedTreeStaysWithinTheSourceGeometryScale(string modelName)
    {
        var asset = PointCloudModelAsset.Load(modelName);
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(asset.SourcePrefab);
        var instance = Object.Instantiate(prefab);
        try
        {
            var bounds = new Bounds();
            var first = true;
            var excluded = new System.Collections.Generic.HashSet<Renderer>();
            foreach (var lod in instance.GetComponentsInChildren<LODGroup>(true))
            {
                var levels = lod.GetLODs();
                for (var i = 1; i < levels.Length; i++)
                    foreach (var renderer in levels[i].renderers)
                        if (renderer != null) excluded.Add(renderer);
            }
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || excluded.Contains(renderer) || renderer is ParticleSystemRenderer) continue;
                Mesh mesh = null;
                var temporary = renderer is SkinnedMeshRenderer;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    mesh = new Mesh();
                    skinned.BakeMesh(mesh, false);
                }
                else mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                foreach (var vertex in mesh.vertices)
                {
                    var world = renderer.transform.TransformPoint(vertex);
                    if (first) { bounds = new Bounds(world, Vector3.zero); first = false; }
                    else bounds.Encapsulate(world);
                }
                if (temporary) Object.DestroyImmediate(mesh);
            }
            var bakedHeight = asset.Height * instance.transform.lossyScale.y;
            // Source geometry includes the invisible corners of cutout leaf cards. Sampling rejects those texels,
            // so its height need not match the raw card bounds; this check guards against unit/scale conversion errors.
            Assert.That(bakedHeight, Is.InRange(bounds.size.y * .5f, bounds.size.y * 1.1f), modelName);
        }
        finally { Object.DestroyImmediate(instance); }
    }

    [TestCase("CandyHouse", 80000)]
    [TestCase("CandyLittleHouse", 80000)]
    [TestCase("Honey Jar", 80000)]
    [TestCase("Birch 1", 60000)]
    [TestCase("Oak 1", 60000)]
    public void ModelsContainAllBakedChunksAndRoundColours(string modelName, int expectedCount)
    {
        var asset = PointCloudModelAsset.Load(modelName);
        Assert.That(asset, Is.Not.Null, "Bake CandyHouse Validation Model before building.");
        Assert.That(asset.Count, Is.EqualTo(expectedCount));
        Assert.That(asset.ChunkCount, Is.EqualTo(expectedCount / 4000));
        Assert.That(asset.Normals.Length, Is.EqualTo(asset.Count));
        Assert.That(asset.Height01.Length, Is.EqualTo(asset.Count));
        Assert.That(asset.Atlas, Is.Not.Null);
        Assert.That(asset.Atlas.filterMode, Is.EqualTo(FilterMode.Bilinear));
        Assert.That(asset.Atlas.mipmapCount, Is.EqualTo(1));
        Assert.That(asset.Atlas.width, Is.EqualTo(2048));
        for (var c = 0; c < asset.ChunkCount; c++)
        {
            Assert.That(asset.Chunks[c], Is.Not.Null, $"chunk {c}");
            asset.ChunkRange(c, out var start, out var end);
            Assert.That(asset.Chunks[c].vertexCount, Is.EqualTo((end - start) * 4));
        }
    }

    [Test]
    public void AppearStartsHiddenAboveAndEndsExactlyAtRest()
    {
        var rest = new Vector3(.2f, 1f, -.1f);
        PointCloudFx.Appear(7, rest, Vector3.forward, .5f, 0f, out _, out var size0);
        Assert.That(size0, Is.EqualTo(0f), "not started yet");
        PointCloudFx.Appear(7, rest, Vector3.forward, .5f, PointCloudFx.AppearSeconds + .01f, out var end, out var size1);
        Assert.That(Vector3.Distance(end, rest), Is.LessThan(1e-5f));
        Assert.That(size1, Is.EqualTo(1f).Within(1e-5f));
    }

    [Test]
    public void LowerPointsStartFirstAndEverythingFinishesInTime()
    {
        Assert.That(PointCloudFx.AppearDelay(0f, 1f), Is.LessThan(PointCloudFx.AppearDelay(1f, 0f)));
        Assert.That(PointCloudFx.AppearDelay(1f, 1f) + PointCloudFx.AppearEachSeconds,
            Is.LessThanOrEqualTo(PointCloudFx.AppearSeconds + 1e-5f));
    }

    [Test]
    public void FlyInStartsOffTheSurfaceAndOnAverageAbove()
    {
        var lift = 0f;
        for (var i = 0; i < 200; i++)
        {
            var delay = PointCloudFx.AppearDelay(.3f, PointCloudFx.Hash01(i, 4));
            PointCloudFx.Appear(i, Vector3.zero, Vector3.right, .3f, delay + 1e-4f, out var start, out var size);
            Assert.That(size, Is.GreaterThan(0f));
            Assert.That(start.magnitude, Is.GreaterThan(.1f), $"point {i}");
            lift += start.y;
        }
        Assert.That(lift / 200f, Is.GreaterThan(.15f), "the cloud gathers from above");
    }

    [Test]
    public void TouchWeightFallsOffToZeroAtTheRadius()
    {
        var hand = Vector3.zero;
        Assert.That(PointCloudFx.TouchWeight(Vector3.zero, hand, .2f), Is.EqualTo(1f));
        Assert.That(PointCloudFx.TouchWeight(new Vector3(.1f, 0, 0), hand, .2f), Is.EqualTo(Mathf.Pow(.5f, 1.5f)).Within(1e-5f));
        Assert.That(PointCloudFx.TouchWeight(new Vector3(.2f, 0, 0), hand, .2f), Is.EqualTo(0f));
    }

    [Test]
    public void TouchedPointsMoveOutOfTheSurfaceNotIntoIt()
    {
        var hand = new Vector3(0f, 1f, .02f);
        for (var i = 0; i < 50; i++)
        {
            var p = new Vector3((i % 7 - 3) * .02f, 1f + (i % 5 - 2) * .02f, 0f);
            var w = PointCloudFx.TouchWeight(p, hand, .2f);
            var offset = PointCloudFx.TouchOffset(i, p, Vector3.forward, hand, w);
            Assert.That(Vector3.Dot(offset, Vector3.forward), Is.GreaterThan(0f), $"point {i}");
        }
        Assert.That(PointCloudFx.TouchOffset(1, Vector3.zero, Vector3.forward, Vector3.one, 0f), Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void ScatteredPointsShrinkAndSpringBackToRest()
    {
        Assert.That(PointCloudFx.TouchSize(0f), Is.EqualTo(1f));
        Assert.That(PointCloudFx.TouchSize(1f), Is.EqualTo(1f - PointCloudFx.TouchShrink).Within(1e-6f));
        var value = new Vector3(.3f, .1f, 0f);
        var velocity = Vector3.zero;
        for (var i = 0; i < 90; i++) value = PointCloudFx.SpringStep(value, ref velocity, Vector3.zero, PointCloudFx.ReleaseStiffness, 1f / 45f);
        Assert.That(value.magnitude, Is.LessThan(.01f), "back within 2 s");
        var pressed = Vector3.zero;
        velocity = Vector3.zero;
        for (var i = 0; i < 14; i++) pressed = PointCloudFx.SpringStep(pressed, ref velocity, Vector3.up, PointCloudFx.PressStiffness, 1f / 45f);
        Assert.That(pressed.y, Is.GreaterThan(.8f), "bursts out within 0.3 s");
    }

    [Test]
    public void LeafNormalsAreTurnedAwayFromTheTrunk()
    {
        var p = new Vector3(.5f, 1f, 0f);
        Assert.That(PointCloudFx.OrientOutward(p, Vector3.left, 1f, 2f).x, Is.GreaterThan(0f));
        Assert.That(PointCloudFx.OrientOutward(p, Vector3.right, 1f, 2f).x, Is.GreaterThan(0f));
    }

    [Test]
    public void AtlasHoldsSoftRoundOwnAndGlowBlocksSideBySide()
    {
        Assert.That(PointCloudFx.AtlasSizeFor(80000), Is.EqualTo(2048));   // 160 000 blocks of 4×4 ≤ 512 × 512
        const int S = 2048;
        // Point 0: own block at texels 0–3, glow block at texels 4–7 of the bottom row of blocks.
        Assert.That(PointCloudFx.BlockUV(0, false, S, 0, 0), Is.EqualTo(new Vector2(.5f / S, .5f / S)));
        Assert.That(PointCloudFx.BlockUV(0, false, S, 1, 1), Is.EqualTo(new Vector2(3.5f / S, 3.5f / S)));
        Assert.That(PointCloudFx.BlockUV(0, true, S, 0, 0).x, Is.EqualTo(4.5f / S).Within(1e-7f));
        // Point 256 → block 512 → first block of the second row.
        Assert.That(PointCloudFx.BlockUV(256, false, S, 0, 0), Is.EqualTo(new Vector2(.5f / S, 4.5f / S)));
        // Soft round blob: solid centre, softer edges, faint corners.
        Assert.That(PointCloudFx.BlobAlpha(1, 2), Is.EqualTo(1f));
        Assert.That(PointCloudFx.BlobAlpha(0, 2), Is.LessThan(.5f));
        Assert.That(PointCloudFx.BlobAlpha(3, 3), Is.LessThan(PointCloudFx.BlobAlpha(3, 1)));
        var uvs = new Vector2[4];
        PointCloudFx.WriteBlockUVs(uvs, 0, 7, true, S);
        Assert.That(uvs[0], Is.EqualTo(PointCloudFx.BlockUV(7, true, S, 0, 0)));
        Assert.That(uvs[2], Is.EqualTo(PointCloudFx.BlockUV(7, true, S, 1, 1)));
        var g = PointCloudFx.GlowColor(new Color32(0, 0, 0, 255));
        Assert.That(g.r, Is.EqualTo(140));
    }

    [Test]
    public void QuadsLieOnTheSurface()
    {
        foreach (var n in new[] { Vector3.up, Vector3.forward, new Vector3(1, 1, 0).normalized })
        {
            PointCloudFx.QuadAxes(11, n, .01f, out var a, out var b);
            Assert.That(Vector3.Dot(a, n), Is.EqualTo(0f).Within(1e-6f));
            Assert.That(Vector3.Dot(b, n), Is.EqualTo(0f).Within(1e-6f));
            Assert.That(a.magnitude, Is.EqualTo(.01f).Within(1e-6f));
            Assert.That(Vector3.Dot(a, b), Is.EqualTo(0f).Within(1e-6f));
        }
    }
}
