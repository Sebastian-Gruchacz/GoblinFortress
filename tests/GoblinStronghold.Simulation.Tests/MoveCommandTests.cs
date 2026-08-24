using GoblinStronghold.Simulation.Map;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class MoveCommandTests
{
    [Fact]
    public void OrderedGoblinTravelsCellByCellAndCompletesAtDestination()
    {
        var engine = CreateEngine();
        var actor = engine.CreateSnapshot().Actors.Single();
        var route = engine.World.FindSurfacePath(actor.Position, engine.Map.HumanVillage)
            ?? throw new InvalidOperationException("Generated settlements are disconnected.");
        var destination = route[2];
        engine.QueueCommand(SimulationCommand.Move(
            new SimulationTick(1),
            sequence: 1,
            actor.Id,
            destination));

        engine.AdvanceTicks(1);

        var ordered = engine.CreateSnapshot().Actors.Single();
        Assert.Equal(ActorJobKind.Move, ordered.Job.Kind);
        Assert.Equal(destination, ordered.Job.Target);
        Assert.Equal(actor.Position, ordered.Position);

        engine.AdvanceTicks(
            (3 * SimulationDefinitions.Foundation.ActorMovementIntervalTicks) - 1);

        var completed = engine.CreateSnapshot().Actors.Single();
        var events = engine.DrainEvents();
        Assert.Equal(destination, completed.Position);
        Assert.Equal(ActorJobKind.None, completed.Job.Kind);
        Assert.Contains(events, item => item.Kind == SimulationEventKind.MoveOrdered);
        Assert.Contains(events, item => item.Kind == SimulationEventKind.MoveCompleted);
    }

    [Fact]
    public void OrderedMoveContinuesIdenticallyAfterSaveLoad()
    {
        var engine = CreateEngine();
        var actor = engine.CreateSnapshot().Actors.Single();
        var route = engine.World.FindSurfacePath(actor.Position, engine.Map.HumanVillage)
            ?? throw new InvalidOperationException("Generated settlements are disconnected.");
        engine.QueueCommand(SimulationCommand.Move(
            new SimulationTick(1),
            sequence: 1,
            actor.Id,
            route[7]));
        engine.AdvanceTicks(17);

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        engine.AdvanceTicks(80);
        restored.AdvanceTicks(80);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void UnknownImpassableDestinationIsRejectedAtExecutionWithoutMovingActor()
    {
        var engine = CreateEngine();
        var actor = engine.CreateSnapshot().Actors.Single();
        var impassable = FindImpassableCell(engine.Map);
        Assert.Equal(CellVisibility.Unknown, engine.Visibility.Get(impassable));
        engine.QueueCommand(SimulationCommand.Move(
            new SimulationTick(1),
            sequence: 1,
            actor.Id,
            impassable));

        engine.AdvanceTicks(1);

        Assert.Equal(actor.Position, engine.CreateSnapshot().Actors.Single().Position);
        Assert.Contains(engine.DrainEvents(), item =>
            item.Kind == SimulationEventKind.CommandRejected &&
            item.Amount == (int)SimulationCommandKind.Move);
    }

    private static GridPosition FindImpassableCell(GeneratedMap map)
    {
        for (var y = map.Height - 1; y >= 0; y--)
        {
            for (var x = map.Width - 1; x >= 0; x--)
            {
                var position = new GridPosition(x, y);
                if (!map.GetCell(position).IsTraversable)
                {
                    return position;
                }
            }
        }

        throw new InvalidOperationException("The generated swamp has no impassable cell.");
    }

    private static SimulationEngine CreateEngine()
    {
        var seed = new WorldSeed(0x4D4F5645UL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        return SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 8);
    }
}
