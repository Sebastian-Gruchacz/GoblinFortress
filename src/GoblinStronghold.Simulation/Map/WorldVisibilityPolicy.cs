using GoblinStronghold.Simulation.Civilizations;

namespace GoblinStronghold.Simulation.Map;

public static class WorldVisibilityPolicy
{
    public const int AdjacentLayerDiscoveryRadius = 1;

    public static int ResolveGoblinVisionRadius(
        CivilizationPerceptionDefinition perception,
        GridPosition position,
        bool isSurfaceNight)
    {
        ArgumentNullException.ThrowIfNull(perception);
        return position.Z < 0 || isSurfaceNight
            ? perception.NightVisionRadius
            : perception.DayVisionRadius;
    }

    public static IReadOnlyList<(GridPosition Position, int Radius)>
        SelectAdjacentLayerDiscoveries(
            IEnumerable<VerticalPassage> passages,
            Func<GridPosition, CellVisibility> visibilityAt)
    {
        ArgumentNullException.ThrowIfNull(passages);
        ArgumentNullException.ThrowIfNull(visibilityAt);

        return passages
            .Where(VerticalPassageOpennessPolicy.IsOpen)
            .SelectMany(passage => GetDiscoveries(passage, visibilityAt))
            .Distinct()
            .OrderByDescending(item => item.Position.Z)
            .ThenBy(item => item.Position.Y)
            .ThenBy(item => item.Position.X)
            .ToArray();
    }

    public static IReadOnlyList<(GridPosition Position, int Radius)>
        SelectEdgeLookDiscoveries(
            IEnumerable<(GridPosition Position, int Radius)> observers,
            Func<GridPosition, bool> isTraversable,
            Func<GridPosition, bool> isOpenUnsupportedVolume,
            Func<GridPosition, bool> isWorldPosition)
    {
        ArgumentNullException.ThrowIfNull(observers);
        ArgumentNullException.ThrowIfNull(isTraversable);
        ArgumentNullException.ThrowIfNull(isOpenUnsupportedVolume);
        ArgumentNullException.ThrowIfNull(isWorldPosition);
        var observerArray = observers.ToArray();
        if (observerArray.Any(observer => observer.Radius <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(observers));
        }

        return observerArray
            .Where(observer => isTraversable(observer.Position))
            .SelectMany(observer => GetCardinalNeighbors(observer.Position)
                .Select(edge => (Edge: edge, observer.Radius)))
            .Where(item => isOpenUnsupportedVolume(item.Edge))
            .Select(item => (
                Position: item.Edge with { Z = item.Edge.Z - 1 },
                item.Radius))
            .Where(item => isWorldPosition(item.Position))
            .GroupBy(item => item.Position)
            .Select(group => (
                Position: group.Key,
                Radius: group.Max(item => item.Radius)))
            .OrderByDescending(item => item.Position.Z)
            .ThenBy(item => item.Position.Y)
            .ThenBy(item => item.Position.X)
            .ToArray();
    }

    private static IEnumerable<(GridPosition Position, int Radius)> GetDiscoveries(
        VerticalPassage passage,
        Func<GridPosition, CellVisibility> visibilityAt)
    {
        if (visibilityAt(passage.Upper) == CellVisibility.Visible)
        {
            yield return (passage.Lower, AdjacentLayerDiscoveryRadius);
        }
        if (visibilityAt(passage.Lower) == CellVisibility.Visible)
        {
            yield return (passage.Upper, AdjacentLayerDiscoveryRadius);
        }
    }

    private static IEnumerable<GridPosition> GetCardinalNeighbors(GridPosition position)
    {
        yield return position with { Y = position.Y - 1 };
        yield return position with { X = position.X + 1 };
        yield return position with { Y = position.Y + 1 };
        yield return position with { X = position.X - 1 };
    }
}
