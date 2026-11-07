using System.Collections;
using Moodium.Interaction;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

namespace Moodium.Opening
{
    public sealed class OpeningLogoTextView : MonoBehaviour
    {
        [SerializeField] TMP_Text[] m_Texts;
        [Header("Soft hand response")]
        [SerializeField, Min(0.02f)] float m_ProximityRadius = 0.14f;
        [SerializeField, Min(0.01f)] float m_TouchRadius = 0.065f;
        [SerializeField, Range(0.02f, 0.25f)] float m_SquashAmount = 0.1f;
        [SerializeField, Range(1f, 80f)] float m_Elasticity = 34f;
        [SerializeField, Range(0.5f, 20f)] float m_Damping = 6.5f;

        XRHandSubsystem m_HandSubsystem;
        Vector3 m_RestScale;
        Color[] m_RestColors;
        float m_Response;
        float m_ResponseVelocity;
        bool m_WasTouched;

        void Awake()
        {
            m_RestScale = transform.localScale;
            CacheTextColors();
        }

        void Update()
        {
            if (!gameObject.activeInHierarchy)
                return;

            var target = 0f;
            var touchPosition = transform.position;
            if (TryGetClosestHandDistance(out var distance, out var closestHandPosition))
            {
                target = 1f - Mathf.InverseLerp(m_TouchRadius, m_ProximityRadius, distance);
                touchPosition = closestHandPosition;
            }

            var touching = target > 0.88f;
            if (touching && !m_WasTouched)
            {
                MoodiumInteractionVFXManager.PlayInteractionEffect(touchPosition, Quaternion.identity);
                Debug.Log("[Moodium Opening] MoodiumLogoText touched.");
            }
            m_WasTouched = touching;

            // A lightly under-damped spring creates the gummy overshoot on release.
            var deltaTime = Mathf.Min(Time.deltaTime, 1f / 30f);
            m_ResponseVelocity += (target - m_Response) * m_Elasticity * deltaTime;
            m_ResponseVelocity *= Mathf.Exp(-m_Damping * deltaTime);
            m_Response = Mathf.Clamp(m_Response + m_ResponseVelocity * deltaTime, -0.12f, 1.08f);
            ApplySoftResponse(m_Response);
        }

        public void SetVisibleImmediate(bool visible)
        {
            gameObject.SetActive(visible);
            SetAlpha(visible ? 1f : 0f);
            if (!visible)
                ResetSoftResponse();
        }

        public IEnumerator FadeIn(float duration)
        {
            gameObject.SetActive(true);
            SetAlpha(0f);
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
                // Smooth, restrained fade suitable for a spatial title.
                SetAlpha(t * t * (3f - 2f * t));
                yield return null;
            }
            SetAlpha(1f);
        }

        void SetAlpha(float alpha)
        {
            if (m_Texts == null)
                return;
            foreach (var text in m_Texts)
            {
                if (text == null)
                    continue;
                var color = text.color;
                color.a = alpha;
                text.color = color;
            }
        }

        void CacheTextColors()
        {
            if (m_Texts == null)
                return;
            m_RestColors = new Color[m_Texts.Length];
            for (var i = 0; i < m_Texts.Length; i++)
                m_RestColors[i] = m_Texts[i] != null ? m_Texts[i].color : Color.white;
        }

        void ApplySoftResponse(float response)
        {
            var compression = Mathf.Max(0f, response);
            var stretch = compression * m_SquashAmount * 0.28f;
            transform.localScale = Vector3.Scale(
                m_RestScale,
                new Vector3(1f + stretch, 1f - compression * m_SquashAmount, 1f + stretch));

            if (m_Texts == null)
                return;
            for (var i = 0; i < m_Texts.Length; i++)
            {
                var text = m_Texts[i];
                if (text == null)
                    continue;
                var alpha = text.color.a;
                var baseColor = m_RestColors != null && i < m_RestColors.Length
                    ? m_RestColors[i]
                    : Color.white;
                var glowColor = Color.Lerp(baseColor, new Color(1f, 0.82f, 1f, 1f), compression * 0.65f);
                glowColor.a = alpha;
                text.color = glowColor;
            }
        }

        void ResetSoftResponse()
        {
            m_Response = 0f;
            m_ResponseVelocity = 0f;
            m_WasTouched = false;
            transform.localScale = m_RestScale;
        }

        bool TryGetClosestHandDistance(out float distance, out Vector3 handPosition)
        {
            distance = float.PositiveInfinity;
            handPosition = transform.position;
            if (m_HandSubsystem == null)
                m_HandSubsystem = XRGeneralSettings.Instance?.Manager?.activeLoader
                    ?.GetLoadedSubsystem<XRHandSubsystem>();
            if (m_HandSubsystem == null || !m_HandSubsystem.running)
                return false;

            m_HandSubsystem.TryUpdateHands(XRHandSubsystem.UpdateType.Dynamic);
            CheckHand(m_HandSubsystem.leftHand, ref distance, ref handPosition);
            CheckHand(m_HandSubsystem.rightHand, ref distance, ref handPosition);
            return !float.IsPositiveInfinity(distance);
        }

        void CheckHand(XRHand hand, ref float closestDistance, ref Vector3 closestPosition)
        {
            if (!hand.isTracked)
                return;
            CheckJoint(hand.GetJoint(XRHandJointID.IndexTip), ref closestDistance, ref closestPosition);
            CheckJoint(hand.GetJoint(XRHandJointID.MiddleTip), ref closestDistance, ref closestPosition);
        }

        void CheckJoint(XRHandJoint joint, ref float closestDistance, ref Vector3 closestPosition)
        {
            if (joint.trackingState == XRHandJointTrackingState.None || !joint.TryGetPose(out var pose))
                return;

            var distance = DistanceToText(pose.position);
            if (distance >= closestDistance)
                return;
            closestDistance = distance;
            closestPosition = pose.position;
        }

        float DistanceToText(Vector3 worldPosition)
        {
            var closest = float.PositiveInfinity;
            if (m_Texts != null)
            {
                foreach (var text in m_Texts)
                {
                    if (text == null)
                        continue;
                    var textRenderer = text.GetComponent<Renderer>();
                    if (textRenderer == null)
                        continue;
                    var point = textRenderer.bounds.ClosestPoint(worldPosition);
                    closest = Mathf.Min(closest, Vector3.Distance(worldPosition, point));
                }
            }
            return float.IsPositiveInfinity(closest)
                ? Vector3.Distance(worldPosition, transform.position)
                : closest;
        }
    }
}
