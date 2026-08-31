using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class ConstructionRemovalTests
{
    [Fact]
    public void CancellingOneAreaSiteCancelsTheWholeConstructionOrder()
    {
        var engine = CreateEngine(initialWoodStock: 10);
        var cells = FindFloorRectangle(engine, width: 2, height: 2);
        engine.QueueCommand(SimulationCommand.BuildWoodenFloor(
            new SimulationTick(1),
            sequence: 1,
            cells[0],
            cells[^1],
            ResourceVariant.OakWood));
        engine.AdvanceTicks(1);

        var sites = engine.CreateSnapshot().ConstructionSites.ToArray();
        Assert.Equal(4, sites.Length);
        engine.QueueCommand(SimulationCommand.CancelConstruction(
            new SimulationTick(2),
            sequence: 2,
            sites[2].Id));

        engine.AdvanceTicks(1);

        Assert.Empty(engine.CreateSnapshot().ConstructionSites);
        Assert.Contains(engine.DrainEvents(), simulationEvent =>
            simulationEvent.Kind == SimulationEventKind.ConstructionCancelled &&
            simulationEvent.Amount == 4);
    }

    [Fact]
    public void CompletedFloorCanBeDismantledAndReleasesItsOccupancy()
    {
        var engine = CreateEngine(initialWoodStock: 1);
        var position = FindFloorRectangle(engine, width: 1, height: 1)[0];
        engine.QueueCommand(SimulationCommand.BuildWoodenFloor(
            new SimulationTick(1),
            sequence: 1,
            position,
            position,
            ResourceVariant.OakWood));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
        var floor = Assert.Single(engine.World.GetWorldObjectsAt(position), worldObject =>
            worldObject.Kind == WorldObjectKind.WoodenFloor);

        engine.QueueCommand(SimulationCommand.DismantleWorldObject(
            engine.CurrentTick.Next(),
            sequence: 2,
            floor.Id,
            floor.Anchor));
        engine.AdvanceTicks(1);

        Assert.DoesNotContain(engine.World.GetWorldObjectsAt(position), worldObject =>
            worldObject.Id == floor.Id);
        Assert.True(engine.World.CanBuildFloors([position]));
        Assert.Contains(engine.DrainWorldChanges(), change =>
            change.Kind == WorldChangeKind.StructureDismantled);
    }

    [Fact]
    public void FinishedStorageCanBeRemoved()
    {
        var engine = CreateEngine(initialWoodStock: 2);
        var position = engine.Map.GoblinSpawn;
        engine.QueueCommand(SimulationCommand.BuildFoodStorage(
            new SimulationTick(1),
            sequence: 1,
            position));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
        var storage = Assert.Single(engine.CreateSnapshot().StorageZones);

        engine.QueueCommand(SimulationCommand.DismantleStorageZone(
            engine.CurrentTick.Next(),
            sequence: 2,
            storage.Id,
            storage.Position));
        engine.AdvanceTicks(1);

        Assert.Empty(engine.CreateSnapshot().StorageZones);
        Assert.DoesNotContain(
            engine.CreateResourceSpatialSnapshot().StorageNodes,
            node => node.ZoneId == storage.Id);
    }

    private static SimulationEngine CreateEngine(int initialWoodStock) =>
        SimulationEngine.Create(
            new WorldSeed(0x52454D4F56414CUL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialWoodStock: initialWoodStock);

    private static IReadOnlyList<GridPosition> FindFloorRectangle(
        SimulationEngine engine,
        int width,
        int height)
    {
        for (var radius = 0; radius <= 10; radius++)
        {
            for (var y = engine.Map.GoblinSpawn.Y - radius;
                 y <= engine.Map.GoblinSpawn.Y + radius; y++)
            {
                for (var x = engine.Map.GoblinSpawn.X - radius;
                     x <= engine.Map.GoblinSpawn.X + radius; x++)
                {
                    var cells = SimulationCommand.GetAreaCells(
                        new GridPosition(x, y),
                        new GridPosition(x + width - 1, y + height - 1));
                    if (cells.All(cell =>
                            engine.Visibility.TryGet(cell, out var visibility) &&
                            visibility.IsDiscovered()) &&
                        engine.World.CanBuildFloors(cells))
                    {
                        return cells;
                    }
                }
            }
        }

        throw new InvalidOperationException("No floor construction rectangle was found.");
    }
}
