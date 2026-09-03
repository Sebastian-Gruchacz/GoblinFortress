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

        var allSleepingMats = worldObjects
            .Where(worldObject =>
                worldObject.Owner == WorldObjectOwner.GoblinTribe &&
                worldObject.Kind == WorldObjectKind.ReedSleepingMat &&
                isTerrainTraversable(worldObject.Anchor))
            .Select(worldObject => worldObject.Anchor)
            .ToHashSet();
        var availableSleepingMats = allSleepingMats
            .Where(position => !reservedSleepingMats.Contains(position))
            .ToArray();

        return new GoblinSleepingPlaceOptions(
            availableSleepingMats.Where(position => !isOpenToSky(position)).ToHashSet(),
            availableSleepingMats.Where(isOpenToSky).ToHashSet(),
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
