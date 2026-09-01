using System.Collections.ObjectModel;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Resources;
using GoblinStronghold.Simulation.Workshops;

namespace GoblinStronghold.Simulation.Map;

public enum PlantKind : byte
{
    BerryBush = 1,
    MushroomCluster = 2,
    EdibleRoots = 3,
    FishShoal = 4,
    ReedBed = 5,
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
    RockExcavated = 9,
    RampExcavated = 10,
    StructureDismantled = 11,
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
    private static readonly SpatialOccupancyChannel[] OccupancyChannels =
        Enum.GetValues<SpatialOccupancyChannel>();
    private readonly SortedDictionary<int, PlantPatchState> _plantPatches;
    private readonly SortedDictionary<WorldObjectId, WorldObjectSnapshot> _worldObjects;
    private readonly Dictionary<SpatialOccupancyKey, SpatialOccupancyClaim> _occupancy;
    private readonly HashSet<GridPosition> _excavatedCaveCells;
    private readonly HashSet<GridPosition> _excavatedTerrainRamps;
    private readonly HashSet<VerticalPassage> _excavatedVerticalPassages;
    private readonly Dictionary<GridPosition, GridPosition> _verticalPassageDestinations;

    private WorldMapState(
        GeneratedMap baseline,
        ulong version,
        SortedDictionary<int, PlantPatchState> plantPatches,
        SortedDictionary<WorldObjectId, WorldObjectSnapshot> worldObjects,
        Dictionary<SpatialOccupancyKey, SpatialOccupancyClaim> occupancy,
        IEnumerable<GridPosition>? excavatedCaveCells = null,
        IEnumerable<GridPosition>? excavatedTerrainRamps = null,
        IEnumerable<VerticalPassage>? excavatedVerticalPassages = null)
    {
        Baseline = baseline;
        Version = version;
        _plantPatches = plantPatches;
        _worldObjects = worldObjects;
        _occupancy = occupancy;
        _excavatedCaveCells = excavatedCaveCells?.ToHashSet() ?? [];
        _excavatedTerrainRamps = excavatedTerrainRamps?.ToHashSet() ?? [];
        _excavatedVerticalPassages = excavatedVerticalPassages?.ToHashSet() ?? [];
        _verticalPassageDestinations = BuildVerticalPassageIndex(
            Baseline.VerticalPassages.Concat(_excavatedVerticalPassages));
    }

    public GeneratedMap Baseline { get; }

    public ulong Version { get; private set; }

    public ulong TopologyVersion { get; private set; }

    public int PlantPatchCount => _plantPatches.Count;

    public int WorldObjectCount => _worldObjects.Count;

    public int MaximumOccupiedLevel => Math.Max(
        Baseline.MaximumWorldLevel,
        _worldObjects.Values
            .SelectMany(worldObject =>
            {
                var effectiveAnchor = GetEffectiveWorldObjectAnchor(worldObject);
                return worldObject.Parts.Select(part =>
                    checked(effectiveAnchor.Z + part.RelativePosition.Z));
            })
            .DefaultIfEmpty(Baseline.MaximumWorldLevel)
            .Max());

    public IReadOnlyCollection<GridPosition> ExcavatedCaveCells => _excavatedCaveCells;

    public IReadOnlyCollection<GridPosition> ExcavatedTerrainRamps => _excavatedTerrainRamps;

    public IReadOnlyCollection<VerticalPassage> ExcavatedVerticalPassages =>
        _excavatedVerticalPassages;

    public IReadOnlyList<VerticalPassage> CreateVerticalPassageSnapshot() =>
        Baseline.VerticalPassages
            .Concat(_excavatedVerticalPassages)
            .OrderBy(passage => passage.Upper.Z)
            .ThenBy(passage => passage.Upper.Y)
            .ThenBy(passage => passage.Upper.X)
            .ThenBy(passage => passage.Lower.Z)
            .ThenBy(passage => passage.Lower.Y)
            .ThenBy(passage => passage.Lower.X)
            .ToArray();

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
                var column = new GridPosition(x, y);
                var position = baseline.GetTerrainSurfacePosition(column);
                var cell = baseline.GetCell(column);
                if (!cell.IsTraversable ||
                    cell.RampDirection != TerrainRampDirection.None ||
                    occupiedColumns.Contains(column) ||
                    column == (baseline.GoblinSpawn with { Z = 0 }) ||
                    column == (baseline.HumanVillage with { Z = 0 }))
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
        IEnumerable<WorldObjectSnapshot> worldObjects,
        IEnumerable<GridPosition>? excavatedCaveCells = null,
        IEnumerable<GridPosition>? excavatedTerrainRamps = null,
        IEnumerable<VerticalPassage>? excavatedVerticalPassages = null)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(plantPatches);
        ArgumentNullException.ThrowIfNull(worldObjects);

        var restoredWorldObjects = worldObjects.ToArray();
        var excavated = excavatedCaveCells?.ToArray() ?? [];
        var excavatedRamps = excavatedTerrainRamps?.ToArray() ?? [];
        var passages = excavatedVerticalPassages?.ToArray() ?? [];
        var lowestSavedLevel = excavated
            .Concat(passages.SelectMany(passage => new[] { passage.Upper, passage.Lower }))
            .Concat(restoredWorldObjects.SelectMany(worldObject =>
                worldObject.GetAbsoluteParts().Select(part => part.Position)))
            .Select(position => position.Z)
            .DefaultIfEmpty(baseline.MinimumWorldLevel)
            .Min();
        while (baseline.DeepestCaveLevel > lowestSavedLevel)
        {
            baseline.MaterializeCaveLevel(baseline.DeepestCaveLevel - 1);
        }

        var restored = new SortedDictionary<int, PlantPatchState>();
        foreach (var patch in plantPatches)
        {
            if (!baseline.IsColumnWithin(patch.Position) ||
                !Enum.IsDefined(patch.Kind) ||
                !IsValidHabitat(baseline, patch.Position, patch.Kind) ||
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

        if (excavated.Any(position =>
                !baseline.IsRockPosition(position) ||
                baseline.GetRockCell(position) is not
                    { Kind: CaveCellKind.SolidRock } and not
                    { Kind: CaveCellKind.Floor, Fluid: CellFluidKind.None }) ||
            excavated.Distinct().Count() != excavated.Length)
        {
            throw new InvalidDataException("The save contains invalid excavated cave cells.");
        }

        if (excavatedRamps.Any(position =>
                !baseline.IsTerrainSurfacePosition(position) ||
                baseline.GetColumnCell(position).RampDirection == TerrainRampDirection.None) ||
            excavatedRamps.Distinct().Count() != excavatedRamps.Length)
        {
            throw new InvalidDataException("The save contains invalid excavated terrain ramps.");
        }

        var allPassages = baseline.VerticalPassages.Concat(passages).ToArray();
        if (passages.Any(passage =>
                passage.Kind != VerticalPassageKind.ExcavatedRamp ||
                passage.Upper.X != passage.Lower.X ||
                passage.Upper.Y != passage.Lower.Y ||
                passage.Upper.Z != passage.Lower.Z + 1 ||
                passage.Upper.Z > 0 ||
                !baseline.IsCavePosition(passage.Lower) ||
                !(baseline.IsTerrainSurfacePosition(passage.Upper) ||
                  baseline.IsCavePosition(passage.Upper)) ||
                !(baseline.GetCaveCell(passage.Lower).IsOpen ||
                  excavated.Contains(passage.Lower)) ||
                (!baseline.IsTerrainSurfacePosition(passage.Upper) &&
                 !(baseline.GetCaveCell(passage.Upper).IsOpen ||
                   excavated.Contains(passage.Upper)))))
        {
            throw new InvalidDataException("The save contains an invalid excavated ramp shape.");
        }
        if (passages.Distinct().Count() != passages.Length)
        {
            throw new InvalidDataException("The save contains duplicate excavated ramps.");
        }
        if (allPassages.SelectMany(passage => new[] { passage.Upper, passage.Lower })
            .GroupBy(position => position)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidDataException("The save contains overlapping vertical passages.");
        }

        var (restoredObjects, occupancy) = ValidateAndIndexObjects(baseline, restoredWorldObjects);
        return new WorldMapState(
            baseline,
            version,
            restored,
            restoredObjects,
            occupancy,
            excavated,
            excavatedRamps,
            passages);
    }

    public PlantPatchSnapshot? GetPlantPatch(GridPosition position)
    {
        if (!Baseline.IsColumnWithin(position) ||
            !_plantPatches.TryGetValue(GetIndex(Baseline, position), out var patch) ||
            patch.Position != position)
        {
            return null;
        }

        return patch.ToSnapshot();
    }

    public IReadOnlyList<PlantPatchSnapshot> CreatePlantSnapshot() =>
        new ReadOnlyCollection<PlantPatchSnapshot>(
            _plantPatches.Values.Select(patch => patch.ToSnapshot()).ToArray());

    public GridPosition? FindNearestHarvestablePlantPosition(
        GridPosition origin,
        GridPosition center,
        int radius,
        PlantKind kind)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        return _plantPatches.Values
            .Where(patch =>
                Math.Abs(patch.Position.X - center.X) +
                    Math.Abs(patch.Position.Y - center.Y) <= radius &&
                patch.Kind == kind &&
                patch.Position.Z == origin.Z &&
                patch.Biomass > 0)
            .OrderBy(patch =>
                Math.Abs(patch.Position.X - origin.X) +
                Math.Abs(patch.Position.Y - origin.Y) +
                Math.Abs(patch.Position.Z - origin.Z))
            .ThenBy(patch => patch.Position.Z)
            .ThenBy(patch => patch.Position.Y)
            .ThenBy(patch => patch.Position.X)
            .Select(patch => (GridPosition?)patch.Position)
            .FirstOrDefault();
    }

    public IReadOnlyList<WorldObjectSnapshot> CreateWorldObjectSnapshot() =>
        new ReadOnlyCollection<WorldObjectSnapshot>(_worldObjects.Values.ToArray());

    internal IEnumerable<WorldObjectSnapshot> EnumerateWorldObjects() =>
        _worldObjects.Values;

    public GridPosition GetEffectiveWorldObjectAnchor(WorldObjectSnapshot worldObject)
    {
        ArgumentNullException.ThrowIfNull(worldObject);
        return worldObject.Anchor.Z == 0 &&
            (worldObject.Kind is WorldObjectKind.Tree or
                WorldObjectKind.DeadTreeStump or WorldObjectKind.Boulder)
            ? Baseline.GetTerrainSurfacePosition(worldObject.Anchor)
            : worldObject.Anchor;
    }

    public int CountWorldObjects(WorldObjectKind kind, WorldObjectOwner owner) =>
        _worldObjects.Values.Count(item => item.Kind == kind && item.Owner == owner);

    public IReadOnlyList<WorldObjectSnapshot> GetWorldObjectsAt(GridPosition position)
    {
        var ids = OccupancyChannels
            .Select(channel => _occupancy.GetValueOrDefault(
                new SpatialOccupancyKey(position, channel)).ObjectId)
            .Where(id => id != default)
            .Distinct()
            .Order()
            .ToArray();
        return new ReadOnlyCollection<WorldObjectSnapshot>(
            ids.Select(id => _worldObjects[id]).ToArray());
    }

    public bool CanDismantleWorldObject(WorldObjectId id) =>
        _worldObjects.TryGetValue(id, out var worldObject) &&
        worldObject.Owner == WorldObjectOwner.GoblinTribe;

    internal WorldChangeEvent DismantleWorldObject(
        WorldObjectId id,
        SimulationTick tick)
    {
        if (!CanDismantleWorldObject(id))
        {
            throw new InvalidOperationException("Only tribe constructions can be dismantled.");
        }

        var target = _worldObjects[id];
        var removedIds = new HashSet<WorldObjectId> { id };
        if (target.Kind is WorldObjectKind.WoodenWall or WorldObjectKind.StoneWall or
                WorldObjectKind.WoodenDoorFrame or WorldObjectKind.StoneDoorFrame)
        {
            foreach (var dependent in _worldObjects.Values.Where(candidate =>
                         candidate.Owner == WorldObjectOwner.GoblinTribe &&
                         candidate.Anchor == target.Anchor &&
                         candidate.Kind is WorldObjectKind.WoodenDoorLeaf or
                             WorldObjectKind.WallTorch))
            {
                removedIds.Add(dependent.Id);
            }
        }

        foreach (var occupancyKey in _occupancy
                     .Where(entry => removedIds.Contains(entry.Value.ObjectId))
                     .Select(entry => entry.Key)
                     .ToArray())
        {
            _occupancy.Remove(occupancyKey);
        }
        foreach (var removedId in removedIds)
        {
            _worldObjects.Remove(removedId);
        }

        return CreateChange(
            tick,
            WorldChangeKind.StructureDismantled,
            target.Anchor,
            removedIds.Count);
    }

    public bool HasPrimitiveWorkshop(GridPosition position) =>
        HasWorkshop(position, WorkshopKind.PrimitiveWorkshop);

    public bool HasWorkshop(GridPosition position, WorkshopKind kind) =>
        TryGetWorkshopKind(position, out var actualKind) && actualKind == kind;

    public bool TryGetWorkshopKind(GridPosition position, out WorkshopKind kind)
    {
        var workshop = _worldObjects.Values.FirstOrDefault(worldObject =>
            worldObject.Owner == WorldObjectOwner.GoblinTribe &&
            worldObject.Anchor == position &&
            worldObject.Kind is WorldObjectKind.PrimitiveWorkshop or
                WorldObjectKind.Bloomery or WorldObjectKind.SmeltingFurnace or
                WorldObjectKind.CrucibleFurnace);
        kind = workshop?.Kind switch
        {
            WorldObjectKind.PrimitiveWorkshop => WorkshopKind.PrimitiveWorkshop,
            WorldObjectKind.Bloomery => WorkshopKind.Bloomery,
            WorldObjectKind.SmeltingFurnace => WorkshopKind.SmeltingFurnace,
            WorldObjectKind.CrucibleFurnace => WorkshopKind.CrucibleFurnace,
            _ => default,
        };
        return workshop is not null;
    }

    internal GridPosition GetNaturalObjectSurfacePosition(WorldObjectSnapshot worldObject) =>
        GetEffectiveWorldObjectAnchor(worldObject);

    internal WorldObjectSnapshot? GetFellableWood(GridPosition position) =>
        _worldObjects.Values.FirstOrDefault(worldObject =>
            worldObject.Kind is WorldObjectKind.Tree or WorldObjectKind.DeadTreeStump &&
            (worldObject.Anchor == position ||
             GetNaturalObjectSurfacePosition(worldObject) == position));

    internal WorldObjectSnapshot? GetQuarriableBoulder(GridPosition position) =>
        _worldObjects.Values.FirstOrDefault(worldObject =>
            worldObject.Kind == WorldObjectKind.Boulder &&
            (worldObject.Anchor == position ||
             GetNaturalObjectSurfacePosition(worldObject) == position));

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

        var hasConstructedSurface = _occupancy.TryGetValue(
            new SpatialOccupancyKey(position, SpatialOccupancyChannel.Surface),
            out var surfaceClaim) &&
            surfaceClaim.PartKind is WorldObjectPartKind.Walkway or WorldObjectPartKind.Floor;
        if (!Baseline.GetCell(position).IsTraversable && !hasConstructedSurface)
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

    public bool IsTerrainReachable(GridPosition position) =>
        HasConstructedSurface(position)
            ? IsMaterialSurfaceReachable(position)
            : Baseline.IsTerrainSurfacePosition(position)
            ? IsMaterialSurfaceReachable(position)
            : IsExcavatedHillReachable(position) ||
              position.Z < 0 && IsSubterraneanReachable(position);

    public bool IsTerrainTraversable(GridPosition position) =>
        IsTerrainReachable(position) && !HasClosedDoorLeaf(position);

    public bool TryResolveGroundItemPosition(
        GridPosition requested,
        out GridPosition resolved)
    {
        if (IsTerrainTraversable(requested))
        {
            resolved = requested;
            return true;
        }

        if (requested.Z == 0 && Baseline.IsColumnWithin(requested))
        {
            var materialSurface = Baseline.GetTerrainSurfacePosition(requested);
            if (materialSurface != requested && HasStableGroundForItem(materialSurface))
            {
                resolved = materialSurface;
                return true;
            }

            for (var radius = 1; radius <= 4; radius++)
            {
                var nearby = Enumerable.Range(-radius, (radius * 2) + 1)
                    .SelectMany(offsetX => new[] { -1, 1 }
                        .Select(sign => new GridPosition(
                            requested.X + offsetX,
                            requested.Y + (sign * (radius - Math.Abs(offsetX))),
                            0)))
                    .Distinct()
                    .Where(Baseline.IsColumnWithin)
                    .Select(Baseline.GetTerrainSurfacePosition)
                    .OrderBy(position => position.Y)
                    .ThenBy(position => position.X);
                foreach (var candidate in nearby)
                {
                    if (HasStableGroundForItem(candidate))
                    {
                        resolved = candidate;
                        return true;
                    }
                }
            }
        }

        resolved = default;
        return false;
    }

    private bool HasStableGroundForItem(GridPosition position)
    {
        if (!IsTerrainTraversable(position))
        {
            return false;
        }

        if (HasConstructedSurface(position) ||
            _excavatedCaveCells.Contains(position) ||
            _excavatedTerrainRamps.Contains(position))
        {
            return true;
        }

        return Baseline.TryGetInitialGeometry(position, out var geometry) &&
            geometry.Support == CellSupportKind.NaturalFlat &&
            geometry.FluidDepthLevels == 0;
    }

    private bool IsMaterialSurfaceReachable(GridPosition position)
    {
        if (!Baseline.TryGetInitialGeometry(position, out var geometry))
        {
            return false;
        }

        var hasConstructedSurface = HasConstructedSurface(position);
        return (geometry.IsOccupiable || hasConstructedSurface) && IsSpatiallyReachable(position);
    }

    private bool HasConstructedSurface(GridPosition position) =>
        TryGetOccupancyClaim(
            position,
            SpatialOccupancyChannel.Surface,
            out var surfaceClaim) &&
        surfaceClaim.PartKind is WorldObjectPartKind.Walkway or WorldObjectPartKind.Floor or
            WorldObjectPartKind.ConstructedRamp;

    private bool IsSpatiallyReachable(GridPosition position)
    {
        var solidIsReachable = !TryGetOccupancyClaim(
                position,
                SpatialOccupancyChannel.Solid,
                out var solidClaim) ||
            solidClaim.PartKind == WorldObjectPartKind.Door;
        var fixtureIsReachable = !TryGetOccupancyClaim(
                position,
                SpatialOccupancyChannel.Fixture,
                out var fixtureClaim) ||
            fixtureClaim.PartKind is WorldObjectPartKind.OpenDoorLeaf or
                WorldObjectPartKind.ClosedDoorLeaf or
                WorldObjectPartKind.AutomaticallyOpenedDoorLeaf;
        return solidIsReachable && fixtureIsReachable;
    }

    private bool HasClosedDoorLeaf(GridPosition position) =>
        TryGetOccupancyClaim(
            position,
            SpatialOccupancyChannel.Fixture,
            out var fixtureClaim) &&
            fixtureClaim.PartKind == WorldObjectPartKind.ClosedDoorLeaf;

    private bool TryGetOccupancyClaim(
        GridPosition position,
        SpatialOccupancyChannel channel,
        out SpatialOccupancyClaim claim)
    {
        if (_occupancy.TryGetValue(new SpatialOccupancyKey(position, channel), out claim))
        {
            return true;
        }

        if (position.Z == 0 || !Baseline.IsTerrainSurfacePosition(position) ||
            !_occupancy.TryGetValue(
                new SpatialOccupancyKey(position with { Z = 0 }, channel),
                out claim))
        {
            return false;
        }

        return _worldObjects.TryGetValue(claim.ObjectId, out var worldObject) &&
            worldObject.Owner is WorldObjectOwner.Nature or WorldObjectOwner.HumanVillage;
    }

    public bool TryGetFluid(
        GridPosition position,
        out CellFluidKind fluid,
        out int depthLevels)
    {
        if (Baseline.TryGetInitialGeometry(position, out var geometry) &&
            geometry.Fluid != CellFluidKind.None && geometry.FluidDepthLevels > 0)
        {
            fluid = geometry.Fluid;
            depthLevels = geometry.FluidDepthLevels;
            return true;
        }

        fluid = CellFluidKind.None;
        depthLevels = 0;
        return false;
    }

    private bool IsSubterraneanReachable(GridPosition position) =>
        Baseline.IsCavePosition(position) &&
        !TryGetFluid(position, out _, out _) &&
        (Baseline.GetCaveCell(position).IsOpen || _excavatedCaveCells.Contains(position)) &&
        IsSpatiallyReachable(position);

    private bool IsExcavatedHillReachable(GridPosition position) =>
        Baseline.IsHillMassPosition(position) &&
        _excavatedCaveCells.Contains(position) &&
        IsSpatiallyReachable(position);

    public IEnumerable<GridPosition> GetTerrainNeighbors(
        GridPosition position,
        bool canOpenDoors = false)
    {
        Func<GridPosition, bool> canTraverse = canOpenDoors
            ? IsTerrainReachable
            : IsTerrainTraversable;
        if (!canTraverse(position))
        {
            yield break;
        }

        var isMaterialSurface = Baseline.IsTerrainSurfacePosition(position);
        foreach (var adjacent in GetCardinalWorldNeighbors(position))
        {
            if (canTraverse(adjacent))
            {
                yield return adjacent;
                continue;
            }

            if (!isMaterialSurface)
            {
                continue;
            }

            var surfaceNeighbor = Baseline.GetTerrainSurfacePosition(adjacent);
            if (canTraverse(surfaceNeighbor) &&
                CanTraverseMaterialSurfaceEdge(position, surfaceNeighbor))
            {
                yield return surfaceNeighbor;
            }
        }

        if (_verticalPassageDestinations.TryGetValue(position, out var passageDestination) &&
            canTraverse(passageDestination))
        {
            yield return passageDestination;
        }

        if (TryGetConstructedRampUpper(position, out var rampUpper) &&
            canTraverse(rampUpper))
        {
            yield return rampUpper;
        }
        foreach (var rampLower in _worldObjects.Values
                     .Where(worldObject =>
                         worldObject.Kind is WorldObjectKind.WoodenRamp or
                             WorldObjectKind.StoneRamp &&
                         GetConstructedRampUpper(worldObject) == position)
                     .Select(worldObject => worldObject.Anchor))
        {
            if (canTraverse(rampLower))
            {
                yield return rampLower;
            }
        }
    }

    public bool CanTraverseTerrainEdge(
        GridPosition from,
        GridPosition to,
        bool canOpenDoors = false) =>
        GetTerrainNeighbors(from, canOpenDoors).Contains(to);

    public IReadOnlyList<GridPosition>? FindTerrainPath(
        GridPosition start,
        GridPosition destination,
        bool canOpenDoors = false,
        Func<GridPosition, GridPosition, bool>? canUseEdge = null) =>
        FindTerrainPath(start, destination, canOpenDoors, canUseEdge, out _);

    internal IReadOnlyList<GridPosition>? FindTerrainPath(
        GridPosition start,
        GridPosition destination,
        bool canOpenDoors,
        Func<GridPosition, GridPosition, bool>? canUseEdge,
        out int expandedNodes)
    {
        expandedNodes = 0;
        Func<GridPosition, bool> canTraverse = canOpenDoors
            ? IsTerrainReachable
            : IsTerrainTraversable;
        if (!IsTerrainTraversable(start) || !canTraverse(destination))
        {
            return null;
        }

        var visited = new HashSet<GridPosition>();
        var predecessors = new Dictionary<GridPosition, GridPosition>();
        var distances = new Dictionary<GridPosition, int> { [start] = 0 };
        var queue = new PriorityQueue<
            GridPosition,
            (int EstimatedTotal, int Heuristic, long Sequence)>();
        var sequence = 0L;
        var startHeuristic = EstimateTerrainDistance(start, destination);
        queue.Enqueue(start, (startHeuristic, startHeuristic, sequence++));
        while (queue.TryDequeue(out var current, out _))
        {
            if (!visited.Add(current))
            {
                continue;
            }
            expandedNodes++;
            if (current == destination)
            {
                var route = new List<GridPosition>();
                while (current != start)
                {
                    route.Add(current);
                    current = predecessors[current];
                }
                route.Reverse();
                return route;
            }

            foreach (var neighbor in GetTerrainNeighbors(current, canOpenDoors))
            {
                if ((canUseEdge is null || canUseEdge(current, neighbor)) &&
                    !visited.Contains(neighbor))
                {
                    var distance = checked(distances[current] + 1);
                    if (distances.TryGetValue(neighbor, out var knownDistance) &&
                        knownDistance <= distance)
                    {
                        continue;
                    }

                    distances[neighbor] = distance;
                    predecessors[neighbor] = current;
                    var heuristic = EstimateTerrainDistance(neighbor, destination);
                    queue.Enqueue(
                        neighbor,
                        (checked(distance + heuristic), heuristic, sequence++));
                }
            }
        }

        return null;
    }

    internal IReadOnlyList<GridPosition>? FindPathToNearestTerrainPosition(
        GridPosition start,
        IReadOnlySet<GridPosition> destinations,
        bool canOpenDoors,
        Func<GridPosition, GridPosition, bool>? canUseEdge,
        out int expandedNodes)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        expandedNodes = 0;
        Func<GridPosition, bool> canTraverse = canOpenDoors
            ? IsTerrainReachable
            : IsTerrainTraversable;
        var reachableDestinations = destinations.Where(canTraverse).ToHashSet();
        if (!IsTerrainTraversable(start) || reachableDestinations.Count == 0)
        {
            return null;
        }

        var visited = new HashSet<GridPosition>();
        var predecessors = new Dictionary<GridPosition, GridPosition>();
        var distances = new Dictionary<GridPosition, int> { [start] = 0 };
        var queue = new PriorityQueue<
            GridPosition,
            (int EstimatedTotal, int Heuristic, long Sequence)>();
        var sequence = 0L;
        var startHeuristic = EstimateTerrainDistance(start, reachableDestinations);
        queue.Enqueue(start, (startHeuristic, startHeuristic, sequence++));
        while (queue.TryDequeue(out var current, out _))
        {
            if (!visited.Add(current))
            {
                continue;
            }
            expandedNodes++;
            if (reachableDestinations.Contains(current))
            {
                var route = new List<GridPosition>();
                while (current != start)
                {
                    route.Add(current);
                    current = predecessors[current];
                }
                route.Reverse();
                return route;
            }

            foreach (var neighbor in GetTerrainNeighbors(current, canOpenDoors))
            {
                if ((canUseEdge is null || canUseEdge(current, neighbor)) &&
                    !visited.Contains(neighbor))
                {
                    var distance = checked(distances[current] + 1);
                    if (distances.TryGetValue(neighbor, out var knownDistance) &&
                        knownDistance <= distance)
                    {
                        continue;
                    }

                    distances[neighbor] = distance;
                    predecessors[neighbor] = current;
                    var heuristic = EstimateTerrainDistance(neighbor, reachableDestinations);
                    queue.Enqueue(
                        neighbor,
                        (checked(distance + heuristic), heuristic, sequence++));
                }
            }
        }

        return null;
    }

    private static int EstimateTerrainDistance(
        GridPosition position,
        GridPosition destination)
    {
        var horizontal = Math.Abs(position.X - destination.X) +
            Math.Abs(position.Y - destination.Y);
        var vertical = Math.Abs(position.Z - destination.Z);
        return Math.Max(horizontal, vertical);
    }

    private static int EstimateTerrainDistance(
        GridPosition position,
        IReadOnlySet<GridPosition> destinations) =>
        destinations.Min(destination => EstimateTerrainDistance(position, destination));

    public bool CanBuildWalkway(IReadOnlyList<GridPosition> positions) =>
        CanBuildWalkway(positions, allowLava: false);

    public bool CanBuildBasaltWalkway(IReadOnlyList<GridPosition> positions) =>
        CanBuildWalkway(positions, allowLava: true);

    private bool CanBuildWalkway(
        IReadOnlyList<GridPosition> positions,
        bool allowLava)
    {
        ArgumentNullException.ThrowIfNull(positions);
        return positions.Count > 0 &&
            positions.Distinct().Count() == positions.Count &&
            positions.All(position =>
                Baseline.IsColumnWithin(position) &&
                position.Z >= Baseline.MinimumWorldLevel &&
                position.Z <= Baseline.MaximumWorldLevel &&
                Baseline.TryGetInitialGeometry(position, out var geometry) &&
                !geometry.IsSolid &&
                (allowLava || geometry.Fluid != CellFluidKind.Lava) &&
                !TryGetOccupancyClaim(position, SpatialOccupancyChannel.Surface, out _) &&
                !TryGetOccupancyClaim(position, SpatialOccupancyChannel.Solid, out _));
    }

    internal WorldChangeEvent BuildWalkway(
        IReadOnlyList<GridPosition> positions,
        SimulationTick tick,
        ResourceVariant materialVariant = ResourceVariant.None) =>
        BuildWalkway(
            positions,
            tick,
            WorldObjectKind.WoodenWalkway,
            allowLava: false,
            materialVariant: materialVariant);

    internal WorldChangeEvent BuildBasaltWalkway(
        IReadOnlyList<GridPosition> positions,
        SimulationTick tick,
        ResourceVariant materialVariant = ResourceVariant.None) =>
        BuildWalkway(
            positions,
            tick,
            WorldObjectKind.BasaltWalkway,
            allowLava: true,
            materialVariant: materialVariant);

    private WorldChangeEvent BuildWalkway(
        IReadOnlyList<GridPosition> positions,
        SimulationTick tick,
        WorldObjectKind kind,
        bool allowLava,
        ResourceVariant materialVariant)
    {
        if (!CanBuildWalkway(positions, allowLava))
        {
            throw new InvalidOperationException("The walkway placement is invalid.");
        }

        var id = new WorldObjectId(_worldObjects.Count == 0
            ? 1UL
            : checked(_worldObjects.Keys.Max(item => item.Value) + 1));
        var anchor = positions[0];
        var worldObject = new WorldObjectSnapshot(
            id,
            kind,
            WorldObjectOwner.GoblinTribe,
            anchor,
            CardinalOrientation.North,
            positions.Select(position => new WorldObjectPartSnapshot(
                new GridPosition(position.X - anchor.X, position.Y - anchor.Y, position.Z - anchor.Z),
                SpatialOccupancyChannel.Surface,
                WorldObjectPartKind.Walkway)),
            materialVariant);
        _worldObjects.Add(id, worldObject);
        foreach (var (position, part) in worldObject.GetAbsoluteParts())
        {
            _occupancy.Add(
                new SpatialOccupancyKey(position, part.Channel),
                new SpatialOccupancyClaim(id, part.Kind));
        }

        return CreateChange(tick, WorldChangeKind.StructureBuilt, anchor, positions.Count);
    }

    public bool CanBuildFloors(IReadOnlyList<GridPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        return positions.Count > 0 &&
            positions.Distinct().Count() == positions.Count &&
            positions.All(position =>
                Baseline.TryGetInitialGeometry(position, out var geometry) &&
                !IsSolidRock(position) &&
                !IsTerrainRampIntact(position) &&
                (geometry.Support != CellSupportKind.NaturalRamp ||
                 _excavatedTerrainRamps.Contains(position)) &&
                !TryGetFluid(position, out _, out _) &&
                !TryGetOccupancyClaim(position, SpatialOccupancyChannel.Surface, out _) &&
                !TryGetOccupancyClaim(position, SpatialOccupancyChannel.Solid, out _));
    }

    public bool CanPlanFloorConstruction(IReadOnlyList<GridPosition> positions) =>
        CanBuildFloors(positions) && positions.All(position => !HasVerticalPassageAt(position));

    public bool HasConstructedFloorSurface(GridPosition position) =>
        TryGetOccupancyClaim(
            position,
            SpatialOccupancyChannel.Surface,
            out var surfaceClaim) &&
        surfaceClaim.PartKind == WorldObjectPartKind.Floor;

    public bool TryInferBuildRamp(GridPosition lower, out GridPosition upper)
    {
        var candidates = GetCardinalWorldNeighbors(lower)
            .Select(neighbor => neighbor with { Z = lower.Z + 1 })
            .Where(candidate => CanBuildRamp(lower, candidate))
            .OrderBy(candidate => IsTerrainTraversable(GetRampApproach(lower, candidate))
                ? 0
                : 1)
            .ThenBy(candidate => DirectionFrom(lower, candidate))
            .ToArray();
        upper = candidates.FirstOrDefault();
        return candidates.Length > 0;
    }

    public bool CanBuildRamp(GridPosition lower, GridPosition upper)
    {
        if (!Baseline.TryGetInitialGeometry(lower, out var lowerGeometry) ||
            lowerGeometry.IsSolid || lowerGeometry.Fluid != CellFluidKind.None ||
            lowerGeometry.Support == CellSupportKind.NaturalRamp ||
            upper.Z != lower.Z + 1 ||
            Math.Abs(upper.X - lower.X) + Math.Abs(upper.Y - lower.Y) != 1 ||
            !IsTerrainTraversable(upper) ||
            TryGetOccupancyClaim(lower, SpatialOccupancyChannel.Surface, out _) ||
            TryGetOccupancyClaim(lower, SpatialOccupancyChannel.Solid, out _) ||
            _worldObjects.Values.Any(worldObject =>
                worldObject.Kind is WorldObjectKind.WoodenRamp or WorldObjectKind.StoneRamp &&
                GetConstructedRampUpper(worldObject) == upper))
        {
            return false;
        }

        return IsTerrainTraversable(lower) ||
            GetCardinalWorldNeighbors(lower).Any(IsTerrainTraversable);
    }

    internal WorldChangeEvent BuildRamp(
        GridPosition lower,
        GridPosition upper,
        SimulationTick tick,
        bool stone,
        ResourceVariant materialVariant)
    {
        if (!CanBuildRamp(lower, upper))
        {
            throw new InvalidOperationException("The ramp placement is invalid.");
        }

        var id = new WorldObjectId(_worldObjects.Count == 0
            ? 1UL
            : checked(_worldObjects.Keys.Max(item => item.Value) + 1));
        var worldObject = new WorldObjectSnapshot(
            id,
            stone ? WorldObjectKind.StoneRamp : WorldObjectKind.WoodenRamp,
            WorldObjectOwner.GoblinTribe,
            lower,
            DirectionFrom(lower, upper),
            [new(default, SpatialOccupancyChannel.Surface,
                WorldObjectPartKind.ConstructedRamp)],
            materialVariant);
        _worldObjects.Add(id, worldObject);
        _occupancy.Add(
            new SpatialOccupancyKey(lower, SpatialOccupancyChannel.Surface),
            new SpatialOccupancyClaim(id, WorldObjectPartKind.ConstructedRamp));
        return CreateChange(tick, WorldChangeKind.StructureBuilt, lower, 1);
    }

    private bool TryGetConstructedRampUpper(
        GridPosition lower,
        out GridPosition upper)
    {
        var ramp = _worldObjects.Values.FirstOrDefault(worldObject =>
            worldObject.Anchor == lower &&
            worldObject.Kind is WorldObjectKind.WoodenRamp or WorldObjectKind.StoneRamp);
        if (ramp is null)
        {
            upper = default;
            return false;
        }

        upper = GetConstructedRampUpper(ramp);
        return true;
    }

    private static GridPosition GetConstructedRampUpper(WorldObjectSnapshot ramp) =>
        ramp.Orientation switch
        {
            CardinalOrientation.North => ramp.Anchor with
                { Y = ramp.Anchor.Y - 1, Z = ramp.Anchor.Z + 1 },
            CardinalOrientation.East => ramp.Anchor with
                { X = ramp.Anchor.X + 1, Z = ramp.Anchor.Z + 1 },
            CardinalOrientation.South => ramp.Anchor with
                { Y = ramp.Anchor.Y + 1, Z = ramp.Anchor.Z + 1 },
            CardinalOrientation.West => ramp.Anchor with
                { X = ramp.Anchor.X - 1, Z = ramp.Anchor.Z + 1 },
            _ => throw new InvalidOperationException("The constructed ramp has no orientation."),
        };

    private static CardinalOrientation DirectionFrom(
        GridPosition lower,
        GridPosition upper) => (upper.X - lower.X, upper.Y - lower.Y) switch
        {
            (0, -1) => CardinalOrientation.North,
            (1, 0) => CardinalOrientation.East,
            (0, 1) => CardinalOrientation.South,
            (-1, 0) => CardinalOrientation.West,
            _ => throw new ArgumentException("Ramp endpoints must be cardinal neighbors."),
        };

    private static GridPosition GetRampApproach(
        GridPosition lower,
        GridPosition upper) => lower with
        {
            X = lower.X - (upper.X - lower.X),
            Y = lower.Y - (upper.Y - lower.Y),
        };

    internal WorldChangeEvent BuildFloor(
        GridPosition position,
        SimulationTick tick,
        bool stone,
        ResourceVariant materialVariant)
    {
        if (!CanBuildFloors([position]))
        {
            throw new InvalidOperationException("The floor placement is invalid.");
        }

        var id = new WorldObjectId(_worldObjects.Count == 0
            ? 1UL
            : checked(_worldObjects.Keys.Max(item => item.Value) + 1));
        var worldObject = new WorldObjectSnapshot(
            id,
            stone ? WorldObjectKind.StoneFloor : WorldObjectKind.WoodenFloor,
            WorldObjectOwner.GoblinTribe,
            position,
            CardinalOrientation.North,
            [new(default, SpatialOccupancyChannel.Surface, WorldObjectPartKind.Floor)],
            materialVariant);
        _worldObjects.Add(id, worldObject);
        _occupancy.Add(
            new SpatialOccupancyKey(position, SpatialOccupancyChannel.Surface),
            new SpatialOccupancyClaim(id, WorldObjectPartKind.Floor));
        if (position.Z == 0)
        {
            _plantPatches.Remove(GetIndex(Baseline, position));
        }

        return CreateChange(tick, WorldChangeKind.StructureBuilt, position, 1);
    }

    public bool CanBuildWoodenBarrier(GridPosition anchor) =>
        (anchor.Z == 0 ? IsSurfaceTraversable(anchor) : IsTerrainTraversable(anchor)) &&
        !_occupancy.Keys.Any(key => key.Position == anchor);

    public bool CanBuildWoodenWalls(IReadOnlyList<GridPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        return positions.Count > 0 &&
            positions.Distinct().Count() == positions.Count &&
            positions.All(CanBuildWoodenBarrier);
    }

    public bool CanBuildStoneWalls(IReadOnlyList<GridPosition> positions) =>
        CanBuildWoodenWalls(positions);

    public bool CanBuildWoodenDoorFrame(GridPosition anchor)
        => CanBuildDoorFrame(anchor, WorldObjectKind.WoodenWall);

    public bool CanBuildStoneDoorFrame(GridPosition anchor)
        => CanBuildDoorFrame(anchor, WorldObjectKind.StoneWall);

    private bool CanBuildDoorFrame(GridPosition anchor, WorldObjectKind replaceableWallKind)
    {
        if (CanBuildWoodenBarrier(anchor))
        {
            return true;
        }

        var claims = _occupancy
            .Where(item => item.Key.Position == anchor)
            .ToArray();
        return HasOpenUnderlyingTerrain(anchor) &&
            claims.Length == 1 &&
            claims[0].Key.Position == anchor &&
            claims[0].Key.Channel == SpatialOccupancyChannel.Solid &&
            claims[0].Value.PartKind == WorldObjectPartKind.Wall &&
            _worldObjects.TryGetValue(claims[0].Value.ObjectId, out var worldObject) &&
            worldObject.Kind == replaceableWallKind &&
            worldObject.Anchor == anchor &&
            worldObject.Parts.Count == 1;
    }

    private bool HasOpenUnderlyingTerrain(GridPosition position) => position.Z switch
    {
        0 => Baseline.IsWithin(position) && Baseline.GetCell(position).IsTraversable,
        < 0 => Baseline.IsCavePosition(position) &&
            (Baseline.GetCaveCell(position).IsOpen || _excavatedCaveCells.Contains(position)),
        _ => false,
    };

    internal WorldChangeEvent BuildWoodenWalls(
        IReadOnlyList<GridPosition> positions,
        SimulationTick tick,
        ResourceVariant materialVariant = ResourceVariant.None)
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
                [new(default, SpatialOccupancyChannel.Solid, WorldObjectPartKind.Wall)],
                materialVariant);
            _worldObjects.Add(id, worldObject);
            _occupancy.Add(
                new SpatialOccupancyKey(position, SpatialOccupancyChannel.Solid),
                new SpatialOccupancyClaim(id, WorldObjectPartKind.Wall));
            if (position.Z == 0)
            {
                _plantPatches.Remove(GetIndex(Baseline, position));
            }
        }

        return CreateChange(tick, WorldChangeKind.StructureBuilt, positions[0], positions.Count);
    }

    internal WorldChangeEvent BuildStoneWalls(
        IReadOnlyList<GridPosition> positions,
        SimulationTick tick,
        ResourceVariant materialVariant = ResourceVariant.None)
    {
        if (!CanBuildStoneWalls(positions))
        {
            throw new InvalidOperationException("The stone wall placement is invalid.");
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
                WorldObjectKind.StoneWall,
                WorldObjectOwner.GoblinTribe,
                position,
                CardinalOrientation.North,
                [new(default, SpatialOccupancyChannel.Solid, WorldObjectPartKind.Wall)],
                materialVariant);
            _worldObjects.Add(id, worldObject);
            _occupancy.Add(
                new SpatialOccupancyKey(position, SpatialOccupancyChannel.Solid),
                new SpatialOccupancyClaim(id, WorldObjectPartKind.Wall));
            if (position.Z == 0)
            {
                _plantPatches.Remove(GetIndex(Baseline, position));
            }
        }

        return CreateChange(tick, WorldChangeKind.StructureBuilt, positions[0], positions.Count);
    }

    internal WorldChangeEvent BuildWoodenDoorFrame(
        GridPosition anchor,
        SimulationTick tick,
        ResourceVariant materialVariant = ResourceVariant.None) => BuildDoorFrame(
            anchor,
            tick,
            WorldObjectKind.WoodenWall,
            WorldObjectKind.WoodenDoorFrame,
            materialVariant);

    internal WorldChangeEvent BuildStoneDoorFrame(
        GridPosition anchor,
        SimulationTick tick,
        ResourceVariant materialVariant = ResourceVariant.None) => BuildDoorFrame(
            anchor,
            tick,
            WorldObjectKind.StoneWall,
            WorldObjectKind.StoneDoorFrame,
            materialVariant);

    private WorldChangeEvent BuildDoorFrame(
        GridPosition anchor,
        SimulationTick tick,
        WorldObjectKind replaceableWallKind,
        WorldObjectKind frameKind,
        ResourceVariant materialVariant)
    {
        if (!CanBuildDoorFrame(anchor, replaceableWallKind))
        {
            throw new InvalidOperationException("The door-frame placement is invalid.");
        }

        WorldObjectId id;
        var replacesExistingWall = false;
        var preferredSide = CardinalOrientation.North;
        var existingClaim = _occupancy.GetValueOrDefault(
            new SpatialOccupancyKey(anchor, SpatialOccupancyChannel.Solid));
        if (existingClaim.PartKind == WorldObjectPartKind.Wall &&
            _worldObjects.TryGetValue(existingClaim.ObjectId, out var existingWall) &&
            existingWall.Kind == replaceableWallKind)
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
            frameKind,
            WorldObjectOwner.GoblinTribe,
            anchor,
            ResolveWallMountSide(anchor, preferredSide),
            [new(default, SpatialOccupancyChannel.Solid, WorldObjectPartKind.Door)],
            materialVariant);
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
        if (anchor.Z == 0)
        {
            _plantPatches.Remove(GetIndex(Baseline, anchor));
        }
        return CreateChange(tick, WorldChangeKind.StructureBuilt, anchor, 1);
    }

    public bool CanBuildWoodenDoor(GridPosition anchor)
    {
        if (!IsTerrainReachable(anchor) ||
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

        return frame.Kind is (WorldObjectKind.WoodenDoorFrame or
            WorldObjectKind.StoneDoorFrame) && frame.Anchor == anchor;
    }

    internal WorldChangeEvent BuildWoodenDoor(
        GridPosition anchor,
        SimulationTick tick,
        ResourceVariant materialVariant = ResourceVariant.None)
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
            [new(default, SpatialOccupancyChannel.Fixture, WorldObjectPartKind.ClosedDoorLeaf)],
            materialVariant);
        _worldObjects.Add(id, worldObject);
        _occupancy.Add(
            new SpatialOccupancyKey(anchor, SpatialOccupancyChannel.Fixture),
            new SpatialOccupancyClaim(id, WorldObjectPartKind.ClosedDoorLeaf));
        return CreateChange(tick, WorldChangeKind.StructureBuilt, anchor, 1);
    }

    public bool CanBuildWallTorch(GridPosition anchor)
    {
        if (_occupancy.ContainsKey(new SpatialOccupancyKey(
                anchor,
                SpatialOccupancyChannel.Fixture)) ||
            !GetCardinalWorldNeighbors(anchor).Any(IsTerrainTraversable))
        {
            return false;
        }

        var hasBuiltWall = _occupancy.TryGetValue(
                new SpatialOccupancyKey(anchor, SpatialOccupancyChannel.Solid),
                out var wallClaim) &&
            wallClaim.PartKind == WorldObjectPartKind.Wall &&
            _worldObjects.TryGetValue(wallClaim.ObjectId, out var wall) &&
            wall.Kind is WorldObjectKind.WoodenWall or WorldObjectKind.StoneWall;
        return hasBuiltWall || IsSolidCaveRock(anchor);
    }

    internal WorldChangeEvent BuildWallTorch(
        GridPosition anchor,
        SimulationTick tick,
        ResourceVariant materialVariant = ResourceVariant.None)
    {
        if (!CanBuildWallTorch(anchor))
        {
            throw new InvalidOperationException("The wall-torch placement is invalid.");
        }

        var id = new WorldObjectId(_worldObjects.Count == 0
            ? 1UL
            : checked(_worldObjects.Keys.Max(item => item.Value) + 1));
        var worldObject = new WorldObjectSnapshot(
            id,
            WorldObjectKind.WallTorch,
            WorldObjectOwner.GoblinTribe,
            anchor,
            ResolveWallMountSide(anchor, CardinalOrientation.North),
            [new(default, SpatialOccupancyChannel.Fixture, WorldObjectPartKind.WallTorch)],
            materialVariant);
        _worldObjects.Add(id, worldObject);
        _occupancy.Add(
            new SpatialOccupancyKey(anchor, SpatialOccupancyChannel.Fixture),
            new SpatialOccupancyClaim(id, WorldObjectPartKind.WallTorch));
        return CreateChange(tick, WorldChangeKind.StructureBuilt, anchor, 1);
    }

    private CardinalOrientation ResolveWallMountSide(
        GridPosition anchor,
        CardinalOrientation preferredSide)
    {
        var barriers = _worldObjects.Values
            .Where(worldObject => worldObject.Kind is
                WorldObjectKind.WoodenWall or WorldObjectKind.StoneWall or
                WorldObjectKind.WoodenDoorFrame or WorldObjectKind.StoneDoorFrame)
            .Select(worldObject => worldObject.Anchor)
            .Where(position => position.Z == anchor.Z)
            .ToHashSet();
        barriers.Add(anchor);
        var structuralSolids = _worldObjects.Values
            .Where(worldObject => worldObject.Kind is not (
                WorldObjectKind.WoodenWall or WorldObjectKind.StoneWall or
                WorldObjectKind.WoodenDoorFrame or WorldObjectKind.StoneDoorFrame or
                WorldObjectKind.WoodenDoorLeaf))
            .SelectMany(worldObject => worldObject.GetAbsoluteParts())
            .Where(item => item.Position.Z == anchor.Z &&
                item.Part.Kind == WorldObjectPartKind.Wall)
            .Select(item => item.Position)
            .ToHashSet();
        if (anchor.Z < 0)
        {
            for (var y = 0; y < Baseline.Height; y++)
            {
                for (var x = 0; x < Baseline.Width; x++)
                {
                    var position = new GridPosition(x, y, anchor.Z);
                    if (IsSolidCaveRock(position))
                    {
                        structuralSolids.Add(position);
                    }
                }
            }
        }
        var wall = WallEnclosureAnalysis.Analyze(
            Baseline.Width,
            Baseline.Height,
            barriers,
            structuralSolids,
            anchor.Z).GetWallSides(anchor);
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
            IsTerrainTraversable(position) &&
            !_occupancy.Keys.Any(key => key.Position == position) &&
            (anchor.Z != 0 || !_occupancy.ContainsKey(new(
                position with { Z = position.Z + 1 },
                SpatialOccupancyChannel.Overhead))));
    }

    internal WorldChangeEvent BuildGoblinFieldCamp(
        GridPosition anchor,
        SimulationTick tick,
        ResourceVariant materialVariant = ResourceVariant.None)
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
            var relative = new GridPosition(
                position.X - anchor.X,
                position.Y - anchor.Y,
                position.Z - anchor.Z);
            parts.Add(new(relative, SpatialOccupancyChannel.Surface, WorldObjectPartKind.Floor));
            if (anchor.Z == 0)
            {
                parts.Add(new(relative with { Z = 1 }, SpatialOccupancyChannel.Overhead, WorldObjectPartKind.Roof));
                _plantPatches.Remove(GetIndex(Baseline, position));
            }
        }

        var worldObject = new WorldObjectSnapshot(
            id,
            WorldObjectKind.GoblinFieldCamp,
            WorldObjectOwner.GoblinTribe,
            anchor,
            CardinalOrientation.North,
            parts,
            materialVariant);
        _worldObjects.Add(id, worldObject);
        foreach (var (position, part) in worldObject.GetAbsoluteParts())
        {
            _occupancy.Add(
                new SpatialOccupancyKey(position, part.Channel),
                new SpatialOccupancyClaim(id, part.Kind));
        }
        return CreateChange(tick, WorldChangeKind.StructureBuilt, anchor, 4);
    }

    public bool CanBuildGoblinHut(GridPosition anchor)
    {
        var footprint = GetSquareFootprint(anchor, 3);
        return footprint.All(position =>
            IsTerrainTraversable(position) &&
            !_occupancy.Keys.Any(key => key.Position == position) &&
            (anchor.Z != 0 || !_occupancy.ContainsKey(new(
                position with { Z = position.Z + 1 },
                SpatialOccupancyChannel.Overhead))));
    }

    internal WorldChangeEvent BuildGoblinHut(
        GridPosition anchor,
        SimulationTick tick,
        ResourceVariant materialVariant = ResourceVariant.None)
    {
        if (!CanBuildGoblinHut(anchor))
        {
            throw new InvalidOperationException("The goblin hut placement is invalid.");
        }

        var id = new WorldObjectId(_worldObjects.Count == 0
            ? 1UL
            : checked(_worldObjects.Keys.Max(item => item.Value) + 1));
        var parts = new List<WorldObjectPartSnapshot>();
        foreach (var position in GetSquareFootprint(anchor, 3))
        {
            var relative = new GridPosition(
                position.X - anchor.X,
                position.Y - anchor.Y,
                0);
            parts.Add(new(relative, SpatialOccupancyChannel.Surface, WorldObjectPartKind.Floor));
            if (anchor.Z == 0)
            {
                parts.Add(new(relative with { Z = 1 }, SpatialOccupancyChannel.Overhead,
                    WorldObjectPartKind.Roof));
                _plantPatches.Remove(GetIndex(Baseline, position));
            }
        }

        var worldObject = new WorldObjectSnapshot(
            id,
            WorldObjectKind.GoblinHut,
            WorldObjectOwner.GoblinTribe,
            anchor,
            CardinalOrientation.North,
            parts,
            materialVariant);
        _worldObjects.Add(id, worldObject);
        foreach (var (position, part) in worldObject.GetAbsoluteParts())
        {
            _occupancy.Add(
                new SpatialOccupancyKey(position, part.Channel),
                new SpatialOccupancyClaim(id, part.Kind));
        }
        return CreateChange(tick, WorldChangeKind.StructureBuilt, anchor, 9);
    }

    public bool CanBuildPrimitiveWorkshop(GridPosition anchor) =>
        CanBuildWorkshop(anchor);

    public bool CanBuildWorkshop(GridPosition anchor) =>
        IsTerrainTraversable(anchor) &&
        !_occupancy.Keys.Any(key => key.Position == anchor);

    internal WorldChangeEvent BuildPrimitiveWorkshop(
        GridPosition anchor,
        SimulationTick tick) =>
        BuildWorkshop(anchor, WorkshopKind.PrimitiveWorkshop, tick);

    internal WorldChangeEvent BuildWorkshop(
        GridPosition anchor,
        WorkshopKind kind,
        SimulationTick tick) =>
        BuildWorkshop(anchor, kind, ResourceVariant.None, tick);

    internal WorldChangeEvent BuildWorkshop(
        GridPosition anchor,
        WorkshopKind kind,
        ResourceVariant materialVariant,
        SimulationTick tick)
    {
        if (!CanBuildWorkshop(anchor))
        {
            throw new InvalidOperationException("The workshop placement is invalid.");
        }

        var (objectKind, partKind) = kind switch
        {
            WorkshopKind.PrimitiveWorkshop => (
                WorldObjectKind.PrimitiveWorkshop,
                WorldObjectPartKind.PrimitiveWorkshop),
            WorkshopKind.Bloomery => (WorldObjectKind.Bloomery, WorldObjectPartKind.Bloomery),
            WorkshopKind.SmeltingFurnace => (
                WorldObjectKind.SmeltingFurnace,
                WorldObjectPartKind.SmeltingFurnace),
            WorkshopKind.CrucibleFurnace => (
                WorldObjectKind.CrucibleFurnace,
                WorldObjectPartKind.CrucibleFurnace),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        var id = new WorldObjectId(_worldObjects.Count == 0
            ? 1UL
            : checked(_worldObjects.Keys.Max(item => item.Value) + 1));
        var worldObject = new WorldObjectSnapshot(
            id,
            objectKind,
            WorldObjectOwner.GoblinTribe,
            anchor,
            CardinalOrientation.North,
            [new(default, SpatialOccupancyChannel.Fixture, partKind)],
            materialVariant);
        _worldObjects.Add(id, worldObject);
        _occupancy.Add(
            new SpatialOccupancyKey(anchor, SpatialOccupancyChannel.Fixture),
            new SpatialOccupancyClaim(id, partKind));
        if (anchor.Z == 0)
        {
            _plantPatches.Remove(GetIndex(Baseline, anchor));
        }
        return CreateChange(tick, WorldChangeKind.StructureBuilt, anchor, 1);
    }

    private static IReadOnlyList<GridPosition> GetFieldCampFootprint(GridPosition anchor) =>
        [
            anchor,
            anchor with { X = anchor.X + 1 },
            anchor with { Y = anchor.Y + 1 },
            new GridPosition(anchor.X + 1, anchor.Y + 1, anchor.Z),
        ];

    private static IReadOnlyList<GridPosition> GetSquareFootprint(GridPosition anchor, int size) =>
        Enumerable.Range(0, size)
            .SelectMany(y => Enumerable.Range(0, size)
                .Select(x => new GridPosition(anchor.X + x, anchor.Y + y, anchor.Z)))
            .ToArray();

    internal bool TryBuildHumanStorehouse(
        GridPosition settlementCenter,
        int maximumDistance,
        IReadOnlySet<GridPosition> reservedPositions,
        SimulationTick tick,
        out WorldChangeEvent change)
    {
        var anchor = FindHumanStorehousePlacement(
            settlementCenter, maximumDistance, reservedPositions);
        return anchor is { } placement && TryBuildHumanStorehouseAt(
            placement, settlementCenter, maximumDistance, reservedPositions, tick, out change) ||
            SetNoWorldChange(out change);
    }

    internal GridPosition? FindHumanStorehousePlacement(
        GridPosition settlementCenter,
        int maximumDistance,
        IReadOnlySet<GridPosition> reservedPositions)
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

        return candidates
                     .OrderBy(item => Distance(new GridPosition(item.X + 1, item.Y + 1), settlementCenter))
                     .ThenBy(item => item.Y)
                     .ThenBy(item => item.X)
                     .Select(anchor => CanBuildHumanStorehouseAt(
                         anchor, settlementCenter, maximumDistance, reservedPositions)
                         ? (GridPosition?)anchor
                         : null)
                     .FirstOrDefault(anchor => anchor is not null);
    }

    internal bool TryBuildHumanStorehouseAt(
        GridPosition anchor,
        GridPosition settlementCenter,
        int maximumDistance,
        IReadOnlySet<GridPosition> reservedPositions,
        SimulationTick tick,
        out WorldChangeEvent change)
    {
        if (!CanBuildHumanStorehouseAt(
                anchor, settlementCenter, maximumDistance, reservedPositions))
        {
            change = default;
            return false;
        }

        const int width = 3;
        const int height = 3;
        var footprint = GetSquareFootprint(anchor, width);
        var id = new WorldObjectId(_worldObjects.Count == 0
            ? 1UL
            : checked(_worldObjects.Keys.Max(item => item.Value) + 1));
        var parts = new List<WorldObjectPartSnapshot>();
        var door = new GridPosition(1, 2);
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
        change = CreateChange(tick, WorldChangeKind.StructureBuilt, anchor, footprint.Count);
        return true;
    }

    internal bool CanBuildHumanStorehouseAt(
        GridPosition anchor,
        GridPosition settlementCenter,
        int maximumDistance,
        IReadOnlySet<GridPosition> reservedPositions)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDistance);
        ArgumentNullException.ThrowIfNull(reservedPositions);
        const int size = 3;
        if (anchor.X < 0 || anchor.Y < 0 ||
            anchor.X > Baseline.Width - size || anchor.Y > Baseline.Height - size ||
            Distance(new GridPosition(anchor.X + 1, anchor.Y + 1), settlementCenter) >
                maximumDistance)
        {
            return false;
        }
        var anchorLevel = Baseline.GetCell(anchor).SurfaceLevel;
        var footprint = GetSquareFootprint(anchor, size);
        if (footprint.Any(position =>
                reservedPositions.Contains(position) ||
                !Baseline.GetCell(position).IsTraversable ||
                Baseline.GetCell(position).SurfaceLevel != anchorLevel ||
                _occupancy.Keys.Any(key =>
                    key.Position.X == position.X && key.Position.Y == position.Y)))
        {
            return false;
        }
        return HasSurfacePath(
            settlementCenter,
            new GridPosition(anchor.X + 1, anchor.Y + 2, anchor.Z));
    }

    private static bool SetNoWorldChange(out WorldChangeEvent change)
    {
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
        var destinations = _plantPatches.Values
            .Where(patch =>
                patch.Biomass > 0 &&
                !excludedTargets.Contains(patch.Position) &&
                (isAllowed is null || isAllowed(patch.Position)))
            .Select(patch => patch.Position)
            .ToHashSet();
        return FindPathToNearestTerrainPosition(
            start,
            destinations,
            canOpenDoors,
            canUseEdge: null,
            out _);
    }

    public IReadOnlyList<GridPosition>? FindNearestBerryBushPath(
        GridPosition start,
        ISet<GridPosition> excludedTargets,
        Func<GridPosition, bool>? isAllowed = null,
        bool canOpenDoors = false)
    {
        ArgumentNullException.ThrowIfNull(excludedTargets);
        var destinations = _plantPatches.Values
            .Where(patch =>
                patch.Kind == PlantKind.BerryBush &&
                !excludedTargets.Contains(patch.Position) &&
                (isAllowed is null || isAllowed(patch.Position)))
            .Select(patch => patch.Position)
            .ToHashSet();
        return FindPathToNearestTerrainPosition(
            start,
            destinations,
            canOpenDoors,
            canUseEdge: null,
            out _);
    }

    internal bool TryHarvest(
        GridPosition position,
        int requestedAmount,
        SimulationTick tick,
        out int harvested,
        out WorldChangeEvent change)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedAmount);

        if (!Baseline.IsColumnWithin(position) ||
            !_plantPatches.TryGetValue(GetIndex(Baseline, position), out var patch) ||
            patch.Position != position ||
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
        if (!Baseline.IsColumnWithin(position) ||
            !_plantPatches.TryGetValue(GetIndex(Baseline, position), out var patch) ||
            patch.Position != position ||
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
            [
                new(default, SpatialOccupancyChannel.Solid, WorldObjectPartKind.TreeStump),
                new(default, SpatialOccupancyChannel.Overhead,
                    WorldObjectPartKind.FelledTreeRemains),
            ]);
        _worldObjects[woodyObject.Id] = stump;
        foreach (var (position, part) in stump.GetAbsoluteParts())
        {
            _occupancy.Add(
                new SpatialOccupancyKey(position, part.Channel),
                new SpatialOccupancyClaim(stump.Id, part.Kind));
        }
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

    public bool IsSolidCaveRock(GridPosition position) =>
        Baseline.IsCavePosition(position) &&
        Baseline.GetCaveCell(position).Kind == CaveCellKind.SolidRock &&
        Baseline.TryGetInitialGeometry(position, out var geometry) &&
        geometry.IsSolid &&
        !_excavatedCaveCells.Contains(position);

    public bool IsSolidHillRock(GridPosition position) =>
        Baseline.IsHillMassPosition(position) &&
        !_excavatedCaveCells.Contains(position);

    public bool IsSolidRock(GridPosition position) =>
        IsSolidCaveRock(position) || IsSolidHillRock(position);

    public bool IsTerrainRampIntact(GridPosition position) =>
        Baseline.IsTerrainSurfacePosition(position) &&
        Baseline.GetColumnCell(position).RampDirection != TerrainRampDirection.None &&
        !_excavatedTerrainRamps.Contains(position);

    private bool CanExcavateTerrainRamp(GridPosition position) =>
        IsTerrainRampIntact(position) &&
        GetCardinalWorldNeighbors(position).Any(IsTerrainTraversable);

    public bool CanExcavateRock(GridPosition position) =>
        (IsSolidRock(position) &&
         GetCardinalWorldNeighbors(position).Any(IsTerrainTraversable)) ||
        CanExcavateTerrainRamp(position);

    internal bool TryExcavateRock(
        GridPosition position,
        SimulationTick tick,
        out RockKind rock,
        out MineralDepositKind deposit,
        out WorldChangeEvent change)
    {
        if (!CanExcavateRock(position))
        {
            rock = default;
            deposit = default;
            change = default;
            return false;
        }

        if (IsTerrainRampIntact(position))
        {
            rock = RockKind.Sandstone;
            deposit = MineralDepositKind.None;
            _excavatedTerrainRamps.Add(position);
            change = CreateChange(tick, WorldChangeKind.RampExcavated, position, 1);
            return true;
        }

        var cell = Baseline.GetRockCell(position);
        rock = cell.Rock;
        deposit = cell.Deposit;
        _excavatedCaveCells.Add(position);
        change = CreateChange(tick, WorldChangeKind.RockExcavated, position, 1);
        return true;
    }

    private bool CanTraverseMaterialSurfaceEdge(GridPosition from, GridPosition to)
    {
        if (!Baseline.CanTraverseTerrainSurfaceEdge(from, to))
        {
            return false;
        }

        var fromLevel = Baseline.GetColumnCell(from).SurfaceLevel;
        var toLevel = Baseline.GetColumnCell(to).SurfaceLevel;
        if (fromLevel == toLevel)
        {
            return true;
        }

        var lower = fromLevel < toLevel ? from : to;
        return !_excavatedTerrainRamps.Contains(lower);
    }

    public bool CanCarveRampDown(GridPosition upper)
    {
        var lower = upper with { Z = upper.Z - 1 };
        return IsTerrainTraversable(upper) &&
            CanOpenCaveLevelForRamp(lower) &&
            !HasVerticalPassageAt(upper) &&
            !HasVerticalPassageAt(lower) &&
            GetWorldObjectsAt(upper).Count == 0 &&
            GetWorldObjectsAt(lower).Count == 0;
    }

    public bool CanCarveRampUp(GridPosition lower)
    {
        var upper = lower with { Z = lower.Z + 1 };
        var upperCanBeOpened = upper.Z == 0
            ? IsTerrainTraversable(upper)
            : CanOpenCaveLevelForRamp(upper);
        return lower.Z < 0 &&
            IsTerrainTraversable(lower) &&
            upperCanBeOpened &&
            !HasVerticalPassageAt(lower) &&
            !HasVerticalPassageAt(upper) &&
            GetWorldObjectsAt(lower).Count == 0 &&
            (upper.Z < 0 || GetWorldObjectsAt(upper).Count == 0);
    }

    private bool CanOpenCaveLevelForRamp(GridPosition position)
    {
        if (Baseline.IsCavePosition(position))
        {
            return IsSolidCaveRock(position) || IsTerrainTraversable(position);
        }

        if (position.Z != Baseline.DeepestCaveLevel - 1 || !Baseline.IsColumnWithin(position))
        {
            return false;
        }

        var cell = Baseline.GetNextCaveLevelCell(position);
        return cell.Kind == CaveCellKind.SolidRock ||
            cell.IsOpen && cell.Fluid == CellFluidKind.None;
    }

    public bool TryGetRampDestinationFluid(
        GridPosition origin,
        bool carveDown,
        out CellFluidKind fluid)
    {
        var destination = origin with { Z = origin.Z + (carveDown ? -1 : 1) };
        if (Baseline.IsCavePosition(destination))
        {
            fluid = Baseline.GetCaveCell(destination).Fluid;
            return fluid != CellFluidKind.None;
        }

        if (destination.Z == Baseline.DeepestCaveLevel - 1 &&
            Baseline.IsColumnWithin(destination))
        {
            fluid = Baseline.GetNextCaveLevelCell(destination).Fluid;
            return fluid != CellFluidKind.None;
        }

        if (Baseline.TryGetInitialGeometry(destination, out var geometry))
        {
            fluid = geometry.Fluid;
            return fluid != CellFluidKind.None;
        }

        fluid = CellFluidKind.None;
        return false;
    }

    public CaveCell GetRampExcavationCell(GridPosition origin, bool carveDown)
    {
        var upper = carveDown ? origin : origin with { Z = origin.Z + 1 };
        var lower = carveDown ? origin with { Z = origin.Z - 1 } : origin;
        var excavated = carveDown ? lower : upper;
        var rockPosition = excavated.Z < 0 ? excavated : lower;
        return Baseline.IsCavePosition(rockPosition)
            ? Baseline.GetCaveCell(rockPosition)
            : Baseline.GetNextCaveLevelCell(rockPosition);
    }

    internal bool TryCarveVerticalRamp(
        GridPosition origin,
        bool carveDown,
        SimulationTick tick,
        out RockKind rock,
        out WorldChangeEvent change)
    {
        if (carveDown ? !CanCarveRampDown(origin) : !CanCarveRampUp(origin))
        {
            rock = default;
            change = default;
            return false;
        }

        var upper = carveDown ? origin : origin with { Z = origin.Z + 1 };
        var lower = carveDown ? origin with { Z = origin.Z - 1 } : origin;
        var excavated = carveDown ? lower : upper;
        if (excavated.Z < 0 && !Baseline.IsCavePosition(excavated))
        {
            Baseline.MaterializeCaveLevel(excavated.Z);
        }
        if (excavated.Z < 0)
        {
            rock = Baseline.GetCaveCell(excavated).Rock;
            _excavatedCaveCells.Add(excavated);
        }
        else
        {
            rock = Baseline.GetCaveCell(lower).Rock;
        }

        var passage = new VerticalPassage(
            upper,
            lower,
            VerticalPassageKind.ExcavatedRamp);
        _excavatedVerticalPassages.Add(passage);
        IndexVerticalPassage(_verticalPassageDestinations, passage);
        change = CreateChange(tick, WorldChangeKind.RampExcavated, origin, carveDown ? -1 : 1);
        return true;
    }

    public bool HasVerticalPassageAt(GridPosition position) =>
        _verticalPassageDestinations.ContainsKey(position);

    private static Dictionary<GridPosition, GridPosition> BuildVerticalPassageIndex(
        IEnumerable<VerticalPassage> passages)
    {
        var destinations = new Dictionary<GridPosition, GridPosition>();
        foreach (var passage in passages)
        {
            IndexVerticalPassage(destinations, passage);
        }

        return destinations;
    }

    private static void IndexVerticalPassage(
        Dictionary<GridPosition, GridPosition> destinations,
        VerticalPassage passage)
    {
        if (!destinations.TryAdd(passage.Upper, passage.Lower) ||
            !destinations.TryAdd(passage.Lower, passage.Upper))
        {
            throw new InvalidDataException("Vertical passages must not overlap.");
        }
    }

    internal IReadOnlyList<WorldChangeEvent> GrowPlants(
        SimulationTick tick,
        int growthPerPatch,
        SeasonKind season,
        FishRegrowthSettings fishRegrowth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(growthPerPatch);
        ArgumentNullException.ThrowIfNull(fishRegrowth);

        var changes = new List<WorldChangeEvent>();
        foreach (var patch in _plantPatches.Values)
        {
            var canGrow = patch.Kind switch
            {
                PlantKind.BerryBush => season == SeasonKind.Summer,
                PlantKind.MushroomCluster => season is SeasonKind.Spring or SeasonKind.Autumn,
                PlantKind.EdibleRoots => season is not SeasonKind.Winter,
                PlantKind.FishShoal => true,
                PlantKind.ReedBed => season is not SeasonKind.Winter,
                _ => false,
            };
            if (!canGrow)
            {
                continue;
            }

            var growthMultiplier = patch.Kind switch
            {
                PlantKind.MushroomCluster or PlantKind.ReedBed => 2,
                PlantKind.FishShoal => GetFishRegrowthMultiplier(
                    patch.Position,
                    fishRegrowth),
                _ => 1,
            };
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

    private int GetFishRegrowthMultiplier(
        GridPosition position,
        FishRegrowthSettings settings)
    {
        var neighbors = Baseline.GetCardinalNeighbors(position)
            .Select(Baseline.GetCell)
            .ToArray();
        var deepWaterNeighbors = neighbors.Count(cell => cell.Terrain == TerrainKind.DeepWater);
        var waterNeighbors = neighbors.Count(IsWater);
        return settings.BaseMultiplier +
            (deepWaterNeighbors > 0 ? settings.DeepWaterBonusMultiplier : 0) +
            (deepWaterNeighbors > 0 && waterNeighbors >= 3
                ? settings.RiverChannelBonusMultiplier
                : 0);
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
            if (waterBodySize >= 12 && RollOccurrence(baseline, subject, sampleKey: 4) < 32)
            {
                return PlantKind.FishShoal;
            }
            return RollOccurrence(baseline, subject, sampleKey: 5) < 20
                ? PlantKind.ReedBed
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
        PlantKind.ReedBed => Math.Max(12, 8 + (cell.Moisture / 3)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static bool IsValidHabitat(
        GeneratedMap baseline,
        GridPosition position,
        PlantKind kind)
    {
        if (!baseline.IsTerrainSurfacePosition(position))
        {
            return false;
        }

        var cell = baseline.GetColumnCell(position);
        if (cell.RampDirection != TerrainRampDirection.None)
        {
            return false;
        }

        return kind switch
        {
            PlantKind.FishShoal or PlantKind.ReedBed =>
                cell.Terrain == TerrainKind.ShallowWater,
            PlantKind.BerryBush or PlantKind.MushroomCluster or PlantKind.EdibleRoots =>
                cell.IsTraversable && cell.Terrain != TerrainKind.ShallowWater,
            _ => false,
        };
    }

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
            WorldChangeKind.BoulderQuarried or WorldChangeKind.RockExcavated or
            WorldChangeKind.RampExcavated or WorldChangeKind.StructureDismantled)
        {
            TopologyVersion = checked(TopologyVersion + 1);
        }
        return new WorldChangeEvent(Version, tick, kind, position, amount);
    }

    public IEnumerable<GridPosition> GetCardinalWorldNeighbors(GridPosition position)
    {
        if (position.X > 0) yield return position with { X = position.X - 1 };
        if (position.X + 1 < Baseline.Width) yield return position with { X = position.X + 1 };
        if (position.Y > 0) yield return position with { Y = position.Y - 1 };
        if (position.Y + 1 < Baseline.Height) yield return position with { Y = position.Y + 1 };
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
                !Enum.IsDefined(worldObject.MaterialVariant) ||
                worldObject.MaterialVariant != ResourceVariant.None &&
                    !MaterialCatalog.TryGet(worldObject.MaterialVariant, out _) ||
                !baseline.IsWorldPosition(worldObject.Anchor) ||
                (worldObject.Anchor.Z != 0 && worldObject.Kind is not (
                    WorldObjectKind.GoblinHut or
                    WorldObjectKind.WoodenWalkway or
                    WorldObjectKind.BasaltWalkway or
                    WorldObjectKind.WoodenFloor or
                    WorldObjectKind.StoneFloor or
                    WorldObjectKind.WoodenRamp or
                    WorldObjectKind.StoneRamp or
                    WorldObjectKind.WoodenWall or
                    WorldObjectKind.StoneWall or
                    WorldObjectKind.WoodenDoorFrame or
                    WorldObjectKind.StoneDoorFrame or
                    WorldObjectKind.WoodenDoorLeaf or
                    WorldObjectKind.WallTorch or
                    WorldObjectKind.PrimitiveWorkshop or
                    WorldObjectKind.Bloomery or
                    WorldObjectKind.SmeltingFurnace or
                    WorldObjectKind.CrucibleFurnace or
                    WorldObjectKind.GoblinFieldCamp)) ||
                worldObject.Parts.Count == 0 ||
                !restored.TryAdd(worldObject.Id, worldObject))
            {
                throw new InvalidDataException("The world contains an invalid spatial object.");
            }

            foreach (var (position, part) in worldObject.GetAbsoluteParts())
            {
                if (!baseline.IsColumnWithin(position) ||
                    position.Z < baseline.MinimumWorldLevel || position.Z > 16 ||
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
