using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;
using Unity.XR.CoreUtils;
using Moodium.Interaction;

namespace Moodium.Interaction
{
    /// <summary>Finger-local mesh indentation for grounded houses; never writes the root pose or energy.</summary>
    [DisallowMultipleComponent]
    public sealed class CompletionHouseSoftInteraction : MonoBehaviour
    {
        static readonly XRHandJointID[] ProbeJoints =
        {
            XRHandJointID.ThumbTip, XRHandJointID.IndexTip, XRHandJointID.MiddleTip,
            XRHandJointID.RingTip, XRHandJointID.LittleTip
        };
        static readonly Vector3[] ProbeDirections =
        {
            Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back
        };

        [Header("Local squishy response (metres)")]
        [SerializeField, Min(.01f)] float m_InfluenceRadius = .12f;
        [SerializeField, Min(.001f)] float m_MaxIndentation = .045f;
        [SerializeField, Min(0f)] float m_PressStrength = 1f;
        [SerializeField, Min(.001f)] float m_FingerRadius = .012f;
        [SerializeField, Min(.001f)] float m_SurfaceSearchDistance = .065f;
        [SerializeField, Min(.01f)] float m_ResponseSpeed = 14f;
        [SerializeField, Min(.01f)] float m_RecoverySpeed = 7f;
        [SerializeField, Min(.01f)] float m_Elasticity = 18f;
        [SerializeField, Min(0f)] float m_BaseAnchorHeight = .025f;
        [SerializeField, Range(15f, 60f)] float m_UpdateRate = 20f;
        [SerializeField, Range(5f, 30f)] float m_NormalUpdateRate = 10f;
        [Header("Diagnostics")]
        [SerializeField] bool m_Debug;
        [SerializeField] int m_ActiveContacts;
        [SerializeField] int m_DeformingMeshes;
        [SerializeField] int m_VertexCount;

        sealed class MeshState
        {
            public MeshFilter Filter;
            public Mesh Source;
            public Mesh Runtime;
            public Vector3[] Original;
            public Vector3[] RestWorld;
            public Vector3[] Vertices;
            public Vector3[] Offsets;
            public Vector3[] Velocities;
            public Vector3[] OriginalNormals;
            public MeshCollider Surface;
            public bool Moving;
            public float NextNormalUpdate;
            public readonly Dictionary<Vector3Int, List<int>> Cells = new();
            public readonly HashSet<int> Candidates = new();
            public readonly List<int> Recovering = new();
        }

        struct Contact
        {
            public Vector3 Point;
            public Vector3 Normal;
            public float Depth;
        }

        readonly List<MeshState> m_Meshes = new();
        readonly Vector3[] m_Fingers = new Vector3[10];
        readonly Contact[] m_Contacts = new Contact[10];
        XRHandSubsystem m_HandSubsystem;
        XROrigin m_XROrigin;
        float m_LastSample = float.NegativeInfinity;
        float m_NextStep;
        float m_BaseY;
        bool m_Configured;
        MoodiumTouchAudio m_HoldAudio;
        float m_ContactBurstCooldown;
        public event System.Action<Vector3> ContactBurstRequested;
        const float CellSize = .1f;

        static Vector3Int Cell(Vector3 position) => Vector3Int.FloorToInt(position / CellSize);

        void OnEnable() => TrySubscribeToHands();

        /// <summary>Capture the final, floor-aligned prefab pose and clone every render mesh once.</summary>
        public void Configure()
        {
            if (m_Configured) return;
            m_BaseY = float.PositiveInfinity;
            foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                var source = filter.sharedMesh;
                if (source == null || renderer == null || !renderer.enabled) continue;
                if (!source.isReadable)
                {
                    Debug.LogWarning($"[House Squishy] {name}/{filter.name}: enable Read/Write on {source.name} before building.", this);
                    continue;
                }
                var state = new MeshState
                {
                    Filter = filter, Source = source, Original = source.vertices,
                    Runtime = Instantiate(source)
                };
                state.Runtime.name = source.name + " (House Squishy Runtime)";
                state.Runtime.MarkDynamic();
                var count = state.Original.Length;
                state.Vertices = (Vector3[])state.Original.Clone();
                state.RestWorld = new Vector3[count];
                state.Offsets = new Vector3[count];
                state.Velocities = new Vector3[count];
                state.OriginalNormals = source.normals;
                for (var i = 0; i < count; i++)
                {
                    state.RestWorld[i] = filter.transform.TransformPoint(state.Original[i]);
                    m_BaseY = Mathf.Min(m_BaseY, state.RestWorld[i].y);
                    var cell = Cell(state.RestWorld[i]);
                    if (!state.Cells.TryGetValue(cell, out var indices))
                        state.Cells.Add(cell, indices = new List<int>());
                    indices.Add(i);
                }
                var bounds = source.bounds;
                var scale = filter.transform.lossyScale;
                var minimumScale = Mathf.Max(.0001f, Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                bounds.Expand(m_MaxIndentation * 2f / minimumScale);
                state.Runtime.bounds = bounds;
                // Keep an immutable surface for contact queries. Never recook a changing MeshCollider.
                var query = new GameObject("House Rest Surface", typeof(MeshCollider));
                query.layer = 2; // Ignore Raycast: only this component explicitly queries the collider.
                query.transform.SetParent(filter.transform, false);
                state.Surface = query.GetComponent<MeshCollider>();
                state.Surface.sharedMesh = source;
                filter.sharedMesh = state.Runtime;
                m_Meshes.Add(state);
                m_VertexCount += count;
            }
            m_DeformingMeshes = m_Meshes.Count;
            m_Configured = true;
            m_HoldAudio = GetComponent<MoodiumTouchAudio>();
            if (GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0)
                Debug.LogWarning($"[House Squishy] {name}: skinned renderers are not supported by the static-house deformation path.", this);
            if (m_Meshes.Count == 0)
                Debug.LogWarning($"[House Squishy] {name}: no readable render meshes; no local deformation can be displayed.", this);
            if (m_Debug) Debug.Log($"[House Squishy] {name}: {m_DeformingMeshes} meshes, {m_VertexCount} vertices, root scale={transform.lossyScale}", this);
        }

        public void ConfigureHoldSound(AudioClip clip)
        {
            m_HoldAudio = GetComponent<MoodiumTouchAudio>();
            if (m_HoldAudio == null)
                m_HoldAudio = gameObject.AddComponent<MoodiumTouchAudio>();
            m_HoldAudio.ConfigureHold(clip);
        }

        void Update()
        {
            if (!m_Configured) return;
            if (m_HandSubsystem == null) TrySubscribeToHands();
            if (Time.unscaledTime - m_LastSample > .12f) m_ActiveContacts = 0;
            if (Time.unscaledTime < m_NextStep) return;
            var delta = Mathf.Min(.05f, Mathf.Max(1f / m_UpdateRate, Time.unscaledDeltaTime));
            m_NextStep = Time.unscaledTime + 1f / m_UpdateRate;
            StepDeformation(delta);
            if (m_HoldAudio != null)
            {
                if (m_ActiveContacts > 0) m_HoldAudio.BeginHold();
                else m_HoldAudio.EndHold();
            }
        }

        void TrySubscribeToHands()
        {
            var subsystem = XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRHandSubsystem>();
            if (subsystem == null || subsystem == m_HandSubsystem) return;
            if (m_HandSubsystem != null) m_HandSubsystem.updatedHands -= OnHandsUpdated;
            m_HandSubsystem = subsystem;
            m_HandSubsystem.updatedHands += OnHandsUpdated;
        }

        void OnHandsUpdated(XRHandSubsystem subsystem, XRHandSubsystem.UpdateSuccessFlags flags,
            XRHandSubsystem.UpdateType updateType)
        {
            if (updateType != XRHandSubsystem.UpdateType.Dynamic || !m_Configured) return;
            if (m_XROrigin == null) m_XROrigin = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
            var origin = m_XROrigin != null ? m_XROrigin.Origin.transform : null;
            var count = CollectFingers(subsystem.leftHand, origin, 0);
            count = CollectFingers(subsystem.rightHand, origin, count);
            SetFingerPositions(m_Fingers, count);
        }

        int CollectFingers(XRHand hand, Transform origin, int count)
        {
            if (!hand.isTracked) return count;
            foreach (var id in ProbeJoints)
            {
                var joint = hand.GetJoint(id);
                if (joint.TryGetPose(out var pose))
                    m_Fingers[count++] = PalmPressGestureController.TransformTrackingPose(pose, origin).position;
            }
            return count;
        }

        /// <summary>Consume the existing XR Hands world-space fingertips, including both pinch fingers.</summary>
        public void SetFingerPositions(Vector3[] positions, int count)
        {
            m_ActiveContacts = 0;
            if (!m_Configured || positions == null) return;
            m_LastSample = Time.unscaledTime;
            for (var i = 0; i < Mathf.Min(count, Mathf.Min(positions.Length, m_Contacts.Length)); i++)
                if (TryFindContact(positions[i], out var contact))
                    m_Contacts[m_ActiveContacts++] = contact;
        }

        bool TryFindContact(Vector3 finger, out Contact contact)
        {
            contact = default;
            var nearest = m_SurfaceSearchDistance * m_SurfaceSearchDistance;
            var found = false;
            foreach (var mesh in m_Meshes)
            {
                if (!mesh.Filter.gameObject.activeInHierarchy) continue;
                var bounds = mesh.Surface.bounds;
                if (bounds.SqrDistance(finger) > nearest) continue;
                var reach = bounds.size.magnitude + m_SurfaceSearchDistance;
                foreach (var outward in ProbeDirections)
                {
                    var ray = new Ray(finger + outward * reach, -outward);
                    if (!mesh.Surface.Raycast(ray, out var hit, reach * 2f)) continue;
                    var distance = (hit.point - finger).sqrMagnitude;
                    if (distance > nearest) continue;
                    var signedDistance = Vector3.Dot(finger - hit.point, hit.normal);
                    var depth = Mathf.Clamp((m_FingerRadius - signedDistance) * m_PressStrength, 0f, m_MaxIndentation);
                    if (depth <= .00001f) continue;
                    nearest = distance;
                    found = true;
                    contact = new Contact { Point = hit.point, Normal = hit.normal, Depth = depth };
                }
            }
            return found;
        }

        /// <summary>Update the local indentation field and damped recovery; the house transform stays untouched.</summary>
        public void StepDeformation(float deltaTime)
        {
            if (!m_Configured || deltaTime <= 0f) return;
            deltaTime = Mathf.Min(deltaTime, .05f);
            m_ContactBurstCooldown = Mathf.Max(0f, m_ContactBurstCooldown - deltaTime);
            if (m_ActiveContacts > 0 && m_ContactBurstCooldown <= 0f && ContactBurstRequested != null)
            {
                var strongest = m_Contacts[0];
                for (var i = 1; i < m_ActiveContacts; i++)
                    if (m_Contacts[i].Depth > strongest.Depth) strongest = m_Contacts[i];
                m_ContactBurstCooldown = .8f;
                ContactBurstRequested.Invoke(strongest.Point + strongest.Normal * .015f);
            }
            foreach (var mesh in m_Meshes)
            {
                if (m_ActiveContacts == 0 && !mesh.Moving) continue;
                mesh.Candidates.Clear();
                foreach (var index in mesh.Recovering) mesh.Candidates.Add(index);
                mesh.Recovering.Clear();
                for (var c = 0; c < m_ActiveContacts; c++)
                {
                    var minimum = Cell(m_Contacts[c].Point - Vector3.one * m_InfluenceRadius);
                    var maximum = Cell(m_Contacts[c].Point + Vector3.one * m_InfluenceRadius);
                    for (var x = minimum.x; x <= maximum.x; x++)
                    for (var y = minimum.y; y <= maximum.y; y++)
                    for (var z = minimum.z; z <= maximum.z; z++)
                        if (mesh.Cells.TryGetValue(new Vector3Int(x, y, z), out var indices))
                            foreach (var index in indices) mesh.Candidates.Add(index);
                }
                if (mesh.Candidates.Count == 0) continue;
                var moving = false;
                var inverse = mesh.Filter.transform.worldToLocalMatrix;
                foreach (var i in mesh.Candidates)
                {
                    var target = Vector3.zero;
                    for (var c = 0; c < m_ActiveContacts; c++)
                    {
                        var contact = m_Contacts[c];
                        var falloff = SoftTouchDeformationController.ContactFalloff(
                            Vector3.Distance(mesh.RestWorld[i], contact.Point), m_InfluenceRadius);
                        target -= contact.Normal * (contact.Depth * falloff);
                    }
                    target = Vector3.ClampMagnitude(target, m_MaxIndentation);
                    if (m_BaseAnchorHeight > 0f)
                        target *= Mathf.SmoothStep(0f, 1f, (mesh.RestWorld[i].y - m_BaseY) / m_BaseAnchorHeight);
                    var speed = target.sqrMagnitude > .00000001f ? m_ResponseSpeed : m_RecoverySpeed;
                    var smoothTime = 2f / Mathf.Max(.01f, speed * Mathf.Sqrt(m_Elasticity / 18f));
                    mesh.Offsets[i] = Vector3.SmoothDamp(mesh.Offsets[i], target, ref mesh.Velocities[i],
                        smoothTime, Mathf.Infinity, deltaTime);
                    if (target == Vector3.zero && mesh.Offsets[i].sqrMagnitude < 1e-10f && mesh.Velocities[i].sqrMagnitude < 1e-8f)
                    {
                        mesh.Offsets[i] = Vector3.zero;
                        mesh.Velocities[i] = Vector3.zero;
                    }
                    if (mesh.Offsets[i] != Vector3.zero || target != Vector3.zero)
                    {
                        moving = true;
                        mesh.Recovering.Add(i);
                    }
                    mesh.Vertices[i] = mesh.Original[i] + inverse.MultiplyVector(mesh.Offsets[i]);
                }
                mesh.Moving = moving;
                mesh.Runtime.vertices = mesh.Vertices;
                if (!moving)
                    mesh.Runtime.normals = mesh.OriginalNormals;
                else if (Time.unscaledTime >= mesh.NextNormalUpdate)
                {
                    mesh.Runtime.RecalculateNormals();
                    mesh.NextNormalUpdate = Time.unscaledTime + 1f / m_NormalUpdateRate;
                }
            }
        }

        void OnDisable()
        {
            if (m_HandSubsystem != null) m_HandSubsystem.updatedHands -= OnHandsUpdated;
            m_HoldAudio?.EndHold();
            m_HandSubsystem = null;
            m_ActiveContacts = 0;
            m_ContactBurstCooldown = 0f;
            foreach (var mesh in m_Meshes)
            {
                System.Array.Clear(mesh.Offsets, 0, mesh.Offsets.Length);
                System.Array.Clear(mesh.Velocities, 0, mesh.Velocities.Length);
                mesh.Moving = false;
                mesh.Recovering.Clear();
                if (mesh.Runtime == null) continue;
                mesh.Runtime.vertices = mesh.Original;
                mesh.Runtime.normals = mesh.OriginalNormals;
            }
        }

        void OnDestroy()
        {
            foreach (var mesh in m_Meshes)
            {
                if (mesh.Filter != null) mesh.Filter.sharedMesh = mesh.Source;
                if (Application.isPlaying)
                {
                    if (mesh.Runtime != null) Destroy(mesh.Runtime);
                    if (mesh.Surface != null) Destroy(mesh.Surface.gameObject);
                }
                else
                {
                    if (mesh.Runtime != null) DestroyImmediate(mesh.Runtime);
                    if (mesh.Surface != null) DestroyImmediate(mesh.Surface.gameObject);
                }
            }
            m_Meshes.Clear();
        }

        void OnDrawGizmosSelected()
        {
            if (!m_Debug) return;
            for (var i = 0; i < m_ActiveContacts; i++)
            {
                var contact = m_Contacts[i];
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(contact.Point, m_InfluenceRadius);
                Gizmos.DrawLine(contact.Point, contact.Point - contact.Normal * contact.Depth);
            }
        }
    }
}
