using Moodium.Calibration;
using Moodium.Flow;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

public sealed class SensoryBagCalibrationCoordinatorTests
{
    GameObject m_Host;
    GameObject m_Anchor;

    [TearDown]
    public void TearDown()
    {
        if (m_Host != null) Object.DestroyImmediate(m_Host);
        if (m_Anchor != null) Object.DestroyImmediate(m_Anchor);
    }

    [Test]
    public void PassiveStickerTracking_DoesNotStartCalibration()
    {
        var coordinator = CreateCoordinator();

        var handled = coordinator.HandleImageAnchorUpdate(
            new TrackableId(1, 2), "CandyWorldPhoneTracking", m_Anchor.transform, TrackingState.Tracking);

        Assert.That(handled, Is.False);
        Assert.That(coordinator.ActiveSessionCount, Is.Zero);
    }

    [Test]
    public void TemporaryTrackingLoss_HidesFrameButPreservesCalibrationSession()
    {
        var coordinator = CreateCoordinator();
        var id = new TrackableId(3, 4);
        coordinator.BeginCalibration(CreateEntry(id, "NatureWorldPhoneTracking"));
        coordinator.HandleImageAnchorUpdate(id, "NatureWorldPhoneTracking", m_Anchor.transform, TrackingState.Tracking);
        var calibrator = coordinator.GetActiveCalibrator(id);
        calibrator.MoveFrameToAnchorPlane(new Vector2(0.04f, -0.02f));

        coordinator.HandleImageAnchorUpdate(id, "NatureWorldPhoneTracking", m_Anchor.transform, TrackingState.None);

        Assert.That(coordinator.ActiveSessionCount, Is.EqualTo(1));
        Assert.That(calibrator.FrameVisible, Is.False);
        Assert.That(calibrator.CurrentData.CenterOffset, Is.EqualTo(new Vector2(0.04f, -0.02f)));
    }

    [Test]
    public void VisionOSUuidSuffixedNatureReferenceImage_StartsSelectedCalibration()
    {
        var coordinator = CreateCoordinator();
        var id = new TrackableId(9, 10);

        coordinator.BeginCalibration(CreateEntry(
            id,
            "NatureWorldPhoneTracking_8FEF101E-8126-4652-B006-F4793992AFA9"));

        Assert.That(coordinator.ActiveSessionCount, Is.EqualTo(1));
        Assert.That(coordinator.GetActiveCalibrator(id), Is.Not.Null);
    }

    [Test]
    public void UnknownImage_RemainsAvailableToTheExistingSpawnerFlow()
    {
        var coordinator = CreateCoordinator();

        var handled = coordinator.HandleImageAnchorUpdate(
            new TrackableId(5, 6), "TestImageTracking", m_Anchor.transform, TrackingState.Tracking);

        Assert.That(handled, Is.False);
        Assert.That(coordinator.ActiveSessionCount, Is.Zero);
    }

    SensoryBagCalibrationCoordinator CreateCoordinator()
    {
        m_Host = new GameObject("Calibration Coordinator");
        m_Anchor = new GameObject("Image Anchor");
        return m_Host.AddComponent<SensoryBagCalibrationCoordinator>();
    }

    StickerWorldEntry CreateEntry(TrackableId id, string referenceImageName)
    {
        var world = ScriptableObject.CreateInstance<MoodiumWorldDefinition>();
        return new StickerWorldEntry(id, referenceImageName, world, m_Anchor.transform, null);
    }
}
