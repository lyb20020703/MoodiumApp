using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;

namespace Moodium.NatureWorld
{
    /// <summary>
    /// Geometry of the portal probe room, in the room's own frame: origin on the floor under the viewer's head,
    /// +z = where the viewer was looking, the door in the front wall. Pure (EditMode tests).
    /// </summary>
    public static class PortalProbeLayout
    {
        /// <summary>Room: x in ±HalfWidth, z from −BackDepth to DoorDistance, floor at FloorLift, ceiling at Height.</summary>
        public const float HalfWidth = 1.1f, BackDepth = 1f, DoorDistance = 1f, Height = 2.6f, FloorLift = .02f;
        /// <summary>The door: centred in the front wall, from the floor up to DoorHeight.</summary>
        public const float DoorWidth = .9f, DoorHeight = 2f;
        /// <summary>Thin visible outline around the door (on the room side), so the door can be found even if occlusion fails.</summary>
        public const float FrameWidth = .025f, FrameInset = .004f;
        /// <summary>Hysteresis for leaving / re-entering the room, metres.</summary>
        public const float Margin = .05f;

        public static bool Contains(Vector3 p, float grow) =>
            p.x > -HalfWidth - grow && p.x < HalfWidth + grow &&
            p.z > -BackDepth - grow && p.z < DoorDistance + grow &&
            p.y < Height + grow;

        /// <summary>One frame of "is the head in the room": leave once clearly outside, come back once clearly inside.</summary>
        public static bool NextInRoom(bool inRoom, Vector3 local) => inRoom ? Contains(local, Margin) : Contains(local, -Margin);

        public static bool InDoor(float x, float y) =>
            Mathf.Abs(x) <= DoorWidth * .5f && y >= FloorLift && y <= DoorHeight;

        /// <summary>The room's six faces, all facing inward, with the door cut out of the front wall.</summary>
        public static Mesh Walls()
        {
            float w = HalfWidth, b = -BackDepth, d = DoorDistance, h = Height, f = FloorLift, dw = DoorWidth * .5f, dh = DoorHeight;
            var v = new List<Vector3>(); var t = new List<int>();
            Quad(v, t, new(-w, f, b), new(w, f, b), new(w, f, d), new(-w, f, d), Vector3.up);       // floor
            Quad(v, t, new(-w, h, b), new(w, h, b), new(w, h, d), new(-w, h, d), Vector3.down);     // ceiling
            Quad(v, t, new(-w, f, b), new(-w, h, b), new(-w, h, d), new(-w, f, d), Vector3.right);  // left
            Quad(v, t, new(w, f, b), new(w, h, b), new(w, h, d), new(w, f, d), Vector3.left);       // right
            Quad(v, t, new(-w, f, b), new(w, f, b), new(w, h, b), new(-w, h, b), Vector3.forward);  // back
            Quad(v, t, new(-w, f, d), new(-dw, f, d), new(-dw, h, d), new(-w, h, d), Vector3.back); // front, left of the door
            Quad(v, t, new(dw, f, d), new(w, f, d), new(w, h, d), new(dw, h, d), Vector3.back);     // front, right of the door
            Quad(v, t, new(-dw, dh, d), new(dw, dh, d), new(dw, h, d), new(-dw, h, d), Vector3.back); // above the door
            return Build("Portal Probe Walls", v, t);
        }

        /// <summary>The door opening seen from the world side: hides the world behind it, so the room shows through.</summary>
        public static Mesh BackDoor()
        {
            float d = DoorDistance, f = FloorLift, dw = DoorWidth * .5f, dh = DoorHeight;
            var v = new List<Vector3>(); var t = new List<int>();
            Quad(v, t, new(-dw, f, d), new(dw, f, d), new(dw, dh, d), new(-dw, dh, d), Vector3.forward);
            return Build("Portal Probe Back Door", v, t);
        }

        /// <summary>Left, right and top strips around the door, just inside the front wall.</summary>
        public static Mesh Frame()
        {
            float z = DoorDistance - FrameInset, f = FloorLift, dw = DoorWidth * .5f, dh = DoorHeight, fw = FrameWidth;
            var v = new List<Vector3>(); var t = new List<int>();
            Quad(v, t, new(-dw - fw, f, z), new(-dw, f, z), new(-dw, dh + fw, z), new(-dw - fw, dh + fw, z), Vector3.back);
            Quad(v, t, new(dw, f, z), new(dw + fw, f, z), new(dw + fw, dh + fw, z), new(dw, dh + fw, z), Vector3.back);
            Quad(v, t, new(-dw, dh, z), new(dw, dh, z), new(dw, dh + fw, z), new(-dw, dh + fw, z), Vector3.back);
            return Build("Portal Probe Door Frame", v, t);
        }

        /// <summary>Adds quad a-b-c-d (a loop) with its front face toward <paramref name="facing"/>.</summary>
        static void Quad(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
        {
            var i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            // Unity front faces wind clockwise as seen by the viewer, i.e. cross(v1 - v0, v2 - v0) points at the viewer.
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) > 0f) t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            else t.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
        }

        static Mesh Build(string name, List<Vector3> v, List<int> t)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(v);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>
    /// Device test for a walk-through portal (2026-11-02), not a feature. PolySpatial 2.4.3 has no RealityKit portal,
    /// so this checks Unity's suggested route: an occlusion material (AR/Basic Occlusion → RealityKit OcclusionMaterial).
    /// Once the viewer is inside a bubble world, an invisible room is put around them with a door 1 m ahead: in the
    /// room the real surroundings should show again and the world only through the door; stepping out through the
    /// door puts them in the world, and looking back the door should show the real room. Off unless the scripting
    /// define MOODIUM_PORTAL_PROBE is set. Shown only on device / simulator: the Editor does not draw occlusion.
    /// </summary>
    public sealed class BubblePortalProbe : MonoBehaviour
    {
        public const string OccluderResource = "MoodiumPortalProbe/PortalOccluder";
        public static readonly string[] OccluderShaders = { "AR/Basic Occlusion", "AR/Occlusion" };
        const float PlaceDelaySeconds = 1f, ScanSeconds = .5f;

        /// <summary>Follow the bubble worlds by itself (the define's bootstrap); off for tests, which drive Place / Step.</summary>
        public bool FollowBubbleWorlds;

        public Transform Room => m_Room;
        public bool InRoom => m_InRoom;
        public bool OccluderFound { get; private set; }

        Transform m_Room;
        GameObject m_Walls, m_BackDoor, m_Frame;
        Material m_Occluder, m_FrameMaterial;
        readonly List<Mesh> m_Meshes = new();
        bool m_InRoom;
        BubbleWorldController m_Target, m_Pending;
        float m_PendingSince, m_NextScan;

#if MOODIUM_PORTAL_PROBE
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("Portal Probe");
            DontDestroyOnLoad(go);
            go.AddComponent<BubblePortalProbe>().FollowBubbleWorlds = true;
            Debug.Log("[Portal Probe] on (MOODIUM_PORTAL_PROBE). Enter a bubble world to test.");
        }
#endif

        /// <summary>The occlusion material: the project asset when present (so the shader is in the build), else by shader name.</summary>
        public static Material CreateOccluder(out bool found)
        {
            var asset = Resources.Load<Material>(OccluderResource);
            if (asset != null && asset.shader != null && System.Array.IndexOf(OccluderShaders, asset.shader.name) >= 0)
            {
                found = true;
                return new Material(asset) { name = "Portal Probe Occluder" };
            }
            foreach (var name in OccluderShaders)
            {
                var shader = Shader.Find(name);
                if (shader == null) continue;
                found = true;
                return new Material(shader) { name = "Portal Probe Occluder" };
            }
            // Without the shader the test means nothing: show the walls as a faint red film so that is obvious on device.
            found = false;
            return BubbleWorldData.CreateUnlit("Portal Probe Occluder (MISSING)", null, true, CullMode.Off, new Color(1f, .2f, .2f, .25f));
        }

        /// <summary>Puts the room around a head at <paramref name="head"/> looking along <paramref name="forward"/>, floor at <paramref name="floorY"/>.</summary>
        public void Place(Vector3 head, Vector3 forward, float floorY)
        {
            if (m_Room == null) Build();
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            m_Room.SetPositionAndRotation(new Vector3(head.x, floorY, head.z), Quaternion.LookRotation(forward.normalized, Vector3.up));
            m_Room.gameObject.SetActive(true);
            m_InRoom = true;
            Apply();
            Debug.Log($"[Portal Probe] room placed: floor {floorY:0.00} m, head {head.y - floorY:0.00} m above it, door {PortalProbeLayout.DoorDistance:0.0} m ahead; occluder shader = {m_Occluder.shader.name}" +
                      (OccluderFound ? "" : " — AR occlusion shader NOT FOUND, run Moodium > Portal Probe > Create Occluder Material"));
        }

        /// <summary>One frame with the head at <paramref name="head"/> (world space).</summary>
        public void Step(Vector3 head)
        {
            if (m_Room == null || !m_Room.gameObject.activeSelf) return;
            var local = m_Room.InverseTransformPoint(head);
            var next = PortalProbeLayout.NextInRoom(m_InRoom, local);
            if (next == m_InRoom) return;
            m_InRoom = next;
            Apply();
            Debug.Log(next
                ? "[Portal Probe] head back in the room: real room around, world only through the door"
                : $"[Portal Probe] head left the room ({(PortalProbeLayout.InDoor(local.x, local.y) && local.z > 0f ? "through the door" : "through a wall")}): world around, door shows the room");
        }

        public void Clear()
        {
            if (m_Room != null) m_Room.gameObject.SetActive(false);
            if (m_Target != null) m_Target.IgnoreWander = false;
            m_Target = null;
        }

        // In the room: the walls hide the world everywhere but the door. Outside: walls off (they would show a
        // box of the room), the back door on, so the door opening shows the room. Swapped by activation, so it
        // does not depend on how the occluder culls faces.
        void Apply()
        {
            m_Walls.SetActive(m_InRoom);
            m_BackDoor.SetActive(!m_InRoom);
        }

        void Build()
        {
            m_Room = new GameObject("Portal Probe Room").transform;
            m_Room.SetParent(transform, false);
            m_Occluder = CreateOccluder(out var found);
            OccluderFound = found;
            m_FrameMaterial = BubbleWorldData.CreateUnlit("Portal Probe Door Frame", null, true, CullMode.Off, new Color(.85f, 1f, .95f, .7f));
            m_Walls = Part("Walls (occluder)", PortalProbeLayout.Walls(), m_Occluder);
            m_BackDoor = Part("Back Door (occluder)", PortalProbeLayout.BackDoor(), m_Occluder);
            m_Frame = Part("Door Frame", PortalProbeLayout.Frame(), m_FrameMaterial);
        }

        GameObject Part(string name, Mesh mesh, Material material)
        {
            m_Meshes.Add(mesh);
            var go = new GameObject(name);
            go.transform.SetParent(m_Room, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        void Update()
        {
            if (!FollowBubbleWorlds) return;
            var viewer = Camera.main;
            if (viewer == null) return;
            if (m_Target != null && m_Target.CurrentPhase != BubbleWorldController.Phase.Inside)
            {
                Debug.Log("[Portal Probe] world left: room removed");
                Clear();
            }
            else if (m_Target == null && m_Room != null && m_Room.gameObject.activeSelf) Clear(); // the world was destroyed

            if (m_Target == null && Time.unscaledTime >= m_NextScan)
            {
                m_NextScan = Time.unscaledTime + ScanSeconds;
                BubbleWorldController inside = null;
                foreach (var world in FindObjectsByType<BubbleWorldController>(FindObjectsSortMode.None))
                    if (world.CurrentPhase == BubbleWorldController.Phase.Inside && !world.DoorOpen) { inside = world; break; }   // the mercury door has its own room
                if (inside != m_Pending) { m_Pending = inside; m_PendingSince = Time.unscaledTime; }
                else if (inside != null && Time.unscaledTime - m_PendingSince >= PlaceDelaySeconds)
                {
                    m_Target = inside;
                    // The door is 1 m away: without this, walking out fades the world and asks the viewer to come back.
                    inside.IgnoreWander = true;
                    var origin = FindFirstObjectByType<XROrigin>();
                    var floor = origin != null && origin.Origin != null ? origin.Origin.transform.position.y : 0f;
                    Place(viewer.transform.position, viewer.transform.forward, floor);
                }
            }
            Step(viewer.transform.position);
        }

        void OnDestroy()
        {
            BubblePlayVisuals.Release(m_Occluder);
            BubblePlayVisuals.Release(m_FrameMaterial);
            foreach (var mesh in m_Meshes) BubblePlayVisuals.Release(mesh);
            m_Meshes.Clear();
        }
    }
}
