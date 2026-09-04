using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Shelter;

public static class GoblinSleepingPlacePolicy
{
    public static GoblinSleepingPlaceOptions CreateOptions(
        IEnumerable<WorldObjectSnapshot> worldObjects,
        IReadOnlySet<GridPosition> reservedSleepingMats,
        IReadOnlySet<GridPosition> shelterFloorCells,
        Func<GridPosition, bool> isTerrainTraversable,
        Func<GridPosition, bool> isOpenToSky)
    {
        ArgumentNullException.ThrowIfNull(worldObjects);
        ArgumentNullException.ThrowIfNull(reservedSleepingMats);
        ArgumentNullException.ThrowIfNull(shelterFloorCells);
        ArgumentNullException.ThrowIfNull(isTerrainTraversable);
        ArgumentNullException.ThrowIfNull(isOpenToSky);

        var sleepingMats = worldObjects
            .Where(worldObject =>
                worldObject.Owner == WorldObjectOwner.GoblinTribe &&
                worldObject.Kind is WorldObjectKind.ReedSleepingMat or
                    WorldObjectKind.WoodenWatchtower)
            .SelectMany(worldObject => worldObject.GetAbsoluteParts()
                .Where(item => item.Part.Kind == WorldObjectPartKind.SleepingMat)
                .Select(item => (item.Position,
                    IsBuiltIn: worldObject.Kind == WorldObjectKind.WoodenWatchtower)))
            .Where(item => isTerrainTraversable(item.Position))
            .ToArray();
        var allSleepingMats = sleepingMats.Select(item => item.Position).ToHashSet();
        var availableSleepingMats = allSleepingMats
            .Where(position => !reservedSleepingMats.Contains(position))
            .ToArray();
        var builtInSleepingMats = sleepingMats
            .Where(item => item.IsBuiltIn)
            .Select(item => item.Position)
            .ToHashSet();

        return new GoblinSleepingPlaceOptions(
            availableSleepingMats
                .Where(position => builtInSleepingMats.Contains(position) || !isOpenToSky(position))
                .ToHashSet(),
            availableSleepingMats
                .Where(position => !builtInSleepingMats.Contains(position) && isOpenToSky(position))
                .ToHashSet(),
            shelterFloorCells
                .Where(isTerrainTraversable)
                .Where(position => !allSleepingMats.Contains(position))
                .ToHashSet());
    }
}

public sealed record GoblinSleepingPlaceOptions(
    IReadOnlySet<GridPosition> CoveredSleepingMats,
    IReadOnlySet<GridPosition> ExposedSleepingMats,
    IReadOnlySet<GridPosition> ShelterFloorFallback);
