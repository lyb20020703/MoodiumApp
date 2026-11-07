using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moodium.Flow.ObjectPicker
{
    public sealed class MoodiumObjectPickerController : MonoBehaviour
    {
        [SerializeField, Min(0.04f)] float m_TargetModelSize = 0.085f;
        [SerializeField, Min(0.08f)] float m_SphereDiameter = 0.15f;
        [SerializeField, Min(0.1f)] float m_ItemSpacing = 0.18f;
        [SerializeField, Min(0.5f)] float m_Distance = 1.2f;
        [SerializeField] float m_HeightOffset = -0.3f;

        readonly List<MoodiumObjectPickerItem> m_Items = new();
        Camera m_Camera;
        Material m_GlassMaterial;
        Action<GameObject> m_OnSelected;

        public IReadOnlyList<MoodiumObjectPickerItem> Items => m_Items;

        public void Configure(Camera camera, Material glassMaterial, Action<GameObject> onSelected)
        {
            m_Camera = camera;
            m_GlassMaterial = glassMaterial;
            m_OnSelected = onSelected;
        }

        public void Build(GameObject[] prefabs)
        {
            Clear();
            if (prefabs == null)
                return;
            var valid = new List<GameObject>();
            foreach (var prefab in prefabs)
                if (prefab != null)
                    valid.Add(prefab);
            valid.Sort((left, right) => GetDisplayOrder(left.name).CompareTo(GetDisplayOrder(right.name)));

            var center = (valid.Count - 1) * 0.5f;
            for (var i = 0; i < valid.Count; i++)
            {
                var itemObject = new GameObject($"Object Picker - {valid[i].name}");
                itemObject.transform.SetParent(transform, false);
                itemObject.transform.localPosition = new Vector3((i - center) * m_ItemSpacing, 0f, 0f);
                var item = itemObject.AddComponent<MoodiumObjectPickerItem>();
                item.Build(valid[i], m_GlassMaterial, m_SphereDiameter, m_TargetModelSize, m_OnSelected);
                m_Items.Add(item);
                Debug.Log($"[Moodium Object Picker] {valid[i].name}: autoScale={item.AppliedPreviewScale:0.######}");
            }
        }

        public void PlaceInFront()
        {
            m_Camera = Camera.main != null ? Camera.main : m_Camera;
            if (m_Camera == null)
                return;
            var forward = Vector3.ProjectOnPlane(m_Camera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f)
                forward = m_Camera.transform.forward.normalized;
            transform.SetPositionAndRotation(
                m_Camera.transform.position + forward * m_Distance + Vector3.up * m_HeightOffset,
                Quaternion.LookRotation(forward, Vector3.up));
        }

        public void Clear()
        {
            m_Items.Clear();
            for (var i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
        }

        static int GetDisplayOrder(string prefabName)
        {
            var normalized = prefabName.Replace("Prefab", string.Empty).Replace("_", string.Empty);
            return normalized switch
            {
                "Chocolate" => 0,
                "Cookie" => 1,
                "Heart" => 2,
                "Star" => 3,
                "Macaron" => 4,
                "ChocolateCapsule" => 5,
                _ => 100
            };
        }
    }
}
