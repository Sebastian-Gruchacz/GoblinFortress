using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Planning;

public readonly record struct DirectionalLadderPlacement(
    GridPosition Lower,
    GridPosition Upper);

public static class DirectionalLadderPlacementPolicy
{
    public static bool TryResolve(
        GridPosition dragStart,
        GridPosition dragEnd,
        Func<GridPosition, GridPosition, bool> canBuild,
        out DirectionalLadderPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(canBuild);
        placement = default;
        if (dragStart.Z != dragEnd.Z ||
            Math.Abs(dragEnd.X - dragStart.X) + Math.Abs(dragEnd.Y - dragStart.Y) != 1)
        {
            return false;
        }

        var fromLower = new DirectionalLadderPlacement(
            dragStart,
            dragEnd with { Z = dragEnd.Z + 1 });
        var fromUpper = new DirectionalLadderPlacement(
            dragEnd with { Z = dragEnd.Z - 1 },
            dragStart);
        var valid = new[] { fromLower, fromUpper }
            .Where(candidate => canBuild(candidate.Lower, candidate.Upper))
            .ToArray();
        if (valid.Length != 1)
        {
            return false;
        }

        placement = valid[0];
        return true;
    }
}
