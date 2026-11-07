using UnityEngine;
using Moodium.UI;
namespace Moodium.Opening
{
    public sealed class OpeningChoiceSpriteFeedback : MonoBehaviour
    {
        string m_Id; bool m_Pressed;
        public void Configure(string id){m_Id=id;Refresh(false);}
        public void SetPressed(){m_Pressed=true;Refresh(false);}
        void Refresh(bool hover)=>GetComponent<SpriteRenderer>().sprite=UIRefreshVisuals.Load("MoodiumOpening/opening_btn_"+m_Id+(m_Pressed?"_pressed":hover?"_gaze":"_idle"));
#if UNITY_EDITOR
        void Update()
        {
            var mouse=UnityEngine.InputSystem.Mouse.current;var camera=Camera.main;if(mouse==null||camera==null)return;
            var ray=camera.ScreenPointToRay(mouse.position.ReadValue());
            bool hover=Physics.Raycast(ray,out var hit,20)&&hit.collider==GetComponent<Collider>();Refresh(hover);
            if(hover&&mouse.leftButton.wasPressedThisFrame)GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
        }
#endif
    }
}
