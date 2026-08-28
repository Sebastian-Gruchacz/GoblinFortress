using GoblinStronghold.Simulation.Map;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class TacticalOrderTests
{
    [Fact]
    public void PatrolIsPersistentAndSurvivesSaveLoad()
    {
        var engine = CreateEngine();
        var actor = Assert.Single(engine.CreateSnapshot().Actors);
        var destination = FindReachableDestination(engine, actor.Position);

        engine.QueueCommand(SimulationCommand.OrderPatrol(
            new SimulationTick(1), sequence: 1, actor.Id, destination, append: false));
        engine.AdvanceTicks(1);

        var patrolling = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.Equal(ActorTacticalOrderKind.Patrol, patrolling.TacticalOrder.Kind);
        Assert.Equal([actor.Position, destination], patrolling.TacticalOrder.PatrolPoints);
        Assert.Equal(ActorJobKind.Move, patrolling.Job.Kind);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(
            ActorTacticalOrderKind.Patrol,
            Assert.Single(restored.CreateSnapshot().Actors).TacticalOrder.Kind);
    }

    [Fact]
    public void TacticalHuntTargetsOnlyAnimalInsideAreaAndSurvivesSaveLoad()
    {
        var engine = CreateEngine();
        var snapshot = engine.CreateSnapshot();
        var actor = Assert.Single(snapshot.Actors);
        var animal = snapshot.Animals
            .Select(candidate => new
            {
                Animal = candidate,
                Route = engine.Navigation.FindPath(actor.Position, candidate.Position),
            })
            .Where(candidate => candidate.Route is not null)
            .OrderBy(candidate => candidate.Route!.Count)
            .First().Animal;

        engine.QueueCommand(SimulationCommand.OrderHuntArea(
            new SimulationTick(1), sequence: 1, actor.Id, animal.Position, radius: 3));
        engine.AdvanceTicks(1);

        var hunter = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.Equal(ActorTacticalOrderKind.HuntArea, hunter.TacticalOrder.Kind);
        Assert.Equal(ActorJobKind.HuntAnimal, hunter.Job.Kind);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void EmptyAttackAreaClearsOrderAndReturnsGoblinToSettlementWork()
    {
        var engine = CreateEngine();
        var actor = Assert.Single(engine.CreateSnapshot().Actors);

        engine.QueueCommand(SimulationCommand.OrderAttackArea(
            new SimulationTick(1), sequence: 1, actor.Id, actor.Position, radius: 3));
        engine.AdvanceTicks(1);

        var returned = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.Equal(ActorTacticalOrderKind.None, returned.TacticalOrder.Kind);
        Assert.NotEqual(ActorJobKind.HuntAnimal, returned.Job.Kind);
    }

    private static SimulationEngine CreateEngine() => SimulationEngine.Create(
        new WorldSeed(0x544143544943414CUL),
        SimulationDefinitions.Foundation,
        initialGoblinCount: 1,
        initialFoodStock: 20,
        initialWoodStock: 10);

    private static GridPosition FindReachableDestination(
        SimulationEngine engine,
        GridPosition origin) =>
        engine.World.GetCardinalWorldNeighbors(origin)
            .Where(position => engine.Navigation.FindPath(origin, position) is { Count: > 0 })
            .First();
}
