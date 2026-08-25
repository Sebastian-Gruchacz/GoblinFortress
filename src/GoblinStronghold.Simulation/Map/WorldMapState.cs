using System.Collections.ObjectModel;
using GoblinStronghold.Simulation;

namespace GoblinStronghold.Simulation.Map;

public enum PlantKind : byte
{
    BerryBush = 1,
    MushroomCluster = 2,
    EdibleRoots = 3,
    FishShoal = 4,
}

public enum WorldChangeKind : byte
{
    VegetationHarvested = 1,
    VegetationRegrown = 2,
    StructureBuilt = 3,
    VegetationRemoved = 4,
    TreeFelled = 5,
    StumpHarvested = 6,
    DoorToggled = 7,
    BoulderQuarried = 8,
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

    public ulong TopologyVersion { get; private set; }

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
        var waterBodySizes = MeasureWaterBodies(baseline);
        for (var y = 0; y < baseline.Height; y++)
        {
            for (var x = 0; x < baseline.Width; x++)
            {
                var position = new GridPosition(x, y);
                var cell = baseline.GetCell(position);
                if (!cell.IsTraversable ||
                    occupiedColumns.Contains(position) ||
                    position == baseline.GoblinSpawn ||
                    position == baseline.HumanVillage)
                {
                    continue;
                }

                var index = GetIndex(baseline, position);
                var subject = new EntityId(checked((ulong)index + 1));
                var kind = SelectFoodSourceKind(
                    baseline,
                    cell,
                    subject,
                    waterBodySizes[index]);
                if (kind is null)
                {
                    continue;
                }

                var capacity = GetFoodSourceCapacity(kind.Value, cell, waterBodySizes[index]);
                patches.Add(index, new PlantPatchState(position, kind.Value, capacity, capacity));
            }
        }

        EnsureBerryPatch(patches, baseline, baseline.GoblinSpawn);
        EnsureBerryPatch(patches, baseline, baseline.HumanVillage);
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
                !Enum.IsDefined(patch.Kind) ||
                !IsValidHabitat(baseline.GetCell(patch.Position), patch.Kind) ||
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

    internal IEnumerable<WorldObjectSnapshot> EnumerateWorldObjects() =>
        _worldObjects.Values;

    public int CountWorldObjects(WorldObjectKind kind, WorldObjectOwner owner) =>
        _worldObjects.Values.Count(item => item.Kind == kind && item.Owner == owner);

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

    internal WorldObjectSnapshot? GetFellableWood(GridPosition anchor) =>
        _worldObjects.Values.FirstOrDefault(worldObject =>
            worldObject.Kind is WorldObjectKind.Tree or WorldObjectKind.DeadTreeStump &&
            worldObject.Anchor == anchor);

    internal WorldObjectSnapshot? GetQuarriableBoulder(GridPosition anchor) =>
        _worldObjects.Values.FirstOrDefault(worldObject =>
            worldObject.Kind == WorldObjectKind.Boulder && worldObject.Anchor == anchor);

    public bool IsSurfaceTraversable(GridPosition position)
    {
        if (!IsSurfaceReachable(position))
        {
            return false;
        }

        return !_occupancy.TryGetValue(
                   new SpatialOccupancyKey(position, SpatialOccupancyChannel.Fixture),
                   out var fixtureClaim) ||
               fixtureClaim.PartKind != WorldObjectPartKind.ClosedDoorLeaf;
    }

    public bool IsSurfaceReachable(GridPosition position)
    {
        if (!Baseline.IsWithin(position))
        {
            return false;
        }

        var hasWalkway = _occupancy.TryGetValue(
            new SpatialOccupancyKey(position, SpatialOccupancyChannel.Surface),
            out var surfaceClaim) &&
            surfaceClaim.PartKind == WorldObjectPartKind.Walkway;
        if (!Baseline.GetCell(position).IsTraversable && !hasWalkway)
        {
            return false;
        }

        var solidIsTraversable = !_occupancy.TryGetValue(
                   new SpatialOccupancyKey(position, SpatialOccupancyChannel.Solid),
                   out var claim) ||
               claim.PartKind == WorldObjectPartKind.Door;
        var fixtureIsReachable = !_occupancy.TryGetValue(
            new SpatialOccupancyKey(position, SpatialOccupancyChannel.Fixture),
            out var fixtureClaim) ||
            fixtureClaim.PartKind is WorldObjectPartKind.OpenDoorLeaf or
                WorldObjectPartKind.ClosedDoorLeaf or
                WorldObjectPartKind.AutomaticallyOpenedDoorLeaf;
        return solidIsTraversable && fixtureIsReachable;
    }

    public bool CanBuildWalkway(IReadOnlyList<GridPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        return positions.Count > 0 && positions.All(position =>
            position.Z == 0 &&
            Baseline.IsWithin(position) &&
            !_occupancy.ContainsKey(new SpatialOccupancyKey(
                position,
                SpatialOccupancyChannel.Surface)) &&
            !_occupancy.ContainsKey(new SpatialOccupancyKey(
                position,
                SpatialOccupancyChannel.Solid)));
    }

    internal WorldChangeEvent BuildWalkway(
        IReadOnlyList<GridPosition> positions,
        SimulationTick tick)
    {
        if (!CanBuildWalkway(positions))
        {
            throw new InvalidOperationException("The walkway placement is invalid.");
        }

        var id = new WorldObjectId(_worldObjects.Count == 0
            ? 1UL
            : checked(_worldObjects.Keys.Max(item => item.Value) + 1));
        var anchor = positions[0];
        var worldObject = new WorldObjectSnapshot(
            id,
            WorldObjectKind.WoodenWalkway,
            WorldObjectOwner.GoblinTribe,
            anchor,
            CardinalOrientation.North,
            positions.Select(position => new WorldObjectPartSnapshot(
                new GridPosition(position.X - anchor.X, position.Y - anchor.Y, position.Z - anchor.Z),
                SpatialOccupancyChannel.Surface,
                WorldObjectPartKind.Walkway)));
        _worldObjects.Add(id, worldObject);
        foreach (var (position, part) in worldObject.GetAbsoluteParts())
        {
            _occupancy.Add(
                new SpatialOccupancyKey(position, part.Channel),
                new SpatialOccupancyClaim(id, part.Kind));
        }

        return CreateChange(tick, WorldChangeKind.StructureBuilt, anchor, positions.Count);
    }

    public bool CanBuildWoodenBarrier(GridPosition anchor) =>
        anchor.Z == 0 &&
        Baseline.IsWithin(anchor) &&
        Baseline.GetCell(anchor).IsTraversable &&
        !_occupancy.Keys.Any(key =>
            key.Position.X == anchor.X && key.Position.Y == anchor.Y);

    public bool CanBuildWoodenWalls(IReadOnlyList<GridPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        return positions.Count > 0 &&
            positions.Distinct().Count() == positions.Count &&
            positions.All(CanBuildWoodenBarrier);
    }

    public bool CanBuildWoodenDoorFrame(GridPosition anchor)
    {
        if (CanBuildWoodenBarrier(anchor))
        {
            return true;
        }

        var claims = _occupancy
            .Where(item =>
                item.Key.Position.X == anchor.X &&
                item.Key.Position.Y == anchor.Y)
            .ToArray();
        return anchor.Z == 0 &&
            Baseline.IsWithin(anchor) &&
            Baseline.GetCell(anchor).IsTraversable &&
            claims.Length == 1 &&
            claims[0].Key.Position == anchor &&
            claims[0].Key.Channel == SpatialOccupancyChannel.Solid &&
            claims[0].Value.PartKind == WorldObjectPartKind.Wall &&
            _worldObjects.TryGetValue(claims[0].Value.ObjectId, out var worldObject) &&
            worldObject.Kind == WorldObjectKind.WoodenWall &&
            worldObject.Anchor == anchor &&
            worldObject.Parts.Count == 1;
    }

    internal WorldChangeEvent BuildWoodenWalls(
        IReadOnlyList<GridPosition> positions,
        SimulationTick tick)
    {
        if (!CanBuildWoodenWalls(positions))
        {
            throw new InvalidOperationException("The wooden barrier placement is invalid.");
        }

        var nextId = _worldObjects.Count == 0
            ? 1UL
            : checked(_worldObjects.Keys.Max(item => item.Value) + 1);
        _ = checked(nextId + (ulong)positions.Count - 1);
        foreach (var position in positions)
        {
            var id = new WorldObjectId(nextId++);
            var worldObject = new WorldObjectSnapshot(
                id,
                WorldObjectKind.WoodenWall,
                WorldObjectOwner.GoblinTribe,
                position,
                CardinalOrientation.North,
                [new(default, SpatialOccupancyChannel.Solid, WorldObjectPartKind.Wall)]);
            _worldObjects.Add(id, worldObject);
            _occupancy.Add(
                new SpatialOccupancyKey(position, SpatialOccupancyChannel.Solid),
                new SpatialOccupancyClaim(id, WorldObjectPartKind.Wall));
            _plantPatches.Remove(GetIndex(Baseline, position));
        }

        return CreateChange(tick, WorldChangeKind.StructureBuilt, positions[0], positions.Count);
    }

    internal WorldChangeEvent BuildWoodenDoorFrame(
        GridPosition anchor,
        SimulationTick tick)
    {
        if (!CanBuildWoodenDoorFrame(anchor))
        {
            throw new InvalidOperationException("The wooden door-frame placement is invalid.");
        }

        WorldObjectId id;
        var replacesExistingWall = false;
        var preferredSide = CardinalOrientation.North;
        var existingClaim = _occupancy.GetValueOrDefault(
            new SpatialOccupancyKey(anchor, SpatialOccupancyChannel.Solid));
        if (existingClaim.PartKind == WorldObjectPartKind.Wall &&
            _worldObjects.TryGetValue(existingClaim.ObjectId, out var existingWall) &&
            existingWall.Kind == WorldObjectKind.WoodenWall)
        {
            id = existingWall.Id;
            replacesExistingWall = true;
            preferredSide = existingWall.Orientation;
        }
        else
        {
            id = new WorldObjectId(_worldObjects.Count == 0
                ? 1UL
                : checked(_worldObjects.Keys.Max(item => item.Value) + 1));
        }

        var worldObject = new WorldObjectSnapshot(
            id,
            WorldObjectKind.WoodenDoorFrame,
            WorldObjectOwner.GoblinTribe,
            anchor,
            ResolveWallMountSide(anchor, preferredSide),
            [new(default, SpatialOccupancyChannel.Solid, WorldObjectPartKind.Door)]);
        var occupancyKey = new SpatialOccupancyKey(anchor, SpatialOccupancyChannel.Solid);
        var occupancyClaim = new SpatialOccupancyClaim(id, WorldObjectPartKind.Door);
        if (replacesExistingWall)
        {
            _worldObjects[id] = worldObject;
            _occupancy[occupancyKey] = occupancyClaim;
        }
        else
        {
            _worldObjects.Add(id, worldObject);
            _occupancy.Add(occupancyKey, occupancyClaim);
        }
        _plantPatches.Remove(GetIndex(Baseline, anchor));
        return CreateChange(tick, WorldChangeKind.StructureBuilt, anchor, 1);
    }

    public bool CanBuildWoodenDoor(GridPosition anchor)
    {
        if (anchor.Z != 0 || !Baseline.IsWithin(anchor) ||
            _occupancy.ContainsKey(new SpatialOccupancyKey(
                anchor,
                SpatialOccupancyChannel.Fixture)) ||
            !_occupancy.TryGetValue(
                new SpatialOccupancyKey(anchor, SpatialOccupancyChannel.Solid),
                out var frameClaim) ||
            frameClaim.PartKind != WorldObjectPartKind.Door ||
            !_worldObjects.TryGetValue(frameClaim.ObjectId, out var frame))
        {
            return false;
        }

        return frame.Kind == WorldObjectKind.WoodenDoorFrame && frame.Anchor == anchor;
    }

    internal WorldChangeEvent BuildWoodenDoor(GridPosition anchor, SimulationTick tick)
    {
        if (!CanBuildWoodenDoor(anchor))
        {
            throw new InvalidOperationException("The wooden door placement is invalid.");
        }

        var id = new WorldObjectId(checked(_worldObjects.Keys.Max(item => item.Value) + 1));
        var frameClaim = _occupancy[
            new SpatialOccupancyKey(anchor, SpatialOccupancyChannel.Solid)];
        var frame = _worldObjects[frameClaim.ObjectId];
        var worldObject = new WorldObjectSnapshot(
            id,
            WorldObjectKind.WoodenDoorLeaf,
            WorldObjectOwner.GoblinTribe,
            anchor,
            frame.Orientation,
            [new(default, SpatialOccupancyChannel.Fixture, WorldObjectPartKind.ClosedDoorLeaf)]);
        _worldObjects.Add(id, worldObject);
        _occupancy.Add(
            new SpatialOccupancyKey(anchor, SpatialOccupancyChannel.Fixture),
            new SpatialOccupancyClaim(id, WorldObjectPartKind.ClosedDoorLeaf));
        return CreateChange(tick, WorldChangeKind.StructureBuilt, anchor, 1);
    }

    private CardinalOrientation ResolveWallMountSide(
        GridPosition anchor,
        CardinalOrientation preferredSide)
    {
        var barriers = _worldObjects.Values
            .Where(worldObject => worldObject.Kind is
                WorldObjectKind.WoodenWall or WorldObjectKind.WoodenDoorFrame)
            .Select(worldObject => worldObject.Anchor)
            .Where(position => position.Z == anchor.Z)
            .ToHashSet();
        barriers.Add(anchor);
        var structuralSolids = _worldObjects.Values
            .Where(worldObject => worldObject.Kind is not (
                WorldObjectKind.WoodenWall or WorldObjectKind.WoodenDoorFrame or
                WorldObjectKind.WoodenDoorLeaf))
            .SelectMany(worldObject => worldObject.GetAbsoluteParts())
            .Where(item => item.Position.Z == anchor.Z &&
                item.Part.Kind == WorldObjectPartKind.Wall)
            .Select(item => item.Position)
            .ToHashSet();
        var wall = WallEnclosureAnalysis.Analyze(
            Baseline.Width,
            Baseline.Height,
            barriers,
            structuralSolids).GetWallSides(anchor);
        if (WallMountPlacementResolver.TryResolve(
            wall,
            preferredSide,
            out var placement))
        {
            return placement.Side;
        }

        var candidates = wall.VisibleFaces != WallInteriorFacing.None
            ? wall.VisibleFaces
            : wall.RoomSides;
        foreach (var (facing, side) in new[]
                 {
                     (WallInteriorFacing.North, CardinalOrientation.North),
                     (WallInteriorFacing.East, CardinalOrientation.East),
                     (WallInteriorFacing.South, CardinalOrientation.South),
                     (WallInteriorFacing.West, CardinalOrientation.West),
                 })
        {
            if ((candidates & facing) != WallInteriorFacing.None)
            {
                return side;
            }
        }

        return preferredSide;
    }

    public bool TryGetWoodenDoorState(GridPosition anchor, out bool isOpen)
    {
        if (_occupancy.TryGetValue(
                new SpatialOccupancyKey(anchor, SpatialOccupancyChannel.Fixture),
                out var claim) &&
            claim.PartKind is WorldObjectPartKind.ClosedDoorLeaf or
                WorldObjectPartKind.OpenDoorLeaf or
                WorldObjectPartKind.AutomaticallyOpenedDoorLeaf &&
            _worldObjects.TryGetValue(claim.ObjectId, out var worldObject) &&
            worldObject.Kind == WorldObjectKind.WoodenDoorLeaf)
        {
            isOpen = claim.PartKind is WorldObjectPartKind.OpenDoorLeaf or
                WorldObjectPartKind.AutomaticallyOpenedDoorLeaf;
            return true;
        }

        isOpen = false;
        return false;
    }

    internal WorldChangeEvent ToggleWoodenDoor(GridPosition anchor, SimulationTick tick)
    {
        if (!TryGetWoodenDoorState(anchor, out var isOpen))
        {
            throw new InvalidOperationException("There is no wooden door at the requested position.");
        }

        var occupancyKey = new SpatialOccupancyKey(anchor, SpatialOccupancyChannel.Fixture);
        var claim = _occupancy[occupancyKey];
        var worldObject = _worldObjects[claim.ObjectId];
        var nextPartKind = isOpen
            ? WorldObjectPartKind.ClosedDoorLeaf
            : WorldObjectPartKind.OpenDoorLeaf;
        SetWoodenDoorPartKind(worldObject, occupancyKey, nextPartKind);
        return CreateChange(tick, WorldChangeKind.DoorToggled, anchor, isOpen ? 0 : 1);
    }

    internal WorldChangeEvent OpenWoodenDoorForTravel(
        GridPosition anchor,
        SimulationTick tick)
    {
        if (!TryGetWoodenDoorState(anchor, out var isOpen) || isOpen)
        {
            throw new InvalidOperationException("The wooden door cannot be automatically opened.");
        }

        var occupancyKey = new SpatialOccupancyKey(anchor, SpatialOccupancyChannel.Fixture);
        var worldObject = _worldObjects[_occupancy[occupancyKey].ObjectId];
        SetWoodenDoorPartKind(
            worldObject,
            occupancyKey,
            WorldObjectPartKind.AutomaticallyOpenedDoorLeaf);
        return CreateChange(tick, WorldChangeKind.DoorToggled, anchor, 1);
    }

    internal IReadOnlyList<GridPosition> GetAutomaticallyOpenedDoorPositions() =>
        _worldObjects.Values
            .Where(worldObject =>
                worldObject.Kind == WorldObjectKind.WoodenDoorLeaf &&
                worldObject.Parts.Single().Kind ==
                    WorldObjectPartKind.AutomaticallyOpenedDoorLeaf)
            .Select(worldObject => worldObject.Anchor)
            .ToArray();

    internal WorldChangeEvent CloseAutomaticallyOpenedDoor(
        GridPosition anchor,
        SimulationTick tick)
    {
        var occupancyKey = new SpatialOccupancyKey(anchor, SpatialOccupancyChannel.Fixture);
        if (!_occupancy.TryGetValue(occupancyKey, out var claim) ||
            claim.PartKind != WorldObjectPartKind.AutomaticallyOpenedDoorLeaf)
        {
            throw new InvalidOperationException("The wooden door is not waiting to close.");
        }

        var worldObject = _worldObjects[claim.ObjectId];
        SetWoodenDoorPartKind(
            worldObject,
            occupancyKey,
            WorldObjectPartKind.ClosedDoorLeaf);
        return CreateChange(tick, WorldChangeKind.DoorToggled, anchor, 0);
    }

    private void SetWoodenDoorPartKind(
        WorldObjectSnapshot worldObject,
        SpatialOccupancyKey occupancyKey,
        WorldObjectPartKind partKind)
    {
        _worldObjects[worldObject.Id] = new WorldObjectSnapshot(
            worldObject.Id,
            worldObject.Kind,
            worldObject.Owner,
            worldObject.Anchor,
            worldObject.Orientation,
            [new(default, SpatialOccupancyChannel.Fixture, partKind)]);
        _occupancy[occupancyKey] = new SpatialOccupancyClaim(worldObject.Id, partKind);
    }

    public bool CanBuildGoblinFieldCamp(GridPosition anchor)
    {
        var footprint = GetFieldCampFootprint(anchor);
        return footprint.All(position =>
            position.Z == 0 &&
            Baseline.IsWithin(position) &&
            Baseline.GetCell(position).IsTraversable &&
            !_occupancy.Keys.Any(key =>
                key.Position.X == position.X && key.Position.Y == position.Y)) &&
            Enumerable.Range(Math.Max(0, anchor.Y - 4),
                    Math.Min(Baseline.Height - 1, anchor.Y + 4) - Math.Max(0, anchor.Y - 4) + 1)
                .SelectMany(y => Enumerable.Range(Math.Max(0, anchor.X - 4),
                        Math.Min(Baseline.Width - 1, anchor.X + 4) - Math.Max(0, anchor.X - 4) + 1)
                    .Select(x => new GridPosition(x, y)))
                .Any(position =>
                    Distance(position, anchor) <= 4 &&
                    Baseline.GetCell(position).Terrain == TerrainKind.ShallowWater &&
                    FindSurfacePath(anchor, position) is not null);
    }

    internal WorldChangeEvent BuildGoblinFieldCamp(GridPosition anchor, SimulationTick tick)
    {
        if (!CanBuildGoblinFieldCamp(anchor))
        {
            throw new InvalidOperationException("The field camp placement is invalid.");
        }

        var id = new WorldObjectId(_worldObjects.Count == 0
            ? 1UL
            : checked(_worldObjects.Keys.Max(item => item.Value) + 1));
        var parts = new List<WorldObjectPartSnapshot>();
        foreach (var position in GetFieldCampFootprint(anchor))
        {
            var relative = new GridPosition(position.X - anchor.X, position.Y - anchor.Y);
            parts.Add(new(relative, SpatialOccupancyChannel.Surface, WorldObjectPartKind.Floor));
            parts.Add(new(relative with { Z = 1 }, SpatialOccupancyChannel.Overhead, WorldObjectPartKind.Roof));
            _plantPatches.Remove(GetIndex(Baseline, position));
        }

        var worldObject = new WorldObjectSnapshot(
            id,
            WorldObjectKind.GoblinFieldCamp,
            WorldObjectOwner.GoblinTribe,
            anchor,
            CardinalOrientation.North,
            parts);
        _worldObjects.Add(id, worldObject);
        foreach (var (position, part) in worldObject.GetAbsoluteParts())
        {
            _occupancy.Add(
                new SpatialOccupancyKey(position, part.Channel),
                new SpatialOccupancyClaim(id, part.Kind));
        }
        return CreateChange(tick, WorldChangeKind.StructureBuilt, anchor, 4);
    }

    private static IReadOnlyList<GridPosition> GetFieldCampFootprint(GridPosition anchor) =>
        [
            anchor,
            anchor with { X = anchor.X + 1 },
            anchor with { Y = anchor.Y + 1 },
            new GridPosition(anchor.X + 1, anchor.Y + 1, anchor.Z),
        ];

    internal bool TryBuildHumanStorehouse(
        GridPosition settlementCenter,
        int maximumDistance,
        IReadOnlySet<GridPosition> reservedPositions,
        SimulationTick tick,
        out WorldChangeEvent change)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDistance);
        ArgumentNullException.ThrowIfNull(reservedPositions);
        const int width = 3;
        const int height = 3;
        var candidates = new List<GridPosition>();
        for (var y = 0; y <= Baseline.Height - height; y++)
        {
            for (var x = 0; x <= Baseline.Width - width; x++)
            {
                var candidate = new GridPosition(x, y);
                if (Distance(new GridPosition(x + 1, y + 1), settlementCenter) <= maximumDistance)
                {
                    candidates.Add(candidate);
                }
            }
        }

        foreach (var anchor in candidates
                     .OrderBy(item => Distance(new GridPosition(item.X + 1, item.Y + 1), settlementCenter))
                     .ThenBy(item => item.Y)
                     .ThenBy(item => item.X))
        {
            var door = new GridPosition(1, 2);
            var doorPosition = anchor with
            {
                X = anchor.X + door.X,
                Y = anchor.Y + door.Y,
            };
            var anchorLevel = Baseline.GetCell(anchor).SurfaceLevel;
            var footprint = Enumerable.Range(0, height)
                .SelectMany(y => Enumerable.Range(0, width)
                    .Select(x => new GridPosition(anchor.X + x, anchor.Y + y)))
                .ToArray();
            if (footprint.Any(position =>
                    reservedPositions.Contains(position) ||
                    !Baseline.GetCell(position).IsTraversable ||
                    Baseline.GetCell(position).SurfaceLevel != anchorLevel ||
                    _occupancy.Keys.Any(key => key.Position.X == position.X && key.Position.Y == position.Y)))
            {
                continue;
            }
            if (!HasSurfacePath(settlementCenter, doorPosition))
            {
                continue;
            }

            var id = new WorldObjectId(_worldObjects.Count == 0
                ? 1UL
                : checked(_worldObjects.Keys.Max(item => item.Value) + 1));
            var parts = new List<WorldObjectPartSnapshot>();
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var relative = new GridPosition(x, y);
                    parts.Add(new(relative, SpatialOccupancyChannel.Surface, WorldObjectPartKind.Floor));
                    parts.Add(new(relative with { Z = 1 }, SpatialOccupancyChannel.Overhead, WorldObjectPartKind.Roof));
                    if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                    {
                        parts.Add(new(relative, SpatialOccupancyChannel.Solid,
                            relative == door ? WorldObjectPartKind.Door : WorldObjectPartKind.Wall));
                    }
                }
            }

            var worldObject = new WorldObjectSnapshot(
                id,
                WorldObjectKind.HumanStorehouse,
                WorldObjectOwner.HumanVillage,
                anchor,
                CardinalOrientation.South,
                parts);
            _worldObjects.Add(id, worldObject);
            foreach (var (position, part) in worldObject.GetAbsoluteParts())
            {
                _occupancy.Add(
                    new SpatialOccupancyKey(position, part.Channel),
                    new SpatialOccupancyClaim(id, part.Kind));
            }
            foreach (var position in footprint)
            {
                _plantPatches.Remove(GetIndex(Baseline, position));
            }
            change = CreateChange(tick, WorldChangeKind.StructureBuilt, anchor, footprint.Length);
            return true;
        }

        change = default;
        return false;
    }

    public bool HasSurfacePath(GridPosition start, GridPosition destination)
        => FindSurfacePath(start, destination) is not null;

    public IReadOnlyList<GridPosition>? FindSurfacePath(
        GridPosition start,
        GridPosition destination,
        bool canOpenDoors = false)
    {
        Func<GridPosition, bool> canTraverse = canOpenDoors
            ? IsSurfaceReachable
            : IsSurfaceTraversable;
        if (!IsSurfaceTraversable(start) || !canTraverse(destination))
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
                if (visited[index] || !canTraverse(neighbor) ||
                    !Baseline.CanTraverseSurfaceEdge(current, neighbor))
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
        ISet<GridPosition> excludedTargets,
        Func<GridPosition, bool>? isAllowed = null,
        bool canOpenDoors = false)
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
        Func<GridPosition, bool> canTraverse = canOpenDoors
            ? IsSurfaceReachable
            : IsSurfaceTraversable;

        while (queue.TryDequeue(out var current))
        {
            if (!excludedTargets.Contains(current) &&
                (isAllowed is null || isAllowed(current)) &&
                _plantPatches.TryGetValue(GetIndex(Baseline, current), out var patch) &&
                patch.Biomass > 0)
            {
                return BuildRoute(start, current, predecessors);
            }

            foreach (var neighbor in Baseline.GetCardinalNeighbors(current))
            {
                var index = GetIndex(Baseline, neighbor);
                if (visited[index] || !canTraverse(neighbor) ||
                    !Baseline.CanTraverseSurfaceEdge(current, neighbor))
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

    public IReadOnlyList<GridPosition>? FindNearestBerryBushPath(
        GridPosition start,
        ISet<GridPosition> excludedTargets,
        Func<GridPosition, bool>? isAllowed = null,
        bool canOpenDoors = false)
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
        Func<GridPosition, bool> canTraverse = canOpenDoors
            ? IsSurfaceReachable
            : IsSurfaceTraversable;

        while (queue.TryDequeue(out var current))
        {
            if (!excludedTargets.Contains(current) &&
                (isAllowed is null || isAllowed(current)) &&
                _plantPatches.TryGetValue(GetIndex(Baseline, current), out var patch) &&
                patch.Kind == PlantKind.BerryBush)
            {
                return BuildRoute(start, current, predecessors);
            }

            foreach (var neighbor in Baseline.GetCardinalNeighbors(current))
            {
                var index = GetIndex(Baseline, neighbor);
                if (visited[index] || !canTraverse(neighbor) ||
                    !Baseline.CanTraverseSurfaceEdge(current, neighbor))
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

    internal bool TryUprootBerryBush(
        GridPosition position,
        SimulationTick tick,
        out WorldChangeEvent change)
    {
        if (!Baseline.IsWithin(position) ||
            !_plantPatches.TryGetValue(GetIndex(Baseline, position), out var patch) ||
            patch.Kind != PlantKind.BerryBush)
        {
            change = default;
            return false;
        }

        _plantPatches.Remove(GetIndex(Baseline, position));
        change = CreateChange(tick, WorldChangeKind.VegetationRemoved, position, -1);
        return true;
    }

    internal bool TryHarvestFellableWood(
        GridPosition anchor,
        SimulationTick tick,
        out int woodQuantity,
        out WorldChangeEvent change)
    {
        var woodyObject = GetFellableWood(anchor);
        if (woodyObject is null)
        {
            woodQuantity = 0;
            change = default;
            return false;
        }

        foreach (var (position, part) in woodyObject.GetAbsoluteParts())
        {
            _occupancy.Remove(new SpatialOccupancyKey(position, part.Channel));
        }

        if (woodyObject.Kind == WorldObjectKind.DeadTreeStump)
        {
            woodQuantity = DeterministicRandom.NextInt(
                Baseline.Seed,
                RandomDomain.Ecology,
                new EntityId(woodyObject.Id.Value),
                SimulationTick.Zero,
                sampleKey: 0x5354554D50UL,
                minimumInclusive: 8,
                maximumExclusive: 17);
            _worldObjects.Remove(woodyObject.Id);
            change = CreateChange(
                tick,
                WorldChangeKind.StumpHarvested,
                anchor,
                woodQuantity);
            return true;
        }

        var trunkSections = woodyObject.Parts.Count(part =>
            part.Kind == WorldObjectPartKind.TreeTrunk);
        woodQuantity = checked(trunkSections * 16);
        var stump = new WorldObjectSnapshot(
            woodyObject.Id,
            WorldObjectKind.DeadTreeStump,
            woodyObject.Owner,
            woodyObject.Anchor,
            woodyObject.Orientation,
            [new(default, SpatialOccupancyChannel.Solid, WorldObjectPartKind.TreeStump)]);
        _worldObjects[woodyObject.Id] = stump;
        _occupancy.Add(
            new SpatialOccupancyKey(anchor, SpatialOccupancyChannel.Solid),
            new SpatialOccupancyClaim(stump.Id, WorldObjectPartKind.TreeStump));
        change = CreateChange(tick, WorldChangeKind.TreeFelled, anchor, woodQuantity);
        return true;
    }

    internal bool TryQuarryBoulder(
        GridPosition anchor,
        SimulationTick tick,
        out int stoneQuantity,
        out WorldChangeEvent change)
    {
        var boulder = GetQuarriableBoulder(anchor);
        if (boulder is null)
        {
            stoneQuantity = 0;
            change = default;
            return false;
        }

        foreach (var (position, part) in boulder.GetAbsoluteParts())
        {
            _occupancy.Remove(new SpatialOccupancyKey(position, part.Channel));
        }

        _worldObjects.Remove(boulder.Id);
        stoneQuantity = DeterministicRandom.NextInt(
            Baseline.Seed,
            RandomDomain.Ecology,
            new EntityId(boulder.Id.Value),
            SimulationTick.Zero,
            sampleKey: 0x424F554C444552UL,
            minimumInclusive: 16,
            maximumExclusive: 33);
        change = CreateChange(tick, WorldChangeKind.BoulderQuarried, anchor, stoneQuantity);
        return true;
    }

    internal IReadOnlyList<WorldChangeEvent> GrowPlants(
        SimulationTick tick,
        int growthPerPatch,
        SeasonKind season)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(growthPerPatch);

        var changes = new List<WorldChangeEvent>();
        foreach (var patch in _plantPatches.Values)
        {
            var canGrow = patch.Kind switch
            {
                PlantKind.BerryBush => season == SeasonKind.Summer,
                PlantKind.MushroomCluster => season is SeasonKind.Spring or SeasonKind.Autumn,
                PlantKind.EdibleRoots => season is not SeasonKind.Winter,
                PlantKind.FishShoal => true,
                _ => false,
            };
            if (!canGrow)
            {
                continue;
            }

            var growthMultiplier = patch.Kind is PlantKind.MushroomCluster or PlantKind.FishShoal
                ? 2
                : 1;
            var grown = Math.Min(
                checked(growthPerPatch * growthMultiplier),
                patch.Capacity - patch.Biomass);
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

    private static void EnsureBerryPatch(
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

    private static PlantKind? SelectFoodSourceKind(
        GeneratedMap baseline,
        MapCell cell,
        EntityId subject,
        int waterBodySize)
    {
        if (cell.Terrain == TerrainKind.ShallowWater)
        {
            return waterBodySize >= 12 && RollOccurrence(baseline, subject, sampleKey: 4) < 32
                ? PlantKind.FishShoal
                : null;
        }

        if (cell.Moisture >= 68 &&
            cell.Fertility >= 20 &&
            RollOccurrence(baseline, subject, sampleKey: 2) < 18)
        {
            return PlantKind.MushroomCluster;
        }

        if (cell.Fertility >= 55 &&
            RollOccurrence(baseline, subject, sampleKey: 3) < 16)
        {
            return PlantKind.EdibleRoots;
        }

        return cell.Fertility >= 35 &&
               cell.Moisture >= 30 &&
               RollOccurrence(baseline, subject, sampleKey: 1) < 18
            ? PlantKind.BerryBush
            : null;
    }

    private static int RollOccurrence(GeneratedMap baseline, EntityId subject, ulong sampleKey) =>
        DeterministicRandom.NextInt(
            baseline.Seed,
            RandomDomain.Ecology,
            subject,
            SimulationTick.Zero,
            sampleKey,
            minimumInclusive: 0,
            maximumExclusive: 100);

    private static int GetFoodSourceCapacity(PlantKind kind, MapCell cell, int waterBodySize) => kind switch
    {
        PlantKind.BerryBush => Math.Max(12, 8 + (cell.Fertility / 3)),
        PlantKind.MushroomCluster => Math.Max(10, 6 + (cell.Moisture / 4)),
        PlantKind.EdibleRoots => Math.Max(10, 6 + (cell.Fertility / 4)),
        PlantKind.FishShoal => Math.Clamp(12 + (waterBodySize / 3), 16, 40),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static bool IsValidHabitat(MapCell cell, PlantKind kind) => kind switch
    {
        PlantKind.FishShoal => cell.Terrain == TerrainKind.ShallowWater,
        PlantKind.BerryBush or PlantKind.MushroomCluster or PlantKind.EdibleRoots =>
            cell.IsTraversable && cell.Terrain != TerrainKind.ShallowWater,
        _ => false,
    };

    private static int[] MeasureWaterBodies(GeneratedMap baseline)
    {
        var sizes = new int[baseline.CellCount];
        var visited = new bool[baseline.CellCount];
        for (var y = 0; y < baseline.Height; y++)
        {
            for (var x = 0; x < baseline.Width; x++)
            {
                var start = new GridPosition(x, y);
                var startIndex = GetIndex(baseline, start);
                if (visited[startIndex] || !IsWater(baseline.GetCell(start)))
                {
                    continue;
                }

                var members = new List<int>();
                var queue = new Queue<GridPosition>();
                visited[startIndex] = true;
                queue.Enqueue(start);
                while (queue.TryDequeue(out var current))
                {
                    members.Add(GetIndex(baseline, current));
                    foreach (var neighbor in baseline.GetCardinalNeighbors(current))
                    {
                        var neighborIndex = GetIndex(baseline, neighbor);
                        if (visited[neighborIndex] || !IsWater(baseline.GetCell(neighbor)))
                        {
                            continue;
                        }

                        visited[neighborIndex] = true;
                        queue.Enqueue(neighbor);
                    }
                }

                foreach (var member in members)
                {
                    sizes[member] = members.Count;
                }
            }
        }

        return sizes;
    }

    private static bool IsWater(MapCell cell) =>
        cell.Terrain is TerrainKind.ShallowWater or TerrainKind.DeepWater;

    private WorldChangeEvent CreateChange(
        SimulationTick tick,
        WorldChangeKind kind,
        GridPosition position,
        int amount)
    {
        Version = checked(Version + 1);
        if (kind is WorldChangeKind.StructureBuilt or WorldChangeKind.TreeFelled or
            WorldChangeKind.StumpHarvested or WorldChangeKind.DoorToggled or
            WorldChangeKind.BoulderQuarried)
        {
            TopologyVersion = checked(TopologyVersion + 1);
        }
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

    private static int Distance(GridPosition left, GridPosition right) =>
        Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y);

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
