using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class PersonalSupplyTests
{
    [Fact]
    public void GoblinsPackPhysicalFoodWithoutChangingTribalTotal()
    {
        var engine = CreateEngine(goblinCount: 2, initialFood: 4);

        for (var tick = 0; tick < 200; tick++)
        {
            engine.AdvanceTicks(1);
            if (engine.CreateSnapshot().Actors.All(actor =>
                    actor.PersonalFood == SimulationDefinitions.Foundation.PersonalFoodCapacity))
            {
                break;
            }
        }

        var snapshot = engine.CreateSnapshot();
        Assert.All(snapshot.Actors, actor =>
        {
            Assert.Equal(SimulationDefinitions.Foundation.PersonalFoodCapacity, actor.PersonalFood);
            Assert.Equal(FoodKind.DriedRations, actor.PersonalFoodKind);
        });
        Assert.Empty(snapshot.ItemStacks);
        Assert.Equal(4, snapshot.FoodStock);
        Assert.Contains(engine.DrainEvents(), item =>
            item.Kind == SimulationEventKind.ActorProvisionedFood);
    }

    [Fact]
    public void GoblinWithEmptyContainerFetchesWaterAndCanDrinkIt()
    {
        var engine = CreateEngine(goblinCount: 1, initialFood: 0);
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        save["actors"]![0]!["personalWater"] = 0;
        save["actors"]![0]!["thirst"] = SimulationDefinitions.Foundation.DrinkThreshold;
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);

        for (var tick = 0; tick < 200; tick++)
        {
            engine.AdvanceTicks(1);
            if (engine.DrainEvents().Any(item => item.Kind == SimulationEventKind.ActorDrank))
            {
                break;
            }
        }

        var actor = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.True(actor.Thirst < SimulationDefinitions.Foundation.DrinkThreshold);
        Assert.InRange(actor.PersonalWater, 0, SimulationDefinitions.Foundation.PersonalWaterCapacity);
    }

    [Fact]
    public void SaveLoadPreservesPersonalSuppliesAndTheirFutureConsumption()
    {
        var engine = CreateEngine(goblinCount: 1, initialFood: 2);
        engine.AdvanceTicks(SimulationDefinitions.Foundation.ResupplyWorkTicks);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        engine.AdvanceTicks(300);
        restored.AdvanceTicks(300);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(engine.CreateSnapshot().Actors, restored.CreateSnapshot().Actors);
    }

    private static SimulationEngine CreateEngine(int goblinCount, int initialFood)
    {
        var seed = new WorldSeed(0x5041434BUL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        return SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: goblinCount,
            initialFoodStock: initialFood);
    }
}
