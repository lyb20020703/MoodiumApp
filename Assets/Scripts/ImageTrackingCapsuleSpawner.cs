using System;
using System.Collections.Generic;
using Moodium.Interaction;
using Moodium.Reality;
using Moodium.Calibration;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Moodium
{
    [DisallowMultipleComponent]
    public sealed class ImageTrackingCapsuleSpawner : MonoBehaviour
    {
        [SerializeField] ARTrackedImageManager m_TrackedImageManager;
        [SerializeField] GameObject m_ChocolatePrefab;
        [SerializeField] Vector3 m_LocalPosition;
        [SerializeField] Vector3 m_LocalEulerAngles;
        [SerializeField] SensoryBagCalibrationCoordinator m_CalibrationCoordinator;

        readonly Dictionary<TrackableId, GameObject> m_Instances = new();
        readonly HashSet<TrackableId> m_Available = new();

        public event Action<ChocolateCapsuleInteraction> CapsuleAvailable;
        public event Action<ChocolateCapsuleInteraction> CapsuleUnavailable;

        public void ConfigureCalibrationCoordinator(SensoryBagCalibrationCoordinator coordinator)
        {
            m_CalibrationCoordinator = coordinator;
        }

        public void Configure(ARTrackedImageManager manager, GameObject chocolatePrefab)
        {
            if (isActiveAndEnabled && m_TrackedImageManager != null)
                m_TrackedImageManager.trackablesChanged.RemoveListener(OnTrackedImagesChanged);
            m_TrackedImageManager = manager;
            m_ChocolatePrefab = chocolatePrefab;
            if (isActiveAndEnabled && m_TrackedImageManager != null)
                m_TrackedImageManager.trackablesChanged.AddListener(OnTrackedImagesChanged);
        }

        public void ClearRuntimeInstances()
        {
            foreach (var pair in m_Instances)
            {
                ReportUnavailable(pair.Key, pair.Value);
                if (pair.Value != null)
                    Destroy(pair.Value);
            }
            m_Instances.Clear();
            m_Available.Clear();
        }

        void OnEnable()
        {
            if (m_TrackedImageManager != null)
                m_TrackedImageManager.trackablesChanged.AddListener(OnTrackedImagesChanged);
        }

        void OnDisable()
        {
            if (m_TrackedImageManager != null)
                m_TrackedImageManager.trackablesChanged.RemoveListener(OnTrackedImagesChanged);
            foreach (var pair in m_Instances)
                ReportUnavailable(pair.Key, pair.Value);
        }

        void OnTrackedImagesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> changes)
        {
            foreach (var trackedImage in changes.added)
                ReportTrackingFromImage(trackedImage);
            foreach (var trackedImage in changes.updated)
                ReportTrackingFromImage(trackedImage);
            foreach (var removed in changes.removed)
                Remove(removed.Value.trackableId);
        }

        GameObject ReportTracking(TrackableId id, Transform anchor, TrackingState state)
        {
            return ReportTrackingInternal(id, anchor, state, null);
        }

        void ReportTrackingFromImage(ARTrackedImage trackedImage)
        {
            ReportTrackingInternal(
                trackedImage.trackableId,
                trackedImage.transform,
                trackedImage.trackingState,
                trackedImage.referenceImage.name);
        }

        GameObject ReportTrackingInternal(TrackableId id, Transform anchor, TrackingState state, string referenceImageName)
        {
            if (m_CalibrationCoordinator != null &&
                m_CalibrationCoordinator.HandleImageAnchorUpdate(id, referenceImageName, anchor, state))
            {
                if (m_Instances.Remove(id, out var legacyInstance))
                {
                    ReportUnavailable(id, legacyInstance);
                    if (legacyInstance != null)
                        Destroy(legacyInstance);
                }
                return null;
            }

            if (state == TrackingState.None)
            {
                if (m_Instances.TryGetValue(id, out var hidden))
                {
                    ReportUnavailable(id, hidden);
                    if (hidden != null)
                        hidden.SetActive(false);
                }
                return hidden;
            }

            if (!m_Instances.TryGetValue(id, out var instance) || instance == null)
            {
                instance = TrackedCapsuleRuntimeFactory.Create(
                    m_ChocolatePrefab,
                    anchor,
                    m_LocalPosition,
                    Quaternion.Euler(m_LocalEulerAngles),
                    "Chocolate_Capsule (Tracked Image)");
                if (instance == null)
                    return null;
                m_Instances[id] = instance;
            }
            else
            {
                var follower = instance.GetComponent<TrackedObjectPoseFollower>();
                follower?.Configure(anchor, m_LocalPosition, Quaternion.Euler(m_LocalEulerAngles));
            }

            instance.SetActive(true);
            if (m_Available.Add(id))
                CapsuleAvailable?.Invoke(instance.GetComponent<ChocolateCapsuleInteraction>());
            return instance;
        }

        void Remove(TrackableId id)
        {
            m_CalibrationCoordinator?.HandleImageRemoved(id);
            if (!m_Instances.Remove(id, out var instance))
                return;
            ReportUnavailable(id, instance);
            if (instance != null)
                Destroy(instance);
        }

        void ReportUnavailable(TrackableId id, GameObject instance)
        {
            if (!m_Available.Remove(id) || instance == null)
                return;
            CapsuleUnavailable?.Invoke(instance.GetComponent<ChocolateCapsuleInteraction>());
        }
    }
}
