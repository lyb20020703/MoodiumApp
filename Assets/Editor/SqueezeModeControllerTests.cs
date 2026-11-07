using Moodium.Squeeze;
using Moodium.MoodCheck;
using Moodium.CandyWorld;
using NUnit.Framework;
using UnityEngine;
public sealed class SqueezeModeControllerTests
{
    [Test] public void SwitchingModesPreservesSharedEnergyAndStopRemovesView()
    {
        var root=new GameObject("Switching session");
        try
        {
            var energy=root.AddComponent<CandyEnergyController>();energy.AddEnergy(37.5f,"test");
            var controller=root.AddComponent<SqueezeModeController>();controller.BeginSession(energy,null,false,_=>true);
            controller.RequestMode(SqueezeMode.Rhythm);Assert.That(controller.Rhythm,Is.Not.Null);
            controller.RequestMode(SqueezeMode.Breath);Assert.That(controller.Breath,Is.Not.Null);
            Assert.That(energy.Energy,Is.EqualTo(37.5f));
            controller.StopSession();Assert.That(root.GetComponent<SqueezeTableView>(),Is.Null);
        }
        finally{root.GetComponent<SqueezeModeController>()?.StopSession();Object.DestroyImmediate(root);}
    }
    [Test] public void CompletedSessionDoesNotRestartOnHeldHeartbeat()
    {
        var root=new GameObject("Session");var bag=new GameObject("Bag");
        try
        {
            var energy=root.AddComponent<CandyEnergyController>();
            var controller=root.AddComponent<SqueezeModeController>();
            var capsule=bag.AddComponent<Moodium.Interaction.ChocolateCapsuleInteraction>();
            controller.BeginSession(energy,null,false,_=>true);
            controller.AcceptPinch(capsule);
            var tracker=(SqueezeTracker)typeof(SqueezeModeController).GetField("tracker",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(controller);
            tracker.ReleaseAllForExit(Time.unscaledTimeAsDouble);tracker.Tick(Time.unscaledTimeAsDouble+1);
            energy.AddEnergy(100,"test");
            typeof(SqueezeModeController).GetMethod("OnContactHeld",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(controller,new object[]{capsule,Vector3.zero});
            Assert.That(controller.IsHeld,Is.False);
        }
        finally{root.GetComponent<SqueezeModeController>()?.StopSession();Object.DestroyImmediate(root);Object.DestroyImmediate(bag);}
    }
    [Test] public void PausedMusicRetainsSampleClockInsteadOfFallingBack()
    {
        var root=new GameObject("Audio clock");var clip=AudioClip.Create("Clock",44100,1,44100,false);
        try
        {
            var manager=root.AddComponent<Moodium.Audio.MoodiumAudioManager>();
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(Moodium.Audio.MoodiumAudioManager).GetMethod("Awake",flags).Invoke(manager,null);
            var source=(AudioSource)typeof(Moodium.Audio.MoodiumAudioManager).GetField("m_BackgroundMusicSource",flags).GetValue(manager);
            source.clip=clip;source.timeSamples=22050;
            typeof(Moodium.Audio.MoodiumAudioManager).GetField("m_CurrentBackgroundMusic",flags).SetValue(manager,clip);
            Assert.That(source.isPlaying,Is.False);
            Assert.That(Moodium.Audio.MoodiumAudioManager.TryGetBackgroundMusicTime(out var seconds),Is.True);
            Assert.That(seconds,Is.EqualTo(.5).Within(.001));
        }
        finally{Object.DestroyImmediate(root);Object.DestroyImmediate(clip);}
    }
    [Test] public void MoodPresetsAndHeldSwitchAreSessionLocal()
    {
        var state=new SqueezeModeState(MoodKind.Tense);
        Assert.That(state.Current,Is.EqualTo(SqueezeMode.Breath));
        Assert.That(SqueezeTuning.ForMood(new SqueezeParameters(),MoodKind.Tense).ExhaleOk,Is.EqualTo(6));
        Assert.That(SqueezeTuning.ForMood(new SqueezeParameters(),MoodKind.Low).Window,Is.EqualTo(.28));
        Assert.That(new SqueezeModeState(null).Current,Is.EqualTo(SqueezeMode.Free));
        Assert.That(state.Request(SqueezeMode.Rhythm,true),Is.True);
        Assert.That(state.Pending,Is.Null);
        Assert.That(state.Current,Is.EqualTo(SqueezeMode.Rhythm));
        Assert.That(state.Released(),Is.False);Assert.That(state.Current,Is.EqualTo(SqueezeMode.Rhythm));
    }
    [Test] public void EnergyAcceptsFractionalAmountsWithoutDuplicateThresholds()
    {
        var root=new GameObject("Energy test");
        try{var energy=root.AddComponent<CandyEnergyController>();int count=0;energy.StageReached+=_=>count++;
            energy.AddEnergy(19.9f,"test");energy.AddEnergy(.2f,"test");Assert.That(count,Is.EqualTo(1));
            energy.RegisterPinch();Assert.That(energy.Energy,Is.EqualTo(25.1f).Within(.001));
            energy.AddEnergy(100,"test");energy.AddEnergy(100,"test");Assert.That(count,Is.EqualTo(4));}
        finally{Object.DestroyImmediate(root);}
    }
}
