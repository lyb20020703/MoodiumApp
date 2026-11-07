using UnityEngine;
using UnityEngine.Video;

namespace Moodium.Opening
{
    /// <summary>Inspector-editable timing and media settings for the app's opening video.</summary>
    [CreateAssetMenu(fileName = "OpeningVideoSequenceConfig", menuName = "Moodium/Opening Video Sequence")]
    public sealed class OpeningVideoSequenceConfig : ScriptableObject
    {
        [SerializeField] VideoClip m_VideoClip;
        [SerializeField] VideoClip m_BagIntroClip;
        [Header("Bag calibration tutorial")]
        [SerializeField] VideoClip m_FrameAdjustingChineseClip;
        [SerializeField] VideoClip m_FrameAdjustingEnglishClip;
        [Header("World squeeze progress videos")]
        [SerializeField] VideoClip m_CandySqueezeClip;
        [SerializeField] VideoClip m_NatureSqueezeClip;
        [Header("Moodi emergence")]
        [SerializeField] GameObject m_MoodiEmergencePrefab;
        [SerializeField] AnimationClip m_MoodiEmergenceClip;
        [SerializeField, Min(0.001f)] float m_MoodiEmergenceScale = 0.01f;
        [SerializeField, Min(0f)] float m_IntroLoopStartTime;
        [SerializeField, Min(0.1f)] float m_IntroLoopEndTime = 3.08f;
        [SerializeField, Min(0f)] float m_LanguageLoopStartTime = 24.466667f;
        [SerializeField, Min(0.1f)] float m_LanguageLoopEndTime = 32.266667f;
        [Header("Eye-relative placement")]
        [SerializeField, Min(0.2f)] float m_DistanceFromUser = 1.35f;
        [SerializeField] float m_HeightOffset = -0.05f;
        [SerializeField] float m_WorldPositionY = 1f;
        [Header("Hardware tutorial branches")]
        [SerializeField] VideoClip m_ChineseTutorialClip;
        [SerializeField] VideoClip m_EnglishTutorialClip;
        [SerializeField, Min(0f)] float m_ChineseHardwareQuestionTime = 9.04f;
        [SerializeField, Min(0.1f)] float m_ChineseHardwareLoopEndTime = 15.03f;
        [SerializeField, Min(0.1f)] float m_ChineseHardwareYesEndTime = 30.06f;
        [SerializeField, Min(0.1f)] float m_ChineseHardwareNoStartTime = 36.05f;
        [SerializeField, Min(0.1f)] float m_ChineseHardwareNoEndTime = 51.08f;
        [SerializeField, Min(0f)] float m_EnglishHardwareQuestionTime = 9.23f;
        [SerializeField, Min(0.1f)] float m_EnglishHardwareLoopEndTime = 15.03f;
        [SerializeField, Min(0.1f)] float m_EnglishHardwareYesEndTime = 30.11f;
        [SerializeField, Min(0.1f)] float m_EnglishHardwareNoStartTime = 35.16f;
        [SerializeField, Min(0.1f)] float m_EnglishHardwareNoEndTime = 50.20f;

        public VideoClip VideoClip => m_VideoClip;
        public VideoClip BagIntroClip => m_BagIntroClip;
        public VideoClip FrameAdjustingChineseClip => m_FrameAdjustingChineseClip;
        public VideoClip FrameAdjustingEnglishClip => m_FrameAdjustingEnglishClip;
        public VideoClip CandySqueezeClip => m_CandySqueezeClip;
        public VideoClip NatureSqueezeClip => m_NatureSqueezeClip;
        public GameObject MoodiEmergencePrefab => m_MoodiEmergencePrefab;
        public AnimationClip MoodiEmergenceClip => m_MoodiEmergenceClip;
        public float MoodiEmergenceScale => m_MoodiEmergenceScale;
        public float IntroLoopStartTime => m_IntroLoopStartTime;
        public float IntroLoopEndTime => Mathf.Max(m_IntroLoopStartTime + 0.01f, m_IntroLoopEndTime);
        public float LanguageLoopStartTime => m_LanguageLoopStartTime;
        public float LanguageLoopEndTime => Mathf.Max(m_LanguageLoopStartTime + 0.01f, m_LanguageLoopEndTime);
        public float DistanceFromUser => m_DistanceFromUser;
        public float HeightOffset => m_HeightOffset;
        public float WorldPositionY => m_WorldPositionY;
        // Compatibility properties retained for older inspectors and tests. The
        // combined timeline always uses the one prepared clip.
        public VideoClip ChineseTutorialClip => m_VideoClip;
        public VideoClip EnglishTutorialClip => m_VideoClip;
        public float HardwareQuestionTime(bool english) => english ? m_EnglishHardwareQuestionTime : m_ChineseHardwareQuestionTime;
        public float HardwareLoopEndTime(bool english) => english ? m_EnglishHardwareLoopEndTime : m_ChineseHardwareLoopEndTime;
        public float HardwareYesEndTime(bool english) => english ? m_EnglishHardwareYesEndTime : m_ChineseHardwareYesEndTime;
        public float HardwareNoStartTime(bool english) => english ? m_EnglishHardwareNoStartTime : m_ChineseHardwareNoStartTime;
        public float HardwareNoEndTime(bool english) => english ? m_EnglishHardwareNoEndTime : m_ChineseHardwareNoEndTime;
    }
}
