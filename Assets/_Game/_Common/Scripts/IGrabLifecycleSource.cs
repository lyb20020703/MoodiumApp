using System;

namespace VFXViewer
{
    public interface IGrabLifecycleSource
    {
        bool IsGrabbed { get; }
        event Action GrabStarted;
        event Action GrabEnded;
    }
}
