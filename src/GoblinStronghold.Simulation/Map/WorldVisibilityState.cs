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
    private readonly CellVisibility[] _cells;

    private WorldVisibilityState(GeneratedMap map, CellVisibility[] cells)
    {
        Map = map;
        _cells = cells;
    }

    public GeneratedMap Map { get; }

    public int DiscoveredCellCount => _cells.Count(state => state != CellVisibility.Unknown);

    internal static WorldVisibilityState Create(GeneratedMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return new WorldVisibilityState(map, new CellVisibility[map.CellCount]);
    }

    internal static WorldVisibilityState Restore(
        GeneratedMap map,
        IEnumerable<CellVisibility> visibility)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(visibility);
        var cells = visibility.ToArray();
        if (cells.Length != map.CellCount || cells.Any(state => !Enum.IsDefined(state)))
        {
            throw new InvalidDataException("The save contains invalid fog-of-war state.");
        }

        return new WorldVisibilityState(map, cells);
    }

    public CellVisibility Get(GridPosition position)
    {
        if (!Map.IsWithin(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        return _cells[GetIndex(position)];
    }

    public IReadOnlyList<CellVisibility> CreateSnapshot() =>
        new ReadOnlyCollection<CellVisibility>((CellVisibility[])_cells.Clone());

    internal void Reveal(IEnumerable<GridPosition> observers, int radius)
    {
        ArgumentNullException.ThrowIfNull(observers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);
        Reveal(observers.Select(observer => (observer, radius)));
    }

    internal void Reveal(IEnumerable<(GridPosition Position, int Radius)> observers)
    {
        ArgumentNullException.ThrowIfNull(observers);
        var observerArray = observers.ToArray();
        if (observerArray.Any(observer => observer.Radius <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(observers));
        }

        for (var index = 0; index < _cells.Length; index++)
        {
            if (_cells[index] == CellVisibility.Visible)
            {
                _cells[index] = CellVisibility.Explored;
            }
        }

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
                    if (distanceSquared <= radiusSquared && Map.IsWithin(position))
                    {
                        _cells[GetIndex(position)] = CellVisibility.Visible;
                    }
                }
            }
        }
    }

    private int GetIndex(GridPosition position) => checked((position.Y * Map.Width) + position.X);
}
