using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using GoblinStronghold.Simulation.Terrain.Jobs;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class TerrainWorkCandidateSelectorTests
{
    [Fact]
    public void EstimatedSelectionKeepsDeterministicPriorityAndPositionOrder()
    {
        var candidates = new[]
        {
            Candidate(3, 4, 3, StoragePriority.Normal, distance: 2),
            Candidate(2, 5, 3, StoragePriority.Normal, distance: 2),
            Candidate(1, 8, 8, StoragePriority.High, distance: 9),
        };

        var plan = TerrainWorkCandidateSelector.SelectFirstReachableByEstimate(
            candidates,
            maximumRouteAttempts: 3,
            target => [target]);

        Assert.NotNull(plan);
        Assert.Equal(new EntityId(1), plan.Value.DesignationId);
    }

    [Fact]
    public void EstimatedSelectionHonorsRouteAttemptBudget()
    {
        var attempts = 0;
        var candidates = new[]
        {
            Candidate(1, 1, 1, StoragePriority.High, distance: 1),
            Candidate(2, 2, 2, StoragePriority.Normal, distance: 2),
        };

        var plan = TerrainWorkCandidateSelector.SelectFirstReachableByEstimate(
            candidates,
            maximumRouteAttempts: 1,
            target =>
            {
                attempts++;
                return target.X == 2 ? [target] : null;
            });

        Assert.Null(plan);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void ShortestSelectionRanksPriorityBeforeActualRouteLength()
    {
        var candidates = new[]
        {
            Candidate(1, 1, 1, StoragePriority.High, distance: 0),
            Candidate(2, 2, 2, StoragePriority.Normal, distance: 0),
        };

        var plan = TerrainWorkCandidateSelector.SelectShortestReachable(
            candidates,
            target => target.X == 1
                ? [target, target, target]
                : [target]);

        Assert.NotNull(plan);
        Assert.Equal(new EntityId(1), plan.Value.DesignationId);
        Assert.Equal(3, plan.Value.Route.Count);
    }

    private static TerrainWorkCandidate Candidate(
        ulong id,
        int x,
        int y,
        StoragePriority priority,
        int distance) => new(
            new EntityId(id),
            new GridPosition(x, y),
            priority,
            distance);
}
