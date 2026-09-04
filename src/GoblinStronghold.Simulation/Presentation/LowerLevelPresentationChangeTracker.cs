using GoblinStronghold.Simulation.Lighting;
using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Presentation;

public sealed record PresentationTopologyObservation(
    ulong Id,
    IReadOnlyList<GridPosition> Positions);

public sealed record PresentationStructureObservation(
    WorldObjectId Id,
    ulong Signature,
    bool EmitsStaticLight,
    IReadOnlyList<GridPosition> Positions);

public readonly record struct PresentationContaminationObservation(
    GridPosition Position,
    int BloodVolume,
    int GrimeVolume);

public readonly record struct PresentationPlantObservation(
    GridPosition Position,
    PlantKind Kind,
    int Biomass,
    int Capacity);

public readonly record struct PresentationFluidObservation(
    GridPosition Position,
    CellFluidKind Fluid,
    int DepthLevels);

public sealed record LowerLevelPresentationObservation(
    ulong TopologyVersion,
    IReadOnlyList<PresentationTopologyObservation> Topology,
    IReadOnlyList<PresentationStructureObservation> Structures,
    IReadOnlyList<PresentationPlantObservation> Plants,
    IReadOnlyList<PresentationContaminationObservation> Contamination,
    IReadOnlyList<PresentationFluidObservation> Fluids);

public readonly record struct PresentationChunkInvalidation(
    GridPosition Position,
    PresentationChunkDirtyReason Reason);

public sealed record PresentationInvalidationBatch(
    bool RequiresFullInvalidation,
    IReadOnlyList<PresentationChunkInvalidation> Invalidations);

public sealed class LowerLevelPresentationChangeTracker
{
    private ulong _topologyVersion;
    private Dictionary<ulong, PresentationTopologyObservation> _topology = [];
    private Dictionary<WorldObjectId, PresentationStructureObservation> _structures = [];
    private Dictionary<GridPosition, PresentationPlantObservation> _plants = [];
    private Dictionary<GridPosition, PresentationContaminationObservation> _contamination = [];
    private Dictionary<GridPosition, PresentationFluidObservation> _fluids = [];
    private bool _initialized;

    public void Reset()
    {
        _topologyVersion = 0;
        _topology.Clear();
        _structures.Clear();
        _plants.Clear();
        _contamination.Clear();
        _fluids.Clear();
        _initialized = false;
    }

    public PresentationInvalidationBatch Synchronize(
        LowerLevelPresentationObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var topology = observation.Topology.ToDictionary(item => item.Id);
        var structures = observation.Structures.ToDictionary(item => item.Id);
        var plants = observation.Plants.ToDictionary(item => item.Position);
        var contamination = observation.Contamination.ToDictionary(item => item.Position);
        var fluids = observation.Fluids.ToDictionary(item => item.Position);
        if (!_initialized)
        {
            Capture(
                observation.TopologyVersion,
                topology,
                structures,
                plants,
                contamination,
                fluids);
            return new PresentationInvalidationBatch(false, []);
        }

        var invalidations = new Dictionary<GridPosition, PresentationChunkDirtyReason>();
        AddTopologyInvalidations(invalidations, _topology, topology);
        AddStructureInvalidations(invalidations, _structures, structures);
        AddPlantInvalidations(invalidations, _plants, plants);
        AddContaminationInvalidations(invalidations, _contamination, contamination);
        AddFluidInvalidations(invalidations, _fluids, fluids);
        var topologyChanged = observation.TopologyVersion != _topologyVersion;
        var hasKnownTopologyChange = invalidations.Values.Any(reason =>
            (reason & (PresentationChunkDirtyReason.Topology |
                PresentationChunkDirtyReason.Structures |
                PresentationChunkDirtyReason.Fluids)) != 0);
        Capture(
            observation.TopologyVersion,
            topology,
            structures,
            plants,
            contamination,
            fluids);
        return new PresentationInvalidationBatch(
            topologyChanged && !hasKnownTopologyChange,
            invalidations
                .OrderBy(item => item.Key.Z)
                .ThenBy(item => item.Key.Y)
                .ThenBy(item => item.Key.X)
                .Select(item => new PresentationChunkInvalidation(item.Key, item.Value))
                .ToArray());
    }

    private void Capture(
        ulong topologyVersion,
        Dictionary<ulong, PresentationTopologyObservation> topology,
        Dictionary<WorldObjectId, PresentationStructureObservation> structures,
        Dictionary<GridPosition, PresentationPlantObservation> plants,
        Dictionary<GridPosition, PresentationContaminationObservation> contamination,
        Dictionary<GridPosition, PresentationFluidObservation> fluids)
    {
        _topologyVersion = topologyVersion;
        _topology = topology;
        _structures = structures;
        _plants = plants;
        _contamination = contamination;
        _fluids = fluids;
        _initialized = true;
    }

    private static void AddTopologyInvalidations(
        IDictionary<GridPosition, PresentationChunkDirtyReason> invalidations,
        IReadOnlyDictionary<ulong, PresentationTopologyObservation> previous,
        IReadOnlyDictionary<ulong, PresentationTopologyObservation> current)
    {
        foreach (var id in previous.Keys.Union(current.Keys))
        {
            if (previous.ContainsKey(id) && current.ContainsKey(id))
            {
                continue;
            }

            foreach (var position in PreviousAndCurrentPositions(previous, current, id))
            {
                AddReason(invalidations, position, PresentationChunkDirtyReason.Topology);
            }
        }
    }

    private static void AddStructureInvalidations(
        IDictionary<GridPosition, PresentationChunkDirtyReason> invalidations,
        IReadOnlyDictionary<WorldObjectId, PresentationStructureObservation> previous,
        IReadOnlyDictionary<WorldObjectId, PresentationStructureObservation> current)
    {
        foreach (var id in previous.Keys.Union(current.Keys))
        {
            previous.TryGetValue(id, out var oldStructure);
            current.TryGetValue(id, out var newStructure);
            if (oldStructure?.Signature == newStructure?.Signature)
            {
                continue;
            }

            var reason = PresentationChunkDirtyReason.Structures;
            if (oldStructure?.EmitsStaticLight == true || newStructure?.EmitsStaticLight == true)
            {
                reason |= PresentationChunkDirtyReason.StaticLighting;
            }
            foreach (var position in (oldStructure?.Positions ?? [])
                         .Concat(newStructure?.Positions ?? [])
                         .Distinct())
            {
                AddReason(invalidations, position, reason);
            }
        }
    }

    private static void AddContaminationInvalidations(
        IDictionary<GridPosition, PresentationChunkDirtyReason> invalidations,
        IReadOnlyDictionary<GridPosition, PresentationContaminationObservation> previous,
        IReadOnlyDictionary<GridPosition, PresentationContaminationObservation> current)
    {
        foreach (var position in previous.Keys.Union(current.Keys))
        {
            previous.TryGetValue(position, out var oldContamination);
            current.TryGetValue(position, out var newContamination);
            if (oldContamination == newContamination)
            {
                continue;
            }

            AddReason(
                invalidations,
                position,
                PresentationChunkDirtyReason.Contamination);
        }
    }

    private static void AddPlantInvalidations(
        IDictionary<GridPosition, PresentationChunkDirtyReason> invalidations,
        IReadOnlyDictionary<GridPosition, PresentationPlantObservation> previous,
        IReadOnlyDictionary<GridPosition, PresentationPlantObservation> current)
    {
        foreach (var position in previous.Keys.Union(current.Keys))
        {
            previous.TryGetValue(position, out var oldPlant);
            current.TryGetValue(position, out var newPlant);
            if (oldPlant == newPlant)
            {
                continue;
            }

            AddReason(invalidations, position, PresentationChunkDirtyReason.Vegetation);
        }
    }

    private static void AddFluidInvalidations(
        IDictionary<GridPosition, PresentationChunkDirtyReason> invalidations,
        IReadOnlyDictionary<GridPosition, PresentationFluidObservation> previous,
        IReadOnlyDictionary<GridPosition, PresentationFluidObservation> current)
    {
        foreach (var position in previous.Keys.Union(current.Keys))
        {
            previous.TryGetValue(position, out var oldFluid);
            current.TryGetValue(position, out var newFluid);
            if (oldFluid == newFluid)
            {
                continue;
            }

            AddReason(invalidations, position, PresentationChunkDirtyReason.Fluids);
        }
    }

    private static IEnumerable<GridPosition> PreviousAndCurrentPositions(
        IReadOnlyDictionary<ulong, PresentationTopologyObservation> previous,
        IReadOnlyDictionary<ulong, PresentationTopologyObservation> current,
        ulong id) =>
        (previous.TryGetValue(id, out var oldItem) ? oldItem.Positions : [])
        .Concat(current.TryGetValue(id, out var newItem) ? newItem.Positions : [])
        .Distinct();

    private static void AddReason(
        IDictionary<GridPosition, PresentationChunkDirtyReason> invalidations,
        GridPosition position,
        PresentationChunkDirtyReason reason)
    {
        invalidations.TryGetValue(position, out var current);
        invalidations[position] = current | reason;
    }
}

public static class LowerLevelPresentationObservationFactory
{
    public static LowerLevelPresentationObservation Create(
        WorldMapState world,
        SimulationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(snapshot);
        return new LowerLevelPresentationObservation(
            world.TopologyVersion,
            CreateTopology(world),
            snapshot.WorldObjects.Select(worldObject => CreateStructure(world, worldObject))
                .ToArray(),
            CreatePlants(world, snapshot),
            CreateContamination(snapshot),
            world.ConnectedWaterCells
                .OrderBy(position => position.Z)
                .ThenBy(position => position.Y)
                .ThenBy(position => position.X)
                .Select(position => new PresentationFluidObservation(
                    position,
                    CellFluidKind.Water,
                    DepthLevels: 1))
                .ToArray());
    }

    private static IReadOnlyList<PresentationTopologyObservation> CreateTopology(
        WorldMapState world)
    {
        var result = new List<PresentationTopologyObservation>();
        result.AddRange(world.ExcavatedCaveCells.Select(position =>
            new PresentationTopologyObservation(Hash(1, position), [position])));
        result.AddRange(world.ExcavatedTerrainRamps.Select(position =>
            new PresentationTopologyObservation(Hash(2, position), [position])));
        result.AddRange(world.ExcavatedVerticalPassages.Select(passage =>
            new PresentationTopologyObservation(
                Hash(3, passage.Upper, passage.Lower),
                [passage.Upper, passage.Lower])));
        return result.OrderBy(item => item.Id).ToArray();
    }

    private static PresentationStructureObservation CreateStructure(
        WorldMapState world,
        WorldObjectSnapshot worldObject)
    {
        var anchor = world.GetEffectiveWorldObjectAnchor(worldObject);
        var positions = worldObject.Parts
            .Select(part => Add(anchor, part.RelativePosition))
            .Append(anchor)
            .Distinct()
            .OrderBy(position => position.Z)
            .ThenBy(position => position.Y)
            .ThenBy(position => position.X)
            .ToArray();
        var signature = HashStructure(worldObject, anchor);
        var emitsStaticLight = LightEmitterCatalog.TryGet(worldObject.Kind, out var light) &&
            LightEmitterActivationPolicy.IsStaticallyActive(light);
        return new PresentationStructureObservation(
            worldObject.Id,
            signature,
            emitsStaticLight,
            positions);
    }

    private static IReadOnlyList<PresentationContaminationObservation> CreateContamination(
        SimulationSnapshot snapshot)
    {
        var blood = snapshot.BloodStains.ToDictionary(item => item.Position, item => item.Volume);
        var grime = snapshot.SurfaceGrime.ToDictionary(item => item.Position, item => item.Volume);
        return blood.Keys.Union(grime.Keys)
            .OrderBy(position => position.Z)
            .ThenBy(position => position.Y)
            .ThenBy(position => position.X)
            .Select(position => new PresentationContaminationObservation(
                position,
                blood.GetValueOrDefault(position),
                grime.GetValueOrDefault(position)))
            .ToArray();
    }

    private static IReadOnlyList<PresentationPlantObservation> CreatePlants(
        WorldMapState world,
        SimulationSnapshot snapshot) => snapshot.PlantPatches
        .Select(plant => new PresentationPlantObservation(
            PlantPresentationPositionPolicy.Resolve(world.Baseline, plant),
            plant.Kind,
            plant.Biomass,
            plant.Capacity))
        .OrderBy(plant => plant.Position.Z)
        .ThenBy(plant => plant.Position.Y)
        .ThenBy(plant => plant.Position.X)
        .ToArray();

    private static ulong HashStructure(WorldObjectSnapshot worldObject, GridPosition anchor)
    {
        var signature = Hash(
            4,
            anchor,
            new GridPosition(
                (int)worldObject.Kind,
                (int)worldObject.Orientation,
                (int)worldObject.MaterialVariant));
        foreach (var part in worldObject.Parts)
        {
            signature = Mix(signature, unchecked((uint)part.RelativePosition.X));
            signature = Mix(signature, unchecked((uint)part.RelativePosition.Y));
            signature = Mix(signature, unchecked((uint)part.RelativePosition.Z));
            signature = Mix(signature, (uint)part.Channel);
            signature = Mix(signature, (uint)part.Kind);
        }
        return signature;
    }

    private static ulong Hash(int kind, params GridPosition[] positions)
    {
        var signature = Mix(14_695_981_039_346_656_037UL, (uint)kind);
        foreach (var position in positions)
        {
            signature = Mix(signature, unchecked((uint)position.X));
            signature = Mix(signature, unchecked((uint)position.Y));
            signature = Mix(signature, unchecked((uint)position.Z));
        }
        return signature;
    }

    private static ulong Mix(ulong signature, uint value) =>
        (signature ^ value) * 1_099_511_628_211UL;

    private static GridPosition Add(GridPosition left, GridPosition right) => new(
        checked(left.X + right.X),
        checked(left.Y + right.Y),
        checked(left.Z + right.Z));
}
