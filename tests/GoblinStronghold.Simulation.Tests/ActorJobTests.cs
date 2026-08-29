using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class ActorJobTests
{
    [Fact]
    public void IdleGoblinsReserveDistinctBerryPatchesAndMoveInSteps()
    {
        var engine = CreateEngine(goblinCount: 4);
        var spawn = engine.Map.GoblinSpawn;

        IReadOnlyList<ActorSnapshot> planned = [];
        for (var tick = 0; tick < 100; tick++)
        {
            engine.AdvanceTicks(1);
            planned = engine.CreateSnapshot().Actors;
            if (planned.Count(actor => actor.Job.Kind == ActorJobKind.Forage) == 3 &&
                planned.Count(actor => actor.Job.Kind == ActorJobKind.Explore) == 1)
            {
                break;
            }
        }

        Assert.Single(planned, actor => actor.Job.Kind == ActorJobKind.Explore);
        var foragers = planned.Where(actor => actor.Job.Kind == ActorJobKind.Forage).ToArray();
        Assert.Equal(3, foragers.Length);
        Assert.Equal(foragers.Length, foragers.Select(actor => actor.Job.Target).Distinct().Count());
        Assert.Contains(planned, actor => actor.Job.Phase == ActorJobPhase.Working);
        Assert.Contains(planned, actor => actor.Job.Phase == ActorJobPhase.Traveling);

        engine.AdvanceTicks(SimulationDefinitions.Foundation.ActorMovementIntervalTicks - 1);
        var moved = engine.CreateSnapshot().Actors;

        Assert.Contains(moved, actor => actor.Position != spawn);
    }

    [Fact]
    public void ForageJobConsumesWorkTimeAndProducesPhysicalFood()
    {
        var engine = CreateEngine(goblinCount: 2);

        engine.AdvanceTicks(1);
        var started = Assert.Single(
            engine.CreateSnapshot().Actors,
            actor => actor.Job.Kind == ActorJobKind.Forage);
        Assert.Equal(ActorJobPhase.Working, started.Job.Phase);
        Assert.Equal(
            SimulationDefinitions.Foundation.ForageWorkTicks - 1,
            started.Job.RemainingWorkTicks);
        Assert.Empty(engine.CreateSnapshot().ItemStacks);

        engine.AdvanceTicks(SimulationDefinitions.Foundation.ForageWorkTicks - 1);

        var food = Assert.Single(engine.CreateSnapshot().ItemStacks);
        Assert.True(food.Quantity >= SimulationDefinitions.Foundation.BaseForageYield);
        Assert.NotEqual(FoodKind.None, food.FoodKind);
        Assert.Contains(
            engine.DrainEvents(),
            simulationEvent => simulationEvent.Kind == SimulationEventKind.FoodGathered);
    }

    [Fact]
    public void SaveLoadDuringTravelAndWorkPreservesFutureOutcome()
    {
        var engine = CreateEngine(goblinCount: 4);
        engine.AdvanceTicks(2);

        var jobs = engine.CreateSnapshot().Actors.Select(actor => actor.Job.Phase).ToArray();
        Assert.Contains(ActorJobPhase.Traveling, jobs);
        Assert.Contains(ActorJobPhase.Working, jobs);

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());

        engine.AdvanceTicks(200);
        restored.AdvanceTicks(200);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(engine.DrainEvents(), restored.DrainEvents());
        Assert.Equal(engine.DrainWorldChanges(), restored.DrainWorldChanges());
    }

    private static SimulationEngine CreateEngine(int goblinCount)
    {
        var seed = new WorldSeed(0x4A4F4253UL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: goblinCount,
            initialFoodStock: 0);
        engine.QueueCommand(SimulationCommand.DesignateWork(
            new SimulationTick(1),
            sequence: 1,
            new GridPosition(0, 0),
            new GridPosition(map.Width - 1, map.Height - 1),
            ResourceKind.Food));
        engine.QueueCommand(SimulationCommand.DesignateScouting(
            new SimulationTick(1),
            sequence: 2,
            new GridPosition(0, 0),
            new GridPosition(map.Width - 1, map.Height - 1)));
        return engine;
    }
}
