using UnityEngine;
using Moodium.Characters;
namespace Moodium.Flow
{
    public static class SoftModelBounds
    {
        public static bool IsSoftBodyRenderer(Renderer renderer) => renderer is MeshRenderer &&
            renderer.GetComponentInParent<MoodiCompanionAnimator>()==null &&
            renderer.GetComponentInParent<MoodiVoice>()==null;

        public static bool TryGetLocalBounds(Transform root,out Bounds result)
        {
            result=default;bool found=false;
            foreach(var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if(!IsSoftBodyRenderer(r))continue;
                var b=r.localBounds;var matrix=root.worldToLocalMatrix*r.localToWorldMatrix;
                for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
                {
                    var p=matrix.MultiplyPoint3x4(b.center+Vector3.Scale(b.extents,new Vector3(x,y,z)));
                    if(found)result.Encapsulate(p);else{result=new Bounds(p,Vector3.zero);found=true;}
                }
            }
            return found;
        }
    }
}
