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
    private int _materializedNegativeLevelCount;

    private WorldVisibilityState(GeneratedMap map, CellVisibility[] cells)
    {
        Map = map;
        _cells = cells;
        _materializedNegativeLevelCount = map.MaterializedNegativeLevelCount;
        _visibleIndices = Enumerable.Range(0, cells.Length)
            .Where(index => cells[index] == CellVisibility.Visible)
            .ToList();
    }

    public GeneratedMap Map { get; }

    public int DiscoveredCellCount
    {
        get
        {
            EnsureLayerCapacity();
            return _cells.Count(state => state != CellVisibility.Unknown);
        }
    }

    internal static WorldVisibilityState Create(GeneratedMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return new WorldVisibilityState(
            map,
            new CellVisibility[checked(map.CellCount *
                (map.MaterializedNegativeLevelCount + map.MaterializedPositiveLevelCount + 1))]);
    }

    internal static WorldVisibilityState Restore(
        GeneratedMap map,
        IEnumerable<CellVisibility> visibility,
        int? savedNegativeLevelCount = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(visibility);
        var cells = visibility.ToArray();
        var legacyLength = checked(map.CellCount * (map.CaveLevelCount + 1));
        var previousExpectedLength = checked(map.CellCount *
            (map.CaveLevelCount + map.MaterializedPositiveLevelCount + 1));
        var expectedLength = checked(map.CellCount *
            (map.MaterializedNegativeLevelCount + map.MaterializedPositiveLevelCount + 1));
        var savedExpectedLength = savedNegativeLevelCount is null
            ? expectedLength
            : checked(map.CellCount *
                (savedNegativeLevelCount.Value + map.MaterializedPositiveLevelCount + 1));
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
                map.MaterializedPositiveLevelCount);
        }

        return new WorldVisibilityState(map, cells);
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
                            _cells[index] = CellVisibility.Visible;
                            _visibleIndices.Add(index);
                        }
                    }
                }
            }
        }
    }

    private bool IsVisibilityPosition(GridPosition position) =>
        Map.IsWithin(position) || Map.IsCavePosition(position) ||
        Map.IsHillMassPosition(position) ||
        Map.IsTerrainSurfacePosition(position);

    private void EnsureLayerCapacity()
    {
        var targetNegativeLevelCount = Map.MaterializedNegativeLevelCount;
        if (_materializedNegativeLevelCount == targetNegativeLevelCount)
        {
            return;
        }

        _cells = ExpandLayers(
            _cells,
            Map.CellCount,
            _materializedNegativeLevelCount,
            targetNegativeLevelCount,
            Map.MaterializedPositiveLevelCount);
        _materializedNegativeLevelCount = targetNegativeLevelCount;
        _visibleIndices.Clear();
        _visibleIndices.AddRange(Enumerable.Range(0, _cells.Length)
            .Where(index => _cells[index] == CellVisibility.Visible));
    }

    private int GetIndex(GridPosition position) => checked(
        ((position.Z <= 0 ? -position.Z : Map.MaterializedNegativeLevelCount + position.Z) * Map.CellCount) +
        (position.Y * Map.Width) + position.X);

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
