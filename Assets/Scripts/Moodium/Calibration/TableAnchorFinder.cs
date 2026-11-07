using System;
using System.Collections.Generic;
using Moodium.Flow;
using Moodium.MoodCheck;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Moodium.Calibration
{
    [DisallowMultipleComponent]
    public sealed class TableAnchorFinder : MonoBehaviour
    {
        [SerializeField] float m_WaitSeconds = 3;
        [SerializeField] float m_HalfFov = 40;
        [SerializeField] float m_MaxHorizontalDistance = 1.5f;
        [SerializeField] float m_MinDrop = .2f;
        [SerializeField] float m_MaxDrop = 1;
        [SerializeField] float m_ForwardDistance = .5f;
        [SerializeField] float m_FallbackTableDrop = .45f;
        [SerializeField] float m_AnchorLift = .05f;

        ARPlaneManager manager;
        XROrigin origin;
        Camera viewer;
        Func<bool> hasUserInteracted;
        GameObject prompt;
        float began;
        bool running, fallback, relocated, warned;
        readonly List<TablePlaneSelector.Candidate> candidates = new();

        public event Action<Transform> AnchorReady;
        public Transform Anchor { get; private set; }
        public bool IsDetecting => running;
        public bool UsedFallback => fallback;
        public bool HasRelocated => relocated;
        public TablePlaneSelector.Settings Settings => new()
        {
            HalfFov = m_HalfFov, MaxDistance = m_MaxHorizontalDistance,
            MinDrop = m_MinDrop, MaxDrop = m_MaxDrop, ForwardDistance = m_ForwardDistance,
            FallbackDrop = m_FallbackTableDrop, AnchorLift = m_AnchorLift
        };

        public void Begin(Camera camera, TMP_FontAsset font, bool english, Func<bool> userInteracted)
        {
            Clear();
            viewer = camera != null ? camera : Camera.main;
            hasUserInteracted = userInteracted;
            began = Time.unscaledTime; running = true; fallback = relocated = warned = false;
            origin = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
            if (origin != null)
            {
                manager = origin.GetComponent<ARPlaneManager>();
                if (manager == null) { manager = origin.gameObject.AddComponent<ARPlaneManager>(); manager.enabled = false; }
                manager.planePrefab = null;
                manager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
                manager.enabled = true;
            }
            var pose = ViewerPose(); var forward = TablePlaneSelector.Forward(pose);
            var label = SensoryBagBranchUI.Text(transform, "Table Search Hint", MoodCheckCopy.Get("calib.searching", english), Vector3.zero, font);
            prompt = label.gameObject;
            prompt.transform.SetPositionAndRotation(pose.position + forward * .6f - Vector3.up * .12f, Quaternion.LookRotation(forward, Vector3.up));
            Debug.Log("[Sensory Bag Branch] mode=none step=table-search-started");
            // A newly enabled manager gets one frame to create/start its existing subsystem.
            if (!Application.isPlaying || origin == null) UseFallback("plane-subsystem-unavailable");
        }

        Pose ViewerPose() => viewer != null ? new Pose(viewer.transform.position, viewer.transform.rotation) : new Pose(new Vector3(0, 1.4f, 0), Quaternion.identity);

        void Update()
        {
            if (!running) return;
            if (Anchor != null && hasUserInteracted?.Invoke() == true) { StopDetection(); return; }
            try
            {
                if (manager == null || manager.subsystem == null || !manager.subsystem.running)
                {
                    if (Time.unscaledTime - began > .1f) UseFallback("plane-subsystem-unavailable");
                    return;
                }
                candidates.Clear();
                foreach (var plane in manager.trackables)
                {
                    if (plane.trackingState != TrackingState.Tracking || plane.subsumedBy != null) continue;
                    var boundary = plane.boundary.IsCreated && plane.boundary.Length >= 3 ? plane.boundary.ToArray() : null;
                    if (boundary == null)
                    {
                        var c = plane.centerInPlaneSpace; var e = plane.extents;
                        boundary = new[] { c + new Vector2(-e.x, -e.y), c + new Vector2(e.x, -e.y), c + e, c + new Vector2(-e.x, e.y) };
                    }
                    candidates.Add(new TablePlaneSelector.Candidate
                    {
                        Pose = new Pose(plane.transform.position, plane.transform.rotation), Alignment = plane.alignment,
                        Classifications = plane.classifications, Boundary = boundary, HalfSize = plane.extents
                    });
                }
                EvaluateCandidates(candidates, Time.unscaledTime - began);
            }
            catch (Exception ex) { UseFallback("plane-detection-unavailable:" + ex.GetType().Name); }
        }

        public void EvaluateCandidates(IReadOnlyList<TablePlaneSelector.Candidate> planes, float elapsed)
        {
            if (!running) return;
            if (TablePlaneSelector.TrySelect(planes, ViewerPose(), Settings, out var table))
            {
                if (Anchor == null) Publish(table, false);
                else if (fallback && !relocated && hasUserInteracted?.Invoke() != true)
                {
                    var pose = TablePlaneSelector.AnchorPose(table, m_AnchorLift);
                    Anchor.SetPositionAndRotation(pose.position, pose.rotation);
                    relocated = true;
                    Debug.Log("[Sensory Bag Branch] mode=none step=fallback-anchor-relocated");
                }
                StopDetection();
            }
            else if (Anchor == null && elapsed >= m_WaitSeconds) UseFallback("timeout", true);
        }

        void UseFallback(string reason, bool keepDetecting = false)
        {
            if (Anchor != null) return;
            if (!warned && reason != "timeout")
                Debug.LogWarning($"[Sensory Bag Branch] mode=none step=table-fallback reason={reason}");
            warned = true;
            Publish(TablePlaneSelector.Fallback(ViewerPose(), Settings), true);
            if (!keepDetecting) StopDetection();
        }

        void Publish(Pose table, bool isFallback)
        {
            var anchor = new GameObject("Sensory Bag Table Anchor").transform;
            anchor.SetParent(origin != null ? origin.transform : transform, false);
            anchor.localScale = StickerWorldSelectionController.GetCompensatedLocalScale(1, anchor.parent.lossyScale);
            var pose = TablePlaneSelector.AnchorPose(table, m_AnchorLift);
            anchor.SetPositionAndRotation(pose.position, pose.rotation);
            Anchor = anchor; fallback = isFallback;
            SensoryBagBranchUI.Dispose(prompt); prompt = null;
            Debug.Log($"[Sensory Bag Branch] mode=none step=table-anchor-ready fallback={isFallback}");
            AnchorReady?.Invoke(anchor);
        }
        public void StopDetection()
        {
            running = false;
            if (manager != null) manager.enabled = false;
            SensoryBagBranchUI.Dispose(prompt); prompt = null;
        }
        public void Clear()
        {
            StopDetection();
            if (Anchor != null) SensoryBagBranchUI.Dispose(Anchor.gameObject);
            Anchor = null;
        }
        void OnDisable() => StopDetection();
        void OnDestroy() => Clear();
    }
}
