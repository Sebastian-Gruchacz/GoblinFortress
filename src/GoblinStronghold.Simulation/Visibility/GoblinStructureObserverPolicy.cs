using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Shelter;

namespace GoblinStronghold.Simulation.Visibility;

public static class GoblinStructureObserverPolicy
{
    public static IReadOnlyList<(GridPosition Position, int Radius)> SelectObservers(
        IEnumerable<WorldObjectSnapshot> worldObjects,
        int shelterVisionRadius)
    {
        ArgumentNullException.ThrowIfNull(worldObjects);
        ArgumentOutOfRangeException.ThrowIfNegative(shelterVisionRadius);

        return worldObjects
            .Where(worldObject => worldObject.Owner == WorldObjectOwner.GoblinTribe)
            .Select(worldObject => worldObject.Kind switch
            {
                _ when shelterVisionRadius > 0 && GoblinShelterPolicy.IsShelter(worldObject) =>
                    (Position: worldObject.Anchor, Radius: shelterVisionRadius),
                _ => default,
            })
            .Where(observer => observer.Radius > 0)
            .OrderByDescending(observer => observer.Position.Z)
            .ThenBy(observer => observer.Position.Y)
            .ThenBy(observer => observer.Position.X)
            .ToArray();
    }
}
