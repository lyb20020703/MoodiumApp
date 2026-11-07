using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moodium.Rewards
{
    public enum RewardSurfaceKind { Table, Ground }
    public enum RewardSide { None, Left, Right }
    public enum RewardPlacementFallback { None, AngleExpanded, DistanceReduced, OppositeSide, Shrunk, VisibilityOnly }

    public readonly struct RewardSurface
    {
        public RewardSurface(Vector3 position, Vector3 normal)
        {
            Position = position;
            Normal = normal;
        }

        public Vector3 Position { get; }
        public Vector3 Normal { get; }
    }

    public readonly struct RewardOccupiedSpace
    {
        public RewardOccupiedSpace(Vector3 position, float radius)
        {
            Position = position;
            Radius = Mathf.Max(0f, radius);
        }

        public Vector3 Position { get; }
        public float Radius { get; }
    }

    public interface IRewardPlacementQueries
    {
        bool TryGetSurface(Vector3 candidate, RewardSurfaceKind kind, out RewardSurface surface);
        bool HasClearance(Vector3 position, float height, float radius);
        bool IsVisible(Vector3 headPosition, Vector3 samplePoint);
    }

    public sealed class RewardPlacementRequest
    {
        public int Stage;
        public RewardSizeClass SizeClass;
        public float TargetHeight;
        public Vector3 HeadPosition;
        public Vector3 Forward;
        public Bounds BagBounds;
        public Bounds VideoBounds;
        public RewardSide PreviousSide;
        public IReadOnlyList<RewardOccupiedSpace> Occupied;
    }

    public readonly struct RewardPlacementResult
    {
        public RewardPlacementResult(Vector3 position, float yawDegrees, float scaleMultiplier, float reservationRadius,
            RewardSurfaceKind surfaceKind, RewardSide side, float signedAngleDegrees, RewardPlacementFallback fallback)
        {
            Position = position;
            YawDegrees = yawDegrees;
            ScaleMultiplier = scaleMultiplier;
            ReservationRadius = reservationRadius;
            SurfaceKind = surfaceKind;
            Side = side;
            SignedAngleDegrees = signedAngleDegrees;
            Fallback = fallback;
            IsValid = true;
        }

        public bool IsValid { get; }
        public Vector3 Position { get; }
        public float YawDegrees { get; }
        public float ScaleMultiplier { get; }
        public float ReservationRadius { get; }
        public RewardSurfaceKind SurfaceKind { get; }
        public RewardSide Side { get; }
        public float SignedAngleDegrees { get; }
        public RewardPlacementFallback Fallback { get; }
    }

    public sealed class RewardPlacementSolver
    {
        const float MinimumGroundNormal = .85f;
        const float ObstacleMargin = .15f;
        const float BoundsMargin = .05f;

        public RewardPlacementResult Solve(RewardPlacementRequest request, IRewardPlacementQueries queries)
        {
            if (request == null || queries == null || !WorldRewardTable.IsSupportedStage(request.Stage) ||
                !float.IsFinite(request.TargetHeight) || request.TargetHeight <= 0f)
                return default;

            var forward = Vector3.ProjectOnPlane(request.Forward, Vector3.up);
            if (forward.sqrMagnitude < .0001f)
                forward = Vector3.forward;
            forward.Normalize();

            if (request.Stage == 20 && TryTable(request, queries, forward, out var table))
                return table;

            var radius = ReservationRadius(request.TargetHeight, request.SizeClass);
            var preferredSide = PreferredSide(request);
            if (TryGround(request, queries, forward, preferredSide, InitialDistance(request.Stage), radius,
                    0, 45, 1f, true, RewardPlacementFallback.None, out var result))
                return result;

            for (var angleLimit = 50; angleLimit <= 50; angleLimit += 10)
                if (TryGround(request, queries, forward, preferredSide, InitialDistance(request.Stage), radius,
                        0, angleLimit, 1f, true, RewardPlacementFallback.AngleExpanded, out result))
                    return result;

            if (TryGround(request, queries, forward, preferredSide, .8f, radius,
                    0, 50, 1f, true, RewardPlacementFallback.DistanceReduced, out result))
                return result;

            if (request.Stage is 50 or 80 && preferredSide != RewardSide.None &&
                TryGround(request, queries, forward, Opposite(preferredSide), .8f, radius,
                    0, 50, 1f, true, RewardPlacementFallback.OppositeSide, out result))
                return result;

            if (TryGround(request, queries, forward, preferredSide, .8f, radius * .8f,
                    0, 50, .8f, true, RewardPlacementFallback.Shrunk, out result))
                return result;

            return TryGround(request, queries, forward, preferredSide, .8f, radius * .8f,
                0, 50, .8f, false, RewardPlacementFallback.VisibilityOnly, out result) ? result : default;
        }

        static bool TryTable(RewardPlacementRequest request, IRewardPlacementQueries queries, Vector3 forward,
            out RewardPlacementResult result)
        {
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            var distance = request.BagBounds.extents.x + .2f;
            foreach (var side in new[] { RewardSide.Right, RewardSide.Left })
            {
                var candidate = request.BagBounds.center + right * (side == RewardSide.Right ? distance : -distance);
                if (InsideExpanded(candidate, request.BagBounds) || InsideExpanded(candidate, request.VideoBounds) ||
                    !queries.TryGetSurface(candidate, RewardSurfaceKind.Table, out var surface) ||
                    surface.Normal.y < MinimumGroundNormal ||
                    Mathf.Abs(surface.Position.y - request.BagBounds.center.y) > .03f)
                    continue;

                var radius = ReservationRadius(request.TargetHeight, request.SizeClass);
                if (!IsClear(request, queries, surface.Position, radius, 1f) || !IsVisible(request, queries, surface.Position, false))
                    continue;

                result = BuildResult(request, surface.Position, forward, side, 90f * (side == RewardSide.Right ? 1f : -1f),
                    radius, RewardSurfaceKind.Table, 1f, RewardPlacementFallback.None);
                return true;
            }

            result = default;
            return false;
        }

        static bool TryGround(RewardPlacementRequest request, IRewardPlacementQueries queries, Vector3 forward,
            RewardSide preferredSide, float minimumDistance, float radius, float minAngle, float maxAngle,
            float scaleMultiplier, bool requireVisibility, RewardPlacementFallback fallback, out RewardPlacementResult result)
        {
            foreach (var angle in AnglesFor(request.Stage, preferredSide, minAngle, maxAngle))
            foreach (var distance in DistancesFor(request.Stage, minimumDistance))
            {
                var direction = Quaternion.AngleAxis(angle, Vector3.up) * forward;
                var candidate = request.HeadPosition + direction * distance;
                if (!queries.TryGetSurface(candidate, RewardSurfaceKind.Ground, out var surface) ||
                    surface.Normal.y < MinimumGroundNormal || surface.Position.y > request.HeadPosition.y - 1f)
                    continue;
                if (!IsClear(request, queries, surface.Position, radius, scaleMultiplier))
                    continue;
                if (requireVisibility && !IsVisible(request, queries, surface.Position, request.SizeClass == RewardSizeClass.Large))
                    continue;

                var side = angle < 0f ? RewardSide.Left : angle > 0f ? RewardSide.Right : RewardSide.None;
                result = BuildResult(request, surface.Position, forward, side, angle, radius, RewardSurfaceKind.Ground,
                    scaleMultiplier, fallback);
                return true;
            }

            result = default;
            return false;
        }

        static IEnumerable<float> AnglesFor(int stage, RewardSide side, float minimum, float maximum)
        {
            if (stage == 100)
            {
                yield return 0f;
                for (var angle = 5f; angle <= 20f; angle += 5f)
                {
                    yield return -angle;
                    yield return angle;
                }
                yield break;
            }

            var activeSide = side == RewardSide.None ? RewardSide.Left : side;
            var sign = activeSide == RewardSide.Left ? -1f : 1f;
            for (var angle = 35f; angle <= 45f; angle += 5f)
                if (angle >= minimum && angle <= maximum)
                    yield return sign * angle;
        }

        static IEnumerable<float> DistancesFor(int stage, float minimum)
        {
            var maximum = stage == 100 ? 2.8f : stage == 20 ? 1.4f : 1.8f;
            var start = Mathf.Max(minimum, stage == 100 ? 2f : stage == 20 ? 1f : 1.2f);
            for (var distance = start; distance <= maximum + .001f; distance += .1f)
                yield return distance;
        }

        static bool IsClear(RewardPlacementRequest request, IRewardPlacementQueries queries, Vector3 position,
            float radius, float scaleMultiplier)
        {
            if (!queries.HasClearance(position, request.TargetHeight * scaleMultiplier, radius))
                return false;
            if (request.Occupied == null)
                return true;

            foreach (var occupied in request.Occupied)
            {
                var planar = position - occupied.Position;
                planar.y = 0f;
                if (planar.magnitude < occupied.Radius + radius + ObstacleMargin)
                    return false;
            }
            return true;
        }

        static bool IsVisible(RewardPlacementRequest request, IRewardPlacementQueries queries, Vector3 basePosition,
            bool large)
        {
            var samples = large ? new[] { .6f, 1f } : new[] { .05f / request.TargetHeight, .3f };
            foreach (var normalizedHeight in samples)
                if (!queries.IsVisible(request.HeadPosition, basePosition + Vector3.up * (request.TargetHeight * normalizedHeight)))
                    return false;
            return true;
        }

        static RewardPlacementResult BuildResult(RewardPlacementRequest request, Vector3 position, Vector3 forward,
            RewardSide side, float angle, float radius, RewardSurfaceKind kind, float scale, RewardPlacementFallback fallback)
        {
            var facingUser = -forward;
            var yaw = Mathf.Repeat(Mathf.Atan2(facingUser.x, facingUser.z) * Mathf.Rad2Deg + 360f, 360f);
            return new RewardPlacementResult(position, yaw, scale, radius, kind, side, angle, fallback);
        }

        static RewardSide PreferredSide(RewardPlacementRequest request) => request.Stage == 80
            ? Opposite(request.PreviousSide == RewardSide.None ? RewardSide.Left : request.PreviousSide)
            : request.Stage == 50 ? RewardSide.Left : RewardSide.None;

        static RewardSide Opposite(RewardSide side) => side == RewardSide.Left ? RewardSide.Right :
            side == RewardSide.Right ? RewardSide.Left : RewardSide.None;

        static float InitialDistance(int stage) => stage == 100 ? 2f : stage == 20 ? 1f : 1.2f;
        static float ReservationRadius(float height, RewardSizeClass size) => size switch
        {
            RewardSizeClass.Small => Mathf.Max(.1f, height * .4f),
            RewardSizeClass.Medium => Mathf.Max(.18f, height * .4f),
            _ => Mathf.Max(.28f, height * .35f)
        };

        static bool InsideExpanded(Vector3 position, Bounds bounds)
        {
            bounds.Expand(BoundsMargin * 2f);
            return bounds.Contains(position);
        }
    }
}
