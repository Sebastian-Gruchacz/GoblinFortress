using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class ActorPlanningTests
{
    [Fact]
    public void NeedPressureReordersThePlanAndEventuallyInterruptsOrderedMovement()
    {
        var lowPressure = CreateMovingGoblin(SimulationDefinitions.Foundation.FoodSeekThreshold);

        lowPressure.AdvanceTicks(1);

        var committed = Assert.Single(lowPressure.CreateSnapshot().Actors);
        Assert.Equal(ActorJobKind.Move, committed.Job.Kind);
        Assert.Equal(ActorPlanIntentKind.CurrentJob, committed.Plan[0].Kind);
        var futureMeal = Assert.Single(committed.Plan, entry =>
            entry.Kind == ActorPlanIntentKind.FindFood);
        Assert.True(futureMeal.Priority < committed.Plan[0].Priority);

        var highPressure = CreateMovingGoblin(
            SimulationDefinitions.Foundation.CriticalHungerThreshold);

        highPressure.AdvanceTicks(1);

        var interrupted = Assert.Single(highPressure.CreateSnapshot().Actors);
        Assert.Equal(ActorJobKind.Eat, interrupted.Job.Kind);
        Assert.Equal(ActorPlanIntentKind.CurrentJob, interrupted.Plan[0].Kind);
        Assert.Contains(interrupted.Plan, entry =>
            entry.Kind == ActorPlanIntentKind.ResumeSuspendedJob &&
            entry.JobKind == ActorJobKind.Move);

        var restored = SimulationEngine.Load(
            highPressure.Save(),
            SimulationDefinitions.Foundation);
        Assert.Equal(interrupted.Plan, Assert.Single(restored.CreateSnapshot().Actors).Plan);
        Assert.Equal(highPressure.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void BusyGoblinsForecastDistinctConcreteTargetsFromThePublicWorkQueue()
    {
        var seed = new WorldSeed(0x4A4F4253UL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 2,
            initialFoodStock: 4);
        var actors = engine.CreateSnapshot().Actors.OrderBy(actor => actor.Id).ToArray();
        engine.QueueCommand(SimulationCommand.DesignateWork(
            new SimulationTick(1),
            sequence: 1,
            new GridPosition(0, 0),
            new GridPosition(map.Width - 1, map.Height - 1),
            ResourceKind.Food));
        engine.QueueCommand(SimulationCommand.Move(
            new SimulationTick(1),
            sequence: 2,
            actors[0].Id,
            map.HumanVillage));
        engine.QueueCommand(SimulationCommand.Move(
            new SimulationTick(1),
            sequence: 3,
            actors[1].Id,
            map.HumanVillage));

        engine.AdvanceTicks(1);

        var snapshot = engine.CreateSnapshot();
        var forecasts = snapshot.Actors
            .Select(actor => Assert.Single(actor.Plan,
                entry => entry.Kind == ActorPlanIntentKind.NextPublicWork))
            .ToArray();
        Assert.All(snapshot.Actors, actor => Assert.Equal(ActorJobKind.Move, actor.Job.Kind));
        Assert.Single(forecasts.Select(entry => entry.WorkOrderId).Distinct());
        Assert.DoesNotContain(forecasts, entry => entry.WorkOrderId == EntityId.None);
        Assert.Equal(forecasts.Length, forecasts.Select(entry => entry.Target).Distinct().Count());

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(
            snapshot.Actors.Select(actor => actor.Plan),
            restored.CreateSnapshot().Actors.Select(actor => actor.Plan));
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    private static SimulationEngine CreateMovingGoblin(int hunger)
    {
        var seed = new WorldSeed(0x504C414EUL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 1);
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        save["actors"]![0]!["hunger"] = hunger;
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        var actor = Assert.Single(engine.CreateSnapshot().Actors);
        var destination = engine.Map.GetCardinalNeighbors(actor.Position)
            .First(engine.World.IsSurfaceTraversable);
        engine.QueueCommand(SimulationCommand.Move(
            new SimulationTick(1),
            sequence: 1,
            actor.Id,
            destination));
        return engine;
    }
}
