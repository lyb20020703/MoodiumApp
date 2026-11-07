using System;
using System.Collections;
using System.Threading.Tasks;
using Moodium.Flow;
using Moodium.NatureWorld;
using Unity.PolySpatial;
using UnityEngine;

namespace Moodium.WishJournal
{
    /// <summary>
    /// Sprite button of the journal: swaps <c>prefix_idle</c> / <c>_pressed</c> (and <c>_open</c> when set) like
    /// UIRefreshSpriteButton, without a hover sprite (RealityKit reports no gaze). Disabled = 38 % and no collider.
    /// </summary>
    public sealed class JournalSpriteButton : MonoBehaviour
    {
        public const float DisabledAlpha = .38f;
        string m_Prefix; Action m_Action; Func<bool> m_Blocked;
        bool m_Pressed, m_Open, m_Interactable = true;
        SpriteRenderer m_Sprite; Collider m_Collider;

        public bool Interactable => m_Interactable;
        public bool Open => m_Open;
        public string Prefix => m_Prefix;

        public static JournalSpriteButton Create(Transform parent, string name, string prefix, Vector3 position, int order, Vector2 hit, Action action, Func<bool> blocked = null)
        {
            var sprite = Moodium.UI.UIRefreshVisuals.Sprite(parent, name, prefix + "_idle", position, order);
            var collider = sprite.gameObject.AddComponent<BoxCollider>(); collider.size = new Vector3(hit.x, hit.y, .012f);
            var button = sprite.gameObject.AddComponent<JournalSpriteButton>();
            button.m_Sprite = sprite; button.m_Collider = collider; button.m_Prefix = prefix; button.m_Action = action; button.m_Blocked = blocked;
            sprite.gameObject.AddComponent<MoodiumSpatialButton>().Configure(null, null, button.Press);
            return button;
        }

        public void SetPrefix(string prefix) { m_Prefix = prefix; Refresh(); }
        public void SetOpen(bool open) { m_Open = open; Refresh(); }

        public void SetInteractable(bool on)
        {
            m_Interactable = on;
            if (m_Collider != null) m_Collider.enabled = on;
            if (m_Sprite != null) { var c = m_Sprite.color; c.a = on ? 1f : DisabledAlpha; m_Sprite.color = c; }
        }

        public void Press()
        {
            if (!m_Interactable || m_Pressed || (m_Blocked != null && m_Blocked())) return;
            if (Application.isPlaying && isActiveAndEnabled) StartCoroutine(Feedback());
            m_Action?.Invoke();
        }

        IEnumerator Feedback() { m_Pressed = true; Refresh(); yield return new WaitForSecondsRealtime(.12f); m_Pressed = false; Refresh(); }

        void Refresh()
        {
            if (m_Sprite == null || m_Prefix == null) return;
            var id = m_Prefix + (m_Pressed ? "_pressed" : m_Open ? "_open" : "_idle");
            var sprite = Moodium.UI.UIRefreshVisuals.Load(id) ?? Moodium.UI.UIRefreshVisuals.Load(m_Prefix + "_idle");
            if (sprite != null) m_Sprite.sprite = sprite;
        }

        void OnDisable() { StopAllCoroutines(); m_Pressed = false; Refresh(); }
    }

    /// <summary>
    /// The Wish Journal button on the selection screen: a glass circle with the chime-and-paper mark, beside the
    /// settings gear. A warm dot shows while a wish was released after the journal was last opened.
    /// </summary>
    public sealed class WishJournalButton : MonoBehaviour
    {
        public const string SeenKey = "Moodium.WishJournal.SeenUntil";
        /// <summary>Distance from the gear's centre (to its left) on the video-backed selection.</summary>
        public const float GearSpacing = .075f;
        const int Order = 23;

        JournalSpriteButton m_Button;
        SpriteRenderer m_Dot;
        Task<string> m_Newest;

        public JournalSpriteButton Button => m_Button;
        public bool DotVisible => m_Dot != null && m_Dot.enabled;

        /// <summary>Builds the button at <paramref name="worldPosition"/>, facing like its parent.</summary>
        public static WishJournalButton Create(Transform parent, Vector3 worldPosition, Action pressed)
        {
            var root = new GameObject("Wish Journal Button", typeof(VisionOSSortingGroup));
            root.transform.SetParent(parent, false);
            root.transform.position = worldPosition;
            root.transform.localRotation = Quaternion.identity;
            Moodium.UI.UIRefreshVisuals.UnitScale(root.transform);
            var b = root.AddComponent<WishJournalButton>();
            b.m_Button = JournalSpriteButton.Create(root.transform, "Journal Glass", WishJournalLayout.Folder + "journal_btn", Vector3.zero, Order, new Vector2(.06f, .06f), pressed);
            b.m_Dot = Moodium.UI.UIRefreshVisuals.Sprite(root.transform, "New Wish Dot", WishJournalLayout.Folder + "journal_dot", new Vector3(.0165f, .0165f, -.002f), Order + 1);
            b.m_Dot.enabled = false;
            b.RefreshDot();
            return b;
        }

        public void SetOpen(bool open) => m_Button?.SetOpen(open);

        public void SetDot(bool on) { if (m_Dot != null) m_Dot.enabled = on; }

        /// <summary>Reads the newest wish off the main thread and lights the dot when it is newer than what was seen.</summary>
        public void RefreshDot()
        {
            if (!Application.isPlaying) return;
            var path = WishChimeStore.DefaultPath;
            m_Newest = Task.Run(() => WishJournalData.Newest(WishChimeStore.Load(path)));
        }

        /// <summary>Opening the journal: everything up to the newest wish counts as seen.</summary>
        public static void MarkSeen(string newest)
        {
            if (string.IsNullOrEmpty(newest)) return;
            PlayerPrefs.SetString(SeenKey, newest); PlayerPrefs.Save();
        }

        void Update()
        {
            if (m_Newest == null || !m_Newest.IsCompleted) return;
            var newest = m_Newest.Status == TaskStatus.RanToCompletion ? m_Newest.Result : null;
            m_Newest = null;
            SetDot(WishJournalData.HasNew(newest, PlayerPrefs.GetString(SeenKey, null)));
        }
    }
}
