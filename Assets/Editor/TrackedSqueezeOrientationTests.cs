using System.Reflection;
using Moodium.Interaction;
using Moodium.Reality;
using NUnit.Framework;
using UnityEngine;

public sealed class TrackedSqueezeOrientationTests
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    [TestCase(0f,90f,0f)]
    [TestCase(20f,75f,15f)]
    public void VideoRetainsTrackedHeadingAsWellAsSurfaceNormal(float x,float y,float z)
    {
        var anchor=new GameObject("Anchor");var soft=new GameObject("Soft");var root=new GameObject("Video controller");
        var display=new GameObject("Disc");
        var controller=root.AddComponent<SqueezeProgressVideoController>();
        try
        {
            anchor.transform.rotation=Quaternion.Euler(x,y,z);
            var capsule=soft.AddComponent<ChocolateCapsuleInteraction>();
            soft.AddComponent<TrackedObjectPoseFollower>().Configure(anchor.transform,Vector3.zero,Quaternion.identity);
            typeof(SqueezeProgressVideoController).GetField("m_Display",Private).SetValue(controller,display);
            controller.BindToSoft(capsule);
            Assert.That(Vector3.Angle(display.transform.up,anchor.transform.up),Is.LessThan(.01f));
            Assert.That(Vector3.Angle(display.transform.forward,anchor.transform.forward),Is.LessThan(.01f));
            anchor.transform.rotation=Quaternion.Euler(-10,125,5);
            typeof(SqueezeProgressVideoController).GetMethod("UpdateDisplayPose",Private).Invoke(controller,null);
            Assert.That(Vector3.Angle(display.transform.forward,anchor.transform.forward),Is.LessThan(.01f));
        }
        finally
        {
            typeof(SqueezeProgressVideoController).GetField("m_Display",Private).SetValue(controller,null);
            Object.DestroyImmediate(display);Object.DestroyImmediate(root);Object.DestroyImmediate(soft);Object.DestroyImmediate(anchor);
        }
    }
    [Test]
    public void HandGuidesFollowModelAxesWithoutChangingAuthoredHandPose()
    {
        var target=new GameObject("Soft");var prefab=new GameObject("Authored hand");var root=new GameObject("Hand guide");
        try
        {
            target.transform.SetPositionAndRotation(new Vector3(1,2,3),Quaternion.Euler(25,90,15));
            prefab.transform.rotation=Quaternion.Euler(10,-20,-90);
            var guide=root.AddComponent<PushHandInteractionGuide>();
            guide.Configure(target.AddComponent<ChocolateCapsuleInteraction>(),prefab,prefab);
            typeof(PushHandInteractionGuide).GetMethod("LateUpdate",Private).Invoke(guide,null);
            var left=(GameObject)typeof(PushHandInteractionGuide).GetField("m_Left",Private).GetValue(guide);
            var right=(GameObject)typeof(PushHandInteractionGuide).GetField("m_Right",Private).GetValue(guide);
            Assert.That(Vector3.Angle(right.transform.position-left.transform.position,target.transform.right),Is.LessThan(.05f));
            Assert.That(Quaternion.Angle(left.transform.rotation,target.transform.rotation*prefab.transform.rotation),Is.LessThan(.01f));
        }
        finally{Object.DestroyImmediate(root);Object.DestroyImmediate(prefab);Object.DestroyImmediate(target);}
    }
}
