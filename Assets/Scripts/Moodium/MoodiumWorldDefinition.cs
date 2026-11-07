using UnityEngine;

namespace Moodium.Flow
{
    [CreateAssetMenu(fileName = "MoodiumWorld", menuName = "Moodium/World Definition")]
    public sealed class MoodiumWorldDefinition : ScriptableObject
    {
        [SerializeField] string m_WorldId;
        [SerializeField] string m_DisplayName;
        [SerializeField] string m_LocalizedName;
        [SerializeField, TextArea] string m_Description;
        [SerializeField] Sprite m_Icon;
        [SerializeField] Sprite m_CardArtwork;
        [SerializeField] AudioClip m_BackgroundMusic;
        [SerializeField] Color m_AccentColor = new(0.58f, 0.68f, 1f, 1f);
        [SerializeField] bool m_IsAvailable = true;
        [SerializeField] GameObject[] m_Prefabs;

        public string WorldId => m_WorldId;
        public string DisplayName => string.IsNullOrWhiteSpace(m_DisplayName) ? name : m_DisplayName;
        public string LocalizedName => m_LocalizedName;
        public string Description => m_Description;
        public Sprite Icon => m_Icon;
        public Sprite CardArtwork => m_CardArtwork;
        public AudioClip BackgroundMusic => m_BackgroundMusic;
        public Color AccentColor => m_AccentColor;
        public bool IsAvailable => m_IsAvailable;
        public GameObject[] Prefabs => m_Prefabs;
    }
}
