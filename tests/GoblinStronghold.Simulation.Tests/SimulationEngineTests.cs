using System.Text.Json.Nodes;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Construction;
using GoblinStronghold.Simulation.Equipment;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class SimulationEngineTests
{
    private static readonly SimulationTick FinalTick = new(480);

    [Fact]
    public void PresentationSnapshotCanSkipTheAuthoritativeStateHash()
    {
        var seed = new WorldSeed(0x50524553454E54UL);
        var map = SwampMapGenerator.Generate(seed, 48, 48);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 2,
            initialFoodStock: 4);

        var presentation = engine.CreateSnapshot(includeStateHash: false);
        var authoritative = engine.CreateSnapshot();

        Assert.Empty(presentation.StateHash);
        Assert.NotEmpty(authoritative.StateHash);
        Assert.Equal(authoritative.Tick, presentation.Tick);
        Assert.Equal(authoritative.Actors, presentation.Actors);
        Assert.Equal(authoritative.MapFingerprint, presentation.MapFingerprint);
    }

    [Fact]
    public void PresentationSnapshotBuildsAreMeasuredSeparatelyFromSimulationTicks()
    {
        var seed = new WorldSeed(0x505245534D4554UL);
        var map = SwampMapGenerator.Generate(seed, 48, 48);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 2,
            initialFoodStock: 4);

        engine.CreatePresentationSnapshot();
        var first = engine.GetMetrics().PresentationSnapshots;
        engine.CreatePresentationSnapshot();
        var second = engine.GetMetrics().PresentationSnapshots;

        Assert.Equal(1, first.Builds);
        Assert.Equal(2, second.Builds);
        Assert.True(first.LastBuildDuration >= TimeSpan.Zero);
        Assert.True(second.TotalBuildDuration >= first.TotalBuildDuration);
        Assert.Equal(0, engine.GetMetrics().TicksExecuted);
    }

    [Fact]
    public void LoadClearsAStaleTransientJobInsteadOfRejectingTheSave()
    {
        var seed = new WorldSeed(0x5354414C454A4FUL);
        var map = SwampMapGenerator.Generate(seed, 48, 48);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 0);
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        var actor = save["actors"]?.AsArray()[0]?.AsObject()
            ?? throw new InvalidOperationException("The save does not contain its initial actor.");
        actor["jobKind"] = (int)ActorJobKind.Move;
        actor["jobPhase"] = (int)ActorJobPhase.Traveling;
        actor["jobTargetX"] = -1;
        actor["jobTargetY"] = -1;
        actor["remainingRoute"] = new JsonArray(new JsonObject
        {
            ["x"] = -1,
            ["y"] = -1,
            ["z"] = 0,
        });

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);

        var restoredActor = Assert.Single(restored.CreateSnapshot().Actors);
        Assert.Equal(ActorJobKind.None, restoredActor.Job.Kind);
        Assert.Equal(ActorJobPhase.None, restoredActor.Job.Phase);
        Assert.Equal(0, restoredActor.Job.RemainingRouteSteps);
    }

    [Fact]
    public void DefaultActiveMapUsesTheExpandedNinetySixCellRegion()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0x4C41524745UL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0);

        Assert.Equal(SwampMapGenerator.DefaultDimension, engine.Map.Width);
        Assert.Equal(SwampMapGenerator.DefaultDimension, engine.Map.Height);
        Assert.True(engine.Map.LevelCount >= 4);
    }

    [Fact]
    public void GoblinProfilesAreDeterministicAndSurviveSaveLoad()
    {
        var first = SimulationEngine.Create(
            new WorldSeed(991),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 8,
            initialFoodStock: 0);
        var second = SimulationEngine.Create(
            new WorldSeed(991),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 8,
            initialFoodStock: 0);

        Assert.Equal(first.CreateSnapshot().Actors, second.CreateSnapshot().Actors);
        Assert.Equal(8, first.CreateSnapshot().Actors.Select(actor => actor.Name).Distinct().Count());
        Assert.All(first.CreateSnapshot().Actors, actor =>
        {
            Assert.False(string.IsNullOrWhiteSpace(actor.Name));
            Assert.NotEqual(GoblinSkill.None, actor.KnownSkills);
            Assert.NotEqual(GoblinTrait.None, actor.KnownTraits);
            Assert.True(actor.WorkPreferences.IsValid);
        });
        Assert.True(first.CreateSnapshot().Actors
            .Select(actor => actor.WorkPreferences)
            .Distinct()
            .Count() > 1);

        var restored = SimulationEngine.Load(first.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(first.CreateSnapshot().Actors, restored.CreateSnapshot().Actors);
        Assert.Equal(first.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void OldSaveWithoutWorkPreferencesDerivesTheOriginalDeterministicProfiles()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0x505245464552454EUL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 4,
            initialFoodStock: 0);
        var expected = engine.CreateSnapshot().Actors
            .Select(actor => actor.WorkPreferences)
            .ToArray();
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");

        foreach (var actor in save["actors"]?.AsArray() ?? [])
        {
            var actorObject = actor?.AsObject()
                ?? throw new InvalidOperationException("The save contains an invalid actor.");
            actorObject.Remove("foragingPreference");
            actorObject.Remove("haulingPreference");
            actorObject.Remove("buildingPreference");
        }

        var restored = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);

        Assert.Equal(expected, restored.CreateSnapshot().Actors
            .Select(actor => actor.WorkPreferences));
    }

    [Fact]
    public void InitialBrushwoodIsDeterministicPhysicalWoodOnLand()
    {
        var seed = new WorldSeed(995);
        var first = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            scatterInitialBrushwood: true);
        var second = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            scatterInitialBrushwood: true);
        var brushwood = first.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Wood)
            .ToArray();

        Assert.NotEmpty(brushwood);
        Assert.Equal(brushwood, second.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Wood));
        Assert.All(brushwood, stack =>
        {
            Assert.Equal(ItemLocationKind.Ground, stack.Location.Kind);
            Assert.True(first.Map.IsTerrainSurfacePosition(stack.Location.Position));
            Assert.True(first.Map.GetColumnCell(stack.Location.Position).Terrain is
                TerrainKind.SolidGround or TerrainKind.Mud);
            Assert.Equal(
                TerrainRampDirection.None,
                first.Map.GetColumnCell(stack.Location.Position).RampDirection);
            Assert.True(first.World.IsTerrainTraversable(stack.Location.Position));
        });
        Assert.Contains(brushwood, stack =>
            Math.Abs(stack.Location.Position.X - first.Map.GoblinSpawn.X) +
            Math.Abs(stack.Location.Position.Y - first.Map.GoblinSpawn.Y) <= 6);
    }

    [Fact]
    public void WoodStockpileCausesBrushwoodHaulingAndExperienceGain()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(996),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialWoodStock: 6);
        var zonePosition = engine.Map.GetCardinalNeighbors(engine.Map.GoblinSpawn)
            .First(engine.World.IsSurfaceTraversable);
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1),
            sequence: 1,
            zonePosition,
            ResourceKind.Wood,
            capacity: 64));

        engine.AdvanceTicks(160);

        var snapshot = engine.CreateSnapshot();
        var zone = Assert.Single(snapshot.StorageZones);
        Assert.True(zone.StoredQuantity > 0);
        var actor = Assert.Single(snapshot.Actors);
        Assert.True(actor.Experience.Foraging > 0);
        Assert.True(actor.Experience.Hauling > 0);
        Assert.True(actor.KnownSkills.HasFlag(GoblinSkill.Foraging));
        Assert.True(actor.KnownSkills.HasFlag(GoblinSkill.Hauling));

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(actor.Experience, Assert.Single(restored.CreateSnapshot().Actors).Experience);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void BuildingFoodStorageConsumesExactlyTwoWood()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(992),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialWoodStock: 5);
        var position = engine.Map.GoblinSpawn;
        engine.QueueCommand(SimulationCommand.BuildFoodStorage(
            new SimulationTick(1),
            sequence: 1,
            position));

        engine.AdvanceTicks(1);

        var ordered = Assert.Single(engine.CreateSnapshot().ConstructionSites);
        Assert.Equal(ConstructionKind.FoodStorage, ordered.Kind);
        Assert.Equal(2, Assert.Single(ordered.Materials).MissingQuantity);
        Assert.Empty(engine.CreateSnapshot().StorageZones);

        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);

        var foodStorage = Assert.Single(engine.CreateSnapshot().StorageZones, zone =>
            zone.Position == position);
        Assert.Equal(96, foodStorage.Capacity);
        Assert.Equal(foodStorage.Capacity, foodStorage.DesiredQuantity);
        Assert.Equal(3, foodStorage.TypeSlotCount);
        Assert.Equal(32, foodStorage.StackCapacity);
        Assert.Equal(3, engine.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Wood)
            .Sum(stack => stack.Quantity));
        Assert.Contains(engine.DrainEvents(), item =>
            item.Kind == SimulationEventKind.ConstructionCompleted && item.Amount == 2);
        var builder = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.True(builder.Experience.Building > 0);
        Assert.True(builder.Experience.Hauling > 0);
        Assert.True(builder.KnownSkills.HasFlag(GoblinSkill.Building));
    }

    [Fact]
    public void FoodStorageMayBePlannedOnOpenCaveFloorAtItsActualLevel()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0x4341564553544FUL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialWoodStock: 5);
        var caveFloor =
            (from level in Enumerable.Range(1, engine.Map.CaveLevelCount)
             from y in Enumerable.Range(0, engine.Map.Height)
             from x in Enumerable.Range(0, engine.Map.Width)
             let position = new GridPosition(x, y, -level)
             where engine.World.IsTerrainTraversable(position)
             select position).First();

        engine.QueueCommand(SimulationCommand.BuildFoodStorage(
            new SimulationTick(1),
            sequence: 1,
            caveFloor));
        engine.AdvanceTicks(1);

        var site = Assert.Single(engine.CreateSnapshot().ConstructionSites);
        Assert.Equal(ConstructionKind.FoodStorage, site.Kind);
        Assert.Equal(caveFloor, site.Anchor);
        Assert.Equal(caveFloor, site.End);
    }


    [Fact]
    public void BuildingWoodStorageConsumesTwoWoodAndAcceptsWood()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(997),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialWoodStock: 5);
        engine.QueueCommand(SimulationCommand.BuildWoodStorage(
            new SimulationTick(1),
            sequence: 1,
            engine.Map.GoblinSpawn));

        engine.AdvanceTicks(1);

        Assert.Single(engine.CreateSnapshot().ConstructionSites);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);

        var zone = Assert.Single(engine.CreateSnapshot().StorageZones);
        Assert.Equal(ResourceKind.Wood, zone.AcceptedResource);
        Assert.Equal(zone.Capacity, zone.DesiredQuantity);
        Assert.Equal(3, engine.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Wood)
            .Sum(stack => stack.Quantity));
    }

    [Fact]
    public void WoodenWallBlueprintBuildsAConnectedSolidObstacle()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(998),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialWoodStock: 2);
        var position = engine.Map.GetCardinalNeighbors(engine.Map.GoblinSpawn)
            .First(engine.World.CanBuildWoodenBarrier);
        engine.QueueCommand(SimulationCommand.BuildWoodenWall(
            new SimulationTick(1), sequence: 1, position));

        engine.AdvanceTicks(1);

        var site = Assert.Single(engine.CreateSnapshot().ConstructionSites);
        Assert.Equal(ConstructionKind.WoodenWall, site.Kind);
        Assert.Equal(2, Assert.Single(site.Materials).RequiredQuantity);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);

        Assert.Contains(engine.World.GetWorldObjectsAt(position), worldObject =>
            worldObject.Kind == WorldObjectKind.WoodenWall &&
            worldObject.Parts.Single().Kind == WorldObjectPartKind.Wall);
        Assert.False(engine.World.IsSurfaceTraversable(position));
        Assert.Equal(0, engine.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Wood)
            .Sum(stack => stack.Quantity));
    }

    [Fact]
    public void StoneWallBlueprintConsumesStoneAndPreservesWood()
    {
        var seed = new WorldSeed(0x53544F4E4557414CUL);
        var generated = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialWoodStock: 2,
            scatterInitialBrushwood: true);
        var save = JsonNode.Parse(generated.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        var stone = save["itemStacks"]!.AsArray()
            .Select(node => node!.AsObject())
            .First(model => model["resource"]!.GetValue<int>() == (int)ResourceKind.Stone);
        stone["x"] = generated.Map.GoblinSpawn.X;
        stone["y"] = generated.Map.GoblinSpawn.Y;
        stone["z"] = generated.Map.GoblinSpawn.Z;
        stone["quantity"] = 2;
        stone["variant"] = (int)ResourceVariant.Sandstone;

        var engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        var initialWoodQuantity = engine.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Wood)
            .Sum(stack => stack.Quantity);
        var initialStoneQuantity = engine.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Stone)
            .Sum(stack => stack.Quantity);
        var position = engine.Map.GetCardinalNeighbors(engine.Map.GoblinSpawn)
            .First(engine.World.CanBuildWoodenBarrier);
        engine.QueueCommand(SimulationCommand.BuildStoneWall(
            new SimulationTick(1), sequence: 1, position));

        engine.AdvanceTicks(1);

        var site = Assert.Single(engine.CreateSnapshot().ConstructionSites);
        Assert.Equal(ConstructionKind.StoneWall, site.Kind);
        var material = Assert.Single(site.Materials);
        Assert.Equal(ResourceKind.Stone, material.Resource);
        Assert.Equal(2, material.RequiredQuantity);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());

        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(restored);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Contains(engine.DrainEvents(), simulationEvent =>
            simulationEvent.Kind == SimulationEventKind.ConstructionCompleted &&
            simulationEvent.Construction == ConstructionKind.StoneWall);
        Assert.Contains(engine.World.GetWorldObjectsAt(position), worldObject =>
            worldObject.Kind == WorldObjectKind.StoneWall &&
            worldObject.Parts.Single().Kind == WorldObjectPartKind.Wall);
        Assert.False(engine.World.IsSurfaceTraversable(position));
        Assert.Equal(initialStoneQuantity - 2, engine.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Stone)
            .Sum(stack => stack.Quantity));
        Assert.Equal(initialWoodQuantity, engine.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Wood)
            .Sum(stack => stack.Quantity));
    }

    [Fact]
    public void LegacyConstructionSaveWithoutResourceDefaultsToWood()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0x4C4547414359574CUL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialWoodStock: 2);
        var position = engine.Map.GetCardinalNeighbors(engine.Map.GoblinSpawn)
            .First(engine.World.CanBuildWoodenBarrier);
        engine.QueueCommand(SimulationCommand.BuildWoodenWall(
            new SimulationTick(1), sequence: 1, position));
        engine.AdvanceTicks(1);
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        Assert.True(save["constructionSites"]!.AsArray()[0]!.AsObject()
            .Remove("requiredResource"));

        var restored = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);

        var material = Assert.Single(Assert.Single(restored.CreateSnapshot().ConstructionSites).Materials);
        Assert.Equal(ResourceKind.Wood, material.Resource);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void WoodenWallLineBuildsIndependentSegmentsAndSurvivesSaveLoad()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0x57414C4C4C494E45UL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialWoodStock: 6);
        var cells = FindBuildableWallLine(engine, segmentCount: 3);
        engine.QueueCommand(SimulationCommand.BuildWoodenWall(
            new SimulationTick(1), sequence: 1, cells[0], cells[^1]));

        engine.AdvanceTicks(1);

        var sites = engine.CreateSnapshot().ConstructionSites
            .OrderBy(site => site.Id)
            .ToArray();
        Assert.Equal(cells.Count, sites.Length);
        Assert.Equal(cells, sites.Select(site => site.Anchor));
        Assert.All(sites, site =>
        {
            Assert.Equal([site.Anchor], site.Footprint);
            Assert.Equal(2, Assert.Single(site.Materials).RequiredQuantity);
            Assert.Equal(45, site.TotalWorkTicks);
        });
        Assert.Equal(6, sites.Sum(site => Assert.Single(site.Materials).RequiredQuantity));
        Assert.Equal(135, sites.Sum(site => site.TotalWorkTicks));
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());

        for (var tick = 0; tick < 1_000 && cells.All(cell =>
                 engine.World.GetWorldObjectsAt(cell).All(worldObject =>
                     worldObject.Kind != WorldObjectKind.WoodenWall)); tick++)
        {
            engine.AdvanceTicks(1);
        }

        var completedSegments = cells.Count(cell => engine.World.GetWorldObjectsAt(cell)
            .Any(worldObject => worldObject.Kind == WorldObjectKind.WoodenWall));
        Assert.InRange(completedSegments, 1, cells.Count - 1);
        Assert.Equal(cells.Count - completedSegments, engine.CreateSnapshot().ConstructionSites.Count);

        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(restored);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        var wallObjects = cells
            .Select(cell => Assert.Single(engine.World.GetWorldObjectsAt(cell)
                .Where(worldObject => worldObject.Kind == WorldObjectKind.WoodenWall)))
            .ToArray();
        Assert.Equal(cells.Count, wallObjects.Select(worldObject => worldObject.Id).Distinct().Count());
        Assert.All(cells, cell => Assert.False(engine.World.IsSurfaceTraversable(cell)));
    }

    [Fact]
    public void WoodenDoorFrameBlueprintRemainsTraversable()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(999),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialWoodStock: 1);
        var position = engine.Map.GetCardinalNeighbors(engine.Map.GoblinSpawn)
            .First(engine.World.CanBuildWoodenBarrier);
        engine.QueueCommand(SimulationCommand.BuildWoodenDoorFrame(
            new SimulationTick(1), sequence: 1, position));

        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);

        Assert.Contains(engine.World.GetWorldObjectsAt(position), worldObject =>
            worldObject.Kind == WorldObjectKind.WoodenDoorFrame &&
            worldObject.Parts.Single().Kind == WorldObjectPartKind.Door);
        Assert.True(engine.World.IsSurfaceTraversable(position));
        Assert.True(engine.Navigation.HasSurfacePath(engine.Map.GoblinSpawn, position));
    }

    [Fact]
    public void WoodenDoorFrameAtomicallyReplacesCompletedWall()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0x444F4F5257414C4CUL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialWoodStock: 3);
        var position = FindBuildableWallLine(engine, segmentCount: 1)[0];
        engine.QueueCommand(SimulationCommand.BuildWoodenWall(
            new SimulationTick(1), sequence: 1, position));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
        var wallId = Assert.Single(engine.World.GetWorldObjectsAt(position)
            .Where(worldObject => worldObject.Kind == WorldObjectKind.WoodenWall)).Id;

        engine.QueueCommand(SimulationCommand.BuildWoodenDoorFrame(
            engine.CurrentTick.Next(), sequence: 2, position));
        engine.AdvanceTicks(1);

        var site = Assert.Single(engine.CreateSnapshot().ConstructionSites);
        Assert.Equal(ConstructionKind.WoodenDoorFrame, site.Kind);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(restored);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        var replacement = Assert.Single(engine.World.GetWorldObjectsAt(position));
        Assert.Equal(wallId, replacement.Id);
        Assert.Equal(WorldObjectKind.WoodenDoorFrame, replacement.Kind);
        Assert.Equal(WorldObjectPartKind.Door, Assert.Single(replacement.Parts).Kind);
        Assert.True(engine.World.IsSurfaceTraversable(position));
    }

    [Fact]
    public void StoneDoorFrameReplacesStoneWallAndAcceptsWoodenDoorLeaf()
    {
        var seed = new WorldSeed(0x53544F4E45444F4FUL);
        var generated = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialWoodStock: 1,
            scatterInitialBrushwood: true);
        var save = JsonNode.Parse(generated.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        var stone = save["itemStacks"]!.AsArray()
            .Select(node => node!.AsObject())
            .First(model => model["resource"]!.GetValue<int>() == (int)ResourceKind.Stone);
        stone["x"] = generated.Map.GoblinSpawn.X;
        stone["y"] = generated.Map.GoblinSpawn.Y;
        stone["z"] = generated.Map.GoblinSpawn.Z;
        stone["quantity"] = 3;
        stone["variant"] = (int)ResourceVariant.Granite;

        var engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        var initialStoneQuantity = engine.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Stone)
            .Sum(stack => stack.Quantity);
        var initialWoodQuantity = engine.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Wood)
            .Sum(stack => stack.Quantity);
        var position = engine.Map.GetCardinalNeighbors(engine.Map.GoblinSpawn)
            .First(engine.World.CanBuildWoodenBarrier);
        engine.QueueCommand(SimulationCommand.BuildStoneWall(
            new SimulationTick(1), sequence: 1, position));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
        var wallId = Assert.Single(engine.World.GetWorldObjectsAt(position)
            .Where(worldObject => worldObject.Kind == WorldObjectKind.StoneWall)).Id;

        engine.QueueCommand(SimulationCommand.BuildStoneDoorFrame(
            engine.CurrentTick.Next(), sequence: 2, position));
        engine.AdvanceTicks(1);

        var frameSite = Assert.Single(engine.CreateSnapshot().ConstructionSites);
        Assert.Equal(ConstructionKind.StoneDoorFrame, frameSite.Kind);
        Assert.Equal(ResourceKind.Stone, Assert.Single(frameSite.Materials).Resource);
        Assert.Equal(PersonalEquipment.None, frameSite.Capabilities.RequiredEquipment);
        Assert.Equal(ToolFunction.Construction, frameSite.Capabilities.RequiredToolFunction);
        Assert.Equal(1, frameSite.Capabilities.MinimumToolLevel);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(restored);

        var frame = Assert.Single(engine.World.GetWorldObjectsAt(position));
        Assert.Equal(wallId, frame.Id);
        Assert.Equal(WorldObjectKind.StoneDoorFrame, frame.Kind);
        Assert.True(engine.World.IsSurfaceTraversable(position));
        engine.QueueCommand(SimulationCommand.BuildWoodenDoor(
            engine.CurrentTick.Next(), sequence: 3, position));
        restored.QueueCommand(SimulationCommand.BuildWoodenDoor(
            restored.CurrentTick.Next(), sequence: 3, position));
        engine.AdvanceTicks(1);
        restored.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(restored);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Contains(engine.World.GetWorldObjectsAt(position), worldObject =>
            worldObject.Kind == WorldObjectKind.StoneDoorFrame);
        Assert.Contains(engine.World.GetWorldObjectsAt(position), worldObject =>
            worldObject.Kind == WorldObjectKind.WoodenDoorLeaf &&
            worldObject.Parts.Single().Kind == WorldObjectPartKind.ClosedDoorLeaf);
        Assert.False(engine.World.IsSurfaceTraversable(position));
        Assert.Equal(initialStoneQuantity - 3, engine.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Stone)
            .Sum(stack => stack.Quantity));
        Assert.Equal(initialWoodQuantity - 1, engine.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Wood)
            .Sum(stack => stack.Quantity));
    }

    [Fact]
    public void WallTorchCanBeBuiltOnDiscoveredCaveRockAndSurvivesSaveLoad()
    {
        var seed = new WorldSeed(0x544F524348434156UL);
        var map = SwampMapGenerator.Generate(seed, 64, 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialWoodStock: 1);
        var placement = (
                from z in new[] { -1, -2 }
                from y in Enumerable.Range(0, map.Height)
                from x in Enumerable.Range(0, map.Width)
                let wall = new GridPosition(x, y, z)
                where engine.World.IsSolidCaveRock(wall)
                from access in engine.World.GetCardinalWorldNeighbors(wall)
                where engine.World.IsTerrainTraversable(access)
                let route = engine.Navigation.FindPath(map.GoblinSpawn, access)
                where route is not null
                orderby route.Count, z descending, y, x
                select new { Wall = wall, Access = access })
            .First();
        var actor = Assert.Single(engine.CreateSnapshot().Actors);
        engine.QueueCommand(SimulationCommand.Move(
            new SimulationTick(1), sequence: 1, actor.Id, placement.Access));
        for (var tick = 0; tick < 20_000 &&
             engine.CreateSnapshot().Actors.Single().Position != placement.Access; tick++)
        {
            engine.AdvanceTicks(1);
        }
        Assert.Equal(placement.Access, engine.CreateSnapshot().Actors.Single().Position);
        Assert.True(engine.CreateSnapshot()
            .GetVisibility(placement.Wall, map.Width)
            .IsDiscovered());

        Assert.True(WallTorchPlacementPolicy.TryResolvePreferredSide(
            placement.Wall,
            placement.Access,
            out var preferredSide));
        engine.QueueCommand(SimulationCommand.BuildWallTorch(
            engine.CurrentTick.Next(), sequence: 2, placement.Wall, preferredSide));
        engine.AdvanceTicks(1);

        var site = Assert.Single(engine.CreateSnapshot().ConstructionSites);
        Assert.Equal(ConstructionKind.WallTorch, site.Kind);
        Assert.Equal(placement.Wall, site.Anchor);
        Assert.Equal(ResourceKind.Wood, Assert.Single(site.Materials).Resource);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(restored);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        var torch = Assert.Single(engine.World.GetWorldObjectsAt(placement.Wall), worldObject =>
            worldObject.Kind == WorldObjectKind.WallTorch);
        Assert.Equal(preferredSide, torch.Orientation);
        Assert.Equal(WorldObjectPartKind.WallTorch, Assert.Single(torch.Parts).Kind);
        Assert.True(engine.World.IsSolidCaveRock(placement.Wall));
        Assert.False(engine.World.CanBuildWallTorch(placement.Wall));
    }

    [Fact]
    public void WoodenDoorBuildsInFrameAndToggleStateSurvivesSaveLoad()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0x444F4F524C454146UL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialWoodStock: 2);
        var position = FindBuildableWallLine(engine, segmentCount: 1)[0];
        engine.QueueCommand(SimulationCommand.BuildWoodenDoorFrame(
            new SimulationTick(1), sequence: 1, position));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);

        engine.QueueCommand(SimulationCommand.BuildWoodenDoor(
            engine.CurrentTick.Next(), sequence: 2, position));
        engine.AdvanceTicks(1);

        var site = Assert.Single(engine.CreateSnapshot().ConstructionSites);
        Assert.Equal(ConstructionKind.WoodenDoor, site.Kind);
        Assert.Equal(1, Assert.Single(site.Materials).RequiredQuantity);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(restored);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        var frame = Assert.Single(engine.World.GetWorldObjectsAt(position)
            .Where(worldObject => worldObject.Kind == WorldObjectKind.WoodenDoorFrame));
        var closedLeaf = Assert.Single(engine.World.GetWorldObjectsAt(position)
            .Where(worldObject => worldObject.Kind == WorldObjectKind.WoodenDoorLeaf));
        Assert.Equal(frame.Orientation, closedLeaf.Orientation);
        Assert.Equal(WorldObjectPartKind.ClosedDoorLeaf, Assert.Single(closedLeaf.Parts).Kind);
        Assert.False(engine.World.IsSurfaceTraversable(position));
        var verticalSightTopologyVersion = engine.World.VerticalSightTopologyVersion;

        engine.QueueCommand(SimulationCommand.ToggleWoodenDoor(
            engine.CurrentTick.Next(), sequence: 3, position));
        restored.QueueCommand(SimulationCommand.ToggleWoodenDoor(
            restored.CurrentTick.Next(), sequence: 3, position));
        engine.AdvanceTicks(1);
        restored.AdvanceTicks(1);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.True(engine.World.TryGetWoodenDoorState(position, out var isOpen));
        Assert.True(isOpen);
        Assert.True(engine.World.IsSurfaceTraversable(position));
        Assert.Equal(verticalSightTopologyVersion, engine.World.VerticalSightTopologyVersion);
        var openSave = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), openSave.ComputeStateHash());

        engine.QueueCommand(SimulationCommand.ToggleWoodenDoor(
            engine.CurrentTick.Next(), sequence: 4, position));
        restored.QueueCommand(SimulationCommand.ToggleWoodenDoor(
            restored.CurrentTick.Next(), sequence: 4, position));
        engine.AdvanceTicks(1);
        restored.AdvanceTicks(1);
        Assert.False(engine.World.IsSurfaceTraversable(position));

        var actorId = Assert.Single(engine.CreateSnapshot().Actors).Id;
        var actorPosition = Assert.Single(engine.CreateSnapshot().Actors).Position;
        Assert.Null(engine.World.FindSurfacePath(actorPosition, position));
        Assert.NotNull(engine.Navigation.FindSurfacePath(actorPosition, position));
        var restoredActorId = Assert.Single(restored.CreateSnapshot().Actors).Id;
        engine.QueueCommand(SimulationCommand.Move(
            engine.CurrentTick.Next(), sequence: 5, actorId, position));
        restored.QueueCommand(SimulationCommand.Move(
            restored.CurrentTick.Next(), sequence: 5, restoredActorId, position));
        SimulationEngine? automaticallyOpenSave = null;
        for (var tick = 0; tick < 100 &&
             engine.CreateSnapshot().Actors.Single().Position != position; tick++)
        {
            engine.AdvanceTicks(1);
            restored.AdvanceTicks(1);
            var doorPart = engine.World.GetWorldObjectsAt(position)
                .Single(worldObject => worldObject.Kind == WorldObjectKind.WoodenDoorLeaf)
                .Parts.Single().Kind;
            if (automaticallyOpenSave is null &&
                doorPart == WorldObjectPartKind.AutomaticallyOpenedDoorLeaf)
            {
                automaticallyOpenSave = SimulationEngine.Load(
                    engine.Save(),
                    SimulationDefinitions.Foundation);
            }
        }
        Assert.Equal(position, engine.CreateSnapshot().Actors.Single().Position);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.NotNull(automaticallyOpenSave);
        automaticallyOpenSave.AdvanceTicks((int)(
            engine.CurrentTick.Value - automaticallyOpenSave.CurrentTick.Value));
        Assert.Equal(engine.ComputeStateHash(), automaticallyOpenSave.ComputeStateHash());
        Assert.True(engine.World.TryGetWoodenDoorState(position, out isOpen));
        Assert.True(isOpen);
        engine.QueueCommand(SimulationCommand.ToggleWoodenDoor(
            engine.CurrentTick.Next(), sequence: 6, position));
        engine.AdvanceTicks(1);

        Assert.True(engine.World.TryGetWoodenDoorState(position, out isOpen));
        Assert.True(isOpen);
        Assert.Contains(engine.DrainEvents(), simulationEvent =>
            simulationEvent.Kind == SimulationEventKind.CommandRejected &&
            simulationEvent.Amount == (int)SimulationCommandKind.ToggleWoodenDoor);

        engine.QueueCommand(SimulationCommand.Move(
            engine.CurrentTick.Next(), sequence: 7, actorId, actorPosition));
        for (var tick = 0; tick < 100 &&
             engine.CreateSnapshot().Actors.Single().Position != actorPosition; tick++)
        {
            engine.AdvanceTicks(1);
        }

        Assert.Equal(actorPosition, engine.CreateSnapshot().Actors.Single().Position);
        Assert.True(engine.World.TryGetWoodenDoorState(position, out isOpen));
        Assert.False(isOpen);
        Assert.False(engine.World.IsSurfaceTraversable(position));

        engine.QueueCommand(SimulationCommand.ToggleWoodenDoor(
            engine.CurrentTick.Next(), sequence: 8, position));
        engine.AdvanceTicks(2);

        Assert.True(engine.World.TryGetWoodenDoorState(position, out isOpen));
        Assert.True(isOpen);
        Assert.Equal(
            WorldObjectPartKind.OpenDoorLeaf,
            Assert.Single(engine.World.GetWorldObjectsAt(position)
                .Single(worldObject => worldObject.Kind == WorldObjectKind.WoodenDoorLeaf)
                .Parts).Kind);
        var manuallyOpenSave = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), manuallyOpenSave.ComputeStateHash());
    }

    [Fact]
    public void ConstructionWithoutEnoughWoodRemainsAsUnsatisfiedBlueprint()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(993),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialWoodStock: 1);
        engine.QueueCommand(SimulationCommand.BuildFoodStorage(
            new SimulationTick(1),
            sequence: 1,
            engine.Map.GoblinSpawn));

        engine.AdvanceTicks(1);

        var snapshot = engine.CreateSnapshot();
        Assert.Empty(snapshot.StorageZones);
        var site = Assert.Single(snapshot.ConstructionSites);
        Assert.Equal(ConstructionKind.FoodStorage, site.Kind);
        Assert.Equal(2, Assert.Single(site.Materials).RequiredQuantity);
        Assert.Equal(0, Assert.Single(site.Materials).DeliveredQuantity);
        Assert.Equal(2, Assert.Single(site.Materials).MissingQuantity);
        Assert.Equal(1, engine.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Wood)
            .Sum(stack => stack.Quantity));
        Assert.Contains(engine.DrainEvents(), item =>
            item.Kind == SimulationEventKind.ConstructionOrdered && item.Target == site.Id);

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void ConstructionOrderDoesNotRequireAvailableMaterialsOrBuilder()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0x42554C44UL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 0,
            initialFoodStock: 0,
            initialWoodStock: 0);
        engine.QueueCommand(SimulationCommand.BuildFoodStorage(
            new SimulationTick(1),
            sequence: 1,
            engine.Map.GoblinSpawn));

        engine.AdvanceTicks(1);

        var snapshot = engine.CreateSnapshot();
        Assert.Empty(snapshot.Actors);
        Assert.Empty(snapshot.StorageZones);
        var site = Assert.Single(snapshot.ConstructionSites);
        Assert.Equal(2, Assert.Single(site.Materials).MissingQuantity);
        Assert.Equal(site.TotalWorkTicks, site.RemainingWorkTicks);
    }

    [Fact]
    public void ImpossibleConstructionRemainsUnassignedAndExplainsItsBlocker()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0x424C4F434B4544UL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialWoodStock: 0,
            scatterInitialBrushwood: false);
        engine.QueueCommand(SimulationCommand.BuildFoodStorage(
            new SimulationTick(1),
            sequence: 1,
            engine.Map.GoblinSpawn));

        engine.AdvanceTicks(1);

        var snapshot = engine.CreateSnapshot();
        var site = Assert.Single(snapshot.ConstructionSites);
        Assert.DoesNotContain(snapshot.Actors, actor =>
            actor.Job.Kind is ActorJobKind.SupplyConstruction or ActorJobKind.BuildConstruction);
        var diagnostic = engine.InspectConstructionReadiness(site.Id);
        Assert.Equal(ConstructionReadinessState.NoAvailableMaterials, diagnostic.State);
        Assert.Equal(2, diagnostic.MissingMaterialQuantity);
        Assert.Equal(0, diagnostic.InTransitQuantity);
        Assert.Equal(0, diagnostic.AvailableMaterialQuantity);
        Assert.Equal(0, diagnostic.MatchingSourceCount);
        Assert.Equal(1, diagnostic.CapableBuilderCount);
    }

    [Fact]
    public void ConstructionDiagnosticTracksMaterialDeliveryAndAssignedBuilder()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0x5245414459UL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialWoodStock: 2,
            scatterInitialBrushwood: false);
        var initialSnapshot = engine.CreateSnapshot();
        var occupied = initialSnapshot.ItemStacks
            .Where(stack => stack.Location.Kind == ItemLocationKind.Ground)
            .Select(stack => stack.Location.Position)
            .Concat(initialSnapshot.StorageZones.Select(zone => zone.Position))
            .ToHashSet();
        var position = Enumerable.Range(0, engine.Map.Height)
            .SelectMany(y => Enumerable.Range(0, engine.Map.Width)
                .Select(x => new GridPosition(x, y)))
            .Where(engine.World.IsSurfaceTraversable)
            .Where(candidate => !occupied.Contains(candidate))
            .OrderBy(candidate => Math.Abs(candidate.X - engine.Map.GoblinSpawn.X) +
                Math.Abs(candidate.Y - engine.Map.GoblinSpawn.Y))
            .First();
        engine.QueueCommand(SimulationCommand.BuildFoodStorage(
            new SimulationTick(1),
            sequence: 1,
            position));
        engine.AdvanceTicks(1);
        var siteId = Assert.Single(engine.CreateSnapshot().ConstructionSites).Id;

        Assert.Equal(
            ConstructionReadinessState.MaterialsInTransit,
            engine.InspectConstructionReadiness(siteId).State);

        for (var tick = 0; tick < 200 &&
             engine.CreateSnapshot().Actors.Single().Job.Kind != ActorJobKind.BuildConstruction; tick++)
        {
            engine.AdvanceTicks(1);
        }

        var diagnostic = engine.InspectConstructionReadiness(siteId);
        Assert.Equal(ConstructionReadinessState.Building, diagnostic.State);
        Assert.Equal(0, diagnostic.MissingMaterialQuantity);
        Assert.Equal(1, diagnostic.CapableBuilderCount);
    }

    [Fact]
    public void UrgentConstructionOutranksACloserLowPrioritySiteAndSurvivesSaveLoad()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0x5052494F52495459UL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialWoodStock: 4,
            scatterInitialBrushwood: false);
        var actor = Assert.Single(engine.CreateSnapshot().Actors);
        var lowPosition = engine.Map.GoblinSpawn;
        var urgentPosition = engine.Map.GetCardinalNeighbors(lowPosition)
            .First(engine.World.IsSurfaceTraversable);
        var moveDestination = Enumerable.Range(0, engine.Map.Height)
            .SelectMany(y => Enumerable.Range(0, engine.Map.Width)
                .Select(x => new GridPosition(x, y)))
            .Select(engine.Map.GetTerrainSurfacePosition)
            .OrderByDescending(position =>
                Math.Abs(position.X - actor.Position.X) + Math.Abs(position.Y - actor.Position.Y))
            .ThenBy(position => position.Y)
            .ThenBy(position => position.X)
            .First(position => engine.Navigation.HasPath(actor.Position, position));
        var executeAt = new SimulationTick(1);
        engine.QueueCommand(SimulationCommand.Move(
            executeAt, sequence: 1, actor.Id, moveDestination));
        engine.QueueCommand(SimulationCommand.BuildFoodStorage(
            executeAt, sequence: 2, lowPosition));
        engine.QueueCommand(SimulationCommand.BuildWoodStorage(
            executeAt, sequence: 3, urgentPosition));
        engine.AdvanceTicks(1);

        var sites = engine.CreateSnapshot().ConstructionSites;
        var low = sites.Single(site => site.Anchor == lowPosition);
        var urgent = sites.Single(site => site.Anchor == urgentPosition);
        executeAt = engine.CurrentTick.Next();
        engine.QueueCommand(SimulationCommand.ConfigureConstructionPriority(
            executeAt, sequence: 4, low.Id, StoragePriority.Low));
        engine.QueueCommand(SimulationCommand.ConfigureConstructionPriority(
            executeAt, sequence: 5, urgent.Id, StoragePriority.Urgent));
        engine.AdvanceTicks(1);

        var configured = engine.CreateSnapshot().ConstructionSites;
        Assert.Equal(StoragePriority.Low, configured.Single(site => site.Id == low.Id).Priority);
        Assert.Equal(StoragePriority.Urgent, configured.Single(site => site.Id == urgent.Id).Priority);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(
            StoragePriority.Urgent,
            restored.CreateSnapshot().ConstructionSites.Single(site => site.Id == urgent.Id).Priority);

        for (var tick = 0; tick < 2_000 &&
             engine.CreateSnapshot().Actors.Single().Job.Kind != ActorJobKind.SupplyConstruction; tick++)
        {
            engine.AdvanceTicks(1);
        }

        var supplier = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.Equal(ActorJobKind.SupplyConstruction, supplier.Job.Kind);
        Assert.Equal(urgent.Id, supplier.Job.DestinationZoneId);
    }

    [Fact]
    public void ConstructionSupplyPlanningDoesNotPathfindEveryLooseWoodStack()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0x504C414EUL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialWoodStock: 0,
            scatterInitialBrushwood: true);
        var woodStackCount = engine.CreateSnapshot().ItemStacks.Count(stack =>
            stack.Resource == ResourceKind.Wood);
        var occupied = engine.CreateSnapshot().ItemStacks
            .Where(stack => stack.Location.Kind == ItemLocationKind.Ground)
            .Select(stack => stack.Location.Position)
            .Concat(engine.CreateSnapshot().StorageZones.Select(zone => zone.Position))
            .ToHashSet();
        var position = Enumerable.Range(0, engine.Map.Height)
            .SelectMany(y => Enumerable.Range(0, engine.Map.Width)
                .Select(x => new GridPosition(x, y)))
            .Where(engine.World.IsSurfaceTraversable)
            .Where(candidate => !occupied.Contains(candidate))
            .OrderBy(candidate => Math.Abs(candidate.X - engine.Map.GoblinSpawn.X) +
                Math.Abs(candidate.Y - engine.Map.GoblinSpawn.Y))
            .First();
        var searchesBefore = engine.Navigation.GetMetrics().Searches;
        engine.QueueCommand(SimulationCommand.BuildFoodStorage(
            new SimulationTick(1),
            sequence: 1,
            position));

        engine.AdvanceTicks(1);

        var searchesAfter = engine.Navigation.GetMetrics().Searches;
        Assert.True(woodStackCount > 12, $"wood stacks: {woodStackCount}");
        Assert.InRange(searchesAfter - searchesBefore, 1, 4);
        Assert.Equal(
            ActorJobKind.SupplyConstruction,
            engine.CreateSnapshot().Actors.Single().Job.Kind);
        var simulationSnapshot = engine.CreateSnapshot();
        var indexedStackIds = engine.CreateResourceSpatialSnapshot().Entries
            .Select(entry => entry.StackId)
            .Order()
            .ToArray();
        var expectedStackIds = simulationSnapshot.ItemStacks
            .Where(stack => stack.Location.Kind != ItemLocationKind.ActorInventory)
            .Select(stack => stack.Id)
            .Order()
            .ToArray();
        Assert.Equal(expectedStackIds, indexedStackIds);
    }

    [Fact]
    public void ConstructionBlueprintClearsLooseStackBeforeBuilding()
    {
        var definitions = SimulationDefinitions.Foundation;
        var engine = SimulationEngine.Create(
            new WorldSeed(0x434C454152UL),
            definitions,
            initialGoblinCount: 1,
            initialFoodStock: 20,
            initialWoodStock: 8);
        var snapshot = engine.CreateSnapshot();
        var occupiedByStack = snapshot.ItemStacks
            .Where(stack => stack.Location.Kind == ItemLocationKind.Ground)
            .Select(stack => stack.Location.Position)
            .ToHashSet();
        var storagePositions = snapshot.StorageZones.Select(zone => zone.Position).ToHashSet();
        var workshop = Enumerable.Range(0, engine.Map.Height)
            .SelectMany(y => Enumerable.Range(0, engine.Map.Width)
                .Select(x => new GridPosition(x, y)))
            .Where(engine.World.CanBuildPrimitiveWorkshop)
            .Where(position => !occupiedByStack.Contains(position) &&
                !storagePositions.Contains(position) &&
                engine.Navigation.HasSurfacePath(engine.Map.GoblinSpawn, position))
            .OrderBy(position => Math.Abs(position.X - engine.Map.GoblinSpawn.X) +
                Math.Abs(position.Y - engine.Map.GoblinSpawn.Y))
            .First();
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var blockingStack = save["itemStacks"]!.AsArray()
            .First(item => item!["resource"]!.GetValue<int>() == (int)ResourceKind.Food)!
            .AsObject();
        var blockingStackId = new EntityId(blockingStack["id"]!.GetValue<ulong>());
        blockingStack["quantity"] = 1;
        blockingStack["locationKind"] = (int)ItemLocationKind.Ground;
        blockingStack["x"] = workshop.X;
        blockingStack["y"] = workshop.Y;
        blockingStack["z"] = workshop.Z;
        blockingStack["ownerId"] = EntityId.None.Value;
        engine = SimulationEngine.Load(save.ToJsonString(), definitions);
        engine.QueueCommand(SimulationCommand.BuildPrimitiveWorkshop(
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            workshop));

        for (var tick = 0; tick < 500 &&
             engine.CreateSnapshot().Actors.Single().Job.Kind !=
                 ActorJobKind.ClearConstructionSite; tick++)
        {
            engine.AdvanceTicks(1);
        }

        var clearing = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.Equal(ActorJobKind.ClearConstructionSite, clearing.Job.Kind);
        var constructionSite = Assert.Single(engine.CreateSnapshot().ConstructionSites);
        Assert.Equal(
            ConstructionReadinessState.AwaitingSiteClearance,
            engine.InspectConstructionReadiness(constructionSite.Id).State);
        var restored = SimulationEngine.Load(engine.Save(), definitions);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());

        for (var tick = 0; tick < 5_000 &&
             !engine.World.HasPrimitiveWorkshop(workshop); tick++)
        {
            engine.AdvanceTicks(1);
            restored.AdvanceTicks(1);
        }

        Assert.True(engine.World.HasPrimitiveWorkshop(workshop));
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        var movedStack = engine.CreateSnapshot().ItemStacks
            .Single(stack => stack.Id == blockingStackId);
        Assert.NotEqual(ItemLocation.OnGround(workshop), movedStack.Location);
    }

    [Fact]
    public void ConstructionClearanceReservesEachBlockingStackForOneGoblin()
    {
        var definitions = SimulationDefinitions.Foundation;
        var engine = SimulationEngine.Create(
            new WorldSeed(0x53494E474C45UL),
            definitions,
            initialGoblinCount: 4,
            initialFoodStock: 20,
            initialWoodStock: 8);
        var snapshot = engine.CreateSnapshot();
        var occupiedByStack = snapshot.ItemStacks
            .Where(stack => stack.Location.Kind == ItemLocationKind.Ground)
            .Select(stack => stack.Location.Position)
            .ToHashSet();
        var storagePositions = snapshot.StorageZones.Select(zone => zone.Position).ToHashSet();
        var workshop = Enumerable.Range(0, engine.Map.Height)
            .SelectMany(y => Enumerable.Range(0, engine.Map.Width)
                .Select(x => new GridPosition(x, y)))
            .Where(engine.World.CanBuildPrimitiveWorkshop)
            .Where(position => !occupiedByStack.Contains(position) &&
                !storagePositions.Contains(position) &&
                engine.Navigation.HasSurfacePath(engine.Map.GoblinSpawn, position))
            .OrderBy(position => Math.Abs(position.X - engine.Map.GoblinSpawn.X) +
                Math.Abs(position.Y - engine.Map.GoblinSpawn.Y))
            .First();
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var blockingStack = save["itemStacks"]!.AsArray()
            .First(item => item!["resource"]!.GetValue<int>() == (int)ResourceKind.Food)!
            .AsObject();
        var blockingStackId = new EntityId(blockingStack["id"]!.GetValue<ulong>());
        blockingStack["quantity"] = definitions.ActorCarryCapacity * 3;
        blockingStack["locationKind"] = (int)ItemLocationKind.Ground;
        blockingStack["x"] = workshop.X;
        blockingStack["y"] = workshop.Y;
        blockingStack["z"] = workshop.Z;
        blockingStack["ownerId"] = EntityId.None.Value;
        engine = SimulationEngine.Load(save.ToJsonString(), definitions);
        engine.QueueCommand(SimulationCommand.BuildPrimitiveWorkshop(
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            workshop));

        var observedClearance = false;
        for (var tick = 0; tick < 1_000 && !engine.World.HasPrimitiveWorkshop(workshop); tick++)
        {
            engine.AdvanceTicks(1);
            var collectors = engine.CreateSnapshot().Actors
                .Where(actor =>
                    actor.Job.Kind == ActorJobKind.ClearConstructionSite &&
                    actor.Job.Stage == ActorJobStage.Collecting &&
                    actor.Job.SourceStackId == blockingStackId)
                .ToArray();
            observedClearance |= collectors.Length == 1;
            Assert.InRange(collectors.Length, 0, 1);
        }

        Assert.True(observedClearance);
    }

    [Fact]
    public void UndergroundStorageBlueprintKeepsItsLevelAndUsesThreeDimensionalAccess()
    {
        var seed = new WorldSeed(0x554E444552UL);
        var map = SwampMapGenerator.Generate(seed, 64, 64);
        var caveFloor = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width)
                .Select(x => new GridPosition(x, y, -1)))
            .First(position => map.GetCaveCell(position).Kind == CaveCellKind.Floor);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 16,
            initialWoodStock: 8);
        engine.QueueCommand(SimulationCommand.BuildFoodStorage(
            new SimulationTick(1),
            sequence: 1,
            caveFloor));

        engine.AdvanceTicks(200);

        var site = Assert.Single(engine.CreateSnapshot().ConstructionSites);
        Assert.Equal(caveFloor, site.Anchor);
        Assert.All(site.Footprint, position => Assert.Equal(-1, position.Z));
        Assert.Equal(2, Assert.Single(site.Materials).MissingQuantity);
        Assert.Contains(engine.CreateSnapshot().Actors, actor =>
            actor.Job.Kind is ActorJobKind.SupplyConstruction or ActorJobKind.BuildConstruction);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());

    }

    [Fact]
    public void StoneWalkwayPreservesSelectedMaterialAcrossSaveLoad()
    {
        var seed = new WorldSeed(0x53544F4E42524944UL);
        var map = SwampMapGenerator.Generate(seed, 64, 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 8);
        var position = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width)
                .Select(x => new GridPosition(x, y, 0)))
            .First(cell => engine.World.CanBuildBasaltWalkway([cell]));

        engine.QueueCommand(SimulationCommand.BuildBasaltWalkway(
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            position,
            position,
            ResourceVariant.Granite));
        engine.AdvanceTicks(1);

        var site = Assert.Single(engine.CreateSnapshot().ConstructionSites);
        Assert.Equal(ResourceVariant.Granite, Assert.Single(site.Materials).Variant);

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        var restoredSite = Assert.Single(restored.CreateSnapshot().ConstructionSites);
        Assert.Equal(ResourceVariant.Granite, Assert.Single(restoredSite.Materials).Variant);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void WalkwayBlueprintPreservesItsNonSurfaceLevel(int level)
    {
        var seed = new WorldSeed(0x425249444745UL);
        var map = SwampMapGenerator.Generate(seed, 64, 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 8,
            initialWoodStock: 4);
        var crossing = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width)
                .Select(x => new GridPosition(x, y, level)))
            .Select(position => new
            {
                Start = position,
                End = position with { X = position.X + 1 },
            })
            .First(candidate =>
                map.IsColumnWithin(candidate.End) &&
                engine.World.CanBuildWalkway([candidate.Start, candidate.End]));

        engine.QueueCommand(SimulationCommand.BuildWalkway(
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            crossing.Start,
            crossing.End));
        engine.AdvanceTicks(1);

        var sites = engine.CreateSnapshot().ConstructionSites;
        Assert.Equal(2, sites.Count);
        Assert.All(sites, site => Assert.Equal(level, site.Anchor.Z));
        var savedSites = JsonNode.Parse(engine.Save())!["constructionSites"]!.AsArray();
        Assert.Equal(2, savedSites.Count);
        Assert.NotEqual(0UL, savedSites[0]!["orderId"]!.GetValue<ulong>());
        Assert.Equal(
            savedSites[0]!["orderId"]!.GetValue<ulong>(),
            savedSites[1]!["orderId"]!.GetValue<ulong>());
        Assert.Equal(0, savedSites[0]!["sequenceIndex"]!.GetValue<int>());
        Assert.Equal(1, savedSites[1]!["sequenceIndex"]!.GetValue<int>());
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void CompletedElevatedWalkwayAndItsWorldChangeSurviveSaveLoad()
    {
        var seed = new WorldSeed(0x454C455641544544UL);
        var map = SwampMapGenerator.Generate(seed, 64, 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 8,
            initialWoodStock: 4);
        var elevated = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width)
                .Select(x => new GridPosition(x, y, 1)))
            .First(position => engine.World.CanBuildWalkway([position]));
        var change = engine.World.BuildWalkway([elevated], engine.CurrentTick);
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        save["undeliveredWorldChanges"]!.AsArray().Add(new JsonObject
        {
            ["version"] = change.Version,
            ["tick"] = change.Tick.Value,
            ["kind"] = (int)change.Kind,
            ["x"] = change.Position.X,
            ["y"] = change.Position.Y,
            ["z"] = change.Position.Z,
            ["amount"] = change.Amount,
        });

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);

        Assert.True(restored.World.IsTerrainTraversable(elevated));
        Assert.Contains(restored.World.GetWorldObjectsAt(elevated), item =>
            item.Kind == WorldObjectKind.WoodenWalkway);
        var restoredChange = Assert.Single(restored.DrainWorldChanges());
        Assert.Equal(elevated, restoredChange.Position);
        Assert.Equal(WorldChangeKind.StructureBuilt, restoredChange.Kind);
    }

    [Fact]
    public void ElevatedHillMiningDesignationSurvivesSaveLoad()
    {
        var seed = new WorldSeed(0x48494C4C4D494E45UL);
        var map = SwampMapGenerator.Generate(seed, 64, 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 8,
            initialWoodStock: 4);
        var rock = Enumerable.Range(1, map.MaximumWorldLevel)
            .SelectMany(z => Enumerable.Range(0, map.Height)
                .SelectMany(y => Enumerable.Range(0, map.Width)
                    .Select(x => new GridPosition(x, y, z))))
            .First(map.IsHillMassPosition);
        engine.QueueCommand(SimulationCommand.DesignateRockMining(
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            rock,
            rock));
        engine.AdvanceTicks(1);
        Assert.Contains(engine.CreateSnapshot().WorkDesignations, designation =>
            designation.Kind == WorkDesignationKind.MineRock && designation.Target == rock);

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);

        Assert.Contains(restored.CreateSnapshot().WorkDesignations, designation =>
            designation.Kind == WorkDesignationKind.MineRock && designation.Target == rock);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void ActiveConstructionDeliveryAndWorkSurviveSaveLoadDeterministically()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0x53495445UL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 20,
            initialWoodStock: 5);
        engine.QueueCommand(SimulationCommand.BuildFoodStorage(
            new SimulationTick(1),
            sequence: 1,
            engine.Map.GoblinSpawn));

        for (var tick = 0; tick < 500 &&
             engine.CreateSnapshot().Actors.Single().Job.Kind != ActorJobKind.BuildConstruction; tick++)
        {
            engine.AdvanceTicks(1);
        }

        Assert.Equal(
            ActorJobKind.BuildConstruction,
            engine.CreateSnapshot().Actors.Single().Job.Kind);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());

        engine.AdvanceTicks(200);
        restored.AdvanceTicks(200);

        Assert.Empty(engine.CreateSnapshot().ConstructionSites);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void WalkwayMakesWaterTraversableAndSurvivesSaveLoad()
    {
        var seed = new WorldSeed(994);
        var map = SwampMapGenerator.Generate(seed, 64, 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialWoodStock: 10);
        var crossing = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width).Select(x => new GridPosition(x, y)))
            .Where(position => !map.GetCell(position).IsTraversable &&
                map.GetCardinalNeighbors(position).Any(neighbor => map.GetCell(neighbor).IsTraversable))
            .SelectMany(water => map.GetCardinalNeighbors(water)
                .Where(neighbor => map.GetCell(neighbor).IsTraversable)
                .Select(land => new { Land = land, Water = water }))
            .Where(candidate => engine.World.CanBuildWalkway([candidate.Land, candidate.Water]))
            .OrderBy(candidate => Math.Abs(candidate.Water.X - map.GoblinSpawn.X) +
                Math.Abs(candidate.Water.Y - map.GoblinSpawn.Y))
            .First();
        var water = crossing.Water;
        var land = crossing.Land;
        var cells = SimulationCommand.GetWalkwayCells(land, water);
        var topologyVersion = engine.World.TopologyVersion;
        Assert.NotNull(engine.Navigation.FindSurfacePath(map.GoblinSpawn, land));
        Assert.NotNull(engine.Navigation.FindSurfacePath(map.GoblinSpawn, land));
        var cachedMetrics = engine.Navigation.GetMetrics();
        Assert.Equal(2, cachedMetrics.Requests);
        Assert.Equal(1, cachedMetrics.Searches);
        Assert.Equal(1, cachedMetrics.CacheHits);
        engine.QueueCommand(SimulationCommand.BuildWalkway(
            new SimulationTick(1),
            sequence: 1,
            land,
            water));

        engine.AdvanceTicks(1);

        Assert.Equal(cells.Count, engine.CreateSnapshot().ConstructionSites.Count);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);

        Assert.True(engine.World.IsSurfaceTraversable(water));
        Assert.Equal(topologyVersion + (ulong)cells.Count, engine.World.TopologyVersion);
        var invalidatedMetrics = engine.Navigation.GetMetrics();
        Assert.True(invalidatedMetrics.CacheInvalidations > cachedMetrics.CacheInvalidations);
        Assert.Equal(engine.World.TopologyVersion, invalidatedMetrics.TopologyVersion);
        Assert.All(cells, cell => Assert.Contains(
            engine.World.GetWorldObjectsAt(cell),
            item => item.Kind == WorldObjectKind.WoodenWalkway && item.Parts.Count == 1));
        Assert.Equal(10 - cells.Count, engine.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Wood)
            .Sum(stack => stack.Quantity));

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.True(restored.World.IsSurfaceTraversable(water));
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Theory]
    [InlineData(SimulationSpeed.Double)]
    [InlineData(SimulationSpeed.Quadruple)]
    [InlineData(SimulationSpeed.Octuple)]
    [InlineData(SimulationSpeed.Unthrottled)]
    public void ScheduledScenarioHasSameResultAtEverySpeed(SimulationSpeed speed)
    {
        var normal = RunScenario(SimulationSpeed.Normal);
        var accelerated = RunScenario(speed);

        Assert.Equal(normal.Snapshot.StateHash, accelerated.Snapshot.StateHash);
        Assert.Equal(normal.Events, accelerated.Events);
    }

    [Fact]
    public void RepeatedScenarioProducesSameHashAndEvents()
    {
        var first = RunScenario(SimulationSpeed.Unthrottled);
        var second = RunScenario(SimulationSpeed.Unthrottled);

        Assert.Equal(first.Snapshot.StateHash, second.Snapshot.StateHash);
        Assert.Equal(first.Events, second.Events);
    }

    [Fact]
    public void DroppingPresentationSnapshotsDoesNotChangeStateOrLoseEvents()
    {
        var withEverySnapshot = CreateScenario();
        var snapshotCount = 0;
        new SimulationRunner(withEverySnapshot).RunUntil(
            FinalTick,
            SimulationSpeed.Normal,
            snapshotConsumer: _ => snapshotCount++);

        var withoutSnapshots = CreateScenario();
        new SimulationRunner(withoutSnapshots).RunUntil(
            FinalTick,
            SimulationSpeed.Unthrottled,
            unthrottledTickBudget: 97);

        Assert.Equal(FinalTick.Value, snapshotCount);
        Assert.Equal(withEverySnapshot.ComputeStateHash(), withoutSnapshots.ComputeStateHash());
        Assert.Equal(withEverySnapshot.DrainEvents(), withoutSnapshots.DrainEvents());
    }

    [Fact]
    public void SaveLoadPreservesFutureOutcomeAndUndeliveredEvents()
    {
        var uninterrupted = CreateScenario();
        var runner = new SimulationRunner(uninterrupted);
        runner.RunUntil(new SimulationTick(173), SimulationSpeed.Octuple);

        var savedHash = uninterrupted.ComputeStateHash();
        var restored = SimulationEngine.Load(uninterrupted.Save(), SimulationDefinitions.Foundation);

        Assert.Equal(savedHash, restored.ComputeStateHash());

        runner.RunUntil(FinalTick, SimulationSpeed.Normal);
        new SimulationRunner(restored).RunUntil(
            FinalTick,
            SimulationSpeed.Unthrottled,
            unthrottledTickBudget: 61);

        Assert.Equal(uninterrupted.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(uninterrupted.DrainEvents(), restored.DrainEvents());
    }

    [Fact]
    public void LoadedSessionExposesSequenceAfterEveryPendingCommand()
    {
        var engine = CreateScenario();
        engine.QueueCommand(SimulationCommand.Move(
            new SimulationTick(50),
            sequence: 9_001,
            new EntityId(1),
            engine.Map.GoblinSpawn));

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);

        Assert.Equal(9_002UL, restored.NextAvailableCommandSequence);
    }

    [Fact]
    public void SavePinsMapGeneratorVersionAndRejectsUnsupportedVersion()
    {
        var engine = CreateScenario();
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");

        Assert.Equal(
            SwampMapGenerator.CurrentVersion,
            save["mapGeneratorVersion"]?.GetValue<int>());

        foreach (var incompatibleVersion in new[]
                 {
                     SwampMapGenerator.MinimumSaveCompatibleVersion - 1,
                     SwampMapGenerator.CurrentVersion + 1,
                 })
        {
            save["mapGeneratorVersion"] = incompatibleVersion;
            var exception = Assert.Throws<InvalidDataException>(() =>
                SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation));
            Assert.Contains(
                "map generator version",
                exception.Message,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void PreviousCompatibleMapGeneratorSaveStillLoads()
    {
        var seed = new WorldSeed(0x56313453415645UL);
        var map = SwampMapGenerator.Generate(
            seed,
            width: 48,
            height: 48,
            generatorVersion: SwampMapGenerator.MinimumSaveCompatibleVersion);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 3,
            initialFoodStock: 30);

        var restored = SimulationEngine.Load(engine.Save(), engine.Definitions);

        Assert.Equal(SwampMapGenerator.MinimumSaveCompatibleVersion, restored.Map.GeneratorVersion);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void CommandsOnSameTickExecuteInSequenceOrder()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(123),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 2,
            initialFoodStock: 0);

        engine.QueueCommand(SimulationCommand.Forage(
            new SimulationTick(1),
            sequence: 20,
            new EntityId(2)));
        engine.QueueCommand(SimulationCommand.Forage(
            new SimulationTick(1),
            sequence: 10,
            new EntityId(1)));

        engine.AdvanceTicks(1);
        var gatheredEvents = engine.DrainEvents()
            .Where(simulationEvent => simulationEvent.Kind == SimulationEventKind.FoodGathered)
            .ToArray();

        Assert.Equal(new EntityId(1), gatheredEvents[0].Subject);
        Assert.Equal(new EntityId(2), gatheredEvents[1].Subject);
    }

    [Fact]
    public void CommandsMustTargetFutureTicksAndExistingActors()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(123),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            engine.QueueCommand(SimulationCommand.Forage(
                SimulationTick.Zero,
                sequence: 1,
                new EntityId(1))));

        Assert.Throws<ArgumentException>(() =>
            engine.QueueCommand(SimulationCommand.Forage(
                new SimulationTick(1),
                sequence: 2,
                new EntityId(999))));
    }

    [Fact]
    public void RandomSamplesAreStableAndDomainSeparated()
    {
        var seed = new WorldSeed(123);
        var actor = new EntityId(7);
        var tick = new SimulationTick(11);

        var first = DeterministicRandom.Sample(seed, RandomDomain.Foraging, actor, tick, sampleKey: 2);
        var repeated = DeterministicRandom.Sample(seed, RandomDomain.Foraging, actor, tick, sampleKey: 2);
        var combat = DeterministicRandom.Sample(seed, RandomDomain.Combat, actor, tick, sampleKey: 2);

        Assert.Equal(first, repeated);
        Assert.NotEqual(first, combat);
    }

    private static ScenarioResult RunScenario(SimulationSpeed speed)
    {
        var engine = CreateScenario();
        new SimulationRunner(engine).RunUntil(
            FinalTick,
            speed,
            unthrottledTickBudget: 73);

        return new ScenarioResult(engine.CreateSnapshot(), engine.DrainEvents().ToArray());
    }

    private static SimulationEngine CreateScenario()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0x474F424C494EUL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 4,
            initialFoodStock: 8);

        ulong sequence = 1;
        for (var tick = 20; tick <= FinalTick.Value; tick += 20)
        {
            for (ulong actor = 1; actor <= 4; actor++)
            {
                engine.QueueCommand(SimulationCommand.Forage(
                    new SimulationTick(tick + (long)actor),
                    sequence++,
                    new EntityId(actor)));
            }
        }

        return engine;
    }

    private static IReadOnlyList<GridPosition> FindBuildableWallLine(
        SimulationEngine engine,
        int segmentCount)
    {
        var candidates =
            from y in Enumerable.Range(0, engine.Map.Height)
            from x in Enumerable.Range(0, engine.Map.Width - segmentCount + 1)
            let cells = Enumerable.Range(0, segmentCount)
                .Select(offset => new GridPosition(x + offset, y))
                .ToArray()
            where !cells.Contains(engine.Map.GoblinSpawn) &&
                engine.World.CanBuildWoodenWalls(cells)
            orderby Math.Abs(x - engine.Map.GoblinSpawn.X) +
                Math.Abs(y - engine.Map.GoblinSpawn.Y)
            select cells;
        return candidates.First();
    }

    private sealed record ScenarioResult(
        SimulationSnapshot Snapshot,
        SimulationEvent[] Events);
}
