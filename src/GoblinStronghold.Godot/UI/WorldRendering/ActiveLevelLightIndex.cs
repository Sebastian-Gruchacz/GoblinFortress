using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Lighting;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Map.Generation;
using System.Diagnostics;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal sealed class ActiveLevelLightIndex
{
    private readonly LightEmitterIndex _emitters = new();
    private readonly HashSet<LightEmitterHandle> _activeWorkshopHandles = [];
    private readonly TimedPresentationOperationCounter _queries = new();
    private long _queryResults;
    private ulong _topologyVersion = ulong.MaxValue;
    private int _level = int.MinValue;

    public void Reset()
    {
        _emitters.Clear();
        _activeWorkshopHandles.Clear();
        _topologyVersion = ulong.MaxValue;
        _level = int.MinValue;
        _queries.Reset();
        _queryResults = 0;
    }

    public void Synchronize(
        SimulationEngine engine,
        SimulationSnapshot snapshot,
        int level)
    {
        var topologyVersion = engine.World.TopologyVersion;
        if (_topologyVersion != topologyVersion || _level != level)
        {
            RebuildStaticEmitters(engine, snapshot, level);
            _topologyVersion = topologyVersion;
            _level = level;
        }

        SynchronizeActiveWorkshops(snapshot, level);
    }

    public (TimedPresentationOperationMetrics Timings, long Results) GetQueryMetrics() =>
        (_queries.Snapshot, _queryResults);

    public IReadOnlyList<LightEmitterSnapshot> Query(
        int level,
        int minimumX,
        int minimumY,
        int maximumX,
        int maximumY)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var result = _emitters.Query(level, minimumX, minimumY, maximumX, maximumY);
        _queries.Record(startedAt);
        _queryResults = checked(_queryResults + result.Count);
        return result;
    }

    private void RebuildStaticEmitters(
        SimulationEngine engine,
        SimulationSnapshot snapshot,
        int level)
    {
        _emitters.Clear();
        _activeWorkshopHandles.Clear();
        foreach (var worldObject in snapshot.WorldObjects.Where(worldObject =>
                     worldObject.Anchor.Z == level))
        {
            if (!LightEmitterCatalog.TryGet(worldObject.Kind, out var definition) ||
                !LightEmitterActivationPolicy.IsStaticallyActive(definition))
            {
                continue;
            }

            _emitters.Upsert(CreateEmitter(
                definition,
                worldObject.Id.Value,
                worldObject.Anchor,
                worldObject.Kind == WorldObjectKind.WallTorch
                    ? worldObject.Orientation
                    : null));
        }

        if (level >= 0)
        {
            return;
        }

        var lava = LightEmitterCatalog.Get(LightEmitterCatalog.LavaId);
        var glowcap = LightEmitterCatalog.Get(LightEmitterCatalog.CaveGlowcapId);
        for (var y = 0; y < engine.Map.Height; y++)
        {
            for (var x = 0; x < engine.Map.Width; x++)
            {
                var position = new GridPosition(x, y, level);
                if (!engine.World.TryGetFluid(position, out var fluid, out _) ||
                    fluid != CellFluidKind.Lava)
                {
                    if (CaveFloraGenerator.TryGet(engine.Map, position, out var flora) &&
                        flora.Kind == CaveFloraKind.GlowcapCluster)
                    {
                        var floraId = checked(
                            (ulong)(-level * engine.Map.CellCount) +
                            (ulong)(y * engine.Map.Width + x) + 1UL);
                        _emitters.Upsert(CreateEmitter(glowcap, floraId, position));
                    }
                    continue;
                }

                var instanceId = checked((ulong)(y * engine.Map.Width + x) + 1UL);
                _emitters.Upsert(CreateEmitter(lava, instanceId, position));
            }
        }
    }

    private void SynchronizeActiveWorkshops(SimulationSnapshot snapshot, int level)
    {
        var activeWorkshops = snapshot.Actors
            .Where(actor =>
                actor.Job.Kind == ActorJobKind.Craft &&
                actor.Job.Phase == ActorJobPhase.Working &&
                actor.Job.Target.Z == level)
            .Select(actor => actor.Job.Target)
            .ToHashSet();
        var desiredHandles = new HashSet<LightEmitterHandle>();
        foreach (var worldObject in snapshot.WorldObjects.Where(worldObject =>
                     worldObject.Anchor.Z == level &&
                     activeWorkshops.Contains(worldObject.Anchor)))
        {
            if (!LightEmitterCatalog.TryGet(worldObject.Kind, out var definition) ||
                definition.Attachment != LightEmitterAttachment.World ||
                !LightEmitterActivationPolicy.IsActive(
                    definition,
                    new LightEmitterActivationContext(
                        IsWorking: true,
                        HasWorkOrderFuel: true)))
            {
                continue;
            }

            var handle = new LightEmitterHandle(definition.Id, worldObject.Id.Value);
            desiredHandles.Add(handle);
            _emitters.Upsert(CreateEmitter(
                definition,
                worldObject.Id.Value,
                worldObject.Anchor));
        }

        foreach (var handle in _activeWorkshopHandles.Except(desiredHandles).ToArray())
        {
            _emitters.Remove(handle);
        }

        _activeWorkshopHandles.Clear();
        _activeWorkshopHandles.UnionWith(desiredHandles);
    }

    private static LightEmitterSnapshot CreateEmitter(
        LightEmitterDefinition definition,
        ulong instanceId,
        GridPosition position,
        CardinalOrientation? facing = null) => new(
        new LightEmitterHandle(definition.Id, instanceId),
        position,
        definition.RadiusCells,
        definition.Intensity,
        facing);
}
