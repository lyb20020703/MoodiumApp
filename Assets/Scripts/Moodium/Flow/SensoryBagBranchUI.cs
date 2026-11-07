using System;
using Moodium.MoodCheck;
using TMPro;
using Unity.PolySpatial;
using UnityEngine;

namespace Moodium.Flow
{
    /// <summary>Small secondary controls shared by the two world-selection surfaces.</summary>
    public static class SensoryBagBranchUI
    {
        public static TextMeshPro Text(Transform parent, string name, string value, Vector3 position,
            TMP_FontAsset font, float fontSize = .022f, float width = .5f)
        {
            var root = new GameObject(name, typeof(TextMeshPro));
            root.transform.SetParent(parent, false);
            root.transform.localPosition = position;
            root.transform.localScale = StickerWorldSelectionController.GetCompensatedLocalScale(1, parent.lossyScale);
            var label = root.GetComponent<TextMeshPro>();
            if (font != null) label.font = font;
            label.text = value; label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.sizeDelta = new Vector2(width, .045f);
            label.color = Color.white;
            return label;
        }

        public static MoodiumSpatialButton Button(Transform parent, string name, string text, Vector3 position,
            TMP_FontAsset font, Action pressed, float width = .34f)
        {
            var label = Text(parent, name, text, position, font, .02f, width);
            var collider = label.gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(width, .045f, .025f);
            var button = label.gameObject.AddComponent<MoodiumSpatialButton>();
            button.Configure(label, null, pressed);
            return button;
        }

        public static string EnterLabel(MoodiumWorldDefinition world, bool english) => english
            ? (world != null && world.WorldId == "nature" ? "Enter Nature World" : "Enter Candy World")
            : (world != null && world.WorldId == "nature" ? "进入自然世界" : "进入糖果世界");

        public static void Dispose(GameObject root)
        {
            if (root == null) return;
            root.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(root); else UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
