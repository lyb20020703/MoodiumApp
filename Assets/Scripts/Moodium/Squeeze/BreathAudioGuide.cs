using System.Collections;
using Moodium.Characters;
using Moodium.NatureWorld;
using UnityEngine;
namespace Moodium.Squeeze
{
    [DisallowMultipleComponent]
    public sealed class BreathAudioGuide : MonoBehaviour
    {
        [SerializeField, Range(0,1)] float m_Volume=.2f;
        AudioSource source;
        Coroutine transition;
        float envelope;
        BubbleWorldNarrator narrator;
        public void Inhale()=>Play(Resources.Load<AudioClip>("MoodiumSqueezeModes/breath_inhale"));
        public void Exhale()=>Play(Resources.Load<AudioClip>("MoodiumSqueezeModes/breath_exhale"));
        void Play(AudioClip clip)
        {
            if(!Application.isPlaying||clip==null)return;
            if(source==null)
            {
                source=gameObject.AddComponent<AudioSource>();source.playOnAwake=false;source.loop=false;
                source.volume=0;source.spatialBlend=0;source.pitch=1;source.priority=128;
                narrator=GetComponent<BubbleWorldNarrator>();
            }
            if(transition!=null)StopCoroutine(transition);
            transition=StartCoroutine(Change(clip));
        }
        IEnumerator Change(AudioClip clip)
        {
            var start=envelope;
            if(source.isPlaying)
                for(float t=0;t<.1f;t+=Time.unscaledDeltaTime){envelope=Mathf.Lerp(start,0,t/.1f);yield return null;}
            source.Stop();source.clip=clip;envelope=0;source.Play();
            for(float t=0;t<.12f;t+=Time.unscaledDeltaTime){envelope=Mathf.Clamp01(t/.12f);yield return null;}
            envelope=1;transition=null;
        }
        void Update()
        {
            if(source==null)return;
            float target=m_Volume*((MoodiVoice.IsAnyoneSpeaking||(narrator!=null&&narrator.IsSpeaking)) ? .25f : 1f)*envelope;
            source.volume=Mathf.MoveTowards(source.volume,target,Time.unscaledDeltaTime*2);
        }
        public void StopAudio()
        {
            if(transition!=null)StopCoroutine(transition);transition=null;envelope=0;
            if(source!=null){source.Stop();source.volume=0;source.clip=null;}
        }
        void OnDisable()=>StopAudio();
        void OnDestroy(){if(source!=null)Destroy(source);}
    }
}
