using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Artngame.TreeGEN.ProceduralIvy;
using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Look + pinch on the room (2026-11-03): visionOS only sends a gaze-pinch for colliders PolySpatial syncs, which are the
/// Default layer only; the scanned room mesh is on layer 4. While vines can be planted, the room gets Default-layer twins.
/// Also: the vine mesh drops Ivy Studio's unused vertices and is never index-less while it grows.
/// </summary>
public sealed class NatureVineGazeTargetTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    GameObject m_Root, m_GeneratorObject, m_Room;
    Mesh m_RoomMesh;

    [SetUp]
    public void SetUp()
    {
        m_Root = new GameObject("Nature Vine Gaze Test Root");
        m_GeneratorObject = new GameObject("Ivy Generator");
        m_GeneratorObject.transform.SetParent(m_Root.transform);
        m_GeneratorObject.AddComponent<MeshFilter>();
        m_GeneratorObject.AddComponent<MeshRenderer>();
        var generator = m_GeneratorObject.AddComponent<IvyGeneratorTREANT>();
        generator.addLeaves = false;
        generator.addColliderPerIvy = false;
        generator.growIvy = false;
        generator.singleMeshBranch = false;
        generator.use3dbranch = true;
        generator.branch3dDivisions = NatureIvySpatialGrowthController.VineDivisions;
        generator.branch3dSize = NatureIvySpatialGrowthController.VineRadius;
        generator.narrowFactor = NatureIvySpatialGrowthController.VineTipNarrow;
        generator.branchPartLength = NatureIvySpatialGrowthController.VineStep;
        generator.branchCount = 1;
        generator.maxBranchPositions = 12;
        generator.zigZagPower = 0f;
        generator.directionality = new Vector4(1f, 0f, 0f, 0f);

        // A scanned-room chunk: a MeshCollider on layer 4, like AR Default Mesh.prefab. Its top is at y = 0.
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        m_RoomMesh = Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh);
        Object.DestroyImmediate(cube);
        m_Room = new GameObject("AR Default Mesh (room chunk)") { layer = 4 };
        m_Room.transform.SetParent(m_Root.transform);
        m_Room.transform.position = new Vector3(0f, -.15f, 0f);
        m_Room.transform.localScale = new Vector3(1f, .3f, 1f);
        m_Room.AddComponent<MeshCollider>().sharedMesh = m_RoomMesh;
        Physics.SyncTransforms();
    }

    [TearDown]
    public void TearDown()
    {
        if (m_Root != null) Object.DestroyImmediate(m_Root);
        if (m_RoomMesh != null) Object.DestroyImmediate(m_RoomMesh);
    }

    NatureIvySpatialGrowthController Planter(out HashSet<Collider> roomColliders)
    {
        var controller = m_Root.AddComponent<NatureIvySpatialGrowthController>();
        controller.Configure(m_GeneratorObject.GetComponent<IvyGeneratorTREANT>(), 10);
        controller.StartGrowth();
        m_Root.AddComponent<Moodium.Flow.WorldJourneyDirector>().Rescued();
        controller.SetProgress(1f);
        roomColliders = (HashSet<Collider>)typeof(NatureIvySpatialGrowthController).GetField("m_SpatialColliders", Flags).GetValue(controller);
        roomColliders.Add(m_Room.GetComponent<Collider>());
        return controller;
    }

    MeshCollider Twin() => m_Room.GetComponentsInChildren<MeshCollider>(true)
        .SingleOrDefault(c => c.name == NatureIvySpatialGrowthController.GazeTargetName);

    [Test]
    public void TheRoomLayerIsNotSyncedToVisionOSButTheTwinLayerIs()
    {
        var settings = Resources.Load<ScriptableObject>("PolySpatialSettings");
        Assert.That(settings, Is.Not.Null);
        var property = new UnityEditor.SerializedObject(settings).FindProperty("m_ColliderSyncLayerMask");
        Assert.That(property, Is.Not.Null);
        var bits = property.FindPropertyRelative("m_Bits");
        var mask = bits != null ? bits.intValue : property.intValue;
        var roomPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Samples/PolySpatial/MixedReality/Prefabs/AR Default Mesh.prefab");
        Assert.That(roomPrefab, Is.Not.Null);
        Assert.That(roomPrefab.GetComponent<MeshCollider>(), Is.Not.Null);
        Assert.That(mask & (1 << NatureIvySpatialGrowthController.GazeTargetLayer), Is.Not.Zero, "the twins' layer must be synced");
        Assert.That(mask & (1 << roomPrefab.layer), Is.Zero,
            "the room mesh's own layer is not synced (if it ever is, the twins are no longer needed)");
    }

    [Test]
    public void TwinsMirrorTheRoomMeshOnTheDefaultLayerAndStayOutOfTheProbeList()
    {
        var controller = Planter(out var room);
        Assert.That(controller.SyncGazeTargets(), Is.EqualTo(1));
        var twin = Twin();
        Assert.That(twin, Is.Not.Null);
        Assert.That(twin.gameObject.layer, Is.EqualTo(NatureIvySpatialGrowthController.GazeTargetLayer));
        Assert.That(twin.sharedMesh, Is.SameAs(m_RoomMesh));
        Assert.That(twin.convex, Is.False);
        Assert.That(twin.transform.parent, Is.SameAs(m_Room.transform));
        Assert.That(twin.transform.localPosition, Is.EqualTo(Vector3.zero));
        Assert.That(controller.GazeTargetCount, Is.EqualTo(1));
        Assert.That(room.Count, Is.EqualTo(1), "vines probe the room mesh only, not its twin as well");
        Assert.That(room.Contains(twin), Is.False);

        Assert.That(controller.SyncGazeTargets(), Is.Zero, "one twin per room mesh");
        Assert.That(m_Room.GetComponentsInChildren<MeshCollider>(true).Count(c => c.name == NatureIvySpatialGrowthController.GazeTargetName), Is.EqualTo(1));

        Assert.That(controller.IsSpatialMeshCollider(twin), Is.True, "a gaze-pinch on the twin counts as the room");
    }

    [Test]
    public void TwinsGoAwayWithTheirRoomMeshAndWhenPlantingStops()
    {
        var controller = Planter(out var room);
        controller.SyncGazeTargets();
        room.Clear();
        controller.SyncGazeTargets();
        Assert.That(controller.GazeTargetCount, Is.Zero, "the room mesh is gone");
        Assert.That(Twin(), Is.Null);

        room.Add(m_Room.GetComponent<Collider>());
        controller.SyncGazeTargets();
        Assert.That(Twin(), Is.Not.Null);
        controller.SetSuspended(true);
        Assert.That(controller.GazeTargetCount, Is.Zero, "inside a world or paused: the room takes no gaze");
        Assert.That(Twin(), Is.Null);

        controller.SetSuspended(false);
        controller.SyncGazeTargets();
        Assert.That(Twin(), Is.Not.Null);
        controller.StopAndClear();
        Assert.That(Twin(), Is.Null, "leaving the world");
    }

    [Test]
    public void ARoomMeshAlreadyOnTheDefaultLayerNeedsNoTwin()
    {
        m_Room.layer = 0;
        var controller = Planter(out _);
        Assert.That(controller.SyncGazeTargets(), Is.Zero);
        Assert.That(Twin(), Is.Null);
    }

    [Test]
    public void TheVineMeshKeepsOnlyTheVerticesItUses()
    {
        var controller = Planter(out _);
        Assert.That(m_Room.GetComponent<Collider>().Raycast(new Ray(new Vector3(-.3f, .3f, 0f), Vector3.down), out var hit, 1f), Is.True);
        Assert.That(controller.TryGrowAtSpatialHit(hit), Is.True);
        var branches = m_GeneratorObject.GetComponentsInChildren<MeshFilter>(true)
            .Where(f => f.gameObject != m_GeneratorObject && f.sharedMesh != null && f.sharedMesh.triangles.Length > 0).ToList();
        Assert.That(branches, Is.Not.Empty);
        foreach (var branch in branches)
        {
            var mesh = branch.sharedMesh;
            Assert.That(mesh.triangles.Max() + 1, Is.EqualTo(mesh.vertexCount), "no trailing unused vertices");
            Assert.That(mesh.vertexCount % NatureIvySpatialGrowthController.VineDivisions, Is.Zero, "whole rings, so the leaf code can read them");
            Assert.That(mesh.bounds.min.y, Is.GreaterThan(-.02f), "bounds no longer reach down to the world origin's zeros");
            Assert.That(mesh.normals.All(n => n.sqrMagnitude > .5f), Is.True, "no zero normals left");
        }
    }

    [Test]
    public void TrimmingLeavesAMeshWithoutUnusedVerticesUnchangedOtherwise()
    {
        var mesh = new Mesh();
        try
        {
            var vertices = new Vector3[12];
            for (var i = 0; i < 4; i++) vertices[i] = new Vector3(1f + i % 2, 1f + i / 2, 1f);
            mesh.vertices = vertices;
            mesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
            mesh.RecalculateNormals();
            Assert.That(NatureIvySpatialGrowthController.TrimUnusedVertices(mesh), Is.EqualTo(8));
            Assert.That(mesh.vertexCount, Is.EqualTo(4));
            Assert.That(mesh.triangles, Is.EqualTo(new[] { 0, 1, 2, 2, 1, 3 }));
            Assert.That(mesh.bounds.min.x, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(NatureIvySpatialGrowthController.TrimUnusedVertices(mesh), Is.Zero, "nothing left to trim");
        }
        finally { Object.DestroyImmediate(mesh); }
    }

    [Test]
    public void TheGrowingVineAlwaysShowsAtLeastItsFirstSegment()
    {
        const int divisions = 8, segment = 6 * divisions;
        Assert.That(NatureIvySpatialGrowthController.RevealIndexCount(10 * segment, 0f, divisions), Is.EqualTo(segment));
        Assert.That(NatureIvySpatialGrowthController.RevealIndexCount(10 * segment, .5f, divisions), Is.EqualTo(5 * segment));
        Assert.That(NatureIvySpatialGrowthController.RevealIndexCount(10 * segment, 1f, divisions), Is.EqualTo(10 * segment));
        Assert.That(NatureIvySpatialGrowthController.RevealIndexCount(30, 0f, divisions), Is.EqualTo(30), "a branch shorter than a segment shows whole");
        Assert.That(NatureIvySpatialGrowthController.RevealIndexCount(0, .5f, divisions), Is.Zero);
        Assert.That(NatureIvySpatialGrowthController.RevealIndexCount(10 * segment, .37f, divisions) % 3, Is.Zero, "whole triangles");
    }

    [Test]
    public void EveryRoomPinchIsLoggedAndOnlyPlantableOnesAreConsumed()
    {
        var input = File.ReadAllText("Assets/Scripts/Moodium/MoodiumSpatialInteractionController.cs");
        Assert.That(input, Does.Contain("ivy.NotePinch(target, hasRay, plantable, grown);"));
        Assert.That(input, Does.Contain("if (plantable) continue;"));
        var controller = File.ReadAllText("Assets/Scripts/Moodium/NatureIvySpatialGrowthController.cs");
        Assert.That(controller, Does.Contain("UpdateGazeTargets(plantable && m_GazePlanting"));
        Assert.That(controller, Does.Contain("m_MeshManager.meshesChanged += OnRoomMeshesChanged"));
        Assert.That(controller, Does.Contain("m_MeshManager.meshesChanged -= OnRoomMeshesChanged"));
        var settings = File.ReadAllText("Assets/Resources/PolySpatialSettings.asset");
        Assert.That(settings, Does.Contain("m_ColliderSyncLayerMask:\n    serializedVersion: 2\n    m_Bits: 1"),
            "the global sync mask is unchanged: only the twins reach visionOS, and only while vines can be planted");
    }
}
