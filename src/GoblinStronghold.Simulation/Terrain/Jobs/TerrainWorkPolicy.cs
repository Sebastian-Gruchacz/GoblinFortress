using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Terrain.Jobs;

internal static class TerrainWorkPolicy
{
    public static bool CanActorPerform(
        TerrainModificationDefinition definition,
        GridPosition target,
        WorldMapState world,
        GoblinSkill knownSkills,
        PersonalEquipment equipment,
        int buildingExperience)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(world);

        if (!knownSkills.HasFlag(GoblinSkill.Building))
        {
            return false;
        }

        return definition.LegacyDesignation switch
        {
            WorkDesignationKind.MineRock =>
                world.CanExcavateRock(target) &&
                MiningCapabilityPolicy.CanMine(
                    world.Baseline.IsRockPosition(target)
                        ? world.Baseline.GetRockCell(target)
                        : new CaveCell(RockKind.Sandstone, CaveCellKind.SolidRock),
                    equipment,
                    buildingExperience),
            WorkDesignationKind.CarveRampDown =>
                CanCarveRamp(world, target, carveDown: true, equipment, buildingExperience),
            WorkDesignationKind.CarveRampUp =>
                CanCarveRamp(world, target, carveDown: false, equipment, buildingExperience),
            _ => false,
        };
    }

    public static bool IsTargetExhausted(
        TerrainModificationDefinition definition,
        GridPosition target,
        WorldMapState world,
        WorldVisibilityState visibility)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(visibility);

        return definition.LegacyDesignation switch
        {
            WorkDesignationKind.MineRock =>
                visibility.Get(target) != CellVisibility.Unknown &&
                !world.IsSolidRock(target) &&
                !world.IsTerrainRampIntact(target),
            WorkDesignationKind.CarveRampDown => !world.CanCarveRampDown(target),
            WorkDesignationKind.CarveRampUp => !world.CanCarveRampUp(target),
            _ => true,
        };
    }

    public static int GetForecastPreference(
        TerrainModificationDefinition definition,
        int buildingPreference,
        int specialistBonus)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return checked(buildingPreference + specialistBonus);
    }

    public static ActorJobKind GetJobKind(TerrainModificationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.LegacyDesignation switch
        {
            WorkDesignationKind.MineRock => ActorJobKind.MineRock,
            WorkDesignationKind.CarveRampDown or WorkDesignationKind.CarveRampUp =>
                ActorJobKind.CarveRamp,
            _ => ActorJobKind.None,
        };
    }

    public static int GetWorkTicks(
        TerrainModificationDefinition definition,
        CaveCell excavationCell,
        int baseWorkTicks)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseWorkTicks);

        return checked(
            baseWorkTicks * definition.Work.BaseTicksMultiplier *
            MiningCapabilityPolicy.WorkMultiplier(excavationCell.Rock));
    }

    private static bool CanCarveRamp(
        WorldMapState world,
        GridPosition target,
        bool carveDown,
        PersonalEquipment equipment,
        int buildingExperience) =>
        (carveDown ? world.CanCarveRampDown(target) : world.CanCarveRampUp(target)) &&
        MiningCapabilityPolicy.CanMine(
            world.GetRampExcavationCell(target, carveDown),
            equipment,
            buildingExperience);
}
