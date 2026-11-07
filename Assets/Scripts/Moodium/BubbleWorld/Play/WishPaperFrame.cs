using UnityEngine;
namespace Moodium.NatureWorld
{
 public sealed class WishPaperFrame : ScriptableObject
 {
  public Vector3[] grid=new Vector3[225];
  public Vector3 Surface(Vector2 uv)
  {
   float x=(1-Mathf.Clamp01(uv.x))*8,y=Mathf.Clamp01(uv.y)*24;int ix=Mathf.Min(7,(int)x),iy=Mathf.Min(23,(int)y);
   return Vector3.Lerp(Vector3.Lerp(grid[iy*9+ix],grid[iy*9+ix+1],x-ix),Vector3.Lerp(grid[(iy+1)*9+ix],grid[(iy+1)*9+ix+1],x-ix),y-iy);
  }
  public Vector3 RightAt(Vector2 uv){float a=Mathf.Max(0,uv.x-.001f),b=Mathf.Min(1,uv.x+.001f);return (Surface(new Vector2(b,uv.y))-Surface(new Vector2(a,uv.y)))/Mathf.Max(.00001f,b-a);}
  public Vector3 UpAt(Vector2 uv){float a=Mathf.Max(0,uv.y-.001f),b=Mathf.Min(1,uv.y+.001f);return (Surface(new Vector2(uv.x,b))-Surface(new Vector2(uv.x,a)))/Mathf.Max(.00001f,b-a);}
  public Vector3 Right=>Surface(new Vector2(1,0))-Surface(Vector2.zero);
  public Vector3 Up=>Surface(new Vector2(0,1))-Surface(Vector2.zero);
  // Canonical writing UVs run left to right on the visible side after FBX handedness conversion.
  public Vector3 Normal=>Vector3.Cross(Up,Right).normalized;
  public Vector2 Project(Vector3 point){var d=point-Surface(Vector2.zero);return new Vector2(Vector3.Dot(d,Right)/Right.sqrMagnitude,Vector3.Dot(d,Up)/Up.sqrMagnitude);}
 }
}
