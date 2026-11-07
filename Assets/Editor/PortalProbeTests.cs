using System.IO;
using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;

/// <summary>Portal probe (device test, 2026-11-02): an occluder room with a door around the viewer inside a bubble world.</summary>
public sealed class PortalProbeTests
{
    static readonly Vector3 Head = new(0f, 1.6f, 0f);
    static Vector3 RoomCentre => new(0f, PortalProbeLayout.Height * .5f, (PortalProbeLayout.DoorDistance - PortalProbeLayout.BackDepth) * .5f);

    [Test]
    public void TheRoomFitsAroundAStandingViewerWithTheDoorAhead()
    {
        Assert.That(PortalProbeLayout.Contains(Head, -PortalProbeLayout.Margin), Is.True, "the viewer starts well inside");
        Assert.That(PortalProbeLayout.DoorWidth * .5f, Is.LessThan(PortalProbeLayout.HalfWidth));
        Assert.That(PortalProbeLayout.DoorHeight, Is.GreaterThan(1.9f).And.LessThan(PortalProbeLayout.Height));
        Assert.That(PortalProbeLayout.DoorDistance, Is.InRange(.6f, 1.5f), "a step or two to the door");
    }

    [Test]
    public void EveryWallFacesIntoTheRoom()
    {
        var mesh = PortalProbeLayout.Walls();
        try
        {
            var v = mesh.vertices; var t = mesh.triangles;
            Assert.That(t.Length, Is.EqualTo(8 * 6));
            for (var i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                var normal = Vector3.Cross(b - a, c - a);
                Assert.That(Vector3.Dot(normal, RoomCentre - (a + b + c) / 3f), Is.GreaterThan(0f), $"triangle {i / 3} faces out");
            }
        }
        finally { Object.DestroyImmediate(mesh); }
    }

    [Test]
    public void TheBackDoorFacesTheWorldAndTheFrameFacesTheRoom()
    {
        var door = PortalProbeLayout.BackDoor();
        var frame = PortalProbeLayout.Frame();
        try
        {
            AssertFacing(door, Vector3.forward);
            AssertFacing(frame, Vector3.back);
            foreach (var p in door.vertices) Assert.That(p.z, Is.EqualTo(PortalProbeLayout.DoorDistance).Within(1e-5f));
            foreach (var p in frame.vertices) Assert.That(p.z, Is.LessThan(PortalProbeLayout.DoorDistance), "frame sits on the room side of the wall");
        }
        finally { Object.DestroyImmediate(door); Object.DestroyImmediate(frame); }
    }

    static void AssertFacing(Mesh mesh, Vector3 facing)
    {
        var v = mesh.vertices; var t = mesh.triangles;
        for (var i = 0; i < t.Length; i += 3)
            Assert.That(Vector3.Dot(Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]), facing), Is.GreaterThan(0f), mesh.name);
    }

    [Test]
    public void FromInsideTheRoomTheWorldIsOnlyVisibleThroughTheDoor()
    {
        var mesh = PortalProbeLayout.Walls();
        try
        {
            var v = mesh.vertices; var t = mesh.triangles;
            int escaped = 0;
            const int rays = 2000;
            for (var k = 0; k < rays; k++)
            {
                // Fibonacci sphere: even directions all around the head.
                var y = 1f - 2f * (k + .5f) / rays;
                var r = Mathf.Sqrt(1f - y * y);
                var phi = k * Mathf.PI * (3f - Mathf.Sqrt(5f));
                var dir = new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r);
                if (Hits(Head, dir, v, t)) continue;
                escaped++;
                Assert.That(dir.z, Is.GreaterThan(0f), $"ray {dir} escapes, not toward the door");
                var p = Head + dir * ((PortalProbeLayout.DoorDistance - Head.z) / dir.z);
                Assert.That(PortalProbeLayout.InDoor(p.x, p.y), Is.True, $"ray {dir} escapes at {p}, outside the door");
            }
            Assert.That(escaped, Is.GreaterThan(rays / 50), "the world shows through the door");
            Assert.That(Hits(Head, Vector3.forward, v, t), Is.False, "straight ahead is the door");
            Assert.That(Hits(Head, Vector3.back, v, t), Is.True);
        }
        finally { Object.DestroyImmediate(mesh); }
    }

    /// <summary>Two-sided ray / triangle test (Möller–Trumbore), edges inclusive so seams between quads do not leak.</summary>
    static bool Hits(Vector3 o, Vector3 d, Vector3[] v, int[] t)
    {
        for (var i = 0; i < t.Length; i += 3)
        {
            Vector3 a = v[t[i]], e1 = v[t[i + 1]] - a, e2 = v[t[i + 2]] - a;
            var p = Vector3.Cross(d, e2);
            var det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-9f) continue;
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
    public void LeavingAndReturningUsesAMargin()
    {
        var d = PortalProbeLayout.DoorDistance;
        Assert.That(PortalProbeLayout.NextInRoom(true, new Vector3(0f, 1.6f, d + .02f)), Is.True, "a lean at the door does not flip it");
        Assert.That(PortalProbeLayout.NextInRoom(true, new Vector3(0f, 1.6f, d + .1f)), Is.False);
        Assert.That(PortalProbeLayout.NextInRoom(false, new Vector3(0f, 1.6f, d - .02f)), Is.False);
        Assert.That(PortalProbeLayout.NextInRoom(false, new Vector3(0f, 1.6f, d - .1f)), Is.True);
        Assert.That(PortalProbeLayout.NextInRoom(true, new Vector3(PortalProbeLayout.HalfWidth + .2f, 1.6f, 0f)), Is.False, "out through a side wall counts too");
    }

    [Test]
    public void ThePlacedRoomSwapsWallsForTheBackDoorWhenTheViewerStepsOut()
    {
        var go = new GameObject("probe");
        try
        {
            var probe = go.AddComponent<BubblePortalProbe>();
            probe.Place(new Vector3(2f, 1.7f, 3f), new Vector3(1f, -.3f, 0f), .1f);
            var room = probe.Room;
            Assert.That(Vector3.Distance(room.position, new Vector3(2f, .1f, 3f)), Is.LessThan(1e-5f));
            Assert.That(Vector3.Angle(room.forward, Vector3.right), Is.LessThan(.01f), "door ahead of where the viewer looked, level");
            var walls = room.Find("Walls (occluder)").gameObject;
            var back = room.Find("Back Door (occluder)").gameObject;
            var frame = room.Find("Door Frame").gameObject;
            Assert.That(probe.InRoom && walls.activeSelf && !back.activeSelf && frame.activeSelf, Is.True);
            Assert.That(walls.GetComponent<MeshRenderer>().sharedMaterial, Is.Not.Null);

            Vector3 At(float z) => room.TransformPoint(new Vector3(0f, 1.6f, z));
            var d = PortalProbeLayout.DoorDistance;
            probe.Step(At(d + .02f));
            Assert.That(probe.InRoom, Is.True);
            probe.Step(At(d + .3f));
            Assert.That(probe.InRoom, Is.False);
            Assert.That(!walls.activeSelf && back.activeSelf && frame.activeSelf, Is.True, "in the world: walls off, the door shows the room");
            probe.Step(At(d - .3f));
            Assert.That(probe.InRoom && walls.activeSelf && !back.activeSelf, Is.True, "back in the room");

            probe.Clear();
            Assert.That(room.gameObject.activeSelf, Is.False);
            probe.Place(new Vector3(0f, 1.6f, 0f), Vector3.forward, 0f);
            Assert.That(room.gameObject.activeSelf && probe.InRoom, Is.True, "placed again for the next world");
        }
        finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void TheOccluderAlwaysComesBackAsAMaterial()
    {
        var material = BubblePortalProbe.CreateOccluder(out var found);
        try
        {
            Assert.That(material, Is.Not.Null);
            if (found) Assert.That(BubblePortalProbe.OccluderShaders, Does.Contain(material.shader.name));
            else Debug.LogWarning("AR occlusion shader not found in this project: run Moodium > Portal Probe > Create Occluder Material and check the log");
        }
        finally { Object.DestroyImmediate(material); }
    }

    [Test]
    public void WalkingOutThroughTheDoorDoesNotFadeTheWorld()
    {
        var controller = File.ReadAllText("Assets/Scripts/Moodium/BubbleWorld/BubbleWorldController.cs");
        Assert.That(controller, Does.Contain("var away = !IgnoreWander && m_Door == null && WanderState(m_Wandering, offset.magnitude);"));
        var probe = File.ReadAllText("Assets/Scripts/Moodium/BubbleWorld/BubblePortalProbe.cs");
        Assert.That(probe, Does.Contain("inside.IgnoreWander = true;"));
        Assert.That(probe, Does.Contain("m_Target.IgnoreWander = false;"), "handed back when the room is removed");
        var go = new GameObject("world");
        try
        {
            var world = go.AddComponent<BubbleWorldController>();
            Assert.That(world.IgnoreWander, Is.False, "off by default: normal play is unchanged");
        }
        finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void TheProbeIsOffUnlessTheDefineIsSet()
    {
        var source = File.ReadAllText("Assets/Scripts/Moodium/BubbleWorld/BubblePortalProbe.cs");
        var gate = source.IndexOf("#if MOODIUM_PORTAL_PROBE", System.StringComparison.Ordinal);
        var boot = source.IndexOf("[RuntimeInitializeOnLoadMethod", System.StringComparison.Ordinal);
        var end = source.IndexOf("#endif", System.StringComparison.Ordinal);
        Assert.That(gate, Is.GreaterThanOrEqualTo(0));
        Assert.That(gate < boot && boot < end, Is.True, "the bootstrap is the only way in, and it is behind the define");
        Assert.That(source.Split("FollowBubbleWorlds = true").Length - 1, Is.EqualTo(1));
    }
}
