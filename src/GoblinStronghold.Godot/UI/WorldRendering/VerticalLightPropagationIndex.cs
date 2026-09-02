using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Lighting;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Presentation;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal sealed class VerticalLightPropagationIndex
{
    private SynchronizationKey? _key;
    private IReadOnlyList<LightEmitterSnapshot> _projected = [];

    public IReadOnlyList<LightEmitterSnapshot> Projected => _projected;

    public void Reset()
    {
        _key = null;
        _projected = [];
    }

    public void Synchronize(
        SimulationEngine engine,
        SimulationSnapshot snapshot,
        int activeLevel,
        PresentationCellBounds visibleBounds,
        LowerLevelExposureIndex exposure,
        IReadOnlyList<VerticalPassage> passages)
    {
        var key = new SynchronizationKey(
            engine.World.TopologyVersion,
            activeLevel,
            visibleBounds);
        if (_key == key)
        {
            return;
        }

        var connectedPassages = passages
            .Where(passage =>
                exposure.IsContinuouslyExposed(passage.Lower) &&
                (passage.Upper.Z == activeLevel ||
                 exposure.IsContinuouslyExposed(passage.Upper)))
            .OrderBy(passage => passage.Upper.Z)
            .ThenBy(passage => passage.Upper.Y)
            .ThenBy(passage => passage.Upper.X)
            .ToArray();
        _projected = connectedPassages.Length == 0
            ? []
            : Build(engine, snapshot.WorldObjects, activeLevel, connectedPassages);
        _key = key;
    }

    private static IReadOnlyList<LightEmitterSnapshot> Build(
        SimulationEngine engine,
        IReadOnlyList<WorldObjectSnapshot> worldObjects,
        int activeLevel,
        IReadOnlyList<VerticalPassage> passages)
    {
        var passageLevels = passages
            .Select(passage => passage.Lower.Z)
            .Distinct()
            .ToHashSet();
        var passageTargets = passages
            .GroupBy(passage => passage.Lower.Z)
            .ToDictionary(
                group => group.Key,
                group => group.Select(passage => passage.Lower).ToArray());
        var passageTargetSets = passageTargets.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToHashSet());
        var candidates = new Dictionary<int, List<LightEmitterSnapshot>>();
        foreach (var worldObject in worldObjects)
        {
            if (!passageLevels.Contains(worldObject.Anchor.Z) ||
                !LightEmitterCatalog.TryGet(worldObject.Kind, out var definition) ||
                !LightEmitterActivationPolicy.IsStaticallyActive(definition) ||
                !HasTargetWithin(
                    worldObject.Anchor,
                    definition.RadiusCells,
                    passageTargetSets[worldObject.Anchor.Z]))
            {
                continue;
            }

            AddCandidate(candidates, new LightEmitterSnapshot(
                new LightEmitterHandle(definition.Id, worldObject.Id.Value),
                worldObject.Anchor,
                definition.RadiusCells,
                definition.Intensity,
                worldObject.Kind == WorldObjectKind.WallTorch
                    ? worldObject.Orientation
                    : null));
        }

        CollectLavaEmitters(engine, passageTargets, candidates);
        var blockersByLevel = BuildBlockingCells(engine, worldObjects, passages);
        var result = new List<LightEmitterSnapshot>();
        foreach (var passage in passages)
        {
            if (!candidates.TryGetValue(passage.Lower.Z, out var levelCandidates))
            {
                continue;
            }

            foreach (var emitter in levelCandidates.ToArray())
            {
                if (!VerticalLightPropagationPolicy.TryProjectThrough(
                        emitter,
                        passage,
                        blockersByLevel[passage.Lower.Z],
                        out var projected))
                {
                    continue;
                }

                AddCandidate(candidates, projected);
                if (projected.Position.Z == activeLevel)
                {
                    result.Add(projected);
                }
            }
        }

        return result
            .DistinctBy(emitter => emitter.Handle)
            .OrderBy(emitter => emitter.Handle.DefinitionId.Value, StringComparer.Ordinal)
            .ThenBy(emitter => emitter.Handle.InstanceId)
            .ToArray();
    }

    private static void CollectLavaEmitters(
        SimulationEngine engine,
        IReadOnlyDictionary<int, GridPosition[]> passageTargets,
        Dictionary<int, List<LightEmitterSnapshot>> candidates)
    {
        var lava = LightEmitterCatalog.Get(LightEmitterCatalog.LavaId);
        var padding = (int)Math.Ceiling(lava.RadiusCells);
        foreach (var (level, targets) in passageTargets)
        {
            if (level >= 0)
            {
                continue;
            }

            var visited = new HashSet<GridPosition>();
            foreach (var target in targets)
            {
                for (var y = Math.Max(0, target.Y - padding);
                     y < Math.Min(engine.Map.Height, target.Y + padding + 1);
                     y++)
                {
                    for (var x = Math.Max(0, target.X - padding);
                         x < Math.Min(engine.Map.Width, target.X + padding + 1);
                         x++)
                    {
                        var position = new GridPosition(x, y, level);
                        if (!visited.Add(position) || !Intersects(position, target, lava.RadiusCells) ||
                            !engine.World.TryGetFluid(position, out var fluid, out _) ||
                            fluid != CellFluidKind.Lava)
                        {
                            continue;
                        }

                        var instanceId = checked((ulong)(y * engine.Map.Width + x) + 1UL);
                        AddCandidate(candidates, new LightEmitterSnapshot(
                            new LightEmitterHandle(lava.Id, instanceId),
                            position,
                            lava.RadiusCells,
                            lava.Intensity));
                    }
                }
            }
        }
    }

    private static IReadOnlyDictionary<int, HashSet<GridPosition>> BuildBlockingCells(
        SimulationEngine engine,
        IReadOnlyList<WorldObjectSnapshot> worldObjects,
        IReadOnlyList<VerticalPassage> passages)
    {
        var padding = (int)Math.Ceiling(
            LightEmitterCatalog.All.Select(definition => definition.RadiusCells).Max());
        return passages
            .GroupBy(passage => passage.Lower.Z)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var targets = group.Select(passage => passage.Lower).ToArray();
                    return LightBlockingCellIndex.Collect(
                        engine,
                        worldObjects,
                        group.Key,
                        targets.Min(position => position.X) - padding,
                        targets.Min(position => position.Y) - padding,
                        targets.Max(position => position.X) + padding + 1,
                        targets.Max(position => position.Y) + padding + 1);
                });
    }

    private static void AddCandidate(
        Dictionary<int, List<LightEmitterSnapshot>> candidates,
        LightEmitterSnapshot emitter)
    {
        if (!candidates.TryGetValue(emitter.Position.Z, out var level))
        {
            level = [];
            candidates.Add(emitter.Position.Z, level);
        }
        if (!level.Any(existing => existing.Handle == emitter.Handle))
        {
            level.Add(emitter);
        }
    }

    private static bool Intersects(
        GridPosition source,
        GridPosition target,
        float radius)
    {
        var deltaX = source.X - target.X;
        var deltaY = source.Y - target.Y;
        return (deltaX * deltaX) + (deltaY * deltaY) < radius * radius;
    }

    private static bool HasTargetWithin(
        GridPosition source,
        float radius,
        IReadOnlySet<GridPosition> targets)
    {
        var padding = (int)Math.Ceiling(radius);
        for (var y = source.Y - padding; y <= source.Y + padding; y++)
        {
            for (var x = source.X - padding; x <= source.X + padding; x++)
            {
                var target = new GridPosition(x, y, source.Z);
                if (targets.Contains(target) && Intersects(source, target, radius))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private readonly record struct SynchronizationKey(
        ulong TopologyVersion,
        int ActiveLevel,
        PresentationCellBounds VisibleBounds);
}
