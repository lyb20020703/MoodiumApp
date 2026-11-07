using System.Reflection;
using Moodium;
using Moodium.Flow;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

public sealed class TrackedCapsuleManipulationTests
{
    GameObject m_SpawnerObject;
    GameObject m_TrackedObject;
    GameObject m_CapsulePrefab;

    [TearDown]
    public void TearDown()
    {
        if (m_SpawnerObject != null)
            Object.DestroyImmediate(m_SpawnerObject);
        if (m_TrackedObject != null)
            Object.DestroyImmediate(m_TrackedObject);
        if (m_CapsulePrefab != null)
            Object.DestroyImmediate(m_CapsulePrefab);
    }

    [Test]
    public void TrackedChocolateCapsuleIgnoresTwoHandScaleInput()
    {
        m_SpawnerObject = new GameObject("Test Tissue Spawner");
        var spawner = m_SpawnerObject.AddComponent<TissueObjectTrackingSpawner>();
        m_CapsulePrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        m_CapsulePrefab.name = "Test Chocolate Capsule Prefab";
        spawner.Configure(null, m_CapsulePrefab);

        m_TrackedObject = new GameObject("Test Tracked Tissue");
        var trackedObject = m_TrackedObject.AddComponent<ARTrackedObject>();
        var spawnMethod = typeof(TissueObjectTrackingSpawner).GetMethod(
            "SpawnChocolate",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(spawnMethod, Is.Not.Null);
        var instance = spawnMethod.Invoke(spawner, new object[] { trackedObject }) as GameObject;
        Assert.That(instance, Is.Not.Null);

        var manipulable = instance.GetComponent<MoodiumManipulable>();
        Assert.That(manipulable, Is.Not.Null);
        Assert.That(manipulable.CanManipulate, Is.False);
        var originalScale = instance.transform.localScale;
        manipulable.BeginPointer(1, Vector3.zero, Quaternion.identity);
        manipulable.BeginPointer(2, Vector3.right, Quaternion.identity);
        manipulable.UpdatePointer(2, Vector3.right * 2f, Quaternion.identity);

        Assert.That(instance.transform.localScale, Is.EqualTo(originalScale));
    }
}
