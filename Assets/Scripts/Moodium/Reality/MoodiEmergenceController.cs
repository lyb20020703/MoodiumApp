using System.Collections.Generic;
using Moodium.Calibration;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Moodium.Reality
{
    /// <summary>
    /// Evaluates Moodi's authored emergence clip at the current world-progress time.
    /// It never auto-plays: the user's accumulated bag interaction is the timeline.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MoodiEmergenceController : MonoBehaviour
    {
        static readonly HashSet<MoodiEmergenceController> ActiveControllers = new();

        CalibratedSensoryBagPresentation m_Presentation;
        Transform m_FollowRoot;
        GameObject m_Moodi;
        AnimationClip m_Clip;
        PlayableGraph m_Graph;
        AnimationClipPlayable m_Playable;
        bool m_Configured;
        Renderer[] m_Renderers;
        Vector3 m_PreviewBaseWorldPosition;
        float m_SoftWorldThickness;

        [Header("Progress-driven vertical emergence")]
        [SerializeField, Range(0f, 1f)] float m_LiftStartProgress = .08f;
        [SerializeField, Range(0f, 1f)] float m_LiftEndProgress = .92f;
        [SerializeField, Range(0f, .5f)] float m_SoftThicknessToLift = .45f;

        public float Progress { get; private set; }

        public static void SetAllProgress(float normalizedProgress)
        {
            foreach (var controller in ActiveControllers)
                if (controller != null)
                    controller.SetProgress(normalizedProgress);
        }

        public void Configure(
            CalibratedSensoryBagPresentation presentation,
            GameObject moodiPrefab,
            AnimationClip clip,
            float scale)
        {
            if (presentation == null || moodiPrefab == null || clip == null)
                return;
            m_Presentation = presentation;
            m_Clip = clip;
            m_FollowRoot = new GameObject("Moodi Emergence Root").transform;
            m_FollowRoot.SetParent(presentation.transform.parent, false);
            m_SoftWorldThickness = CalculateVisualWorldThickness(presentation.VisualRoot);
            SyncToBagCenter();
            CreateAndConfigureMoodi(moodiPrefab, scale);
        }

        /// <summary>
        /// Uses the same authored-clip graph as production, without enabling the
        /// XR interaction stack. This is intentionally limited to the editor
        /// orientation preview.
        /// </summary>
        public void ConfigurePreview(
            Transform followRoot,
            Transform bagVisualRoot,
            GameObject moodiPrefab,
            AnimationClip clip,
            float scale)
        {
            if (followRoot == null || bagVisualRoot == null || moodiPrefab == null || clip == null)
                return;

            m_Clip = clip;
            m_FollowRoot = followRoot;
            m_PreviewBaseWorldPosition = followRoot.position;
            m_SoftWorldThickness = CalculateVisualWorldThickness(bagVisualRoot);
            CreateAndConfigureMoodi(moodiPrefab, scale);
        }

        public void SetProgress(float normalizedProgress)
        {
            if (!m_Configured || m_Moodi == null || m_Clip == null)
                return;
            Progress = Mathf.Clamp01(normalizedProgress);
            m_Playable.SetTime(MoodiEmergenceProgress.ToAnimationTime(Progress, m_Clip.length));
            m_Graph.Evaluate(0f);
            SyncToBagCenter();
            // Keep the GameObject active.  Deactivating it at progress zero can stop
            // Playables from applying their first visible pose when it is re-enabled.
            SetRenderersVisible(MoodiEmergenceProgress.ShouldBeVisible(Progress));
        }

        void OnEnable() => ActiveControllers.Add(this);

        void OnDisable() => ActiveControllers.Remove(this);

        void LateUpdate() => SyncToBagCenter();

        void CreateAndConfigureMoodi(GameObject moodiPrefab, float scale)
        {
            if (m_Moodi != null)
                Destroy(m_Moodi);

            m_Moodi = Instantiate(moodiPrefab, m_FollowRoot);
            m_Moodi.name = "Moodi_V2 Emergence";
            m_Moodi.transform.localPosition = Vector3.zero;
            // Moodi's authored model needs this local correction relative to the
            // approved CandySoft/NatureSoft pose. Keep it centralised so preview
            // and the Vision runtime cannot diverge again.
            m_Moodi.transform.localRotation = MoodiEmergenceProgress.AuthoredLocalRotation;
            m_Moodi.transform.localScale = Vector3.one * Mathf.Max(.001f, scale);

            var animator = m_Moodi.GetComponentInChildren<Animator>(true) ?? m_Moodi.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            m_Graph = PlayableGraph.Create("Moodi Emergence Progress Graph");
            var output = AnimationPlayableOutput.Create(m_Graph, "Moodi Emergence", animator);
            m_Playable = AnimationClipPlayable.Create(m_Graph, m_Clip);
            m_Playable.SetSpeed(0d);
            output.SetSourcePlayable(m_Playable);
            m_Graph.Play();
            m_Configured = true;
            m_Renderers = m_Moodi.GetComponentsInChildren<Renderer>(true);
            Debug.Log($"[Moodi Emergence] Configured clip='{m_Clip.name}' length={m_Clip.length:F3}s " +
                      $"scale={m_Moodi.transform.localScale.x:F3}, renderers={m_Renderers.Length}.");
            SetProgress(0f);
        }

        void SyncToBagCenter()
        {
            if (m_FollowRoot == null)
                return;

            var lift = MoodiEmergenceProgress.CalculateVerticalLift(
                Progress,
                m_LiftStartProgress,
                m_LiftEndProgress,
                CalculateSurfaceLimitedWorldLift());

            if (m_Presentation == null)
            {
                m_FollowRoot.position = m_PreviewBaseWorldPosition + Vector3.up * lift;
                return;
            }

            m_FollowRoot.SetPositionAndRotation(
                m_Presentation.CenterWorldPosition + Vector3.up * lift,
                MoodiEmergenceProgress.ResolveVisualOrientation(m_Presentation.VisualRoot.rotation));
            // The bag face may be calibrated non-uniformly on X/Z. Moodi should
            // share the bag's overall size without inheriting that anisotropy,
            // which would squash the mascot's face as the user resizes one edge.
            m_FollowRoot.localScale = Vector3.one * MoodiEmergenceProgress.CalculateUniformBagScale(
                m_Presentation.VisualRoot.lossyScale);
        }

        float CalculateSurfaceLimitedWorldLift()
        {
            return MoodiEmergenceProgress.CalculateSurfaceLimitedLift(
                m_SoftWorldThickness,
                m_SoftThicknessToLift);
        }

        static float CalculateVisualWorldThickness(Transform root)
        {
            if (root == null)
                return 0f;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var hasBounds = false;
            var bounds = new Bounds();
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                    continue;
                if (hasBounds)
                    bounds.Encapsulate(renderer.bounds);
                else
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
            }
            return hasBounds ? bounds.size.y : 0f;
        }

        void SetRenderersVisible(bool visible)
        {
            if (m_Renderers == null)
                return;
            foreach (var renderer in m_Renderers)
                if (renderer != null)
                    renderer.enabled = visible;
        }

        void OnDestroy()
        {
            ActiveControllers.Remove(this);
            if (m_Graph.IsValid())
                m_Graph.Destroy();
            if (m_FollowRoot != null)
                Destroy(m_FollowRoot.gameObject);
        }
    }

    public static class MoodiEmergenceProgress
    {
        public static Quaternion AuthoredLocalRotation => Quaternion.Euler(-90f, 0f, 180f);

        /// <summary>
        /// Maps the calibrated tabletop face scale (X/Z) to a single mascot
        /// scale. The geometric mean matches the bag's existing depth-fitting
        /// policy while preserving Moodi's authored proportions.
        /// </summary>
        public static float CalculateUniformBagScale(Vector3 visualWorldScale) =>
            Mathf.Sqrt(Mathf.Max(.0001f, Mathf.Abs(visualWorldScale.x * visualWorldScale.z)));

        /// <summary>
        /// Moodi begins at the Soft centre, so the surface is half of the Soft's
        /// vertical thickness away. Clamp the ratio to that surface distance: it
        /// must emerge from the bag, never rise above it like a floating object.
        /// </summary>
        public static float CalculateSurfaceLimitedLift(float worldThickness, float thicknessFraction) =>
            Mathf.Max(0f, worldThickness) * Mathf.Clamp(thicknessFraction, 0f, .5f);

        /// <summary>
        /// The authored pose alone does not move Moodi outside the Soft mesh.
        /// This adds a smooth world-up emergence that is independent of a bag's
        /// local axes and therefore remains upright on every tracked table plane.
        /// </summary>
        public static float CalculateVerticalLift(
            float normalizedProgress,
            float liftStartProgress,
            float liftEndProgress,
            float maximumLiftMeters)
        {
            var start = Mathf.Clamp01(liftStartProgress);
            var end = Mathf.Max(start + .0001f, Mathf.Clamp01(liftEndProgress));
            var interpolation = Mathf.InverseLerp(start, end, Mathf.Clamp01(normalizedProgress));
            return Mathf.SmoothStep(0f, 1f, interpolation) * Mathf.Max(0f, maximumLiftMeters);
        }

        public static float ToAnimationTime(float normalizedProgress, float clipLength) =>
            Mathf.Clamp01(normalizedProgress) * Mathf.Max(0f, clipLength);

        public static bool ShouldBeVisible(float normalizedProgress) => normalizedProgress > 0f;

        /// <summary>Emergence shares the authored world orientation of the fitted bag visual.</summary>
        public static Quaternion ResolveVisualOrientation(Quaternion visualOrientation) => visualOrientation;
    }
}
