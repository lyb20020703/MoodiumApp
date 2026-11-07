using System.Collections.Generic;
using Moodium.Interaction;
using UnityEngine;

namespace Moodium.Flow
{
    public sealed class MoodiumManipulable : MonoBehaviour
    {
        struct PointerPose
        {
            public Vector3 Position;
            public Quaternion Rotation;
        }

        [SerializeField] bool m_AllowMove = true;
        [SerializeField] bool m_AllowScale = true;
        [SerializeField, Min(0.05f)] float m_MinScaleMultiplier = 0.25f;
        [SerializeField, Min(0.1f)] float m_MaxScaleMultiplier = 4f;

        readonly Dictionary<int, PointerPose> m_Pointers = new();

        Vector3 m_GrabPositionOffset;
        Quaternion m_GrabRotationOffset;
        float m_InitialTwoHandDistance;
        Vector3 m_InitialTwoHandScale;
        Vector3 m_BaseScale;

        public MoodiumInteractionState InteractionState { get; private set; } = MoodiumInteractionState.EditMode;
        public bool CanManipulate => InteractionState == MoodiumInteractionState.EditMode;

        public void Configure(bool allowMove, bool allowScale)
        {
            m_AllowMove = allowMove;
            m_AllowScale = allowScale;
            m_BaseScale = transform.localScale;
        }

        void Awake()
        {
            m_BaseScale = transform.localScale;
        }

        public void BeginPointer(int id, Vector3 position, Quaternion rotation)
        {
            if (!CanManipulate)
                return;

            m_Pointers[id] = new PointerPose { Position = position, Rotation = rotation };

            if (m_Pointers.Count == 1)
            {
                PlayEditModeTouchFeedback();
                m_GrabRotationOffset = Quaternion.Inverse(rotation) * transform.rotation;
                m_GrabPositionOffset = Quaternion.Inverse(rotation) * (transform.position - position);
                Debug.Log($"[Moodium Interaction] Grab started: {name}");
            }
            else if (m_Pointers.Count == 2 && m_AllowScale)
            {
                CaptureScaleStart();
                Debug.Log($"[Moodium Interaction] Two-hand scale started: {name}");
            }
        }

        void PlayEditModeTouchFeedback()
        {
            // Edit-mode grab/move/rotate is spatial editing, not object interaction.
            // Selection audio is played by MoodiumObjectPickerItem; touch audio is
            // reserved for Interaction Mode through MoodiumObjectInteraction.
            var particleFeedback = GetComponent<TouchParticleFeedback>();
            if (particleFeedback != null)
                particleFeedback.Play();
            else
                MoodiumInteractionVFXManager.PlayInteractionEffect(
                    GetVisualCenter(),
                    transform.rotation,
                    gameObject);

        }

        Vector3 GetVisualCenter()
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return transform.position;
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds.center;
        }

        public void UpdatePointer(int id, Vector3 position, Quaternion rotation)
        {
            if (!CanManipulate)
                return;

            if (!m_Pointers.ContainsKey(id))
                return;

            m_Pointers[id] = new PointerPose { Position = position, Rotation = rotation };

            if (m_Pointers.Count >= 2 && m_AllowScale)
            {
                var distance = GetFirstTwoPointerDistance();
                if (m_InitialTwoHandDistance > 0.001f)
                {
                    var ratio = distance / m_InitialTwoHandDistance;
                    var minScale = m_BaseScale * m_MinScaleMultiplier;
                    var maxScale = m_BaseScale * m_MaxScaleMultiplier;
                    var desired = m_InitialTwoHandScale * ratio;
                    transform.localScale = new Vector3(
                        Mathf.Clamp(desired.x, minScale.x, maxScale.x),
                        Mathf.Clamp(desired.y, minScale.y, maxScale.y),
                        Mathf.Clamp(desired.z, minScale.z, maxScale.z));
                }

                return;
            }

            if (m_AllowMove)
            {
                transform.SetPositionAndRotation(
                    position + rotation * m_GrabPositionOffset,
                    rotation * m_GrabRotationOffset);
            }
        }

        public void EndPointer(int id)
        {
            if (!m_Pointers.Remove(id))
                return;

            if (m_Pointers.Count == 1)
            {
                foreach (var pointer in m_Pointers.Values)
                {
                    m_GrabRotationOffset = Quaternion.Inverse(pointer.Rotation) * transform.rotation;
                    m_GrabPositionOffset =
                        Quaternion.Inverse(pointer.Rotation) * (transform.position - pointer.Position);
                    break;
                }
            }

            Debug.Log($"[Moodium Interaction] Pointer released: {name}");
        }

        public void SetInteractionState(MoodiumInteractionState state)
        {
            InteractionState = state;
            m_Pointers.Clear();
            Debug.Log($"[Moodium Interaction] {name} state={InteractionState}");
        }

        void CaptureScaleStart()
        {
            m_InitialTwoHandDistance = GetFirstTwoPointerDistance();
            m_InitialTwoHandScale = transform.localScale;
        }

        float GetFirstTwoPointerDistance()
        {
            var found = 0;
            var first = Vector3.zero;
            foreach (var pointer in m_Pointers.Values)
            {
                if (found == 0)
                    first = pointer.Position;
                else
                    return Vector3.Distance(first, pointer.Position);
                found++;
            }

            return 0f;
        }
    }
}
