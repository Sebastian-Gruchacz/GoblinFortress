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
            .SelectMany(passage => GetDiscoveries(passage, visibilityAt))
            .Distinct()
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
}
