using UnityEngine;
namespace Moodium.Squeeze
{
    public sealed class TabletopMotePool
    {
        sealed class Mote {public SpriteRenderer Sprite;public Vector3 From,To;public float Born,Life,Height;public bool Inward;}
        readonly Mote[] pool=new Mote[80];readonly TableVisuals v;
        int next;
        public TabletopMotePool(TableVisuals visuals)
        {
            v=visuals;
            for(int i=0;i<pool.Length;i++)
            {
                var sprite=v.Sprite(v.Root,"Table Mote "+i,"breath_mote",Vector3.zero,.008f,28,v.Soft);
                sprite.gameObject.SetActive(false);pool[i]=new Mote{Sprite=sprite};
            }
        }
        public void Emit(string key,int count,bool inward,float spread=0,float life=1.4f,float maxHeight=.012f)
        {
            for(int i=0;i<count;i++)
            {
                var mote=pool[next++%pool.Length];var angle=Random.Range(0,Mathf.PI*2);
                var outer=RoundedRectBandMesh.Outline(v.HalfSize,angle,Random.Range(.07f,.16f));
                var inner=RoundedRectBandMesh.Outline(v.HalfSize,angle,.004f+spread);
                mote.From=inward?outer:RoundedRectBandMesh.Outline(v.HalfSize,angle,.014f);
                mote.To=inward?inner:RoundedRectBandMesh.Outline(v.HalfSize,angle,Random.Range(.11f,.18f));
                mote.Born=Time.unscaledTime;mote.Life=life;mote.Height=maxHeight;mote.Inward=inward;
                mote.Sprite.sprite=v.Load(key);mote.Sprite.gameObject.SetActive(true);
                var width=key=="breath_mote_seed"?.018f:key=="squeeze_sparkle"?Random.Range(.008f,.018f):.01f;
                mote.Sprite.transform.localScale=Vector3.one*width/mote.Sprite.sprite.bounds.size.x;
                mote.Sprite.transform.localRotation=Quaternion.Euler(90,180,Random.Range(0,360));
            }
        }
        public void Tick(float fade)
        {
            var camera=Camera.main;
            foreach(var mote in pool)
            {
                if(!mote.Sprite.gameObject.activeSelf)continue;
                var t=(Time.unscaledTime-mote.Born)/mote.Life;
                if(t>=1){mote.Sprite.gameObject.SetActive(false);continue;}
                var eased=1-(1-t)*(1-t);var pos=Vector3.Lerp(mote.From,mote.To,eased);
                pos.y=.002f+Mathf.Sin(t*Mathf.PI)*mote.Height;
                mote.Sprite.transform.localPosition=pos;
                if(camera!=null)
                {
                    // Only motes turn toward the viewer; table text and guide frame stay fixed.
                    var toward=v.Root.InverseTransformPoint(camera.transform.position)-pos;toward.y=0;
                    if(toward.sqrMagnitude>.0001f)mote.Sprite.transform.localRotation=Quaternion.LookRotation(toward.normalized,Vector3.up);
                }
                var c=v.Soft;c.a=(mote.Inward?Mathf.Sin(t*Mathf.PI):1-t)*fade;mote.Sprite.color=c;
            }
        }
        public void Clear(){foreach(var mote in pool)mote.Sprite.gameObject.SetActive(false);}
    }
}
