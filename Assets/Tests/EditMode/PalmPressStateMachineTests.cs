using NUnit.Framework;

namespace Moodium.Gestures.Tests
{
    public sealed class PalmPressStateMachineTests
    {
        [Test]
        public void InitialContactTriggersOnce()
        {
            var state = new PalmPressStateMachine(0.3f);

            Assert.That(state.Update(true, 1f), Is.True);
            Assert.That(state.Update(true, 1.1f), Is.False);
        }

        [Test]
        public void LeavingTargetRearmsNextContact()
        {
            var state = new PalmPressStateMachine(0.3f);
            state.Update(true, 1f);

            Assert.That(state.Update(false, 1.1f), Is.False);
            Assert.That(state.Update(true, 1.31f), Is.True);
        }

        [Test]
        public void ReentryDuringCooldownDoesNotTriggerLaterWhileStillTouching()
        {
            var state = new PalmPressStateMachine(0.3f);
            state.Update(true, 1f);
            state.Update(false, 1.05f);

            Assert.That(state.Update(true, 1.1f), Is.False);
            Assert.That(state.Update(true, 1.31f), Is.False);
        }

        [Test]
        public void ResetClearsContactWithoutBypassingCooldown()
        {
            var state = new PalmPressStateMachine(0.3f);
            state.Update(true, 1f);

            state.Reset();

            Assert.That(state.Update(true, 1.1f), Is.False);
            state.Update(false, 1.2f);
            Assert.That(state.Update(true, 1.31f), Is.True);
        }
    }
}
