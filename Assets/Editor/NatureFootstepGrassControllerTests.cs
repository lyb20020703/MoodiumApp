using System.Collections.Generic;
using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;

public sealed class NatureFootstepGrassControllerTests
{
    [Test]
    public void EnablingWalkingAgainDoesNotResetTrackedStepDistance()
    {
        var root = new GameObject("Walk gate regression");
        var controller = root.AddComponent<NatureFootstepGrassController>();
        controller.SetWalkingEnabled(true);
        var field = typeof(NatureFootstepGrassController).GetField("m_HasLastGrassPosition",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        field.SetValue(controller, true);
        controller.SetWalkingEnabled(true);
        Assert.That(field.GetValue(controller), Is.True);
        Object.DestroyImmediate(root);
    }
    [Test]
    public void FootstepGrassOnlyActivatesForACompletedNatureWorld()
    {
        Assert.That(NatureFootstepGrassController.ShouldBeActive(true, 1f), Is.True);
        Assert.That(NatureFootstepGrassController.ShouldBeActive(true, .99f), Is.False);
        Assert.That(NatureFootstepGrassController.ShouldBeActive(false, 1f), Is.False);
    }

    [Test]
    public void FootstepBeansOnlyActivateForACompletedCandyWorld()
    {
        Assert.That(NatureFootstepGrassController.ShouldBeActiveForCandy(true, 1f), Is.True);
        Assert.That(NatureFootstepGrassController.ShouldBeActiveForCandy(true, .99f), Is.False);
        Assert.That(NatureFootstepGrassController.ShouldBeActiveForCandy(false, 1f), Is.False);
    }

    [Test]
    public void FootstepGrassRejectsNearbyExistingPlacements()
    {
        var existing = new List<Vector3> { Vector3.zero };

        Assert.That(NatureFootstepGrassController.HasMinimumHorizontalSeparation(
            new Vector3(.24f, 5f, 0f), existing, .25f), Is.False);
        Assert.That(NatureFootstepGrassController.HasMinimumHorizontalSeparation(
            new Vector3(.25f, -3f, 0f), existing, .25f), Is.True);
    }

    [Test]
    public void FootstepGrassRecyclesOnlyTheOldestInstanceWhenAtCapacity()
    {
        Assert.That(NatureFootstepGrassController.GetInstancesToRecycle(59, 60), Is.EqualTo(0));
        Assert.That(NatureFootstepGrassController.GetInstancesToRecycle(60, 60), Is.EqualTo(1));
        Assert.That(NatureFootstepGrassController.GetInstancesToRecycle(75, 60), Is.EqualTo(16));
    }

    [Test]
    public void FootstepGrassGroundQueriesIncludeTheFootCenterAndNearbyMeshSamples()
    {
        var offsets = NatureFootstepGrassController.GetGroundProbeOffsets();

        Assert.That(offsets, Does.Contain(Vector2.zero));
        Assert.That(offsets.Length, Is.GreaterThan(1));
    }

    [Test]
    public void FootstepGrassAcceptsColliderOwnedByGeneratedSpatialMesh()
    {
        var trackablesParent = new GameObject("Trackables Parent");
        var meshObject = new GameObject("AR Default Mesh");
        var unrelatedObject = new GameObject("Virtual Mesh");
        meshObject.transform.SetParent(trackablesParent.transform);
        var meshFilter = meshObject.AddComponent<MeshFilter>();
        var spatialCollider = meshObject.AddComponent<MeshCollider>();
        var unrelatedCollider = unrelatedObject.AddComponent<MeshCollider>();

        try
        {
            var generatedMeshes = new List<MeshFilter> { meshFilter };

            Assert.That(NatureFootstepGrassController.IsGeneratedSpatialMeshCollider(
                spatialCollider, generatedMeshes), Is.True);
            Assert.That(NatureFootstepGrassController.IsGeneratedSpatialMeshCollider(
                unrelatedCollider, generatedMeshes), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(trackablesParent);
            Object.DestroyImmediate(unrelatedObject);
        }
    }

    [Test]
    public void FootstepPrefabsAlternateBetweenGrassAndWildflowers()
    {
        var grass = new GameObject("grass");
        var flowers = new GameObject("Wildflowers");
        try
        {
            Assert.That(NatureFootstepGrassController.SelectFootstepPrefab(grass, flowers, 0), Is.SameAs(grass));
            Assert.That(NatureFootstepGrassController.SelectFootstepPrefab(grass, flowers, 1), Is.SameAs(flowers));
            Assert.That(NatureFootstepGrassController.SelectFootstepPrefab(grass, flowers, 2), Is.SameAs(grass));
        }
        finally
        {
            Object.DestroyImmediate(grass);
            Object.DestroyImmediate(flowers);
        }
    }
}
