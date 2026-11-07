using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace VFXViewer
{
    public partial class ExhibitPlacementManager
    {
        private const float AnchorSubsystemRetryPollIntervalSeconds = 0.2f;
        private const float AnchorSubsystemRetryTimeoutSeconds = 5f;

        private void LogAnchorLifecycle(string message)
        {
            if (!m_EnableAnchorLifecycleLogs)
                return;

            string formattedMessage = $"[AnchorLifecycle] {message}";
            Debug.Log(formattedMessage, this);
            PlacementDebugFileLogger.Log(formattedMessage);
        }

        private static string FormatVector3(Vector3 value)
        {
            return $"({value.x:F3}, {value.y:F3}, {value.z:F3})";
        }

        private static string FormatRotation(Quaternion value)
        {
            return FormatVector3(value.eulerAngles);
        }

        private static string FormatPose(Pose pose)
        {
            return $"pos={FormatVector3(pose.position)}, rot={FormatRotation(pose.rotation)}";
        }

        private static string DescribeTransformPose(Transform target)
        {
            if (target == null)
                return "target=null";

            string parentName = target.parent != null ? target.parent.name : "<null>";
            return
                $"target={target.name}, parent={parentName}, worldPos={FormatVector3(target.position)}, worldRot={FormatRotation(target.rotation)}, localPos={FormatVector3(target.localPosition)}, localRot={FormatRotation(target.localRotation)}";
        }

        private static string DescribeAnchorState(ARAnchor anchor)
        {
            if (anchor == null)
                return "anchor=null";

            return
                $"trackableId={anchor.trackableId}, pending={anchor.pending}, name={anchor.name}, worldPos={FormatVector3(anchor.transform.position)}, worldRot={FormatRotation(anchor.transform.rotation)}, childCount={anchor.transform.childCount}";
        }

        private sealed class PendingAnchorAttachment
        {
            public TrackableId trackableId;
            public Transform target;
            public bool isRoot;
            public bool saveLayout;
            public bool suppressStatusMessage;
            public string anchorName;
            public string displayName;
            public bool promoteSessionCandidatesOnSuccess;
            public string previousOriginalTrackableId;
            public bool persistOnSuccess;
            public bool transientRuntime;
            public string runtimeInstanceId;
        }

        private readonly Dictionary<TrackableId, PendingAnchorAttachment> m_PendingAnchorAttachments =
            new Dictionary<TrackableId, PendingAnchorAttachment>();
        private readonly Dictionary<int, TrackableId> m_PendingAnchorTrackableIdsByTarget =
            new Dictionary<int, TrackableId>();
        private readonly Dictionary<int, int> m_InFlightAnchorAttachmentRequestVersionByTarget =
            new Dictionary<int, int>();
        private readonly Dictionary<int, Coroutine> m_RetryAnchorAttachmentCoroutinesByTarget =
            new Dictionary<int, Coroutine>();

        public bool HasPendingAnchorAttachments =>
            m_PendingAnchorAttachments.Count > 0 ||
            m_InFlightAnchorAttachmentRequestVersionByTarget.Count > 0 ||
            m_RetryAnchorAttachmentCoroutinesByTarget.Count > 0;

        private bool IsAnchorAttachmentPending(Transform target)
        {
            if (target == null)
                return false;

            int targetId = target.GetInstanceID();
            return m_PendingAnchorTrackableIdsByTarget.ContainsKey(targetId) ||
                   m_InFlightAnchorAttachmentRequestVersionByTarget.ContainsKey(targetId) ||
                   m_RetryAnchorAttachmentCoroutinesByTarget.ContainsKey(targetId);
        }

        private bool TryGetPendingAnchorTrackableId(Transform target, out TrackableId trackableId)
        {
            trackableId = TrackableId.invalidId;
            if (target == null)
                return false;

            return m_PendingAnchorTrackableIdsByTarget.TryGetValue(target.GetInstanceID(), out trackableId);
        }

        private bool TryBeginAnchorAttachment(
            Transform target,
            Pose pose,
            string anchorName,
            bool isRoot,
            bool saveLayout,
            bool suppressStatusMessage,
            string displayName,
            bool promoteSessionCandidatesOnSuccess = false,
            string previousOriginalTrackableId = null,
            bool persistOnSuccess = true,
            bool transientRuntime = false,
            string runtimeInstanceId = null)
        {
            if (target == null)
                return false;

            if (IsAnchorAttachmentPending(target))
            {
                LogAnchorLifecycle($"begin skipped already pending: {displayName}");
                if (!suppressStatusMessage)
                    onStatusMessage?.Invoke($"{displayName} 的空间锚点正在创建中，请稍候。");
                return true;
            }

            if (m_AnchorManager == null || !m_AnchorManager.enabled)
            {
                if (!suppressStatusMessage)
                    onStatusMessage?.Invoke($"创建锚点失败：未找到可用的 ARAnchorManager（{displayName}）。");
                return false;
            }

            if (!HasUsableAnchorSubsystem(out string anchorSubsystemIssue))
            {
                if (TryScheduleAnchorAttachmentRetry(
                        target,
                        pose,
                        anchorName,
                        isRoot,
                        saveLayout,
                        suppressStatusMessage,
                        displayName,
                        anchorSubsystemIssue,
                        promoteSessionCandidatesOnSuccess,
                        previousOriginalTrackableId,
                        persistOnSuccess,
                        transientRuntime,
                        runtimeInstanceId))
                {
                    if (!suppressStatusMessage)
                        onStatusMessage?.Invoke($"空间锚点系统尚未就绪，正在等待：{displayName}");
                    RefreshPlacementProgressInDirector();
                    return true;
                }

                LogAnchorLifecycle($"begin skipped unavailable subsystem: {displayName}, reason={anchorSubsystemIssue}");
                Debug.LogWarning($"创建锚点失败：{displayName}，{anchorSubsystemIssue}", this);
                if (!suppressStatusMessage)
                    onStatusMessage?.Invoke($"创建锚点失败：{displayName}，{anchorSubsystemIssue}");
                RefreshPlacementProgressInDirector();
                return false;
            }

            LogAnchorLifecycle(
                $"begin request: displayName={displayName}, isRoot={isRoot}, requestedPose={FormatPose(pose)}, {DescribeTransformPose(target)}");
            int targetInstanceId = target.GetInstanceID();
            int requestVersion = RegisterInFlightAnchorAttachment(targetInstanceId);
            _ = BeginAnchorAttachmentAsync(
                target,
                pose,
                anchorName,
                isRoot,
                saveLayout,
                suppressStatusMessage,
                displayName,
                promoteSessionCandidatesOnSuccess,
                previousOriginalTrackableId,
                persistOnSuccess,
                transientRuntime,
                runtimeInstanceId,
                targetInstanceId,
                requestVersion);
            return true;
        }

        private async Task BeginAnchorAttachmentAsync(
            Transform target,
            Pose pose,
            string anchorName,
            bool isRoot,
            bool saveLayout,
            bool suppressStatusMessage,
            string displayName,
            bool promoteSessionCandidatesOnSuccess,
            string previousOriginalTrackableId,
            bool persistOnSuccess,
            bool transientRuntime,
            string runtimeInstanceId,
            int targetInstanceId,
            int requestVersion)
        {
            try
            {
                var result = await m_AnchorManager.TryAddAnchorAsync(pose);
                if (!result.status.IsSuccess() || result.value == null)
                {
                    CompleteInFlightAnchorAttachment(targetInstanceId, requestVersion);
                    LogAnchorLifecycle(
                        $"begin failed: displayName={displayName}, requestedPose={FormatPose(pose)}, status={result.status}, {DescribeTransformPose(target)}");
                    if (transientRuntime)
                        HandleTransientRuntimeAnchorRequestFailure(runtimeInstanceId, isRoot, target, pose, displayName, result.status.ToString());
                    if (!suppressStatusMessage)
                        onStatusMessage?.Invoke($"创建锚点失败：{displayName}");
                    RefreshPlacementProgressInDirector();
                    return;
                }

                var anchor = result.value;
                if (!IsCurrentInFlightAnchorAttachment(targetInstanceId, requestVersion) || !isActiveAndEnabled)
                {
                    CompleteInFlightAnchorAttachment(targetInstanceId, requestVersion);
                    LogAnchorLifecycle(
                        $"begin stale result ignored: displayName={displayName}, requestedPose={FormatPose(pose)}, trackableId={anchor.trackableId}");
                    QueueAnchorNodeForDestruction(anchor.gameObject);
                    return;
                }

                LogAnchorLifecycle(
                    $"begin returned: displayName={displayName}, requestedPose={FormatPose(pose)}, {DescribeAnchorState(anchor)}, {DescribeTransformPose(target)}");
                if (target == null)
                {
                    CompleteInFlightAnchorAttachment(targetInstanceId, requestVersion);
                    LogAnchorLifecycle($"begin target missing, destroying anchor: {displayName}, trackableId={anchor.trackableId}");
                    QueueAnchorNodeForDestruction(anchor.gameObject);
                    return;
                }

                RegisterPendingAnchorAttachment(
                    anchor,
                    target,
                    isRoot,
                    saveLayout,
                    suppressStatusMessage,
                    anchorName,
                    displayName,
                    promoteSessionCandidatesOnSuccess,
                    previousOriginalTrackableId,
                    persistOnSuccess,
                    transientRuntime,
                    runtimeInstanceId,
                    targetInstanceId,
                    requestVersion);
            }
            catch (Exception ex)
            {
                CompleteInFlightAnchorAttachment(targetInstanceId, requestVersion);
                LogAnchorLifecycle($"begin exception: {displayName}, {ex}");
                if (transientRuntime)
                    HandleTransientRuntimeAnchorRequestFailure(runtimeInstanceId, isRoot, target, pose, displayName, ex.GetType().Name);
                Debug.LogWarning($"创建锚点异常：{displayName}\n{ex}", this);
                if (!suppressStatusMessage)
                    onStatusMessage?.Invoke($"创建锚点失败：{displayName}");
                RefreshPlacementProgressInDirector();
            }
        }

        private bool HasUsableAnchorSubsystem(out string issue)
        {
            issue = string.Empty;
            if (m_AnchorManager == null)
            {
                issue = "未找到 ARAnchorManager";
                return false;
            }

            if (!m_AnchorManager.enabled)
            {
                issue = "ARAnchorManager 未启用";
                return false;
            }

            XRAnchorSubsystem subsystem = m_AnchorManager.subsystem;
            if (subsystem == null)
            {
                issue = Application.isEditor
                    ? "编辑器当前没有可用的 AR Anchor 子系统"
                    : "当前没有可用的 AR Anchor 子系统";
                return false;
            }

            if (!subsystem.running)
            {
                issue = "AR Anchor 子系统尚未启动";
                return false;
            }

            return true;
        }

        private bool TryScheduleAnchorAttachmentRetry(
            Transform target,
            Pose pose,
            string anchorName,
            bool isRoot,
            bool saveLayout,
            bool suppressStatusMessage,
            string displayName,
            string issue,
            bool promoteSessionCandidatesOnSuccess,
            string previousOriginalTrackableId,
            bool persistOnSuccess,
            bool transientRuntime,
            string runtimeInstanceId)
        {
            if (target == null)
                return false;

            int targetInstanceId = target.GetInstanceID();
            if (m_RetryAnchorAttachmentCoroutinesByTarget.ContainsKey(targetInstanceId))
            {
                LogAnchorLifecycle($"begin retry already scheduled: {displayName}, reason={issue}");
                return true;
            }

            Coroutine retryCoroutine = StartCoroutine(RetryBeginAnchorAttachmentWhenSubsystemReady(
                target,
                pose,
                anchorName,
                isRoot,
                saveLayout,
                suppressStatusMessage,
                displayName,
                targetInstanceId,
                issue,
                promoteSessionCandidatesOnSuccess,
                previousOriginalTrackableId,
                persistOnSuccess,
                transientRuntime,
                runtimeInstanceId));
            m_RetryAnchorAttachmentCoroutinesByTarget[targetInstanceId] = retryCoroutine;
            LogAnchorLifecycle($"begin retry scheduled: {displayName}, reason={issue}");
            return true;
        }

        private IEnumerator RetryBeginAnchorAttachmentWhenSubsystemReady(
            Transform target,
            Pose pose,
            string anchorName,
            bool isRoot,
            bool saveLayout,
            bool suppressStatusMessage,
            string displayName,
            int targetInstanceId,
            string initialIssue,
            bool promoteSessionCandidatesOnSuccess,
            string previousOriginalTrackableId,
            bool persistOnSuccess,
            bool transientRuntime,
            string runtimeInstanceId)
        {
            float deadline = Time.realtimeSinceStartup + AnchorSubsystemRetryTimeoutSeconds;
            string lastIssue = initialIssue;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (target == null)
                {
                    LogAnchorLifecycle($"begin retry target missing: {displayName}");
                    m_RetryAnchorAttachmentCoroutinesByTarget.Remove(targetInstanceId);
                    yield break;
                }

                if (target.GetComponentInParent<ARAnchor>() != null)
                {
                    LogAnchorLifecycle($"begin retry skipped target already anchored: {displayName}");
                    m_RetryAnchorAttachmentCoroutinesByTarget.Remove(targetInstanceId);
                    yield break;
                }

                if (HasUsableAnchorSubsystem(out lastIssue))
                {
                    LogAnchorLifecycle($"begin retry subsystem ready: {displayName}");
                    m_RetryAnchorAttachmentCoroutinesByTarget.Remove(targetInstanceId);
                    TryBeginAnchorAttachment(
                        target,
                        pose,
                        anchorName,
                        isRoot,
                        saveLayout,
                        suppressStatusMessage,
                        displayName,
                        promoteSessionCandidatesOnSuccess,
                        previousOriginalTrackableId,
                        persistOnSuccess,
                        transientRuntime,
                        runtimeInstanceId);
                    yield break;
                }

                yield return new WaitForSecondsRealtime(AnchorSubsystemRetryPollIntervalSeconds);
            }

            m_RetryAnchorAttachmentCoroutinesByTarget.Remove(targetInstanceId);
            LogAnchorLifecycle($"begin retry timeout: {displayName}, reason={lastIssue}");
            if (transientRuntime)
                HandleTransientRuntimeAnchorRequestFailure(runtimeInstanceId, isRoot, target, pose, displayName, lastIssue);
            Debug.LogWarning($"创建锚点失败：{displayName}，等待空间锚点系统就绪超时。最后状态：{lastIssue}", this);
            if (!suppressStatusMessage)
                onStatusMessage?.Invoke($"创建锚点失败：{displayName}，等待空间锚点系统就绪超时。");
            RefreshPlacementProgressInDirector();
        }

        private void RegisterPendingAnchorAttachment(
            ARAnchor anchor,
            Transform target,
            bool isRoot,
            bool saveLayout,
            bool suppressStatusMessage,
            string anchorName,
            string displayName,
            bool promoteSessionCandidatesOnSuccess,
            string previousOriginalTrackableId,
            bool persistOnSuccess,
            bool transientRuntime,
            string runtimeInstanceId,
            int targetInstanceId,
            int requestVersion)
        {
            if (anchor == null || target == null)
            {
                CompleteInFlightAnchorAttachment(targetInstanceId, requestVersion);
                return;
            }

            TryParentAnchorToModuleContainer(anchor.transform);
            if (!string.IsNullOrWhiteSpace(anchorName))
                anchor.gameObject.name = anchorName;

            var pending = new PendingAnchorAttachment
            {
                trackableId = anchor.trackableId,
                target = target,
                isRoot = isRoot,
                saveLayout = saveLayout,
                suppressStatusMessage = suppressStatusMessage,
                anchorName = anchorName,
                displayName = displayName,
                promoteSessionCandidatesOnSuccess = promoteSessionCandidatesOnSuccess,
                previousOriginalTrackableId = previousOriginalTrackableId,
                persistOnSuccess = persistOnSuccess,
                transientRuntime = transientRuntime,
                runtimeInstanceId = runtimeInstanceId
            };

            CompleteInFlightAnchorAttachment(targetInstanceId, requestVersion);
            m_PendingAnchorAttachments[anchor.trackableId] = pending;
            m_PendingAnchorTrackableIdsByTarget[targetInstanceId] = anchor.trackableId;
            LogAnchorLifecycle(
                $"registered pending: displayName={displayName}, anchorName={anchor.gameObject.name}, {DescribeAnchorState(anchor)}, {DescribeTransformPose(target)}");
            RefreshCurrentExhibitManipulationState();
        }

        private int RegisterInFlightAnchorAttachment(int targetInstanceId)
        {
            int requestVersion = 1;
            if (m_InFlightAnchorAttachmentRequestVersionByTarget.TryGetValue(targetInstanceId, out int existingVersion))
                requestVersion = existingVersion + 1;

            m_InFlightAnchorAttachmentRequestVersionByTarget[targetInstanceId] = requestVersion;
            return requestVersion;
        }

        private bool IsCurrentInFlightAnchorAttachment(int targetInstanceId, int requestVersion)
        {
            return m_InFlightAnchorAttachmentRequestVersionByTarget.TryGetValue(targetInstanceId, out int currentVersion) &&
                   currentVersion == requestVersion;
        }

        private void CompleteInFlightAnchorAttachment(int targetInstanceId, int requestVersion)
        {
            if (IsCurrentInFlightAnchorAttachment(targetInstanceId, requestVersion))
                m_InFlightAnchorAttachmentRequestVersionByTarget.Remove(targetInstanceId);
        }

        private bool CancelInFlightAnchorAttachmentForTarget(Transform target)
        {
            if (target == null)
                return false;

            int targetInstanceId = target.GetInstanceID();
            if (!m_InFlightAnchorAttachmentRequestVersionByTarget.Remove(targetInstanceId))
                return false;

            LogAnchorLifecycle($"cancel in-flight by target: {target.name}, targetInstanceId={targetInstanceId}");
            return true;
        }

        private bool CancelRetryingAnchorAttachmentForTarget(Transform target)
        {
            if (target == null)
                return false;

            int targetInstanceId = target.GetInstanceID();
            if (!m_RetryAnchorAttachmentCoroutinesByTarget.TryGetValue(targetInstanceId, out Coroutine retryCoroutine))
                return false;

            if (retryCoroutine != null)
                StopCoroutine(retryCoroutine);

            m_RetryAnchorAttachmentCoroutinesByTarget.Remove(targetInstanceId);
            LogAnchorLifecycle($"cancel retry by target: {target.name}, targetInstanceId={targetInstanceId}");
            return true;
        }

        private void TryCompletePendingAnchorAttachments(IEnumerable<ARAnchor> anchors)
        {
            if (anchors == null)
                return;

            foreach (var anchor in anchors)
            {
                LogAnchorLifecycle($"event candidate: {DescribeAnchorState(anchor)}");
                TryCompletePendingAnchorAttachment(anchor);
            }
        }

        private bool TryCompletePendingAnchorAttachment(ARAnchor anchor)
        {
            if (anchor == null)
                return false;

            if (!m_PendingAnchorAttachments.TryGetValue(anchor.trackableId, out var pending))
                return false;

            if (anchor.pending)
            {
                LogAnchorLifecycle(
                    $"complete proceeding while provider still pending: displayName={pending.displayName}, isRoot={pending.isRoot}, {DescribeAnchorState(anchor)}, {DescribeTransformPose(pending.target)}");
            }

            LogAnchorLifecycle(
                $"complete match: displayName={pending.displayName}, isRoot={pending.isRoot}, {DescribeAnchorState(anchor)}, {DescribeTransformPose(pending.target)}");
            m_PendingAnchorAttachments.Remove(anchor.trackableId);

            if (pending.target == null)
            {
                LogAnchorLifecycle($"complete aborted target missing: {pending.displayName}, trackableId={anchor.trackableId}");
                QueueAnchorNodeForDestruction(anchor.gameObject);
                RefreshCurrentExhibitManipulationState();
                RefreshPlacementProgressInDirector();
                return false;
            }

            m_PendingAnchorTrackableIdsByTarget.Remove(pending.target.GetInstanceID());
            TryParentAnchorToModuleContainer(anchor.transform);
            if (!string.IsNullOrWhiteSpace(pending.anchorName))
                anchor.gameObject.name = pending.anchorName;

            pending.target.SetParent(anchor.transform, false);
            pending.target.localPosition = Vector3.zero;
            pending.target.localRotation = Quaternion.identity;
            LogAnchorLifecycle(
                $"complete attached: displayName={pending.displayName}, isRoot={pending.isRoot}, {DescribeAnchorState(anchor)}, {DescribeTransformPose(pending.target)}");

            if (pending.transientRuntime)
            {
                HandleCompletedTransientRuntimeAnchorAttachment(pending, anchor);
                RefreshCurrentExhibitManipulationState();
                RefreshPlacementProgressInDirector();
                return true;
            }

            if (pending.isRoot)
            {
                m_RuntimeRootTrans = pending.target;
                m_HasRestoredRootExhibit = true;
                m_RootSessionTrackableId = anchor.trackableId;
                string rootOriginalTrackableId = pending.promoteSessionCandidatesOnSuccess &&
                                                 !string.IsNullOrWhiteSpace(pending.previousOriginalTrackableId)
                    ? pending.previousOriginalTrackableId
                    : anchor.trackableId.ToString();
                string rootFallbackTrackableId = pending.promoteSessionCandidatesOnSuccess &&
                                                 !string.IsNullOrWhiteSpace(pending.previousOriginalTrackableId)
                    ? anchor.trackableId.ToString()
                    : null;
                SetRootSessionTrackableIds(rootOriginalTrackableId, rootFallbackTrackableId);
                m_HasResolvedRootForPlayback = true;
                TryEvaluateRootPoseConfidence(
                    new Pose(pending.target.position, pending.target.rotation),
                    out RestoreConfidence rootConfidence,
                    out _,
                    out _,
                    out _,
                    out _);
                SetRootRestoreRuntimeState(
                    GetAnchoredRootRestoreState(anchor.trackableId),
                    rootConfidence == RestoreConfidence.Unknown ? RestoreConfidence.Weak : rootConfidence,
                    string.Empty,
                    "layout_recovery_anchor_created_root",
                    pending.target,
                    anchor);
                CleanupOrphanRootAnchors(pending.target);
                if (pending.persistOnSuccess)
                    SaveRootStateToFilesPreservingCandidates(saveLayout: !pending.promoteSessionCandidatesOnSuccess && HasAnySceneExhibits());
                if (!pending.suppressStatusMessage)
                    onStatusMessage?.Invoke("定位点已固定。");
            }
            else
            {
                if (TryGetRegisteredExhibitInfo(pending.target, out var info))
                {
                    string instanceId = NormalizeInstanceId(info.instanceID);
                    if (pending.promoteSessionCandidatesOnSuccess)
                    {
                        string originalTrackableId = !string.IsNullOrWhiteSpace(pending.previousOriginalTrackableId)
                            ? pending.previousOriginalTrackableId
                            : anchor.trackableId.ToString();
                        string fallbackTrackableId = !string.IsNullOrWhiteSpace(pending.previousOriginalTrackableId)
                            ? anchor.trackableId.ToString()
                            : null;
                        UpdateSessionSnapshotTrackableIds(instanceId, originalTrackableId, fallbackTrackableId);
                    }

                    if (m_SessionSnapshotByInstanceId.TryGetValue(instanceId, out var snapshot) && snapshot != null)
                    {
                        TryEvaluateModulePoseConfidence(
                            snapshot,
                            pending.target,
                            out RestoreConfidence moduleConfidence,
                            out _,
                            out _,
                            out string failureReason);
                        MarkSessionExhibitAvailable(
                            snapshot,
                            pending.target,
                            GetAnchoredSessionRestoreState(snapshot, anchor.trackableId),
                            moduleConfidence,
                            "layout_recovery_anchor_created",
                            anchor,
                            failureReason);
                    }
                }

                if (pending.persistOnSuccess)
                    QueueSaveToFiles(pending.saveLayout, suppressStatusMessage: true);
                if (!pending.suppressStatusMessage)
                {
                    string exhibitName = string.IsNullOrWhiteSpace(pending.displayName)
                        ? "当前展品"
                        : pending.displayName;
                    onStatusMessage?.Invoke($"展品空间锚点已固定：{exhibitName}");
                }
            }

            RefreshCurrentExhibitManipulationState();
            RefreshPlacementProgressInDirector();
            return true;
        }

        private bool CancelPendingAnchorAttachmentForTarget(Transform target)
        {
            bool cancelled = CancelInFlightAnchorAttachmentForTarget(target);
            cancelled |= CancelRetryingAnchorAttachmentForTarget(target);

            if (TryGetPendingAnchorTrackableId(target, out var trackableId))
            {
                LogAnchorLifecycle($"cancel pending by target: {target.name}, trackableId={trackableId}");
                RemovePendingAnchorAttachment(trackableId, removeAnchorTrackable: true);
                cancelled = true;
            }

            return cancelled;
        }

        private void RemovePendingAnchorAttachment(TrackableId trackableId, bool removeAnchorTrackable)
        {
            if (!m_PendingAnchorAttachments.TryGetValue(trackableId, out var pending))
                return;

            LogAnchorLifecycle($"remove pending: {(pending.target != null ? pending.target.name : pending.displayName)}, trackableId={trackableId}, removeAnchorTrackable={removeAnchorTrackable}");
            m_PendingAnchorAttachments.Remove(trackableId);
            if (pending.target != null)
                m_PendingAnchorTrackableIdsByTarget.Remove(pending.target.GetInstanceID());

            if (removeAnchorTrackable && m_AnchorManager != null)
            {
                var anchor = m_AnchorManager.GetAnchor(trackableId);
                if (anchor != null)
                    QueueAnchorNodeForDestruction(anchor.gameObject);
            }

            RefreshCurrentExhibitManipulationState();
        }

        private void ClearPendingAnchorAttachments(bool removeAnchorTrackables)
        {
            if (m_PendingAnchorAttachments.Count == 0)
            {
                m_InFlightAnchorAttachmentRequestVersionByTarget.Clear();
                ClearPendingAnchorAttachmentRetries();
                return;
            }

            var trackableIds = new List<TrackableId>(m_PendingAnchorAttachments.Keys);
            for (int i = 0; i < trackableIds.Count; i++)
                RemovePendingAnchorAttachment(trackableIds[i], removeAnchorTrackables);

            m_InFlightAnchorAttachmentRequestVersionByTarget.Clear();
            ClearPendingAnchorAttachmentRetries();
        }

        private void ClearPendingAnchorAttachmentRetries()
        {
            if (m_RetryAnchorAttachmentCoroutinesByTarget.Count == 0)
                return;

            foreach (var pair in m_RetryAnchorAttachmentCoroutinesByTarget)
            {
                if (pair.Value != null)
                    StopCoroutine(pair.Value);
            }

            m_RetryAnchorAttachmentCoroutinesByTarget.Clear();
        }

        private const float TransientExperienceAnchorRetryIntervalSeconds = 1f;
        private const string TransientExperienceRootRuntimeKey = "__experience_root__";

        private sealed class TransientExperiencePlacementState
        {
            public string instanceId;
            public string moduleId;
            public string displayName;
            public bool isStageModule;
            public bool requiresIndependentAnchor;
            public int chapterIndex = -1;
            public Transform target;
            public bool instantiated;
            public bool anchorPending;
            public bool anchorBound;
            public TrackableId anchorTrackableId = TrackableId.invalidId;
            public float anchorReadinessWaitStartedAt = -1f;
            public bool anchorPlaybackGraceExpired;
            public bool anchorWaitLogged;
            public bool anchorReadyLogged;
            public float lastAnchorFailureAt = -1f;
            public Coroutine retryCoroutine;
            public Coroutine spawnCoroutine;
        }

        private readonly Dictionary<string, TransientExperiencePlacementState> m_TransientExperienceStatesByInstanceId =
            new Dictionary<string, TransientExperiencePlacementState>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> m_TransientExperienceInstanceIdByModuleId =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<TrackableId> m_TransientExperienceAnchorTrackableIds =
            new HashSet<TrackableId>();

        private Pose m_TransientExperienceBootstrapRootPose;
        private bool m_HasTransientExperienceBootstrapRootPose;
        private bool m_TransientExperienceHasLayoutAtBootstrap;
        private float m_TransientExperienceNextSpawnEarliestTime;
        private int m_TransientExperienceNextSpawnEarliestFrame;
        private bool m_TransientExperienceRootAnchorPending;
        private bool m_TransientExperienceRootAnchorBound;
        private TrackableId m_TransientExperienceRootAnchorTrackableId = TrackableId.invalidId;
        private Coroutine m_TransientExperienceRootRetryCoroutine;

        public bool HasTransientExperienceBootstrapRootPose => m_HasTransientExperienceBootstrapRootPose;
        public bool HasTransientExperienceLayoutAtBootstrap =>
            m_HasTransientExperienceBootstrapRootPose && m_TransientExperienceHasLayoutAtBootstrap;

        public void PrepareTransientExperienceModeEntry()
        {
            ClearTransientExperienceRuntime("prepare_transient_experience_entry");
            PruneStaleAnchorsForExperience("prepare_transient_experience_entry");
            m_HasTransientExperienceBootstrapRootPose = false;
            m_TransientExperienceHasLayoutAtBootstrap = false;
            m_TransientExperienceNextSpawnEarliestTime = Time.unscaledTime;
            m_TransientExperienceNextSpawnEarliestFrame = Time.frameCount;
            m_TransientExperienceRootAnchorPending = false;
            m_TransientExperienceRootAnchorBound = false;
            m_TransientExperienceRootAnchorTrackableId = TrackableId.invalidId;
            m_RuntimeRootTrans = null;
            ResetRuntimeRestoreTrackingState();
            NotifyUIUpdate();
        }

        private void PruneStaleAnchorsForExperience(string reason)
        {
            if (m_AnchorManager == null)
            {
                LogAnchorLifecycle(
                    $"experience_stale_anchor_sweep_skipped: reason={reason}, issue=missing_anchor_manager");
                return;
            }

            var anchors = new List<ARAnchor>();
            foreach (var anchor in m_AnchorManager.trackables)
            {
                if (anchor != null)
                    anchors.Add(anchor);
            }

            PruneStaleAnchorsForExperience(anchors, reason);
        }

        private void PruneStaleAnchorsForExperience(IEnumerable<ARAnchor> anchors, string reason)
        {
            if (anchors == null)
                return;

            int scannedCount = 0;
            int removedCount = 0;
            int keptPendingCount = 0;
            int keptTransientRuntimeCount = 0;
            int keptSessionReferencedCount = 0;
            int keptSceneBoundCount = 0;
            int keptPendingProviderCount = 0;

            foreach (var anchor in anchors)
            {
                if (anchor == null)
                    continue;

                scannedCount++;
                if (!ShouldRemoveStaleAnchorForExperience(anchor, out string keepReason))
                {
                    switch (keepReason)
                    {
                        case "pending_attachment":
                            keptPendingCount++;
                            break;
                        case "transient_runtime":
                            keptTransientRuntimeCount++;
                            break;
                        case "session_reference":
                            keptSessionReferencedCount++;
                            break;
                        case "scene_bound":
                            keptSceneBoundCount++;
                            break;
                        case "provider_pending":
                            keptPendingProviderCount++;
                            break;
                    }

                    continue;
                }

                removedCount++;
                LogAnchorLifecycle(
                    $"experience_stale_anchor_prune: reason={reason}, decision=remove, {DescribeAnchorState(anchor)}");
                QueueAnchorNodeForDestruction(anchor.gameObject, $"experience_stale_anchor:{reason}");
            }

            LogAnchorLifecycle(
                $"experience_stale_anchor_sweep: reason={reason}, scanned={scannedCount}, removed={removedCount}, " +
                $"keptPending={keptPendingCount}, keptTransientRuntime={keptTransientRuntimeCount}, " +
                $"keptSessionReferenced={keptSessionReferencedCount}, keptSceneBound={keptSceneBoundCount}, " +
                $"keptProviderPending={keptPendingProviderCount}");
        }

        private bool ShouldRemoveStaleAnchorForExperience(ARAnchor anchor, out string keepReason)
        {
            keepReason = string.Empty;
            if (anchor == null)
            {
                keepReason = "null";
                return false;
            }

            if (m_PendingAnchorAttachments.ContainsKey(anchor.trackableId))
            {
                keepReason = "pending_attachment";
                return false;
            }

            if (m_TransientExperienceAnchorTrackableIds.Contains(anchor.trackableId) ||
                anchor.trackableId == m_TransientExperienceRootAnchorTrackableId)
            {
                keepReason = "transient_runtime";
                return false;
            }

            if (m_SessionLookup.ContainsKey(anchor.trackableId) ||
                RootSessionContainsTrackableId(anchor.trackableId) ||
                anchor.trackableId == m_RootSessionTrackableId)
            {
                keepReason = "session_reference";
                return false;
            }

            if (anchor.transform.childCount > 0)
            {
                keepReason = "scene_bound";
                return false;
            }

            if (anchor.pending)
            {
                keepReason = "provider_pending";
                return false;
            }

            return true;
        }

        public bool TryBeginTransientExperienceBootstrap(
            Pose bootstrapRootPose,
            out bool hasLayout,
            out string failureReason)
        {
            hasLayout = false;
            failureReason = string.Empty;

            if (m_CurrentAppMode != ExhibitAppMode.Experience)
            {
                failureReason = "当前不在体验模式，无法启动 RootCard + layout 恢复。";
                return false;
            }

            if (m_RootMarkerPrefab == null && ResolveRootMarkerPrefabFromPlan() == null)
                LogAnchorLifecycle("experience bootstrap warning: root marker prefab is not configured, fallback primitive will be used");

            m_TransientExperienceBootstrapRootPose = bootstrapRootPose;
            m_HasTransientExperienceBootstrapRootPose = true;
            m_TransientExperienceHasLayoutAtBootstrap = HasUsableLayoutSnapshot();
            hasLayout = m_TransientExperienceHasLayoutAtBootstrap;
            m_TransientExperienceNextSpawnEarliestTime = Time.unscaledTime;
            m_TransientExperienceNextSpawnEarliestFrame = Time.frameCount;

            Transform existingRoot = GetRootTransform();
            if (existingRoot != null)
            {
                CancelPendingAnchorAttachmentForTarget(existingRoot);
                DestroyRootForTransientExperience(existingRoot, destroyAnchor: true);
            }

            Transform rootTransform = CreateRootMarker(bootstrapRootPose, suppressInteractionModuleAutoStart: true);
            if (rootTransform == null)
            {
                failureReason = "无法创建体验模式 Root。";
                m_HasTransientExperienceBootstrapRootPose = false;
                m_TransientExperienceHasLayoutAtBootstrap = false;
                return false;
            }

            ParentLooseRootToModuleContainer(rootTransform);
            rootTransform.SetPositionAndRotation(bootstrapRootPose.position, bootstrapRootPose.rotation);
            m_RuntimeRootTrans = rootTransform;
            m_HasResolvedRootForPlayback = true;
            m_HasRestoredRootExhibit = false;
            SetRootMarkerVisible(false);
            SetRootRestoreRuntimeState(
                RootRestoreState.LayoutRecoveredLoose,
                RestoreConfidence.Verified,
                string.Empty,
                "experience_bootstrap_root_created",
                rootTransform,
                null);

            LogAnchorLifecycle(
                $"image_sampling_accepted: bootstrapRootPose={FormatPose(bootstrapRootPose)}, hasLayout={hasLayout}, {DescribeTransformPose(rootTransform)}");

            m_TransientExperienceRootAnchorPending = false;
            m_TransientExperienceRootAnchorBound = false;
            m_TransientExperienceRootAnchorTrackableId = TrackableId.invalidId;
            TryBeginTransientRuntimeAnchorAttachment(
                rootTransform,
                bootstrapRootPose,
                isRoot: true,
                displayName: "定位点",
                runtimeInstanceId: TransientExperienceRootRuntimeKey);

            if (!hasLayout)
                return true;

            EnsureTransientExperienceStagePlacements();
            return true;
        }

        public void ClearTransientExperienceRuntime(string reason)
        {
            CancelTransientExperienceRuntimeCoroutines();

            var registeredExhibits = CaptureRegisteredExhibitSnapshot();
            var processedTransformIds = new HashSet<int>();
            for (int i = 0; i < registeredExhibits.Count; i++)
            {
                ExhibitInfo info = registeredExhibits[i];
                Transform exhibitRoot = info != null ? info.transform : null;
                if (exhibitRoot == null)
                    continue;

                int transformId = exhibitRoot.GetInstanceID();
                if (!processedTransformIds.Add(transformId))
                    continue;

                bool destroyAnchor = IsTransientExperienceTarget(exhibitRoot);
                CancelPendingAnchorAttachmentForTarget(exhibitRoot);
                DestroyScenePlacementForTransientExperience(exhibitRoot, destroyAnchor);
                RemoveRegisteredExhibitHierarchy(exhibitRoot);
            }

            Transform rootTransform = GetRootTransform();
            if (rootTransform != null)
            {
                bool destroyRootAnchor = IsTransientExperienceRoot(rootTransform);
                CancelPendingAnchorAttachmentForTarget(rootTransform);
                DestroyRootForTransientExperience(rootTransform, destroyRootAnchor);
            }

            m_TransientExperienceStatesByInstanceId.Clear();
            m_TransientExperienceInstanceIdByModuleId.Clear();
            m_TransientExperienceAnchorTrackableIds.Clear();
            m_TransientExperienceRootAnchorPending = false;
            m_TransientExperienceRootAnchorBound = false;
            m_TransientExperienceRootAnchorTrackableId = TrackableId.invalidId;
            m_RuntimeRootTrans = null;
            ClearManualSelection(refreshUi: true);
            RefreshCurrentExhibitManipulationState();
            LogAnchorLifecycle($"experience runtime cleared: reason={reason}");
        }

        internal bool TryEnsureTransientExperiencePlacementAvailableByModuleId(string moduleId)
        {
            moduleId = NormalizeModuleId(moduleId);
            if (string.IsNullOrEmpty(moduleId) ||
                !m_HasTransientExperienceBootstrapRootPose ||
                !m_TransientExperienceHasLayoutAtBootstrap)
            {
                return false;
            }

            if (m_TransientExperienceInstanceIdByModuleId.TryGetValue(moduleId, out string existingInstanceId) &&
                m_TransientExperienceStatesByInstanceId.TryGetValue(existingInstanceId, out var existingState) &&
                existingState != null &&
                existingState.target != null)
            {
                return IsTransientExperienceStateReadyForPlayback(existingState);
            }

            if (!TryGetTransientExperienceLayoutItemByModuleId(moduleId, out LayoutData layoutData) || layoutData == null)
                return false;

            TransientExperiencePlacementState state = GetOrCreateTransientExperiencePlacementState(layoutData);
            if (state == null)
                return false;
            if (state.target != null)
                return IsTransientExperienceStateReadyForPlayback(state);

            if (state.spawnCoroutine != null)
                return false;

            float delaySeconds = 0f;
            int delayFrames = 0;
            if (!state.isStageModule)
            {
                float spawnIntervalSeconds = Mathf.Max(0f, m_TransientExperienceSpawnIntervalSeconds);
                if (spawnIntervalSeconds <= 0f)
                {
                    delayFrames = Mathf.Max(0, m_TransientExperienceNextSpawnEarliestFrame - Time.frameCount);
                    m_TransientExperienceNextSpawnEarliestFrame =
                        Mathf.Max(m_TransientExperienceNextSpawnEarliestFrame, Time.frameCount) + 1;
                }
                else
                {
                    delaySeconds = Mathf.Max(0f, m_TransientExperienceNextSpawnEarliestTime - Time.unscaledTime);
                    m_TransientExperienceNextSpawnEarliestTime =
                        Mathf.Max(m_TransientExperienceNextSpawnEarliestTime, Time.unscaledTime) + spawnIntervalSeconds;
                }
            }

            state.spawnCoroutine = StartCoroutine(
                SpawnTransientExperiencePlacementAfterDelay(state, layoutData, delaySeconds, delayFrames));
            return false;
        }

        private void EnsureTransientExperienceStagePlacements()
        {
            var stageModuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < m_CachedLayout.Count; i++)
            {
                LayoutData layoutData = m_CachedLayout[i];
                if (layoutData == null)
                    continue;

                string moduleId = NormalizeModuleId(layoutData.moduleId);
                if (string.IsNullOrEmpty(moduleId) || !IsStageModuleId(moduleId))
                    continue;

                stageModuleIds.Add(moduleId);
            }

            foreach (string stageModuleId in stageModuleIds)
                TryEnsureTransientExperiencePlacementAvailableByModuleId(stageModuleId);
        }

        private IEnumerator SpawnTransientExperiencePlacementAfterDelay(
            TransientExperiencePlacementState state,
            LayoutData layoutData,
            float delaySeconds,
            int delayFrames)
        {
            if (state == null || layoutData == null)
                yield break;

            if (delaySeconds > 0f)
                yield return new WaitForSecondsRealtime(delaySeconds);
            else
            {
                while (delayFrames-- > 0)
                    yield return null;
            }

            state.spawnCoroutine = null;

            if (m_CurrentAppMode != ExhibitAppMode.Experience ||
                !m_HasTransientExperienceBootstrapRootPose ||
                state.target != null)
            {
                yield break;
            }

            if (!TryGetDeployableModuleDefinition(state.moduleId, out var definition) || definition.prefab == null)
            {
                LogAnchorLifecycle(
                    $"runtime_object_instantiated skipped missing prefab: moduleId={state.moduleId}, instanceId={state.instanceId}");
                yield break;
            }

            Pose worldPose = GetTransientExperienceWorldPose(layoutData);
            GameObject exhibitObject = Instantiate(definition.prefab);
            exhibitObject.name = $"Exhibit_{state.moduleId}_{state.displayName}_{state.instanceId}";
            AttachExhibitInfo(
                exhibitObject,
                state.moduleId,
                state.displayName,
                state.instanceId,
                layoutData.placementContentHidden,
                markPersistenceDirty: false);
            ParentTransientExperiencePlacementToRoot(exhibitObject.transform, layoutData, worldPose);
            exhibitObject.transform.localScale = layoutData.scale;

            bool shouldBeActive = state.isStageModule;
            if (!shouldBeActive)
                exhibitObject.SetActive(false);

            state.target = exhibitObject.transform;
            state.instantiated = true;
            state.anchorReadinessWaitStartedAt = Time.unscaledTime;
            state.anchorPlaybackGraceExpired = false;
            state.anchorPending = false;
            state.anchorBound = false;
            state.anchorTrackableId = TrackableId.invalidId;
            state.anchorWaitLogged = false;
            state.anchorReadyLogged = false;
            m_TransientExperienceInstanceIdByModuleId[state.moduleId] = state.instanceId;

            LogAnchorLifecycle(
                $"runtime_object_instantiated: moduleId={state.moduleId}, instanceId={state.instanceId}, displayName={state.displayName}, " +
                $"chapterIndex={state.chapterIndex}, isStageModule={state.isStageModule}, initialActive={shouldBeActive}, " +
                $"anchorMode={(state.requiresIndependentAnchor ? "independent" : "root_relative")}, " +
                $"worldPose={FormatPose(worldPose)}, {DescribeTransformPose(exhibitObject.transform)}");

            if (state.requiresIndependentAnchor)
            {
                TryBeginTransientRuntimeAnchorAttachment(
                    exhibitObject.transform,
                    worldPose,
                    isRoot: false,
                    displayName: state.displayName,
                    runtimeInstanceId: state.instanceId);
            }
            else
            {
                LogAnchorLifecycle(
                    $"runtime_anchor_skipped: runtimeInstanceId={state.instanceId}, displayName={state.displayName}, " +
                    $"moduleId={state.moduleId}, anchorMode=root_relative, rootTrackableId={m_TransientExperienceRootAnchorTrackableId}");
            }

            RefreshCurrentExhibitManipulationState();
        }

        private bool TryBeginTransientRuntimeAnchorAttachment(
            Transform target,
            Pose pose,
            bool isRoot,
            string displayName,
            string runtimeInstanceId)
        {
            if (target == null)
                return false;

            runtimeInstanceId = NormalizeTransientRuntimeInstanceId(runtimeInstanceId);
            if (string.IsNullOrEmpty(runtimeInstanceId))
                return false;

            ARAnchor existingAnchor = GetDirectParentAnchor(target);
            if (existingAnchor != null)
            {
                SetTransientRuntimeAnchorPending(runtimeInstanceId, false);
                m_TransientExperienceAnchorTrackableIds.Add(existingAnchor.trackableId);
                if (isRoot)
                {
                    m_TransientExperienceRootAnchorBound = true;
                    m_TransientExperienceRootAnchorTrackableId = existingAnchor.trackableId;
                }
                else if (m_TransientExperienceStatesByInstanceId.TryGetValue(runtimeInstanceId, out var existingState) && existingState != null)
                {
                    existingState.anchorBound = true;
                    existingState.anchorTrackableId = existingAnchor.trackableId;
                }
                return true;
            }

            if (IsAnchorAttachmentPending(target))
            {
                SetTransientRuntimeAnchorPending(runtimeInstanceId, true);
                return true;
            }

            if (!HasUsableAnchorSubsystem(out string issue))
            {
                LogAnchorLifecycle(
                    $"runtime_anchor_request_failed: runtimeInstanceId={runtimeInstanceId}, displayName={displayName}, " +
                    $"isRoot={isRoot}, reason={issue}, requestedPose={FormatPose(pose)}");
                HandleTransientRuntimeAnchorRequestFailure(runtimeInstanceId, isRoot, target, pose, displayName, issue);
                return false;
            }

            LogAnchorLifecycle(
                $"runtime_anchor_request_started: runtimeInstanceId={runtimeInstanceId}, displayName={displayName}, " +
                $"isRoot={isRoot}, requestedPose={FormatPose(pose)}, {DescribeTransformPose(target)}");
            if (isRoot)
            {
                LogAnchorLifecycle(
                    $"root_anchor_waiting: runtimeInstanceId={runtimeInstanceId}, displayName={displayName}, " +
                    $"requestedPose={FormatPose(pose)}");
            }
            SetTransientRuntimeAnchorPending(runtimeInstanceId, true);
            return TryBeginAnchorAttachment(
                target,
                pose,
                isRoot ? "RuntimeAnchor_Root" : $"RuntimeAnchor_{displayName}",
                isRoot,
                saveLayout: false,
                suppressStatusMessage: true,
                displayName,
                promoteSessionCandidatesOnSuccess: false,
                previousOriginalTrackableId: null,
                persistOnSuccess: false,
                transientRuntime: true,
                runtimeInstanceId: runtimeInstanceId);
        }

        private void HandleTransientRuntimeAnchorRequestFailure(
            string runtimeInstanceId,
            bool isRoot,
            Transform target,
            Pose pose,
            string displayName,
            string reason)
        {
            runtimeInstanceId = NormalizeTransientRuntimeInstanceId(runtimeInstanceId);
            SetTransientRuntimeAnchorPending(runtimeInstanceId, false);
            RecordTransientRuntimeAnchorFailure(runtimeInstanceId);
            ScheduleTransientRuntimeAnchorRetry(runtimeInstanceId, isRoot, target, pose, displayName, reason);
        }

        private void ScheduleTransientRuntimeAnchorRetry(
            string runtimeInstanceId,
            bool isRoot,
            Transform target,
            Pose pose,
            string displayName,
            string reason)
        {
            if (target == null || m_CurrentAppMode != ExhibitAppMode.Experience)
                return;

            runtimeInstanceId = NormalizeTransientRuntimeInstanceId(runtimeInstanceId);
            if (string.IsNullOrEmpty(runtimeInstanceId))
                return;

            if (isRoot)
            {
                if (m_TransientExperienceRootRetryCoroutine != null)
                    return;

                m_TransientExperienceRootRetryCoroutine = StartCoroutine(
                    RetryTransientRuntimeAnchorRequestAfterDelay(
                        runtimeInstanceId,
                        isRoot,
                        target,
                        pose,
                        displayName,
                        reason));
                return;
            }

            if (!m_TransientExperienceStatesByInstanceId.TryGetValue(runtimeInstanceId, out var state) || state == null)
                return;

            if (state.retryCoroutine != null)
                return;

            state.retryCoroutine = StartCoroutine(
                RetryTransientRuntimeAnchorRequestAfterDelay(
                    runtimeInstanceId,
                    isRoot,
                    target,
                    pose,
                    displayName,
                    reason));
        }

        private IEnumerator RetryTransientRuntimeAnchorRequestAfterDelay(
            string runtimeInstanceId,
            bool isRoot,
            Transform target,
            Pose pose,
            string displayName,
            string reason)
        {
            LogAnchorLifecycle(
                $"runtime_anchor_request_retried: runtimeInstanceId={runtimeInstanceId}, displayName={displayName}, " +
                $"isRoot={isRoot}, reason={reason}, retryInSeconds={TransientExperienceAnchorRetryIntervalSeconds:0.##}");
            yield return new WaitForSecondsRealtime(TransientExperienceAnchorRetryIntervalSeconds);

            if (isRoot)
                m_TransientExperienceRootRetryCoroutine = null;
            else if (m_TransientExperienceStatesByInstanceId.TryGetValue(runtimeInstanceId, out var state) && state != null)
                state.retryCoroutine = null;

            if (m_CurrentAppMode != ExhibitAppMode.Experience ||
                target == null ||
                GetDirectParentAnchor(target) != null)
            {
                yield break;
            }

            TryBeginTransientRuntimeAnchorAttachment(target, pose, isRoot, displayName, runtimeInstanceId);
        }

        private void HandleCompletedTransientRuntimeAnchorAttachment(PendingAnchorAttachment pending, ARAnchor anchor)
        {
            if (pending == null || anchor == null)
                return;

            string runtimeInstanceId = NormalizeTransientRuntimeInstanceId(pending.runtimeInstanceId);
            SetTransientRuntimeAnchorPending(runtimeInstanceId, false);
            m_TransientExperienceAnchorTrackableIds.Add(anchor.trackableId);

            if (pending.isRoot)
            {
                m_TransientExperienceRootAnchorBound = true;
                m_TransientExperienceRootAnchorTrackableId = anchor.trackableId;
                LogAnchorLifecycle(
                    $"root_anchor_ready: runtimeInstanceId={runtimeInstanceId}, displayName={pending.displayName}, " +
                    $"{DescribeAnchorState(anchor)}, {DescribeTransformPose(pending.target)}");
                SetRootRestoreRuntimeState(
                    RootRestoreState.LayoutRecoveredLoose,
                    RestoreConfidence.Verified,
                    string.Empty,
                    "root_anchor_ready",
                    pending.target,
                    anchor);
                LogAnchorLifecycle(
                    $"runtime_anchor_bound: runtimeInstanceId={runtimeInstanceId}, displayName={pending.displayName}, isRoot=true, " +
                    $"{DescribeAnchorState(anchor)}, {DescribeTransformPose(pending.target)}");
                return;
            }

            if (!m_TransientExperienceStatesByInstanceId.TryGetValue(runtimeInstanceId, out var transientState) || transientState == null)
                return;

            transientState.anchorBound = true;
            transientState.anchorTrackableId = anchor.trackableId;
            LogAnchorLifecycle(
                $"runtime_anchor_bound: runtimeInstanceId={runtimeInstanceId}, displayName={pending.displayName}, isRoot=false, " +
                $"moduleId={transientState.moduleId}, instanceId={transientState.instanceId}, {DescribeAnchorState(anchor)}, {DescribeTransformPose(pending.target)}");
        }

        private static string NormalizeTransientRuntimeInstanceId(string runtimeInstanceId)
        {
            return string.IsNullOrWhiteSpace(runtimeInstanceId) ? string.Empty : runtimeInstanceId.Trim();
        }

        private void SetTransientRuntimeAnchorPending(string runtimeInstanceId, bool isPending)
        {
            runtimeInstanceId = NormalizeTransientRuntimeInstanceId(runtimeInstanceId);
            if (string.IsNullOrEmpty(runtimeInstanceId))
                return;

            if (string.Equals(runtimeInstanceId, TransientExperienceRootRuntimeKey, StringComparison.Ordinal))
            {
                m_TransientExperienceRootAnchorPending = isPending;
                if (!isPending && !m_TransientExperienceRootAnchorBound)
                    m_TransientExperienceRootAnchorTrackableId = TrackableId.invalidId;
                return;
            }

            if (!m_TransientExperienceStatesByInstanceId.TryGetValue(runtimeInstanceId, out var transientState) || transientState == null)
                return;

            transientState.anchorPending = isPending;
            if (!isPending && !transientState.anchorBound)
                transientState.anchorTrackableId = TrackableId.invalidId;
        }

        private void RecordTransientRuntimeAnchorFailure(string runtimeInstanceId)
        {
            runtimeInstanceId = NormalizeTransientRuntimeInstanceId(runtimeInstanceId);
            if (string.IsNullOrEmpty(runtimeInstanceId))
                return;

            if (string.Equals(runtimeInstanceId, TransientExperienceRootRuntimeKey, StringComparison.Ordinal))
                return;

            if (!m_TransientExperienceStatesByInstanceId.TryGetValue(runtimeInstanceId, out var transientState) || transientState == null)
                return;

            transientState.lastAnchorFailureAt = Time.unscaledTime;
        }

        private void CancelTransientExperienceRuntimeCoroutines()
        {
            if (m_TransientExperienceRootRetryCoroutine != null)
            {
                StopCoroutine(m_TransientExperienceRootRetryCoroutine);
                m_TransientExperienceRootRetryCoroutine = null;
            }

            foreach (var pair in m_TransientExperienceStatesByInstanceId)
            {
                var transientState = pair.Value;
                if (transientState == null)
                    continue;

                if (transientState.retryCoroutine != null)
                {
                    StopCoroutine(transientState.retryCoroutine);
                    transientState.retryCoroutine = null;
                }

                if (transientState.spawnCoroutine != null)
                {
                    StopCoroutine(transientState.spawnCoroutine);
                    transientState.spawnCoroutine = null;
                }
            }
        }

        private void DestroyScenePlacementForTransientExperience(Transform target, bool destroyAnchor)
        {
            if (target == null)
                return;

            ARAnchor anchor = GetDirectParentAnchor(target);
            if (anchor != null)
            {
                if (destroyAnchor)
                {
                    QueueAnchorNodeForDestruction(anchor.gameObject);
                }
                else
                {
                    target.SetParent(null, true);
                    Destroy(target.gameObject);
                }
            }
            else
            {
                Destroy(target.gameObject);
            }
        }

        private void DestroyRootForTransientExperience(Transform rootTransform, bool destroyAnchor)
        {
            if (rootTransform == null)
                return;

            ARAnchor rootAnchor = GetDirectParentAnchor(rootTransform);
            if (rootAnchor != null)
            {
                if (destroyAnchor)
                {
                    QueueAnchorNodeForDestruction(rootAnchor.gameObject);
                }
                else
                {
                    rootTransform.SetParent(null, true);
                    Destroy(rootTransform.gameObject);
                }
            }
            else
            {
                Destroy(rootTransform.gameObject);
            }
        }

        private bool IsTransientExperienceTarget(Transform target)
        {
            if (target == null)
                return false;

            if (target == m_RuntimeRootTrans && IsTransientExperienceRoot(target))
                return true;

            if (!TryParseExhibitInfo(target, out _, out _, out string instanceId))
                return false;

            instanceId = NormalizeInstanceId(instanceId);
            return !string.IsNullOrEmpty(instanceId) &&
                   m_TransientExperienceStatesByInstanceId.ContainsKey(instanceId);
        }

        private bool IsTransientExperienceRoot(Transform rootTransform)
        {
            if (rootTransform == null)
                return false;

            ARAnchor rootAnchor = GetDirectParentAnchor(rootTransform);
            return rootAnchor != null &&
                   m_TransientExperienceAnchorTrackableIds.Contains(rootAnchor.trackableId);
        }

        private bool TryGetTransientExperienceLayoutItemByModuleId(string moduleId, out LayoutData layoutData)
        {
            layoutData = null;
            moduleId = NormalizeModuleId(moduleId);
            if (string.IsNullOrEmpty(moduleId))
                return false;

            for (int i = 0; i < m_CachedLayout.Count; i++)
            {
                LayoutData candidate = m_CachedLayout[i];
                if (candidate == null)
                    continue;

                if (string.Equals(NormalizeModuleId(candidate.moduleId), moduleId, StringComparison.OrdinalIgnoreCase))
                {
                    layoutData = candidate;
                    return true;
                }
            }

            return false;
        }

        private TransientExperiencePlacementState GetOrCreateTransientExperiencePlacementState(LayoutData layoutData)
        {
            string instanceId = NormalizeInstanceId(layoutData != null ? layoutData.instanceID : string.Empty);
            if (string.IsNullOrEmpty(instanceId))
                return null;

            if (m_TransientExperienceStatesByInstanceId.TryGetValue(instanceId, out var existingState) &&
                existingState != null)
            {
                return existingState;
            }

            string moduleId = NormalizeModuleId(layoutData.moduleId);
            var transientState = new TransientExperiencePlacementState
            {
                instanceId = instanceId,
                moduleId = moduleId,
                displayName = string.IsNullOrWhiteSpace(layoutData.displayName) ? moduleId : layoutData.displayName,
                isStageModule = IsStageModuleId(moduleId),
                requiresIndependentAnchor = ShouldUseIndependentTransientExperienceAnchor(moduleId),
                chapterIndex = ResolveTransientExperienceChapterIndex(moduleId)
            };

            m_TransientExperienceStatesByInstanceId[instanceId] = transientState;
            if (!string.IsNullOrEmpty(moduleId))
                m_TransientExperienceInstanceIdByModuleId[moduleId] = instanceId;
            return transientState;
        }

        private int ResolveTransientExperienceChapterIndex(string moduleId)
        {
            ChapterPlacementPlan plan = ResolvePlacementPlan();
            if (plan == null || plan.chapters == null)
                return -1;

            moduleId = NormalizeModuleId(moduleId);
            for (int chapterIndex = 0; chapterIndex < plan.chapters.Count; chapterIndex++)
            {
                ChapterPlacementChapter chapter = plan.chapters[chapterIndex];
                if (chapter == null || chapter.steps == null)
                    continue;

                for (int stepIndex = 0; stepIndex < chapter.steps.Count; stepIndex++)
                {
                    ChapterPlacementStep step = chapter.steps[stepIndex];
                    if (step == null)
                        continue;

                    if (string.Equals(NormalizeModuleId(step.moduleId), moduleId, StringComparison.OrdinalIgnoreCase))
                        return chapterIndex;
                }
            }

            return -1;
        }

        private Pose GetTransientExperienceWorldPose(LayoutData layoutData)
        {
            return new Pose(
                m_TransientExperienceBootstrapRootPose.position +
                (m_TransientExperienceBootstrapRootPose.rotation * layoutData.relativePosition),
                m_TransientExperienceBootstrapRootPose.rotation * layoutData.relativeRotation);
        }

        private bool ShouldUseIndependentTransientExperienceAnchor(string moduleId)
        {
            moduleId = NormalizeModuleId(moduleId);
            return !string.IsNullOrEmpty(moduleId) &&
                   !string.Equals(moduleId, "RootStep", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsTransientExperienceStateReadyForPlayback(TransientExperiencePlacementState state)
        {
            if (state == null || state.target == null)
                return false;

            if (!state.requiresIndependentAnchor)
                return true;

            if (TryMarkTransientExperienceStateAnchorIfPresent(state))
                return true;

            MarkTransientExperienceStateAnchorMissing(state);
            EnsureTransientExperienceAnchorRequest(state);

            if (state.anchorReadinessWaitStartedAt < 0f)
                state.anchorReadinessWaitStartedAt = Time.unscaledTime;

            float graceSeconds = Mathf.Max(0f, m_TransientExperienceAnchorPlaybackGraceSeconds);
            if (graceSeconds <= 0f)
            {
                if (!state.anchorPlaybackGraceExpired)
                {
                    state.anchorPlaybackGraceExpired = true;
                    LogAnchorLifecycle(
                        $"runtime_anchor_playback_wait_bypassed: runtimeInstanceId={state.instanceId}, " +
                        $"moduleId={state.moduleId}, displayName={state.displayName}, graceSeconds={graceSeconds:0.##}");
                }
                return true;
            }

            if (!state.anchorWaitLogged)
            {
                state.anchorWaitLogged = true;
                LogAnchorLifecycle(
                    $"runtime_anchor_playback_wait_started: runtimeInstanceId={state.instanceId}, " +
                    $"moduleId={state.moduleId}, displayName={state.displayName}, " +
                    $"graceSeconds={graceSeconds:0.##}, pending={state.anchorPending}, retryScheduled={(state.retryCoroutine != null)}");
            }

            float elapsedSeconds = Time.unscaledTime - state.anchorReadinessWaitStartedAt;
            if (elapsedSeconds < graceSeconds)
                return false;

            if (!state.anchorPlaybackGraceExpired)
            {
                state.anchorPlaybackGraceExpired = true;
                LogAnchorLifecycle(
                    $"runtime_anchor_playback_grace_expired: runtimeInstanceId={state.instanceId}, " +
                    $"displayName={state.displayName}, waitedSeconds={elapsedSeconds:0.##}, " +
                    $"graceSeconds={graceSeconds:0.##}, pending={state.anchorPending}, retryScheduled={(state.retryCoroutine != null)}");
            }
            return true;
        }

        private bool TryMarkTransientExperienceStateAnchorIfPresent(TransientExperiencePlacementState state)
        {
            if (state == null || state.target == null)
                return false;

            ARAnchor anchor = GetDirectParentAnchor(state.target);
            if (anchor == null)
                return false;

            state.anchorPending = false;
            state.anchorBound = true;
            state.anchorTrackableId = anchor.trackableId;
            m_TransientExperienceAnchorTrackableIds.Add(anchor.trackableId);
            if (!state.anchorReadyLogged)
            {
                state.anchorReadyLogged = true;
                LogAnchorLifecycle(
                    $"runtime_anchor_playback_ready: runtimeInstanceId={state.instanceId}, " +
                    $"moduleId={state.moduleId}, displayName={state.displayName}, {DescribeAnchorState(anchor)}, " +
                    $"{DescribeTransformPose(state.target)}");
            }
            return true;
        }

        private void MarkTransientExperienceStateAnchorMissing(TransientExperiencePlacementState state)
        {
            if (state == null)
                return;

            if (state.anchorTrackableId != TrackableId.invalidId)
            {
                LogAnchorLifecycle(
                    $"runtime_anchor_lost: runtimeInstanceId={state.instanceId}, moduleId={state.moduleId}, " +
                    $"displayName={state.displayName}, previousTrackableId={state.anchorTrackableId}, " +
                    $"{DescribeTransformPose(state.target)}");
                m_TransientExperienceAnchorTrackableIds.Remove(state.anchorTrackableId);
            }

            state.anchorBound = false;
            state.anchorTrackableId = TrackableId.invalidId;
            state.anchorReadyLogged = false;
            if (state.target == null || !IsAnchorAttachmentPending(state.target))
                state.anchorPending = false;
        }

        private bool EnsureTransientExperienceAnchorRequest(TransientExperiencePlacementState state)
        {
            if (state == null || state.target == null || !state.requiresIndependentAnchor)
                return false;

            if (IsAnchorAttachmentPending(state.target))
            {
                SetTransientRuntimeAnchorPending(state.instanceId, true);
                return true;
            }

            if (state.retryCoroutine != null)
                return true;

            Pose pose = new Pose(state.target.position, state.target.rotation);
            LogAnchorLifecycle(
                $"runtime_anchor_request_ensure: runtimeInstanceId={state.instanceId}, " +
                $"moduleId={state.moduleId}, displayName={state.displayName}, pose={FormatPose(pose)}, " +
                $"{DescribeTransformPose(state.target)}");
            return TryBeginTransientRuntimeAnchorAttachment(
                state.target,
                pose,
                isRoot: false,
                displayName: state.displayName,
                runtimeInstanceId: state.instanceId);
        }

        private void RefreshTransientExperienceRuntimeAnchorsAfterResume()
        {
            if (m_CurrentAppMode != ExhibitAppMode.Experience || !m_HasTransientExperienceBootstrapRootPose)
                return;

            int checkedCount = 0;
            int boundCount = 0;
            int pendingCount = 0;
            int requestedCount = 0;
            int missingTargetCount = 0;

            Transform rootTransform = GetRootTransform();
            if (rootTransform == null)
            {
                missingTargetCount++;
            }
            else
            {
                checkedCount++;
                ARAnchor rootAnchor = GetDirectParentAnchor(rootTransform);
                if (rootAnchor != null)
                {
                    boundCount++;
                    m_TransientExperienceRootAnchorBound = true;
                    m_TransientExperienceRootAnchorTrackableId = rootAnchor.trackableId;
                    m_TransientExperienceAnchorTrackableIds.Add(rootAnchor.trackableId);
                }
                else
                {
                    m_TransientExperienceRootAnchorBound = false;
                    if (!IsAnchorAttachmentPending(rootTransform))
                        m_TransientExperienceRootAnchorPending = false;

                    bool alreadyPending = IsAnchorAttachmentPending(rootTransform) ||
                                          m_TransientExperienceRootRetryCoroutine != null;
                    Pose rootPose = new Pose(rootTransform.position, rootTransform.rotation);
                    if (TryBeginTransientRuntimeAnchorAttachment(
                            rootTransform,
                            rootPose,
                            isRoot: true,
                            displayName: "定位点",
                            runtimeInstanceId: TransientExperienceRootRuntimeKey) &&
                        !alreadyPending)
                    {
                        requestedCount++;
                        LogAnchorLifecycle(
                            $"runtime_anchor_resume_rebind_requested: runtimeInstanceId={TransientExperienceRootRuntimeKey}, " +
                            $"displayName=定位点, isRoot=true, {DescribeTransformPose(rootTransform)}");
                    }

                    if (m_TransientExperienceRootAnchorPending || alreadyPending)
                        pendingCount++;
                }
            }

            foreach (var pair in m_TransientExperienceStatesByInstanceId)
            {
                TransientExperiencePlacementState state = pair.Value;
                if (state == null || !state.requiresIndependentAnchor)
                    continue;

                checkedCount++;
                if (state.target == null)
                {
                    missingTargetCount++;
                    continue;
                }

                if (TryMarkTransientExperienceStateAnchorIfPresent(state))
                {
                    boundCount++;
                    continue;
                }

                MarkTransientExperienceStateAnchorMissing(state);
                bool alreadyPending = IsAnchorAttachmentPending(state.target) || state.retryCoroutine != null;
                if (EnsureTransientExperienceAnchorRequest(state) && !alreadyPending)
                {
                    requestedCount++;
                    LogAnchorLifecycle(
                        $"runtime_anchor_resume_rebind_requested: runtimeInstanceId={state.instanceId}, " +
                        $"moduleId={state.moduleId}, displayName={state.displayName}, {DescribeTransformPose(state.target)}");
                }

                if (state.anchorPending || alreadyPending)
                    pendingCount++;
            }

            LogAnchorLifecycle(
                $"runtime_anchor_resume_check: checked={checkedCount}, bound={boundCount}, " +
                $"pendingOrRetrying={pendingCount}, requested={requestedCount}, missingTarget={missingTargetCount}");
        }

        private string BuildTransientExperienceAnchorSummary()
        {
            int stateCount = 0;
            int exhibitAnchorRequiredCount = 0;
            int exhibitBoundCount = 0;
            int exhibitPendingCount = 0;
            int exhibitRetryCount = 0;
            int exhibitMissingTargetCount = 0;

            foreach (var pair in m_TransientExperienceStatesByInstanceId)
            {
                TransientExperiencePlacementState state = pair.Value;
                if (state == null)
                    continue;

                stateCount++;
                if (!state.requiresIndependentAnchor)
                    continue;

                exhibitAnchorRequiredCount++;
                if (state.target == null)
                {
                    exhibitMissingTargetCount++;
                    continue;
                }

                if (GetDirectParentAnchor(state.target) != null || state.anchorBound)
                    exhibitBoundCount++;
                if (IsAnchorAttachmentPending(state.target) || state.anchorPending)
                    exhibitPendingCount++;
                if (state.retryCoroutine != null)
                    exhibitRetryCount++;
            }

            Transform rootTransform = GetRootTransform();
            bool rootRequired = m_HasTransientExperienceBootstrapRootPose;
            bool rootMissing = rootRequired && rootTransform == null;
            bool rootBound = rootRequired &&
                             rootTransform != null &&
                             (GetDirectParentAnchor(rootTransform) != null || m_TransientExperienceRootAnchorBound);
            bool rootPending = rootRequired &&
                               rootTransform != null &&
                               (IsAnchorAttachmentPending(rootTransform) || m_TransientExperienceRootAnchorPending);
            bool rootRetrying = rootRequired && m_TransientExperienceRootRetryCoroutine != null;
            int rootAnchorRequiredCount = rootRequired ? 1 : 0;
            int rootBoundCount = rootBound ? 1 : 0;
            int rootPendingCount = rootPending ? 1 : 0;
            int rootRetryCount = rootRetrying ? 1 : 0;
            int rootMissingTargetCount = rootMissing ? 1 : 0;

            return
                $"hasBootstrap={m_HasTransientExperienceBootstrapRootPose}, hasLayout={m_TransientExperienceHasLayoutAtBootstrap}, " +
                $"states={stateCount}, anchorRequired={rootAnchorRequiredCount + exhibitAnchorRequiredCount}, " +
                $"bound={rootBoundCount + exhibitBoundCount}, pending={rootPendingCount + exhibitPendingCount}, " +
                $"retrying={rootRetryCount + exhibitRetryCount}, missingTarget={rootMissingTargetCount + exhibitMissingTargetCount}, " +
                $"rootRequired={rootRequired}, rootBound={rootBound}, rootPending={rootPending}, rootRetrying={rootRetrying}, " +
                $"exhibitAnchorRequired={exhibitAnchorRequiredCount}, exhibitBound={exhibitBoundCount}";
        }

        private void ParentTransientExperiencePlacementToRoot(Transform target, LayoutData layoutData, Pose worldPose)
        {
            if (target == null)
                return;

            Transform rootTransform = GetRootTransform();
            if (rootTransform != null)
            {
                target.SetParent(rootTransform, false);
                target.localPosition = layoutData.relativePosition;
                target.localRotation = layoutData.relativeRotation;
                return;
            }

            target.SetPositionAndRotation(worldPose.position, worldPose.rotation);
            ParentLooseExhibitToModuleContainer(target);
        }

        private static ARAnchor GetDirectParentAnchor(Transform target)
        {
            return target != null && target.parent != null
                ? target.parent.GetComponent<ARAnchor>()
                : null;
        }

        private void RefreshPlacementProgressInDirector()
        {
            var director = FindFirstObjectByType<ChapterPlacementDirector>(FindObjectsInactive.Include);
            if (director == null || !director.IsPlacementMode)
                return;

            director.RefreshPlacementProgressFromScenePreserveCurrent();
        }
    }
}
