using System.Text.Json.Nodes;
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
    public void ClearingOrdersAtPatrolWaypointCancelsPersistentPatrol()
    {
        var engine = CreateEngine();
        var actor = Assert.Single(engine.CreateSnapshot().Actors);
        var destination = FindReachableDestination(engine, actor.Position);

        engine.QueueCommand(SimulationCommand.OrderPatrol(
            new SimulationTick(1), sequence: 1, actor.Id, destination, append: false));
        engine.AdvanceTicks(1);
        Assert.Equal(
            ActorTacticalOrderKind.Patrol,
            Assert.Single(engine.CreateSnapshot().Actors).TacticalOrder.Kind);

        engine.QueueCommand(SimulationCommand.ClearWorkDesignations(
            new SimulationTick(2), sequence: 2, destination, destination));
        engine.AdvanceTicks(1);

        var cancelled = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.Equal(ActorTacticalOrderKind.None, cancelled.TacticalOrder.Kind);
        Assert.Empty(cancelled.TacticalOrder.PatrolPoints);
        Assert.NotEqual(ActorJobKind.Move, cancelled.Job.Kind);
        Assert.Equal(ActorJobKind.None, cancelled.Job.SuspendedKind);
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
    public void TacticalHuntPartyFocusesTheSameDangerousAnimal()
    {
        var engine = CreateEngine(initialGoblinCount: 2);
        var snapshot = engine.CreateSnapshot();
        var actors = snapshot.Actors.ToArray();
        var cluster = snapshot.Animals
            .Select(center => new
            {
                Center = center.Position,
                Animals = snapshot.Animals.Where(animal =>
                    Distance(animal.Position, center.Position) <=
                    SimulationEngine.MaximumRaidTargetRadius).ToArray(),
            })
            .Where(candidate => candidate.Animals.Length >= 2)
            .Where(candidate => candidate.Animals.All(animal => actors.All(actor =>
                engine.Navigation.FindPath(actor.Position, animal.Position) is not null)))
            .First();
        var expected = cluster.Animals
            .OrderByDescending(animal =>
                AnimalCombatPolicy.GetAttackDamage(animal.Kind, animal.Position))
            .ThenByDescending(animal => animal.Health)
            .ThenBy(animal => Distance(animal.Position, cluster.Center))
            .ThenBy(animal => animal.Id)
            .First();
        ulong sequence = 1;
        foreach (var actor in actors)
        {
            engine.QueueCommand(SimulationCommand.OrderHuntArea(
                new SimulationTick(1),
                sequence++,
                actor.Id,
                cluster.Center,
                SimulationEngine.MaximumRaidTargetRadius));
        }

        engine.AdvanceTicks(1);

        var hunters = engine.CreateSnapshot().Actors;
        Assert.All(hunters, actor => Assert.Equal(ActorJobKind.HuntAnimal, actor.Job.Kind));
        var targetIds = JsonNode.Parse(engine.Save())!["actors"]!.AsArray()
            .Select(item => item!["tacticalTargetEntityId"]!.GetValue<ulong>())
            .ToArray();
        Assert.All(targetIds, targetId => Assert.Equal(expected.Id, targetId));

        static int Distance(GridPosition left, GridPosition right) =>
            Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y) +
            Math.Abs(left.Z - right.Z);
    }

    [Fact]
    public void PublicHuntAreaDesignatesVisibleAnimalsInsideCircleWithoutSelectedGoblin()
    {
        var engine = CreateEngine();
        var initial = engine.CreateSnapshot();
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var visibility = save["visibility"]!.AsArray();
        foreach (var animal in initial.Animals.Where(animal => animal.Position.Z == 0))
        {
            visibility[animal.Position.Y * engine.Map.Width + animal.Position.X] =
                (int)CellVisibility.Explored;
        }
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        var snapshot = engine.CreateSnapshot();
        var center = snapshot.Animals
            .Where(animal => snapshot.GetVisibility(animal.Position, engine.Map.Width)
                .IsDiscovered())
            .Select(animal => animal.Position)
            .First();
        const int radius = 4;
        var expected = snapshot.Animals
            .Where(animal => animal.Position.Z == center.Z &&
                Distance(animal.Position, center) <= radius &&
                snapshot.GetVisibility(animal.Position, engine.Map.Width).IsDiscovered())
            .Select(animal => animal.Id)
            .Order()
            .ToArray();
        Assert.NotEmpty(expected);

        engine.QueueCommand(SimulationCommand.DesignateHuntArea(
            new SimulationTick(1), sequence: 1, center, radius));
        engine.AdvanceTicks(1);

        var designated = engine.CreateSnapshot().WorkDesignations
            .Where(item => item.Kind == WorkDesignationKind.HuntAnimal)
            .Select(item => item.TargetEntityId.Value)
            .Order()
            .ToArray();
        Assert.Equal(expected, designated);

        static int Distance(GridPosition left, GridPosition right) =>
            Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y) +
            Math.Abs(left.Z - right.Z);
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

    private static SimulationEngine CreateEngine(int initialGoblinCount = 1) =>
        SimulationEngine.Create(
        new WorldSeed(0x544143544943414CUL),
        SimulationDefinitions.Foundation,
        initialGoblinCount: initialGoblinCount,
        initialFoodStock: 20,
        initialWoodStock: 10);

    private static GridPosition FindReachableDestination(
        SimulationEngine engine,
        GridPosition origin) =>
        engine.World.GetCardinalWorldNeighbors(origin)
            .Where(position => engine.Navigation.FindPath(origin, position) is { Count: > 0 })
            .First();
}
