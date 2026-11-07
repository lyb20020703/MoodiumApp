using System;
using Moodium.Interaction;
using UnityEngine;
using UnityEngine.Video;
namespace Moodium.Reality
{
    public sealed class SqueezeProgressDisplayRouter:MonoBehaviour,ISqueezeProgressDisplay
    {
        [SerializeField] bool m_UseMoodiRise=true;
        SqueezeProgressVideoController video;
        public MoodiRiseDisplay Mesh {get;private set;}
        ISqueezeProgressDisplay active;
        ChocolateCapsuleInteraction bound;
        bool nature;
        float progress;
        public event Action<Vector3> Popped;
        public float Squash=>active?.Squash??0;
        public void Configure(Camera c,VideoClip a,VideoClip b)
        {
            video=GetComponent<SqueezeProgressVideoController>()??gameObject.AddComponent<SqueezeProgressVideoController>();
            Mesh=GetComponent<MoodiRiseDisplay>()??gameObject.AddComponent<MoodiRiseDisplay>();
            Mesh.Configure(c,a,b);video.Configure(c,a,b);active=video;
            video.Popped-=Pop;video.Popped+=Pop;Mesh.Popped-=Pop;Mesh.Popped+=Pop;
        }
        void Pop(Vector3 p)=>Popped?.Invoke(p);
        public void Begin(bool n){ResetAndHide();nature=n;active=null;Mesh.Begin(n);}
        public void BindToSoft(ChocolateCapsuleInteraction c)
        {
            if(c==null||bound==c)return;
            if(m_UseMoodiRise)Mesh.BindToSoft(c);
            if(m_UseMoodiRise&&Mesh.Ready){video.ResetAndHide();active=Mesh;}
            else
            {
                active=video;video.Begin(nature);video.BindToSoft(c);
                Debug.LogWarning("[Moodi Emergence] Rise disabled or resources unavailable; using video fallback.");
            }
            bound=c;active.SetProgress(progress);
            Debug.Log($"[Moodi Emergence] {(nature?"nature":"candy")} display = {(active==Mesh?"mesh":"video")}");
        }
        public void SetProgress(float p){progress=p;active?.SetProgress(p);}
        public bool TryGetDomeCenter(out Vector3 c,out Vector3 u){c=Vector3.zero;u=Vector3.up;return active!=null&&active.TryGetDomeCenter(out c,out u);}
        public void Splash(float a)=>active?.Splash(a);
        public void SetBubbleSuspended(bool v)=>active?.SetBubbleSuspended(v);
        public bool TryGetBurstExclusion(out Vector3 c,out float r){c=Vector3.zero;r=0;return active!=null&&active.TryGetBurstExclusion(out c,out r);}
        public bool OverlapsBurstVideo(Bounds b)=>active!=null&&active.OverlapsBurstVideo(b);
        public void ResetAndHide(){video?.ResetAndHide();Mesh?.ResetAndHide();bound=null;progress=0;active=null;}
    }
}
