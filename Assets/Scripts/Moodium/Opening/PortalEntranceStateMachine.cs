namespace Moodium.Opening
{
    public enum PortalEntranceState
    {
        Hidden,
        Opening,
        BagFalling,
        EntryEmerging,
        Ready,
        Entering,
        Completed
    }

    public sealed class PortalEntranceStateMachine
    {
        public PortalEntranceState State { get; private set; } = PortalEntranceState.Hidden;

        public void Reset() => State = PortalEntranceState.Hidden;

        public void BeginOpening()
        {
            if (State == PortalEntranceState.Hidden || State == PortalEntranceState.Completed)
                State = PortalEntranceState.Opening;
        }

        public void MarkBagFalling()
        {
            if (State == PortalEntranceState.Opening)
                State = PortalEntranceState.BagFalling;
        }

        public void MarkEntryEmerging()
        {
            if (State == PortalEntranceState.BagFalling)
                State = PortalEntranceState.EntryEmerging;
        }

        public void MarkReady()
        {
            if (State == PortalEntranceState.EntryEmerging)
                State = PortalEntranceState.Ready;
        }

        public bool TryBeginEntering()
        {
            if (State != PortalEntranceState.Ready)
                return false;
            State = PortalEntranceState.Entering;
            return true;
        }

        public void MarkCompleted()
        {
            if (State == PortalEntranceState.Entering)
                State = PortalEntranceState.Completed;
        }
    }
}
