using UnityEngine;
using Moodium.Interaction;
using Moodium.Audio;

namespace Moodium.Flow
{
    public sealed class MoodiumObjectInteraction : MonoBehaviour
    {
        SoftTouchFeedback m_SoftTouchFeedback;
        TouchParticleFeedback m_ParticleFeedback;
        MoodiumTouchAudio m_TouchAudio;
        SoftTouchDeformationController m_Deformation;

        public bool InteractionEnabled { get; private set; }

        public void SetInteractionEnabled(bool enabled)
        {
            InteractionEnabled = enabled;
            EnsureFeedback();
            m_SoftTouchFeedback.SetFeedbackEnabled(enabled);
            if (!enabled)
            {
                m_Deformation?.TouchEnd();
                m_TouchAudio?.EndHold();
            }
        }

        public void PointerStarted(int pointerId, Vector3 position)
        {
            if (!InteractionEnabled)
                return;

            EnsureFeedback();
            m_SoftTouchFeedback.TriggerFeedback();
            m_Deformation?.TouchBegin(position);
            m_ParticleFeedback?.Play();
            if (m_TouchAudio != null && m_TouchAudio.HasHoldSound)
                m_TouchAudio.BeginHold();
            else if (m_TouchAudio != null)
                m_TouchAudio.Play();
            else
                MoodiumAudioManager.Play(MoodiumAudioCue.ObjectTouch, position);
            Debug.Log($"[Moodium Play] Interaction started: {name}, pointer={pointerId}, position={position}");
        }

        public void PointerEnded(int pointerId)
        {
            if (!InteractionEnabled)
                return;

            m_Deformation?.TouchEnd();
            m_TouchAudio?.EndHold();
            Debug.Log($"[Moodium Play] Interaction ended: {name}, pointer={pointerId}");
        }

        public void PointerMoved(int pointerId, Vector3 position)
        {
            if (InteractionEnabled)
                m_Deformation?.TouchHold(position);
        }

        void EnsureFeedback()
        {
            if (m_SoftTouchFeedback == null)
                m_SoftTouchFeedback = GetComponent<SoftTouchFeedback>();
            if (m_SoftTouchFeedback == null)
                m_SoftTouchFeedback = gameObject.AddComponent<SoftTouchFeedback>();
            if (m_ParticleFeedback == null)
                m_ParticleFeedback = GetComponent<TouchParticleFeedback>();
            if (m_ParticleFeedback == null)
                m_ParticleFeedback = gameObject.AddComponent<TouchParticleFeedback>();
            if (m_TouchAudio == null)
                m_TouchAudio = GetComponent<MoodiumTouchAudio>();
            if (m_Deformation == null)
                m_Deformation = GetComponent<SoftTouchDeformationController>();
        }
    }
}
