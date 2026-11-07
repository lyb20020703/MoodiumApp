using System.IO;
using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;

public sealed class BubbleHandWakeTests
{
    /// <summary>MSPC v1 bytes (BubbleWorldData), points in the given order (near → far as exported).</summary>
    static byte[] Mspc(params Vector3[] points)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(new[] { (byte)'M', (byte)'S', (byte)'P', (byte)'C' });
        writer.Write(1); writer.Write(points.Length); writer.Write(1.3f); writer.Write(50f);
        foreach (var p in points)
        {
            writer.Write(p.x); writer.Write(p.y); writer.Write(p.z);
            writer.Write(Mathf.FloatToHalf(.05f));
            writer.Write((byte)200); writer.Write((byte)120); writer.Write((byte)80); writer.Write((byte)255);
        }
        writer.Flush();
        return stream.ToArray();
    }

    [Test]
    public void BinsFollowTheEquirectMapping()
    {
        int lon = BubbleHandWakeMath.LonBins, lat = BubbleHandWakeMath.LatBins;
        var forward = BubbleHandWakeMath.Bin(Vector3.forward);
        Assert.That(forward % lon, Is.EqualTo(lon / 2));
        Assert.That(forward / lon, Is.EqualTo(lat / 2));
        Assert.That(BubbleHandWakeMath.Bin(Vector3.up) / lon, Is.EqualTo(lat - 1));
        Assert.That(BubbleHandWakeMath.Bin(Vector3.down) / lon, Is.Zero);
        Assert.That(BubbleHandWakeMath.Bin(Vector3.back), Is.Not.EqualTo(forward));
        Assert.That(BubbleHandWakeMath.Bin(Vector3.forward * 7f), Is.EqualTo(forward), "length does not matter");
    }

    [Test]
    public void LookupKeepsTheNearestPointsOfEachDirection()
    {
        // 0: near, forward. 1: far, forward. 2: up.
        var bytes = Mspc(new Vector3(0, 0, 2), new Vector3(0, 0, 12), new Vector3(0, 9, 0));
        var lookup = BubbleHandWakeMath.BuildLookup(bytes);
        Assert.That(lookup, Is.Not.Null);
        var forward = BubbleHandWakeMath.Bin(Vector3.forward) * BubbleHandWakeMath.PerBin;
        Assert.That(lookup[forward], Is.EqualTo(0), "nearest first");
        Assert.That(lookup[forward + 1], Is.EqualTo(1));
        Assert.That(lookup[forward + 2], Is.EqualTo(-1));
        Assert.That(BubbleHandWakeMath.Pick(lookup, Vector3.forward, 0f), Is.EqualTo(0));
        Assert.That(BubbleHandWakeMath.Pick(lookup, Vector3.forward, .99f), Is.EqualTo(1));
        Assert.That(BubbleHandWakeMath.Pick(lookup, Vector3.up, .5f), Is.EqualTo(2));
    }

    [Test]
    public void EmptyDirectionsBorrowFromNeighboursWithinReach()
    {
        var lookup = BubbleHandWakeMath.BuildLookup(Mspc(new Vector3(0, 0, 3)));
        // ~4° off a filled bin: borrowed. Straight behind: nothing within reach.
        var nearby = Quaternion.Euler(0f, 4f, 0f) * Vector3.forward;
        Assert.That(BubbleHandWakeMath.Pick(lookup, nearby, .5f, 0), Is.EqualTo(-1), "no ring: empty bin");
        Assert.That(BubbleHandWakeMath.Pick(lookup, nearby, .5f), Is.EqualTo(0));
        Assert.That(BubbleHandWakeMath.Pick(lookup, Vector3.back, .5f), Is.EqualTo(-1));
    }

    [Test]
    public void InvalidDataGivesNoLookup()
    {
        Assert.That(BubbleHandWakeMath.BuildLookup(null), Is.Null);
        Assert.That(BubbleHandWakeMath.BuildLookup(new byte[] { 1, 2, 3 }), Is.Null);
        var bytes = Mspc(Vector3.forward);
        bytes[4] = 2; // version
        Assert.That(BubbleHandWakeMath.BuildLookup(bytes), Is.Null);
    }

    [Test]
    public void SpawnRateAndSizeCurves()
    {
        Assert.That(BubbleHandWakeMath.SpawnRate(.1f, .25f, 1.2f, 160f), Is.Zero, "slow hands leave nothing");
        Assert.That(BubbleHandWakeMath.SpawnRate(1.2f, .25f, 1.2f, 160f), Is.EqualTo(160f).Within(1e-3f));
        Assert.That(BubbleHandWakeMath.SpawnRate(3f, .25f, 1.2f, 160f), Is.EqualTo(160f).Within(1e-3f), "capped");
        Assert.That(BubbleHandWakeMath.SpawnRate(.6f, .25f, 1.2f, 160f),
            Is.GreaterThan(0f).And.LessThan(BubbleHandWakeMath.SpawnRate(.9f, .25f, 1.2f, 160f)));
        Assert.That(BubbleHandWakeMath.SizeAt(0f), Is.Zero);
        Assert.That(BubbleHandWakeMath.SizeAt(.3f), Is.EqualTo(1f).Within(1e-3f));
        Assert.That(BubbleHandWakeMath.SizeAt(1f), Is.Zero, "dissolves to nothing");
    }

    [Test]
    public void DragSettlesTheWake()
    {
        var v = new Vector3(1f, 0f, 0f);
        for (var i = 0; i < 120; i++) v = BubbleHandWakeMath.Drift(v, Vector3.zero, .3f, i / 60f, 1f / 60f, 1.6f, 0f, 0f);
        Assert.That(v.magnitude, Is.LessThan(.25f));
    }

    [Test]
    public void WakePointsUseTheSceneColourBehindTheHandAndDissolve()
    {
        var bytes = Mspc(new Vector3(0, 0, 3), new Vector3(3, 0, 0));
        var atlas = BubbleWorldData.RoundAtlasSizeFor(2);
        var colors = new Texture2D(atlas, atlas, TextureFormat.RGBA32, false);
        var go = new GameObject("Wake");
        var scan = new GameObject("Scan").transform;
        try
        {
            var wake = go.AddComponent<BubbleHandWake>();
            wake.Initialize(bytes, colors, 1.3f, false, scan, null, 16);
            Assert.That(wake.WaitForLookup(5000), Is.True);
            Assert.That(wake.WakeMaterial.shader.name, Is.Not.EqualTo(BubbleSplatMaterial.ShaderName), "static round-point material");

            // Hand in front of the eye, toward +X: the colour comes from point 1 (the one seen in that direction).
            Assert.That(wake.SpawnAt(new Vector3(.4f, 0f, 0f), Vector3.zero, Vector3.zero), Is.True);
            Assert.That(wake.LivePoints, Is.EqualTo(1));
            wake.Step(.2f, Vector3.right, Vector3.up);
            var expected = new Vector2[4];
            BubbleWorldData.WriteRoundUVs(expected, 0, 1, atlas);
            var uvs = wake.WakeMesh.uv;
            for (var k = 0; k < 4; k++) Assert.That((uvs[k] - expected[k]).magnitude, Is.LessThan(1e-6f));
            var vertices = wake.WakeMesh.vertices;
            Assert.That((vertices[2] - vertices[0]).magnitude, Is.GreaterThan(0f), "visible quad");

            for (var i = 0; i < 60; i++) wake.Step(.05f, Vector3.right, Vector3.up);
            Assert.That(wake.LivePoints, Is.Zero);
            vertices = wake.WakeMesh.vertices;
            Assert.That((vertices[2] - vertices[0]).magnitude, Is.Zero, "dissolved quad is collapsed");
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(scan.gameObject);
            Object.DestroyImmediate(colors);
        }
    }

    [Test]
    public void FullWakeReusesTheOldestPoint()
    {
        var bytes = Mspc(new Vector3(0, 0, 3));
        var atlas = BubbleWorldData.RoundAtlasSizeFor(1);
        var colors = new Texture2D(atlas, atlas, TextureFormat.RGBA32, false);
        var go = new GameObject("Wake");
        try
        {
            var wake = go.AddComponent<BubbleHandWake>();
            wake.Initialize(bytes, colors, 1.3f, false, null, null, 4);
            Assert.That(wake.WaitForLookup(5000), Is.True);
            for (var i = 0; i < 10; i++) wake.SpawnAt(new Vector3(0f, 0f, .5f), Vector3.zero, Vector3.zero);
            Assert.That(wake.LivePoints, Is.EqualTo(wake.Capacity));
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(colors);
        }
    }
}
