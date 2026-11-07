using System;
using System.Linq;
using Moodium.Interaction;
using Moodium.Characters;
using UnityEngine;
using UnityEngine.Video;

namespace Moodium.Reality
{
    public sealed class MoodiRiseDisplay : MonoBehaviour, ISqueezeProgressDisplay
    {
        [SerializeField] float m_SmoothTime=.25f, m_MaxSpeed=.3f, m_PopThreshold=.995f;
        [SerializeField] float m_ReboundLift=.07f, m_SurfaceRadius=.015f, m_RiseScale=1f, m_AttachDuration=.12f, m_DetachDuration=.3f;
        // Moodi's largest side while rising, in metres (same 4 cm as the rescue Moodi). Measured from the eye
        // bones, because renderer bounds of the v2 skinned mesh are not reliable for sizing.
        [SerializeField, Range(.02f,.08f)] float m_RiseSize=.04f;
        const float EyeSpanRig=.678f, LargestSideRig=1.904f;   // Moodi_Rig rest pose (Blender), Eye_L-Eye_R and largest side
        [SerializeField] Vector2 m_SpotOffset;
        [SerializeField] float m_BlinkClose=.1f, m_BlinkOpen=.1f, m_ClosedScale=.08f;
        // Touching Moodi itself (not the bag around it) plays its Touched clip instead of the press squash.
        [SerializeField, Range(0f,.04f)] float m_TouchExtraReach=.015f;
        const string TouchedState="Touched";
        bool touching, touchWasEmerging;
        float touchUntil=-100f;
        Transform anchor, rig, visual, rescueRoot, stem;
        Vector3 anchorLocalPosition, anchorLocalUp; Quaternion anchorLocalRotation;
        float attachedAt; Camera viewer;
        bool stageBlink;
        Animator moodi;
        MoodiCompanionAnimator companion;
        Moodium.Flow.MoodiBreathingFloat floating;
        bool floatingEnabled;
        MoodiVoice voice;
        readonly float[] voiceSamples=new float[64];
        bool companionEnabled, popped, suspended, pressed, attached, peeking;
        ChocolateCapsuleInteraction capsule;
        float target, progress, velocity, spring, springVelocity, blink=-100f, blinkAgain=-100f;
        int presses;
        Transform[] eyes;
        Vector3[] eyeScales;
        Vector3 alignedPosition;
        Vector3 originalPosition,originalScale;
        Quaternion originalRotation;
        float boundAt;
        GameObject fallback;
        ParticleSystem particles;
        bool natureWorld;
        bool resourcesAvailable;
        public bool HasStory { get; set; } = true;
        [SerializeField] int m_FallParticles=6,m_PopParticles=14;
        public float Squash=>spring;
        public bool Ready=>resourcesAvailable && anchor!=null;
        public bool Attached=>attached;
        public event Action<Vector3> Popped;
        public void Configure(Camera c, VideoClip a, VideoClip b) { viewer=c; }
        public void Begin(bool nature) { ResetAndHide();natureWorld=nature; }
        public static bool SupportsRise(Animator animator)
        {
            if(animator==null||animator.runtimeAnimatorController==null)return false;
            // Prefab assets have no initialized Animator state machine. Validate the
            // controller on a tiny temporary Animator, without spawning visible Moodi.
            var probe=new GameObject("Rise controller validation");
            try{var a=probe.AddComponent<Animator>();a.runtimeAnimatorController=animator.runtimeAnimatorController;a.Rebind();a.Update(0);return a.HasState(0,Animator.StringToHash("Emerge"));}
            finally{if(Application.isPlaying)Destroy(probe);else DestroyImmediate(probe);}
        }
        public void BindToSoft(ChocolateCapsuleInteraction c)
        {
            if(capsule==c && Ready)return;
            capsule=c;boundAt=Time.time;if(c==null)return;
            var presentation=c.GetComponentInParent<Moodium.Calibration.CalibratedSensoryBagPresentation>();
            anchor=presentation!=null?presentation.transform:c.transform;
            var up=presentation!=null?presentation.AnchorOrientation*Vector3.up:c.transform.up;
            var center=presentation!=null?presentation.CenterWorldPosition:c.transform.position;
            var samples=Resources.Load<SoftSurfaceSamples>("MoodiEmergence/"+(natureWorld?"Nature":"Candy")+"Surface");
            if(Moodium.Flow.SoftModelBounds.TryGetLocalBounds(c.transform,out var bounds))
            {
                center+=c.transform.TransformVector(new Vector3(bounds.size.x*m_SpotOffset.x,0,bounds.size.z*m_SpotOffset.y));
                float best=float.NegativeInfinity;
                if(samples!=null)foreach(var vertex in samples.Points)
                {
                    var point=c.transform.TransformPoint(vertex);var delta=point-center;
                    if(Vector3.ProjectOnPlane(delta,up).sqrMagnitude<=m_SurfaceRadius*m_SurfaceRadius)best=Mathf.Max(best,Vector3.Dot(delta,up));
                }
                if(!float.IsNegativeInfinity(best))center+=up*best;
            }
            anchorLocalPosition=anchor.InverseTransformPoint(center);anchorLocalUp=anchor.InverseTransformDirection(up);
            var camera=viewer!=null?viewer:Camera.main;
            var toward=camera!=null?Vector3.ProjectOnPlane(camera.transform.position-center,up):Vector3.ProjectOnPlane(Vector3.back,up);
            if(toward.sqrMagnitude<.0001f)toward=Vector3.ProjectOnPlane(c.transform.forward,up);
            anchorLocalRotation=Quaternion.Inverse(anchor.rotation)*Quaternion.LookRotation(-toward.normalized,up);
            var prefab=Resources.Load<GameObject>("MoodiEmergence/Moodi");
            var animator=prefab!=null?prefab.GetComponentInChildren<Animator>(true):null;
            resourcesAvailable=samples!=null && SupportsRise(animator);
            if(Ready&&!HasStory)CreateFallback();
        }
        void CreateFallback()
        {
            if(fallback!=null)return;
            var holder=new GameObject("Moodi Rise Fallback");fallback=holder;
            var actor=Instantiate(Resources.Load<GameObject>("MoodiEmergence/Moodi"),holder.transform);
            var renderers=actor.GetComponentsInChildren<Renderer>();var b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);
            actor.transform.localScale*=.04f/Mathf.Max(b.size.x,Mathf.Max(b.size.y,b.size.z));
            AttachMoodi(actor.GetComponentInChildren<Animator>(true));SetPeeking(false);
        }
        public bool TryGetDomeCenter(out Vector3 center,out Vector3 up)
        {center=anchor!=null?anchor.TransformPoint(anchorLocalPosition):Vector3.zero;up=anchor!=null?anchor.TransformDirection(anchorLocalUp).normalized:Vector3.up;return Ready;}
        public bool TryGetBurstExclusion(out Vector3 c,out float r){r=.035f;return TryGetDomeCenter(out c,out _)&&!suspended&&!popped;}
        public bool OverlapsBurstVideo(Bounds b)=>TryGetBurstExclusion(out var c,out var r)&&(b.ClosestPoint(c)-c).sqrMagnitude<r*r;
        public void SetBubbleSuspended(bool value)
        {suspended=value;if(value&&particles!=null)particles.Clear();if(moodi!=null)foreach(var r in moodi.GetComponentsInChildren<Renderer>())r.enabled=!value;}
        public void SetProgress(float p){target=Mathf.Max(target,Mathf.Clamp01(p));}
        public void Splash(float amount){spring=amount;springVelocity=0;Emit(m_FallParticles);}
        void Emit(int count)
        {
            if(!TryGetDomeCenter(out var center,out var up))return;
            if(particles==null)
            {
                var mat=Resources.Load<Material>("MoodiEmergence/"+(natureWorld?"Nature":"Candy")+"Particles");if(mat==null)return;
                var obj=new GameObject("Moodi emergence particles");obj.transform.SetParent(transform,false);
                particles=obj.AddComponent<ParticleSystem>();particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var main=particles.main;main.loop=false;main.playOnAwake=false;main.simulationSpace=ParticleSystemSimulationSpace.World;main.startLifetime=.8f;main.startSize=.006f;main.gravityModifier=.12f;main.maxParticles=32;
                var emission=particles.emission;emission.enabled=false;var shape=particles.shape;shape.enabled=false;
                var fade=particles.colorOverLifetime;fade.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(0,1)});fade.color=gradient;
                obj.GetComponent<ParticleSystemRenderer>().sharedMaterial=mat;
            }
            particles.Play();
            for(int i=0;i<count;i++){var direction=UnityEngine.Random.insideUnitCircle.normalized;particles.Emit(new ParticleSystem.EmitParams{position=center,velocity=up*.4f+Vector3.ProjectOnPlane(new Vector3(direction.x,0,direction.y),up).normalized*.2f},1);}
        }
        public void SetPeeking(bool value)
        {peeking=value;touching=false;if(moodi!=null){moodi.SetBool("Peeking",value);moodi.SetBool("Emerging",!value);}}
        /// <summary>Pure: should the bag press squash Moodi this frame? Not while a hand is on Moodi itself or its
        /// touch reaction is playing, so patting Moodi never reads as pressing it down into the bag.</summary>
        public static bool PressesMoodi(bool bagPinched,bool handOnMoodi,bool touchPlaying)=>bagPinched&&!handOnMoodi&&!touchPlaying;
        /// <summary>Length of the Touched clip on this controller (falls back to 1.2 s).</summary>
        public static float TouchedSeconds(Animator animator)
        {
            var controller=animator!=null?animator.runtimeAnimatorController:null;
            if(controller!=null)foreach(var clip in controller.animationClips)if(clip!=null&&clip.name.EndsWith(TouchedState))return Mathf.Max(.3f,clip.length);
            return 1.2f;
        }
        public bool IsTouchPlaying=>touching;
        // The Peek / Emerge states are entered from Any State while their bools are on, which would cut the Touched
        // clip off at once. Turn them off for the length of the clip, then hand Moodi back to the same state.
        void PlayTouchedOnSoft()
        {
            if(moodi==null)return;
            touchWasEmerging=moodi.GetBool("Emerging");
            moodi.SetBool("Peeking",false);moodi.SetBool("Emerging",false);
            spring=0;springVelocity=0;moodi.SetFloat("Press",0);
            moodi.CrossFadeInFixedTime(TouchedState,.1f,0,0);
            touching=true;touchUntil=Time.time+TouchedSeconds(moodi);
        }
        void EndTouched()
        {
            touching=false;
            if(moodi!=null){moodi.SetBool("Peeking",peeking);moodi.SetBool("Emerging",!peeking&&touchWasEmerging);}
        }
        public void AttachMoodi(Animator animator)
        {
            if(!Ready||animator==null)return;
            moodi=animator;rig=moodi.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Moodi_Rig");
            if(rig==null){moodi=null;return;}
            companion=moodi.GetComponentInParent<MoodiCompanionAnimator>();
            floating=moodi.GetComponentInParent<Moodium.Flow.MoodiBreathingFloat>();
            if(floating!=null){floatingEnabled=floating.enabled;floating.enabled=false;}
            voice=moodi.GetComponentInParent<MoodiVoice>();
            if(companion!=null){companionEnabled=companion.enabled;companion.enabled=false;}
            visual=companion!=null?companion.transform:moodi.transform.parent;
            rescueRoot=visual.parent;
            originalPosition=visual.localPosition;originalScale=visual.localScale;originalRotation=visual.localRotation;
            eyes=new[]{rig.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Eye_L"),rig.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Eye_R")};
            visual.localScale=originalScale*SizeCorrection()*m_RiseScale;
            TryGetDomeCenter(out var center,out var up);
            rescueRoot.SetPositionAndRotation(center,anchor.rotation*anchorLocalRotation);
            FaceViewer(up);
            anchorLocalRotation=Quaternion.Inverse(anchor.rotation)*rescueRoot.rotation;
            alignedPosition=originalPosition-rescueRoot.InverseTransformVector(rig.position-rescueRoot.position);
            attachedAt=Time.time;
            eyeScales=eyes.Select(t=>t!=null?t.localScale:Vector3.one).ToArray();
            attached=true; moodi.SetBool("Emerging",false);moodi.SetBool("Peeking",false);moodi.CrossFadeInFixedTime("FallIn",.1f,0,0);
            stem=rig.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Sprout_Stem");
        }
        // The companion turns its animated model (not its root) to face the viewer while it flies in, and
        // the model's authored front axis is not the root's +Z. So measure where the face actually points
        // (eyes relative to the upper body, like MoodiCompanionAnimator.FindFaceDirection) and yaw the
        // rescue root about the surface normal until that face points at the viewer.
        void FaceViewer(Vector3 up)
        {
            var upper=rig.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Body_Upper");
            if(eyes==null||eyes[0]==null||eyes[1]==null||upper==null)return;
            var face=Vector3.ProjectOnPlane((eyes[0].position+eyes[1].position)*.5f-upper.position,up);
            var camera=viewer!=null?viewer:Camera.main;
            if(camera==null||face.sqrMagnitude<1e-10f)return;
            var toward=Vector3.ProjectOnPlane(camera.transform.position-rescueRoot.position,up);
            if(toward.sqrMagnitude<1e-8f)return;
            rescueRoot.rotation=Quaternion.AngleAxis(Vector3.SignedAngle(face,toward,up),up)*rescueRoot.rotation;
        }
        // Scale factor that makes Moodi's largest side m_RiseSize, measured from the eye span.
        float SizeCorrection()
        {
            if(eyes==null||eyes[0]==null||eyes[1]==null)return 1f;
            var span=Vector3.Distance(eyes[0].position,eyes[1].position);
            if(span<1e-6f)return 1f;
            var largest=span/EyeSpanRig*LargestSideRig;
            var k=m_RiseSize/largest;
            Debug.Log($"[Moodi Rise] measured Moodi size {largest*100f:F1} cm -> {m_RiseSize*100f:F1} cm (x{k:F2})");
            return k;
        }
        public void DetachMoodi(bool immediate=false)
        {
            if(eyes!=null)for(int i=0;i<eyes.Length;i++)if(eyes[i]!=null)eyes[i].localScale=eyeScales[i];
            if(moodi!=null){moodi.SetBool("Emerging",false);moodi.SetBool("Peeking",false);moodi.SetFloat("Press",0);
                moodi.CrossFadeInFixedTime("Happy",.1f);
                if(!immediate && Application.isPlaying && isActiveAndEnabled)StartCoroutine(RestoreSize(visual,originalPosition,originalRotation,originalScale,floating,floatingEnabled));
                else{visual.localPosition=originalPosition;visual.localRotation=originalRotation;visual.localScale=originalScale;if(floating!=null)floating.enabled=floatingEnabled;}}
            if(companion!=null && companionEnabled)companion.ResumeAfterRescue();
            attached=false;touching=false;moodi=null;rig=null;companion=null;voice=null;eyes=null;
        }
        public void Blink(){blink=Time.time;}
        System.Collections.IEnumerator RestoreSize(Transform model,Vector3 position,Quaternion rotation,Vector3 scale,Moodium.Flow.MoodiBreathingFloat floatEffect,bool enableFloat)
        {
            var p=model.localPosition;var r=model.localRotation;var s=model.localScale;
            for(float t=0;t<m_DetachDuration;t+=Time.deltaTime){if(model==null)yield break;var k=Mathf.SmoothStep(0,1,t/m_DetachDuration);model.localPosition=Vector3.Lerp(p,position,k);model.localRotation=Quaternion.Slerp(r,rotation,k);model.localScale=Vector3.Lerp(s,scale,k);yield return null;}
            if(model!=null){model.localPosition=position;model.localRotation=rotation;model.localScale=scale;}
            if(floatEffect!=null)floatEffect.enabled=enableFloat;
        }
        public void NotifyStage(int stage){if(stage==20&&!stageBlink){stageBlink=true;Blink();blinkAgain=Time.time+.3f;}}
        public static float Advance(float current,float target,ref float velocity,float time,float speed,float dt)
            =>Mathf.Max(current,Mathf.SmoothDamp(current,target,ref velocity,time,speed,dt));
        void Update()
        {
            if(!Ready||suspended||popped)return;
            if(!attached && fallback==null && (!HasStory || Time.time-boundAt>8f))
            {
                CreateFallback();
            }
            var handOnMoodi=false;
            if(attached&&companion!=null&&moodi!=null&&companion.PollHandTouch(out handOnMoodi,m_TouchExtraReach)&&!touching)PlayTouchedOnSoft();
            if(touching&&Time.time>=touchUntil)EndTouched();
            var down=PressesMoodi(capsule!=null&&capsule.IsPinched,handOnMoodi,touching);
            if(down&&!pressed){springVelocity+=2.5f;presses++;if(presses==1||presses==5||presses==10||presses==15)Blink();}
            pressed=down;
            spring=SqueezeProgressVideoController.StepSpring(spring,ref springVelocity,down?1:0,down?2500:220,down?.85f:.32f,Time.deltaTime);
            progress=Advance(progress,target,ref velocity,m_SmoothTime,m_MaxSpeed,Time.deltaTime);
            if(moodi!=null){moodi.SetFloat("EmergeProgress",progress);moodi.SetFloat("Press",Mathf.Max(0,spring));}
            if(target>=1&&progress>=m_PopThreshold){popped=true;TryGetDomeCenter(out var center,out _);Emit(m_PopParticles);Moodium.CandyWorld.CandyWorldManager.SpawnSoftSqueezeFeedback(center);Popped?.Invoke(center);if(fallback!=null){DetachMoodi();Destroy(fallback);fallback=null;}}
        }
        void LateUpdate()
        {
            if(!attached||moodi==null)return;
            TryGetDomeCenter(out var center,out var up);
            rescueRoot.SetPositionAndRotation(center,anchor.rotation*anchorLocalRotation);
            var k=Application.isPlaying?Mathf.Clamp01((Time.time-attachedAt)/m_AttachDuration):1f;
            visual.localPosition=Vector3.Lerp(originalPosition,alignedPosition,k);
            // Strain already contains positive lift. Only add the negative rebound.
            if(spring<0)visual.position+=up*(m_ReboundLift/1.83f*.04f*m_RiseScale*spring);
            if(peeking&&stem!=null)
            {
                var amplitude=voice!=null && voice.TryGetOutputLevel(voiceSamples,out var rms)?Mathf.Lerp(2f,14f,Mathf.Clamp01(rms*12f)):2f;
                stem.localRotation*=Quaternion.Euler(0,0,Mathf.Sin(Time.time*7f*2*Mathf.PI)*amplitude);
            }
            if(Time.time>=blinkAgain&&blinkAgain>0){blink=Time.time;blinkAgain=-100;}
            var t=Time.time-blink;
            var close=t>=0&&t<m_BlinkClose?Mathf.Lerp(1,m_ClosedScale,t/m_BlinkClose):t<m_BlinkClose+m_BlinkOpen?Mathf.Lerp(m_ClosedScale,1,(t-m_BlinkClose)/m_BlinkOpen):1;
            for(var i=0;i<eyes.Length;i++)if(eyes[i]!=null)eyes[i].localScale=Vector3.Scale(eyeScales[i],new Vector3(1,close,1));
        }
        public void ResetAndHide()
        {
            StopAllCoroutines();DetachMoodi(true);target=progress=velocity=spring=springVelocity=0;popped=suspended=pressed=peeking=false;presses=0;stageBlink=false;blink=blinkAgain=-100f;
            if(fallback!=null){Destroy(fallback);fallback=null;}
            if(particles!=null)particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            capsule=null;anchor=null;
        }
        void OnDisable()=>ResetAndHide();
    }
}
