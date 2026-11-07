using Unity.Profiling;
using System;
using System.Collections.Generic;
using UnityEngine;
namespace Moodium.NatureWorld
{
 [DefaultExecutionOrder(-20)]
 public sealed class BubbleWorldPlay : MonoBehaviour
 {
  static readonly ProfilerMarker s_Profile=new("BubblePlay.BubbleWorldPlay");

  public readonly BubblePlayHands Hands=new();
  WishChimeGarden garden;
  public bool ChimeFocus=>active&&garden!=null&&(garden.FocusActive||garden.IsWriting);
  public int WriterHand=>garden!=null?garden.WriterHand:-1;
  public bool TryChimeKeepOut(out Vector3 centre){centre=default;return active&&garden!=null&&garden.TryKeepOut(out centre);}
  BubbleLightRipple ripple;BubbleCatchFall catchFall;BubbleWorldNarrator narrator;
  Func<Vector3,bool> blocked;string folder;bool active;float metricAt,worst,sum;int frames;
  /// <param name="layer">0 = the world the user entered first, 1 = its next layer (either world of the family can be either).</param>
  public void Initialize(string world,int layer,Transform scan,Camera viewer,IList<GameObject> layers,float[] radii,Material material,Action<float> sparkle,Func<Vector3,bool> exclusion)
  {
   folder=world;blocked=exclusion;narrator=FindFirstObjectByType<BubbleWorldNarrator>();
   ripple=gameObject.AddComponent<BubbleLightRipple>();ripple.Initialize(layers,radii,material,scan,BubbleWorldData.IsCandy(folder),sparkle);
   var catchKind=CatchKindFor(folder);
   if(catchKind.HasValue){catchFall=gameObject.AddComponent<BubbleCatchFall>();catchFall.Initialize(Hands,viewer,catchKind.Value);}
   garden=gameObject.AddComponent<WishChimeGarden>();garden.Initialize(folder,scan,viewer,Hands,narrator,null,layer);
   gameObject.SetActive(false);
  }
  /// <summary>What can be caught in a world: snow (雪原), chocolate and mint (薄荷巧克力), maple leaves (枫林); none elsewhere.</summary>
  public static CatchKind? CatchKindFor(string world)=>world==BubbleWorldData.NestedNatureFolder?CatchKind.Snow
   :world==BubbleWorldData.NestedCandyFolder?CatchKind.Candy:world==BubbleWorldData.MapleNatureFolder?CatchKind.Maple:(CatchKind?)null;
  public void SetInside(bool value)
  {
   if(active==value)return;Hands.Reset();active=value;gameObject.SetActive(value);if(value)garden.Enter();
  }
  void Update(){
   using var timing=s_Profile.Auto();   float ms=Time.unscaledDeltaTime*1000;worst=Mathf.Max(worst,ms);sum+=ms;frames++;
   if(Time.unscaledTime-metricAt>5){Debug.Log($"[Bubble Play] {folder} writing={garden.IsWriting} frames={frames} avg={sum/Mathf.Max(1,frames):0.0}ms worst={worst:0.0}ms");metricAt=Time.unscaledTime;frames=0;worst=sum=0;}
   Hands.Sample();foreach(var h in Hands.Hands)if(h.Valid&&h.Started)ripple.Trigger((blocked?.Invoke(h.Tip)==true)||garden.IsWriting||garden.Near(h.Tip));}
 }
}
