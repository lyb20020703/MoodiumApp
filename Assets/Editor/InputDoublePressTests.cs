using System.IO;
using Moodium.Flow;
using NUnit.Framework;
using UnityEngine;

/// <summary>One poke must press a button once, even when the fingertip bridge and the system Touch both report it.</summary>
public sealed class InputDoublePressTests
{
    [Test]
    public void PressesCloserThanTheCooldownAreOnePress()
    {
        Assert.That(MoodiumSpatialButton.IsRepeat(float.NegativeInfinity, 0f), Is.False, "first press");
        Assert.That(MoodiumSpatialButton.IsRepeat(10f, 10.1f), Is.True);
        Assert.That(MoodiumSpatialButton.IsRepeat(10f, 10f + MoodiumSpatialButton.PressCooldown + .01f), Is.False);
    }

    [Test]
    public void ASecondImmediatePressIsIgnored()
    {
        var go = new GameObject("Button");
        try
        {
            int calls = 0;
            var b = go.AddComponent<MoodiumSpatialButton>();
            b.Configure(null, null, () => calls++);
            b.Press(); b.Press();
            Assert.That(calls, Is.EqualTo(1));
        }
        finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void FingertipBridgeRunsBeforeTheRouter()
    {
        var bridge = typeof(SensoryBagDirectTouchInput).GetCustomAttributes(typeof(DefaultExecutionOrder), false);
        Assert.That(bridge.Length, Is.EqualTo(1));
        Assert.That(((DefaultExecutionOrder)bridge[0]).order, Is.LessThan(0), "router has the default order 0");
        Assert.That(typeof(MoodiumSpatialInteractionController).GetCustomAttributes(typeof(DefaultExecutionOrder), false), Is.Empty);
    }

    [Test]
    public void CalibrationStepButtonsAreDebounced()
    {
        var source = File.ReadAllText("Assets/Scripts/Moodium/Calibration/SensoryBagCalibrationHandle.cs");
        Assert.That(source, Does.Contain("MoodiumSpatialButton.IsRepeat(m_LastTap, now)"));
    }
}
