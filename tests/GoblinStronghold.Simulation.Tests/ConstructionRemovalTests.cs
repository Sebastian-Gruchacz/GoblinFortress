using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Construction;
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

        Assert.Contains(engine.World.GetWorldObjectsAt(position), worldObject =>
            worldObject.Id == floor.Id);
        Assert.Contains(engine.CreateSnapshot().WorkDesignations, designation =>
            designation.Kind == WorkDesignationKind.DismantleWorldObject &&
            designation.TargetEntityId.Value == floor.Id.Value);
        var dismantler = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.Equal(ActorJobKind.DismantleConstruction, dismantler.Job.Kind);
        Assert.Equal(
            ConstructionDismantlingPolicy.GetWorkTicks(ConstructionKind.WoodenFloor) - 1,
            dismantler.Job.RemainingWorkTicks);

        engine = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);

        AdvanceUntil(engine, () => engine.World.GetWorldObjectsAt(position)
            .All(worldObject => worldObject.Id != floor.Id));

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

        Assert.Single(engine.CreateSnapshot().StorageZones);
        Assert.Contains(engine.CreateSnapshot().WorkDesignations, designation =>
            designation.Kind == WorkDesignationKind.DismantleStorageZone &&
            designation.TargetEntityId == storage.Id);

        AdvanceUntil(engine, () => engine.CreateSnapshot().StorageZones.Count == 0);

        Assert.Empty(engine.CreateSnapshot().StorageZones);
        Assert.DoesNotContain(
            engine.CreateResourceSpatialSnapshot().StorageNodes,
            node => node.ZoneId == storage.Id);
    }

    [Fact]
    public void FloorConstructionCanBeOrderedBelowAnExistingStorage()
    {
        var engine = CreateEngine(initialWoodStock: 2);
        var position = engine.Map.GoblinSpawn;
        engine.QueueCommand(SimulationCommand.BuildFoodStorage(
            new SimulationTick(1),
            sequence: 1,
            position));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);

        engine.QueueCommand(SimulationCommand.BuildWoodenFloor(
            engine.CurrentTick.Next(),
            sequence: 2,
            position,
            position,
            ResourceVariant.OakWood));
        engine.AdvanceTicks(1);

        var floorSite = Assert.Single(engine.CreateSnapshot().ConstructionSites);
        Assert.Equal(ConstructionKind.WoodenFloor, floorSite.Kind);
        Assert.Equal(position, floorSite.Anchor);
    }

    [Fact]
    public void GoblinFallsAfterTheFloorBelowItIsDismantled()
    {
        var generated = CreateEngine(initialWoodStock: 0);
        var position = FindElevatedUnsupportedPosition(generated);
        generated.World.BuildFloor(
            position,
            SimulationTick.Zero,
            stone: false,
            ResourceVariant.OakWood);
        var floor = Assert.Single(generated.World.GetWorldObjectsAt(position), worldObject =>
            worldObject.Kind == WorldObjectKind.WoodenFloor);
        var save = JsonNode.Parse(generated.Save())!.AsObject();
        save["actors"]![0]!["x"] = position.X;
        save["actors"]![0]!["y"] = position.Y;
        save["actors"]![0]!["z"] = position.Z;
        var engine = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);

        engine.QueueCommand(SimulationCommand.DismantleWorldObject(
            engine.CurrentTick.Next(),
            sequence: 1,
            floor.Id,
            floor.Anchor));
        AdvanceUntil(engine, () => engine.World.GetWorldObjectsAt(position)
            .All(worldObject => worldObject.Id != floor.Id));

        var actor = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.Equal(position.X, actor.Position.X);
        Assert.Equal(position.Y, actor.Position.Y);
        Assert.True(actor.Position.Z < position.Z);
        Assert.True(engine.World.IsTerrainTraversable(actor.Position));
    }

    [Fact]
    public void LooseItemsAndCorpsesFallAfterTheirFloorIsDismantled()
    {
        var generated = CreateEngine(initialWoodStock: 0, initialFoodStock: 1);
        var position = FindElevatedUnsupportedPosition(generated);
        generated.World.BuildFloor(
            position,
            SimulationTick.Zero,
            stone: false,
            ResourceVariant.OakWood);
        var floor = Assert.Single(generated.World.GetWorldObjectsAt(position), worldObject =>
            worldObject.Kind == WorldObjectKind.WoodenFloor);
        var save = JsonNode.Parse(generated.Save())!.AsObject();
        save["actors"]![0]!["x"] = position.X;
        save["actors"]![0]!["y"] = position.Y;
        save["actors"]![0]!["z"] = position.Z;
        var looseStack = save["itemStacks"]!.AsArray().Single()!.AsObject();
        looseStack["x"] = position.X;
        looseStack["y"] = position.Y;
        looseStack["z"] = position.Z;
        var corpseId = save["nextEntityId"]!.GetValue<ulong>();
        save["nextEntityId"] = corpseId + 1;
        save["corpses"]!.AsArray().Add(new JsonObject
        {
            ["id"] = corpseId,
            ["kind"] = (int)CorpseKind.Goblin,
            ["name"] = "Glek",
            ["x"] = position.X,
            ["y"] = position.Y,
            ["z"] = position.Z,
            ["createdAtTick"] = save["currentTick"]!.GetValue<long>(),
            ["containedWater"] = 0,
            ["ediblePortions"] = 5,
            ["contents"] = new JsonArray(),
        });
        var engine = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);

        engine.QueueCommand(SimulationCommand.DismantleWorldObject(
            engine.CurrentTick.Next(),
            sequence: 1,
            floor.Id,
            floor.Anchor));
        AdvanceUntil(engine, () => engine.World.GetWorldObjectsAt(position)
            .All(worldObject => worldObject.Id != floor.Id));

        var landing = Assert.Single(engine.CreateSnapshot().Actors).Position;
        Assert.True(landing.Z < position.Z);
        Assert.Contains(engine.CreateSnapshot().ItemStacks, stack =>
            stack.Location == ItemLocation.OnGround(landing));
        Assert.Equal(landing, Assert.Single(engine.CreateSnapshot().Corpses).Position);
    }

    [Fact]
    public void DismantlingOrderCanBeCancelledBeforeTheStructureIsRemoved()
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
        var order = Assert.Single(engine.CreateSnapshot().WorkDesignations);
        Assert.Equal(ActorJobKind.DismantleConstruction,
            Assert.Single(engine.CreateSnapshot().Actors).Job.Kind);

        engine.QueueCommand(SimulationCommand.ClearWorkDesignationOrder(
            engine.CurrentTick.Next(),
            sequence: 3,
            order.OrderId));
        engine.AdvanceTicks(1);

        Assert.Empty(engine.CreateSnapshot().WorkDesignations);
        Assert.Equal(ActorJobKind.None, Assert.Single(engine.CreateSnapshot().Actors).Job.Kind);
        Assert.Contains(engine.World.GetWorldObjectsAt(position), worldObject =>
            worldObject.Id == floor.Id);
    }

    private static void AdvanceUntil(
        SimulationEngine engine,
        Func<bool> completed,
        int maximumTicks = 500)
    {
        for (var tick = 0; tick < maximumTicks && !completed(); tick++)
        {
            engine.AdvanceTicks(1);
        }

        Assert.True(completed());
    }

    private static SimulationEngine CreateEngine(
        int initialWoodStock,
        int initialFoodStock = 0) =>
        SimulationEngine.Create(
            new WorldSeed(0x52454D4F56414CUL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: initialFoodStock,
            initialWoodStock: initialWoodStock);

    private static GridPosition FindElevatedUnsupportedPosition(SimulationEngine engine) =>
        Enumerable.Range(0, engine.Map.Width)
            .SelectMany(x => Enumerable.Range(0, engine.Map.Height)
                .SelectMany(y => Enumerable.Range(1, engine.Map.MaximumWorldLevel)
                    .Select(z => new GridPosition(x, y, z))))
            .First(candidate =>
                engine.World.CanBuildFloors([candidate]) &&
                !engine.World.IsTerrainTraversable(candidate) &&
                Enumerable.Range(engine.Map.DeepestCaveLevel, candidate.Z -
                        engine.Map.DeepestCaveLevel)
                    .Select(z => candidate with { Z = z })
                    .Any(engine.World.IsTerrainTraversable));

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
