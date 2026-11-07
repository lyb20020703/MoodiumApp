using System.Linq;
using UnityEngine;

namespace Moodium.Reality
{
    // Runs after the Animator so the short landing blink is not overwritten by Happy.
    public sealed class MoodiLandingBlink : MonoBehaviour
    {
        Transform[] eyes;
        Vector3[] scales;
        float startsAt;
        public static void Play(GameObject actor)
        {
            var blink=actor.GetComponent<MoodiLandingBlink>()??actor.AddComponent<MoodiLandingBlink>();
            blink.eyes=actor.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Eye_L"||t.name=="Eye_R").ToArray();
            blink.scales=blink.eyes.Select(t=>t.localScale).ToArray();
            blink.startsAt=Time.time+.3f;blink.enabled=true;
        }
        public static float ScaleAt(float t)
        {
            if(t<0 || t>=.2f)return 1f;
            return t<.1f?Mathf.Lerp(1f,.08f,t/.1f):Mathf.Lerp(.08f,1f,(t-.1f)/.1f);
        }
        void LateUpdate()
        {
            var t=Time.time-startsAt;
            if(t<0)return;
            for(int i=0;i<eyes.Length;i++)if(eyes[i]!=null)eyes[i].localScale=Vector3.Scale(scales[i],new Vector3(1,ScaleAt(t),1));
            if(t>=.2f)enabled=false;
        }
    }
}
