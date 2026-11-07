using UnityEngine;
namespace Moodium.Squeeze
{
    public sealed class RhythmGuideView
    {
        readonly TableVisuals v;readonly SqueezeModeController mode;readonly TabletopMotePool motes;
        readonly RoundedRectBandMesh ring;readonly Material ringMaterial;
        readonly SpriteRenderer target,splash;readonly SpriteRenderer[] beads=new SpriteRenderer[8];
        readonly Vector3 targetScale,splashScale;
        float hitAt=-100;int lastCombo;
        public RhythmGuideView(TableVisuals visuals,SqueezeModeController controller,TabletopMotePool particles)
        {
            v=visuals;mode=controller;motes=particles;
            target=v.Sprite(v.Root,"Rhythm Target","rhythm_target_outline",new Vector3(0,.0008f,0),v.HalfSize.x*2+.04f,8,v.Accent);
            target.transform.localScale=new Vector3((v.HalfSize.x*2+.04f)/target.sprite.bounds.size.x,(v.HalfSize.y*2+.04f)/target.sprite.bounds.size.y,1);
            targetScale=target.transform.localScale;
            ring=v.Band("Rhythm Incoming Ring","rhythm_ring_band",.0011f,11,out ringMaterial);
            splash=v.Sprite(v.Root,"Rhythm Hit Splash","rhythm_hit_splash",new Vector3(0,.0013f,0),v.HalfSize.x*2+.04f,13,v.Soft);splashScale=splash.transform.localScale;
            for(int i=0;i<8;i++)
            {
                var angle=Mathf.Lerp(27,153,i/7f)*Mathf.Deg2Rad;
                beads[i]=v.Sprite(v.Root,"Combo Bead "+i,"rhythm_combo_bead_empty",new Vector3(Mathf.Cos(angle)*(v.HalfSize.x+.045f),.0016f,-Mathf.Sin(angle)*(v.HalfSize.y+.035f)),.026f,16,v.Accent);
            }
        }
        public void Hit()
        {
            if(mode.Rhythm==null||mode.Rhythm.LastAccuracy<=0)return;
            hitAt=Time.unscaledTime;lastCombo=mode.Rhythm.Combo;
            motes.Emit("squeeze_sparkle",mode.Rhythm.LastRush?46:14,false,0,.8f,.025f);
        }
        public void Tick(bool active,float alpha)
        {
            target.gameObject.SetActive(active);ring.gameObject.SetActive(active);
            foreach(var bead in beads)bead.gameObject.SetActive(active);
            splash.gameObject.SetActive(active&&Time.unscaledTime-hitAt<.35f);
            if(!active)return;
            var phase=(float)mode.RhythmPhase;var scale=1+1.2f*Mathf.Pow(1-phase,1.6f);
            ring.Set((v.HalfSize+Vector2.one*.02f)*scale,-.003f,.006f,1);
            TableVisuals.Tint(ringMaterial,v.Accent,(.25f+.75f*Mathf.Min(1,phase*1.4f))*alpha);
            var window=(float)(mode.Parameters.Window/mode.Parameters.RhythmPeriod);
            var rush=mode.Rhythm!=null&&mode.Rhythm.LastRush&&Time.unscaledTime-hitAt<.2f;
            var color=rush?Color.white:v.Accent;color.a=(phase<window||phase>1-window?1:.6f)*alpha;target.color=color;
            target.transform.localScale=targetScale;
            var age=Time.unscaledTime-hitAt;splash.color=new Color(v.Soft.r,v.Soft.g,v.Soft.b,.9f*(1-Mathf.Clamp01(age/.35f))*alpha);
            splash.transform.localScale=splashScale*Mathf.Lerp(1,1.35f,age/.35f);
            var combo=mode.Rhythm?.Combo??0;var lit=combo>0?(combo-1)%8+1:0;
            for(int i=0;i<8;i++)
            {
                beads[i].sprite=v.Load(i<lit?"rhythm_combo_bead_full":"rhythm_combo_bead_empty");
                var c=v.Accent;c.a=alpha;beads[i].color=c;
                var pulse=i==lit-1&&combo==lastCombo&&age<.25f?age<.13f?Mathf.Lerp(.6f,1.15f,age/.13f):Mathf.Lerp(1.15f,1,(age-.13f)/.12f):1;
                beads[i].transform.localScale=Vector3.one*(.026f/beads[i].sprite.bounds.size.x)*pulse;
            }
        }
        public void Reset(){hitAt=-100;lastCombo=0;}
    }
}
