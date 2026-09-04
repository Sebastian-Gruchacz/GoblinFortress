using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Equipment;

namespace GoblinStronghold.Simulation.Terrain.Jobs;

internal static class TerrainWorkPolicy
{
    public static bool CanActorPerform(
        TerrainModificationDefinition definition,
        GridPosition target,
        WorldMapState world,
        GoblinSkill knownSkills,
        PersonalEquipment equipment,
        int buildingExperience,
        GridPosition? rampDestination = null)
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
                ExcavationCapabilityPolicy.CanExcavate(
                    world.Baseline.IsRockPosition(target)
                        ? world.Baseline.GetRockCell(target)
                        : new CaveCell(RockKind.Sandstone, CaveCellKind.SolidRock),
                    equipment,
                    buildingExperience),
            WorkDesignationKind.StripFloor =>
                world.CanStripFloor(target) &&
                (world.HasConstructedFloorSurface(target)
                    ? ToolCapabilityCatalog.MeetsRequirement(
                        equipment,
                        ToolFunction.Construction,
                        minimumLevel: 1)
                    : ExcavationCapabilityPolicy.CanExcavate(
                        world.GetFloorStrippingCell(target),
                        equipment,
                        buildingExperience)),
            WorkDesignationKind.CarveRampDown =>
                CanCarveRamp(
                    world,
                    target,
                    rampDestination,
                    carveDown: true,
                    equipment,
                    buildingExperience),
            WorkDesignationKind.CarveRampUp =>
                CanCarveRamp(
                    world,
                    target,
                    rampDestination,
                    carveDown: false,
                    equipment,
                    buildingExperience),
            _ => false,
        };
    }

    public static bool IsTargetExhausted(
        TerrainModificationDefinition definition,
        GridPosition target,
        WorldMapState world,
        WorldVisibilityState visibility,
        GridPosition? rampDestination = null)
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
            WorkDesignationKind.StripFloor => !world.CanStripFloor(target),
            WorkDesignationKind.CarveRampDown => !(rampDestination is { } lower
                ? world.CanCarveRampDown(target, lower)
                : world.CanCarveRampDown(target)),
            WorkDesignationKind.CarveRampUp => !(rampDestination is { } upper
                ? world.CanCarveRampUp(target, upper)
                : world.CanCarveRampUp(target)),
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
            WorkDesignationKind.StripFloor => ActorJobKind.StripFloor,
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
            ExcavationCapabilityPolicy.WorkMultiplier(excavationCell));
    }

    private static bool CanCarveRamp(
        WorldMapState world,
        GridPosition target,
        GridPosition? rampDestination,
        bool carveDown,
        PersonalEquipment equipment,
        int buildingExperience) =>
        (carveDown
            ? rampDestination is { } lower
                ? world.CanCarveRampDown(target, lower)
                : world.CanCarveRampDown(target)
            : rampDestination is { } upper
                ? world.CanCarveRampUp(target, upper)
                : world.CanCarveRampUp(target)) &&
        ExcavationCapabilityPolicy.CanExcavate(
            rampDestination is { } destination
                ? world.GetRampExcavationCell(target, destination, carveDown)
                : world.GetRampExcavationCell(target, carveDown),
            equipment,
            buildingExperience);
}
