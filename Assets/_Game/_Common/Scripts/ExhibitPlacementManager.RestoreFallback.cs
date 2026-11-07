using System;
using System.Collections.Generic;
using Interaction;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace VFXViewer
{
    public partial class ExhibitPlacementManager
    {
        private enum SessionRestoreState
        {
            Unresolved = 0,
            OriginalAnchored = 1,
            FallbackAnchored = 2,
            LayoutRecoveredLoose = 3,
            Invalid = 4
        }

        private enum RootRestoreState
        {
            Unresolved = 0,
            OriginalAnchored = 1,
            FallbackAnchored = 2,
            LayoutRecoveredLoose = 3,
            Invalid = 4
        }

        private enum RestoreConfidence
        {
            Unknown = 0,
            Verified = 1,
            Weak = 2,
            Unsafe = 3
        }

        private struct RootInferenceSample
        {
            public string instanceId;
            public Transform exhibitTransform;
            public LayoutData layoutData;
        }

        private readonly Dictionary<string, SessionRestoreState> m_RestoreStateByInstanceId =
            new Dictionary<string, SessionRestoreState>(StringComparer.Ordinal);
        private readonly Dictionary<string, RestoreConfidence> m_RestoreConfidenceByInstanceId =
            new Dictionary<string, RestoreConfidence>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> m_RestoreFailureReasonByInstanceId =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private bool m_HasResolvedRootForPlayback;
        private bool m_HasSeenFirstAddedEventForLayoutFallback;
        private bool m_HasLoggedWaitingForFirstAddedEventForLayoutFallback;
        private string m_LastRootLayoutFallbackDiagnostic = string.Empty;
        private RootRestoreState m_RootRestoreState = RootRestoreState.Unresolved;
        private RestoreConfidence m_RootRestoreConfidence = RestoreConfidence.Unknown;
        private string m_RootRestoreFailureReason = string.Empty;
        private string m_LastPartialPoseConflictSummary = string.Empty;

        public bool HasResolvedRootForPlayback => m_HasResolvedRootForPlayback;
        public bool IsRootReadyForPlayback =>
            m_HasResolvedRootForPlayback &&
            m_RootRestoreState != RootRestoreState.Unresolved &&
            m_RootRestoreState != RootRestoreState.Invalid &&
            m_RootRestoreConfidence != RestoreConfidence.Unsafe;

        private static void AddUniqueRestoreTrackableId(List<string> target, string candidate)
        {
            if (target == null || target.Count >= 2)
                return;

            candidate = string.IsNullOrWhiteSpace(candidate) ? string.Empty : candidate.Trim();
            if (string.IsNullOrEmpty(candidate) || target.Contains(candidate))
                return;

            target.Add(candidate);
        }

        private static string NormalizeTrackableIdString(string trackableId)
        {
            return string.IsNullOrWhiteSpace(trackableId) ? string.Empty : trackableId.Trim();
        }

        private static string NormalizeOriginalTrackableId(string originalTrackableId)
        {
            return NormalizeTrackableIdString(originalTrackableId);
        }

        private static string NormalizeFallbackTrackableId(string originalTrackableId, string fallbackTrackableId)
        {
            originalTrackableId = NormalizeOriginalTrackableId(originalTrackableId);
            fallbackTrackableId = NormalizeTrackableIdString(fallbackTrackableId);
            if (string.IsNullOrEmpty(fallbackTrackableId) ||
                string.Equals(originalTrackableId, fallbackTrackableId, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return fallbackTrackableId;
        }

        private static List<string> BuildRestoreTrackableIds(string originalTrackableId, string fallbackTrackableId)
        {
            var trackableIds = new List<string>(2);
            AddUniqueRestoreTrackableId(trackableIds, NormalizeOriginalTrackableId(originalTrackableId));
            AddUniqueRestoreTrackableId(trackableIds, NormalizeFallbackTrackableId(originalTrackableId, fallbackTrackableId));
            return trackableIds;
        }

        private static List<string> GetCandidateTrackableIds(SessionData data)
        {
            return data == null
                ? new List<string>()
                : BuildRestoreTrackableIds(data.originalTrackableId, data.fallbackTrackableId);
        }

        private static List<string> GetCandidateTrackableIds(RootSessionData data)
        {
            return data == null
                ? new List<string>()
                : BuildRestoreTrackableIds(data.originalTrackableId, data.fallbackTrackableId);
        }

        private static string GetFallbackTrackableId(SessionData data)
        {
            return data == null
                ? string.Empty
                : NormalizeFallbackTrackableId(data.originalTrackableId, data.fallbackTrackableId);
        }

        private static string GetFallbackTrackableId(RootSessionData data)
        {
            return data == null
                ? string.Empty
                : NormalizeFallbackTrackableId(data.originalTrackableId, data.fallbackTrackableId);
        }

        private static string FormatRestoreTrackableIdForLog(string trackableId)
        {
            return string.IsNullOrWhiteSpace(trackableId) ? "<none>" : trackableId;
        }

        private static string ClassifyRestoreTrackableSource(
            string originalTrackableId,
            string fallbackTrackableId,
            string trackableId,
            bool layoutFallbackOnly = false)
        {
            if (layoutFallbackOnly)
                return "layout_fallback";

            originalTrackableId = NormalizeOriginalTrackableId(originalTrackableId);
            fallbackTrackableId = NormalizeFallbackTrackableId(originalTrackableId, fallbackTrackableId);
            trackableId = NormalizeTrackableIdString(trackableId);

            if (string.IsNullOrEmpty(trackableId))
                return "unknown";

            if (string.Equals(originalTrackableId, trackableId, StringComparison.Ordinal))
                return "original";

            if (string.Equals(fallbackTrackableId, trackableId, StringComparison.Ordinal))
                return "fallback";

            return "unknown";
        }

        private static string DescribeRestoreSource(SessionData data, string trackableId, bool layoutFallbackOnly = false)
        {
            string originalTrackableId = GetOriginalTrackableId(data);
            string fallbackTrackableId = GetFallbackTrackableId(data);
            string restoreSource = ClassifyRestoreTrackableSource(
                originalTrackableId,
                fallbackTrackableId,
                trackableId,
                layoutFallbackOnly);
            return
                $"restoreSource={restoreSource}, " +
                $"originalTrackableId={FormatRestoreTrackableIdForLog(originalTrackableId)}, " +
                $"fallbackTrackableId={FormatRestoreTrackableIdForLog(fallbackTrackableId)}, " +
                $"matchedTrackableId={FormatRestoreTrackableIdForLog(trackableId)}";
        }

        private string DescribeRootRestoreSource(string trackableId, bool layoutFallbackOnly = false)
        {
            string originalTrackableId = GetOriginalRootCandidateTrackableId();
            string fallbackTrackableId = GetFallbackRootCandidateTrackableId();
            string restoreSource = ClassifyRestoreTrackableSource(
                originalTrackableId,
                fallbackTrackableId,
                trackableId,
                layoutFallbackOnly);
            return
                $"restoreSource={restoreSource}, " +
                $"originalTrackableId={FormatRestoreTrackableIdForLog(originalTrackableId)}, " +
                $"fallbackTrackableId={FormatRestoreTrackableIdForLog(fallbackTrackableId)}, " +
                $"matchedTrackableId={FormatRestoreTrackableIdForLog(trackableId)}";
        }

        private static string GetOriginalTrackableId(SessionData data)
        {
            return data == null
                ? string.Empty
                : NormalizeOriginalTrackableId(data.originalTrackableId);
        }

        private static string GetOriginalTrackableId(RootSessionData data)
        {
            return data == null
                ? string.Empty
                : NormalizeOriginalTrackableId(data.originalTrackableId);
        }

        private static string GetPreferredRestoreCandidateTrackableId(SessionData data)
        {
            return GetOriginalTrackableId(data);
        }

        private static string GetPreferredRestoreCandidateTrackableId(RootSessionData data)
        {
            return GetOriginalTrackableId(data);
        }

        private static int GetRestoreCandidatePriority(
            string originalTrackableId,
            string fallbackTrackableId,
            string trackableId)
        {
            originalTrackableId = NormalizeOriginalTrackableId(originalTrackableId);
            fallbackTrackableId = NormalizeFallbackTrackableId(originalTrackableId, fallbackTrackableId);
            trackableId = NormalizeTrackableIdString(trackableId);
            if (string.IsNullOrEmpty(trackableId))
                return int.MaxValue;

            if (string.Equals(originalTrackableId, trackableId, StringComparison.Ordinal))
                return 0;

            if (string.Equals(fallbackTrackableId, trackableId, StringComparison.Ordinal))
                return 1;

            return int.MaxValue;
        }

        private static bool ShouldPreferRestoreCandidate(
            string originalTrackableId,
            string fallbackTrackableId,
            string candidateTrackableId,
            string currentTrackableId)
        {
            int candidatePriority = GetRestoreCandidatePriority(originalTrackableId, fallbackTrackableId, candidateTrackableId);
            if (candidatePriority == int.MaxValue)
                return false;

            int currentPriority = GetRestoreCandidatePriority(originalTrackableId, fallbackTrackableId, currentTrackableId);
            return currentPriority == int.MaxValue || candidatePriority < currentPriority;
        }

        private List<string> GetRootCandidateTrackableIdsAsStrings()
        {
            return BuildRestoreTrackableIds(m_RootSessionOriginalTrackableId, m_RootSessionFallbackTrackableId);
        }

        private static void SetRestoreTrackableIds(
            SessionData data,
            string originalTrackableId,
            string fallbackTrackableId = null)
        {
            if (data == null)
                return;

            data.originalTrackableId = NormalizeOriginalTrackableId(originalTrackableId);
            data.fallbackTrackableId = NormalizeFallbackTrackableId(data.originalTrackableId, fallbackTrackableId);
            data.trackableId = data.originalTrackableId;
        }

        private static void SetRestoreTrackableIds(
            RootSessionData data,
            string originalTrackableId,
            string fallbackTrackableId = null)
        {
            if (data == null)
                return;

            data.originalTrackableId = NormalizeOriginalTrackableId(originalTrackableId);
            data.fallbackTrackableId = NormalizeFallbackTrackableId(data.originalTrackableId, fallbackTrackableId);
            data.trackableId = data.originalTrackableId;
        }

        private void SetRootSessionTrackableIds(
            string originalTrackableId,
            string fallbackTrackableId = null)
        {
            m_RootSessionOriginalTrackableId = NormalizeOriginalTrackableId(originalTrackableId);
            m_RootSessionFallbackTrackableId =
                NormalizeFallbackTrackableId(m_RootSessionOriginalTrackableId, fallbackTrackableId);
        }

        private bool RootSessionContainsTrackableId(TrackableId trackableId)
        {
            string trackableIdString = NormalizeTrackableIdString(trackableId.ToString());
            if (string.IsNullOrEmpty(trackableIdString))
                return false;

            return string.Equals(m_RootSessionOriginalTrackableId, trackableIdString, StringComparison.Ordinal) ||
                   string.Equals(m_RootSessionFallbackTrackableId, trackableIdString, StringComparison.Ordinal);
        }

        private string GetOriginalRootCandidateTrackableId()
        {
            return m_RootSessionOriginalTrackableId;
        }

        private string GetFallbackRootCandidateTrackableId()
        {
            return m_RootSessionFallbackTrackableId;
        }

        private string GetPreferredRootRestoreCandidateTrackableId()
        {
            return GetOriginalRootCandidateTrackableId();
        }

        private bool ShouldPreferRootRestoreCandidate(TrackableId candidateTrackableId, TrackableId currentTrackableId)
        {
            if (candidateTrackableId == TrackableId.invalidId)
                return false;

            return ShouldPreferRestoreCandidate(
                m_RootSessionOriginalTrackableId,
                m_RootSessionFallbackTrackableId,
                candidateTrackableId.ToString(),
                currentTrackableId != TrackableId.invalidId ? currentTrackableId.ToString() : string.Empty);
        }

        private bool HasSavedRootSessionTrackableId()
        {
            return !string.IsNullOrWhiteSpace(m_RootSessionOriginalTrackableId) ||
                   !string.IsNullOrWhiteSpace(m_RootSessionFallbackTrackableId);
        }

        private static string FormatRestoreConfidence(RestoreConfidence confidence)
        {
            switch (confidence)
            {
                case RestoreConfidence.Verified:
                    return "Verified";
                case RestoreConfidence.Weak:
                    return "Weak";
                case RestoreConfidence.Unsafe:
                    return "Unsafe";
                default:
                    return "Unknown";
            }
        }

        private static string FormatSessionRestoreState(SessionRestoreState state)
        {
            switch (state)
            {
                case SessionRestoreState.OriginalAnchored:
                    return "OriginalAnchored";
                case SessionRestoreState.FallbackAnchored:
                    return "FallbackAnchored";
                case SessionRestoreState.LayoutRecoveredLoose:
                    return "LayoutRecoveredLoose";
                case SessionRestoreState.Invalid:
                    return "Invalid";
                default:
                    return "Unresolved";
            }
        }

        private static string FormatRootRestoreState(RootRestoreState state)
        {
            switch (state)
            {
                case RootRestoreState.OriginalAnchored:
                    return "OriginalAnchored";
                case RootRestoreState.FallbackAnchored:
                    return "FallbackAnchored";
                case RootRestoreState.LayoutRecoveredLoose:
                    return "LayoutRecoveredLoose";
                case RootRestoreState.Invalid:
                    return "Invalid";
                default:
                    return "Unresolved";
            }
        }

        private static SessionRestoreState GetAnchoredSessionRestoreState(SessionData data, TrackableId trackableId)
        {
            string restoreSource = ClassifyRestoreTrackableSource(
                GetOriginalTrackableId(data),
                GetFallbackTrackableId(data),
                trackableId.ToString());
            return string.Equals(restoreSource, "fallback", StringComparison.Ordinal)
                ? SessionRestoreState.FallbackAnchored
                : SessionRestoreState.OriginalAnchored;
        }

        private RootRestoreState GetAnchoredRootRestoreState(TrackableId trackableId)
        {
            string restoreSource = ClassifyRestoreTrackableSource(
                GetOriginalRootCandidateTrackableId(),
                GetFallbackRootCandidateTrackableId(),
                trackableId.ToString());
            return string.Equals(restoreSource, "fallback", StringComparison.Ordinal)
                ? RootRestoreState.FallbackAnchored
                : RootRestoreState.OriginalAnchored;
        }

        private SessionRestoreState GetRestoreState(string instanceId)
        {
            instanceId = NormalizeInstanceId(instanceId);
            if (string.IsNullOrEmpty(instanceId))
                return SessionRestoreState.Unresolved;

            return m_RestoreStateByInstanceId.TryGetValue(instanceId, out var state)
                ? state
                : SessionRestoreState.Unresolved;
        }

        private RestoreConfidence GetRestoreConfidence(string instanceId)
        {
            instanceId = NormalizeInstanceId(instanceId);
            if (string.IsNullOrEmpty(instanceId))
                return RestoreConfidence.Unknown;

            return m_RestoreConfidenceByInstanceId.TryGetValue(instanceId, out var confidence)
                ? confidence
                : RestoreConfidence.Unknown;
        }

        private string GetRestoreFailureReason(string instanceId)
        {
            instanceId = NormalizeInstanceId(instanceId);
            if (string.IsNullOrEmpty(instanceId))
                return string.Empty;

            return m_RestoreFailureReasonByInstanceId.TryGetValue(instanceId, out var reason)
                ? reason ?? string.Empty
                : string.Empty;
        }

        private bool SetRestoreState(string instanceId, SessionRestoreState nextState)
        {
            instanceId = NormalizeInstanceId(instanceId);
            if (string.IsNullOrEmpty(instanceId))
                return false;

            SessionRestoreState currentState = GetRestoreState(instanceId);
            if (currentState == nextState)
                return false;
            m_RestoreStateByInstanceId[instanceId] = nextState;
            return true;
        }

        private bool SetRestoreConfidence(
            string instanceId,
            RestoreConfidence confidence,
            string failureReason)
        {
            instanceId = NormalizeInstanceId(instanceId);
            if (string.IsNullOrEmpty(instanceId))
                return false;

            failureReason = string.IsNullOrWhiteSpace(failureReason) ? string.Empty : failureReason.Trim();
            bool changed = false;
            if (GetRestoreConfidence(instanceId) != confidence)
            {
                m_RestoreConfidenceByInstanceId[instanceId] = confidence;
                changed = true;
            }

            string currentReason = GetRestoreFailureReason(instanceId);
            if (!string.Equals(currentReason, failureReason, StringComparison.Ordinal))
            {
                if (string.IsNullOrEmpty(failureReason))
                    m_RestoreFailureReasonByInstanceId.Remove(instanceId);
                else
                    m_RestoreFailureReasonByInstanceId[instanceId] = failureReason;
                changed = true;
            }

            return changed;
        }

        private bool IsSessionInstanceAvailable(string instanceId)
        {
            SessionRestoreState state = GetRestoreState(instanceId);
            return state != SessionRestoreState.Unresolved &&
                   state != SessionRestoreState.Invalid;
        }

        private bool IsSessionInstanceUsable(string instanceId)
        {
            return IsSessionInstanceAvailable(instanceId) &&
                   GetRestoreConfidence(instanceId) != RestoreConfidence.Unsafe;
        }

        private void NotifyRestoreAvailabilityChanged()
        {
            RestoreAvailabilityChanged?.Invoke();
        }

        private bool SetRootRestoreRuntimeState(
            RootRestoreState restoreState,
            RestoreConfidence confidence,
            string failureReason,
            string logPrefix,
            Transform rootTransform,
            ARAnchor anchor = null,
            bool notifyAvailabilityChanged = true)
        {
            failureReason = string.IsNullOrWhiteSpace(failureReason) ? string.Empty : failureReason.Trim();
            bool wasReady = IsRootReadyForPlayback;
            bool wasResolved = m_RootRestoreState != RootRestoreState.Unresolved &&
                               m_RootRestoreState != RootRestoreState.Invalid;
            bool stateChanged = m_RootRestoreState != restoreState;
            bool confidenceChanged = m_RootRestoreConfidence != confidence;
            bool reasonChanged = !string.Equals(m_RootRestoreFailureReason, failureReason, StringComparison.Ordinal);

            m_RootRestoreState = restoreState;
            m_RootRestoreConfidence = confidence;
            m_RootRestoreFailureReason = failureReason;
            bool isReady = IsRootReadyForPlayback;
            bool isResolved = restoreState != RootRestoreState.Unresolved &&
                              restoreState != RootRestoreState.Invalid;

            if (!wasResolved && isResolved)
                onExhibitRestored?.Invoke(string.Empty);

            if (notifyAvailabilityChanged && wasReady != isReady)
                NotifyRestoreAvailabilityChanged();

            if (!string.IsNullOrWhiteSpace(logPrefix) && (stateChanged || confidenceChanged || reasonChanged))
            {
                string anchorState = anchor != null ? DescribeAnchorState(anchor) : "anchor=<layout_recovered>";
                LogAnchorLifecycle(
                    $"{logPrefix}: state={FormatRootRestoreState(restoreState)}, confidence={FormatRestoreConfidence(confidence)}, " +
                    $"failureReason={(string.IsNullOrEmpty(failureReason) ? "<none>" : failureReason)}, " +
                    $"{DescribeRootRestoreSource(anchor != null ? anchor.trackableId.ToString() : string.Empty, anchor == null && restoreState == RootRestoreState.LayoutRecoveredLoose)}, " +
                    $"{anchorState}, {DescribeTransformPose(rootTransform)}");
            }

            return stateChanged || confidenceChanged || reasonChanged;
        }

        private bool MarkSessionExhibitAvailable(
            SessionData data,
            Transform exhibitTransform,
            SessionRestoreState restoreState,
            RestoreConfidence confidence,
            string logPrefix,
            ARAnchor anchor = null,
            string failureReason = null,
            bool notifyAvailabilityChanged = true)
        {
            if (data == null)
                return false;

            string instanceId = NormalizeInstanceId(data.instanceID);
            if (string.IsNullOrEmpty(instanceId))
                return false;

            bool wasAvailable = IsSessionInstanceAvailable(instanceId);
            bool wasUsable = IsSessionInstanceUsable(instanceId);
            bool stateChanged = SetRestoreState(instanceId, restoreState);
            bool confidenceChanged = SetRestoreConfidence(instanceId, confidence, failureReason);
            bool isUsable = IsSessionInstanceUsable(instanceId);

            if (IsSessionInstanceAvailable(instanceId))
            {
                m_HasRestoredSessionExhibits = true;
                string normalizedModuleId = NormalizeModuleId(data.moduleId);
                if (!string.IsNullOrEmpty(normalizedModuleId))
                    m_RestoredModuleIds.Add(normalizedModuleId);

                if (!wasAvailable)
                    onExhibitRestored?.Invoke(normalizedModuleId);
            }

            if (notifyAvailabilityChanged && wasUsable != isUsable)
                NotifyRestoreAvailabilityChanged();

            if (stateChanged || confidenceChanged)
            {
                string anchorState = anchor != null ? DescribeAnchorState(anchor) : "anchor=<layout_recovered>";
                string restoreSource = anchor != null
                    ? DescribeRestoreSource(data, anchor.trackableId.ToString())
                    : DescribeRestoreSource(data, string.Empty, layoutFallbackOnly: restoreState == SessionRestoreState.LayoutRecoveredLoose);
                LogAnchorLifecycle(
                    $"{logPrefix}: moduleId={NormalizeModuleId(data.moduleId)}, instanceId={instanceId}, " +
                    $"state={FormatSessionRestoreState(restoreState)}, confidence={FormatRestoreConfidence(confidence)}, " +
                    $"failureReason={(string.IsNullOrWhiteSpace(failureReason) ? "<none>" : failureReason)}, " +
                    $"{restoreSource}, {anchorState}, {DescribeTransformPose(exhibitTransform)}");
            }

            return stateChanged || confidenceChanged;
        }

        private bool TryGetLayoutSnapshotItem(string instanceId, out LayoutData layoutData)
        {
            layoutData = null;
            instanceId = NormalizeInstanceId(instanceId);
            if (string.IsNullOrEmpty(instanceId))
                return false;

            return m_LayoutSnapshotByInstanceId.TryGetValue(instanceId, out layoutData) && layoutData != null;
        }

        private void UpdateSessionSnapshotTrackableIds(
            string instanceId,
            string originalTrackableId,
            string fallbackTrackableId = null)
        {
            instanceId = NormalizeInstanceId(instanceId);
            if (string.IsNullOrEmpty(instanceId) ||
                !m_SessionSnapshotByInstanceId.TryGetValue(instanceId, out var existingSnapshot) ||
                existingSnapshot == null)
            {
                return;
            }

            var updatedSnapshot = CloneSessionData(existingSnapshot);
            SetRestoreTrackableIds(updatedSnapshot, originalTrackableId, fallbackTrackableId);
            m_SessionSnapshotByInstanceId[instanceId] = updatedSnapshot;
        }

        private void ResetRootLayoutFallbackDiagnostics()
        {
            m_LastRootLayoutFallbackDiagnostic = string.Empty;
        }

        private void LogRootLayoutFallbackDiagnostic(string message)
        {
            if (string.IsNullOrWhiteSpace(message) ||
                string.Equals(m_LastRootLayoutFallbackDiagnostic, message, StringComparison.Ordinal))
            {
                return;
            }

            m_LastRootLayoutFallbackDiagnostic = message;
            LogAnchorLifecycle(message);
        }

        private string DescribeRootCandidateState(Transform rootTransform)
        {
            if (rootTransform == null)
                return "currentRoot=<none>";

            bool hasRootMarker = rootTransform.GetComponent<PlacementRootMarker>() != null;
            bool isLegacyInstanceZero = false;
            if (!hasRootMarker &&
                TryGetRegisteredExhibitByInstanceId("0", out var legacyInfo) &&
                legacyInfo != null &&
                legacyInfo.transform == rootTransform)
            {
                isLegacyInstanceZero = true;
            }

            ARAnchor rootAnchor = rootTransform.GetComponentInParent<ARAnchor>();
            string rootAnchorState = rootAnchor != null
                ? DescribeAnchorState(rootAnchor)
                : "rootAnchor=<none>";
            return
                $"currentRootHasMarker={hasRootMarker}, currentRootIsLegacyInstance0={isLegacyInstanceZero}, " +
                $"{rootAnchorState}, {DescribeTransformPose(rootTransform)}";
        }

        private static string BuildRootInferenceDiagnostics(
            int totalSessionItems,
            int emptyInstanceIdCount,
            int nullSessionDataCount,
            int unresolvedCount,
            int missingLayoutCount,
            int missingRegisteredExhibitCount,
            int missingTransformCount,
            int sampledCount,
            List<string> sampledInstanceIds,
            Vector3 rootMarkerLocalScale,
            bool hasBestPose,
            float bestError)
        {
            string sampledInstanceSummary = sampledInstanceIds != null && sampledInstanceIds.Count > 0
                ? string.Join("|", sampledInstanceIds)
                : "<none>";
            string bestErrorSummary = hasBestPose ? bestError.ToString("F6") : "<none>";
            return
                $"totalSessionItems={totalSessionItems}, sampled={sampledCount}, sampledInstanceIds={sampledInstanceSummary}, " +
                $"skippedEmptyInstanceId={emptyInstanceIdCount}, skippedNullSessionData={nullSessionDataCount}, " +
                $"skippedUnresolved={unresolvedCount}, skippedMissingLayout={missingLayoutCount}, " +
                $"skippedMissingRegistered={missingRegisteredExhibitCount}, skippedMissingTransform={missingTransformCount}, " +
                $"rootMarkerScale={FormatVector3(rootMarkerLocalScale)}, bestError={bestErrorSummary}";
        }

        private bool TryPromoteRestoredSessionCandidate(SessionData data, TrackableId activeTrackableId)
        {
            if (data == null || activeTrackableId == TrackableId.invalidId)
                return false;

            if (!string.IsNullOrWhiteSpace(GetOriginalTrackableId(data)))
                return false;

            string activeTrackableIdString = activeTrackableId.ToString();
            UpdateSessionSnapshotTrackableIds(
                data.instanceID,
                activeTrackableIdString);
            QueueSaveToFiles(saveLayout: false, suppressStatusMessage: true);
            return true;
        }

        private static float ScoreRootPoseError(float positionErrorMeters, float rotationErrorDegrees)
        {
            return (positionErrorMeters * positionErrorMeters) +
                   Mathf.Pow(rotationErrorDegrees / Mathf.Max(1f, RootRestoreWeakRotationToleranceDegrees), 2f);
        }

        private static RestoreConfidence ClassifyModulePoseConfidence(float positionErrorMeters, float rotationErrorDegrees)
        {
            if (positionErrorMeters <= ModuleRestoreVerifiedPositionToleranceMeters &&
                rotationErrorDegrees <= ModuleRestoreVerifiedRotationToleranceDegrees)
            {
                return RestoreConfidence.Verified;
            }

            if (positionErrorMeters <= ModuleRestoreWeakPositionToleranceMeters &&
                rotationErrorDegrees <= ModuleRestoreWeakRotationToleranceDegrees)
            {
                return RestoreConfidence.Weak;
            }

            return RestoreConfidence.Unsafe;
        }

        private static RestoreConfidence ClassifyRootPoseConfidence(
            int sampleCount,
            float maxPositionErrorMeters,
            float maxRotationErrorDegrees)
        {
            if (sampleCount >= 2 &&
                maxPositionErrorMeters <= RootRestoreVerifiedPositionToleranceMeters &&
                maxRotationErrorDegrees <= RootRestoreVerifiedRotationToleranceDegrees)
            {
                return RestoreConfidence.Verified;
            }

            if (sampleCount >= 1 &&
                maxPositionErrorMeters <= RootRestoreWeakPositionToleranceMeters &&
                maxRotationErrorDegrees <= RootRestoreWeakRotationToleranceDegrees)
            {
                return RestoreConfidence.Weak;
            }

            return RestoreConfidence.Unsafe;
        }

        private bool TryGetExpectedModulePoseFromLayout(string instanceId, out Pose expectedPose)
        {
            expectedPose = default;
            Transform rootTransform = GetRootTransform();
            if (rootTransform == null || !TryGetLayoutSnapshotItem(instanceId, out var layoutData) || layoutData == null)
                return false;

            expectedPose = new Pose(
                rootTransform.TransformPoint(layoutData.relativePosition),
                rootTransform.rotation * layoutData.relativeRotation);
            return true;
        }

        private RestoreConfidence GetLayoutRecoveryConfidence()
        {
            if (m_RootRestoreConfidence == RestoreConfidence.Verified)
                return RestoreConfidence.Verified;

            if (IsRootReadyForPlayback)
                return RestoreConfidence.Weak;

            return RestoreConfidence.Unknown;
        }

        private bool TryEvaluateModulePoseConfidence(
            SessionData data,
            Transform exhibitTransform,
            out RestoreConfidence confidence,
            out float positionErrorMeters,
            out float rotationErrorDegrees,
            out string failureReason)
        {
            confidence = RestoreConfidence.Unknown;
            positionErrorMeters = 0f;
            rotationErrorDegrees = 0f;
            failureReason = string.Empty;

            if (data == null || exhibitTransform == null)
            {
                confidence = RestoreConfidence.Unsafe;
                failureReason = "missing";
                return false;
            }

            string instanceId = NormalizeInstanceId(data.instanceID);
            if (string.IsNullOrEmpty(instanceId))
            {
                confidence = RestoreConfidence.Unsafe;
                failureReason = "missing";
                return false;
            }

            if (!IsRootReadyForPlayback || !TryGetExpectedModulePoseFromLayout(instanceId, out Pose expectedPose))
            {
                confidence = RestoreConfidence.Weak;
                failureReason = string.Empty;
                return true;
            }

            positionErrorMeters = Vector3.Distance(exhibitTransform.position, expectedPose.position);
            rotationErrorDegrees = Quaternion.Angle(exhibitTransform.rotation, expectedPose.rotation);
            confidence = ClassifyModulePoseConfidence(positionErrorMeters, rotationErrorDegrees);
            failureReason = confidence == RestoreConfidence.Unsafe ? "inconsistent" : string.Empty;
            return true;
        }

        private bool TryCollectRootInferenceSamples(
            out List<RootInferenceSample> samples,
            out List<string> sampledInstanceIds,
            out Vector3 rootMarkerLocalScale,
            out string diagnostics)
        {
            samples = new List<RootInferenceSample>();
            sampledInstanceIds = new List<string>();
            rootMarkerLocalScale = GetExpectedRootMarkerLocalScale();
            int totalSessionItems = m_SessionSnapshotByInstanceId.Count;
            int emptyInstanceIdCount = 0;
            int nullSessionDataCount = 0;
            int unresolvedCount = 0;
            int missingLayoutCount = 0;
            int missingRegisteredExhibitCount = 0;
            int missingTransformCount = 0;

            foreach (var entry in m_SessionSnapshotByInstanceId)
            {
                string instanceId = NormalizeInstanceId(entry.Key);
                SessionData data = entry.Value;
                if (string.IsNullOrEmpty(instanceId))
                {
                    emptyInstanceIdCount++;
                    continue;
                }

                if (data == null)
                {
                    nullSessionDataCount++;
                    continue;
                }

                SessionRestoreState restoreState = GetRestoreState(instanceId);
                if (restoreState == SessionRestoreState.Unresolved ||
                    restoreState == SessionRestoreState.Invalid ||
                    GetRestoreConfidence(instanceId) == RestoreConfidence.Unsafe)
                {
                    unresolvedCount++;
                    continue;
                }

                if (!TryGetLayoutSnapshotItem(instanceId, out var layoutData))
                {
                    missingLayoutCount++;
                    continue;
                }

                if (!TryGetSceneExhibitByInstanceId(instanceId, out var info) || info == null)
                {
                    missingRegisteredExhibitCount++;
                    continue;
                }

                if (info.transform == null)
                {
                    missingTransformCount++;
                    continue;
                }

                samples.Add(new RootInferenceSample
                {
                    instanceId = instanceId,
                    exhibitTransform = info.transform,
                    layoutData = layoutData
                });
                sampledInstanceIds.Add(instanceId);
            }

            diagnostics = BuildRootInferenceDiagnostics(
                totalSessionItems,
                emptyInstanceIdCount,
                nullSessionDataCount,
                unresolvedCount,
                missingLayoutCount,
                missingRegisteredExhibitCount,
                missingTransformCount,
                samples.Count,
                sampledInstanceIds,
                rootMarkerLocalScale,
                hasBestPose: false,
                bestError: 0f);
            return samples.Count > 0;
        }

        private static void EvaluateRootPoseErrorMetrics(
            Pose candidateRootPose,
            List<RootInferenceSample> samples,
            Vector3 rootMarkerLocalScale,
            out float score,
            out float maxPositionErrorMeters,
            out float maxRotationErrorDegrees)
        {
            score = 0f;
            maxPositionErrorMeters = 0f;
            maxRotationErrorDegrees = 0f;

            for (int i = 0; i < samples.Count; i++)
            {
                RootInferenceSample sample = samples[i];
                Vector3 scaledOffset = ScaleLayoutOffset(sample.layoutData.relativePosition, rootMarkerLocalScale);
                Vector3 predictedPosition = candidateRootPose.position + (candidateRootPose.rotation * scaledOffset);
                Quaternion predictedRotation = candidateRootPose.rotation * sample.layoutData.relativeRotation;
                float positionErrorMeters = Vector3.Distance(predictedPosition, sample.exhibitTransform.position);
                float rotationErrorDegrees = Quaternion.Angle(predictedRotation, sample.exhibitTransform.rotation);

                score += ScoreRootPoseError(positionErrorMeters, rotationErrorDegrees);
                maxPositionErrorMeters = Mathf.Max(maxPositionErrorMeters, positionErrorMeters);
                maxRotationErrorDegrees = Mathf.Max(maxRotationErrorDegrees, rotationErrorDegrees);
            }
        }

        private bool TryEvaluateRootPoseConfidence(
            Pose rootPose,
            out RestoreConfidence confidence,
            out string diagnostics,
            out int sampleCount,
            out float maxPositionErrorMeters,
            out float maxRotationErrorDegrees)
        {
            confidence = RestoreConfidence.Unknown;
            diagnostics = string.Empty;
            sampleCount = 0;
            maxPositionErrorMeters = 0f;
            maxRotationErrorDegrees = 0f;

            if (!TryCollectRootInferenceSamples(
                    out var samples,
                    out var sampledInstanceIds,
                    out var rootMarkerLocalScale,
                    out string baseDiagnostics))
            {
                diagnostics = baseDiagnostics;
                confidence = RestoreConfidence.Weak;
                return false;
            }

            EvaluateRootPoseErrorMetrics(
                rootPose,
                samples,
                rootMarkerLocalScale,
                out float score,
                out maxPositionErrorMeters,
                out maxRotationErrorDegrees);

            sampleCount = samples.Count;
            diagnostics =
                $"{baseDiagnostics}, score={score:F6}, maxPositionError={maxPositionErrorMeters:F4}, " +
                $"maxRotationError={maxRotationErrorDegrees:F2}, sampledInstanceIds={string.Join("|", sampledInstanceIds)}";
            confidence = ClassifyRootPoseConfidence(sampleCount, maxPositionErrorMeters, maxRotationErrorDegrees);
            return true;
        }

        private bool TryResolveRootFromLayoutFallback()
        {
            Transform currentRoot = GetRootTransform();
            ARAnchor currentRootAnchor = currentRoot != null ? currentRoot.GetComponentInParent<ARAnchor>() : null;
            if (currentRoot != null && currentRootAnchor != null)
            {
                RestoreConfidence currentConfidence;
                string currentDiagnostics;
                int sampleCount;
                float maxPositionErrorMeters;
                float maxRotationErrorDegrees;
                bool hasSamples = TryEvaluateRootPoseConfidence(
                    new Pose(currentRoot.position, currentRoot.rotation),
                    out currentConfidence,
                    out currentDiagnostics,
                    out sampleCount,
                    out maxPositionErrorMeters,
                    out maxRotationErrorDegrees);

                if (!hasSamples || currentConfidence != RestoreConfidence.Unsafe)
                {
                    m_HasResolvedRootForPlayback = true;
                    m_HasRestoredRootExhibit = true;
                    SetRootRestoreRuntimeState(
                        GetAnchoredRootRestoreState(currentRootAnchor.trackableId),
                        hasSamples ? currentConfidence : RestoreConfidence.Weak,
                        hasSamples && currentConfidence == RestoreConfidence.Unsafe ? "inconsistent" : string.Empty,
                        "root_inference_skipped_anchored",
                        currentRoot,
                        currentRootAnchor);
                    LogRootLayoutFallbackDiagnostic(
                        $"layout fallback skipped root inference: existing anchored root available, " +
                        $"sampleCount={sampleCount}, maxPositionError={maxPositionErrorMeters:F4}, maxRotationError={maxRotationErrorDegrees:F2}, " +
                        $"{currentDiagnostics}, {DescribeRootCandidateState(currentRoot)}");
                    return true;
                }
            }

            if (!TryInferRootPoseFromRestoredExhibits(
                    out var inferredRootPose,
                    out var inferenceDiagnostics,
                    out RestoreConfidence inferredConfidence,
                    out int inferredSampleCount,
                    out float inferredMaxPositionErrorMeters,
                    out float inferredMaxRotationErrorDegrees))
            {
                SetRootRestoreRuntimeState(
                    RootRestoreState.Unresolved,
                    RestoreConfidence.Unknown,
                    "missing",
                    "root_inference_failed",
                    currentRoot,
                    currentRootAnchor);
                LogRootLayoutFallbackDiagnostic(
                    $"layout fallback failed to infer root: restoredRoot={m_HasRestoredRootExhibit}, " +
                    $"restoredExhibits={RestoredSessionExhibitCount}/{ExpectedSessionExhibitCount}, " +
                    $"{inferenceDiagnostics}, {DescribeRootCandidateState(currentRoot)}");
                return false;
            }

            if (inferredConfidence == RestoreConfidence.Unsafe)
            {
                SetRootRestoreRuntimeState(
                    RootRestoreState.Invalid,
                    inferredConfidence,
                    "inconsistent",
                    "root_inference_rejected",
                    currentRoot,
                    currentRootAnchor);
                LogRootLayoutFallbackDiagnostic(
                    $"layout fallback rejected inferred root: sampleCount={inferredSampleCount}, " +
                    $"maxPositionError={inferredMaxPositionErrorMeters:F4}, maxRotationError={inferredMaxRotationErrorDegrees:F2}, " +
                    $"{inferenceDiagnostics}, {DescribeRootCandidateState(currentRoot)}");
                return false;
            }

            if (currentRootAnchor != null)
            {
                currentRoot.SetParent(null, true);
                ParentLooseRootToModuleContainer(currentRoot);
                QueueAnchorNodeForDestruction(currentRootAnchor.gameObject);
            }

            PlacementRootMarker rootMarker = GetRootMarker();
            bool reusedExistingRootMarker = rootMarker != null;
            Transform rootTransform = rootMarker != null ? rootMarker.transform : CreateRootMarker(inferredRootPose);
            if (rootTransform == null)
            {
                LogRootLayoutFallbackDiagnostic(
                    $"layout fallback failed to materialize root: reuseExistingRootMarker={reusedExistingRootMarker}, " +
                    $"{inferenceDiagnostics}, {DescribeRootCandidateState(currentRoot)}");
                return false;
            }

            ParentLooseRootToModuleContainer(rootTransform);
            rootTransform.SetPositionAndRotation(inferredRootPose.position, inferredRootPose.rotation);
            m_RuntimeRootTrans = rootTransform;
            m_HasResolvedRootForPlayback = true;
            m_HasRestoredRootExhibit = false;
            SetRootMarkerVisible(ShouldShowRootMarkerInCurrentMode());
            SetRootRestoreRuntimeState(
                RootRestoreState.LayoutRecoveredLoose,
                inferredConfidence,
                inferredConfidence == RestoreConfidence.Unsafe ? "inconsistent" : string.Empty,
                "root_inference_succeeded",
                rootTransform,
                null);
            LogRootLayoutFallbackDiagnostic(
                $"layout fallback inferred root: materializeSource={(reusedExistingRootMarker ? "reuse_existing_root_marker" : "create_new_root_marker")}, " +
                $"sampleCount={inferredSampleCount}, maxPositionError={inferredMaxPositionErrorMeters:F4}, " +
                $"maxRotationError={inferredMaxRotationErrorDegrees:F2}, {inferenceDiagnostics}, {DescribeRootCandidateState(rootTransform)}");

            if (rootTransform.GetComponentInParent<ARAnchor>() == null)
            {
                TryBeginAnchorAttachment(
                    rootTransform,
                    new Pose(rootTransform.position, rootTransform.rotation),
                    "Anchor_Root",
                    isRoot: true,
                    saveLayout: false,
                    suppressStatusMessage: true,
                    displayName: "定位点",
                    promoteSessionCandidatesOnSuccess: true,
                    previousOriginalTrackableId: GetPreferredRootRestoreCandidateTrackableId());
            }

            return true;
        }

        private bool TryInferRootPoseFromRestoredExhibits(
            out Pose rootPose,
            out string diagnostics,
            out RestoreConfidence confidence,
            out int sampleCount,
            out float maxPositionErrorMeters,
            out float maxRotationErrorDegrees)
        {
            rootPose = default;
            diagnostics = string.Empty;
            confidence = RestoreConfidence.Unknown;
            sampleCount = 0;
            maxPositionErrorMeters = 0f;
            maxRotationErrorDegrees = 0f;
            if (!TryCollectRootInferenceSamples(
                    out var samples,
                    out _,
                    out Vector3 rootMarkerLocalScale,
                    out string baseDiagnostics))
            {
                diagnostics = baseDiagnostics;
                return false;
            }

            float bestError = float.MaxValue;
            Pose bestPose = default;
            bool hasBestPose = false;
            float bestMaxPositionErrorMeters = 0f;
            float bestMaxRotationErrorDegrees = 0f;

            for (int i = 0; i < samples.Count; i++)
            {
                Pose candidateRootPose = CalculateRootPoseFromLayoutSample(samples[i], rootMarkerLocalScale);
                EvaluateRootPoseErrorMetrics(
                    candidateRootPose,
                    samples,
                    rootMarkerLocalScale,
                    out float candidateError,
                    out float candidateMaxPositionErrorMeters,
                    out float candidateMaxRotationErrorDegrees);
                if (hasBestPose && candidateError >= bestError)
                    continue;

                bestError = candidateError;
                bestPose = candidateRootPose;
                bestMaxPositionErrorMeters = candidateMaxPositionErrorMeters;
                bestMaxRotationErrorDegrees = candidateMaxRotationErrorDegrees;
                hasBestPose = true;
            }

            if (!hasBestPose)
            {
                diagnostics = $"{baseDiagnostics}, bestScore=<none>";
                return false;
            }

            rootPose = bestPose;
            sampleCount = samples.Count;
            maxPositionErrorMeters = bestMaxPositionErrorMeters;
            maxRotationErrorDegrees = bestMaxRotationErrorDegrees;
            confidence = ClassifyRootPoseConfidence(sampleCount, maxPositionErrorMeters, maxRotationErrorDegrees);
            diagnostics =
                $"{baseDiagnostics}, bestScore={bestError:F6}, maxPositionError={maxPositionErrorMeters:F4}, " +
                $"maxRotationError={maxRotationErrorDegrees:F2}, confidence={FormatRestoreConfidence(confidence)}";
            return true;
        }

        private static Vector3 ScaleLayoutOffset(Vector3 relativePosition, Vector3 rootMarkerLocalScale)
        {
            return new Vector3(
                relativePosition.x * rootMarkerLocalScale.x,
                relativePosition.y * rootMarkerLocalScale.y,
                relativePosition.z * rootMarkerLocalScale.z);
        }

        private static Pose CalculateRootPoseFromLayoutSample(
            RootInferenceSample sample,
            Vector3 rootMarkerLocalScale)
        {
            Quaternion rootRotation = sample.exhibitTransform.rotation * Quaternion.Inverse(sample.layoutData.relativeRotation);
            Vector3 scaledOffset = ScaleLayoutOffset(sample.layoutData.relativePosition, rootMarkerLocalScale);
            Vector3 rootPosition = sample.exhibitTransform.position - (rootRotation * scaledOffset);
            return new Pose(rootPosition, rootRotation);
        }

        private void TryResolveMissingSessionObjectsFromLayout()
        {
            if (m_CurrentAppMode != ExhibitAppMode.Experience ||
                !m_HasLayoutLoaded ||
                m_CachedLayout.Count == 0 ||
                m_SessionSnapshotByInstanceId.Count == 0)
            {
                return;
            }

            if (!m_HasSeenFirstAddedEventForLayoutFallback)
            {
                if (!m_HasLoggedWaitingForFirstAddedEventForLayoutFallback)
                {
                    LogAnchorLifecycle("layout fallback deferred: waiting for first anchorsChanged added batch");
                    m_HasLoggedWaitingForFirstAddedEventForLayoutFallback = true;
                }

                return;
            }

            if (!m_HasResolvedRootForPlayback && !TryResolveRootFromLayoutFallback())
                return;

            Transform rootTransform = GetRootTransform();
            if (rootTransform == null)
            {
                LogRootLayoutFallbackDiagnostic(
                    $"layout fallback aborted after root resolution: rootTransform missing, " +
                    $"hasResolvedRootForPlayback={m_HasResolvedRootForPlayback}, restoredRoot={m_HasRestoredRootExhibit}, " +
                    $"restoredExhibits={RestoredSessionExhibitCount}/{ExpectedSessionExhibitCount}");
                return;
            }

            var sessionItems = BuildOrderedSessionSnapshotItems();
            bool createdFallbackObject = false;
            for (int i = 0; i < sessionItems.Count; i++)
            {
                SessionData data = sessionItems[i];
                if (data == null)
                    continue;

                string instanceId = NormalizeInstanceId(data.instanceID);
                if (string.IsNullOrEmpty(instanceId) || IsSessionInstanceAvailable(instanceId))
                    continue;

                if (TryGetSceneExhibitByInstanceId(instanceId, out var existingInfo) &&
                    existingInfo != null &&
                    existingInfo.transform != null)
                {
                    SessionRestoreState existingState = existingInfo.transform.GetComponentInParent<ARAnchor>() != null
                        ? GetAnchoredSessionRestoreState(data, existingInfo.transform.GetComponentInParent<ARAnchor>().trackableId)
                        : SessionRestoreState.LayoutRecoveredLoose;
                    TryEvaluateModulePoseConfidence(
                        data,
                        existingInfo.transform,
                        out RestoreConfidence existingConfidence,
                        out _,
                        out _,
                        out string failureReason);
                    MarkSessionExhibitAvailable(
                        data,
                        existingInfo.transform,
                        existingState,
                        existingConfidence,
                        "layout_fallback_reused_existing",
                        existingInfo.transform.GetComponentInParent<ARAnchor>(),
                        failureReason);
                    continue;
                }

                if (!TryGetLayoutSnapshotItem(instanceId, out var layoutData) || layoutData == null)
                    continue;

                createdFallbackObject |= TryCreateLayoutFallbackExhibit(rootTransform, data, layoutData);
            }

            if (createdFallbackObject)
                RefreshCurrentExhibitManipulationState();
        }

        private bool TryCreateLayoutFallbackExhibit(Transform rootTransform, SessionData data, LayoutData layoutData)
        {
            if (rootTransform == null || data == null || layoutData == null)
                return false;

            string instanceId = NormalizeInstanceId(data.instanceID);
            if (string.IsNullOrEmpty(instanceId) || CheckIfInstanceExists(instanceId))
                return false;

            if (!TryGetDeployableModuleDefinition(data.moduleId, out var definition) || definition.prefab == null)
            {
                LogAnchorLifecycle(
                    $"layout fallback skipped missing deployable module: moduleId={data.moduleId}, instanceId={instanceId}");
                return false;
            }

            Vector3 worldPosition = rootTransform.TransformPoint(layoutData.relativePosition);
            Quaternion worldRotation = rootTransform.rotation * layoutData.relativeRotation;

            GameObject exhibitObject = Instantiate(definition.prefab);
            exhibitObject.name = $"Exhibit_{data.moduleId}_{data.displayName}_{instanceId}";
            AttachExhibitInfo(
                exhibitObject,
                data.moduleId,
                data.displayName,
                instanceId,
                data.placementContentHidden,
                markPersistenceDirty: false);
            exhibitObject.transform.SetPositionAndRotation(worldPosition, worldRotation);
            ParentLooseExhibitToModuleContainer(exhibitObject.transform);
            exhibitObject.transform.localScale = layoutData.scale;

            bool shouldPreview = m_CurrentAppMode == ExhibitAppMode.Placement && m_EnablePlacementPreviewInPlacementMode;
            if (m_HideRestoredExhibitsUntilPlayback && !shouldPreview)
                exhibitObject.SetActive(false);
            else if (shouldPreview)
                EnablePlacementPreviewForExhibitInPlacementMode(exhibitObject.transform);

            MarkSessionExhibitAvailable(
                data,
                exhibitObject.transform,
                SessionRestoreState.LayoutRecoveredLoose,
                GetLayoutRecoveryConfidence(),
                "layout_recovery_started");

            TryBeginAnchorAttachment(
                exhibitObject.transform,
                new Pose(exhibitObject.transform.position, exhibitObject.transform.rotation),
                $"Anchor_{data.displayName}",
                isRoot: false,
                saveLayout: false,
                suppressStatusMessage: true,
                displayName: data.displayName,
                promoteSessionCandidatesOnSuccess: true,
                previousOriginalTrackableId: GetPreferredRestoreCandidateTrackableId(data));
            return true;
        }

        private void RefreshExperienceRestoreHealthAndRecovery(string reason)
        {
            if (m_CurrentAppMode != ExhibitAppMode.Experience)
                return;

            if (GetRootTransform() != null)
                RefreshRootRuntimeHealth(reason);

            var sessionItems = BuildOrderedSessionSnapshotItems();
            for (int i = 0; i < sessionItems.Count; i++)
            {
                SessionData data = sessionItems[i];
                if (data == null)
                    continue;

                string instanceId = NormalizeInstanceId(data.instanceID);
                if (string.IsNullOrEmpty(instanceId) ||
                    !TryGetSceneExhibitByInstanceId(instanceId, out var info) ||
                    info == null ||
                    info.transform == null)
                {
                    continue;
                }

                RefreshSessionExhibitRuntimeHealth(data, info.transform, reason);
            }
        }

        private void RefreshRootRuntimeHealth(string reason)
        {
            Transform rootTransform = GetRootTransform();
            if (rootTransform == null)
            {
                SetRootRestoreRuntimeState(
                    RootRestoreState.Unresolved,
                    RestoreConfidence.Unknown,
                    "missing",
                    "root_missing",
                    null,
                    null);
                return;
            }

            ARAnchor rootAnchor = rootTransform.GetComponentInParent<ARAnchor>();
            Pose currentRootPose = new Pose(rootTransform.position, rootTransform.rotation);
            bool hasSamples = TryEvaluateRootPoseConfidence(
                currentRootPose,
                out RestoreConfidence confidence,
                out string diagnostics,
                out _,
                out float maxPositionErrorMeters,
                out float maxRotationErrorDegrees);

            if (rootAnchor != null && hasSamples && confidence == RestoreConfidence.Unsafe)
            {
                LogAnchorLifecycle(
                    $"root_inference_started: reason={reason}, maxPositionError={maxPositionErrorMeters:F4}, " +
                    $"maxRotationError={maxRotationErrorDegrees:F2}, diagnostics={diagnostics}");
                TryResolveRootFromLayoutFallback();
                return;
            }

            RestoreConfidence finalConfidence = hasSamples ? confidence : RestoreConfidence.Weak;
            RootRestoreState restoreState = rootAnchor != null
                ? GetAnchoredRootRestoreState(rootAnchor.trackableId)
                : RootRestoreState.LayoutRecoveredLoose;
            string failureReason = finalConfidence == RestoreConfidence.Unsafe ? "inconsistent" : string.Empty;
            SetRootRestoreRuntimeState(
                restoreState,
                finalConfidence,
                failureReason,
                "root_health_refresh",
                rootTransform,
                rootAnchor);
        }

        private void RefreshSessionExhibitRuntimeHealth(SessionData data, Transform exhibitTransform, string reason)
        {
            if (data == null || exhibitTransform == null)
                return;

            ARAnchor anchor = exhibitTransform.GetComponentInParent<ARAnchor>();
            TryEvaluateModulePoseConfidence(
                data,
                exhibitTransform,
                out RestoreConfidence confidence,
                out float positionErrorMeters,
                out float rotationErrorDegrees,
                out string failureReason);

            if (anchor != null && confidence == RestoreConfidence.Unsafe && IsRootReadyForPlayback)
            {
                TryRecoverDriftedSessionExhibit(
                    data,
                    exhibitTransform,
                    anchor,
                    positionErrorMeters,
                    rotationErrorDegrees,
                    reason);
                return;
            }

            SessionRestoreState restoreState = anchor != null
                ? GetAnchoredSessionRestoreState(data, anchor.trackableId)
                : SessionRestoreState.LayoutRecoveredLoose;
            MarkSessionExhibitAvailable(
                data,
                exhibitTransform,
                restoreState,
                confidence,
                "restore_health_refresh",
                anchor,
                failureReason);
        }

        private bool TryRecoverDriftedSessionExhibit(
            SessionData data,
            Transform exhibitTransform,
            ARAnchor currentAnchor,
            float positionErrorMeters,
            float rotationErrorDegrees,
            string reason)
        {
            if (data == null || exhibitTransform == null || currentAnchor == null)
                return false;

            string instanceId = NormalizeInstanceId(data.instanceID);
            if (string.IsNullOrEmpty(instanceId) ||
                !TryGetLayoutSnapshotItem(instanceId, out var layoutData) ||
                layoutData == null ||
                !TryGetExpectedModulePoseFromLayout(instanceId, out Pose expectedPose))
            {
                return false;
            }

            LogAnchorLifecycle(
                $"layout_recovery_started: moduleId={NormalizeModuleId(data.moduleId)}, instanceId={instanceId}, " +
                $"reason={reason}, previousTrackableId={currentAnchor.trackableId}, " +
                $"positionError={positionErrorMeters:F4}, rotationError={rotationErrorDegrees:F2}, " +
                $"{DescribeAnchorState(currentAnchor)}");

            CancelPendingAnchorAttachmentForTarget(exhibitTransform);
            exhibitTransform.SetParent(null, true);
            ParentLooseExhibitToModuleContainer(exhibitTransform);
            QueueAnchorNodeForDestruction(currentAnchor.gameObject);
            exhibitTransform.SetPositionAndRotation(expectedPose.position, expectedPose.rotation);
            exhibitTransform.localScale = layoutData.scale;

            MarkSessionExhibitAvailable(
                data,
                exhibitTransform,
                SessionRestoreState.LayoutRecoveredLoose,
                GetLayoutRecoveryConfidence(),
                "layout_recovery_applied",
                null);

            if (!IsAnchorAttachmentPending(exhibitTransform))
            {
                TryBeginAnchorAttachment(
                    exhibitTransform,
                    expectedPose,
                    $"Anchor_{data.displayName}",
                    isRoot: false,
                    saveLayout: false,
                    suppressStatusMessage: true,
                    displayName: data.displayName,
                    promoteSessionCandidatesOnSuccess: true,
                    previousOriginalTrackableId: GetPreferredRestoreCandidateTrackableId(data));
            }

            return true;
        }

        private bool TryAdoptExistingSessionExhibitForAnchor(
            ARAnchor anchor,
            SessionData data,
            bool deferPostRestoreRefresh,
            out Transform exhibitTransform,
            out bool attachedThisPass)
        {
            exhibitTransform = null;
            attachedThisPass = false;
            if (anchor == null || data == null)
                return false;

            string instanceId = NormalizeInstanceId(data.instanceID);
            if (string.IsNullOrEmpty(instanceId) ||
                !TryGetSceneExhibitByInstanceId(instanceId, out var existingInfo) ||
                existingInfo == null ||
                existingInfo.transform == null)
            {
                return false;
            }

            exhibitTransform = existingInfo.transform;
            ARAnchor existingAnchor = exhibitTransform.GetComponentInParent<ARAnchor>();
            if (existingAnchor == anchor)
                return true;

            TryEvaluateModulePoseConfidence(
                data,
                anchor.transform,
                out RestoreConfidence candidateConfidence,
                out float candidatePositionErrorMeters,
                out float candidateRotationErrorDegrees,
                out string candidateFailureReason);

            if (candidateConfidence == RestoreConfidence.Unsafe)
            {
                LogAnchorLifecycle(
                    $"restore_rejected_inconsistent: moduleId={NormalizeModuleId(data.moduleId)}, instanceId={instanceId}, " +
                    $"candidateTrackableId={anchor.trackableId}, positionError={candidatePositionErrorMeters:F4}, " +
                    $"rotationError={candidateRotationErrorDegrees:F2}, reason={candidateFailureReason}, {DescribeAnchorState(anchor)}");
                return false;
            }

            if (existingAnchor != null && existingAnchor != anchor)
            {
                TryEvaluateModulePoseConfidence(
                    data,
                    exhibitTransform,
                    out RestoreConfidence currentConfidence,
                    out _,
                    out _,
                    out _);

                bool shouldSwitch = currentConfidence == RestoreConfidence.Unsafe
                    ? candidateConfidence != RestoreConfidence.Unsafe
                    : ShouldPreferRestoreCandidate(
                        GetOriginalTrackableId(data),
                        GetFallbackTrackableId(data),
                        anchor.trackableId.ToString(),
                        existingAnchor.trackableId.ToString());
                if (!shouldSwitch)
                {
                    LogAnchorLifecycle(
                        $"restore skipped late anchor after anchored restore: moduleId={data.moduleId}, instanceId={instanceId}, " +
                        $"activeAnchor={existingAnchor.trackableId}, activeSource={DescribeRestoreSource(data, existingAnchor.trackableId.ToString())}, " +
                        $"lateAnchor={anchor.trackableId}, lateSource={DescribeRestoreSource(data, anchor.trackableId.ToString())}, " +
                        $"activeConfidence={FormatRestoreConfidence(currentConfidence)}, candidateConfidence={FormatRestoreConfidence(candidateConfidence)}");
                    return true;
                }

                if (anchor.transform.childCount > 0)
                {
                    LogAnchorLifecycle(
                        $"restore skipped preferred late anchor occupied: moduleId={data.moduleId}, instanceId={instanceId}, " +
                        $"activeAnchor={existingAnchor.trackableId}, activeSource={DescribeRestoreSource(data, existingAnchor.trackableId.ToString())}, " +
                        $"preferredAnchor={anchor.trackableId}, preferredSource={DescribeRestoreSource(data, anchor.trackableId.ToString())}, {DescribeAnchorState(anchor)}");
                    return true;
                }

                exhibitTransform.SetParent(anchor.transform, false);
                exhibitTransform.localPosition = Vector3.zero;
                exhibitTransform.localRotation = Quaternion.identity;
                if (TryGetLayoutSnapshotScale(data.instanceID, out var preferredScale))
                    exhibitTransform.localScale = preferredScale;

                bool shouldPreviewPreferredAnchor = m_CurrentAppMode == ExhibitAppMode.Placement && m_EnablePlacementPreviewInPlacementMode;
                if (shouldPreviewPreferredAnchor)
                    EnablePlacementPreviewForExhibitInPlacementMode(exhibitTransform);

                if (!deferPostRestoreRefresh)
                    RefreshCurrentExhibitManipulationState();
                if (shouldPreviewPreferredAnchor && !deferPostRestoreRefresh)
                    InteractionModule.ForceRefreshPlacementGuidePathPreviews();

                string switchEvent = string.Equals(
                    ClassifyRestoreTrackableSource(
                        GetOriginalTrackableId(data),
                        GetFallbackTrackableId(data),
                        anchor.trackableId.ToString()),
                    "original",
                    StringComparison.Ordinal)
                    ? "late_original_takeover"
                    : "restore_switched_to_candidate";
                LogAnchorLifecycle(
                    $"{switchEvent}: moduleId={data.moduleId}, instanceId={instanceId}, " +
                    $"previousAnchor={existingAnchor.trackableId}, previousSource={DescribeRestoreSource(data, existingAnchor.trackableId.ToString())}, " +
                    $"candidateAnchor={anchor.trackableId}, candidateSource={DescribeRestoreSource(data, anchor.trackableId.ToString())}, " +
                    $"candidateConfidence={FormatRestoreConfidence(candidateConfidence)}, {DescribeAnchorState(anchor)}, {DescribeTransformPose(exhibitTransform)}");
                attachedThisPass = true;
                return true;
            }

            if (anchor.transform.childCount > 0)
            {
                LogAnchorLifecycle(
                    $"restore skipped anchor occupied before adopting fallback: moduleId={data.moduleId}, instanceId={instanceId}, " +
                    $"{DescribeAnchorState(anchor)}");
                return true;
            }

            CancelPendingAnchorAttachmentForTarget(exhibitTransform);
            AttachExhibitInfo(
                exhibitTransform.gameObject,
                data.moduleId,
                data.displayName,
                data.instanceID,
                data.placementContentHidden,
                markPersistenceDirty: false);

            exhibitTransform.SetParent(anchor.transform, false);
            exhibitTransform.localPosition = Vector3.zero;
            exhibitTransform.localRotation = Quaternion.identity;
            if (TryGetLayoutSnapshotScale(data.instanceID, out var savedScale))
                exhibitTransform.localScale = savedScale;

            bool shouldPreview = m_CurrentAppMode == ExhibitAppMode.Placement && m_EnablePlacementPreviewInPlacementMode;
            if (shouldPreview)
                EnablePlacementPreviewForExhibitInPlacementMode(exhibitTransform);

            if (!deferPostRestoreRefresh)
                RefreshCurrentExhibitManipulationState();
            if (shouldPreview && !deferPostRestoreRefresh)
                InteractionModule.ForceRefreshPlacementGuidePathPreviews();

            LogAnchorLifecycle(
                $"restore adopted existing fallback: moduleId={data.moduleId}, instanceId={instanceId}, " +
                $"candidateConfidence={FormatRestoreConfidence(candidateConfidence)}, {DescribeAnchorState(anchor)}, {DescribeTransformPose(exhibitTransform)}");
            attachedThisPass = true;
            return true;
        }
    }
}
