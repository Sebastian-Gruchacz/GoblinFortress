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
        var engine = CreateEngine(
            initialHunger: SimulationDefinitions.Foundation.StarvationHungerThreshold,
            initialHealth: 3);

        engine.AdvanceTicks(1);
        Assert.Equal(2, Assert.Single(engine.CreateSnapshot().Actors).Health);

        engine.AdvanceTicks(2);

        Assert.Empty(engine.CreateSnapshot().Actors);
        Assert.Contains(
            engine.DrainEvents(),
            simulationEvent => simulationEvent.Kind == SimulationEventKind.ActorDied);
    }

    [Fact]
    public void DeathCancelsFutureCommandsAndLeavesLoadableState()
    {
        var engine = CreateEngine(
            initialHunger: SimulationDefinitions.Foundation.StarvationHungerThreshold,
            initialHealth: 1);
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
    public void HungerInterruptsHaulCollectionForReachableMeal()
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

    [Fact]
    public void MealInterruptionResumesOrderedMove()
    {
        var engine = CreateEngine(initialHunger: 0, initialHealth: 10_000, initialFood: 3);
        var actor = Assert.Single(engine.CreateSnapshot().Actors);
        engine.QueueCommand(SimulationCommand.Move(
            new SimulationTick(1),
            sequence: 1,
            actor.Id,
            engine.Map.HumanVillage));
        engine.AdvanceTicks(SimulationDefinitions.Foundation.ActorMovementIntervalTicks);
        var moving = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.Equal(ActorJobKind.Move, moving.Job.Kind);
        Assert.NotEqual(engine.Map.GoblinSpawn, moving.Position);

        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        save["actors"]![0]!["hunger"] = SimulationDefinitions.Foundation.CriticalHungerThreshold;
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);

        var ate = false;
        for (var tick = 0; tick < 80 && !ate; tick++)
        {
            engine.AdvanceTicks(1);
            ate = engine.DrainEvents().Any(item => item.Kind == SimulationEventKind.ActorAte);
        }

        Assert.True(ate);
        var resumed = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.Equal(ActorJobKind.Move, resumed.Job.Kind);
        Assert.Equal(engine.Map.HumanVillage, resumed.Job.Target);
        Assert.Equal(ActorJobKind.None, resumed.Job.SuspendedKind);
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
