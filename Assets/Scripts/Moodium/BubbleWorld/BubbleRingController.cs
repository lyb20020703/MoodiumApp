using System;
using System.Collections.Generic;
using Moodium.Interaction;
using Moodium.Flow;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

namespace Moodium.NatureWorld
{
    public sealed class BubbleRingController : MonoBehaviour
    {
        sealed class Ring { public Vector3 Point; public bool Table, Inside; public float Hold, Away, Cooldown; public SpriteRenderer Visual; public Mesh Fill; public MeshRenderer FillRenderer; public Material FillMaterial; public float Flash; public readonly Vector3[] Vertices = new Vector3[212]; }
        readonly List<Ring> rings=new();
        readonly List<Vector3> steps=new();
        readonly List<Vector3> occupied=new();
        Transform viewer; BubbleWorldController bubble; Ring active;
        bool explore,onHand,help,present,candy,visible=true,saidFloor,saidTable,saidSummon;
        XRHandSubsystem hands; XROrigin origin; Texture2D glow;
        public event Action<string,Vector3> HintRequested;
        public event Action FirstRingShown;
        public int RingCount => rings.Count;
        public void Configure(Transform viewer, BubbleWorldController bubble, bool candy) {this.viewer=viewer;this.bubble=bubble;this.candy=candy;}
        public void SetInteractionState(bool explore,bool moodiOnHand,bool helpOpen,bool bubblePresent) {this.explore=explore;onHand=moodiOnHand;help=helpOpen;present=bubblePresent;}
        public void RecordStep(Vector3 point) {steps.Add(point);if(steps.Count>100)steps.RemoveAt(0);}
        public void SetOccupiedPositions(IEnumerable<Vector3> positions) {occupied.Clear();if(positions!=null)occupied.AddRange(positions);}
        public void OnGardenDiscovered() { if (viewer != null) OnGardenDiscovered(viewer.position); }
        public void OnGardenDiscovered(Vector3 headSnapshot)
        {
            Debug.Log("[World Journey] Bubble rings disabled; waiting for squeeze-bag entry.");
        }
        public void OnWalkInvited(Bounds? bagBounds,float tableHeight,IReadOnlyList<Bounds> obstacles)
        {
            Debug.Log("[World Journey] Bubble rings disabled; waiting for squeeze-bag entry.");
        }
        void Add(Vector3 p,bool table)
        {
            var go=new GameObject(table?"Table Bubble Ring":"Floor Bubble Ring");go.transform.SetParent(transform,false);go.transform.position=p+Vector3.up*.012f;go.transform.rotation=Quaternion.Euler(90,0,0);
            var sr=go.AddComponent<SpriteRenderer>();sr.sprite=Resources.Load<Sprite>("MoodiumWalkTrail/walk_floor_ring");
            float diameter=table?.14f:1f;go.transform.localScale=Vector3.one*(sr.sprite!=null?diameter/sr.sprite.bounds.size.x:diameter);
            if(table) { var collider=go.AddComponent<SphereCollider>(); collider.radius=.08f/Mathf.Max(.001f,go.transform.localScale.x); var button=go.AddComponent<MoodiumSpatialButton>(); button.Configure(null,null,()=>PinchAt(p)); }
            var ring=new Ring{Point=p,Table=table,Visual=sr};
            BuildFeedback(ring,diameter);rings.Add(ring);go.SetActive(visible);
            if(rings.Count==1)FirstRingShown?.Invoke();
            if(table?!saidTable:!saidFloor) {HintRequested?.Invoke(table?"F1t":"F1",p);if(table)saidTable=true;else saidFloor=true;}
        }
        public void SetVisible(bool value) {visible=value;foreach(var r in rings)if(r.Visual!=null)r.Visual.gameObject.SetActive(value);}
        public void NotifyBubbleEntered() {active=null;SetVisible(false);}
        public void NotifyBubbleReturned() {active=null;present=false;foreach(var r in rings){r.Hold=0;r.Cooldown=3;}SetVisible(true);}
        public void PinchAt(Vector3 position) {if(!Allowed)return;foreach(var r in rings)if(r.Table && Vector3.Distance(position,r.Point)<.12f && r.Cooldown<=0){Summon(r);break;}}
        bool Allowed=>visible && active==null && BubbleRingPlacement.CanTrigger(explore,onHand,help,present);
        void Summon(Ring r)
        {
            if(bubble==null||viewer==null)return;
            active=r;present=true;r.Away=0;r.Hold=0;r.Flash=.3f;
            var forward=Vector3.ProjectOnPlane(viewer.forward,Vector3.up).normalized;
            bubble.Summon(r.Point,r.Table?r.Point+Vector3.up*.3f:viewer.position+forward*.5f-Vector3.up*.4f);
            if(!saidSummon){saidSummon=true;HintRequested?.Invoke("F2",r.Point);}
        }
        void Update()
        {
            if(viewer==null)return;
            hands??=XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRHandSubsystem>();
            if(origin==null)origin=FindFirstObjectByType<XROrigin>();
            foreach(var r in rings) {
                r.Cooldown=Mathf.Max(0,r.Cooldown-Time.deltaTime);
                bool inside=r.Table?(PalmIn(hands?.leftHand,r.Point)||PalmIn(hands?.rightHand,r.Point)):BubbleRingPlacement.FloorInside(viewer.position,r.Point,r.Inside);r.Inside=inside;
                if(active==r && explore) {r.Away=inside?0:r.Away+Time.deltaTime;if(r.Away>=(r.Table?8f:1.5f)){bubble.Dismiss();active=null;present=false;r.Cooldown=3.6f;}}
                if(Allowed&&r.Cooldown<=0) {r.Hold=inside?r.Hold+Time.deltaTime:0;if(r.Hold>=(r.Table?1.5f:2f))Summon(r);}else r.Hold=0;
                UpdateFeedback(r);
                if(r.Visual!=null){var c=candy?new Color(.949f,.49f,.714f):new Color(.333f,.769f,.498f);c.a=.7f*(1+.15f*Mathf.Sin(Time.time*2*Mathf.PI/3));r.Visual.color=c*(1+.3f*Mathf.Clamp01(r.Hold/(r.Table?1.5f:2f)));}
            }
#if UNITY_EDITOR
            if(Mouse.current!=null&&Mouse.current.leftButton.wasPressedThisFrame&&Camera.main!=null){var ray=Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());foreach(var r in rings)if(r.Table&&new Plane(Vector3.up,r.Point).Raycast(ray,out var d))PinchAt(ray.GetPoint(d));}
#endif
        }
        // Main ring, inward fill and four billboards share one mesh, material and draw per ring.
        void BuildFeedback(Ring r,float diameter)
        {
            var root=r.Visual.transform;
            var fill=new GameObject("Inward dwell fill");fill.transform.SetParent(root,false);
            fill.transform.localPosition=Vector3.back*.002f;
            r.Fill=new Mesh {name="Bubble ring dwell annulus"};r.Fill.MarkDynamic();
            var triangles=new int[48*12+4*6];
            var uv=new Vector2[212];for(int i=0;i<196;i++)uv[i]=Vector2.one*.5f;
            for(int i=0;i<48;i++){int t=i*6,v=i*2;triangles[t]=v;triangles[t+1]=v+2;triangles[t+2]=v+1;triangles[t+3]=v+1;triangles[t+4]=v+2;triangles[t+5]=v+3;}
            for(int i=0;i<48;i++){int t=288+i*6,v=98+i*2;triangles[t]=v;triangles[t+1]=v+2;triangles[t+2]=v+1;triangles[t+3]=v+1;triangles[t+4]=v+2;triangles[t+5]=v+3;}
            for(int i=0;i<4;i++){int v=196+i*4,t=576+i*6;triangles[t]=v;triangles[t+1]=v+1;triangles[t+2]=v+2;triangles[t+3]=v;triangles[t+4]=v+2;triangles[t+5]=v+3;uv[v]=Vector2.zero;uv[v+1]=Vector2.right;uv[v+2]=Vector2.one;uv[v+3]=Vector2.up;}
            r.Fill.vertices=r.Vertices;r.Fill.uv=uv;r.Fill.triangles=triangles;
            fill.AddComponent<MeshFilter>().sharedMesh=r.Fill;
            r.FillRenderer=fill.AddComponent<MeshRenderer>();
            if(glow==null){glow=new Texture2D(32,32,TextureFormat.RGBA32,false){name="Bubble ring shared soft glow",wrapMode=TextureWrapMode.Clamp};var pixels=new Color[1024];for(int y=0;y<32;y++)for(int x=0;x<32;x++){float d=new Vector2((x-15.5f)/15.5f,(y-15.5f)/15.5f).magnitude;pixels[y*32+x]=new Color(1,1,1,Mathf.Pow(Mathf.Clamp01(1-d),2));}glow.SetPixels(pixels);glow.Apply();}
            r.FillMaterial=BubbleWorldData.CreateUnlit("Bubble ring shared feedback",glow,true,UnityEngine.Rendering.CullMode.Off,Color.white);
            r.FillRenderer.sharedMaterial=r.FillMaterial;
            r.FillRenderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            r.FillRenderer.receiveShadows=false;r.FillRenderer.enabled=true;
            r.Visual.enabled=false;
        }

        void UpdateFeedback(Ring r)
        {
            if(r.Visual==null)return;
            float progress=Mathf.Clamp01(r.Hold/(r.Table?1.5f:2f));
            r.Flash=Mathf.Max(0,r.Flash-Time.deltaTime);
            var color=candy?new Color(.949f,.49f,.714f):new Color(.333f,.769f,.498f);
            float radius=r.Visual.sprite!=null?r.Visual.sprite.bounds.size.x*.5f:.5f;
            r.FillRenderer.enabled=true;
            float inner=radius*(1-(r.Flash>0?1:progress));
            for(int i=0;i<=48;i++){
                float angle=i*Mathf.PI*2/48;var direction=new Vector3(Mathf.Cos(angle),Mathf.Sin(angle),0);
                r.Vertices[i*2]=direction*radius;r.Vertices[i*2+1]=direction*(progress>0||r.Flash>0?inner:radius);
                r.Vertices[98+i*2]=direction*radius;r.Vertices[99+i*2]=direction*(radius*.94f);
            }
            color.a=(.65f+.15f*Mathf.Sin(Time.time*2*Mathf.PI/3))*(r.Flash>0?1.3f:1);
            r.FillMaterial.SetColor("_BaseColor",color);
            for(int i=0;i<4;i++){
                float phase=Mathf.Repeat(Time.time/3f+i*.25f,1);float angle=i*2.39996f;
                var center=new Vector3(Mathf.Cos(angle)*radius*.72f,Mathf.Sin(angle)*radius*.72f,-radius*(.04f+phase*.5f));
                float size=radius*.035f*Mathf.Sin(phase*Mathf.PI);
                var right=r.Visual.transform.InverseTransformDirection(viewer!=null?viewer.right:Vector3.right)*size;
                var up=r.Visual.transform.InverseTransformDirection(viewer!=null?viewer.up:Vector3.up)*size;
                int v=196+i*4;r.Vertices[v]=center-right-up;r.Vertices[v+1]=center+right-up;r.Vertices[v+2]=center+right+up;r.Vertices[v+3]=center-right+up;
            }
            r.Fill.vertices=r.Vertices;r.Fill.RecalculateBounds();
        }

        static void Release(UnityEngine.Object value) {if(value==null)return;if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
        bool PalmIn(XRHand? maybe,Vector3 p)
        {
            if(!maybe.HasValue||!maybe.Value.isTracked)return false;var hand=maybe.Value;
            if(!hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out var w)||!hand.GetJoint(XRHandJointID.MiddleProximal).TryGetPose(out var m))return false;
            var pose=new Pose(Vector3.Lerp(w.position,m.position,.55f),Quaternion.identity);
            return BubbleRingPlacement.PalmInside(PalmPressGestureController.TransformTrackingPose(pose,origin!=null?origin.Origin.transform:null).position,p);
        }
        public void StopAndClear() {foreach(var r in rings)if(r.Visual!=null){Release(r.Fill);Release(r.FillMaterial);Release(r.Visual.gameObject);}Release(glow);glow=null;rings.Clear();steps.Clear();occupied.Clear();active=null;present=false;saidFloor=saidTable=saidSummon=false;}
        void OnDestroy()=>StopAndClear();
    }
}
