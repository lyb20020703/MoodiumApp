using System.Reflection;
using Moodium.CandyWorld;
using Moodium.Flow;
using Moodium.Interaction;
using Moodium.Reality;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

public sealed class DualTrackingRealityFlowTests
{
    GameObject m_Root;
    GameObject m_First;
    GameObject m_Second;
    MoodiumWorldDefinition m_World;

    [TearDown]
    public void TearDown()
    {
        if (m_First != null) Object.DestroyImmediate(m_First);
        if (m_Second != null) Object.DestroyImmediate(m_Second);
        if (m_Root != null) Object.DestroyImmediate(m_Root);
        if (m_World != null) Object.DestroyImmediate(m_World);
    }

    [Test]
    public void RealityFlowKeepsObjectAndImageCapsulesRegisteredTogether()
    {
        m_Root = new GameObject("Dual Tracking Flow");
        var flow = m_Root.AddComponent<RealityEnhancementFlowController>();
        m_First = new GameObject("Object Capsule");
        m_Second = new GameObject("Image Capsule");
        var first = m_First.AddComponent<ChocolateCapsuleInteraction>();
        var second = m_Second.AddComponent<ChocolateCapsuleInteraction>();
        var register = typeof(RealityEnhancementFlowController).GetMethod(
            "RegisterAvailableCapsule", BindingFlags.Instance | BindingFlags.NonPublic);
        var count = typeof(RealityEnhancementFlowController).GetProperty(
            "InteractiveCapsuleCount", BindingFlags.Instance | BindingFlags.Public);

        Assert.That(register, Is.Not.Null);
        Assert.That(count, Is.Not.Null);
        register.Invoke(flow, new object[] { first });
        register.Invoke(flow, new object[] { second });

        Assert.That(count.GetValue(flow), Is.EqualTo(2));
    }

    [Test]
    public void ImageTrackedCapsuleIncreasesSharedCandyEnergy()
    {
        m_Root = new GameObject("Candy World");
        var manager = m_Root.AddComponent<CandyWorldManager>();
        m_World = ScriptableObject.CreateInstance<MoodiumWorldDefinition>();
        manager.Configure(null, null, null, m_World, null, null, null, null, null);
        manager.StartExperience(m_World);

        var anchor = new GameObject("Tracked Image");
        anchor.transform.SetParent(m_Root.transform);
        anchor.AddComponent<ARTrackedImage>();
        m_First = new GameObject("Image Capsule");
        var follower = m_First.AddComponent<TrackedObjectPoseFollower>();
        follower.Configure(anchor.transform, Vector3.zero, Quaternion.identity);
        var interaction = m_First.AddComponent<ChocolateCapsuleInteraction>();
        interaction.SetInteractionEnabled(true);

        interaction.PalmContactStarted();

        Assert.That(manager.Energy, Is.GreaterThan(0f));
    }
}
