using TMPro;
using UnityEngine;

namespace Moodium.Flow
{
    public sealed class WorldModelCarouselItem : WorldCarouselItem
    {
        const float ColliderPadding = 0.035f;
        const float MinimumColliderSize = 0.12f;

        TMP_Text m_StatusLabel;

        public static GameObject Create(
            Transform parent,
            MoodiumWorldDefinition world,
            int index,
            GameObject modelPrefab,
            TMP_FontAsset font)
        {
            var root = new GameObject($"World Model - {world?.DisplayName ?? index.ToString()}");
            root.transform.SetParent(parent, false);
            var item = root.AddComponent<WorldModelCarouselItem>();
            item.Build(world, index, modelPrefab, font);
            return root;
        }

        void Build(
            MoodiumWorldDefinition world,
            int index,
            GameObject modelPrefab,
            TMP_FontAsset font)
        {
            Bind(world, index);

            if (modelPrefab != null)
            {
                var visual = Instantiate(modelPrefab, transform, false);
                visual.name = modelPrefab.name;
            }

            var modelBounds = CalculateLocalRendererBounds();
            CreateHitTarget(modelBounds);
            CreateStatusLabel(world, font, modelBounds);
            SetSelected(false);
        }

        public override void SetSelected(bool selected)
        {
            if (m_StatusLabel != null && m_StatusLabel.gameObject.activeSelf != selected)
                m_StatusLabel.gameObject.SetActive(selected);
        }

        Bounds CalculateLocalRendererBounds()
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(Vector3.zero, Vector3.one * MinimumColliderSize);

            var hasPoint = false;
            var bounds = new Bounds();
            foreach (var renderer in renderers)
            {
                var worldBounds = renderer.bounds;
                for (var x = -1; x <= 1; x += 2)
                for (var y = -1; y <= 1; y += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    var worldPoint = worldBounds.center + Vector3.Scale(
                        worldBounds.extents,
                        new Vector3(x, y, z));
                    var localPoint = transform.InverseTransformPoint(worldPoint);
                    if (!hasPoint)
                    {
                        bounds = new Bounds(localPoint, Vector3.zero);
                        hasPoint = true;
                    }
                    else
                    {
                        bounds.Encapsulate(localPoint);
                    }
                }
            }

            return bounds;
        }

        void CreateHitTarget(Bounds modelBounds)
        {
            var size = modelBounds.size + Vector3.one * ColliderPadding * 2f;
            size.x = Mathf.Max(size.x, MinimumColliderSize);
            size.y = Mathf.Max(size.y, MinimumColliderSize);
            size.z = Mathf.Max(size.z, MinimumColliderSize);

            var hitTarget = gameObject.AddComponent<BoxCollider>();
            hitTarget.center = modelBounds.center;
            hitTarget.size = size;
        }

        void CreateStatusLabel(
            MoodiumWorldDefinition world,
            TMP_FontAsset font,
            Bounds modelBounds)
        {
            var labelObject = new GameObject("Selected World Status");
            labelObject.transform.SetParent(transform, false);
            labelObject.transform.localPosition = new Vector3(
                modelBounds.center.x,
                modelBounds.min.y - 0.055f,
                modelBounds.min.z - 0.02f);
            labelObject.transform.localRotation = Quaternion.identity;
            labelObject.transform.localScale = Vector3.one * 0.1f;

            m_StatusLabel = labelObject.AddComponent<TextMeshPro>();
            if (font != null)
                m_StatusLabel.font = font;
            m_StatusLabel.text = world == null
                ? string.Empty
                : world.IsAvailable
                    ? world.DisplayName
                    : $"{world.DisplayName}\nComing Soon";
            m_StatusLabel.fontSize = 2.2f;
            m_StatusLabel.alignment = TextAlignmentOptions.Center;
            m_StatusLabel.color = Color.white;
            m_StatusLabel.textWrappingMode = TextWrappingModes.NoWrap;
            m_StatusLabel.rectTransform.sizeDelta = new Vector2(3.8f, 1.1f);
            var labelRenderer = m_StatusLabel.GetComponent<Renderer>();
            if (labelRenderer != null)
                labelRenderer.sortingOrder = 30;
        }
    }
}
