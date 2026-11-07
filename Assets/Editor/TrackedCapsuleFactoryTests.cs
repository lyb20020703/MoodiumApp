using System;
using System.Reflection;
using Moodium.Flow;
using Moodium.Interaction;
using Moodium.Reality;
using NUnit.Framework;
using UnityEngine;

public sealed class TrackedCapsuleFactoryTests
{
    GameObject m_Prefab;
    GameObject m_Anchor;
    GameObject m_Instance;

    [TearDown]
    public void TearDown()
    {
        if (m_Instance != null) UnityEngine.Object.DestroyImmediate(m_Instance);
        if (m_Prefab != null) UnityEngine.Object.DestroyImmediate(m_Prefab);
        if (m_Anchor != null) UnityEngine.Object.DestroyImmediate(m_Anchor);
    }

    [Test]
    public void FactoryCreatesInteractiveNonScalableFollowingCapsule()
    {
        m_Prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        m_Anchor = new GameObject("Tracked Anchor");
        var factoryType = typeof(TrackedObjectPoseFollower).Assembly.GetType(
            "Moodium.Reality.TrackedCapsuleRuntimeFactory");

        Assert.That(factoryType, Is.Not.Null,
            "The shared tracked-Capsule factory must exist.");
        var create = factoryType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
        m_Instance = create?.Invoke(null, new object[]
        {
            m_Prefab, m_Anchor.transform, Vector3.zero, Quaternion.identity, "Tracked Capsule"
        }) as GameObject;

        Assert.That(m_Instance, Is.Not.Null);
        Assert.That(m_Instance.GetComponent<TrackedObjectPoseFollower>().Anchor,
            Is.EqualTo(m_Anchor.transform));
        var interaction = m_Instance.GetComponent<ChocolateCapsuleInteraction>();
        Assert.That(interaction.InteractionEnabled, Is.True);
        Assert.That(interaction.SpatialPointerEnabled, Is.False);
        Assert.That(m_Instance.GetComponent<MoodiumManipulable>().CanManipulate, Is.False);
        Assert.That(m_Instance.GetComponent<TrackedCapsuleHandCollision>(), Is.Not.Null);
    }

    [Test]
    public void FactoryAddsSoftDeformationForTrackedCandy()
    {
        m_Prefab = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        m_Prefab.name = "CandySoft";
        m_Anchor = new GameObject("Tracked Anchor");

        var factoryType = typeof(TrackedObjectPoseFollower).Assembly.GetType(
            "Moodium.Reality.TrackedCapsuleRuntimeFactory");
        var create = factoryType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
        m_Instance = create?.Invoke(null, new object[]
        {
            m_Prefab, m_Anchor.transform, Vector3.zero, Quaternion.identity, "Tracked CandySoft"
        }) as GameObject;

        Assert.That(m_Instance, Is.Not.Null);
        Assert.That(m_Instance.GetComponent<SoftTouchDeformationController>(), Is.Not.Null,
            "Tracked soft candy must use the same deformation controller as Creative Space objects.");
    }

    [Test]
    public void FactoryAddsSoftDeformationForTrackedNature()
    {
        m_Prefab = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        m_Prefab.name = "NatureSoft";
        m_Anchor = new GameObject("Tracked Anchor");

        var factoryType = typeof(TrackedObjectPoseFollower).Assembly.GetType(
            "Moodium.Reality.TrackedCapsuleRuntimeFactory");
        var create = factoryType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
        m_Instance = create?.Invoke(null, new object[]
        {
            m_Prefab, m_Anchor.transform, Vector3.zero, Quaternion.identity, "Tracked NatureSoft"
        }) as GameObject;

        Assert.That(m_Instance, Is.Not.Null);
        Assert.That(m_Instance.GetComponent<SoftTouchDeformationController>(), Is.Not.Null,
            "Tracked NatureSoft must use the shared soft-deformation interaction.");
    }
}
