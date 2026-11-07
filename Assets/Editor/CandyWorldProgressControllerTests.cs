using System.Reflection;
using System.IO;
using Moodium.CandyWorld;
using Moodium.Reality;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

public sealed class CandyWorldProgressControllerTests
{
    const string CandyWorldAssetPath = "Assets/Data/Moodium/Worlds/CandyWorld.asset";
    const string HoneyJarGuid = "56c4aa9d9aece463e9149c33795332a5";
    GameObject m_Root;

    [SetUp]
    public void SetUp() => m_Root = new GameObject("Candy Progress Test Root");

    [TearDown]
    public void TearDown()
    {
        if (m_Root != null)
            Object.DestroyImmediate(m_Root);
    }

    [Test]
    public void DisabledSpatialTransformationDoesNotStartCandyMeshOverlay()
    {
        var meshManager = m_Root.AddComponent<ARMeshManager>();
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        Assert.That(shader, Is.Not.Null);
        var material = new Material(shader);
        var transformation = m_Root.AddComponent<CandySpatialMeshTransformationController>();
        transformation.Configure(meshManager, material);

        var progress = m_Root.AddComponent<CandyWorldProgressController>();
        progress.Configure(null, transformation, null, null, null);
        progress.SetSpatialTransformationEnabled(false);
        progress.StartProgress();

        var isRunning = (bool)typeof(CandySpatialMeshTransformationController)
            .GetField("m_Running", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(transformation);
        Assert.That(isRunning, Is.False);

        Object.DestroyImmediate(material);
    }

    [Test]
    public void HoneyJarIsScheduledForTheThirdAmbientCandyNode()
    {
        Assert.That(CandyWorldProgressController.IsHoneyJarStage(.7f), Is.False);
        Assert.That(CandyWorldProgressController.IsHoneyJarStage(.75f), Is.True);
        Assert.That(CandyWorldProgressController.IsHoneyJarStage(.86f), Is.True);
        Assert.That(CandyWorldProgressController.IsHoneyJarStage(.9f), Is.False);
    }

    [Test]
    public void HoneyJarNameWithSpaceIsRecognizedByProgressPool()
    {
        Assert.That(CandyWorldProgressController.NormalizePrefabName("Honey Jar"),
            Is.EqualTo("HoneyJar"));
    }

    [Test]
    public void CandyWorldPrefabPoolIncludesHoneyJar()
    {
        var asset = File.ReadAllText(CandyWorldAssetPath);

        Assert.That(asset, Does.Contain(HoneyJarGuid));
    }

}
