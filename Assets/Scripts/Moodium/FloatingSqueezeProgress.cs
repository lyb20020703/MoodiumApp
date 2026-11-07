using TMPro;
using Unity.PolySpatial;
using UnityEngine;
using Moodium.UI;

namespace Moodium.CandyWorld
{
    /// <summary>Passive model-anchored energy ring. At 100% it plays its full-ring sparkle, holds briefly, then fades
    /// out and hides, so the space above the model is clear once the bubble world and the Shower are on offer.</summary>
    public sealed class FloatingSqueezeProgress : MonoBehaviour
    {
        /// <summary>How long a full ring stays (sparkle wave ≈ 0.9 s, then "100%" readable), and its fade-out.</summary>
        public const float FullHoldSeconds = 1.6f, FullFadeSeconds = .6f;

        /// <summary>Ring opacity target: hidden after a full hold; dims to 0.6 when no press for 8 s; otherwise 1.</summary>
        public static float AlphaTarget(float sinceFull, float sincePress) =>
            sinceFull >= 0 ? (sinceFull < FullHoldSeconds ? 1f : 0f) : sincePress > 8f ? .6f : 1f;

        Transform m_Target; Camera m_Camera; Renderer[] m_TargetRenderers;
        float m_Progress, m_LastPress, m_FullAt=-1, m_ShownAt, m_Alpha=1; bool m_HiddenFull;
        Color m_World; TMP_FontAsset m_Chinese, m_English; TextMeshPro m_Number;
        SpriteRenderer m_Halo; readonly SpriteRenderer[] m_Dots=new SpriteRenderer[40], m_Gems=new SpriteRenderer[4];
        readonly SpriteRenderer[,] m_Ripples=new SpriteRenderer[4,2];
        readonly float[] m_DotAt=new float[40], m_GiftAt=new float[4];
        public int LitCount => MoodiumUIRefreshLayout.EnergyLitCount(m_Progress);
        public string DisplayText => m_Number != null ? m_Number.text : "";
        public void ConfigureFonts(TMP_FontAsset chinese, TMP_FontAsset english) { m_Chinese=chinese; m_English=english; UpdateFont(); }
        void UpdateFont() { if(m_Number!=null) { var f=PlayerPrefs.GetString("Moodium.Language","Chinese")=="English"?m_English:m_Chinese; if(f!=null)m_Number.font=f; } }
        public void Bind(Transform target, Camera viewer, bool nature)
        {
            m_Target=target; m_Camera=viewer; m_TargetRenderers=target!=null?target.GetComponentsInChildren<Renderer>(true):System.Array.Empty<Renderer>();
            m_World=nature?MoodiumUIRefreshLayout.Nature:MoodiumUIRefreshLayout.Candy;
            if(m_Halo==null) Build();
            m_HiddenFull=false; m_ShownAt=m_LastPress=Time.unscaledTime; m_FullAt=m_Progress>=1?Time.unscaledTime:-1; m_Alpha=1;
            for(int i=0;i<40;i++)m_DotAt[i]=-100;
            for(int i=0;i<4;i++)m_GiftAt[i]=-100;
            UpdateFont(); UIRefreshVisuals.UnitScale(transform); gameObject.SetActive(true); Refresh(Time.unscaledTime);
        }
        void Build()
        {
            gameObject.AddComponent<VisionOSSortingGroup>();
            m_Halo=UIRefreshVisuals.Sprite(transform,"Energy Halo","energy_halo",new Vector3(0,0,.001f),1);
            for(int i=0;i<40;i++)m_Dots[i]=UIRefreshVisuals.Sprite(transform,"Energy Dot "+i,"energy_dot_off",MoodiumUIRefreshLayout.EnergyDotPosition(i),2);
            for(int i=0;i<4;i++)
            {
                m_Gems[i]=UIRefreshVisuals.Sprite(transform,"Energy Gift "+i,"energy_gem_empty",MoodiumUIRefreshLayout.EnergyGemPosition(i),5);
                for(int j=0;j<2;j++)m_Ripples[i,j]=UIRefreshVisuals.Sprite(transform,"Gift Ripple "+i+"-"+j,"energy_ripple",MoodiumUIRefreshLayout.EnergyGemPosition(i),6);
            }
            m_Number=UIRefreshVisuals.Text(transform,"Energy Percent",m_Chinese,30,7); m_Number.fontStyle=FontStyles.Bold;
        }
        public void NotifyPressed() { m_LastPress=Time.unscaledTime; }
        public void SetProgress(float progress)
        {
            var old=m_Progress; m_Progress=Mathf.Clamp01(progress); var now=Time.unscaledTime;
            for(int i=MoodiumUIRefreshLayout.EnergyLitCount(old);i<LitCount;i++)m_DotAt[i]=now;
            for(int i=0;i<4;i++)if(!MoodiumUIRefreshLayout.EnergyGiftReached(i,old)&&MoodiumUIRefreshLayout.EnergyGiftReached(i,m_Progress))m_GiftAt[i]=now;
            if(m_Progress>old)NotifyPressed();
            if(old<1&&m_Progress>=1)m_FullAt=now; else if(m_Progress<1)m_FullAt=-1;
            // Energy fell below full again after the ring hid itself: bring it back.
            if(m_HiddenFull&&m_Progress<1&&m_Target!=null){m_HiddenFull=false;m_Alpha=0;m_ShownAt=now;gameObject.SetActive(true);}
            if(m_Halo!=null)Refresh(now);
        }
        void Refresh(float now)
        {
            var head=MoodiumUIRefreshLayout.EnergyHeadIndex(m_Progress);
            for(int i=0;i<40;i++)
            {
                var lit=i<LitCount; UIRefreshVisuals.SetOrder(m_Dots[i],i==head?4:lit?3:2); m_Dots[i].sprite=UIRefreshVisuals.Load(i==head?"energy_dot_head":lit?"energy_dot_on":"energy_dot_off");
                var c=i==head?MoodiumUIRefreshLayout.EnergyHeadColor(m_World):lit?m_World:Color.white; c.a*=m_Alpha; m_Dots[i].color=c;
                float scale=1+.8f*(1-UIRefreshVisuals.Ease((now-m_DotAt[i])/.35f));
                if(i==head)scale*=1+.075f*(1+Mathf.Sin(now*Mathf.PI*2/1.6f));
                if(m_FullAt>=0) { var age=now-m_FullAt-i*.8f/40; if(age>=0&&age<.12f)scale*=1+.4f*Mathf.Sin(age/.12f*Mathf.PI); }
                m_Dots[i].transform.localScale=Vector3.one*scale;
            }
            for(int i=0;i<4;i++)
            {
                var full=MoodiumUIRefreshLayout.EnergyGiftReached(i,m_Progress); m_Gems[i].sprite=UIRefreshVisuals.Load(full?"energy_gem_full":"energy_gem_empty");
                var c=full?m_World:Color.white;c.a*=m_Alpha;m_Gems[i].color=c;var age=now-m_GiftAt[i];
                m_Gems[i].transform.localScale=Vector3.one*(age>=0&&age<.5f?1+.5f*Mathf.Sin(age/.5f*Mathf.PI):1);
                for(int j=0;j<2;j++){var t=(age-j*.12f)/.7f;var r=m_Ripples[i,j];r.enabled=t>=0&&t<1;r.transform.localScale=Vector3.one*Mathf.Lerp(.5f,1.6f,UIRefreshVisuals.Ease(t));var rc=m_World;rc.a=.8f*(1-Mathf.Clamp01(t))*m_Alpha;r.color=rc;}
            }
            m_Halo.color=new Color(1,1,1,m_Alpha);m_Number.color=new Color(1,1,1,m_Alpha);
            m_Number.text=Mathf.RoundToInt(m_Progress*100)+"<size=47%><color=#FFFFFFB8>%</color></size>";
        }
        void LateUpdate()=>Tick(Time.unscaledTime,Time.unscaledDeltaTime);
        // Drive the actual visibility lifecycle with an explicit clock for editor verification.
        void Tick(float now,float delta)
        {
            if(m_Target==null||!m_Target.gameObject.activeInHierarchy){gameObject.SetActive(false);return;}
            var b=new Bounds(m_Target.position,Vector3.zero);foreach(var r in m_TargetRenderers)if(r!=null&&r.enabled&&r.gameObject.activeInHierarchy)b.Encapsulate(r.bounds);
            transform.position=new Vector3(b.center.x,b.max.y+.16f,b.center.z);
            if(m_Camera!=null){var forward=transform.position-m_Camera.transform.position;forward.y=0;if(forward.sqrMagnitude>.0001f)transform.rotation=Quaternion.LookRotation(forward,Vector3.up);}
            var target=AlphaTarget(m_FullAt>=0?now-m_FullAt:-1,now-m_LastPress);
            m_Alpha=Mathf.MoveTowards(m_Alpha,target,delta/(target<1?FullFadeSeconds:.2f));
            if(target<=0&&m_Alpha<=0){m_HiddenFull=true;gameObject.SetActive(false);return;}
            UIRefreshVisuals.UnitScale(transform);transform.localScale*=Mathf.Lerp(.9f,1,UIRefreshVisuals.Ease((now-m_ShownAt)/.4f));Refresh(now);
        }
    }
}
