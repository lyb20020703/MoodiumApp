using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

public sealed class WishPresetIntegrationTests
{
 const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
 static object Read(object o,string name)=>o.GetType().GetField(name,Flags).GetValue(o);
 static void Set(object o,string name,object value)=>o.GetType().GetField(name,Flags).SetValue(o,value);
 static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Flags).Invoke(o,args);
 sealed class Fixture:IDisposable
 {
  public readonly GameObject Root;public readonly WishChime Chime;public readonly WishChimeGarden Garden;public readonly BubblePlayHands Hands=new();public readonly string DirectoryPath,Path;
  public WishInkMesh Ink=>Chime.GetComponentInChildren<WishInkMesh>();
  public Fixture(bool candy=true)
  {
   DirectoryPath=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"wish-preset-"+Guid.NewGuid().ToString("N"));Path=System.IO.Path.Combine(DirectoryPath,"wish_chimes.json");
   Root=new GameObject("Preset fixture");var camera=Root.AddComponent<Camera>();Garden=Root.AddComponent<WishChimeGarden>();
   Garden.Initialize(candy?BubbleWorldData.CandyFolder:BubbleWorldData.NatureFolder,Root.transform,camera,Hands,null,Path);Set(Garden,"taught",true);
   var go=new GameObject("Wish");go.transform.SetParent(Root.transform,false);Chime=go.AddComponent<WishChime>();
   Chime.Initialize(Garden,new WishChimeRecord{id="fixture",position=new[]{0f,0f,0f}});Set(Chime,"<State>k__BackingField",WishChime.Stage.Blank);Chime.BeginWrite();
  }
  public void Pick(string id)=>Call(Chime,"PickPreset",id);
  public void Finish(){var t=(WishWritingTimeline)Read(Chime,"timeline");Set(Chime,"presetStart",Time.time-t.Total-.01f);Call(Chime,"StepPreset");}
  public void SettleErase(){Chime.StopAllCoroutines();var erase=(IEnumerator)Call(Chime,"Erase",0f,null);while(erase.MoveNext()){} }
  public void Dispose(){Object.DestroyImmediate(Root);if(System.IO.Directory.Exists(DirectoryPath))System.IO.Directory.Delete(DirectoryPath,true);}
 }
 [TestCase(true)][TestCase(false)]
 public void ChoosingAndFinishingProducesExactlyThePresetAndAllowsSignature(bool candy)
 {
  using var f=new Fixture(candy);f.Pick("peace_joy");Assert.That(f.Chime.PresetWriting,Is.True);f.Finish();
  var expected=WishPresetLibrary.Find("peace_joy",Moodium.Characters.MoodiVoice.LanguageCode);
  Assert.That(f.Chime.HasInk,Is.True);Assert.That(f.Chime.PresetWriting,Is.False);
  Assert.That(Newtonsoft.Json.JsonConvert.SerializeObject(Read(f.Chime,"strokes")),Is.EqualTo(Newtonsoft.Json.JsonConvert.SerializeObject(expected.strokes)));
  var strokes=(List<List<float[]>>)Read(f.Chime,"strokes");strokes.Add(new(){new[]{.2f,.1f},new[]{.5f,.1f}});var count=strokes.Count;f.Pick("peace_joy");Assert.That(strokes.Count,Is.EqualTo(count));Assert.That(f.Chime.PresetWriting,Is.False);
  var frame=Resources.Load<WishPaperFrame>("MoodiumBubblePlay/PaperFrame_"+(candy?"Candy":"Nature"));
  f.Hands.Hands[1]=new BubblePlayHand{Valid=true,Pinching=true,Tip=f.Chime.Strip.TransformPoint(frame.Surface(new Vector2(.5f,.15f)))};Call(f.Chime,"Update");
  Assert.That(Read(f.Chime,"current"),Is.Not.Null);Assert.That(f.Ink.InkScale,Is.EqualTo(WishPresetLibrary.InkWidth));
 }
 [Test]
 public void PresetAnimationBlocksHandwritingAndReleaseIsEnabledEvenBeforeFirstStrokeEnds()
 {
  using var f=new Fixture();f.Pick("happy");var frame=Resources.Load<WishPaperFrame>("MoodiumBubblePlay/PaperFrame_Candy");
  f.Hands.Hands[1]=new BubblePlayHand{Valid=true,Pinching=true,Tip=f.Chime.Strip.TransformPoint(frame.Surface(new Vector2(.5f,.5f)))};
  Call(f.Chime,"Update");Assert.That(f.Chime.PresetWriting,Is.True);Assert.That(Read(f.Chime,"current"),Is.Null);
  Assert.That(((WishActionButton)Read(f.Chime,"releaseButton")).Interactable,Is.True);
 }
 [UnityTest]
 public IEnumerator ReleaseDuringAnimationCompletesAndSavesThePresetId()
 {
  using var f=new Fixture();f.Pick("let_go");f.Chime.ReleaseWish();Assert.That(f.Chime.PresetWriting,Is.False);Assert.That(f.Chime.HasInk,Is.True);
  var timeout=System.Diagnostics.Stopwatch.StartNew();while(!File.Exists(f.Path)&&timeout.Elapsed.TotalSeconds<5)yield return null;
  Assert.That(File.Exists(f.Path),Is.True,"Release must save to the isolated path");
  var record=WishChimeStore.Load(f.Path).chimes.Single();Assert.That(record.preset,Is.EqualTo("let_go"));
  Assert.That(Newtonsoft.Json.JsonConvert.SerializeObject(record.strokes),Is.EqualTo(Newtonsoft.Json.JsonConvert.SerializeObject(WishPresetLibrary.Find("let_go",Moodium.Characters.MoodiVoice.LanguageCode).strokes)));
 }
 [Test]
 public void WriteOwnClearsPresetButKeepsPureHandwriting()
 {
  using var f=new Fixture();f.Pick("stay_well");f.Finish();Call(f.Chime,"WriteOwn");f.SettleErase();
  Assert.That(f.Chime.HasInk,Is.False);Assert.That(f.Chime.PresetId,Is.Null);Assert.That(f.Ink.InkScale,Is.EqualTo(1));Assert.That(f.Ink.GlowScale,Is.EqualTo(1));
  var strokes=new List<List<float[]>>{new(){new[]{.2f,.2f},new[]{.6f,.6f}}};Set(f.Chime,"strokes",strokes);Set(f.Chime,"total",2);Call(f.Chime,"WriteOwn");Assert.That(Read(f.Chime,"strokes"),Is.SameAs(strokes));Assert.That(f.Chime.HasInk,Is.True);
 }
 [Test]
 public void SwitchingDuringFirstStrokePreservesVisibleInkForTheFade()
 {
  using var f=new Fixture();f.Pick("happy");var timeline=(WishWritingTimeline)Read(f.Chime,"timeline");Set(f.Chime,"presetStart",Time.time-timeline.Groups[0].Duration*.5f);Call(f.Chime,"StepPreset");
  var mesh=f.Ink.transform.Find("Current Ink").GetComponent<MeshFilter>().sharedMesh;Assert.That(mesh.vertexCount,Is.GreaterThan(0));
  f.Pick("good_vibes");Assert.That(Read(f.Chime,"clearing"),Is.True);Assert.That(mesh.vertexCount,Is.GreaterThan(0),"Current ink fades instead of disappearing before the fade starts");
 }
 [TestCase(true)][TestCase(false)]
 public void RestoringPresetAndOldHandwrittenRecordKeepsTheirRespectiveWidths(bool preset)
 {
  using var f=new Fixture();var go=new GameObject("restored");go.transform.SetParent(f.Root.transform,false);var c=go.AddComponent<WishChime>();
  c.Initialize(f.Garden,new WishChimeRecord{id="restored",position=new[]{0f,0f,0f},preset=preset?"happy":null,strokes=WishPresetLibrary.CopyStrokes(WishPresetLibrary.Find("happy","zh"))});
  Assert.That(c.GetComponentInChildren<WishInkMesh>().InkScale,Is.EqualTo(preset?WishPresetLibrary.InkWidth:1));
 }
}
