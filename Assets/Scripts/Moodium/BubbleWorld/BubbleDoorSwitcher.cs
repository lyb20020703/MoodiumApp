using System;
using System.Collections.Generic;
using Unity.PolySpatial;
using UnityEngine;

namespace Moodium.NatureWorld
{
    /// <summary>
    /// The controls on the mercury door (门入口 2026-11-02 c):
    /// - the world's name sits on the top of the lip (棉花糖世界 / 薄荷巧克力世界, or 森林世界 / 枫林世界 / 雪原世界);
    /// - a liquid-metal arrow on each side, at hand height, switches to the family's other world (the families never mix):
    ///   the door folds into a droplet, the world is swapped, the droplet opens again;
    /// - a small liquid-metal cross at the upper right closes the door: the droplet flies back beside the sensory bag.
    /// Look + pinch, a fingertip, or the editor mouse. Only one world is ever loaded. Pure layout here (EditMode tests).
    /// </summary>
    public static class DoorSwitchRules
    {
        /// <summary>The choice_world_* name sprites are 8.7 × 4.2 cm at scale 1 (4000 px per unit); at the door they are read from ~1.3 m.</summary>
        public const float LabelScale = 1.6f;
        /// <summary>The arrows stand this far outside the fattest lip, at the door's middle height; the cross this far beyond the lip at its angle.</summary>
        public const float ArrowGap = .09f, CloseGap = .06f, CloseAngle = 42f, ControlForward = .04f;
        public const float ArrowArm = .07f, ArrowThickness = .022f, CloseArm = .045f, CloseThickness = .016f;
        public const float FadeSeconds = .3f;

        /// <summary>
        /// The worlds of <paramref name="folder"/>'s family, in a fixed order (the family's first world first).
        /// Nature goes through the seasons: 森林 (summer green) → 枫林 (autumn) → 雪原 (winter), and round again.
        /// </summary>
        public static string[] Family(string folder)
        {
            if (BubbleWorldData.IsCandy(folder)) return new[] { BubbleWorldData.CandyFolder, BubbleWorldData.NestedCandyFolder };
            if (BubbleWorldData.IsNature(folder))
                return new[] { BubbleWorldData.NatureFolder, BubbleWorldData.NestedNatureFolder }; // Maple temporarily disabled.
            return new[] { folder };
        }

        /// <summary>The world one step <paramref name="direction"/> (−1 left, +1 right) from <paramref name="current"/>, wrapping round.</summary>
        public static string Next(IReadOnlyList<string> worlds, string current, int direction)
        {
            if (worlds == null || worlds.Count == 0) return null;
            var index = 0;
            for (var i = 0; i < worlds.Count; i++) if (worlds[i] == current) { index = i; break; }
            var n = worlds.Count;
            return worlds[((index + (direction < 0 ? -1 : 1)) % n + n) % n];
        }

        /// <summary>Centre of the world's name (room frame): sitting on the top of the lip, just in front of it.</summary>
        public static Vector3 NameCentre(float doorZ, float labelHeight) => new(0f,
            BubbleDoorLayout.CenterHeight + BubbleDoorLayout.HalfOpenHeight + BubbleDoorLayout.RimOffset + BubbleDoorLayout.RimRadius * .9f + labelHeight * .5f - .01f,
            doorZ - BubbleDoorLayout.RimRadius * 1.8f);

        /// <summary>Centre of the left (−1) or right (+1) arrow (room frame).</summary>
        public static Vector3 ArrowCentre(int side, float doorZ) => new(
            (side < 0 ? -1f : 1f) * (BubbleDoorLayout.HalfOpenWidth + BubbleDoorLayout.RimOffset + BubbleDoorLayout.RimRadius * BubbleDoorLayout.RimThick + ArrowGap),
            BubbleDoorLayout.CenterHeight, doorZ - ControlForward);

        /// <summary>Centre of the close cross (room frame): upper right, outside the lip.</summary>
        public static Vector3 CloseCentre(float doorZ)
        {
            var reach = BubbleDoorLayout.RimOffset + BubbleDoorLayout.RimRadius * BubbleDoorLayout.RimThick + CloseGap;
            var angle = CloseAngle * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle) * (BubbleDoorLayout.HalfOpenWidth + reach),
                BubbleDoorLayout.CenterHeight + Mathf.Sin(angle) * (BubbleDoorLayout.HalfOpenHeight + reach), doorZ - ControlForward);
        }

        /// <summary>The chevron's two arms (local, facing the viewer, +x = the viewer's right) for side −1 / +1.</summary>
        public static (Vector3 a, Vector3 tip, Vector3 b) Chevron(int side)
        {
            var s = side < 0 ? -1f : 1f;
            var h = ArrowArm * Mathf.Sin(45f * Mathf.Deg2Rad);
            return (new Vector3(-s * h * .5f, h, 0f), new Vector3(s * h * .5f, 0f, 0f), new Vector3(-s * h * .5f, -h, 0f));
        }
    }

    public sealed class BubbleDoorSwitcher : MonoBehaviour
    {
        BubbleDoor m_Door;
        Action<string> m_Chosen;
        Action m_Close;
        readonly List<string> m_Worlds = new();
        SpriteRenderer m_Name;
        Transform m_Left, m_Right, m_CloseButton;
        readonly List<Collider> m_Hits = new();
        Material m_Chrome;
        string m_Current, m_Busy;
        bool m_English;
        float m_Visible;

        public string Current => m_Current;
        public string Busy => m_Busy;
        public bool Shown => m_Visible > .01f;
        public int Count => m_Worlds.Count;
        public SpriteRenderer Name => m_Name;
        public Transform LeftArrow => m_Left;
        public Transform RightArrow => m_Right;
        public Transform CloseButton => m_CloseButton;

        /// <summary>
        /// The controls for <paramref name="door"/>. <paramref name="chosen"/> gets the folder an arrow switched to;
        /// <paramref name="close"/> is the cross. With a single world there are no arrows.
        /// </summary>
        public static BubbleDoorSwitcher Create(BubbleDoor door, IReadOnlyList<string> worlds, string current, Action<string> chosen, Action close)
        {
            var go = new GameObject("Door Controls");
            go.transform.SetParent(door.transform, false);
            var switcher = go.AddComponent<BubbleDoorSwitcher>();
            switcher.Build(door, worlds, current, chosen, close);
            return switcher;
        }

        void Build(BubbleDoor door, IReadOnlyList<string> worlds, string current, Action<string> chosen, Action close)
        {
            m_Door = door; m_Current = current; m_Chosen = chosen; m_Close = close;
            m_English = Moodium.Characters.MoodiVoice.LanguageCode == "en";
            foreach (var w in worlds) m_Worlds.Add(w);
            m_Chrome = BubbleDoor.Chrome();
            var name = new GameObject("World Name");
            name.transform.SetParent(transform, false);
            m_Name = name.AddComponent<SpriteRenderer>();
            m_Name.color = new Color(1f, 1f, 1f, 0f);
            SetNameSprite(current);
            name.SetActive(false);
            if (m_Worlds.Count > 1)
            {
                m_Left = Control("Previous World", () => Step(-1), DoorSwitchRules.Chevron(-1), DoorSwitchRules.ArrowThickness, new Vector3(.12f, .17f, .08f));
                m_Right = Control("Next World", () => Step(1), DoorSwitchRules.Chevron(1), DoorSwitchRules.ArrowThickness, new Vector3(.12f, .17f, .08f));
            }
            var arm = DoorSwitchRules.CloseArm * .5f;
            m_CloseButton = Control("Close Door", () => { if (Shown) m_Close?.Invoke(); },
                (new Vector3(-arm, arm, 0f), Vector3.zero, new Vector3(arm, -arm, 0f)), DoorSwitchRules.CloseThickness, new Vector3(.1f, .1f, .08f),
                (new Vector3(-arm, -arm, 0f), new Vector3(arm, arm, 0f)));
            Debug.Log($"[Bubble Door] controls: {string.Join(" / ", m_Worlds.ConvertAll(BubbleChoiceRules.Key))}, behind the door {BubbleChoiceRules.Key(current)}");
        }

        /// <summary>
        /// A liquid-metal control: capsules along a polyline (a → tip → b, plus an optional extra bar), one box collider,
        /// look + pinch / fingertip press, and the system hover highlight.
        /// </summary>
        Transform Control(string label, Action pressed, (Vector3 a, Vector3 tip, Vector3 b) line, float thickness, Vector3 hitSize, (Vector3, Vector3)? extra = null)
        {
            var root = new GameObject(label);
            root.transform.SetParent(transform, false);
            if (line.tip == Vector3.zero && extra.HasValue) Bar(root.transform, line.a, line.b, thickness);   // the cross: two straight bars
            else { Bar(root.transform, line.a, line.tip, thickness); Bar(root.transform, line.tip, line.b, thickness); }
            if (extra.HasValue) Bar(root.transform, extra.Value.Item1, extra.Value.Item2, thickness);
            var hit = root.AddComponent<BoxCollider>();
            hit.size = hitSize;
            m_Hits.Add(hit);
            root.AddComponent<Moodium.Flow.MoodiumSpatialButton>().Configure(null, null, pressed);
            var hover = root.AddComponent<VisionOSHoverEffect>();
            hover.Type = VisionOSHoverEffect.EffectType.Highlight;
            hover.Color = Color.white; hover.IntensityMultiplier = .6f;
            root.SetActive(false);
            return root.transform;
        }

        void Bar(Transform parent, Vector3 a, Vector3 b, float thickness)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            bar.name = "Bar";
            BubblePlayVisuals.Release(bar.GetComponent<Collider>());
            bar.transform.SetParent(parent, false);
            var along = b - a;
            // The built-in capsule is 2 tall (caps included) and 1 wide along y.
            bar.transform.localPosition = (a + b) * .5f;
            bar.transform.localRotation = Quaternion.FromToRotation(Vector3.up, along.sqrMagnitude > 1e-8f ? along.normalized : Vector3.up);
            bar.transform.localScale = new Vector3(thickness, (along.magnitude + thickness) * .5f, thickness);
            var renderer = bar.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = m_Chrome;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        void SetNameSprite(string folder)
        {
            if (m_Name == null) return;
            m_Name.sprite = Moodium.UI.UIRefreshVisuals.Load(BubbleChoiceRules.WorldLabel(folder, m_English));
            if (m_Name.sprite == null) Debug.LogWarning($"[Bubble Door] no name sprite for {folder}");
        }

        /// <summary>An arrow: the family's world one step to that side (ignored while hidden or busy).</summary>
        public void Step(int direction)
        {
            if (!Shown || m_Busy != null) return;
            var next = DoorSwitchRules.Next(m_Worlds, m_Current, direction);
            if (next != null && next != m_Current) m_Chosen?.Invoke(next);
        }

        /// <summary>Picks <paramref name="folder"/> directly (tests, tools). Ignored while hidden or busy, and for the current world.</summary>
        public void Choose(string folder)
        {
            if (!Shown || m_Busy != null || folder == m_Current || !m_Worlds.Contains(folder)) return;
            m_Chosen?.Invoke(folder);
        }

        /// <summary>The owner started loading <paramref name="folder"/>: its name shows, pulsing.</summary>
        public void SetBusy(string folder) { m_Busy = folder; SetNameSprite(folder); }

        /// <summary><paramref name="folder"/> is now the world behind the door.</summary>
        public void SetCurrent(string folder) { m_Current = folder; m_Busy = null; SetNameSprite(folder); }

        /// <summary>Editor mouse: presses the control under <paramref name="ray"/>, if any.</summary>
        public bool TryPick(Ray ray)
        {
            foreach (var hit in m_Hits)
                if (hit != null && hit.enabled && hit.gameObject.activeInHierarchy && hit.Raycast(ray, out _, 100f))
                {
                    hit.GetComponent<Moodium.Flow.MoodiumSpatialButton>()?.Press();
                    return true;
                }
            return false;
        }

        /// <summary>One frame: fades in when <paramref name="show"/>, out otherwise; everything turns to face <paramref name="eye"/> (world space).</summary>
        public void Step(Vector3 eye, bool show, float dt, float time)
        {
            if (m_Door == null) return;
            m_Visible = dt <= 0f ? (show ? 1f : 0f) : Mathf.MoveTowards(m_Visible, show ? 1f : 0f, dt / DoorSwitchRules.FadeSeconds);
            var room = m_Door.Room;
            var z = m_Door.DoorZ;
            var on = m_Visible > .01f;
            if (m_Name != null && m_Name.sprite != null)
            {
                var height = m_Name.sprite.bounds.size.y * DoorSwitchRules.LabelScale;
                Place(m_Name.transform, room.TransformPoint(DoorSwitchRules.NameCentre(z, height)), eye, DoorSwitchRules.LabelScale, on);
                var alpha = m_Visible * (m_Busy != null ? .6f + .4f * (.5f + .5f * Mathf.Sin(time * Mathf.PI * 2f / 1.2f)) : 1f);
                m_Name.color = new Color(1f, 1f, 1f, alpha);
            }
            // The arrows lean gently toward where they point; the controls shrink in and out instead of fading (they are metal).
            var nudge = .008f * Mathf.Sin(time * Mathf.PI * 2f / 1.6f);
            var scale = Mathf.SmoothStep(0f, 1f, m_Visible);
            if (m_Left != null) Place(m_Left, room.TransformPoint(DoorSwitchRules.ArrowCentre(-1, z) + Vector3.left * nudge), eye, scale, on);
            if (m_Right != null) Place(m_Right, room.TransformPoint(DoorSwitchRules.ArrowCentre(1, z) + Vector3.right * nudge), eye, scale, on);
            if (m_CloseButton != null) Place(m_CloseButton, room.TransformPoint(DoorSwitchRules.CloseCentre(z)), eye, scale, on);
            foreach (var hit in m_Hits) if (hit != null) hit.enabled = show && m_Busy == null;
        }

        static void Place(Transform t, Vector3 at, Vector3 eye, float scale, bool on)
        {
            if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
            if (!on) return;
            var look = at - eye;
            t.SetPositionAndRotation(at, look.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(look, Vector3.up) : Quaternion.identity);
            t.localScale = Vector3.one * Mathf.Max(1e-4f, scale);
        }

        void OnDestroy() => BubblePlayVisuals.Release(m_Chrome);
    }
}
