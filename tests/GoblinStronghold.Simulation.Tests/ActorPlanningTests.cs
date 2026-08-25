using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
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
