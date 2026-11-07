using Moodium.Calibration;
using NUnit.Framework;
using UnityEngine;

public sealed class SensoryBagCalibrationDataTests
{
    [Test]
    public void DefaultData_UsesConfiguredBagSizeAndOffset()
    {
        var settings = new SensoryBagCalibrationSettings(
            defaultWidth: 0.25f,
            defaultHeight: 0.17f,
            defaultDepth: 0.05f,
            defaultCenterOffset: new Vector2(0.03f, -0.02f),
            minWidth: 0.08f,
            maxWidth: 0.6f,
            minHeight: 0.06f,
            maxHeight: 0.45f);

        var data = SensoryBagCalibrationData.CreateDefault(
            "CandyWorldPhoneTracking", SensoryBagWorldKind.Candy, settings);

        Assert.That(data.ReferenceImageName, Is.EqualTo("CandyWorldPhoneTracking"));
        Assert.That(data.WorldKind, Is.EqualTo(SensoryBagWorldKind.Candy));
        Assert.That(data.Width, Is.EqualTo(0.25f));
        Assert.That(data.Height, Is.EqualTo(0.17f));
        Assert.That(data.Depth, Is.EqualTo(0.05f));
        Assert.That(data.CenterOffset, Is.EqualTo(new Vector2(0.03f, -0.02f)));
    }

    [Test]
    public void ClampDimensions_ConstrainsValuesWithoutChangingAnchorLocalOffset()
    {
        var settings = new SensoryBagCalibrationSettings(
            defaultWidth: 0.25f,
            defaultHeight: 0.17f,
            defaultDepth: 0.05f,
            defaultCenterOffset: Vector2.zero,
            minWidth: 0.08f,
            maxWidth: 0.6f,
            minHeight: 0.06f,
            maxHeight: 0.45f);
        var data = new SensoryBagCalibrationData
        {
            Width = 0.01f,
            Height = 1f,
            CenterOffset = new Vector2(-0.16f, 0.11f)
        };

        data.ClampDimensions(settings);

        Assert.That(data.Width, Is.EqualTo(0.08f));
        Assert.That(data.Height, Is.EqualTo(0.45f));
        Assert.That(data.CenterOffset, Is.EqualTo(new Vector2(-0.16f, 0.11f)));
    }
}
