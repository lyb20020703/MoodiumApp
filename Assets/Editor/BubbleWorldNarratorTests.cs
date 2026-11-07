using System.IO;
using Moodium.Characters;
using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;

public sealed class BubbleWorldNarratorTests
{
    [Test]
    public void EachBubbleLayerHasItsOwnIntro()
    {
        Assert.That(BubbleWorldNarrator.IntroId(BubbleWorldData.CandyFolder), Is.EqualTo("W1c"), "marshmallow");
        Assert.That(BubbleWorldNarrator.IntroId(BubbleWorldData.NestedCandyFolder), Is.EqualTo("W2c"), "mint chocolate");
        Assert.That(BubbleWorldNarrator.IntroId(BubbleWorldData.NatureFolder), Is.EqualTo("W1n"), "green forest");
        Assert.That(BubbleWorldNarrator.IntroId(BubbleWorldData.NestedNatureFolder), Is.EqualTo("W2n"), "snowy forest");
        Assert.That(BubbleWorldNarrator.IntroId(BubbleWorldData.MapleNatureFolder), Is.EqualTo("W3n"), "maple forest");
        Assert.That(BubbleWorldNarrator.IntroId("Somewhere/"), Is.Null);
    }

    [Test]
    public void IntroClipsShipInBothLanguages()
    {
        foreach (var id in BubbleWorldNarrator.AllIds)
        foreach (var lang in new[] { "zh", "en" })
        {
            var clip = Resources.Load<AudioClip>(MoodiVoice.ClipResourcePath(id, lang));
            Assert.That(clip, Is.Not.Null, $"{id} {lang}");
            Assert.That(clip.channels, Is.EqualTo(1), $"{id} {lang}");
            Assert.That(clip.length, Is.InRange(10f, 35f), $"{id} {lang}: a short spoken welcome");
        }
    }

    [Test]
    public void IntroIsNotOneOfMoodisShortLines()
    {
        // The intros are 20-30 s and play 2D; MoodiVoice's 0.4-6 s line checks must not include them.
        foreach (var id in BubbleWorldNarrator.AllIds) Assert.That(MoodiVoice.AllIds, Does.Not.Contain(id));
    }

    [Test]
    public void BubbleLayersAnnounceThemselvesAndTheManagerNarrates()
    {
        var controller = File.ReadAllText("Assets/Scripts/Moodium/BubbleWorld/BubbleWorldController.cs");
        Assert.That(controller, Does.Contain("LayerRevealed?.Invoke(m_Folder)"));
        Assert.That(controller, Does.Contain("m_Nested.LayerRevealed += folder => LayerRevealed?.Invoke(folder)"), "nested layer forwarded");
        var manager = File.ReadAllText("Assets/Scripts/Moodium/CandyWorldManager.cs");
        Assert.That(manager, Does.Contain("m_BubbleWorld.LayerRevealed += OnBubbleLayerRevealed"));
        Assert.That(manager, Does.Contain("m_BubbleWorld.LayerRevealed -= OnBubbleLayerRevealed"));
        Assert.That(manager, Does.Not.Contain("Say(\"F3\")"), "The user removed the former F3 welcome");
    }

    [Test]
    public void NarratorPlaysEachLayerOnceAndIsNotSpatial()
    {
        var go = new GameObject("Narrator");
        try
        {
            var narrator = go.AddComponent<BubbleWorldNarrator>();
            Assert.That(narrator.PlayIntro(BubbleWorldData.CandyFolder), Is.True);
            Assert.That(narrator.IsSpeaking, Is.True);
            Assert.That(narrator.PlayIntro(BubbleWorldData.CandyFolder), Is.False, "once per session");
            narrator.Stop();
            Assert.That(narrator.IsSpeaking, Is.False);
            narrator.ResetSession();
            Assert.That(narrator.PlayIntro(BubbleWorldData.CandyFolder), Is.True, "new session");
        }
        finally { Object.DestroyImmediate(go); }
    }
}
