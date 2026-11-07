using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using Moodium.Reality;
using Moodium.Calibration;
using Moodium.Interaction;
using Moodium.UI;
public class WorldFix1030Tests
{
    static readonly BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
    [TestCase(false)][TestCase(true)]
    public void OpeningButtonsArePlainCanvasImagesWithoutHoverOrCustomMaterial(bool hardware)
    {
        var choice=new GameObject("choice",typeof(RectTransform));
        try
        {
            if(hardware)choice.AddComponent<Moodium.Opening.OpeningVideoHardwareChoice>().Configure(null,null,false);
            else choice.AddComponent<Moodium.Opening.OpeningVideoLanguageChoice>().Configure(null,null);
            var buttons=choice.GetComponentsInChildren<Moodium.Opening.OpeningVideoSpatialButton>();Assert.That(buttons.Length,Is.EqualTo(2));
            foreach(var b in buttons)
            {
                var image=b.GetComponent<UnityEngine.UI.Image>();Assert.That(image,Is.Not.Null);Assert.That(image.sprite.name,Does.EndWith("_idle"));
                Assert.That(image.material,Is.EqualTo(image.defaultMaterial));
                Assert.That(b.GetComponentInChildren<Unity.PolySpatial.VisionOSHoverEffect>(true),Is.Null);
                var size=b.GetComponent<RectTransform>().sizeDelta;Assert.That(size.x/size.y,Is.EqualTo(498f/282f).Within(.01f),"keep the art's aspect ratio");
            }
            Assert.That(buttons[0].GetComponent<BoxCollider>().bounds.Intersects(buttons[1].GetComponent<BoxCollider>().bounds),Is.False);
        }finally{Object.DestroyImmediate(choice);}
    }
    [Test] public void DisabledRiseRoutesToVideo()
    {
        var host=new GameObject("router");var soft=new GameObject("soft");
        try{var router=host.AddComponent<SqueezeProgressDisplayRouter>();router.Configure(null,null,null);typeof(SqueezeProgressDisplayRouter).GetField("m_UseMoodiRise",Flags).SetValue(router,false);router.Begin(false);router.BindToSoft(soft.AddComponent<ChocolateCapsuleInteraction>());Assert.That(typeof(SqueezeProgressDisplayRouter).GetField("active",Flags).GetValue(router),Is.TypeOf<SqueezeProgressVideoController>());}
        finally{Object.DestroyImmediate(host);Object.DestroyImmediate(soft);}
    }
    [TestCase("Assets/prefab/Moodi meshy.prefab")][TestCase("Assets/Resources/MoodiEmergence/Moodi.prefab")]
    public void MoodiModelInstanceIsNotTurnedOrRescaled(string path)
    {
        // d9c4093 turned this instance 180° (Moodi showed its back in the world selection UI), scaled it
        // 1.39x and lifted it 6 cm. It must stay as approved before the v2 rig: identity, scale 0.1, origin.
        var rigged=AssetDatabase.LoadAssetAtPath<GameObject>(path).transform.Find("moodi_rigged");
        Assert.That(rigged,Is.Not.Null);
        Assert.That(Quaternion.Angle(rigged.localRotation,Quaternion.identity),Is.LessThan(.5f));
        Assert.That(rigged.localScale.x,Is.EqualTo(.1f).Within(.001f));
        Assert.That(rigged.localPosition.magnitude,Is.LessThan(.001f));
    }
    [TestCase(0f)][TestCase(90f)][TestCase(-135f)]
    public void RiseFacesTheViewerAndKeepsFourCentimetres(float viewerYaw)
    {
        var anchor=new GameObject("anchor");var host=new GameObject("rise");var root=new GameObject("rescue");var cam=new GameObject("viewer");
        var actor=Object.Instantiate(Resources.Load<GameObject>("MoodiEmergence/Moodi"),root.transform);
        // Simulate the companion having turned its model on the way in: an arbitrary yaw on the animated model.
        var animator=actor.GetComponentInChildren<Animator>();animator.transform.rotation=Quaternion.AngleAxis(73f,Vector3.up)*animator.transform.rotation;
        try
        {
            var camera=cam.AddComponent<Camera>();cam.transform.position=Quaternion.AngleAxis(viewerYaw,Vector3.up)*new Vector3(0,.4f,-.6f);
            var presentation=anchor.AddComponent<CalibratedSensoryBagPresentation>();
            presentation.Configure(anchor.transform,new SensoryBagCalibrationData{Width=.4f,Height=.25f,Depth=.05f,LocalOrientation=Quaternion.identity},AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefab/NatureSoft.prefab"));
            var rise=host.AddComponent<MoodiRiseDisplay>();rise.Configure(camera,null,null);rise.Begin(true);
            rise.BindToSoft(presentation.VisualRoot.GetComponentInChildren<ChocolateCapsuleInteraction>());
            rise.AttachMoodi(animator);typeof(MoodiRiseDisplay).GetMethod("LateUpdate",Flags).Invoke(rise,null);
            var bones=actor.GetComponentsInChildren<Transform>(true);
            var eyeL=bones.Single(t=>t.name=="Eye_L");var eyeR=bones.Single(t=>t.name=="Eye_R");var upper=bones.Single(t=>t.name=="Body_Upper");
            rise.TryGetDomeCenter(out var surface,out var up);
            var face=Vector3.ProjectOnPlane((eyeL.position+eyeR.position)*.5f-upper.position,up);
            var toward=Vector3.ProjectOnPlane(cam.transform.position-surface,up);
            Assert.That(Vector3.Angle(face,toward),Is.LessThan(2f),"Moodi's face must point at the viewer");
            Assert.That(Vector3.Distance(eyeL.position,eyeR.position)/.678f*1.904f,Is.EqualTo(.04f).Within(.004f),"largest side stays 4 cm");
        }finally{Object.DestroyImmediate(host);Object.DestroyImmediate(root);Object.DestroyImmediate(anchor);Object.DestroyImmediate(cam);}
    }
    [TestCase("Nature")][TestCase("Candy")]
    public void RisePreservesParentAndAlignsRigAtSurface(string world)
    {
        var anchor=new GameObject("anchor");var host=new GameObject("rise");var root=new GameObject("rescue");
        var actor=Object.Instantiate(Resources.Load<GameObject>("MoodiEmergence/Moodi"),root.transform);
        var renderers=actor.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
        actor.transform.localScale*=.04f/Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));
        try
        {
            var presentation=anchor.AddComponent<CalibratedSensoryBagPresentation>();
            presentation.Configure(anchor.transform,new SensoryBagCalibrationData{Width=.4f,Height=.25f,Depth=.05f,LocalOrientation=Quaternion.identity},AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/prefab/{world}Soft.prefab"));
            var rise=host.AddComponent<MoodiRiseDisplay>();rise.Begin(world=="Nature");rise.BindToSoft(presentation.VisualRoot.GetComponentInChildren<ChocolateCapsuleInteraction>());
            Assert.That(rise.Ready,Is.True);var animator=actor.GetComponentInChildren<Animator>();var parent=animator.transform.parent;
            rise.AttachMoodi(animator);typeof(MoodiRiseDisplay).GetMethod("LateUpdate",Flags).Invoke(rise,null);
            var rig=actor.GetComponentsInChildren<Transform>().Single(t=>t.name=="Moodi_Rig");rise.TryGetDomeCenter(out var surface,out _);
            Assert.That(Vector3.Distance(rig.position,surface),Is.LessThan(.001f));Assert.That(animator.transform.parent,Is.SameAs(parent));
            var before=rig.position;typeof(MoodiRiseDisplay).GetField("spring",Flags).SetValue(rise,1f);typeof(MoodiRiseDisplay).GetMethod("LateUpdate",Flags).Invoke(rise,null);
            Assert.That(Vector3.Distance(rig.position,before),Is.LessThan(.0001f));
            int pops=0;rise.Popped+=_=>pops++;
            for(int i=0;i<2;i++)
            {
                rise.SetProgress(1);typeof(MoodiRiseDisplay).GetField("progress",Flags).SetValue(rise,1f);
                typeof(MoodiRiseDisplay).GetMethod("Update",Flags).Invoke(rise,null);typeof(MoodiRiseDisplay).GetMethod("Update",Flags).Invoke(rise,null);
                Assert.That(pops,Is.EqualTo(i+1));rise.ResetAndHide();Assert.That(actor.transform.localPosition,Is.EqualTo(Vector3.zero));
                rise.BindToSoft(presentation.VisualRoot.GetComponentInChildren<ChocolateCapsuleInteraction>());
            }
        }finally{Object.DestroyImmediate(host);Object.DestroyImmediate(root);Object.DestroyImmediate(anchor);}
    }
    [Test] public void ProgressNeverReversesOrCompletesInUnderThreeSeconds()
    {float p=0,v=0;for(int i=0;i<180;i++)p=MoodiRiseDisplay.Advance(p,1,ref v,.25f,.3f,1f/60);Assert.That(p,Is.LessThan(.995f));Assert.That(MoodiRiseDisplay.Advance(p,0,ref v,.25f,.3f,.016f),Is.GreaterThanOrEqualTo(p));}
    [Test] public void MissingEmergeStateIsNotSupported()
    {var go=new GameObject("empty");try{Assert.That(MoodiRiseDisplay.SupportsRise(go.AddComponent<Animator>()),Is.False);}finally{Object.DestroyImmediate(go);}}
}
