using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class ActiveEatingTests
{
    [Theory]
    [InlineData(FoodKind.Berries, 2_800)]
    [InlineData(FoodKind.Mushrooms, 3_400)]
    [InlineData(FoodKind.EdibleRoots, 4_200)]
    [InlineData(FoodKind.Fish, 4_800)]
    [InlineData(FoodKind.DriedRations, 5_700)]
    public void FoodKindsRestoreTheirConfiguredSatiety(FoodKind foodKind, int expectedSatiety)
    {
        var engine = CreateEngine(goblinCount: 1, initialFood: 1, initialHunger: 6_500);
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        save["itemStacks"]![0]!["foodKind"] = (int)foodKind;
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);

        Assert.Equal(expectedSatiety, engine.Definitions.Food.GetSatiety(foodKind));
        for (var tick = 0; tick < engine.Definitions.EatWorkTicks + 2; tick++)
        {
            var hungerBefore = Assert.Single(engine.CreateSnapshot().Actors).Hunger;
            engine.AdvanceTicks(1);
            if (engine.DrainEvents().Any(item => item.Kind == SimulationEventKind.ActorAte))
            {
                var hungerAfter = Assert.Single(engine.CreateSnapshot().Actors).Hunger;
                Assert.Equal(
                    expectedSatiety,
                    hungerBefore + engine.Definitions.HungerPerTick - hungerAfter);
                return;
            }
        }

        throw new Xunit.Sdk.XunitException("The goblin did not finish the meal in time.");
    }

    [Fact]
    public void HungryGoblinsReserveOnlyAvailablePortions()
    {
        var engine = CreateEngine(goblinCount: 3, initialFood: 2, initialHunger: 6_500);

        engine.AdvanceTicks(1);

        var actors = engine.CreateSnapshot().Actors;
        var eating = actors.Where(actor => actor.Job.Kind == ActorJobKind.Eat).ToArray();
        Assert.Equal(2, eating.Length);
        Assert.Equal(2, eating.Sum(actor => actor.Job.ReservedQuantity));
        Assert.Single(eating.Select(actor => actor.Job.SourceStackId).Distinct());
        Assert.Contains(actors, actor => actor.Job.Kind == ActorJobKind.Forage);
    }

    [Fact]
    public void EatingTakesTimeConsumesPhysicalFoodAndSurvivesSaveLoad()
    {
        var engine = CreateEngine(goblinCount: 1, initialFood: 1, initialHunger: 6_500);
        engine.AdvanceTicks(1);

        var eating = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.Equal(ActorJobKind.Eat, eating.Job.Kind);
        Assert.Equal(SimulationDefinitions.Foundation.EatWorkTicks - 1, eating.Job.RemainingWorkTicks);

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());

        engine.AdvanceTicks(SimulationDefinitions.Foundation.EatWorkTicks - 1);
        restored.AdvanceTicks(SimulationDefinitions.Foundation.EatWorkTicks - 1);

        Assert.Empty(engine.CreateSnapshot().ItemStacks);
        Assert.True(Assert.Single(engine.CreateSnapshot().Actors).Hunger < 3_000);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        var events = engine.DrainEvents();
        Assert.Contains(
            events,
            simulationEvent => simulationEvent.Kind == SimulationEventKind.ActorAte);
        Assert.Equal(events, restored.DrainEvents());
    }

    [Fact]
    public void HungryGoblinCanFetchFoodFromStorage()
    {
        var engine = CreateEngine(goblinCount: 1, initialFood: 3, initialHunger: 0);
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1),
            sequence: 1,
            engine.Map.GoblinSpawn,
            ResourceKind.Food,
            capacity: 3));
        engine.AdvanceTicks(2 * SimulationDefinitions.Foundation.HaulHandlingTicks);

        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        save["actors"]![0]!["hunger"] = SimulationDefinitions.Foundation.FoodSeekThreshold;
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);

        engine.AdvanceTicks(1);

        var actor = Assert.Single(engine.CreateSnapshot().Actors);
        var source = Assert.Single(engine.CreateSnapshot().ItemStacks, stack =>
            stack.Id == actor.Job.SourceStackId);
        Assert.Equal(ActorJobKind.Eat, actor.Job.Kind);
        Assert.Equal(ItemLocationKind.StorageZone, source.Location.Kind);
    }

    private static SimulationEngine CreateEngine(
        int goblinCount,
        int initialFood,
        int initialHunger)
    {
        var seed = new WorldSeed(0x454154UL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        return SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: goblinCount,
            initialFoodStock: initialFood,
            initialHunger: initialHunger);
    }
}
