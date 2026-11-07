using System.Collections;
using System.Collections.Generic;
using Samples.PolySpatial.SwiftUI.Scripts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using Unity.PolySpatial;
using Moodium.Audio;
using Moodium.CandyWorld;
using Moodium.Flow.ObjectPicker;
using Moodium.Opening;
using Moodium.Reality;
using Moodium.Calibration;
using Moodium.MoodCheck;
using Moodium.Rewards;

namespace Moodium.Flow
{
    public sealed class MoodiumAppFlowController : MonoBehaviour
    {
        [Header("Existing scene modules")]
        [SerializeField] ARTrackedObjectManager m_TrackedObjectManager;
        [SerializeField] ARTrackedImageManager m_TrackedImageManager;
        [SerializeField] ARMeshManager m_SpatialMeshManager;
        [SerializeField] MeshSwiftUIDriver m_SpatialPhysicsDriver;

        [Header("Moodium entrance")]
        [SerializeField] MoodiumPortalEntranceController m_PortalEntrance;

        [Header("Creative Space worlds")]
        [SerializeField] MoodiumWorldDefinition[] m_Worlds;
        [SerializeField] MoodiumWorldDatabase m_WorldDatabase;
        [SerializeField] GameObject m_WorldCardPrefab;
        [SerializeField, Min(0.5f)] float m_SpawnDistance = 1f;

        [Header("Reality Enhancement world models")]
        [SerializeField] GameObject m_RealityCandyWorldModelPrefab;
        [SerializeField] GameObject m_RealityNatureWorldModelPrefab;
        [SerializeField] GameObject m_RealityFallbackWorldModelPrefab;
        [SerializeField] GameObject m_MoodiMascotPrefab;

        [Header("Reality Enhancement tracked soft objects")]
        [SerializeField] GameObject m_RealityCandySoftPrefab;
        [SerializeField] GameObject m_RealityNatureSoftPrefab;
        [SerializeField] GameObject m_PushLeftHandGuidePrefab;
        [SerializeField] GameObject m_PushRightHandGuidePrefab;

        [Header("Nature World forest growth")]
        [Tooltip("Static tree prefabs with LODGroup components. Spawned progressively only in Nature World.")]
        [SerializeField] GameObject[] m_NatureForestTreePrefabs;
        [Tooltip("Grass grown on the real floor beneath a walking player after Nature World reaches 100% energy.")]
        [SerializeField] GameObject m_NatureFootstepGrassPrefab;
        [Tooltip("Alternate Nature footstep ground prefab, alternated with the regular grass.")]
        [SerializeField] GameObject m_NatureFootstepAlternatePrefab;
        [Tooltip("Bean prefab grown on the real floor beneath a walking player after Candy World reaches 100% energy.")]
        [SerializeField] GameObject m_CandyFootstepBeanPrefab;
        [SerializeField] GameObject m_WalkTrailGuidePrefab;
        [Tooltip("360-degree Nature World background shown only after Nature energy reaches 100%.")]
        [SerializeField] Texture2D m_NatureCompletionPanoramaTexture;
        [Tooltip("360-degree Candy World background shown only after Candy energy reaches 100%.")]
        [SerializeField] Texture2D m_CandyCompletionPanoramaTexture;
        [Tooltip("Candy World building shown once when the energy journey reaches 100%.")]
        [SerializeField] GameObject m_CandyCompletionHousePrefab;
        [Tooltip("Smaller Candy World building shown when the energy journey reaches 100%.")]
        [SerializeField] GameObject m_CandyLittleCompletionHousePrefab;
        [Tooltip("Nature World burst prefabs emitted from the calibrated soft model.")]
        [SerializeField] GameObject[] m_NatureBurstPrefabs;
        [Header("World reward collection")]
        [SerializeField] WorldRewardTable m_CandyWorldRewardTable;
        [SerializeField] WorldRewardTable m_NatureWorldRewardTable;
        [Header("Nature Burst Debug")]
        [Tooltip("One-shot diagnostic: when NatureSoft becomes available, force a pure Candy-pipeline Nature burst without progress or pinch.")]
        [SerializeField] bool m_ForceNatureBurstOnStart;
        [SerializeField] NatureBurstDebugPrefabSelection m_ForceNatureBurstPrefab = NatureBurstDebugPrefabSelection.Random;
        [Tooltip("Temporary diagnostic: Nature World squeezes emit Candy prefabs through the normal, proven Candy burst path.")]
        [SerializeField] bool m_UseCandyBurstPrefabsInNatureForDebug;
        [SerializeField] GameObject m_NatureIvyGeneratorPrefab;
        [SerializeField] Material m_NatureIvyGrowMaterial;

        [Header("Creative Space initial object scale")]
        [Tooltip("Creative Space only. Each value multiplies the source prefab scale when it is spawned.")]
        [SerializeField] CreativePrefabScaleConfig[] m_CreativePrefabScaleConfigs;

        [Header("Prototype UI")]
        [SerializeField] TMP_FontAsset m_FontAsset;
        [SerializeField] TMP_FontAsset m_ChineseFontAsset;
        [SerializeField] GameObject m_GlassPanelPrefab;
        [SerializeField] Material m_ObjectPickerGlassMaterial;
        [SerializeField] GameObject m_SpatialTablePrefab;
        [SerializeField] GameObject m_CandyTransformationPreviewPrefab;
        [SerializeField] Material m_CandyTransformationMaterial;
        [Tooltip("Transparent white crack texture placed on Candy-wrapped spatial surfaces after hand impact.")]
        [SerializeField] Texture2D m_CandyWrapperCrackTexture;
        [SerializeField] Material m_CandyWrapperCrackMaterial;
        [SerializeField] GameObject m_CandyWrapperCrackPrefab;
        [SerializeField] Sprite m_WristHudRoundedSprite;
        [SerializeField] Sprite m_WristHudCircleSprite;
        [SerializeField] GameObject m_CandyFrostPrefab;
        [Tooltip("Nature-only falling leaves shown after the sensory-bag journey reaches 100%.")]
        [SerializeField] GameObject m_NatureLeafFallPrefab;
        [Tooltip("Sound played when a full-energy release reward is touched and disappears.")]
        [SerializeField] AudioClip m_ReleaseRewardTouchSound;
        [Tooltip("Looped while a Candy World progress prefab is being squeezed.")]
        [SerializeField] AudioClip m_CandyPrefabDeformationSound;
        [SerializeField] Sprite m_CreativeSpaceModeArtwork;
        [SerializeField] Sprite m_RealityEnhancementModeArtwork;

        [Header("Mood Check")]
        [SerializeField] GameObject m_MoodCheckPanelPrefab;

        MoodiumMode m_CurrentMode;
        Transform m_UiRoot;
        GameObject m_ModePanel;
        GameObject m_WorldSelectionPanel;
        GameObject m_ObjectWorldSelectionPanel;
        GameObject m_NoObjectPanel;
        GameObject m_InteractionPanel;
        GameObject m_ObjectPanel;
        TMP_Text m_SpawnObjectsButtonLabel;
        MoodiumNoObjectModeController m_NoObjectModeController;
        MoodiumWorldDefinition m_SelectedWorld;
        MoodiumWorldNavigationState m_WorldNavigationState;
        TMP_Text m_WorldEditTitle;
        MoodiumObjectPickerController m_ObjectPicker;
        WorldCarouselController m_WorldCarousel;
        WorldCarouselController m_ObjectWorldCarousel;
        CandyWorldManager m_CandyWorldManager;
        RealitySpatialTableManager m_RealitySpatialTableManager;
        RealityEnhancementFlowController m_RealityEnhancementFlow;
        StickerWorldSelectionController m_StickerWorldSelection;
        WorldCarouselSelectionController m_BranchCarousel;
        TableAnchorFinder m_TableAnchorFinder;
        GameObject m_BagSwitch;
        GameObject m_FallbackSelectionDisplay;
        bool m_SelectionHasBag = true;
        SensoryBagCalibrationCoordinator m_CalibrationCoordinator;
        bool m_UseCalibratedImageBag;
        OpeningVideoSession m_OpeningVideoSession;
        StickerWorldEntry m_SelectedStickerEntry;
        Coroutine m_StickerTrackingWarmupRoutine;
        GameObject m_CandyTransformationPreviewInstance;
        Camera m_MainCamera;
        bool m_LoggedMissingFont;
        bool m_FlowUiReady;
        MoodCheckController m_MoodCheckController;
        MoodCheckContext m_MoodCheckContext;
        MoodCheckLaunchSource m_MoodCheckLaunchSource;
        bool m_TestMoodAvailabilityOverride;
        bool m_TestCandyAvailable;
        bool m_TestNatureAvailable;
        bool m_DisableMoodLoggingForTests;
        bool m_DisableNavigationForTests;

        enum MoodCheckLaunchSource { None, Opening, StickerSelection, PostVisit }

        public MoodiumMode CurrentMode => m_CurrentMode;
        public bool MoodCheckInProgress => m_MoodCheckController != null &&
            m_MoodCheckController.State is not (MoodCheckState.Hidden or MoodCheckState.Done);
        public MoodCheckContext MoodContext => m_MoodCheckContext;
        public bool UsesStickerSelection => m_SelectionHasBag;

        public void ConfigureMoodCheckForTests(MoodCheckController controller, MoodCheckContext context)
        {
            UnsubscribeMoodCheck();
            m_MoodCheckController = controller;
            m_MoodCheckContext = context;
            m_TestMoodAvailabilityOverride = true;
            m_TestCandyAvailable = true;
            m_TestNatureAvailable = true;
            m_DisableMoodLoggingForTests = true;
            m_DisableNavigationForTests = true;
            SubscribeMoodCheck();
        }

        public void BeginEntryMoodCheck(bool english)
        {
            // The opening flow must not interrupt the sensory-bag choice with the
            // full mood panel. Mood guidance lives on the sticker-selection hint.
            m_MoodCheckController?.DismissImmediate();
            m_MoodCheckLaunchSource = MoodCheckLaunchSource.None;
        }

        public void RequestStickerMoodCheck()
        {
            if (MoodCheckInProgress || m_SelectionSettingsPanel != null || m_JournalPanel != null) return;
            EnsureMoodCheck();
            StopStickerTrackingWarmup();
            m_StickerWorldSelection?.SetMoodCheckSuspended(true);
            m_BranchCarousel?.SetMoodCheckSuspended(true);
            SetSelectionColumnVisible(false);
            if (m_TrackedImageManager != null) m_TrackedImageManager.enabled = false;
            m_MoodCheckLaunchSource = MoodCheckLaunchSource.StickerSelection;
            m_MoodCheckController.ShowRecommendCard = m_SelectionHasBag;
            m_MoodCheckController?.BeginPreCheck(CurrentMoodAvailability(), IsEnglish());
            // Video-backed (has-bag) selection: the panel stands in front of the video and moves with its handle.
            if (m_SelectionHasBag)
                AttachToOpeningWindow(m_MoodCheckController?.PanelTransform, true);
        }

        /// <summary>
        /// Layout rule for panels opened from the video-backed selection (mood panel, settings panel):
        /// they stand <see cref="OpeningPanelClearance"/> in front of the video plane, toward the viewer, and keep
        /// that pose relative to the video while its window handle is dragged. Transparent layers this close
        /// to each other sort unreliably on visionOS, so they must never touch or cross the video.
        /// </summary>
        public const float OpeningPanelClearance = .3f;
        /// <summary>The handle keeps the video at least this far away, so panels in front stay ≥ 0.55 m from the eyes.</summary>
        public const float OpeningWindowMinDistance = .85f;
        /// <summary>No longer used: the settings panel now opens in front of the video like the other panels. Kept for compatibility.</summary>
        public const float SettingsPanelExtraClearance = .06f;

        /// <summary>Mood panel pose: on the line from the viewer to the video, <paramref name="clearance"/> in front of it, at reading height.</summary>
        public static Pose PanelPoseInFrontOfVideo(Transform display, Vector3 viewer, float clearance)
        {
            var away = Vector3.ProjectOnPlane(display.forward, Vector3.up);
            if (away.sqrMagnitude < 1e-6f) away = Vector3.ProjectOnPlane(display.position - viewer, Vector3.up);
            away = away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.forward;
            var position = display.position - away * clearance;
            position.y = viewer.y - .12f;   // same reading height as MoodCheckController.PlacePanel
            return new Pose(position, Quaternion.LookRotation(away, Vector3.up));
        }

        void AttachToOpeningWindow(Transform panel, bool placeInFront)
        {
            var display = m_OpeningVideoSession?.DisplayRoot;
            if (panel == null || display == null || !display.gameObject.activeInHierarchy)
                return;
            if (placeInFront)
            {
                var camera = m_MainCamera != null ? m_MainCamera : Camera.main;
                if (camera != null)
                {
                    var pose = PanelPoseInFrontOfVideo(display, camera.transform.position, OpeningPanelClearance);
                    panel.SetPositionAndRotation(pose.position, pose.rotation);
                }
            }
            var handle = display.GetComponentInChildren<MoodiumWindowHandle>(true);
            if (handle != null) handle.AddFollower(panel);
            Debug.Log($"[Moodium Window] {panel.name} follows the opening video (handle={(handle != null)}).");
        }

        public void CancelMoodCheckImmediate()
        {
            m_MoodCheckController?.DismissImmediate();
            ResumeStickerTrackingAfterMoodCheck();
            m_MoodCheckLaunchSource = MoodCheckLaunchSource.None;
        }

        void ResumeStickerTrackingAfterMoodCheck()
        {
            if (m_MoodCheckLaunchSource == MoodCheckLaunchSource.StickerSelection && !m_SelectionHasBag)
            {
                m_BranchCarousel?.SetMoodCheckSuspended(false);
                Debug.Log("[Sensory Bag Branch] mode=none step=resume-carousel image-tracking=false");
                return;
            }
            if (m_MoodCheckLaunchSource != MoodCheckLaunchSource.StickerSelection ||
                m_StickerWorldSelection == null || !m_StickerWorldSelection.IsSelectionActive) return;
            m_StickerWorldSelection.SetMoodCheckSuspended(false);
            SetSelectionColumnVisible(true);
            if (m_TrackedImageManager != null) m_TrackedImageManager.enabled = true;
            StartStickerTrackingWarmup();
        }

        public string BeginMoodVisit(string enteredWorldId, float realtime)
        {
            EnsureMoodCheck();
            var visitId = m_MoodCheckContext.BeginVisit(enteredWorldId, realtime);
            if (!m_DisableMoodLoggingForTests)
                Moodium.WishJournal.WishJournalVisitLog.TryAppend(Moodium.WishJournal.WishJournalVisitLog.Build(
                    visitId, enteredWorldId, Moodium.WishJournal.WishJournalVisitLog.Enter, System.DateTime.UtcNow));
            if (m_MoodCheckContext.PreSample != null)
            {
                m_MoodCheckContext.PreSample.visitId = visitId;
                m_MoodCheckContext.PreSample.enteredWorldId = enteredWorldId ?? string.Empty;
            }
            return visitId;
        }

        public void RequestMoodCheckOnWorldExit(float realtime)
        {
            StopCandyWorldExperience();
            // The Wish Journal shows which world, when and for how long (the mood log only has check-ins).
            if (!m_DisableMoodLoggingForTests && m_MoodCheckContext != null && m_MoodCheckContext.HasActiveVisit)
            {
                var metrics = m_MoodCheckContext.SnapshotMetrics(realtime);
                Moodium.WishJournal.WishJournalVisitLog.TryAppend(Moodium.WishJournal.WishJournalVisitLog.Build(
                    m_MoodCheckContext.ActiveVisitId, m_MoodCheckContext.EnteredWorldId, Moodium.WishJournal.WishJournalVisitLog.Leave,
                    System.DateTime.UtcNow, metrics.DurationInWorldSeconds, metrics.PinchCount));
            }
            // Returning from a world is immediate; mood check is an optional hint
            // before sticker selection, never a blocking post-visit panel.
            m_MoodCheckContext?.CompleteVisit();
            m_MoodCheckLaunchSource = MoodCheckLaunchSource.None;
            FinishReturnToStickerWorldSelection();
        }

        public static bool SupportsRealityWorld(MoodiumWorldDefinition world)
        {
            if (world == null)
                return false;
            return world.WorldId.Equals("candy", System.StringComparison.OrdinalIgnoreCase) ||
                   world.WorldId.Equals("nature", System.StringComparison.OrdinalIgnoreCase);
        }

        public void PrepareForOpening()
        {
            MoodiumAudioManager.StopBackgroundMusic();
            SetExistingModules(false, false);
            HideAllPanels();
            m_PortalEntrance?.HideImmediate();
        }

        public void BeginPortalFlow()
        {
            enabled = true;
            if (m_FlowUiReady)
                ShowObjectWorldSelection();
        }

        /// <summary>Entry point used by the opening video. The launch sequence no longer enters Portal.</summary>
        public void ShowObjectWorldSelectionFromOpening()
        {
            ShowObjectWorldSelection();
        }

        public void BeginStickerWorldSelection(OpeningVideoSession session)
        {
            SensoryBagBranchUI.Dispose(m_SelectionSettingsPanel); m_SelectionSettingsPanel = null;
            DisposeJournal();
            if (session == null || session.IsDisposed)
            {
                if (m_FallbackSelectionDisplay == null)
                {
                    m_FallbackSelectionDisplay = new GameObject("Selection Surface Without Opening Video");
                    var camera = m_MainCamera != null ? m_MainCamera : Camera.main;
                    if (camera != null)
                        m_FallbackSelectionDisplay.transform.SetPositionAndRotation(camera.transform.position + camera.transform.forward * .9f,
                            Quaternion.LookRotation(camera.transform.forward, Vector3.up));
                }
                session = new OpeningVideoSession(null, m_FallbackSelectionDisplay.transform, null, IsEnglish(), () => { });
            }
            StopStickerTrackingWarmup();
            m_StickerWorldSelection?.End();
            m_BranchCarousel?.End();
            SensoryBagBranchUI.Dispose(m_BagSwitch); m_BagSwitch = null; m_SettingsGear = null;
            DisposeReplayButton();
            m_CalibrationCoordinator?.EndCalibrationMode();
            m_CalibrationCoordinator?.ClearPresentations();
            m_TableAnchorFinder?.Clear();
            m_SelectedStickerEntry = null;
            m_SelectionHasBag = SensoryBagOwnership.HasBag;
            m_UseCalibratedImageBag = false;
            ExitCreativeSpaceIfNeeded();
            StopCandyWorldExperience();
            m_OpeningVideoSession = session;
            SetMode(MoodiumMode.ObjectTracking);
            HideAllPanels();
            m_PortalEntrance?.HideImmediate();
            var imageSpawner = FindFirstObjectByType<ImageTrackingCapsuleSpawner>(FindObjectsInactive.Include);
            imageSpawner?.ClearRuntimeInstances();
            if (imageSpawner != null)
                imageSpawner.enabled = false;
            if (m_TrackedObjectManager != null)
                m_TrackedObjectManager.enabled = false;
            EnsureStickerWorldSelection();
            EnsureMoodCheck();
            m_MoodCheckController?.DismissImmediate();
            m_CalibrationCoordinator?.ClearPresentations();
            var candy = FindRealityWorld("candy");
            var nature = FindRealityWorld("nature");
            m_StickerWorldSelection.Configure(
                m_TrackedImageManager,
                m_RealityCandyWorldModelPrefab,
                m_RealityNatureWorldModelPrefab,
                candy,
                nature);
            m_StickerWorldSelection.ConfigureFonts(m_ChineseFontAsset, m_FontAsset);
            m_StickerWorldSelection.ConfigureMoodCheck(m_MoodCheckContext);
            m_StickerWorldSelection.ConfigureEntryGlassMaterial(GetPickerGlassMaterial());
            m_StickerWorldSelection.SetMoodCheckSummary(m_MoodCheckContext.PreSample);
            m_StickerWorldSelection.End();
            if (!m_SelectionHasBag)
            {
                if (m_TrackedImageManager != null) m_TrackedImageManager.enabled = false;
                if (imageSpawner != null) imageSpawner.Configure(null, null);
                if (m_BranchCarousel == null)
                    m_BranchCarousel = GetComponent<WorldCarouselSelectionController>() ?? gameObject.AddComponent<WorldCarouselSelectionController>();
                m_BranchCarousel.Configure(m_RealityCandyWorldModelPrefab, m_RealityNatureWorldModelPrefab, candy, nature,
                    m_MainCamera != null ? m_MainCamera : Camera.main);
                m_BranchCarousel.ConfigureFonts(m_ChineseFontAsset, m_FontAsset);
                m_BranchCarousel.ConfigureEntryGlassMaterial(GetPickerGlassMaterial());
                m_BranchCarousel.WorldEntrySelected -= OnStickerWorldEntrySelected;
                m_BranchCarousel.WorldEntrySelected += OnStickerWorldEntrySelected;
                m_BranchCarousel.MoodCheckRequested -= RequestStickerMoodCheck;
                m_BranchCarousel.MoodCheckRequested += RequestStickerMoodCheck;
                m_BranchCarousel.OwnershipSwitchRequested -= OpenSelectionSettings;
                m_BranchCarousel.OwnershipSwitchRequested += OpenSelectionSettings;
                m_BranchCarousel.SetMoodCheckSummary(m_MoodCheckContext.PreSample);
                m_BranchCarousel.Begin(session);
                var anchor = m_BranchCarousel.SettingsAnchor;
                if (anchor != null && anchor.parent != null)
                {
                    // The gear sits right of the mood chip (chip spans x ±0.16): the journal mirrors it on the left.
                    var mirrored = new Vector3(-anchor.localPosition.x, anchor.localPosition.y, anchor.localPosition.z);
                    m_JournalButton = Moodium.WishJournal.WishJournalButton.Create(anchor.parent, anchor.parent.TransformPoint(mirrored),
                        () => { if (m_BranchCarousel != null && m_BranchCarousel.CanInteract) ToggleJournal(); });
                    // Replay sits right of the gear (gear | ↺), one column pitch further out.
                    if (FindOpening() != null)
                        m_ReplayButton = Moodium.UI.UIRefreshVisuals.ReplayButton(anchor.parent, ReplayBesideGear(anchor.localPosition),
                            () => { if (m_BranchCarousel != null && m_BranchCarousel.CanInteract) ReplayOpening(); });
                }
                Debug.Log("[Sensory Bag Branch] mode=none step=selection-begin image-tracking=false");
                return;
            }
            // Subscribe before enabling the manager. This mirrors the previously working
            // world flow and prevents visionOS's first image-anchor event being missed.
            m_StickerWorldSelection.Begin(session);
            EnsureOpeningWindowHandle(session.DisplayRoot);
            if (m_TrackedImageManager != null)
                m_TrackedImageManager.enabled = true;
            StartStickerTrackingWarmup();
            session.SetVisible(true);
            // Settings and Wish Journal stand in a column at the video's top-right corner, out of the way of the
            // video and of the instruction / mood chip below it (they used to sit in a row under the video).
            var display = session.DisplayRoot;
            m_SettingsGear = Moodium.UI.UIRefreshVisuals.Gear(display, SelectionColumnLocal(display.localScale, 0), OpenSelectionSettings);
            m_BagSwitch = m_SettingsGear.transform.parent.gameObject;
            m_BagSwitch.AddComponent<SensoryBagDirectTouchInput>();
            m_JournalButton = Moodium.WishJournal.WishJournalButton.Create(display,
                display.TransformPoint(SelectionColumnLocal(display.localScale, 1)), ToggleJournal);
            m_JournalButton.gameObject.AddComponent<SensoryBagDirectTouchInput>();
            if (FindOpening() != null)
            {
                m_ReplayButton = Moodium.UI.UIRefreshVisuals.ReplayButton(display, SelectionColumnLocal(display.localScale, ReplayColumnSlot), ReplayOpening);
                m_ReplayButton.AddComponent<SensoryBagDirectTouchInput>();
            }
            // Ownership was chosen in the opening. Do not put a one-touch
            // no-bag switch beside mood check / window movement in sticker mode.
            if (m_StickerWorldSelection.MoodCheckButton != null &&
                m_StickerWorldSelection.MoodCheckButton.GetComponent<SensoryBagDirectTouchInput>() == null)
                m_StickerWorldSelection.MoodCheckButton.gameObject.AddComponent<SensoryBagDirectTouchInput>();
            Debug.Log("[Sensory Bag Branch] mode=has step=selection-begin image-tracking=true");
            Debug.Log($"[Moodium Flow] Video-backed sticker world selection started. Image manager enabled={m_TrackedImageManager != null && m_TrackedImageManager.enabled}; candy prefab={m_RealityCandyWorldModelPrefab != null}; nature prefab={m_RealityNatureWorldModelPrefab != null}; candy world={candy != null}; nature world={nature != null}.");
        }

        public void SwitchSensoryBagOwnership()
        {
            // Also reject stale callbacks from a disposed selection surface.
            if (m_SelectionHasBag || SensoryBagOwnership.HasBag) return;
            if (m_SelectedStickerEntry != null || MoodCheckInProgress) return;
            SensoryBagOwnership.SetHasBag(true);
            Debug.Log("[Sensory Bag Branch] step=selection-switch");
            BeginStickerWorldSelection(m_OpeningVideoSession);
        }

        GameObject m_SelectionSettingsPanel;
        Moodium.UI.UIRefreshSpriteButton m_SettingsGear;

        /// <summary>Top-right corner of the selection video (metres from its centre) where the button column starts.</summary>
        public static readonly Vector2 SelectionColumnCorner = new(.58f, .30f);
        /// <summary>Centre-to-centre spacing of the column (buttons are 4.5 cm, press areas 6 cm).</summary>
        public const float SelectionColumnPitch = .075f;

        /// <summary>
        /// Local position on the (non-uniformly scaled) video display of column slot <paramref name="slot"/>:
        /// 0 = settings gear at the top, 1 = Wish Journal below it, 2 = replay the opening. 4 cm in front of the video,
        /// like the gear always was.
        /// </summary>
        public static Vector3 SelectionColumnLocal(Vector3 displayScale, int slot) => new(
            SelectionColumnCorner.x / Mathf.Max(1e-4f, displayScale.x),
            (SelectionColumnCorner.y - slot * SelectionColumnPitch) / Mathf.Max(1e-4f, displayScale.y),
            -.04f / Mathf.Max(1e-4f, displayScale.z));

        /// <summary>The corner column hides during a mood check (as the gear did), and shows otherwise.</summary>
        void SetSelectionColumnVisible(bool visible)
        {
            if (m_BagSwitch != null) m_BagSwitch.SetActive(visible);
            if (m_JournalButton != null && m_SelectionHasBag) m_JournalButton.gameObject.SetActive(visible);
            if (m_ReplayButton != null && m_SelectionHasBag) m_ReplayButton.SetActive(visible);
        }

        void OpenSelectionSettings()
        {
            if (m_SelectedStickerEntry != null || MoodCheckInProgress) return;
            if (m_SelectionSettingsPanel != null) { m_SelectionSettingsPanel.GetComponent<Moodium.UI.SelectionSettingsVisual>().Close(); return; }
            if (m_JournalPanel != null) return; // Panels are mutually exclusive.
            var gear = m_SelectionHasBag ? m_BagSwitch?.transform : m_BranchCarousel?.SettingsAnchor;
            if (gear == null) return;
            var panel = new GameObject("Selection Settings Panel");
            m_SelectionSettingsPanel = panel;
            panel.transform.SetPositionAndRotation(gear.position + gear.up * Moodium.UI.MoodiumUIRefreshLayout.SettingsPanelAboveKey, gear.rotation);
            // With the video, the gear is in the top-right corner: the panel opens where the mood and journal panels
            // do (30 cm in front of the video, reading height) and the corner gear itself shows the open state, so
            // the panel carries no gear of its own. Without the video (carousel) nothing changes.
            panel.AddComponent<Moodium.UI.SelectionSettingsVisual>().Initialize(IsEnglish(), SensoryBagOwnership.HasBag,
                OpeningFlowPreferences.AskLanguage, OpeningFlowPreferences.AskBag,
                ApplySelectionSettings, ApplyOpeningQuestions, CloseSelectionSettings, embeddedGear: !m_SelectionHasBag,
                sensitivity: Moodium.Interaction.SqueezeSensitivity.Level,
                applySensitivity: level => Moodium.Interaction.SqueezeSensitivity.Set(level),
                pace: Moodium.CandyWorld.ProgressPace.Level,
                applyPace: pace => Moodium.CandyWorld.ProgressPace.Set(pace));
            if (m_SelectionHasBag)
                AttachToOpeningWindow(panel.transform, true);
            SuspendForSettings();
        }
        void SuspendForSettings()
        {
            m_StickerWorldSelection?.SetMoodCheckSuspended(true);
            m_BranchCarousel?.SetMoodCheckSuspended(true);
            // The corner gear stays and shows "open" (pressing it again closes the panel).
            if (m_SettingsGear != null) m_SettingsGear.SetOpen(true);
            else if (m_BagSwitch != null) m_BagSwitch.SetActive(false);
        }
        void CloseSelectionSettings()
        {
            SensoryBagBranchUI.Dispose(m_SelectionSettingsPanel); m_SelectionSettingsPanel=null;
            m_StickerWorldSelection?.SetMoodCheckSuspended(false);
            m_BranchCarousel?.SetMoodCheckSuspended(false);
            if (m_SettingsGear != null) m_SettingsGear.SetOpen(false);
            else if (m_BagSwitch != null) m_BagSwitch.SetActive(true);
        }

        // Settings → Intro: which questions the next opening asks. Takes effect at the next launch; nothing here changes.
        static void ApplyOpeningQuestions(bool askLanguage, bool askBag)
        {
            OpeningFlowPreferences.AskLanguage = askLanguage;
            OpeningFlowPreferences.AskBag = askBag;
        }

        // ---------- Replay the opening (2026-11-04) ----------
        // Between visitors the next person should see the opening again without force-quitting the app. A ↺ button
        // sits under the Wish Journal in the video's corner column (with a bag) or right of the gear (manual pick).
        // Pressing it closes the selection, starts a fresh mood session and plays the opening from the start; the
        // opening then hands over to the selection exactly as at launch. Returning users still get Skip on the video.

        /// <summary>Corner-column slot of the replay button on the video: under the Wish Journal.</summary>
        public const int ReplayColumnSlot = 2;

        /// <summary>Carousel (no bag): local position right of the gear at <paramref name="gear"/>, one column pitch out.</summary>
        public static Vector3 ReplayBesideGear(Vector3 gear) => gear + new Vector3(SelectionColumnPitch, 0f, 0f);

        GameObject m_ReplayButton;
        OpeningManager m_OpeningManager;
        public GameObject ReplayButton => m_ReplayButton;

        OpeningManager FindOpening()
        {
            if (m_OpeningManager == null)
                m_OpeningManager = FindFirstObjectByType<OpeningManager>(FindObjectsInactive.Include);
            return m_OpeningManager;
        }

        void DisposeReplayButton()
        {
            SensoryBagBranchUI.Dispose(m_ReplayButton); m_ReplayButton = null;
        }

        /// <summary>Only from a quiet selection screen: no world chosen, no mood check, no panel open, not already replaying.</summary>
        public bool CanReplayOpening
        {
            get
            {
                if (m_SelectedStickerEntry != null || MoodCheckInProgress) return false;
                if (m_SelectionSettingsPanel != null || m_JournalPanel != null) return false;
                var opening = FindOpening();
                return opening != null && !opening.IsReplaying;
            }
        }

        public void ReplayOpening()
        {
            if (!CanReplayOpening) return;
            Debug.Log("[Moodium Flow] Replay opening: closing world selection for the next person.");
            StopStickerTrackingWarmup();
            m_StickerWorldSelection?.End();
            m_BranchCarousel?.End();
            SensoryBagBranchUI.Dispose(m_BagSwitch); m_BagSwitch = null; m_SettingsGear = null;
            DisposeJournal();
            DisposeReplayButton();
            if (m_TrackedImageManager != null) m_TrackedImageManager.enabled = false;
            var imageSpawner = FindFirstObjectByType<ImageTrackingCapsuleSpawner>(FindObjectsInactive.Include);
            imageSpawner?.ClearRuntimeInstances();
            if (imageSpawner != null) imageSpawner.enabled = false;
            m_MoodCheckController?.DismissImmediate();
            m_MoodCheckLaunchSource = MoodCheckLaunchSource.None;
            // A new visitor: a new mood session, so the mood chip no longer shows the last person's answer.
            m_MoodCheckContext = new MoodCheckContext();
            // The old video window (and everything parented to it) goes; the opening builds a new one in front of
            // whoever is wearing the headset now.
            var session = m_OpeningVideoSession; m_OpeningVideoSession = null;
            session?.DisposeOnce();
            SensoryBagBranchUI.Dispose(m_FallbackSelectionDisplay); m_FallbackSelectionDisplay = null;
            PrepareForOpening();
            m_OpeningManager.ReplayOpening();
        }

        // ---------- Wish Journal ----------

        Moodium.WishJournal.WishJournalButton m_JournalButton;
        GameObject m_JournalPanel;
        public bool JournalOpen => m_JournalPanel != null;

        void ToggleJournal()
        {
            if (m_JournalPanel != null) { m_JournalPanel.GetComponent<Moodium.WishJournal.WishJournalPanel>().Close(); return; }
            OpenJournal();
        }

        public void OpenJournal()
        {
            if (m_SelectedStickerEntry != null || MoodCheckInProgress || m_JournalPanel != null) return;
            if (m_SelectionSettingsPanel != null) return; // Panels are mutually exclusive.
            var panel = new GameObject("Wish Journal Panel");
            m_JournalPanel = panel;
            var camera = m_MainCamera != null ? m_MainCamera : Camera.main;
            if (m_SelectionHasBag && m_OpeningVideoSession?.DisplayRoot != null && m_OpeningVideoSession.DisplayRoot.gameObject.activeInHierarchy)
                AttachToOpeningWindow(panel.transform, true);   // 30 cm in front of the video, follows its handle
            else if (camera != null)
            {
                // Same reading pose as the mood panel without a video window.
                var flat = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
                flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
                panel.transform.SetPositionAndRotation(camera.transform.position + flat * .75f - Vector3.up * .12f, Quaternion.LookRotation(flat, Vector3.up));
            }
            panel.AddComponent<Moodium.WishJournal.WishJournalPanel>().Initialize(IsEnglish(), m_ChineseFontAsset, m_FontAsset, CloseJournalNow);
            m_StickerWorldSelection?.SetMoodCheckSuspended(true);
            m_BranchCarousel?.SetMoodCheckSuspended(true);
            m_JournalButton?.SetOpen(true);
            m_JournalButton?.SetDot(false);
            var path = Moodium.NatureWorld.WishChimeStore.DefaultPath;
            System.Threading.Tasks.Task.Run(() => Moodium.WishJournal.WishJournalData.Newest(Moodium.NatureWorld.WishChimeStore.Load(path)))
                .ContinueWith(t => { if (t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) m_PendingJournalSeen = t.Result; });
            Debug.Log("[Wish Journal] open");
        }

        // Written from a worker thread, stored on the main thread (PlayerPrefs is main-thread only).
        volatile string m_PendingJournalSeen;

        void LateUpdate()
        {
            var seen = m_PendingJournalSeen;
            if (seen == null) return;
            m_PendingJournalSeen = null;
            Moodium.WishJournal.WishJournalButton.MarkSeen(seen);
        }

        void CloseJournalNow()
        {
            if (m_JournalPanel == null) return;
            SensoryBagBranchUI.Dispose(m_JournalPanel); m_JournalPanel = null;
            m_JournalButton?.SetOpen(false);
            if (m_SelectionSettingsPanel == null && !MoodCheckInProgress)
            {
                m_StickerWorldSelection?.SetMoodCheckSuspended(false);
                m_BranchCarousel?.SetMoodCheckSuspended(false);
            }
            Debug.Log("[Wish Journal] closed");
        }

        void DisposeJournal()
        {
            SensoryBagBranchUI.Dispose(m_JournalPanel); m_JournalPanel = null;
            if (m_JournalButton != null) SensoryBagBranchUI.Dispose(m_JournalButton.gameObject);
            m_JournalButton = null;
        }

        void ApplySelectionSettings(bool english, bool hasBag)
        {
            PlayerPrefs.SetString("Moodium.Language", english ? "English" : "Chinese");
            SensoryBagOwnership.SetHasBag(hasBag);
            PlayerPrefs.Save();
            m_OpeningVideoSession?.SetLanguage(english);
            var panel = m_SelectionSettingsPanel; m_SelectionSettingsPanel = null;
            BeginStickerWorldSelection(m_OpeningVideoSession);
            m_SelectionSettingsPanel = panel;
            if(panel!=null) SuspendForSettings();
        }

        void Awake()
        {
            HideSpatialMeshVisualization();
            if (m_TrackedObjectManager != null)
                m_TrackedObjectManager.enabled = false;
            if (m_TrackedImageManager != null)
                m_TrackedImageManager.enabled = false;
            if (m_SpatialPhysicsDriver != null)
                m_SpatialPhysicsDriver.enabled = false;
        }

        IEnumerator Start()
        {
            if (GetComponent<MoodiumSpatialInteractionController>() == null)
                gameObject.AddComponent<MoodiumSpatialInteractionController>();

            m_NoObjectModeController = GetComponent<MoodiumNoObjectModeController>();
            if (m_NoObjectModeController == null)
                m_NoObjectModeController = gameObject.AddComponent<MoodiumNoObjectModeController>();

            if (m_TrackedObjectManager != null)
                m_TrackedObjectManager.enabled = false;
            if (m_TrackedImageManager != null)
                m_TrackedImageManager.enabled = false;

            if (m_SpatialPhysicsDriver != null)
                m_SpatialPhysicsDriver.enabled = false;

            var timeout = Time.realtimeSinceStartup + 10f;
            while (Camera.main == null && Time.realtimeSinceStartup < timeout)
                yield return null;

            m_MainCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
            if (m_MainCamera == null)
            {
                Debug.LogError("[Moodium Flow] Main Camera was not found.");
                yield break;
            }

            BuildFlowUI();
            m_FlowUiReady = true;

            // The opening sequence owns the initial flow when it is present in the
            // scene. It calls BeginPortalFlow only after the video and candy reveal.
            if (FindFirstObjectByType<Moodium.Opening.OpeningManager>() != null)
            {
                Debug.Log("[Moodium Flow] Waiting for MoodiumOpening before showing the portal.");
                yield break;
            }
            ShowPortalEntrance();
        }

        void BuildFlowUI()
        {
            m_UiRoot = new GameObject("Moodium Flow UI").transform;

            BuildObjectWorldSelectionPanel();
            BuildObjectPanel();
        }

        void BuildObjectWorldSelectionPanel()
        {
            m_ObjectWorldSelectionPanel = CreatePanelRoot("Moodium Object World Selection", -0.08f, false);
            CreateText(
                m_ObjectWorldSelectionPanel.transform,
                "Choose a tracked-object world",
                new Vector3(0f, 0.36f, 0f),
                3.1f,
                new Vector2(7f, 0.8f));

            var carouselObject = new GameObject("Reality Enhancement World Carousel");
            carouselObject.transform.SetParent(m_ObjectWorldSelectionPanel.transform, false);
            carouselObject.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            m_ObjectWorldCarousel = carouselObject.AddComponent<WorldCarouselController>();
            m_ObjectWorldCarousel.ConfigureModels(
                m_WorldDatabase,
                m_RealityCandyWorldModelPrefab,
                m_RealityNatureWorldModelPrefab,
                m_RealityFallbackWorldModelPrefab,
                m_FontAsset,
                SelectObjectWorld,
                PreviewWorldMusic);

            m_ObjectWorldSelectionPanel.SetActive(false);
        }

        void BuildWorldSelectionPanel()
        {
            m_WorldSelectionPanel = CreatePanelRoot("Moodium World Selection", -0.08f, false);
            CreateText(
                m_WorldSelectionPanel.transform,
                "Explore sensory worlds",
                new Vector3(0f, 0.36f, 0f),
                3.1f,
                new Vector2(7f, 0.8f));

            var carouselObject = new GameObject("World Carousel");
            carouselObject.transform.SetParent(m_WorldSelectionPanel.transform, false);
            carouselObject.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            m_WorldCarousel = carouselObject.AddComponent<WorldCarouselController>();
            m_WorldCarousel.Configure(m_WorldDatabase, m_WorldCardPrefab, SelectWorld, PreviewWorldMusic);

            CreateButton(
                m_WorldSelectionPanel.transform,
                "Back",
                new Vector3(0f, -0.36f, 0f),
                new Vector2(0.28f, 0.09f),
                ShowModeSelection);
            m_WorldSelectionPanel.SetActive(false);
        }

        void BuildModeSelection()
        {
            var spaceCreationSprite = Resources.Load<Sprite>("UI/SpaceCreationUI");
            var augmentedRealitySprite = Resources.Load<Sprite>("UI/AugmentedRealityUI");
            if (spaceCreationSprite == null)
                spaceCreationSprite = m_CreativeSpaceModeArtwork;
            if (augmentedRealitySprite == null)
                augmentedRealitySprite = m_RealityEnhancementModeArtwork;
            Debug.Log(
                $"[Moodium UI] spaceCreationSprite is null={spaceCreationSprite == null}; " +
                $"augmentedRealitySprite is null={augmentedRealitySprite == null}; " +
                $"platform={Application.platform}");

            m_ModePanel = CreatePanelRoot("Moodium Mode Selection", -0.05f, false);
            CreateText(
                m_ModePanel.transform,
                "Choose your Moodium space",
                new Vector3(0f, 0.3f, 0f),
                2.7f,
                new Vector2(7f, 0.8f));

            CreateModeCard(
                m_ModePanel.transform,
                "空间创造模式",
                "Creative Space Mode",
                "打造属于你的幻想世界，释放无限创意。",
                spaceCreationSprite,
                new Vector3(-0.225f, 0f, 0f),
                EnterNoObjectMode);
            CreateModeCard(
                m_ModePanel.transform,
                "现实增强模式",
                "Reality Enhancement Mode",
                "赋予真实世界魔力，开启感官新体验。",
                augmentedRealitySprite,
                new Vector3(0.225f, 0f, 0f),
                EnterObjectMode);
            m_ModePanel.SetActive(false);
        }

        void BuildNoObjectPanel()
        {
            // Edit Mode uses real 3D picker objects rather than a dark panel or text list.
            m_NoObjectPanel = CreatePanelRoot("Moodium Model Library", -0.2f, false);
            m_WorldEditTitle = CreateText(
                m_NoObjectPanel.transform,
                "Candy World · Choose an object",
                new Vector3(0f, 0.4f, 0f),
                2.6f,
                new Vector2(7f, 0.8f));

            var pickerObject = new GameObject("Candy World 3D Object Picker");
            pickerObject.transform.SetParent(m_NoObjectPanel.transform, false);
            m_ObjectPicker = pickerObject.AddComponent<MoodiumObjectPickerController>();
            m_ObjectPicker.Configure(m_MainCamera, GetPickerGlassMaterial(), SpawnFreeObject);

            CreateButton(
                m_NoObjectPanel.transform,
                "Finish Editing",
                new Vector3(-0.23f, -0.30f, 0f),
                new Vector2(0.42f, 0.09f),
                EnterNoObjectInteractionMode);
            CreateButton(
                m_NoObjectPanel.transform,
                "Clear Scene",
                new Vector3(0.18f, -0.30f, 0f),
                new Vector2(0.36f, 0.09f),
                ClearNoObjectScene);
            CreateButton(
                m_NoObjectPanel.transform,
                "Worlds",
                new Vector3(0f, -0.41f, 0f),
                new Vector2(0.28f, 0.09f),
                ShowWorldSelection);
            m_NoObjectPanel.SetActive(false);
        }

        void RebuildWorldModelButtons()
        {
            if (m_ObjectPicker == null || m_SelectedWorld == null)
                return;

            m_WorldEditTitle.text = $"{m_SelectedWorld.DisplayName} - Edit Mode";
            m_ObjectPicker.Configure(m_MainCamera, GetPickerGlassMaterial(), SpawnFreeObject);
            m_ObjectPicker.Build(m_SelectedWorld.Prefabs);
        }

        void BuildInteractionPanel()
        {
            m_InteractionPanel = CreatePanelRoot("Moodium Interaction Mode", -0.2f, false);
            CreateText(
                m_InteractionPanel.transform,
                "Candy World",
                new Vector3(0f, 0.105f, 0f),
                2.35f,
                new Vector2(5.8f, 0.65f));

            var toolbar = new GameObject("Candy World Floating Toolbar").transform;
            toolbar.SetParent(m_InteractionPanel.transform, false);
            toolbar.localPosition = new Vector3(0f, -0.005f, 0f);
            CreateGlassVisual(toolbar, "Toolbar Glass Capsule", new Vector2(0.7f, 0.15f), 2);

            CreateButton(
                toolbar,
                "Edit",
                new Vector3(-0.235f, 0f, -0.012f),
                new Vector2(0.17f, 0.075f),
                EnterNoObjectEditMode);
            var spawnButton = CreateButton(
                toolbar,
                "Spawn Objects · OFF",
                new Vector3(0f, 0f, -0.012f),
                new Vector2(0.25f, 0.075f),
                ToggleCreativeSpaceSpawning);
            spawnButton.Label.fontSize = 2.05f;
            m_SpawnObjectsButtonLabel = spawnButton.Label;
            CreateButton(
                toolbar,
                "← Back",
                new Vector3(0.235f, 0f, -0.012f),
                new Vector2(0.17f, 0.075f),
                ShowWorldSelection);
            m_InteractionPanel.SetActive(false);
        }

        void BuildObjectPanel()
        {
            // Exit now lives on the wrist HUD so nothing is fixed in the user's room.
            m_ObjectPanel = new GameObject("Moodium Reality Enhancement Mode (Legacy Root)");
            m_ObjectPanel.transform.SetParent(transform, false);
            m_ObjectPanel.SetActive(false);
        }

        public void ShowModeSelection()
        {
            ShowObjectWorldSelection();
        }

        public void ShowPortalEntrance()
        {
            ExitCreativeSpaceIfNeeded();
            StopCandyWorldExperience();
            MoodiumAudioManager.StopBackgroundMusic();
            SetMode(MoodiumMode.Intro);
            SetExistingModules(false, false);
            HideAllPanels();
            if (m_PortalEntrance == null)
            {
                Debug.LogError("[Moodium Flow] Portal entrance is missing; opening Reality world selection as fallback.");
                ShowObjectWorldSelection();
                return;
            }
            m_PortalEntrance.Completed -= HandlePortalCompleted;
            m_PortalEntrance.Completed += HandlePortalCompleted;
            m_PortalEntrance.Show();
            Debug.Log("[Moodium Flow] Moodium portal entrance shown.");
        }

        void HandlePortalCompleted()
        {
            if (m_PortalEntrance != null)
                m_PortalEntrance.Completed -= HandlePortalCompleted;
            ShowObjectWorldSelection();
        }

        void EnterNoObjectMode()
        {
            SetMode(MoodiumMode.NoObject);
            SetExistingModules(false, false);
            ShowWorldSelection();
            Debug.Log("[Moodium Flow] Entered Creative Space Mode / 空间创造模式.");
        }

        void ShowWorldSelection()
        {
            StopCandyWorldExperience();
            MoodiumAudioManager.StopBackgroundMusic();
            SetCreativeSpacePhysicsEnabled(false);
            SetMode(MoodiumMode.NoObject);
            m_WorldNavigationState = MoodiumWorldNavigationState.WorldSelection;
            HideAllPanels();
            PlacePanel(m_WorldSelectionPanel, -0.08f);
            m_WorldSelectionPanel.SetActive(true);
            if (m_WorldCarousel != null)
                m_WorldCarousel.ResetToFirstAvailable();
            Debug.Log("[Moodium World] World selection shown.");
        }

        void SelectWorld(MoodiumWorldDefinition world)
        {
            if (world == null)
                return;
            if (!world.IsAvailable)
            {
                Debug.Log($"[Moodium World] {world.DisplayName} is coming soon.");
                return;
            }

            m_SelectedWorld = world;
            MoodiumAudioManager.PlayBackgroundMusic(world.BackgroundMusic);
            m_NoObjectModeController.SelectWorld(world);
            RebuildWorldModelButtons();
            EnterNoObjectEditMode();
        }

        void PreviewWorldMusic(MoodiumWorldDefinition world)
        {
            if (world != null)
                MoodiumAudioManager.PlayBackgroundMusic(world.BackgroundMusic);
        }

        void EnterNoObjectEditMode()
        {
            if (m_SelectedWorld == null)
            {
                ShowWorldSelection();
                return;
            }

            SetMode(MoodiumMode.NoObject);
            m_WorldNavigationState = MoodiumWorldNavigationState.WorldEdit;
            SetCreativeSpacePhysicsEnabled(false);
            m_NoObjectModeController.EnterEditMode();
            HideAllPanels();
            PlacePanel(m_NoObjectPanel, -0.2f);
            m_NoObjectPanel.SetActive(true);
            m_ObjectPicker?.PlaceInFront();
            Debug.Log("[Moodium Flow] Creative Space Mode: Edit Mode.");
        }

        void EnterNoObjectInteractionMode()
        {
            if (m_SelectedWorld == null)
            {
                ShowWorldSelection();
                return;
            }

            SetMode(MoodiumMode.NoObject);
            m_WorldNavigationState = MoodiumWorldNavigationState.WorldInteraction;
            m_NoObjectModeController.EnterInteractionMode();
            SetCreativeSpacePhysicsEnabled(true);
            HideAllPanels();
            PlacePanel(m_InteractionPanel, -0.2f);
            m_InteractionPanel.SetActive(true);
            Debug.Log("[Moodium Flow] Creative Space Mode: Interaction Mode.");
        }

        void ClearNoObjectScene()
        {
            m_NoObjectModeController.ClearScene();
        }

        void EnterObjectMode()
        {
            ShowObjectWorldSelection();
        }

        void ShowObjectWorldSelection()
        {
            ExitCreativeSpaceIfNeeded();
            // The Back button routes here. Stop the current world before showing the picker so
            // Nature's runtime-grown trees (and every other world-specific runtime effect) do
            // not remain in the user's space after leaving the experience.
            StopCandyWorldExperience();
            SetMode(MoodiumMode.ObjectTracking);
            SetExistingModules(false, false);
            m_PortalEntrance?.HideImmediate();
            HideAllPanels();
            PlacePanel(m_ObjectWorldSelectionPanel, -0.08f);
            m_ObjectWorldSelectionPanel.SetActive(true);
            if (m_ObjectWorldCarousel != null)
                m_ObjectWorldCarousel.ResetToFirstAvailable();
            Debug.Log("[Moodium Flow] Reality Enhancement Mode world selection shown.");
        }

        void ReturnToStickerWorldSelection()
        {
            RequestMoodCheckOnWorldExit(Time.realtimeSinceStartup);
        }

        void FinishReturnToStickerWorldSelection()
        {
            if (m_DisableNavigationForTests)
                return;
            if (m_OpeningVideoSession == null || m_OpeningVideoSession.IsDisposed)
            {
                BeginStickerWorldSelection(null);
                return;
            }
            BeginStickerWorldSelection(m_OpeningVideoSession);
        }

        void EnsureStickerWorldSelection()
        {
            if (m_StickerWorldSelection == null)
                m_StickerWorldSelection = GetComponent<StickerWorldSelectionController>();
            if (m_StickerWorldSelection == null)
                m_StickerWorldSelection = gameObject.AddComponent<StickerWorldSelectionController>();
            m_StickerWorldSelection.WorldEntrySelected -= OnStickerWorldEntrySelected;
            m_StickerWorldSelection.WorldEntrySelected += OnStickerWorldEntrySelected;
            m_StickerWorldSelection.MoodCheckRequested -= RequestStickerMoodCheck;
            m_StickerWorldSelection.MoodCheckRequested += RequestStickerMoodCheck;
            if (m_CalibrationCoordinator == null)
                m_CalibrationCoordinator = GetComponent<SensoryBagCalibrationCoordinator>();
            if (m_CalibrationCoordinator == null)
                m_CalibrationCoordinator = gameObject.AddComponent<SensoryBagCalibrationCoordinator>();
            m_CalibrationCoordinator.CalibrationCompleted -= OnCalibrationCompleted;
            m_CalibrationCoordinator.CalibrationCompleted += OnCalibrationCompleted;
        }

        void StartStickerTrackingWarmup()
        {
            StopStickerTrackingWarmup();
            m_StickerTrackingWarmupRoutine = StartCoroutine(RefreshStickerTrackingAfterEnable());
        }

        void StopStickerTrackingWarmup()
        {
            if (m_StickerTrackingWarmupRoutine == null)
                return;
            StopCoroutine(m_StickerTrackingWarmupRoutine);
            m_StickerTrackingWarmupRoutine = null;
        }

        IEnumerator RefreshStickerTrackingAfterEnable()
        {
            // On visionOS image anchors may be created in the same frame as a manager
            // is enabled. Polling the authoritative ARFoundation collection for a small,
            // bounded number of frames makes the hand-off deterministic without creating
            // another tracking session or changing the existing tracking pipeline.
            for (var frame = 0; frame < 12; frame++)
            {
                yield return null;
                if (m_StickerWorldSelection == null || !m_StickerWorldSelection.IsSelectionActive)
                    break;
                m_StickerWorldSelection.RefreshTrackedImages();
            }
            m_StickerTrackingWarmupRoutine = null;
        }

        void OnStickerWorldEntrySelected(StickerWorldEntry entry)
        {
            if (entry == null || MoodCheckInProgress)
                return;
            m_MoodCheckController?.DismissImmediate();
            m_MoodCheckLaunchSource = MoodCheckLaunchSource.None;
            m_SelectedStickerEntry = entry;
            StopStickerTrackingWarmup();
            m_StickerWorldSelection?.End();
            m_BranchCarousel?.End();
            SensoryBagBranchUI.Dispose(m_BagSwitch); m_BagSwitch = null; m_SettingsGear = null;
            DisposeJournal();
            DisposeReplayButton();
            m_OpeningVideoSession?.SetVisible(false);
            m_OpeningVideoSession?.Player?.Pause();
            var imageSpawner = FindFirstObjectByType<ImageTrackingCapsuleSpawner>(FindObjectsInactive.Include);
            imageSpawner?.ClearRuntimeInstances();
            imageSpawner?.Configure(m_SelectionHasBag ? m_TrackedImageManager : null, null);
            if (imageSpawner != null)
                imageSpawner.enabled = m_SelectionHasBag;
            var softPrefab = entry.World != null && entry.World.WorldId.Equals("nature", System.StringComparison.OrdinalIgnoreCase)
                ? m_RealityNatureSoftPrefab
                : m_RealityCandySoftPrefab;
            m_CalibrationCoordinator?.ConfigureVisualPrefab(
                entry.World != null && entry.World.WorldId.Equals("nature", System.StringComparison.OrdinalIgnoreCase)
                    ? SensoryBagWorldKind.Nature
                    : SensoryBagWorldKind.Candy,
                softPrefab);
            m_CalibrationCoordinator?.ConfigurePushHandGuides(
                m_PushLeftHandGuidePrefab,
                m_PushRightHandGuidePrefab);
            m_CalibrationCoordinator?.ConfigureFonts(m_ChineseFontAsset, m_FontAsset);
            m_CalibrationCoordinator?.ConfigureButtonStyle(m_WristHudRoundedSprite);
            m_CalibrationCoordinator?.ConfigureFrameAdjustingTutorial(m_OpeningVideoSession);
            m_UseCalibratedImageBag = true;
            if (m_SelectionHasBag)
            {
                m_CalibrationCoordinator?.ConfirmDefault(entry);
            }
            else
            {
                if (m_TrackedImageManager != null) m_TrackedImageManager.enabled = false;
                m_TableAnchorFinder = GetComponent<TableAnchorFinder>() ?? gameObject.AddComponent<TableAnchorFinder>();
                m_TableAnchorFinder.AnchorReady -= OnTableAnchorReady;
                m_TableAnchorFinder.AnchorReady += OnTableAnchorReady;
                m_TableAnchorFinder.Begin(m_MainCamera != null ? m_MainCamera : Camera.main,
                    IsEnglish() ? m_FontAsset : m_ChineseFontAsset, IsEnglish(),
                    () => m_CalibrationCoordinator.GetActiveCalibrator(UnityEngine.XR.ARSubsystems.TrackableId.invalidId)?.HasUserInteracted == true);
            }
        }

        void OnTableAnchorReady(Transform anchor)
        {
            if (m_SelectionHasBag || m_SelectedStickerEntry == null || anchor == null) return;
            m_SelectedStickerEntry = new StickerWorldEntry(UnityEngine.XR.ARSubsystems.TrackableId.invalidId,
                string.Empty, m_SelectedStickerEntry.World, anchor, null);
            m_CalibrationCoordinator.BeginAnchoredCalibration(m_SelectedStickerEntry, anchor);
        }

        void OnCalibrationCompleted(StickerWorldEntry entry, SensoryBagCalibrationData data)
        {
            if (entry == null || entry != m_SelectedStickerEntry)
                return;
            m_TableAnchorFinder?.StopDetection();
            m_CalibrationCoordinator?.EndCalibrationMode();
            m_SelectedStickerEntry = null;
            SelectObjectWorld(entry.World);
        }

        MoodiumWorldDefinition FindRealityWorld(string worldId)
        {
            if (m_WorldDatabase != null && m_WorldDatabase.Worlds != null)
                foreach (var world in m_WorldDatabase.Worlds)
                    if (world != null && world.WorldId.Equals(worldId, System.StringComparison.OrdinalIgnoreCase))
                        return world;
            if (m_Worlds != null)
                foreach (var world in m_Worlds)
                    if (world != null && world.WorldId.Equals(worldId, System.StringComparison.OrdinalIgnoreCase))
                        return world;
            return null;
        }

        void SelectObjectWorld(MoodiumWorldDefinition world)
        {
            if (world == null || !world.IsAvailable)
                return;
            m_MoodCheckController?.DismissImmediate();
            m_MoodCheckLaunchSource = MoodCheckLaunchSource.None;
            if (!SupportsRealityWorld(world))
            {
                Debug.Log($"[Moodium Object World] {world.DisplayName} is not implemented yet.");
                return;
            }

            m_SelectedWorld = world;
            BeginMoodVisit(world.WorldId, Time.realtimeSinceStartup);
            m_PortalEntrance?.HideImmediate();
            MoodiumAudioManager.PlayBackgroundMusic(world.BackgroundMusic);
            ConfigureTrackedSoftPrefab(world);
            EnsureCandyWorldManager();
            m_CandyWorldManager.SetEntryMood(Moodium.NatureWorld.BubbleChoiceRules.FreshMood(m_MoodCheckContext?.PreSample, System.DateTime.UtcNow));
            m_CandyWorldManager.StartExperience(world);
            if (m_SelectionHasBag)
            {
                EnsureRealityEnhancementFlow();
                m_RealityEnhancementFlow.StartFlow();
            }
            SetMode(MoodiumMode.ObjectTracking);
            SetExistingModules(true, false);
            HideAllPanels();
            Debug.Log($"[Moodium Flow] Reality Enhancement Mode: {world.DisplayName}. Tissue tracking enabled.");
        }

        void ConfigureTrackedSoftPrefab(MoodiumWorldDefinition world)
        {
            var prefab = world.WorldId.Equals("nature", System.StringComparison.OrdinalIgnoreCase)
                ? m_RealityNatureSoftPrefab
                : m_RealityCandySoftPrefab;
            if (prefab == null)
            {
                Debug.LogError($"[Moodium Object World] Missing tracked soft prefab for {world.DisplayName}.");
                return;
            }

            var objectSpawner = FindFirstObjectByType<TissueObjectTrackingSpawner>(FindObjectsInactive.Include);
            var imageSpawner = FindFirstObjectByType<ImageTrackingCapsuleSpawner>(FindObjectsInactive.Include);
            objectSpawner?.ClearRuntimeInstances();
            imageSpawner?.ClearRuntimeInstances();
            objectSpawner?.Configure(m_TrackedObjectManager, prefab);
            imageSpawner?.Configure(m_SelectionHasBag ? m_TrackedImageManager : null, m_UseCalibratedImageBag ? null : prefab);
            if (!m_SelectionHasBag && imageSpawner != null) imageSpawner.enabled = false;
        }

        void SetCreativeSpacePhysicsEnabled(bool enabled)
        {
            if (m_SpatialPhysicsDriver == null)
                return;
            m_SpatialPhysicsDriver.SetEmbeddedMoodiumMode(enabled);
            m_SpatialPhysicsDriver.enabled = enabled;
            if (!enabled)
                m_SpatialPhysicsDriver.SetSpawningObjects(false);
            UpdateSpawnObjectsLabel();
        }

        void ToggleCreativeSpaceSpawning()
        {
            if (m_SpatialPhysicsDriver == null)
                return;
            if (!m_SpatialPhysicsDriver.enabled)
                SetCreativeSpacePhysicsEnabled(true);
            m_SpatialPhysicsDriver.SetSpawningObjects(!m_SpatialPhysicsDriver.IsSpawningObjects);
            UpdateSpawnObjectsLabel();
        }

        void UpdateSpawnObjectsLabel()
        {
            if (m_SpawnObjectsButtonLabel != null)
                m_SpawnObjectsButtonLabel.text = m_SpatialPhysicsDriver != null && m_SpatialPhysicsDriver.IsSpawningObjects
                    ? "Spawn Objects · ON"
                    : "Spawn Objects · OFF";
        }

        void EnsureCandyWorldManager()
        {
            if (m_CandyWorldManager != null)
                return;
            var managerObject = new GameObject("Candy World Reality Enhancement");
            managerObject.transform.SetParent(transform, false);
            m_CandyWorldManager = managerObject.AddComponent<CandyWorldManager>();
            m_CandyWorldManager.ConfigureWalkTrailGuide(m_WalkTrailGuidePrefab);
            m_CandyWorldManager.ConfigureSqueezeContext(m_MoodCheckContext);
            m_CandyWorldManager.Configure(
                m_MainCamera,
                m_GlassPanelPrefab,
                m_FontAsset,
                m_SelectedWorld,
                m_SpatialMeshManager,
                m_CandyTransformationMaterial,
                m_WristHudRoundedSprite,
                m_WristHudCircleSprite,
                m_CandyFrostPrefab,
                m_CandyWrapperCrackTexture,
                m_CandyWrapperCrackMaterial,
                m_CandyWrapperCrackPrefab,
                m_NatureLeafFallPrefab);
            m_CandyWorldManager.ConfigureEnvironmentPromptFonts(m_ChineseFontAsset, m_FontAsset);
            m_CandyWorldManager.ConfigureNatureForestTrees(m_NatureForestTreePrefabs);
            m_CandyWorldManager.ConfigureNatureFootstepGrass(m_NatureFootstepGrassPrefab);
            m_CandyWorldManager.ConfigureNatureFootstepAlternate(m_NatureFootstepAlternatePrefab);
            m_CandyWorldManager.ConfigureCandyFootstepBean(m_CandyFootstepBeanPrefab);
            m_CandyWorldManager.ConfigureNatureCompletionPanorama(m_NatureCompletionPanoramaTexture);
            m_CandyWorldManager.ConfigureCandyCompletionPanorama(m_CandyCompletionPanoramaTexture);
            m_CandyWorldManager.ConfigureCandyCompletionHouse(m_CandyCompletionHousePrefab);
            m_CandyWorldManager.ConfigureCandyLittleCompletionHouse(m_CandyLittleCompletionHousePrefab);
            m_CandyWorldManager.ConfigureCandyBurstPrefabs(FindRealityWorld("candy")?.Prefabs);
            m_CandyWorldManager.ConfigureNatureBurstPrefabs(m_NatureBurstPrefabs);
            m_CandyWorldManager.ConfigureNatureBurstPrefabSource(m_UseCandyBurstPrefabsInNatureForDebug);
            m_CandyWorldManager.ConfigureRewardTouchSound(m_ReleaseRewardTouchSound);
            m_CandyWorldManager.ConfigureCandyPrefabDeformationSound(m_CandyPrefabDeformationSound);
            m_CandyWorldManager.ConfigureExitAction(ReturnToStickerWorldSelection);
            m_CandyWorldManager.ConfigureNatureBurstDebug(m_ForceNatureBurstOnStart, m_ForceNatureBurstPrefab);
            m_CandyWorldManager.ConfigureNatureIvy(m_NatureIvyGeneratorPrefab, m_NatureIvyGrowMaterial);
        }

        void StopCandyWorldExperience()
        {
            m_RealityEnhancementFlow?.StopFlow();
            if (m_CandyWorldManager != null)
                m_CandyWorldManager.StopExperience();
            m_RealitySpatialTableManager?.HideTable();
            if (m_CandyTransformationPreviewInstance != null)
            {
                Destroy(m_CandyTransformationPreviewInstance);
                m_CandyTransformationPreviewInstance = null;
            }
        }

        void EnsureMoodCheck()
        {
            m_MoodCheckContext ??= new MoodCheckContext();
            if (m_MoodCheckController == null)
                m_MoodCheckController = GetComponent<MoodCheckController>();
            if (m_MoodCheckController == null)
                m_MoodCheckController = gameObject.AddComponent<MoodCheckController>();
            if (!m_TestMoodAvailabilityOverride)
            {
                if (m_MainCamera == null) m_MainCamera = Camera.main;
                // Unity destroyed/missing references compare equal to null, but ??=
                // does not recognize them. Retry the actual resource in that case.
                if (m_MoodCheckPanelPrefab == null)
                    m_MoodCheckPanelPrefab = Resources.Load<GameObject>("Moodium/MoodCheck/MoodCheckPanel");
                m_MoodCheckController.Configure(m_MoodCheckPanelPrefab, m_MainCamera);
            }
            SubscribeMoodCheck();
        }

        void SubscribeMoodCheck()
        {
            if (m_MoodCheckController == null)
                return;
            m_MoodCheckController.Completed -= OnMoodCheckCompleted;
            m_MoodCheckController.Completed += OnMoodCheckCompleted;
            m_MoodCheckController.Skipped -= OnMoodCheckSkipped;
            m_MoodCheckController.Skipped += OnMoodCheckSkipped;
        }

        void UnsubscribeMoodCheck()
        {
            if (m_MoodCheckController == null)
                return;
            m_MoodCheckController.Completed -= OnMoodCheckCompleted;
            m_MoodCheckController.Skipped -= OnMoodCheckSkipped;
        }

        void OnMoodCheckCompleted(MoodCheckSample sample)
        {
            if (sample == null)
                return;
            sample.sessionId = m_MoodCheckContext.SessionId;
            sample.language = IsEnglish() ? "en" : "zh-CN";
            if (sample.phase == MoodCheckPhase.Pre)
            {
                m_MoodCheckContext.SetPreSample(sample);
                m_StickerWorldSelection?.SetMoodCheckSummary(sample);
                m_BranchCarousel?.SetMoodCheckSummary(sample);
                ResumeStickerTrackingAfterMoodCheck();
            }
            else
            {
                sample.visitId = m_MoodCheckContext.ActiveVisitId ?? string.Empty;
                sample.enteredWorldId = m_MoodCheckContext.EnteredWorldId ?? string.Empty;
            }
            if (!m_DisableMoodLoggingForTests)
                MoodCheckLog.TryAppend(sample);
            if (m_MoodCheckLaunchSource != MoodCheckLaunchSource.PostVisit)
                return;
            m_MoodCheckContext.CompleteVisit();
            m_MoodCheckController.DismissImmediate();
            m_MoodCheckLaunchSource = MoodCheckLaunchSource.None;
            FinishReturnToStickerWorldSelection();
        }

        void OnMoodCheckSkipped()
        {
            ResumeStickerTrackingAfterMoodCheck();
            if (m_MoodCheckLaunchSource == MoodCheckLaunchSource.PostVisit)
            {
                m_MoodCheckContext.CompleteVisit();
                m_MoodCheckLaunchSource = MoodCheckLaunchSource.None;
                FinishReturnToStickerWorldSelection();
            }
        }

        MoodCheckAvailability CurrentMoodAvailability()
        {
            if (m_TestMoodAvailabilityOverride)
                return new MoodCheckAvailability(m_TestCandyAvailable, m_TestNatureAvailable);
            return new MoodCheckAvailability(
                FindRealityWorld("candy")?.IsAvailable == true,
                FindRealityWorld("nature")?.IsAvailable == true);
        }

        bool IsEnglish() => PlayerPrefs.GetString("Moodium.Language", "Chinese")
            .Equals("English", System.StringComparison.OrdinalIgnoreCase);

        void OnDestroy()
        {
            SensoryBagBranchUI.Dispose(m_SelectionSettingsPanel);
            DisposeJournal();
            UnsubscribeMoodCheck();
            m_TableAnchorFinder?.StopDetection();
            if (m_FallbackSelectionDisplay != null) SensoryBagBranchUI.Dispose(m_FallbackSelectionDisplay);
        }

        void ShowCandyTransformationPreview()
        {
            if (m_CandyTransformationPreviewInstance != null || m_CandyTransformationPreviewPrefab == null)
                return;
            m_CandyTransformationPreviewInstance = Instantiate(m_CandyTransformationPreviewPrefab);
            m_CandyTransformationPreviewInstance.name = "Candy Material Transformation Vision Preview";
            m_CandyTransformationPreviewInstance.SetActive(true);
        }

        void EnsureRealitySpatialTableManager()
        {
            if (m_RealitySpatialTableManager == null)
                m_RealitySpatialTableManager = GetComponent<RealitySpatialTableManager>();
            if (m_RealitySpatialTableManager == null)
                m_RealitySpatialTableManager = gameObject.AddComponent<RealitySpatialTableManager>();
            m_RealitySpatialTableManager.Configure(m_SpatialTablePrefab);
        }

        void EnsureRealityEnhancementFlow()
        {
            if (m_RealityEnhancementFlow == null)
                m_RealityEnhancementFlow = GetComponent<RealityEnhancementFlowController>();
            if (m_RealityEnhancementFlow == null)
                m_RealityEnhancementFlow = gameObject.AddComponent<RealityEnhancementFlowController>();
            var spawner = FindFirstObjectByType<TissueObjectTrackingSpawner>(FindObjectsInactive.Include);
            var imageSpawner = FindFirstObjectByType<ImageTrackingCapsuleSpawner>(FindObjectsInactive.Include);
            m_RealityEnhancementFlow.Configure(
                spawner,
                imageSpawner,
                m_MainCamera,
                m_GlassPanelPrefab,
                m_FontAsset,
                m_ChineseFontAsset,
                m_PushLeftHandGuidePrefab,
                m_PushRightHandGuidePrefab);
        }

        void SetExistingModules(bool objectTracking, bool spatialPhysics)
        {
            // Spatial mesh data and colliders remain active; only the sample's grey
            // visualization renderer is hidden for the finished Moodium experience.
            HideSpatialMeshVisualization();
            if (m_TrackedObjectManager != null)
                m_TrackedObjectManager.enabled = objectTracking && m_SelectionHasBag;
            if (m_TrackedImageManager != null)
                m_TrackedImageManager.enabled = objectTracking && m_SelectionHasBag;
            if (m_SpatialPhysicsDriver != null)
            {
                if (spatialPhysics)
                    m_SpatialPhysicsDriver.SetEmbeddedMoodiumMode(false);
                m_SpatialPhysicsDriver.enabled = spatialPhysics;
                if (!spatialPhysics)
                    m_SpatialPhysicsDriver.SetSpawningObjects(false);
            }
        }

        void SetMode(MoodiumMode mode)
        {
            m_CurrentMode = mode;
        }

        void ExitCreativeSpaceIfNeeded()
        {
            if (m_CurrentMode != MoodiumMode.NoObject || m_NoObjectModeController == null)
                return;

            SetCreativeSpacePhysicsEnabled(false);
            var fallingCount = m_SpatialPhysicsDriver != null
                ? m_SpatialPhysicsDriver.SpawnedObjectCount
                : 0;
            m_SpatialPhysicsDriver?.ClearSpawnedObjects();
            m_NoObjectModeController.ClearScene();
            var registryCount = MoodiumRuntimeObjectRegistry.ClearCreativeObjects();
            Debug.Log(
                $"[Moodium Flow] Exited Creative Space Mode; cleared {fallingCount} Spawn Objects instances. " +
                $"Cleared all user-placed objects and {registryCount} registered runtime objects/effects.");
        }

        void HideAllPanels()
        {
            if (m_ModePanel != null)
                m_ModePanel.SetActive(false);
            if (m_WorldSelectionPanel != null)
                m_WorldSelectionPanel.SetActive(false);
            if (m_ObjectWorldSelectionPanel != null)
                m_ObjectWorldSelectionPanel.SetActive(false);
            if (m_NoObjectPanel != null)
                m_NoObjectPanel.SetActive(false);
            if (m_InteractionPanel != null)
                m_InteractionPanel.SetActive(false);
            if (m_ObjectPanel != null)
                m_ObjectPanel.SetActive(false);
        }

        void SpawnFreeObject(GameObject prefab)
        {
            if (prefab == null || m_MainCamera == null)
                return;

            var cameraTransform = m_MainCamera.transform;
            var forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f)
                forward = cameraTransform.forward.normalized;

            var position = cameraTransform.position + forward * m_SpawnDistance;
            var rotation = Quaternion.LookRotation(forward, Vector3.up);
            var instance = Instantiate(prefab, position, rotation);
            instance.name = $"{prefab.name} (Free Mode)";
            var sourceScale = instance.transform.localScale;
            var scaleMultiplier = GetCreativeInitialScaleMultiplier(prefab);
            instance.transform.localScale = sourceScale * scaleMultiplier;
            MoodiumRuntimeObjectSetup.Configure(
                instance,
                allowMove: true,
                allowScale: true,
                disablePhysics: true);
            m_NoObjectModeController.Register(instance);

            Debug.Log(
                $"[Moodium Flow] Free object spawned: {prefab.name}, " +
                $"position={position}, prefabScale={sourceScale}, " +
                $"initialScaleMultiplier={scaleMultiplier:0.###}, scale={instance.transform.localScale}");
        }

        float GetCreativeInitialScaleMultiplier(GameObject prefab)
        {
            if (m_CreativePrefabScaleConfigs != null)
            {
                foreach (var config in m_CreativePrefabScaleConfigs)
                {
                    if (config.Prefab == prefab)
                        return config.InitialScaleMultiplier;
                }
            }

            // Safe defaults apply only to Creative Space instances. Inspector entries override them.
            return prefab.name switch
            {
                "HeartPrefab" => 0.5f,
                "ChocolatePrefab" => 0.7f,
                "CookiePrefab" => 0.7f,
                "StarPrefab" => 0.7f,
                "Macaron" => 0.7f,
                _ => 1f
            };
        }

        GameObject CreatePanelRoot(
            string panelName,
            float verticalOffset,
            bool addGlassBackground = true,
            bool addWindowHandle = true)
        {
            var panel = new GameObject(panelName);
            panel.transform.SetParent(m_UiRoot, false);
            if (addGlassBackground)
            {
                var panelSize = GetPanelSize(panelName);
                CreateGlassVisual(panel.transform, "Glass Panel Background", panelSize, 0);
            }
            if (addWindowHandle)
                CreateWindowHandle(panel.transform, GetPanelSize(panelName));
            PlacePanel(panel, verticalOffset);
            return panel;
        }

        void PlacePanel(GameObject panel, float verticalOffset, float distance = 1.1f)
        {
            if (panel == null)
                return;

            // Resolve the active XR camera for every transition. Cached camera transforms can be
            // stale after visionOS relocalization or XR Origin changes.
            var activeCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
            if (activeCamera == null)
                return;
            m_MainCamera = activeCamera;
            var cameraTransform = activeCamera.transform;
            var forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f)
                forward = cameraTransform.forward.normalized;

            panel.transform.SetPositionAndRotation(
                cameraTransform.position + forward * distance + Vector3.up * verticalOffset,
                Quaternion.LookRotation(forward, Vector3.up));
            Debug.Log($"[Moodium Window] Placed {panel.name} in front of camera. position={panel.transform.position}, forward={forward}");
        }

        void CreateWindowHandle(Transform panel, Vector2 panelSize)
        {
            var handle = new GameObject("VisionOS Window Handle");
            handle.transform.SetParent(panel, false);
            var handleY = -panelSize.y * 0.5f - 0.035f;
            if (panel.name == "Moodium World Selection" ||
                panel.name == "Moodium Object World Selection")
                handleY = -0.47f; // Clear the Back button at y=-0.36m.
            handle.transform.localPosition = new Vector3(0f, handleY, -0.012f);
            handle.transform.localRotation = Quaternion.identity;

            var handleRenderer = CreateGlassVisual(
                handle.transform,
                "Window Handle Visual",
                new Vector2(0.12f, 0.018f),
                35);
            if (handleRenderer != null)
            {
                var handleMaterial = handleRenderer.material;
                var color = new Color(0.9f, 0.93f, 1f, 0.72f);
                if (handleMaterial.HasProperty("_Tint"))
                    handleMaterial.SetColor("_Tint", color);
                else if (handleMaterial.HasProperty("_BaseColor"))
                    handleMaterial.SetColor("_BaseColor", color);
                else if (handleMaterial.HasProperty("_Color"))
                    handleMaterial.SetColor("_Color", color);
            }
            var collider = handle.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.16f, 0.045f, 0.025f);
            var drag = handle.AddComponent<MoodiumWindowHandle>();
            drag.Configure(panel);
        }

        public void EnsureOpeningWindowHandle(Transform displayRoot)
        {
            if (displayRoot == null || displayRoot.Find("VisionOS Opening Window Handle") != null)
                return;

            var handle = new GameObject("VisionOS Opening Window Handle");
            handle.transform.SetParent(displayRoot, false);
            // The opening display is a wide, floating video surface. Keep the handle
            // just below it so it never overlaps subtitles or sticker entry buttons.
            handle.transform.localPosition = new Vector3(0f, -0.54f, -0.012f);
            handle.transform.localRotation = Quaternion.identity;

            var renderer = CreateGlassVisual(
                handle.transform,
                "Opening Window Handle Visual",
                new Vector2(0.22f, 0.028f),
                35);
            if (renderer != null)
            {
                var material = renderer.material;
                var color = new Color(0.92f, 0.95f, 1f, 0.78f);
                if (material.HasProperty("_Tint"))
                    material.SetColor("_Tint", color);
                else if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", color);
                else if (material.HasProperty("_Color"))
                    material.SetColor("_Color", color);
            }

            var collider = handle.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.23f, 0.052f, 0.025f);
            var drag = handle.AddComponent<MoodiumWindowHandle>();
            drag.Configure(displayRoot);
            drag.MinDistanceFromViewer = OpeningWindowMinDistance;
        }

        TMP_Text CreateText(
            Transform parent,
            string text,
            Vector3 localPosition,
            float fontSize,
            Vector2 rectSize,
            TMP_FontAsset fontOverride = null)
        {
            var textObject = new GameObject($"Text - {text.Replace("\n", " ")}");
            textObject.transform.SetParent(parent, false);
            textObject.transform.localPosition = localPosition + new Vector3(0f, 0f, -0.035f);
            textObject.transform.localRotation = Quaternion.identity;
            textObject.transform.localScale = Vector3.one * 0.1f;

            var tmp = textObject.AddComponent<TextMeshPro>();
            var selectedFont = fontOverride != null ? fontOverride : m_FontAsset;
            if (selectedFont != null)
                tmp.font = selectedFont;
            else if (!m_LoggedMissingFont)
            {
                m_LoggedMissingFont = true;
                Debug.LogError("[Moodium UI] TMP font asset is missing. Run Moodium/Setup Flow Font.");
            }
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = rectSize;
            tmp.renderer.sortingOrder = 20;
            return tmp;
        }

        MoodiumSpatialButton CreateButton(
            Transform parent,
            string label,
            Vector3 localPosition,
            Vector2 size,
            System.Action onPressed)
        {
            var buttonObject = new GameObject($"Button - {label}");
            buttonObject.transform.SetParent(parent, false);
            buttonObject.transform.localPosition = localPosition;
            buttonObject.transform.localRotation = Quaternion.identity;

            var renderer = CreateGlassVisual(buttonObject.transform, "Button Glass Visual", size, 5);
            var hitTarget = buttonObject.AddComponent<BoxCollider>();
            hitTarget.size = new Vector3(size.x, size.y, 0.018f);

            var labelText = CreateText(
                buttonObject.transform,
                label,
                Vector3.zero,
                2.8f,
                new Vector2(Mathf.Max(2.5f, size.x * 9f), 0.85f));
            labelText.transform.localPosition = new Vector3(0f, 0f, -0.02f);

            var button = buttonObject.AddComponent<MoodiumSpatialButton>();
            button.Configure(labelText, renderer, onPressed);
            return button;
        }

        MoodiumSpatialButton CreateModeCard(
            Transform parent,
            string chineseTitle,
            string englishTitle,
            string description,
            Sprite artwork,
            Vector3 localPosition,
            System.Action onPressed)
        {
            var card = new GameObject($"Mode Card - {englishTitle}");
            card.transform.SetParent(parent, false);
            card.transform.localPosition = localPosition;
            card.transform.localRotation = Quaternion.Euler(0f, localPosition.x < 0f ? 3f : -3f, 0f);
            var size = new Vector2(0.4f, 0.48f);
            var background = CreateModeCardFrame(card.transform, size);
            CreateModeCardArtwork(card.transform, artwork, size);

            var chinese = CreateText(
                card.transform,
                chineseTitle,
                new Vector3(0.01f, -0.105f, -0.018f),
                3.05f,
                new Vector2(3.45f, 0.68f),
                m_ChineseFontAsset != null ? m_ChineseFontAsset : m_FontAsset);
            var english = CreateText(
                card.transform,
                englishTitle,
                new Vector3(0.01f, -0.153f, -0.018f),
                1.72f,
                new Vector2(3.45f, 0.52f));
            var detail = CreateText(
                card.transform,
                description,
                new Vector3(0.01f, -0.198f, -0.018f),
                1.22f,
                new Vector2(3.45f, 0.45f),
                m_ChineseFontAsset != null ? m_ChineseFontAsset : m_FontAsset);
            chinese.alignment = TextAlignmentOptions.MidlineLeft;
            english.alignment = TextAlignmentOptions.MidlineLeft;
            detail.alignment = TextAlignmentOptions.MidlineLeft;
            english.color = new Color(0.94f, 0.95f, 1f, 0.86f);
            detail.color = new Color(0.94f, 0.95f, 1f, 0.72f);
            ApplyModeCardTextReadability(chinese);
            ApplyModeCardTextReadability(english);
            ApplyModeCardTextReadability(detail, false);

            var button = card.AddComponent<MoodiumSpatialButton>();
            button.Configure(chinese, background, onPressed, true);
            return button;
        }

        Renderer CreateModeCardFrame(Transform card, Vector2 size)
        {
            var frame = Instantiate(m_GlassPanelPrefab, card, false);
            frame.name = "Mode Card Glow Frame";
            frame.transform.localScale = new Vector3(size.x / 0.72f, size.y / 0.43f, 1f);

            foreach (var text in frame.GetComponentsInChildren<TMP_Text>(true))
                text.gameObject.SetActive(false);

            Renderer edge = null;
            foreach (var renderer in frame.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.gameObject.name == "Glass Surface")
                {
                    renderer.gameObject.SetActive(false);
                    continue;
                }
                renderer.sortingOrder += 18;
                if (renderer.gameObject.name == "Soft Edge Highlight")
                    edge = renderer;
            }

            if (edge != null)
            {
                var hitTarget = edge.gameObject.AddComponent<BoxCollider>();
                hitTarget.size = new Vector3(0.72f, 0.43f, 0.025f);
                var hover = edge.gameObject.AddComponent<VisionOSHoverEffect>();
                hover.Type = VisionOSHoverEffect.EffectType.Highlight;
                hover.Color = new Color(0.67f, 0.72f, 1f, 1f);
                hover.IntensityMultiplier = 0.55f;
            }
            return edge;
        }

        void CreateModeCardArtwork(Transform card, Sprite artwork, Vector2 cardSize)
        {
            if (artwork == null)
            {
                Debug.LogWarning($"[Moodium UI] Mode card artwork is missing on {card.name}.");
                return;
            }

            var canvasObject = new GameObject("Background Image", typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(card, false);
            canvasObject.transform.localPosition = new Vector3(0f, 0f, -0.008f);
            canvasObject.transform.localRotation = Quaternion.identity;

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = m_MainCamera;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 12;

            const float pixelsPerMeter = 1000f;
            var rect = canvasObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(cardSize.x * 0.94f * pixelsPerMeter, cardSize.y * 0.94f * pixelsPerMeter);
            rect.localScale = Vector3.one / pixelsPerMeter;

            var imageObject = new GameObject("Artwork", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.transform.SetParent(canvasObject.transform, false);
            var imageRect = imageObject.GetComponent<RectTransform>();
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = Vector2.zero;
            imageRect.offsetMax = Vector2.zero;

            var image = imageObject.GetComponent<Image>();
            image.sprite = artwork;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = Color.white;
            image.material = null;

            var tintObject = new GameObject("Glass Tint Overlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            tintObject.transform.SetParent(canvasObject.transform, false);
            var tintRect = tintObject.GetComponent<RectTransform>();
            tintRect.anchorMin = Vector2.zero;
            tintRect.anchorMax = Vector2.one;
            tintRect.offsetMin = Vector2.zero;
            tintRect.offsetMax = Vector2.zero;
            var tint = tintObject.GetComponent<Image>();
            tint.sprite = artwork;
            tint.preserveAspect = true;
            tint.raycastTarget = false;
            tint.color = new Color(0.16f, 0.1f, 0.3f, 0.12f);
            tint.material = null;
        }

        static void ApplyModeCardTextReadability(TMP_Text text, bool bold = true)
        {
            if (text.color.a >= 0.99f)
                text.color = Color.white;
            text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            text.outlineColor = Color.clear;
            text.outlineWidth = 0f;
            text.GetComponent<Renderer>().sortingOrder = 30;
        }

        Renderer CreateGlassVisual(Transform parent, string visualName, Vector2 size, int sortingOrder)
        {
            if (m_GlassPanelPrefab == null)
            {
                Debug.LogError("[Moodium UI] Glass Panel Prefab is missing. Run Moodium/Setup Glass Panel Test.");
                return null;
            }

            var visual = Instantiate(m_GlassPanelPrefab, parent, false);
            visual.name = visualName;
            visual.transform.localScale = new Vector3(size.x / 0.72f, size.y / 0.43f, 1f);

            foreach (var text in visual.GetComponentsInChildren<TMP_Text>(true))
                text.gameObject.SetActive(false);

            var renderers = visual.GetComponentsInChildren<MeshRenderer>(true);
            Renderer surface = null;
            foreach (var meshRenderer in renderers)
            {
                meshRenderer.sortingOrder += sortingOrder;
                if (meshRenderer.gameObject.name == "Glass Surface")
                    surface = meshRenderer;
            }
            return surface != null ? surface : (renderers.Length > 0 ? renderers[0] : null);
        }

        Material GetPickerGlassMaterial()
        {
            if (m_ObjectPickerGlassMaterial != null)
                return m_ObjectPickerGlassMaterial;
            if (m_GlassPanelPrefab == null)
                return null;
            foreach (var renderer in m_GlassPanelPrefab.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.sharedMaterial != null && renderer.sharedMaterial.name.Contains("Glass"))
                    return renderer.sharedMaterial;
            }
            return m_GlassPanelPrefab.GetComponentInChildren<Renderer>(true)?.sharedMaterial;
        }

        static Vector2 GetPanelSize(string panelName)
        {
            return panelName switch
            {
                "Moodium Mode Selection" => new Vector2(0.86f, 0.64f),
                "Moodium World Selection" => new Vector2(0.86f, 0.68f),
                "Moodium Object World Selection" => new Vector2(0.86f, 0.68f),
                "Moodium Model Library" => new Vector2(0.9f, 1.02f),
                "Moodium Interaction Mode" => new Vector2(0.76f, 0.5f),
                "Moodium Reality Enhancement Mode" => new Vector2(0.76f, 0.52f),
                "Spatial Physics Back" => new Vector2(0.54f, 0.2f),
                _ => new Vector2(0.8f, 0.55f)
            };
        }

        static string GetDisplayName(string prefabName)
        {
            return prefabName
                .Replace("Prefab", string.Empty)
                .Replace("_", " ")
                .Trim();
        }

        void HideSpatialMeshVisualization()
        {
            if (m_SpatialMeshManager == null)
                return;

            foreach (var renderer in m_SpatialMeshManager.GetComponentsInChildren<MeshRenderer>(true))
            {
                // Candy transformation is a separately managed visual-only overlay.
                // Never disable that layer here; only hide ARMeshManager's source mesh.
                if (renderer.gameObject.name == "Moodium Candy Visual Overlay")
                    continue;
                if (renderer.GetComponent<MeshCollider>() != null ||
                    renderer.gameObject.name.StartsWith("AR Default Mesh", System.StringComparison.Ordinal))
                    renderer.enabled = false;
            }
        }
    }
}
