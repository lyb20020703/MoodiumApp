using UnityEngine;

namespace Moodium.Flow
{
    [CreateAssetMenu(fileName = "MoodiSelectionAnimationConfig", menuName = "Moodium/Selection Moodi Animation Config")]
    public sealed class MoodiSelectionAnimationConfig : ScriptableObject
    {
        [SerializeField] AnimationClip m_IdleBlink;
        [SerializeField] AnimationClip m_ShySmile;
        [SerializeField] AnimationClip m_Wink;

        public AnimationClip IdleBlink => m_IdleBlink;
        public AnimationClip ShySmile => m_ShySmile;
        public AnimationClip Wink => m_Wink;

        public void Configure(AnimationClip idleBlink, AnimationClip shySmile, AnimationClip wink)
        {
            m_IdleBlink = idleBlink;
            m_ShySmile = shySmile;
            m_Wink = wink;
        }
    }
}
