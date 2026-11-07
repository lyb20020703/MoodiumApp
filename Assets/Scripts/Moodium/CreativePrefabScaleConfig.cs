using System;
using UnityEngine;

namespace Moodium.Flow
{
    [Serializable]
    public struct CreativePrefabScaleConfig
    {
        [SerializeField] GameObject m_Prefab;
        [SerializeField, Min(0.01f)] float m_InitialScaleMultiplier;

        public GameObject Prefab => m_Prefab;
        public float InitialScaleMultiplier => Mathf.Max(0.01f, m_InitialScaleMultiplier);
    }
}
