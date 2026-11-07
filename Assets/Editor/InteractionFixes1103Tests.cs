using System.IO;
using System.Reflection;
using Artngame.TreeGEN.ProceduralIvy;
using Moodium.Characters;
using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Interaction fixes (2026-11-03 review, items 1 / 5 / 8 / 9 / 10):
/// 1 the vine debug dots are off for users; 5 leaving mid-rescue no longer leaves Moodi's voice muffled;
/// 8 a world's intro counts as said only once heard; 9 teaching lines are taught when they start, expire while waiting,
/// and are cancelled by MarkLearned; 10 the open door pauses the vines too.
/// </summary>
public sealed class InteractionFixes1103Tests
{
    const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
    const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
    GameObject m_Host;

    [SetUp]
    public void SetUp() => MoodiVoice.ResetSession();

    [TearDown]
    public void TearDown()
    {
        typeof(MoodiVoice).GetField("s_BusyUntil", Static).SetValue(null, -10f);
        if (m_Host != null)
        {
            var voice = m_Host.GetComponent<MoodiVoice>();
            if (voice != null) voice.Stop();
            Object.DestroyImmediate(m_Host);
        }
        MoodiVoice.ClearMuffle();
        MoodiVoice.ResetSession();
    }

    // ---------- 1 ----------

    [Test]
    public void VineDebugDotsAreOffForUsers()
    {
        m_Host = new GameObject("Vines");
        Assert.That(m_Host.AddComponent<NatureIvySpatialGrowthController>().DiagnosticMarkers, Is.False);
    }

    // ---------- 5 ----------

    [Test]
    public void LeavingTheRescueUnmufflesMoodi()
    {
        m_Host = new GameObject("Rescue Moodi");
        var voice = m_Host.AddComponent<MoodiVoice>();
        voice.Muffle(1f);
        Assert.That(MoodiVoice.IsMuffled, Is.True);
        MoodiVoice.ClearMuffle();
        Assert.That(MoodiVoice.IsMuffled, Is.False);
        voice.Muffle(.5f);
        Assert.That(MoodiVoice.IsMuffled, Is.True, "muffling still works after a clear");
        var director = File.ReadAllText("Assets/Scripts/Moodium/Reality/MoodiRescueDirector.cs");
        var stop = director.Substring(director.IndexOf("public void StopAndClear()", System.StringComparison.Ordinal));
        stop = stop.Substring(0, stop.IndexOf("void OnDisable", System.StringComparison.Ordinal));
        Assert.That(stop, Does.Contain("MoodiVoice.ClearMuffle();"), "Exit before the rescue goes through StopAndClear");
    }

    // ---------- 8 ----------

    [Test]
    public void AnIntroCountsAsSaidOnlyOnceHeard()
    {
        Assert.That(BubbleWorldNarrator.CountsAsHeard(16f, 20f), Is.True);
        Assert.That(BubbleWorldNarrator.CountsAsHeard(16f * .99f, 20f), Is.False);
        Assert.That(BubbleWorldNarrator.CountsAsHeard(0f, 0f), Is.False);

        m_Host = new GameObject("Narrator");
        var narrator = m_Host.AddComponent<BubbleWorldNarrator>();
        var clip = Resources.Load<AudioClip>(MoodiVoice.ClipResourcePath("W1n", "zh"));
        Assert.That(clip, Is.Not.Null);
        typeof(BubbleWorldNarrator).GetField("m_Pending", Instance).SetValue(narrator, "W1n");
        typeof(BubbleWorldNarrator).GetField("m_PendingClip", Instance).SetValue(narrator, clip);
        Assert.That(narrator.PlayIntro(BubbleWorldData.NatureFolder), Is.False, "already scheduled: not twice");
        narrator.Stop();   // stepped back out during the 1 s delay
        Assert.That(narrator.Pending, Is.Null);
        Assert.That(narrator.HasSaid("W1n"), Is.False, "the welcome plays again next time");
    }

    // ---------- 9 ----------

    MoodiVoice BusyVoice()
    {
        m_Host = new GameObject("Moodi");
        var voice = m_Host.AddComponent<MoodiVoice>();
        typeof(MoodiVoice).GetMethod("Awake", Instance).Invoke(voice, null);
        // Someone is talking for a while: every request waits in the queue.
        typeof(MoodiVoice).GetField("s_BusyUntil", Static).SetValue(null, Time.unscaledTime + 60f);
        return voice;
    }

    [Test]
    public void MarkLearnedCancelsAWaitingTip()
    {
        var voice = BusyVoice();
        Assert.That(voice.Say("C6"), Is.True, "queued behind the current line");
        Assert.That(MoodiVoice.IsQueued("C6"), Is.True);
        Assert.That(voice.Say("C6"), Is.False, "not queued twice");
        MoodiVoice.MarkLearned("C6");   // the user turned the hand over: Moodi flew off
        Assert.That(MoodiVoice.IsQueued("C6"), Is.False, "the tip must not play after Moodi has left");
        Assert.That(voice.Say("C6"), Is.False);
    }

    [Test]
    public void ATipThatNeverStartedIsNotLost()
    {
        var voice = BusyVoice();
        Assert.That(voice.Say("A3"), Is.True);
        voice.Stop();   // Moodi hidden / stopped: the waiting tip is dropped before it started
        Assert.That(MoodiVoice.IsQueued("A3"), Is.False);
        Assert.That(voice.Say("A3"), Is.True, "it can still be said later in the session");
    }

    [Test]
    public void WaitingTipsExpire()
    {
        Assert.That(MoodiVoice.ReservedTimeout, Is.InRange(10f, 30f));
        var source = File.ReadAllText("Assets/Scripts/Moodium/Characters/MoodiVoice.cs");
        Assert.That(source, Does.Contain("Time.unscaledTime > q.Expires) continue;"));
        Assert.That(source, Does.Not.Contain("!q.Reserved && Time.unscaledTime > q.Expires"), "reserved lines used to wait forever");
        Assert.That(source, Does.Contain("if (TeachingIds.Contains(id)) s_Taught.Add(id);"), "taught when it starts");
    }

    // ---------- 10 ----------

    [Test]
    public void TheOpenDoorPausesTheVines()
    {
        var manager = File.ReadAllText("Assets/Scripts/Moodium/CandyWorldManager.cs");
        var walk = manager.Substring(manager.IndexOf("void SetWalkSuspended(bool value)", System.StringComparison.Ordinal));
        walk = walk.Substring(0, walk.IndexOf('}') + 1);
        Assert.That(walk, Does.Contain("m_NatureIvyGrowth?.SetSuspended(value);"));

        m_Host = new GameObject("Vines");
        var generatorObject = new GameObject("Ivy Generator");
        generatorObject.transform.SetParent(m_Host.transform);
        generatorObject.AddComponent<MeshFilter>();
        generatorObject.AddComponent<MeshRenderer>();
        var generator = generatorObject.AddComponent<IvyGeneratorTREANT>();
        generator.addLeaves = false;
        generator.growIvy = false;
        var vines = m_Host.AddComponent<NatureIvySpatialGrowthController>();
        vines.Configure(generator, 10);
        vines.StartGrowth();
        m_Host.AddComponent<Moodium.Flow.WorldJourneyDirector>().Rescued();
        vines.SetProgress(1f);
        Assert.That(vines.CanPlantEnvironment, Is.True);
        vines.SetSuspended(true);    // door open
        Assert.That(vines.CanPlantEnvironment, Is.False, "no touch, no gaze twins, no tip while the door stands open");
        vines.SetSuspended(false);   // door parked
        Assert.That(vines.CanPlantEnvironment, Is.True);
    }
}
