#if MOODIUM_IVY_STUDIO
using System.Collections.Generic;
using Artngame.TreeGEN.ProceduralIvy;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Moodium.NatureWorld
{
    /// <summary>
    /// Bridges Nature World interactions to Ivy Studio's public runtime API. Ivy is only
    /// planted on colliders owned by the scanned spatial mesh, never on tracked props.
    /// After 100% (Explore stage), touching a wall or table with a fingertip, or looking at it and pinching, grows a thin
    /// vine from that point along the room mesh, wrapping over edges, with green ivy or red maple leaves unfolding as it
    /// grows (NatureVineGrowth.cs).
    /// </summary>
    /// <remarks>
    /// Look + pinch (2026-11-03): visionOS only sends a gaze-pinch to the app when the user looks at a collider PolySpatial
    /// has synced, and PolySpatial only syncs Default-layer colliders (PolySpatialSettings: Collider Objects Layer Mask = 1).
    /// The scanned room mesh is on layer 4, so visionOS never saw it and a pinch at a wall reached nothing. While vines can
    /// be planted, every room MeshCollider gets a Default-layer twin ("Nature Ivy Gaze Target"); they are removed again as
    /// soon as planting stops (inside a world, paused, leaving), so the room does not take taps from virtual objects then.
    /// Every step logs a "[Nature Vine]" line, and a small coloured dot marks where a touch or pinch landed.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class NatureIvySpatialGrowthController : MonoBehaviour
    {
        bool m_Suspended;
        bool m_EnvironmentUnlocked;
        readonly HashSet<Mesh> m_OwnedMeshes = new();
        public void SetProgress(float progress)
        {
            if (progress < 1f || m_EnvironmentUnlocked) return;
            m_EnvironmentUnlocked = true;
            Debug.Log("[Nature Vine] Energy full: vines unlocked (" + PlantableState() + ")");
        }
        public bool EnvironmentUnlocked => m_EnvironmentUnlocked;
        public bool CanPlantEnvironment => m_Running && !m_Suspended && m_EnvironmentUnlocked &&
            GetComponent<Moodium.Flow.WorldJourneyDirector>()?.Stage == Moodium.Flow.WorldJourneyStage.Explore;

        public void SetSuspended(bool value)
        {
            if (m_Suspended == value) return;
            m_Suspended = value;
            if (value) { ClearGazeTargets(); EndGuideSpot(); }
        }
        public void RegisterRoomVisibility(Moodium.Flow.RoomContentVisibility visibility) { if (m_Generator != null) visibility.Register(m_Generator.gameObject); }

        readonly HashSet<Collider> m_SpatialColliders = new();
        Camera m_Camera;
        ARMeshManager m_MeshManager;
        IvyGeneratorTREANT m_Generator;
        [Header("Nature Vine Debug")]
        [SerializeField] bool m_DebugMode;
        [SerializeField] bool m_ForceTestVineGrowth;
        [Header("Surface Search")]
        [SerializeField, Range(0f, 1f)] float m_MinUpNormalDot = .65f;
        [SerializeField] float m_MaxSurfaceSearchDistance = .8f;
        [SerializeField] float m_SurfaceSampleRadius = .12f;
        [SerializeField] int m_SurfaceSampleCount = 8;
        [SerializeField] float m_SurfaceOffset = .004f;
        [Header("PolySpatial Leaf Visuals")]
        [Tooltip("URP Lit, alpha clip, double-sided. Copied per leaf texture (Resources/MoodiumNature/VineLeaves).")]
        [SerializeField] Material m_PolySpatialLeafMaterial;
        [Header("Touch Vines")]
        [SerializeField] bool m_TouchPlanting = true;
        [Header("Look + Pinch Vines")]
        [Tooltip("Give the room mesh Default-layer twins while vines can be planted, so visionOS sends gaze-pinches on it.")]
        [SerializeField] bool m_GazePlanting = true;
        [Header("Diagnostics")]
        [Tooltip("Debug only: a small dot where a touch or pinch landed (green = vine grows, yellow = surface found but no " +
                 "branch, red = refused). Off for users (2026-11-03): resting a hand on the table showed a stream of red dots. " +
                 "The [Nature Vine] logs stay on.")]
        [SerializeField] bool m_DiagnosticMarkers;
        [SerializeField] float m_VineGrowSeconds = 2.6f;
        [Header("Sound")]
        [Tooltip("A light rustle + wooden notes where a vine starts (Resources/MoodiumGrowth/vine_grow_1..3).")]
        [SerializeField] bool m_GrowSound = true;
        [SerializeField, Range(0f, 1f)] float m_GrowSoundVolume = NatureVineSound.Volume;
        [Header("Guide")]
        [Tooltip("While Moodi gives a vine tip, a soft green spot shows a reachable wall or table ahead.")]
        [SerializeField] bool m_GuideSpot = true;
        [Header("Completion Burst")]
        [SerializeField, Range(4, 8)] int m_CompletionBranchCount = 6;
        [SerializeField] float m_CompletionRadius = 1f;
        [SerializeField] float m_StartPointScatterRadius = .12f;
        [SerializeField, Range(0f, 45f)] float m_DirectionRandomness = 15f;
        [SerializeField] float m_CompletionGrowthDuration = 1.5f;
        [SerializeField] float m_ForceTestLength = .75f;
        [SerializeField, TextArea] string m_LastDebugStatus;

        int m_MaxStrokes = 3;
        int m_GrownStrokeCount;
        float m_LastColliderRefresh;
        bool m_Running;
        bool m_CompletionBurstTriggered;
        Vector3 m_LastNatureSoftOrigin;

        public int GrownStrokeCount => m_GrownStrokeCount;
        /// <summary>Vines planted by a fingertip touch this session.</summary>
        public int TouchVineCount => m_TouchVines;
        /// <summary>Vines the user planted this session (touch or look + pinch); the completion ring does not count.</summary>
        public int UserVineCount => m_UserVines;
        public SpriteRenderer GuideSpot => m_Spot;
        public int GrowingCount => m_Growing;
        /// <summary>Room meshes that currently have a Default-layer gaze twin.</summary>
        public int GazeTargetCount => m_GazeTargets.Count;
        public bool DiagnosticMarkers { get => m_DiagnosticMarkers; set => m_DiagnosticMarkers = value; }

        // ---------- look + pinch on the room ----------
        public const string GazeTargetName = "Nature Ivy Gaze Target";
        /// <summary>The only layer PolySpatial syncs to visionOS for input (Collider Objects Layer Mask = 1).</summary>
        public const int GazeTargetLayer = 0;
        public const float GazeSyncSeconds = 1f;
        readonly Dictionary<MeshCollider, MeshCollider> m_GazeTargets = new();
        readonly HashSet<Collider> m_GazeTargetSet = new();
        readonly HashSet<MeshCollider> m_RecookTargets = new();
        readonly List<MeshCollider> m_StaleTargets = new();
        float m_NextGazeSync;
        bool m_RoomMeshesChanged;

        // ---------- diagnostics ----------
        public enum MarkerKind { Planted, NoBranch, Refused }
        public const float MarkerSize = .016f, MarkerSeconds = 2f, NearTouchLogSeconds = 3f;
        Material[] m_MarkerMaterials;
        bool? m_LastPlantable;
        readonly float[] m_HandNearLog = { -100f, -100f };

        // Thin vines like the reference: a 9 mm stem tapering toward the tip, 4.5 cm steps, up to ~1.3 m per branch.
        public const int VineBranches = 1, VinePoints = 30, VineDivisions = 8, VineUnattachedSteps = 2;
        public const float VineStep = .045f, VineRadius = .0045f, VineTipNarrow = .0032f, VineZigZag = .8f;

        readonly BubblePlayHands m_Hands = new();
        readonly bool[] m_HandTouching = new bool[2];
        readonly float[] m_HandLastPlant = { -100f, -100f };
        readonly Vector3?[] m_HandLastPoint = new Vector3?[2];
        readonly Collider[] m_Overlap = new Collider[32];
        readonly List<Collider> m_Nearby = new();
        List<Collider> m_ProbeColliders;
        int m_Growing, m_TouchVines, m_UserVines;
        // ---------- sound + guide spot ----------
        readonly AudioClip[] m_GrowClips = new AudioClip[NatureVineSound.Clips.Length];
        float m_LastGrowSound = -100f;
        int m_LastGrowClip = -1;
        SpriteRenderer m_Spot;
        float m_SpotStart = -1f, m_SpotCancelAt = -1f;
        readonly System.Random m_Random = new();
        readonly List<Material> m_RuntimeMaterials = new();
        Material m_BaseBranchMaterial, m_IvyStem, m_MapleStem;
        Material[] m_IvyLeaves, m_MapleLeaves;
        // One quiet line of text, the first time vines can be planted (and once more if none has been planted after 45 s).
        public const float HintFirstDelay = 3f, HintRepeat = 45f, HintIn = .5f, HintHold = 5f, HintOut = .8f, HintCancel = .3f, HintAlphaMax = .9f;
        public const int HintMaxShows = 2;
        SpriteRenderer m_Hint;
        float m_PlantableSince = -1f, m_HintStart = -1f, m_HintCancelAt = -1f, m_HintShownAt = -100f;
        int m_HintCount;
        public SpriteRenderer Hint => m_Hint;
        public string LastDebugStatus => m_LastDebugStatus;

        // The authored Ivy Studio URP prefab uses 3D branch geometry. Its flat-mesh
        // path and custom growth shader both render incorrectly through PolySpatial.
        public static bool ShouldForceFlatIvyGeometry() => false;
        public static bool ShouldUseRuntimeGrowShader() => false;
        public static bool ShouldUseIvyStudioAutoLeaves() => true;

        public void SetNatureSoftOrigin(Vector3 origin) => m_LastNatureSoftOrigin = origin;
        public void SetDebugMode(bool enabled) => m_DebugMode = enabled;

        public void Configure(IvyGeneratorTREANT generator, int maxStrokes)
        {
            m_Generator = generator;
            if (m_Generator != null)
            {
                m_Generator.surfaceRaycast = RaycastRoom;
                m_Generator.requireSurfaceAttachment = true;
                m_Generator.maxUnattachedSteps = VineUnattachedSteps;
                m_Generator.disableRuntimeBatching = true;
                m_BaseBranchMaterial = m_Generator.branchMaterial;
            }
            m_MaxStrokes = Mathf.Max(1, maxStrokes);
        }

        public void Configure(
            Camera camera,
            ARMeshManager meshManager,
            GameObject ivyGeneratorPrefab,
            Material growMaterial,
            int maxStrokes)
        {
            StopAndClear();
            m_Camera = camera;
            m_MeshManager = meshManager;
            if (m_MeshManager != null) m_MeshManager.meshesChanged += OnRoomMeshesChanged;
            m_MaxStrokes = Mathf.Max(1, maxStrokes);
            if (ivyGeneratorPrefab == null)
            {
                Debug.LogError("[Nature Vine AUTO] Configure failed: ivy generator prefab is missing");
                m_Generator = null;
                return;
            }

            var instance = Instantiate(ivyGeneratorPrefab, transform);
            instance.name = "Nature Runtime Ivy Generator";
            m_Generator = instance.GetComponent<IvyGeneratorTREANT>();
            if (m_Generator == null)
            {
                Debug.LogError("[Nature Vine AUTO] Configure failed: prefab has no IvyGeneratorTREANT on its root");
                Destroy(instance);
                return;
            }

            // Keep the procedural mesh deliberately light enough for a spatial session.
            m_Generator.player = camera != null ? camera.transform : null;
            m_Generator.runTimePlanting = false;
            m_Generator.addColliderPerIvy = false;
            m_Generator.globalBatchEditorIvy = false;
            m_Generator.autoLOD = false;
            m_Generator.limitPlantingArea = false;
            m_Generator.use3dbranch = !ShouldForceFlatIvyGeometry();
            m_Generator.singleMeshBranch = false;
            m_Generator.branchCount = VineBranches;
            m_Generator.maxBranchPositions = VinePoints;
            m_Generator.branchPartLength = VineStep;
            m_Generator.branch3dSize = VineRadius;
            m_Generator.narrowFactor = VineTipNarrow;   // must stay below the radius or the tube turns inside out
            m_Generator.branch3dDivisions = VineDivisions;   // a multiple of 4: NatureVineRings reads the path back from it
            m_Generator.zigZagPower = VineZigZag;
            // Ivy Studio's runtime batcher hid every branch (see IvyGeneratorTREANT.disableRuntimeBatching).
            m_Generator.disableRuntimeBatching = true;
            m_Generator.maxUnattachedSteps = VineUnattachedSteps;
            m_Generator.ivyTrailWidth = 0.014f;
            m_Generator.addLeaves = ShouldUseIvyStudioAutoLeaves();
            m_Generator.flowerChance = 0f; // Leaf-only vines, no daisies.
            m_Generator.flowerDivide = 24; // Plugin probability: 9 valid outcomes out of 24, rather than 6/9.
            m_Generator.LeafScale = .55f;
            m_Generator.leafScaleMinMax = new Vector2(.8f, 1.15f);
            var singleLeaf = Resources.Load<GameObject>("MoodiumNature/NativeVine/SingleIvy");
            if (singleLeaf != null) m_Generator.leavesPrefab = singleLeaf;
            var useRuntimeGrowShader = ShouldUseRuntimeGrowShader() && growMaterial != null;
            m_Generator.growIvy = useRuntimeGrowShader;
            m_Generator.growFlowers = false;
            m_Generator.growIvySpeed = 0.65f;
            m_Generator.growIvyMaterial = useRuntimeGrowShader ? growMaterial : null;
            m_Generator.surfaceOffset = m_SurfaceOffset;
            m_Generator.surfaceRaycast = RaycastRoom;
            m_Generator.requireSurfaceAttachment = true;
            m_BaseBranchMaterial = m_Generator.branchMaterial;
            if (m_PolySpatialLeafMaterial == null) m_PolySpatialLeafMaterial = Resources.Load<Material>("MoodiumNature/NatureIvyLeaf");
            RefreshSpatialColliders();
        }

        public void StartGrowth()
        {
            m_AutoMeshSuccesses = m_AutoMeshAttempts = 0; m_AutoMeshNext = 0; m_AutoMeshPoints.Clear();
            m_EnvironmentUnlocked = false;
            m_Suspended = false;
            m_Running = m_Generator != null;
            Debug.Log($"[Nature Vine AUTO] StartGrowth: generator={m_Generator != null}, meshManager={m_MeshManager != null}, active={gameObject.activeInHierarchy}");
            m_GrownStrokeCount = 0;
            m_CompletionBurstTriggered = false;
            ResetTouch();
            m_TouchVines = 0; m_UserVines = 0; m_HintCount = 0; m_PlantableSince = -1f; m_HintShownAt = -100f;
            EndGuideSpot();
            RefreshSpatialColliders();
        }

        public bool TryGrowNearInteraction(Vector3 interactionPosition)
        {
            m_LastNatureSoftOrigin = interactionPosition;
            if (!m_Running || m_Generator == null)
                return false;

            if (Time.time - m_LastColliderRefresh > 1f)
                RefreshSpatialColliders();
            Trace("Pinch Detected → Nature Interaction Accepted → Surface Search Started", interactionPosition);
            var resolver = CreateSurfaceResolver();
            if (resolver.TryFindSurfaceBelow(interactionPosition + Vector3.up * .15f, out var hit))
            {
                TraceSurface("Surface Found", hit);
                return RequestIvy(hit, true, "Pinch Vine");
            }
            Trace("Surface Failed: no upward AR Spatial Mesh below NatureSoft.", interactionPosition);
            return false;
        }

        /// <summary>
        /// Called by the shared spatial-pointer path when a pinch begins on scanned room geometry.
        /// The pointer's target collider is preserved so this does not have to guess a different
        /// surface from the camera-to-pointer ray.
        /// </summary>
        public bool TryGrowFromSpatialPointer(Collider targetCollider, Vector3 interactionPosition)
        {
            if (!CanPlantEnvironment || m_Camera == null || !IsSpatialMeshCollider(targetCollider)) return false;
            // interactionPosition is on the target, not the tracked pinch fingers.
            return TryGrowFromGazeRay(new Ray(m_Camera.transform.position,
                interactionPosition - m_Camera.transform.position));
        }

        public bool TryGrowFromGazeRay(Ray ray)
        {
            if (!CanPlantEnvironment) return false;
            RefreshSpatialColliders();
            if (!RaycastRoom(ray, 8f, out var hit))
            {
                Trace($"Gaze pinch → the gaze ray hit no room mesh within 8 m ({m_SpatialColliders.Count} room meshes)", ray.origin);
                return false;
            }
            return TryGrowAtSpatialHit(hit);
        }

        /// <summary>One line per gaze-pinch on the room, from MoodiumSpatialInteractionController.</summary>
        public void NotePinch(GameObject target, bool hasRay, bool plantable, bool grown)
        {
            var name = target != null ? $"{target.name} (layer {target.layer})" : "<no target>";
            Debug.Log($"[Nature Vine] Gaze pinch on {name}: ray={(hasRay ? "system" : "derived")}, " +
                      (plantable ? (grown ? "vine requested" : "no vine (see the lines above)") : "not plantable (" + PlantableState() + ")"));
        }

        // While a vine is being generated only the room chunks within its reach are probed (a stroke is ~150 probes).
        bool RaycastRoom(Ray ray, float distance, out RaycastHit hit) => m_ProbeColliders != null
            ? NatureIvySurfaceResolver.TryRaycast(m_ProbeColliders, ray, distance, out hit)
            : NatureIvySurfaceResolver.TryRaycast(m_SpatialColliders, ray, distance, out hit);

        /// <summary>True only for colliders supplied by the room mesh, never tracked bag visuals.</summary>
        public bool IsSpatialMeshCollider(Collider collider)
        {
            if (collider == null)
                return false;
            if (Time.time - m_LastColliderRefresh > 1f)
                RefreshSpatialColliders();
            return IsTrackedSpatialMeshCollider(collider);
        }

        public bool TryGrowAtSpatialHit(RaycastHit hit)
        {
            if (!CanPlantEnvironment || m_Generator == null ||
                !IsSpatialMeshCollider(hit.collider))
            {
                Trace($"Environment Vine refused: plantable={CanPlantEnvironment}, {m_GrownStrokeCount}/{m_MaxStrokes} planted, room mesh={IsSpatialMeshCollider(hit.collider)}", hit.point);
                Mark(hit.point, MarkerKind.Refused);
                return false;
            }
            if (m_Growing >= NatureVineTouch.MaxGrowing)
            {
                Trace($"Environment Vine refused: {m_Growing} vines still growing", hit.point);
                Mark(hit.point, MarkerKind.Refused);
                return false;
            }
            if (!RequestIvy(hit, true, "Environment Vine")) return false;
            m_UserVines++;
            return true;
        }

        /// <summary>Debug-only entry: use the latest NatureSoft position, without pinch or progress gating.</summary>
        [ContextMenu("Force Test Vine Growth")]
        public void ForceTestVineGrowth()
        {
            TryForceTestGrowth(m_LastNatureSoftOrigin == Vector3.zero ? transform.position : m_LastNatureSoftOrigin);
        }

        public bool TryForceTestGrowth(Vector3 natureSoftOrigin)
        {
            if (!m_Running || m_Generator == null)
            {
                Trace("Force Test failed: generator is not running.", natureSoftOrigin);
                return false;
            }
            RefreshSpatialColliders();
            Trace("Force Test → Surface Search Started", natureSoftOrigin);
            if (!CreateSurfaceResolver().TryFindSurfaceBelow(natureSoftOrigin + Vector3.up * .15f, out var hit))
            {
                Trace("Force Test → Surface Failed", natureSoftOrigin);
                return false;
            }
            TraceSurface("Force Test → Surface Found", hit);
            return RequestIvy(hit, false, "Force Test", m_ForceTestLength);
        }

        public bool TryStartCompletionBurst(Vector3 natureSoftOrigin)
        {
            if (!m_Running || m_Generator == null || m_CompletionBurstTriggered)
                return false;
            // Completion is a world-state transition, not a retry loop: consume the one
            // chance even when the current spatial mesh has no usable tabletop yet.
            m_CompletionBurstTriggered = true;
            RefreshSpatialColliders();
            if (!CreateSurfaceResolver().TryFindSurfaceBelow(natureSoftOrigin + Vector3.up * .15f, out var primaryHit))
            {
                Trace("Completion Burst → Surface Failed", natureSoftOrigin);
                return false;
            }
            TraceSurface($"Completion Burst Triggered: branches={m_CompletionBranchCount}", primaryHit);
            StartCoroutine(GrowCompletionBurst(primaryHit));
            return true;
        }

        public void StopAndClear()
        {
            StopAllCoroutines();
            if (m_MeshManager != null) m_MeshManager.meshesChanged -= OnRoomMeshesChanged;
            ClearGazeTargets();
            m_LastPlantable = null;
            m_EnvironmentUnlocked = false;
            m_Running = false;
            foreach (var mesh in m_OwnedMeshes)
                if (mesh != null) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }
            m_OwnedMeshes.Clear();
            m_RetainedVines.Clear();
            m_GrownStrokeCount = 0;
            m_CompletionBurstTriggered = false;
            m_SpatialColliders.Clear();
            m_Growing = 0;
            m_UserVines = 0;
            ResetTouch();
            EndHint();
            EndGuideSpot();
            if (m_Generator != null)
            {
                m_Generator.surfaceRaycast = null;
                if (Application.isPlaying) Destroy(m_Generator.gameObject);
                else DestroyImmediate(m_Generator.gameObject);
            }
            m_Generator = null;
            foreach (var material in m_RuntimeMaterials)
                if (material != null) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
            m_RuntimeMaterials.Clear();
            m_IvyLeaves = m_MapleLeaves = null; m_IvyStem = m_MapleStem = null; m_MarkerMaterials = null;
        }

        void RefreshSpatialColliders()
        {
            m_LastColliderRefresh = Time.time;
            m_SpatialColliders.Clear();
            if (m_MeshManager != null)
            {
                // ARFoundation parents generated meshes under XROrigin.TrackablesParent,
                // not necessarily under the ARMeshManager component's transform.
                foreach (var filter in m_MeshManager.meshes)
                    if (filter != null)
                        foreach (var collider in filter.GetComponentsInChildren<MeshCollider>(true))
                            if (!m_GazeTargetSet.Contains(collider) && collider.name != GazeTargetName)
                                m_SpatialColliders.Add(collider);
                // Keep explicitly authored child colliders supported (including test fixtures).
                foreach (var collider in m_MeshManager.GetComponentsInChildren<MeshCollider>(true))
                    if (!m_GazeTargetSet.Contains(collider) && collider.name != GazeTargetName)
                        m_SpatialColliders.Add(collider);
            }

        }

        bool IsTrackedSpatialMeshCollider(Collider collider)
        {
            if (collider == null)
                return false;
            if (m_SpatialColliders.Contains(collider) || m_GazeTargetSet.Contains(collider))
                return true;
            return m_MeshManager != null &&
                   collider.GetComponentInParent<ARMeshManager>() == m_MeshManager;
        }

        // Temporary device diagnostic: bypass progress and gestures, but keep room suspension.
        [SerializeField] bool m_AutoMeshDiagnostic = false;
        int m_AutoMeshSuccesses, m_AutoMeshAttempts;
        float m_AutoMeshNext;
        readonly List<Vector3> m_AutoMeshPoints = new();

        void AutoMeshDiagnostic()
        {
            if (!m_AutoMeshDiagnostic || Time.time < m_AutoMeshNext) return;
            m_AutoMeshNext = Time.time + 4f;
            Debug.Log($"[Nature Vine AUTO] status running={m_Running} suspended={m_Suspended} generator={m_Generator != null} meshManager={m_MeshManager != null} meshManagerEnabled={m_MeshManager != null && m_MeshManager.isActiveAndEnabled} growing={m_Growing} generated={m_AutoMeshSuccesses}/3 attempts={m_AutoMeshAttempts}/9");
            if (!m_Running || m_Suspended || m_Generator == null || m_AutoMeshSuccesses >= 3 || m_AutoMeshAttempts >= 9) return;
            if (m_Growing > 0) return;
            RefreshSpatialColliders();
            var eye = m_Camera != null ? m_Camera.transform.position : transform.position + Vector3.up;
            var forward = m_Camera != null ? m_Camera.transform.forward : Vector3.forward;
            RaycastHit chosen = default;
            var found = false;
            var directions = new[] { forward, (forward + Vector3.down * .7f).normalized, Vector3.down,
                Quaternion.Euler(0, -35, 0) * forward, Quaternion.Euler(0, 35, 0) * forward };
            foreach (var direction in directions)
            {
                if (!RaycastRoom(new Ray(eye, direction), 5f, out var hit)) continue;
                bool nearby = false;
                foreach (var point in m_AutoMeshPoints) if (Vector3.Distance(point, hit.point) < .25f) nearby = true;
                if (nearby) continue;
                chosen = hit; found = true; break;
            }
            if (!found)
            {
                Debug.Log($"[Nature Vine AUTO] Waiting for a reachable room surface: trackedMeshes={(m_MeshManager != null ? m_MeshManager.meshes.Count : 0)} colliders={m_SpatialColliders.Count}; no progress or gesture required");
                return;
            }
            m_AutoMeshAttempts++;
            var originalBranches = m_Generator.branchCount;
            bool success;
            try { m_Generator.branchCount = 1; success = RequestIvy(chosen, false, "AUTO mesh diagnostic", .7f); }
            finally { m_Generator.branchCount = originalBranches; }
            if (success) { m_AutoMeshSuccesses++; m_AutoMeshPoints.Add(chosen.point); }
            Mark(chosen.point, success ? MarkerKind.Planted : MarkerKind.NoBranch);
            Debug.Log($"[Nature Vine AUTO] attempt={m_AutoMeshAttempts}/9 generated={m_AutoMeshSuccesses}/3 success={success} mesh={chosen.collider.name} point={chosen.point}");
        }

        void Update()
        {
            AutoMeshDiagnostic();
            var plantable = m_Generator != null && CanPlantEnvironment;
            NotePlantable(plantable);
            UpdateGazeTargets(plantable && m_GazePlanting);
            UpdateGuideSpot(plantable, Time.time);
            if (m_Suspended) { ResetTouch(); return; }
            if (m_ForceTestVineGrowth)
            {
                m_ForceTestVineGrowth = false;
                ForceTestVineGrowth();
            }
            // Vine text hint removed by request; touch planting remains active.
            if (plantable && m_TouchPlanting) UpdateTouch();
            else ResetTouch();
        }

        void ResetTouch()
        {
            m_HandTouching[0] = m_HandTouching[1] = false;
        }

        /// <summary>Fingertip on the room mesh → a vine from that point (see NatureVineTouch for the rules).</summary>
        void UpdateTouch()
        {
            if (Time.time - m_LastColliderRefresh > 1f) RefreshSpatialColliders();
            m_Hands.Sample();
            for (var h = 0; h < 2; h++)
            {
                var hand = m_Hands.Hands[h];
                if (!hand.Valid || !CollectNearby(hand.Index, NatureVineTouch.ReleaseDistance + .03f)) { m_HandTouching[h] = false; continue; }
                if (m_HandTouching[h])
                {
                    if (!NatureVineTouch.TryFindSurface(hand.Index, m_Nearby, NatureVineTouch.ReleaseDistance, out _)) m_HandTouching[h] = false;
                    continue;
                }
                if (!NatureVineTouch.TryFindSurface(hand.Index, m_Nearby, NatureVineTouch.TouchDistance, out var hit))
                {
                    LogNearTouch(h, hand.Index);
                    continue;
                }
                m_HandTouching[h] = true;
                var soft = m_LastNatureSoftOrigin == Vector3.zero ? (Vector3?)null : m_LastNatureSoftOrigin;
                if (!NatureVineTouch.CanPlant(true, Time.time, m_HandLastPlant[h], hit.point, m_HandLastPoint[h], soft, m_Growing))
                {
                    Trace($"Touch ({(h == 0 ? "L" : "R")}) ignored: cooldown, spacing, bag or {m_Growing} growing", hit.point);
                    Mark(hit.point, MarkerKind.Refused);
                    continue;
                }
                var source = h == 0 ? "Touch Vine L" : "Touch Vine R";
                if (!RequestIvy(hit, true, source)) continue;
                m_HandLastPlant[h] = Time.time;
                m_HandLastPoint[h] = hit.point;
                m_TouchVines++;
                m_UserVines++;
                Debug.Log($"[Nature Vine] {source} planted at {hit.point} normal={hit.normal} ({m_GrownStrokeCount}/{m_MaxStrokes})");
            }
        }

        /// <summary>Room colliders whose shape is within <paramref name="radius"/> of <paramref name="point"/>.</summary>
        bool CollectNearby(Vector3 point, float radius)
        {
            m_Nearby.Clear();
            var count = Physics.OverlapSphereNonAlloc(point, radius, m_Overlap, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
                if (m_Overlap[i] != null && m_SpatialColliders.Contains(m_Overlap[i])) m_Nearby.Add(m_Overlap[i]);
            return m_Nearby.Count > 0;
        }

        // ---------- grow sound ----------
        /// <summary>One light sound where a vine starts (several vines starting together share one).</summary>
        void PlayGrowSound(Vector3 point)
        {
            if (!m_GrowSound || !Application.isPlaying || !NatureVineSound.CanPlay(Time.time, m_LastGrowSound)) return;
            var index = NatureVineSound.Pick(m_LastGrowClip, m_Random.NextDouble());
            if (m_GrowClips[index] == null) m_GrowClips[index] = Resources.Load<AudioClip>(NatureVineSound.Resource(index));
            var clip = m_GrowClips[index];
            if (clip == null) { Debug.LogWarning("[Nature Vine] Missing grow sound " + NatureVineSound.Resource(index)); return; }
            m_LastGrowSound = Time.time;
            m_LastGrowClip = index;
            var go = new GameObject("Nature Vine Sound");
            go.transform.SetParent(transform, false);
            go.transform.position = point;
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = .75f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = .5f;
            source.maxDistance = 6f;
            source.dopplerLevel = 0f;
            source.PlayOneShot(clip, m_GrowSoundVolume);
            Destroy(go, clip.length + .2f);
        }

        // ---------- guide spot ----------
        /// <summary>
        /// Shown with Moodi's vine tip: a soft green spot on the nearest wall or table ahead (room mesh only, within
        /// reach, not the floor). It breathes for 12 s and fades as soon as the user plants a vine or planting stops.
        /// </summary>
        public bool ShowGuideSpot()
        {
            if (!m_GuideSpot || m_Camera == null || !CanPlantEnvironment) return false;
            if (Time.time - m_LastColliderRefresh > 1f) RefreshSpatialColliders();
            var eye = m_Camera.transform;
            var found = false;
            RaycastHit best = default;
            foreach (var direction in NatureVineGuideSpot.Directions(eye.forward, eye.right))
                if (RaycastRoom(new Ray(eye.position, direction), NatureVineGuideSpot.MaxReach, out var hit) &&
                    NatureVineGuideSpot.Usable(eye.position, hit) && (!found || hit.distance < best.distance))
                {
                    best = hit;
                    found = true;
                }
            if (!found)
            {
                Debug.Log($"[Nature Vine] Guide spot: no wall or table within {NatureVineGuideSpot.MaxReach} m ahead ({m_SpatialColliders.Count} room meshes)");
                return false;
            }
            var sprite = Resources.Load<Sprite>(NatureVineGuideSpot.SpriteResource);
            if (sprite == null) { Debug.LogWarning("[Nature Vine] Missing guide spot sprite " + NatureVineGuideSpot.SpriteResource); return false; }
            if (m_Spot == null)
            {
                var go = new GameObject("Nature Vine Guide Spot");
                go.transform.SetParent(transform, false);
                m_Spot = go.AddComponent<SpriteRenderer>();
                m_Spot.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            m_Spot.sprite = sprite;
            // The sprite's front faces away from the wall, toward the viewer.
            m_Spot.transform.SetPositionAndRotation(best.point + best.normal * NatureVineGuideSpot.Lift, Quaternion.LookRotation(-best.normal));
            m_Spot.transform.localScale = Vector3.one * (NatureVineGuideSpot.Size / Mathf.Max(.001f, sprite.bounds.size.x));
            m_Spot.color = new Color(NatureVineGuideSpot.Tint.r, NatureVineGuideSpot.Tint.g, NatureVineGuideSpot.Tint.b, 0f);
            m_Spot.gameObject.SetActive(true);
            m_SpotStart = Time.time;
            m_SpotCancelAt = -1f;
            Debug.Log($"[Nature Vine] Guide spot at {best.point}, {best.distance:0.00} m away, on {best.collider.name}");
            return true;
        }

        void UpdateGuideSpot(bool plantable, float now)
        {
            if (m_Spot == null || m_SpotStart < 0f) return;
            if ((!plantable || m_UserVines > 0) && m_SpotCancelAt < 0f) m_SpotCancelAt = now;
            var t = now - m_SpotStart;
            var sinceCancel = m_SpotCancelAt < 0f ? -1f : now - m_SpotCancelAt;
            if (NatureVineGuideSpot.Finished(t, sinceCancel)) { EndGuideSpot(); return; }
            var color = m_Spot.color;
            color.a = NatureVineGuideSpot.Alpha(t, sinceCancel);
            m_Spot.color = color;
        }

        void EndGuideSpot()
        {
            m_SpotStart = m_SpotCancelAt = -1f;
            if (m_Spot == null) return;
            if (Application.isPlaying) Destroy(m_Spot.gameObject); else DestroyImmediate(m_Spot.gameObject);
            m_Spot = null;
        }

        // ---------- hint ----------
        /// <summary>Hint opacity <paramref name="t"/> s after it appeared, or <paramref name="sinceCancel"/> s after it was cancelled (negative: not cancelled).</summary>
        public static float HintAlpha(float t, float sinceCancel)
        {
            if (t < 0f) return 0f;
            var a = t < HintIn ? t / HintIn : t < HintIn + HintHold ? 1f : 1f - (t - HintIn - HintHold) / HintOut;
            if (sinceCancel >= 0f) a = Mathf.Min(a, 1f - sinceCancel / HintCancel);
            return Mathf.Clamp01(a) * HintAlphaMax;
        }

        /// <summary>Shows 「用手指碰一碰墙面或桌面，长出藤蔓」 in front of the user, below eye level, for ~6 s. Tests call it directly.</summary>
        public void UpdateHint(bool plantable, float now)
        {
            if (!plantable) m_PlantableSince = -1f;
            else if (m_PlantableSince < 0f) m_PlantableSince = now;
            if (m_HintStart < 0f && plantable && m_TouchVines == 0 && m_HintCount < HintMaxShows && m_Camera != null)
            {
                var due = m_PlantableSince + HintFirstDelay;
                if (m_HintCount > 0) due = Mathf.Max(due, m_HintShownAt + HintRepeat);
                if (now >= due) ShowHint(now);
            }
            if (m_HintStart < 0f || m_Hint == null) return;
            if ((!plantable || m_TouchVines > 0) && m_HintCancelAt < 0f) m_HintCancelAt = now;
            var t = now - m_HintStart;
            var alpha = HintAlpha(t, m_HintCancelAt < 0f ? -1f : now - m_HintCancelAt);
            var color = Color.white; color.a = alpha;
            m_Hint.color = color;
            var on = alpha > .01f;
            if (m_Hint.gameObject.activeSelf != on) m_Hint.gameObject.SetActive(on);
            if (!on && t > HintIn) { m_HintStart = -1f; m_HintCancelAt = -1f; }
        }
        void UpdateHint(bool plantable) => UpdateHint(plantable, Time.time);

        void ShowHint(float now)
        {
            var english = Moodium.Characters.MoodiVoice.LanguageCode == "en";
            if (m_Hint == null)
            {
                var go = new GameObject("Nature Vine Hint");
                go.transform.SetParent(transform, false);
                m_Hint = go.AddComponent<SpriteRenderer>();
                m_Hint.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            m_Hint.sprite = Resources.Load<Sprite>(NatureVineLook.HintSprite(english));
            var eye = m_Camera.transform;
            var forward = Vector3.ProjectOnPlane(eye.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(eye.up, Vector3.up);
            var position = eye.position + forward.normalized * .6f - Vector3.up * .12f;
            m_Hint.transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - eye.position, Vector3.up));
            Moodium.UI.UIRefreshVisuals.UnitScale(m_Hint.transform);
            m_Hint.color = new Color(1f, 1f, 1f, 0f);
            m_Hint.gameObject.SetActive(false);
            m_HintStart = now; m_HintCancelAt = -1f; m_HintShownAt = now; m_HintCount++;
            Debug.Log($"[Nature Vine] hint shown ({m_HintCount}/{HintMaxShows}): {NatureVineLook.HintText(english)}");
        }

        void EndHint()
        {
            m_HintStart = m_HintCancelAt = -1f;
            if (m_Hint != null) { if (Application.isPlaying) Destroy(m_Hint.gameObject); else DestroyImmediate(m_Hint.gameObject); }
            m_Hint = null;
        }

        // ---------- look ----------
        Material[] LeafMaterials(bool maple)
        {
            var cached = maple ? m_MapleLeaves : m_IvyLeaves;
            if (cached != null || m_PolySpatialLeafMaterial == null) return cached;
            var names = NatureVineLook.Textures(maple);
            cached = new Material[names.Length];
            for (var i = 0; i < names.Length; i++)
            {
                var material = new Material(m_PolySpatialLeafMaterial) { name = "Nature Vine Leaf " + (maple ? "Maple " : "Ivy ") + i };
                var texture = Resources.Load<Texture2D>(names[i]);
                if (texture != null) { material.mainTexture = texture; if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture); }
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
                m_RuntimeMaterials.Add(material);
                cached[i] = material;
            }
            if (maple) m_MapleLeaves = cached; else m_IvyLeaves = cached;
            return cached;
        }

        Material StemMaterial(bool maple)
        {
            var cached = maple ? m_MapleStem : m_IvyStem;
            if (cached != null || m_BaseBranchMaterial == null) return cached != null ? cached : m_BaseBranchMaterial;
            cached = new Material(m_BaseBranchMaterial) { name = "Nature Vine Stem " + (maple ? "Maple" : "Ivy") };
            if (cached.HasProperty("_BaseColor")) cached.SetColor("_BaseColor", NatureVineLook.Stem(maple));
            m_RuntimeMaterials.Add(cached);
            if (maple) m_MapleStem = cached; else m_IvyStem = cached;
            return cached;
        }

        NatureIvySurfaceResolver CreateSurfaceResolver() => new(
            m_SpatialColliders, m_MinUpNormalDot, m_MaxSurfaceSearchDistance,
            m_SurfaceSampleRadius, m_SurfaceSampleCount);

        public const int MaxRetainedVineGroups = 12;
        readonly Queue<List<MeshFilter>> m_RetainedVines = new();
        void RetainVine(List<MeshFilter> filters)
        {
            m_RetainedVines.Enqueue(filters);
            while (m_RetainedVines.Count > MaxRetainedVineGroups)
                foreach (var old in m_RetainedVines.Dequeue())
                    if (old != null)
                    {
                        var mesh = old.sharedMesh;
                        if (mesh != null && m_OwnedMeshes.Remove(mesh)) BubblePlayVisuals.Release(mesh);
                        old.gameObject.SetActive(false);
                        BubblePlayVisuals.Release(old.gameObject);
                    }
        }

        readonly Dictionary<Material, Material> m_NativeLeafMaterials = new();
        readonly Dictionary<Material, Material> m_MapleNativeMaterials = new();
        void FixNativeLeafMaterials(MeshRenderer renderer, bool maple = false)
        {
            if (renderer == null || m_PolySpatialLeafMaterial == null) return;
            var cache = maple ? m_MapleNativeMaterials : m_NativeLeafMaterials;
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                var source = materials[i];
                if (source == null || source.shader == null || source.shader.name == "Universal Render Pipeline/Lit") continue;
                if (!cache.TryGetValue(source, out var converted) || converted == null)
                {
                    converted = new Material(m_PolySpatialLeafMaterial) { name = source.name + " (Native alpha clip)" };
                    var slot = source.HasProperty("_Diffuse") ? "_Diffuse" : source.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
                    var texture = source.HasProperty(slot) ? source.GetTexture(slot) : source.mainTexture;
                    texture = Resources.Load<Texture2D>(maple ? "MoodiumNature/NativeVine/Maple" : "MoodiumNature/NativeVine/Leaf 3");
                    converted.mainTexture = texture;
                    if (converted.HasProperty("_MainTex")) converted.SetTexture("_MainTex", texture);
                    converted.SetTexture("_BaseMap", texture);
                    if (source.HasProperty(slot)) { converted.SetTextureScale("_BaseMap", source.GetTextureScale(slot)); converted.SetTextureOffset("_BaseMap", source.GetTextureOffset(slot)); }
                    Debug.Log($"[Nature Vine Leaf] {source.name}: slot={slot}, texture={(texture != null ? texture.name : "MISSING")}, shader={converted.shader.name}");
                    converted.SetColor("_BaseColor", maple ? new Color(.9f,.32f,.12f,1f) : Color.white);
                    converted.SetFloat("_AlphaClip", 1); converted.EnableKeyword("_ALPHATEST_ON");
                    converted.SetFloat("_Cutoff", source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : .45f);
                    converted.SetFloat("_Cull", 0);
                    m_RuntimeMaterials.Add(converted); cache[source] = converted;
                }
                materials[i] = converted;
            }
            renderer.sharedMaterials = materials;
        }

        bool RequestIvy(RaycastHit hit, bool consumeManualBudget, string source, float requestedLength = -1f)
        {
            if (hit.collider == null || m_Generator == null)
                return false;
            var previousBranches = m_Generator.transform.childCount;
            var existing = new HashSet<MeshFilter>(m_Generator.GetComponentsInChildren<MeshFilter>(true));
            var originalPositions = m_Generator.maxBranchPositions;
            var originalStem = m_Generator.branchMaterial;
            var maple = m_Random.NextDouble() < NatureVineLook.MapleShare;
            if (requestedLength > 0f)
                m_Generator.maxBranchPositions = Mathf.Max(2, Mathf.CeilToInt(requestedLength / Mathf.Max(.01f, m_Generator.branchPartLength)));
            var stem = StemMaterial(maple);
            if (stem != null) m_Generator.branchMaterial = stem;
            m_ProbeColliders = CollidersWithin(hit.point, m_Generator.maxBranchPositions * m_Generator.branchPartLength + .15f);
            TraceSurface($"{source} → Ivy Request → generateIvyA Called ({(maple ? "maple" : "ivy")}, probing {m_ProbeColliders.Count} room chunks)", hit);
            try { m_Generator.generateIvyA(hit); }
            finally
            {
                m_Generator.maxBranchPositions = originalPositions;
                m_Generator.branchMaterial = originalStem;
                m_ProbeColliders = null;
            }
            RemoveBatchers();
            var generated = new List<MeshFilter>();
            int vertices = 0, trimmed = 0, triangles = 0;
            foreach (var filter in m_Generator.GetComponentsInChildren<MeshFilter>(true))
                if (!existing.Contains(filter) && filter.sharedMesh != null)
                {
                    FixNativeLeafMaterials(filter.GetComponent<MeshRenderer>(), maple);
                    // Plugin leaves may reference prefab asset meshes; reveal only an owned copy.
                    var ownedMesh = Instantiate(filter.sharedMesh);
                    ownedMesh.name = filter.sharedMesh.name + " (Nature runtime)";
                    filter.sharedMesh = ownedMesh;
                    m_OwnedMeshes.Add(ownedMesh);
                    generated.Add(filter);
                    trimmed += TrimUnusedVertices(filter.sharedMesh);
                    vertices += filter.sharedMesh.vertexCount;
                    triangles += filter.sharedMesh.triangles.Length / 3;
                }
            if (generated.Count == 0)
            {
                Trace($"{source} → no branch: the surface walk found nothing around this point", hit.point);
                Mark(hit.point, MarkerKind.NoBranch);
                return false;
            }
            var renderer = generated[0].GetComponent<MeshRenderer>();
            Trace($"{source} → {generated.Count} branches, {vertices} vertices ({trimmed} unused removed), {triangles} triangles, " +
                  $"renderer enabled={renderer != null && renderer.enabled}, active={generated[0].gameObject.activeInHierarchy}, " +
                  $"shader={(renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.shader.name : "<none>")}", hit.point);
            RetainVine(generated);
            Mark(hit.point, MarkerKind.Planted);
            PlayGrowSound(hit.point);
            if (Application.isPlaying) StartCoroutine(RevealBranches(generated, maple));
            if (consumeManualBudget)
                m_GrownStrokeCount++;
            if (Application.isPlaying && m_DebugMode) StartCoroutine(InspectGeneratedIvy(source, previousBranches, hit.normal));
            return true;
        }

        List<Collider> CollidersWithin(Vector3 point, float radius)
        {
            var list = new List<Collider>();
            var r2 = radius * radius;
            foreach (var collider in m_SpatialColliders)
                if (collider != null && collider.bounds.SqrDistance(point) <= r2) list.Add(collider);
            return list;
        }

        /// <summary>Safety net: no Ivy Studio batcher may run on our vines (it disables the branch renderers).</summary>
        void RemoveBatchers()
        {
            if (m_Generator == null) return;
            foreach (var batcher in m_Generator.GetComponentsInChildren<Artngame.INfiniDy.ControlCombineChildrenINfiniDyGrassLAND>(true))
                if (Application.isPlaying) Destroy(batcher); else DestroyImmediate(batcher);
        }

        System.Collections.IEnumerator InspectGeneratedIvy(string source, int previousBranches, Vector3 surfaceNormal)
        {
            yield return null;
            while (m_Suspended) yield return null;
            var renderers = m_Generator != null ? m_Generator.GetComponentsInChildren<MeshRenderer>(true) : null;
            var created = m_Generator != null && m_Generator.transform.childCount > previousBranches;
            if (renderers == null || renderers.Length == 0)
            {
                Trace($"{source} → Generated Object {(created ? "Found" : "Not Found")} → Renderer Not Found", transform.position);
                yield break;
            }

            Trace($"{source} → MeshRenderer Count={renderers.Length}", transform.position);
            foreach (var renderer in renderers)
            {
                var material = renderer.sharedMaterial;
                var shader = material != null && material.shader != null ? material.shader.name : "<none>";
                Trace($"{source} → Generated Object {(created ? "Found" : "Not Found")} → Renderer Found: enabled={renderer.enabled}, active={renderer.gameObject.activeInHierarchy}, material={material?.name ?? "<none>"}, shader={shader}, bounds={renderer.bounds}, position={renderer.transform.position}, scale={renderer.transform.lossyScale}", renderer.transform.position);
            }
        }

        // IvyStudio creates triangles in root-to-tip order. Reveal its actual geometry,
        // without its unsupported custom growth shader or scaling the surface attachment.
        // Leaves unfold as the reveal passes them; they are one quad mesh per leaf texture, rebuilt while growing.
        System.Collections.IEnumerator RevealBranches(List<MeshFilter> filters, bool maple)
        {
            m_Growing++;
            // Native leaf meshes must not participate in the stem's index reveal.
            var nativeLeaves = new List<(MeshFilter filter, Vector3 scale, Renderer renderer, float at, Vector3 stalk, Quaternion rotation, Vector3 size)>();
            var stems = filters.FindAll(f => f != null && f.GetComponent<MeshRenderer>() != null &&
                !f.GetComponent<MeshRenderer>().sharedMaterial.name.Contains("Native alpha clip"));
            foreach (var leaf in filters)
            {
                if (leaf == null || stems.Contains(leaf)) continue;
                float nearest = float.PositiveInfinity, at = 0f; Vector3 joint = leaf.transform.position;
                Vector3 tangent = leaf.transform.up, surfaceNormal = leaf.transform.forward;
                foreach (var stem in stems)
                {
                    var vertices = stem.sharedMesh.vertices;
                    for (int v = 0; v < vertices.Length; v++)
                    {
                        var point = stem.transform.TransformPoint(vertices[v]);
                        var distance = (point - leaf.transform.position).sqrMagnitude;
                        if (distance < nearest)
                        {
                            nearest = distance; joint = point; at = (float)v / Mathf.Max(1, vertices.Length - 1);
                            int ringSize = m_Generator != null ? m_Generator.branch3dDivisions : VineDivisions;
                            int lo = Mathf.Max(0, v - ringSize), hi = Mathf.Min(vertices.Length - 1, v + ringSize);
                            var direction = stem.transform.TransformVector(vertices[hi] - vertices[lo]);
                            if (direction.sqrMagnitude > .000001f) tangent = direction.normalized;
                        }
                    }
                }
                if (RaycastRoom(new Ray(joint + surfaceNormal * .08f, -surfaceNormal), .2f, out var surface)) surfaceNormal = surface.normal;
                // The viewer is in the room, so the surface's open side faces them; never lift a leaf into the wall.
                if (m_Camera != null && Vector3.Dot(surfaceNormal, m_Camera.transform.position - joint) < 0f) surfaceNormal = -surfaceNormal;
                // No built petiole any more (2026-11-03): the texture's own drawn stalk starts on the stem and the leaf points
                // outward from it, to the side the plugin chose, leaning forward along the growth and lifted off the wall.
                var side = NatureVineLeafPose.Side(leaf.transform.position, joint, tangent, surfaceNormal, nativeLeaves.Count);
                var forward = Mathf.Lerp(NatureVineLeafPose.MinForward, NatureVineLeafPose.MaxForward, (float)m_Random.NextDouble());
                var lift = Mathf.Lerp(NatureVineLeafPose.MinLift, NatureVineLeafPose.MaxLift, (float)m_Random.NextDouble());
                var outward = NatureVineLeafPose.Outward(tangent, surfaceNormal, side, forward);
                if (outward.sqrMagnitude > .5f) leaf.transform.rotation = NatureVineLeafPose.Rotation(surfaceNormal, outward, lift);
                var size = leaf.transform.lossyScale;
                leaf.transform.position = NatureVineLeafPose.Centre(joint, leaf.transform.rotation, size);
                var renderer = leaf.GetComponent<Renderer>();
                nativeLeaves.Add((leaf, leaf.transform.localScale, renderer, at, joint, leaf.transform.rotation, size));
                renderer.enabled = false;
            }
            filters = stems;
            var indices = new List<int[]>();
            var vines = new List<VineLeafSet>();
            var materials = LeafMaterials(maple);
            foreach (var filter in filters)
            {
                var triangles = filter.sharedMesh.triangles;
                indices.Add(triangles);
                var set = ShouldUseIvyStudioAutoLeaves() ? null : PlanLeaves(filter, triangles.Length, materials);
                if (set != null) vines.Add(set);
                // Never hand PolySpatial an index-less mesh: the reveal starts with the first ring segment.
                filter.sharedMesh.SetTriangles(triangles, 0, RevealIndexCount(triangles.Length, 0f, m_Generator != null ? m_Generator.branch3dDivisions : VineDivisions), 0);
            }
            var grow = Mathf.Max(.01f, m_VineGrowSeconds);
            var total = grow + NatureVineLeaves.UnfoldSeconds;
            var elapsed = 0f;
            while (elapsed < total)
            {
                yield return null;
                if (m_Suspended) continue;
                elapsed += Time.deltaTime;
                var fraction = Mathf.Clamp01(elapsed / grow);
                for (var i = 0; i < filters.Count; i++)
                    if (filters[i] != null && filters[i].sharedMesh != null)
                        filters[i].sharedMesh.SetTriangles(indices[i], 0,
                            RevealIndexCount(indices[i].Length, fraction, m_Generator != null ? m_Generator.branch3dDivisions : VineDivisions), 0);
                foreach (var native in nativeLeaves)
                    if (native.filter != null)
                    {
                        var k = Mathf.Clamp01((elapsed - native.at * grow) / .45f);
                        var unfold = Mathf.SmoothStep(.001f, 1f, k);
                        native.filter.transform.localScale = native.scale * unfold;
                        // The quad scales about its centre, so keep the stalk end pinned to the stem while it unfolds.
                        native.filter.transform.position = NatureVineLeafPose.Centre(native.stalk, native.rotation, native.size * unfold);
                        native.renderer.enabled = k > 0f;
                        foreach (var child in native.filter.GetComponentsInChildren<Renderer>()) child.enabled = k > 0f;
                    }
                foreach (var vine in vines) vine.Show(elapsed, grow);
            }
            for (var i = 0; i < filters.Count; i++)
                if (filters[i] != null && filters[i].sharedMesh != null)
                    filters[i].sharedMesh.SetTriangles(indices[i], 0);
            foreach (var vine in vines) vine.Show(float.MaxValue, grow);
            m_Growing = Mathf.Max(0, m_Growing - 1);
            var drawn = 0; var leaves = 0;
            foreach (var filter in filters)
            {
                var r = filter != null ? filter.GetComponent<MeshRenderer>() : null;
                if (r != null && r.enabled && r.gameObject.activeInHierarchy) drawn++;
            }
            foreach (var vine in vines) leaves += vine.Leaves.Count;
            var first = filters.Count > 0 && filters[0] != null ? filters[0].GetComponent<MeshRenderer>() : null;
            Trace($"Vine grown: {drawn}/{filters.Count} branches drawn, {leaves} leaves" +
                  (first != null ? $", first branch centre {first.bounds.center}, size {first.bounds.size}" : ""), Vector3.zero);
        }

        /// <summary>
        /// How many triangle indices of a branch the reveal shows at <paramref name="fraction"/> (0..1): never fewer than
        /// the first ring segment, so the mesh always has triangles.
        /// </summary>
        public static int RevealIndexCount(int total, float fraction, int divisions)
        {
            if (total <= 0) return 0;
            var minimum = Mathf.Min(total, 6 * Mathf.Max(1, divisions));
            var shown = Mathf.FloorToInt(total / 3 * Mathf.Clamp01(fraction)) * 3;
            return Mathf.Clamp(shown, minimum, total);
        }

        /// <summary>
        /// Ivy Studio's 3D branch allocates four times the vertices it uses and leaves the rest at the world origin with
        /// zero normals. Drops them (only the used prefix is referenced), so the mesh, its bounds and its tangents are
        /// clean before it goes to visionOS. Returns how many vertices were removed.
        /// </summary>
        public static int TrimUnusedVertices(Mesh mesh)
        {
            if (mesh == null) return 0;
            var triangles = mesh.triangles;
            if (triangles.Length == 0) return 0;
            var used = 0;
            foreach (var index in triangles) if (index + 1 > used) used = index + 1;
            var count = mesh.vertexCount;
            if (used >= count) return 0;
            var vertices = mesh.vertices; var normals = mesh.normals; var uv = mesh.uv; var tangents = mesh.tangents;
            mesh.Clear();
            mesh.SetVertices(vertices, 0, used);
            if (normals.Length == count) mesh.SetNormals(normals, 0, used);
            if (uv.Length == count) mesh.SetUVs(0, uv, 0, used);
            if (tangents.Length == count) mesh.SetTangents(tangents, 0, used);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return count - used;
        }

        /// <summary>Leaves along one branch: path read back from the tube, leaves planned, two leaf meshes created (empty).</summary>
        VineLeafSet PlanLeaves(MeshFilter branch, int triangleIndexCount, Material[] materials)
        {
            if (materials == null || materials.Length == 0 || m_Generator == null) return null;
            var centres = new List<Vector3>(); var normals = new List<Vector3>(); var sides = new List<Vector3>();
            var mesh = branch.sharedMesh;
            var rings = NatureVineRings.Extract(mesh.vertices, triangleIndexCount, m_Generator.branch3dDivisions, centres, normals, sides);
            if (rings < 2) return null;
            for (var i = 0; i < rings; i++)   // tube vertices are in the branch object's space
            {
                centres[i] = branch.transform.TransformPoint(centres[i]);
                normals[i] = branch.transform.TransformDirection(normals[i]).normalized;
                sides[i] = branch.transform.TransformDirection(sides[i]).normalized;
            }
            var set = new VineLeafSet();
            NatureVineLeaves.Plan(centres, normals, sides, m_Random, set.Leaves);
            if (set.Leaves.Count == 0) return null;
            set.Scales = new float[set.Leaves.Count];
            var parent = branch.transform.parent != null ? branch.transform.parent : branch.transform;
            set.Meshes = new Mesh[materials.Length];
            for (var k = 0; k < materials.Length; k++)
            {
                var go = new GameObject("Nature Vine Leaves " + k);
                go.transform.SetParent(parent, false);
                var filter = go.AddComponent<MeshFilter>();
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = materials[k];
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.enabled = false;   // switched on with its first leaf: no empty mesh goes to visionOS
                set.Renderers.Add(renderer);
                var leafMesh = new Mesh { name = "Nature Vine Leaves" };
                leafMesh.MarkDynamic();
                filter.sharedMesh = leafMesh;
                m_OwnedMeshes.Add(leafMesh);
                set.Meshes[k] = leafMesh;
                set.Spaces.Add(go.transform);
            }
            return set;
        }

        sealed class VineLeafSet
        {
            public readonly List<NatureVineLeaf> Leaves = new();
            public float[] Scales;
            public Mesh[] Meshes;
            public readonly List<Transform> Spaces = new();
            public readonly List<MeshRenderer> Renderers = new();
            bool m_Done;
            readonly List<Vector3> m_Vertices = new(), m_Normals = new();
            readonly List<Vector2> m_Uvs = new();
            readonly List<int> m_Triangles = new();
            readonly bool[] m_Shown = new bool[8];

            public void Show(float elapsed, float grow)
            {
                if (m_Done) return;
                var changed = false; var all = true;
                for (var i = 0; i < Leaves.Count; i++)
                {
                    var scale = NatureVineLeaves.Unfold(elapsed - Leaves[i].At * grow);
                    if (elapsed == float.MaxValue) scale = 1f;
                    if (!Mathf.Approximately(scale, Scales[i])) { Scales[i] = scale; changed = true; }
                    if (scale < 1f || Leaves[i].At * grow + NatureVineLeaves.UnfoldSeconds > elapsed) all = false;
                }
                if (changed)
                    for (var k = 0; k < Meshes.Length; k++)
                        if (Meshes[k] != null && Spaces[k] != null)
                        {
                            NatureVineLeaves.Build(Meshes[k], Leaves, Scales, k, Spaces[k], m_Vertices, m_Normals, m_Uvs, m_Triangles);
                            // Turn the renderer on once, when its mesh first has a leaf (later hiding by others is kept).
                            if (k < Renderers.Count && k < m_Shown.Length && Renderers[k] != null && !m_Shown[k] && Meshes[k].vertexCount > 0)
                            {
                                Renderers[k].enabled = true;
                                m_Shown[k] = true;
                            }
                        }
                m_Done = all;
            }
        }

        System.Collections.IEnumerator GrowCompletionBurst(RaycastHit primaryHit)
        {
            var originalBranchCount = m_Generator.branchCount;
            var originalDirectionality = m_Generator.directionality;
            var originalPositions = m_Generator.maxBranchPositions;
            m_Generator.branchCount = 1;
            m_Generator.maxBranchPositions = Mathf.Max(2, Mathf.CeilToInt(m_CompletionRadius / Mathf.Max(.01f, m_Generator.branchPartLength)));
            for (var i = 0; i < m_CompletionBranchCount; i++)
            {
                while (m_Suspended) yield return null;
                var angle = (360f * i / m_CompletionBranchCount) + Random.Range(-m_DirectionRandomness, m_DirectionRandomness);
                var baseDirection = Vector3.ProjectOnPlane(Vector3.right, primaryHit.normal);
                if (baseDirection.sqrMagnitude < .0001f)
                    baseDirection = Vector3.ProjectOnPlane(Vector3.forward, primaryHit.normal);
                var direction = Quaternion.AngleAxis(angle, primaryHit.normal) * baseDirection.normalized;
                var scatter = direction * Random.Range(0f, m_StartPointScatterRadius);
                var searchOrigin = primaryHit.point + scatter + primaryHit.normal * .2f;
                if (CreateSurfaceResolver().TryFindSurfaceBelow(searchOrigin, out var branchHit))
                {
                    m_Generator.directionality = new Vector4(direction.x, direction.y, direction.z, 0f);
                    TraceSurface($"Completion Branch {i + 1}/{m_CompletionBranchCount} → Surface Found", branchHit);
                    RequestIvy(branchHit, false, $"Completion Branch {i + 1}");
                }
                else
                    Trace($"Completion Branch {i + 1}/{m_CompletionBranchCount} → Surface Failed", searchOrigin);
                yield return new WaitForSeconds(m_CompletionGrowthDuration / Mathf.Max(1, m_CompletionBranchCount));
            }
            if (m_Generator != null)
            {
                m_Generator.branchCount = originalBranchCount;
                m_Generator.directionality = originalDirectionality;
                m_Generator.maxBranchPositions = originalPositions;
            }
        }

        void TraceSurface(string stage, RaycastHit hit) => Trace(
            $"{stage}; mesh={hit.collider?.name ?? "<none>"}; point={hit.point}; normal={hit.normal}; distance={hit.distance}", hit.point);

        // Every step is logged (2026-11-03): the device build was silent, so nobody could tell where a vine stopped.
        void Trace(string stage, Vector3 _)
        {
            m_LastDebugStatus = stage;
            Debug.Log($"[Nature Vine] {stage}");
        }

        string PlantableState()
        {
            var journey = GetComponent<Moodium.Flow.WorldJourneyDirector>();
            return $"running={m_Running}, generator={m_Generator != null}, unlocked={m_EnvironmentUnlocked}, suspended={m_Suspended}, " +
                   $"stage={(journey != null ? journey.Stage.ToString() : "none")}, " +
                   $"room meshes={m_SpatialColliders.Count}, gaze targets={m_GazeTargets.Count}";
        }

        void NotePlantable(bool plantable)
        {
            if (m_LastPlantable == plantable) return;
            m_LastPlantable = plantable;
            Debug.Log($"[Nature Vine] Planting {(plantable ? "ON" : "off")}: {PlantableState()}");
        }

        /// <summary>A fingertip near the room mesh but not close enough to count: how far it is (at most every 3 s per hand).</summary>
        void LogNearTouch(int hand, Vector3 tip)
        {
            if (Time.time - m_HandNearLog[hand] < NearTouchLogSeconds) return;
            if (!NatureVineTouch.TryFindSurface(tip, m_Nearby, NatureVineTouch.ReleaseDistance + .03f, out var near)) return;
            m_HandNearLog[hand] = Time.time;
            Debug.Log($"[Nature Vine] Touch ({(hand == 0 ? "L" : "R")}) near the room mesh: fingertip {Vector3.Distance(near.point, tip) * 100f:0.0} cm away " +
                      $"(a touch needs ≤ {NatureVineTouch.TouchDistance * 100f:0} cm), mesh={near.collider?.name}");
        }

        /// <summary>A small dot for two seconds where a touch or pinch landed (only in play mode, while markers are on).</summary>
        void Mark(Vector3 point, MarkerKind kind)
        {
            if (!m_DiagnosticMarkers || !Application.isPlaying) return;
            if (m_MarkerMaterials == null)
            {
                m_MarkerMaterials = new[]
                {
                    BubblePlayVisuals.Material("#34C759"), BubblePlayVisuals.Material("#FFCC00"), BubblePlayVisuals.Material("#FF3B30")
                };
                m_RuntimeMaterials.AddRange(m_MarkerMaterials);
            }
            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "Nature Vine Marker (" + kind + ")";
            DestroyImmediate(dot.GetComponent<Collider>());   // a marker must never catch a ray
            dot.transform.SetParent(transform, false);
            dot.transform.position = point;
            dot.transform.localScale = Vector3.one * MarkerSize;
            var renderer = dot.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = m_MarkerMaterials[(int)kind];
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Destroy(dot, MarkerSeconds);
        }

        // ---------- look + pinch: Default-layer twins of the room mesh ----------

        void OnRoomMeshesChanged(ARMeshesChangedEventArgs args)
        {
            m_RoomMeshesChanged = true;
            if (args.updated != null)
                foreach (var filter in args.updated)
                    if (filter != null && filter.TryGetComponent<MeshCollider>(out var collider)) m_RecookTargets.Add(collider);
        }

        void UpdateGazeTargets(bool expose)
        {
            if (!expose) { ClearGazeTargets(); return; }
            if (!m_RoomMeshesChanged && Time.unscaledTime < m_NextGazeSync) return;
            m_NextGazeSync = Time.unscaledTime + GazeSyncSeconds;
            m_RoomMeshesChanged = false;
            RefreshSpatialColliders();
            SyncGazeTargets();
        }

        /// <summary>
        /// Gives every room MeshCollider (not already on the Default layer) a Default-layer twin, re-cooks twins whose room
        /// mesh changed, and removes twins whose room mesh is gone. Returns how many twins were added.
        /// </summary>
        public int SyncGazeTargets()
        {
            var added = 0;
            foreach (var collider in m_SpatialColliders)
            {
                if (collider is not MeshCollider source || source == null || source.sharedMesh == null) continue;
                if (source.gameObject.layer == GazeTargetLayer) continue;   // PolySpatial already syncs it
                if (!m_GazeTargets.TryGetValue(source, out var target) || target == null)
                {
                    target = CreateGazeTarget(source);
                    m_GazeTargets[source] = target;
                    m_GazeTargetSet.Add(target);
                    added++;
                }
                else if (target.sharedMesh != source.sharedMesh || m_RecookTargets.Contains(source))
                {
                    // ARMeshManager rewrites a mesh in place; a MeshCollider only re-cooks when its mesh is reassigned.
                    target.sharedMesh = null;
                    target.sharedMesh = source.sharedMesh;
                }
                if (target.enabled != source.enabled) target.enabled = source.enabled;
            }
            m_RecookTargets.Clear();
            m_StaleTargets.Clear();
            foreach (var pair in m_GazeTargets)
                if (pair.Key == null || !m_SpatialColliders.Contains(pair.Key)) m_StaleTargets.Add(pair.Key);
            foreach (var key in m_StaleTargets)
            {
                var target = m_GazeTargets[key];
                m_GazeTargets.Remove(key);
                DestroyGazeTarget(target);
            }
            if (added > 0 || m_StaleTargets.Count > 0)
                Debug.Log($"[Nature Vine] Room exposed to gaze: {m_GazeTargets.Count} room meshes have a Default-layer target " +
                          $"(+{added}, -{m_StaleTargets.Count}).");
            return added;
        }

        /// <summary>A Default-layer twin of a room collider, so PolySpatial syncs it to visionOS for gaze + pinch.</summary>
        public static MeshCollider CreateGazeTarget(MeshCollider source)
        {
            var go = new GameObject(GazeTargetName) { layer = GazeTargetLayer };
            go.transform.SetParent(source.transform, false);
            var target = go.AddComponent<MeshCollider>();
            target.convex = false;
            target.sharedMesh = source.sharedMesh;
            target.enabled = source.enabled;
            return target;
        }

        void DestroyGazeTarget(MeshCollider target)
        {
            m_GazeTargetSet.Remove(target);   // also when ARMeshManager already destroyed it with its room mesh
            if (target == null) return;
            if (Application.isPlaying) Destroy(target.gameObject); else DestroyImmediate(target.gameObject);
        }

        void ClearGazeTargets()
        {
            if (m_GazeTargets.Count == 0) return;
            var count = m_GazeTargets.Count;
            foreach (var target in m_GazeTargets.Values) DestroyGazeTarget(target);
            m_GazeTargets.Clear();
            m_GazeTargetSet.Clear();
            m_RecookTargets.Clear();
            Debug.Log($"[Nature Vine] Room hidden from gaze again ({count} targets removed).");
        }

        void OnDisable() => StopAndClear();
    }
}

#else
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Moodium.NatureWorld
{
    /// <summary>Optional Ivy Studio integration. Install the licensed dependency and define MOODIUM_IVY_STUDIO to enable.</summary>
    public sealed class NatureIvySpatialGrowthController : MonoBehaviour
    {
        public bool EnvironmentUnlocked => false;
        public bool CanPlantEnvironment => false;
        public int UserVineCount => 0;
        public void Configure(Camera camera, ARMeshManager meshManager, GameObject prefab, Material material, int maxStrokes) { }
        public void SetProgress(float progress) { }
        public void SetSuspended(bool suspended) { }
        public void SetDebugMode(bool enabled) { }
        public void SetNatureSoftOrigin(Vector3 position) { }
        public void RegisterRoomVisibility(Moodium.Flow.RoomContentVisibility visibility) { }
        public void StartGrowth() { }
        public void StopAndClear() { }
        public bool ShowGuideSpot() => false;
        public bool IsSpatialMeshCollider(Collider collider) => false;
        public bool TryGrowFromGazeRay(Ray ray) => false;
        public bool TryGrowFromSpatialPointer(Collider collider, Vector3 position) => false;
        public void NotePinch(GameObject target, bool hasRay, bool plantable, bool grown) { }
    }
}
#endif
