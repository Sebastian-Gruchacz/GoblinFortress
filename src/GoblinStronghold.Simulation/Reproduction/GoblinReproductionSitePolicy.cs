using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Reproduction;

internal static class GoblinReproductionSitePolicy
{
    public static IEnumerable<GridPosition> EnumerateSites(
        IEnumerable<WorldObjectSnapshot> worldObjects,
        Func<GridPosition, bool> isTraversable,
        Func<GridPosition, bool> isMoist)
    {
        foreach (var worldObject in worldObjects.Where(item =>
                     item.Owner == WorldObjectOwner.GoblinTribe))
        {
            if (worldObject.Kind == WorldObjectKind.GoblinCompost &&
                isTraversable(worldObject.Anchor))
            {
                yield return worldObject.Anchor;
                continue;
            }

            if (worldObject.Kind != WorldObjectKind.GoblinHut)
            {
                continue;
            }

            foreach (var item in worldObject.GetAbsoluteParts().Where(item =>
                         item.Position.Z == 0 &&
                         item.Part.Kind is WorldObjectPartKind.Floor or WorldObjectPartKind.Door &&
                         isTraversable(item.Position) && isMoist(item.Position)))
            {
                yield return item.Position;
            }
        }
    }
}
