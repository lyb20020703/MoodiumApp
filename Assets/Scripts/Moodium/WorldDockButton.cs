using System;
using System.Collections;
using Moodium.Flow;
using Unity.PolySpatial;
using UnityEngine;
using UnityEngine.Rendering;

namespace Moodium.CandyWorld
{

    /// <summary>One capsule of the world dock: hit capsule (collider, spatial button, system hover) + glass sprite.</summary>
    public sealed class WorldDockButton : MonoBehaviour
    {
        SpriteRenderer m_Sprite;
        MeshRenderer m_HitRenderer;
        Collider m_Collider;
        Func<string, string> m_Id;
        bool m_Locked, m_Pressed, m_Hover;
        float m_Alpha = 1f;
        Vector3 m_Home,m_HomePosition;
        Mesh m_Mesh;Material m_Material;
        public bool Confirming { get; set; }

        public static WorldDockButton Create(Transform parent, string name, Vector3 position, bool systemHover, Action press)
        {
            // Holder (pulse / shake move this) with two siblings: the hit capsule and the glass sprite.
            // The sprite is NOT a child of the hit capsule, so the system highlight cannot spread to its
            // rectangular quad: only the capsule-shaped mesh lights up when looked at.
            var holder = new GameObject(name).transform;
            holder.SetParent(parent, false); holder.localPosition = position;
            Moodium.UI.UIRefreshVisuals.UnitScale(holder);
            var hit = new GameObject(name + " Hit", typeof(MeshFilter), typeof(MeshRenderer));
            hit.transform.SetParent(holder, false);
            hit.GetComponent<MeshFilter>().sharedMesh = CapsuleMesh(WorldActionDock.ButtonWidth, WorldActionDock.ButtonHeight, 16);
            var mr = hit.GetComponent<MeshRenderer>();
            mr.sharedMaterial = Moodium.NatureWorld.BubbleWorldData.CreateUnlit("Dock hit capsule", null, true, CullMode.Off, new Color(1, 1, 1, .04f));
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            Moodium.UI.UIRefreshVisuals.Register(parent, hit, 0);
            var box = hit.AddComponent<BoxCollider>();
            box.size = new Vector3(WorldActionDock.ButtonWidth + 2 * WorldActionDock.HitPad, WorldActionDock.ButtonHeight + 2 * WorldActionDock.HitPad, WorldActionDock.HitDepth);
            // Mostly behind the glass (+Z is away from the viewer): a poking finger meets the front face at the glass.
            box.center = new Vector3(0, 0, WorldActionDock.HitDepth / 2f - .004f);
            if (systemHover)
            {
                var hover = hit.AddComponent<VisionOSHoverEffect>();
                hover.Type = VisionOSHoverEffect.EffectType.Highlight;
                hover.Color = Color.white; hover.IntensityMultiplier = .5f;
            }
            hit.AddComponent<MoodiumSpatialButton>().Configure(null, null, press);
            var b = holder.gameObject.AddComponent<WorldDockButton>();
            b.m_HitRenderer = mr; b.m_Collider = box; b.m_Home = holder.localScale;b.m_HomePosition=position;b.m_Mesh=hit.GetComponent<MeshFilter>().sharedMesh;b.m_Material=mr.sharedMaterial;
            b.m_Sprite = Moodium.UI.UIRefreshVisuals.Sprite(holder, name + " Glass", WorldActionDock.Folder + "dock_exit_zh_idle", Vector3.zero, 1);
            return b;
        }

        static Mesh CapsuleMesh(float width, float height, int segments)
        {
            var r = height / 2f; var half = width / 2f - r;
            var v = new System.Collections.Generic.List<Vector3> { Vector3.zero };
            for (int side = 0; side < 2; side++)
                for (int i = 0; i <= segments; i++)
                {
                    var a = Mathf.PI / 2f + (side == 0 ? 0 : Mathf.PI) + Mathf.PI * i / segments;
                    v.Add(new Vector3((side == 0 ? -half : half) + Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0));
                }
            var tris = new System.Collections.Generic.List<int>();
            for (int i = 1; i < v.Count; i++) { tris.Add(0); tris.Add(i); tris.Add(i == v.Count - 1 ? 1 : i + 1); }
            var mesh = new Mesh { name = "Dock capsule" };
            mesh.SetVertices(v); mesh.SetTriangles(tris, 0); mesh.RecalculateBounds(); mesh.RecalculateNormals();
            return mesh;
        }

        public float DistanceTo(Vector3 point)=>m_Collider!=null?Vector3.Distance(point,m_Collider.ClosestPoint(point)):float.PositiveInfinity;

        public void SetSprites(Func<string, string> id, bool locked) { m_Id = id; m_Locked = locked; Refresh(); }
        /// <summary>At or below this the button draws nothing at all.</summary>
        public const float HiddenAlpha = .01f;

        public void SetAlpha(float a)
        {
            m_Alpha = a; Refresh();
            // Fully faded = no renderer at all. An alpha-0 sprite is still a full rectangular quad on visionOS, and
            // inside the bubble world (sky + point layers around the user) the two dock quads showed as dark blocks.
            var shown = a > HiddenAlpha;
            if (m_Sprite != null) m_Sprite.enabled = shown;
            if (m_HitRenderer != null) m_HitRenderer.enabled = shown;
        }

        /// <summary>True while either the glass sprite or the hit capsule is drawn.</summary>
        public bool Drawn => (m_Sprite != null && m_Sprite.enabled) || (m_HitRenderer != null && m_HitRenderer.enabled);
        public void SetInteractive(bool on) { if (m_Collider != null) m_Collider.enabled = on; }

        public void Flash() { if (isActiveAndEnabled) StartCoroutine(PressedFor(.15f)); }
        public void Pulse() { if (isActiveAndEnabled) StartCoroutine(Scale(new[] { 1.08f, 1f, 1.08f, 1f }, .22f)); }
        public void Shake() { if (isActiveAndEnabled) StartCoroutine(ShakeRoutine()); }

        IEnumerator PressedFor(float seconds) { m_Pressed = true; Refresh(); yield return new WaitForSeconds(seconds); m_Pressed = false; Refresh(); }

        IEnumerator Scale(float[] steps, float each)
        {
            var from = 1f;
            foreach (var to in steps)
            {
                for (float t = 0; t < each; t += Time.deltaTime) { transform.localScale = m_Home * Mathf.SmoothStep(from, to, t / each); yield return null; }
                from = to;
            }
            transform.localScale = m_Home;
        }

        IEnumerator ShakeRoutine()
        {
            var start = transform.localPosition;
            for (float t = 0; t < .35f; t += Time.deltaTime)
            {
                transform.localPosition = start + Vector3.right * Mathf.Sin(t * 60f) * .003f * (1f - t / .35f);
                yield return null;
            }
            transform.localPosition = start;
        }

        void Refresh()
        {
            if (m_Sprite == null || m_Id == null) return;
            var state = m_Locked ? "" : m_Pressed ? "pressed" : m_Hover ? "gaze" : "idle";
            m_Sprite.sprite = Moodium.UI.UIRefreshVisuals.Load(m_Id(state));
            m_Sprite.color = new Color(1, 1, 1, m_Alpha);
        }

#if UNITY_EDITOR
        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current; var viewer = Camera.main;
            if (mouse == null || viewer == null || m_Collider == null || !m_Collider.enabled) return;
            var ray = viewer.ScreenPointToRay(mouse.position.ReadValue());
            var hover = Physics.Raycast(ray, out var hit, 20) && hit.collider == m_Collider;
            if (hover != m_Hover) { m_Hover = hover; Refresh(); }
            if(hover&&mouse.leftButton.wasPressedThisFrame)m_Collider.GetComponent<MoodiumSpatialButton>().Press();
#endif
        }
#endif

        void OnDestroy()
        {
            Moodium.NatureWorld.BubblePlayVisuals.Release(m_Mesh);
            Moodium.NatureWorld.BubblePlayVisuals.Release(m_Material);
        }

        void OnDisable()
        {
            StopAllCoroutines(); m_Pressed = m_Hover = false;
            if (m_Home != Vector3.zero) transform.localScale = m_Home;
            transform.localPosition=m_HomePosition;
            Refresh();
        }
    }
}
