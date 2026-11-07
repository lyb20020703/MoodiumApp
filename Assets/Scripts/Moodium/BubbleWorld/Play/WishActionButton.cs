using System;
using System.Collections;
using Moodium.Flow;
using UnityEngine;

namespace Moodium.NatureWorld
{
    /// <summary>
    /// Liquid-glass Rewrite / Release button beside the wish strip (same look as the opening buttons).
    /// Sprite swap only: idle / gaze (editor hover) / pressed / disabled. No VisionOSHoverEffect and no custom
    /// material, for the same reason as UIRefreshVisuals sprite buttons (the system highlight lights the whole quad).
    /// </summary>
    public sealed class WishActionButton : MonoBehaviour
    {
        public const string Folder = "MoodiumBubblePlay/WishButtons/";
        public const float Width = .09f, Height = .031f;
        /// <summary>Horizontal distance from the strip centre to each button centre (strip is 8.5 cm wide while writing).</summary>
        public const float Offset = .12f;
        public const float DisabledAlpha = .45f;

        string m_Prefix;
        Action m_Action;
        Func<bool> m_Blocked;
        SpriteRenderer m_Sprite;
        bool m_Interactable = true, m_Pressed, m_Hover;

        public bool Interactable => m_Interactable;

        public static string Prefix(bool release, bool candy, bool english) =>
            Folder + "wish_btn_" + (release ? "release_" + (candy ? "candy" : "nature") : "rewrite") + "_" + (english ? "en" : "zh");

        public static string Suffix(bool interactable, bool pressed, bool hover) =>
            !interactable ? "_disabled" : pressed ? "_pressed" : hover ? "_gaze" : "_idle";

        /// <summary>
        /// Builds the sprite, collider and spatial button under <paramref name="parent"/>.
        /// <paramref name="blocked"/> returns true while a press must be ignored (a stroke is being drawn, etc.).
        /// </summary>
        public static WishActionButton Create(Transform parent, string name, string prefix, Action action, Func<bool> blocked, int order)
        {
            var renderer = Moodium.UI.UIRefreshVisuals.Sprite(parent, name, prefix + "_idle", Vector3.zero, order);
            var button = renderer.gameObject.AddComponent<WishActionButton>();
            button.m_Sprite = renderer;
            button.m_Prefix = prefix;
            button.m_Action = action;
            button.m_Blocked = blocked;
            // Only the capsule (plus 5 mm) is pressable, not the transparent shadow margin.
            renderer.gameObject.AddComponent<BoxCollider>().size = new Vector3(Width + .01f, Height + .01f, .015f);
            renderer.gameObject.AddComponent<MoodiumSpatialButton>().Configure(null, null, button.Press);
            button.Refresh();
            return button;
        }

#if UNITY_EDITOR
        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse=UnityEngine.InputSystem.Mouse.current;var viewer=Camera.main;
            if(mouse==null||viewer==null)return;
            var ray=viewer.ScreenPointToRay(mouse.position.ReadValue());
            bool hover=Physics.Raycast(ray,out var hit,20)&&hit.collider==GetComponent<Collider>();
            if(m_Hover!=hover)SetHovered(hover);
            if(hover&&mouse.leftButton.wasPressedThisFrame)Press();
#endif
        }
#endif
        public void SetInteractable(bool value)
        {
            if (m_Interactable == value) return;
            m_Interactable = value;
            Refresh();
        }

        /// <summary>Editor mouse hover only; on device RealityKit does not report gaze.</summary>
        public void SetHovered(bool value)
        {
#if UNITY_EDITOR
            m_Hover = value;
            Refresh();
#endif
        }

        public void Press()
        {
            if (!m_Interactable || m_Pressed || (m_Blocked != null && m_Blocked())) return;
            if (Application.isPlaying) StartCoroutine(Feedback());
            m_Action?.Invoke();
        }

        Vector3 m_BaseScale; bool m_Pulsing;

        /// <summary>Two soft 6% swells (0.9 s), used on Release once a preset wish has finished writing.</summary>
        public void Pulse()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || m_Pulsing) return;
            StartCoroutine(PulseRoutine());
        }

        IEnumerator PulseRoutine()
        {
            m_Pulsing = true; m_BaseScale = transform.localScale;
            for (float t = 0; t < .9f; t += Time.deltaTime)
            {
                transform.localScale = m_BaseScale * (1f + .06f * Mathf.Abs(Mathf.Sin(t / .45f * Mathf.PI)));
                yield return null;
            }
            transform.localScale = m_BaseScale; m_Pulsing = false;
        }

        IEnumerator Feedback()
        {
            m_Pressed = true;
            Refresh();
            yield return new WaitForSecondsRealtime(.15f);
            m_Pressed = false;
            Refresh();
        }

        void Refresh()
        {
            if (m_Sprite == null || m_Prefix == null) return;
            var sprite = Moodium.UI.UIRefreshVisuals.Load(m_Prefix + Suffix(m_Interactable, m_Pressed, m_Hover));
            var faded = false;
            if (sprite == null)
            {
                // Rewrite has no _disabled art: fade the idle sprite instead.
                sprite = Moodium.UI.UIRefreshVisuals.Load(m_Prefix + "_idle");
                faded = !m_Interactable;
            }
            m_Sprite.sprite = sprite;
            m_Sprite.color = new Color(1f, 1f, 1f, faded ? DisabledAlpha : 1f);
        }

        void OnDisable()
        {
            StopAllCoroutines();
            if (m_Pulsing) { transform.localScale = m_BaseScale; m_Pulsing = false; }
            m_Pressed = m_Hover = false;
            Refresh();
        }
    }
}
