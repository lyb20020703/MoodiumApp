using System.Collections.Generic;
using UnityEngine;

namespace Moodium.NatureWorld
{
    /// <summary>
    /// Room content (trees, candy house, footstep flowers, rewards, Moodi, the sensory bag …) standing beyond the mercury
    /// door's plane would show inside the door, mixed into the bubble world: the booth's walls hide it everywhere else,
    /// but not through the opening. While the world shows through the door to a viewer in the room, those renderers are
    /// switched off; everything on the viewer's side stays. Inside the world the room content is hidden anyway
    /// (RoomContentVisibility), and when the door closes everything comes back.
    /// </summary>
    public sealed class BubbleDoorRoomContent
    {
        readonly List<Renderer> m_Hidden = new();

        public int HiddenCount => m_Hidden.Count;

        /// <summary>Beyond the door plane (a centimetre of slack), in the door's room frame.</summary>
        public static bool Beyond(Transform room, float doorZ, Vector3 point) => room.InverseTransformPoint(point).z > doorZ - .01f;

        /// <summary>Shows what was hidden, then hides the renderers under <paramref name="roots"/> whose bounds' centre is beyond the door.</summary>
        public void Apply(IEnumerable<GameObject> roots, Transform room, float doorZ, bool hideAll = false)
        {
            Restore();
            if (roots == null || room == null) return;
            foreach (var root in roots)
            {
                if (root == null) continue;
                foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                    if (renderer.enabled && (hideAll || Beyond(room, doorZ, renderer.bounds.center)))
                    {
                        renderer.enabled = false;
                        m_Hidden.Add(renderer);
                    }
            }
        }

        public void KeepHidden()
        {
            foreach (var renderer in m_Hidden) if (renderer != null) renderer.enabled = false;
        }

        public void Restore()
        {
            foreach (var renderer in m_Hidden) if (renderer != null) renderer.enabled = true;
            m_Hidden.Clear();
        }
    }
}
