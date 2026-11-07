using System;
using Moodium.Flow;
using TMPro;
using Unity.PolySpatial;
using UnityEngine;

namespace Moodium.MoodCheck
{
    /// <summary>Layered recommendation artwork with live localized text and sticker motion.</summary>
    public sealed class MoodRecommendationView : MonoBehaviour
    {
        Transform m_Sticker;
        SpriteRenderer m_Glow;
        SpriteRenderer m_Hover;
        MoodiumSpatialButton m_Continue;
        float m_Started;
        VisionOSSortingGroup m_Sorting;
        int m_Order;
        TMP_FontAsset m_Font;
        readonly System.Collections.Generic.List<SpriteRenderer> m_Sprites = new();
        readonly System.Collections.Generic.List<TMP_Text> m_Texts = new();
        readonly System.Collections.Generic.List<float> m_TextAlpha = new();
        readonly System.Collections.Generic.List<Collider> m_Buttons = new();
        bool m_Closing;
        float m_CloseStarted;
        Action m_Accept;
        Vector3 m_StickerScale;

        public void Show(MoodCheckSample sample, bool english, TMP_FontAsset font,
            Action accept, Action retry)
        {
            m_Font = font;
            m_Sprites.Clear();
            m_Texts.Clear();
            m_TextAlpha.Clear();
            m_Buttons.Clear();
            m_Closing = false;
            m_Accept = accept;
            foreach (Transform child in transform)
            {
                child.gameObject.SetActive(false);
                if (child.name.StartsWith("Design ") || child.name == "Find Sticker" || child.name == "Check Again")
                    Destroy(child.gameObject);
            }
            m_Started = Time.unscaledTime;
            m_Sorting = GetComponent<VisionOSSortingGroup>() ?? gameObject.AddComponent<VisionOSSortingGroup>();
            m_Sorting.DepthPass = VisionOSSortingGroup.DepthPassType.PostPass;
            m_Sorting.Renderers.Clear();
            m_Order = 0;
            var world = sample.recommendedWorldId == "candy" ? "candy" : "nature";
            var full = new Vector2(.462f, .566f);
            Sprite("Glass", "rec_card_glass", full, Vector2.zero);
            Sprite("Tint", "rec_card_tint_" + world, full, Vector2.zero);
            Sprite("Rim", "rec_card_rim", full, Vector2.zero);
            m_Glow = Sprite("Glow", "rec_tile_glow_" + world, full, Vector2.zero);
            m_Sticker = Sprite("Sticker", "rec_sticker_tile_" + world,
                Vector2.one * .26f, new Vector2(0f, .075f)).transform;
            m_StickerScale = m_Sticker.localScale;
            Sprite("Badge Background", "rec_chip_bg", full, Vector2.zero);
            Text("Badge Text", english ? "✦ Moodi recommends" : "✦ Moodi 为你推荐", .221f, .0125f);
            Text("World Title", world == "candy" ? (english ? "Candy World" : "糖果世界")
                : (english ? "Nature World" : "自然世界"), -.077f, english ? .029f : .032f, true);
            Text("Short Copy", MoodCheckCopy.Get("rec.short." + sample.mood.ToString().ToLowerInvariant(), english),
                -.117f, .0145f).color = new Color(1f, 1f, 1f, .7f);
            Sprite("CTA Background", "rec_cta_bg", full, Vector2.zero);
            m_Hover = Sprite("CTA Hover", "rec_cta_hover", full, Vector2.zero);
            m_Hover.color = new Color(1f, 1f, 1f, 0f);
            var cta = Text("CTA Text", english ? "Find this sticker  →" : "去找这张贴纸  →", -.18f, .0165f, true);
            cta.color = new Color(.11f, .11f, .13f, 1f);
            m_Continue = Button("Find Sticker", -.18f, new Vector2(.28f, .064f), cta, () =>
            {
                if (m_Closing) return;
                m_Closing = true;
                m_CloseStarted = Time.unscaledTime;
            });
            var retryText = Text("Retry Text", english ? "Check again" : "再测一次", -.237f, .0135f);
            retryText.color = new Color(1f, 1f, 1f, .58f);
            Button("Check Again", -.237f, new Vector2(.14f, .04f), retryText, retry);
            if (string.IsNullOrEmpty(sample.recommendedWorldId))
                Text("Unavailable", english ? "No world available" : "暂无可用世界", -.077f, .029f, true);
            foreach (var text in m_Texts) m_TextAlpha.Add(text.color.a);
            ApplyFade(0f);
        }

        void ApplyFade(float opacity)
        {
            foreach (var sprite in m_Sprites)
            {
                if (sprite == m_Hover) continue;
                var color = sprite.color;
                color.a = opacity * (sprite == m_Glow ? .9f : 1f);
                sprite.color = color;
            }
            for (var i = 0; i < m_Texts.Count; i++)
                m_Texts[i].alpha = opacity * m_TextAlpha[i];
            foreach (var collider in m_Buttons) collider.enabled = opacity > .95f && !m_Closing;
        }

        void Register(GameObject go)
        {
            m_Sorting.Renderers.Add(new VisionOSSortingGroup.RendererSorting
            { Renderer = go, Order = m_Order++, ApplyToDescendants = true });
        }

        SpriteRenderer Sprite(string name, string resource, Vector2 size, Vector2 position)
        {
            var go = new GameObject("Design " + name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(position.x, position.y, -.004f - m_Order * .002f);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = Resources.Load<UnityEngine.Sprite>("MoodiumMoodRecommendation/" + resource);
            if (renderer.sprite != null)
            {
                var bounds = renderer.sprite.bounds.size;
                go.transform.localScale = new Vector3(size.x / bounds.x, size.y / bounds.y, 1f);
            }
            Register(go);
            m_Sprites.Add(renderer);
            return renderer;
        }

        TMP_Text Text(string name, string value, float y, float height, bool bold = false)
        {
            var go = new GameObject("Design " + name, typeof(TextMeshPro));
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, y, -.004f - m_Order * .002f);
            go.transform.localScale = Vector3.one * .03f;
            var text = go.GetComponent<TextMeshPro>();
            text.font = m_Font;
            MoodTextMaterial.Apply(text);
            text.fontSize = height / .0029f;
            text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            text.alignment = TextAlignmentOptions.Center;
            text.rectTransform.sizeDelta = new Vector2(.42f / .03f, .035f / .03f);
            text.text = value;
            text.ForceMeshUpdate();
            Register(go);
            m_Texts.Add(text);
            return text;
        }

        MoodiumSpatialButton Button(string name, float y, Vector2 size, TMP_Text label, Action pressed)
        {
            var go = new GameObject(name, typeof(BoxCollider));
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, y, -.045f);
            go.GetComponent<BoxCollider>().size = new Vector3(size.x, size.y, .02f);
            m_Buttons.Add(go.GetComponent<BoxCollider>());
            var button = go.AddComponent<MoodiumSpatialButton>();
            button.Configure(label, null, pressed);
            var hover = go.AddComponent<VisionOSHoverEffect>();
            hover.Type = VisionOSHoverEffect.EffectType.Highlight;
            hover.IntensityMultiplier = .4f;
            Register(go);
            return button;
        }

        void Update()
        {
            if (m_Sticker == null) return;
            var elapsed = Time.unscaledTime - m_Started;
            var closing = m_Closing ? Mathf.Clamp01((Time.unscaledTime - m_CloseStarted) / .2f) : 0f;
            ApplyFade(Mathf.Clamp01((elapsed - .2f) / .25f) * (1f - closing));
            if (m_Closing && closing >= 1f)
            {
                m_Accept?.Invoke();
                return;
            }
            var appear = Mathf.Clamp01(elapsed / .45f);
            var bounce = 1f - Mathf.Pow(1f - appear, 3f);
            m_Sticker.localScale = m_StickerScale * Mathf.Lerp(.9f, 1f, bounce) * (1f + .06f * closing);
            var wave = Mathf.Sin(elapsed * Mathf.PI * .5f);
            var p = m_Sticker.localPosition;
            p.y = .075f + wave * .003f;
            m_Sticker.localPosition = p;
            m_Sticker.localRotation = Quaternion.Euler(0f, 0f, wave);
            m_Glow.color = new Color(1f, 1f, 1f, (.9f + wave * .1f) * appear * (1f - closing));
            var hovered = false;
#if UNITY_EDITOR
            var mouse = UnityEngine.InputSystem.Mouse.current;
            var camera = Camera.main;
            if (mouse != null && camera != null && Physics.Raycast(camera.ScreenPointToRay(mouse.position.ReadValue()),
                out var hit, 20f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
                hovered = hit.collider.gameObject == m_Continue.gameObject;
#endif
            var color = m_Hover.color;
            color.a = Mathf.MoveTowards(color.a, hovered ? 1f : 0f, Time.unscaledDeltaTime / .15f);
            m_Hover.color = color;
        }
    }
}
