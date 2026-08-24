using GoblinStronghold.Simulation.Map;
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

        engine.AdvanceTicks(engine.Definitions.TicksPerDay - 1);
        var beforeDayBoundary = engine.CreateSnapshot().HumanVillage;
        Assert.Equal(48, beforeDayBoundary.FoodStock);
        Assert.Equal(24, beforeDayBoundary.WoodStock);
        Assert.Equal(4, beforeDayBoundary.GoodsStock);

        engine.AdvanceTicks(1);
        var afterDayBoundary = engine.CreateSnapshot().HumanVillage;
        Assert.Equal(52, afterDayBoundary.FoodStock);
        Assert.Equal(28, afterDayBoundary.WoodStock);
        Assert.Equal(5, afterDayBoundary.GoodsStock);
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
}
