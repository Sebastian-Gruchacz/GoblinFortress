using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class StarvationTests
{
    [Fact]
    public void StarvationDamagesHealthAndEventuallyKillsActor()
    {
        var engine = CreateEngine(initialHunger: 10_000, initialHealth: 100);

        engine.AdvanceTicks(1);
        Assert.Equal(60, Assert.Single(engine.CreateSnapshot().Actors).Health);

        engine.AdvanceTicks(2);

        Assert.Empty(engine.CreateSnapshot().Actors);
        Assert.Contains(
            engine.DrainEvents(),
            simulationEvent => simulationEvent.Kind == SimulationEventKind.ActorDied);
    }

    [Fact]
    public void DeathCancelsFutureCommandsAndLeavesLoadableState()
    {
        var engine = CreateEngine(initialHunger: 10_000, initialHealth: 20);
        engine.QueueCommand(SimulationCommand.Forage(
            new SimulationTick(10),
            sequence: 1,
            new EntityId(1)));

        engine.AdvanceTicks(1);

        var events = engine.DrainEvents();
        Assert.Contains(events, item => item.Kind == SimulationEventKind.ActorDied);
        Assert.Contains(events, item => item.Kind == SimulationEventKind.CommandRejected);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());

        restored.AdvanceTicks(20);
        Assert.DoesNotContain(
            restored.DrainEvents(),
            item => item.Kind == SimulationEventKind.CommandRejected);
    }

    [Fact]
    public void CriticalHungerInterruptsHaulCollectionForReachableMeal()
    {
        var engine = CreateEngine(initialHunger: 0, initialHealth: 10_000, initialFood: 3);
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1),
            sequence: 1,
            engine.Map.GoblinSpawn,
            ResourceKind.Food,
            capacity: 3));
        engine.AdvanceTicks(1);
        Assert.Equal(ActorJobKind.Haul, Assert.Single(engine.CreateSnapshot().Actors).Job.Kind);

        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        save["actors"]![0]!["hunger"] = SimulationDefinitions.Foundation.CriticalHungerThreshold;
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);

        engine.AdvanceTicks(1);

        var actor = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.Equal(ActorJobKind.Eat, actor.Job.Kind);
        Assert.Equal(1, actor.Job.ReservedQuantity);
        Assert.Equal(EntityId.None, actor.Job.DestinationZoneId);
    }

    private static SimulationEngine CreateEngine(
        int initialHunger,
        int initialHealth,
        int initialFood = 0)
    {
        var seed = new WorldSeed(0x535441525645UL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        return SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: initialFood,
            initialHunger: initialHunger,
            initialHealth: initialHealth);
    }
}
