using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Construction;

public readonly record struct FloorCoveringPlacement(
    ConstructionKind Kind,
    GridPosition Anchor,
    GridPosition End);

public static class FloorCoveringPlacementPolicy
{
    public static bool TryResolve(
        WorldMapState world,
        ConstructionKind floorKind,
        GridPosition position,
        out FloorCoveringPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (floorKind is not (ConstructionKind.WoodenFloor or ConstructionKind.StoneFloor))
        {
            throw new ArgumentOutOfRangeException(nameof(floorKind));
        }

        if (world.CanPlanFloorConstruction([position]))
        {
            placement = new FloorCoveringPlacement(floorKind, position, position);
            return true;
        }

        if (world.TryGetNaturalRampUpper(position, out var upper) &&
            world.CanBuildRamp(position, upper))
        {
            placement = new FloorCoveringPlacement(
                floorKind == ConstructionKind.StoneFloor
                    ? ConstructionKind.StoneRamp
                    : ConstructionKind.WoodenRamp,
                position,
                upper);
            return true;
        }

        placement = default;
        return false;
    }
}
