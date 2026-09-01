using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Terrain.Jobs;

internal readonly record struct TerrainWorkAssignment(
    ActorJobKind JobKind,
    EntityId DesignationId,
    GridPosition JobTarget,
    IReadOnlyList<GridPosition> Route,
    int WorkTicks);

internal static class TerrainWorkAssignmentFactory
{
    public static TerrainWorkAssignment Create(
        TerrainModificationDefinition definition,
        TerrainWorkPlan plan,
        int workTicks)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workTicks);
        if (plan.DesignationId == EntityId.None)
        {
            throw new ArgumentException(
                "A terrain work assignment requires a designation.",
                nameof(plan));
        }

        var jobKind = TerrainWorkPolicy.GetJobKind(definition);
        if (jobKind == ActorJobKind.None)
        {
            throw new ArgumentException(
                $"Terrain modification '{definition.Id}' has no actor-job adapter.",
                nameof(definition));
        }

        return new TerrainWorkAssignment(
            jobKind,
            plan.DesignationId,
            plan.JobTarget,
            plan.Route,
            workTicks);
    }
}
