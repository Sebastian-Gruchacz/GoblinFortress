using GoblinStronghold.Simulation.Map.Generation;

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
        foreach (var entrance in map.CaveEntrances)
        {
            reservedCells.Add(entrance);
        }
        foreach (var neighbor in map.GetCardinalNeighbors(map.HumanVillage))
        {
            reservedCells.Add(neighbor);
        }

        if (Math.Min(map.Width, map.Height) >= 32)
        {
            ReserveSettlementAccess(map, reservedCells);
        }
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

        if (map.GeneratorVersion >= 16)
        {
            var ruinObjects = GoblinStarterRuinPlanner.Generate(map, reservedCells, nextId);
            objects.AddRange(ruinObjects);
            nextId = checked(nextId + (ulong)ruinObjects.Count);
            foreach (var worldObject in ruinObjects)
            {
                ReserveFootprint(worldObject, reservedCells);
            }
        }
        else
        {
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
        }

        if (map.GeneratorVersion >= 4)
        {
            AddNaturalVegetation(objects, reservedCells, map, ref nextId);
            AddNaturalStone(objects, reservedCells, map, ref nextId);
        }

        return objects;
    }

    private static void AddNaturalStone(
        List<WorldObjectSnapshot> objects,
        HashSet<GridPosition> reservedCells,
        GeneratedMap map,
        ref ulong nextId)
    {
        for (var y = 1; y < map.Height - 1; y++)
        {
            for (var x = 1; x < map.Width - 1; x++)
            {
                var position = new GridPosition(x, y);
                if (!CanPlaceBoulder(map, position, reservedCells) ||
                    SamplePercent(map, position, sampleKey: 32_001) >= 2)
                {
                    continue;
                }

                AddBoulder(objects, reservedCells, new WorldObjectId(nextId++), position);
            }
        }

        if (objects.Any(item => item.Kind == WorldObjectKind.Boulder &&
                Distance(item.Anchor, map.GoblinSpawn) <= 7d))
        {
            return;
        }

        var fallback = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width)
                .Select(x => new GridPosition(x, y)))
            .Where(position =>
                Distance(position, map.GoblinSpawn) is >= 3d and <= 7d &&
                CanPlaceBoulder(map, position, reservedCells))
            .OrderBy(position => Distance(position, map.GoblinSpawn))
            .ThenBy(position => position.Y)
            .ThenBy(position => position.X)
            .FirstOrDefault();
        if (fallback != default)
        {
            AddBoulder(objects, reservedCells, new WorldObjectId(nextId++), fallback);
        }
    }

    private static bool CanPlaceBoulder(
        GeneratedMap map,
        GridPosition position,
        HashSet<GridPosition> reservedCells)
    {
        if (!map.IsWithin(position) || reservedCells.Contains(position))
        {
            return false;
        }

        var cell = map.GetCell(position);
        return cell.Terrain is TerrainKind.SolidGround or TerrainKind.Mud &&
            cell.SurfaceRoute == SurfaceRouteKind.None &&
            cell.RampDirection == TerrainRampDirection.None &&
            map.GetCardinalNeighbors(position).Any(neighbor =>
                map.GetCell(neighbor).IsTraversable && !reservedCells.Contains(neighbor));
    }

    private static void AddBoulder(
        List<WorldObjectSnapshot> objects,
        HashSet<GridPosition> reservedCells,
        WorldObjectId id,
        GridPosition anchor)
    {
        var boulder = new WorldObjectSnapshot(
            id,
            WorldObjectKind.Boulder,
            WorldObjectOwner.Nature,
            anchor,
            CardinalOrientation.North,
            [new(default, SpatialOccupancyChannel.Solid, WorldObjectPartKind.Boulder)]);
        ReserveFootprint(boulder, reservedCells);
        objects.Add(boulder);
    }

    private static void AddNaturalVegetation(
        List<WorldObjectSnapshot> objects,
        HashSet<GridPosition> reservedCells,
        GeneratedMap map,
        ref ulong nextId)
    {
        var forestOuterRadius = Math.Max(10d, Math.Min(map.Width, map.Height) * 0.38d);
        var forestInnerRadius = Math.Max(5d, Math.Min(map.Width, map.Height) * 0.1d);
        for (var y = 1; y < map.Height - 1; y++)
        {
            for (var x = 1; x < map.Width - 1; x++)
            {
                var position = new GridPosition(x, y);
                var distance = Distance(position, map.HumanVillage);
                var outsideForestRegion = map.GeneratorVersion < 7 &&
                    (x < map.Width * 0.42d || y > map.Height * 0.62d);
                if (outsideForestRegion ||
                    distance < forestInnerRadius || distance > forestOuterRadius ||
                    !CanPlaceTree(map, position, reservedCells) ||
                    SamplePercent(map, position, sampleKey: 31_001) >= 78)
                {
                    continue;
                }

                AddTree(objects, reservedCells, map, new WorldObjectId(nextId++), position);
            }
        }

        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var position = new GridPosition(x, y);
                var inSwampRegion = map.GeneratorVersion >= 7 ||
                    x <= map.Width * 0.42d || y >= map.Height * 0.64d;
                if (!inSwampRegion || reservedCells.Contains(position) ||
                    map.GetCell(position).Terrain != TerrainKind.Mud ||
                    map.GetCell(position).SurfaceRoute != SurfaceRouteKind.None ||
                    map.GetCell(position).RampDirection != TerrainRampDirection.None ||
                    SamplePercent(map, position, sampleKey: 31_002) >= 5)
                {
                    continue;
                }

                AddStump(objects, reservedCells, new WorldObjectId(nextId++), position);
            }
        }
    }

    private static bool CanPlaceTree(
        GeneratedMap map,
        GridPosition anchor,
        HashSet<GridPosition> reservedCells)
    {
        var anchorCell = map.GetCell(anchor);
        for (var y = -1; y <= 1; y++)
        {
            for (var x = -1; x <= 1; x++)
            {
                var position = new GridPosition(anchor.X + x, anchor.Y + y);
                if (!map.IsWithin(position) || reservedCells.Contains(position) ||
                    map.GetCell(position).Terrain != TerrainKind.SolidGround ||
                    map.GetCell(position).SurfaceRoute != SurfaceRouteKind.None ||
                    map.GetCell(position).SurfaceLevel != anchorCell.SurfaceLevel ||
                    map.GetCell(position).RampDirection != TerrainRampDirection.None)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void AddTree(
        List<WorldObjectSnapshot> objects,
        HashSet<GridPosition> reservedCells,
        GeneratedMap map,
        WorldObjectId id,
        GridPosition anchor)
    {
        var trunkHeight = 1 + (SamplePercent(map, anchor, sampleKey: 31_003) % 3);
        var parts = new List<WorldObjectPartSnapshot>();
        for (var z = 0; z < trunkHeight; z++)
        {
            parts.Add(new WorldObjectPartSnapshot(
                new GridPosition(0, 0, z),
                SpatialOccupancyChannel.Solid,
                WorldObjectPartKind.TreeTrunk));
        }
        for (var y = -1; y <= 1; y++)
        {
            for (var x = -1; x <= 1; x++)
            {
                parts.Add(new WorldObjectPartSnapshot(
                    new GridPosition(x, y, trunkHeight),
                    SpatialOccupancyChannel.Overhead,
                    WorldObjectPartKind.TreeCrown));
            }
        }

        var tree = new WorldObjectSnapshot(
            id,
            WorldObjectKind.Tree,
            WorldObjectOwner.Nature,
            anchor,
            CardinalOrientation.North,
            parts);
        ReserveFootprint(tree, reservedCells);
        objects.Add(tree);
    }

    private static void AddStump(
        List<WorldObjectSnapshot> objects,
        HashSet<GridPosition> reservedCells,
        WorldObjectId id,
        GridPosition anchor)
    {
        var stump = new WorldObjectSnapshot(
            id,
            WorldObjectKind.DeadTreeStump,
            WorldObjectOwner.Nature,
            anchor,
            CardinalOrientation.North,
            [new(default, SpatialOccupancyChannel.Solid, WorldObjectPartKind.TreeStump)]);
        ReserveFootprint(stump, reservedCells);
        objects.Add(stump);
    }

    private static int SamplePercent(GeneratedMap map, GridPosition position, ulong sampleKey)
    {
        var subject = new EntityId(checked((ulong)((position.Y * map.Width) + position.X) + 1));
        return DeterministicRandom.NextInt(
            map.Seed,
            RandomDomain.MapGeneration,
            subject,
            SimulationTick.Zero,
            sampleKey,
            minimumInclusive: 0,
            maximumExclusive: 100);
    }

    private static double Distance(GridPosition first, GridPosition second)
    {
        var deltaX = first.X - second.X;
        var deltaY = first.Y - second.Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
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

        var anchor = FindPlacement(map, settlementCenter, width, height, reservedCells, owner);
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
        var anchor = FindPlacement(
            map,
            settlementCenter,
            width,
            height,
            reservedCells,
            WorldObjectOwner.HumanVillage);
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
        HashSet<GridPosition> reservedCells,
        WorldObjectOwner owner)
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
            if (CanPlace(map, candidate.Position, width, height, reservedCells, owner))
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
        HashSet<GridPosition> reservedCells,
        WorldObjectOwner owner)
    {
        sbyte? footprintLevel = null;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var position = new GridPosition(anchor.X + x, anchor.Y + y);
                var cell = map.GetCell(position);
                if (reservedCells.Contains(position) ||
                    !cell.IsTraversable ||
                    cell.Terrain is TerrainKind.ShallowWater or TerrainKind.DeepWater ||
                    cell.SurfaceRoute != SurfaceRouteKind.None ||
                    cell.RampDirection != TerrainRampDirection.None ||
                    (owner == WorldObjectOwner.HumanVillage &&
                        Math.Min(map.Width, map.Height) >= 32 &&
                        cell.Terrain != TerrainKind.SolidGround) ||
                    (footprintLevel is not null && cell.SurfaceLevel != footprintLevel.Value))
                {
                    return false;
                }

                footprintLevel ??= cell.SurfaceLevel;
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
