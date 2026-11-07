using System;
using System.Collections;
using Moodium.Interaction;
using Unity.PolySpatial;
using UnityEngine;

namespace Moodium.Flow.ObjectPicker
{
    public sealed class MoodiumObjectPickerItem : MonoBehaviour
    {
        GameObject m_SourcePrefab;
        Action<GameObject> m_OnSelected;
        Transform m_VisualRoot;
        Vector3 m_BaseScale;
        bool m_Hovered;
        bool m_Pressing;
        TouchParticleFeedback m_ParticleFeedback;
        MoodiumTouchAudio m_TouchAudio;

        public GameObject SourcePrefab => m_SourcePrefab;
        public float AppliedPreviewScale { get; private set; }

        public void Build(
            GameObject sourcePrefab,
            Material glassMaterial,
            float sphereDiameter,
            float targetModelSize,
            Action<GameObject> onSelected)
        {
            m_SourcePrefab = sourcePrefab;
            m_OnSelected = onSelected;
            m_BaseScale = transform.localScale;

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Glass Selection Sphere";
            sphere.transform.SetParent(transform, false);
            sphere.transform.localScale = Vector3.one * sphereDiameter;
            var sphereRenderer = sphere.GetComponent<MeshRenderer>();
            if (glassMaterial != null)
                sphereRenderer.sharedMaterial = glassMaterial;
            sphereRenderer.sortingOrder = 2;

            var hover = sphere.AddComponent<VisionOSHoverEffect>();
            hover.Type = VisionOSHoverEffect.EffectType.Highlight;
            hover.Color = new Color(0.68f, 0.76f, 1f, 1f);
            hover.IntensityMultiplier = 0.7f;

            var preview = Instantiate(sourcePrefab, transform, false);
            preview.name = $"{sourcePrefab.name} Selection Preview";
            PreparePreview(preview);
            AppliedPreviewScale = NormalizePreview(preview, targetModelSize);
            m_VisualRoot = preview.transform;

            var sourceFeedback = sourcePrefab.GetComponent<TouchParticleFeedback>();
            if (sourceFeedback != null && sourceFeedback.NativeBaselinePrefab != null)
            {
                m_ParticleFeedback = gameObject.AddComponent<TouchParticleFeedback>();
                m_ParticleFeedback.Configure(transform, null);
                m_ParticleFeedback.ConfigureVisionOS(sourceFeedback.NativeBaselinePrefab, true, 1f);
            }

            var sourceAudio = sourcePrefab.GetComponent<MoodiumTouchAudio>();
            if (sourceAudio != null && sourceAudio.TouchSound != null)
            {
                m_TouchAudio = gameObject.AddComponent<MoodiumTouchAudio>();
                m_TouchAudio.Configure(sourceAudio.TouchSound);
            }
        }

        public void SetHovered(bool hovered) => m_Hovered = hovered;

        public void Press()
        {
            if (!m_Pressing)
                StartCoroutine(PressFeedback());
        }

        void Update()
        {
            if (m_Pressing)
                return;
            var target = m_Hovered ? m_BaseScale * 1.08f : m_BaseScale;
            transform.localScale = Vector3.Lerp(
                transform.localScale,
                target,
                1f - Mathf.Exp(-12f * Time.deltaTime));
        }

        IEnumerator PressFeedback()
        {
            m_Pressing = true;
            if (m_ParticleFeedback != null)
                m_ParticleFeedback.Play();
            else
                MoodiumInteractionVFXManager.PlayInteractionEffect(
                    transform.position,
                    transform.rotation,
                    gameObject);
            m_TouchAudio?.Play();
            yield return AnimateScale(m_BaseScale * 1.15f, 0.09f);
            yield return AnimateScale(m_BaseScale, 0.12f);
            m_Pressing = false;
            m_OnSelected?.Invoke(m_SourcePrefab);
        }

        IEnumerator AnimateScale(Vector3 target, float duration)
        {
            var start = transform.localScale;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var eased = t * t * (3f - 2f * t);
                transform.localScale = Vector3.LerpUnclamped(start, target, eased);
                yield return null;
            }
            transform.localScale = target;
        }

        static void PreparePreview(GameObject preview)
        {
            foreach (var behaviour in preview.GetComponentsInChildren<MonoBehaviour>(true))
                behaviour.enabled = false;
            foreach (var animator in preview.GetComponentsInChildren<Animator>(true))
                animator.enabled = false;
            foreach (var particles in preview.GetComponentsInChildren<ParticleSystem>(true))
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var source in preview.GetComponentsInChildren<AudioSource>(true))
                source.playOnAwake = false;
            foreach (var collider in preview.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (var body in preview.GetComponentsInChildren<Rigidbody>(true))
            {
                body.useGravity = false;
                body.isKinematic = true;
            }
        }

        static float NormalizePreview(GameObject preview, float targetSize)
        {
            var renderers = preview.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return 1f;
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            var maxDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (maxDimension <= 0.00001f)
                return 1f;

            var factor = targetSize / maxDimension;
            preview.transform.localScale *= factor;

            bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            preview.transform.position += preview.transform.parent.position - bounds.center;
            return factor;
        }
    }
}
