using UnityEngine;
using UnityEngine.Rendering;
using Unity.PolySpatial;
namespace Moodium.NatureWorld
{
 public sealed class BubbleHoldRing:MonoBehaviour
 {
  const int Segments=48;Mesh mesh;Material material;int previous=-1;
  public static BubbleHoldRing Create(Transform parent,bool candy,VisionOSSortingGroup sorting=null)
  {
   var go=new GameObject("Hold to leave ring");go.transform.SetParent(parent,false);
   var ring=go.AddComponent<BubbleHoldRing>();ring.mesh=new Mesh{name="Bubble hold arc"};go.AddComponent<MeshFilter>().sharedMesh=ring.mesh;
   ColorUtility.TryParseHtmlString(candy?"#FFB3D1":"#D9FF8A",out var color);
   ring.material=BubbleWorldData.CreateUnlit("Bubble hold ring",null,false,CullMode.Off,color);
   go.AddComponent<MeshRenderer>().sharedMaterial=ring.material;
   // Separate group keeps the progress arc above the membrane and out of the scan's scale.
   var group=sorting!=null?sorting:go.AddComponent<VisionOSSortingGroup>();group.Renderers.Add(new VisionOSSortingGroup.RendererSorting{Renderer=go,Order=4,ApplyToDescendants=true});
   go.SetActive(false);return ring;
  }
  public void Show(float progress,Vector3 centre,float radius,Camera viewer)
  {
   if(progress<=0||viewer==null){gameObject.SetActive(false);previous=-1;return;}
   gameObject.SetActive(true);transform.position=centre+(viewer.transform.position-centre).normalized*(radius+.004f);
   transform.rotation=Quaternion.LookRotation(viewer.transform.forward,viewer.transform.up);Moodium.UI.UIRefreshVisuals.UnitScale(transform);
   int count=Mathf.Clamp(Mathf.CeilToInt(progress*Segments),1,Segments);
   // Radius can change during parking; rebuild only when its segment count changes.
   transform.localScale*=radius/.08f;
   if(count==previous)return;previous=count;
   var v=new Vector3[(count+1)*2];var t=new int[count*6];
   for(int i=0;i<=count;i++){float a=Mathf.PI*.5f-i*Mathf.PI*2/Segments;var d=new Vector3(Mathf.Cos(a),Mathf.Sin(a),0);v[i*2]=d*(.08f*1.15f-.0015f*.08f/radius);v[i*2+1]=d*(.08f*1.15f+.0015f*.08f/radius);if(i==count)continue;int b=i*2,j=i*6;t[j]=b;t[j+1]=b+1;t[j+2]=b+2;t[j+3]=b+1;t[j+4]=b+3;t[j+5]=b+2;}
   mesh.Clear();mesh.vertices=v;mesh.triangles=t;mesh.RecalculateBounds();
  }
  void OnDestroy(){BubblePlayVisuals.Release(mesh);BubblePlayVisuals.Release(material);}
 }
}
