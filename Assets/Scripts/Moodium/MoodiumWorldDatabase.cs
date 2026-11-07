using UnityEngine;

namespace Moodium.Flow
{
    [CreateAssetMenu(fileName = "MoodiumWorldDatabase", menuName = "Moodium/World Database")]
    public sealed class MoodiumWorldDatabase : ScriptableObject
    {
        [SerializeField] MoodiumWorldDefinition[] m_Worlds;

        public MoodiumWorldDefinition[] Worlds => m_Worlds;
        public int Count => m_Worlds == null ? 0 : m_Worlds.Length;

        public MoodiumWorldDefinition GetWorld(int index)
        {
            return index >= 0 && index < Count ? m_Worlds[index] : null;
        }
    }
}
