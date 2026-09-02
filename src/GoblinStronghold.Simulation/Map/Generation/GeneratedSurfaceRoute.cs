using System.Collections.ObjectModel;

namespace GoblinStronghold.Simulation.Map.Generation;

public enum GeneratedSurfaceRouteRole : byte
{
    ThroughRoad = 1,
    JunctionBranch = 2,
}

public enum SurfaceRouteEndpoint : byte
{
    Junction = 0,
    NorthEdge = 1,
    EastEdge = 2,
    SouthEdge = 3,
    WestEdge = 4,
}

public sealed class GeneratedSurfaceRoute
{
    public GeneratedSurfaceRoute(
        GeneratedSurfaceRouteRole role,
        SurfaceRouteEndpoint entry,
        SurfaceRouteEndpoint exit,
        IEnumerable<GridPosition> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var positions = path.ToArray();
        if (positions.Length == 0)
        {
            throw new ArgumentException("A generated surface route cannot be empty.", nameof(path));
        }

        Role = role;
        Entry = entry;
        Exit = exit;
        Path = new ReadOnlyCollection<GridPosition>(positions);
    }

    public GeneratedSurfaceRouteRole Role { get; }

    public SurfaceRouteEndpoint Entry { get; }

    public SurfaceRouteEndpoint Exit { get; }

    public IReadOnlyList<GridPosition> Path { get; }

    public GridPosition EntryPosition => Path[0];

    public GridPosition ExitPosition => Path[^1];

    public IReadOnlyList<GridPosition> CreateApproach(
        SurfaceRouteEndpoint from,
        int maximumCellCount)
    {
        if (maximumCellCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCellCount));
        }
        if (from != Entry && from != Exit)
        {
            throw new ArgumentException(
                $"Endpoint {from} does not belong to route {Role}.",
                nameof(from));
        }

        return from == Entry
            ? Path.Take(maximumCellCount).ToArray()
            : Path.Reverse().Take(maximumCellCount).ToArray();
    }
}
