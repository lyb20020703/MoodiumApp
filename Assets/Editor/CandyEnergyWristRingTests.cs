using Moodium.CandyWorld;
using NUnit.Framework;
using System.Reflection;
using UnityEngine;

public sealed class CandyEnergyWristRingTests
{
    [TestCase(0f, 0f)]
    [TestCase(.25f, 90f)]
    [TestCase(.5f, 180f)]
    [TestCase(.75f, 270f)]
    [TestCase(1f, 360f)]
    public void ProgressMapsToTheExpectedRingArc(float progress, float expectedDegrees)
    {
        Assert.That(CandyEnergyWristUI.ProgressToArcDegrees(progress), Is.EqualTo(expectedDegrees));
    }

    [Test]
    public void WorldThemeSelectsTheSpecifiedRingColors()
    {
        Assert.That(CandyEnergyWristUI.GetProgressRingColor(false),
            Is.EqualTo(new Color(1f, 91f / 255f, 157f / 255f, 1f)));
        Assert.That(CandyEnergyWristUI.GetProgressRingColor(true),
            Is.EqualTo(new Color(141f / 255f, 220f / 255f, 135f / 255f, 1f)));
    }

    [Test]
    public void OverlayAnchorSitsAboveTheOuterEdgeOfTheWristRing()
    {
        var canvasUnits = CandyEnergyWristUI.CalculateOverlayVerticalAnchor(
            ringRadius: .055f,
            ringThickness: .010f,
            clearance: .025f,
            canvasScale: .00023f);

        Assert.That(canvasUnits, Is.EqualTo(.085f / .00023f).Within(.01f));
    }

    [Test]
    public void ChineseLanguageUsesLocalizedWorldEnergyTitles()
    {
        var method = typeof(CandyEnergyWristUI).GetMethod("GetWorldEnergyTitle",
            BindingFlags.Public | BindingFlags.Static);

        Assert.That(method, Is.Not.Null, "Wrist UI needs a testable localized title source.");
        Assert.That((string)method.Invoke(null, new object[] { false, false }), Is.EqualTo("糖果能量"));
        Assert.That((string)method.Invoke(null, new object[] { true, false }), Is.EqualTo("自然能量"));
    }

    [Test]
    public void ChineseLanguageUsesLocalizedReleaseToggleStates()
    {
        var method = typeof(CandyEnergyWristUI).GetMethod("GetReleaseToggleLabel",
            BindingFlags.Public | BindingFlags.Static);

        Assert.That(method, Is.Not.Null, "Wrist UI needs a testable localized Release label source.");
        Assert.That((string)method.Invoke(null, new object[] { false, false }), Is.EqualTo("释放：关闭"));
        Assert.That((string)method.Invoke(null, new object[] { true, false }), Is.EqualTo("释放：开启"));
    }

    [Test]
    public void ExitButtonUsesLocalizedDirectExitLabels()
    {
        var method = typeof(CandyEnergyWristUI).GetMethod("GetExitButtonLabel",
            new[] { typeof(bool) });

        Assert.That(method, Is.Not.Null, "Wrist UI needs a testable localized exit label source.");
        Assert.That((string)method.Invoke(null, new object[] { false }), Is.EqualTo("退出世界"));
        Assert.That((string)method.Invoke(null, new object[] { true }), Is.EqualTo("Exit World"));
    }
}
