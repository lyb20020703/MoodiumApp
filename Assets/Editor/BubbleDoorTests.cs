using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Mercury door entry, 2026-11-02 b: no bubble, a droplet that opens into a door standing in the room (an occluder booth
/// round the viewer with an oval hole), the world at full size behind it; walk in, walk back out; a switch on top of
/// the door picks the family's world. No next layer.
/// </summary>
public sealed class BubbleDoorTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Vector3 Head = new(0f, 1.6f, 0f);

    // ---------- geometry ----------

    static Mesh Walls(float doorZ, float a, float b, DoorBooth booth)
    {
        var mesh = new Mesh();
        BubbleDoorLayout.Walls(mesh, doorZ, a, b, booth);
        return mesh;
    }

    static DoorBooth Grown => BubbleDoorLayout.BoothFor(new Vector3(1.9f, 1.6f, -1.3f));

    [Test]
    public void TheDoorStandsInFrontOfAStandingViewerWithTheLipOffTheFloor()
    {
        Assert.That(BubbleDoorLayout.DoorDistance, Is.InRange(.9f, 1.5f), "a step or two away");
        Assert.That(BubbleDoorLayout.CenterHeight + BubbleDoorLayout.HalfOpenHeight, Is.GreaterThan(1.95f), "tall enough to walk through");
        Assert.That(BubbleDoorLayout.HalfOpenWidth * 2f, Is.InRange(.8f, 1.1f));
        // The fattest lip with the biggest wobble still clears the floor.
        var lowest = BubbleDoorLayout.CenterHeight - BubbleDoorLayout.HalfOpenHeight - BubbleDoorLayout.RimOffset - .02f - BubbleDoorLayout.RimRadius * BubbleDoorLayout.RimThick;
        Assert.That(lowest, Is.GreaterThan(BubbleDoorLayout.FloorLift));
        var top = BubbleDoorLayout.CenterHeight + BubbleDoorLayout.HalfOpenHeight * 1.04f + BubbleDoorLayout.RimOffset + .02f + BubbleDoorLayout.RimRadius * BubbleDoorLayout.RimThick;
        Assert.That(top, Is.LessThan(BubbleDoorLayout.Height - .2f));
        Assert.That(BubbleDoorLayout.RimRadius * 2f, Is.GreaterThan(.15f), "a fat liquid lip, not a thin tube");
    }

    [Test]
    public void TheBoothGrowsWithTheViewerInTenCentimetreSteps()
    {
        var still = BubbleDoorLayout.BoothFor(Head);
        Assert.That(still, Is.EqualTo(DoorBooth.Default));
        var moved = BubbleDoorLayout.BoothFor(new Vector3(1.93f, 1.6f, -1.42f));
        Assert.That(moved.Right, Is.GreaterThanOrEqualTo(1.93f + BubbleDoorLayout.SidePad - 1e-4f));
        Assert.That(moved.Left, Is.EqualTo(-BubbleDoorLayout.HalfWidth));
        Assert.That(moved.Back, Is.LessThanOrEqualTo(-1.42f - BubbleDoorLayout.BackPad + 1e-4f));
        Assert.That(moved.Right / BubbleDoorLayout.BoothStep, Is.EqualTo(Mathf.Round(moved.Right / BubbleDoorLayout.BoothStep)).Within(1e-3f));
        Assert.That(BubbleDoorLayout.BoothFor(new Vector3(1.94f, 1.6f, -1.43f)), Is.EqualTo(moved), "small steps do not rebuild it");
    }

    static IEnumerable<TestCaseData> Doors()
    {
        yield return new TestCaseData(1.25f, .5f, .92f, false);
        yield return new TestCaseData(1.25f, .005f, .005f, false);
        yield return new TestCaseData(.5f, .5f, 1.05f, false);   // gliding, overshooting: the hole is clamped inside the wall
        yield return new TestCaseData(1.25f, .5f, .92f, true);   // the booth grew with the viewer
    }

    [TestCaseSource(nameof(Doors))]
    public void EveryWallFacesIntoTheBooth(float doorZ, float a, float b, bool grown)
    {
        var booth = grown ? Grown : DoorBooth.Default;
        var mesh = Walls(doorZ, a, b, booth);
        try
        {
            var v = mesh.vertices; var t = mesh.triangles;
            var centre = new Vector3((booth.Left + booth.Right) * .5f, BubbleDoorLayout.Height * .5f, (doorZ + booth.Back) * .5f);
            Assert.That(t.Length, Is.GreaterThan(0));
            for (var i = 0; i < t.Length; i += 3)
            {
                Vector3 p = v[t[i]], q = v[t[i + 1]], r = v[t[i + 2]];
                Assert.That(Vector3.Dot(Vector3.Cross(q - p, r - p), centre - (p + q + r) / 3f), Is.GreaterThan(0f), $"triangle {i / 3} faces out");
            }
        }
        finally { Object.DestroyImmediate(mesh); }
    }

    static IEnumerable<TestCaseData> Views()
    {
        yield return new TestCaseData(1.25f, .5f, .92f, false, new Vector3(0f, 1.6f, 0f));
        yield return new TestCaseData(1.25f, .005f, .005f, false, new Vector3(0f, 1.6f, 0f));
        yield return new TestCaseData(.5f, .5f, .92f, false, new Vector3(0f, 1.6f, 0f));
        yield return new TestCaseData(1.25f, .5f, .92f, false, new Vector3(.3f, 1.5f, .4f));
        yield return new TestCaseData(1.25f, .5f, .92f, true, new Vector3(1.9f, 1.6f, -1.3f));
    }

    [TestCaseSource(nameof(Views))]
    public void FromTheBoothTheWorldShowsOnlyThroughTheOval(float doorZ, float a, float b, bool grown, Vector3 head)
    {
        var mesh = Walls(doorZ, a, b, grown ? Grown : DoorBooth.Default);
        try
        {
            var v = mesh.vertices; var t = mesh.triangles;
            var hole = BubbleDoorLayout.ClampHole(a, b);
            var escaped = 0;
            const int rays = 1500;
            for (var k = 0; k < rays; k++)
            {
                var y = 1f - 2f * (k + .5f) / rays;
                var r = Mathf.Sqrt(1f - y * y);
                var phi = k * Mathf.PI * (3f - Mathf.Sqrt(5f));
                var dir = new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r);
                if (Hits(head, dir, v, t)) continue;
                escaped++;
                Assert.That(dir.z, Is.GreaterThan(0f), $"ray {dir} escapes away from the door");
                var p = head + dir * ((doorZ - head.z) / dir.z);
                var dx = p.x / hole.x; var dy = (p.y - BubbleDoorLayout.CenterHeight) / hole.y;
                Assert.That(dx * dx + dy * dy, Is.LessThanOrEqualTo(1.001f), $"ray {dir} escapes at {p}, outside the oval");
            }
            if (a > .1f) Assert.That(escaped, Is.GreaterThan(0), "the world shows through the door");
            Assert.That(Hits(head, Vector3.back, v, t), Is.True);
        }
        finally { Object.DestroyImmediate(mesh); }
    }

    /// <summary>Two-sided ray / triangle (Möller–Trumbore), edges inclusive so seams between triangles do not leak.</summary>
    static bool Hits(Vector3 o, Vector3 d, Vector3[] v, int[] t)
    {
        for (var i = 0; i < t.Length; i += 3)
        {
            Vector3 a = v[t[i]], e1 = v[t[i + 1]] - a, e2 = v[t[i + 2]] - a;
            var p = Vector3.Cross(d, e2);
            var det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-12f) continue;
            var s = o - a;
            var u = Vector3.Dot(s, p) / det;
            if (u < -1e-5f || u > 1f + 1e-5f) continue;
            var q = Vector3.Cross(s, e1);
            var w = Vector3.Dot(d, q) / det;
            if (w < -1e-5f || u + w > 1f + 1e-5f) continue;
            if (Vector3.Dot(e2, q) / det > 1e-5f) return true;
        }
        return false;
    }

    [Test]
    public void TheBackDoorFillsTheOvalFacingTheWorld()
    {
        var mesh = new Mesh();
        try
        {
            BubbleDoorLayout.BackDoor(mesh, 1.25f, .5f, .92f);
            var v = mesh.vertices; var t = mesh.triangles;
            float area = 0f;
            for (var i = 0; i < t.Length; i += 3)
            {
                var cross = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                Assert.That(cross.z, Is.GreaterThan(0f), "seen from the world side");
                area += cross.magnitude * .5f;
            }
            Assert.That(area, Is.EqualTo(Mathf.PI * .5f * .92f).Within(.02f));
            foreach (var p in v) Assert.That(p.z, Is.EqualTo(1.25f).Within(1e-5f));
        }
        finally { Object.DestroyImmediate(mesh); }
    }

    // ---------- the liquid lip ----------

    [Test]
    public void TheLipIsAFatTubeFacingOutwardThatSwellsAndThins()
    {
        var mesh = new Mesh();
        try
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            var centre = BubbleDoorLayout.DoorCenter(1.25f);
            BubbleDoorLayout.Rim(mesh, centre, .5f, .92f, BubbleDoorLayout.RimRadius, 3f, .5f, new[] { new Vector3(1f, 1f, .2f) }, null, v, n, uv, t);
            var verts = mesh.vertices; var normals = mesh.normals; var tris = mesh.triangles;
            Assert.That(tris.Length, Is.EqualTo(BubbleDoorLayout.RimSegments * BubbleDoorLayout.RimSides * 6));
            for (var i = 0; i < tris.Length; i += 3)
            {
                var cross = Vector3.Cross(verts[tris[i + 1]] - verts[tris[i]], verts[tris[i + 2]] - verts[tris[i]]);
                var normal = normals[tris[i]] + normals[tris[i + 1]] + normals[tris[i + 2]];
                Assert.That(Vector3.Dot(cross, normal), Is.GreaterThan(0f), $"lip triangle {i / 3} faces in");
            }
            // Rebuilding keeps the same triangles and only moves the vertices (one mesh per frame on device).
            BubbleDoorLayout.Rim(mesh, centre, .5f, .92f, BubbleDoorLayout.RimRadius, 3.1f, .5f, null, null, v, n, uv, t);
            Assert.That(mesh.triangles.Length, Is.EqualTo(tris.Length));
        }
        finally { Object.DestroyImmediate(mesh); }
        float thin = float.MaxValue, thick = 0f;
        for (var angle = 0f; angle < Mathf.PI * 2f; angle += .05f)
        {
            var w = BubbleDoorLayout.Wobble(angle, 2f, .15f, null).y;
            thin = Mathf.Min(thin, w); thick = Mathf.Max(thick, w);
        }
        Assert.That(thin, Is.LessThan(.8f), "thinner in places");
        Assert.That(thick, Is.GreaterThan(1.2f), "bulging in others, like a liquid");
        var touched = BubbleDoorLayout.Wobble(1f, 0f, 0f, new[] { new Vector3(1f, 1f, 0f) });
        var untouched = BubbleDoorLayout.Wobble(1f, 0f, 0f, null);
        Assert.That(touched.y, Is.GreaterThan(untouched.y + .1f).Or.EqualTo(BubbleDoorLayout.RimThick), "a fingertip swells the lip where it touches");
    }

    [Test]
    public void TheThinnestLipStillCoversTheHolesEdge()
    {
        var pokes = new[] { new Vector3(.3f, 1f, .1f), new Vector3(2f, 1f, .5f) };
        for (var angle = 0f; angle < Mathf.PI * 2f; angle += .05f)
            for (var time = 0f; time < 12f; time += .37f)
                foreach (var agitation in new[] { 0f, .5f, 1f })
                {
                    var w = BubbleDoorLayout.Wobble(angle, time, agitation, agitation > .9f ? pokes : null);
                    // Centre line sits RimOffset + radial outside the hole's edge: the tube must reach past the edge.
                    Assert.That(BubbleDoorLayout.RimRadius * w.y, Is.GreaterThan(BubbleDoorLayout.RimOffset + w.x + .003f), $"angle {angle}, t {time}");
                    // The droplet: the lip closes over the middle.
                    var d = BubbleDoorLayout.Wobble(angle, time, BubbleDoorLayout.DropletAgitation, null);
                    Assert.That(BubbleDoorLayout.DropletTube * d.y, Is.GreaterThan(BubbleDoorLayout.DropletHole + BubbleDoorLayout.RimOffset + d.x));
                }
    }

    [Test]
    public void TheRainbowFilmShowsOnlyWhereTheLipIsSeenEdgeOn()
    {
        Assert.That(BubbleDoorLayout.FilmAlpha(0f), Is.EqualTo(0f));
        Assert.That(BubbleDoorLayout.FilmAlpha(.4f), Is.EqualTo(0f), "the front of the lip is pure mirror");
        Assert.That(BubbleDoorLayout.FilmAlpha(1f), Is.InRange(.6f, .8f));
        var mesh = new Mesh();
        try
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            var centre = BubbleDoorLayout.DoorCenter(1.25f);
            var eye = new Vector3(0f, centre.y, -20f);   // far in front, looking along +z
            BubbleDoorLayout.Rim(mesh, centre, .5f, .92f, BubbleDoorLayout.RimRadius, 0f, 0f, null, eye, v, n, uv, t);
            float facingU = 1f, edgeU = 0f;
            for (var i = 0; i < n.Count; i++)
            {
                if (Vector3.Dot(n[i], Vector3.back) > .99f) facingU = Mathf.Min(facingU, uv[i].x);
                if (Mathf.Abs(n[i].z) < .05f) edgeU = Mathf.Max(edgeU, uv[i].x);
                Assert.That(uv[i].x, Is.InRange(.03f, .97f), "kept off the texture's edges");
            }
            Assert.That(facingU, Is.LessThan(.1f));
            Assert.That(edgeU, Is.GreaterThan(.85f));
        }
        finally { Object.DestroyImmediate(mesh); }
        var texture = BubbleDoorLayout.FilmTexture();
        try { Assert.That(texture.width, Is.EqualTo(64)); }   // alpha per column is FilmAlpha (the texture is not readable)
        finally { Object.DestroyImmediate(texture); }
    }

    [Test]
    public void TheDoorTearsOpenFromTheDropletFoldsBackAndGlides()
    {
        var start = BubbleDoorLayout.OpenSize(0f);
        Assert.That(Vector2.Distance(start, new Vector2(BubbleDoorLayout.DropletHole, BubbleDoorLayout.DropletHole)), Is.LessThan(1e-4f), "starts as the droplet");
        var open = BubbleDoorLayout.OpenSize(1f);
        Assert.That(open.x, Is.EqualTo(BubbleDoorLayout.HalfOpenWidth).Within(1e-4f));
        Assert.That(open.y, Is.EqualTo(BubbleDoorLayout.HalfOpenHeight).Within(1e-4f));
        var tallest = 0f; var widest = 0f;
        for (var t = 0f; t <= 1f; t += .01f) { var s = BubbleDoorLayout.OpenSize(t); tallest = Mathf.Max(tallest, s.y); widest = Mathf.Max(widest, s.x); }
        Assert.That(widest, Is.GreaterThan(BubbleDoorLayout.HalfOpenWidth * 1.03f), "a liquid overshoot");
        Assert.That(tallest, Is.LessThan(BubbleDoorLayout.HalfOpenHeight * 1.04f), "but the lip stays off the floor");

        Assert.That(BubbleDoorLayout.FoldSize(0f, open), Is.EqualTo(open));
        Assert.That(BubbleDoorLayout.FoldSize(1f, open).x, Is.EqualTo(BubbleDoorLayout.DropletHole).Within(1e-5f));
        Assert.That(BubbleDoorLayout.FoldSize(.5f, open).x, Is.GreaterThan(open.x * .2f), "slow first");

        Assert.That(BubbleDoorLayout.GlideZ(0f, 1.25f, -.35f), Is.EqualTo(1.25f));
        Assert.That(BubbleDoorLayout.GlideZ(1f, 1.25f, -.35f), Is.EqualTo(-.35f).Within(1e-5f));
        Assert.That(BubbleDoorLayout.GlideTarget(true, .1f), Is.EqualTo(.1f - BubbleDoorLayout.GlidePast).Within(1e-5f));
        Assert.That(BubbleDoorLayout.GlideTarget(false, 2f), Is.EqualTo(2f + BubbleDoorLayout.DoorDistance).Within(1e-5f));
    }

    [Test]
    public void CrossingAndReachingUseTheDoorPlane()
    {
        var d = BubbleDoorLayout.DoorDistance;
        Assert.That(BubbleDoorLayout.NextInRoom(true, new Vector3(0f, 1.6f, d + .02f), d), Is.True, "a lean at the door does not flip it");
        Assert.That(BubbleDoorLayout.NextInRoom(true, new Vector3(0f, 1.6f, d + .1f), d), Is.False);
        Assert.That(BubbleDoorLayout.NextInRoom(false, new Vector3(0f, 1.6f, d - .02f), d), Is.False);
        Assert.That(BubbleDoorLayout.NextInRoom(false, new Vector3(0f, 1.6f, d - .1f), d), Is.True);
        Assert.That(BubbleDoorLayout.ReachingIn(new Vector3(0f, 1.2f, d + .08f), d, .5f, .92f), Is.True);
        Assert.That(BubbleDoorLayout.ReachingIn(new Vector3(0f, 1.2f, d - .02f), d, .5f, .92f), Is.False, "in front of the door");
        Assert.That(BubbleDoorLayout.ReachingIn(new Vector3(.8f, 1.2f, d + .08f), d, .5f, .92f), Is.False, "beside it, through the wall");
    }

    [Test]
    public void NearLayersThatWouldFloatInTheBoothWait()
    {
        // Layer 0: one point 0.5 m in front of the viewer. Layer 1: only beyond the door. Layer 2: none sampled. Layer 3: 2.5 m to the right.
        var samples = new[] { new[] { new Vector3(0f, 1.2f, .5f) }, new[] { new Vector3(0f, 1.2f, 2.5f), new Vector3(.5f, .5f, 4f) }, null, new[] { new Vector3(2.5f, 1.2f, 0f) } };
        var identity = new[] { Matrix4x4.identity, Matrix4x4.identity, Matrix4x4.identity, Matrix4x4.identity };
        Assert.That(BubbleDoorLayout.HeldLayers(samples, identity, BubbleDoorLayout.DoorDistance), Is.EqualTo(new[] { true, false, false, false }));
        Assert.That(BubbleDoorLayout.HeldLayers(samples, identity, BubbleDoorLayout.DoorDistance, Grown), Is.EqualTo(new[] { true, false, false, true }),
            "the booth grew to the right: that layer waits too");
        var groundOnly = BubbleDoorLayout.HeldLayers(new[] { new[] { new Vector3(0f, 0f, .3f) } }, identity, BubbleDoorLayout.DoorDistance);
        Assert.That(groundOnly[0], Is.False, "ground under the real floor stays (the floor hides it)");
    }

    [Test]
    public void NearSamplesComeFromTheLayersCloseToTheScanCentre()
    {
        const int count = 200, layers = 20;
        var bytes = new byte[20 + count * BubbleWorldData.RecordSize];
        bytes[0] = (byte)'M'; bytes[1] = (byte)'S'; bytes[2] = (byte)'P'; bytes[3] = (byte)'C';
        BitConverter.GetBytes(1).CopyTo(bytes, 4);
        BitConverter.GetBytes(count).CopyTo(bytes, 8);
        BitConverter.GetBytes(1.4f).CopyTo(bytes, 12);
        BitConverter.GetBytes(20f).CopyTo(bytes, 16);
        for (var i = 0; i < count; i++) BitConverter.GetBytes(.1f * (i + 1)).CopyTo(bytes, 20 + i * BubbleWorldData.RecordSize + 8); // z = 0.1 … 20 m
        Assert.That(BubbleWorldData.TryReadSummary(bytes, layers, out var n, out _, out var radii), Is.True);
        var samples = BubbleWorldController.NearSamples(bytes, n, radii, layers, 4f);
        Assert.That(samples[0], Has.Length.EqualTo(4), "every third of 10 points");
        Assert.That(samples[0][1].z, Is.EqualTo(.4f).Within(1e-5f));
        Assert.That(samples[3], Is.Not.Null, "starts at 3 m");
        Assert.That(samples[4], Is.Null, "starts at 4 m: beyond the booth");
    }

    // ---------- the door component ----------

    sealed class Fixture : IDisposable
    {
        public readonly GameObject Host = new("door host"), Viewer = new("viewer");
        public BubbleDoor Door;
        public Vector3? Tip;
        public readonly List<bool> Crossings = new();
        public readonly Vector3 Park = new(-.3f, 1f, .4f);
        public Fixture()
        {
            Viewer.transform.position = Head;
            Door = BubbleDoor.Create(Host.transform, Head, Quaternion.identity, 0f, Viewer.transform, _ => Tip, null, Park);
            if (Door == null) Assert.Ignore("AR occlusion material not in this project (Moodium ▸ Portal Probe ▸ Create Occluder Material)");
            Door.Driven = true;
            Door.WentThrough += Crossings.Add;
        }
        public GameObject Part(string name) => Door.transform.Find(name).gameObject;
        public bool Walls => Part("Walls (occluder)").activeSelf;
        public bool Back => Part("Back Door (occluder)").activeSelf;
        public void Run(float seconds, Vector3? head = null)
        {
            if (head.HasValue) Viewer.transform.position = head.Value;
            for (var t = 0f; t < seconds && Door != null; t += .05f) Door.Step(.05f, Viewer.transform.position, Vector3.forward);
        }
        public void Open() => Run(BubbleDoorLayout.ArriveSeconds + BubbleDoorLayout.OpenSeconds + .2f);
        public void Dispose() { Object.DestroyImmediate(Host); Object.DestroyImmediate(Viewer); }
    }

    [Test]
    public void TheDropletArrivesAndTearsOpenIntoTheDoor()
    {
        using var f = new Fixture();
        Assert.That(f.Door.State, Is.EqualTo(BubbleDoor.Stage.Arriving));
        Assert.That(f.Door.ShowsWorld || f.Walls || f.Back, Is.False, "a droplet hides nothing in the room");
        f.Run(BubbleDoorLayout.ArriveSeconds + .05f);
        Assert.That(f.Door.State, Is.EqualTo(BubbleDoor.Stage.Opening));
        Assert.That(f.Door.ShowsWorld && f.Walls && !f.Back, Is.True);
        Assert.That(f.Door.Collider.enabled, Is.False, "not pressable while tearing open");
        f.Run(BubbleDoorLayout.OpenSeconds + .1f);
        Assert.That(f.Door.State, Is.EqualTo(BubbleDoor.Stage.Open));
        Assert.That(f.Door.HalfSize, Is.EqualTo(new Vector2(BubbleDoorLayout.HalfOpenWidth, BubbleDoorLayout.HalfOpenHeight)));
        Assert.That(f.Door.Collider.enabled, Is.True);
        f.Run(30f);
        Assert.That(f.Door.State, Is.EqualTo(BubbleDoor.Stage.Open), "no automatic glide: the viewer walks in when they want to");
    }

    [Test]
    public void TheViewerWalksInAndBackOutAndInAgain()
    {
        using var f = new Fixture();
        f.Open();
        var d = BubbleDoorLayout.DoorDistance;
        f.Run(.2f, new Vector3(0f, 1.6f, d + .02f));
        Assert.That(f.Door.InRoom && f.Crossings.Count == 0, Is.True, "leaning at the door");
        f.Run(.2f, new Vector3(0f, 1.6f, d + .4f));
        Assert.That(f.Crossings, Is.EqualTo(new[] { true }));
        Assert.That(!f.Walls && f.Back, Is.True, "looking back, the door shows the room");
        Assert.That(f.Door.Collider.enabled, Is.False);
        f.Run(.2f, new Vector3(0f, 1.6f, .3f));
        Assert.That(f.Crossings, Is.EqualTo(new[] { true, false }));
        Assert.That(f.Door.InRoom && f.Walls && !f.Back && f.Door.State == BubbleDoor.Stage.Open, Is.True, "back in the room, the world waits behind the door");
        f.Run(.2f, new Vector3(.2f, 1.6f, d + .4f));
        Assert.That(f.Crossings, Is.EqualTo(new[] { true, false, true }), "and in again");
    }

    [Test]
    public void HoldingItClosedFoldsItIntoADropletAndLettingGoOpensIt()
    {
        using var f = new Fixture();
        f.Open();
        f.Door.HoldClosed = true;
        f.Run(.1f);
        Assert.That(f.Door.State, Is.EqualTo(BubbleDoor.Stage.Folding));
        Assert.That(f.Door.ShowsWorld, Is.True, "the world shows through the closing hole until it is shut");
        f.Run(BubbleDoorLayout.FoldSeconds + .1f);
        Assert.That(f.Door.State, Is.EqualTo(BubbleDoor.Stage.Droplet));
        Assert.That(f.Door.ShowsWorld || f.Walls || f.Back, Is.False);
        f.Run(2f);
        Assert.That(f.Door.State, Is.EqualTo(BubbleDoor.Stage.Droplet), "held");
        f.Door.HoldClosed = false;
        f.Run(.1f);
        Assert.That(f.Door.State, Is.EqualTo(BubbleDoor.Stage.Arriving).Or.EqualTo(BubbleDoor.Stage.Opening));
        f.Open();
        Assert.That(f.Door.State, Is.EqualTo(BubbleDoor.Stage.Open));
        Assert.That(f.Door.DoorZ, Is.EqualTo(BubbleDoorLayout.DoorDistance).Within(1e-4f));
    }

    [Test]
    public void WalkingAwayLeavesTheDoorOpenUntilExplicitlyParked()
    {
        using var f = new Fixture();
        f.Open();
        var version = f.Door.Version;
        f.Run(.5f, new Vector3(1.9f, 1.6f, -1.3f));
        Assert.That(f.Door.Version, Is.Not.EqualTo(version), "the booth grew round the viewer");
        Assert.That(f.Door.Booth.Right, Is.GreaterThan(2.8f));
        Assert.That(f.Door.State, Is.EqualTo(BubbleDoor.Stage.Open), "door remains open");
        f.Run(12f, new Vector3(0f, 1.6f, BubbleDoorLayout.DoorDistance - 3.2f));
        Assert.That(f.Door.State, Is.EqualTo(BubbleDoor.Stage.Open));
        f.Door.RequestPark();
        f.Run(BubbleDoorLayout.FoldSeconds + BubbleDoorLayout.ParkSeconds + .4f);
        Assert.That(f.Door.State, Is.EqualTo(BubbleDoor.Stage.Parked));
        Assert.That(f.Door.IsParked && !f.Door.ShowsWorld && !f.Walls && !f.Back, Is.True, "no occluders in the room while parked");
        Assert.That(Vector3.Distance(f.Door.CentreWorld, f.Park), Is.LessThan(.05f), "beside the sensory bag");
        f.Run(2f, Head);
        Assert.That(f.Door.State, Is.EqualTo(BubbleDoor.Stage.Parked), "coming back does not open it: the room stays free for walking");
        f.Door.Press();
        f.Run(.1f);
        Assert.That(f.Door.State, Is.EqualTo(BubbleDoor.Stage.Arriving));
        f.Open();
        Assert.That(f.Door.State, Is.EqualTo(BubbleDoor.Stage.Open), "a touch on the droplet opens it again");
    }

    [Test]
    public void TheCrossParksTheDoorAndTheParkedDropletOpensWhereTheViewerLooks()
    {
        using var f = new Fixture();
        f.Open();
        f.Door.RequestPark();
        Assert.That(f.Door.IsParked, Is.True, "parked from the moment it starts folding (walking may resume)");
        f.Run(BubbleDoorLayout.FoldSeconds + BubbleDoorLayout.ParkSeconds + .3f);
        Assert.That(f.Door.State, Is.EqualTo(BubbleDoor.Stage.Parked));
        Assert.That(Vector3.Distance(f.Door.CentreWorld, f.Park), Is.LessThan(.05f));
        var elsewhere = new Vector3(1.5f, 1.6f, -1f);
        f.Viewer.transform.position = elsewhere;
        var gaze = new Vector3(-1f, 0f, 1f).normalized;
        f.Door.Press();
        f.Door.Step(.05f, elsewhere, gaze);
        Assert.That(f.Door.State, Is.EqualTo(BubbleDoor.Stage.Arriving));
        Assert.That(Vector3.Angle(f.Door.Room.forward, gaze), Is.LessThan(1f), "opens where the viewer looks");
        Assert.That(new Vector2(f.Door.Room.position.x - elsewhere.x, f.Door.Room.position.z - elsewhere.z).magnitude, Is.LessThan(1e-3f), "with the booth round them");
        Assert.That(f.Door.DoorZ, Is.EqualTo(BubbleDoorLayout.DoorDistance).Within(1e-4f));
    }

    [Test]
    public void DraggingTheRingSlidesTheDoorOverTheFloorOnly()
    {
        using var f = new Fixture();
        f.Open();
        var d = f.Door.DoorZ;
        Assert.That(f.Door.BeginMove(f.Door.Room.TransformPoint(new Vector3(0f, BubbleDoorLayout.CenterHeight, d))), Is.False, "a pinch in the opening is not a grab");
        var grab = f.Door.Room.TransformPoint(new Vector3(BubbleDoorLayout.HalfOpenWidth + .03f, BubbleDoorLayout.CenterHeight + .2f, d));
        Assert.That(f.Door.BeginMove(grab), Is.True);
        var version = f.Door.Version;
        f.Door.MoveTo(grab + new Vector3(.8f, .5f, .4f));   // up and sideways: only the floor position follows
        Assert.That(f.Door.Version, Is.Not.EqualTo(version));
        var centre = f.Door.Room.TransformPoint(BubbleDoorLayout.DoorCenter(f.Door.DoorZ));
        Assert.That(centre.y, Is.EqualTo(BubbleDoorLayout.CenterHeight).Within(1e-4f), "the height never changes");
        Assert.That(f.Door.Room.position.y, Is.EqualTo(0f).Within(1e-5f));
        var toDoor = centre - Head; toDoor.y = 0f;
        Assert.That(Vector3.Angle(f.Door.Room.forward, toDoor), Is.LessThan(1f), "it turns to face the viewer");
        Assert.That(centre.x, Is.GreaterThan(.5f), "moved sideways");
        f.Door.MoveTo(Head + Vector3.forward * .1f);
        centre = f.Door.Room.TransformPoint(BubbleDoorLayout.DoorCenter(f.Door.DoorZ));
        Assert.That(new Vector2(centre.x - Head.x, centre.z - Head.z).magnitude, Is.GreaterThanOrEqualTo(BubbleDoorLayout.MinDoorDistance - 1e-3f), "never onto the viewer");
        f.Door.MoveTo(Head + Vector3.forward * 20f);
        centre = f.Door.Room.TransformPoint(BubbleDoorLayout.DoorCenter(f.Door.DoorZ));
        Assert.That(new Vector2(centre.x - Head.x, centre.z - Head.z).magnitude, Is.LessThanOrEqualTo(BubbleDoorLayout.MaxDoorDistance + 1e-3f));
        f.Door.EndMove();
        f.Run(.2f);
        Assert.That(f.Door.InRoom && f.Crossings.Count == 0 && f.Door.State == BubbleDoor.Stage.Open, Is.True, "moving it never takes the viewer through");
    }

    [Test]
    public void TheHandleTellsATapFromADrag()
    {
        using var f = new Fixture();
        var handle = f.Door.Collider.GetComponent<BubbleDoorHandle>();
        Assert.That(handle, Is.Not.Null);
        Assert.That(f.Door.Collider.GetComponent<Moodium.Flow.MoodiumSpatialButton>(), Is.Null, "taps go through the handle");
        f.Open();
        var ring = f.Door.Room.TransformPoint(new Vector3(-BubbleDoorLayout.HalfOpenWidth - .03f, BubbleDoorLayout.CenterHeight, f.Door.DoorZ));
        handle.PointerStarted(1, ring);
        handle.PointerMoved(1, ring + Vector3.right * .01f);
        Assert.That(f.Door.Moving, Is.False, "a centimetre is still a tap");
        handle.PointerMoved(1, ring + Vector3.left * .3f);
        Assert.That(f.Door.Moving && handle.Dragging, Is.True);
        handle.PointerEnded(1);
        Assert.That(f.Door.Moving, Is.False);
        f.Door.RequestPark();
        f.Run(BubbleDoorLayout.FoldSeconds + BubbleDoorLayout.ParkSeconds + .3f);
        handle.PointerStarted(2, f.Park);
        handle.PointerEnded(2);
        f.Run(.1f);
        Assert.That(f.Door.State, Is.EqualTo(BubbleDoor.Stage.Arriving), "a tap on the parked droplet opens it");
    }

    [Test]
    public void AHandThroughTheDoorBringsItOverAndHomeTakesItBack()
    {
        using var f = new Fixture();
        f.Open();
        f.Tip = new Vector3(0f, 1.2f, BubbleDoorLayout.DoorDistance + .1f);
        f.Run(.1f);
        Assert.That(f.Door.State, Is.EqualTo(BubbleDoor.Stage.Gliding));
        f.Tip = null;
        f.Run(BubbleDoorLayout.GlideSeconds + .2f);
        Assert.That(f.Crossings, Is.EqualTo(new[] { true }), "the door passed the standing viewer");
        Assert.That(f.Door.DoorZ, Is.EqualTo(Head.z - BubbleDoorLayout.GlidePast).Within(.01f));
        Assert.That(f.Back, Is.True);
        f.Door.RequestGlideBack();
        f.Run(BubbleDoorLayout.GlideSeconds + .2f);
        Assert.That(f.Crossings, Is.EqualTo(new[] { true, false }));
        Assert.That(f.Door.InRoom && f.Walls, Is.True);
        Assert.That(f.Door.DoorZ, Is.EqualTo(Head.z + BubbleDoorLayout.DoorDistance).Within(.01f), "it settles in front again");
    }

    [Test]
    public void ClosingDropsTheOccludersAtOnceAndThenTheDoor()
    {
        using var f = new Fixture();
        f.Open();
        f.Door.Close();
        Assert.That(f.Walls || f.Back, Is.False, "nothing stays hidden while it closes");
        f.Run(BubbleDoorLayout.CloseSeconds + .2f);
        Assert.That(f.Door == null, Is.True, "removed");
    }

    // ---------- the halo ----------

    [Test]
    public void TheHaloIsAMirrorSkirtFacingTheRoomThatFadesOut()
    {
        Assert.That(BubbleDoorLayout.SkirtAlpha(0f), Is.InRange(.4f, .6f));
        Assert.That(BubbleDoorLayout.SkirtAlpha(1f), Is.EqualTo(0f).Within(1e-5f), "it melts into the real room");
        for (var s = 0f; s < 1f; s += .1f) Assert.That(BubbleDoorLayout.SkirtAlpha(s + .1f), Is.LessThanOrEqualTo(BubbleDoorLayout.SkirtAlpha(s)));
        var mesh = new Mesh();
        try
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            var centre = BubbleDoorLayout.DoorCenter(1.25f);
            BubbleDoorLayout.Skirt(mesh, centre, .5f, .9f, 2f, .3f, null, v, n, uv, t);
            var verts = mesh.vertices; var tris = mesh.triangles;
            Assert.That(tris.Length, Is.EqualTo(BubbleDoorLayout.RimSegments * BubbleDoorLayout.SkirtRings * 6));
            for (var i = 0; i < tris.Length; i += 3)
            {
                var cross = Vector3.Cross(verts[tris[i + 1]] - verts[tris[i]], verts[tris[i + 2]] - verts[tris[i]]);
                Assert.That(cross.z, Is.LessThan(0f), $"halo triangle {i / 3} faces away from the room");
            }
            foreach (var p in verts)
            {
                Assert.That(p.z, Is.LessThan(1.25f - .002f), "in front of the door plane, never cut by the booth's wall");
                var dx = p.x / .5f; var dy = (p.y - centre.y) / .9f;
                Assert.That(dx * dx + dy * dy, Is.GreaterThan(1f), "outside the opening");
            }
        }
        finally { Object.DestroyImmediate(mesh); }
        using var f = new Fixture();
        var halo = f.Part("Rim Halo");
        f.Open();
        Assert.That(halo.GetComponent<MeshRenderer>().enabled, Is.True, "the open door glows");
        f.Door.SetHalo(false);
        Assert.That(halo.activeSelf, Is.False);
    }

    // ---------- the controls ----------

    [Test]
    public void TheControlsSitOnTheLipAndBesideTheDoor()
    {
        Assert.That(DoorSwitchRules.Family(BubbleWorldData.NestedCandyFolder), Is.EqualTo(new[] { BubbleWorldData.CandyFolder, BubbleWorldData.NestedCandyFolder }));
        Assert.That(DoorSwitchRules.Family(BubbleWorldData.NestedNatureFolder), Does.Not.Contain(BubbleWorldData.CandyFolder), "candy and nature never mix");
        var worlds = DoorSwitchRules.Family(BubbleWorldData.NatureFolder);
        Assert.That(worlds, Is.EqualTo(new[] { BubbleWorldData.NatureFolder, BubbleWorldData.NestedNatureFolder }),
            "summer, autumn, winter");
        Assert.That(DoorSwitchRules.Next(worlds, BubbleWorldData.NatureFolder, 1), Is.EqualTo(BubbleWorldData.NestedNatureFolder));
        Assert.That(DoorSwitchRules.Next(worlds, BubbleWorldData.NatureFolder, -1), Is.EqualTo(BubbleWorldData.NestedNatureFolder), "wraps round");
        Assert.That(DoorSwitchRules.Next(new[] { "a", "b", "c" }, "a", -1), Is.EqualTo("c"));
        const float height = .067f;
        var name = DoorSwitchRules.NameCentre(1.25f, height);
        var lipCentreTop = BubbleDoorLayout.CenterHeight + BubbleDoorLayout.HalfOpenHeight + BubbleDoorLayout.RimOffset;
        Assert.That(name.y, Is.GreaterThan(lipCentreTop).And.LessThan(2.2f), "on the lip, lower than before (2.24 m)");
        Assert.That(name.z, Is.LessThan(1.25f - BubbleDoorLayout.RimRadius), "in front of the lip");
        var left = DoorSwitchRules.ArrowCentre(-1, 1.25f); var right = DoorSwitchRules.ArrowCentre(1, 1.25f);
        Assert.That(left.x, Is.EqualTo(-right.x).Within(1e-5f));
        Assert.That(right.x, Is.GreaterThan(BubbleDoorLayout.HalfOpenWidth + BubbleDoorLayout.RimOffset + BubbleDoorLayout.RimRadius * BubbleDoorLayout.RimThick), "outside the fattest lip");
        Assert.That(right.y, Is.EqualTo(BubbleDoorLayout.CenterHeight), "at hand height");
        var close = DoorSwitchRules.CloseCentre(1.25f);
        Assert.That(close.x, Is.GreaterThan(0f));
        Assert.That(close.y, Is.InRange(1.6f, 2f), "upper right, below the name");
        var dx = close.x / (BubbleDoorLayout.HalfOpenWidth + BubbleDoorLayout.RimOffset + BubbleDoorLayout.RimRadius * BubbleDoorLayout.RimThick);
        var dy = (close.y - BubbleDoorLayout.CenterHeight) / (BubbleDoorLayout.HalfOpenHeight + BubbleDoorLayout.RimOffset + BubbleDoorLayout.RimRadius * BubbleDoorLayout.RimThick);
        Assert.That(dx * dx + dy * dy, Is.GreaterThan(1f), "outside the lip");
        var chevron = DoorSwitchRules.Chevron(1);
        Assert.That(chevron.tip.x, Is.GreaterThan(chevron.a.x), "the right arrow points right");
        Assert.That(DoorSwitchRules.Chevron(-1).tip.x, Is.LessThan(DoorSwitchRules.Chevron(-1).a.x));
    }

    [Test]
    public void TheArrowsSwitchTheWorldAndTheCrossClosesTheDoor()
    {
        using var f = new Fixture();
        var chosen = new List<string>(); var closed = 0;
        var worlds = DoorSwitchRules.Family(BubbleWorldData.NatureFolder);
        var s = BubbleDoorSwitcher.Create(f.Door, worlds, BubbleWorldData.NatureFolder, chosen.Add, () => closed++);
        Assert.That(s.LeftArrow != null && s.RightArrow != null && s.CloseButton != null, Is.True);
        s.Step(Head, false, 0f, 0f);
        Assert.That(s.RightArrow.gameObject.activeSelf || s.Name.gameObject.activeSelf, Is.False, "hidden");
        s.Step(1);
        Assert.That(chosen, Is.Empty, "nothing works while hidden");
        s.Step(Head, true, 0f, 0f);
        Assert.That(s.RightArrow.gameObject.activeSelf && s.LeftArrow.gameObject.activeSelf && s.CloseButton.gameObject.activeSelf, Is.True);
        if (s.Name.sprite != null) Assert.That(s.Name.gameObject.activeSelf && s.Name.transform.position.y > BubbleDoorLayout.CenterHeight + BubbleDoorLayout.HalfOpenHeight, Is.True);
        Assert.That(s.RightArrow.position.x, Is.GreaterThan(s.LeftArrow.position.x), "right is on the viewer's right");
        Assert.That(s.RightArrow.GetComponent<Moodium.Flow.MoodiumSpatialButton>(), Is.Not.Null);
        s.Step(1);
        Assert.That(chosen, Is.EqualTo(new[] { BubbleWorldData.NestedNatureFolder }));
        s.SetBusy(BubbleWorldData.NestedNatureFolder);
        s.Step(-1);
        Assert.That(chosen, Has.Count.EqualTo(1), "busy");
        s.SetCurrent(BubbleWorldData.NestedNatureFolder);
        Assert.That(s.Current, Is.EqualTo(BubbleWorldData.NestedNatureFolder));
        s.Step(-1);
        Assert.That(chosen[^1], Is.EqualTo(BubbleWorldData.NatureFolder));
        s.CloseButton.GetComponent<Moodium.Flow.MoodiumSpatialButton>().Press();
        Assert.That(closed, Is.EqualTo(1));
    }

    [Test]
    public void RoomContentBeyondTheDoorIsHiddenOnlyWhileTheDoorShowsTheWorld()
    {
        var room = new GameObject("room frame");
        var beyond = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var near = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            beyond.transform.position = new Vector3(.3f, .5f, 2.5f);   // a tree behind where the door stands
            near.transform.position = new Vector3(.3f, .5f, .4f);      // a flower at the viewer's feet
            var content = new BubbleDoorRoomContent();
            content.Apply(new[] { beyond, near }, room.transform, 1.25f);
            Assert.That(beyond.GetComponent<Renderer>().enabled, Is.False);
            Assert.That(near.GetComponent<Renderer>().enabled, Is.True);
            Assert.That(content.HiddenCount, Is.EqualTo(1));
            content.Restore();
            Assert.That(beyond.GetComponent<Renderer>().enabled, Is.True);
        }
        finally { Object.DestroyImmediate(room); Object.DestroyImmediate(beyond); Object.DestroyImmediate(near); }
    }

    // ---------- the controller ----------

    [Test]
    public void OnlyTheRoomEntryUsesTheDoorAndThereIsNoNextLayer()
    {
        var host = new GameObject("world");
        var child = new GameObject("nested");
        child.transform.SetParent(host.transform);
        try
        {
            var outer = host.AddComponent<BubbleWorldController>();
            var inner = child.AddComponent<BubbleWorldController>();
            var eligible = typeof(BubbleWorldController).GetProperty("DoorEligible", Private);
            Assert.That((bool)typeof(BubbleWorldController).GetField("m_DoorEntry", Private).GetValue(outer), Is.True, "on by default");
            Assert.That((bool)typeof(BubbleWorldController).GetField("m_NextLayer", Private).GetValue(outer), Is.False, "no next layer by default");
            Assert.That((bool)typeof(BubbleWorldController).GetField("m_DoorHalo", Private).GetValue(outer), Is.True, "the halo is on by default");
            Assert.That(outer.DoorBlocksWalking || outer.DoorParked || outer.DoorShowsWorldFromRoom, Is.False, "no door yet");
            Assert.That((bool)eligible.GetValue(outer), Is.True);
            Assert.That((bool)eligible.GetValue(inner), Is.False);
            typeof(BubbleWorldController).GetField("m_DoorEntry", Private).SetValue(outer, false);
            Assert.That((bool)eligible.GetValue(outer), Is.False, "switched off: the bubble entry as before");
            Assert.That(outer.DoorOpen, Is.False);
            Assert.That(BubbleWorldController.MotionStage(BubbleWorldController.Phase.Doorway), Is.EqualTo(BubbleSplatMotion.Stage.Inside));

            typeof(BubbleWorldController).GetField("m_Folder", Private).SetValue(outer, BubbleWorldData.NatureFolder);
            typeof(BubbleWorldController).GetMethod("PrepareNested", Private).Invoke(outer, null);
            Assert.That(typeof(BubbleWorldController).GetField("m_Nested", Private).GetValue(outer), Is.Null, "no next-layer bubble inside the world");
        }
        finally { Object.DestroyImmediate(host); }
    }

    static string Flat(string source) => source.Replace("\r\n", "\n");

    [Test]
    public void TheControllerRunsTheDoorSession()
    {
        var source = Flat(File.ReadAllText("Assets/Scripts/Moodium/BubbleWorld/BubbleWorldController.cs"));
        var run = source.IndexOf("IEnumerator Run()", StringComparison.Ordinal);
        var doorBranch = source.IndexOf("BeginDoor(viewer);", run, StringComparison.Ordinal);
        var bubble = source.IndexOf("// Appear: pop in at the lower left of the gaze.", run, StringComparison.Ordinal);
        Assert.That(doorBranch, Is.GreaterThan(run).And.LessThan(bubble), "no bubble: the droplet comes first");
        Assert.That(source, Does.Contain("if (!m_DoorFellBack) { m_Routine = null; yield break; }"), "no occlusion material: the bubble entry carries on");
        Assert.That(source, Does.Contain("if (m_Phase == Phase.Doorway) { if (m_Door != null) m_Door.Press(); return; }"), "a pinch on the door");
        Assert.That(source, Does.Contain("if (m_DoorMode && m_Door != null) { m_Door.RequestGlideBack(); return; }"), "home is back out through the door");
        Assert.That(source, Does.Contain("if (m_DoorMode && m_Door != null) return false;"), "returning to the room does not summon a second entry");
        Assert.That(source, Does.Contain("Left?.Invoke(LeaveTarget.Room);\n        }\n\n        /// <summary>Through the door"), "walking back out returns to the room");
        Assert.That(source, Does.Contain("Touched?.Invoke();\n                return;"), "walking in enters");
        Assert.That(source, Does.Contain("m_Door == null && WanderState"), "walking back to the door does not fade the world");
        Assert.That(source, Does.Contain("if (!m_NextLayer || m_NatureWorldDepth != 0 || m_Nested != null) return;"));
        Assert.That(source, Does.Contain("var unload = Resources.UnloadUnusedAssets();"), "only one world in memory");
        Assert.That(source, Does.Contain("m_Door.SetPark("), "closed, the droplet parks beside the sensory bag");
        Assert.That(source, Does.Contain("ChooseDoorWorld, () => { if (m_Door != null) m_Door.RequestPark(); });"), "the cross closes the door");
        var manager = Flat(File.ReadAllText("Assets/Scripts/Moodium/CandyWorldManager.cs"));
        Assert.That(manager, Does.Contain("UpdateDoorRoomRules();"));
        Assert.That(manager, Does.Contain("var block = m_BubbleWorld.DoorBlocksWalking;"), "walking waits while the door is open");
        Assert.That(manager, Does.Contain("SetWalkSuspended(m_DoorBlocksWalk);"), "walking back out of the world keeps the walk waiting while the door is open");
        Assert.That(manager, Does.Contain("m_DoorSide.Apply(m_DoorSideRoots.Targets, door.Room, door.DoorZ, hideAll: true);"), "room content beyond the door is hidden");
        Assert.That(manager, Does.Contain("RegisterRoomContent(m_RoomVisibility);"), "and everything inside the world, as before");
        var input = Flat(File.ReadAllText("Assets/Scripts/Moodium/MoodiumSpatialInteractionController.cs"));
        Assert.That(input, Does.Contain("doorHandle.PointerStarted(id, pointer.interactionPosition);"), "pinch-drag on the ring");
        Assert.That(input, Does.Contain("m_MouseDragPlane = new Plane(Vector3.up, hit.point);"), "editor mouse drags on the floor plane");
        var probe = File.ReadAllText("Assets/Scripts/Moodium/BubbleWorld/BubblePortalProbe.cs");
        Assert.That(probe, Does.Contain("!world.DoorOpen"), "the probe leaves door worlds alone");
    }
}
