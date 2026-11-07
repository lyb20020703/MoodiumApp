using System.Reflection;
using Moodium.CandyWorld;
using Moodium.Flow;
using Moodium.Interaction;
using Moodium.Reality;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Utils;
using UnityEngine.XR.ARFoundation;

public sealed class PalmPressWorldSpaceContactTests
{
    GameObject m_Root;
    GameObject m_DetachedCapsule;
    MoodiumWorldDefinition m_World;

    [TearDown]
    public void TearDown()
    {
        if (m_DetachedCapsule != null)
            Object.DestroyImmediate(m_DetachedCapsule);
        if (m_Root != null)
            Object.DestroyImmediate(m_Root);
        if (m_World != null)
            Object.DestroyImmediate(m_World);
    }

    [Test]
    public void TrackingPoseIsConvertedThroughXROrigin()
    {
        m_Root = new GameObject("Palm world-space test root");
        var origin = new GameObject("XR Origin").transform;
        origin.SetParent(m_Root.transform);
        origin.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 90f, 0f));
        var trackingPose = new Pose(new Vector3(0.2f, 0.3f, 0.4f), Quaternion.Euler(10f, 20f, 30f));

        var method = typeof(PalmPressGestureController).GetMethod(
            "TransformTrackingPose",
            BindingFlags.Public | BindingFlags.Static);

        Assert.That(method, Is.Not.Null);
        var worldPose = (Pose)method.Invoke(null, new object[] { trackingPose, origin });
        Assert.That(worldPose.position, Is.EqualTo(origin.TransformPoint(trackingPose.position)).Using(Vector3ComparerWithEqualsOperator.Instance));
        Assert.That(Quaternion.Angle(worldPose.rotation, origin.rotation * trackingPose.rotation), Is.LessThan(0.001f));
    }

    [Test]
    public void PalmVolumeCanBeDerivedWithoutUnsupportedPalmJoint()
    {
        var method = typeof(PalmPressGestureController).GetMethod(
            "TryBuildPalmBox",
            BindingFlags.Public | BindingFlags.Static);
        Assert.That(method, Is.Not.Null,
            "visionOS never supplies XRHandJointID.Palm, so the volume must use wrist/metacarpals.");
        var args = new object[]
        {
            new Pose(Vector3.zero, Quaternion.identity),
            new Pose(new Vector3(-0.03f, 0f, 0.08f), Quaternion.identity),
            new Pose(new Vector3(0f, 0f, 0.09f), Quaternion.identity),
            new Pose(new Vector3(0.03f, 0f, 0.075f), Quaternion.identity),
            Vector3.zero,
            Quaternion.identity
        };

        var valid = (bool)method.Invoke(null, args);

        Assert.That(valid, Is.True);
        Assert.That((Vector3)args[4],
            Is.EqualTo(new Vector3(0f, 0f, 0.045f))
                .Using(Vector3ComparerWithEqualsOperator.Instance));
        Assert.That(Vector3.Angle(((Quaternion)args[5]) * Vector3.forward, Vector3.forward),
            Is.LessThan(0.01f));
    }

    [Test]
    public void PalmContactChecksCapsuleColliderInsteadOfTrackedObjectVolume()
    {
        m_Root = new GameObject("Palm capsule contact test root");
        var controller = m_Root.AddComponent<PalmPressGestureController>();

        var capsuleObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        capsuleObject.name = "Tracked capsule visual";
        capsuleObject.transform.SetParent(m_Root.transform);
        capsuleObject.transform.position = new Vector3(0.4f, 0.8f, -0.2f);
        var capsule = capsuleObject.AddComponent<ChocolateCapsuleInteraction>();

        var trackedObject = new GameObject("Tracked tissue anchor");
        trackedObject.transform.SetParent(m_Root.transform);
        trackedObject.transform.position = Vector3.right * 10f;

        controller.SetTarget(capsule);

        var isTouching = typeof(PalmPressGestureController).GetMethod(
            "IsTouchingTarget",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(isTouching, Is.Not.Null);
        Physics.SyncTransforms();
        var touching = (bool)isTouching.Invoke(controller, new object[]
        {
            capsuleObject.GetComponent<BoxCollider>().bounds.center,
            capsuleObject.transform.rotation
        });
        Assert.That(touching, Is.True);
    }

    [Test]
    public void TrackedCapsuleUsesExplicitWorldPoseInsteadOfParentTransformPropagation()
    {
        m_Root = new GameObject("Tracked capsule follow test root");
        var anchor = new GameObject("Tracked tissue anchor").transform;
        anchor.SetParent(m_Root.transform);
        anchor.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 30f, 0f));
        var capsule = new GameObject("Tracked capsule visual");
        m_DetachedCapsule = capsule;
        capsule.transform.SetParent(m_Root.transform);
        var follower = capsule.AddComponent<TrackedObjectPoseFollower>();
        var localOffset = new Vector3(0.1f, 0.2f, -0.3f);
        var localRotation = Quaternion.Euler(10f, 20f, 30f);

        follower.Configure(anchor, localOffset, localRotation);

        Assert.That(capsule.transform.parent, Is.Null);
        Assert.That(capsule.transform.position,
            Is.EqualTo(anchor.TransformPoint(localOffset)).Using(Vector3ComparerWithEqualsOperator.Instance));

        anchor.SetPositionAndRotation(new Vector3(-2f, 0.5f, 4f), Quaternion.Euler(0f, -45f, 0f));
        follower.SyncNow();

        Assert.That(capsule.transform.position,
            Is.EqualTo(anchor.TransformPoint(localOffset)).Using(Vector3ComparerWithEqualsOperator.Instance));
        Assert.That(Quaternion.Angle(capsule.transform.rotation, anchor.rotation * localRotation), Is.LessThan(0.001f));
    }

    [Test]
    public void DetachedTrackedCapsuleStillIncreasesCandyEnergy()
    {
        m_Root = new GameObject("Detached tracked capsule progress test root");
        var manager = m_Root.AddComponent<CandyWorldManager>();
        m_World = ScriptableObject.CreateInstance<MoodiumWorldDefinition>();
        manager.Configure(null, null, null, m_World, null, null, null, null, null);
        manager.StartExperience(m_World);

        var anchor = new GameObject("Tracked tissue anchor");
        anchor.transform.SetParent(m_Root.transform);
        anchor.AddComponent<ARTrackedObject>();
        m_DetachedCapsule = new GameObject("Detached tracked capsule");
        var follower = m_DetachedCapsule.AddComponent<TrackedObjectPoseFollower>();
        follower.Configure(anchor.transform, Vector3.zero, Quaternion.identity);
        var interaction = m_DetachedCapsule.AddComponent<ChocolateCapsuleInteraction>();
        interaction.SetInteractionEnabled(true);

        interaction.PalmContactStarted();

        Assert.That(manager.Energy, Is.GreaterThan(0f));
    }
}
