using System;
using System.Reflection;
using Moodium.Calibration;
using Moodium.Flow;
using Moodium.MoodCheck;
using Moodium.Opening;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

public sealed class SensoryBagBranchTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Key = "Moodium.HasSensoryBag";
    bool hadKey;
    int saved;
    GameObject host;
    GameObject anchor;
    MoodiumWorldDefinition world;

    [SetUp] public void Before()
    {
        hadKey = PlayerPrefs.HasKey(Key); saved = PlayerPrefs.GetInt(Key);
        PlayerPrefs.DeleteKey(Key);
        host = new GameObject("Branch Test");
        anchor = new GameObject("Bag Anchor");
    }
    [TearDown] public void After()
    {
        if (host != null) UnityEngine.Object.DestroyImmediate(host);
        if (anchor != null) UnityEngine.Object.DestroyImmediate(anchor);
        if (world != null) UnityEngine.Object.DestroyImmediate(world);
        if (hadKey) PlayerPrefs.SetInt(Key, saved); else PlayerPrefs.DeleteKey(Key);
    }

    [Test] public void OwnershipDefaultsToHasBagAndPersistsBothChoices()
    {
        var type = typeof(StickerWorldEntry).Assembly.GetType("Moodium.Flow.SensoryBagOwnership");
        Assert.That(type, Is.Not.Null, "The ownership branch must have one persistent source of truth.");
        var has = type.GetProperty("HasBag");
        Assert.That(has.GetValue(null), Is.True);
        type.GetMethod("SetHasBag").Invoke(null, new object[] { false });
        Assert.That(PlayerPrefs.GetInt(Key, -1), Is.Zero);
        Assert.That(has.GetValue(null), Is.False);
        type.GetMethod("SetHasBag").Invoke(null, new object[] { true });
        Assert.That(PlayerPrefs.GetInt(Key, -1), Is.EqualTo(1));
    }

    [Test] public void OpeningAnswerIsSavedAtTheActualChoice()
    {
        var choice = host.AddComponent<OpeningVideoHardwareChoice>();
        typeof(OpeningVideoHardwareChoice).GetMethod("SelectFromPress", Private)
            .Invoke(choice, new object[] { false, -1 });
        Assert.That(PlayerPrefs.GetInt(Key, -1), Is.Zero, "Skip after answering must retain that answer.");
    }

    [Test] public void HasBagHardBlocksManualCarouselEvenIfBeginIsCalled()
    {
        SensoryBagOwnership.SetHasBag(true);
        var carousel = host.AddComponent<WorldCarouselSelectionController>();
        carousel.Begin(null);
        Assert.That(carousel.IsSelectionActive, Is.False,
            "Manual world carousel must never be created when sticker selection is required.");
        Assert.That(carousel.CanInteract, Is.False);
    }

    [Test] public void DefaultConfirmationBypassesFrameAndCompletesOnlyOnce()
    {
        var coordinator = host.AddComponent<SensoryBagCalibrationCoordinator>();
        world = ScriptableObject.CreateInstance<MoodiumWorldDefinition>();
        var entry = new StickerWorldEntry(new TrackableId(41, 42), "CandyWorldPhoneTracking", world, anchor.transform, null);
        var method = typeof(SensoryBagCalibrationCoordinator).GetMethod("ConfirmDefault");
        Assert.That(method, Is.Not.Null, "Has-bag entry must not call BeginCalibration.");
        var completed = 0;
        SensoryBagCalibrationData result = default;
        coordinator.CalibrationCompleted += (_, data) => { completed++; result = data; };
        method.Invoke(coordinator, new object[] { entry });
        method.Invoke(coordinator, new object[] { entry });
        Assert.That(completed, Is.EqualTo(1));
        Assert.That(coordinator.ActiveSessionCount, Is.Zero);
        Assert.That(result.Width, Is.EqualTo(.4f));
        Assert.That(result.Height, Is.EqualTo(.25f));
        Assert.That(result.CenterOffset, Is.EqualTo(Vector2.zero));
        Assert.That(result.PlaneLocalOffset, Is.EqualTo(new Vector3(0, -.05f, 0)));
        Assert.That(Quaternion.Angle(result.LocalOrientation, Quaternion.Euler(90, 0, 0)), Is.LessThan(.001f));
        Assert.That(coordinator.GetPresentation(entry.TrackableId).CenterWorldPosition.y, Is.EqualTo(-.05f).Within(.0001f));
    }

    [Test] public void NoBagMoodConfirmationSkipsRecommendationAndCompletesOnce()
    {
        var controller = host.AddComponent<MoodCheckController>();
        var property = typeof(MoodCheckController).GetProperty("ShowRecommendCard");
        Assert.That(property, Is.Not.Null);
        property.SetValue(controller, false);
        controller.BeginPreCheck(new MoodCheckAvailability(true, true), false);
        var count = 0;
        controller.Completed += _ => count++;
        controller.QuickSelect(MoodKind.Tense);
        controller.Confirm(); controller.Confirm();
        Assert.That(controller.State, Is.EqualTo(MoodCheckState.Done));
        Assert.That(controller.IsVisible, Is.False);
        Assert.That(count, Is.EqualTo(1));
    }

    [TestCase("Moodium.Flow.WorldCarouselLayout")]
    [TestCase("Moodium.Calibration.TablePlaneSelector")]
    public void NonTrackedSelectionHasPureLayoutAndTableRules(string typeName)
    {
        Assert.That(typeof(StickerWorldEntry).Assembly.GetType(typeName), Is.Not.Null);
    }
}
