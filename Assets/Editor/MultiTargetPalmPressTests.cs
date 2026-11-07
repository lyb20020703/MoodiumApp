using System.Reflection;
using Moodium.Interaction;
using NUnit.Framework;
using UnityEngine;

public sealed class MultiTargetPalmPressTests
{
    GameObject m_ControllerObject;
    GameObject m_FirstObject;
    GameObject m_SecondObject;

    [TearDown]
    public void TearDown()
    {
        if (m_ControllerObject != null) Object.DestroyImmediate(m_ControllerObject);
        if (m_FirstObject != null) Object.DestroyImmediate(m_FirstObject);
        if (m_SecondObject != null) Object.DestroyImmediate(m_SecondObject);
    }

    [Test]
    public void OverlapReturnsTheSpecificRegisteredCapsule()
    {
        var controller = CreateControllerAndTargets(out var first, out var second);
        Register(controller, first);
        Register(controller, second);

        Assert.That(Resolve(controller, second.transform.position), Is.SameAs(second));
    }

    [Test]
    public void UnregisteringOneCapsuleLeavesTheOtherInteractive()
    {
        var controller = CreateControllerAndTargets(out var first, out var second);
        Register(controller, first);
        Register(controller, second);
        InvokeTargetMethod(controller, "UnregisterTarget", first);

        Assert.That(Resolve(controller, first.transform.position), Is.Null);
        Assert.That(Resolve(controller, second.transform.position), Is.SameAs(second));
    }

    PalmPressGestureController CreateControllerAndTargets(
        out ChocolateCapsuleInteraction first,
        out ChocolateCapsuleInteraction second)
    {
        m_ControllerObject = new GameObject("Palm Controller");
        var controller = m_ControllerObject.AddComponent<PalmPressGestureController>();
        controller.enabled = false;
        first = CreateCapsule("First", Vector3.left * 0.5f, out m_FirstObject);
        second = CreateCapsule("Second", Vector3.right * 0.5f, out m_SecondObject);
        Physics.SyncTransforms();
        return controller;
    }

    static ChocolateCapsuleInteraction CreateCapsule(string name, Vector3 position, out GameObject root)
    {
        root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        root.name = name;
        root.transform.position = position;
        return root.AddComponent<ChocolateCapsuleInteraction>();
    }

    static void Register(PalmPressGestureController controller, ChocolateCapsuleInteraction target)
    {
        InvokeTargetMethod(controller, "RegisterTarget", target);
    }

    static void InvokeTargetMethod(
        PalmPressGestureController controller,
        string methodName,
        ChocolateCapsuleInteraction target)
    {
        var method = typeof(PalmPressGestureController).GetMethod(
            methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.That(method, Is.Not.Null, $"{methodName} must support multiple live Capsules.");
        method.Invoke(controller, new object[] { target });
    }

    static ChocolateCapsuleInteraction Resolve(PalmPressGestureController controller, Vector3 position)
    {
        var method = typeof(PalmPressGestureController).GetMethod(
            "FindTouchingTarget", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return method.Invoke(controller, new object[] { position, Quaternion.identity })
            as ChocolateCapsuleInteraction;
    }
}
