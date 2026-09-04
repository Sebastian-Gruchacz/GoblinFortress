using GoblinStronghold.Simulation.Equipment;
using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Terrain.Jobs;

internal static class ExcavationCapabilityPolicy
{
    public static bool CanExcavate(
        CaveCell material,
        PersonalEquipment equipment,
        int buildingExperience) => material.IsLooseMaterial
        ? ToolCapabilityCatalog.MeetsRequirement(
            equipment,
            ToolFunction.Earthmoving,
            minimumLevel: 1)
        : MiningCapabilityPolicy.CanMine(material, equipment, buildingExperience);

    public static int WorkMultiplier(CaveCell material) => material.IsLooseMaterial
        ? 1
        : MiningCapabilityPolicy.WorkMultiplier(material.Rock);
}
