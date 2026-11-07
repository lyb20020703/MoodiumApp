using UnityEngine;
namespace Moodium.Squeeze
{
    public sealed class TableTextSlot
    {
        readonly TableVisuals v;
        readonly SpriteRenderer word,hint,toast;
        readonly SpriteRenderer[] dots=new SpriteRenderer[9];
        string lastWord,lastHint;
        float wordSince,toastSince=-100;
        public TableTextSlot(TableVisuals visuals,Transform parent)
        {
            v=visuals;
            word=v.Sprite(parent,"Breath Word","breath_text_inhale_"+v.World+"_"+v.Language,new Vector3(0,.0021f,v.HalfSize.y+.04f),.18f,20);
            hint=v.Sprite(parent,"Guide Hint","guide_hint_ready_"+v.World+"_"+v.Language,new Vector3(0,.0022f,v.HalfSize.y+.08f),.24f,21);
            toast=v.Sprite(parent,"Mode Toast","mode_toast_free_"+v.World+"_"+v.Language,new Vector3(0,.0023f,v.HalfSize.y+.04f),.14f,22);
            // Positive X tilt lays text toward the tabletop; 60 degrees means
            // its far edge is 30 degrees above the table, fixed to the UI frame.
            foreach(var text in new[]{word,hint,toast})text.transform.localRotation=Quaternion.Euler(60,180,0);
            for(int i=0;i<9;i++)dots[i]=v.Sprite(parent,"Breath Count "+i,"breath_count_dot_empty",
                new Vector3((i-4)*.012f,.0018f,v.HalfSize.y+.064f),.008f,18,v.Accent);
            Clear();
        }
        public void ShowWord(string key,float intensity)
        {
            if(lastWord!=key){lastWord=key;word.sprite=v.Load("breath_text_"+key+"_"+v.World+"_"+v.Language);wordSince=Time.unscaledTime;}
            word.gameObject.SetActive(key!=null);
            word.color=new Color(1,1,1,Mathf.SmoothStep(0,1,(Time.unscaledTime-wordSince)/.4f)*Mathf.Lerp(.75f,1,intensity));
        }
        public void ShowHint(string key)
        {
            if(lastHint!=key){lastHint=key;if(key!=null)hint.sprite=v.Load("guide_hint_"+key+"_"+v.World+"_"+v.Language);}
            hint.gameObject.SetActive(key!=null);
        }
        public void Count(int count,bool show){for(int i=0;i<9;i++){dots[i].gameObject.SetActive(show);dots[i].sprite=v.Load(i<count?"breath_count_dot_full":"breath_count_dot_empty");}}
        public void Toast(SqueezeMode mode){toast.sprite=v.Load("mode_toast_"+mode.ToString().ToLowerInvariant()+"_"+v.World+"_"+v.Language);toastSince=Time.unscaledTime;}
        public void Tick(bool breathBusy)
        {
            var age=Time.unscaledTime-toastSince;toast.gameObject.SetActive(age<1.4f&&!breathBusy);
            toast.color=new Color(1,1,1,age<.2f?age/.2f:1-Mathf.Clamp01((age-1)/.4f));
        }
        public void Clear(){word.gameObject.SetActive(false);hint.gameObject.SetActive(false);toast.gameObject.SetActive(false);foreach(var dot in dots)dot.gameObject.SetActive(false);lastWord=lastHint=null;}
    }
}
