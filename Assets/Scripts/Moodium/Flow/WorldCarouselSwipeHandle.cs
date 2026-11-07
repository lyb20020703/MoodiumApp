using UnityEngine;

namespace Moodium.Flow
{
    [DisallowMultipleComponent]
    public sealed class WorldCarouselSwipeHandle : MonoBehaviour
    {
        WorldCarouselSelectionController owner;
        int pointer = -1;
        int tapIndex = -1;
        public bool CanInteract => isActiveAndEnabled && owner != null && owner.CanInteract;
        public void Configure(WorldCarouselSelectionController controller, int itemIndex = -1)
        { owner = controller; tapIndex = itemIndex; }
        public void PointerStarted(int id, Vector3 world)
        {
            if (!CanInteract || pointer != -1 || !owner.BeginSwipe(world)) return;
            pointer = id;
        }
        public void PointerMoved(int id, Vector3 world)
        { if (pointer == id && owner != null) owner.MoveSwipe(world); }
        public void PointerEnded(int id)
        {
            if (pointer != id) return;
            pointer = -1;
            if (owner != null) owner.EndSwipe(tapIndex);
        }
        public void CancelPointer(int id)
        {
            if (pointer != id) return;
            pointer = -1;
            if (owner != null) owner.CancelSwipe();
        }
        void OnDisable() { if (pointer != -1) CancelPointer(pointer); }
    }
}
