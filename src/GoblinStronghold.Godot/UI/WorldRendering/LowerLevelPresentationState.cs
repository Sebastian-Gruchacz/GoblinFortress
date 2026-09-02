using Godot;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Lighting;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Presentation;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal sealed class LowerLevelPresentationState : IDisposable
{
    private readonly LowerLevelPresentationCacheState _cache = new();
    private readonly LowerLevelChunkTextureCache _textures = new();
    private readonly LowerLevelPresentationChangeTracker _changeTracker = new();
    private SynchronizationKey? _synchronizationKey;
    private ObservationKey? _observationKey;
    private SimulationSnapshot? _observationSnapshot;
    private LowerLevelExposureIndex? _exposure;
    private IReadOnlyDictionary<GridPosition, GridPosition> _openingDestinations =
        new Dictionary<GridPosition, GridPosition>();

    public IReadOnlyList<LowerLevelExposureRegion> VisibleRegions =>
        _exposure?.Regions ?? [];

    public IReadOnlyList<PresentationChunkCacheSnapshot> RebuildCandidates =>
        _cache.GetVisibleRebuildCandidates();

    public IReadOnlyList<LowerLevelChunkTexture> VisibleTextures => _exposure is null
        ? []
        : _textures.GetVisibleTextures(_exposure);

    public void Initialize(Texture2D terrainAtlas, Texture2D caveAtlas) =>
        _textures.Initialize(terrainAtlas, caveAtlas);

    public void Reset()
    {
        _cache.Clear();
        _textures.ResetWorld();
        _changeTracker.Reset();
        _synchronizationKey = null;
        _observationKey = null;
        _observationSnapshot = null;
        _exposure = null;
        _openingDestinations = new Dictionary<GridPosition, GridPosition>();
    }

    public bool Synchronize(
        SimulationEngine engine,
        SimulationSnapshot snapshot,
        int activeLevel,
        PresentationCellBounds visibleBounds)
    {
        SynchronizeInvalidations(engine, snapshot);
        var key = new SynchronizationKey(
            engine.World.TopologyVersion,
            activeLevel,
            visibleBounds);
        if (_synchronizationKey == key)
        {
            return _exposure is not null &&
                _textures.RebuildVisibleDirty(engine, snapshot, _exposure, _cache) > 0;
        }

        var directlyExposed = new List<GridPosition>();
        if (activeLevel >= 0)
        {
            for (var y = visibleBounds.MinimumY; y < visibleBounds.MaximumY; y++)
            {
                for (var x = visibleBounds.MinimumX; x < visibleBounds.MaximumX; x++)
                {
                    var surface = engine.Map.GetColumnCell(new GridPosition(x, y)).SurfaceLevel;
                    if (surface < activeLevel)
                    {
                        directlyExposed.Add(new GridPosition(x, y, surface));
                    }
                }
            }
        }

        var passages = engine.World.CreateVerticalPassageSnapshot()
            .Where(passage =>
                passage.Upper.Z <= activeLevel &&
                visibleBounds.Contains(passage.Upper) &&
                visibleBounds.Contains(passage.Lower))
            .ToArray();
        _openingDestinations = passages
            .Where(passage => passage.Upper.Z == activeLevel)
            .GroupBy(passage => passage.Upper)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(passage => passage.Lower.Z)
                    .ThenBy(passage => passage.Lower.Y)
                    .ThenBy(passage => passage.Lower.X)
                    .First().Lower);
        _exposure = LowerLevelExposureIndex.Build(
            activeLevel,
            directlyExposed,
            passages);
        _cache.SynchronizeExposure(_exposure);
        _textures.RebuildVisibleDirty(engine, snapshot, _exposure, _cache);
        _synchronizationKey = key;
        return true;
    }

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

    public void Dispose() => _textures.Dispose();

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
                        definition.Activation == LightEmitterActivation.Always)
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

    private readonly record struct SynchronizationKey(
        ulong TopologyVersion,
        int ActiveLevel,
        PresentationCellBounds VisibleBounds);

    private readonly record struct ObservationKey(
        ulong WorldVersion,
        ulong TopologyVersion,
        ulong ContaminationSignature);
}
