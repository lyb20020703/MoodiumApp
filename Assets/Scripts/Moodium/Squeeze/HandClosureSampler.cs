using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;
namespace Moodium.Squeeze
{
    public sealed class HandClosureSampler
    {
        static readonly XRHandJointID[] Tips={XRHandJointID.ThumbTip,XRHandJointID.IndexTip,XRHandJointID.MiddleTip,XRHandJointID.RingTip,XRHandJointID.LittleTip};
        float openLeft,openRight;
        public float? Sample()
        {
            var subsystem=XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRHandSubsystem>();
            if(subsystem==null)return null;
            var left=Read(subsystem.leftHand,ref openLeft);var right=Read(subsystem.rightHand,ref openRight);
            if(!left.HasValue)return right;if(!right.HasValue)return left;return Mathf.Max(left.Value,right.Value);
        }
        static float? Read(XRHand hand,ref float open)
        {
            if(!hand.isTracked||!hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out var wrist)||
                !hand.GetJoint(XRHandJointID.MiddleMetacarpal).TryGetPose(out var middle))return null;
            var palm=(wrist.position+middle.position)*.5f;float distance=0;
            foreach(var id in Tips){if(!hand.GetJoint(id).TryGetPose(out var tip))return null;distance+=Vector3.Distance(tip.position,palm)/5;}
            // Bonus is unavailable until an open hand has actually been observed.
            if(distance>.065f)open=Mathf.Max(open,distance);
            return open>.065f?Mathf.Clamp01(1-distance/open):null;
        }
    }
}
