using System.IO;
using Moodium.CandyWorld;
using NUnit.Framework;
using UnityEngine;

/// <summary>Trees and candy houses grow in with a soft magical sound, not the sparkle-explosion clip.</summary>
public sealed class GrowthSoundTests
{
    [TestCase(true)]
    [TestCase(false)]
    public void GrowthSoundShipsForEachWorld(bool nature)
    {
        var clip = Resources.Load<AudioClip>(CandyWorldManager.GrowthSoundResource(nature));
        Assert.That(clip, Is.Not.Null, CandyWorldManager.GrowthSoundResource(nature));
        Assert.That(clip.channels, Is.EqualTo(1), "played as a 3D one-shot at the spawn point");
        Assert.That(clip.length, Is.InRange(1.5f, 3f), "a short rise and tail around the 0.75 s grow-in");
    }

    [Test]
    public void OnlyGrowthUsesTheNewSound()
    {
        var source = File.ReadAllText("Assets/Scripts/Moodium/CandyWorldManager.cs");
        Assert.That(source, Does.Contain("m_NatureForestGrowth.ConfigureSpawnSound(GrowthSound(true));"));
        Assert.That(source, Does.Contain("GrowthSound(false));"), "candy house and little houses");
        Assert.That(source, Does.Contain("m_RainReward.ConfigureTouchAudio(m_RewardTouchSound);"), "rain reward keeps its sound");
        Assert.That(source, Does.Contain("m_BubbleWorld.ConfigureTouchSound(m_RewardTouchSound);"), "bubble touch keeps its sound");
    }
}
