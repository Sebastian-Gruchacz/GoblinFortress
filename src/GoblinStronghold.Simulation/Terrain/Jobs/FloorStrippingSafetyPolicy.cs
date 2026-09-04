using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Terrain.Jobs;

internal static class FloorStrippingSafetyPolicy
{
    public static bool IsSafeWorkPosition(
        WorldMapState world,
        GridPosition position,
        GridPosition target,
        IReadOnlySet<GridPosition> activeTargets)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(activeTargets);

        return world.IsTerrainTraversable(position) &&
            position != target &&
            !activeTargets.Contains(position) &&
            world.GetCardinalWorldNeighbors(position).Any(exit =>
                exit != target && world.IsTerrainTraversable(exit));
    }
}
