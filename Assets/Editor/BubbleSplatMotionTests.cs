using System;
using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class BubbleSplatMotionTests
{
    [TestCase(false)] [TestCase(true)]
    public void BakePreservesPointGeometryAndStoresRigidMotionData(bool candy)
    {
        var cloud = new BubbleWorldData.Cloud { Count = 2, EyeHeight = 1.3f,
            Positions = new[] { new Vector3(2, -1, 0), new Vector3(0, 3, 30) },
            Sizes = new[] { .05f, .2f }, Colors = new[] { new Color32(20, 180, 30, 255), new Color32(180, 20, 80, 255) } };
        var mesh = BubbleWorldData.BuildLayerMesh(cloud, 0, 2, "motion test", candy: candy);
        try
        {
            var uv = mesh.uv2; var centers = mesh.colors; var vertices = mesh.vertices;
            var rawBounds = new Bounds(vertices[0], Vector3.zero);
            foreach (var vertex in vertices) rawBounds.Encapsulate(vertex);
            Assert.That(Vector3.Distance(mesh.bounds.extents, rawBounds.extents + Vector3.one * .8f), Is.LessThan(.0001f));
            for (var i = 0; i < 2; i++)
            {
                Assert.That(uv[i * 4].x, Is.InRange(0f, .9999999f));
                Assert.That(uv[i * 4].y, Is.InRange(candy ? .35f : .2f, 1f));
                Assert.That(Vector3.Distance(BubbleSplatMotionMath.DecodeCenter(centers[i * 4]), cloud.Positions[i]), Is.LessThan(.0001f));
                for (var corner = 1; corner < 4; corner++)
                {
                    Assert.That(uv[i * 4 + corner], Is.EqualTo(uv[i * 4]));
                    Assert.That(centers[i * 4 + corner], Is.EqualTo(centers[i * 4]));
                }
                var a = BubbleSplatMotionMath.Parameters.Default; a.Strength = 1; a.Boost = 8;
                a.HandA = new Vector4(0, 0, .5f, .35f); a.HandAmount = Vector2.one;
                var delta = BubbleSplatMotionMath.Displace(BubbleSplatMotionMath.DecodeCenter(centers[i * 4]), uv[i * 4].x, uv[i * 4].y, 13, a);
                for (var corner = 1; corner < 4; corner++)
                {
                    var other = BubbleSplatMotionMath.Displace(BubbleSplatMotionMath.DecodeCenter(centers[i * 4 + corner]), uv[i * 4 + corner].x, uv[i * 4 + corner].y, 13, a);
                    Assert.That((delta - other).magnitude, Is.LessThan(cloud.Sizes[i] * .05f));
                }
            }
        }
        finally { Object.DestroyImmediate(mesh); }
    }
    [Test]
    public void SeedNeverIncludesOneAcrossAllWorldPointIndices()
    {
        for (var i = 0; i < 2000000; i++) Assert.That(BubbleWorldData.MotionSeed(i), Is.InRange(0f, .9999999f));
    }
    [Test]
    public void NoInputsMeansNoDisplacementAndFarWindIsVisible()
    {
        var a = BubbleSplatMotionMath.Parameters.Default;
        Assert.That(BubbleSplatMotionMath.Displace(new Vector3(0, 3, 30), .37f, 1, 1, a), Is.EqualTo(Vector3.zero));
        a.Strength = 1; var near = 0f; var far = 0f;
        for (var i = 0; i < 600; i++)
        {
            near = Mathf.Max(near, BubbleSplatMotionMath.Displace(Vector3.forward * 2, .37f, 1, i * .05f, a).magnitude);
            far = Mathf.Max(far, BubbleSplatMotionMath.Displace(Vector3.forward * 30, .37f, 1, i * .05f, a).magnitude);
        }
        Assert.That(far, Is.GreaterThan(near * 2));
        Assert.That(near, Is.GreaterThan(.005f));
    }
    [Test]
    public void DualHandsAndBoostStayFiniteAndInsideExpandedBounds()
    {
        var a = BubbleSplatMotionMath.Parameters.Default; a.Strength = 1; a.Boost = 8; a.HandAmount = Vector2.one;
        a.HandA = new Vector4(0, 0, 0, .35f); a.HandB = new Vector4(0, 0, .7f, .35f);
        var random = new System.Random(1781);
        for (var i = 0; i < 1000; i++)
        {
            var p = i == 0 ? Vector3.zero : new Vector3((float)random.NextDouble() * 60 - 30, (float)random.NextDouble() * 10 - 2, (float)random.NextDouble() * 70);
            a.Style = i % 2;
            var d = BubbleSplatMotionMath.Displace(p, (float)random.NextDouble(), 1, i * .13f, a);
            Assert.That(float.IsNaN(d.sqrMagnitude) || float.IsInfinity(d.sqrMagnitude), Is.False);
            Assert.That(d.magnitude, Is.LessThanOrEqualTo(.80001f));
        }
    }
    [Test]
    public void HandFalloffHasExpectedNearAndRayBoundaries()
    {
        Assert.That(BubbleSplatMotionMath.Near(.18f, .3f), Is.EqualTo(1).Within(.0001f));
        Assert.That(BubbleSplatMotionMath.Near(.3f, .3f), Is.Zero);
        var a = BubbleSplatMotionMath.Parameters.Default;
        Assert.That(BubbleSplatMotionMath.Ray(Vector3.forward * .3f, Vector3.forward * .6f, Vector3.zero, a.OuterCos, a.InnerCos), Is.Zero);
        Assert.That(BubbleSplatMotionMath.Ray(Vector3.forward * 20, Vector3.forward * .6f, Vector3.zero, a.OuterCos, a.InnerCos), Is.EqualTo(1));
        Assert.That(BubbleSplatMotionMath.Ray(Vector3.right * 20, Vector3.forward * .6f, Vector3.zero, a.OuterCos, a.InnerCos), Is.Zero);
    }
    [Test]
    public void LocalCoordinatesIncludeRotationAndPhysicalRadiusScale()
    {
        var go = new GameObject("coordinates");
        try
        {
            go.transform.SetPositionAndRotation(new Vector3(3, 2, 1), Quaternion.Euler(10, 70, 0));
            go.transform.localScale = Vector3.one * 1.015f;
            var world = new Vector3(4, 2.5f, 3);
            var local = BubbleSplatMotion.LocalHand(go.transform, world, .3f);
            Assert.That(Vector3.Distance(go.transform.TransformPoint(local), world), Is.LessThan(.00001f));
            Assert.That(local.w * go.transform.lossyScale.x, Is.EqualTo(.3f).Within(.00001f));
        }
        finally { Object.DestroyImmediate(go); }
    }
    [Test]
    public void GestureDelaysPinchCooldownTrackingLossAndDecay()
    {
        var go = new GameObject("gesture");
        try
        {
            var config = go.AddComponent<BubbleSplatMotion>(); var state = new BubbleSplatMotion.HandState();
            var now = 0f;
            void Step(bool tracked, float gap, bool enabled = true, bool handoff = false)
            { now += .01f; state.Step(tracked, Vector3.forward, gap, true, enabled, handoff, now, .01f, config); }
            for (var i = 0; i < 14; i++) Step(true, .08f);
            Assert.That(state.Amount, Is.Zero);
            for (var i = 0; i < 100; i++) Step(true, .08f);
            Assert.That(state.Amount, Is.GreaterThan(.9f));
            Step(true, .02f);
            var atPinch = state.Amount;
            for (var i = 0; i < 79; i++) Step(true, .08f);
            Assert.That(state.EligibleTime, Is.Zero);
            Assert.That(state.Amount, Is.LessThan(atPinch));
            for (var i = 0; i < 200; i++) Step(true, .08f);
            Assert.That(state.Amount, Is.GreaterThan(.99f));
            for (var i = 0; i < 31; i++) Step(false, 0);
            Assert.That(state.EligibleTime, Is.Zero);
            var beforeDecay = state.Amount;
            for (var i = 0; i < 100; i++) Step(false, 0);
            Assert.That(state.Amount / beforeDecay, Is.EqualTo(Mathf.Exp(-1)).Within(.001f));
            for (var i = 0; i < 1000; i++) Step(false, 0);
            Assert.That(state.Amount, Is.Zero);
            for (var i = 0; i < 100; i++) Step(true, .05f);
            Assert.That(state.Amount, Is.Zero, "Half-closed hands must not trigger.");
        }
        finally { Object.DestroyImmediate(go); }
    }
    [Test]
    public void WindLifecycleKeepsHandoffValueAndResetClearsMaterial()
    {
        var go = new GameObject("wind lifecycle");
        var material = BubbleSplatMaterial.Create(null, 1.3f, false, true);
        try
        {
            var motion = go.AddComponent<BubbleSplatMotion>(); motion.Initialize(material, go.transform, null);
            motion.SetStage(BubbleSplatMotion.Stage.Miniature);
            Assert.That(motion.AdvanceWind(.01f), Is.EqualTo(.3f));
            motion.SetStage(BubbleSplatMotion.Stage.Inside);
            Assert.That(motion.AdvanceWind(.75f), Is.EqualTo(.65f).Within(.0001f));
            motion.SetStage(BubbleSplatMotion.Stage.HandingOff);
            Assert.That(motion.AdvanceWind(5), Is.EqualTo(.65f).Within(.0001f));
            motion.SetStage(BubbleSplatMotion.Stage.Leaving);
            Assert.That(motion.AdvanceWind(.5f), Is.Zero);
            material.SetVector("_HandAmount", Vector4.one); material.SetFloat("_WindStrength", 1);
            motion.enabled = false;
            // Exercise the reset used by OnDisable without invoking engine callbacks in EditMode.
            motion.ResetMotion();
            Assert.That(material.GetFloat("_WindStrength"), Is.Zero);
            Assert.That(material.GetVector("_HandAmount"), Is.EqualTo(Vector4.zero));
        }
        finally { Object.DestroyImmediate(go); Object.DestroyImmediate(material); }
    }
    [Test]
    public void HandRiseAndPositionSmoothingUseSpecifiedTimeConstants()
    {
        var go = new GameObject("smoothing");
        try
        {
            var config = go.AddComponent<BubbleSplatMotion>(); config.TriggerDelay = 0;
            var state = new BubbleSplatMotion.HandState { HasPosition = true, Position = Vector3.zero, LastSeen = 0 };
            for (var i = 1; i <= 35; i++) state.Step(true, Vector3.forward, .08f, true, true, false, i * .01f, .01f, config);
            Assert.That(state.Amount, Is.EqualTo(1 - Mathf.Exp(-1)).Within(.0001f));
            Assert.That(state.Position.z, Is.EqualTo(1 - Mathf.Exp(-.35f / .08f)).Within(.0001f));
            for (var i = 36; i <= 300; i++) state.Step(true, Vector3.forward, .08f, true, true, false, i * .01f, .01f, config);
            Assert.That(state.Growth, Is.EqualTo(1).Within(.00001f));
        }
        finally { Object.DestroyImmediate(go); }
    }
    [Test]
    public void HandoffDisablesHandsAndDecaysFasterThanNormalWithdrawal()
    {
        var go = new GameObject("handoff");
        try
        {
            var config = go.AddComponent<BubbleSplatMotion>();
            var state = new BubbleSplatMotion.HandState { Amount = 1, HasPosition = true };
            for (var i = 1; i <= 30; i++) state.Step(true, Vector3.forward, .08f, true, false, true, i * .01f, .01f, config);
            Assert.That(state.Amount, Is.Zero);
            Assert.That(BubbleWorldController.MotionStage(BubbleWorldController.Phase.HandingOff), Is.EqualTo(BubbleSplatMotion.Stage.HandingOff));
            Assert.That(BubbleWorldController.MotionStage(BubbleWorldController.Phase.Portal), Is.EqualTo(BubbleSplatMotion.Stage.Miniature));
            Assert.That(BubbleWorldController.MotionStage(BubbleWorldController.Phase.Leaving), Is.EqualTo(BubbleSplatMotion.Stage.Leaving));
        }
        finally { Object.DestroyImmediate(go); }
    }
}
