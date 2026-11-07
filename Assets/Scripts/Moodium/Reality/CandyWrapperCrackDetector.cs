using System.Collections.Generic;
using Moodium.Interaction;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

namespace Moodium.Reality
{
    /// <summary>
    /// Detects deliberate hand contact with candy-wrapped AR mesh colliders and displays
    /// pooled, surface-bound cracks. The detector only observes existing XR/mesh systems.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CandyWrapperCrackDetector : MonoBehaviour
    {
        enum LogicalJoint { Palm, ThumbTip, IndexTip, MiddleTip, RingTip, LittleTip }

        struct JointSample
        {
            public bool Valid;
            public bool WasNearSurface;
            public Vector3 PreviousPosition;
            public Vector3 SmoothedVelocity;
            public float PreviousTime;
        }

        struct ContactCandidate
        {
            public int Hand;
            public LogicalJoint Joint;
            public Collider Collider;
            public Vector3 Point;
            public Vector3 Normal;
            public float Strength;
            public float Time;
        }

        [Header("Contact zones (metres)")]
        [SerializeField, Min(.002f)] float m_ContactDistance = .025f;
        [SerializeField, Min(.003f)] float m_ReleaseDistance = .045f;
        [SerializeField, Min(0f)] float m_MinimumFingerTowardVelocity = .045f;
        [SerializeField, Min(0f)] float m_MinimumPalmTowardVelocity = .06f;
        [SerializeField, Range(0f, .95f)] float m_VelocitySmoothing = .35f;
        [SerializeField] LayerMask m_CollisionLayers = ~0;

        [Header("Multi-joint clustering")]
        [SerializeField, Range(.04f, .2f)] float m_ClusterWindow = .1f;
        [SerializeField, Range(2, 5)] int m_MinimumJointCountForLarge = 3;
        [SerializeField, Min(.03f)] float m_SameSurfaceDistance = .12f;
        [SerializeField, Range(-1f, 1f)] float m_SameSurfaceNormalDot = .75f;
        [SerializeField, Min(.05f)] float m_PerHandCooldown = .2f;
        [SerializeField, Min(.01f)] float m_MinimumSpawnDistance = .065f;

        [Header("Crack visuals")]
        [SerializeField] Texture2D[] m_SmallCrackTextures;
        [SerializeField] Texture2D[] m_LargeCrackTextures;
        [SerializeField, Min(.01f)] float m_SmallCrackMinSize = .03f;
        [SerializeField, Min(.01f)] float m_SmallCrackMaxSize = .06f;
        [SerializeField, Min(.01f)] float m_LargeCrackMinSize = .07f;
        [SerializeField, Min(.01f)] float m_LargeCrackMaxSize = .14f;
        [SerializeField, Min(0f)] float m_SurfaceOffset = .002f;
        [SerializeField, Range(.04f, .3f)] float m_SpawnAnimationDuration = .14f;
        [SerializeField, Range(1f, 1.4f)] float m_OvershootScale = 1.16f;
        [SerializeField] float m_Lifetime = 6f;
        [SerializeField, Min(0f)] float m_FadeDuration = .35f;
        [SerializeField, Range(1, 48)] int m_MaxActiveCracks = 24;

        [Header("Debug")]
        [SerializeField] bool m_DebugMode = true;
        [SerializeField] bool m_ShowDebugGizmos = true;
        [SerializeField] string m_LastDecision = "Disabled";
        [SerializeField] string m_LastJoint = "None";
        [SerializeField] Vector3 m_LastContactPoint;
        [SerializeField] Vector3 m_LastSurfaceNormal;
        [SerializeField] Vector3 m_LastJointVelocity;
        [SerializeField] float m_LastContactDistance;
        [SerializeField] float m_LastTowardSurfaceVelocity;
        [SerializeField] int m_LastClusterJointCount;
        [SerializeField] float m_LastImpactStrength;
        [SerializeField] int m_ActiveCrackCount;
        string m_LastLoggedDecision;

        readonly HashSet<Collider> m_SpatialColliders = new();
        readonly Collider[] m_OverlapResults = new Collider[32];
        readonly RaycastHit[] m_RaycastResults = new RaycastHit[16];
        readonly List<ContactCandidate> m_Candidates = new();
        readonly List<CandyWrapperCrackVisual> m_Pool = new();
        readonly JointSample[,] m_Samples = new JointSample[2, 6];
        readonly float[] m_LastHandSpawn = { -100f, -100f };
        readonly Vector3[] m_LastHandPoint = new Vector3[2];
        readonly List<Vector3> m_DebugPoints = new();
        ARMeshManager m_MeshManager;
        CandySpatialMeshTransformationController m_Wrapper;
        XRHandSubsystem m_Hands;
        XROrigin m_XROrigin;
        Material m_SharedMaterial;
        GameObject m_CrackPrefab;
        bool m_InteractionEnabled;
        float m_LastColliderRefresh;

        static readonly XRHandJointID[] TipJointIds =
        {
            XRHandJointID.ThumbTip, XRHandJointID.IndexTip, XRHandJointID.MiddleTip,
            XRHandJointID.RingTip, XRHandJointID.LittleTip
        };

        public int ActiveCrackCount => m_ActiveCrackCount;

        public void Configure(ARMeshManager meshManager, CandySpatialMeshTransformationController wrapper, Texture2D crackTexture,
            Material crackMaterial, GameObject crackPrefab)
        {
            m_MeshManager = meshManager;
            m_Wrapper = wrapper;
            m_CrackPrefab = crackPrefab;
            if (m_SharedMaterial == null && crackMaterial != null)
                m_SharedMaterial = new Material(crackMaterial)
                {
                    name = "Candy Crack Shared Runtime Material",
                    hideFlags = HideFlags.DontSave
                };
            if ((m_SmallCrackTextures == null || m_SmallCrackTextures.Length == 0) && crackTexture != null)
                m_SmallCrackTextures = new[] { crackTexture };
            if ((m_LargeCrackTextures == null || m_LargeCrackTextures.Length == 0) && crackTexture != null)
                m_LargeCrackTextures = new[] { crackTexture };
            RefreshSpatialColliders();
            EnsureSharedMaterial();
        }

        public void SetInteractionEnabled(bool enabled)
        {
            m_InteractionEnabled = enabled;
            m_LastDecision = enabled ? "Waiting for Candy Wrapper" : "Disabled";
            m_Candidates.Clear();
            for (var hand = 0; hand < 2; hand++)
                for (var joint = 0; joint < 6; joint++)
                    m_Samples[hand, joint] = default;
        }

        public void ClearAll()
        {
            foreach (var visual in m_Pool)
                if (visual != null) visual.Despawn();
            m_ActiveCrackCount = 0;
        }

        /// <summary>
        /// Late-world gaze/pinch path. It deliberately bypasses velocity/contact-zone
        /// heuristics, but still requires an active candy wrapper on AR mesh geometry.
        /// </summary>
        public bool TrySpawnFromGazeHit(RaycastHit hit)
        {
            if (!m_InteractionEnabled || m_Wrapper == null || !m_Wrapper.IsInteractionReady ||
                hit.collider == null || !IsSpatialMeshCollider(hit.collider) ||
                !m_Wrapper.IsOverlayActive(hit.collider))
                return false;

            var texture = PickTexture(m_LargeCrackTextures);
            if (texture == null)
            {
                m_LastDecision = "Missing Crack Texture";
                return false;
            }
            var visual = GetVisual();
            var size = Mathf.Lerp(m_LargeCrackMinSize, m_LargeCrackMaxSize, .65f);
            visual.Spawn(hit.collider.transform, hit.point, hit.normal, size, Random.Range(0f, 360f), texture,
                m_SurfaceOffset, m_SpawnAnimationDuration, m_OvershootScale, m_Lifetime, m_FadeDuration);
            m_LastContactPoint = hit.point;
            m_LastSurfaceNormal = hit.normal;
            m_LastImpactStrength = .65f;
            m_LastClusterJointCount = 0;
            m_LastDecision = "Gaze Large Crack";
            UpdateVisualCount();
            if (m_DebugMode)
                Debug.Log($"[Candy Crack] Gaze Large Crack at {hit.point}.");
            return true;
        }

        void Update()
        {
            UpdateVisualCount();
            if (!m_InteractionEnabled)
                return;
            if (m_Wrapper == null || !m_Wrapper.IsInteractionReady)
            {
                m_LastDecision = "Wrapper Not Active";
                return;
            }
            if (Time.unscaledTime - m_LastColliderRefresh > 1f)
                RefreshSpatialColliders();
            if (!TryEnsureHands())
                return;
            m_Hands.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
            CheckHand(0, m_Hands.leftHand);
            CheckHand(1, m_Hands.rightHand);
            FlushExpiredClusters(Time.unscaledTime);
        }

        void LateUpdate()
        {
            // The Inspector has the detailed values; this compact state-transition log is
            // specifically for Vision Pro/Xcode diagnosis without flooding every frame.
            if (!m_DebugMode || m_LastLoggedDecision == m_LastDecision)
                return;
            m_LastLoggedDecision = m_LastDecision;
            Debug.Log($"[Candy Crack] {m_LastDecision}; joint={m_LastJoint}, distance={m_LastContactDistance:F3}, " +
                      $"toward={m_LastTowardSurfaceVelocity:F3}, active={m_ActiveCrackCount}.");
        }

        void OnDestroy()
        {
            foreach (var visual in m_Pool)
                if (visual != null) Destroy(visual.gameObject);
            if (m_SharedMaterial != null) Destroy(m_SharedMaterial);
        }

        bool TryEnsureHands()
        {
            if (m_Hands == null)
                m_Hands = XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRHandSubsystem>();
            if (m_XROrigin == null)
                m_XROrigin = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
            return m_Hands != null && m_Hands.running;
        }

        void CheckHand(int handIndex, XRHand hand)
        {
            if (!hand.isTracked)
                return;
            var origin = m_XROrigin != null ? m_XROrigin.Origin.transform : null;
            for (var i = 0; i < TipJointIds.Length; i++)
            {
                var xrJoint = hand.GetJoint(TipJointIds[i]);
                if (xrJoint.TryGetPose(out var pose))
                    CheckJoint(handIndex, (LogicalJoint)(i + 1), PalmPressGestureController.TransformTrackingPose(pose, origin));
            }
            if (TryGetDerivedPalmPose(hand, origin, out var palmPose))
                CheckJoint(handIndex, LogicalJoint.Palm, palmPose);
        }

        static bool TryGetDerivedPalmPose(XRHand hand, Transform origin, out Pose result)
        {
            result = default;
            var wrist = hand.GetJoint(XRHandJointID.Wrist);
            var index = hand.GetJoint(XRHandJointID.IndexMetacarpal);
            var middle = hand.GetJoint(XRHandJointID.MiddleMetacarpal);
            var little = hand.GetJoint(XRHandJointID.LittleMetacarpal);
            if (!wrist.TryGetPose(out var wristPose) || !index.TryGetPose(out var indexPose) ||
                !middle.TryGetPose(out var middlePose) || !little.TryGetPose(out var littlePose))
                return false;
            wristPose = PalmPressGestureController.TransformTrackingPose(wristPose, origin);
            indexPose = PalmPressGestureController.TransformTrackingPose(indexPose, origin);
            middlePose = PalmPressGestureController.TransformTrackingPose(middlePose, origin);
            littlePose = PalmPressGestureController.TransformTrackingPose(littlePose, origin);
            if (!PalmPressGestureController.TryBuildPalmBox(wristPose, indexPose, middlePose, littlePose,
                    out var center, out var rotation))
                return false;
            result = new Pose(center, rotation);
            return true;
        }

        void CheckJoint(int handIndex, LogicalJoint logicalJoint, Pose pose)
        {
            var jointIndex = (int)logicalJoint;
            var sample = m_Samples[handIndex, jointIndex];
            var now = Time.unscaledTime;
            var velocity = Vector3.zero;
            if (sample.Valid && now > sample.PreviousTime)
            {
                var raw = (pose.position - sample.PreviousPosition) / Mathf.Max(.001f, now - sample.PreviousTime);
                velocity = Vector3.Lerp(sample.SmoothedVelocity, raw, 1f - m_VelocitySmoothing);
                sample.SmoothedVelocity = velocity;
            }
            sample.Valid = true;
            sample.PreviousPosition = pose.position;
            sample.PreviousTime = now;

            if (!TryFindSurface(pose.position, velocity, out var collider, out var point, out var normal, out var distance))
            {
                sample.WasNearSurface = false;
                m_Samples[handIndex, jointIndex] = sample;
                return;
            }
            m_LastJoint = logicalJoint.ToString();
            m_LastContactPoint = point;
            m_LastSurfaceNormal = normal;
            m_LastJointVelocity = velocity;
            m_LastContactDistance = distance;
            if (!m_Wrapper.IsOverlayActive(collider))
            {
                m_LastDecision = "Wrapper Not Active";
                m_Samples[handIndex, jointIndex] = sample;
                return;
            }
            if (distance > m_ReleaseDistance)
                sample.WasNearSurface = false;
            if (distance > m_ContactDistance)
            {
                m_LastDecision = "Too Far";
                m_Samples[handIndex, jointIndex] = sample;
                return;
            }
            var towardVelocity = CandyWrapperCrackMath.TowardSurfaceVelocity(velocity, normal);
            m_LastTowardSurfaceVelocity = towardVelocity;
            var threshold = logicalJoint == LogicalJoint.Palm ? m_MinimumPalmTowardVelocity : m_MinimumFingerTowardVelocity;
            if (towardVelocity < threshold)
            {
                m_LastDecision = "Not Moving Toward Surface";
                m_Samples[handIndex, jointIndex] = sample;
                return;
            }
            if (!sample.WasNearSurface)
            {
                sample.WasNearSurface = true;
                m_Candidates.Add(new ContactCandidate
                {
                    Hand = handIndex, Joint = logicalJoint, Collider = collider, Point = point, Normal = normal,
                    Strength = CandyWrapperCrackMath.Impact01(towardVelocity, threshold, .45f), Time = now
                });
                if (m_DebugMode)
                    Debug.Log($"[Candy Crack] Candidate {logicalJoint}: distance={distance:F3}, toward={towardVelocity:F3}");
            }
            m_Samples[handIndex, jointIndex] = sample;
        }

        bool TryFindSurface(Vector3 position, Vector3 velocity, out Collider found, out Vector3 point, out Vector3 normal, out float distance)
        {
            found = null; point = default; normal = Vector3.up; distance = float.MaxValue;
            // A short cast along the joint trajectory is the authoritative path: it gives
            // a real mesh contact point and triangle normal instead of guessing from bounds.
            if (velocity.sqrMagnitude > .000001f)
            {
                var direction = velocity.normalized;
                var start = position - direction * m_ReleaseDistance;
                var hitCount = Physics.RaycastNonAlloc(start, direction, m_RaycastResults,
                    m_ReleaseDistance * 2f, m_CollisionLayers, QueryTriggerInteraction.Ignore);
                for (var i = 0; i < hitCount; i++)
                {
                    var hit = m_RaycastResults[i];
                    if (hit.collider == null || !IsSpatialMeshCollider(hit.collider)) continue;
                    var hitDistance = Vector3.Distance(position, hit.point);
                    if (hitDistance > m_ReleaseDistance || hitDistance > distance) continue;
                    found = hit.collider;
                    point = hit.point;
                    normal = hit.normal;
                    distance = hitDistance;
                }
                if (found != null)
                {
                    if (Vector3.Dot(normal, position - point) < 0f) normal = -normal;
                    return true;
                }
            }
            var count = Physics.OverlapSphereNonAlloc(position, m_ReleaseDistance, m_OverlapResults,
                m_CollisionLayers, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                var candidate = m_OverlapResults[i];
                if (candidate == null || !IsSpatialMeshCollider(candidate)) continue;
                var closest = candidate.ClosestPoint(position);
                var candidateDistance = Vector3.Distance(position, closest);
                if (candidateDistance > distance) continue;
                found = candidate; point = closest; distance = candidateDistance;
            }
            if (found == null)
                return false;

            // ClosestPoint has no triangle normal. The previous code substituted the
            // vector from mesh to hand here, which tilted cracks when a hand approached
            // a wall diagonally. Probe the selected collider itself so every spawned
            // crack uses a real RaycastHit.normal from the spatial mesh triangle.
            var handSide = position - point;
            if (handSide.sqrMagnitude < .000001f)
            {
                m_LastDecision = "Surface Normal Unavailable";
                return false;
            }
            var probe = CandyWrapperCrackMath.BuildSurfaceNormalProbe(point, position, .004f);
            if (!found.Raycast(probe, out var authoritativeHit, m_ReleaseDistance * 3f))
            {
                m_LastDecision = "Surface Normal Unavailable";
                return false;
            }
            point = authoritativeHit.point;
            normal = authoritativeHit.normal;
            if (Vector3.Dot(normal, position - point) < 0f)
                normal = -normal;
            distance = Vector3.Distance(position, point);
            return true;
        }

        void FlushExpiredClusters(float now)
        {
            while (m_Candidates.Count > 0 && now - m_Candidates[0].Time >= m_ClusterWindow)
            {
                var seed = m_Candidates[0];
                m_Candidates.RemoveAt(0);
                var cluster = new List<ContactCandidate> { seed };
                for (var i = m_Candidates.Count - 1; i >= 0; i--)
                {
                    var other = m_Candidates[i];
                    if (other.Collider == seed.Collider && other.Hand == seed.Hand &&
                        Vector3.Distance(other.Point, seed.Point) <= m_SameSurfaceDistance &&
                        Vector3.Dot(other.Normal, seed.Normal) >= m_SameSurfaceNormalDot)
                    {
                        cluster.Add(other);
                        m_Candidates.RemoveAt(i);
                    }
                }
                SpawnCluster(cluster, now);
            }
        }

        void SpawnCluster(List<ContactCandidate> cluster, float now)
        {
            if (cluster.Count == 0) return;
            var hand = cluster[0].Hand;
            var center = Vector3.zero; var normal = Vector3.zero; var strength = 0f; var hasPalm = false;
            foreach (var contact in cluster)
            {
                center += contact.Point;
                normal += contact.Normal;
                strength += contact.Strength;
                hasPalm |= contact.Joint == LogicalJoint.Palm;
            }
            center /= cluster.Count;
            normal.Normalize();
            strength = Mathf.Clamp01(strength / cluster.Count);
            m_LastClusterJointCount = cluster.Count;
            m_LastImpactStrength = strength;
            if (now - m_LastHandSpawn[hand] < m_PerHandCooldown)
            {
                m_LastDecision = "Cooldown";
                return;
            }
            if (CandyWrapperCrackMath.IsDuplicateSpawn(center, m_LastHandPoint[hand], now, m_LastHandSpawn[hand],
                    m_MinimumSpawnDistance, m_PerHandCooldown))
            {
                m_LastDecision = "Duplicate Position";
                return;
            }
            var large = CandyWrapperCrackMath.IsLargeCluster(cluster.Count, hasPalm, m_MinimumJointCountForLarge);
            var size = large
                ? Mathf.Lerp(m_LargeCrackMinSize, m_LargeCrackMaxSize, strength)
                : Mathf.Lerp(m_SmallCrackMinSize, m_SmallCrackMaxSize, strength);
            var texture = PickTexture(large ? m_LargeCrackTextures : m_SmallCrackTextures);
            if (texture == null)
            {
                m_LastDecision = "Missing Crack Texture";
                return;
            }
            var visual = GetVisual();
            visual.Spawn(cluster[0].Collider.transform, center, normal, size, Random.Range(0f, 360f), texture,
                m_SurfaceOffset, m_SpawnAnimationDuration, m_OvershootScale, m_Lifetime, m_FadeDuration);
            m_LastHandSpawn[hand] = now;
            m_LastHandPoint[hand] = center;
            m_LastDecision = large ? "Large Crack" : "Small Crack";
            m_DebugPoints.Add(center);
            if (m_DebugMode)
                Debug.Log($"[Candy Crack] {m_LastDecision}: joints={cluster.Count}, strength={strength:F2}, active={m_ActiveCrackCount}");
        }

        CandyWrapperCrackVisual GetVisual()
        {
            foreach (var availableVisual in m_Pool)
                if (availableVisual != null && !availableVisual.IsActive) return availableVisual;
            if (m_Pool.Count >= m_MaxActiveCracks)
            {
                CandyWrapperCrackVisual oldest = null;
                foreach (var oldestCandidate in m_Pool)
                    if (oldestCandidate != null && (oldest == null || oldestCandidate.SpawnedAt < oldest.SpawnedAt)) oldest = oldestCandidate;
                oldest?.Despawn();
                return oldest;
            }
            var go = m_CrackPrefab != null
                ? Instantiate(m_CrackPrefab)
                : GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Candy Crack (Pooled)";
            var collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            var createdVisual = go.GetComponent<CandyWrapperCrackVisual>();
            if (createdVisual == null) createdVisual = go.AddComponent<CandyWrapperCrackVisual>();
            createdVisual.Configure(m_SharedMaterial);
            go.SetActive(false);
            m_Pool.Add(createdVisual);
            return createdVisual;
        }

        Texture2D PickTexture(Texture2D[] textures)
        {
            if (textures == null || textures.Length == 0) return null;
            return textures[Random.Range(0, textures.Length)];
        }

        void EnsureSharedMaterial()
        {
            if (m_SharedMaterial != null) return;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) return;
            m_SharedMaterial = new Material(shader) { name = "Candy Crack Shared Runtime Material", hideFlags = HideFlags.DontSave };
            m_SharedMaterial.SetOverrideTag("RenderType", "Transparent");
            m_SharedMaterial.renderQueue = 3100;
            if (m_SharedMaterial.HasProperty("_Surface")) m_SharedMaterial.SetFloat("_Surface", 1f);
            if (m_SharedMaterial.HasProperty("_ZWrite")) m_SharedMaterial.SetFloat("_ZWrite", 0f);
        }

        void RefreshSpatialColliders()
        {
            m_LastColliderRefresh = Time.unscaledTime;
            m_SpatialColliders.Clear();
            if (m_MeshManager != null)
                foreach (var filter in m_MeshManager.GetComponentsInChildren<MeshFilter>(true))
                    foreach (var collider in filter.GetComponentsInChildren<Collider>(true))
                        m_SpatialColliders.Add(collider);
            if (m_SpatialColliders.Count == 0)
                foreach (var collider in FindObjectsByType<MeshCollider>(
                             FindObjectsInactive.Include, FindObjectsSortMode.None))
                    m_SpatialColliders.Add(collider);
        }

        bool IsSpatialMeshCollider(Collider collider)
        {
            if (collider == null)
                return false;
            if (m_SpatialColliders.Contains(collider))
                return true;
            return m_MeshManager != null &&
                   collider.GetComponentInParent<ARMeshManager>() == m_MeshManager;
        }

        void UpdateVisualCount()
        {
            m_ActiveCrackCount = 0;
            foreach (var visual in m_Pool)
                if (visual != null && visual.IsActive) m_ActiveCrackCount++;
        }

        void OnDrawGizmos()
        {
            if (!m_ShowDebugGizmos) return;
            Gizmos.color = Color.white;
            foreach (var point in m_DebugPoints)
                Gizmos.DrawWireSphere(point, .018f);
            if (m_LastSurfaceNormal.sqrMagnitude > .001f)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawSphere(m_LastContactPoint, .01f);
                Gizmos.DrawLine(m_LastContactPoint, m_LastContactPoint + m_LastSurfaceNormal * .12f);
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(m_LastContactPoint, m_LastContactPoint + m_LastJointVelocity * .08f);
            }
        }
    }
}
