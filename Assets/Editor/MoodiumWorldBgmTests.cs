using System.Linq;
using System.Reflection;
using Moodium.Audio;
using Moodium.Flow;
using NUnit.Framework;
using UnityEngine;

public sealed class MoodiumWorldBgmTests
{
    GameObject m_ManagerObject;

    [TearDown]
    public void TearDown()
    {
        if (m_ManagerObject != null)
            Object.DestroyImmediate(m_ManagerObject);
    }

    [Test]
    public void WorldDefinitionExposesBackgroundMusic()
    {
        var property = typeof(MoodiumWorldDefinition).GetProperty(
            "BackgroundMusic",
            BindingFlags.Instance | BindingFlags.Public);

        Assert.That(property, Is.Not.Null);
        Assert.That(property.PropertyType, Is.EqualTo(typeof(AudioClip)));
    }

    [Test]
    public void PlayingBackgroundMusicUsesDedicatedLoopingTwoDimensionalSource()
    {
        var playMethod = typeof(MoodiumAudioManager).GetMethod(
            "PlayBackgroundMusic",
            BindingFlags.Static | BindingFlags.Public);
        Assert.That(playMethod, Is.Not.Null);

        CreateInitializedAudioManager();
        var clip = AudioClip.Create("Test World BGM", 4410, 1, 44100, false);

        playMethod.Invoke(null, new object[] { clip });

        var source = m_ManagerObject.GetComponentsInChildren<AudioSource>()
            .SingleOrDefault(candidate => candidate.gameObject.name == "Moodium Background Music");
        Assert.That(source, Is.Not.Null);
        Assert.That(source.clip, Is.SameAs(clip));
        Assert.That(source.loop, Is.True);
        Assert.That(source.playOnAwake, Is.False);
        Assert.That(source.spatialBlend, Is.Zero);
    }

    [Test]
    public void StoppingBackgroundMusicLeavesEffectSourcesIntact()
    {
        var playMethod = typeof(MoodiumAudioManager).GetMethod(
            "PlayBackgroundMusic",
            BindingFlags.Static | BindingFlags.Public);
        var stopMethod = typeof(MoodiumAudioManager).GetMethod(
            "StopBackgroundMusic",
            BindingFlags.Static | BindingFlags.Public);
        Assert.That(playMethod, Is.Not.Null);
        Assert.That(stopMethod, Is.Not.Null);

        CreateInitializedAudioManager();
        var clip = AudioClip.Create("Test World BGM", 4410, 1, 44100, false);
        playMethod.Invoke(null, new object[] { clip });

        stopMethod.Invoke(null, null);

        var sources = m_ManagerObject.GetComponentsInChildren<AudioSource>();
        var backgroundMusic = sources.Single(source => source.gameObject.name == "Moodium Background Music");
        Assert.That(backgroundMusic.clip, Is.Null);
        Assert.That(sources.Single(source => source.gameObject.name == "Moodium Interface Audio").clip, Is.Null);
        Assert.That(sources.Single(source => source.gameObject.name == "Moodium Spatial Audio").clip, Is.Null);
    }

    [Test]
    public void BackgroundMusicUsesTwoLoopingSourcesForCrossfade()
    {
        CreateInitializedAudioManager();

        var backgroundSources = m_ManagerObject.GetComponentsInChildren<AudioSource>()
            .Where(source => source.gameObject.name.StartsWith("Moodium Background Music"))
            .ToArray();
        Assert.That(backgroundSources.Length, Is.EqualTo(2));
        Assert.That(backgroundSources.All(source => source.loop), Is.True);
        Assert.That(backgroundSources.All(source => source.spatialBlend == 0f), Is.True);
    }

    void CreateInitializedAudioManager()
    {
        m_ManagerObject = new GameObject("Moodium Audio Manager Test");
        var manager = m_ManagerObject.AddComponent<MoodiumAudioManager>();
        if (m_ManagerObject.GetComponentsInChildren<AudioSource>().Length > 0)
            return;

        typeof(MoodiumAudioManager).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(manager, null);
    }
}
