using Godot;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Lighting;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Presentation;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal sealed class LowerLevelPresentationState : IDisposable
{
    private LowerLevelPresentationCacheState _cache = new();
    private readonly LowerLevelChunkTextureCache _textures = new();
    private readonly LowerLevelPresentationChangeTracker _changeTracker = new();
    private readonly LowerLevelActorOverlayState _actors = new();
    private readonly VerticalLightPropagationIndex _verticalLights = new();
    private SynchronizationKey? _synchronizationKey;
    private ObservationKey? _observationKey;
    private SimulationSnapshot? _observationSnapshot;
    private LowerLevelExposureIndex? _exposure;
    private PresentationSliceWorkload _workload;
    private int _chunkSize = LowerLevelExposureIndex.DefaultChunkSize;
    private double _nextTextureRebuildSeconds;
    private IReadOnlyDictionary<GridPosition, GridPosition> _openingDestinations =
        new Dictionary<GridPosition, GridPosition>();

    public IReadOnlyList<LowerLevelExposureRegion> VisibleRegions =>
        _exposure?.Regions ?? [];

    public IReadOnlyList<PresentationChunkCacheSnapshot> RebuildCandidates =>
        _cache.GetVisibleRebuildCandidates();

    public IReadOnlyList<LowerLevelChunkTexture> VisibleTextures => _exposure is null
        ? []
        : _textures.GetVisibleTextures(_exposure);

    public IReadOnlyList<LowerLevelActorMarker> VisibleActors => _actors.Markers;

    public IReadOnlyList<LightEmitterSnapshot> ProjectedLightEmitters =>
        _verticalLights.Projected;

    public (
        TimedPresentationOperationMetrics Timings,
        long Chunks,
        long GeometryTextures,
        long StaticLightTextures,
        int VisibleDirtyChunks,
        PresentationSliceWorkload Workload) GetPerformanceMetrics()
    {
        var textures = _textures.GetMetrics();
        return (
            textures.Timings,
            textures.Chunks,
            textures.GeometryTextures,
            textures.StaticLightTextures,
            _cache.GetVisibleRebuildCandidates().Count,
            _workload);
    }

    public void Initialize(
        Texture2D terrainAtlas,
        Texture2D caveAtlas,
        Texture2D environmentAtlas,
        Texture2D itemIconAtlas,
        Texture2D treePartAtlas,
        Texture2D treeCrownAtlas) =>
        _textures.Initialize(
            terrainAtlas,
            caveAtlas,
            environmentAtlas,
            itemIconAtlas,
            treePartAtlas,
            treeCrownAtlas);

    public void Reset()
    {
        _cache.Clear();
        _textures.ResetWorld();
        _changeTracker.Reset();
        _actors.Reset();
        _verticalLights.Reset();
        _synchronizationKey = null;
        _observationKey = null;
        _observationSnapshot = null;
        _exposure = null;
        _workload = default;
        _nextTextureRebuildSeconds = 0d;
        _openingDestinations = new Dictionary<GridPosition, GridPosition>();
    }

    public void ConfigureChunkSize(int chunkSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);
        if (_chunkSize == chunkSize)
        {
            return;
        }

        _chunkSize = chunkSize;
        _cache = new LowerLevelPresentationCacheState(chunkSize);
        _textures.ConfigureChunkSize(chunkSize);
        _changeTracker.Reset();
        _actors.Reset();
        _verticalLights.Reset();
        _synchronizationKey = null;
        _observationKey = null;
        _observationSnapshot = null;
        _exposure = null;
        _workload = default;
        _nextTextureRebuildSeconds = 0d;
        _openingDestinations = new Dictionary<GridPosition, GridPosition>();
    }

    public bool Synchronize(
        SimulationEngine engine,
        SimulationSnapshot snapshot,
        int activeLevel,
        PresentationCellBounds visibleBounds,
        double currentSeconds,
        double baseIntervalSeconds)
    {
        SynchronizeInvalidations(engine, snapshot);
        _nextTextureRebuildSeconds = Math.Min(
            _nextTextureRebuildSeconds,
            currentSeconds);
        var key = new SynchronizationKey(
            engine.World.TopologyVersion,
            activeLevel,
            visibleBounds,
            CreateActiveLevelVisibilitySignature(engine, activeLevel, visibleBounds));
        if (_synchronizationKey == key)
        {
            if (_exposure is null)
            {
                return false;
            }

            var actorsChanged = _actors.Synchronize(
                snapshot,
                _exposure,
                visibleBounds,
                force: false);
            var chunksRebuilt = RebuildTextures(
                engine,
                snapshot,
                activeLevel,
                currentSeconds,
                baseIntervalSeconds);
            return actorsChanged || chunksRebuilt;
        }

        var plan = PresentationSlicePlanner.Create(
            new PresentationSliceRequest(activeLevel, visibleBounds, _chunkSize),
            (x, y) => engine.Map.GetColumnCell(new GridPosition(x, y)).SurfaceLevel,
            engine.World.CreateVerticalPassageSnapshot(),
            position => IsPresentationDiscovered(engine, position),
            engine.World.HasConstructedFloorSurface);
        _openingDestinations = plan.OpeningDestinations;
        _exposure = plan.Exposure;
        _workload = plan.Workload;
        _cache.SynchronizeExposure(_exposure);
        RebuildTextures(
            engine,
            snapshot,
            activeLevel,
            currentSeconds,
            baseIntervalSeconds);
        _actors.Synchronize(snapshot, _exposure, visibleBounds, force: true);
        _verticalLights.Synchronize(
            engine,
            snapshot,
            activeLevel,
            visibleBounds,
            _exposure,
            plan.LightPassages);
        _synchronizationKey = key;
        return true;
    }

    public bool RebuildReadyTextures(
        SimulationEngine engine,
        SimulationSnapshot snapshot,
        int activeLevel,
        double currentSeconds,
        double baseIntervalSeconds) =>
        _exposure is not null &&
        currentSeconds >= _nextTextureRebuildSeconds &&
        RebuildTextures(
            engine,
            snapshot,
            activeLevel,
            currentSeconds,
            baseIntervalSeconds);

    public bool IsDynamicPresentationActive(GridPosition position) =>
        _exposure?.IsContinuouslyExposed(position) == true;

    public bool HasCachedGeometryAt(GridPosition position) =>
        _exposure?.IsContinuouslyExposed(position) == true &&
        _textures.HasGeometryAt(position);

    public bool TryGetOpeningTexture(
        GridPosition upperPosition,
        out LowerLevelOpeningTexture texture)
    {
        if (_openingDestinations.TryGetValue(upperPosition, out var lowerPosition) &&
            _exposure?.IsContinuouslyExposed(lowerPosition) == true)
        {
            return _textures.TryGetOpeningTexture(lowerPosition, out texture);
        }

        texture = null!;
        return false;
    }

    public IReadOnlyList<LowerLevelActorMarker> GetOpeningActors(
        GridPosition upperPosition)
    {
        if (!_openingDestinations.TryGetValue(upperPosition, out var lowerPosition))
        {
            return [];
        }

        return _actors.Markers
            .Where(actor => actor.Position == lowerPosition)
            .ToArray();
    }

    public void Dispose() => _textures.Dispose();

    private bool RebuildTextures(
        SimulationEngine engine,
        SimulationSnapshot snapshot,
        int activeLevel,
        double currentSeconds,
        double baseIntervalSeconds)
    {
        if (_exposure is null)
        {
            return false;
        }

        var result = _textures.RebuildVisibleDirty(
            engine,
            snapshot,
            _exposure,
            _cache,
            activeLevel,
            currentSeconds,
            baseIntervalSeconds);
        _nextTextureRebuildSeconds = result.NextEligibleSeconds;
        return result.RebuiltChunks > 0;
    }

    private void SynchronizeInvalidations(
        SimulationEngine engine,
        SimulationSnapshot snapshot)
    {
        if (ReferenceEquals(_observationSnapshot, snapshot) &&
            _observationKey is { } observed &&
            observed.WorldVersion == engine.World.Version &&
            observed.TopologyVersion == engine.World.TopologyVersion)
        {
            return;
        }

        var key = new ObservationKey(
            engine.World.Version,
            engine.World.TopologyVersion,
            CreateContaminationSignature(snapshot));
        if (_observationKey == key)
        {
            _observationSnapshot = snapshot;
            return;
        }

        var changes = _changeTracker.Synchronize(
            LowerLevelPresentationObservationFactory.Create(engine.World, snapshot));
        if (changes.RequiresFullInvalidation)
        {
            _cache.InvalidateAll(
                PresentationChunkDirtyReason.Topology |
                PresentationChunkDirtyReason.Structures |
                PresentationChunkDirtyReason.Fluids |
                PresentationChunkDirtyReason.StaticLighting);
        }
        foreach (var invalidation in changes.Invalidations)
        {
            var localReason = invalidation.Reason &
                ~PresentationChunkDirtyReason.StaticLighting;
            _cache.InvalidateRetained(invalidation.Position, localReason);
            if ((invalidation.Reason & PresentationChunkDirtyReason.StaticLighting) != 0)
            {
                var radius = LightEmitterCatalog.All
                    .Where(definition =>
                        LightEmitterActivationPolicy.IsStaticallyActive(definition))
                    .Select(definition => definition.RadiusCells)
                    .DefaultIfEmpty(0f)
                    .Max();
                _cache.InvalidateRetainedArea(
                    invalidation.Position,
                    radius,
                    PresentationChunkDirtyReason.StaticLighting);
            }
        }
        _observationKey = key;
        _observationSnapshot = snapshot;
    }

    private static ulong CreateContaminationSignature(SimulationSnapshot snapshot)
    {
        const ulong offset = 14_695_981_039_346_656_037UL;
        const ulong prime = 1_099_511_628_211UL;
        var signature = offset;
        foreach (var stain in snapshot.BloodStains)
        {
            signature = (signature ^ unchecked((uint)stain.Position.X)) * prime;
            signature = (signature ^ unchecked((uint)stain.Position.Y)) * prime;
            signature = (signature ^ unchecked((uint)stain.Position.Z)) * prime;
            signature = (signature ^ (uint)stain.Volume) * prime;
        }
        foreach (var stain in snapshot.SurfaceGrime)
        {
            signature = (signature ^ unchecked((uint)stain.Position.X)) * prime;
            signature = (signature ^ unchecked((uint)stain.Position.Y)) * prime;
            signature = (signature ^ unchecked((uint)stain.Position.Z)) * prime;
            signature = (signature ^ (uint)stain.Volume) * prime;
        }
        return signature;
    }

    private static ulong CreateActiveLevelVisibilitySignature(
        SimulationEngine engine,
        int activeLevel,
        PresentationCellBounds visibleBounds)
    {
        const ulong offset = 14_695_981_039_346_656_037UL;
        const ulong prime = 1_099_511_628_211UL;
        var signature = offset;
        for (var y = visibleBounds.MinimumY; y < visibleBounds.MaximumY; y++)
        {
            for (var x = visibleBounds.MinimumX; x < visibleBounds.MaximumX; x++)
            {
                var position = new GridPosition(x, y, activeLevel);
                var discovered = IsPresentationDiscovered(engine, position);
                signature = (signature ^ (discovered ? 1UL : 0UL)) * prime;
            }
        }

        return signature;
    }

    private static bool IsPresentationDiscovered(
        SimulationEngine engine,
        GridPosition position)
    {
        if (engine.Visibility.TryGet(position, out var visibility) &&
            visibility.IsDiscovered())
        {
            return true;
        }
        if (position.Z <= 0)
        {
            return false;
        }

        var surfaceLevel = engine.Map.GetColumnCell(position).SurfaceLevel;
        return surfaceLevel != position.Z &&
            engine.Visibility.TryGet(
                new GridPosition(position.X, position.Y, surfaceLevel),
                out var surfaceVisibility) &&
            surfaceVisibility.IsDiscovered();
    }

    private readonly record struct SynchronizationKey(
        ulong TopologyVersion,
        int ActiveLevel,
        PresentationCellBounds VisibleBounds,
        ulong VisibilitySignature);

    private readonly record struct ObservationKey(
        ulong WorldVersion,
        ulong TopologyVersion,
        ulong ContaminationSignature);
}
