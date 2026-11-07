using Moodium.Opening;
using NUnit.Framework;

public sealed class PortalEntranceStateMachineTests
{
    [Test]
    public void FollowsTheExpectedOneWaySequence()
    {
        var machine = new PortalEntranceStateMachine();

        Assert.That(machine.State, Is.EqualTo(PortalEntranceState.Hidden));
        machine.BeginOpening();
        Assert.That(machine.State, Is.EqualTo(PortalEntranceState.Opening));
        machine.MarkBagFalling();
        Assert.That(machine.State, Is.EqualTo(PortalEntranceState.BagFalling));
        machine.MarkEntryEmerging();
        Assert.That(machine.State, Is.EqualTo(PortalEntranceState.EntryEmerging));
        machine.MarkReady();
        Assert.That(machine.State, Is.EqualTo(PortalEntranceState.Ready));
        Assert.That(machine.TryBeginEntering(), Is.True);
        Assert.That(machine.State, Is.EqualTo(PortalEntranceState.Entering));
        machine.MarkCompleted();
        Assert.That(machine.State, Is.EqualTo(PortalEntranceState.Completed));
    }

    [Test]
    public void RejectsContactBeforeReadyAndAfterFirstTouch()
    {
        var machine = new PortalEntranceStateMachine();

        Assert.That(machine.TryBeginEntering(), Is.False);
        machine.BeginOpening();
        Assert.That(machine.TryBeginEntering(), Is.False);
        machine.MarkBagFalling();
        Assert.That(machine.TryBeginEntering(), Is.False);
        machine.MarkEntryEmerging();
        Assert.That(machine.TryBeginEntering(), Is.False);
        machine.MarkReady();
        Assert.That(machine.TryBeginEntering(), Is.True);
        Assert.That(machine.TryBeginEntering(), Is.False);
    }

    [Test]
    public void OpeningCannotBecomeReadyBeforeTheBagAndEntryStages()
    {
        var machine = new PortalEntranceStateMachine();

        machine.BeginOpening();
        machine.MarkReady();

        Assert.That(machine.State, Is.EqualTo(PortalEntranceState.Opening));
        Assert.That(machine.TryBeginEntering(), Is.False);
    }

    [Test]
    public void ResetReturnsToHidden()
    {
        var machine = new PortalEntranceStateMachine();
        machine.BeginOpening();
        machine.MarkBagFalling();
        machine.MarkEntryEmerging();
        machine.MarkReady();
        machine.TryBeginEntering();
        machine.MarkCompleted();

        machine.Reset();

        Assert.That(machine.State, Is.EqualTo(PortalEntranceState.Hidden));
    }
}
