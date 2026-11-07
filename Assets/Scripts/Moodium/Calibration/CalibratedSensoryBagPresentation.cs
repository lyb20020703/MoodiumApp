using UnityEngine;
using Moodium.Flow;
using Moodium.Interaction;

namespace Moodium.Calibration
{
    [DisallowMultipleComponent]
    public sealed class CalibratedSensoryBagPresentation : MonoBehaviour
    {
        Transform m_Anchor;
        SensoryBagCalibrationData m_Data;
        public Transform VisualRoot { get; private set; }
        public Transform InteractionRoot { get; private set; }
        public SensoryBagInteractionVolume InteractionVolume { get; private set; }
        public SensoryBagCalibrationData CurrentCalibration => m_Data;
        public Vector3 CenterWorldPosition => transform.position;
        public Quaternion AnchorOrientation => m_Anchor != null ? m_Anchor.rotation : transform.rotation;

        public void Configure(Transform anchor, SensoryBagCalibrationData data, GameObject visualPrefab)
        {
            m_Anchor = anchor;
            m_Data = data;
            EnsureRoots();
            ClearVisual();
            if (visualPrefab != null)
            {
                var visual = Instantiate(visualPrefab, VisualRoot);
                visual.name = visualPrefab.name;
                FitVisualToCalibration(visual);
                MoodiumRuntimeObjectSetup.Configure(visual, allowMove: false, allowScale: false, disablePhysics: true);
                if (visual.GetComponent<SoftTouchDeformationController>() == null)
                    visual.AddComponent<SoftTouchDeformationController>();
                var capsule = visual.GetComponent<ChocolateCapsuleInteraction>() ?? visual.AddComponent<ChocolateCapsuleInteraction>();
                capsule.SetInteractionEnabled(true);
                // Keep the established gaze/pinch path active. Hand-collision remains
                // available as an additional route, rather than replacing it.
                capsule.SetSpatialPointerEnabled(true);
                if (visual.GetComponent<TrackedCapsuleHandCollision>() == null)
                    visual.AddComponent<TrackedCapsuleHandCollision>();
            }
            InteractionVolume.Configure(data.Width, data.Height, data.Depth);
            // The interaction region spans above the table, not symmetrically through it.
            InteractionVolume.Collider.center=Quaternion.Inverse(data.LocalOrientation)*Vector3.up*(data.Depth*.5f);
            SyncNow();
            SetTrackingVisible(true);
        }

        public void SetTrackingVisible(bool visible)
        {
            if (VisualRoot != null) VisualRoot.gameObject.SetActive(visible);
            if (InteractionRoot != null) InteractionRoot.gameObject.SetActive(visible);
        }

        public void SyncNow()
        {
            if (m_Anchor == null) return;
            transform.SetPositionAndRotation(
                m_Anchor.TransformPoint(new Vector3(m_Data.CenterOffset.x, m_Data.PlaneLocalOffset.y, m_Data.CenterOffset.y)),
                // The confirmed model must occupy the exact same X/Z bag plane as
                // the calibration frame. Keeping it anchor-only made Width/Height
                // scale against the prefab's X/Y plane instead, rotating Candy's
                // long edge and breaking both worlds' calibrated proportions.
                m_Anchor.rotation * m_Data.LocalOrientation);
            // Bags are authored in the image anchor's X/Z plane. Cancel only the
            // calibration UI's plane correction, while retaining the full tracked
            // image rotation (including pitch and roll) for physical alignment.
            if (VisualRoot != null)
                VisualRoot.localRotation = Quaternion.Inverse(m_Data.LocalOrientation);
        }

        void EnsureRoots()
        {
            if (VisualRoot == null)
            {
                VisualRoot = new GameObject("VisualRoot").transform;
                VisualRoot.SetParent(transform, false);
            }
            if (InteractionRoot == null)
            {
                InteractionRoot = new GameObject("InteractionRoot").transform;
                InteractionRoot.SetParent(transform, false);
                InteractionVolume = InteractionRoot.gameObject.AddComponent<SensoryBagInteractionVolume>();
            }
        }

        void ClearVisual()
        {
            for (var i = VisualRoot.childCount - 1; i >= 0; i--)
                Destroy(VisualRoot.GetChild(i).gameObject);
            VisualRoot.localScale = Vector3.one;
        }

        void FitVisualToCalibration(GameObject visual)
        {
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            // Preserve the scale authored on the prefab root. CandySoft carries
            // a deliberate 0.5 root scale while NatureSoft is authored at 1.0;
            // forcing both to one silently doubled Candy before calibration.
            var authoredRootScale = visual.transform.localScale;
            visual.transform.localScale = authoredRootScale;
            var bounds = CalculateLocalRendererBounds(visual.transform);
            // CandySoft/NatureSoft are authored as tabletop bags: X/Z is their
            // visible bag face, and Y is their vertical depth/detail axis. The
            // calibration frame is also an X/Z table plane. Mapping Height onto
            // Y made a wide bag become a tall, narrow slab on-device. Keeping
            // Y fixed made a small bag look unnaturally thick, so it follows the
            // overall X/Z scale rather than either dimension independently.
            VisualRoot.localScale = CalculateTablePlaneFitScale(
                m_Data.Width, m_Data.Height, bounds, authoredRootScale);
            // Align the actual geometry, not its authoring pivot, with the base plane.
            var bottom=Mathf.Min(bounds.min.y*authoredRootScale.y,bounds.max.y*authoredRootScale.y);
            visual.transform.localPosition=new Vector3(-bounds.center.x*authoredRootScale.x,-bottom,-bounds.center.z*authoredRootScale.z);
        }

        /// <summary>
        /// Converts the two-dimensional calibration frame to the authored bag
        /// plane. Kept public for the regression test because both Candy and
        /// Nature use this common presentation component.
        /// </summary>
        public static Vector3 CalculateTablePlaneFitScale(float calibratedWidth, float calibratedHeight, Bounds prefabBounds)
        {
            return CalculateTablePlaneFitScale(calibratedWidth, calibratedHeight, prefabBounds, Vector3.one);
        }

        /// <summary>
        /// As above, while preserving any scale authored on the prefab's root.
        /// Renderer bounds expressed in root-local coordinates deliberately omit
        /// that scale, so it needs to be put back before calculating the fit.
        /// </summary>
        public static Vector3 CalculateTablePlaneFitScale(
            float calibratedWidth,
            float calibratedHeight,
            Bounds prefabBounds,
            Vector3 authoredRootScale)
        {
            var authoredWidth = Mathf.Max(.0001f, prefabBounds.size.x * Mathf.Abs(authoredRootScale.x));
            var authoredHeight = Mathf.Max(.0001f, prefabBounds.size.z * Mathf.Abs(authoredRootScale.z));
            var widthScale = calibratedWidth / authoredWidth;
            var heightScale = calibratedHeight / authoredHeight;
            // Geometric mean preserves a proportional overall scale: 2x × 2x
            // yields 2x thickness, .5x × .5x yields .5x. A one-axis resize only
            // affects depth moderately instead of creating a flat sheet.
            var thicknessScale = Mathf.Sqrt(Mathf.Max(.0001f, widthScale * heightScale));
            return new Vector3(widthScale, thicknessScale, heightScale);
        }

        static Bounds CalculateLocalRendererBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var hasBounds = false;
            var result = new Bounds(Vector3.zero, Vector3.zero);
            foreach (var renderer in renderers)
            {
                if(!Moodium.Flow.SoftModelBounds.IsSoftBodyRenderer(renderer))continue;
                // A world AABB cannot be inverted into an accurate tilted local bound.
                var bounds=renderer.localBounds;
                var matrix=root.worldToLocalMatrix*renderer.localToWorldMatrix;
                for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
                {
                    var point=matrix.MultiplyPoint3x4(bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z)));
                    if(hasBounds)result.Encapsulate(point);else{result=new Bounds(point,Vector3.zero);hasBounds=true;}
                }
            }
            return hasBounds ? result : new Bounds(Vector3.zero, Vector3.one);
        }

        void LateUpdate() => SyncNow();
    }
}
