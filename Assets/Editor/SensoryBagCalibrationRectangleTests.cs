using Moodium.Calibration;
using NUnit.Framework;
using UnityEngine;

public sealed class SensoryBagCalibrationRectangleTests
{
    static readonly SensoryBagCalibrationSettings Settings = new(
        defaultWidth: 0.25f,
        defaultHeight: 0.17f,
        defaultDepth: 0.05f,
        defaultCenterOffset: Vector2.zero,
        minWidth: 0.08f,
        maxWidth: 0.6f,
        minHeight: 0.06f,
        maxHeight: 0.45f);

    [Test]
    public void RightEdgeResize_HoldsLeftEdgeFixed()
    {
        var rectangle = CreateRectangle(0.25f, 0.17f, Vector2.zero);

        rectangle.ResizeFromHandle(CalibrationHandleKind.Right, new Vector2(0.20f, 0f));

        Assert.That(rectangle.Data.Width, Is.EqualTo(0.325f).Within(0.0001f));
        Assert.That(rectangle.Left, Is.EqualTo(-0.125f).Within(0.0001f));
    }

    [Test]
    public void TopRightCornerResize_HoldsBottomLeftFixed()
    {
        var rectangle = CreateRectangle(0.25f, 0.17f, Vector2.zero);
        var originalBottomLeft = new Vector2(rectangle.Left, rectangle.Bottom);

        rectangle.ResizeFromHandle(CalibrationHandleKind.TopRight, new Vector2(0.20f, 0.16f));

        Assert.That(rectangle.Left, Is.EqualTo(originalBottomLeft.x).Within(0.0001f));
        Assert.That(rectangle.Bottom, Is.EqualTo(originalBottomLeft.y).Within(0.0001f));
        Assert.That(rectangle.Right, Is.EqualTo(0.20f).Within(0.0001f));
        Assert.That(rectangle.Top, Is.EqualTo(0.16f).Within(0.0001f));
    }

    [Test]
    public void LeftEdgeResize_ClampsAtMinimumInsteadOfFlipping()
    {
        var rectangle = CreateRectangle(0.25f, 0.17f, Vector2.zero);
        var originalRight = rectangle.Right;

        rectangle.ResizeFromHandle(CalibrationHandleKind.Left, new Vector2(1f, 0f));

        Assert.That(rectangle.Data.Width, Is.EqualTo(0.08f).Within(0.0001f));
        Assert.That(rectangle.Right, Is.EqualTo(originalRight).Within(0.0001f));
        Assert.That(rectangle.Left, Is.LessThan(rectangle.Right));
    }

    [Test]
    public void ProjectWorldPointToAnchorPlane_DiscardsLocalDepth()
    {
        var anchor = new GameObject("Anchor");
        try
        {
            anchor.transform.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(10f, 35f, 5f));
            var projected = SensoryBagCalibrationRectangle.ProjectWorldPointToAnchorPlane(
                anchor.transform,
                anchor.transform.TransformPoint(new Vector3(0.1f, -0.03f, 3f)));

            Assert.That(projected.x, Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(projected.y, Is.EqualTo(-0.03f).Within(0.0001f));
        }
        finally
        {
            Object.DestroyImmediate(anchor);
        }
    }

    static SensoryBagCalibrationRectangle CreateRectangle(float width, float height, Vector2 center)
    {
        var data = SensoryBagCalibrationData.CreateDefault(
            "CandyWorldPhoneTracking", SensoryBagWorldKind.Candy, Settings);
        data.Width = width;
        data.Height = height;
        data.CenterOffset = center;
        return new SensoryBagCalibrationRectangle(data, Settings);
    }
}
