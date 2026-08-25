using System.Text.Json.Nodes;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class SimulationEngineTests
{
    private static readonly SimulationTick FinalTick = new(480);

    [Fact]
    public void DefaultActiveMapUsesTheExpandedSixtyFourCellRegion()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0x4C41524745UL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0);

        Assert.Equal(64, engine.Map.Width);
        Assert.Equal(64, engine.Map.Height);
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
        });

        var restored = SimulationEngine.Load(first.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(first.CreateSnapshot().Actors, restored.CreateSnapshot().Actors);
        Assert.Equal(first.ComputeStateHash(), restored.ComputeStateHash());
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
            Assert.True(first.Map.GetCell(stack.Location.Position).Terrain is
                TerrainKind.SolidGround or TerrainKind.Mud);
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
        Assert.Equal(3, engine.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Wood)
            .Sum(stack => stack.Quantity));
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
    public void UndergroundStorageBlueprintKeepsItsLevelAndWaitsForThreeDimensionalAccess()
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
        Assert.DoesNotContain(engine.CreateSnapshot().Actors, actor =>
            actor.Job.Kind is ActorJobKind.SupplyConstruction or ActorJobKind.BuildConstruction);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());

        Assert.Throws<ArgumentException>(() => engine.QueueCommand(
            SimulationCommand.BuildWalkway(
                engine.CurrentTick.Next(),
                sequence: 2,
                caveFloor,
                caveFloor with { X = caveFloor.X + 1 })));
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

        Assert.Single(engine.CreateSnapshot().ConstructionSites);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);

        Assert.True(engine.World.IsSurfaceTraversable(water));
        Assert.Equal(topologyVersion + 1, engine.World.TopologyVersion);
        var invalidatedMetrics = engine.Navigation.GetMetrics();
        Assert.True(invalidatedMetrics.CacheInvalidations > cachedMetrics.CacheInvalidations);
        Assert.Equal(engine.World.TopologyVersion, invalidatedMetrics.TopologyVersion);
        Assert.Contains(engine.CreateSnapshot().WorldObjects, item =>
            item.Kind == WorldObjectKind.WoodenWalkway && item.Parts.Count == cells.Count);
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

        save["mapGeneratorVersion"] = SwampMapGenerator.CurrentVersion + 1;

        var exception = Assert.Throws<InvalidDataException>(() =>
            SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation));
        Assert.Contains("map generator version", exception.Message, StringComparison.OrdinalIgnoreCase);
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

    private sealed record ScenarioResult(
        SimulationSnapshot Snapshot,
        SimulationEvent[] Events);
}
