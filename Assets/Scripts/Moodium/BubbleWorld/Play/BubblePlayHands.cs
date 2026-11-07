using System.Collections.Generic;
using Moodium.Characters;
using Unity.PolySpatial;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;
namespace Moodium.NatureWorld
{
    public struct BubblePlayHand { public bool Valid,Pinching,Started,Open;public Vector3 Tip,Palm,Up,Index; }
    public sealed class BubblePlayHands
    {
        readonly List<XRHandSubsystem> systems=new();XRHandSubsystem system;XROrigin origin;
        readonly bool[] pinched=new bool[2],initialized=new bool[2];
        public readonly BubblePlayHand[] Hands=new BubblePlayHand[2];
        public static bool PinchState(bool before,float distance)=>distance<(before?.025f:.015f);
        public static bool CanCatch(Vector3 palm,Vector3 up,Vector3 point,int held)=>held<5&&Vector3.Dot(up,Vector3.up)>.6f&&Vector3.Distance(point,palm)<.07f&&Vector3.Dot(point-palm,Vector3.up)>=0;
        public void Reset(){for(int i=0;i<2;i++){initialized[i]=pinched[i]=false;Hands[i]=default;}}
        public void Sample()
        {
            if(system==null||!system.running){systems.Clear();SubsystemManager.GetSubsystems(systems);system=systems.Find(s=>s.running);}
            if(origin==null)origin=Object.FindFirstObjectByType<XROrigin>();
            for(int i=0;i<2;i++)
            {
                Hands[i]=default;if(system==null)continue;var hand=i==0?system.leftHand:system.rightHand;
                if(!hand.isTracked||!Joint(hand,XRHandJointID.ThumbTip,out var thumb)||!Joint(hand,XRHandJointID.IndexTip,out var index)||!Joint(hand,XRHandJointID.Wrist,out var wrist)||!Joint(hand,XRHandJointID.IndexMetacarpal,out var im)||!Joint(hand,XRHandJointID.MiddleMetacarpal,out var mm)||!Joint(hand,XRHandJointID.LittleMetacarpal,out var lm)){initialized[i]=false;pinched[i]=false;continue;}
                bool now=PinchState(pinched[i],Vector3.Distance(thumb,index));
                var palm=Joint(hand,XRHandJointID.MiddleProximal,out var knuckle)?Vector3.Lerp(wrist,knuckle,.7f):(wrist+mm)*.5f;
                Hands[i]=new BubblePlayHand{Valid=true,Pinching=now,Started=initialized[i]&&!pinched[i]&&now,Open=Vector3.Distance(thumb,index)>.05f,Tip=(thumb+index)*.5f,Index=index,Palm=palm,Up=MoodiHandCompanion.PalmFacing(wrist,im,mm,lm,i==1)};
                initialized[i]=true;pinched[i]=now;
            }
        }
        bool Joint(XRHand h,XRHandJointID id,out Vector3 p){p=default;if(!h.GetJoint(id).TryGetPose(out var pose))return false;p=origin!=null?origin.Origin.transform.TransformPoint(pose.position):pose.position;return true;}
    }
    public static class BubblePlayVisuals
    {
        public static Material Material(string html,float alpha=1){ColorUtility.TryParseHtmlString(html,out var c);c.a=alpha;return BubbleWorldData.CreateUnlit("Bubble Play",null,alpha<1,UnityEngine.Rendering.CullMode.Off,c);}
        public static AudioClip Clip(string id)=>Resources.Load<AudioClip>("MoodiumBubblePlay/Audio/"+id);
        public static void Release(Object o){if(o==null)return;if(Application.isPlaying)Object.Destroy(o);else Object.DestroyImmediate(o);}

        /// <summary>
        /// Drops sorting entries whose object is gone, and the entries of <paramref name="leaving"/> and everything under it
        /// (call this before destroying something registered in the group: Destroy only takes effect at the end of the frame).
        /// The bubble world's group collects entries from every chime, ring and button; stale ones were left behind when
        /// chimes, LOD spheres and hold rings were destroyed, and are the likely reason a newly built set of wish buttons
        /// sometimes drew behind the world (invisible but still pressable). Returns how many entries were removed.
        /// </summary>
        public static int PruneSorting(VisionOSSortingGroup group, Transform leaving = null)
        {
            if (group == null) return 0;
            var removed = 0;
            for (int i = group.Renderers.Count - 1; i >= 0; i--)
            {
                var go = group.Renderers[i].Renderer;
                if (go == null || (leaving != null && go.transform.IsChildOf(leaving))) { group.Renderers.RemoveAt(i); removed++; }
            }
            if (removed > 0) SortingChanged(group);
            return removed;
        }

        /// <summary>Tells PolySpatial the group's entry list changed (entries added or removed after it was first sent).</summary>
        public static void SortingChanged(VisionOSSortingGroup group)
        {
            // The public Renderers setter marks the group dirty in PolySpatial 2.4.3.
            // Add/Remove also notify through ObservableList; reassign to flush the complete batch.
            if (group != null && Application.isPlaying) group.Renderers = group.Renderers;
        }
    }
}
