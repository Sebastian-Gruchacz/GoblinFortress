using System.Collections.ObjectModel;

namespace GoblinStronghold.Simulation.Map;

public enum CellVisibility : byte
{
    Unknown = 0,
    Explored = 1,
    Visible = 2,
}

public static class CellVisibilityExtensions
{
    public static bool IsDiscovered(this CellVisibility visibility) =>
        visibility is CellVisibility.Explored or CellVisibility.Visible;
}

public sealed class WorldVisibilityState
{
    private CellVisibility[] _cells;
    private readonly List<int> _visibleIndices;
    private readonly HashSet<int> _discoveredIndices;
    private readonly HashSet<GridPosition> _verticalDiscoverySeeds;
    private readonly Queue<GridPosition> _pendingVerticalDiscoverySeeds;
    private int _materializedNegativeLevelCount;
    private int _materializedPositiveLevelCount;
    private ulong? _verticalDiscoveryTopologyVersion;
    private (GridPosition Position, int Radius)[] _verticalRevealObservers = [];
    private GridPosition[] _verticalRevealPositions = [];
    private int _verticalRevealMinimumLevel;
    private int _verticalRevealMaximumLevel;
    private ulong? _verticalRevealTopologyVersion;

    private WorldVisibilityState(
        GeneratedMap map,
        CellVisibility[] cells,
        int materializedPositiveLevelCount)
    {
        Map = map;
        _cells = cells;
        _materializedNegativeLevelCount = map.MaterializedNegativeLevelCount;
        _materializedPositiveLevelCount = materializedPositiveLevelCount;
        _visibleIndices = Enumerable.Range(0, cells.Length)
            .Where(index => cells[index] == CellVisibility.Visible)
            .ToList();
        _discoveredIndices = Enumerable.Range(0, cells.Length)
            .Where(index => cells[index] != CellVisibility.Unknown)
            .ToHashSet();
        _verticalDiscoverySeeds = _visibleIndices
            .Select(GetPosition)
            .ToHashSet();
        _pendingVerticalDiscoverySeeds = new Queue<GridPosition>(_verticalDiscoverySeeds);
    }

    public GeneratedMap Map { get; }

    public int DiscoveredCellCount
    {
        get
        {
            EnsureLayerCapacity();
            return _discoveredIndices.Count;
        }
    }

    internal static WorldVisibilityState Create(GeneratedMap map, int? maximumLevel = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        var positiveLevelCount = Math.Max(
            map.MaterializedPositiveLevelCount,
            Math.Max(0, maximumLevel ?? map.MaximumWorldLevel));
        return new WorldVisibilityState(
            map,
            new CellVisibility[checked(map.CellCount *
                (map.MaterializedNegativeLevelCount + positiveLevelCount + 1))],
            positiveLevelCount);
    }

    internal static WorldVisibilityState Restore(
        GeneratedMap map,
        IEnumerable<CellVisibility> visibility,
        int? savedNegativeLevelCount = null,
        int? maximumLevel = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(visibility);
        var cells = visibility.ToArray();
        var targetPositiveLevelCount = Math.Max(
            map.MaterializedPositiveLevelCount,
            Math.Max(0, maximumLevel ?? map.MaximumWorldLevel));
        if (cells.Length % map.CellCount == 0)
        {
            var storedPositiveLevelCount =
                (cells.Length / map.CellCount) - map.MaterializedNegativeLevelCount - 1;
            if (storedPositiveLevelCount is >= 0 and <= WorldMapState.MaximumSupportedLevel)
            {
                targetPositiveLevelCount = Math.Max(
                    targetPositiveLevelCount,
                    storedPositiveLevelCount);
            }
        }
        var legacyLength = checked(map.CellCount * (map.CaveLevelCount + 1));
        var previousExpectedLength = checked(map.CellCount *
            (map.CaveLevelCount + map.MaterializedPositiveLevelCount + 1));
        var expectedLength = checked(map.CellCount *
            (map.MaterializedNegativeLevelCount + targetPositiveLevelCount + 1));
        var savedExpectedLength = savedNegativeLevelCount is null
            ? expectedLength
            : checked(map.CellCount *
                (savedNegativeLevelCount.Value + targetPositiveLevelCount + 1));
        if (cells.Any(state => !Enum.IsDefined(state)) ||
            cells.Length != map.CellCount && cells.Length != legacyLength &&
            cells.Length != previousExpectedLength &&
            cells.Length != expectedLength && cells.Length != savedExpectedLength)
        {
            throw new InvalidDataException("The save contains invalid fog-of-war state.");
        }

        if (cells.Length != expectedLength)
        {
            cells = ExpandLayers(
                cells,
                map.CellCount,
                savedNegativeLevelCount ??
                    Math.Min(map.CaveLevelCount, (cells.Length / map.CellCount) - 1),
                map.MaterializedNegativeLevelCount,
                targetPositiveLevelCount);
        }

        return new WorldVisibilityState(map, cells, targetPositiveLevelCount);
    }

    public CellVisibility Get(GridPosition position)
    {
        if (!TryGet(position, out var visibility))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        return visibility;
    }

    public bool TryGet(GridPosition position, out CellVisibility visibility)
    {
        EnsureLayerCapacity();
        if (!IsVisibilityPosition(position))
        {
            visibility = default;
            return false;
        }

        visibility = _cells[GetIndex(position)];
        return true;
    }

    public IReadOnlyList<CellVisibility> CreateSnapshot()
    {
        EnsureLayerCapacity();
        return new ReadOnlyCollection<CellVisibility>((CellVisibility[])_cells.Clone());
    }

    internal void Reveal(IEnumerable<GridPosition> observers, int radius)
    {
        ArgumentNullException.ThrowIfNull(observers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);
        Reveal(observers.Select(observer => (observer, radius)));
    }

    internal void Reveal(
        IEnumerable<(GridPosition Position, int Radius)> observers,
        Func<GridPosition, bool>? isSolidHillRock = null)
    {
        ArgumentNullException.ThrowIfNull(observers);
        EnsureLayerCapacity();
        var observerArray = observers.ToArray();
        if (observerArray.Any(observer => observer.Radius <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(observers));
        }

        foreach (var index in _visibleIndices)
        {
            _cells[index] = CellVisibility.Explored;
        }
        _visibleIndices.Clear();

        foreach (var (observer, radius) in observerArray)
        {
            var radiusSquared = checked(radius * radius);
            for (var y = observer.Y - radius; y <= observer.Y + radius; y++)
            {
                for (var x = observer.X - radius; x <= observer.X + radius; x++)
                {
                    var position = new GridPosition(x, y, observer.Z);
                    var distanceSquared = checked(
                        ((x - observer.X) * (x - observer.X)) +
                        ((y - observer.Y) * (y - observer.Y)));
                    if (distanceSquared <= radiusSquared && IsVisibilityPosition(position) &&
                        !(isSolidHillRock?.Invoke(position) ?? Map.IsHillMassPosition(position)))
                    {
                        var index = GetIndex(position);
                        if (_cells[index] != CellVisibility.Visible)
                        {
                            TrackDiscovery(index, isVerticalSeed: true);
                            _cells[index] = CellVisibility.Visible;
                            _visibleIndices.Add(index);
                        }
                    }
                }
            }
        }
    }

    internal void Discover(
        IEnumerable<(GridPosition Position, int Radius)> observations,
        Func<GridPosition, bool>? exclude = null)
    {
        ArgumentNullException.ThrowIfNull(observations);
        EnsureLayerCapacity();
        var observationArray = observations.ToArray();
        if (observationArray.Any(observation => observation.Radius <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(observations));
        }

        foreach (var (observation, radius) in observationArray)
        {
            var radiusSquared = checked(radius * radius);
            for (var y = observation.Y - radius; y <= observation.Y + radius; y++)
            {
                for (var x = observation.X - radius; x <= observation.X + radius; x++)
                {
                    var position = new GridPosition(x, y, observation.Z);
                    var distanceSquared = checked(
                        ((x - observation.X) * (x - observation.X)) +
                        ((y - observation.Y) * (y - observation.Y)));
                    if (distanceSquared > radiusSquared ||
                        !IsVisibilityPosition(position) ||
                        (exclude?.Invoke(position) ?? false))
                    {
                        continue;
                    }

                    var index = GetIndex(position);
                    TrackDiscovery(index, isVerticalSeed: true);
                }
            }
        }
    }

    internal void DiscoverOpenVerticalColumns(
        int minimumLevel,
        int maximumLevel,
        ulong topologyVersion,
        Func<GridPosition, GridPosition, bool> canSeeVertically)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimumLevel, maximumLevel);
        ArgumentNullException.ThrowIfNull(canSeeVertically);
        EnsureLayerCapacity(maximumLevel);
        if (_verticalDiscoveryTopologyVersion != topologyVersion)
        {
            _pendingVerticalDiscoverySeeds.Clear();
            _verticalDiscoverySeeds.Clear();
            foreach (var index in _visibleIndices)
            {
                var seed = GetPosition(index);
                _verticalDiscoverySeeds.Add(seed);
                _pendingVerticalDiscoverySeeds.Enqueue(seed);
            }
            _verticalDiscoveryTopologyVersion = topologyVersion;
        }

        var processed = new HashSet<GridPosition>();
        while (_pendingVerticalDiscoverySeeds.TryDequeue(out var source))
        {
            if (!processed.Add(source))
            {
                continue;
            }

            var lower = source;
            while (lower.Z > minimumLevel)
            {
                var next = lower with { Z = lower.Z - 1 };
                if (!canSeeVertically(lower, next))
                {
                    break;
                }

                DiscoverExact(next);
                lower = next;
            }

            var upper = source;
            while (upper.Z < maximumLevel)
            {
                var next = upper with { Z = upper.Z + 1 };
                DiscoverExact(next);
                if (!canSeeVertically(next, upper))
                {
                    break;
                }

                upper = next;
            }
        }
    }

    internal void RevealOpenVerticalColumns(
        IEnumerable<(GridPosition Position, int Radius)> observers,
        int minimumLevel,
        int maximumLevel,
        ulong topologyVersion,
        Func<GridPosition, GridPosition, bool> canSeeVertically,
        Func<GridPosition, bool>? excludeOrigin = null)
    {
        ArgumentNullException.ThrowIfNull(observers);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimumLevel, maximumLevel);
        ArgumentNullException.ThrowIfNull(canSeeVertically);
        EnsureLayerCapacity(maximumLevel);
        var observerArray = observers
            .Distinct()
            .OrderBy(observer => observer.Position.Z)
            .ThenBy(observer => observer.Position.Y)
            .ThenBy(observer => observer.Position.X)
            .ThenBy(observer => observer.Radius)
            .ToArray();
        if (observerArray.Any(observer => observer.Radius <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(observers));
        }

        if (_verticalRevealTopologyVersion != topologyVersion ||
            _verticalRevealMinimumLevel != minimumLevel ||
            _verticalRevealMaximumLevel != maximumLevel ||
            !_verticalRevealObservers.SequenceEqual(observerArray))
        {
            _verticalRevealObservers = observerArray;
            _verticalRevealMinimumLevel = minimumLevel;
            _verticalRevealMaximumLevel = maximumLevel;
            _verticalRevealTopologyVersion = topologyVersion;
            _verticalRevealPositions = BuildOpenVerticalRevealPositions(
                observerArray,
                minimumLevel,
                maximumLevel,
                canSeeVertically,
                excludeOrigin);
        }

        foreach (var position in _verticalRevealPositions)
        {
            RevealExact(position);
        }
    }

    private GridPosition[] BuildOpenVerticalRevealPositions(
        IReadOnlyList<(GridPosition Position, int Radius)> observers,
        int minimumLevel,
        int maximumLevel,
        Func<GridPosition, GridPosition, bool> canSeeVertically,
        Func<GridPosition, bool>? excludeOrigin)
    {
        var origins = new HashSet<GridPosition>();
        foreach (var (observer, radius) in observers)
        {
            var radiusSquared = checked(radius * radius);
            for (var y = observer.Y - radius; y <= observer.Y + radius; y++)
            {
                for (var x = observer.X - radius; x <= observer.X + radius; x++)
                {
                    var origin = new GridPosition(x, y, observer.Z);
                    var distanceSquared = checked(
                        ((x - observer.X) * (x - observer.X)) +
                        ((y - observer.Y) * (y - observer.Y)));
                    if (distanceSquared <= radiusSquared && IsVisibilityPosition(origin) &&
                        !(excludeOrigin?.Invoke(origin) ?? false))
                    {
                        origins.Add(origin);
                    }
                }
            }
        }

        var visible = new HashSet<GridPosition>();
        foreach (var origin in origins)
        {
            visible.Add(origin);
            var lower = origin;
            while (lower.Z > minimumLevel)
            {
                var next = lower with { Z = lower.Z - 1 };
                if (!canSeeVertically(lower, next))
                {
                    break;
                }

                visible.Add(next);
                lower = next;
            }

            var upper = origin;
            while (upper.Z < maximumLevel)
            {
                var next = upper with { Z = upper.Z + 1 };
                visible.Add(next);
                if (!canSeeVertically(next, upper))
                {
                    break;
                }

                upper = next;
            }
        }

        return visible
            .OrderBy(position => position.Z)
            .ThenBy(position => position.Y)
            .ThenBy(position => position.X)
            .ToArray();
    }

    private bool IsVisibilityPosition(GridPosition position) =>
        Map.IsColumnWithin(position) &&
        position.Z >= -_materializedNegativeLevelCount &&
        position.Z <= _materializedPositiveLevelCount;

    private void EnsureLayerCapacity(int? maximumLevel = null)
    {
        var targetNegativeLevelCount = Map.MaterializedNegativeLevelCount;
        var targetPositiveLevelCount = Math.Max(
            _materializedPositiveLevelCount,
            Math.Max(Map.MaterializedPositiveLevelCount, Math.Max(0, maximumLevel ?? 0)));
        if (_materializedNegativeLevelCount == targetNegativeLevelCount &&
            _materializedPositiveLevelCount == targetPositiveLevelCount)
        {
            return;
        }

        _cells = ExpandLayers(
            _cells,
            Map.CellCount,
            _materializedNegativeLevelCount,
            targetNegativeLevelCount,
            targetPositiveLevelCount);
        _materializedNegativeLevelCount = targetNegativeLevelCount;
        _materializedPositiveLevelCount = targetPositiveLevelCount;
        _visibleIndices.Clear();
        _visibleIndices.AddRange(Enumerable.Range(0, _cells.Length)
            .Where(index => _cells[index] == CellVisibility.Visible));
        _discoveredIndices.Clear();
        foreach (var index in Enumerable.Range(0, _cells.Length)
                     .Where(index => _cells[index] != CellVisibility.Unknown))
        {
            _discoveredIndices.Add(index);
        }
        _verticalDiscoverySeeds.Clear();
        _pendingVerticalDiscoverySeeds.Clear();
        foreach (var index in _visibleIndices)
        {
            var seed = GetPosition(index);
            _verticalDiscoverySeeds.Add(seed);
            _pendingVerticalDiscoverySeeds.Enqueue(seed);
        }
        _verticalDiscoveryTopologyVersion = null;
    }

    private int GetIndex(GridPosition position) => checked(
        ((position.Z <= 0 ? -position.Z : Map.MaterializedNegativeLevelCount + position.Z) * Map.CellCount) +
        (position.Y * Map.Width) + position.X);

    private GridPosition GetPosition(int index)
    {
        var layer = index / Map.CellCount;
        var cell = index % Map.CellCount;
        var level = layer <= Map.MaterializedNegativeLevelCount
            ? -layer
            : layer - Map.MaterializedNegativeLevelCount;
        return new GridPosition(cell % Map.Width, cell / Map.Width, level);
    }

    private void DiscoverExact(GridPosition position)
    {
        if (!IsVisibilityPosition(position))
        {
            return;
        }

        TrackDiscovery(GetIndex(position), isVerticalSeed: false);
    }

    private void RevealExact(GridPosition position)
    {
        if (!IsVisibilityPosition(position))
        {
            return;
        }

        var index = GetIndex(position);
        if (_cells[index] == CellVisibility.Visible)
        {
            return;
        }

        TrackDiscovery(index, isVerticalSeed: false);
        _cells[index] = CellVisibility.Visible;
        _visibleIndices.Add(index);
    }

    private void TrackDiscovery(int index, bool isVerticalSeed)
    {
        if (_cells[index] == CellVisibility.Unknown)
        {
            _cells[index] = CellVisibility.Explored;
            _discoveredIndices.Add(index);
        }

        var position = GetPosition(index);
        if (isVerticalSeed && _verticalDiscoverySeeds.Add(position))
        {
            _pendingVerticalDiscoverySeeds.Enqueue(position);
        }
    }

    private static CellVisibility[] ExpandLayers(
        CellVisibility[] source,
        int cellCount,
        int sourceNegativeLevelCount,
        int targetNegativeLevelCount,
        int positiveLevelCount)
    {
        var result = new CellVisibility[checked(
            cellCount * (targetNegativeLevelCount + positiveLevelCount + 1))];
        var nonPositiveLength = checked(
            cellCount * (Math.Min(sourceNegativeLevelCount, targetNegativeLevelCount) + 1));
        Array.Copy(source, result, Math.Min(nonPositiveLength, source.Length));

        var sourceLayerCount = source.Length / cellCount;
        var sourcePositiveLevelCount = Math.Max(0, sourceLayerCount - sourceNegativeLevelCount - 1);
        var copiedPositiveLevelCount = Math.Min(sourcePositiveLevelCount, positiveLevelCount);
        if (copiedPositiveLevelCount > 0)
        {
            Array.Copy(
                source,
                checked(cellCount * (sourceNegativeLevelCount + 1)),
                result,
                checked(cellCount * (targetNegativeLevelCount + 1)),
                checked(cellCount * copiedPositiveLevelCount));
        }

        return result;
    }
}
