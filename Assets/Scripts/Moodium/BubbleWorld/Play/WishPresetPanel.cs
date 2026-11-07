using System;
using System.Collections;
using System.Collections.Generic;
using Moodium.Flow;
using UnityEngine;

namespace Moodium.NatureWorld
{
    /// <summary>
    /// The column of preset chips on the left of the enlarged wish strip: five wishes and "write my own".
    /// Liquid-glass sprites, same recipe and pixels-per-metre as WishActionButton. Sprite swap only
    /// (idle / gaze / pressed / selected); no VisionOSHoverEffect, no custom material.
    /// </summary>
    public sealed class WishPresetPanel : MonoBehaviour
    {
        // Layout in metres, in the viewer-facing plane, relative to the strip centre (x right, y up).
        public const float ColumnX = -WishActionButton.Offset;          // chips left, buttons right
        public const float ChipWidth = .0933f, ChipHeight = .0237f;
        public const float ChipPitch = .034f, WriteOwnExtraGap = .009f;
        /// <summary>Pressable margin around each capsule (each side gets half).</summary>
        public const float ChipPad = .008f;
        public const int ChipCount = 6;
        /// <summary>Release (+) and Rewrite (-) are stacked on the right of the strip.</summary>
        public const float ButtonY = .026f;

        /// <summary>Vertical offset of chip <paramref name="index"/> (0 = top, 5 = write my own). The column is centred.</summary>
        public static float ChipY(int index)
        {
            var total = (ChipCount - 1) * ChipPitch + WriteOwnExtraGap;
            return total / 2f - index * ChipPitch - (index == ChipCount - 1 ? WriteOwnExtraGap : 0f);
        }

        readonly List<WishPresetChip> m_Chips = new();
        public IReadOnlyList<WishPresetChip> Chips => m_Chips;

        /// <summary>
        /// Builds the six chips under <paramref name="parent"/>. Each chip holder is placed at
        /// <paramref name="centre"/> + right * ColumnX + up * ChipY and faces <paramref name="forward"/>.
        /// </summary>
        public static WishPresetPanel Create(Transform parent, Vector3 centre, Vector3 right, Vector3 up, Vector3 forward,
            bool candy, string lang, Action<string> pick, Action writeOwn, Func<bool> blocked, int order)
        {
            var root = new GameObject("Wish Presets");
            root.transform.SetParent(parent, false);
            var panel = root.AddComponent<WishPresetPanel>();
            for (int i = 0; i < ChipCount; i++)
            {
                var id = i < WishPresetLibrary.Ids.Length ? WishPresetLibrary.Ids[i] : WishPresetLibrary.WriteOwnId;
                var holder = new GameObject(id).transform;
                holder.SetParent(root.transform, false);
                holder.position = centre + right * ColumnX + up * ChipY(i);
                holder.rotation = Quaternion.LookRotation(forward, Vector3.up);
                Moodium.UI.UIRefreshVisuals.UnitScale(holder);
                Action action = id == WishPresetLibrary.WriteOwnId ? writeOwn : () => pick(id);
                panel.m_Chips.Add(WishPresetChip.Create(holder, id, lang, candy, action, blocked, order));
            }
            return panel;
        }

        /// <summary>Highlights the chip of <paramref name="id"/>; null clears the selection.</summary>
        public void Select(string id)
        {
            foreach (var chip in m_Chips) chip.SetSelected(chip.Id == id);
        }
    }

    public sealed class WishPresetChip : MonoBehaviour
    {
        string m_Prefix;
        Action m_Action;
        Func<bool> m_Blocked;
        SpriteRenderer m_Sprite;
        bool m_Candy, m_Selected, m_Pressed, m_Hover;

        public string Id { get; private set; }
        public bool Selected => m_Selected;

        public static WishPresetChip Create(Transform parent, string id, string lang, bool candy, Action action, Func<bool> blocked, int order)
        {
            var prefix = WishPresetLibrary.ChipPrefix(id, lang);
            var renderer = Moodium.UI.UIRefreshVisuals.Sprite(parent, id + " Glass", prefix + "_idle", Vector3.zero, order);
            var chip = renderer.gameObject.AddComponent<WishPresetChip>();
            chip.Id = id; chip.m_Prefix = prefix; chip.m_Action = action; chip.m_Blocked = blocked;
            chip.m_Sprite = renderer; chip.m_Candy = candy;
            // Only the capsule (plus 4 mm) is pressable, not the transparent shadow margin.
            renderer.gameObject.AddComponent<BoxCollider>().size =
                new Vector3(WishPresetPanel.ChipWidth + WishPresetPanel.ChipPad, WishPresetPanel.ChipHeight + WishPresetPanel.ChipPad, .015f);
            renderer.gameObject.AddComponent<MoodiumSpatialButton>().Configure(null, null, chip.Press);
            chip.Refresh();
            return chip;
        }

        public void SetSelected(bool value)
        {
            if (m_Selected == value) return;
            m_Selected = value;
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
            if (m_Pressed || (m_Blocked != null && m_Blocked())) return;
            if (Application.isPlaying) StartCoroutine(Feedback());
            m_Action?.Invoke();
        }

#if UNITY_EDITOR
        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current; var viewer = Camera.main;
            if (mouse == null || viewer == null) return;
            var ray = viewer.ScreenPointToRay(mouse.position.ReadValue());
            bool hover = Physics.Raycast(ray, out var hit, 20) && hit.collider == GetComponent<Collider>();
            if (m_Hover != hover) SetHovered(hover);
            if (hover && mouse.leftButton.wasPressedThisFrame) Press();
#endif
        }
#endif

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
            var sprite = Moodium.UI.UIRefreshVisuals.Load(m_Prefix + WishPresetLibrary.ChipSuffix(Id, m_Pressed, m_Selected, m_Hover, m_Candy));
            if (sprite == null) sprite = Moodium.UI.UIRefreshVisuals.Load(m_Prefix + "_idle");
            m_Sprite.sprite = sprite;
        }

        void OnDisable()
        {
            StopAllCoroutines();
            m_Pressed = m_Hover = false;
            Refresh();
        }
    }
}
