using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace Moodium.Flow
{
    public sealed class RoomContentVisibility : MonoBehaviour
    {
        public const int ObjectsPerFrame = 20;
        public const float HideSeconds = .6f, RestoreSeconds = .8f;
        sealed class Item
        {
            public GameObject Target;
            public Action Hide, Restore;
            public bool Snapshot, Active, Applied, ImmediateHide;
            public Vector3 Scale;
            public SpriteRenderer[] Sprites;
            public Color[] Colors;
            public float From;
        }
        readonly List<Item> m_Items = new();
        readonly HashSet<GameObject> m_Targets = new();
        Coroutine m_Transition;
        public bool Hidden { get; private set; }
        public bool IsTransitioning => m_Transition != null;
        /// <summary>The registered objects (not the hide / restore callbacks).</summary>
        public IEnumerable<GameObject> Targets => m_Targets;
        public void Register(Action hide, Action restore) => m_Items.Add(new Item { Hide = hide, Restore = restore });
        public void Register(GameObject target)
        {
            if (target != null && m_Targets.Add(target)) m_Items.Add(new Item { Target = target });
        }
        public void RegisterImmediateHide(GameObject target)
        {
            if (target == null) return;
            Register(target);
            foreach (var item in m_Items)
                if (item.Target == target) { item.ImmediateHide = true; break; }
        }
        public void SetHidden(bool hidden)
        {
            if (Hidden == hidden) return;
            Hidden = hidden;
            // Snapshot before deactivation so returning to the room restores the
            // authored active state. Remove expensive clouds in this same frame.
            if (hidden)
                foreach (var item in m_Items)
                    if (item.ImmediateHide && item.Target != null)
                    { Capture(item); item.Target.SetActive(false); }
            if (m_Transition != null) StopCoroutine(m_Transition);
            m_Transition = StartCoroutine(Apply(hidden));
        }
        void Capture(Item item)
        {
            if (item.Snapshot || item.Target == null) return;
            item.Snapshot = true; item.Active = item.Target.activeSelf; item.Scale = item.Target.transform.localScale;
            item.Sprites = item.Target.GetComponentsInChildren<SpriteRenderer>(true);
            item.Colors = new Color[item.Sprites.Length];
            for (int i = 0; i < item.Sprites.Length; i++) item.Colors[i] = item.Sprites[i].color;
        }
        void Visual(Item item, float alpha)
        {
            if (item.Target == null) return;
            for (int i = 0; i < item.Sprites.Length; i++)
                if (item.Sprites[i] != null) { var c = item.Colors[i]; c.a *= alpha; item.Sprites[i].color = c; }
            // Opaque materials retain their authored render state and use the specified growth fallback.
            item.Target.transform.localScale = item.Scale * Mathf.Lerp(.9f, 1f, alpha);
        }
        IEnumerator Apply(bool hidden)
        {
            // Snapshot each object once for the whole hide/restore cycle. A reversal never captures the hidden state.
            for (int i = 0; i < m_Items.Count; i++)
            {
                if (i > 0 && i % ObjectsPerFrame == 0) yield return null;
                var item = m_Items[i]; Capture(item);
                if (item.Target != null)
                {
                    item.From = item.Target.activeSelf ? Mathf.InverseLerp(.9f, 1f,
                        item.Scale.x == 0 ? 1f : item.Target.transform.localScale.x / item.Scale.x) : 0f;
                    if (!hidden && item.Active) item.Target.SetActive(true);
                }
                else if (hidden && !item.Applied) { item.Hide?.Invoke(); item.Applied = true; }
                else if (!hidden && item.Applied) { item.Restore?.Invoke(); item.Applied = false; }
            }
            yield return null;
            var start = Time.realtimeSinceStartup;
            var duration = hidden ? HideSeconds : RestoreSeconds;
            do
            {
                for (int i = 0; i < m_Items.Count; i++)
                {
                    if (i > 0 && i % ObjectsPerFrame == 0) yield return null;
                    var item = m_Items[i];
                    if (!item.Snapshot || item.Target == null || (hidden && item.ImmediateHide)) continue;
                    var t = Mathf.Clamp01((Time.realtimeSinceStartup - start) / duration);
                    Visual(item, Mathf.Lerp(item.From, hidden ? 0f : 1f, Mathf.SmoothStep(0, 1, t)));
                    if (t >= 1f)
                    {
                        item.Target.SetActive(!hidden && item.Active);
                        if (!hidden) { Visual(item, 1f); item.Snapshot = false; }
                    }
                }
                yield return null;
            } while (Time.realtimeSinceStartup - start < duration);
            // Finalize all entries even when a large batch straddled the deadline.
            for (int i = 0; i < m_Items.Count; i++)
            {
                if (i > 0 && i % ObjectsPerFrame == 0) yield return null;
                var item = m_Items[i]; if (item.Target == null || !item.Snapshot) continue;
                if (!(hidden && item.ImmediateHide)) Visual(item, hidden ? 0f : 1f); item.Target.SetActive(!hidden && item.Active);
                if (!hidden) item.Snapshot = false;
            }
            m_Transition = null;
        }
        public void Clear()
        {
            if (m_Transition != null) StopCoroutine(m_Transition); m_Transition = null;
            foreach (var item in m_Items)
            {
                if (item.Snapshot && item.Target != null) { Visual(item, 1f); item.Target.SetActive(item.Active); }
                if (item.Applied) { item.Restore?.Invoke(); item.Applied = false; }
            }
            m_Items.Clear(); m_Targets.Clear(); Hidden = false;
        }
        void OnDestroy() => Clear();
    }
}
