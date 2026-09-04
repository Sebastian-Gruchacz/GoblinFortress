using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Construction;

public static class WallTorchPlacementPolicy
{
    public static bool TryResolvePreferredSide(
        GridPosition wall,
        GridPosition handle,
        out CardinalOrientation side)
    {
        if (handle == wall)
        {
            side = CardinalOrientation.North;
            return true;
        }
        if (handle.Z != wall.Z ||
            Math.Abs(handle.X - wall.X) + Math.Abs(handle.Y - wall.Y) != 1)
        {
            side = default;
            return false;
        }

        side = handle.X > wall.X
            ? CardinalOrientation.East
            : handle.X < wall.X
                ? CardinalOrientation.West
                : handle.Y > wall.Y
                    ? CardinalOrientation.South
                    : CardinalOrientation.North;
        return true;
    }

    public static GridPosition CreateHandle(
        GridPosition wall,
        CardinalOrientation side) => side switch
    {
        CardinalOrientation.North => wall with { Y = wall.Y - 1 },
        CardinalOrientation.East => wall with { X = wall.X + 1 },
        CardinalOrientation.South => wall with { Y = wall.Y + 1 },
        CardinalOrientation.West => wall with { X = wall.X - 1 },
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
    };
}
