using System.Collections.ObjectModel;

namespace GoblinStronghold.Simulation.Map;

public enum PlantKind : byte
{
    BerryBush = 1,
}

public enum WorldChangeKind : byte
{
    VegetationHarvested = 1,
    VegetationRegrown = 2,
}

public readonly record struct PlantPatchSnapshot(
    GridPosition Position,
    PlantKind Kind,
    int Biomass,
    int Capacity);

public readonly record struct WorldChangeEvent(
    ulong Version,
    SimulationTick Tick,
    WorldChangeKind Kind,
    GridPosition Position,
    int Amount);

public sealed class WorldMapState
{
    private readonly SortedDictionary<int, PlantPatchState> _plantPatches;
    private readonly SortedDictionary<WorldObjectId, WorldObjectSnapshot> _worldObjects;
    private readonly Dictionary<SpatialOccupancyKey, SpatialOccupancyClaim> _occupancy;

    private WorldMapState(
        GeneratedMap baseline,
        ulong version,
        SortedDictionary<int, PlantPatchState> plantPatches,
        SortedDictionary<WorldObjectId, WorldObjectSnapshot> worldObjects,
        Dictionary<SpatialOccupancyKey, SpatialOccupancyClaim> occupancy)
    {
        Baseline = baseline;
        Version = version;
        _plantPatches = plantPatches;
        _worldObjects = worldObjects;
        _occupancy = occupancy;
    }

    public GeneratedMap Baseline { get; }

    public ulong Version { get; private set; }

    public int PlantPatchCount => _plantPatches.Count;

    public int WorldObjectCount => _worldObjects.Count;

    internal static WorldMapState CreateInitial(GeneratedMap baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        var generatedObjects = GeneratedSettlementStructureGenerator.Generate(baseline);
        var (worldObjects, occupancy) = ValidateAndIndexObjects(baseline, generatedObjects);
        var occupiedColumns = worldObjects.Values
            .SelectMany(worldObject => worldObject.GetAbsoluteParts())
            .Where(item => item.Position.Z == 0)
            .Select(item => item.Position)
            .ToHashSet();
        var patches = new SortedDictionary<int, PlantPatchState>();
        for (var y = 0; y < baseline.Height; y++)
        {
            for (var x = 0; x < baseline.Width; x++)
            {
                var position = new GridPosition(x, y);
                var cell = baseline.GetCell(position);
                if (!cell.IsTraversable ||
                    cell.Fertility < 35 ||
                    cell.Moisture < 30 ||
                    occupiedColumns.Contains(position))
                {
                    continue;
                }

                var index = GetIndex(baseline, position);
                var subject = new EntityId(checked((ulong)index + 1));
                var occurrence = DeterministicRandom.NextInt(
                    baseline.Seed,
                    RandomDomain.Ecology,
                    subject,
                    SimulationTick.Zero,
                    sampleKey: 1,
                    minimumInclusive: 0,
                    maximumExclusive: 100);

                if (occurrence >= 22 &&
                    position != baseline.GoblinSpawn &&
                    position != baseline.HumanVillage)
                {
                    continue;
                }

                var capacity = Math.Max(12, 8 + (cell.Fertility / 3));
                patches.Add(index, new PlantPatchState(position, PlantKind.BerryBush, capacity, capacity));
            }
        }

        EnsurePatch(patches, baseline, baseline.GoblinSpawn);
        EnsurePatch(patches, baseline, baseline.HumanVillage);
        return new WorldMapState(baseline, version: 0, patches, worldObjects, occupancy);
    }

    internal static WorldMapState Restore(
        GeneratedMap baseline,
        ulong version,
        IEnumerable<PlantPatchSnapshot> plantPatches,
        IEnumerable<WorldObjectSnapshot> worldObjects)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(plantPatches);
        ArgumentNullException.ThrowIfNull(worldObjects);

        var restored = new SortedDictionary<int, PlantPatchState>();
        foreach (var patch in plantPatches)
        {
            if (!baseline.IsWithin(patch.Position) ||
                !baseline.GetCell(patch.Position).IsTraversable ||
                !Enum.IsDefined(patch.Kind) ||
                patch.Capacity <= 0 ||
                patch.Biomass < 0 ||
                patch.Biomass > patch.Capacity)
            {
                throw new InvalidDataException("The save contains an invalid plant patch.");
            }

            var index = GetIndex(baseline, patch.Position);
            if (!restored.TryAdd(
                    index,
                    new PlantPatchState(patch.Position, patch.Kind, patch.Biomass, patch.Capacity)))
            {
                throw new InvalidDataException("The save contains duplicate plant patches.");
            }
        }

        var (restoredObjects, occupancy) = ValidateAndIndexObjects(baseline, worldObjects);
        return new WorldMapState(baseline, version, restored, restoredObjects, occupancy);
    }

    public PlantPatchSnapshot? GetPlantPatch(GridPosition position)
    {
        if (!Baseline.IsWithin(position) ||
            !_plantPatches.TryGetValue(GetIndex(Baseline, position), out var patch))
        {
            return null;
        }

        return patch.ToSnapshot();
    }

    public IReadOnlyList<PlantPatchSnapshot> CreatePlantSnapshot() =>
        new ReadOnlyCollection<PlantPatchSnapshot>(
            _plantPatches.Values.Select(patch => patch.ToSnapshot()).ToArray());

    public IReadOnlyList<WorldObjectSnapshot> CreateWorldObjectSnapshot() =>
        new ReadOnlyCollection<WorldObjectSnapshot>(_worldObjects.Values.ToArray());

    public IReadOnlyList<WorldObjectSnapshot> GetWorldObjectsAt(GridPosition position)
    {
        var ids = _occupancy
            .Where(item => item.Key.Position == position)
            .Select(item => item.Value.ObjectId)
            .Distinct()
            .Order()
            .ToArray();
        return new ReadOnlyCollection<WorldObjectSnapshot>(
            ids.Select(id => _worldObjects[id]).ToArray());
    }

    public bool IsSurfaceTraversable(GridPosition position)
    {
        if (!Baseline.IsWithin(position) || !Baseline.GetCell(position).IsTraversable)
        {
            return false;
        }

        return !_occupancy.TryGetValue(
                   new SpatialOccupancyKey(position, SpatialOccupancyChannel.Solid),
                   out var claim) ||
               claim.PartKind == WorldObjectPartKind.Door;
    }

    public bool HasSurfacePath(GridPosition start, GridPosition destination)
        => FindSurfacePath(start, destination) is not null;

    public IReadOnlyList<GridPosition>? FindSurfacePath(GridPosition start, GridPosition destination)
    {
        if (!IsSurfaceTraversable(start) || !IsSurfaceTraversable(destination))
        {
            return null;
        }

        var visited = new bool[Baseline.CellCount];
        var predecessors = new GridPosition?[Baseline.CellCount];
        var queue = new Queue<GridPosition>();
        visited[GetIndex(Baseline, start)] = true;
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == destination)
            {
                return BuildRoute(start, current, predecessors);
            }

            foreach (var neighbor in Baseline.GetCardinalNeighbors(current))
            {
                var index = GetIndex(Baseline, neighbor);
                if (visited[index] || !IsSurfaceTraversable(neighbor))
                {
                    continue;
                }

                visited[index] = true;
                predecessors[index] = current;
                queue.Enqueue(neighbor);
            }
        }

        return null;
    }

    public IReadOnlyList<GridPosition>? FindNearestHarvestablePlantPath(
        GridPosition start,
        ISet<GridPosition> excludedTargets)
    {
        ArgumentNullException.ThrowIfNull(excludedTargets);
        if (!IsSurfaceTraversable(start))
        {
            return null;
        }

        var visited = new bool[Baseline.CellCount];
        var predecessors = new GridPosition?[Baseline.CellCount];
        var queue = new Queue<GridPosition>();
        visited[GetIndex(Baseline, start)] = true;
        queue.Enqueue(start);

        while (queue.TryDequeue(out var current))
        {
            if (!excludedTargets.Contains(current) &&
                _plantPatches.TryGetValue(GetIndex(Baseline, current), out var patch) &&
                patch.Biomass > 0)
            {
                return BuildRoute(start, current, predecessors);
            }

            foreach (var neighbor in Baseline.GetCardinalNeighbors(current))
            {
                var index = GetIndex(Baseline, neighbor);
                if (visited[index] || !IsSurfaceTraversable(neighbor))
                {
                    continue;
                }

                visited[index] = true;
                predecessors[index] = current;
                queue.Enqueue(neighbor);
            }
        }

        return null;
    }

    internal bool TryHarvest(
        GridPosition position,
        int requestedAmount,
        SimulationTick tick,
        out int harvested,
        out WorldChangeEvent change)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedAmount);

        if (!Baseline.IsWithin(position) ||
            !_plantPatches.TryGetValue(GetIndex(Baseline, position), out var patch) ||
            patch.Biomass == 0)
        {
            harvested = 0;
            change = default;
            return false;
        }

        harvested = Math.Min(requestedAmount, patch.Biomass);
        patch.Biomass -= harvested;
        change = CreateChange(tick, WorldChangeKind.VegetationHarvested, position, -harvested);
        return true;
    }

    internal IReadOnlyList<WorldChangeEvent> GrowPlants(
        SimulationTick tick,
        int growthPerPatch)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(growthPerPatch);

        var changes = new List<WorldChangeEvent>();
        foreach (var patch in _plantPatches.Values)
        {
            var grown = Math.Min(growthPerPatch, patch.Capacity - patch.Biomass);
            if (grown == 0)
            {
                continue;
            }

            patch.Biomass += grown;
            changes.Add(CreateChange(
                tick,
                WorldChangeKind.VegetationRegrown,
                patch.Position,
                grown));
        }

        return changes;
    }

    private static void EnsurePatch(
        SortedDictionary<int, PlantPatchState> patches,
        GeneratedMap baseline,
        GridPosition position)
    {
        var index = GetIndex(baseline, position);
        if (patches.ContainsKey(index))
        {
            return;
        }

        var cell = baseline.GetCell(position);
        var capacity = Math.Max(12, 8 + (cell.Fertility / 3));
        patches.Add(index, new PlantPatchState(position, PlantKind.BerryBush, capacity, capacity));
    }

    private WorldChangeEvent CreateChange(
        SimulationTick tick,
        WorldChangeKind kind,
        GridPosition position,
        int amount)
    {
        Version = checked(Version + 1);
        return new WorldChangeEvent(Version, tick, kind, position, amount);
    }

    private static (
        SortedDictionary<WorldObjectId, WorldObjectSnapshot> Objects,
        Dictionary<SpatialOccupancyKey, SpatialOccupancyClaim> Occupancy)
        ValidateAndIndexObjects(
            GeneratedMap baseline,
            IEnumerable<WorldObjectSnapshot> worldObjects)
    {
        var restored = new SortedDictionary<WorldObjectId, WorldObjectSnapshot>();
        var occupancy = new Dictionary<SpatialOccupancyKey, SpatialOccupancyClaim>();

        foreach (var worldObject in worldObjects.OrderBy(item => item.Id))
        {
            if (worldObject.Id == WorldObjectId.None ||
                !Enum.IsDefined(worldObject.Kind) ||
                !Enum.IsDefined(worldObject.Owner) ||
                !Enum.IsDefined(worldObject.Orientation) ||
                worldObject.Anchor.Z != 0 ||
                worldObject.Parts.Count == 0 ||
                !restored.TryAdd(worldObject.Id, worldObject))
            {
                throw new InvalidDataException("The world contains an invalid spatial object.");
            }

            foreach (var (position, part) in worldObject.GetAbsoluteParts())
            {
                if (!baseline.IsWithin(position with { Z = 0 }) ||
                    position.Z is < -16 or > 16 ||
                    !Enum.IsDefined(part.Channel) ||
                    !Enum.IsDefined(part.Kind))
                {
                    throw new InvalidDataException("A spatial object has an invalid part.");
                }

                var key = new SpatialOccupancyKey(position, part.Channel);
                if (!occupancy.TryAdd(
                        key,
                        new SpatialOccupancyClaim(worldObject.Id, part.Kind)))
                {
                    throw new InvalidDataException("Spatial object parts conflict in one occupancy channel.");
                }
            }
        }

        return (restored, occupancy);
    }

    private static int GetIndex(GeneratedMap map, GridPosition position) =>
        checked((position.Y * map.Width) + position.X);

    private IReadOnlyList<GridPosition> BuildRoute(
        GridPosition start,
        GridPosition destination,
        IReadOnlyList<GridPosition?> predecessors)
    {
        var route = new List<GridPosition>();
        var current = destination;
        while (current != start)
        {
            route.Add(current);
            current = predecessors[GetIndex(Baseline, current)]
                ?? throw new InvalidOperationException("Surface path is missing a predecessor.");
        }

        route.Reverse();
        return route;
    }

    private sealed class PlantPatchState(
        GridPosition position,
        PlantKind kind,
        int biomass,
        int capacity)
    {
        public GridPosition Position { get; } = position;

        public PlantKind Kind { get; } = kind;

        public int Biomass { get; set; } = biomass;

        public int Capacity { get; } = capacity;

        public PlantPatchSnapshot ToSnapshot() => new(Position, Kind, Biomass, Capacity);
    }
}
