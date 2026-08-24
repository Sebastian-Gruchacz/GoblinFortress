namespace GoblinStronghold.Simulation.Map;

internal static class GeneratedSettlementStructureGenerator
{
    public static IReadOnlyList<WorldObjectSnapshot> Generate(GeneratedMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var objects = new List<WorldObjectSnapshot>();
        var reservedCells = new HashSet<GridPosition>
        {
            map.GoblinSpawn,
            map.HumanVillage,
        };
        ReserveSettlementAccess(map, reservedCells);
        ulong nextId = 1;

        AddBuilding(
            objects,
            reservedCells,
            map,
            new WorldObjectId(nextId++),
            WorldObjectKind.HumanCottage,
            WorldObjectOwner.HumanVillage,
            map.HumanVillage,
            width: 4,
            height: 3);
        AddBuilding(
            objects,
            reservedCells,
            map,
            new WorldObjectId(nextId++),
            WorldObjectKind.HumanCottage,
            WorldObjectOwner.HumanVillage,
            map.HumanVillage,
            width: 4,
            height: 3);
        AddBuilding(
            objects,
            reservedCells,
            map,
            new WorldObjectId(nextId++),
            WorldObjectKind.HumanBarn,
            WorldObjectOwner.HumanVillage,
            map.HumanVillage,
            width: 5,
            height: 4);
        AddWell(
            objects,
            reservedCells,
            map,
            new WorldObjectId(nextId++),
            map.HumanVillage);

        var hutCount = 2 + DeterministicRandom.NextInt(
            map.Seed,
            RandomDomain.MapGeneration,
            EntityId.None,
            SimulationTick.Zero,
            sampleKey: 30_001,
            minimumInclusive: 0,
            maximumExclusive: 2);
        for (var index = 0; index < hutCount; index++)
        {
            AddBuilding(
                objects,
                reservedCells,
                map,
                new WorldObjectId(nextId++),
                WorldObjectKind.GoblinHut,
                WorldObjectOwner.GoblinTribe,
                map.GoblinSpawn,
                width: 3,
                height: 3);
        }

        return objects;
    }

    private static void ReserveSettlementAccess(
        GeneratedMap map,
        HashSet<GridPosition> reservedCells)
    {
        var xStep = map.GoblinSpawn.X <= map.HumanVillage.X ? 1 : -1;
        for (var x = map.GoblinSpawn.X;
             x != map.HumanVillage.X + xStep;
             x += xStep)
        {
            reservedCells.Add(new GridPosition(x, map.GoblinSpawn.Y));
        }

        var yStep = map.GoblinSpawn.Y <= map.HumanVillage.Y ? 1 : -1;
        for (var y = map.GoblinSpawn.Y;
             y != map.HumanVillage.Y + yStep;
             y += yStep)
        {
            reservedCells.Add(new GridPosition(map.HumanVillage.X, y));
        }
    }

    private static void AddBuilding(
        List<WorldObjectSnapshot> objects,
        HashSet<GridPosition> reservedCells,
        GeneratedMap map,
        WorldObjectId id,
        WorldObjectKind kind,
        WorldObjectOwner owner,
        GridPosition settlementCenter,
        int width,
        int height)
    {
        var orientation = GetOrientation(map.Seed, id);
        if (orientation is CardinalOrientation.East or CardinalOrientation.West)
        {
            (width, height) = (height, width);
        }

        var anchor = FindPlacement(map, settlementCenter, width, height, reservedCells);
        var parts = CreateBuildingParts(width, height, orientation);
        var worldObject = new WorldObjectSnapshot(id, kind, owner, anchor, orientation, parts);
        ReserveFootprint(worldObject, reservedCells);
        objects.Add(worldObject);
    }

    private static void AddWell(
        List<WorldObjectSnapshot> objects,
        HashSet<GridPosition> reservedCells,
        GeneratedMap map,
        WorldObjectId id,
        GridPosition settlementCenter)
    {
        const int width = 2;
        const int height = 2;
        var anchor = FindPlacement(map, settlementCenter, width, height, reservedCells);
        var parts = new List<WorldObjectPartSnapshot>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                parts.Add(new WorldObjectPartSnapshot(
                    new GridPosition(x, y, -1),
                    SpatialOccupancyChannel.Subsurface,
                    WorldObjectPartKind.WellShaft));
                parts.Add(new WorldObjectPartSnapshot(
                    new GridPosition(x, y, 0),
                    SpatialOccupancyChannel.Solid,
                    WorldObjectPartKind.WellRim));
            }
        }

        var worldObject = new WorldObjectSnapshot(
            id,
            WorldObjectKind.HumanWell,
            WorldObjectOwner.HumanVillage,
            anchor,
            CardinalOrientation.North,
            parts);
        ReserveFootprint(worldObject, reservedCells);
        objects.Add(worldObject);
    }

    private static IReadOnlyList<WorldObjectPartSnapshot> CreateBuildingParts(
        int width,
        int height,
        CardinalOrientation orientation)
    {
        var parts = new List<WorldObjectPartSnapshot>();
        var door = orientation switch
        {
            CardinalOrientation.North => new GridPosition(width / 2, 0),
            CardinalOrientation.East => new GridPosition(width - 1, height / 2),
            CardinalOrientation.South => new GridPosition(width / 2, height - 1),
            CardinalOrientation.West => new GridPosition(0, height / 2),
            _ => throw new ArgumentOutOfRangeException(nameof(orientation)),
        };

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var position = new GridPosition(x, y, 0);
                parts.Add(new WorldObjectPartSnapshot(
                    position,
                    SpatialOccupancyChannel.Surface,
                    WorldObjectPartKind.Floor));
                parts.Add(new WorldObjectPartSnapshot(
                    position with { Z = 1 },
                    SpatialOccupancyChannel.Overhead,
                    WorldObjectPartKind.Roof));

                var isPerimeter = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                if (!isPerimeter)
                {
                    continue;
                }

                parts.Add(new WorldObjectPartSnapshot(
                    position,
                    SpatialOccupancyChannel.Solid,
                    position == door ? WorldObjectPartKind.Door : WorldObjectPartKind.Wall));
            }
        }

        return parts;
    }

    private static GridPosition FindPlacement(
        GeneratedMap map,
        GridPosition center,
        int width,
        int height,
        HashSet<GridPosition> reservedCells)
    {
        var candidates = new List<(GridPosition Position, int Distance)>();
        for (var y = 0; y <= map.Height - height; y++)
        {
            for (var x = 0; x <= map.Width - width; x++)
            {
                var candidate = new GridPosition(x, y);
                var centerX = x + (width / 2);
                var centerY = y + (height / 2);
                var distance = Math.Abs(centerX - center.X) + Math.Abs(centerY - center.Y);
                candidates.Add((candidate, distance));
            }
        }

        foreach (var candidate in candidates
                     .OrderBy(item => item.Distance)
                     .ThenBy(item => item.Position.Y)
                     .ThenBy(item => item.Position.X))
        {
            if (CanPlace(map, candidate.Position, width, height, reservedCells))
            {
                return candidate.Position;
            }
        }

        throw new InvalidOperationException("The generated settlement structures do not fit on the map.");
    }

    private static bool CanPlace(
        GeneratedMap map,
        GridPosition anchor,
        int width,
        int height,
        HashSet<GridPosition> reservedCells)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var position = new GridPosition(anchor.X + x, anchor.Y + y);
                if (reservedCells.Contains(position) || !map.GetCell(position).IsTraversable)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void ReserveFootprint(
        WorldObjectSnapshot worldObject,
        HashSet<GridPosition> reservedCells)
    {
        foreach (var (position, _) in worldObject.GetAbsoluteParts())
        {
            reservedCells.Add(position with { Z = 0 });
        }
    }

    private static CardinalOrientation GetOrientation(WorldSeed seed, WorldObjectId id) =>
        (CardinalOrientation)DeterministicRandom.NextInt(
            seed,
            RandomDomain.MapGeneration,
            new EntityId(id.Value),
            SimulationTick.Zero,
            sampleKey: 30_002,
            minimumInclusive: (int)CardinalOrientation.North,
            maximumExclusive: (int)CardinalOrientation.West + 1);
}
