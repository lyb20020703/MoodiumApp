using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Moodium.Opening
{
    /// <summary>
    /// One press endpoint shared by editor UI clicks and PolySpatial pointers.
    /// The first accepted press wins so a simulated click and a spatial pointer
    /// cannot submit the same choice twice.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OpeningVideoSpatialButton : MonoBehaviour, IPointerClickHandler
    {
        Action<int> m_OnPressed;
        bool m_Consumed;

        public void Configure(Action<int> onPressed)
        {
            m_OnPressed = onPressed;
        }

        public void PressFromSpatialPointer(int touchId)
        {
            Press(touchId);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            Press(-1);
        }

        void Press(int touchId)
        {
            if (m_Consumed)
                return;
            m_Consumed = true;
            m_OnPressed?.Invoke(touchId);
        }
    }
}
