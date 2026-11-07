using System;
using System.Collections;
using TMPro;
using Unity.PolySpatial;
using UnityEngine;
using Moodium.Flow;

namespace Moodium.UI
{
    public static class UIRefreshVisuals
    {
        static readonly System.Collections.Generic.Dictionary<string,Sprite> s_Sprites=new();
        public static Sprite Load(string id)
        {
            if(s_Sprites.TryGetValue(id,out var sprite)&&sprite!=null)return sprite;
            return s_Sprites[id]=Resources.Load<Sprite>(id.Contains("/") ? id : "MoodiumUIRefresh/" + id);
        }
        public static SpriteRenderer Sprite(Transform parent, string name, string id, Vector3 position, int order)
        {
            var go = new GameObject(name, typeof(SpriteRenderer));
            go.transform.SetParent(parent, false); go.transform.localPosition = position;
            var r = go.GetComponent<SpriteRenderer>(); r.sprite = Load(id); r.sortingOrder = order;
            Register(parent, go, order);
            return r;
        }
        public static void Register(Transform parent, GameObject go, int order)
        {
            var group = parent.GetComponentInParent<VisionOSSortingGroup>();
            if (group != null) group.Renderers.Add(new VisionOSSortingGroup.RendererSorting
                { Renderer = go, Order = order, ApplyToDescendants = false });
        }
        public static void SetOrder(SpriteRenderer renderer, int order)
        {
            if(renderer.sortingOrder==order)return;
            renderer.sortingOrder=order;
            var group=renderer.GetComponentInParent<VisionOSSortingGroup>();
            if(group==null)return;
            for(int i=0;i<group.Renderers.Count;i++)
            {
                var entry=group.Renderers[i];
                if(entry.Renderer!=renderer.gameObject)continue;
                entry.Order=order;group.Renderers[i]=entry;break;
            }
        }
        public static void UnitScale(Transform t) => t.localScale = StickerWorldSelectionController.GetCompensatedLocalScale(1, t.parent != null ? t.parent.lossyScale : Vector3.one);
        public static TextMeshPro Text(Transform parent, string name, TMP_FontAsset font, float pixels, int order)
        {
            var go = new GameObject(name, typeof(TextMeshPro)); go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshPro>(); if (font != null) t.font = font;
            t.fontSize = pixels; t.alignment = TextAlignmentOptions.Center; t.textWrappingMode = TextWrappingModes.NoWrap;
            t.rectTransform.sizeDelta = new Vector2(200, 60);
            // TMP glyphs use 0.1 world units per font point; map design pixels to metres.
            t.transform.localScale = Vector3.one * MoodiumUIRefreshLayout.DesignPxToMeters * 10f;
            t.GetComponent<Renderer>().sortingOrder = order; Register(parent, go, order);
            return t;
        }
        // No VisionOSHoverEffect and no custom material on sprite buttons: the system Highlight lights the
        // whole rectangular sprite quad (a translucent plate around the art), and the gaze-swap shader
        // rendered black on device. The plain sprite is the only thing drawn.
        public static MoodiumSpatialButton Button(SpriteRenderer sprite, Vector2 hit, Action action)
        {
            sprite.gameObject.AddComponent<BoxCollider>().size = new Vector3(hit.x, hit.y, .01f);
            var b = sprite.gameObject.AddComponent<MoodiumSpatialButton>(); b.Configure(null, null, action); return b;
        }
        public static UIRefreshSpriteButton Gear(Transform parent, Vector3 position, Action pressed)
        {
            var root = new GameObject("Selection Settings", typeof(VisionOSSortingGroup));
            root.transform.SetParent(parent, false); root.transform.localPosition = position; UnitScale(root.transform);
            var r = Sprite(root.transform, "Settings Gear", "settings_btn_idle", Vector3.zero, 23);
            var visual = r.gameObject.AddComponent<UIRefreshSpriteButton>(); visual.Configure("settings_btn", pressed);
            Button(r, new Vector2(.06f,.06f), visual.Press);
            return visual;
        }
        /// <summary>
        /// Replay-the-opening button (2026-11-04): the gear's glass circle with a ↺ round a small play mark, so the next
        /// person can see the opening without the app being force-quit. Sprites <c>replay_btn_idle</c> / <c>_pressed</c>.
        /// </summary>
        public static GameObject ReplayButton(Transform parent, Vector3 position, Action pressed)
        {
            var root = new GameObject("Replay Opening", typeof(VisionOSSortingGroup));
            root.transform.SetParent(parent, false); root.transform.localPosition = position; UnitScale(root.transform);
            Moodium.WishJournal.JournalSpriteButton.Create(root.transform, "Replay Glass", "replay_btn", Vector3.zero, 23,
                new Vector2(.06f, .06f), pressed);
            return root;
        }
        public static float Ease(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3);
    }
    public sealed class UIRefreshSpriteButton : MonoBehaviour
    {
        string m_Prefix; Action m_Action; bool m_Open, m_Pressed, m_Hover;
        public void Configure(string prefix, Action pressed) { m_Prefix = prefix; m_Action = pressed; Refresh(); }
        public void SetOpen(bool value) { m_Open = value; Refresh(); }
        public void SetHovered(bool value)
        {
#if UNITY_EDITOR
            m_Hover = value; Refresh();
#endif
        }
        public void ShowPressedFeedback() { if(Application.isPlaying&&!m_Pressed)StartCoroutine(Feedback()); }
        public void Press() { if (m_Pressed) return; if (Application.isPlaying) StartCoroutine(Feedback()); m_Action?.Invoke(); }
        IEnumerator Feedback() { m_Pressed=true; Refresh(); yield return new WaitForSecondsRealtime(.12f); m_Pressed=false; Refresh(); }
        void Refresh() { if (m_Prefix != null) GetComponent<SpriteRenderer>().sprite=UIRefreshVisuals.Load(m_Prefix + (m_Pressed ? "_pressed" : m_Open ? "_open" : m_Hover ? "_gaze" : "_idle")); }
        void OnDisable() { StopAllCoroutines(); m_Pressed=m_Hover=false; Refresh(); }
    }
}
