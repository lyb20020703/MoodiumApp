using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using Moodium.Opening;

public sealed class MoodiumOpeningPersistenceTests
{
    const string PrefabPath = "Assets/prefab/MoodiumOpening/MoodiumOpening.prefab";
    const string SequenceConfigPath = "Assets/Resources/MoodiumOpening/OpeningVideoSequenceConfig.asset";

    [Test]
    public void OpeningPrefab_KeepsAllRuntimeIntroAssetsAssigned()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(prefab, Is.Not.Null);

        var manager = prefab.GetComponent<Moodium.Opening.OpeningManager>();
        Assert.That(manager, Is.Not.Null);
        var serialized = new SerializedObject(manager);

        var sequenceConfig = serialized.FindProperty("m_VideoSequenceConfig").objectReferenceValue;
        Assert.That(sequenceConfig, Is.Not.Null, "The intro sequence configuration must survive editor restarts.");
        Assert.That(AssetDatabase.GetAssetPath(sequenceConfig), Is.EqualTo(SequenceConfigPath));

        var sequence = AssetDatabase.LoadAssetAtPath<Moodium.Opening.OpeningVideoSequenceConfig>(SequenceConfigPath);
        Assert.That(sequence, Is.Not.Null);
        Assert.That(AssetDatabase.GetAssetPath(sequence.VideoClip), Is.EqualTo("Assets/Video/VideoPLUS.mp4"));
        Assert.That(sequence.VideoClip.frameRate, Is.EqualTo(OpeningVideoTimeline.FramesPerSecond).Within(0.001d));
        Assert.That(sequence.VideoClip.frameCount, Is.GreaterThan((ulong)OpeningVideoTimeline.English.No.EndFrame),
            "VideoPLUS must contain the final English 'No' segment through 02:10:29.");
        Assert.That(sequence.IntroLoopStartTime, Is.EqualTo(0f));
        Assert.That(sequence.IntroLoopEndTime, Is.EqualTo(3.08f).Within(0.001f));
        Assert.That(sequence.LanguageLoopStartTime,
            Is.EqualTo(OpeningVideoTimeline.FrameToSeconds(OpeningVideoTimeline.LanguageLoop.StartFrame)).Within(0.0001d));
        Assert.That(sequence.LanguageLoopEndTime,
            Is.EqualTo(OpeningVideoTimeline.FrameToSeconds(OpeningVideoTimeline.LanguageLoop.EndFrame)).Within(0.0001d));
        Assert.That(sequence.DistanceFromUser, Is.EqualTo(1.35f).Within(0.001f),
            "The video must use the same eye-relative distance as the portal window.");
        Assert.That(sequence.HeightOffset, Is.EqualTo(-0.05f).Within(0.001f),
            "The video must use the same eye-relative height as the portal window.");
        Assert.That(sequence.WorldPositionY, Is.EqualTo(1f).Within(0.001f),
            "The runtime-created opening video display must use the requested world Pos Y.");
        Assert.That(sequence.ChineseTutorialClip, Is.SameAs(sequence.VideoClip));
        Assert.That(sequence.EnglishTutorialClip, Is.SameAs(sequence.VideoClip));

        var previewDuration = serialized.FindProperty("m_InteractiveVideoPreviewDuration");
        Assert.That(previewDuration, Is.Not.Null,
            "The intro video needs a persisted preview duration so editor restarts cannot restore an old loop length.");
        Assert.That(previewDuration.floatValue, Is.EqualTo(3.08f).Within(0.001f));
        Assert.That(serialized.FindProperty("m_LeftHandGuidePrefab").objectReferenceValue,
            Is.TypeOf<GameObject>(), "The left-hand guide prefab must remain assigned.");
        Assert.That(serialized.FindProperty("m_RightHandGuidePrefab").objectReferenceValue,
            Is.TypeOf<GameObject>(), "The right-hand guide prefab must remain assigned.");
        Assert.That(serialized.FindProperty("m_HandPromptFont").objectReferenceValue,
            Is.TypeOf<TMP_FontAsset>(), "The Chinese subtitle font must remain assigned.");
    }
}
