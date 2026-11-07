using Moodium.Characters;
using NUnit.Framework;
using UnityEngine;

/// <summary>Moodi voice: every line has a zh and an en clip, and teaching lines play only once.</summary>
public sealed class MoodiVoiceTests
{
    GameObject m_Host;
    string m_PreviousLanguage;

    [SetUp]
    public void SetUp()
    {
        m_PreviousLanguage = PlayerPrefs.GetString("Moodium.Language", "Chinese");
        MoodiVoice.ResetSession();
    }

    [TearDown]
    public void TearDown()
    {
        if (m_Host != null) Object.DestroyImmediate(m_Host);
        PlayerPrefs.SetString("Moodium.Language", m_PreviousLanguage);
        MoodiVoice.ResetSession();
    }

    [Test]
    public void EveryLineHasAChineseAndAnEnglishClip()
    {
        Assert.That(MoodiVoice.AllIds.Length, Is.EqualTo(48));
        foreach (var language in new[] { "zh", "en" })
        foreach (var id in MoodiVoice.AllIds)
        {
            var path = MoodiVoice.ClipResourcePath(id, language);
            var clip = Resources.Load<AudioClip>(path);
            Assert.That(clip, Is.Not.Null, path);
            Assert.That(clip.length, Is.InRange(.4f, 6f), path);
            Assert.That(clip.channels, Is.EqualTo(1), path + ": reimport the folder");
        }
    }

    [Test]
    public void FinaleTeachingAndCooldownMatchTheJourney()
    {
        foreach (var id in new[] { "F1", "F1t", "F2", "F3", "F4", "F5", "F6", "F7" })
        {
            Assert.That(System.Array.IndexOf(MoodiVoice.AllIds, id), Is.GreaterThanOrEqualTo(0));
            Assert.That(MoodiVoice.IsTeaching(id), Is.EqualTo(id == "F1" || id == "F1t" || id == "F2" || id == "F4"));
        }
        var cooldowns = (System.Collections.Generic.Dictionary<string, float>)typeof(MoodiVoice)
            .GetField("Cooldowns", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
        Assert.That(cooldowns["F5"], Is.EqualTo(20f));
    }

    [Test]
    public void LanguageFollowsTheAppSetting()
    {
        PlayerPrefs.SetString("Moodium.Language", "English");
        Assert.That(MoodiVoice.LanguageCode, Is.EqualTo("en"));
        PlayerPrefs.SetString("Moodium.Language", "Chinese");
        Assert.That(MoodiVoice.LanguageCode, Is.EqualTo("zh"));
    }

    [Test]
    public void TeachingLinesAreSaidOnlyOnceAndNotAfterTheUserLearned()
    {
        m_Host = new GameObject("Moodi");
        var voice = m_Host.AddComponent<MoodiVoice>();
        Assert.That(MoodiVoice.IsTeaching("A2"), Is.True);
        Assert.That(MoodiVoice.IsTeaching("B2"), Is.False);
        Assert.That(voice.Say("A2"), Is.True);
        voice.Stop();
        Assert.That(voice.Say("A2"), Is.False, "teaching line repeats");
        MoodiVoice.MarkLearned("C1");
        Assert.That(voice.Say("C1"), Is.False, "learned teaching line still plays");
    }

    [Test]
    public void StickerVoiceUsesSessionLanguageInsteadOfPlayerPreferences()
    {
        m_Host=new GameObject("Sticker Moodi");
        var voice=m_Host.AddComponent<MoodiVoice>();
        PlayerPrefs.SetString("Moodium.Language","Chinese");
        voice.EnglishOverride=true;
        var load=typeof(MoodiVoice).GetMethod("Load",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        Assert.That(((AudioClip)load.Invoke(voice,new object[]{"S1c"})).name,Is.EqualTo("moodi_vo_S1c_en"));
        voice.EnglishOverride=false;
        Assert.That(((AudioClip)load.Invoke(voice,new object[]{"S1n"})).name,Is.EqualTo("moodi_vo_S1n_zh"));
    }

    [Test]
    public void HidingOrStoppingMoodiCancelsItsDelayedInvitation()
    {
        m_Host=new GameObject("Sticker Moodi");var voice=m_Host.AddComponent<MoodiVoice>();
        typeof(MoodiVoice).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(voice,null);
        voice.SayAfter("S1c",0f);m_Host.SetActive(false);
        // Ordinary MonoBehaviours do not receive OnDisable from EditMode tests.
        typeof(MoodiVoice).GetMethod("OnDisable",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(voice,null);
        m_Host.SetActive(true);
        var update=typeof(MoodiVoice).GetMethod("Update",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        update.Invoke(voice,null);
        Assert.That(m_Host.GetComponent<AudioSource>().clip,Is.Null,"Hidden entry must discard the old invitation.");
        voice.SayAfter("S1n",0f);voice.Stop();update.Invoke(voice,null);
        Assert.That(m_Host.GetComponent<AudioSource>().clip,Is.Null,"Stop must cancel delayed speech too.");
    }

    [Test]
    public void SqueezeRescueLinesExistAndOnlyK3Teaches()
    {
        foreach (var id in new[] { "K1", "K2", "K3", "K4", "K5", "K6", "K7", "K8a", "K8b", "K8c", "K9", "K10" })
        {
            Assert.That(System.Array.IndexOf(MoodiVoice.AllIds, id), Is.GreaterThanOrEqualTo(0), id);
            Assert.That(MoodiVoice.IsTeaching(id), Is.EqualTo(id == "K3"), id);
        }
    }

    [Test]
    public void MuffleGoesFromDeepInsideToClear()
    {
        Assert.That(MoodiVoice.MuffleCutoff(1f), Is.EqualTo(1400f).Within(1f));
        Assert.That(MoodiVoice.MuffleCutoff(0f), Is.EqualTo(22000f).Within(1f));
        Assert.That(MoodiVoice.MuffleCutoff(.5f), Is.LessThan(MoodiVoice.MuffleCutoff(.2f)));
    }

    [Test]
    public void RescueTeachingWaitIncludesQueuedSpeechAndStopClearsIt()
    {
        m_Host = new GameObject("Rescue Moodi");
        var voice = m_Host.AddComponent<MoodiVoice>();
        typeof(MoodiVoice).GetField("m_SpeechEndsAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .SetValue(voice, Time.time);
        Assert.That(voice.Say("K3"), Is.True);
        Assert.That(voice.HasPendingSpeech, Is.True, "The tutorial must wait for the queued request.");
        voice.Stop();
        Assert.That(voice.HasPendingSpeech, Is.False);
    }
}
