using TMPro;
using UnityEngine;
using Unity.PolySpatial;
using Moodium.Calibration;
namespace Moodium.UI
{
    public sealed class CalibrationRotateDial : MonoBehaviour
    {
        SpriteRenderer m_Knob,m_Circle,m_Tick,m_Tether,m_Pill; TextMeshPro m_Label;
        float m_Alpha,m_SnapAt=-100; bool m_WasZero; TMP_FontAsset m_Font;
        public Transform Knob => m_Knob != null ? m_Knob.transform : null;
        public void Initialize(SensoryBagCalibrator calibrator,TMP_FontAsset font)
        {
            m_Font=font;
            m_Circle=UIRefreshVisuals.Sprite(transform,"Rotation Circle","calib_rot_circle",Vector3.zero,1);
            m_Tick=UIRefreshVisuals.Sprite(transform,"Rotation Zero Tick","calib_rot_zero_tick",Vector3.zero,2);
            m_Tether=UIRefreshVisuals.Sprite(transform,"Rotation Tether","calib_rot_tether",Vector3.zero,5);
            m_Knob=UIRefreshVisuals.Sprite(transform,"Rotation Knob","calib_rot_knob_idle",Vector3.zero,11);
            m_Knob.gameObject.AddComponent<BoxCollider>().size=new Vector3(.06f,.06f,.01f);
            m_Knob.gameObject.AddComponent<SensoryBagCalibrationHandle>().Configure(calibrator,CalibrationHandleKind.Rotate);
            m_Pill=UIRefreshVisuals.Sprite(transform,"Rotation Angle Pill","calib_rot_pill",Vector3.zero,12);
            m_Label=UIRefreshVisuals.Text(transform,"Rotation Angle",font,16,13);m_Label.fontStyle=FontStyles.Bold;
        }
        public void Refresh(float width,float depth,float yaw,bool dragging,bool nature,Transform viewer)
        {
            if(m_Knob==null)return;
            var normal=-transform.forward;var pos=MoodiumUIRefreshLayout.RotateKnobOnTable(width,depth);
            var basePoint=transform.position+transform.right*pos.x+transform.up*pos.y;
            var point=basePoint+normal*MoodiumUIRefreshLayout.RotateKnobCentreAboveTable;
            var forward=viewer!=null?Vector3.ProjectOnPlane(point-viewer.position,Vector3.up):Vector3.forward;
            var facing=forward.sqrMagnitude>.00001f?Quaternion.LookRotation(forward,Vector3.up):Quaternion.identity;
            m_Knob.transform.SetPositionAndRotation(point,facing);
            m_Knob.sprite=UIRefreshVisuals.Load(dragging?(nature?"calib_rot_knob_active_nature":"calib_rot_knob_active_candy"):"calib_rot_knob_idle");
#if UNITY_EDITOR
            if(!dragging&&Camera.main!=null&&UnityEngine.InputSystem.Mouse.current!=null){var ray=Camera.main.ScreenPointToRay(UnityEngine.InputSystem.Mouse.current.position.ReadValue());if(m_Knob.GetComponent<Collider>().Raycast(ray,out _,20))m_Knob.sprite=UIRefreshVisuals.Load("calib_rot_knob_gaze");}
#endif
            var tetherLength=.05f-.021f;m_Tether.transform.SetPositionAndRotation(basePoint+normal*tetherLength*.5f,facing);
            m_Tether.transform.localScale=new Vector3(1,tetherLength/(64*MoodiumUIRefreshLayout.DesignPxToMeters),1);
            var radius=MoodiumUIRefreshLayout.RotateCircleScale(width,depth);
            m_Circle.transform.localPosition=new Vector3(0,0,-.001f);m_Circle.transform.localRotation=Quaternion.identity;m_Circle.transform.localScale=Vector3.one*radius;
            var zeroDirection=Quaternion.AngleAxis(-yaw,Vector3.up)*(basePoint-transform.position);
            m_Tick.transform.position=transform.position+zeroDirection+normal*.002f;
            m_Tick.transform.rotation=Quaternion.LookRotation(transform.forward,zeroDirection.normalized);
            var zero=Mathf.Abs(yaw)<.001f;if(dragging&&zero&&!m_WasZero)m_SnapAt=Time.unscaledTime;m_WasZero=zero;
            var age=Time.unscaledTime-m_SnapAt;m_Tick.transform.localScale=Vector3.one*(age<.25f?1+.4f*Mathf.Sin(Mathf.Clamp01(age/.25f)*Mathf.PI):1);
            m_Pill.transform.SetPositionAndRotation(point+Vector3.up*.045f,facing);
            m_Label.transform.SetPositionAndRotation(m_Pill.transform.position-facing*Vector3.forward*.001f,facing);
            m_Label.text=MoodiumUIRefreshLayout.FormatYaw(yaw);
            m_Alpha=Application.isPlaying?Mathf.MoveTowards(m_Alpha,dragging?1:0,Time.unscaledDeltaTime/(dragging?.2f:.3f)):(dragging?1:0);
            foreach(var r in new[]{m_Circle,m_Tick,m_Pill}){r.enabled=m_Alpha>0;r.color=new Color(1,1,1,m_Alpha);}m_Label.color=new Color(1,1,1,m_Alpha);
        }
    }
}
