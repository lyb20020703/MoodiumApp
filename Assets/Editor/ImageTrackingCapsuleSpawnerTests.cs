using System;
using System.Reflection;
using Moodium.Interaction;
using Moodium.Reality;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public sealed class ImageTrackingCapsuleSpawnerTests
{
    GameObject m_Host;
    GameObject m_Prefab;
    GameObject m_Anchor;
    GameObject m_Instance;

    [TearDown]
    public void TearDown()
    {
        if (m_Instance != null) UnityEngine.Object.DestroyImmediate(m_Instance);
        if (m_Host != null) UnityEngine.Object.DestroyImmediate(m_Host);
        if (m_Prefab != null) UnityEngine.Object.DestroyImmediate(m_Prefab);
        if (m_Anchor != null) UnityEngine.Object.DestroyImmediate(m_Anchor);
    }

    [Test]
    public void TrackingOneImageTwiceCreatesOneCapsuleAndOneAvailabilityTransition()
    {
        var harness = CreateHarness();
        var availableCount = 0;
        AddEventHandler(harness.Spawner, "CapsuleAvailable",
            new Action<ChocolateCapsuleInteraction>(_ => availableCount++));

        m_Instance = harness.Report(new TrackableId(1, 2), TrackingState.Tracking);
        var second = harness.Report(new TrackableId(1, 2), TrackingState.Tracking);

        Assert.That(second, Is.SameAs(m_Instance));
        Assert.That(availableCount, Is.EqualTo(1));
    }

    [Test]
    public void LosingImageTrackingHidesCapsuleAndReportsUnavailableOnce()
    {
        var harness = CreateHarness();
        var unavailableCount = 0;
        AddEventHandler(harness.Spawner, "CapsuleUnavailable",
            new Action<ChocolateCapsuleInteraction>(_ => unavailableCount++));

        m_Instance = harness.Report(new TrackableId(3, 4), TrackingState.Tracking);
        harness.Report(new TrackableId(3, 4), TrackingState.None);
        harness.Report(new TrackableId(3, 4), TrackingState.None);

        Assert.That(m_Instance.activeSelf, Is.False);
        Assert.That(unavailableCount, Is.EqualTo(1));
    }

    [Test]
    public void SceneUsesTenCentimetreTestImageLibrary()
    {
        var library = AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(
            "Assets/Moodium/Tracking/MoodiumImageReferenceLibrary.asset");

        Assert.That(library, Is.Not.Null);
        Assert.That(library.count, Is.EqualTo(1));
        Assert.That(library[0].name, Is.EqualTo("TestImageTracking"));
        Assert.That(library[0].specifySize, Is.True);
        Assert.That(library[0].size, Is.EqualTo(new Vector2(0.1f, 0.1f)));
        const string scenePath = "Assets/Samples/PolySpatial/Scenes/Meshing 1.unity";
        var scene = SceneManager.GetSceneByPath(scenePath);
        var openedForTest = !scene.isLoaded;
        if (openedForTest)
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        try
        {
            ARTrackedImageManager manager = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                manager = root.GetComponentInChildren<ARTrackedImageManager>(true);
                if (manager != null) break;
            }
            Assert.That(manager, Is.Not.Null);
            Assert.That(manager.referenceLibrary, Is.SameAs(library));
        }
        finally
        {
            if (openedForTest)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    [Test]
    public void ImageLibraryIncludesFiveCentimetreCandyAndNaturePhoneImages()
    {
        var library = AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(
            "Assets/Moodium/Tracking/MoodiumImageReferenceLibrary.asset");

        Assert.That(library.count, Is.EqualTo(3));
        Assert.That(library[1].name, Is.EqualTo("CandyWorldPhoneTracking"));
        Assert.That(library[1].specifySize, Is.True);
        Assert.That(library[1].size, Is.EqualTo(new Vector2(0.05f, 0.05f)));
        Assert.That(library[2].name, Is.EqualTo("NatureWorldPhoneTracking"));
        Assert.That(library[2].specifySize, Is.True);
        Assert.That(library[2].size, Is.EqualTo(new Vector2(0.05f, 0.05f)));
    }

    Harness CreateHarness()
    {
        var type = typeof(TrackedObjectPoseFollower).Assembly.GetType(
            "Moodium.ImageTrackingCapsuleSpawner");
        Assert.That(type, Is.Not.Null, "The Image Tracking Capsule spawner must exist.");
        m_Host = new GameObject("Image Tracking Spawner");
        var spawner = m_Host.AddComponent(type);
        m_Prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        m_Anchor = new GameObject("Tracked Image Anchor");
        type.GetMethod("Configure")?.Invoke(spawner, new object[] { null, m_Prefab });
        var report = type.GetMethod("ReportTracking", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(report, Is.Not.Null);
        return new Harness(spawner, report, m_Anchor.transform);
    }

    static void AddEventHandler(Component spawner, string name, Delegate handler)
    {
        spawner.GetType().GetEvent(name)?.AddEventHandler(spawner, handler);
    }

    readonly struct Harness
    {
        public readonly Component Spawner;
        readonly MethodInfo m_Report;
        readonly Transform m_Anchor;

        public Harness(Component spawner, MethodInfo report, Transform anchor)
        {
            Spawner = spawner;
            m_Report = report;
            m_Anchor = anchor;
        }

        public GameObject Report(TrackableId id, TrackingState state)
        {
            return m_Report.Invoke(Spawner, new object[] { id, m_Anchor, state }) as GameObject;
        }
    }
}
