using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Moodium.Opening
{
    /// <summary>World-space language picker shown during the configured video segment.</summary>
    public sealed class OpeningVideoLanguageChoice : MonoBehaviour
    {
        public bool HasSelection { get; private set; }
        public string SelectedLanguage { get; private set; }
        GameObject m_Chinese;
        GameObject m_English;
        readonly OpeningVideoSpatialChoiceGate m_TransitionGate = new OpeningVideoSpatialChoiceGate();

        float m_SelectedAt = float.NegativeInfinity;
        public bool IsReadyForVideoTransition =>
            Time.unscaledTime-m_SelectedAt >= .3f && m_TransitionGate.IsReadyForVideoTransition(Time.frameCount, false);

        public void Configure(TMP_FontAsset font, Sprite roundedSprite)
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            gameObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
            gameObject.AddComponent<GraphicRaycaster>();
            gameObject.AddComponent<OpeningVideoSpatialUIInputManager>();
            var rect = GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(900f, 340f);

            var question = CreateText("Language Question", "你更希望 Moodi 用哪种语言陪你探索？\nWhich language would you like Moodi to use?", font, 36f);
            question.rectTransform.anchoredPosition = new Vector2(0f, 260f);
            question.rectTransform.sizeDelta = new Vector2(860f, 115f);
            m_Chinese = CreateButton("Language Button - Chinese", new Vector2(-165f, -160f), "中文", "lang_zh");
            m_English = CreateButton("Language Button - English", new Vector2(165f, -160f), "English", "lang_en");
        }

        public void SelectChineseWhenTimedOut() => SelectAutomatically("中文");

        TextMeshProUGUI CreateText(string name, string value, TMP_FontAsset font, float size)
        {
            var item = new GameObject(name, typeof(RectTransform));
            item.transform.SetParent(transform, false);
            var text = item.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.raycastTarget=false;
            text.font = font;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.color = Color.white;
            text.outlineWidth = 0.08f;
            text.outlineColor = new Color(.08f, .02f, .16f, .85f);
            return text;
        }

        // Restored from f8ab1d9 (the first liquid-glass version): a plain world-space canvas Image,
        // no hover effect and no custom material, so nothing but the button art is ever drawn.
        // The art is a 498x282 canvas (capsule 330x114 + soft shadow). Keep that aspect ratio so the
        // capsule and its text are not squashed: 330x187 shows the capsule at 219x76 canvas units.
        GameObject CreateButton(string name, Vector2 position, string value, string assetId)
        {
            var item = new GameObject(name, typeof(RectTransform));
            item.transform.SetParent(transform, false);
            var rect = item.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(330f, 187f);
            rect.anchoredPosition = position;
            var image = item.AddComponent<Image>();
            image.sprite = Resources.Load<Sprite>("MoodiumOpening/opening_btn_" + assetId + "_idle");
            image.type = Image.Type.Simple;
            var spatialButton = item.AddComponent<OpeningVideoSpatialButton>();
            spatialButton.Configure(touchId =>
            {
                if (HasSelection) return;
                image.sprite = Resources.Load<Sprite>("MoodiumOpening/opening_btn_" + assetId + "_pressed");
                SelectFromPress(value, touchId);
            });
            var button = item.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = image;
            button.onClick.AddListener(() => spatialButton.OnPointerClick(null));
            var collider = item.AddComponent<BoxCollider>();
            collider.isTrigger = false;
            // Covers the visible capsule (219x76) with a little margin, not the transparent shadow.
            collider.size = new Vector3(240f, 96f, 2f);
            return item;
        }

        void SelectFromPress(string value, int touchId)
        {
            if (HasSelection)
                return;
            SelectedLanguage = value;
            if (touchId >= 0)
                m_TransitionGate.SelectFromSpatialTouch(Time.frameCount, touchId);
            else
                m_TransitionGate.SelectAutomatically(Time.frameCount);
            m_SelectedAt=Time.unscaledTime;
            HasSelection = true;
            Debug.Log($"[Moodium Opening] Video language selected by {(touchId >= 0 ? "spatial pointer" : "UI click")}: {value}.");
        }

        void SelectAutomatically(string value)
        {
            if (HasSelection)
                return;
            SelectedLanguage = value;
            m_TransitionGate.SelectAutomatically(Time.frameCount);
            HasSelection = true;
            Debug.Log($"[Moodium Opening] Video language selected automatically: {value}.");
        }

    }
}
