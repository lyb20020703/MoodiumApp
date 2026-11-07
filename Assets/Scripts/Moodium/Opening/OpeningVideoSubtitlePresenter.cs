using TMPro;
using UnityEngine;
using UnityEngine.Video;

namespace Moodium.Opening
{
    /// <summary>Renders subtitles for the current frame without controlling playback.</summary>
    public sealed class OpeningVideoSubtitlePresenter : MonoBehaviour
    {
        VideoPlayer m_Player;
        TextMeshPro m_Text;
        string m_CurrentText = string.Empty;

        public void Configure(VideoPlayer player, TMP_FontAsset font, Transform videoDisplay)
        {
            m_Player = player;
            transform.SetParent(videoDisplay, false);
            transform.localPosition = new Vector3(0f, -0.39f, -0.03f);
            transform.localRotation = Quaternion.identity;

            var parentScale = videoDisplay.localScale;
            transform.localScale = new Vector3(
                SafeScaleDivide(0.045f, parentScale.x),
                SafeScaleDivide(0.045f, parentScale.y),
                SafeScaleDivide(0.045f, parentScale.z));

            m_Text = gameObject.AddComponent<TextMeshPro>();
            m_Text.font = font;
            m_Text.fontSize = 7f;
            m_Text.alignment = TextAlignmentOptions.Center;
            m_Text.textWrappingMode = TextWrappingModes.Normal;
            m_Text.rectTransform.sizeDelta = new Vector2(24f, 5f);
            m_Text.lineSpacing = 4f;
            m_Text.color = Color.white;
            m_Text.outlineWidth = 0.08f;
            m_Text.outlineColor = new Color(0.08f, 0.02f, 0.16f, 0.85f);
            m_Text.text = string.Empty;
            m_Text.enabled = false;
        }

        void Update()
        {
            if (m_Player == null || m_Text == null)
                return;

            var frame = m_Player.frame >= 0
                ? m_Player.frame
                : (long)System.Math.Round(m_Player.time * OpeningVideoTimeline.FramesPerSecond);
            var nextText = OpeningVideoSubtitleTimeline.GetTextAtFrame(frame);
            if (nextText == m_CurrentText)
                return;

            m_CurrentText = nextText;
            m_Text.text = nextText;
            m_Text.enabled = !string.IsNullOrEmpty(nextText);
            if (m_Text.enabled)
                m_Text.ForceMeshUpdate(ignoreActiveState: true);
        }

        static float SafeScaleDivide(float value, float divisor) =>
            Mathf.Abs(divisor) > 0.0001f ? value / divisor : value;
    }
}
