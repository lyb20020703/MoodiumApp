using System.Collections.Generic;
using UnityEngine;

namespace Moodium.Rewards
{
    [DisallowMultipleComponent]
    public sealed class RewardBracelet : MonoBehaviour
    {
        bool m_InteractionEnabled = true;
        public void SetInteractionEnabled(bool value) { m_InteractionEnabled = value; if (!value) SelectedSlot = -1; }
        const int Slots = 4;
        readonly List<RewardEntry> m_Entries = new();
        readonly List<RewardOrb> m_Orbs = new();

        public int SlotCount => Slots;
        public int CollectedCount => m_Entries.Count;
        public float NextSlotProgress { get; private set; }
        public float VisualAlpha { get; private set; }
        public float VisualScale { get; private set; } = 1f;
        public int SelectedSlot { get; private set; } = -1;

        void Awake() => BuildVisual();

        public void SetProgress(float normalizedProgress) => NextSlotProgress = Mathf.Clamp01(normalizedProgress);

        public void SetTrackedWrist(bool tracked, bool inView)
        {
            VisualAlpha = tracked ? inView ? 1f : .35f : 0f;
            VisualScale = tracked && inView ? 1.15f : 1f;
        }

        public float GetSlotScale(int index) => index is 1 or 2 ? 1f : .9f;

        public bool TryCollect(RewardOrb orb, RewardEntry entry)
        {
            if (orb == null || m_Entries.Count >= Slots)
                return false;
            m_Entries.Add(entry);
            m_Orbs.Add(orb);
            orb.transform.SetParent(transform, true);
            orb.transform.localScale = Vector3.one * .015f;
            orb.transform.localPosition = SlotLocalPosition(m_Entries.Count - 1);
            return true;
        }

        public string InspectSlot(int index, string language)
        {
            if (!m_InteractionEnabled || index < 0 || index >= m_Entries.Count)
                return string.Empty;
            SelectedSlot = index;
            return RewardTextTable.Get("gem.label", language, index + 1, m_Entries[index].displayName);
        }

        public void Clear()
        {
            m_Entries.Clear();
            m_Orbs.Clear();
            SelectedSlot = -1;
            NextSlotProgress = 0f;
        }

        static Vector3 SlotLocalPosition(int index)
        {
            var x = (index - 1.5f) * .017f;
            return new Vector3(x, .005f, .02f);
        }

        void BuildVisual()
        {
            if (transform.Find("Glass Band") != null)
                return;
            var band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            band.name = "Glass Band";
            band.transform.SetParent(transform, false);
            band.transform.localScale = new Vector3(.055f, .006f, .042f);
            band.GetComponent<Collider>().enabled = false;
            RewardOrb.ApplyGlass(band.GetComponent<Renderer>(), new Color(1f, 1f, 1f, .28f));
            for (var index = 0; index < Slots; index++)
            {
                var cup = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                cup.name = $"Gem Slot {index + 1}";
                cup.transform.SetParent(transform, false);
                cup.transform.localPosition = SlotLocalPosition(index);
                cup.transform.localScale = Vector3.one * (.008f * GetSlotScale(index));
                cup.GetComponent<Collider>().enabled = false;
                RewardOrb.ApplyGlass(cup.GetComponent<Renderer>(), new Color(1f, 1f, 1f, .5f));
            }
        }
    }
}
