using GoblinStronghold.Simulation.Terrain;
using GoblinStronghold.Simulation.Terrain.Jobs;
using GoblinStronghold.Simulation.Map;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class TerrainWorkPolicyTests
{
    [Theory]
    [InlineData(WorkDesignationKind.MineRock, ActorJobKind.MineRock)]
    [InlineData(WorkDesignationKind.CarveRampDown, ActorJobKind.CarveRamp)]
    [InlineData(WorkDesignationKind.CarveRampUp, ActorJobKind.CarveRamp)]
    public void EveryTerrainDefinitionMapsToItsLegacyActorJob(
        WorkDesignationKind designation,
        ActorJobKind expectedJob)
    {
        Assert.Equal(
            expectedJob,
            TerrainWorkPolicy.GetJobKind(TerrainModificationCatalog.Get(designation)));
    }

    [Fact]
    public void TerrainForecastUsesBuildingPreferenceAndSpecialistBonus()
    {
        Assert.Equal(
            9,
            TerrainWorkPolicy.GetForecastPreference(
                TerrainModificationCatalog.Get(WorkDesignationKind.MineRock),
                buildingPreference: 4,
                specialistBonus: 5));
    }

    [Theory]
    [InlineData(WorkDesignationKind.MineRock, 8)]
    [InlineData(WorkDesignationKind.CarveRampDown, 12)]
    [InlineData(WorkDesignationKind.CarveRampUp, 12)]
    public void WorkDurationKeepsActionAndRockMultipliers(
        WorkDesignationKind designation,
        int actionMultiplier)
    {
        var rockMultiplier = MiningCapabilityPolicy.WorkMultiplier(RockKind.Granite);

        Assert.Equal(
            10 * actionMultiplier * rockMultiplier,
            TerrainWorkPolicy.GetWorkTicks(
                TerrainModificationCatalog.Get(designation),
                new CaveCell(RockKind.Granite, CaveCellKind.SolidRock),
                baseWorkTicks: 10));
    }

    [Fact]
    public void ShovelMovesLooseMaterialButCannotMineRock()
    {
        var soil = new CaveCell(
            RockKind.Sandstone,
            CaveCellKind.SolidRock,
            LooseMaterial: LooseMaterialKind.Soil);
        var rock = new CaveCell(RockKind.Sandstone, CaveCellKind.SolidRock);

        Assert.True(ExcavationCapabilityPolicy.CanExcavate(
            soil, PersonalEquipment.WoodenShovel, buildingExperience: 0));
        Assert.False(ExcavationCapabilityPolicy.CanExcavate(
            rock, PersonalEquipment.WoodenShovel, buildingExperience: 1000));
        Assert.True(ExcavationCapabilityPolicy.CanExcavate(
            soil, PersonalEquipment.PrimitivePickaxe, buildingExperience: 0));
        Assert.Equal(1, ExcavationCapabilityPolicy.WorkMultiplier(soil));
    }

    [Fact]
    public void AssignmentIsCompleteBeforeActorMutation()
    {
        var route = new[] { new GridPosition(2, 3, -1) };
        var plan = new TerrainWorkPlan(
            new EntityId(7),
            route[0],
            route);

        var assignment = TerrainWorkAssignmentFactory.Create(
            TerrainModificationCatalog.Get(WorkDesignationKind.MineRock),
            plan,
            workTicks: 80);

        Assert.Equal(ActorJobKind.MineRock, assignment.JobKind);
        Assert.Equal(plan.DesignationId, assignment.DesignationId);
        Assert.Equal(plan.JobTarget, assignment.JobTarget);
        Assert.Same(route, assignment.Route);
        Assert.Equal(80, assignment.WorkTicks);
    }
}
