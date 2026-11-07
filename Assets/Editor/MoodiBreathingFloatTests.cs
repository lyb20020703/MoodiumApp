using Moodium.Flow;
using NUnit.Framework;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public sealed class MoodiBreathingFloatTests
{
    [Test]
    public void EvaluateOffset_UsesRequestedAmplitudeAndPeriod()
    {
        var offsetAtQuarterPeriod = MoodiBreathingFloat.EvaluateOffset(.7f, .01f, 2.8f);

        Assert.That(offsetAtQuarterPeriod, Is.EqualTo(.01f).Within(.000001f));
    }

    [Test]
    public void EvaluateScale_PreservesNonUniformBaseline()
    {
        var baseline = new Vector3(1f, 2f, 3f);

        var result = MoodiBreathingFloat.EvaluateScale(baseline, .5f, .01f);

        Assert.That(result.x, Is.EqualTo(1.005f).Within(.000001f));
        Assert.That(result.y, Is.EqualTo(2.01f).Within(.000001f));
        Assert.That(result.z, Is.EqualTo(3.015f).Within(.000001f));
    }

    [TestCase(0f)]
    [TestCase(-2f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void EvaluateOffset_InvalidPeriod_RemainsFinite(float invalidPeriod)
    {
        var offset = MoodiBreathingFloat.EvaluateOffset(10f, .01f, invalidPeriod);

        Assert.That(float.IsNaN(offset), Is.False);
        Assert.That(float.IsInfinity(offset), Is.False);
    }

    [Test]
    public void Disable_RestoresBaselineAcrossRepeatedEnableCycles()
    {
        var root = new GameObject("Moodi meshy");
        var baselinePosition = new Vector3(.064f, .018f, -.087f);
        var baselineScale = new Vector3(.03f, .04f, .05f);
        root.transform.localPosition = baselinePosition;
        root.transform.localScale = baselineScale;

        try
        {
            var breathing = root.AddComponent<MoodiBreathingFloat>();
            InvokeLifecycle(breathing, "OnEnable");
            root.transform.localPosition += Vector3.up;
            root.transform.localScale *= 2f;
            InvokeLifecycle(breathing, "OnDisable");

            Assert.That(root.transform.localPosition, Is.EqualTo(baselinePosition));
            Assert.That(root.transform.localScale, Is.EqualTo(baselineScale));

            InvokeLifecycle(breathing, "OnEnable");
            root.transform.localPosition += Vector3.down;
            root.transform.localScale *= .5f;
            InvokeLifecycle(breathing, "OnDisable");

            Assert.That(root.transform.localPosition, Is.EqualTo(baselinePosition));
            Assert.That(root.transform.localScale, Is.EqualTo(baselineScale));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void CandyWorldUI_MoodiMeshy_HasBreathingFloat()
    {
        AssertPrefabMoodi("Assets/prefab/CandyWorldUI.prefab",
            new Vector3(0f, .023f, -.11f), Vector3.one * .05f);
    }

    [Test]
    public void NatureWorldUI_MoodiMeshy_HasBreathingFloat()
    {
        AssertPrefabMoodi("Assets/prefab/NatureWorldUI.prefab",
            new Vector3(.064f, .018f, -.087f), Vector3.one * .03f);
    }

    static void AssertPrefabMoodi(string path, Vector3 expectedPosition, Vector3 expectedScale)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(prefab, Is.Not.Null, $"Could not load {path}");

        Transform moodi = null;
        foreach (var child in prefab.GetComponentsInChildren<Transform>(true))
            if (child.name == "Moodi meshy")
            {
                moodi = child;
                break;
            }

        Assert.That(moodi, Is.Not.Null, $"{path} must contain Moodi meshy");
        Assert.That(moodi.GetComponents<MoodiBreathingFloat>(), Has.Length.EqualTo(1));
        Assert.That(moodi.localPosition, Is.EqualTo(expectedPosition));
        Assert.That(moodi.localScale, Is.EqualTo(expectedScale));
    }

    static void InvokeLifecycle(MoodiBreathingFloat target, string methodName)
    {
        var method = typeof(MoodiBreathingFloat).GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        method.Invoke(target, null);
    }
}
