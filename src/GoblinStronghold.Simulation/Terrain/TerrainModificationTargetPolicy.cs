using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Terrain;

public static class TerrainModificationTargetPolicy
{
    public static IReadOnlyList<GridPosition> QueryApplicableTargets(
        TerrainModificationDefinition definition,
        WorldMapState world,
        WorldVisibilityState visibility,
        GridPosition minimum,
        GridPosition maximum)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(visibility);

        var targets = new List<GridPosition>();
        for (var y = minimum.Y; y <= maximum.Y; y++)
        {
            for (var x = minimum.X; x <= maximum.X; x++)
            {
                var position = new GridPosition(x, y, minimum.Z);
                if (CanDesignate(definition, world, visibility, position))
                {
                    targets.Add(position);
                }
            }
        }

        return targets;
    }

    public static bool CanRetainDesignation(
        TerrainModificationDefinition definition,
        WorldMapState world,
        WorldVisibilityState visibility,
        GridPosition target,
        GridPosition? rampDestination = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(visibility);

        return definition.LegacyDesignation switch
        {
            WorkDesignationKind.MineRock =>
                visibility.Get(target) == CellVisibility.Unknown ||
                world.IsSolidRock(target) ||
                world.IsTerrainRampIntact(target) ||
                world.TryGetFluid(target, out _, out _),
            WorkDesignationKind.StripFloor => world.CanStripFloor(target),
            WorkDesignationKind.CarveRampDown => rampDestination is { } lower
                ? world.CanCarveRampDown(target, lower)
                : world.CanCarveRampDown(target),
            WorkDesignationKind.CarveRampUp => rampDestination is { } upper
                ? world.CanCarveRampUp(target, upper)
                : world.CanCarveRampUp(target),
            _ => false,
        };
    }

    private static bool CanDesignate(
        TerrainModificationDefinition definition,
        WorldMapState world,
        WorldVisibilityState visibility,
        GridPosition position) => definition.LegacyDesignation switch
    {
        WorkDesignationKind.MineRock =>
            (world.Baseline.IsRockPosition(position) || world.IsTerrainRampIntact(position)) &&
            (visibility.Get(position) == CellVisibility.Unknown ||
             world.IsSolidRock(position) || world.IsTerrainRampIntact(position)),
        WorkDesignationKind.StripFloor =>
            visibility.Get(position) != CellVisibility.Unknown && world.CanStripFloor(position),
        WorkDesignationKind.CarveRampDown =>
            visibility.Get(position) != CellVisibility.Unknown && world.CanCarveRampDown(position),
        WorkDesignationKind.CarveRampUp =>
            visibility.Get(position) != CellVisibility.Unknown && world.CanCarveRampUp(position),
        _ => false,
    };
}
