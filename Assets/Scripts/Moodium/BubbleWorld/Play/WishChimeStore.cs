using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
namespace Moodium.NatureWorld
{
 [Serializable] public sealed class WishChimeRecord
 {
  public string id,world,folder,createdAt;public int layer;public float[] position;public float yaw;public List<List<float[]>> strokes=new();
  // Preset wish id (WishPresetLibrary.Ids) when the paper was written by a preset; null for handwriting. Old files simply lack it.
  public string preset;
 }
 [Serializable] public sealed class WishChimeDocument {public int version=1;public List<WishChimeRecord> chimes=new();}
 public static class WishChimeStore
 {
  static readonly object FileGate=new();
  public static bool Append(string path,WishChimeRecord record){lock(FileGate){var latest=Load(path);latest.chimes.Add(record);return Save(path,latest);}}
  public static string DefaultPath=>Path.Combine(Application.persistentDataPath,"wish_chimes.json");
  public static WishChimeDocument Load(string path)
  {
   try{if(!File.Exists(path))return new();var d=JsonConvert.DeserializeObject<WishChimeDocument>(File.ReadAllText(path));if(d==null||d.version!=1||d.chimes==null)return new();
    d.chimes.RemoveAll(r=>r==null||string.IsNullOrEmpty(r.folder)||r.position==null||r.position.Length!=3||Array.Exists(r.position,x=>float.IsNaN(x)||float.IsInfinity(x))||!float.IsFinite(r.yaw)||r.strokes==null||r.strokes.Exists(s=>s==null||s.Exists(p=>p==null||p.Length!=2||!float.IsFinite(p[0])||!float.IsFinite(p[1]))));return d;
   }catch(Exception e){Debug.LogWarning("[Wish Chime] Read failed; keeping existing file: "+e.Message);return new();}
  }
  public static bool Save(string path,WishChimeDocument document)
  {
   try{Directory.CreateDirectory(Path.GetDirectoryName(path));var temp=path+".tmp";using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){using var writer=new StreamWriter(stream);writer.Write(JsonConvert.SerializeObject(document));writer.Flush();stream.Flush(true);}
    if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);return true;
   }catch(Exception e){Debug.LogWarning("[Wish Chime] Save failed: "+e.Message);return false;}
  }
  public static bool CanRelease(List<List<float[]>> strokes)=>strokes!=null&&strokes.Exists(s=>s!=null&&s.Count>=2);
  public static bool FarEnough(Vector3 candidate,IList<Vector3> occupied){foreach(var p in occupied)if(Vector3.Distance(p,candidate)<.35f)return false;return true;}
  public static Vector2 Smooth(Vector2 previous,Vector2 next)=>Vector2.Lerp(previous,next,.5f);
  public static bool AddPoint(List<float[]> stroke,Vector2 uv,float paperScale=1)
  {
   if(stroke.Count>0){var last=new Vector2(stroke[^1][0],stroke[^1][1]);uv=Smooth(last,uv);if(Vector2.Scale(uv-last,new Vector2(.034f,.11f)*paperScale).magnitude<.0015f)return false;}
   stroke.Add(new[]{Mathf.Clamp01(uv.x),Mathf.Clamp01(uv.y)});return true;
  }
 }
}
