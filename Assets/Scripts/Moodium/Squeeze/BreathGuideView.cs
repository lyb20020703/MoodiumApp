using UnityEngine;
namespace Moodium.Squeeze
{
    public sealed class BreathGuideView
    {
        readonly TableVisuals v;readonly SqueezeModeController mode;readonly TabletopMotePool motes;
        readonly RoundedRectBandMesh tide,track,trace;readonly Material tideMaterial,trackMaterial,traceMaterial;
        readonly RoundedRectBandMesh[] ripples=new RoundedRectBandMesh[3];readonly Material[] rippleMaterials=new Material[3];
        readonly float[] rippleAt={-100,-100,-100};readonly SpriteRenderer[] heads=new SpriteRenderer[2];
        float emitBudget,lastSpread=.012f,releaseSpread=.012f,flashAt=-100;int rippleCount;bool flashed;
        public float Intensity {get;private set;}
        public BreathGuideView(TableVisuals visuals,SqueezeModeController controller,TabletopMotePool particles)
        {
            v=visuals;mode=controller;motes=particles;
            tide=v.Band("Breath Tide","breath_tide_band",.0004f,4,out tideMaterial);
            track=v.Band("Breath Contour Track","rhythm_ring_band",.0008f,8,out trackMaterial);
            trace=v.Band("Breath Contour Progress","rhythm_ring_band",.0009f,9,out traceMaterial);
            for(int i=0;i<2;i++)heads[i]=v.Sprite(v.Root,"Breath Head "+i,"breath_trace_head",Vector3.zero,.016f,10,Color.white);
            for(int i=0;i<3;i++)ripples[i]=v.Band("Breath Exhale Ripple "+i,"rhythm_ring_band",.0006f+i*.00002f,6,out rippleMaterials[i]);
        }
        public void Released(SqueezeSample sample)
        {
            releaseSpread=Mathf.Lerp(.012f,.065f,Ease((float)(sample.HoldSeconds/mode.Parameters.Inhale)));
            rippleCount=1;rippleAt[0]=Time.unscaledTime;flashed=false;
        }
        public void Tick(bool active,float alpha)
        {
            tide.gameObject.SetActive(active);track.gameObject.SetActive(active);trace.gameObject.SetActive(active);
            foreach(var head in heads)head.gameObject.SetActive(active);
            if(!active){foreach(var ripple in ripples)ripple.gameObject.SetActive(false);return;}
            var held=mode.IsHeld;
            var exhaling=!held&&double.IsFinite(mode.SinceRelease);
            var progress=held?Mathf.Clamp01((float)(mode.HeldSeconds/mode.Parameters.Inhale)):
                exhaling?1-Mathf.Clamp01((float)(mode.SinceRelease/mode.Parameters.ExhaleOk)):0;
            Intensity=Ease(progress);
            lastSpread=held?Mathf.Lerp(.012f,.065f,Intensity):exhaling?Mathf.Lerp(releaseSpread,.012f,Ease((float)(mode.SinceRelease/mode.Parameters.ExhaleOk))):.012f+.004f*(.5f+.5f*Mathf.Sin(Time.unscaledTime*Mathf.PI*.5f));
            tide.Set(v.HalfSize,.004f,lastSpread,1);track.Set(v.HalfSize,.018f,.004f,1);trace.Set(v.HalfSize,.017f,.006f,progress);
            TableVisuals.Tint(tideMaterial,v.Accent,Mathf.Lerp(.35f,.9f,Intensity)*alpha);
            TableVisuals.Tint(trackMaterial,v.Soft,.15f*alpha);
            if(held&&progress>=1&&!flashed){flashed=true;flashAt=Time.unscaledTime;motes.Emit("breath_mote",12,false,0,.8f,.012f);}
            if(held&&progress<.1f)flashed=false;
            TableVisuals.Tint(traceMaterial,Time.unscaledTime-flashAt<.15f?Color.white:v.Accent,alpha*.95f);
            for(int i=0;i<2;i++)
            {
                var point=RoundedRectBandMesh.Outline(v.HalfSize,Mathf.PI*.5f+(i==0?-1:1)*Mathf.PI*progress,.020f);
                point.y=.001f;heads[i].transform.localPosition=point;heads[i].color=new Color(1,1,1,alpha*.95f);
            }
            if(exhaling&&rippleCount<3&&mode.SinceRelease>=rippleCount*1.2){rippleAt[rippleCount++]=Time.unscaledTime;}
            for(int i=0;i<3;i++)
            {
                var age=(Time.unscaledTime-rippleAt[i])/2.4f;var visible=age<1&&age>=0;ripples[i].gameObject.SetActive(visible);
                if(visible){ripples[i].Set(v.HalfSize,Mathf.Lerp(.005f,.12f,Ease(age)),.004f,1);TableVisuals.Tint(rippleMaterials[i],v.Soft,.6f*(1-age)*alpha);}
            }
            emitBudget+=Time.unscaledDeltaTime*(held?15:exhaling&&mode.SinceRelease<mode.Parameters.ExhaleOk?9:0);
            var count=Mathf.Min(8,Mathf.FloorToInt(emitBudget));emitBudget-=count;
            if(count>0)motes.Emit(held?"breath_mote":v.World=="nature"?"breath_mote_seed":"breath_mote_sugar",count,held,lastSpread,held?1.4f:2.6f,held?.010f:.030f);
        }
        public void Reset(){emitBudget=0;rippleCount=0;flashed=false;flashAt=-100;for(int i=0;i<3;i++)rippleAt[i]=-100;}
        static float Ease(float t)=>.5f-.5f*Mathf.Cos(Mathf.Clamp01(t)*Mathf.PI);
    }
}
