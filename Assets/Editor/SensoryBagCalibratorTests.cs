using Moodium.Calibration;
using NUnit.Framework;
using TMPro;
using UnityEngine;

public sealed class SensoryBagCalibratorTests
{
    GameObject m_Anchor;
    GameObject m_CalibratorObject;

    [TearDown]
    public void TearDown()
    {
        if (m_CalibratorObject != null) Object.DestroyImmediate(m_CalibratorObject);
        if (m_Anchor != null) Object.DestroyImmediate(m_Anchor);
    }

    [Test]
    public void Confirm_EmitsAnchorLocalDataAndHidesFrame()
    {
        var calibrator = CreateCalibrator();
        calibrator.Begin(m_Anchor.transform, DefaultData(), DefaultSettings());
        calibrator.MoveFrameToAnchorPlane(new Vector2(0.08f, -0.04f));
        SensoryBagCalibrationData received = default;
        calibrator.CalibrationConfirmed += data => received = data;

        calibrator.ConfirmCalibration();

        Assert.That(received.CenterOffset, Is.EqualTo(new Vector2(0.08f, -0.04f)));
        Assert.That(calibrator.FrameVisible, Is.False);
    }

    [Test]
    public void AnchorMovement_ChangesFrameWorldPoseButPreservesAnchorLocalData()
    {
        var calibrator = CreateCalibrator();
        calibrator.Begin(m_Anchor.transform, DefaultData(), DefaultSettings());
        calibrator.MoveFrameToAnchorPlane(new Vector2(0.06f, 0.02f));
        var expectedLocalData = calibrator.CurrentData;

        m_Anchor.transform.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 45f, 0f));
        calibrator.SyncFrameNow();

        Assert.That(calibrator.CurrentData.Width, Is.EqualTo(expectedLocalData.Width));
        Assert.That(calibrator.CurrentData.Height, Is.EqualTo(expectedLocalData.Height));
        Assert.That(calibrator.CurrentData.CenterOffset, Is.EqualTo(expectedLocalData.CenterOffset));
        Assert.That(calibrator.FrameRoot.position, Is.EqualTo(m_Anchor.transform.TransformPoint(new Vector3(0.06f, -.05f, 0.02f))));
    }

    [Test]
    public void Reset_RestoresDefaultsWithoutChangingAnchor()
    {
        var calibrator = CreateCalibrator();
        var settings = DefaultSettings();
        calibrator.Begin(m_Anchor.transform, DefaultData(), settings);
        calibrator.MoveFrameToAnchorPlane(new Vector2(0.2f, 0.2f));
        calibrator.ResizeFromHandle(CalibrationHandleKind.Right, new Vector2(0.4f, 0f));

        calibrator.ResetCalibration();

        Assert.That(calibrator.CurrentData.Width, Is.EqualTo(settings.DefaultWidth));
        Assert.That(calibrator.CurrentData.Height, Is.EqualTo(settings.DefaultHeight));
        Assert.That(calibrator.CurrentData.CenterOffset, Is.EqualTo(settings.DefaultCenterOffset));
        Assert.That(calibrator.Anchor, Is.SameAs(m_Anchor.transform));
    }

    [Test]
    public void Begin_CreatesVisibleHandlesAndCanvasActionLabelsAtRequestedSize()
    {
        var calibrator = CreateCalibrator();
        calibrator.Begin(m_Anchor.transform, DefaultData(), DefaultSettings());

        Assert.That(calibrator.FrameRoot.Find("Top Left Corner Handle")?.GetComponent<Renderer>()?.enabled, Is.True);
        var confirm = calibrator.FrameRoot.Find("Calibration Confirm Button/Button Visual/Label")
            ?.GetComponent<TextMeshProUGUI>();
        var reset = calibrator.FrameRoot.Find("Calibration Reset Button/Button Visual/Label")
            ?.GetComponent<TextMeshProUGUI>();
        Assert.That(confirm, Is.Not.Null);
        Assert.That(reset, Is.Not.Null);
        Assert.That(confirm.fontSize, Is.EqualTo(18f));
        Assert.That(reset.fontSize, Is.EqualTo(18f));
    }

    [Test]
    public void DefaultCalibration_UsesHorizontalImagePlaneOrientation()
    {
        var data = DefaultData();

        Assert.That(Mathf.Abs(Vector3.Dot(data.LocalOrientation * Vector3.forward, Vector3.up)),
            Is.GreaterThan(0.999f));
    }

    SensoryBagCalibrator CreateCalibrator()
    {
        m_Anchor = new GameObject("Image Anchor");
        m_CalibratorObject = new GameObject("Sensory Bag Calibrator");
        return m_CalibratorObject.AddComponent<SensoryBagCalibrator>();
    }

    static SensoryBagCalibrationData DefaultData() => SensoryBagCalibrationData.CreateDefault(
        "CandyWorldPhoneTracking", SensoryBagWorldKind.Candy, DefaultSettings());

    static SensoryBagCalibrationSettings DefaultSettings() => new(
        defaultWidth: 0.25f,
        defaultHeight: 0.17f,
        defaultDepth: 0.05f,
        defaultCenterOffset: Vector2.zero,
        minWidth: 0.08f,
        maxWidth: 0.6f,
        minHeight: 0.06f,
        maxHeight: 0.45f);
}
