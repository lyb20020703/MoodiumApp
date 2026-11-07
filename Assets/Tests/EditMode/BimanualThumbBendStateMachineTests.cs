using NUnit.Framework;

namespace Moodium.Gestures.Tests
{
    public sealed class BimanualThumbBendStateMachineTests
    {
        [Test]
        public void OneBentThumbDoesNotStartSqueeze()
        {
            var state = CreateStateMachine();

            var result = state.Update(0.8f, 0.2f, true, true, 0.1f);

            Assert.That(result.Started, Is.False);
            Assert.That(result.IsSqueezing, Is.False);
        }

        [Test]
        public void BothThumbsMustRemainBentForDebounceDurationToStart()
        {
            var state = CreateStateMachine();

            var first = state.Update(0.8f, 0.7f, true, true, 0.03f);
            var second = state.Update(0.8f, 0.7f, true, true, 0.03f);

            Assert.That(first.Started, Is.False);
            Assert.That(second.Started, Is.True);
            Assert.That(second.IsSqueezing, Is.True);
            Assert.That(second.Strength, Is.EqualTo(0.7f).Within(0.0001f));
        }

        [Test]
        public void HysteresisKeepsSqueezeActiveBetweenStartAndReleaseThresholds()
        {
            var state = CreateStateMachine();
            state.Update(0.8f, 0.8f, true, true, 0.06f);

            var result = state.Update(0.45f, 0.45f, true, true, 0.1f);

            Assert.That(result.Ended, Is.False);
            Assert.That(result.IsSqueezing, Is.True);
        }

        [Test]
        public void TrackingLossUsesGracePeriodBeforeEndingSqueeze()
        {
            var state = CreateStateMachine();
            state.Update(0.8f, 0.8f, true, true, 0.06f);

            var briefLoss = state.Update(0f, 0f, false, false, 0.08f);
            var prolongedLoss = state.Update(0f, 0f, false, false, 0.05f);

            Assert.That(briefLoss.Ended, Is.False);
            Assert.That(briefLoss.IsSqueezing, Is.True);
            Assert.That(prolongedLoss.Ended, Is.True);
            Assert.That(prolongedLoss.IsSqueezing, Is.False);
        }

        [Test]
        public void ResetEndsActiveSqueezeAndClearsPendingDebounce()
        {
            var state = CreateStateMachine();
            state.Update(0.8f, 0.8f, true, true, 0.06f);

            var reset = state.Reset();
            var nextSample = state.Update(0.8f, 0.8f, true, true, 0.02f);

            Assert.That(reset.Ended, Is.True);
            Assert.That(reset.IsSqueezing, Is.False);
            Assert.That(nextSample.Started, Is.False);
        }

        static BimanualThumbBendStateMachine CreateStateMachine()
        {
            return new BimanualThumbBendStateMachine(
                startThreshold: 0.55f,
                releaseThreshold: 0.35f,
                startDebounceSeconds: 0.05f,
                trackingLossGraceSeconds: 0.12f);
        }
    }
}
