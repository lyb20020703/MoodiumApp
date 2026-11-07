using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Moodium.Opening;
using Moodium.MoodCheck;
using Moodium.UI;

namespace Moodium.Flow
{
    /// <summary>
    /// Displays world-entry models for every recognized Moodium sticker while the
    /// opening video is in its post-tutorial selection loop.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StickerWorldSelectionController : MonoBehaviour
    {
        [Header("Image Tracking")]
        [SerializeField] ARTrackedImageManager m_TrackedImageManager;
        [SerializeField] string m_CandyReferenceImageName = "CandyWorldPhoneTracking";
        [SerializeField] string m_NatureReferenceImageName = "NatureWorldPhoneTracking";

        [Header("World Entries")]
        [SerializeField] GameObject m_CandyWorldUiPrefab;
        [SerializeField] GameObject m_NatureWorldUiPrefab;
        [SerializeField] GameObject m_MoodiMascotPrefab;
        [SerializeField] MoodiumWorldDefinition m_CandyWorld;
        [SerializeField] MoodiumWorldDefinition m_NatureWorld;
        [SerializeField] TMP_FontAsset m_ChineseFont;
        [SerializeField] TMP_FontAsset m_EnglishFont;
        [SerializeField] Vector3 m_EntryLocalPosition = new(0f, 0.12f, 0.10f);
        [SerializeField] Vector3 m_EntryLocalEulerAngles;
        [SerializeField] Vector3 m_EntryLocalScale = Vector3.one;
        [SerializeField] float m_ButtonLocalHeight = 0.18f;
        [SerializeField] float m_ButtonLocalForwardOffset = -0.03f;

        readonly Dictionary<TrackableId, StickerWorldEntry> m_Entries = new();
        // Moodi's spoken invitation when a world entry appears (voice lines S1c / S1n / S2).
        readonly Dictionary<string, float> m_LastMoodiInvite = new();
        float m_LastAnyMoodiInvite = -100f;
        public const float MoodiInviteGap = 4f;
        public const float MoodiInviteWorldCooldown = 20f;
        OpeningVideoSession m_Session;
        TextMeshPro m_Instruction;
        // Head-following oval frame → step back → found → slide-the-knob demo (StickerAlignGuide).
        StickerAlignGuide m_Guide;
        public StickerAlignGuide Guide => m_Guide;
        MoodiumSpatialButton m_MoodCheckButton;
        MoodCheckSample m_MoodCheckSummary;
        MoodCheckContext m_MoodCheckContext;
        Material m_EntryGlassMaterial;
        string m_RecommendedWorldId;
        bool m_Active;
        bool m_English;

        public event Action<StickerWorldEntry> WorldEntrySelected;
        public event Action MoodCheckRequested;
        public int ActiveEntryCount => m_Entries.Count;
        public bool HasVisibleEntry
        {
            get
            {
                foreach (var entry in m_Entries.Values)
                    if (entry.Root != null && entry.Root.activeInHierarchy) return true;
                return false;
            }
        }
        public bool IsSelectionActive => m_Active;

        public GameObject CandyWorldUiPrefab => m_CandyWorldUiPrefab;
        public GameObject NatureWorldUiPrefab => m_NatureWorldUiPrefab;
        public Vector3 EntryLocalPosition => m_EntryLocalPosition;
        public MoodiumSpatialButton MoodCheckButton => m_MoodCheckButton;

        /// <summary>Line Moodi says when a world entry appears: the recommendation for the recommended world,
        /// otherwise the world's own invitation.</summary>
        public static string MoodiInviteLine(string worldId, string recommendedWorldId)
        {
            if (!string.IsNullOrEmpty(recommendedWorldId) &&
                string.Equals(worldId, recommendedWorldId, StringComparison.OrdinalIgnoreCase)) return "S2";
            return string.Equals(worldId, "candy", StringComparison.OrdinalIgnoreCase) ? "S1c" : "S1n";
        }

        /// <summary>At most one Moodi speaks every few seconds, and the same world waits longer before repeating.</summary>
        public static bool CanMoodiInvite(float now, float lastAny, float lastThisWorld, bool ignoreWorldCooldown = false) =>
            now - lastAny >= MoodiInviteGap && (ignoreWorldCooldown || now - lastThisWorld >= MoodiInviteWorldCooldown);

        public static Vector3 GetCompensatedLocalScale(float targetWorldScale, Vector3 parentScale) => new(
            SafeScaleDivide(targetWorldScale, parentScale.x),
            SafeScaleDivide(targetWorldScale, parentScale.y),
            SafeScaleDivide(targetWorldScale, parentScale.z));

        public void Configure(
            ARTrackedImageManager trackedImageManager,
            GameObject candyWorldUiPrefab,
            GameObject natureWorldUiPrefab,
            MoodiumWorldDefinition candyWorld,
            MoodiumWorldDefinition natureWorld)
        {
            if (m_Active && m_TrackedImageManager != null)
                m_TrackedImageManager.trackablesChanged.RemoveListener(OnTrackedImagesChanged);
            m_TrackedImageManager = trackedImageManager;
            m_CandyWorldUiPrefab = candyWorldUiPrefab;
            m_NatureWorldUiPrefab = natureWorldUiPrefab;
            m_CandyWorld = candyWorld;
            m_NatureWorld = natureWorld;
            if (m_Active && m_TrackedImageManager != null)
                m_TrackedImageManager.trackablesChanged.AddListener(OnTrackedImagesChanged);
        }

        public void ConfigureForTests(
            GameObject candyWorldUiPrefab,
            GameObject natureWorldUiPrefab,
            MoodiumWorldDefinition candyWorld,
            MoodiumWorldDefinition natureWorld)
        {
            Configure(null, candyWorldUiPrefab, natureWorldUiPrefab, candyWorld, natureWorld);
        }

        public void ConfigureFonts(TMP_FontAsset chineseFont, TMP_FontAsset englishFont)
        {
            m_ChineseFont = chineseFont;
            m_EnglishFont = englishFont;
        }

        public void ConfigureMascotPrefab(GameObject prefab) => m_MoodiMascotPrefab = prefab;

        public void ConfigureMoodCheck(MoodCheckContext context) => m_MoodCheckContext = context;

        public void ConfigureEntryGlassMaterial(Material material) => m_EntryGlassMaterial = material;

        public void SetMoodCheckSummary(MoodCheckSample sample)
        {
            m_MoodCheckSummary = sample;
            m_RecommendedWorldId = sample?.recommendedWorldId;
            CreateOrUpdateMoodCheckButton();
            UpdateAllRecommendationTags();
        }

        public void SetRecommendedWorld(string worldId)
        {
            m_RecommendedWorldId = worldId;
            UpdateAllRecommendationTags();
        }

        public void Begin(OpeningVideoSession session)
        {
            m_Session = session;
            m_English = session != null && session.English;
            if (m_Active)
                return;
            m_Active = true;
            CreateOrUpdateInstruction();
            if (Application.isPlaying && m_Guide == null)
                m_Guide = StickerAlignGuide.Create(Camera.main, m_English, VisibleSlider);
            SeekSelectionLoopStart();
            if (m_TrackedImageManager != null)
            {
                m_TrackedImageManager.trackablesChanged.AddListener(OnTrackedImagesChanged);
                RefreshTrackedImages();
                Debug.Log($"[Moodium Sticker Selection] Subscribed before Image Tracking is enabled. Existing anchors: {m_TrackedImageManager.trackables.count}.");
            }
        }

        bool m_MoodCheckSuspended;
        public void SetMoodCheckSuspended(bool suspended)
        {
            m_MoodCheckSuspended = suspended;
            m_Guide?.SetSuspended(suspended);
            foreach (var entry in m_Entries.Values)
                if (entry.Root != null) entry.Root.SetActive(!suspended);
            if (!suspended) RefreshTrackedImages();
        }

        /// <summary>
        /// Rehydrates world entries from ARFoundation's current trackable collection.
        /// visionOS can publish an image anchor before a runtime listener is attached,
        /// so the app-flow calls this over the first few enabled frames as a safe catch-up.
        /// </summary>
        public void RefreshTrackedImages()
        {
            if (!m_Active || m_MoodCheckSuspended || m_TrackedImageManager == null)
                return;

            foreach (var image in m_TrackedImageManager.trackables)
                HandleTrackedImage(image);
        }

        public void End()
        {
            m_Loop = null;
            if (m_TrackedImageManager != null)
                m_TrackedImageManager.trackablesChanged.RemoveListener(OnTrackedImagesChanged);
            m_Active = false;
            m_MoodCheckSuspended = false;
            m_Session = null;
            if (m_Instruction != null)
                SensoryBagBranchUI.Dispose(m_Instruction.gameObject);
            m_Instruction = null;
            if (m_Guide != null)
                SensoryBagBranchUI.Dispose(m_Guide.gameObject);
            m_Guide = null;
            if (m_MoodCheckButton != null)
                SensoryBagBranchUI.Dispose(m_MoodCheckButton.gameObject);
            m_MoodCheckButton = null;
            foreach (var entry in m_Entries.Values)
                if (entry.Root != null)
                    SensoryBagBranchUI.Dispose(entry.Root);
            m_Entries.Clear();
        }

        void OnDestroy()
        {
            End();
        }

        void Update()
        {
            if (!m_Active || m_Session?.Player == null)
                return;
            var player = m_Session.Player;
            if (m_Loop == null) { SeekSelectionLoopStart(); return; }
            if (!player.isPrepared)
            {
                if (!m_Preparing) { m_Preparing = true; player.Prepare(); }
                return;
            }
            if (m_Preparing) { m_Preparing = false; IssueLoopSeek(player); return; }
            var frame = player.frame >= 0
                ? player.frame
                : (long)Math.Round(player.time * OpeningVideoTimeline.FramesPerSecond);
            switch (m_Loop.Tick(Time.realtimeSinceStartup, frame, player.isPlaying))
            {
                case VideoLoopAction.Play:
                    player.Play();
                    break;
                case VideoLoopAction.Seek:
                    IssueLoopSeek(player);
                    break;
            }
        }

        void OnTrackedImagesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> changes)
        {
            Debug.Log($"[Moodium Sticker Selection] Image update received: added={changes.added.Count}, updated={changes.updated.Count}, removed={changes.removed.Count}.");
            foreach (var image in changes.added)
                HandleTrackedImage(image);
            foreach (var image in changes.updated)
                HandleTrackedImage(image);
            foreach (var image in changes.removed)
                RemoveEntry(image.Value.trackableId);
        }

        void HandleTrackedImage(ARTrackedImage image)
        {
            Debug.Log($"[Moodium Sticker Selection] Anchor '{image.referenceImage.name}' state={image.trackingState}, id={image.trackableId}.");
            HandleTrackedImageUpdate(image.trackableId, image.referenceImage.name, image.transform, image.trackingState);
        }

        public void HandleTrackedImageUpdate(
            TrackableId trackableId,
            string referenceImageName,
            Transform anchor,
            TrackingState trackingState)
        {
            if (m_MoodCheckSuspended) return;
            if (!m_Active || !TryGetDefinition(referenceImageName, out var prefab, out var world))
            {
                if (m_Active)
                    Debug.LogWarning($"[Moodium Sticker Selection] Ignored image '{referenceImageName}'. No configured Moodium world prefab/definition was available.");
                return;
            }

            if (trackingState == TrackingState.None)
            {
                if (m_Entries.TryGetValue(trackableId, out var hidden) && hidden.Root != null)
                    hidden.Root.SetActive(false);
                return;
            }

            var appeared = false;
            if (!m_Entries.TryGetValue(trackableId, out var entry) || entry.Root == null)
            {
                appeared = true;
                var root = Instantiate(prefab, anchor);
                root.name = $"Sticker World Entry - {world.DisplayName}";
                root.transform.localPosition = m_EntryLocalPosition;
                root.transform.localRotation = Quaternion.Euler(m_EntryLocalEulerAngles);
                root.transform.localScale = m_EntryLocalScale;
                root.AddComponent<StickerWorldEntryUprightFollower>()
                    .Configure(anchor, m_EntryLocalPosition, m_EntryLocalScale);
                MoodiSelectionMascotController.AttachTo(root);
                CreateEnterButton(root.transform, trackableId, world);
                entry = new StickerWorldEntry(trackableId, referenceImageName, world, anchor, root);
                m_Entries[trackableId] = entry;
                UpdateRecommendationTag(entry);
                Debug.Log($"[Moodium Sticker Selection] Spawned '{world.DisplayName}' entry for '{referenceImageName}' ({trackableId}).");
            }
            else if (entry.Root.transform.parent != anchor)
            {
                entry.Root.transform.SetParent(anchor, false);
                entry.Root.transform.localPosition = m_EntryLocalPosition;
                entry.Root.transform.localRotation = Quaternion.Euler(m_EntryLocalEulerAngles);
                entry.Root.transform.localScale = m_EntryLocalScale;
                var follower = entry.Root.GetComponent<StickerWorldEntryUprightFollower>() ??
                    entry.Root.AddComponent<StickerWorldEntryUprightFollower>();
                follower.Configure(anchor, m_EntryLocalPosition, m_EntryLocalScale);
            }

            if (!entry.Root.activeSelf) appeared = true;
            entry.Root.SetActive(true);
            if (appeared) MoodiSays(entry, MoodiInviteLine(entry.World != null ? entry.World.WorldId : null, m_RecommendedWorldId), false);
        }

        void MoodiSays(StickerWorldEntry entry, string lineId, bool ignoreWorldCooldown)
        {
            if (entry?.Root == null || string.IsNullOrEmpty(lineId)) return;
            var key = entry.World != null && entry.World.WorldId != null ? entry.World.WorldId : entry.ReferenceImageName ?? "";
            if (!m_LastMoodiInvite.TryGetValue(key, out var last)) last = -100f;
            var now = Time.time;
            if (!CanMoodiInvite(now, m_LastAnyMoodiInvite, last, ignoreWorldCooldown)) return;
            var moodi = entry.Root.GetComponentInChildren<Moodium.Characters.MoodiCompanionAnimator>(true);
            if (moodi == null) return;
            if (!moodi.TryGetComponent(out Moodium.Characters.MoodiVoice voice))
                voice = moodi.gameObject.AddComponent<Moodium.Characters.MoodiVoice>();
            voice.EnglishOverride = m_English;
            voice.SayAfter(lineId, .6f); // let the entry appear and Moodi start its wave first
            m_LastAnyMoodiInvite = now;
            m_LastMoodiInvite[key] = now;
        }

        /// <summary>The slider of the first world entry on screen (the sticker has been recognised), or null.</summary>
        public GlassSlideToEnter VisibleSlider()
        {
            foreach (var entry in m_Entries.Values)
                if (entry.Root != null && entry.Root.activeInHierarchy)
                {
                    var slider = entry.Root.GetComponentInChildren<GlassSlideToEnter>();
                    if (slider != null) return slider;
                }
            return null;
        }

        public StickerWorldEntry GetEntry(TrackableId trackableId)
        {
            m_Entries.TryGetValue(trackableId, out var entry);
            return entry;
        }

        public void SelectEntry(TrackableId trackableId)
        {
            if (!m_Active || m_MoodCheckSuspended) return;
            if (!m_Active || !m_Entries.TryGetValue(trackableId, out var entry))
                return;
            WorldEntrySelected?.Invoke(entry);
        }

        void RemoveEntry(TrackableId trackableId)
        {
            if (!m_Entries.Remove(trackableId, out var entry) || entry.Root == null)
                return;
            Destroy(entry.Root);
        }

        bool TryGetDefinition(string referenceImageName, out GameObject prefab, out MoodiumWorldDefinition world)
        {
            if (MoodiumImageReferenceName.Matches(referenceImageName, m_CandyReferenceImageName))
            {
                prefab = m_CandyWorldUiPrefab;
                world = m_CandyWorld;
            }
            else if (MoodiumImageReferenceName.Matches(referenceImageName, m_NatureReferenceImageName))
            {
                prefab = m_NatureWorldUiPrefab;
                world = m_NatureWorld;
            }
            else
            {
                prefab = null;
                world = null;
                return false;
            }

            return prefab != null && world != null;
        }

        void CreateEnterButton(Transform parent, TrackableId trackableId, MoodiumWorldDefinition world)
        {
            var buttonRoot = new GameObject("Enter World Slider");
            buttonRoot.transform.SetParent(parent, false);
            buttonRoot.transform.localPosition = new Vector3(0f, m_ButtonLocalHeight, m_ButtonLocalForwardOffset);
            var isNature = world != null && string.Equals(world.WorldId, "nature", StringComparison.OrdinalIgnoreCase);
            var slider = buttonRoot.AddComponent<GlassSlideToEnter>();
            slider.Configure(
                EntryButtonLabel(world),
                isNature ? GlassSlideToEnter.Theme.Nature : GlassSlideToEnter.Theme.Candy,
                m_English ? m_EnglishFont : m_ChineseFont,
                m_EntryGlassMaterial,
                m_English ? "Entering…" : "正在进入…");
            slider.Completed += () => SelectEntry(trackableId);
            // One index finger can drag the knob like on a phone (pinching still works): the same fingertip bridge the
            // no-bag world carousel, the journal and calibration already use. Within 12 mm it grabs; 3 cm away it lets go.
            if (parent.GetComponent<SensoryBagDirectTouchInput>() == null)
                parent.gameObject.AddComponent<SensoryBagDirectTouchInput>();
        }

        string EntryButtonLabel(MoodiumWorldDefinition world)
        {
            if (world == null)
                return m_English ? "Enter" : "进入世界";
            if (m_English)
                return $"Enter {world.DisplayName}";
            var displayName = world.WorldId switch
            {
                "candy" => "糖果世界",
                "nature" => "自然世界",
                _ => world.DisplayName
            };
            return $"进入{displayName}";
        }

        void CreateOrUpdateInstruction()
        {
            if (m_Session?.DisplayRoot == null)
                return;
            if (m_Instruction == null)
            {
                var root = new GameObject("Sticker Selection Instruction", typeof(TextMeshPro));
                root.transform.SetParent(m_Session.DisplayRoot, false);
                root.transform.localPosition = new Vector3(0f, -0.35f, -0.04f);
                root.transform.localScale = GetCompensatedLocalScale(0.045f, m_Session.DisplayRoot.localScale);
                m_Instruction = root.GetComponent<TextMeshPro>();
                m_Instruction.font = m_English ? m_EnglishFont : m_ChineseFont;
                m_Instruction.fontSize = 7f;
                m_Instruction.alignment = TextAlignmentOptions.Center;
                m_Instruction.rectTransform.sizeDelta = new Vector2(24f, 4f);
                m_Instruction.color = Color.white;
                if (Application.isPlaying)
                {
                    m_Instruction.outlineWidth = 0.08f;
                    m_Instruction.outlineColor = new Color(0.08f, 0.02f, 0.16f, 0.85f);
                }
            }
            m_Instruction.text = m_English ? "Look at the sticker and fit it in the frame" : "看向贴纸，把它放进框里";
            CreateOrUpdateMoodCheckButton();
        }

        void CreateOrUpdateMoodCheckButton()
        {
            if (m_Session?.DisplayRoot == null)
                return;
            if (m_MoodCheckButton == null)
            {
                var root = new GameObject("Mood Check Entry");
                root.transform.SetParent(m_Session.DisplayRoot, false);
                root.transform.localScale = GetCompensatedLocalScale(1f, m_Session.DisplayRoot.lossyScale);
                root.transform.localPosition = m_Instruction != null
                    ? m_Session.DisplayRoot.InverseTransformPoint(m_Instruction.transform.position -
                        m_Instruction.transform.up * .05f)
                    : new Vector3(0f, -.41f, -.04f);
                m_MoodCheckButton = root.AddComponent<MoodEntryChip>().Initialize(this,
                    m_English ? m_EnglishFont : m_ChineseFont, () => MoodCheckRequested?.Invoke());
            }
            var text = MoodCheckCopy.Get("prompt.entry", m_English);
            if (m_MoodCheckSummary != null)
            {
                var mood = ShortMoodName(m_MoodCheckSummary.mood);
                var world = WorldName(m_MoodCheckSummary.recommendedWorldId);
                text = m_English ? $"Now: {mood} · {world} recommended" : $"现在：{mood} · 推荐{world}";
            }
            if (m_MoodCheckButton.Label != null)
                m_MoodCheckButton.Label.text = text;
            m_MoodCheckButton.GetComponent<MoodEntryChip>()?.SetContent(m_English, m_MoodCheckSummary);
        }

        void UpdateAllRecommendationTags()
        {
            foreach (var entry in m_Entries.Values)
                UpdateRecommendationTag(entry);
        }

        void UpdateRecommendationTag(StickerWorldEntry entry)
        {
            if (entry?.Root == null)
                return;
            var existing = entry.Root.transform.Find("Moodi Recommendation Tag");
            var matches = entry.World != null && !string.IsNullOrEmpty(m_RecommendedWorldId) &&
                          string.Equals(entry.World.WorldId, m_RecommendedWorldId, StringComparison.OrdinalIgnoreCase);
            if (!matches)
            {
                if (existing != null) Destroy(existing.gameObject);
                return;
            }
            if (existing != null)
                return;
            var tag = new GameObject("Moodi Recommendation Tag", typeof(TextMeshPro));
            tag.transform.SetParent(entry.Root.transform, false);
            tag.transform.localPosition = new Vector3(0f, m_ButtonLocalHeight + .055f, m_ButtonLocalForwardOffset);
            tag.transform.localScale = Vector3.one;
            var label = tag.GetComponent<TextMeshPro>();
            label.text = "★ " + MoodCheckCopy.Get("tag.pick", m_English);
            label.font = m_English ? m_EnglishFont : m_ChineseFont;
            label.fontSize = .025f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(1f, 1f, 1f, .95f);
            label.rectTransform.sizeDelta = new Vector2(.18f, .025f);
            tag.AddComponent<MoodRecommendationTagPulse>();
            var moodi=entry.Root.GetComponentInChildren<Moodium.Characters.MoodiCompanionAnimator>(true);
            if(moodi!=null)moodi.PlayHappy();
            MoodiSays(entry, "S2", true);
        }

        string ShortMoodName(MoodKind mood)
        {
            var value = MoodCheckCopy.Get("mood." + mood.ToString().ToLowerInvariant(), m_English);
            var separator = value.IndexOf(" · ", StringComparison.Ordinal);
            return separator >= 0 ? value.Substring(0, separator) : value;
        }

        string WorldName(string worldId)
        {
            if (string.IsNullOrEmpty(worldId)) return m_English ? "either world" : "任一世界";
            if (m_English) return worldId == "candy" ? "Candy World" : "Nature World";
            return worldId == "candy" ? "糖果世界" : "自然世界";
        }

        // Sticker-selection loop (frames 1240–1380). The driver decides; this class only talks to the VideoPlayer.
        VideoSegmentLoopDriver m_Loop;
        bool m_Preparing;
        public VideoSegmentLoopDriver SelectionLoop => m_Loop;

        void SeekSelectionLoopStart()
        {
            var player = m_Session?.Player;
            if (player == null)
                return;
            if (!Application.isPlaying)
            {
                player.time = OpeningVideoTimeline.FrameToSeconds(
                    OpeningVideoTimeline.StickerSelectionLoop.StartFrame);
                player.Play();
                return;
            }
            var loop = OpeningVideoTimeline.StickerSelectionLoop;
            m_Loop = new VideoSegmentLoopDriver(loop.StartFrame, loop.EndFrame);
            m_Preparing = false;
            if (!player.isPrepared)
            {
                // Update seeks once the player is prepared.
                m_Preparing = true;
                player.Prepare();
                return;
            }
            IssueLoopSeek(player);
        }

        void IssueLoopSeek(UnityEngine.Video.VideoPlayer player)
        {
            // Never Pause() first: a paused AVFoundation player may not present the sought frame or raise
            // seekCompleted until it plays again, which is how the picture used to stay on one frame.
            player.frame = m_Loop.StartFrame;
            player.Play();
            m_Loop.NotifySeekIssued(Time.realtimeSinceStartup);
        }

        static float SafeScaleDivide(float value, float divisor) =>
            Mathf.Abs(divisor) > 0.0001f ? value / divisor : value;

    }
}
