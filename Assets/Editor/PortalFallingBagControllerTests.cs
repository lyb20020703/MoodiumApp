using System.Reflection;
using Moodium.Opening;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class PortalFallingBagControllerTests
{
    GameObject m_Root;
    GameObject m_BagPrefab;
    GameObject m_SecondBagPrefab;

    [TearDown]
    public void TearDown()
    {
        if (m_Root != null)
            Object.DestroyImmediate(m_Root);
        if (m_BagPrefab != null)
            Object.DestroyImmediate(m_BagPrefab);
        if (m_SecondBagPrefab != null)
            Object.DestroyImmediate(m_SecondBagPrefab);
    }

    [Test]
    public void SpawnedBagReceivesPhysicsAndStaysInsideSpawnBounds()
    {
        m_Root = new GameObject("Bag Rain Test");
        m_BagPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(m_BagPrefab.GetComponent<Collider>());
        var controller = m_Root.AddComponent<PortalFallingBagController>();
        controller.ConfigureForTests(new[] { m_BagPrefab }, 3);

        controller.BuildPoolForTests();

        Assert.That(controller.SpawnedCount, Is.EqualTo(3));
        foreach (Transform child in m_Root.transform)
        {
            Assert.That(child.GetComponent<Rigidbody>(), Is.Not.Null);
            Assert.That(child.GetComponent<BoxCollider>(), Is.Not.Null);
            Assert.That(child.localPosition.x, Is.InRange(-0.28f, 0.28f));
            Assert.That(child.localPosition.y, Is.InRange(0.78f, 1.08f));
            Assert.That(child.localPosition.z, Is.InRange(0.2f, 0.5f));
        }
    }

    [Test]
    public void PortalPrefabContainsConfiguredBagRainAndInvisibleChamber()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/prefab/MoodiumOpening/MoodiumPortalEntrance.prefab");
        Assert.That(prefab, Is.Not.Null);
        var content = prefab.transform.Find("PortalVisualRoot/PortalContentRoot");
        Assert.That(content, Is.Not.Null);
        var controller = content.GetComponent<PortalFallingBagController>();
        Assert.That(controller, Is.Not.Null);

        var serialized = new SerializedObject(controller);
        Assert.That(serialized.FindProperty("m_BagPrefabs").arraySize, Is.EqualTo(3));
        Assert.That(serialized.FindProperty("m_ConcurrentCount").intValue, Is.EqualTo(30));
        var chamber = content.Find("BagPhysicsChamber");
        Assert.That(chamber, Is.Not.Null);
        Assert.That(chamber.GetComponentsInChildren<BoxCollider>(true), Has.Length.EqualTo(6));
        Assert.That(chamber.Find("Floor"), Is.Not.Null);
        Assert.That(chamber.GetComponentsInChildren<Renderer>(true), Is.Empty);
    }

    [Test]
    public void PortalPrefabSpawnsAboveTheVisibleBagArea()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/prefab/MoodiumOpening/MoodiumPortalEntrance.prefab");
        var controller = prefab.transform.Find("PortalVisualRoot/PortalContentRoot")
            .GetComponent<PortalFallingBagController>();
        var serialized = new SerializedObject(controller);

        Assert.That(serialized.FindProperty("m_SpawnY").vector2Value,
            Is.EqualTo(new Vector2(0.78f, 1.08f)));
    }

    [Test]
    public void PortalPrefabConfiguresStaggeredBagArrival()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/prefab/MoodiumOpening/MoodiumPortalEntrance.prefab");
        var controller = prefab.transform.Find("PortalVisualRoot/PortalContentRoot")
            .GetComponent<PortalFallingBagController>();
        var serialized = new SerializedObject(controller);

        var arrivalInterval = serialized.FindProperty("m_InitialSpawnInterval");
        Assert.That(arrivalInterval, Is.Not.Null);
        Assert.That(arrivalInterval.floatValue, Is.EqualTo(0.35f));
    }

    [Test]
    public void PortalPrefabProvidesFloorAndCapacityForPersistentBagStack()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/prefab/MoodiumOpening/MoodiumPortalEntrance.prefab");
        var content = prefab.transform.Find("PortalVisualRoot/PortalContentRoot");
        var controller = content.GetComponent<PortalFallingBagController>();
        var serialized = new SerializedObject(controller);

        Assert.That(serialized.FindProperty("m_ConcurrentCount").intValue, Is.EqualTo(30));
        Assert.That(content.Find("BagPhysicsChamber/Floor"), Is.Not.Null);
    }

    [Test]
    public void PortalPrefabUsesLargerBalancedBagsForTheFullStack()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/prefab/MoodiumOpening/MoodiumPortalEntrance.prefab");
        var controller = prefab.transform.Find("PortalVisualRoot/PortalContentRoot")
            .GetComponent<PortalFallingBagController>();
        var serialized = new SerializedObject(controller);

        Assert.That(serialized.FindProperty("m_ConcurrentCount").intValue, Is.EqualTo(30));
        Assert.That(serialized.FindProperty("m_UniformScale").vector2Value,
            Is.EqualTo(new Vector2(0.15f, 0.18f)));
        var leafScale = serialized.FindProperty("m_LeafBagScaleMultiplier");
        Assert.That(leafScale, Is.Not.Null);
        Assert.That(leafScale.floatValue, Is.EqualTo(1.15f));
    }

    [Test]
    public void PortalPrefabIncludesCandyBagInTheFiftyBagLibrary()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/prefab/MoodiumOpening/MoodiumPortalEntrance.prefab");
        var controller = prefab.transform.Find("PortalVisualRoot/PortalContentRoot")
            .GetComponent<PortalFallingBagController>();
        var serialized = new SerializedObject(controller);
        var prefabs = serialized.FindProperty("m_BagPrefabs");
        var candy = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefab/CandyBag.prefab");

        Assert.That(prefabs.arraySize, Is.EqualTo(3));
        Assert.That(prefabs.GetArrayElementAtIndex(2).objectReferenceValue, Is.EqualTo(candy));
        Assert.That(serialized.FindProperty("m_ConcurrentCount").intValue, Is.EqualTo(30));
    }

    [Test]
    public void StackingBagsKeepTheirMutualCollisionsEnabled()
    {
        m_Root = new GameObject("Stacking Bag Rain Test");
        m_BagPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(m_BagPrefab.GetComponent<Collider>());
        var controller = m_Root.AddComponent<PortalFallingBagController>();
        controller.ConfigureForTests(new[] { m_BagPrefab }, 2);

        controller.BuildPoolForTests();

        var first = m_Root.transform.GetChild(0).GetComponent<Collider>();
        var second = m_Root.transform.GetChild(1).GetComponent<Collider>();
        Assert.That(Physics.GetIgnoreCollision(first, second), Is.False);
    }

    [Test]
    public void SixBagsAlternateBetweenConfiguredPrefabs()
    {
        m_Root = new GameObject("Balanced Bag Rain Test");
        m_BagPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        m_BagPrefab.name = "WoodBag";
        Object.DestroyImmediate(m_BagPrefab.GetComponent<Collider>());
        m_SecondBagPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        m_SecondBagPrefab.name = "LeafBag";
        Object.DestroyImmediate(m_SecondBagPrefab.GetComponent<Collider>());
        var controller = m_Root.AddComponent<PortalFallingBagController>();
        controller.ConfigureForTests(new[] { m_BagPrefab, m_SecondBagPrefab }, 6);

        controller.BuildPoolForTests();

        var woodCount = 0;
        var leafCount = 0;
        foreach (Transform child in m_Root.transform)
        {
            if (child.name.StartsWith("WoodBag")) woodCount++;
            if (child.name.StartsWith("LeafBag")) leafCount++;
        }
        Assert.That(woodCount, Is.EqualTo(3));
        Assert.That(leafCount, Is.EqualTo(3));
    }

    [Test]
    public void SpawnedBagsDisableDefaultGravityForPortalLocalGravity()
    {
        m_Root = new GameObject("Slow Gravity Bag Rain Test");
        m_BagPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(m_BagPrefab.GetComponent<Collider>());
        var controller = m_Root.AddComponent<PortalFallingBagController>();
        controller.ConfigureForTests(new[] { m_BagPrefab }, 2);

        controller.BuildPoolForTests();

        foreach (Transform child in m_Root.transform)
            Assert.That(child.GetComponent<Rigidbody>().useGravity, Is.False);
    }

    [Test]
    public void SpawnedBagsUseDiscreteNonInterpolatedPhysicsForPortalPerformance()
    {
        m_Root = new GameObject("Cheap Physics Bag Rain Test");
        m_BagPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(m_BagPrefab.GetComponent<Collider>());
        var controller = m_Root.AddComponent<PortalFallingBagController>();
        controller.ConfigureForTests(new[] { m_BagPrefab }, 2);

        controller.BuildPoolForTests();

        foreach (Transform child in m_Root.transform)
        {
            var body = child.GetComponent<Rigidbody>();
            Assert.That(body.collisionDetectionMode, Is.EqualTo(CollisionDetectionMode.Discrete));
            Assert.That(body.interpolation, Is.EqualTo(RigidbodyInterpolation.None));
        }
    }

    [Test]
    public void SpawnedBagsDoNotReceiveCollisionBurstComponent()
    {
        m_Root = new GameObject("Bag Collision VFX Test");
        m_BagPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(m_BagPrefab.GetComponent<Collider>());
        var controller = m_Root.AddComponent<PortalFallingBagController>();
        controller.ConfigureForTests(new[] { m_BagPrefab }, 2);

        controller.BuildPoolForTests();

        foreach (Transform child in m_Root.transform)
            Assert.That(child.GetComponent("PortalBagCollisionVFX"), Is.Null);
    }
}
