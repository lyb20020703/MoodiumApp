using System.Collections.Generic;
using System;
using Moodium.Flow;
using Moodium.Opening;
using TMPro;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace Moodium.Calibration
{
    /// <summary>
    /// Bridges the existing image-tracking pipeline to Phase 1 calibration sessions.
    /// It deliberately owns no AR session and keeps all saved values anchor-local.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SensoryBagCalibrationCoordinator : MonoBehaviour
    {
        [Header("Image Tracking")]
        [SerializeField] ImageTrackingCapsuleSpawner m_ImageSpawner;
        [SerializeField] string m_CandyReferenceImageName = "CandyWorldPhoneTracking";
        [SerializeField] string m_NatureReferenceImageName = "NatureWorldPhoneTracking";

        [Header("Calibration Defaults")]
        [SerializeField, Min(0.01f)] float m_DefaultWidth = 0.40f;
        [SerializeField, Min(0.01f)] float m_DefaultHeight = 0.25f;
        [SerializeField, Min(0.005f)] float m_DefaultDepth = 0.08f;
        [SerializeField] Vector2 m_DefaultCenterOffset;
        [SerializeField, Min(0.01f)] float m_MinWidth = 0.08f;
        [SerializeField, Min(0.01f)] float m_MaxWidth = 0.6f;
        [SerializeField, Min(0.01f)] float m_MinHeight = 0.06f;
        [SerializeField, Min(0.01f)] float m_MaxHeight = 0.45f;

        readonly Dictionary<TrackableId, SensoryBagCalibrator> m_Sessions = new();
        readonly Dictionary<TrackableId, SensoryBagCalibrationData> m_ConfirmedData = new();
        readonly Dictionary<TrackableId, CalibratedSensoryBagPresentation> m_Presentations = new();
        readonly HashSet<Moodium.Reality.PushHandInteractionGuide> m_PushHandGuides = new();
        readonly Dictionary<SensoryBagWorldKind, GameObject> m_VisualPrefabs = new();
        GameObject m_PushLeftHandPrefab;
        GameObject m_PushRightHandPrefab;
        TMP_FontAsset m_ChineseFont;
        TMP_FontAsset m_EnglishFont;
        Sprite m_RoundedButtonSprite;
        StickerWorldEntry m_SelectedEntry;
        OpeningVideoSession m_OpeningVideoSession;
        FrameAdjustingTutorialPlayback m_FrameAdjustingTutorial;
        StickerWorldEntry m_CompletedEntry;
        bool m_UsesImageTracking;
        bool m_LoggedMissingTutorial;

        public event Action<StickerWorldEntry, SensoryBagCalibrationData> CalibrationCompleted;

        public int ActiveSessionCount => m_Sessions.Count;

        public void ConfigureVisualPrefab(SensoryBagWorldKind worldKind, GameObject prefab) =>
            m_VisualPrefabs[worldKind] = prefab;

        public void ConfigurePushHandGuides(GameObject leftPrefab, GameObject rightPrefab)
        {
            m_PushLeftHandPrefab = leftPrefab;
            m_PushRightHandPrefab = rightPrefab;
        }

        public void ConfigureFonts(TMP_FontAsset chineseFont, TMP_FontAsset englishFont)
        {
            m_ChineseFont = chineseFont;
            m_EnglishFont = englishFont;
        }

        public void ConfigureButtonStyle(Sprite roundedButtonSprite)
        {
            m_RoundedButtonSprite = roundedButtonSprite;
            foreach (var session in m_Sessions.Values)
                session?.ConfigureButtonStyle(roundedButtonSprite);
        }

        /// <summary>Supplies the already-positioned opening-video surface and selected language.</summary>
        public void ConfigureFrameAdjustingTutorial(OpeningVideoSession session)
        {
            m_OpeningVideoSession = session;
        }

        public CalibratedSensoryBagPresentation GetPresentation(TrackableId trackableId)
        {
            m_Presentations.TryGetValue(trackableId, out var presentation);
            return presentation;
        }

        public void ClearPresentations()
        {
            StopFrameAdjustingTutorial();
            foreach (var guide in m_PushHandGuides)
                if (guide != null)
                    DisposeObject(guide.gameObject);
            m_PushHandGuides.Clear();
            foreach (var presentation in m_Presentations.Values)
                if (presentation != null)
                    DisposeObject(presentation.gameObject);
            m_Presentations.Clear();
            m_CompletedEntry = null;
        }

        void Awake()
        {
            if (m_ImageSpawner == null)
                m_ImageSpawner = GetComponent<ImageTrackingCapsuleSpawner>();
            m_ImageSpawner?.ConfigureCalibrationCoordinator(this);
        }

        void OnDestroy()
        {
            foreach (var session in m_Sessions.Values)
                if (session != null)
                    DisposeObject(session.gameObject);
            m_Sessions.Clear();
            ClearPresentations();
        }

        public SensoryBagCalibrationSettings CreateSettings() => new(
            m_DefaultWidth, m_DefaultHeight, m_DefaultDepth, m_DefaultCenterOffset,
            m_MinWidth, m_MaxWidth, m_MinHeight, m_MaxHeight);

        public bool HandlesReferenceImage(string referenceImageName)
        {
            return TryGetWorldKind(referenceImageName, out _);
        }

        public bool HandleImageAnchorUpdate(
            TrackableId trackableId,
            string referenceImageName,
            Transform anchor,
            TrackingState trackingState)
        {
            if (!m_UsesImageTracking || m_SelectedEntry == null ||
                trackableId != m_SelectedEntry.TrackableId ||
                !TryGetWorldKind(referenceImageName, out var worldKind))
                return false;

            if (trackingState == TrackingState.None)
            {
                if (m_Sessions.TryGetValue(trackableId, out var existing))
                    existing.SetTrackingVisible(false);
                GetPresentation(trackableId)?.SetTrackingVisible(false);
                return true;
            }

            if (m_Presentations.TryGetValue(trackableId, out var existingPresentation) && existingPresentation != null)
            {
                existingPresentation.SetTrackingVisible(true);
                return true;
            }

            if (!m_Sessions.TryGetValue(trackableId, out var calibrator) || calibrator == null)
            {
                var sessionRoot = new GameObject($"Sensory Bag Calibration - {referenceImageName}");
                sessionRoot.transform.SetParent(transform, false);
                calibrator = sessionRoot.AddComponent<SensoryBagCalibrator>();
                calibrator.ConfigureFonts(m_ChineseFont, m_EnglishFont);
                calibrator.ConfigureButtonStyle(m_RoundedButtonSprite);
                var initial = SensoryBagCalibrationData.CreateDefault(referenceImageName, worldKind, CreateSettings());
                calibrator.Begin(anchor, initial, CreateSettings());
                sessionRoot.AddComponent<SensoryBagDirectTouchInput>();
                calibrator.CalibrationConfirmed += data =>
                {
                    CompleteCalibration(m_SelectedEntry, anchor, data);
                };
                m_Sessions[trackableId] = calibrator;
                StartFrameAdjustingTutorial();
            }
            else
            {
                calibrator.SetAnchor(anchor);
                calibrator.SetTrackingVisible(true);
            }

            return true;
        }

        public void BeginCalibration(StickerWorldEntry entry)
        {
            if (entry == null || entry.Anchor == null || !TryGetWorldKind(entry.ReferenceImageName, out _))
                return;
            EndCalibrationMode();
            m_SelectedEntry = entry;
            m_CompletedEntry = null;
            m_UsesImageTracking = true;
            HandleImageAnchorUpdate(
                entry.TrackableId,
                entry.ReferenceImageName,
                entry.Anchor,
                TrackingState.Tracking);
        }

        public SensoryBagCalibrationData CreateInitialData(StickerWorldEntry entry)
        {
            var kind = entry.World != null && string.Equals(entry.World.WorldId, "nature", StringComparison.OrdinalIgnoreCase)
                ? SensoryBagWorldKind.Nature : SensoryBagWorldKind.Candy;
            if (TryGetWorldKind(entry.ReferenceImageName, out var imageKind)) kind = imageKind;
            return SensoryBagCalibrationData.CreateDefault(entry.ReferenceImageName ?? string.Empty, kind, CreateSettings());
        }

        public void ConfirmDefault(StickerWorldEntry entry)
        {
            if (entry == null || entry.Anchor == null || ReferenceEquals(m_CompletedEntry, entry)) return;
            EndCalibrationMode();
            m_SelectedEntry = entry;
            m_UsesImageTracking = true;
            Debug.Log("[Sensory Bag Branch] mode=has step=confirm-default skip-frame=true skip-video=true");
            CompleteCalibration(entry, entry.Anchor, CreateInitialData(entry));
        }

        public void BeginAnchoredCalibration(StickerWorldEntry entry, Transform anchor)
        {
            if (entry == null || entry.World == null || anchor == null) return;
            EndCalibrationMode();
            m_SelectedEntry = entry;
            m_CompletedEntry = null;
            m_UsesImageTracking = false;
            var root = new GameObject("Sensory Bag Calibration - Table");
            root.transform.SetParent(transform, false);
            var calibrator = root.AddComponent<SensoryBagCalibrator>();
            calibrator.ConfigureFonts(m_ChineseFont, m_EnglishFont);
            calibrator.ConfigureButtonStyle(m_RoundedButtonSprite);
            calibrator.Begin(anchor, CreateInitialData(entry), CreateSettings());
            root.AddComponent<SensoryBagDirectTouchInput>();
            calibrator.CalibrationConfirmed += data => CompleteCalibration(entry, anchor, data);
            m_Sessions[entry.TrackableId] = calibrator;
            StartFrameAdjustingTutorial();
            Debug.Log("[Sensory Bag Branch] mode=none step=calibration-started anchor=table");
        }

        void CompleteCalibration(StickerWorldEntry entry, Transform anchor, SensoryBagCalibrationData data)
        {
            if (entry == null || anchor == null || ReferenceEquals(m_CompletedEntry, entry)) return;
            m_CompletedEntry = entry;
            m_ConfirmedData[entry.TrackableId] = data;
            CreatePresentation(entry.TrackableId, anchor, data);
            CalibrationCompleted?.Invoke(entry, data);
        }

        public void EndCalibrationMode()
        {
            StopFrameAdjustingTutorial();
            if (m_SelectedEntry != null &&
                m_Sessions.Remove(m_SelectedEntry.TrackableId, out var calibrator) &&
                calibrator != null)
                DisposeObject(calibrator.gameObject);
            m_SelectedEntry = null;
        }

        void StartFrameAdjustingTutorial()
        {
            StopFrameAdjustingTutorial();
            if (m_OpeningVideoSession == null || m_OpeningVideoSession.IsDisposed)
            {
                if (!m_LoggedMissingTutorial)
                    Debug.LogWarning("[Sensory Bag Branch] step=calibration-video-unavailable reason=no-opening-surface");
                m_LoggedMissingTutorial = true;
                return;
            }

            var config = Resources.Load<OpeningVideoSequenceConfig>("MoodiumOpening/OpeningVideoSequenceConfig");
            if (config == null)
            {
                Debug.LogWarning("[Sensory Bag Calibration] Frame-adjusting tutorial config was not found.");
                return;
            }

            var clip = m_OpeningVideoSession.English
                ? config.FrameAdjustingEnglishClip
                : config.FrameAdjustingChineseClip;
            if (clip == null)
            {
                Debug.LogWarning("[Sensory Bag Calibration] Frame-adjusting tutorial clip is not assigned.");
                return;
            }

            var font = m_OpeningVideoSession.English ? m_EnglishFont : m_ChineseFont;
            m_FrameAdjustingTutorial = FrameAdjustingTutorialPlayback.CreateAndPlay(
                clip,
                m_OpeningVideoSession.DisplayRoot,
                m_OpeningVideoSession.English,
                font);
        }

        void StopFrameAdjustingTutorial()
        {
            if (m_FrameAdjustingTutorial != null)
                m_FrameAdjustingTutorial.StopAndDestroy();
            m_FrameAdjustingTutorial = null;
        }

        public void HandleImageRemoved(TrackableId trackableId)
        {
            // A brief remove/re-add can happen during visionOS tracking recovery. Preserve
            // anchor-local calibration data and the session until the scene itself is reset.
            if (m_Sessions.TryGetValue(trackableId, out var calibrator) && calibrator != null)
                calibrator.SetTrackingVisible(false);
            GetPresentation(trackableId)?.SetTrackingVisible(false);
        }

        public SensoryBagCalibrator GetActiveCalibrator(TrackableId trackableId)
        {
            m_Sessions.TryGetValue(trackableId, out var calibrator);
            return calibrator;
        }

        public bool TryGetConfirmedCalibration(TrackableId trackableId, out SensoryBagCalibrationData data)
        {
            return m_ConfirmedData.TryGetValue(trackableId, out data);
        }

        void CreatePresentation(TrackableId trackableId, Transform anchor, SensoryBagCalibrationData data)
        {
            if (m_Presentations.Remove(trackableId, out var existing) && existing != null)
                DisposeObject(existing.gameObject);
            m_VisualPrefabs.TryGetValue(data.WorldKind, out var prefab);
            var root = new GameObject($"Calibrated Bag Root - {data.ReferenceImageName}");
            root.transform.SetParent(transform, false);
            var presentation = root.AddComponent<CalibratedSensoryBagPresentation>();
            presentation.Configure(anchor, data, prefab);
            var capsule = presentation.GetComponentInChildren<Moodium.Interaction.ChocolateCapsuleInteraction>();
            if (capsule == null && presentation.VisualRoot.childCount > 0)
            {
                capsule = presentation.VisualRoot.GetChild(0).gameObject.AddComponent<Moodium.Interaction.ChocolateCapsuleInteraction>();
                capsule.SetInteractionEnabled(true);
            }
            if (capsule != null && (m_PushLeftHandPrefab != null || m_PushRightHandPrefab != null))
            {
                var guide = new GameObject("Push Hand Interaction Guide").AddComponent<Moodium.Reality.PushHandInteractionGuide>();
                guide.Configure(capsule, m_PushLeftHandPrefab, m_PushRightHandPrefab);
                m_PushHandGuides.Add(guide);
            }
            m_Presentations[trackableId] = presentation;
        }

        bool TryGetWorldKind(string referenceImageName, out SensoryBagWorldKind worldKind)
        {
            if (MoodiumImageReferenceName.Matches(referenceImageName, m_CandyReferenceImageName))
            {
                worldKind = SensoryBagWorldKind.Candy;
                return true;
            }
            if (MoodiumImageReferenceName.Matches(referenceImageName, m_NatureReferenceImageName))
            {
                worldKind = SensoryBagWorldKind.Nature;
                return true;
            }
            worldKind = default;
            return false;
        }

        static void DisposeObject(GameObject target)
        {
            if (target == null) return;
            target.SetActive(false);
            if (Application.isPlaying) Destroy(target); else DestroyImmediate(target);
        }
    }
}
