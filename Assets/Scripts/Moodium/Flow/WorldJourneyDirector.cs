using System;
using UnityEngine;
namespace Moodium.Flow
{
    public enum WorldJourneyStage { Rescue, Explore, Bubble, Ending }
    public sealed class WorldJourneyDirector : MonoBehaviour
    {
        public WorldJourneyStage Stage { get; private set; } = WorldJourneyStage.Rescue;
        public bool CanGrowInRoom => Stage == WorldJourneyStage.Rescue || Stage == WorldJourneyStage.Explore;
        public event Action<WorldJourneyStage> StageChanged;
        bool m_ExitPending;
        public void Begin() { m_ExitPending = false; Set(WorldJourneyStage.Rescue); }
        public void Rescued() { if (Stage == WorldJourneyStage.Rescue) Set(WorldJourneyStage.Explore); }
        public void EnergyFull(bool hasRescue) { if (!hasRescue) Rescued(); }
        public bool EnterBubble() { if (Stage != WorldJourneyStage.Explore || m_ExitPending) return false; Set(WorldJourneyStage.Bubble); return true; }
        public bool RequestExit() { if (m_ExitPending || Stage == WorldJourneyStage.Ending) return false; m_ExitPending = true; if (Stage != WorldJourneyStage.Bubble) Set(WorldJourneyStage.Ending); return true; }
        public void Returned() { if (Stage == WorldJourneyStage.Bubble) Set(m_ExitPending ? WorldJourneyStage.Ending : WorldJourneyStage.Explore); }
        public void End() { m_ExitPending = true; Set(WorldJourneyStage.Ending); }
        void Set(WorldJourneyStage value) { if (Stage == value) return; Stage = value; Debug.Log("[World Journey] " + value); StageChanged?.Invoke(value); }
    }
}
