using GoblinStronghold.Simulation.Map;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class SurvivalNeedTests
{
    [Fact]
    public void TiredGoblinChoosesAReachableHutAndRecovers()
    {
        var engine = CreateEngine();
        ActorSnapshot resting = default;
        for (var tick = 0; tick < 2_000; tick++)
        {
            engine.AdvanceTicks(1);
            var actor = Assert.Single(engine.CreateSnapshot().Actors);
            if (actor.Job.Kind == ActorJobKind.Rest && actor.Job.Phase == ActorJobPhase.Working)
            {
                resting = actor;
                break;
            }
        }

        Assert.NotEqual(EntityId.None, resting.Id);
        Assert.Contains(
            engine.World.GetWorldObjectsAt(resting.Job.Target),
            worldObject => worldObject.Kind == WorldObjectKind.GoblinHut);
        var fatigueBeforeRest = resting.Fatigue;

        engine.AdvanceTicks(5);

        Assert.True(Assert.Single(engine.CreateSnapshot().Actors).Fatigue < fatigueBeforeRest);
    }

    [Fact]
    public void SaveLoadDuringRestPreservesFutureOutcome()
    {
        var engine = CreateEngine();
        for (var tick = 0; tick < 2_000; tick++)
        {
            engine.AdvanceTicks(1);
            if (Assert.Single(engine.CreateSnapshot().Actors).Job.Kind == ActorJobKind.Rest)
            {
                break;
            }
        }

        Assert.Equal(ActorJobKind.Rest, Assert.Single(engine.CreateSnapshot().Actors).Job.Kind);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());

        engine.AdvanceTicks(300);
        restored.AdvanceTicks(300);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(engine.DrainEvents(), restored.DrainEvents());
        Assert.Equal(engine.DrainWorldChanges(), restored.DrainWorldChanges());
    }

    private static SimulationEngine CreateEngine()
    {
        var seed = new WorldSeed(0x52455354UL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        return SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 0);
    }
}
