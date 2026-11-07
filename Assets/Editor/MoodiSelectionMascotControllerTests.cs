using Moodium.Flow;
using NUnit.Framework;
using UnityEngine;

public sealed class MoodiSelectionMascotControllerTests
{
    [Test]
    public void FirstTouchSelectsShySmile()
    {
        Assert.That(MoodiSelectionMascotController.GetNextReactionIndex(-1), Is.EqualTo(0));
    }

    [Test]
    public void ConsecutiveTouchesAlternateBetweenTheTwoReactionClips()
    {
        Assert.That(MoodiSelectionMascotController.GetNextReactionIndex(0), Is.EqualTo(1));
        Assert.That(MoodiSelectionMascotController.GetNextReactionIndex(1), Is.EqualTo(0));
    }

    [Test]
    public void MascotAnimatorSelectionPrefersTheAuthoredMoodiModelOverOtherAnimatedChildren()
    {
        var root = new GameObject("World Entry");
        var worldVisual = new GameObject("World Visual");
        var moodi = new GameObject("IP_Unity_3Clips");
        worldVisual.transform.SetParent(root.transform, false);
        moodi.transform.SetParent(root.transform, false);
        var unrelatedAnimator = worldVisual.AddComponent<Animator>();
        var moodiAnimator = moodi.AddComponent<Animator>();

        try
        {
            Assert.That(MoodiSelectionMascotController.FindMascotAnimator(root), Is.SameAs(moodiAnimator));
            Assert.That(MoodiSelectionMascotController.FindMascotAnimator(root), Is.Not.SameAs(unrelatedAnimator));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ContactCheckUsesTheMascotColliderDirectly()
    {
        var mascot = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var collider = mascot.GetComponent<BoxCollider>();
        try
        {
            Assert.That(MoodiSelectionMascotController.IsPointWithinContactRadius(
                collider, new Vector3(.51f, 0f, 0f), .02f), Is.True);
            Assert.That(MoodiSelectionMascotController.IsPointWithinContactRadius(
                collider, new Vector3(.7f, 0f, 0f), .02f), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(mascot);
        }
    }

    [Test]
    public void MotionRootPreservesAuthoredPoseAndSeparatesItFromAnimatedRoot()
    {
        var parent = new GameObject("Entry Root");
        var animated = new GameObject("Moodi Animated Root");
        animated.transform.SetParent(parent.transform, false);
        animated.transform.localPosition = new Vector3(.1f, .2f, .3f);
        animated.transform.localRotation = Quaternion.Euler(10f, 20f, 30f);
        animated.transform.localScale = Vector3.one * .015f;

        var motionRoot = MoodiSelectionMascotController.EnsureMotionRoot(animated.transform);

        Assert.That(motionRoot.parent, Is.EqualTo(parent.transform));
        Assert.That(motionRoot.localPosition, Is.EqualTo(new Vector3(.1f, .2f, .3f)));
        Assert.That(Quaternion.Angle(motionRoot.localRotation, Quaternion.Euler(10f, 20f, 30f)), Is.LessThan(.01f));
        Assert.That(motionRoot.localScale, Is.EqualTo(Vector3.one * .015f));
        Assert.That(animated.transform.parent, Is.EqualTo(motionRoot));
        Assert.That(animated.transform.localPosition, Is.EqualTo(Vector3.zero));
        Assert.That(animated.transform.localRotation, Is.EqualTo(Quaternion.identity));
        Assert.That(animated.transform.localScale, Is.EqualTo(Vector3.one));

        Object.DestroyImmediate(parent);
    }
}
