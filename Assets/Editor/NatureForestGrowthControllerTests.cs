using System.IO;
using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;

public sealed class NatureForestGrowthControllerTests
{
    const string MeshingScenePath = "Assets/Samples/PolySpatial/Scenes/Meshing 1.unity";

    GameObject m_Root;
    GameObject m_TreePrefab;

    [SetUp]
    public void SetUp()
    {
        m_Root = new GameObject("Nature Forest Test Root");
        m_TreePrefab = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        m_TreePrefab.name = "LOD Tree Test Prefab";
        m_TreePrefab.SetActive(false);
    }

    [TearDown]
    public void TearDown()
    {
        if (m_Root != null)
            Object.DestroyImmediate(m_Root);
        if (m_TreePrefab != null)
            Object.DestroyImmediate(m_TreePrefab);
    }

    [Test]
    public void ProgressThresholdsGrowOneForestTreeAtATime()
    {
        var controller = m_Root.AddComponent<NatureForestGrowthController>();
        var viewer = new GameObject("Nature Viewer").transform;
        viewer.SetParent(m_Root.transform);
        viewer.position = Vector3.zero;
        viewer.forward = Vector3.forward;
        controller.Configure(viewer, new[] { m_TreePrefab });
        controller.StartGrowth();

        controller.SetProgress(0.34f);
        Assert.That(controller.SpawnedTreeCount, Is.EqualTo(0));

        controller.SetProgress(0.35f);
        Assert.That(controller.SpawnedTreeCount, Is.EqualTo(1));

        controller.SetProgress(0.55f);
        Assert.That(controller.SpawnedTreeCount, Is.EqualTo(2));
    }

    [Test]
    public void StopAndClearRemovesSpawnedForestTrees()
    {
        var controller = m_Root.AddComponent<NatureForestGrowthController>();
        var viewer = new GameObject("Nature Viewer").transform;
        viewer.SetParent(m_Root.transform);
        controller.Configure(viewer, new[] { m_TreePrefab });
        controller.StartGrowth();
        controller.SetProgress(1f);

        controller.StopAndClear();

        Assert.That(controller.SpawnedTreeCount, Is.EqualTo(0));
    }

    [Test]
    public void SpawnedForestTreeUsesOneSixthOfItsPrefabScale()
    {
        m_TreePrefab.transform.localScale = new Vector3(2f, 4f, 6f);
        var controller = m_Root.AddComponent<NatureForestGrowthController>();
        var viewer = new GameObject("Nature Viewer").transform;
        viewer.SetParent(m_Root.transform);
        viewer.position = Vector3.zero;
        viewer.forward = Vector3.forward;
        controller.Configure(viewer, new[] { m_TreePrefab });
        controller.StartGrowth();

        controller.SetProgress(0.35f);

        var forestRoot = controller.ForestRoot;
        Assert.That(forestRoot, Is.Not.Null);
        Assert.That(forestRoot.childCount, Is.EqualTo(1));
        Assert.That(forestRoot.GetChild(0).localScale, Is.EqualTo(new Vector3(1f / 3f, 2f / 3f, 1f)));
    }

    [Test]
    public void FullGrowthDistributesMoreTreesBehindThanInFront()
    {
        var controller = m_Root.AddComponent<NatureForestGrowthController>();
        var viewer = new GameObject("Nature Viewer").transform;
        viewer.SetParent(m_Root.transform);
        viewer.position = Vector3.zero;
        viewer.forward = Vector3.forward;
        controller.Configure(viewer, new[] { m_TreePrefab });
        controller.StartGrowth();

        controller.SetProgress(1f);

        var forestRoot = controller.ForestRoot;
        Assert.That(forestRoot.childCount, Is.EqualTo(5));

        var frontCount = 0;
        var backCount = 0;
        for (var i = 0; i < forestRoot.childCount; i++)
        {
            if (forestRoot.GetChild(i).position.z > 0f) frontCount++;
            if (forestRoot.GetChild(i).position.z < 0f) backCount++;
        }
        Assert.That(frontCount, Is.EqualTo(5));
        Assert.That(backCount, Is.EqualTo(0));
    }

    [Test]
    public void FirstForestTreeIsPlacedInTheViewersForwardLeftArc()
    {
        Assert.That(NatureForestGrowthController.GetViewerRelativeSpawnOffset(0),
            Is.EqualTo(new Vector2(-2.4f, 2.8f)));
    }

    [Test]
    public void NatureForest_UsesAnimatedBirchAndOakPrefabs()
    {
        var scene = File.ReadAllText(MeshingScenePath);

        Assert.That(scene, Does.Contain("guid: 42639e27cb175d8488974c697fdf13d4"));
        Assert.That(scene, Does.Contain("guid: 867151643308bbc458d63f0412bd3a54"));
    }

    [Test]
    public void MissingSpatialMeshFallsBackToFloorLevelInsteadOfViewerHeight()
    {
        var controller = m_Root.AddComponent<NatureForestGrowthController>();
        var viewer = new GameObject("Nature Viewer").transform;
        viewer.SetParent(m_Root.transform);
        viewer.position = new Vector3(0f, 1.65f, 0f);
        viewer.forward = Vector3.forward;
        controller.Configure(viewer, new[] { m_TreePrefab });
        controller.StartGrowth();

        controller.SetProgress(0.35f);

        var tree = controller.ForestRoot.GetChild(0);
        Assert.That(tree.position.y, Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void SpawnedTreesRemainWorldUpright_WhenControllerParentIsRotated()
    {
        m_Root.transform.rotation = Quaternion.Euler(35f, 20f, 15f);
        var controller = m_Root.AddComponent<NatureForestGrowthController>();
        var viewer = new GameObject("Nature Viewer").transform;
        viewer.position = Vector3.zero;
        viewer.forward = Vector3.forward;
        controller.Configure(viewer, new[] { m_TreePrefab });
        controller.StartGrowth();

        controller.SetProgress(0.35f);

        Assert.That(Vector3.Dot(controller.ForestRoot.GetChild(0).up, Vector3.up), Is.GreaterThan(.999f));
    }

    [Test]
    public void MinimumHorizontalSeparationRejectsOverlappingTreePositions()
    {
        Assert.That(NatureForestGrowthController.HasMinimumHorizontalSeparation(
            new Vector3(0f, 0f, 0f), new Vector3(1.9f, 10f, 0f), 2f), Is.False);
        Assert.That(NatureForestGrowthController.HasMinimumHorizontalSeparation(
            new Vector3(0f, 0f, 0f), new Vector3(2f, 10f, 0f), 2f), Is.True);
    }
}
