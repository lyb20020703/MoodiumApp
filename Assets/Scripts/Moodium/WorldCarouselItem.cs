using UnityEngine;

namespace Moodium.Flow
{
    public class WorldCarouselItem : MonoBehaviour
    {
        public MoodiumWorldDefinition World { get; private set; }
        public int Index { get; private set; }

        public virtual void Bind(MoodiumWorldDefinition world, int index)
        {
            World = world;
            Index = index;
        }

        public virtual void SetSelected(bool selected)
        {
        }
    }
}
