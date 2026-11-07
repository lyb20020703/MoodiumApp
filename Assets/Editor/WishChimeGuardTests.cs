using System.Collections;
using System.Reflection;
using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;
public sealed class WishChimeGuardTests
{
 const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Set(object o,string name,object v)=>o.GetType().GetField(name,Private).SetValue(o,v);
 [TestCase(true,1f,0f,false,false)]
 [TestCase(false,1f,.8f,false,false)]
 [TestCase(false,1f,0f,true,false)]
 [TestCase(false,1f,0f,false,true)]
 public void DrawingRecentStrokeEraseAndSaveBlockAction(bool stroke,float now,float ended,bool erase,bool save)
 {Assert.That(WishChime.ActionBlocked(stroke,now,ended,erase,save),Is.True);}
 [Test]public void FinishedStrokeEventuallyAllowsAction()=>Assert.That(WishChime.ActionBlocked(false,1,.5f,false,false),Is.False);
 [Test]public void GraceRequiresFingerToLeaveBeforeRearming()
 {
  bool armed=true;Assert.That(BubbleWorldController.TouchAfterGrace(true,10.7f,10,ref armed),Is.False);Assert.That(armed,Is.False);
  Assert.That(BubbleWorldController.TouchAfterGrace(true,11,10,ref armed),Is.False);
  Assert.That(BubbleWorldController.TouchAfterGrace(false,11.1f,10,ref armed),Is.False);Assert.That(armed,Is.True);
  Assert.That(BubbleWorldController.TouchAfterGrace(true,11.2f,10,ref armed),Is.True);
 }
 [Test]public void DisabledOrBlockedButtonsNeverInvokeAction()
 {
  var go=new GameObject("Button test");int calls=0;bool blocked=false;
  try{var b=WishActionButton.Create(go.transform,"Release",WishActionButton.Prefix(true,true,false),()=>calls++,()=>blocked,5);
   b.SetInteractable(false);b.Press();Assert.That(calls,Is.Zero);Assert.That(b.GetComponent<SpriteRenderer>().sprite.name,Does.EndWith("_disabled"));
   b.SetInteractable(true);blocked=true;b.Press();Assert.That(calls,Is.Zero);blocked=false;b.Press();Assert.That(calls,Is.EqualTo(1));
  }finally{Object.DestroyImmediate(go);}
 }
 [Test]public void LockedPortalDoesNotStartEntry()
 {
  var go=new GameObject("Portal");try{var c=go.AddComponent<BubbleWorldController>();Set(c,"m_Phase",BubbleWorldController.Phase.Portal);Set(c,"m_PortalLocked",true);c.EnterFromPortal();Assert.That(c.CurrentPhase,Is.EqualTo(BubbleWorldController.Phase.Portal));}
  finally{Object.DestroyImmediate(go);}
 }
 [Test]public void LockedHomeRejectsOrdinaryRequestAndForceLeaves()
 {
  var go=new GameObject("Home");var play=new GameObject("Play");try{
   var visual=new GameObject("Return visual");visual.transform.SetParent(go.transform);var sky=GameObject.CreatePrimitive(PrimitiveType.Sphere);sky.transform.SetParent(go.transform);
   var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));sky.GetComponent<Renderer>().sharedMaterial=material;
   var c=go.AddComponent<BubbleWorldController>();Set(c,"m_WorldRoot",visual.transform);Set(c,"m_Sky",sky.transform);Set(c,"m_SkyRenderer",sky.GetComponent<Renderer>());Set(c,"m_SkyFadeMaterial",material);
   var p=play.AddComponent<BubbleWorldPlay>();var garden=play.AddComponent<WishChimeGarden>();Set(p,"garden",garden);Set(p,"active",true);
   typeof(WishChimeGarden).GetProperty("FocusActive").SetValue(garden,true);Set(c,"m_Play",p);Set(c,"m_Phase",BubbleWorldController.Phase.Inside);
   int requests=0;c.HomeRequested+=()=>requests++;
   var request=typeof(BubbleWorldController).GetMethod("RequestHome",Private);request.Invoke(c,new object[]{false});Assert.That(requests,Is.Zero);Assert.That(c.CurrentPhase,Is.EqualTo(BubbleWorldController.Phase.Inside));
   request.Invoke(c,new object[]{true});Assert.That(requests,Is.EqualTo(1));
  }finally{Object.DestroyImmediate(go);Object.DestroyImmediate(play);}
 }
    [Test]
    public void LockedPortalCanBeEnteredOnlyWithForce()
    {
        var host = new GameObject("Outer");
        var child = new GameObject("Inner"); child.transform.SetParent(host.transform);
        var cameraObject = new GameObject("Viewer");
        var outerVisual = new GameObject("Outer visual");
        var innerVisual = new GameObject("Inner visual");
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        try
        {
            var outer = host.AddComponent<BubbleWorldController>();
            var inner = child.AddComponent<BubbleWorldController>();
            var camera = cameraObject.AddComponent<Camera>();
            Set(outer, "m_Root", outerVisual);
            Set(outer, "m_OuterEntryFrom", new Vector3(1, .7f, 2));
            Set(inner, "m_Viewer", camera);
            Set(inner, "m_Root", innerVisual);
            Set(inner, "m_WorldRoot", innerVisual.transform);
            Set(inner, "m_Membrane", innerVisual.transform);
            Set(inner, "m_MembraneCollider", innerVisual.AddComponent<SphereCollider>());
            Set(inner, "m_MembraneMaterial", material);
            Set(inner, "m_Phase", BubbleWorldController.Phase.Portal);
            Set(inner, "m_Radius", .08f);
            innerVisual.transform.position = new Vector3(.2f, 1.1f, .6f);
            var start = innerVisual.transform.position;
            // Step the shared entry animation directly: t=0 must not jump.
            var routine = (IEnumerator)typeof(BubbleWorldController).GetMethod("ApproachAndReveal", Private).Invoke(inner, new object[] { camera });
            Assert.That(routine.MoveNext(), Is.True);
            Assert.That(Vector3.Distance(start, innerVisual.transform.position), Is.LessThan(.001f));
            Assert.That((float)Read(inner, "m_Radius"), Is.EqualTo(.08f).Within(.001f));
            Assert.That(outerVisual.activeSelf, Is.True);
            Assert.That((float)Read(inner, "m_NestedSkyScale") * BubbleWorldController.SkyMaxRadius, Is.LessThan(BubbleWorldController.SkyMaxRadius));
            Set(inner,"m_PortalLocked",true);inner.EnterFromPortal();Assert.That(inner.CurrentPhase,Is.EqualTo(BubbleWorldController.Phase.Portal));
            inner.EnterFromPortal(true);
            Assert.That(inner.CurrentPhase, Is.EqualTo(BubbleWorldController.Phase.Approach));
            Assert.That((Vector3)Read(inner, "m_From"), Is.EqualTo(new Vector3(1, .7f, 2)));
            Assert.That(Vector3.Distance(start, innerVisual.transform.position), Is.LessThan(.001f));
            inner.StopAllCoroutines();
        }
        finally
        {
            // Cleanup is deferred by the production controller; prevent duplicate ownership in this synthetic fixture.
            var outer = host.GetComponent<BubbleWorldController>(); Set(outer, "m_Root", null);
            var inner = child.GetComponent<BubbleWorldController>(); Set(inner, "m_Root", null); Set(inner, "m_MembraneMaterial", null);
            Object.DestroyImmediate(host); Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(outerVisual); Object.DestroyImmediate(innerVisual); Object.DestroyImmediate(material);
        }
    }
 static object Read(object o,string n)=>o.GetType().GetField(n,Private).GetValue(o);
}
