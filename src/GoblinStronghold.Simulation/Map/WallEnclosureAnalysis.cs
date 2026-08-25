namespace GoblinStronghold.Simulation.Map;

[Flags]
public enum WallInteriorFacing : byte
{
    None = 0,
    North = 1,
    East = 2,
    South = 4,
    West = 8,
}

public readonly record struct WallRenderSides(
    WallInteriorFacing Connections,
    WallInteriorFacing RoomSides,
    WallInteriorFacing CoveredSides,
    WallInteriorFacing VisibleFaces);

public readonly record struct WallMountPlacement(CardinalOrientation Side)
{
    public bool RunsHorizontally => Side is CardinalOrientation.North or CardinalOrientation.South;
}

public static class WallMountPlacementResolver
{
    public static bool TryResolve(
        WallRenderSides wall,
        CardinalOrientation? preferredSide,
        out WallMountPlacement placement)
    {
        if (preferredSide is { } preferred && !Enum.IsDefined(preferred))
        {
            throw new ArgumentOutOfRangeException(nameof(preferredSide), preferredSide, null);
        }

        var candidates = wall.VisibleFaces != WallInteriorFacing.None
            ? wall.VisibleFaces
            : wall.RoomSides != WallInteriorFacing.None
                ? wall.RoomSides
                : GetNeutralFaces(wall.Connections) & ~wall.CoveredSides;
        if (preferredSide is { } requested &&
            (candidates & ToFacing(requested)) != WallInteriorFacing.None)
        {
            placement = new WallMountPlacement(requested);
            return true;
        }

        if (TryGetSingleSide(candidates, out var singleSide))
        {
            placement = new WallMountPlacement(singleSide);
            return true;
        }

        if (candidates == (WallInteriorFacing.North | WallInteriorFacing.South))
        {
            placement = new WallMountPlacement(CardinalOrientation.North);
            return true;
        }
        if (candidates == (WallInteriorFacing.East | WallInteriorFacing.West))
        {
            placement = new WallMountPlacement(CardinalOrientation.East);
            return true;
        }

        placement = default;
        return false;
    }

    private static bool TryGetSingleSide(
        WallInteriorFacing facing,
        out CardinalOrientation side)
    {
        side = facing switch
        {
            WallInteriorFacing.North => CardinalOrientation.North,
            WallInteriorFacing.East => CardinalOrientation.East,
            WallInteriorFacing.South => CardinalOrientation.South,
            WallInteriorFacing.West => CardinalOrientation.West,
            _ => default,
        };
        return facing is WallInteriorFacing.North or WallInteriorFacing.East or
            WallInteriorFacing.South or WallInteriorFacing.West;
    }

    private static WallInteriorFacing ToFacing(CardinalOrientation side) => side switch
    {
        CardinalOrientation.North => WallInteriorFacing.North,
        CardinalOrientation.East => WallInteriorFacing.East,
        CardinalOrientation.South => WallInteriorFacing.South,
        CardinalOrientation.West => WallInteriorFacing.West,
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
    };

    private static WallInteriorFacing GetNeutralFaces(WallInteriorFacing connections) =>
        WallEnclosureAnalysis.GetNeutralFaces(connections);
}

public sealed class WallEnclosureAnalysis
{
    private readonly HashSet<GridPosition> _interiorCells;
    private readonly Dictionary<GridPosition, WallRenderSides> _wallSides;

    private WallEnclosureAnalysis(
        HashSet<GridPosition> interiorCells,
        Dictionary<GridPosition, WallRenderSides> wallSides)
    {
        _interiorCells = interiorCells;
        _wallSides = wallSides;
    }

    public IReadOnlySet<GridPosition> InteriorCells => _interiorCells;

    public WallInteriorFacing GetInteriorFacing(GridPosition wallPosition) =>
        GetWallSides(wallPosition).RoomSides;

    public WallRenderSides GetWallSides(GridPosition wallPosition) =>
        _wallSides.GetValueOrDefault(wallPosition);

    public static WallEnclosureAnalysis Analyze(
        int width,
        int height,
        IReadOnlySet<GridPosition> barriers,
        IReadOnlySet<GridPosition>? solidCells = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(barriers);

        var walls = barriers
            .Where(position => position.Z == 0 && IsWithin(position, width, height))
            .ToHashSet();
        var solids = solidCells?
            .Where(position => position.Z == 0 && IsWithin(position, width, height))
            .ToHashSet() ?? [];
        var blocked = walls.Concat(solids).ToHashSet();
        var exterior = new HashSet<GridPosition>();
        var queue = new Queue<GridPosition>();
        for (var x = 0; x < width; x++)
        {
            EnqueueExterior(new GridPosition(x, 0), blocked, exterior, queue);
            EnqueueExterior(new GridPosition(x, height - 1), blocked, exterior, queue);
        }
        for (var y = 0; y < height; y++)
        {
            EnqueueExterior(new GridPosition(0, y), blocked, exterior, queue);
            EnqueueExterior(new GridPosition(width - 1, y), blocked, exterior, queue);
        }

        while (queue.TryDequeue(out var current))
        {
            foreach (var neighbor in GetCardinalNeighbors(current))
            {
                if (IsWithin(neighbor, width, height) &&
                    !blocked.Contains(neighbor) &&
                    exterior.Add(neighbor))
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        var interior = new HashSet<GridPosition>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var position = new GridPosition(x, y);
                if (!blocked.Contains(position) && !exterior.Contains(position))
                {
                    interior.Add(position);
                }
            }
        }

        var sides = walls.ToDictionary(
            position => position,
            position => GetWallSides(position, walls, solids, interior));
        return new WallEnclosureAnalysis(interior, sides);
    }

    private static WallRenderSides GetWallSides(
        GridPosition wall,
        IReadOnlySet<GridPosition> walls,
        IReadOnlySet<GridPosition> solids,
        IReadOnlySet<GridPosition> interior)
    {
        var connections = GetCardinalMask(wall, walls);
        var roomSides = GetInteriorFacing(wall, interior);
        var coveredSides = GetCardinalMask(wall, solids);
        var candidateFaces = roomSides != WallInteriorFacing.None
            ? roomSides
            : coveredSides != WallInteriorFacing.None
                ? GetNeutralFaces(connections)
                : WallInteriorFacing.None;
        return new WallRenderSides(
            connections,
            roomSides,
            coveredSides,
            candidateFaces & ~coveredSides);
    }

    internal static WallInteriorFacing GetNeutralFaces(WallInteriorFacing connections)
    {
        var count = CountBits(connections);
        if (count == 0)
        {
            return WallInteriorFacing.North | WallInteriorFacing.East |
                WallInteriorFacing.South | WallInteriorFacing.West;
        }
        if (count == 1)
        {
            return (connections & (WallInteriorFacing.East | WallInteriorFacing.West)) != 0
                ? WallInteriorFacing.North | WallInteriorFacing.South
                : WallInteriorFacing.East | WallInteriorFacing.West;
        }
        if (count == 2)
        {
            if (connections == (WallInteriorFacing.East | WallInteriorFacing.West))
            {
                return WallInteriorFacing.North | WallInteriorFacing.South;
            }
            if (connections == (WallInteriorFacing.North | WallInteriorFacing.South))
            {
                return WallInteriorFacing.East | WallInteriorFacing.West;
            }
            return connections;
        }
        if (count == 3)
        {
            const WallInteriorFacing all = WallInteriorFacing.North |
                WallInteriorFacing.East | WallInteriorFacing.South |
                WallInteriorFacing.West;
            return all & ~connections;
        }
        return WallInteriorFacing.None;
    }

    private static WallInteriorFacing GetCardinalMask(
        GridPosition position,
        IReadOnlySet<GridPosition> cells)
    {
        var mask = WallInteriorFacing.None;
        if (cells.Contains(position with { Y = position.Y - 1 })) mask |= WallInteriorFacing.North;
        if (cells.Contains(position with { X = position.X + 1 })) mask |= WallInteriorFacing.East;
        if (cells.Contains(position with { Y = position.Y + 1 })) mask |= WallInteriorFacing.South;
        if (cells.Contains(position with { X = position.X - 1 })) mask |= WallInteriorFacing.West;
        return mask;
    }

    private static int CountBits(WallInteriorFacing value)
    {
        var bits = (byte)value;
        var count = 0;
        while (bits != 0)
        {
            count += bits & 1;
            bits >>= 1;
        }
        return count;
    }

    private static WallInteriorFacing GetInteriorFacing(
        GridPosition wall,
        IReadOnlySet<GridPosition> interior)
    {
        var facing = WallInteriorFacing.None;
        if (interior.Contains(wall with { Y = wall.Y - 1 })) facing |= WallInteriorFacing.North;
        if (interior.Contains(wall with { X = wall.X + 1 })) facing |= WallInteriorFacing.East;
        if (interior.Contains(wall with { Y = wall.Y + 1 })) facing |= WallInteriorFacing.South;
        if (interior.Contains(wall with { X = wall.X - 1 })) facing |= WallInteriorFacing.West;
        if (facing != WallInteriorFacing.None)
        {
            return facing;
        }

        if (interior.Contains(wall with { X = wall.X - 1, Y = wall.Y - 1 }))
        {
            facing |= WallInteriorFacing.North | WallInteriorFacing.West;
        }
        if (interior.Contains(wall with { X = wall.X + 1, Y = wall.Y - 1 }))
        {
            facing |= WallInteriorFacing.North | WallInteriorFacing.East;
        }
        if (interior.Contains(wall with { X = wall.X + 1, Y = wall.Y + 1 }))
        {
            facing |= WallInteriorFacing.South | WallInteriorFacing.East;
        }
        if (interior.Contains(wall with { X = wall.X - 1, Y = wall.Y + 1 }))
        {
            facing |= WallInteriorFacing.South | WallInteriorFacing.West;
        }
        return facing;
    }

    private static void EnqueueExterior(
        GridPosition position,
        IReadOnlySet<GridPosition> blocked,
        ISet<GridPosition> exterior,
        Queue<GridPosition> queue)
    {
        if (!blocked.Contains(position) && exterior.Add(position))
        {
            queue.Enqueue(position);
        }
    }

    private static IEnumerable<GridPosition> GetCardinalNeighbors(GridPosition position)
    {
        yield return position with { Y = position.Y - 1 };
        yield return position with { X = position.X + 1 };
        yield return position with { Y = position.Y + 1 };
        yield return position with { X = position.X - 1 };
    }

    private static bool IsWithin(GridPosition position, int width, int height) =>
        position.X >= 0 && position.X < width &&
        position.Y >= 0 && position.Y < height;
}
