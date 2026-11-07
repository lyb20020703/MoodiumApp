using System;
using Object=UnityEngine.Object;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;
public sealed class WishChimeTests
{
 [Test]public void InkRejectsJitterAndNeedsARealStroke()
 {
  var stroke=new List<float[]>();Assert.That(WishChimeStore.AddPoint(stroke,new Vector2(.5f,.5f)),Is.True);Assert.That(WishChimeStore.AddPoint(stroke,new Vector2(.501f,.501f)),Is.False);
  Assert.That(WishChimeStore.CanRelease(new(){stroke}),Is.False);Assert.That(WishChimeStore.AddPoint(stroke,new Vector2(.8f,.8f)),Is.True);Assert.That(WishChimeStore.CanRelease(new(){stroke}),Is.True);Assert.That(stroke[1][0],Is.EqualTo(.65f).Within(.0001f));
 }
 [Test]public void SpacingUsesTheEnlargedWritingSurface()
 {var stroke=new List<float[]>{new[]{.5f,.5f}};Assert.That(WishChimeStore.AddPoint(stroke,new Vector2(.54f,.5f),2.5f),Is.True);}
 [Test]public void HangingPositionsStayApart(){Assert.That(WishChimeStore.FarEnough(Vector3.zero,new[]{Vector3.right*.34f}),Is.False);Assert.That(WishChimeStore.FarEnough(Vector3.zero,new[]{Vector3.right*.36f}),Is.True);}
 [Test]public void SavedWishesRoundTripAndReplaceAtomically()
 {
  string dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")),path=Path.Combine(dir,"wish_chimes.json");
  try{
   var doc=new WishChimeDocument();doc.chimes.Add(new WishChimeRecord{id="test",world="candy",folder=BubbleWorldData.NestedCandyFolder,layer=1,createdAt=DateTimeOffset.Now.ToString("o"),position=new[]{.42f,.38f,-.71f},yaw=35,strokes=new(){new(){new[]{.31f,.12f},new[]{.33f,.15f}},new(){new[]{.52f,.40f},new[]{.50f,.47f}}}});
   Assert.That(WishChimeStore.Save(path,doc),Is.True);Assert.That(WishChimeStore.Save(path,doc),Is.True);var result=WishChimeStore.Load(path);Assert.That(Newtonsoft.Json.JsonConvert.SerializeObject(result),Is.EqualTo(Newtonsoft.Json.JsonConvert.SerializeObject(doc)));Assert.That(result.version,Is.EqualTo(1));Assert.That(result.chimes[0].position,Is.EqualTo(doc.chimes[0].position));Assert.That(result.chimes[0].strokes[1][1],Is.EqualTo(doc.chimes[0].strokes[1][1]));Assert.That(File.Exists(path+".tmp"),Is.False);
   File.WriteAllText(path,"corrupt {");Assert.That(WishChimeStore.Load(path).chimes,Is.Empty);
  }finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
 }
 [TestCase("Candy")][TestCase("Nature")]
 public void ChimePartsAndBakedSurfaceAreAvailable(string world)
 {
  var model=Resources.Load<GameObject>("MoodiumBubblePlay/WishChime_"+world);Assert.That(model,Is.Not.Null);var names=model.GetComponentsInChildren<Transform>(true).Select(t=>t.name).ToArray();
  foreach(var name in new[]{"Ring","String_Top","Bell","Glow","Clapper","Clapper_String","String_Strip","Strip"})Assert.That(names,Does.Contain(name));
  var frame=Resources.Load<WishPaperFrame>("MoodiumBubblePlay/PaperFrame_"+world);Assert.That(frame,Is.Not.Null);Assert.That(frame.grid.Length,Is.EqualTo(225));
  foreach(var uv in new[]{Vector2.zero,Vector2.one,new Vector2(.23f,.74f)})Assert.That((frame.Project(frame.Surface(uv))-uv).magnitude,Is.LessThan(.001f));
 }
 [Test]public void DynamicStrokeNeverExceedsThreeHundredVertices()
 {
  var paper=new GameObject("paper");var go=new GameObject("ink");
  try{var ink=go.AddComponent<WishInkMesh>();ink.Initialize(paper.transform,Resources.Load<WishPaperFrame>("MoodiumBubblePlay/PaperFrame_Candy"),true);var stroke=new List<float[]>();for(int i=0;i<200;i++)stroke.Add(new[]{i/200f,.5f});ink.Active(stroke);Assert.That(go.GetComponentsInChildren<MeshFilter>()[0].sharedMesh.vertexCount,Is.LessThanOrEqualTo(300));}
  finally{Object.DestroyImmediate(paper);if(go!=null)Object.DestroyImmediate(go);}
 }
 [Test]public void NaturePaperDecorationFollowsItsWritingScale()
 {
  var root=new GameObject("chime fixture");
  try{var camera=root.AddComponent<Camera>();var garden=root.AddComponent<WishChimeGarden>();garden.Initialize(BubbleWorldData.NatureFolder,root.transform,camera,new BubblePlayHands(),null);var host=new GameObject("wish");host.transform.SetParent(root.transform,false);var chime=host.AddComponent<WishChime>();chime.Initialize(garden,new WishChimeRecord{id="test",folder=BubbleWorldData.NatureFolder,position=new[]{0f,0f,0f}});Assert.That(chime.Strip.Find("Leaf_Deco"),Is.Not.Null);}
  finally{Object.DestroyImmediate(root);}
 }
 [Test]public void AllPlaySoundsAndVoicesLoad()
 {
  foreach(var world in new[]{"candy","nature"})
  {
   Assert.That(BubblePlayVisuals.Clip("ripple_"+world),Is.Not.Null);for(int i=0;i<3;i++)Assert.That(BubblePlayVisuals.Clip("chime_ring_"+world+"_"+i),Is.Not.Null);
  }
  foreach(var name in new[]{"chime_write_loop","chime_release","catch_snow_0","catch_snow_1","catch_snow_2","catch_choco_0","catch_choco_1","catch_choco_2"})Assert.That(BubblePlayVisuals.Clip(name),Is.Not.Null);
  foreach(var id in new[]{"B1","B2c","B2n","B3","B4","B5","B6","B7","B8"})foreach(var lang in new[]{"zh","en"})Assert.That(Resources.Load<AudioClip>($"MoodiumVoice/{lang}/moodi_vo_{id}_{lang}"),Is.Not.Null);
  var bits=Resources.Load<GameObject>("MoodiumBubblePlay/CatchBits");Assert.That(bits,Is.Not.Null);foreach(var part in new[]{"Snowflake","ChocoChip","MintShard"})Assert.That(bits.GetComponentsInChildren<Transform>().Any(t=>t.name==part),Is.True);
 }
}
