using GoblinStronghold.Simulation.Map;
using System.Text.Json.Nodes;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class HumanVillageTests
{
    [Fact]
    public void VillageStartsAsThreeStablePopulationCohorts()
    {
        var engine = CreateEngine();
        var village = engine.CreateSnapshot().HumanVillage;

        Assert.Equal(engine.Map.HumanVillage, village.Anchor);
        Assert.Equal(12, village.Population);
        Assert.Equal(village.Population, village.Cohorts.Sum(cohort => cohort.Population));
        Assert.Equal(
            [HumanCohortRole.Farmers, HumanCohortRole.Workers, HumanCohortRole.Guards],
            village.Cohorts.Select(cohort => cohort.Role));
        Assert.All(village.Cohorts, cohort => Assert.True(engine.World.IsSurfaceTraversable(cohort.Position)));
    }

    [Fact]
    public void VillageProducesAndConsumesStocksOncePerDay()
    {
        var engine = CreateEngine();

        engine = JumpToNextDayBoundary(engine);
        var beforeDayBoundary = engine.CreateSnapshot().HumanVillage;
        Assert.Equal(48, beforeDayBoundary.FoodStock);
        Assert.Equal(24, beforeDayBoundary.WoodStock);
        Assert.Equal(4, beforeDayBoundary.GoodsStock);

        engine.AdvanceTicks(1);
        var afterDayBoundary = engine.CreateSnapshot().HumanVillage;
        Assert.Equal(36, afterDayBoundary.FoodStock);
        Assert.Equal(42, afterDayBoundary.WaterStock);
        Assert.Equal(24, afterDayBoundary.WoodStock);
        Assert.Equal(4, afterDayBoundary.GoodsStock);
        Assert.All(afterDayBoundary.Fields, field => Assert.Equal(1, field.GrowthDays));
    }

    [Fact]
    public void DispatcherClearsEnoughFieldsForPopulationAndCropsNeedHalfAYear()
    {
        var engine = CreateEngine();
        var initial = engine.CreateSnapshot().HumanVillage;
        Assert.Equal(4, initial.Fields.Count);
        Assert.Equal(8, initial.PlannedFieldCount);
        Assert.All(initial.Fields, field => Assert.Null(engine.World.GetPlantPatch(field.Position)));

        engine = AdvanceVillageDays(engine, 20);
        var expanded = engine.CreateSnapshot().HumanVillage;
        Assert.Equal(expanded.PlannedFieldCount, expanded.Fields.Count);
        Assert.True(expanded.WoodStock > initial.WoodStock);
        Assert.DoesNotContain(expanded.Fields, field => field.Phase == HumanFieldPhase.Ripe);
        Assert.All(expanded.Fields, field => Assert.Null(engine.World.GetPlantPatch(field.Position)));

        engine = AdvanceVillageDays(engine, 100);
        Assert.Contains(engine.CreateSnapshot().HumanVillage.Fields,
            field => field.Phase == HumanFieldPhase.Ripe);

        engine = AdvanceVillageDays(engine, 1);
        var harvested = engine.CreateSnapshot();
        Assert.Equal(1, harvested.HumanVillage.StorehouseCount);
        var storehouse = Assert.Single(harvested.WorldObjects,
            item => item.Kind == WorldObjectKind.HumanStorehouse);
        Assert.Equal(WorldObjectOwner.HumanVillage, storehouse.Owner);
        Assert.Equal(9, storehouse.Parts.Count(item => item.Kind == WorldObjectPartKind.Floor));
        Assert.Contains(storehouse.Parts, item => item.Kind == WorldObjectPartKind.Door);
        Assert.Equal(480, harvested.HumanVillage.FoodCapacity);
        var door = storehouse.GetAbsoluteParts()
            .Single(item => item.Part.Kind == WorldObjectPartKind.Door).Position;
        Assert.True(engine.World.HasSurfacePath(engine.Map.HumanVillage, door));

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Single(restored.CreateSnapshot().WorldObjects,
            item => item.Kind == WorldObjectKind.HumanStorehouse);
    }

    [Fact]
    public void CohortsStayTraversableAndNearTheirVillage()
    {
        var engine = CreateEngine();

        engine.AdvanceTicks(800);

        var cohorts = engine.CreateSnapshot().HumanVillage.Cohorts;
        Assert.Equal(cohorts.Count, cohorts.Select(cohort => cohort.Position).Distinct().Count());
        Assert.All(cohorts, cohort =>
        {
            Assert.True(engine.World.IsSurfaceTraversable(cohort.Position));
            Assert.InRange(
                Math.Abs(cohort.Position.X - engine.Map.HumanVillage.X) +
                Math.Abs(cohort.Position.Y - engine.Map.HumanVillage.Y),
                0,
                engine.Definitions.HumanVillageActivityRadius);
        });
    }

    [Fact]
    public void SaveLoadPreservesVillageAndItsFuture()
    {
        var engine = CreateEngine();
        engine.AdvanceTicks(337);

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(engine.CreateSnapshot().HumanVillage.Cohorts, restored.CreateSnapshot().HumanVillage.Cohorts);

        engine.AdvanceTicks(500);
        restored.AdvanceTicks(500);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(
            engine.CreateSnapshot().HumanVillage.Cohorts,
            restored.CreateSnapshot().HumanVillage.Cohorts);
    }

    private static SimulationEngine CreateEngine()
    {
        var seed = new WorldSeed(0x48554D414EUL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        return SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 0);
    }

    private static SimulationEngine AdvanceVillageDays(SimulationEngine engine, int days)
    {
        for (var day = 0; day < days; day++)
        {
            engine = JumpToNextDayBoundary(engine);
            engine.AdvanceTicks(1);
        }
        return engine;
    }

    private static SimulationEngine JumpToNextDayBoundary(SimulationEngine engine)
    {
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        var nextBoundary = SimulationCalendar.NextDayStart(
            engine.CurrentTick,
            engine.Definitions.Clock);
        save["currentTick"] = nextBoundary.Value - 1;
        return SimulationEngine.Load(save.ToJsonString(), engine.Definitions);
    }
}
