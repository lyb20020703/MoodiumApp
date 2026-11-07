using TMPro;
using Moodium.Opening;
using UnityEngine;

namespace Moodium.Reality
{
    /// <summary>One-shot world-space instruction. It deliberately has no panel so a transparent guide video can replace it later.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldEnvironmentPrompt : MonoBehaviour
    {
        const float WorldScale = .008f;

        TextMeshPro m_Text;
        Camera m_Camera;
        TMP_FontAsset m_ChineseFont;
        TMP_FontAsset m_EnglishFont;
        bool m_UsesOpeningPose;

        public void Configure(Camera camera, TMP_FontAsset chineseFont, TMP_FontAsset englishFont)
        {
            m_Camera = camera;
            m_ChineseFont = chineseFont;
            m_EnglishFont = englishFont;
        }

        public void Show(bool natureWorld, bool english, Vector3 worldAnchor)
        {
            if (m_Text == null)
            {
                m_Text = gameObject.AddComponent<TextMeshPro>();
                m_Text.alignment = TextAlignmentOptions.Center;
                m_Text.fontSize = 7f;
                m_Text.rectTransform.sizeDelta = new Vector2(24f, 5f);
                m_Text.lineSpacing = 4f;
                m_Text.color = Color.white;
                m_Text.outlineWidth = .08f;
                m_Text.outlineColor = new Color(.06f, .02f, .12f, .9f);
            }
            m_Text.font = english ? m_EnglishFont : m_ChineseFont;
            m_Text.text = WorldEnvironmentPromptPolicy.Message(natureWorld, english);

            // Reproduce the opening subtitle's world-space pose, but keep this
            // prompt independent from the opening video's object/lifetime.
            if (m_Camera == null)
                m_Camera = Camera.main;
            var openingPoseApplied = false;
            if (m_Camera != null && WorldEnvironmentPromptLayout.TryGetOpeningSubtitlePose(
                    m_Camera, out var subtitlePosition, out var subtitleRotation))
            {
                transform.SetPositionAndRotation(subtitlePosition, subtitleRotation);
                transform.localScale = Vector3.one * .045f;
                openingPoseApplied = true;
            }
            else
            {
                transform.localScale = Vector3.one * WorldScale;
                transform.position = m_Camera != null
                    ? WorldEnvironmentPromptLayout.WorldAnchorPosition(
                        worldAnchor, m_Camera.transform.forward, Vector3.up, m_Camera.transform.position)
                    : worldAnchor;
            }
            gameObject.SetActive(true);
            m_UsesOpeningPose = openingPoseApplied;
            if (!openingPoseApplied)
                FaceCamera();
        }

        void LateUpdate()
        {
            // The opening-equivalent pose is already authored in world space. The
            // fallback anchor pose needs the usual billboard update.
            if (!m_UsesOpeningPose)
                FaceCamera();
        }

        void FaceCamera()
        {
            if (m_Camera == null)
                m_Camera = Camera.main;
            if (m_Camera == null)
                return;
            // TMP's front face is its local +Z, so it must point back toward the viewer.
            var direction = m_Camera.transform.position - transform.position;
            if (direction.sqrMagnitude > .0001f)
                transform.rotation = Quaternion.LookRotation(direction.normalized, m_Camera.transform.up);
        }

    }

    /// <summary>Shared, testable placement for the small world-play instruction.</summary>
    public static class WorldEnvironmentPromptLayout
    {
        public const float SubtitleDistance = .9f;
        public const float SubtitleVerticalOffset = .03f;
        public const float WorldAnchorVerticalOffset = .22f;
        public const float WorldAnchorForwardOffset = .12f;

        public static bool TryGetOpeningSubtitlePose(
            Camera camera,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            if (camera == null)
                return false;

            var config = Resources.Load<OpeningVideoSequenceConfig>(
                "MoodiumOpening/OpeningVideoSequenceConfig");
            if (config == null)
                return false;

            var viewForward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
            if (viewForward.sqrMagnitude < .01f)
                viewForward = camera.transform.forward;
            viewForward.Normalize();

            var displayPosition = camera.transform.position + viewForward * config.DistanceFromUser +
                                  Vector3.up * config.HeightOffset;
            displayPosition.y = config.WorldPositionY;
            var displayRotation = Quaternion.LookRotation(camera.transform.forward, camera.transform.up);
            var displayScale = new Vector3(1.28f, .72f, 1f);
            var subtitleLocalPosition = new Vector3(0f, -.39f, -.03f);
            position = displayPosition + displayRotation * Vector3.Scale(subtitleLocalPosition, displayScale);
            rotation = displayRotation;
            return true;
        }

        public static Vector3 CameraRelativePosition(Vector3 cameraPosition, Vector3 cameraForward, Vector3 up)
        {
            var planarForward = Vector3.ProjectOnPlane(cameraForward, up);
            if (planarForward.sqrMagnitude < .0001f)
                planarForward = cameraForward;
            return cameraPosition + planarForward.normalized * SubtitleDistance + up.normalized * SubtitleVerticalOffset;
        }

        public static Vector3 WorldAnchorPosition(
            Vector3 worldAnchor,
            Vector3 cameraForward,
            Vector3 up,
            Vector3 cameraPosition)
        {
            var normalizedUp = up.sqrMagnitude > .0001f ? up.normalized : Vector3.up;
            var toCamera = cameraPosition - worldAnchor;
            var planarToCamera = Vector3.ProjectOnPlane(toCamera, normalizedUp);
            if (planarToCamera.sqrMagnitude < .0001f)
                planarToCamera = -Vector3.ProjectOnPlane(cameraForward, normalizedUp);
            if (planarToCamera.sqrMagnitude < .0001f)
                planarToCamera = Vector3.back;
            return worldAnchor + normalizedUp * WorldAnchorVerticalOffset +
                   planarToCamera.normalized * WorldAnchorForwardOffset;
        }
    }
}
