using UnityEngine;

namespace Moodium.Reality
{
    /// <summary>Shared-material crack visual. It never creates a material instance per crack.</summary>
    [DisallowMultipleComponent]
    public sealed class CandyWrapperCrackVisual : MonoBehaviour
    {
        MeshRenderer m_Renderer;
        MaterialPropertyBlock m_Block;
        float m_SpawnedAt;
        float m_Size;
        float m_AnimationDuration;
        float m_Overshoot;
        float m_Lifetime;
        float m_FadeDuration;
        bool m_Active;

        public bool IsActive => m_Active;
        public float SpawnedAt => m_SpawnedAt;

        public void Configure(Material sharedMaterial)
        {
            m_Renderer = GetComponent<MeshRenderer>();
            if (m_Renderer != null)
                m_Renderer.sharedMaterial = sharedMaterial;
            m_Block ??= new MaterialPropertyBlock();
        }

        public void Spawn(
            Transform surface, Vector3 point, Vector3 normal, float size, float roll,
            Texture texture, float surfaceOffset, float animationDuration, float overshoot,
            float lifetime, float fadeDuration)
        {
            // Store the pose in mesh-local space. AR mesh transforms can receive small
            // tracking corrections; keeping only the world pose made cracks drift in air.
            transform.SetParent(surface, false);
            var localPose = CandyWrapperCrackMath.ToSurfaceLocalPose(surface, point, normal, surfaceOffset, roll);
            transform.localPosition = localPose.LocalPosition;
            transform.localRotation = localPose.LocalRotation;
            m_Size = Mathf.Max(.001f, size);
            m_AnimationDuration = Mathf.Max(.01f, animationDuration);
            m_Overshoot = Mathf.Max(1f, overshoot);
            m_Lifetime = lifetime;
            m_FadeDuration = Mathf.Max(0f, fadeDuration);
            m_SpawnedAt = Time.unscaledTime;
            m_Active = true;
            gameObject.SetActive(true);
            ApplyTextureAndAlpha(texture, 1f);
            transform.localScale = Vector3.one * (m_Size * .1f);
        }

        public void Despawn()
        {
            m_Active = false;
            gameObject.SetActive(false);
            transform.SetParent(null, true);
        }

        void Update()
        {
            if (!m_Active)
                return;
            var age = Time.unscaledTime - m_SpawnedAt;
            if (age < m_AnimationDuration)
            {
                var t = age / m_AnimationDuration;
                var scale = t < .66f
                    ? Mathf.Lerp(.1f, m_Overshoot, Mathf.SmoothStep(0f, 1f, t / .66f))
                    : Mathf.Lerp(m_Overshoot, 1f, Mathf.SmoothStep(0f, 1f, (t - .66f) / .34f));
                transform.localScale = Vector3.one * (m_Size * scale);
            }
            else
                transform.localScale = Vector3.one * m_Size;

            if (m_Lifetime <= 0f || age < m_Lifetime)
                return;
            if (m_FadeDuration <= 0f)
            {
                Despawn();
                return;
            }
            var fade01 = Mathf.Clamp01((age - m_Lifetime) / m_FadeDuration);
            ApplyTextureAndAlpha(null, 1f - fade01);
            if (fade01 >= 1f)
                Despawn();
        }

        void ApplyTextureAndAlpha(Texture texture, float alpha)
        {
            if (m_Renderer == null)
                return;
            m_Block ??= new MaterialPropertyBlock();
            m_Renderer.GetPropertyBlock(m_Block);
            if (texture != null)
            {
                m_Block.SetTexture("_BaseMap", texture);
                m_Block.SetTexture("_MainTex", texture);
            }
            m_Block.SetColor("_BaseColor", new Color(1f, 1f, 1f, alpha));
            m_Block.SetColor("_Color", new Color(1f, 1f, 1f, alpha));
            m_Renderer.SetPropertyBlock(m_Block);
        }
    }
}
