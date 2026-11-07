using Unity.Profiling;
using System.Collections.Generic;
using UnityEngine;
namespace Moodium.NatureWorld
{
 /// <summary>What falls: snowflakes (雪原), chocolate chips and mint shards (薄荷巧克力), or maple leaves (枫林).</summary>
 public enum CatchKind{Snow,Candy,Maple}

 /// <summary>
 /// Pure rules for the falling maple leaves (枫林 2026-11-03). A leaf drifts down slower than snow, swinging side to side
 /// like a pendulum and tilting with the swing; caught on an open palm it rests for a moment, then the wind lifts it off
 /// and it drifts away and fades: something set down, then let go.
 /// </summary>
 public static class MapleLeafFall
 {
  public const string TextureFolder="MoodiumNature/VineLeaves/";
  public static readonly string[] Textures={"vine_leaf_maple_a","vine_leaf_maple_b"};
  public const float MinSize=.055f,MaxSize=.08f,MinSpeed=.07f,MaxSpeed=.12f,SwingMetres=.09f,SwingSeconds=2.2f,Tilt=35f;
  public const float RestSeconds=2.4f,DriftSeconds=1.6f,DriftUp=.25f,DriftAway=.35f;
  public const int LeafCount=30;
  /// <summary>Sideways swing (metres) and tilt (degrees) of a falling leaf <paramref name="time"/> s into its fall.</summary>
  public static (float offset,float tilt) Swing(float time,float phase){var s=Mathf.Sin(2f*Mathf.PI*time/SwingSeconds+phase);return(s*SwingMetres,-s*Tilt);}
  /// <summary>Where a released leaf is (relative to the palm it left) and how big it still is, <paramref name="t"/> s after the rest ended.</summary>
  public static (Vector3 offset,float scale) Drift(float t,Vector3 away)
  {
   var k=Mathf.Clamp01(t/DriftSeconds);var e=k*k*(3f-2f*k);
   away.y=0f;away=away.sqrMagnitude<1e-6f?Vector3.forward:away.normalized;
   return(Vector3.up*DriftUp*e+away*DriftAway*e,1f-e);
  }
  public static bool Gone(float age)=>age>=RestSeconds+DriftSeconds;
 }

 public sealed class BubbleCatchFall : MonoBehaviour
 {
  static readonly ProfilerMarker s_Profile=new("BubblePlay.BubbleCatchFall");

  sealed class Bit {public GameObject Object;public Vector3 Base,Offset,Scale,Away,Rest;public Quaternion Rotation;public float Speed,Age,Phase,Fall;public int Hand=-1;}
  readonly List<Bit> bits=new();readonly List<Material> materials=new();readonly List<Mesh> meshes=new();BubblePlayHands hands;Camera viewer;CatchKind kind;bool candy;float spawnAt;AudioSource sound;
  public CatchKind Kind=>kind;
  public int Count=>bits.Count;
  public void Initialize(BubblePlayHands input,Camera camera,bool isCandy)=>Initialize(input,camera,isCandy?CatchKind.Candy:CatchKind.Snow);
  public void Initialize(BubblePlayHands input,Camera camera,CatchKind what)
  {
   hands=input;viewer=camera;kind=what;candy=what==CatchKind.Candy;var audioObject=new GameObject("Catch Audio");audioObject.transform.SetParent(transform,false);sound=audioObject.AddComponent<AudioSource>();sound.playOnAwake=false;sound.spatialBlend=1;sound.volume=.5f;
   if(kind==CatchKind.Maple){BuildLeaves();return;}
   var prefab=Resources.Load<GameObject>("MoodiumBubblePlay/CatchBits");if(prefab==null)return;
   var snow=BubblePlayVisuals.Material("#F2FBFF");var chocolate=BubblePlayVisuals.Material("#5A3524");var mint=BubblePlayVisuals.Material("#BFF2DD",.85f);materials.AddRange(new[]{snow,chocolate,mint});
   var parts=prefab.GetComponentsInChildren<Transform>(true);
   for(int i=0;i<36;i++)
   {
    var name=candy?(i%2==0?"ChocoChip":"MintShard"):"Snowflake";var part=System.Array.Find(parts,t=>t.name==name);if(part==null)continue;
    var go=Instantiate(part.gameObject,transform);foreach(var r in go.GetComponentsInChildren<Renderer>()){r.sharedMaterial=!candy?snow:i%2==0?chocolate:mint;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;}
    go.transform.localScale=part.lossyScale;go.transform.rotation=part.rotation;go.SetActive(false);bits.Add(new Bit{Object=go,Scale=go.transform.localScale,Rotation=go.transform.rotation,Phase=Random.value*Mathf.PI*2});
   }
  }

  /// <summary>Thin double-sided quads with the red / orange maple leaf textures (no collider: they never take a pinch).</summary>
  void BuildLeaves()
  {
   var quad=new Mesh{name="Maple Leaf Quad"};
   quad.vertices=new[]{new Vector3(-.5f,0f,-.5f),new Vector3(.5f,0f,-.5f),new Vector3(-.5f,0f,.5f),new Vector3(.5f,0f,.5f)};
   quad.uv=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(0,1),new Vector2(1,1)};
   quad.triangles=new[]{0,2,1,2,3,1};quad.RecalculateNormals();quad.RecalculateBounds();meshes.Add(quad);
   var leafMaterials=new List<Material>();
   foreach(var name in MapleLeafFall.Textures)
   {
    var texture=Resources.Load<Texture2D>(MapleLeafFall.TextureFolder+name);
    if(texture==null){Debug.LogWarning("[Bubble Play] Missing maple leaf texture "+name);continue;}
    var m=BubbleWorldData.CreateUnlit("Bubble Play Maple Leaf",texture,true,UnityEngine.Rendering.CullMode.Off,Color.white);materials.Add(m);leafMaterials.Add(m);
   }
   if(leafMaterials.Count==0)return;
   for(int i=0;i<MapleLeafFall.LeafCount;i++)
   {
    var go=new GameObject("Maple Leaf "+i);go.transform.SetParent(transform,false);
    go.AddComponent<MeshFilter>().sharedMesh=quad;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=leafMaterials[i%leafMaterials.Count];r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
    var size=Random.Range(MapleLeafFall.MinSize,MapleLeafFall.MaxSize);go.transform.localScale=new Vector3(size,1f,size);
    var rotation=Quaternion.Euler(0f,Random.Range(0f,360f),0f);go.transform.rotation=rotation;go.SetActive(false);
    bits.Add(new Bit{Object=go,Scale=go.transform.localScale,Rotation=rotation,Phase=Random.value*Mathf.PI*2});
   }
  }

  void Update()
  {
   using var timing=s_Profile.Auto();
   if(viewer==null)return;int[] count=new int[2];foreach(var b in bits)if(b.Object.activeSelf&&b.Hand>=0)count[b.Hand]++;
   var maple=kind==CatchKind.Maple;
   if(Time.time>=spawnAt){spawnAt=Time.time+(maple?.45f:.3f);var b=bits.Find(x=>!x.Object.activeSelf);if(b!=null){var d=Random.insideUnitCircle*1.2f;b.Base=viewer.transform.position+new Vector3(d.x,1,d.y);b.Hand=-1;b.Age=0;b.Fall=0;b.Speed=maple?Random.Range(MapleLeafFall.MinSpeed,MapleLeafFall.MaxSpeed):Random.Range(.12f,.22f);b.Object.transform.localScale=b.Scale;b.Object.transform.rotation=b.Rotation;b.Object.SetActive(true);}}
   foreach(var b in bits)
   {
    if(!b.Object.activeSelf)continue;float dt=Time.deltaTime;
    if(b.Hand>=0)
    {
     if(maple){UpdateCaughtLeaf(b,count,dt);continue;}
     var h=hands.Hands[b.Hand];if(!h.Valid||Vector3.Dot(h.Up,Vector3.up)<=.6f){count[b.Hand]--;b.Hand=-1;b.Base=b.Object.transform.position;continue;}
     b.Age+=dt;b.Object.transform.position=h.Palm+Vector3.up*.015f+b.Offset;b.Object.transform.rotation=Quaternion.FromToRotation(Vector3.up,h.Up)*b.Rotation;
     if(!candy)b.Object.transform.localScale=b.Scale*Mathf.Max(0,1-b.Age/2.5f);
     if(b.Age>=(candy?3:2.5f)){if(candy)Moodium.CandyWorld.CandyWorldManager.SpawnSoftSqueezeFeedback(b.Object.transform.position);b.Object.SetActive(false);count[b.Hand]--;b.Hand=-1;}
    }
    else
    {
     b.Base+=Vector3.down*b.Speed*dt;
     if(maple)
     {
      b.Fall+=dt;var (offset,tilt)=MapleLeafFall.Swing(b.Fall,b.Phase);var side=b.Rotation*Vector3.right;
      b.Object.transform.position=b.Base+side*offset;b.Object.transform.rotation=Quaternion.AngleAxis(tilt,b.Rotation*Vector3.forward)*b.Rotation;
     }
     else{b.Object.transform.position=b.Base+new Vector3(Mathf.Sin(Time.time*2.5f+b.Phase)*.03f,0,0);b.Object.transform.Rotate(Vector3.up,dt*18,Space.World);}
     if(b.Base.y<viewer.transform.position.y-1.2f){b.Object.SetActive(false);continue;}
     for(int i=0;i<2;i++){var h=hands.Hands[i];if(h.Valid&&BubblePlayHands.CanCatch(h.Palm,h.Up,b.Object.transform.position,count[i])){b.Hand=i;b.Age=0;b.Offset=Random.insideUnitSphere*.015f;count[i]++;sound.transform.position=h.Palm;sound.PlayOneShot(BubblePlayVisuals.Clip(CatchClip(kind,Random.Range(0,3))));break;}}
    }
   }
  }

  /// <summary>A caught leaf rests on the palm, then the wind lifts it off and away (it no longer follows the hand).</summary>
  void UpdateCaughtLeaf(Bit b,int[] count,float dt)
  {
   b.Age+=dt;
   if(b.Age<MapleLeafFall.RestSeconds)
   {
    var h=hands.Hands[b.Hand];
    if(!h.Valid||Vector3.Dot(h.Up,Vector3.up)<=.6f){count[b.Hand]--;b.Hand=-1;b.Base=b.Object.transform.position;b.Fall=0;return;}
    b.Object.transform.position=h.Palm+Vector3.up*.012f+b.Offset;b.Object.transform.rotation=Quaternion.FromToRotation(Vector3.up,h.Up)*b.Rotation;
    b.Rest=b.Object.transform.position;b.Away=b.Rest-viewer.transform.position;
    return;
   }
   if(MapleLeafFall.Gone(b.Age)){b.Object.SetActive(false);count[b.Hand]--;b.Hand=-1;return;}
   var (offset,scale)=MapleLeafFall.Drift(b.Age-MapleLeafFall.RestSeconds,b.Away);
   b.Object.transform.position=b.Rest+offset;b.Object.transform.localScale=b.Scale*scale;
   b.Object.transform.Rotate(Vector3.up,dt*60f,Space.World);
  }

  /// <summary>Resources/MoodiumBubblePlay/Audio clip for a catch: catch_snow_N, catch_choco_N or catch_maple_N (N = 0..2).</summary>
  public static string CatchClip(CatchKind what,int variant)=>(what==CatchKind.Candy?"catch_choco_":what==CatchKind.Maple?"catch_maple_":"catch_snow_")+Mathf.Clamp(variant,0,2);

  public void Clear(){foreach(var b in bits){b.Object.SetActive(false);b.Hand=-1;}}
  void OnDisable(){Clear();if(sound!=null)sound.Stop();}
  void OnDestroy(){foreach(var m in materials)BubblePlayVisuals.Release(m);foreach(var m in meshes)BubblePlayVisuals.Release(m);}
 }
}
