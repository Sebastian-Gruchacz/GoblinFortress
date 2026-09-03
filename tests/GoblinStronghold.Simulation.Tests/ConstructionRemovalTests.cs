using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Construction;
using GoblinStronghold.Simulation.Lighting;
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
    public void TribalCompostCanBeBuiltDismantledAndRebuilt()
    {
        var engine = AddLooseStack(
            CreateEngine(initialWoodStock: 0),
            ResourceKind.Reeds,
            quantity: 4);
        var position = Enumerable.Range(0, engine.Map.Width)
            .SelectMany(x => Enumerable.Range(0, engine.Map.Height)
                .Select(y => new GridPosition(x, y)))
            .First(candidate =>
                engine.Visibility.TryGet(candidate, out var visibility) &&
                visibility.IsDiscovered() &&
                engine.World.CanBuildGoblinCompost(candidate));

        engine.QueueCommand(SimulationCommand.BuildGoblinCompost(
            engine.CurrentTick.Next(), sequence: 1, position));
        engine.AdvanceTicks(1);

        var site = Assert.Single(engine.CreateSnapshot().ConstructionSites);
        Assert.Equal(ConstructionKind.GoblinCompost, site.Kind);
        var material = Assert.Single(site.Materials);
        Assert.Equal(ResourceKind.Reeds, material.Resource);
        Assert.Equal(2, material.RequiredQuantity);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
        var compost = Assert.Single(engine.World.GetWorldObjectsAt(position), worldObject =>
            worldObject.Kind == WorldObjectKind.GoblinCompost);

        engine = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        engine.QueueCommand(SimulationCommand.DismantleWorldObject(
            engine.CurrentTick.Next(), sequence: 2, compost.Id, compost.Anchor));
        AdvanceUntil(engine, () => engine.World.GetWorldObjectsAt(position)
            .All(worldObject => worldObject.Id != compost.Id));

        Assert.True(engine.World.CanBuildGoblinCompost(position));
        engine.QueueCommand(SimulationCommand.BuildGoblinCompost(
            engine.CurrentTick.Next(), sequence: 3, position));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);

        Assert.Contains(engine.World.GetWorldObjectsAt(position), worldObject =>
            worldObject.Kind == WorldObjectKind.GoblinCompost);
    }

    [Fact]
    public void WoodenWatchtowerBuildsAReachableUpperPlatform()
    {
        var engine = CreateEngine(initialWoodStock: 8);
        var actorPositions = engine.CreateSnapshot().Actors
            .Select(actor => actor.Position)
            .ToHashSet();
        var position = Enumerable.Range(0, engine.Map.Width - 1)
            .SelectMany(x => Enumerable.Range(0, engine.Map.Height - 1)
                .Select(y => new GridPosition(x, y)))
            .First(candidate =>
            {
                var footprint = SimulationCommand.GetAreaCells(
                    candidate,
                    candidate with { X = candidate.X + 1, Y = candidate.Y + 1 });
                return footprint.All(cell =>
                           engine.Visibility.TryGet(cell, out var visibility) &&
                           visibility.IsDiscovered() &&
                           !actorPositions.Contains(cell)) &&
                       engine.World.CanBuildWoodenWatchtower(candidate);
            });

        engine.QueueCommand(SimulationCommand.BuildWoodenWatchtower(
            engine.CurrentTick.Next(), sequence: 1, position));
        engine.AdvanceTicks(1);

        var site = Assert.Single(engine.CreateSnapshot().ConstructionSites);
        Assert.Equal(ConstructionKind.WoodenWatchtower, site.Kind);
        Assert.Equal(4, site.Footprint.Count);
        Assert.Equal(8, Assert.Single(site.Materials).RequiredQuantity);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
        engine = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        var watchtower = Assert.Single(engine.World.GetWorldObjectsAt(position), worldObject =>
            worldObject.Kind == WorldObjectKind.WoodenWatchtower);

        Assert.Equal(8, watchtower.Parts.Count);
        Assert.Equal(4, watchtower.Parts.Count(part =>
            part.Channel == SpatialOccupancyChannel.Solid &&
            part.Kind == WorldObjectPartKind.WatchtowerSupport &&
            part.RelativePosition.Z == 0));
        Assert.Equal(4, watchtower.Parts.Count(part =>
            part.Channel == SpatialOccupancyChannel.Surface &&
            part.Kind == WorldObjectPartKind.WatchtowerPlatform &&
            part.RelativePosition.Z == 1));
        Assert.All(watchtower.Parts.Where(part => part.RelativePosition.Z == 1), part =>
            Assert.True(engine.World.IsTerrainTraversable(new GridPosition(
                watchtower.Anchor.X + part.RelativePosition.X,
                watchtower.Anchor.Y + part.RelativePosition.Y,
                watchtower.Anchor.Z + part.RelativePosition.Z))));
        Assert.True(ConstructionDismantlingPolicy.TryGetConstructionKind(
            watchtower.Kind,
            out var construction));
        Assert.Equal(ConstructionKind.WoodenWatchtower, construction);
    }

    [Fact]
    public void ReedSleepingMatBuildsOutsideShelterAndSurvivesSaveLoad()
    {
        var engine = AddLooseStack(
            CreateEngine(initialWoodStock: 0),
            ResourceKind.Reeds,
            quantity: 2);
        var ruin = Assert.Single(engine.World.CreateWorldObjectSnapshot(), worldObject =>
            worldObject.Kind == WorldObjectKind.GoblinRuin);
        var shelterCells = ruin.GetAbsoluteParts()
            .Select(item => item.Position)
            .ToHashSet();
        var position = Enumerable.Range(0, engine.Map.Width)
            .SelectMany(x => Enumerable.Range(0, engine.Map.Height)
                .Select(y => new GridPosition(x, y)))
            .First(candidate =>
                !shelterCells.Contains(candidate) &&
                engine.Visibility.TryGet(candidate, out var visibility) &&
                visibility.IsDiscovered() &&
                engine.World.CanBuildReedSleepingMat(candidate));

        Assert.True(engine.World.IsOpenToSky(position));

        engine.QueueCommand(SimulationCommand.BuildReedSleepingMat(
            engine.CurrentTick.Next(), sequence: 1, position));
        engine.AdvanceTicks(1);

        var site = Assert.Single(engine.CreateSnapshot().ConstructionSites);
        Assert.Equal(ConstructionKind.ReedSleepingMat, site.Kind);
        Assert.Equal(2, Assert.Single(site.Materials).RequiredQuantity);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
        engine = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);

        var sleepingMat = Assert.Single(
            engine.World.GetWorldObjectsAt(position),
            worldObject => worldObject.Kind == WorldObjectKind.ReedSleepingMat);
        Assert.Equal(WorldObjectPartKind.SleepingMat, Assert.Single(sleepingMat.Parts).Kind);
        Assert.True(engine.World.IsTerrainTraversable(position));
        Assert.True(ConstructionDismantlingPolicy.TryGetConstructionKind(
            sleepingMat.Kind,
            out var construction));
        Assert.Equal(ConstructionKind.ReedSleepingMat, construction);
    }

    [Fact]
    public void StandingTorchBuildsAsATraversableOmnidirectionalLight()
    {
        var engine = CreateEngine(initialWoodStock: 1);
        var position = Enumerable.Range(0, engine.Map.Width)
            .SelectMany(x => Enumerable.Range(0, engine.Map.Height)
                .Select(y => new GridPosition(x, y)))
            .First(candidate =>
                engine.Visibility.TryGet(candidate, out var visibility) &&
                visibility.IsDiscovered() &&
                engine.World.CanBuildStandingTorch(candidate));

        engine.QueueCommand(SimulationCommand.BuildStandingTorch(
            engine.CurrentTick.Next(), sequence: 1, position));
        engine.AdvanceTicks(1);

        Assert.Equal(
            ConstructionKind.StandingTorch,
            Assert.Single(engine.CreateSnapshot().ConstructionSites).Kind);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
        engine = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);

        var torch = Assert.Single(
            engine.World.GetWorldObjectsAt(position),
            worldObject => worldObject.Kind == WorldObjectKind.StandingTorch);
        Assert.Equal(WorldObjectPartKind.StandingTorch, Assert.Single(torch.Parts).Kind);
        Assert.True(engine.World.IsTerrainTraversable(position));
        Assert.True(LightEmitterCatalog.TryGet(torch.Kind, out var light));
        Assert.Equal(LightEmitterCatalog.WallTorchId, light.Id);
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

    private static SimulationEngine AddLooseStack(
        SimulationEngine engine,
        ResourceKind resource,
        int quantity)
    {
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var nextId = save["nextEntityId"]!.GetValue<ulong>();
        var position = engine.Map.GoblinSpawn;
        save["itemStacks"]!.AsArray().Add(new JsonObject
        {
            ["id"] = nextId,
            ["resource"] = (int)resource,
            ["foodKind"] = (int)FoodKind.None,
            ["variant"] = (int)ResourceVariant.None,
            ["quantity"] = quantity,
            ["locationKind"] = (int)ItemLocationKind.Ground,
            ["x"] = position.X,
            ["y"] = position.Y,
            ["z"] = position.Z,
            ["ownerId"] = EntityId.None.Value,
        });
        save["nextEntityId"] = nextId + 1;
        return SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
    }

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
