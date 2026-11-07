using System.Collections.Generic;
using Moodium.Interaction;
using UnityEngine;

namespace Moodium.Flow
{
    public sealed class MoodiumNoObjectModeController : MonoBehaviour
    {
        readonly List<GameObject> m_SpawnedObjects = new();

        public MoodiumInteractionState State { get; private set; } = MoodiumInteractionState.EditMode;
        public IReadOnlyList<GameObject> SpawnedObjects => m_SpawnedObjects;
        public MoodiumWorldDefinition CurrentWorld { get; private set; }

        public void SelectWorld(MoodiumWorldDefinition world)
        {
            if (world == null)
                return;

            if (CurrentWorld != null && CurrentWorld != world)
                ClearScene();

            CurrentWorld = world;
            EnterEditMode();
            Debug.Log($"[Moodium World] Selected: {CurrentWorld.DisplayName}");
        }

        public void Register(GameObject instance)
        {
            if (instance == null || m_SpawnedObjects.Contains(instance))
                return;

            m_SpawnedObjects.Add(instance);
            MoodiumRuntimeObjectRegistry.RegisterCreative(instance);
            ApplyState(instance);
            Debug.Log($"[Moodium Space] Object registered: {instance.name}; total={m_SpawnedObjects.Count}");
        }

        public void EnterEditMode()
        {
            SetState(MoodiumInteractionState.EditMode);
        }

        public void EnterInteractionMode()
        {
            SetState(MoodiumInteractionState.InteractionMode);
        }

        public void ClearScene()
        {
            for (var i = m_SpawnedObjects.Count - 1; i >= 0; i--)
            {
                if (m_SpawnedObjects[i] != null)
                    Destroy(m_SpawnedObjects[i]);
            }

            m_SpawnedObjects.Clear();
            Debug.Log("[Moodium Space] User-created objects cleared.");
        }

        void SetState(MoodiumInteractionState state)
        {
            State = state;
            m_SpawnedObjects.RemoveAll(item => item == null);
            foreach (var instance in m_SpawnedObjects)
                ApplyState(instance);

            Debug.Log($"[Moodium Space] State changed: {State}; objects={m_SpawnedObjects.Count}");
        }

        void ApplyState(GameObject instance)
        {
            var editing = State == MoodiumInteractionState.EditMode;
            var manipulable = instance.GetComponent<MoodiumManipulable>();
            if (manipulable != null)
                manipulable.SetInteractionState(State);

            var interaction = instance.GetComponent<MoodiumObjectInteraction>();
            if (interaction != null)
                interaction.SetInteractionEnabled(!editing);

            // Chocolate_Capsule uses the same complete crack controller in both modes.
            // In Edit Mode it remains manipulable only; Interaction Mode enables crack,
            // squash, round particles and its per-prefab touch sound.
            var chocolate = instance.GetComponent<ChocolateCapsuleInteraction>();
            if (chocolate != null)
                chocolate.SetInteractionEnabled(!editing);
        }
    }
}
