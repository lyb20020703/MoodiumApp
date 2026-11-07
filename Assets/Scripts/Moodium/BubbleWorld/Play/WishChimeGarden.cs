using Unity.Profiling;
using System.Collections.Generic;
using System.Collections;
using System.Threading.Tasks;
using System.Linq;
using TMPro;
using Unity.PolySpatial;
using UnityEngine;
namespace Moodium.NatureWorld
{
 [DefaultExecutionOrder(-10)]
 public sealed class WishChimeGarden : MonoBehaviour
 {
  static readonly ProfilerMarker s_Profile=new("BubblePlay.WishChimeGarden");

  public BubblePlayHands Hands{get;private set;}public Camera Viewer{get;private set;}public bool Candy=>BubbleWorldData.IsCandy(Folder);public string Folder{get;private set;}public int Layer{get;private set;}
  public TMP_FontAsset ChineseFont{get;private set;}public WishChime Writing{get;set;}
  public bool FocusActive{get;private set;}public int WriterHand{get;private set;}=-1;float focusUntil;
  public bool TryKeepOut(out Vector3 centre){if(Writing!=null&&Writing.Strip!=null){centre=Writing.StripCentre;return true;}if(blank!=null&&blank.State==WishChime.Stage.Blank){centre=blank.transform.position;return true;}centre=default;return false;}
  public bool IsWriting=>Writing!=null;
  string storagePath;
  Transform scan;BubbleWorldNarrator narrator;WishChimeDocument document;readonly List<WishChime> chimes=new();WishChime blank;
  float introEnded=-1,nextBlank,nextLod;Vector3 previousHead;bool running,taught,b7,restoring,hadSaved;
  /// <param name="layer">The layer this world was entered as (0 first, 1 next). Null: the old fixed order (棉花糖 / 绿色森林 first).</param>
  public void Initialize(string folder,Transform world,Camera camera,BubblePlayHands hands,BubbleWorldNarrator voice,string persistencePath=null,int? layer=null)
  {
   storagePath=persistencePath??WishChimeStore.DefaultPath;Folder=folder;scan=world;Viewer=camera;Hands=hands;narrator=voice;
   Layer=layer??(folder==BubbleWorldData.CandyFolder||folder==BubbleWorldData.NatureFolder?0:1);
   // Existing world UI font reference, without making an unreadable TMP material copy.
   var fonts=FindFirstObjectByType<Moodium.MoodCheck.MoodCheckFontReferences>(FindObjectsInactive.Include);var manager=FindFirstObjectByType<Moodium.CandyWorld.CandyWorldManager>();ChineseFont=fonts!=null?fonts.Chinese:manager!=null&&manager.BubblePlayChineseFont!=null?manager.BubblePlayChineseFont:TMP_Settings.defaultFontAsset;
  }
  public void Enter()
  {
   running=true;hadSaved=false;introEnded=-1;nextBlank=float.PositiveInfinity;previousHead=Viewer.transform.position;StartCoroutine(LoadSaved());
   if(Folder==BubbleWorldData.NatureFolder){introEnded=Time.time;blank=Create(null);Say("B3");}
  }
  public void Leave(){StopAllCoroutines();FocusActive=false;WriterHand=-1;focusUntil=0;running=false;Writing=null;blank=null;var sorting=scan!=null?scan.GetComponent<VisionOSSortingGroup>():null;foreach(var c in chimes)if(c!=null){BubblePlayVisuals.PruneSorting(sorting,c.transform);BubblePlayVisuals.Release(c.gameObject);}chimes.Clear();}
  IEnumerator LoadSaved()
  {
   restoring=true;var path=storagePath??WishChimeStore.DefaultPath;var task=Task.Run(()=>WishChimeStore.Load(path));
   while(!task.IsCompleted)yield return null;
   document=task.Status==TaskStatus.RanToCompletion?task.Result:new WishChimeDocument();
   var records=document.chimes.Where(r=>r.folder==Folder).OrderBy(r=>Vector3.Distance(scan.TransformPoint(new Vector3(r.position[0],r.position[1],r.position[2])),Viewer.transform.position)).ToArray();
   hadSaved=records.Length>0;
   for(int i=0;i<records.Length;i++){Create(records[i],i>=40);yield return null;}
   restoring=false;
  }
  WishChime Create(WishChimeRecord record,bool lite=false)
  {
   var go=new GameObject(record==null?"Blank Wish Chime":"Saved Wish Chime");go.transform.SetParent(scan,false);
   if(record!=null){go.transform.localPosition=new Vector3(record.position[0],record.position[1],record.position[2]);go.transform.localRotation=Quaternion.Euler(0,record.yaw,0);}
   else go.transform.position=Viewer.transform.position+Vector3.ProjectOnPlane(Viewer.transform.forward,Vector3.up).normalized*.55f-Vector3.up*.25f;
   var sorting=scan.GetComponent<VisionOSSortingGroup>();
   BubblePlayVisuals.PruneSorting(sorting);
   var c=go.AddComponent<WishChime>();c.Initialize(this,record,lite);chimes.Add(c);BubblePlayVisuals.SortingChanged(sorting);return c;
  }
  void Update()
  {
   using var timing=s_Profile.Auto();
   if(!running||Viewer==null)return;
   float speed=(Viewer.transform.position-previousHead).magnitude/Mathf.Max(.001f,Time.deltaTime);previousHead=Viewer.transform.position;
   if(introEnded<0&&(narrator==null||!narrator.IsSpeaking)){introEnded=Time.time;nextBlank=Time.time+2;}
   if(introEnded>=0&&!restoring&&hadSaved&&!b7){b7=true;Say("B7");}
   if(!restoring&&blank==null&&!IsWriting&&introEnded>=0&&Time.time>=nextBlank&&speed<.15f){blank=Create(null);Say("B3");}
   if(Time.time>=nextLod){nextLod=Time.time+2;UpdateNearest();}
   bool flying=chimes.Exists(c=>c!=null&&c.State==WishChime.Stage.Flying);float nearest=float.PositiveInfinity;int nearHand=-1;
   if(blank!=null&&blank.State==WishChime.Stage.Blank)for(int i=0;i<2;i++){var h=Hands.Hands[i];if(h.Valid&&blank.Near(h.Tip,ChimeFocus.HandNearChime)){nearest=0;nearHand=i;}}
   if(Writing!=null&&Writing.LastWriter>=0)WriterHand=Writing.LastWriter;else if(nearHand>=0)WriterHand=nearHand;
   bool raw=ChimeFocus.RawFocus(Writing!=null,flying,blank!=null,nearest);focusUntil=ChimeFocus.NextFocusUntil(raw,Time.time,focusUntil);
   bool focus=ChimeFocus.Active(raw,Time.time,focusUntil);
   if(focus!=FocusActive)Debug.Log(focus?$"[Bubble Play] chime focus on ({(Writing!=null?"writing":flying?"flying":"reach")}) writer={(WriterHand==0?"L":WriterHand==1?"R":"?")}":"[Bubble Play] chime focus off, bubble returning (grace 0.8s)");
   FocusActive=focus;
  }
  void UpdateNearest()
  {
   var sorted=chimes.Where(c=>c!=null&&c.Record!=null&&c.State==WishChime.Stage.Hanging).OrderBy(c=>Vector3.Distance(c.transform.position,Viewer.transform.position)).ToArray();
   for(int i=0;i<sorted.Length;i++)sorted[i].SetFull(i<40);
  }
  public bool Near(Vector3 position){foreach(var c in chimes)if(c!=null&&c.Near(position))return true;return false;}
  public bool CanWrite(WishChime chime)=>Writing==null||Writing==chime;
  public void TeachWriting(){if(!taught&&PlayerPrefs.GetInt("Moodium.WishWritingTaught",0)==0){taught=true;PlayerPrefs.SetInt("Moodium.WishWritingTaught",1);Say("B4");}}
  public void Say(string id)=>narrator?.SayLine(id);
  public Task<bool> PersistAsync(WishChimeRecord record)
  {
   var path=storagePath??WishChimeStore.DefaultPath;
   return Task.Run(()=>WishChimeStore.Append(path,record));
  }
  public void Released(WishChime chime){blank=null;nextBlank=Time.time+20;}
  /// <summary>× while writing: the chime is a blank again (keeping its ink) and the follow bubbles unlock once the hand leaves.</summary>
  public void PutAway(WishChime chime){if(Writing==chime)Writing=null;blank=chime;}
  public bool TryDestination(out Vector3 local)
  {
   var occupied=new List<Vector3>();foreach(var c in chimes)if(c!=null&&c.Record!=null)occupied.Add(c.transform.position);
   var forward=Vector3.ProjectOnPlane(Viewer.transform.forward,Vector3.up).normalized;
   for(int attempt=0;attempt<128;attempt++)
   {
    float angle=UnityEngine.Random.Range(0,360)*Mathf.Deg2Rad;var direction=new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle));if(Vector3.Dot(direction,forward)>Mathf.Cos(30*Mathf.Deg2Rad))continue;
    var p=scan.position+direction*UnityEngine.Random.Range(.8f,1.6f);p.y=Viewer.transform.position.y+UnityEngine.Random.Range(.3f,.8f);
    if(WishChimeStore.FarEnough(p,occupied)){local=scan.InverseTransformPoint(p);return true;}
   }
   local=default;return false;
  }
  void OnDisable()=>Leave();
 }
}
