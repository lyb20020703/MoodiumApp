using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Moodium.Flow
{
    public sealed class WorldCarouselController : MonoBehaviour
    {
        [Header("Spatial layout")]
        [SerializeField] float m_FirstCardOffset = 0.32f;
        [SerializeField] float m_SecondCardOffset = 0.56f;
        [SerializeField] float m_DepthStep = 0.06f;
        [SerializeField] float m_FirstSideScale = 0.78f;
        [SerializeField] float m_SecondSideScale = 0.62f;
        [SerializeField] float m_FirstSideAngle = 18f;
        [SerializeField] float m_SecondSideAngle = 30f;

        [Header("3D model layout")]
        [SerializeField] float m_ModelFirstItemOffset = 0.62f;

        [Header("Gesture")]
        [SerializeField] float m_DragDistancePerCard = 0.2f;
        [SerializeField] float m_ClickThreshold = 0.028f;
        [SerializeField] float m_SnapSmoothTime = 0.14f;
        [SerializeField] float m_LayoutResponsiveness = 14f;

        readonly List<WorldCarouselItem> m_Items = new();
        readonly List<MoodiumWorldDefinition> m_VisibleWorlds = new();
        MoodiumWorldDatabase m_Database;
        GameObject m_CardPrefab;
        GameObject m_CandyModelPrefab;
        GameObject m_NatureModelPrefab;
        GameObject m_FallbackModelPrefab;
        TMP_FontAsset m_ModelLabelFont;
        Action<MoodiumWorldDefinition> m_OnWorldSelected;
        Action<MoodiumWorldDefinition> m_OnPreviewWorldChanged;
        bool m_UseModelItems;
        float m_CurrentPosition;
        float m_TargetPosition;
        float m_SnapVelocity;
        bool m_Dragging;
        int m_PointerId = -1;
        int m_PressedItemIndex = -1;
        Vector3 m_DragStartWorldPosition;
        float m_DragStartCarouselPosition;
        float m_DragDistance;
        int m_LastPreviewIndex = -1;

        public int CurrentIndex => Mathf.Clamp(Mathf.RoundToInt(m_TargetPosition), 0, Mathf.Max(0, m_Items.Count - 1));
        public MoodiumWorldDefinition SelectedWorld => GetVisibleWorld(CurrentIndex);

        public void Configure(
            MoodiumWorldDatabase database,
            GameObject cardPrefab,
            Action<MoodiumWorldDefinition> onWorldSelected,
            Action<MoodiumWorldDefinition> onPreviewWorldChanged = null)
        {
            m_Database = database;
            m_CardPrefab = cardPrefab;
            m_OnWorldSelected = onWorldSelected;
            m_OnPreviewWorldChanged = onPreviewWorldChanged;
            m_UseModelItems = false;
            BuildItems();
        }

        public void ConfigureModels(
            MoodiumWorldDatabase database,
            GameObject candyModelPrefab,
            GameObject natureModelPrefab,
            GameObject fallbackModelPrefab,
            TMP_FontAsset labelFont,
            Action<MoodiumWorldDefinition> onWorldSelected,
            Action<MoodiumWorldDefinition> onPreviewWorldChanged = null)
        {
            m_Database = database;
            m_CandyModelPrefab = candyModelPrefab;
            m_NatureModelPrefab = natureModelPrefab;
            m_FallbackModelPrefab = fallbackModelPrefab;
            m_ModelLabelFont = labelFont;
            m_OnWorldSelected = onWorldSelected;
            m_OnPreviewWorldChanged = onPreviewWorldChanged;
            m_UseModelItems = true;
            BuildItems();
        }

        public void PointerStarted(int pointerId, WorldCarouselItem item, Vector3 worldPosition)
        {
            if (m_Dragging || item == null)
                return;

            m_Dragging = true;
            m_PointerId = pointerId;
            m_PressedItemIndex = item.Index;
            m_DragStartWorldPosition = worldPosition;
            m_DragStartCarouselPosition = m_CurrentPosition;
            m_DragDistance = 0f;
            m_SnapVelocity = 0f;
        }

        public void PointerMoved(int pointerId, Vector3 worldPosition)
        {
            if (!m_Dragging || pointerId != m_PointerId || m_Items.Count == 0)
                return;

            var horizontalDistance = Vector3.Dot(worldPosition - m_DragStartWorldPosition, transform.right);
            m_DragDistance = Mathf.Max(m_DragDistance, Mathf.Abs(horizontalDistance));
            m_CurrentPosition = Mathf.Clamp(
                m_DragStartCarouselPosition - horizontalDistance / Mathf.Max(0.05f, m_DragDistancePerCard),
                0f,
                m_Items.Count - 1);
            m_TargetPosition = m_CurrentPosition;
        }

        public void PointerEnded(int pointerId)
        {
            if (!m_Dragging || pointerId != m_PointerId)
                return;

            var wasClick = m_DragDistance < m_ClickThreshold;
            m_Dragging = false;
            m_PointerId = -1;

            if (wasClick && m_PressedItemIndex >= 0)
            {
                if (Mathf.Abs(m_PressedItemIndex - m_CurrentPosition) > 0.35f)
                {
                    m_TargetPosition = m_PressedItemIndex;
                }
                else
                {
                    m_TargetPosition = m_PressedItemIndex;
                    var world = GetVisibleWorld(m_PressedItemIndex);
                    if (world != null && world.IsAvailable)
                        m_OnWorldSelected?.Invoke(world);
                    else if (world != null)
                        Debug.Log($"[Moodium Carousel] {world.DisplayName} is coming soon.");
                }
            }
            else
            {
                m_TargetPosition = Mathf.Clamp(Mathf.Round(m_CurrentPosition), 0f, m_Items.Count - 1);
            }

            m_PressedItemIndex = -1;
        }

        public void ResetToFirstAvailable()
        {
            if (m_VisibleWorlds.Count == 0)
                return;

            for (var i = 0; i < m_VisibleWorlds.Count; i++)
            {
                var world = m_VisibleWorlds[i];
                if (world != null && world.IsAvailable)
                {
                    m_CurrentPosition = i;
                    m_TargetPosition = i;
                    m_LastPreviewIndex = -1;
                    ApplyLayout(true);
                    return;
                }
            }

            m_CurrentPosition = 0f;
            m_TargetPosition = 0f;
            m_LastPreviewIndex = -1;
            ApplyLayout(true);
        }

        void Update()
        {
            if (!m_Dragging && m_Items.Count > 0)
            {
                m_CurrentPosition = Mathf.SmoothDamp(
                    m_CurrentPosition,
                    m_TargetPosition,
                    ref m_SnapVelocity,
                    m_SnapSmoothTime);
            }
            ApplyLayout(false);
        }

        void BuildItems()
        {
            foreach (var item in m_Items)
            {
                if (item != null)
                    Destroy(item.gameObject);
            }
            m_Items.Clear();
            m_VisibleWorlds.Clear();
            m_LastPreviewIndex = -1;

            if (m_Database == null || (!m_UseModelItems && m_CardPrefab == null))
            {
                Debug.LogError("[Moodium Carousel] World Database or item prefab is missing.");
                return;
            }

            for (var i = 0; i < m_Database.Count; i++)
            {
                var world = m_Database.GetWorld(i);
                if (!ShouldShowInWorldSelection(world))
                    continue;

                var visibleIndex = m_VisibleWorlds.Count;
                WorldCarouselItem item;
                if (m_UseModelItems)
                {
                    var modelPrefab = WorldModelPrefabResolver.Resolve(
                        world?.WorldId,
                        m_CandyModelPrefab,
                        m_NatureModelPrefab,
                        m_FallbackModelPrefab);
                    if (modelPrefab == null)
                    {
                        Debug.LogError($"[Moodium Carousel] Model prefab is missing for {world?.DisplayName ?? i.ToString()}.");
                        continue;
                    }

                    var instance = WorldModelCarouselItem.Create(
                        transform,
                        world,
                        visibleIndex,
                        modelPrefab,
                        m_ModelLabelFont);
                    item = instance.GetComponent<WorldModelCarouselItem>();
                }
                else
                {
                    var instance = Instantiate(m_CardPrefab, transform, false);
                    instance.name = $"World Card - {world?.DisplayName ?? visibleIndex.ToString()}";
                    item = instance.GetComponent<WorldCarouselItem>();
                    if (item == null)
                    {
                        Debug.LogError("[Moodium Carousel] WorldCarouselItem component is missing from the card prefab.");
                        Destroy(instance);
                        continue;
                    }
                    item.Bind(world, visibleIndex);
                }

                m_VisibleWorlds.Add(world);
                m_Items.Add(item);
            }

            ResetToFirstAvailable();
        }

        void ApplyLayout(bool immediate)
        {
            var interpolation = immediate ? 1f : 1f - Mathf.Exp(-m_LayoutResponsiveness * Time.deltaTime);
            var firstItemOffset = m_UseModelItems ? m_ModelFirstItemOffset : m_FirstCardOffset;
            var centeredIndex = Mathf.Clamp(
                Mathf.RoundToInt(m_CurrentPosition),
                0,
                Mathf.Max(0, m_Items.Count - 1));
            for (var i = 0; i < m_Items.Count; i++)
            {
                var item = m_Items[i];
                if (item == null)
                    continue;

                var offset = i - m_CurrentPosition;
                var absoluteOffset = Mathf.Abs(offset);
                var visible = absoluteOffset <= 2.55f;
                if (item.gameObject.activeSelf != visible)
                    item.gameObject.SetActive(visible);
                if (!visible)
                    continue;

                item.SetSelected(i == centeredIndex);

                var sign = Mathf.Sign(offset);
                var clamped = Mathf.Min(2f, absoluteOffset);
                var x = clamped <= 1f
                    ? firstItemOffset * clamped
                    : Mathf.Lerp(firstItemOffset, m_SecondCardOffset, clamped - 1f);
                var scale = clamped <= 1f
                    ? Mathf.Lerp(1f, m_FirstSideScale, clamped)
                    : Mathf.Lerp(m_FirstSideScale, m_SecondSideScale, clamped - 1f);
                var angle = clamped <= 1f
                    ? Mathf.Lerp(0f, m_FirstSideAngle, clamped)
                    : Mathf.Lerp(m_FirstSideAngle, m_SecondSideAngle, clamped - 1f);

                var targetPosition = new Vector3(sign * x, 0f, clamped * m_DepthStep);
                var targetRotation = Quaternion.Euler(0f, -sign * angle, 0f);
                var targetScale = Vector3.one * scale;
                item.transform.localPosition = Vector3.Lerp(item.transform.localPosition, targetPosition, interpolation);
                item.transform.localRotation = Quaternion.Slerp(item.transform.localRotation, targetRotation, interpolation);
                item.transform.localScale = Vector3.Lerp(item.transform.localScale, targetScale, interpolation);
            }

            NotifyPreviewWorldChanged(centeredIndex);
        }

        MoodiumWorldDefinition GetVisibleWorld(int index)
        {
            return index >= 0 && index < m_VisibleWorlds.Count ? m_VisibleWorlds[index] : null;
        }

        static bool ShouldShowInWorldSelection(MoodiumWorldDefinition world)
        {
            if (world == null)
                return false;

            return string.Equals(world.WorldId, "candy", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(world.WorldId, "nature", StringComparison.OrdinalIgnoreCase);
        }

        void NotifyPreviewWorldChanged(int centeredIndex)
        {
            if (m_Items.Count == 0 || centeredIndex == m_LastPreviewIndex)
                return;

            m_LastPreviewIndex = centeredIndex;
            m_OnPreviewWorldChanged?.Invoke(GetVisibleWorld(centeredIndex));
        }
    }
}
