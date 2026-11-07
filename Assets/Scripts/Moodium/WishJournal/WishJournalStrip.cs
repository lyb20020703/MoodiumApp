using System;
using System.Collections.Generic;
using Moodium.Flow;
using Moodium.NatureWorld;
using Unity.PolySpatial;
using UnityEngine;
using UnityEngine.Rendering;

namespace Moodium.WishJournal
{
    /// <summary>
    /// One wish paper in the journal: the chime's own Strip mesh and paper mapping (PaperFrame_*) with a
    /// <see cref="WishInkMesh"/> on it, so the ink looks exactly like the chime in the world. The transform of this
    /// component is the hanging point (top centre of the paper); the paper hangs below it and can sway.
    /// </summary>
    public sealed class WishJournalStrip : MonoBehaviour
    {
        public const string CandyPaper = "#FFE6F0", NaturePaper = "#C9A574";

        Transform m_Paper;
        MeshRenderer m_PaperRenderer;
        Material m_PaperMaterial;
        WishInkMesh m_Ink;
        BoxCollider m_Hit;
        List<List<float[]>> m_Strokes;
        WishWritingTimeline m_Timeline;
        float m_ReplayStart, m_Phase, m_Alpha = 1f;
        int m_ReplayDone = -1;
        bool m_Sway = true;
        Action m_Finished;

        public JournalEntry Entry { get; private set; }
        public float Width { get; private set; }
        public float Height => Width * WishWritingTimeline.PaperHeightCm / WishWritingTimeline.PaperWidthCm;
        public bool Replaying => m_Timeline != null;
        public BoxCollider Hit => m_Hit;
        public Transform Paper => m_Paper;

        /// <summary>
        /// Builds a paper of real width <paramref name="width"/> (metres) whose top centre is at <paramref name="top"/>
        /// (parent-local). <paramref name="pressed"/> null = not pressable. Renderers are registered in the parent's sorting
        /// group: paper <paramref name="baseOrder"/>, glow +1, ink +2.
        /// </summary>
        public static WishJournalStrip Create(Transform parent, string name, JournalEntry entry, float width, Vector3 top, int baseOrder, Action pressed)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = top;
            Moodium.UI.UIRefreshVisuals.UnitScale(holder.transform);
            var strip = holder.AddComponent<WishJournalStrip>();
            strip.Build(entry, width, baseOrder, pressed);
            return strip;
        }

        void Build(JournalEntry entry, float width, int baseOrder, Action pressed)
        {
            Entry = entry; Width = width; m_Strokes = entry.Record.strokes;
            m_Phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            var candy = entry.Candy;
            var frame = Resources.Load<WishPaperFrame>("MoodiumBubblePlay/PaperFrame_" + (candy ? "Candy" : "Nature"));
            var prefab = Resources.Load<GameObject>("MoodiumBubblePlay/WishChime_" + (candy ? "Candy" : "Nature"));
            Mesh mesh = null;
            if (prefab != null)
                foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Strip" && t.TryGetComponent<MeshFilter>(out var mf)) { mesh = mf.sharedMesh; break; }
            if (frame == null || mesh == null) { Debug.LogWarning("[Wish Journal] missing wish paper mesh or frame"); return; }

            // Mesh space → hanging pose: the paper's visible normal (frame.Normal) faces the viewer (−Z of the panel) and
            // its up (frame.Up) is +Y; scaled so the paper is `width` wide; its top centre sits on this transform.
            var paper = new GameObject("Paper", typeof(MeshFilter), typeof(MeshRenderer)).transform;
            paper.SetParent(transform, false);
            var rotation = Quaternion.LookRotation(Vector3.back, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(frame.Normal, frame.Up.normalized));
            var scale = width / Mathf.Max(1e-6f, frame.Right.magnitude);
            paper.localRotation = rotation;
            paper.localScale = Vector3.one * scale;
            paper.localPosition = -(rotation * (frame.Surface(new Vector2(.5f, 1f)) * scale));
            paper.GetComponent<MeshFilter>().sharedMesh = mesh;
            ColorUtility.TryParseHtmlString(candy ? CandyPaper : NaturePaper, out var color);
            m_PaperMaterial = BubbleWorldData.CreateUnlit("Journal paper", null, true, CullMode.Off, color);
            m_PaperRenderer = paper.GetComponent<MeshRenderer>();
            m_PaperRenderer.sharedMaterial = m_PaperMaterial;
            m_PaperRenderer.shadowCastingMode = ShadowCastingMode.Off; m_PaperRenderer.receiveShadows = false;
            m_PaperRenderer.sortingOrder = baseOrder;
            Moodium.UI.UIRefreshVisuals.Register(transform, paper.gameObject, baseOrder);
            m_Paper = paper;

            m_Ink = new GameObject("Wish Ink").AddComponent<WishInkMesh>();
            m_Ink.Initialize(paper, frame, candy);   // registers glow / ink at WishInkMesh orders 5 / 6
            if (entry.Preset != null) m_Ink.SetWidth(WishPresetLibrary.InkWidth, WishPresetLibrary.GlowWidth);
            WishJournalSorting.Shift(GetComponentInParent<VisionOSSortingGroup>(), m_Ink.transform, baseOrder - WishInkMesh.PaperOrder);
            m_Ink.Completed(m_Strokes);

            if (pressed != null)
            {
                var hit = new GameObject("Paper Hit");
                hit.transform.SetParent(transform, false);
                hit.transform.localPosition = new Vector3(0f, -Height / 2f, 0f);
                m_Hit = hit.AddComponent<BoxCollider>();
                m_Hit.size = new Vector3(width + 2f * WishJournalLayout.StripHitPad, Height + 2f * WishJournalLayout.StripHitPad, .02f);
                hit.AddComponent<MoodiumSpatialButton>().Configure(null, null, pressed);
            }
        }

        public void SetSway(bool on) { m_Sway = on; if (!on) transform.localRotation = Quaternion.identity; }

        /// <summary>Fades paper and ink; at (near) zero nothing is drawn at all (an alpha-0 quad still draws on visionOS).</summary>
        public void SetAlpha(float a)
        {
            m_Alpha = Mathf.Clamp01(a);
            if (m_PaperMaterial != null) { BubbleWorldData.SetAlpha(m_PaperMaterial, m_Alpha); PolySpatialObjectUtils.MarkDirty(m_PaperMaterial); }
            if (m_PaperRenderer != null) m_PaperRenderer.enabled = m_Alpha > .01f;
            if (m_Ink != null) { m_Ink.Alpha(m_Alpha); m_Ink.Show(m_Alpha > .01f); }
        }

        public void SetPressable(bool on) { if (m_Hit != null) m_Hit.enabled = on; }

        // ---------- replay: the wish writes itself again in its original stroke order ----------

        /// <summary>Writing order for the replay: the preset's own kinds when the strokes are exactly a preset, else from the strokes.</summary>
        public static int[] Kinds(JournalEntry entry)
        {
            var strokes = entry.Record.strokes;
            if (entry.Preset != null)
                foreach (var lang in new[] { "zh", "en" })
                {
                    var p = WishPresetLibrary.Find(entry.Preset, lang);
                    if (p != null && p.kinds != null && p.strokes != null && p.strokes.Count == strokes.Count && p.kinds.Length == strokes.Count) return p.kinds;
                }
            return WishJournalData.ReplayKinds(strokes);
        }

        public void Replay(Action finished)
        {
            if (m_Ink == null || m_Strokes == null || m_Strokes.Count == 0) { finished?.Invoke(); return; }
            m_Timeline = new WishWritingTimeline(m_Strokes, Kinds(Entry));
            m_ReplayStart = Time.time; m_ReplayDone = -1; m_Finished = finished;
            m_Ink.Completed(new List<List<float[]>>()); m_Ink.Active(null);
        }

        /// <summary>Skips to the end of a running replay.</summary>
        public void FinishReplay()
        {
            if (m_Timeline == null) return;
            m_Timeline = null;
            m_Ink.Active(null); m_Ink.Completed(m_Strokes);
            var done = m_Finished; m_Finished = null; done?.Invoke();
        }

        void Update()
        {
            if (m_Sway) transform.localRotation = Quaternion.Euler(0f, 0f, WishJournalLayout.SwayDegrees * Mathf.Sin(Time.time * 2f * Mathf.PI / WishJournalLayout.SwayPeriod + m_Phase));
            if (m_Timeline == null) return;
            var t = Time.time - m_ReplayStart;
            if (t >= m_Timeline.Total) { FinishReplay(); return; }
            var f = m_Timeline.Sample(t);
            if (f.Done != m_ReplayDone) { m_ReplayDone = f.Done; m_Ink.Completed(m_Strokes.GetRange(0, Mathf.Clamp(f.Done, 0, m_Strokes.Count))); }
            if (f.PenMoving) m_Ink.Active(f.Partial, f.Tip); else m_Ink.Active(null);
        }

        void OnDestroy() { if (m_PaperMaterial != null) BubblePlayVisuals.Release(m_PaperMaterial); }
    }

    /// <summary>Sorting-group bookkeeping for the journal (one group on the panel root, PostPass).</summary>
    public static class WishJournalSorting
    {
        /// <summary>Adds <paramref name="by"/> to the order of every entry under <paramref name="root"/>.</summary>
        public static void Shift(VisionOSSortingGroup group, Transform root, int by)
        {
            if (group == null || root == null || by == 0) return;
            for (int i = 0; i < group.Renderers.Count; i++)
            {
                var e = group.Renderers[i];
                if (e.Renderer == null || !e.Renderer.transform.IsChildOf(root)) continue;
                e.Order += by; group.Renderers[i] = e;
                var r = e.Renderer.GetComponent<Renderer>(); if (r != null) r.sortingOrder = e.Order;
            }
        }

        /// <summary>Removes the entries of <paramref name="root"/> and its children (call before destroying it) and stale ones.</summary>
        public static int Remove(VisionOSSortingGroup group, Transform root)
        {
            if (group == null) return 0;
            var removed = 0;
            for (int i = group.Renderers.Count - 1; i >= 0; i--)
            {
                var go = group.Renderers[i].Renderer;
                if (go == null || (root != null && go.transform.IsChildOf(root))) { group.Renderers.RemoveAt(i); removed++; }
            }
            return removed;
        }

        /// <summary>Registers TMP fallback-font sub-meshes (created on mesh update) with their text's order, as MoodCheckController does.</summary>
        public static void RegisterSubMeshes(VisionOSSortingGroup group, Transform root)
        {
            if (group == null || root == null) return;
            foreach (var sub in root.GetComponentsInChildren<TMPro.TMP_SubMesh>(true))
            {
                var exists = false;
                foreach (var e in group.Renderers) if (e.Renderer == sub.gameObject) { exists = true; break; }
                if (exists) continue;
                var owner = sub.textComponent != null ? sub.textComponent.GetComponent<Renderer>() : null;
                var order = owner != null ? owner.sortingOrder : 7;
                group.Renderers.Add(new VisionOSSortingGroup.RendererSorting { Renderer = sub.gameObject, Order = order, ApplyToDescendants = false });
            }
        }
    }
}
