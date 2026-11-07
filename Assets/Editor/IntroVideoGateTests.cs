using Moodium.Opening;
using NUnit.Framework;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

public sealed class IntroVideoGateTests
{
    [Test]
    public void Gate_LoopsPreviewUntilVideoIsExplicitlyActivated()
    {
        var gate = new IntroVideoGate(3d);

        Assert.That(gate.ShouldLoopAt(3d), Is.True);
        Assert.That(gate.ShouldLoopAt(4.2d), Is.True);
    }

    [Test]
    public void Gate_ContinuesFromPreviewEndAfterActivation()
    {
        var gate = new IntroVideoGate(3d);

        gate.Activate();

        Assert.That(gate.ShouldLoopAt(3d), Is.False);
        Assert.That(gate.ResumeTime, Is.EqualTo(3d));
    }

    [Test]
    public void Gate_BeginsOnlyOnePreviewSeekUntilThatSeekCompletes()
    {
        var gate = new IntroVideoGate(2d);

        Assert.That(gate.TryBeginPreviewLoop(2d), Is.True);
        Assert.That(gate.TryBeginPreviewLoop(2d), Is.False);

        Assert.That(gate.TryCompletePreviewSeek(2d), Is.False);
        Assert.That(gate.TryCompletePreviewSeek(0.05d), Is.True);

        Assert.That(gate.TryBeginPreviewLoop(2d), Is.True);
    }

    [Test]
    public void Gate_CanLoopAnArbitrarySegmentAndResumeAfterItsEnd()
    {
        var gate = new IntroVideoGate(24d, 32d);

        Assert.That(gate.ShouldLoopAt(32d), Is.True);
        Assert.That(gate.TryBeginPreviewLoop(32d), Is.True);
        Assert.That(gate.TryCompletePreviewSeek(24.01d), Is.True);

        gate.Activate();

        Assert.That(gate.ResumeTime, Is.EqualTo(32d));
        Assert.That(gate.ShouldLoopAt(32d), Is.False);
    }

    [Test]
    public void Gate_KeepsWaitingWhenVideoPlayerReachesEndWithoutSelection()
    {
        var gate = new IntroVideoGate(9.04d, 15.03d);

        Assert.That(gate.ShouldKeepSelectionPromptVisible(false), Is.True);
        gate.Activate();
        Assert.That(gate.ShouldKeepSelectionPromptVisible(false), Is.False);
    }

    [Test]
    public void Gate_KeepsCoroutineAliveWhenSelectionArrivesBetweenFrames()
    {
        var gate = new IntroVideoGate(24.14d, 32.09d);

        Assert.That(gate.ShouldKeepSelectionPromptVisible(true), Is.True,
            "The coroutine must get one more iteration to consume a button selection and start the tutorial branch.");

        gate.Activate();
        Assert.That(gate.ShouldKeepSelectionPromptVisible(true), Is.False);
    }

    [Test]
    public void TutorialFlowGate_DoesNotAllowWorldSelectionWhenTutorialFailsToStart()
    {
        var gate = new OpeningTutorialFlowGate();

        gate.MarkTutorialFailed();

        Assert.That(gate.CanEnterWorldSelection, Is.False);
    }

    [Test]
    public void TutorialFlowGate_BlocksWorldSelectionUntilTutorialCompletes()
    {
        var gate = new OpeningTutorialFlowGate();

        Assert.That(gate.CanEnterWorldSelection, Is.False);
    }

    [Test]
    public void TutorialSwitch_DoesNotClearThePlayerClipWhileListeningForErrors()
    {
        var source = File.ReadAllText("Assets/Scripts/Moodium/Opening/OpeningManager.cs");

        StringAssert.DoesNotContain("player.clip = null;", source,
            "Clearing the active clip can emit a native visionOS VideoPlayer error and falsely fail the tutorial handoff.");
    }

    [Test]
    public void TutorialSwitch_DoesNotUseRuntimeFrameMetadataToRejectTheTutorial()
    {
        var source = File.ReadAllText("Assets/Scripts/Moodium/Opening/OpeningManager.cs");

        StringAssert.DoesNotContain("ShouldBlockTutorialForRuntimeMetadata", source,
            "visionOS can report transient VideoPlayer metadata after a seek; it must not reject a tutorial that is already playing.");
    }

    [Test]
    public void TutorialFailureNotice_UsesTheVisionOSDiagnosticRevision()
    {
        var source = File.ReadAllText("Assets/Scripts/Moodium/Opening/OpeningManager.cs");

        StringAssert.Contains("VP-20260924-02", source);
    }

    [Test]
    public void SpatialChoice_UsesTheSameNextFrameTransitionAsAutomaticChoice()
    {
        var gate = new OpeningVideoSpatialChoiceGate();

        gate.SelectFromSpatialTouch(frame: 100, touchId: 17);

        Assert.That(gate.IsReadyForVideoTransition(frame: 100, selectingTouchIsActive: true), Is.False);
        Assert.That(gate.IsReadyForVideoTransition(frame: 101, selectingTouchIsActive: true), Is.True,
            "A Vision Pro spatial pointer can remain active after the press event; it must not hold the video flow hostage.");
    }

    [Test]
    public void AutomaticChoice_TransitionsOnTheNextFrameWithoutAHandRelease()
    {
        var gate = new OpeningVideoSpatialChoiceGate();

        gate.SelectAutomatically(frame: 100);

        Assert.That(gate.IsReadyForVideoTransition(frame: 100, selectingTouchIsActive: false), Is.False);
        Assert.That(gate.IsReadyForVideoTransition(frame: 101, selectingTouchIsActive: false), Is.True);
    }

    [Test]
    public void OpeningRendering_DoesNotRunInTheBackground()
    {
        var projectSettings = File.ReadAllText("ProjectSettings/ProjectSettings.asset");
        var updater = File.ReadAllText("Assets/Scripts/Moodium/Opening/PolySpatialVideoRenderTextureUpdater.cs");

        StringAssert.Contains("runInBackground: 0", projectSettings);
        StringAssert.Contains("Application.isFocused", updater);
    }

    [Test]
    public void LanguageChoices_SupportMouseAndSpatialPointerThroughTheSameButton()
    {
        var root = new GameObject("Language Choice Test", typeof(RectTransform));
        try
        {
            var choice = root.AddComponent<OpeningVideoLanguageChoice>();
            choice.Configure(null, null);
            var button = root.transform.Find("Language Button - Chinese");

            Assert.That(button, Is.Not.Null);
            Assert.That(button.GetComponent<Button>(), Is.Not.Null,
                "Editor mouse clicks require the Unity UI Button path.");
            Assert.That(button.GetComponent<OpeningVideoSpatialButton>(), Is.Not.Null,
                "Vision Pro eye/hand input requires the shared spatial button path.");
            Assert.That(button.GetComponent<BoxCollider>().isTrigger, Is.False,
                "The PolySpatial sample uses a solid collider on the interactive object.");
            Assert.That(root.GetComponent<OpeningVideoSpatialUIInputManager>(), Is.Not.Null);

            button.GetComponent<Button>().onClick.Invoke();
            Assert.That(choice.HasSelection, Is.True);
            Assert.That(choice.SelectedLanguage, Is.EqualTo("中文"));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void HardwareChoices_UseTheSameHybridButtonImplementation()
    {
        var root = new GameObject("Hardware Choice Test", typeof(RectTransform));
        try
        {
            var choice = root.AddComponent<OpeningVideoHardwareChoice>();
            choice.Configure(null, null, false);
            var button = root.transform.Find("Hardware Button - Yes");

            Assert.That(button, Is.Not.Null);
            Assert.That(button.GetComponent<Button>(), Is.Not.Null);
            Assert.That(button.GetComponent<OpeningVideoSpatialButton>(), Is.Not.Null);
            Assert.That(button.GetComponent<BoxCollider>().isTrigger, Is.False);
            Assert.That(root.GetComponent<OpeningVideoSpatialUIInputManager>(), Is.Not.Null);

            button.GetComponent<Button>().onClick.Invoke();
            Assert.That(choice.HasSelection, Is.True);
            Assert.That(choice.HasHardware, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void TutorialFlowGate_AllowsWorldSelectionOnlyAfterTutorialCompletes()
    {
        var gate = new OpeningTutorialFlowGate();

        gate.MarkTutorialCompleted();

        Assert.That(gate.CanEnterWorldSelection, Is.True);
    }

    [Test]
    public void HandGuideLayout_PlacesHandsSymmetricallyOnVideoSides()
    {
        var layout = new IntroVideoHandGuideLayout();

        Assert.That(layout.LeftLocalPosition.x, Is.LessThan(0f));
        Assert.That(layout.RightLocalPosition.x, Is.GreaterThan(0f));
        Assert.That(layout.LeftLocalPosition.x, Is.EqualTo(-layout.RightLocalPosition.x).Within(0.001f));
        Assert.That(layout.LeftLocalPosition.y, Is.EqualTo(layout.RightLocalPosition.y).Within(0.001f));
    }
}
