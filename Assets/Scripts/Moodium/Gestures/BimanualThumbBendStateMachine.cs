using System;

namespace Moodium.Gestures
{
    public readonly struct BimanualThumbBendResult
    {
        public BimanualThumbBendResult(bool started, bool ended, bool isSqueezing, float strength)
        {
            Started = started;
            Ended = ended;
            IsSqueezing = isSqueezing;
            Strength = strength;
        }

        public bool Started { get; }
        public bool Ended { get; }
        public bool IsSqueezing { get; }
        public float Strength { get; }
    }

    public sealed class BimanualThumbBendStateMachine
    {
        readonly float m_StartThreshold;
        readonly float m_ReleaseThreshold;
        readonly float m_StartDebounceSeconds;
        readonly float m_TrackingLossGraceSeconds;

        float m_StartCandidateDuration;
        float m_TrackingLossDuration;

        public BimanualThumbBendStateMachine(
            float startThreshold,
            float releaseThreshold,
            float startDebounceSeconds,
            float trackingLossGraceSeconds)
        {
            if (startThreshold <= releaseThreshold)
                throw new ArgumentException("Start threshold must be greater than release threshold.");

            m_StartThreshold = startThreshold;
            m_ReleaseThreshold = releaseThreshold;
            m_StartDebounceSeconds = Math.Max(0f, startDebounceSeconds);
            m_TrackingLossGraceSeconds = Math.Max(0f, trackingLossGraceSeconds);
        }

        public bool IsSqueezing { get; private set; }

        public BimanualThumbBendResult Update(
            float leftCurl,
            float rightCurl,
            bool leftTracked,
            bool rightTracked,
            float deltaTime)
        {
            deltaTime = Math.Max(0f, deltaTime);
            var bothTracked = leftTracked && rightTracked;
            var strength = bothTracked ? Math.Min(leftCurl, rightCurl) : 0f;

            if (!bothTracked)
            {
                m_StartCandidateDuration = 0f;
                if (!IsSqueezing)
                    return Result(false, false, strength);

                m_TrackingLossDuration += deltaTime;
                if (m_TrackingLossDuration < m_TrackingLossGraceSeconds)
                    return Result(false, false, strength);

                IsSqueezing = false;
                return Result(false, true, strength);
            }

            m_TrackingLossDuration = 0f;
            if (IsSqueezing)
            {
                if (leftCurl > m_ReleaseThreshold && rightCurl > m_ReleaseThreshold)
                    return Result(false, false, strength);

                IsSqueezing = false;
                return Result(false, true, strength);
            }

            if (leftCurl < m_StartThreshold || rightCurl < m_StartThreshold)
            {
                m_StartCandidateDuration = 0f;
                return Result(false, false, strength);
            }

            m_StartCandidateDuration += deltaTime;
            if (m_StartCandidateDuration < m_StartDebounceSeconds)
                return Result(false, false, strength);

            m_StartCandidateDuration = 0f;
            IsSqueezing = true;
            return Result(true, false, strength);
        }

        public BimanualThumbBendResult Reset()
        {
            var ended = IsSqueezing;
            IsSqueezing = false;
            m_StartCandidateDuration = 0f;
            m_TrackingLossDuration = 0f;
            return Result(false, ended, 0f);
        }

        BimanualThumbBendResult Result(bool started, bool ended, float strength)
        {
            return new BimanualThumbBendResult(started, ended, IsSqueezing, strength);
        }
    }
}
