using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation;

public static class MiningCapabilityPolicy
{
    public static bool HasPickaxe(PersonalEquipment equipment) =>
        GetPickaxeLevel(equipment) > 0;

    public static int RequiredSkillLevel(RockKind rock) =>
        GetRockMaterial(rock).Acquisition.MinimumSkillLevel;

    public static int RequiredToolLevel(RockKind rock) =>
        GetRockMaterial(rock).Acquisition.MinimumToolLevel;

    public static int WorkMultiplier(RockKind rock) =>
        GetRockMaterial(rock).Acquisition.WorkMultiplier;

    public static bool CanMine(
        CaveCell cell,
        PersonalEquipment equipment,
        int buildingExperience)
    {
        if (GetPickaxeLevel(equipment) < RequiredToolLevel(cell.Rock) ||
            GoblinExperienceSnapshot.GetLevel(buildingExperience) < RequiredSkillLevel(cell.Rock))
        {
            return false;
        }
        return true;
    }

    private static int GetPickaxeLevel(PersonalEquipment equipment) =>
        equipment.HasFlag(PersonalEquipment.ReinforcedPickaxe)
            ? 2
            : equipment.HasFlag(PersonalEquipment.PrimitivePickaxe) ? 1 : 0;

    private static MaterialDefinition GetRockMaterial(RockKind rock) =>
        MaterialCatalog.Get(rock switch
        {
            RockKind.Sandstone => ResourceVariant.Sandstone,
            RockKind.Granite => ResourceVariant.Granite,
            RockKind.Basalt => ResourceVariant.Basalt,
            RockKind.Obsidian => ResourceVariant.Obsidian,
            _ => throw new ArgumentOutOfRangeException(nameof(rock)),
        });
}
