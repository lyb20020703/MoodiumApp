using TMPro;
using UnityEngine;

namespace Moodium.Flow
{
    public sealed class WorldCard : WorldCarouselItem
    {
        [SerializeField] TMP_Text m_WorldName;
        [SerializeField] TMP_Text m_LocalizedName;
        [SerializeField] TMP_Text m_Description;
        [SerializeField] TMP_Text m_Availability;
        [SerializeField] SpriteRenderer m_Artwork;
        [SerializeField] SpriteRenderer m_Icon;
        [SerializeField] Renderer m_AccentRenderer;
        [SerializeField] Vector2 m_IconTargetSize = new(0.29f, 0.19f);

        MaterialPropertyBlock m_PropertyBlock;

        public override void Bind(MoodiumWorldDefinition world, int index)
        {
            base.Bind(world, index);
            if (world == null)
                return;

            if (m_WorldName != null)
                m_WorldName.text = world.DisplayName;
            if (m_LocalizedName != null)
            {
                m_LocalizedName.text = world.LocalizedName;
                m_LocalizedName.gameObject.SetActive(!string.IsNullOrWhiteSpace(world.LocalizedName));
            }
            if (m_Description != null)
                m_Description.text = world.Description;
            if (m_Availability != null)
                m_Availability.text = world.IsAvailable ? "Pinch to enter" : "Coming Soon";
            if (m_Artwork != null)
            {
                m_Artwork.sprite = world.CardArtwork;
                m_Artwork.gameObject.SetActive(world.CardArtwork != null);
            }
            if (m_Icon != null)
            {
                m_Icon.sprite = world.Icon;
                m_Icon.gameObject.SetActive(world.Icon != null);
                FitIconWithoutCropping();
            }

            if (m_AccentRenderer != null)
            {
                m_PropertyBlock ??= new MaterialPropertyBlock();
                m_AccentRenderer.GetPropertyBlock(m_PropertyBlock);
                if (m_AccentRenderer.sharedMaterial != null && m_AccentRenderer.sharedMaterial.HasProperty("_BaseColor"))
                    m_PropertyBlock.SetColor("_BaseColor", world.AccentColor);
                if (m_AccentRenderer.sharedMaterial != null && m_AccentRenderer.sharedMaterial.HasProperty("_Color"))
                    m_PropertyBlock.SetColor("_Color", world.AccentColor);
                m_AccentRenderer.SetPropertyBlock(m_PropertyBlock);
            }
        }

        void FitIconWithoutCropping()
        {
            if (m_Icon == null || m_Icon.sprite == null)
                return;

            var size = m_Icon.sprite.bounds.size;
            if (size.x <= Mathf.Epsilon || size.y <= Mathf.Epsilon)
                return;
            var scale = Mathf.Min(m_IconTargetSize.x / size.x, m_IconTargetSize.y / size.y);
            m_Icon.transform.localScale = Vector3.one * scale;
        }
    }
}
