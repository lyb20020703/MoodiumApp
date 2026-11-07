using System;
using System.Collections.Generic;
using UnityEngine;

namespace VFXViewer
{
    public partial class ExhibitPlacementManager
    {
        public string SpectatorRootObjectName => m_RootObjectName;

        public bool TryGetSpectatorRootPose(out Pose pose)
        {
            Transform root = GetRootTransform();
            if (root == null)
            {
                pose = default;
                return false;
            }

            pose = new Pose(root.position, root.rotation);
            return true;
        }

        public SharedLayoutSnapshot CaptureSharedLayoutSnapshot(int revision)
        {
            var snapshot = new SharedLayoutSnapshot
            {
                layoutRevision = revision,
                rootObjectName = m_RootObjectName,
                items = Array.Empty<SharedLayoutItem>(),
            };

            Transform root = GetRootTransform();
            if (root == null)
                return snapshot;

            List<ExhibitInfo> exhibits = CaptureRegisteredExhibitSnapshot();
            var items = new List<SharedLayoutItem>(exhibits.Count);
            for (int i = 0; i < exhibits.Count; i++)
            {
                ExhibitInfo info = exhibits[i];
                if (info == null || info.transform == null)
                    continue;

                string instanceId = string.IsNullOrWhiteSpace(info.instanceID)
                    ? $"{NormalizeModuleId(info.moduleId)}::{info.transform.GetInstanceID()}"
                    : info.instanceID.Trim();
                string moduleId = NormalizeModuleId(info.moduleId);
                if (string.IsNullOrEmpty(moduleId))
                    continue;

                Vector3 relativePosition = root.InverseTransformPoint(info.transform.position);
                Quaternion relativeRotation = Quaternion.Inverse(root.rotation) * info.transform.rotation;
                Vector3 relativeScale = info.transform.localScale;

                items.Add(new SharedLayoutItem
                {
                    instanceId = instanceId,
                    moduleId = moduleId,
                    displayName = string.IsNullOrWhiteSpace(info.displayName) ? moduleId : info.displayName.Trim(),
                    relativePosition = new SerializableVector3(relativePosition),
                    relativeRotation = new SerializableQuaternion(relativeRotation),
                    scale = new SerializableVector3(relativeScale),
                    placementContentHidden = info.placementContentHidden,
                });
            }

            items.Sort(CompareSharedLayoutItem);
            snapshot.items = items.ToArray();
            return snapshot;
        }

        private static int CompareSharedLayoutItem(SharedLayoutItem left, SharedLayoutItem right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left == null)
                return 1;
            if (right == null)
                return -1;

            return string.Compare(left.instanceId, right.instanceId, StringComparison.Ordinal);
        }
    }
}
