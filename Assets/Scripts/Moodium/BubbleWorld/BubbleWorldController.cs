using System;
using System.Collections;
using System.Collections.Generic;
using Moodium.Flow;
using Moodium.Interaction;
using Unity.PolySpatial;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

namespace Moodium.NatureWorld
{
    /// <summary>
    /// Completion reward for Nature World and Candy World: a small bubble holding a tiny copy of that world's scan
    /// is summoned by a ring. Touching it (fingertip, or look + pinch) makes it fly to the viewer, swell around them
    /// and then spread the world's points out from near to far until the whole scan surrounds them.
    ///
    /// Rendering stays on the stock URP/Unlit shader: every point is a small quad whose colour comes from one texel
    /// of point_colors.png, and the sky behind the points is an inward-facing panorama sphere. Each world has its own
    /// folder (Resources/MoodiumBubbleWorld/ for Nature, Resources/MoodiumBubbleWorld/Candy/ for Candy) with a baked
    /// Meshes/ subfolder (Moodium ▸ Bubble World ▸ Bake Meshes); missing meshes are built at runtime from points.bytes.
    /// </summary>
    public enum LeaveTarget { Room, Exit }

    [DisallowMultipleComponent]
    public sealed class BubbleWorldController : MonoBehaviour
    {
        public enum Phase { Idle, Armed, Waiting, Floating, Approach, Envelop, Reveal, Done, Rising, Sinking, Inside, Leaving, Portal, HandingOff, Doorway }

        BubbleWorldController m_Nested;
        bool m_FromPortal;
        // 泡泡世界 2.0 (P0): the room entry offers both worlds of the family (BubbleEntryChoice); the chosen one is the
        // first layer and the other one its next layer. The first layer shows home + next layer side by side.
        BubbleEntryChoice m_Choice;
        string m_EntryPick;
        bool m_EntryPickUsed, m_AutoEnter;
        float m_SwitchRadius;
        Coroutine m_SwitchRoutine;
        float m_PortalRadius = .08f, m_PairShownAt = -1f;
        BubbleTag m_HomeTag, m_NextTag;
        GameObject m_TagHost;
        BubbleWorldPlay m_Play;
        bool m_ExitingHandoff;
        /// <summary>
        /// What fills the gaps between points far away.
        /// Soft: a 64×32 blur of the panorama — only its colours, so it reads as the world's air and never shows as a
        /// separate photo. Photo: the original panorama. None: no sky; the room shows through the gaps.
        /// </summary>
        public enum SkyStyle { Soft, Photo, None }

        [Header("Sky")]
        [SerializeField] SkyStyle m_SkyStyle = SkyStyle.Soft;
        [Tooltip("Draw the sky before every point layer (native visionOS sorting group). Without it the sky sphere and " +
                 "the point shells share the same centre, so RealityKit's distance sort can put the sky in front.")]
        [SerializeField] bool m_FixedSkyOrder = true;
        [Header("Hand wake")]
        [Tooltip("Moving a hand inside the world pulls small points of the scene along (BubbleHandWake).")]
        [SerializeField] bool m_HandWake = true;
        public SkyStyle Sky => m_SkyStyle;

        [Header("Door entry")]
        [Tooltip("Room entry through a mercury door (2026-11-02 b): no bubble. When the entry is summoned a mercury droplet " +
                 "flies out and tears open into a door with the world at full size behind it and the real room around. " +
                 "Walking through enters, walking back out returns to the room (the world waits behind the door); the names on " +
                 "top of the door switch between the family's two worlds. Needs the AR occlusion material, which draws on " +
                 "device / simulator only. Off: the old bubble entry (two bubbles to choose from, the bubble swells round the viewer).")]
        [SerializeField] bool m_DoorEntry = true;
        [Tooltip("Next layer: a bubble inside the first world that opens the family's other world. Off since 2026-11-02 b " +
                 "(one world at a time; go back out through the door and pick the other one on the door's switch).")]
        [SerializeField] bool m_NextLayer = false;
        [Tooltip("The soft glowing mirror halo round the door's lip (reflects the room, warped; fades into the real room).")]
        [SerializeField] bool m_DoorHalo = true;
        BubbleDoor m_Door;
        BubbleDoorSwitcher m_Switcher;
        Vector3[][] m_NearSamples;
        bool m_DoorFellBack;
        // Door session: the door owns the entry until Leave (exit) or StopAndClear. m_KeepDoor keeps it through a world switch.
        bool m_DoorMode, m_KeepDoor, m_DoorWorldShown;
        bool[] m_Held;
        int m_HeldVersion = -1;
        string m_DoorSwitchTo;
        float m_RevealStarted;
        List<int> m_RevealOrder;
        /// <summary>Layers that start within this distance of the scan centre may reach into the room in front of the door.</summary>
        const float DoorSampleReach = 4f;
        /// <summary>The mercury door is up (arriving, open, folded into a droplet, or behind the viewer after they went through).</summary>
        public bool DoorOpen => m_Door != null;
        public BubbleDoor Door => m_Door;
        public BubbleDoorSwitcher DoorSwitch => m_Switcher;
        /// <summary>The door is folded away beside the sensory bag (or flying there).</summary>
        public bool DoorParked => m_Door != null && m_Door.IsParked;
        /// <summary>The door stands open in the room (or is opening / switching): walking interactions wait, or people walk into it.</summary>
        public bool DoorBlocksWalking => m_DoorMode && m_Door != null && !m_Door.IsParked;
        /// <summary>The world shows through the door to a viewer in the room: room content beyond the door plane should hide.</summary>
        public bool DoorShowsWorldFromRoom => m_DoorMode && m_Door != null && m_DoorWorldShown && m_Door.InRoom;
        float m_HeldAt;

        [Header("Nested handoff")]
        [SerializeField] float m_OuterFadeStartRadius = 1f;
        [SerializeField] float m_OuterFadeEndRadius = 4f;
        [SerializeField, Range(.9f, .99f)] float m_NestedSkyScale = .97f;
        [Tooltip("Seconds after entering a world before the next-layer bubble / exit bubble appear. 0 = at once " +
                 "(they used to wait 10 s).")]
        [SerializeField, Min(0f)] float m_NextBubbleDelaySeconds = 0f;
        [Tooltip("Build the next layer while the viewer is still in the room, so its bubble is ready the moment the " +
                 "viewer is inside. Off: it is built after entering (a few seconds' wait).")]
        [SerializeField] bool m_PrepareNestedEarly = true;
        public static float OuterAlpha(float radius, float start = 1f, float end = 4f) =>
            1f - Mathf.InverseLerp(start, end, radius);

        const float CompletedProgress = .999f;
        /// <summary>Points within this radius (metres, scan scale) live inside the small bubble (4 of 20 layers for the shipped scan).</summary>
        public const float MiniWorldRadius = 10.5f;
        public const float SkyMaxRadius = 80f;
        public const int BackgroundRenderQueue = 1000;

        [Header("Appear")]
        [SerializeField] bool m_SqueezeBreath = true;
        [Tooltip("Relative to the head when the bubble appears: x = right, y = up, z = forward (yaw only).")]
        [SerializeField] Vector3 m_AppearOffset = new(-.35f, -.15f, .9f);
        [SerializeField, Min(.05f)] float m_BubbleRadius = .15f;
        [SerializeField, Min(0f)] float m_BobAmplitude = .015f;
        [SerializeField, Min(.1f)] float m_BobPeriod = 3f;
        [SerializeField] float m_SpinDegreesPerSecond = 6f;
        [SerializeField, Range(0f, 1f)] float m_MembraneAlpha = .85f;
        [SerializeField] Color m_NatureFilmTint = Color.white;
        [SerializeField] Color m_CandyFilmTint = new(1f, .9f, .97f, 1f);

        [Header("Touch")]
        [SerializeField, Min(0f)] float m_TouchPadding = .01f;

        [Header("Grow")]
        [SerializeField, Min(.1f)] float m_ApproachSeconds = 1.4f;
        [SerializeField, Min(.2f)] float m_ApproachRadius = .6f;
        [SerializeField, Min(.1f)] float m_EnvelopSeconds = 1.2f;
        [SerializeField, Min(.1f)] float m_RevealSeconds = 3f;
        [SerializeField, Min(.05f)] float m_LayerGrowSeconds = .25f;
        [SerializeField, Min(0)] int m_SparklesPerLayer = 3;
        [Tooltip("After the last layer lights up, the sky fades in at SkyMaxRadius to close the gaps.")]
        [SerializeField, Min(.1f)] float m_SkyFadeSeconds = 1.5f;
        [Tooltip("Put the scan's ground on the real floor (XR Origin height) when the head height looks plausible.")]
        [SerializeField] bool m_AlignGroundToFloor = true;

        public event Action Appeared;
        public event Action Touched;
        public event Action Revealed;
        /// <summary>A layer finished opening around the viewer, with that layer's folder. Fires for the first
        /// layer and (forwarded) for the nested one, unlike Revealed which the nested layer keeps to itself.</summary>
        public event Action<string> LayerRevealed;

        public Phase CurrentPhase => m_Phase;

        Camera m_Viewer;
        string m_Folder = BubbleWorldData.NatureFolder;
        string m_OuterFolder = BubbleWorldData.NatureFolder;
        AudioClip m_TouchSound;
        Phase m_Phase = Phase.Idle;
        Coroutine m_Routine;
        Coroutine m_BuildRoutine;
        bool m_CoreLayersReady;
        bool m_AllLayersReady;
        // The next-layer bubble and the exit bubble appear right where the viewer's finger just touched to enter.
        // A touch only counts once the fingertip has been away from them, so a resting hand never triggers them.
        bool m_PortalArmed, m_HomeArmed;
        bool m_Parked,m_PortalLocked;Vector3 m_ParkTarget;float m_UnlockAt=-10f,m_PortalUnlockAt=-10f,m_GuardScale=1,m_GuardAlpha=1,m_FollowScale=1,m_FollowAlpha=1;
        BubbleHoldGate m_HomeHold,m_Hold;BubbleHoldRing m_HomeHoldRing,m_PortalHoldRing;
        bool ChimeLocked=>m_Play!=null&&m_Play.ChimeFocus;
        bool PortalLocked=>m_PortalLocked||(transform.parent!=null&&transform.parent.TryGetComponent<BubbleWorldController>(out var outer)&&outer.ChimeLocked);
        public static bool TouchAfterGrace(bool touching,float now,float unlockAt,ref bool armed)
        {
            if(now-unlockAt<ChimeFocus.UnlockGraceSeconds){armed=false;return false;}
            if(!armed){armed=!touching;return false;}return touching;
        }


        // Scene objects
        GameObject m_Root;          // bubble centre; the scan centre (eye point) sits here
        Transform m_WorldRoot;      // scaled copy of the scan (k = radius / MiniWorldRadius)
        Transform m_Membrane;
        Collider m_MembraneCollider;
        Transform m_Sky;
        readonly List<GameObject> m_Layers = new();
        readonly List<Mesh> m_RuntimeMeshes = new();
        Vector3[] m_LayerRestPositions;
        Quaternion[] m_LayerRestRotations;
        Material m_HomeMaterial;
        Material m_PointMaterial, m_SkyMaterial, m_SkyFadeMaterial, m_MembraneMaterial;
        MeshRenderer m_SkyRenderer;
        VisionOSSortingGroup m_WorldSorting;
        BubbleHandWake m_Wake;
        AudioSource m_Audio;
        BubbleSplatMotion m_Motion;

        // Data
        float m_EyeHeight;
        float[] m_LayerRadii;
        int m_MiniLayerCount;
        float m_BobPhase;
        float m_Radius;

        // Hands
        XRHandSubsystem m_Hands;
        XROrigin m_Origin;

        static readonly Dictionary<string, bool> s_HasData = new();

        /// <summary>True when <paramref name="folder"/>/points.bytes is in the build.</summary>
        public static bool HasData(string folder)
        {
            if (!s_HasData.TryGetValue(folder, out var has))
                s_HasData[folder] = has = Resources.Load<TextAsset>(BubbleWorldData.PointsResource(folder)) != null;
            return has;
        }

        public string Folder => m_Folder;

        public static bool ShouldAppear(float normalizedProgress) => normalizedProgress >= CompletedProgress;

        // ---------- Pure helpers (EditMode tests) ----------

        /// <summary>Smoothstep ease used for every phase.</summary>
        public static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Bubble radius during the approach (t 0..1): small → arm's-length bubble.</summary>
        public static float ApproachRadius(float t, float from, float to) => Mathf.Lerp(from, to, Ease(t));

        /// <summary>
        /// Envelop radius (t 0..1). Grows exponentially so the swell feels even: each 10% of the time multiplies the
        /// radius by the same factor (0.6 m → 10.5 m ≈ ×1.33 per step).
        /// </summary>
        public static float EnvelopRadius(float t, float from, float to)
        {
            from = Mathf.Max(.001f, from);
            return from * Mathf.Pow(to / from, Ease(t));
        }

        /// <summary>The bubble film fades out once the viewer is well inside it.</summary>
        public static float MembraneAlpha(float radius, float baseAlpha) =>
            baseAlpha * (1f - Mathf.InverseLerp(1f, 2.5f, radius));

        /// <summary>Index of the first layer that does not fit inside the small bubble.</summary>
        public static int MiniLayerCount(float[] layerOuterRadii, float miniRadius)
        {
            if (layerOuterRadii == null) return 0;
            var n = 0;
            while (n < layerOuterRadii.Length && layerOuterRadii[n] <= miniRadius) n++;
            return n;
        }

        /// <summary>Number of near layers needed for the visible miniature bubble.</summary>
        public static int WarmupLayerCount(int miniLayerCount, int totalLayers) =>
            Mathf.Clamp(miniLayerCount, 1, Mathf.Max(1, totalLayers));

        /// <summary>When (0..1 of the reveal) the j-th of <paramref name="remaining"/> outer layers switches on.</summary>
        public static float LayerRevealTime(int j, int remaining) => remaining <= 0 ? 0f : j / (float)remaining;

        /// <summary>
        /// The sky inside the small bubble fades out while it swells past the viewer, so the room shows through the
        /// gaps between points until the reveal is finished (then the sky fades back in far away).
        /// </summary>
        public static float SkyAlphaWhileEnveloping(float radius) => 1f - Mathf.InverseLerp(.8f, 1.6f, radius);

        /// <summary>Scale-in of a newly revealed layer (0.9 → 1 with a small overshoot).</summary>
        // ---------- first-entry warm-up ----------
        // On device the expensive part of a big point mesh is its first display (RealityKit builds the mesh resource).
        // The shells outside the mini world first appeared during the reveal, mid-immersion: the "first entry stutters"
        // report. Re-entries reuse them, which is why only the first one stuttered. While the small bubble floats (or
        // the next-world portal waits), each outer shell is shown for a couple of frames collapsed to a point at the scan
        // centre, then hidden again — one at a time, with gaps, and abandoned the moment the bubble is touched.

        public const int WarmFramesPerLayer = 2, WarmGapFrames = 3;
        public const float WarmCollapsedScale = 1e-4f;
        int m_WarmCursor = -1, m_WarmLayer = -1, m_WarmFrame, m_WarmGap;
        float m_WarmStartedAt;

        public static bool WarmupWindow(Phase phase, bool allLayersReady) =>
            allLayersReady && (phase == Phase.Floating || phase == Phase.Portal);

        /// <summary>True once every shell outside the mini world has been displayed once (or there are none).</summary>
        public bool OuterLayersWarm => m_AllLayersReady && m_WarmCursor >= m_Layers.Count;

        void StepOuterWarmup()
        {
            var window = WarmupWindow(m_Phase, m_AllLayersReady);
            if (m_WarmLayer >= 0)
            {
                // Touched mid-way: put the shell back as the reveal expects it (hidden, full size) and retry it next time.
                if (!window) { EndWarmLayer(false); return; }
                if (++m_WarmFrame >= WarmFramesPerLayer) EndWarmLayer(true);
                return;
            }
            if (!window || OuterLayersWarm) return;
            if (m_WarmCursor < m_MiniLayerCount) { m_WarmCursor = m_MiniLayerCount; m_WarmStartedAt = Time.realtimeSinceStartup; }
            if (m_WarmGap > 0) { m_WarmGap--; return; }
            if (m_WarmCursor >= m_Layers.Count) return;
            var layer = m_Layers[m_WarmCursor];
            if (layer == null || layer.activeSelf) { m_WarmCursor++; return; }   // already shown by someone else
            layer.transform.localScale = Vector3.one * WarmCollapsedScale;
            layer.SetActive(true);
            m_WarmLayer = m_WarmCursor; m_WarmFrame = 0;
        }

        void EndWarmLayer(bool done)
        {
            var layer = m_WarmLayer >= 0 && m_WarmLayer < m_Layers.Count ? m_Layers[m_WarmLayer] : null;
            if (layer != null) { layer.SetActive(false); layer.transform.localScale = Vector3.one; }
            m_WarmLayer = -1; m_WarmGap = WarmGapFrames;
            if (!done) return;
            m_WarmCursor++;
            if (m_WarmCursor >= m_Layers.Count)
                Debug.Log($"[Bubble World] {m_Folder}: {m_Layers.Count - m_MiniLayerCount} outer shells pre-displayed in {Time.realtimeSinceStartup - m_WarmStartedAt:0.00}s");
        }

        public static float LayerGrowScale(float t)
        {
            t = Mathf.Clamp01(t);
            var overshoot = Mathf.Sin(t * Mathf.PI) * .03f;
            return Mathf.Lerp(.9f, 1f, Ease(t)) + overshoot * (1f - t);
        }

        /// <summary>
        /// Where the scan centre ends up: at the viewer's head horizontally, and — when the head height above the floor
        /// is plausible — at floor + scanned eye height, so the scanned ground lies on the real floor.
        /// </summary>
        public static Vector3 FinalCenter(Vector3 head, float floorY, float scanEyeHeight, bool alignToFloor)
        {
            if (!alignToFloor) return head;
            var headAboveFloor = head.y - floorY;
            if (headAboveFloor < .9f || headAboveFloor > 2.1f || scanEyeHeight <= 0f) return head;
            return new Vector3(head.x, floorY + scanEyeHeight, head.z);
        }

        public static bool IsTouching(Vector3 fingertip, Vector3 center, float radius, float padding) =>
            (fingertip - center).sqrMagnitude <= (radius + padding) * (radius + padding);

        /// <summary>Yaw (degrees) that turns the scan's forward (+Z, the big tree) to face along <paramref name="gaze"/>.</summary>
        public static float GazeYaw(Vector3 gaze)
        {
            gaze.y = 0f;
            return gaze.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(gaze.x, gaze.z) * Mathf.Rad2Deg;
        }

        // ---------- Public API (CandyWorldManager) ----------

        public void ConfigureTouchSound(AudioClip clip) => m_TouchSound = clip;

        /// <summary>
        /// Arms the reward for a new world session. <paramref name="folder"/> is BubbleWorldData.NatureFolder or
        /// CandyFolder. Nothing is shown until progress reaches 100%.
        /// </summary>
        public void Begin(Camera viewer, string folder)
        {
            m_HasOuterEntry = false;
            m_Choice?.Hide();
            Begin(viewer, folder, 0);
        }

        /// <summary>
        /// The mood's pick for this session's first room entry (BubbleChoiceRules.Recommend), or null. Begin with the
        /// pick as the folder, so the recommended world is the real, already built bubble.
        /// </summary>
        public void ConfigureEntry(string recommendedFolder)
        {
            m_EntryPick = recommendedFolder;
            m_EntryPickUsed = false;
        }
        public BubbleEntryChoice EntryChoice => m_Choice;

        void Begin(Camera viewer, string folder, int natureWorldDepth)
        {
            ResetWorlds();
            m_Viewer = viewer;
            m_Folder = string.IsNullOrEmpty(folder) ? BubbleWorldData.NatureFolder : folder;
            if (natureWorldDepth == 0) m_OuterFolder = m_Folder;
            m_NatureWorldDepth = natureWorldDepth;
            if (!HasData(m_Folder))
            {
                Debug.LogWarning($"[Bubble World] Missing Resources/{BubbleWorldData.PointsResource(m_Folder)}.bytes; the bubble world reward is off for this world.");
                return;
            }
            m_Phase = Phase.Armed;
            // Start building while the user is still in the room. This moves mesh
            // deserialization and GPU setup away from the eventual bubble tap.
            m_BuildRoutine = StartCoroutine(Build());
        }

        public void SetProgress(float normalizedProgress)
        {
            // Progress alone never summons a bubble. Rings own the invitation.
        }

        /// <summary>Same as touching the bubble (look + pinch, or debug).</summary>
        public void Touch()
        {
            if (m_Phase == Phase.Doorway) { if (m_Door != null) m_Door.Press(); return; }
            if (m_Phase == Phase.Portal) EnterFromPortal();
            else if (m_Phase == Phase.Floating || m_Phase == Phase.Rising) m_TouchRequested = true;
        }

        public void StopAndClear()
        {
            if (m_SwitchRoutine != null) { StopCoroutine(m_SwitchRoutine); m_SwitchRoutine = null; }
            m_AutoEnter = false;
            m_KeepDoor = false; m_DoorSwitchTo = null;
            if (m_Choice != null) { BubblePlayVisuals.Release(m_Choice.gameObject); m_Choice = null; }
            ResetWorlds();
        }

        /// <summary>StopAndClear without the entry choice (a switch to the other world rebuilds while its bubble stays).</summary>
        void ResetWorlds()
        {
            if (m_Nested != null) { m_Nested.StopAndClear(); Destroy(m_Nested.gameObject); m_Nested = null; }
            ClearVisuals();
        }

        void ClearVisuals()
        {
            m_ExitingHandoff = false;
            if (m_Door != null && !m_KeepDoor) { BubblePlayVisuals.Release(m_Door.gameObject); m_Door = null; m_Switcher = null; }
            if (!m_KeepDoor) m_DoorMode = false;
            m_DoorWorldShown = false; m_Held = null; m_HeldVersion = -1; m_RevealOrder = null;
            m_NearSamples = null;
            m_Parked=m_PortalLocked=false;m_GuardScale=m_GuardAlpha=m_FollowScale=m_FollowAlpha=1;m_UnlockAt=m_PortalUnlockAt=-10;
            m_HomeHold.Reset();m_Hold.Reset();
            // The rings are registered in the world's sorting group: take their entries out before destroying them.
            if(m_HomeHoldRing!=null){Moodium.NatureWorld.BubblePlayVisuals.PruneSorting(m_WorldSorting,m_HomeHoldRing.transform);Destroy(m_HomeHoldRing.gameObject);}
            if(m_PortalHoldRing!=null){Moodium.NatureWorld.BubblePlayVisuals.PruneSorting(m_WorldSorting,m_PortalHoldRing.transform);Destroy(m_PortalHoldRing.gameObject);}
            m_HomeHoldRing=m_PortalHoldRing=null;
            m_Play=null;
            m_Motion = null;
            if (m_Routine != null) StopCoroutine(m_Routine);
            m_Routine = null;
            if (m_BuildRoutine != null) StopCoroutine(m_BuildRoutine);
            m_BuildRoutine = null;
            m_CoreLayersReady = false;
            m_AllLayersReady = false;
            m_WarmCursor = -1; m_WarmLayer = -1; m_WarmGap = 0;
            m_TouchRequested = false;
            m_Phase = Phase.Idle;
            m_Breath = 0f; m_BreathScale = 1f; m_WanderFade = 1f; m_WanderTarget = 1f;
            m_Wandering = false; m_InsideAt = 0f; m_BobPhase = 0f; m_Radius = 0f;
            m_From = m_Hover = m_Entry = m_HomeTarget = Vector3.zero;
            if (m_Root != null) m_Root.SetActive(false);
            if (m_Home != null) m_Home.SetActive(false);
            if (m_Home != null) Destroy(m_Home);
            m_Home = null;
            DropTags();
            m_Layers.Clear();
            m_LayerRestPositions = null;
            m_LayerRestRotations = null;
            if (m_Root != null) Destroy(m_Root);
            m_Root = null; m_WorldRoot = null; m_Membrane = null; m_MembraneCollider = null; m_Sky = null; m_SkyRenderer = null; m_Audio = null;
            m_WorldSorting = null; m_Wake = null;   // destroyed with m_Root
            foreach (var mesh in m_RuntimeMeshes) if (mesh != null) Destroy(mesh);
            m_RuntimeMeshes.Clear();
            DestroyMaterial(ref m_HomeMaterial);
            DestroyMaterial(ref m_PointMaterial);
            DestroyMaterial(ref m_SkyMaterial);
            DestroyMaterial(ref m_SkyFadeMaterial);
            DestroyMaterial(ref m_MembraneMaterial);
        }

        void OnDisable() => StopAndClear();

        public event Action<LeaveTarget> Left;
        public event Action HomeRequested;
        public event Action Wandered;
        Vector3 m_From, m_Hover, m_Entry, m_HomeTarget;
        Vector3 m_OuterEntryFrom, m_OuterEntryHover;
        float m_InsideAt, m_Breath, m_BreathScale = 1f, m_WanderFade = 1f;
        float m_WanderTarget = 1f;
        bool m_Wandering, m_HasOuterEntry;
        int m_NatureWorldDepth;
        GameObject m_Home;

        public static Vector3 SummonPosition(Vector3 from, Vector3 hover, float t) =>
            Vector3.Lerp(from, hover, Ease(t)) + Vector3.up * (Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * .12f);
        public static float LeaveRadius(float t) => EnvelopRadius(t, MiniWorldRadius, .15f);
        public static float LayerCollapseTime(int index, int count) => count <= 1 ? 0f : (count - 1 - index) / (float)(count - 1);
        public static bool WanderState(bool away, float distance) => away ? distance > .6f : distance > .8f;

        public void Summon(Vector3 from, Vector3 hover)
        {
            if (m_Phase != Phase.Armed) return;
            m_From = from; m_Hover = hover;
            if (m_NatureWorldDepth == 0)
            {
                m_OuterEntryFrom = from;
                m_OuterEntryHover = hover;
                m_HasOuterEntry = true;
            }
            m_Phase = Phase.Rising;
            m_Routine = StartCoroutine(Run());
        }

        /// <summary>Reopens the last room entry after returning from the bubble world.</summary>
        public bool ResummonAtLastEntry()
        {
            if (m_Phase != Phase.Armed || m_Viewer == null || !m_HasOuterEntry)
                return false;
            // The door stays in the room after the viewer walks back out (or while it swaps worlds): no second entry.
            if (m_DoorMode && m_Door != null) return false;
            var from = m_OuterEntryFrom;
            var hover = m_OuterEntryHover;
            // Returning from a completed bubble keeps its meshes/materials hidden.
            // Reuse that warmed cache instead of synchronously rebuilding it again.
            if (m_Root == null || m_Folder != m_OuterFolder || !m_AllLayersReady)
                Begin(m_Viewer, m_OuterFolder, 0);
            Summon(from, hover);
            return true;
        }
        public void Dismiss()
        {
            if (m_Phase != Phase.Floating && m_Phase != Phase.Rising) return;
            m_Choice?.Dismiss();
            if (m_Routine != null) StopCoroutine(m_Routine);
            m_Routine = StartCoroutine(Sink());
        }
        IEnumerator Sink()
        {
            m_Phase = Phase.Sinking;
            if (m_Root != null)
            {
                var start = m_Root.transform.position;
                var radius = m_Radius;
                for (float t = 0; t < .6f; t += Time.deltaTime)
                {
                    m_Root.transform.position = Vector3.Lerp(start, m_From, Ease(t / .6f));
                    SetRadius(Mathf.Lerp(radius, .001f, Ease(t / .6f)));
                    yield return null;
                }
                m_Root.SetActive(false);
            }
            m_TouchRequested = false; m_Phase = Phase.Armed; m_Routine = null;
        }
        public void SetBreath(float amount) => m_Breath = float.IsFinite(amount) ? Mathf.Clamp01(amount) : 0f;
        /// <summary>No fade or "come back" prompt when the viewer walks away from the centre (the portal probe walks them out through a door).</summary>
        public bool IgnoreWander { get; set; }
        public void SetWanderFade(float alpha) => m_WanderTarget = float.IsFinite(alpha) ? Mathf.Clamp(alpha, .25f, 1f) : 1f;
        public void Leave(LeaveTarget target)
        {
            if (m_Nested != null && m_Phase == Phase.HandingOff)
            {
                if (m_Nested.m_Phase == Phase.Leaving) return;
                m_ExitingHandoff = true;
                m_Routine = StartCoroutine(FadeOuterOnExit());
                m_Nested.Leave(target);
                return;
            }
            if (m_Phase != Phase.Inside && m_Phase != Phase.Approach && m_Phase != Phase.Envelop && m_Phase != Phase.Reveal && m_Phase != Phase.Doorway) return;
            if (m_Routine != null) StopCoroutine(m_Routine);
            CloseDoor();
            m_Phase = Phase.Leaving;
            if (m_Motion != null) m_Motion.SetStage(BubbleSplatMotion.Stage.Leaving);
            m_Routine = StartCoroutine(Return(target));
        }
        IEnumerator Return(LeaveTarget target)
        {
            if (m_Nested != null) { m_Nested.StopAndClear(); Destroy(m_Nested.gameObject); m_Nested = null; }
            if (m_Home != null) m_Home.SetActive(false);
            DropTags();
            m_Breath = 0f; m_BreathScale = 1f;
            m_WorldRoot.localScale = Vector3.one * (m_Radius / MiniWorldRadius);
            m_Sky.localScale = Vector3.one * SkyMaxRadius;
            m_SkyRenderer.sharedMaterial = m_SkyFadeMaterial;
            for (float t = 0; t < 1f; t += Time.deltaTime)
            { BubbleWorldData.SetAlpha(m_SkyFadeMaterial, (1f - Ease(t)) * m_WanderFade); yield return null; }
            m_Sky.gameObject.SetActive(false);
            for (float t = 0; t < 2f; t += Time.deltaTime)
            {
                for (int i = m_MiniLayerCount; i < m_Layers.Count; i++)
                    if (t / 2f >= LayerCollapseTime(i - m_MiniLayerCount, m_Layers.Count - m_MiniLayerCount)) m_Layers[i].SetActive(false);
                yield return null;
            }
            for (int i = m_MiniLayerCount; i < m_Layers.Count; i++) m_Layers[i].SetActive(false);
            if (m_DoorMode)
            {
                // Door entry: no bubble flies back; the world fades where it stands and the door has already closed.
                m_DoorMode = false;
                m_Root.SetActive(false); m_TouchRequested = false;
                m_Phase = Phase.Armed; m_Routine = null;
                Left?.Invoke(target);
                yield break;
            }
            var start = m_Root.transform.position;
            var viewer = ResolveViewer();
            var destination = m_From + Vector3.up * .3f;
            if (!float.IsFinite(destination.x) || !float.IsFinite(destination.y) || !float.IsFinite(destination.z))
                destination = viewer != null ? viewer.transform.position + Quaternion.Euler(0, GazeYaw(viewer.transform.forward), 0) * Vector3.forward * .5f : start;
            m_Membrane.gameObject.SetActive(true);
            for (float t = 0; t < 1.2f; t += Time.deltaTime)
            {
                m_Root.transform.position = Vector3.Lerp(start, destination, Ease(t / 1.2f));
                SetRadius(LeaveRadius(t / 1.2f)); yield return null;
            }
            if (m_TouchSound != null && m_Audio != null) m_Audio.PlayOneShot(m_TouchSound);
            for (float t = 0; t < .12f; t += Time.deltaTime)
            {
                SetRadius(.15f * Mathf.Lerp(1f, 1.25f, t / .12f));
                BubbleWorldData.SetAlpha(m_MembraneMaterial, m_MembraneAlpha * (1f - t / .12f));
                yield return null;
            }
            m_Root.SetActive(false); m_TouchRequested = false;
            m_Phase = Phase.Armed; m_Routine = null;
            Left?.Invoke(target);
            // Return() dropped the next layer; build it again in the room so a re-entry shows its bubble at once.
            if (m_PrepareNestedEarly && m_Root != null) PrepareNested();
        }
        void ResetCached()
        {
            m_Root.SetActive(true); m_Membrane.gameObject.SetActive(true); m_MembraneCollider.enabled = true;
            m_Sky.gameObject.SetActive(true); m_Sky.localScale = Vector3.one * MiniWorldRadius;
            m_SkyRenderer.sharedMaterial = m_SkyMaterial;
            m_BreathScale = 1f; m_Breath = 0; m_WanderFade = 1f; m_WanderTarget = 1f; m_Wandering = false;
            m_TouchRequested = false; m_BobPhase = 0f;
            if (m_Home != null) m_Home.SetActive(false);
            RestoreQuietLayerPose();
            BubbleWorldData.SetAlpha(m_PointMaterial, 1f);
            foreach (var layer in m_Layers) { layer.SetActive(m_Layers.IndexOf(layer) < m_MiniLayerCount); layer.transform.localScale = Vector3.one; }
        }

        void CacheQuietLayerPose()
        {
            m_LayerRestPositions = new Vector3[m_Layers.Count];
            m_LayerRestRotations = new Quaternion[m_Layers.Count];
            for (var i = 0; i < m_Layers.Count; i++)
            {
                m_LayerRestPositions[i] = m_Layers[i].transform.localPosition;
                m_LayerRestRotations[i] = m_Layers[i].transform.localRotation;
            }
        }

        void RestoreQuietLayerPose()
        {
            if (m_LayerRestPositions == null || m_LayerRestRotations == null) return;
            var count = Mathf.Min(m_Layers.Count, m_LayerRestPositions.Length);
            for (var i = 0; i < count; i++)
            {
                m_Layers[i].transform.localPosition = m_LayerRestPositions[i];
                m_Layers[i].transform.localRotation = m_LayerRestRotations[i];
            }
        }

        public static BubbleSplatMotion.Stage MotionStage(Phase phase)
        {
            switch (phase)
            {
                case Phase.Inside: case Phase.Doorway: return BubbleSplatMotion.Stage.Inside;
                case Phase.HandingOff: return BubbleSplatMotion.Stage.HandingOff;
                case Phase.Leaving: return BubbleSplatMotion.Stage.Leaving;
                case Phase.Idle: case Phase.Armed: case Phase.Done: return BubbleSplatMotion.Stage.Dormant;
                default: return BubbleSplatMotion.Stage.Miniature;
            }
        }
        void Update()
        {
            if (m_Motion != null) m_Motion.SetStage(MotionStage(m_Phase));
            if (m_Wake != null) m_Wake.Active = m_Phase == Phase.Inside && m_WanderFade > .6f;
            if(m_Play!=null)m_Play.SetInside(m_Phase==Phase.Inside);
            CheckEditorMouse();
            StepOuterWarmup();
            if (m_DoorMode && m_Door != null) StepDoor();
            if (m_Phase == Phase.Portal)
            {
                m_WorldRoot.Rotate(Vector3.up, m_SpinDegreesPerSecond * Time.deltaTime, Space.Self);
                var touchingPortal = FingertipTouching(m_Root.transform.position, m_Radius);
                if(PortalLocked)
                {
                    if(m_Hold.Update(touchingPortal,Time.deltaTime)){Debug.Log("[Bubble Play] locked bubble hold 0.60s → enter");EnterFromPortal(true);}
                    UpdateHoldRing(false,m_Phase==Phase.Portal?m_Hold.Progress:0);return;
                }
                m_Hold.Reset();UpdateHoldRing(false,0);
                if(TouchAfterGrace(touchingPortal,Time.time,m_PortalUnlockAt,ref m_PortalArmed))EnterFromPortal();
                return;
            }
            if (m_Phase == Phase.HandingOff)
            {
                if (!m_ExitingHandoff && m_Nested != null && m_PointMaterial != null)
                    BubbleWorldData.SetAlpha(m_PointMaterial, OuterAlpha(m_Nested.m_Radius, m_OuterFadeStartRadius, m_OuterFadeEndRadius));
                if (m_Nested != null && m_Nested.m_Phase == Phase.Reveal && m_Sky != null)
                    m_Sky.gameObject.SetActive(false);
                return;
            }
            if (m_Phase != Phase.Inside) return;
            // Keep scan frames fixed; play interactions use independent actors and layer tint swaps.
            var viewer = ResolveViewer(); if (viewer == null) return;
            var offset = viewer.transform.position - m_Entry; offset.y = 0;
            // Through the door the viewer may walk back to it (it shows the room): no fade or "come back" then.
            var away = !IgnoreWander && m_Door == null && WanderState(m_Wandering, offset.magnitude);
            if (away && !m_Wandering) Wandered?.Invoke();
            m_Wandering = away;
            m_WanderFade = Mathf.MoveTowards(m_WanderFade, Mathf.Min(m_WanderTarget, away ? .25f : 1f), Time.deltaTime * .75f / (away ? .4f : .6f));
            BubbleWorldData.SetAlpha(m_PointMaterial, m_WanderFade);
            m_SkyRenderer.sharedMaterial = m_SkyFadeMaterial;
            BubbleWorldData.SetAlpha(m_SkyFadeMaterial, m_WanderFade);
            var breath = m_SqueezeBreath ? Mathf.Lerp(1f, 1.015f, m_Breath) : 1f;
            m_BreathScale = Mathf.MoveTowards(m_BreathScale, breath, Time.deltaTime * .015f / (breath > m_BreathScale ? .4f : 1.2f));
            m_WorldRoot.localScale = Vector3.one * m_BreathScale;
            m_Sky.localScale = Vector3.one * SkyScaleForBreath(m_BreathScale);
            if (Time.time - m_InsideAt < m_NextBubbleDelaySeconds) return;
            // First layer: home (left) + next layer (right), following as one pair. Last layer: home only.
            if (m_Nested != null && !m_Nested.m_CoreLayersReady) return;
            if (m_PairShownAt < 0f) { m_PairShownAt = Time.time; m_HomeTarget = PortalPosition(viewer); }
            bool locked = UpdateFollowTarget(viewer);
            var pair = BubbleChoiceRules.Pair(m_HomeTarget, viewer.transform.forward);
            var homeAt = m_Nested != null ? pair.home : m_HomeTarget;
            if (m_Nested != null)
            {
                if (m_Nested.m_Phase == Phase.Armed) m_Nested.ShowPortal(pair.next, BubbleChoiceRules.PortalRadius);
                m_Nested.SetPortalGuard(locked, locked ? ChimeFocus.ParkScale : 1, locked ? ChimeFocus.ParkAlpha : 1);
                m_Nested.SetPortalTarget(pair.next);
            }
            UpdateHome(viewer, locked, homeAt);
            UpdateTags(viewer, locked);
        }

        void UpdateHome(Camera viewer, bool locked, Vector3 target)
        {
            // Home is reached through the door; no clickable home bubble remains.
            if (m_Home != null) m_Home.SetActive(false);
            m_HomeTag?.Hide();
            m_HomeHold.Reset();
            UpdateHoldRing(true, 0f);
        }

        /// <summary>「回家」 under the home bubble and 「下一层 · 〈world〉」 under the next-layer bubble: shown for a few seconds after they appear and whenever a fingertip comes close.</summary>
        void UpdateTags(Camera viewer, bool locked)
        {
            if (m_TagHost == null) { m_TagHost = new GameObject("Bubble Tags"); m_TagHost.transform.SetParent(transform, false); }
            if (m_HomeTag == null) { m_HomeTag = new BubbleTag(m_TagHost.transform, "Home Tag"); m_HomeTag.SetSprite(BubbleChoiceRules.HomeLabel(Moodium.Characters.MoodiVoice.LanguageCode == "en")); }
            var eye = viewer.transform.position;
            var since = Time.time - m_PairShownAt;
            var fade = m_FollowAlpha * (locked ? 0f : 1f);
            if (m_Home != null && m_Home.activeSelf)
            {
                var p = m_Home.transform.position;
                var show = BubbleChoiceRules.TagVisible(since, NearestTipDistance(p)) ? fade : 0f;
                m_HomeTag.Place(p - Vector3.up * (.08f * m_FollowScale + BubbleChoiceRules.LabelGap), eye, show, BubbleChoiceRules.TagFade, Time.deltaTime);
            }
            if (m_Nested == null || m_Nested.m_Phase != Phase.Portal || m_Nested.m_Root == null) { m_NextTag?.Hide(); return; }
            if (m_NextTag == null) { m_NextTag = new BubbleTag(m_TagHost.transform, "Next Tag"); m_NextTag.SetSprite(BubbleChoiceRules.NextLabel(m_Nested.m_Folder, Moodium.Characters.MoodiVoice.LanguageCode == "en")); }
            var q = m_Nested.m_Root.transform.position;
            var nextShow = BubbleChoiceRules.TagVisible(since, NearestTipDistance(q)) ? fade : 0f;
            m_NextTag.Place(q - Vector3.up * (m_Nested.m_Radius + BubbleChoiceRules.LabelGap), eye, nextShow, BubbleChoiceRules.TagFade, Time.deltaTime);
        }

        void DropTags()
        {
            if (m_TagHost != null) BubblePlayVisuals.Release(m_TagHost);
            m_TagHost = null; m_HomeTag = m_NextTag = null; m_PairShownAt = -1f;
        }

        public static float SkyScaleForBreath(float breathScale) => SkyMaxRadius / Mathf.Max(.001f, breathScale);

        public const int SkySortOrder = 0, PointLayerSortOrder = 1, HandWakeSortOrder = 2;

        /// <summary>Texture for the sky sphere. Soft falls back to the photo (with a warning) if panorama_soft is missing.</summary>
        public static Texture2D SkyTexture(string folder, SkyStyle style, Texture2D panorama)
        {
            if (style != SkyStyle.Soft) return panorama;
            var soft = Resources.Load<Texture2D>(BubbleWorldData.SoftPanoramaResource(folder));
            if (soft != null) return soft;
            Debug.LogWarning($"[Bubble World] Missing Resources/{BubbleWorldData.SoftPanoramaResource(folder)}; using the photo panorama.");
            return panorama;
        }

        public static void ConfigureTransparentPoints(Material material)
        {
            if (material == null) return;
            Moodium.Opening.OpeningVideoSurfaceSettings.TransparentAlpha.ApplyTo(material);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        void CheckEditorMouse()
        {
#if UNITY_EDITOR
            Vector2 position;
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            position = mouse.position.ReadValue();
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (!Input.GetMouseButtonDown(0)) return;
            position = Input.mousePosition;
#else
            return;
#endif
#if ENABLE_INPUT_SYSTEM || ENABLE_LEGACY_INPUT_MANAGER
            var viewer = ResolveViewer();
            if (viewer == null) return;
            var ray = viewer.ScreenPointToRay(position);
            if (m_Switcher != null && m_Switcher.TryPick(ray)) return;
            var collider = m_Phase == Phase.Doorway && m_Door != null ? m_Door.Collider :
                m_Phase == Phase.Floating || m_Phase == Phase.Rising || m_Phase == Phase.Portal ? m_MembraneCollider :
                m_Phase == Phase.Inside && m_Home != null && m_Home.activeInHierarchy ? m_Home.GetComponent<Collider>() : null;
            if (m_Choice != null && m_Choice.Visible && m_Choice.PartnerBubble != null &&
                m_Choice.PartnerBubble.GetComponentInChildren<Collider>() is { } partnerCollider && partnerCollider.Raycast(ray, out _, 100f))
            { m_Choice.ChoosePartner(); return; }
            if (collider == null || !collider.enabled || !collider.Raycast(ray, out _, 100f)) return;
            if (m_Phase == Phase.Floating || m_Phase == Phase.Rising || m_Phase == Phase.Portal || m_Phase == Phase.Doorway) Touch(); else RequestHome();
#endif
#endif
        }
        void RequestHome(bool force=false)
        {
            if(ChimeLocked&&!force)return;
            if(!force&&Time.time-m_UnlockAt<ChimeFocus.UnlockGraceSeconds)return;
            if (m_Phase != Phase.Inside) return;
            // Door entry: home is back out through the door; it passes over the viewer and the world waits behind it.
            if (m_DoorMode && m_Door != null) { m_Door.RequestGlideBack(); return; }
            HomeRequested?.Invoke();
            Leave(LeaveTarget.Room);
        }

        static float FollowBlend(bool locked)=>1f-Mathf.Exp(-Time.deltaTime*4.6f/(locked?ChimeFocus.SlideSeconds:ChimeFocus.ReturnSeconds));
        bool UpdateFollowTarget(Camera viewer)
        {
            bool focus=ChimeLocked;var eye=viewer.transform.position;
            if(focus)
            {
                if(!m_Parked||Vector3.Angle(viewer.transform.forward,m_ParkTarget-eye)>ChimeFocus.ReparkAngle)
                {m_ParkTarget=ChimeFocus.ParkPosition(eye,GazeYaw(viewer.transform.forward),m_Play.WriterHand);Debug.Log($"[Bubble Play] follow bubble parked at yaw={(m_Play.WriterHand==0?50:-50)} locked");}
                m_Parked=true;m_HomeTarget=m_ParkTarget;
            }
            else
            {
                if(m_Parked){m_Parked=false;m_UnlockAt=Time.time;m_HomeArmed=false;m_HomeTarget=PortalPosition(viewer);}
                if(Vector3.Angle(viewer.transform.forward,m_HomeTarget-eye)>60)m_HomeTarget=PortalPosition(viewer);
            }
            if(m_Play!=null&&m_Play.TryChimeKeepOut(out var keep))m_HomeTarget=ChimeFocus.AvoidKeepOut(eye,m_HomeTarget,keep,ChimeFocus.KeepOutRadius,.08f*(focus?ChimeFocus.ParkScale:1));
            return focus;
        }
        public void SetPortalGuard(bool locked,float scale,float alpha)
        {
            if(m_PortalLocked&&!locked){m_PortalUnlockAt=Time.time;m_PortalArmed=false;m_Hold.Reset();}
            m_PortalLocked=locked;m_GuardScale=scale;m_GuardAlpha=alpha;
            if(m_Phase!=Phase.Portal)return;
            float blend=FollowBlend(locked);m_FollowScale=Mathf.Lerp(m_FollowScale,m_GuardScale,blend);m_FollowAlpha=Mathf.Lerp(m_FollowAlpha,m_GuardAlpha,blend);
            SetRadius(m_PortalRadius*m_FollowScale);BubbleWorldData.SetAlpha(m_MembraneMaterial,m_MembraneAlpha*m_FollowAlpha);
            if(m_MembraneCollider!=null)m_MembraneCollider.enabled=!locked;
        }
        void UpdateHoldRing(bool home,float progress)
        {
            var viewer=ResolveViewer();var centre=home?m_Home:m_Root;if(centre==null)return;
            var ring=home?m_HomeHoldRing:m_PortalHoldRing;
            if(ring==null&&progress>0){ring=BubbleHoldRing.Create(transform,BubbleWorldData.IsCandy(m_Folder),m_WorldSorting);if(home)m_HomeHoldRing=ring;else m_PortalHoldRing=ring;}
            if(ring!=null)ring.Show(progress,centre.transform.position,home?.08f*m_FollowScale:m_Radius,viewer);
        }

        static Vector3 PortalPosition(Camera viewer) => viewer.transform.position +
            Quaternion.Euler(0, GazeYaw(viewer.transform.forward), 0) * Vector3.forward * .6f - Vector3.up * .25f;

        void PrepareNested()
        {
            if (!m_NextLayer || m_NatureWorldDepth != 0 || m_Nested != null) return;
            // The next layer is the other world of the family, whichever one the user entered first.
            var folder = BubbleChoiceRules.Partner(m_Folder);
            if (folder == null || !HasData(folder)) return;
            var host = new GameObject("Nested Bubble Controller");
            host.transform.SetParent(transform, false);
            m_Nested = host.AddComponent<BubbleWorldController>();
            m_Nested.ConfigureTouchSound(m_TouchSound);
            m_Nested.m_NestedSkyScale = m_NestedSkyScale;
            m_Nested.m_SkyStyle = m_SkyStyle;
            m_Nested.m_FixedSkyOrder = m_FixedSkyOrder;
            m_Nested.m_HandWake = m_HandWake;
            m_Nested.Begin(m_Viewer, folder, 1);
            m_Nested.HomeRequested += () => HomeRequested?.Invoke();
            m_Nested.Wandered += () => Wandered?.Invoke();
            m_Nested.Left += target =>
            {
                ClearVisuals();
                if (m_Nested != null) { m_Nested.StopAndClear(); Destroy(m_Nested.gameObject); m_Nested = null; }
                m_Phase = Phase.Armed;
                Left?.Invoke(target);
            };
            m_Nested.Touched += () =>
            {
                CloseDoor();
                if (m_Home != null) m_Home.SetActive(false);
                m_HomeTag?.Hide(); m_NextTag?.Hide();
                m_Phase = Phase.HandingOff;
                if (m_Motion != null) m_Motion.SetStage(BubbleSplatMotion.Stage.HandingOff);
            };
            m_Nested.Revealed += () => { ClearVisuals(); m_Phase = Phase.HandingOff; };
            m_Nested.LayerRevealed += folder => LayerRevealed?.Invoke(folder);
        }

        public void ShowPortal(Vector3 position, float radius)
        {
            if (m_Phase != Phase.Armed || !m_CoreLayersReady) return;
            ResetCached();
            m_Root.transform.SetPositionAndRotation(position, Quaternion.identity);
            m_PortalRadius = radius;
            SetRadius(radius);
            m_PortalArmed = false;
            m_Phase = Phase.Portal;
        }

        public void SetPortalTarget(Vector3 position)
        {
            if (m_Phase == Phase.Portal)
                m_Root.transform.position = Vector3.Lerp(m_Root.transform.position, position, FollowBlend(m_PortalLocked));
        }

        public void EnterFromPortal(bool force=false)
        {
            if(PortalLocked&&!force)return;
            if(!force&&Time.time-m_PortalUnlockAt<ChimeFocus.UnlockGraceSeconds)return;
            if (m_Phase != Phase.Portal) return;
            UpdateHoldRing(false,0);
            m_FromPortal = true;
            var outer = transform.parent != null ? transform.parent.GetComponent<BubbleWorldController>() : null;
            if (outer != null) m_From = outer.m_OuterEntryFrom;
            m_Phase = Phase.Approach;
            m_MembraneCollider.enabled = false;
            Touched?.Invoke();
            if (Application.isPlaying)
                Moodium.CandyWorld.CandyWorldManager.SpawnSoftSqueezeFeedback(m_Root.transform.position);
            if (m_TouchSound != null) m_Audio.PlayOneShot(m_TouchSound);
            m_Routine = StartCoroutine(ApproachAndReveal(ResolveViewer()));
        }

        IEnumerator FadeOuterOnExit()
        {
            for (float t = 0; t < .4f; t += Time.deltaTime)
            {
                BubbleWorldData.SetAlpha(m_PointMaterial, 1f - t / .4f);
                if (m_SkyRenderer != null) m_SkyRenderer.sharedMaterial = m_SkyFadeMaterial;
                BubbleWorldData.SetAlpha(m_SkyFadeMaterial, 1f - t / .4f);
                yield return null;
            }
            ClearVisuals();
            m_Phase = Phase.HandingOff;
        }

        // ---------- Sequence ----------

        bool m_TouchRequested;

        IEnumerator Run()
        {
            m_Phase = Phase.Rising;
            var viewer = ResolveViewer();
            if (viewer == null)
            {
                Debug.LogWarning("[Bubble World] No camera; cannot show the bubble.");
                m_Phase = Phase.Idle;
                yield break;
            }

            while (!m_CoreLayersReady && m_BuildRoutine != null)
                yield return null;
            if (m_Root != null) ResetCached();
            if (m_Root == null)
            {
                m_Phase = Phase.Idle;
                yield break;
            }

            // Door entry: no bubble; a droplet flies out and opens into the door. Without the occlusion material the
            // bubble entry below carries on.
            if (DoorEligible)
            {
                m_DoorFellBack = false;
                BeginDoor(viewer);
                if (!m_DoorFellBack) { m_Routine = null; yield break; }
            }

            // Appear: pop in at the lower left of the gaze.
            var head = viewer.transform.position;
            var yaw = Quaternion.Euler(0f, GazeYaw(viewer.transform.forward), 0f);
            var home = m_Hover;
            m_Root.transform.SetPositionAndRotation(home, Quaternion.identity);
            m_WorldRoot.localRotation = yaw;
            // Room entry: offer the other world of the family beside this one. The mood's pick is bigger, closer and
            // ringed until the user has entered once this session (walking away keeps it). After a switch the chosen
            // world enters straight away.
            var autoEnter = m_AutoEnter;
            var pick = m_EntryPickUsed ? null : m_EntryPick;
            var radius = autoEnter ? m_SwitchRadius : BubbleChoiceRules.EntryRadius(m_BubbleRadius, pick, m_Folder);
            var partner = BubbleChoiceRules.Partner(m_Folder);
            if (!autoEnter && m_NatureWorldDepth == 0 && partner != null && HasData(partner))
            {
                if (m_Choice == null) m_Choice = BubbleEntryChoice.Create();
                m_Choice.Show(viewer, m_Folder, pick, m_From, home, m_BubbleRadius, () => m_Root != null ? m_Root.transform.position : home,
                    () => m_Radius, (c, r) => FingertipTouching(c, r), BubbleWorldData.IsCandy(m_Folder) ? m_CandyFilmTint : m_NatureFilmTint, m_MembraneAlpha);
            }
            for (var t = 0f; t < .8f; t += Time.deltaTime)
            {
                m_Root.transform.position = SummonPosition(m_From, home, t / .8f);
                SetRadius(radius * Ease(t / .8f));
                yield return null;
            }
            SetRadius(radius);
            if (autoEnter) m_Choice?.Hide();

            m_Root.transform.position = home;
            m_Phase = Phase.Floating;
            Appeared?.Invoke();

            // Float until touched (or until the other world's bubble is chosen).
            var spin = 0f;
            m_AutoEnter = false;
            while (!autoEnter && !m_TouchRequested && !FingertipTouching(m_Root.transform.position, m_Radius))
            {
                if (m_Choice != null && m_Choice.Visible && m_Choice.PartnerTouched)
                {
                    m_Routine = null;
                    m_SwitchRoutine = StartCoroutine(SwitchToPartner());
                    yield break;
                }
                m_BobPhase += Time.deltaTime / m_BobPeriod * Mathf.PI * 2f;
                m_Root.transform.position = home + Vector3.up * (Mathf.Sin(m_BobPhase) * m_BobAmplitude);
                spin += m_SpinDegreesPerSecond * Time.deltaTime;
                m_WorldRoot.localRotation = yaw * Quaternion.Euler(0f, spin, 0f);
                yield return null;
            }

            // Touch: wobble, sparkle, chime. The other world's bubble sinks back.
            m_EntryPickUsed = true;
            m_Choice?.Dismiss();
            m_Phase = Phase.Approach;
            if (m_MembraneCollider != null) m_MembraneCollider.enabled = false; // never swallow other pinches while it grows
            Touched?.Invoke();
            // Let room point-cloud deactivation reach the renderer before the
            // bubble starts its growth and feedback work.
            yield return null;
            Moodium.CandyWorld.CandyWorldManager.SpawnSoftSqueezeFeedback(m_Root.transform.position);
            if (m_TouchSound != null && m_Audio != null) m_Audio.PlayOneShot(m_TouchSound);
            for (var t = 0f; t < .3f; t += Time.deltaTime)
            {
                var wobble = 1f - Mathf.Sin(t / .3f * Mathf.PI * 2f) * Mathf.Exp(-t * 9f) * .12f;
                SetRadius(radius * wobble);
                yield return null;
            }

            m_FromPortal = false;
            yield return ApproachAndReveal(viewer);
        }

        /// <summary>
        /// The other world's bubble was chosen in the room: this bubble sinks back, the controller rebuilds with that
        /// world as the first layer (its next layer is then the world it just left), and the new real bubble appears
        /// where the chosen one floats and enters at once. The chosen bubble stays, gently pulsing, until then.
        /// </summary>
        IEnumerator SwitchToPartner()
        {
            var partner = BubbleChoiceRules.Partner(m_Folder);
            var at = m_Choice.PartnerCentre;
            var radius = m_Choice.PartnerRadius;
            var entryFrom = m_OuterEntryFrom; var entryHover = m_OuterEntryHover; var hadEntry = m_HasOuterEntry;
            Debug.Log($"[Bubble Choice] chose {BubbleChoiceRules.Key(partner)}: rebuilding it as the first layer");
            m_Choice.BeginLoading();
            m_EntryPickUsed = true;
            m_Phase = Phase.Sinking;
            if (m_MembraneCollider != null) m_MembraneCollider.enabled = false;
            if (m_Root != null)
            {
                var start = m_Root.transform.position; var r0 = m_Radius;
                for (float t = 0; t < .6f; t += Time.deltaTime)
                {
                    m_Root.transform.position = Vector3.Lerp(start, m_From, Ease(t / .6f));
                    SetRadius(Mathf.Lerp(r0, .001f, Ease(t / .6f)));
                    yield return null;
                }
            }
            var started = Time.realtimeSinceStartup;
            Begin(m_Viewer, partner, 0);
            while (!m_CoreLayersReady && m_BuildRoutine != null) yield return null;
            Debug.Log($"[Bubble Choice] {BubbleChoiceRules.Key(partner)} core ready in {Time.realtimeSinceStartup - started:0.00}s");
            m_SwitchRoutine = null;
            if (!m_CoreLayersReady) { m_Choice?.Hide(); yield break; }
            m_AutoEnter = true;
            m_SwitchRadius = radius;
            Summon(at, at);
            // Summon recorded the chosen bubble's spot as the room entry: keep the original one for re-entries.
            if (hadEntry) { m_OuterEntryFrom = entryFrom; m_OuterEntryHover = entryHover; m_HasOuterEntry = true; }
        }

        IEnumerator ApproachAndReveal(Camera viewer)
        {
            var startRadius = m_Radius;
            // Approach starts from the actual invitation pose and radius.
            var startPos = m_Root.transform.position;
            var startRot = m_WorldRoot.localRotation;
            for (var t = 0f; t < m_ApproachSeconds; t += Time.deltaTime)
            {
                var u = t / m_ApproachSeconds;
                var target = Target(viewer);
                m_Root.transform.position = Vector3.Lerp(startPos, target.center, Ease(u));
                m_WorldRoot.localRotation = Quaternion.Slerp(startRot, target.yaw, Ease(u));
                SetRadius(ApproachRadius(u, startRadius, m_ApproachRadius));
                yield return null;
            }

            // Envelop: swell past the viewer to the size of the mini world. Lock the final pose now so walking or
            // turning does not drag the world along afterwards.
            m_Phase = Phase.Envelop;
            m_Entry = viewer.transform.position;
            var final = Target(viewer);
            m_Root.transform.position = final.center;
            m_WorldRoot.localRotation = final.yaw;
            // Both layers: the small bubble's sky fades out while it swells past the viewer and the sky only comes
            // back last, far away, after the points (below). The second layer used to fade its full sky in here,
            // before any of its points had spread out, so its panorama was the first and biggest thing in view.
            m_SkyRenderer.sharedMaterial = m_SkyFadeMaterial;
            for (var t = 0f; t < m_EnvelopSeconds; t += Time.deltaTime)
            {
                SetRadius(EnvelopRadius(t / m_EnvelopSeconds, m_ApproachRadius, MiniWorldRadius));
                var skyAlpha = SkyAlphaWhileEnveloping(m_Radius);
                BubbleWorldData.SetAlpha(m_SkyFadeMaterial, skyAlpha);
                if (skyAlpha <= 0f && m_Sky.gameObject.activeSelf) m_Sky.gameObject.SetActive(false);
                yield return null;
            }
            SetRadius(MiniWorldRadius);
            m_Membrane.gameObject.SetActive(false);
            m_Sky.gameObject.SetActive(false);

            // The visible mini world was ready immediately; wait here only when a
            // user has touched unusually quickly, so the full reveal never hitches.
            while (!m_AllLayersReady && m_BuildRoutine != null)
                yield return null;
            if (!m_AllLayersReady)
            {
                m_Phase = Phase.Idle;
                yield break;
            }

            // Reveal: the rest of the scan spreads out shell by shell, the sky receding just behind it.
            m_Phase = Phase.Reveal;
            var remaining = m_Layers.Count - m_MiniLayerCount;
            var started = new float[m_Layers.Count];
            for (var i = 0; i < started.Length; i++) started[i] = -1f;
            var elapsed = 0f;
            var finished = remaining <= 0;
            var worstFrame = 0f;
            Debug.Log($"[Bubble World] {m_Folder}: reveal starts, outer shells pre-displayed={OuterLayersWarm}");
            while (!finished)
            {
                elapsed += Time.deltaTime;
                worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime);
                finished = true;
                for (var j = 0; j < remaining; j++)
                {
                    var index = m_MiniLayerCount + j;
                    var layer = m_Layers[index];
                    if (started[index] < 0f)
                    {
                        if (elapsed < LayerRevealTime(j, remaining) * m_RevealSeconds) { finished = false; continue; }
                        started[index] = elapsed;
                        if (layer != null) layer.SetActive(true);
                        Sparkle(m_LayerRadii[index]);
                    }
                    var grow = (elapsed - started[index]) / m_LayerGrowSeconds;
                    if (layer != null) layer.transform.localScale = Vector3.one * LayerGrowScale(grow);
                    if (grow < 1f) finished = false;
                }
                yield return null;
            }

            Debug.Log($"[Bubble World] {m_Folder}: reveal done, worst frame {worstFrame * 1000f:0}ms");

            // Sky: fades in far away and closes the last gaps; then back to the opaque material. A nested world's sky
            // sits just inside the outer world's (which is hidden during this reveal) and fades in the same way.
            m_Sky.localScale = Vector3.one * (m_FromPortal ? SkyMaxRadius * m_NestedSkyScale : SkyMaxRadius);
            m_SkyRenderer.sharedMaterial = m_SkyFadeMaterial;
            BubbleWorldData.SetAlpha(m_SkyFadeMaterial, 0f);
            m_Sky.gameObject.SetActive(true);
            for (var t = 0f; t < m_SkyFadeSeconds; t += Time.deltaTime)
            {
                BubbleWorldData.SetAlpha(m_SkyFadeMaterial, Ease(t / m_SkyFadeSeconds));
                yield return null;
            }
            m_SkyRenderer.sharedMaterial = m_SkyMaterial;
            m_Phase = Phase.Inside;
            if (m_Motion != null) m_Motion.SetStage(BubbleSplatMotion.Stage.Inside);
            m_InsideAt = Time.time;
            if (m_Home != null) { Destroy(m_Home); m_Home = null; }
            m_Routine = null;
            Revealed?.Invoke();
            LayerRevealed?.Invoke(m_Folder);
            PrepareNested();
        }

        // ---------- Door entry ----------

        /// <summary>The room entry of the first layer (not the next layer's bubble, which opens inside a world).</summary>
        bool DoorEligible => m_DoorEntry && m_NatureWorldDepth == 0 && !m_FromPortal &&
                             (transform.parent == null || transform.parent.GetComponent<BubbleWorldController>() == null);

        /// <summary>
        /// Opens the door session: a droplet flies from where the entry was summoned to a spot in front of the viewer and
        /// tears open (once every layer is loaded) into the mercury door, the world at full size behind it. From here
        /// <see cref="StepDoor"/> runs it every frame. Sets <see cref="m_DoorFellBack"/> without the occlusion material.
        /// </summary>
        void BeginDoor(Camera viewer)
        {
            var head = viewer.transform.position;
            var yaw = Quaternion.Euler(0f, GazeYaw(viewer.transform.forward), 0f);
            var floor = ResolveOrigin() != null ? m_Origin.Origin.transform.position.y : 0f;
            // The same ground the world stands on (FinalCenter keeps the head height when the floor looks wrong).
            var ground = FinalCenter(head, floor, m_EyeHeight, m_AlignGroundToFloor).y - m_EyeHeight;
            var from = m_From != Vector3.zero && float.IsFinite(m_From.x) ? m_From : head + yaw * new Vector3(-.3f, -.5f, .5f);
            m_Door = BubbleDoor.Create(transform, head, yaw, ground, viewer.transform, HandTip, null, from);
            if (m_Door == null) { m_DoorFellBack = true; return; }
            m_Door.Driven = true;
            // Closed, the droplet waits where the entry appeared: beside the sensory bag.
            m_Door.SetPark(m_Hover != Vector3.zero && float.IsFinite(m_Hover.x) ? m_Hover : from);
            m_Door.SetHalo(m_DoorHalo);
            m_Door.WentThrough += OnDoorThrough;
            var worlds = new List<string>();
            foreach (var folder in DoorSwitchRules.Family(m_Folder)) if (folder == m_Folder || HasData(folder)) worlds.Add(folder);
            m_Switcher = BubbleDoorSwitcher.Create(m_Door, worlds, m_Folder, ChooseDoorWorld, () => { if (m_Door != null) m_Door.RequestPark(); });
            m_DoorMode = true; m_DoorWorldShown = false; m_Held = null; m_HeldVersion = -1; m_RevealOrder = null; m_DoorSwitchTo = null;
            m_Root.SetActive(false);
            m_Phase = Phase.Doorway;
            Appeared?.Invoke();
            Debug.Log($"[Bubble Door] {m_Folder}: the door opens automatically; use the close control to park it");
        }

        /// <summary>
        /// One frame of the door session: the door is held shut while the world loads or switches; the world shows only
        /// while the door is open (the droplet has no occluders); layers that would float in the room wait until the
        /// viewer is through, then grow in.
        /// </summary>
        void StepDoor()
        {
            var viewer = ResolveViewer();
            if (viewer == null) return;
            var door = m_Door;
            door.HoldClosed = !m_AllLayersReady || m_Root == null || m_DoorSwitchTo != null;
            door.Step(Time.deltaTime, viewer.transform.position, viewer.transform.forward);
            if (m_Door == null || m_Door != door) return;
            var show = door.ShowsWorld && m_AllLayersReady && m_Root != null;
            if (show != m_DoorWorldShown) ShowDoorWorld(show);
            if (show)
            {
                PlaceBehindDoor();
                // While the door is dragged the check runs a few times a second, not every frame.
                if (door.Version != m_HeldVersion && (!door.Moving || Time.time - m_HeldAt > .25f)) RefreshHeld();
                if (m_Phase == Phase.Reveal) StepDoorReveal();
            }
            if (m_Switcher != null)
                m_Switcher.Step(viewer.transform.position,
                    m_Phase == Phase.Doorway && m_DoorSwitchTo == null && door.InRoom && door.State == BubbleDoor.Stage.Open,
                    Time.deltaTime, Time.time);
        }

        void ShowDoorWorld(bool on)
        {
            m_DoorWorldShown = on;
            if (m_Root == null) return;
            if (!on) { m_Root.SetActive(false); return; }
            m_Root.SetActive(true);
            m_Membrane.gameObject.SetActive(false);
            if (m_MembraneCollider != null) m_MembraneCollider.enabled = false;
            m_BreathScale = 1f; m_WanderFade = 1f; m_Wandering = false;
            SetRadius(MiniWorldRadius);
            BubbleWorldData.SetAlpha(m_PointMaterial, 1f);
            m_Sky.localScale = Vector3.one * SkyMaxRadius;
            m_SkyRenderer.sharedMaterial = m_SkyMaterial;
            m_Sky.gameObject.SetActive(true);
            PlaceBehindDoor();
            foreach (var layer in m_Layers) if (layer != null) { layer.transform.localScale = Vector3.one; layer.SetActive(true); }
            RefreshHeld();
            var centre = m_Door.Room.TransformPoint(BubbleDoorLayout.DoorCenter(m_Door.DoorZ));
            if (m_TouchSound != null && Application.isPlaying) AudioSource.PlayClipAtPoint(m_TouchSound, centre);
            if (Application.isPlaying) Moodium.CandyWorld.CandyWorldManager.SpawnSoftSqueezeFeedback(centre);
            Debug.Log($"[Bubble Door] {m_Folder}: door open, {CountHeld()} near layers wait until the viewer is through");
        }

        int CountHeld()
        {
            var n = 0;
            if (m_Held != null) foreach (var h in m_Held) if (h) n++;
            return n;
        }

        /// <summary>Which layers reach into the booth (it grows with the viewer); in the room those stay hidden.</summary>
        void RefreshHeld()
        {
            if (m_Door == null) return;
            m_HeldVersion = m_Door.Version;
            m_HeldAt = Time.time;
            m_Held = HeldNearLayers();
            if (!m_Door.InRoom || m_Phase == Phase.Reveal || m_Phase == Phase.Inside) return;
            for (var i = 0; i < m_Layers.Count; i++)
            {
                if (m_Layers[i] == null) continue;
                if (m_Held[i]) m_Layers[i].SetActive(false);
                else if (!m_Layers[i].activeSelf) { m_Layers[i].transform.localScale = Vector3.one; m_Layers[i].SetActive(true); }
            }
        }

        /// <summary>The viewer crossed the door plane: into the world (enter), or back into the room (the world waits).</summary>
        void OnDoorThrough(bool intoWorld)
        {
            if (!m_DoorMode || m_Door == null) return;
            if (intoWorld)
            {
                if (m_Phase != Phase.Doorway || !m_DoorWorldShown) return;
                m_EntryPickUsed = true;
                m_Phase = Phase.Reveal;
                m_RevealStarted = Time.time;
                m_RevealOrder = new List<int>();
                if (m_Held != null)
                    for (var i = 0; i < m_Layers.Count && i < m_Held.Length; i++)
                        if (m_Held[i] && m_Layers[i] != null && !m_Layers[i].activeSelf) m_RevealOrder.Add(i);
                Debug.Log($"[Bubble Door] {m_Folder}: entered; {m_RevealOrder.Count} near layers grow in");
                Touched?.Invoke();
                return;
            }
            if (m_Phase != Phase.Reveal && m_Phase != Phase.Inside) return;
            // Back in the room: the near layers would float round the viewer again, so they wait once more.
            m_RevealOrder = null;
            m_Phase = Phase.Doorway;
            RefreshHeld();
            if (m_Home != null) m_Home.SetActive(false);
            DropTags();
            m_HomeHold.Reset(); UpdateHoldRing(true, 0);
            Debug.Log($"[Bubble Door] {m_Folder}: back in the room, the world waits behind the door");
            Left?.Invoke(LeaveTarget.Room);
        }

        /// <summary>Through the door: the waiting near layers grow in round the viewer, nearest first; then the world counts as revealed.</summary>
        void StepDoorReveal()
        {
            var elapsed = Time.time - m_RevealStarted;
            var finished = true;
            if (m_RevealOrder != null)
                for (var j = 0; j < m_RevealOrder.Count; j++)
                {
                    var start = j * .12f;
                    if (elapsed < start) { finished = false; continue; }
                    var index = m_RevealOrder[j];
                    var layer = m_Layers[index];
                    if (layer == null) continue;
                    if (!layer.activeSelf) { layer.SetActive(true); Sparkle(m_LayerRadii[index]); }
                    var grow = (elapsed - start) / m_LayerGrowSeconds;
                    layer.transform.localScale = Vector3.one * LayerGrowScale(grow);
                    if (grow < 1f) finished = false;
                }
            if (m_Door.State == BubbleDoor.Stage.Gliding) finished = false;
            if (!finished) return;
            if (m_RevealOrder != null) foreach (var i in m_RevealOrder) if (m_Layers[i] != null) m_Layers[i].transform.localScale = Vector3.one;
            m_RevealOrder = null;
            m_Entry = m_Root.transform.position;
            m_Phase = Phase.Inside;
            m_InsideAt = Time.time;
            if (m_Home != null) { Destroy(m_Home); m_Home = null; }
            Revealed?.Invoke();
            LayerRevealed?.Invoke(m_Folder);
        }

        /// <summary>A name on the door's switch was picked (in the room, door open): swap the world behind the door.</summary>
        void ChooseDoorWorld(string folder)
        {
            if (!m_DoorMode || m_Door == null || !m_Door.InRoom || m_Phase != Phase.Doorway) return;
            if (m_DoorSwitchTo != null || folder == m_Folder || !HasData(folder)) return;
            m_DoorSwitchTo = folder;
            m_Switcher?.SetBusy(folder);
            Debug.Log($"[Bubble Door] switch {BubbleChoiceRules.Key(m_Folder)} → {BubbleChoiceRules.Key(folder)}");
            if (m_SwitchRoutine != null) StopCoroutine(m_SwitchRoutine);
            m_SwitchRoutine = StartCoroutine(SwitchDoorWorld(folder));
        }

        /// <summary>
        /// The door folds into a droplet (StepDoor holds it shut while <see cref="m_DoorSwitchTo"/> is set), the old world
        /// is dropped and unloaded, the new one is built, and the droplet opens again with it. Only one world is loaded.
        /// </summary>
        IEnumerator SwitchDoorWorld(string folder)
        {
            while (m_Door != null && m_Door.State != BubbleDoor.Stage.Droplet) yield return null;
            if (m_Door == null) { m_DoorSwitchTo = null; m_SwitchRoutine = null; yield break; }
            var started = Time.realtimeSinceStartup;
            var from = m_From; var hover = m_Hover;
            m_KeepDoor = true;
            Begin(m_Viewer, folder, 0);
            m_KeepDoor = false;
            m_From = from; m_Hover = hover;
            m_DoorMode = true;
            var unload = Resources.UnloadUnusedAssets();
            while (!unload.isDone) yield return null;
            while (!m_AllLayersReady && m_BuildRoutine != null) yield return null;
            m_SwitchRoutine = null;
            if (!m_AllLayersReady || m_Door == null)
            {
                Debug.LogWarning($"[Bubble Door] could not load {folder}: closing the door");
                m_DoorSwitchTo = null;
                CloseDoor();
                m_DoorMode = false;
                yield break;
            }
            Debug.Log($"[Bubble Door] {BubbleChoiceRules.Key(folder)} ready in {Time.realtimeSinceStartup - started:0.00}s: the droplet opens again");
            m_Switcher?.SetCurrent(folder);
            m_Phase = Phase.Doorway;
            m_DoorSwitchTo = null;
        }

        /// <summary>The scan's eye point stands just past the door, facing the way the door faces (and moves with it while it glides).</summary>
        void PlaceBehindDoor()
        {
            if (m_Door == null || m_Root == null) return;
            m_Root.transform.position = m_Door.Room.TransformPoint(new Vector3(0f, m_EyeHeight, m_Door.DoorZ + BubbleDoorLayout.EyeBeyondDoor));
            m_WorldRoot.localRotation = Quaternion.Inverse(m_Root.transform.rotation) * m_Door.Room.rotation;
        }

        bool[] HeldNearLayers()
        {
            var samples = new Vector3[m_Layers.Count][];
            var toRoom = new Matrix4x4[m_Layers.Count];
            for (var i = 0; i < m_Layers.Count; i++)
            {
                samples[i] = m_NearSamples != null && i < m_NearSamples.Length ? m_NearSamples[i] : null;
                toRoom[i] = m_Layers[i] != null ? m_Door.Room.worldToLocalMatrix * m_Layers[i].transform.localToWorldMatrix : Matrix4x4.identity;
            }
            return BubbleDoorLayout.HeldLayers(samples, toRoom, m_Door.DoorZ, m_Door.Booth);
        }

        /// <summary>Every third point of the layers that start within <see cref="DoorSampleReach"/> of the scan centre (scan frame).</summary>
        public static Vector3[][] NearSamples(byte[] bytes, int count, float[] layerOuterRadii, int layers, float reach, int stride = 3)
        {
            var samples = new Vector3[layers][];
            for (var l = 0; l < layers; l++)
            {
                var inner = l == 0 ? 0f : layerOuterRadii[l - 1];
                if (inner >= reach) break;
                BubbleWorldData.LayerRange(l, count, layers, out var s, out var e);
                var list = new List<Vector3>((e - s) / stride + 1);
                for (var i = s; i < e; i += stride)
                {
                    var o = 20 + i * BubbleWorldData.RecordSize;
                    list.Add(new Vector3(BitConverter.ToSingle(bytes, o), BitConverter.ToSingle(bytes, o + 4), BitConverter.ToSingle(bytes, o + 8)));
                }
                samples[l] = list.ToArray();
            }
            return samples;
        }

        void CloseDoor()
        {
            if (m_SwitchRoutine != null && m_DoorSwitchTo != null) { StopCoroutine(m_SwitchRoutine); m_SwitchRoutine = null; }
            m_DoorSwitchTo = null; m_KeepDoor = false;
            if (m_Door == null) return;
            m_Door.WentThrough -= OnDoorThrough;
            m_Door.Close();
            m_Door = null; m_Switcher = null;
        }

        /// <summary>Index fingertip of the left (0) or right (1) hand in world space, or null when untracked.</summary>
        Vector3? HandTip(int index)
        {
            if (m_Hands == null || !m_Hands.running)
            {
                m_Hands = XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRHandSubsystem>();
                if (m_Hands == null) return null;
            }
            var hand = index == 0 ? m_Hands.leftHand : m_Hands.rightHand;
            if (!hand.isTracked || !hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var pose)) return null;
            var origin = ResolveOrigin() != null ? m_Origin.Origin.transform : null;
            return PalmPressGestureController.TransformTrackingPose(pose, origin).position;
        }

        (Vector3 center, Quaternion yaw) Target(Camera viewer)
        {
            var head = viewer.transform.position;
            var floor = ResolveOrigin() != null ? m_Origin.Origin.transform.position.y : 0f;
            var center = FinalCenter(head, floor, m_EyeHeight, m_AlignGroundToFloor);
            // The export header contains the measured capture height for every world.
            return (center, Quaternion.Euler(0f, GazeYaw(viewer.transform.forward), 0f));
        }

        /// <summary>World scale follows the bubble radius: the mini world exactly fills it.</summary>
        void SetRadius(float radius)
        {
            m_Radius = Mathf.Max(.001f, radius);
            m_WorldRoot.localScale = Vector3.one * (m_Radius / MiniWorldRadius);
            m_Membrane.localScale = Vector3.one * (m_Radius * 2f); // built-in sphere has radius 0.5
            BubbleWorldData.SetAlpha(m_MembraneMaterial, MembraneAlpha(m_Radius, m_MembraneAlpha));
        }

        void Sparkle(float layerRadius)
        {
            if (m_SparklesPerLayer <= 0 || m_Root == null) return;
            var center = m_Root.transform.position;
            // Bursts are sized for arm's length, so keep them within a few metres even for far shells.
            var distance = Mathf.Clamp(layerRadius, 1.2f, 4f);
            for (var i = 0; i < m_SparklesPerLayer; i++)
            {
                var angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                var height = UnityEngine.Random.Range(-.6f, .8f);
                var direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                Moodium.CandyWorld.CandyWorldManager.SpawnSoftSqueezeFeedback(center + direction * distance + Vector3.up * height);
            }
        }

        // ---------- Building ----------

        IEnumerator Build()
        {
            // Give the current world transition one frame to settle before resource work.
            yield return null;
            var points = Resources.Load<TextAsset>(BubbleWorldData.PointsResource(m_Folder));
            var colors = Resources.Load<Texture2D>(BubbleWorldData.RoundColorsResource(m_Folder));
            var panorama = Resources.Load<Texture2D>(BubbleWorldData.PanoramaResource(m_Folder));
            var film = Resources.Load<Texture2D>(BubbleWorldData.FilmResource);
            if (points == null || colors == null || panorama == null)
            {
                Debug.LogWarning($"[Bubble World] Missing points / point_colors / panorama under Resources/{m_Folder}.");
                m_BuildRoutine = null;
                yield break;
            }
            var bytes = points.bytes;
            if (!BubbleWorldData.TryReadSummary(bytes, BubbleWorldData.LayerCount, out _, out m_EyeHeight, out m_LayerRadii))
            {
                Debug.LogWarning("[Bubble World] points.bytes is not a valid MSPC v1 file.");
                m_BuildRoutine = null;
                yield break;
            }
            m_MiniLayerCount = MiniLayerCount(m_LayerRadii, MiniWorldRadius);
            if (m_DoorEntry && m_NatureWorldDepth == 0 && BubbleWorldData.TryReadSummary(bytes, BubbleWorldData.LayerCount, out var pointCount, out _, out _))
                m_NearSamples = NearSamples(bytes, pointCount, m_LayerRadii, BubbleWorldData.LayerCount, DoorSampleReach);

            m_PointMaterial = BubbleSplatMaterial.Create(colors, m_EyeHeight,
                m_Folder == BubbleWorldData.CandyFolder || m_Folder == BubbleWorldData.NestedCandyFolder);
            var skyTexture = SkyTexture(m_Folder, m_SkyStyle, panorama);
            m_SkyMaterial = BubbleWorldData.CreateUnlit("Bubble World Sky", skyTexture, false, CullMode.Back, Color.white);
            m_SkyMaterial.renderQueue = BackgroundRenderQueue + m_NatureWorldDepth;
            if (m_SkyMaterial.HasProperty("_ZWrite")) m_SkyMaterial.SetFloat("_ZWrite", 0f);
            m_SkyFadeMaterial = BubbleWorldData.CreateUnlit("Bubble World Sky Fade", skyTexture, true, CullMode.Back, Color.white);
            m_SkyFadeMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            var tint = BubbleWorldData.IsCandy(m_Folder) ? m_CandyFilmTint : m_NatureFilmTint;
            tint.a = m_MembraneAlpha;
            m_MembraneMaterial = BubbleWorldData.CreateUnlit("Bubble World Film", film, true, CullMode.Off, tint);
            m_MembraneMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            m_Root = new GameObject($"Bubble World ({BubbleChoiceRules.Key(m_Folder) ?? m_Folder})");
            m_Root.SetActive(false);
            m_WorldRoot = new GameObject("Scan (scaled)").transform;
            m_WorldRoot.SetParent(m_Root.transform, false);
            if (m_PointMaterial.shader.name == BubbleSplatMaterial.ShaderName)
            {
                m_Motion = m_Root.AddComponent<BubbleSplatMotion>();
                m_Motion.Initialize(m_PointMaterial, m_WorldRoot, ResolveViewer());
            }

            // Sky
            var skyMesh = Resources.Load<Mesh>(BubbleWorldData.MeshResource(m_Folder, BubbleWorldData.PanoramaMeshName));
            if (skyMesh == null)
            {
                skyMesh = BubbleWorldData.BuildPanoramaSphere();
                m_RuntimeMeshes.Add(skyMesh);
            }
            m_Sky = CreateRenderer("Sky", m_WorldRoot, skyMesh, m_SkyMaterial).transform;
            // Panorama assets are already level equirectangular images. Only the
            // scan points need the source capture-frame correction.
            m_Sky.localRotation = Quaternion.identity;
            m_SkyRenderer = m_Sky.GetComponent<MeshRenderer>();
            m_Sky.localScale = Vector3.one * (MiniWorldRadius * .99f);
            // None: the sky object keeps its timing logic but never draws.
            m_SkyRenderer.enabled = m_SkyStyle != SkyStyle.None;
            if (m_FixedSkyOrder)
            {
                // The sky sphere and every point shell are centred on the scan origin, so RealityKit's per-object
                // distance sort cannot tell them apart and sometimes drew the (transparent, while inside) sky over
                // the points. A native group draws the sky first, then the layers. PostPass, as for the UI.
                m_WorldSorting = m_WorldRoot.gameObject.AddComponent<VisionOSSortingGroup>();
                m_WorldSorting.DepthPass = VisionOSSortingGroup.DepthPassType.PostPass;
                m_WorldSorting.Renderers.Add(new VisionOSSortingGroup.RendererSorting
                    { Renderer = m_Sky.gameObject, Order = SkySortOrder, ApplyToDescendants = false });
            }

            if (m_HandWake)
            {
                var wake = new GameObject("Bubble Hand Wake");
                // Under the scan root and in its sorting group, drawn after the point layers: a renderer outside
                // the group had no defined order against the PostPass group and could be hidden behind it.
                wake.transform.SetParent(m_WorldRoot, false);
                m_Wake = wake.AddComponent<BubbleHandWake>();
                m_Wake.Initialize(bytes, colors, m_EyeHeight, BubbleWorldData.IsCandy(m_Folder), m_WorldRoot, ResolveViewer());
                if (m_WorldSorting != null)
                    m_WorldSorting.Renderers.Add(new VisionOSSortingGroup.RendererSorting
                        { Renderer = wake, Order = HandWakeSortOrder, ApplyToDescendants = false });
            }

            // The interaction shell belongs to the core warmup, so a user can
            // summon the miniature world while outer point layers keep streaming.
            var membrane = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            membrane.name = "Bubble Film";
            membrane.transform.SetParent(m_Root.transform, false);
            var membraneRenderer = membrane.GetComponent<MeshRenderer>();
            membraneRenderer.sharedMaterial = m_MembraneMaterial;
            membraneRenderer.shadowCastingMode = ShadowCastingMode.Off;
            membraneRenderer.receiveShadows = false;
            var button = membrane.AddComponent<MoodiumSpatialButton>();
            button.Configure(null, null, Touch);
            var hover = membrane.AddComponent<VisionOSHoverEffect>();
            hover.Type = VisionOSHoverEffect.EffectType.Highlight;
            hover.Color = new Color(.85f, 1f, .9f, 1f);
            if(m_WorldSorting!=null)m_WorldSorting.Renderers.Add(new VisionOSSortingGroup.RendererSorting{Renderer=membrane,Order=3,ApplyToDescendants=false});
            m_Membrane = membrane.transform;
            m_MembraneCollider = membrane.GetComponent<Collider>();
            m_Audio = m_Root.AddComponent<AudioSource>();
            m_Audio.playOnAwake = false;
            m_Audio.spatialBlend = 1f;

            // Point layers (near → far). Baked meshes first; otherwise build them a few per frame.
            BubbleWorldData.Cloud cloud = null;
            var warmupLayers = WarmupLayerCount(m_MiniLayerCount, BubbleWorldData.LayerCount);
            for (var layer = 0; layer < BubbleWorldData.LayerCount; layer++)
            {
                var resourcePath = BubbleWorldData.MeshResource(m_Folder, BubbleWorldData.LayerMeshName(layer));
                Mesh mesh;
                // Stream core layers too: loading a dozen large meshes synchronously stalls input.
                var request = Resources.LoadAsync<Mesh>(resourcePath);
                yield return request;
                mesh = request.asset as Mesh;
                if (mesh == null)
                {
                    if (cloud == null && !BubbleWorldData.TryParse(bytes, out cloud))
                    {
                        Debug.LogWarning("[Bubble World] Could not parse points.bytes.");
                        StopAndClear();
                        yield break;
                    }
                    BubbleWorldData.LayerRange(layer, cloud.Count, BubbleWorldData.LayerCount, out var s, out var e);
                    mesh = BubbleWorldData.BuildLayerMesh(cloud, s, e, BubbleWorldData.LayerMeshName(layer), candy: BubbleWorldData.IsCandy(m_Folder));
                    m_RuntimeMeshes.Add(mesh);
                    if (layer % 2 == 1) yield return null;
                }
                var go = CreateRenderer(BubbleWorldData.LayerMeshName(layer), m_WorldRoot, mesh, m_PointMaterial);
                go.SetActive(layer < m_MiniLayerCount);
                if (m_WorldSorting != null)
                    m_WorldSorting.Renderers.Add(new VisionOSSortingGroup.RendererSorting
                        { Renderer = go, Order = PointLayerSortOrder, ApplyToDescendants = false });
                m_Layers.Add(go);
                if (layer + 1 == warmupLayers)
                {
                    // The miniature bubble can now appear. Remaining layers stream
                    // in one per frame while it floats and approaches the viewer.
                    SetRadius(.001f);
                    m_CoreLayersReady = true;
                    yield return null;
                }
                else
                    yield return null;
            }
            var playObject=new GameObject("Bubble World Play");playObject.transform.SetParent(m_WorldRoot,false);
            m_Play=playObject.AddComponent<BubbleWorldPlay>();
            m_Play.Initialize(m_Folder,m_NatureWorldDepth,m_WorldRoot,ResolveViewer(),m_Layers,m_LayerRadii,m_PointMaterial,Sparkle,
                point=>(m_Home!=null&&m_Home.activeInHierarchy&&Vector3.Distance(point,m_Home.transform.position)<.15f)||
                    (m_Nested!=null&&m_Nested.m_Phase==Phase.Portal&&Vector3.Distance(point,m_Nested.m_Root.transform.position)<.15f));
            if(m_WorldSorting!=null)m_WorldSorting.Renderers.Add(new VisionOSSortingGroup.RendererSorting{Renderer=playObject,Order=3,ApplyToDescendants=true});
            CacheQuietLayerPose();
            m_AllLayersReady = true;
            m_BuildRoutine = null;
            if (cloud != null) Debug.Log("[Bubble World] Built point meshes at runtime; bake them (Moodium ▸ Bubble World ▸ Bake Meshes) for device builds.");

            if (m_Phase == Phase.Armed) SetRadius(.001f);
            // ResetCached enables the root only when the bubble is actually summoned.
            if (m_PrepareNestedEarly) PrepareNested();
        }

        static GameObject CreateRenderer(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        // ---------- Input ----------

        bool FingertipTouching(Vector3 center, float radius)
        {
            if (m_Hands == null || !m_Hands.running)
            {
                m_Hands = XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRHandSubsystem>();
                if (m_Hands == null) return false;
            }
            var origin = ResolveOrigin() != null ? m_Origin.Origin.transform : null;
            return TipTouching(m_Hands.leftHand, origin, center, radius) || TipTouching(m_Hands.rightHand, origin, center, radius);
        }

        /// <summary>Distance from <paramref name="point"/> to the nearest tracked index fingertip (infinity without hands).</summary>
        float NearestTipDistance(Vector3 point)
        {
            if (m_Hands == null || !m_Hands.running)
            {
                m_Hands = XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRHandSubsystem>();
                if (m_Hands == null) return float.PositiveInfinity;
            }
            var origin = ResolveOrigin() != null ? m_Origin.Origin.transform : null;
            var best = float.PositiveInfinity;
            foreach (var hand in new[] { m_Hands.leftHand, m_Hands.rightHand })
                if (hand.isTracked && hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var pose))
                    best = Mathf.Min(best, Vector3.Distance(PalmPressGestureController.TransformTrackingPose(pose, origin).position, point));
            return best;
        }

        bool TipTouching(XRHand hand, Transform origin, Vector3 center, float radius)
        {
            if (!hand.isTracked) return false;
            if (!hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var pose)) return false;
            var tip = PalmPressGestureController.TransformTrackingPose(pose, origin).position;
            return IsTouching(tip, center, radius, m_TouchPadding);
        }

        XROrigin ResolveOrigin()
        {
            if (m_Origin == null) m_Origin = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
            return m_Origin;
        }

        Camera ResolveViewer()
        {
            if (m_Viewer == null) m_Viewer = Camera.main;
            return m_Viewer;
        }

        static void DestroyMaterial(ref Material material)
        {
            if (material != null) Destroy(material);
            material = null;
        }
    }
}
