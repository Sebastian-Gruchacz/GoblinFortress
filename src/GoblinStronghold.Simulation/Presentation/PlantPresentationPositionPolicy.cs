using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Presentation;

public static class PlantPresentationPositionPolicy
{
    public static GridPosition Resolve(GeneratedMap map, PlantPatchSnapshot plant)
    {
        ArgumentNullException.ThrowIfNull(map);
        return plant.Position.Z < 0
            ? plant.Position
            : map.GetTerrainSurfacePosition(plant.Position);
    }
}
