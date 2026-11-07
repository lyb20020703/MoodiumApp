using TMPro;
using UnityEngine;

namespace Moodium.MoodCheck
{
    public sealed class MoodCheckFontReferences : MonoBehaviour
    {
        [SerializeField] TMP_FontAsset m_Chinese;
        [SerializeField] TMP_FontAsset m_English;

        public TMP_FontAsset Chinese => m_Chinese;
        public TMP_FontAsset English => m_English;
        public void Configure(TMP_FontAsset chinese, TMP_FontAsset english)
        {
            m_Chinese = chinese;
            m_English = english;
        }
    }
}
