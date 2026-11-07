#if MOODIUM_IVY_STUDIO
using System.IO;
using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Vine leaves (2026-11-03): no built cylinder petiole; the texture's own drawn stalk starts on the stem and the leaf
/// points outward from the stem, leaning forward along the growth and lifted a little off the wall.
/// </summary>
public sealed class NatureVineLeafPoseTests
{
    static readonly Vector3 Normal = Vector3.back;       // a wall facing the viewer at -z
    static readonly Vector3 Up = Vector3.up;              // the stem climbs straight up

    [Test]
    public void LeavesPointSidewaysAndLeanForwardAlongTheGrowth()
    {
        var across = Vector3.Cross(Normal, Up);
        var left = NatureVineLeafPose.Outward(Up, Normal, 1f, 0f);
        Assert.That(Vector3.Distance(left, across), Is.LessThan(1e-4f), "0°: straight out to the side");
        var right = NatureVineLeafPose.Outward(Up, Normal, -1f, 0f);
        Assert.That(Vector3.Distance(right, -across), Is.LessThan(1e-4f), "the other side");
        foreach (var degrees in new[] { NatureVineLeafPose.MinForward, NatureVineLeafPose.MaxForward })
        {
            var outward = NatureVineLeafPose.Outward(Up, Normal, 1f, degrees);
            Assert.That(outward.magnitude, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(Vector3.Dot(outward, Normal), Is.EqualTo(0f).Within(1e-4f), "stays in the wall plane");
            Assert.That(Vector3.Dot(outward, Up), Is.EqualTo(Mathf.Sin(degrees * Mathf.Deg2Rad)).Within(1e-4f), "leans toward the growth");
        }
        Assert.That(NatureVineLeafPose.Outward(Normal, Normal, 1f, 30f), Is.EqualTo(Vector3.zero), "a stem along the normal has no outward");
    }

    [Test]
    public void TheTipPointsOutwardAndLiftsOffTheWall()
    {
        var outward = NatureVineLeafPose.Outward(Up, Normal, 1f, 35f);
        foreach (var lift in new[] { NatureVineLeafPose.MinLift, NatureVineLeafPose.MaxLift })
        {
            var rotation = NatureVineLeafPose.Rotation(Normal, outward, lift);
            var tip = rotation * Vector3.up;
            Assert.That(Vector3.Dot(tip, Normal), Is.EqualTo(Mathf.Sin(lift * Mathf.Deg2Rad)).Within(1e-4f), "tip lifts toward the viewer");
            Assert.That(Vector3.Dot(Vector3.ProjectOnPlane(tip, Normal).normalized, outward), Is.GreaterThan(.9999f));
        }
    }

    [Test]
    public void TheDrawnStalkEndLandsOnTheStemAtEveryUnfoldStep()
    {
        var joint = new Vector3(.3f, 1.2f, 1f);
        var rotation = NatureVineLeafPose.Rotation(Normal, NatureVineLeafPose.Outward(Up, Normal, -1f, 40f), 15f);
        var size = new Vector3(.05f, .05f, .05f);
        var leaf = new GameObject("Leaf");
        try
        {
            foreach (var unfold in new[] { .001f, .3f, 1f })
            {
                leaf.transform.SetPositionAndRotation(NatureVineLeafPose.Centre(joint, rotation, size * unfold), rotation);
                leaf.transform.localScale = size * unfold;
                Assert.That(Vector3.Distance(leaf.transform.TransformPoint(NatureVineLeafPose.StalkEnd), joint), Is.LessThan(1e-5f), "unfold " + unfold);
            }
            var tip = leaf.transform.TransformPoint(new Vector3(0f, .5f, 0f));
            Assert.That(Vector3.Dot(tip - joint, NatureVineLeafPose.Outward(Up, Normal, -1f, 40f)), Is.GreaterThan(.04f), "the leaf lies outward of the stem, not across it");
        }
        finally { Object.DestroyImmediate(leaf); }
    }

    [Test]
    public void LeavesKeepThePluginsSideAndAlternateWhenOnTheStem()
    {
        var joint = Vector3.zero;
        var across = Vector3.Cross(Normal, Up);
        Assert.That(NatureVineLeafPose.Side(across * .02f, joint, Up, Normal, 0), Is.EqualTo(1f));
        Assert.That(NatureVineLeafPose.Side(-across * .02f, joint, Up, Normal, 0), Is.EqualTo(-1f));
        Assert.That(NatureVineLeafPose.Side(Up * .02f, joint, Up, Normal, 0), Is.EqualTo(1f));
        Assert.That(NatureVineLeafPose.Side(Up * .02f, joint, Up, Normal, 1), Is.EqualTo(-1f));
    }

    [Test]
    public void TheLeafTextureDrawsItsStalkEndAtTheBottomWhereThePoseExpectsIt()
    {
        var texture = new Texture2D(2, 2);
        try
        {
            Assert.That(texture.LoadImage(File.ReadAllBytes("Assets/Resources/MoodiumNature/NativeVine/Leaf 3.png")), Is.True);
            var u = NatureVineLeafPose.StalkEnd.x + .5f;
            Assert.That(texture.GetPixelBilinear(u, .002f).a, Is.GreaterThan(.5f), "the drawn stalk reaches the bottom edge at u = 0.532");
            Assert.That(texture.GetPixelBilinear(.25f, .002f).a, Is.LessThan(.1f));
            Assert.That(texture.GetPixelBilinear(.75f, .002f).a, Is.LessThan(.1f));
        }
        finally { Object.DestroyImmediate(texture); }
    }

    [Test]
    public void NoCylinderPetioleIsBuiltAndLeavesStayPinnedWhileUnfolding()
    {
        var source = File.ReadAllText("Assets/Scripts/Moodium/NatureIvySpatialGrowthController.cs");
        var reveal = source.Substring(source.IndexOf("System.Collections.IEnumerator RevealBranches(", System.StringComparison.Ordinal));
        Assert.That(reveal, Does.Not.Contain("PrimitiveType.Cylinder"));
        Assert.That(reveal, Does.Contain("NatureVineLeafPose.Centre(joint, leaf.transform.rotation, size)"));
        Assert.That(reveal, Does.Contain("NatureVineLeafPose.Centre(native.stalk, native.rotation, native.size * unfold)"));
    }
}

#endif
