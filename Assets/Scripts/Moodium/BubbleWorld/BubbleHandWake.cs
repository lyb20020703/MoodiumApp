using System;
using System.Threading.Tasks;
using Moodium.Interaction;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

namespace Moodium.NatureWorld
{
    /// <summary>
    /// Pure helpers for <see cref="BubbleHandWake"/> (EditMode tests). Thread-safe: no Unity objects.
    /// </summary>
    public static class BubbleHandWakeMath
    {
        public const int LonBins = 128, LatBins = 64, PerBin = 12;

        /// <summary>Equirectangular direction bin (scan frame), the same mapping as the panorama.</summary>
        public static int Bin(Vector3 direction, int lonBins = LonBins, int latBins = LatBins)
        {
            var length = direction.magnitude;
            if (length < 1e-6f) direction = Vector3.forward; else direction /= length;
            var lon = Math.Atan2(direction.x, direction.z);
            var lat = Math.Asin(Math.Max(-1.0, Math.Min(1.0, direction.y)));
            var i = (int)((lon + Math.PI) / (2 * Math.PI) * lonBins);
            var j = (int)((lat + Math.PI * .5) / Math.PI * latBins);
            i = Math.Max(0, Math.Min(lonBins - 1, i));
            j = Math.Max(0, Math.Min(latBins - 1, j));
            return j * lonBins + i;
        }

        /// <summary>
        /// Direction → point lookup built from points.bytes (MSPC v1, see BubbleWorldData). Points are stored near → far,
        /// so each bin keeps the first (nearest) points in that direction — the ones the viewer actually sees there.
        /// Returns <c>lonBins × latBins × perBin</c> point indices, −1 for empty slots; null for invalid data.
        /// </summary>
        public static int[] BuildLookup(byte[] bytes, int lonBins = LonBins, int latBins = LatBins, int perBin = PerBin)
        {
            if (bytes == null || bytes.Length < 20 || bytes[0] != 'M' || bytes[1] != 'S' || bytes[2] != 'P' || bytes[3] != 'C')
                return null;
            if (BitConverter.ToInt32(bytes, 4) != 1) return null;
            var count = BitConverter.ToInt32(bytes, 8);
            if (count <= 0 || bytes.Length < 20 + (long)count * BubbleWorldData.RecordSize) return null;
            var bins = lonBins * latBins;
            var lookup = new int[bins * perBin];
            for (var k = 0; k < lookup.Length; k++) lookup[k] = -1;
            var fill = new int[bins];
            var full = 0;
            for (var i = 0; i < count && full < bins; i++)
            {
                var offset = 20 + i * BubbleWorldData.RecordSize;
                var p = new Vector3(BitConverter.ToSingle(bytes, offset), BitConverter.ToSingle(bytes, offset + 4),
                    BitConverter.ToSingle(bytes, offset + 8));
                if (p.sqrMagnitude < 1e-6f) continue;
                var bin = Bin(p, lonBins, latBins);
                if (fill[bin] >= perBin) continue;
                lookup[bin * perBin + fill[bin]] = i;
                if (++fill[bin] == perBin) full++;
            }
            return lookup;
        }

        /// <summary>
        /// A point the viewer sees in <paramref name="direction"/> (scan frame), or −1. Bins without points (open sky)
        /// borrow from the nearest non-empty bin up to <paramref name="maxRing"/> bins away.
        /// </summary>
        public static int Pick(int[] lookup, Vector3 direction, float random01, int maxRing = 6,
            int lonBins = LonBins, int latBins = LatBins, int perBin = PerBin)
        {
            if (lookup == null) return -1;
            var bin = Bin(direction, lonBins, latBins);
            int j0 = bin / lonBins, i0 = bin % lonBins;
            for (var ring = 0; ring <= maxRing; ring++)
            for (var dj = -ring; dj <= ring; dj++)
            for (var di = -ring; di <= ring; di++)
            {
                if (Math.Max(Math.Abs(di), Math.Abs(dj)) != ring) continue;
                var j = j0 + dj;
                if (j < 0 || j >= latBins) continue;
                var b = j * lonBins + ((i0 + di) % lonBins + lonBins) % lonBins;
                var n = 0;
                while (n < perBin && lookup[b * perBin + n] >= 0) n++;
                if (n > 0) return lookup[b * perBin + Math.Min(n - 1, (int)(random01 * n))];
            }
            return -1;
        }

        /// <summary>Any point of the world (first filled bin from a random start), or −1 when the lookup is empty.</summary>
        public static int PickAny(int[] lookup, float random01, int perBin = PerBin)
        {
            if (lookup == null || lookup.Length < perBin) return -1;
            var bins = lookup.Length / perBin;
            var first = Math.Min(bins - 1, (int)(random01 * bins));
            for (var k = 0; k < bins; k++)
            {
                var b = (first + k) % bins;
                if (lookup[b * perBin] >= 0) return lookup[b * perBin];
            }
            return -1;
        }

        /// <summary>Points per second for one hand at <paramref name="speed"/> m/s.</summary>
        public static float SpawnRate(float speed, float startSpeed, float fullSpeed, float maxRate) =>
            maxRate * Mathf.Clamp01((speed - startSpeed) / Mathf.Max(.001f, fullSpeed - startSpeed));

        /// <summary>Size multiplier over a point's life (0..1): quick pop-in, hold, then shrink away to nothing.</summary>
        public static float SizeAt(float age01) =>
            Mathf.Clamp01(age01 / .08f) * (1f - BubbleSplatMotionMath.Smooth(.45f, 1f, age01));

        /// <summary>Velocity after one step: a gentle swirl, a little lift, and drag so the wake settles.</summary>
        public static Vector3 Drift(Vector3 velocity, Vector3 position, float seed, float time, float dt,
            float drag, float swirl, float lift)
        {
            var s = new Vector3(
                Mathf.Sin(position.y * 9f + time * 1.7f + seed * 6.283f),
                Mathf.Sin(position.z * 9f + time * 1.3f + seed * 12.57f),
                Mathf.Sin(position.x * 9f + time * 1.1f + seed * 18.85f));
            velocity += (s * swirl + Vector3.up * lift) * dt;
            return velocity * Mathf.Exp(-drag * dt);
        }
    }

    /// <summary>
    /// Hand wake for the bubble worlds. While the viewer is inside a world and moves a hand, small round points come
    /// off the palm and fingertips, ride along with the motion, swirl and dissolve, as if the hand were stirring the scan.
    ///
    /// The scan's own points are 0.8 m or more away (out of reach) and its meshes are immutable, so the wake does not
    /// move them. Instead every wake point borrows a real scan point: the one the viewer sees straight behind the hand.
    /// It uses that point's 4×4 block in the world's round colour atlas and the same URP/Unlit round-point material as
    /// the world (BubbleSplatMaterial.Create, static path), so colour and dot shape match the scene and never turn
    /// into squares on visionOS. Size fades to zero instead of alpha (one material for all points).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BubbleHandWake : MonoBehaviour
    {
        [Header("Spawning")]
        [Tooltip("Hand speed (m/s) where points start to come off.")]
        [Min(0f)] public float StartSpeed = .06f;
        [Tooltip("Hand speed (m/s) that gives the full stream.")]
        [Min(.1f)] public float FullSpeed = .7f;
        [Min(0f)] public float MaxPerSecondPerHand = 180f;
        [Tooltip("Points per second from a hand that is held still in view, so reaching out always shows something.")]
        [Min(0f)] public float IdlePerSecondPerHand = 14f;
        [Tooltip("Points spawn within this radius around the palm and fingertips (m).")]
        [Min(0f)] public float SpawnRadius = .035f;
        [Tooltip("Hands closer than this in front of the eyes do not spawn (m).")]
        [Min(0f)] public float MinForward = .15f;

        [Header("Look")]
        [Tooltip("Quad edge in metres, random between x and y (wake points only; the world's points are not affected). " +
                 "4–9 mm was too small to notice on device.")]
        public Vector2 SizeMetres = new(.007f, .014f);
        public Vector2 LifeSeconds = new(1.1f, 2f);

        [Header("Motion")]
        [Tooltip("Share of the hand velocity a point takes along.")]
        [Range(0f, 1.5f)] public float Carry = .55f;
        [Tooltip("Random outward speed (m/s) at full hand speed.")]
        [Min(0f)] public float Spread = .25f;
        [Min(0f)] public float Drag = 1.6f;
        [Min(0f)] public float Swirl = .35f;
        public float Lift = .03f;

        /// <summary>Set by BubbleWorldController: only while the viewer is inside the world.</summary>
        public bool Active { get; set; }
        public int Capacity => m_Points != null ? m_Points.Length : 0;
        public int LivePoints => m_Live;
        public bool LookupReady => m_Lookup != null;
        public Mesh WakeMesh => m_Mesh;
        public Material WakeMaterial => m_Material;

        struct Point
        {
            public Vector3 Position, Velocity;
            public float Age, Life, Size, Seed;
            public bool Alive;
        }

        sealed class HandTrack
        {
            public bool Has;
            public Vector3 Last, Velocity;
            public float Pending;
        }

        static readonly XRHandJointID[] SpawnJoints =
        {
            XRHandJointID.Palm, XRHandJointID.Palm, XRHandJointID.IndexTip, XRHandJointID.MiddleTip,
            XRHandJointID.RingTip, XRHandJointID.LittleTip, XRHandJointID.ThumbTip
        };

        Point[] m_Points;
        int m_Live, m_Next;
        bool m_UploadedEmpty;
        Mesh m_Mesh;
        Material m_Material;
        Vector3[] m_Vertices;
        Vector2[] m_UVs;
        bool m_UVsDirty;
        int[] m_Lookup;
        Task<int[]> m_LookupTask;
        int m_AtlasSize;
        Transform m_Scan;
        Camera m_Viewer;
        XRHandSubsystem m_Hands;
        XROrigin m_Origin;
        int m_Spawned, m_LoggedSpawned = -1;
        float m_NextLogAt;
        bool m_WasActive;
        readonly HandTrack[] m_Track = { new(), new() };
        System.Random m_Random = new(20261030);

        /// <param name="points">points.bytes of this world (the array BubbleWorldController already loaded).</param>
        /// <param name="roundColors">point_colors_round of this world (4×4 block per point).</param>
        /// <param name="scan">The scan root: directions are looked up in its frame.</param>
        public void Initialize(byte[] points, Texture roundColors, float eyeHeight, bool candy, Transform scan,
            Camera viewer, int capacity = 700)
        {
            m_Scan = scan;
            m_Viewer = viewer;
            m_AtlasSize = roundColors != null ? roundColors.width : 0;
            var count = points != null && points.Length >= 12 ? BitConverter.ToInt32(points, 8) : 0;
            if (count > 0 && m_AtlasSize != BubbleWorldData.RoundAtlasSizeFor(count))
                Debug.LogWarning($"[Bubble Wake] Colour atlas is {m_AtlasSize} px, expected {BubbleWorldData.RoundAtlasSizeFor(count)} for {count} points.");

            m_Points = new Point[Mathf.Max(1, capacity)];
            m_Vertices = new Vector3[m_Points.Length * 4];
            m_UVs = new Vector2[m_Points.Length * 4];
            var indices = new int[m_Points.Length * 6];
            for (var k = 0; k < m_Points.Length; k++)
            {
                int v = k * 4, q = k * 6;
                indices[q] = v; indices[q + 1] = v + 1; indices[q + 2] = v + 2;
                indices[q + 3] = v; indices[q + 4] = v + 2; indices[q + 5] = v + 3;
            }
            m_Mesh = new Mesh { name = "Bubble Hand Wake" };
            if (m_Vertices.Length > 65535) m_Mesh.indexFormat = IndexFormat.UInt32;
            m_Mesh.MarkDynamic();
            m_Mesh.vertices = m_Vertices;
            m_Mesh.uv = m_UVs;
            m_Mesh.triangles = indices;
            // Points live within a few metres of the viewer; never let the empty or tiny mesh be culled.
            m_Mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 20f);

            // Same static round-point material as the world (URP/Unlit, alpha clip 0 → round dots on visionOS).
            m_Material = BubbleSplatMaterial.Create(roundColors, eyeHeight, candy);
            m_Material.name = "Bubble Hand Wake Points";

            if (!TryGetComponent<MeshFilter>(out var filter)) filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = m_Mesh;
            if (!TryGetComponent<MeshRenderer>(out var renderer)) renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = m_Material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            m_Lookup = null;
            m_LookupTask = points != null ? Task.Run(() => BubbleHandWakeMath.BuildLookup(points)) : null;
        }

        /// <summary>Tests: block until the background lookup is built.</summary>
        public bool WaitForLookup(int milliseconds)
        {
            if (m_LookupTask != null && !m_LookupTask.Wait(milliseconds)) return false;
            CollectLookup();
            return m_Lookup != null;
        }

        void CollectLookup()
        {
            if (m_LookupTask == null || !m_LookupTask.IsCompleted) return;
            m_Lookup = m_LookupTask.Status == TaskStatus.RanToCompletion ? m_LookupTask.Result : null;
            if (m_Lookup == null) Debug.LogWarning("[Bubble Wake] Could not build the point lookup; the hand wake stays off.");
            else Debug.Log("[Bubble Wake] Point lookup ready.");
            m_LookupTask = null;
        }

        /// <summary>
        /// Starts one wake point at <paramref name="position"/> (world), coloured like the scan point the viewer sees
        /// from <paramref name="eye"/> through it. False when nothing is in that direction.
        /// </summary>
        public bool SpawnAt(Vector3 position, Vector3 velocity, Vector3 eye)
        {
            if (m_Lookup == null || m_Points == null || m_AtlasSize <= 0) return false;
            var direction = m_Scan != null ? m_Scan.InverseTransformDirection(position - eye) : position - eye;
            var r = (float)m_Random.NextDouble();
            var index = BubbleHandWakeMath.Pick(m_Lookup, direction, r);
            // Open sky behind the hand: a colour from a wider neighbourhood, then any point of this world.
            if (index < 0) index = BubbleHandWakeMath.Pick(m_Lookup, direction, r, 24);
            if (index < 0) index = BubbleHandWakeMath.PickAny(m_Lookup, r);
            if (index < 0) return false;
            m_Spawned++;
            var slot = m_Next;
            m_Next = (m_Next + 1) % m_Points.Length;   // when full, the oldest point gives way
            if (!m_Points[slot].Alive) m_Live++;
            m_Points[slot] = new Point
            {
                Position = position,
                Velocity = velocity,
                Age = 0f,
                Life = Mathf.Lerp(LifeSeconds.x, LifeSeconds.y, (float)m_Random.NextDouble()),
                Size = Mathf.Lerp(SizeMetres.x, SizeMetres.y, (float)m_Random.NextDouble()),
                Seed = (float)m_Random.NextDouble(),
                Alive = true
            };
            BubbleWorldData.WriteRoundUVs(m_UVs, slot * 4, index, m_AtlasSize);
            m_UVsDirty = true;
            return true;
        }

        /// <summary>Moves, ages and draws the points. <paramref name="right"/>/<paramref name="up"/>: viewer axes (world).</summary>
        public void Step(float dt, Vector3 right, Vector3 up)
        {
            if (m_Points == null) return;
            dt = Mathf.Clamp(dt, 0f, .05f);
            var time = Time.time;
            for (var k = 0; k < m_Points.Length; k++)
            {
                ref var p = ref m_Points[k];
                if (!p.Alive) continue;
                p.Age += dt;
                if (p.Age >= p.Life) { p.Alive = false; m_Live--; continue; }
                p.Velocity = BubbleHandWakeMath.Drift(p.Velocity, p.Position, p.Seed, time, dt, Drag, Swirl, Lift);
                p.Position += p.Velocity * dt;
            }
            Upload(right, up);
        }

        void Upload(Vector3 right, Vector3 up)
        {
            if (m_Live == 0 && m_UploadedEmpty && !m_UVsDirty) return;   // nothing changed: no mesh sync
            // Local vectors of one world metre: correct under the (breathing) scan scale.
            var r = transform.InverseTransformVector(right.normalized);
            var u = transform.InverseTransformVector(up.normalized);
            for (var k = 0; k < m_Points.Length; k++)
            {
                var v = k * 4;
                ref var p = ref m_Points[k];
                if (!p.Alive)
                {
                    m_Vertices[v] = m_Vertices[v + 1] = m_Vertices[v + 2] = m_Vertices[v + 3] = Vector3.zero;
                    continue;
                }
                var half = .5f * p.Size * BubbleHandWakeMath.SizeAt(p.Age / Mathf.Max(.001f, p.Life));
                var c = transform.InverseTransformPoint(p.Position);
                var a = (r + u) * half;
                var b = (r - u) * half;
                m_Vertices[v] = c - a;
                m_Vertices[v + 1] = c + b;
                m_Vertices[v + 2] = c + a;
                m_Vertices[v + 3] = c - b;
            }
            m_Mesh.vertices = m_Vertices;
            if (m_UVsDirty) { m_Mesh.uv = m_UVs; m_UVsDirty = false; }
            m_UploadedEmpty = m_Live == 0;
        }

        void LateUpdate()
        {
            if (m_Mesh == null) return;
            if (m_Lookup == null) CollectLookup();
            if (m_Viewer == null) m_Viewer = Camera.main;
            if (m_Viewer == null) return;
            var dt = Time.deltaTime;
            if (Active && m_Lookup != null) SpawnFromHands(dt);
            else { m_Track[0].Has = m_Track[1].Has = false; }
            LogState();
            var view = m_Viewer.transform;
            Step(dt, view.right, view.up);
        }

        /// <summary>Device logs: one line on each Active change and every 5 s while active, to see why nothing shows.</summary>
        void LogState()
        {
            var changed = Active != m_WasActive;
            m_WasActive = Active;
            if (!changed && (!Active || Time.unscaledTime < m_NextLogAt)) return;
            m_NextLogAt = Time.unscaledTime + 5f;
            if (!changed && m_Spawned == m_LoggedSpawned) return;
            m_LoggedSpawned = m_Spawned;
            var hands = m_Hands != null ? (m_Hands.running ? "running" : "stopped") : "missing";
            var left = m_Hands != null && m_Hands.leftHand.isTracked;
            var right = m_Hands != null && m_Hands.rightHand.isTracked;
            Debug.Log($"[Bubble Wake] active={Active} lookup={(m_Lookup != null)} hands={hands} L={left} R={right} " +
                      $"spawned={m_Spawned} live={m_Live} renderer={(TryGetComponent<MeshRenderer>(out var mr) && mr.enabled && gameObject.activeInHierarchy)}");
        }

        void SpawnFromHands(float dt)
        {
            if (m_Hands == null || !m_Hands.running)
                m_Hands = XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRHandSubsystem>();
            if (m_Hands == null || !m_Hands.running || dt <= 0f) return;
            if (m_Origin == null) m_Origin = FindFirstObjectByType<XROrigin>();
            var tracking = m_Origin != null ? m_Origin.Origin.transform : null;
            var eye = m_Viewer.transform.position;
            var forward = m_Viewer.transform.forward;
            for (var h = 0; h < 2; h++)
            {
                var hand = h == 0 ? m_Hands.leftHand : m_Hands.rightHand;
                var track = m_Track[h];
                if (!hand.isTracked || !hand.GetJoint(XRHandJointID.Palm).TryGetPose(out var palmPose))
                { track.Has = false; continue; }
                var palm = PalmPressGestureController.TransformTrackingPose(palmPose, tracking).position;
                if (!track.Has) { track.Has = true; track.Last = palm; track.Velocity = Vector3.zero; track.Pending = 0f; continue; }
                var raw = (palm - track.Last) / dt;
                track.Velocity = Vector3.Lerp(track.Velocity, raw, 1f - Mathf.Exp(-dt / .06f));
                track.Last = palm;
                if (Vector3.Dot(palm - eye, forward) < MinForward) { track.Pending = 0f; continue; }
                var speed = track.Velocity.magnitude;
                track.Pending += (IdlePerSecondPerHand +
                    BubbleHandWakeMath.SpawnRate(speed, StartSpeed, FullSpeed, MaxPerSecondPerHand)) * dt;
                var burst = Mathf.Min(64, (int)track.Pending);
                track.Pending -= burst;
                var spread = Spread * Mathf.Clamp01(speed / FullSpeed);
                for (var n = 0; n < burst; n++)
                {
                    var joint = SpawnJoints[m_Random.Next(SpawnJoints.Length)];
                    if (!hand.GetJoint(joint).TryGetPose(out var pose)) continue;
                    var at = PalmPressGestureController.TransformTrackingPose(pose, tracking).position + RandomInSphere() * SpawnRadius;
                    var carry = Carry * Mathf.Lerp(.6f, 1.2f, (float)m_Random.NextDouble());
                    SpawnAt(at, track.Velocity * carry + RandomInSphere() * spread, eye);
                }
            }
        }

        Vector3 RandomInSphere()
        {
            for (var guard = 0; guard < 8; guard++)
            {
                var v = new Vector3((float)m_Random.NextDouble() * 2f - 1f, (float)m_Random.NextDouble() * 2f - 1f,
                    (float)m_Random.NextDouble() * 2f - 1f);
                if (v.sqrMagnitude <= 1f) return v;
            }
            return Vector3.zero;
        }

        /// <summary>Removes all points at once (leaving, hand-off).</summary>
        public void Clear()
        {
            if (m_Points == null) return;
            for (var k = 0; k < m_Points.Length; k++) m_Points[k].Alive = false;
            m_Live = 0;
            m_UploadedEmpty = false;
            if (m_Viewer != null) Upload(m_Viewer.transform.right, m_Viewer.transform.up);
        }

        void OnDestroy()
        {
            Release(m_Mesh);
            Release(m_Material);
        }

        static void Release(UnityEngine.Object asset)
        {
            if (asset == null) return;
            if (Application.isPlaying) Destroy(asset); else DestroyImmediate(asset);
        }
    }
}
