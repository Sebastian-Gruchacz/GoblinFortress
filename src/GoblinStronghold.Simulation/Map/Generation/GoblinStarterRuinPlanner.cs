using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation.Map.Generation;

internal static class GoblinStarterRuinPlanner
{
    private const int FullWidth = 6;
    private const int FullHeight = 5;

    public static IReadOnlyList<WorldObjectSnapshot> Generate(
        GeneratedMap map,
        IReadOnlySet<GridPosition> reservedCells,
        ulong firstObjectId)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(reservedCells);

        var dimensions = FindPlacement(map, reservedCells, FullWidth, FullHeight) is { } full
            ? (Anchor: full, Width: FullWidth, Height: FullHeight)
            : FindPlacement(map, reservedCells, width: 4, height: 3) is { } compact
                ? (Anchor: compact, Width: 4, Height: 3)
                : throw new InvalidOperationException(
                    "The goblin starter ruin does not fit on the map.");
        var anchor = dimensions.Anchor;
        var objects = new List<WorldObjectSnapshot>();
        var ruin = new WorldObjectSnapshot(
            new WorldObjectId(firstObjectId),
            WorldObjectKind.GoblinRuin,
            WorldObjectOwner.GoblinTribe,
            anchor,
            CardinalOrientation.North,
            CreateRuinParts(map.Seed, dimensions.Width, dimensions.Height));
        objects.Add(ruin);

        var workshopPosition = Add(anchor, 1, 1);
        objects.Add(new WorldObjectSnapshot(
            new WorldObjectId(checked(firstObjectId + 1)),
            WorldObjectKind.PrimitiveWorkshop,
            WorldObjectOwner.GoblinTribe,
            workshopPosition,
            CardinalOrientation.North,
            [new(default, SpatialOccupancyChannel.Fixture,
                WorldObjectPartKind.PrimitiveWorkshop)],
            ResourceVariant.PineWood));

        var compostPosition = Add(anchor, dimensions.Width - 2, dimensions.Height - 2);
        objects.Add(new WorldObjectSnapshot(
            new WorldObjectId(checked(firstObjectId + 2)),
            WorldObjectKind.GoblinCompost,
            WorldObjectOwner.GoblinTribe,
            compostPosition,
            CardinalOrientation.North,
            [new(default, SpatialOccupancyChannel.Fixture,
                WorldObjectPartKind.CompostHeap)]));

        var blockedRuinCells = ruin.GetAbsoluteParts()
            .Where(item => item.Part.Channel == SpatialOccupancyChannel.Solid)
            .Select(item => item.Position)
            .ToHashSet();
        var sleepingMatPositions = ruin.GetAbsoluteParts()
            .Where(item => item.Part.Kind == WorldObjectPartKind.Roof)
            .Select(item => item.Position with { Z = item.Position.Z - 1 })
            .Where(position => !blockedRuinCells.Contains(position) &&
                position != workshopPosition && position != compostPosition)
            .Distinct()
            .OrderBy(position => position.Y)
            .ThenBy(position => position.X)
            .Take(4);
        foreach (var sleepingMatPosition in sleepingMatPositions)
        {
            objects.Add(new WorldObjectSnapshot(
                new WorldObjectId(checked(firstObjectId + (ulong)objects.Count)),
                WorldObjectKind.ReedSleepingMat,
                WorldObjectOwner.GoblinTribe,
                sleepingMatPosition,
                CardinalOrientation.North,
                [new(default, SpatialOccupancyChannel.Fixture,
                    WorldObjectPartKind.SleepingMat)]));
        }

        if (dimensions.Width == FullWidth && dimensions.Height == FullHeight)
        {
            objects.Add(new WorldObjectSnapshot(
                new WorldObjectId(checked(firstObjectId + (ulong)objects.Count)),
                WorldObjectKind.CookingFire,
                WorldObjectOwner.GoblinTribe,
                Add(anchor, 3, 2),
                CardinalOrientation.North,
                [new(default, SpatialOccupancyChannel.Fixture,
                    WorldObjectPartKind.CookingFire)],
                ResourceVariant.PineWood));
            objects.Add(new WorldObjectSnapshot(
                new WorldObjectId(checked(firstObjectId + (ulong)objects.Count)),
                WorldObjectKind.StandingTorch,
                WorldObjectOwner.GoblinTribe,
                Add(anchor, 2, 3),
                CardinalOrientation.North,
                [new(default, SpatialOccupancyChannel.Fixture,
                    WorldObjectPartKind.StandingTorch)],
                ResourceVariant.PineWood));
        }

        foreach (var (offset, orientation) in new[]
                 {
                     (new GridPosition(1, 0), CardinalOrientation.South),
                     (new GridPosition(dimensions.Width - 2, dimensions.Height - 1),
                         CardinalOrientation.North),
                 })
        {
            objects.Add(new WorldObjectSnapshot(
                new WorldObjectId(checked(firstObjectId + (ulong)objects.Count)),
                WorldObjectKind.WallTorch,
                WorldObjectOwner.GoblinTribe,
                Add(anchor, offset.X, offset.Y),
                orientation,
                [new(default, SpatialOccupancyChannel.Fixture,
                    WorldObjectPartKind.WallTorch)],
                ResourceVariant.PineWood));
        }

        return objects;
    }

    private static IReadOnlyList<WorldObjectPartSnapshot> CreateRuinParts(
        WorldSeed seed,
        int width,
        int height)
    {
        var parts = new List<WorldObjectPartSnapshot>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var courtyard = IsCourtyard(x, y, width, height);
                if (!courtyard)
                {
                    parts.Add(new(
                        new GridPosition(x, y),
                        SpatialOccupancyChannel.Surface,
                        WorldObjectPartKind.Floor));
                }

                var roofedSleepingBay = !courtyard && y > 0 && y < height - 1 &&
                    (x == 1 || x == width - 2);
                if (roofedSleepingBay)
                {
                    parts.Add(new(
                        new GridPosition(x, y, 1),
                        SpatialOccupancyChannel.Overhead,
                        WorldObjectPartKind.Roof));
                }

                var perimeter = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                if (!perimeter || IsGap(seed, x, y, width, height))
                {
                    continue;
                }

                parts.Add(new(
                    new GridPosition(x, y),
                    SpatialOccupancyChannel.Solid,
                    IsDoor(x, y, width, height)
                        ? WorldObjectPartKind.Door
                        : WorldObjectPartKind.Wall));
            }
        }

        return parts;
    }

    private static bool IsCourtyard(int x, int y, int width, int height) =>
        x == 1 && y == 1 || x == width - 2 && y == height - 2 ||
        width == FullWidth && x is 2 or 3 && y is 2 or 3;

    private static bool IsDoor(int x, int y, int width, int height) =>
        x == width / 2 && y == height - 1;

    private static bool IsGap(WorldSeed seed, int x, int y, int width, int height)
    {
        if (IsDoor(x, y, width, height) || (x == width - 1 && y == height / 2))
        {
            return true;
        }

        if ((x == 1 && y == 0) || (x == width - 2 && y == height - 1))
        {
            return false;
        }

        var perimeterIndex = checked((y * width) + x);
        return DeterministicRandom.NextInt(
            seed,
            RandomDomain.MapGeneration,
            EntityId.None,
            SimulationTick.Zero,
            sampleKey: checked(36_000UL + (ulong)perimeterIndex),
            minimumInclusive: 0,
            maximumExclusive: 100) < 22;
    }

    private static GridPosition? FindPlacement(
        GeneratedMap map,
        IReadOnlySet<GridPosition> reservedCells,
        int width,
        int height)
    {
        for (var radius = 1; radius <= Math.Max(map.Width, map.Height); radius++)
        {
            var candidates = Enumerable.Range(0, map.Height - height + 1)
                .SelectMany(y => Enumerable.Range(0, map.Width - width + 1)
                    .Select(x => new GridPosition(x, y)))
                .Where(anchor => DistanceToFootprint(map.GoblinSpawn, anchor, width, height) <= radius)
                .OrderBy(anchor => DistanceToFootprint(map.GoblinSpawn, anchor, width, height))
                .ThenBy(anchor => anchor.Y)
                .ThenBy(anchor => anchor.X);
            foreach (var candidate in candidates)
            {
                if (CanPlace(map, candidate, reservedCells, width, height))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static bool CanPlace(
        GeneratedMap map,
        GridPosition anchor,
        IReadOnlySet<GridPosition> reservedCells,
        int width,
        int height)
    {
        sbyte? level = null;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var position = Add(anchor, x, y);
                var cell = map.GetCell(position);
                var isOpenSpawnCourtyard = position == map.GoblinSpawn &&
                    IsCourtyard(x, y, width, height) && !(x == 1 && y == 1);
                if (reservedCells.Contains(position) && !isOpenSpawnCourtyard ||
                    !cell.IsTraversable ||
                    cell.Terrain is TerrainKind.ShallowWater or TerrainKind.DeepWater ||
                    cell.SurfaceRoute != SurfaceRouteKind.None ||
                    cell.RampDirection != TerrainRampDirection.None ||
                    level is not null && cell.SurfaceLevel != level.Value)
                {
                    return false;
                }

                level ??= cell.SurfaceLevel;
            }
        }

        return true;
    }

    private static int DistanceToFootprint(
        GridPosition position,
        GridPosition anchor,
        int width,
        int height)
    {
        var nearestX = Math.Clamp(position.X, anchor.X, anchor.X + width - 1);
        var nearestY = Math.Clamp(position.Y, anchor.Y, anchor.Y + height - 1);
        return Math.Abs(position.X - nearestX) + Math.Abs(position.Y - nearestY);
    }

    private static GridPosition Add(GridPosition anchor, int x, int y) =>
        new(anchor.X + x, anchor.Y + y, anchor.Z);
}
