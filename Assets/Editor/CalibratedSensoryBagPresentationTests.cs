using Moodium.Calibration;
using NUnit.Framework;
using UnityEngine;

public sealed class CalibratedSensoryBagPresentationTests
{
    GameObject m_Anchor;
    GameObject m_Root;
    GameObject m_VisualPrefab;

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(m_VisualPrefab);
        Object.DestroyImmediate(m_Root);
        Object.DestroyImmediate(m_Anchor);
    }

    [Test]
    public void Presentation_UsesCalibrationFrameOrientation_ForTwoDimensionalFit()
    {
        m_Anchor = new GameObject("Image Anchor");
        m_Anchor.transform.rotation = Quaternion.Euler(20f, 35f, 10f);
        m_Root = new GameObject("Calibrated Presentation");
        m_VisualPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var presentation = m_Root.AddComponent<CalibratedSensoryBagPresentation>();
        var data = new SensoryBagCalibrationData
        {
            Width = .25f,
            Height = .17f,
            Depth = .05f,
            LocalOrientation = Quaternion.Euler(90f, 0f, 0f)
        };

        presentation.Configure(m_Anchor.transform, data, m_VisualPrefab);

        Assert.That(Quaternion.Angle(
                presentation.transform.rotation,
                m_Anchor.transform.rotation * data.LocalOrientation),
            Is.LessThan(.01f));
    }

    [Test]
    public void VisualRoot_FollowsStickerOrientation_WhileFittingBagXZ()
    {
        m_Anchor = new GameObject("Rotated image anchor");
        m_Anchor.transform.rotation = Quaternion.Euler(35f, 70f, 15f);
        m_Root = new GameObject("Calibrated Presentation");
        m_VisualPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var presentation = m_Root.AddComponent<CalibratedSensoryBagPresentation>();
        var data = new SensoryBagCalibrationData
        {
            Width = .25f,
            Height = .17f,
            Depth = .05f,
            LocalOrientation = Quaternion.Euler(90f, 0f, 0f)
        };

        presentation.Configure(m_Anchor.transform, data, m_VisualPrefab);

        Assert.That(Quaternion.Angle(presentation.VisualRoot.rotation, m_Anchor.transform.rotation), Is.LessThan(.01f));
        Assert.That(Quaternion.Angle(
                presentation.VisualRoot.localRotation,
                Quaternion.Inverse(data.LocalOrientation)),
            Is.LessThan(.01f));
        var before = presentation.VisualRoot.rotation;
        m_Anchor.transform.rotation = Quaternion.AngleAxis(30f, Vector3.forward) * before;
        presentation.SyncNow();
        Assert.That(Quaternion.Angle(before, presentation.VisualRoot.rotation), Is.EqualTo(30f).Within(.01f));
        Assert.That(Quaternion.Angle(presentation.VisualRoot.rotation, m_Anchor.transform.rotation), Is.LessThan(.01f));
        Assert.That(presentation.VisualRoot.localScale.x, Is.EqualTo(data.Width).Within(.0001f));
        Assert.That(presentation.VisualRoot.localScale.y, Is.EqualTo(Mathf.Sqrt(data.Width*data.Height)).Within(.0001f));
        Assert.That(presentation.VisualRoot.localScale.z, Is.EqualTo(data.Height).Within(.0001f));
    }

    [Test]
    public void VisualFit_MapsFrameWidthAndHeightOntoPrefabTablePlaneXAndZ()
    {
        var scale = CalibratedSensoryBagPresentation.CalculateTablePlaneFitScale(
            calibratedWidth: .30f,
            calibratedHeight: .18f,
            prefabBounds: new Bounds(Vector3.zero, new Vector3(2f, .40f, 1f)));

        Assert.That(scale.x, Is.EqualTo(.15f).Within(.0001f));
        Assert.That(scale.y, Is.EqualTo(Mathf.Sqrt(.15f*.18f)).Within(.0001f));
        Assert.That(scale.z, Is.EqualTo(.18f).Within(.0001f));
    }

    [Test]
    public void VisualFit_ScalesThicknessFromTheOverallTablePlaneFit()
    {
        var scale = CalibratedSensoryBagPresentation.CalculateTablePlaneFitScale(
            calibratedWidth: .40f,
            calibratedHeight: .10f,
            prefabBounds: new Bounds(Vector3.zero, new Vector3(2f, .25f, 1f)));

        // X = .20 and Z = .10. Their geometric mean is the overall bag-size
        // change, so Y must no longer remain at a fixed 1.0 thickness.
        Assert.That(scale.y, Is.EqualTo(Mathf.Sqrt(.02f)).Within(.0001f));
    }

    [Test]
    public void VisualFit_IncludesThePrefabAuthoredRootScaleInItsReferenceBounds()
    {
        var scale = CalibratedSensoryBagPresentation.CalculateTablePlaneFitScale(
            calibratedWidth: .40f,
            calibratedHeight: .10f,
            prefabBounds: new Bounds(Vector3.zero, new Vector3(2f, .25f, 1f)),
            authoredRootScale: Vector3.one * .5f);

        // A .5 authored prefab must still fill the same calibrated frame, rather
        // than ending up at half the physical bag size.
        Assert.That(scale.x, Is.EqualTo(.40f).Within(.0001f));
        Assert.That(scale.z, Is.EqualTo(.20f).Within(.0001f));
    }
}
