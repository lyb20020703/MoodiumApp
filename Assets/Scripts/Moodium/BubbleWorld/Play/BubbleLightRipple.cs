using Unity.Profiling;
using System;
using System.Collections.Generic;
using UnityEngine;
namespace Moodium.NatureWorld
{
 public sealed class BubbleLightRipple : MonoBehaviour
 {
  static readonly ProfilerMarker s_Profile=new("BubblePlay.BubbleLightRipple");

  Material original,peak,fall;readonly List<Renderer> layers=new();float[] radii;int[] states;float started=-100,last=-100;Action<float> sparkle;Transform scan;AudioSource audioSource;
  public static float Arrival(float radius)=>radius/6;
  public static bool CanTrigger(float now,float previous,bool excluded)=>!excluded&&now-previous>=2.5f;
  public void Initialize(IList<GameObject> objects,float[] distances,Material source,Transform root,bool candy,Action<float> spark)
  {
   original=source;scan=root;radii=distances;states=new int[objects.Count];sparkle=spark;
   foreach(var o in objects)layers.Add(o.GetComponent<Renderer>());
   peak=new Material(source);fall=new Material(source);peak.SetColor("_BaseColor",candy?new Color(1,.86f,.93f):new Color(1,.95f,.82f));fall.SetColor("_BaseColor",candy?new Color(1,.93f,.97f):new Color(1,.98f,.92f));
   audioSource=gameObject.AddComponent<AudioSource>();audioSource.playOnAwake=false;audioSource.spatialBlend=0;audioSource.volume=.6f;audioSource.clip=BubblePlayVisuals.Clip(candy?"ripple_candy":"ripple_nature");
  }
  public bool Trigger(bool excluded)
  {
   if(!CanTrigger(Time.time,last,excluded))return false;started=last=Time.time;audioSource.Play();return true;
  }
  void Update()
  {
   using var timing=s_Profile.Auto();
   if(original==null)return;
   var alpha=original.GetColor("_BaseColor").a;
   foreach(var m in new[]{peak,fall}){var c=m.GetColor("_BaseColor");if(!Mathf.Approximately(c.a,alpha)){c.a=alpha;m.SetColor("_BaseColor",c);}}
   int swaps=0;
   for(int i=0;i<layers.Count&&swaps<3;i++)
   {
    float age=Time.time-started-Arrival(radii[i]*Mathf.Abs(scan.lossyScale.x));int next=age<0?0:age<.18f?1:age<.43f?2:3;
    if(states[i]==next)continue;if(layers[i]!=null)layers[i].sharedMaterial=next==1?peak:next==2?fall:original;
    if(next==1&&i<8)sparkle?.Invoke(radii[i]);states[i]=next;swaps++;
   }
  }
  public void ResetLayers(){for(int i=0;i<layers.Count;i++){if(layers[i]!=null)layers[i].sharedMaterial=original;states[i]=0;}started=-100;}
  void OnDisable(){ResetLayers();if(audioSource!=null)audioSource.Stop();}
  void OnDestroy(){BubblePlayVisuals.Release(peak);BubblePlayVisuals.Release(fall);}
 }
}
