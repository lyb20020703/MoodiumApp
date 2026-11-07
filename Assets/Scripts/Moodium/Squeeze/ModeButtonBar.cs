using Moodium.Flow;
using Unity.PolySpatial;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;
using Unity.XR.CoreUtils;
namespace Moodium.Squeeze
{
    public sealed class ModeButtonBar
    {
        readonly SqueezeModeController mode;readonly TableVisuals v;
        readonly Transform[] buttons=new Transform[3];
        readonly SpriteRenderer[] icons=new SpriteRenderer[3],labels=new SpriteRenderer[3],glows=new SpriteRenderer[3],ripples=new SpriteRenderer[3],badges=new SpriteRenderer[3],bases=new SpriteRenderer[3];
        readonly BoxCollider[] colliders=new BoxCollider[3];
        readonly bool[] touching=new bool[3];readonly float[] pressed={-100,-100,-100};
        readonly bool[] lastRippleVisible=new bool[3],lastColliderEnabled=new bool[3];
        readonly Color[] lastBaseColor=new Color[3],lastGlowColor=new Color[3],lastIconColor=new Color[3],lastLabelColor=new Color[3],lastBadgeColor=new Color[3];
        readonly Vector3[] lastButtonScale=new Vector3[3];
        readonly float born;
        XROrigin xrOrigin;
        /// <summary>A mode button was pressed (the help card turns to that mode's page).</summary>
        public event System.Action<SqueezeMode> Pressed;
        public ModeButtonBar(TableVisuals visuals,Transform parent,SqueezeModeController controller)
        {
            v=visuals;mode=controller;born=Time.unscaledTime;
            for(int i=0;i<3;i++)
            {
                var index=i;var key=((SqueezeMode)i).ToString().ToLowerInvariant();
                // +Z points toward the viewer, so screen-right is local -X.
                // Keep the same readable orientation, but place the row beyond the
                // far (-Z) bag edge instead of between the user and the bag.
                var node=v.Node(parent,"Mode "+key,new Vector3((1-i)*.085f,0,-v.HalfSize.y-.13f));buttons[i]=node.transform;
                bases[i]=v.Sprite(node.transform,"Glass Puck","mode_btn_base",new Vector3(0,.002f,0),.08f,20);
                glows[i]=v.Sprite(node.transform,"Selected Glow","mode_btn_selected_glow",new Vector3(0,.0017f,0),.08f,17,v.Accent);
                ripples[i]=v.Sprite(node.transform,"Press Ripple","mode_btn_press_ripple",new Vector3(0,.0015f,0),.12f,15,v.Soft);
                icons[i]=v.Sprite(node.transform,"Icon","mode_icon_"+key,new Vector3(0,.0026f,0),.03f,26);
                labels[i]=v.Sprite(node.transform,"Label","mode_label_"+key+"_"+v.Language,new Vector3(0,.002f,.056f),.075f,20);
                badges[i]=v.Sprite(node.transform,"Recommended","rhythm_combo_bead_full",new Vector3(-.027f,.003f,-.025f),.012f,27,v.Accent);
                colliders[i]=node.AddComponent<BoxCollider>();colliders[i].size=new Vector3(.07f,.02f,.07f);
                node.AddComponent<MoodiumSpatialButton>().Configure(null,null,()=>Press(index));
                node.AddComponent<VisionOSHoverEffect>().Type=VisionOSHoverEffect.EffectType.Highlight;
                v.Register(node,30);ripples[i].gameObject.SetActive(false);
            }
        }
        public void Press(int i)
        {
            if(mode.Complete||Time.unscaledTime-born<1||Time.unscaledTime-pressed[i]<.2f)return;
            pressed[i]=Time.unscaledTime;var old=mode.Mode;mode.RequestMode((SqueezeMode)i);
            if(old!=mode.Mode||mode.Pending==(SqueezeMode)i)Pressed?.Invoke((SqueezeMode)i);
        }
        public void Tick(float completionAlpha)
        {
            var age=Time.unscaledTime-born;var alpha=Mathf.SmoothStep(0,1,(age-1)/.4f)*completionAlpha;
            var subsystem=XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRHandSubsystem>();
            for(int i=0;i<3;i++)
            {
                bool selected=mode.Mode==(SqueezeMode)i;var pressAge=Time.unscaledTime-pressed[i];
                var pos=buttons[i].localPosition;pos.y=(selected?.004f:0)-Mathf.Max(0,1-pressAge/.12f)*.003f-(1-Mathf.Clamp01((age-1)/.4f))*.01f;buttons[i].localPosition=pos;
                SetScale(buttons[i],Vector3.one*(selected?1.04f:1),ref lastButtonScale[i]);
                SetColor(bases[i],new Color(1,1,1,alpha),ref lastBaseColor[i]);SetColor(glows[i],WithAlpha(v.Accent,selected?alpha:mode.Pending==(SqueezeMode)i?alpha*.4f:0),ref lastGlowColor[i]);
                SetColor(icons[i],WithAlpha(selected?v.Accent:Color.white,alpha*(selected?1:.55f)),ref lastIconColor[i]);SetColor(labels[i],WithAlpha(selected?v.Accent:new Color(.85f,.83f,.89f),alpha*(selected?1:.6f)),ref lastLabelColor[i]);
                SetColor(badges[i],WithAlpha(v.Accent,mode.Recommended==(SqueezeMode)i?alpha:0),ref lastBadgeColor[i]);
                var rippleVisible=pressAge<.45f;if(rippleVisible!=lastRippleVisible[i]){ripples[i].gameObject.SetActive(rippleVisible);lastRippleVisible[i]=rippleVisible;}
                if(pressAge<.45f){ripples[i].color=WithAlpha(v.Soft,(1-pressAge/.45f)*.9f*alpha);var size=.12f/ripples[i].sprite.bounds.size.x;ripples[i].transform.localScale=Vector3.one*size*Mathf.Lerp(1,1.5f,pressAge/.45f);}
                var colliderEnabled=alpha>.5f&&!mode.Complete;if(colliderEnabled!=lastColliderEnabled[i]){colliders[i].enabled=colliderEnabled;lastColliderEnabled[i]=colliderEnabled;}
                var touch=colliders[i].enabled&&subsystem!=null&&(Touches(subsystem.leftHand,colliders[i])||Touches(subsystem.rightHand,colliders[i]));
                if(touch&&!touching[i])Press(i);touching[i]=touch;
            }
        }
        bool Touches(XRHand hand,Collider collider)
        {
            if(!hand.isTracked||!hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var tip))return false;
            if(xrOrigin==null)xrOrigin=Object.FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
            return TouchesPoint(tip.position,collider,xrOrigin!=null?xrOrigin.Origin.transform:null);
        }
        public static bool TouchesPoint(Vector3 trackingPosition,Collider collider,Transform origin)
        {
            var position=origin!=null?origin.TransformPoint(trackingPosition):trackingPosition;
            return (collider.ClosestPoint(position)-position).sqrMagnitude<.000025f;
        }
        static Color WithAlpha(Color c,float a){c.a=a;return c;}
        static void SetColor(SpriteRenderer r,Color c,ref Color last){if(last==c)return;r.color=c;last=c;}
        static void SetScale(Transform t,Vector3 s,ref Vector3 last){if(last==s)return;t.localScale=s;last=s;}
    }
}
