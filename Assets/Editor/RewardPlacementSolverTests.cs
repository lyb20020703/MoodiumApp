using System.Collections.Generic;
using Moodium.Rewards;
using NUnit.Framework;
using UnityEngine;

public sealed class RewardPlacementSolverTests
{
    [Test]
    public void Solve_SmallRewardUsesClearTableSideAndAvoidsBagAndVideo()
    {
        var bag = new Bounds(new Vector3(0f, .75f, .7f), new Vector3(.2f, .1f, .2f));
        var video = new Bounds(new Vector3(-.24f, .82f, .7f), new Vector3(.12f, .02f, .12f));
        var result = new RewardPlacementSolver().Solve(Request(20, RewardSizeClass.Small, .25f, bag, video), new FlatRoomQueries());

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.SurfaceKind, Is.EqualTo(RewardSurfaceKind.Table));
        Assert.That(result.Position.y, Is.EqualTo(.75f).Within(.001f));
        Assert.That(Expanded(bag, .05f).Contains(result.Position), Is.False);
        Assert.That(Expanded(video, .05f).Contains(result.Position), Is.False);
    }

    [Test]
    public void Solve_MediumStagesUseOppositeForwardSides()
    {
        var solver = new RewardPlacementSolver();
        var fifty = solver.Solve(Request(50, RewardSizeClass.Medium, .5f), new FlatRoomQueries());
        var eighty = solver.Solve(Request(80, RewardSizeClass.Medium, .85f, previousSide: fifty.Side), new FlatRoomQueries());

        Assert.That(fifty.IsValid && eighty.IsValid, Is.True);
        Assert.That(Mathf.Abs(fifty.SignedAngleDegrees), Is.InRange(30f, 45f));
        Assert.That(Mathf.Abs(eighty.SignedAngleDegrees), Is.InRange(30f, 45f));
        Assert.That(fifty.Side, Is.Not.EqualTo(eighty.Side));
        Assert.That(PlanarDistance(fifty.Position), Is.InRange(1.2f, 1.8f));
        Assert.That(PlanarDistance(eighty.Position), Is.InRange(1.2f, 1.8f));
    }

    [Test]
    public void Solve_LargeRewardStaysInForwardTwentyDegreeSector()
    {
        var result = new RewardPlacementSolver().Solve(Request(100, RewardSizeClass.Large, 1.4f), new FlatRoomQueries());

        Assert.That(result.IsValid, Is.True);
        Assert.That(Mathf.Abs(result.SignedAngleDegrees), Is.LessThanOrEqualTo(20f));
        Assert.That(PlanarDistance(result.Position), Is.InRange(2f, 2.8f));
        Assert.That(result.YawDegrees, Is.EqualTo(180f).Within(.01f));
    }

    [Test]
    public void Solve_BlockedVisibilityUsesVisibilityOnlyFallbackAfterSafeCandidatesAreExhausted()
    {
        var queries = new FlatRoomQueries { HideEverything = true };
        var result = new RewardPlacementSolver().Solve(Request(50, RewardSizeClass.Medium, .5f), queries);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Fallback, Is.EqualTo(RewardPlacementFallback.VisibilityOnly));
        Assert.That(Mathf.Abs(result.SignedAngleDegrees), Is.LessThanOrEqualTo(50f));
    }

    [Test]
    public void Solve_ExistingRewardForcesAnotherValidForwardSide()
    {
        var occupied = new List<RewardOccupiedSpace>
        {
            new(new Vector3(-.9f, 0f, 1.2f), .8f)
        };
        var request = Request(50, RewardSizeClass.Medium, .5f, occupied: occupied);
        var result = new RewardPlacementSolver().Solve(request, new FlatRoomQueries());

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Position.x, Is.GreaterThan(0f));
        Assert.That(Vector3.Distance(new Vector3(result.Position.x, 0f, result.Position.z), occupied[0].Position),
            Is.GreaterThanOrEqualTo(occupied[0].Radius + result.ReservationRadius + .15f));
    }

    static RewardPlacementRequest Request(int stage, RewardSizeClass size, float height,
        Bounds? bag = null, Bounds? video = null, RewardSide previousSide = RewardSide.None,
        IReadOnlyList<RewardOccupiedSpace> occupied = null) => new()
    {
        Stage = stage,
        SizeClass = size,
        TargetHeight = height,
        HeadPosition = new Vector3(0f, 1.6f, 0f),
        Forward = Vector3.forward,
        BagBounds = bag ?? new Bounds(new Vector3(0f, .75f, .7f), new Vector3(.2f, .1f, .2f)),
        VideoBounds = video ?? new Bounds(new Vector3(0f, .82f, .7f), new Vector3(.1f, .02f, .1f)),
        PreviousSide = previousSide,
        Occupied = occupied ?? new List<RewardOccupiedSpace>()
    };

    static float PlanarDistance(Vector3 position) => new Vector2(position.x, position.z).magnitude;
    static Bounds Expanded(Bounds value, float amount) { value.Expand(amount * 2f); return value; }

    sealed class FlatRoomQueries : IRewardPlacementQueries
    {
        public bool HideEverything;

        public bool TryGetSurface(Vector3 candidate, RewardSurfaceKind kind, out RewardSurface surface)
        {
            surface = new RewardSurface(new Vector3(candidate.x, kind == RewardSurfaceKind.Table ? .75f : 0f, candidate.z), Vector3.up);
            return true;
        }

        public bool HasClearance(Vector3 position, float height, float radius) => true;
        public bool IsVisible(Vector3 headPosition, Vector3 samplePoint) => !HideEverything;
    }
}
