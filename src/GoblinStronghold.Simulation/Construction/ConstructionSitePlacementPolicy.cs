using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Construction;

public static class ConstructionSitePlacementPolicy
{
    public static bool Conflicts(
        ConstructionKind firstKind,
        GridPosition firstAnchor,
        GridPosition firstEnd,
        IReadOnlyList<GridPosition> firstFootprint,
        ConstructionKind secondKind,
        GridPosition secondAnchor,
        GridPosition secondEnd,
        IReadOnlyList<GridPosition> secondFootprint)
    {
        ArgumentNullException.ThrowIfNull(firstFootprint);
        ArgumentNullException.ThrowIfNull(secondFootprint);
        if (firstFootprint.Intersect(secondFootprint).Any())
        {
            return true;
        }

        if (IsRamp(firstKind) && IsRamp(secondKind) && firstEnd == secondEnd)
        {
            return true;
        }

        return (IsRamp(firstKind) && IsFloor(secondKind) &&
                secondAnchor == firstAnchor with { Z = firstAnchor.Z + 1 }) ||
            (IsFloor(firstKind) && IsRamp(secondKind) &&
                firstAnchor == secondAnchor with { Z = secondAnchor.Z + 1 });
    }

    private static bool IsRamp(ConstructionKind kind) =>
        kind is ConstructionKind.WoodenRamp or ConstructionKind.StoneRamp;

    private static bool IsFloor(ConstructionKind kind) =>
        kind is ConstructionKind.WoodenFloor or ConstructionKind.StoneFloor;
}
