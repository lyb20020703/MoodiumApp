using System.Collections.Generic;
using UnityEngine;

namespace Moodium.Flow
{
    public sealed class MoodiumWindowHandle : MonoBehaviour
    {
        Transform m_Window;
        int m_ActivePointer = -1;
        Vector3 m_GrabOffset;
        readonly List<Transform> m_Followers = new();
        readonly List<Pose> m_FollowerOffsets = new();

        /// <summary>
        /// Smallest horizontal distance (metres) between the viewer and the window while dragging; 0 = no limit.
        /// Panels stand in front of the opening video, so the video must not be pulled into the viewer's face.
        /// </summary>
        public float MinDistanceFromViewer { get; set; }

        /// <summary>Objects that keep their pose relative to the window while it is dragged (opened panels).</summary>
        public IReadOnlyList<Transform> Followers => m_Followers;

        public void Configure(Transform windowRoot) => m_Window = windowRoot;

        /// <summary>
        /// <paramref name="follower"/> moves and turns with the window from now on. It is not re-parented: the
        /// opening video root is non-uniformly scaled (1.28 × 0.72), which would distort a child panel.
        /// </summary>
        public void AddFollower(Transform follower)
        {
            if (follower == null || m_Followers.Contains(follower))
                return;
            m_Followers.Add(follower);
            m_FollowerOffsets.Add(m_Window != null ? RelativePose(m_Window, follower) : Pose.identity);
        }

        public void RemoveFollower(Transform follower)
        {
            var index = m_Followers.IndexOf(follower);
            if (index < 0)
                return;
            m_Followers.RemoveAt(index);
            m_FollowerOffsets.RemoveAt(index);
        }

        /// <summary>Pose of <paramref name="follower"/> in the window's position/rotation frame (scale ignored).</summary>
        public static Pose RelativePose(Transform window, Transform follower)
        {
            var inverse = Quaternion.Inverse(window.rotation);
            return new Pose(inverse * (follower.position - window.position), inverse * follower.rotation);
        }

        public void PointerStarted(int pointerId, Vector3 position)
        {
            if (m_Window == null || m_ActivePointer >= 0)
                return;
            m_ActivePointer = pointerId;
            m_GrabOffset = m_Window.position - position;
            CaptureFollowers();
            FaceCurrentUser();
            ApplyFollowers();
            Debug.Log($"[Moodium Window] Drag started: {m_Window.name} ({m_Followers.Count} followers)");
        }

        public void PointerMoved(int pointerId, Vector3 position)
        {
            if (pointerId != m_ActivePointer || m_Window == null)
                return;
            m_Window.position = KeepAwayFromViewer(position + m_GrabOffset);
            FaceCurrentUser();
            ApplyFollowers();
        }

        public void PointerEnded(int pointerId)
        {
            if (pointerId != m_ActivePointer)
                return;
            m_ActivePointer = -1;
            Debug.Log($"[Moodium Window] Drag ended: {(m_Window != null ? m_Window.name : name)}");
        }

        void CaptureFollowers()
        {
            for (var i = m_Followers.Count - 1; i >= 0; i--)
            {
                if (m_Followers[i] == null)
                {
                    m_Followers.RemoveAt(i);
                    m_FollowerOffsets.RemoveAt(i);
                    continue;
                }
                m_FollowerOffsets[i] = RelativePose(m_Window, m_Followers[i]);
            }
        }

        void ApplyFollowers()
        {
            for (var i = 0; i < m_Followers.Count; i++)
            {
                var follower = m_Followers[i];
                if (follower == null)
                    continue;
                var offset = m_FollowerOffsets[i];
                follower.SetPositionAndRotation(m_Window.position + m_Window.rotation * offset.position,
                    m_Window.rotation * offset.rotation);
            }
        }

        static Camera Viewer() => Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();

        Vector3 KeepAwayFromViewer(Vector3 target)
        {
            if (MinDistanceFromViewer <= 0f)
                return target;
            var camera = Viewer();
            if (camera == null)
                return target;
            var eye = camera.transform.position;
            var flat = Vector3.ProjectOnPlane(target - eye, Vector3.up);
            if (flat.magnitude >= MinDistanceFromViewer)
                return target;
            var direction = flat.sqrMagnitude > 1e-6f
                ? flat.normalized
                : Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            if (direction.sqrMagnitude < 1e-6f)
                return target;
            var kept = eye + direction * MinDistanceFromViewer;
            kept.y = target.y;
            return kept;
        }

        void FaceCurrentUser()
        {
            if (m_Window == null)
                return;
            var camera = Viewer();
            if (camera == null)
                return;

            // Moodium panels render toward local -Z, so local +Z points from the user
            // toward the window. Updating this vector while dragging also produces a
            // natural yaw change when the window is moved left or right.
            var awayFromUser = Vector3.ProjectOnPlane(
                m_Window.position - camera.transform.position,
                Vector3.up).normalized;
            if (awayFromUser.sqrMagnitude < 0.001f)
                awayFromUser = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            if (awayFromUser.sqrMagnitude >= 0.001f)
                m_Window.rotation = Quaternion.LookRotation(awayFromUser, Vector3.up);
        }
    }
}
