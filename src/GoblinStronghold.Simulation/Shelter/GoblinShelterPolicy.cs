using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Shelter;

internal static class GoblinShelterPolicy
{
    public static bool IsPermanentShelter(WorldObjectSnapshot worldObject) =>
        worldObject.Owner == WorldObjectOwner.GoblinTribe &&
        worldObject.Kind is WorldObjectKind.GoblinHut or WorldObjectKind.GoblinRuin;

    public static bool IsShelter(WorldObjectSnapshot worldObject) =>
        IsPermanentShelter(worldObject) ||
        worldObject.Owner == WorldObjectOwner.GoblinTribe &&
        worldObject.Kind == WorldObjectKind.GoblinFieldCamp;

    public static IEnumerable<GridPosition> EnumerateFloorCells(
        WorldObjectSnapshot worldObject) =>
        worldObject.GetAbsoluteParts()
            .Where(item => item.Part.Kind is
                WorldObjectPartKind.Floor or WorldObjectPartKind.Door)
            .Select(item => item.Position);

    public static int CalculateCapacity(IEnumerable<WorldObjectSnapshot> worldObjects)
    {
        var objects = worldObjects
            .Where(item => item.Owner == WorldObjectOwner.GoblinTribe)
            .ToArray();
        var enclosedFloorCapacity = objects
            .Where(IsPermanentShelter)
            .SelectMany(EnumerateFloorCells)
            .Distinct()
            .Count();
        var fieldCampCapacity = objects.Count(item =>
            item.Kind == WorldObjectKind.GoblinFieldCamp) *
            SimulationDefinitions.FieldCampCapacity;
        return checked(enclosedFloorCapacity + fieldCampCapacity);
    }
}
