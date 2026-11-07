using System.Reflection;
using Moodium.CandyWorld;
using NUnit.Framework;
using UnityEngine;
using Object=UnityEngine.Object;
public sealed class WorldLayoutLifecycleTests
{
 const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Tick(FloatingSqueezeProgress ring,float now,float dt)=>typeof(FloatingSqueezeProgress).GetMethod("Tick",Flags).Invoke(ring,new object[]{now,dt});
 [Test]public void FullRingHoldsFadesAndStaysHiddenWhenPressedAgain()
 {
  var model=GameObject.CreatePrimitive(PrimitiveType.Cube);var root=new GameObject("ring");var camera=new GameObject("camera");
  try{
   var ring=root.AddComponent<FloatingSqueezeProgress>();ring.Bind(model.transform,camera.AddComponent<Camera>(),false);ring.SetProgress(1);
   float full=(float)typeof(FloatingSqueezeProgress).GetField("m_FullAt",Flags).GetValue(ring);
   Tick(ring,full+.5f,.5f);Assert.That(root.activeSelf,Is.True);Assert.That(ring.DisplayText,Does.StartWith("100"));
   Tick(ring,full+1.59f,.1f);Assert.That(root.activeSelf,Is.True);
   Tick(ring,full+1.9f,.3f);Assert.That(root.activeSelf,Is.True);Assert.That((float)typeof(FloatingSqueezeProgress).GetField("m_Alpha",Flags).GetValue(ring),Is.InRange(.45f,.55f));
   ring.NotifyPressed();Tick(ring,full+2.3f,.4f);Assert.That(root.activeSelf,Is.False);
   ring.NotifyPressed();ring.SetProgress(1);Assert.That(root.activeSelf,Is.False);
   ring.SetProgress(.8f);Assert.That(root.activeSelf,Is.True);Tick(ring,full+2.5f,.2f);Assert.That(ring.DisplayText,Does.StartWith("80"));
  }finally{Object.DestroyImmediate(root);Object.DestroyImmediate(model);Object.DestroyImmediate(camera);}
 }
 [Test]public void FallingEnergyDoesNotUndoAnExternalHide()
 {
  var model=GameObject.CreatePrimitive(PrimitiveType.Cube);var root=new GameObject("ring");
  try{var ring=root.AddComponent<FloatingSqueezeProgress>();ring.Bind(model.transform,null,true);ring.SetProgress(.7f);root.SetActive(false);ring.SetProgress(.5f);Assert.That(root.activeSelf,Is.False);}
  finally{Object.DestroyImmediate(root);Object.DestroyImmediate(model);}
 }
}
