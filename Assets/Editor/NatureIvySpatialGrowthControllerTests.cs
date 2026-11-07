#if MOODIUM_IVY_STUDIO
using Artngame.TreeGEN.ProceduralIvy;
using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;

public sealed class NatureIvySpatialGrowthControllerTests
{
    GameObject m_Root;
    GameObject m_GeneratorObject;
    GameObject m_Surface;

    [SetUp]
    public void SetUp()
    {
        m_Root = new GameObject("Nature Ivy Test Root");
        m_GeneratorObject = new GameObject("Ivy Generator");
        m_GeneratorObject.transform.SetParent(m_Root.transform);
        m_GeneratorObject.AddComponent<MeshFilter>();
        m_GeneratorObject.AddComponent<MeshRenderer>();
        var generator = m_GeneratorObject.AddComponent<IvyGeneratorTREANT>();
        generator.branchCount = 1;
        generator.maxBranchPositions = 3;
        generator.branchPartLength = 0.08f;
        generator.addLeaves = false;
        generator.addColliderPerIvy = false;
        generator.growIvy = false;

        m_Surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
        m_Surface.name = "Spatial Mesh Surface";
        m_Surface.transform.SetParent(m_Root.transform);
        m_Surface.transform.position = new Vector3(0f, 0f, 1f);
        m_Surface.transform.localScale = new Vector3(2f, 2f, 0.1f);
    }

    [TearDown]
    public void TearDown()
    {
        if (m_Root != null)
            Object.DestroyImmediate(m_Root);
    }

    [TestCase(1)]
    [TestCase(5)]
    public void SpatialHitKeepsPlantingBeyondTheOldLifetimeBudget(int budget)
    {
        var controller = m_Root.AddComponent<NatureIvySpatialGrowthController>();
        controller.Configure(m_GeneratorObject.GetComponent<IvyGeneratorTREANT>(), budget);
        controller.StartGrowth();
        m_Root.AddComponent<Moodium.Flow.WorldJourneyDirector>().Rescued();
        var colliders = (System.Collections.Generic.HashSet<Collider>)typeof(NatureIvySpatialGrowthController)
            .GetField("m_SpatialColliders", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .GetValue(controller);
        colliders.Add(m_Surface.GetComponent<Collider>());
        controller.SetProgress(1f);
        Physics.SyncTransforms();
        Assert.That(Physics.Raycast(new Ray(Vector3.zero, Vector3.forward), out var hit, 3f), Is.True);

        for (var i = 0; i < budget; i++)
            Assert.That(controller.TryGrowAtSpatialHit(hit), Is.True);
        Assert.That(controller.GrownStrokeCount, Is.EqualTo(budget));
        Assert.That(controller.TryGrowAtSpatialHit(hit), Is.True);
    }

    [Test]
    public void EnvironmentRequiresFullEnergyExploreAndRegisteredSurface()
    {
        var controller = m_Root.AddComponent<NatureIvySpatialGrowthController>();
        controller.Configure(m_GeneratorObject.GetComponent<IvyGeneratorTREANT>(), 48);
        controller.StartGrowth();
        var journey = m_Root.AddComponent<Moodium.Flow.WorldJourneyDirector>();
        journey.Rescued();
        controller.SetProgress(.999f);
        Assert.That(controller.CanPlantEnvironment, Is.False);
        controller.SetProgress(1f);
        Assert.That(controller.CanPlantEnvironment, Is.True);
        Physics.SyncTransforms();
        Assert.That(Physics.Raycast(Vector3.zero, Vector3.forward, out var hit, 3f), Is.True);
        Assert.That(controller.TryGrowAtSpatialHit(hit), Is.False, "Unregistered virtual surface");
        Assert.That(controller.GrownStrokeCount, Is.Zero);
        controller.SetSuspended(true);
        Assert.That(controller.CanPlantEnvironment, Is.False);
        controller.SetSuspended(false);
        journey.EnterBubble();
        Assert.That(controller.CanPlantEnvironment, Is.False);
        journey.Returned();
        Assert.That(controller.CanPlantEnvironment, Is.True);
        controller.StopAndClear();
        Assert.That(controller.EnvironmentUnlocked, Is.False);
        Assert.That(controller.CanPlantEnvironment, Is.False);
    }

    [Test]
    public void VisionOSIvyPreservesThePrefab3DGeometryAndDoesNotInjectTheGrowShader()
    {
        Assert.That(NatureIvySpatialGrowthController.ShouldForceFlatIvyGeometry(), Is.False);
        Assert.That(NatureIvySpatialGrowthController.ShouldUseRuntimeGrowShader(), Is.False);
    }

    [Test]
    public void VisionOSIvyUsesTheDedicatedLeafVisualLayerInsteadOfIvyStudiosLeafPrefabs()
    {
        Assert.That(NatureIvySpatialGrowthController.ShouldUseIvyStudioAutoLeaves(), Is.True);
    }
}

#endif
