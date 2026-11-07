using Moodium.Calibration;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Calibration UI v2: sprites, layout and live readout. Existing calibration tests stay as they are.</summary>
public sealed class CalibrationUIv2Tests
{
    GameObject m_Anchor;
    GameObject m_Host;
    string m_PreviousLanguage;

    [SetUp]
    public void SetUp() => m_PreviousLanguage = PlayerPrefs.GetString("Moodium.Language", "Chinese");

    [TearDown]
    public void TearDown()
    {
        if (m_Host != null) Object.DestroyImmediate(m_Host);
        if (m_Anchor != null) Object.DestroyImmediate(m_Anchor);
        PlayerPrefs.SetString("Moodium.Language", m_PreviousLanguage);
    }

    static readonly string[] FrameSprites = { "calib_line", "calib_corner", "calib_grip", "calib_drop_line", "calib_plane_ring" };
    static readonly string[] CanvasSprites =
    {
        "calib_elevator_dark", "calib_elevator_light", "calib_card_dark", "calib_btn_primary", "calib_circle_glass",
        "calib_flat_icon_candy", "calib_flat_icon_nature", "calib_icon_reset", "calib_icon_check", "calib_icon_minus",
        "calib_icon_plus", "calib_icon_chevron_up", "calib_icon_chevron_down"
    };

    [Test]
    public void SpritesAreImportedAtRealSize()
    {
        foreach (var name in FrameSprites)
        {
            var sprite = Resources.Load<Sprite>("MoodiumCalibrationUI/" + name);
            Assert.That(sprite, Is.Not.Null, name);
            Assert.That(sprite.pixelsPerUnit, Is.EqualTo(4000f).Within(.5f), $"{name}: reimport the folder");
        }
        foreach (var name in CanvasSprites)
            Assert.That(Resources.Load<Sprite>("MoodiumCalibrationUI/" + name), Is.Not.Null, name);

        var corner = Resources.Load<Sprite>("MoodiumCalibrationUI/calib_corner");
        Assert.That(corner.bounds.size.x, Is.EqualTo(.055f).Within(.0005f), "corner bracket is 55 mm including glow");
        Assert.That(corner.pivot.x, Is.EqualTo(36f).Within(.5f), "pivot sits on the frame corner");
        Assert.That(Resources.Load<Sprite>("MoodiumCalibrationUI/calib_elevator_dark").border.y, Is.EqualTo(48f));
    }

    [Test]
    public void FrameIsDrawnWithSpritesAndEdgesCanBeGrabbedAlongTheirWholeLength()
    {
        var calibrator = Begin("Chinese", SensoryBagWorldKind.Candy);
        var data = calibrator.CurrentData;
        var top = calibrator.FrameRoot.Find("Top Edge Handle");
        Assert.That(top.GetComponent<SpriteRenderer>(), Is.Not.Null);
        var collider = top.GetComponent<BoxCollider>();
        Assert.That(collider.size.x * top.localScale.x, Is.EqualTo(data.Width).Within(.001f));
        Assert.That(calibrator.FrameRoot.Find("Top Edge Grip"), Is.Not.Null);
        var corner = calibrator.FrameRoot.Find("Bottom Right Corner Handle");
        Assert.That(corner.localPosition.x, Is.EqualTo(data.Width * .5f).Within(.0001f));
        Assert.That(corner.localPosition.y, Is.EqualTo(-data.Height * .5f).Within(.0001f));
    }

    [Test]
    public void HeightControlAndTipCardSitOutsideTheFrameAboveTheTable()
    {
        var calibrator = Begin("Chinese", SensoryBagWorldKind.Candy);
        var data = calibrator.CurrentData;
        var elevator = calibrator.FrameRoot.Find("Table Height Drag Handle");
        var card = calibrator.FrameRoot.Find("Calibration Guide Card");
        Assert.That(elevator, Is.Not.Null);
        Assert.That(card, Is.Not.Null);
        Assert.That(calibrator.FrameRoot.Find("Calibration Panel"), Is.Null, "the big v2 panel is gone");
        // The layout follows the viewer (or the frame's own axes without a camera), so check relations, not axes.
        var inPlaneElevator = Vector3.ProjectOnPlane(elevator.localPosition, Vector3.forward);
        var inPlaneCard = Vector3.ProjectOnPlane(card.localPosition, Vector3.forward);
        var half = Mathf.Min(data.Width, data.Height) * .5f;
        Assert.That(inPlaneElevator.magnitude, Is.GreaterThan(half), "height control is outside the frame");
        Assert.That(inPlaneCard.magnitude, Is.GreaterThan(half), "tip card is outside the frame");
        Assert.That(Mathf.Abs(Vector3.Dot(inPlaneElevator.normalized, inPlaneCard.normalized)), Is.LessThan(.5f),
            "height control is to the side, tip card is behind");
        Assert.That(elevator.localPosition.z, Is.LessThan(-.04f), "height control floats above the table (frame local -Z)");
        Assert.That(card.localPosition.z, Is.LessThan(-.1f), "tip card floats above the bag");
        var raise = calibrator.FrameRoot.Find("Calibration Raise Table Button");
        var confirm = calibrator.FrameRoot.Find("Calibration Confirm Button");
        Assert.That(Vector3.Distance(raise.position, elevator.position), Is.LessThan(.05f), "+ sits on the height control");
        Assert.That(Vector3.Distance(confirm.position, card.position), Is.LessThan(.1f), "Confirm sits under the tip card");
    }

    [Test]
    public void HeightControlShowsTheLiveTableHeight()
    {
        var calibrator = Begin("Chinese", SensoryBagWorldKind.Candy);
        var readout = calibrator.FrameRoot.Find("Table Height Drag Handle/Elevator Visual/Table Height Readout").GetComponent<TextMeshProUGUI>();
        var unit = calibrator.FrameRoot.Find("Table Height Drag Handle/Elevator Visual/Unit").GetComponent<TextMeshProUGUI>();
        Assert.That(readout.text, Is.EqualTo("-5.0"));
        Assert.That(unit.text, Is.EqualTo("厘米"));
        calibrator.AdjustPlaneHeight(.005f);
        Assert.That(readout.text, Is.EqualTo("-4.5"));
    }

    [Test]
    public void TipCardUsesTheWorldPictogramAndLanguage()
    {
        var calibrator = Begin("English", SensoryBagWorldKind.Nature);
        var card = calibrator.FrameRoot.Find("Calibration Guide Card/Card Visual");
        Assert.That(card.Find("Title").GetComponent<TextMeshProUGUI>().text, Is.EqualTo("Keep the frame flat on the table"));
        Assert.That(card.Find("Icon").GetComponent<Image>().sprite.name, Is.EqualTo("calib_flat_icon_nature"));
        Assert.That(calibrator.FrameRoot.Find("Calibration Confirm Button/Button Visual/Label").GetComponent<TextMeshProUGUI>().text, Is.EqualTo("Confirm"));
    }

    SensoryBagCalibrator Begin(string language, SensoryBagWorldKind world)
    {
        PlayerPrefs.SetString("Moodium.Language", language);
        m_Anchor = new GameObject("Image Anchor");
        m_Host = new GameObject("Calibrator");
        var calibrator = m_Host.AddComponent<SensoryBagCalibrator>();
        var settings = new SensoryBagCalibrationSettings(.40f, .25f, .05f, Vector2.zero, .08f, .6f, .06f, .45f);
        calibrator.Begin(m_Anchor.transform, SensoryBagCalibrationData.CreateDefault("Test", world, settings), settings);
        return calibrator;
    }
}

