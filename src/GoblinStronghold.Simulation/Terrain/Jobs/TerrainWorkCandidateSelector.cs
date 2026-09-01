using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation.Terrain.Jobs;

internal readonly record struct TerrainWorkCandidate(
    EntityId DesignationId,
    GridPosition JobTarget,
    StoragePriority Priority,
    int EstimatedDistance);

internal readonly record struct TerrainWorkPlan(
    EntityId DesignationId,
    GridPosition JobTarget,
    IReadOnlyList<GridPosition> Route);

internal static class TerrainWorkCandidateSelector
{
    public static TerrainWorkPlan? SelectFirstReachableByEstimate(
        IEnumerable<TerrainWorkCandidate> candidates,
        int maximumRouteAttempts,
        Func<GridPosition, IReadOnlyList<GridPosition>?> findRoute)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRouteAttempts);
        ArgumentNullException.ThrowIfNull(findRoute);

        foreach (var candidate in OrderByEstimate(candidates).Take(maximumRouteAttempts))
        {
            var route = findRoute(candidate.JobTarget);
            if (route is not null)
            {
                return new TerrainWorkPlan(
                    candidate.DesignationId,
                    candidate.JobTarget,
                    route);
            }
        }

        return null;
    }

    public static TerrainWorkPlan? SelectShortestReachable(
        IEnumerable<TerrainWorkCandidate> candidates,
        Func<GridPosition, IReadOnlyList<GridPosition>?> findRoute)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(findRoute);

        return candidates
            .Select(candidate => new
            {
                Candidate = candidate,
                Route = findRoute(candidate.JobTarget),
            })
            .Where(item => item.Route is not null)
            .OrderByDescending(item => item.Candidate.Priority)
            .ThenBy(item => item.Route!.Count)
            .ThenBy(item => item.Candidate.DesignationId)
            .Select(item => (TerrainWorkPlan?)new TerrainWorkPlan(
                item.Candidate.DesignationId,
                item.Candidate.JobTarget,
                item.Route!))
            .FirstOrDefault();
    }

    private static IOrderedEnumerable<TerrainWorkCandidate> OrderByEstimate(
        IEnumerable<TerrainWorkCandidate> candidates) => candidates
        .OrderByDescending(candidate => candidate.Priority)
        .ThenBy(candidate => candidate.EstimatedDistance)
        .ThenBy(candidate => candidate.DesignationId)
        .ThenBy(candidate => candidate.JobTarget.Z)
        .ThenBy(candidate => candidate.JobTarget.Y)
        .ThenBy(candidate => candidate.JobTarget.X);
}
