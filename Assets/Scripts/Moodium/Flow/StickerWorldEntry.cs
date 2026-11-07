using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace Moodium.Flow
{
    /// <summary>Identifies one image-tracked world entry without owning its lifecycle.</summary>
    public sealed class StickerWorldEntry
    {
        public StickerWorldEntry(
            TrackableId trackableId,
            string referenceImageName,
            MoodiumWorldDefinition world,
            Transform anchor,
            GameObject root)
        {
            TrackableId = trackableId;
            ReferenceImageName = referenceImageName;
            World = world;
            Anchor = anchor;
            Root = root;
        }

        public TrackableId TrackableId { get; }
        public string ReferenceImageName { get; }
        public MoodiumWorldDefinition World { get; }
        public Transform Anchor { get; }
        public GameObject Root { get; }
    }
}
