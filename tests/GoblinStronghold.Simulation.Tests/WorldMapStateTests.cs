using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using System.Text.Json.Nodes;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class WorldMapStateTests
{
    [Fact]
    public void DeepRiverVolumeOverridesLegacyCaveRockAfterSaveLoad()
    {
        var seed = new WorldSeed(456);
        var map = SwampMapGenerator.Generate(seed, 64, 64, generatorVersion: 10);
        var water = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width)
                .Select(x => new GridPosition(x, y, -1)))
            .First(position =>
                map.TryGetInitialGeometry(position, out var geometry) &&
                geometry.Fluid == CellFluidKind.Water);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 0);

        Assert.True(engine.World.TryGetFluid(water, out var fluid, out var depthLevels));
        Assert.Equal(CellFluidKind.Water, fluid);
        Assert.True(depthLevels > 0);
        Assert.False(engine.World.IsSolidCaveRock(water));
        Assert.False(engine.World.CanExcavateRock(water));
        Assert.False(engine.World.IsTerrainReachable(water));
        Assert.False(engine.World.IsTerrainTraversable(water));

        Assert.Equal(CaveCellKind.SolidRock, map.GetCaveCell(water).Kind);
        var oldSave = JsonNode.Parse(engine.Save())!.AsObject();
        oldSave["excavatedCaveCells"]!.AsArray().Add(new JsonObject
        {
            ["x"] = water.X,
            ["y"] = water.Y,
            ["z"] = water.Z,
        });
        var restored = SimulationEngine.Load(
            oldSave.ToJsonString(),
            SimulationDefinitions.Foundation);
        Assert.True(restored.World.TryGetFluid(water, out fluid, out depthLevels));
        Assert.Equal(CellFluidKind.Water, fluid);
        Assert.True(depthLevels > 0);
        Assert.Contains(water, restored.World.ExcavatedCaveCells);
        Assert.False(restored.World.IsSolidCaveRock(water));
        Assert.False(restored.World.IsTerrainTraversable(water));
        var roundTripped = SimulationEngine.Load(
            restored.Save(),
            SimulationDefinitions.Foundation);
        Assert.Equal(restored.ComputeStateHash(), roundTripped.ComputeStateHash());
    }

    [Fact]
    public void InitialEcologyContainsDistinctDeterministicFoodSources()
    {
        var seed = new WorldSeed(0x474F424C494EUL);
        var first = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            SwampMapGenerator.Generate(seed, 64, 64),
            initialGoblinCount: 1,
            initialFoodStock: 0);
        var second = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            SwampMapGenerator.Generate(seed, 64, 64),
            initialGoblinCount: 1,
            initialFoodStock: 0);

        var firstSources = first.World.CreatePlantSnapshot();
        Assert.Equal(firstSources, second.World.CreatePlantSnapshot());
        Assert.Contains(firstSources, source => source.Kind == PlantKind.BerryBush);
        Assert.Contains(firstSources, source => source.Kind == PlantKind.MushroomCluster);
        Assert.Contains(firstSources, source => source.Kind == PlantKind.EdibleRoots);
        Assert.Contains(firstSources, source => source.Kind == PlantKind.FishShoal);
        Assert.Contains(firstSources, source => source.Kind == PlantKind.ReedBed);
    }

    [Fact]
    public void RegionalMapPlacesForestNearVillageAndDeadwoodInSwamp()
    {
        var seed = new WorldSeed(0x464F52455354UL);
        var map = SwampMapGenerator.Generate(seed, 64, 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 0);

        var objects = engine.World.CreateWorldObjectSnapshot();
        var trees = objects.Where(item => item.Kind == WorldObjectKind.Tree).ToArray();
        var stumps = objects.Where(item => item.Kind == WorldObjectKind.DeadTreeStump).ToArray();

        Assert.NotEmpty(trees);
        Assert.Contains(trees, tree => tree.Parts.Count(part =>
            part.Kind == WorldObjectPartKind.TreeTrunk) > 1);
        Assert.All(trees, tree =>
        {
            Assert.Equal(WorldObjectOwner.Nature, tree.Owner);
            var trunkParts = tree.Parts
                .Where(part => part.Kind == WorldObjectPartKind.TreeTrunk)
                .OrderBy(part => part.RelativePosition.Z)
                .ToArray();
            Assert.InRange(trunkParts.Length, 1, 3);
            Assert.Equal(Enumerable.Range(0, trunkParts.Length),
                trunkParts.Select(part => part.RelativePosition.Z));
            Assert.Equal(9, tree.Parts.Count(part => part.Kind == WorldObjectPartKind.TreeCrown));
            Assert.All(tree.Parts.Where(part => part.Kind == WorldObjectPartKind.TreeCrown),
                part => Assert.Equal(trunkParts.Length, part.RelativePosition.Z));
            Assert.True(tree.Anchor.X >= map.Width * 0.42);
            Assert.True(tree.Anchor.Y <= map.Height * 0.62);
        });
        Assert.NotEmpty(stumps);
        Assert.All(stumps, stump =>
        {
            Assert.Equal(TerrainKind.Mud, map.GetCell(stump.Anchor).Terrain);
            Assert.True(stump.Anchor.X <= map.Width * 0.42 || stump.Anchor.Y >= map.Height * 0.64);
        });
    }

    [Fact]
    public void FreshRegionalMapContainsDeterministicLooseStoneAndBlockingBoulders()
    {
        var seed = new WorldSeed(0x53544F4EUL);
        var map = SwampMapGenerator.Generate(seed, 64, 64);
        var first = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 2,
            initialFoodStock: 0,
            scatterInitialBrushwood: true);
        var second = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            SwampMapGenerator.Generate(seed, 64, 64),
            initialGoblinCount: 2,
            initialFoodStock: 0,
            scatterInitialBrushwood: true);

        var boulders = first.World.CreateWorldObjectSnapshot()
            .Where(item => item.Kind == WorldObjectKind.Boulder)
            .ToArray();
        Assert.NotEmpty(boulders);
        Assert.Equal(
            boulders.Select(item => item.Anchor),
            second.World.CreateWorldObjectSnapshot()
                .Where(item => item.Kind == WorldObjectKind.Boulder)
                .Select(item => item.Anchor));
        Assert.All(boulders, boulder =>
        {
            Assert.Equal(WorldObjectOwner.Nature, boulder.Owner);
            Assert.False(first.World.IsSurfaceTraversable(boulder.Anchor));
            Assert.Contains(boulder.Parts, part => part.Kind == WorldObjectPartKind.Boulder);
        });

        var looseStone = first.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Stone &&
                stack.Location.Kind == ItemLocationKind.Ground)
            .ToArray();
        Assert.NotEmpty(looseStone);
        Assert.Contains(looseStone, stack =>
            Math.Abs(stack.Location.Position.X - map.GoblinSpawn.X) +
            Math.Abs(stack.Location.Position.Y - map.GoblinSpawn.Y) <=
            SimulationDefinitions.Foundation.VisionRadius);
        Assert.Equal(
            looseStone,
            second.CreateSnapshot().ItemStacks.Where(stack =>
                stack.Resource == ResourceKind.Stone &&
                stack.Location.Kind == ItemLocationKind.Ground));
    }

    [Fact]
    public void FishShoalsOnlyOccupyShallowsInLargerConnectedWaterBodies()
    {
        var seed = new WorldSeed(0x474F424C494EUL);
        var map = SwampMapGenerator.Generate(seed, 64, 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 0);

        var shoals = engine.World.CreatePlantSnapshot()
            .Where(source => source.Kind == PlantKind.FishShoal)
            .ToArray();
        Assert.NotEmpty(shoals);
        foreach (var shoal in shoals)
        {
            Assert.Equal(TerrainKind.ShallowWater, map.GetCell(shoal.Position).Terrain);
            Assert.True(MeasureWaterBody(map, shoal.Position) >= 12);
        }
    }

    [Fact]
    public void FreshSandboxEcologyContainsADeepEmergencyForagingReserve()
    {
        var seed = new WorldSeed(0x474F424C494EUL);
        var map = SwampMapGenerator.Generate(seed, 64, 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 8,
            initialFoodStock: 16,
            scatterInitialBrushwood: true);

        var snapshot = engine.CreateSnapshot();
        Assert.Equal(8, snapshot.Actors.Count);
        var looseFood = snapshot.ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Food)
            .Sum(stack => stack.Quantity);
        var wildFood = snapshot.PlantPatches.Sum(patch => patch.Biomass);
        Assert.True(looseFood + wildFood >= snapshot.Actors.Count * 6);
    }

    [Fact]
    public void ForagingDepletesLocalVegetationAndPublishesDirtyCell()
    {
        var engine = CreateEngine();
        var position = engine.Map.GoblinSpawn;
        var before = engine.World.GetPlantPatch(position)!.Value;
        var topologyVersion = engine.World.TopologyVersion;

        engine.QueueCommand(SimulationCommand.Forage(
            new SimulationTick(1),
            sequence: 1,
            new EntityId(1)));
        engine.AdvanceTicks(1);

        var after = engine.World.GetPlantPatch(position)!.Value;
        var change = Assert.Single(engine.DrainWorldChanges());
        var gathered = Assert.Single(
            engine.DrainEvents().Where(item => item.Kind == SimulationEventKind.FoodGathered));

        Assert.True(after.Biomass < before.Biomass);
        Assert.Equal(before.Biomass - after.Biomass, gathered.Amount);
        Assert.Equal(WorldChangeKind.VegetationHarvested, change.Kind);
        Assert.Equal(position, change.Position);
        Assert.Equal(-gathered.Amount, change.Amount);
        Assert.Equal(engine.World.Version, change.Version);
        Assert.Equal(topologyVersion, engine.World.TopologyVersion);
    }

    [Fact]
    public void DepletedVegetationRejectsFurtherForagingUntilItRegrows()
    {
        var engine = CreateEngine();
        var position = engine.Map.GoblinSpawn;
        var capacity = engine.World.GetPlantPatch(position)!.Value.Capacity;

        for (var tick = 1; tick <= capacity; tick++)
        {
            engine.QueueCommand(SimulationCommand.Forage(
                new SimulationTick(1),
                sequence: (ulong)tick,
                new EntityId(1)));
        }

        engine.AdvanceTicks(1);
        Assert.Equal(0, engine.World.GetPlantPatch(position)!.Value.Biomass);

        var rejectionTick = 2;
        engine.QueueCommand(SimulationCommand.Forage(
            new SimulationTick(rejectionTick),
            sequence: (ulong)rejectionTick,
            new EntityId(1)));
        engine.AdvanceTicks(1);

        Assert.Contains(
            engine.DrainEvents(),
            item => item.Kind == SimulationEventKind.CommandRejected);
    }

    [Fact]
    public void VegetationRegrowsAtStableLogicalIntervals()
    {
        var engine = MoveToStartOfSummer(CreateEngine());
        var position = engine.Map.GoblinSpawn;

        engine.QueueCommand(SimulationCommand.Forage(
            engine.CurrentTick.Next(),
            sequence: 1,
            new EntityId(1)));
        engine.AdvanceTicks(1);
        var harvested = engine.World.GetPlantPatch(position)!.Value;
        engine.DrainEvents();
        engine.DrainWorldChanges();

        engine.AdvanceTicks(
            SimulationDefinitions.Foundation.PlantGrowthIntervalTicks - 1);

        var change = Assert.Single(
            engine.DrainWorldChanges(),
            item => item.Kind == WorldChangeKind.VegetationRegrown && item.Position == position);
        Assert.Equal(WorldChangeKind.VegetationRegrown, change.Kind);
        Assert.Equal(
            new SimulationTick(engine.Definitions.Clock.Climate.GetSeasonStartTick(SeasonKind.Summer) +
                engine.Definitions.PlantGrowthIntervalTicks),
            change.Tick);
        Assert.Equal(1, change.Amount);
    }

    [Fact]
    public void HarvestedBerryBushRemainsInWorldWhileItsFruitRegrows()
    {
        var engine = MoveToStartOfSummer(CreateEngine());
        var position = engine.Map.GoblinSpawn;
        var capacity = engine.World.GetPlantPatch(position)!.Value.Capacity;
        for (var sequence = 1; sequence <= capacity; sequence++)
        {
            engine.QueueCommand(SimulationCommand.Forage(
                engine.CurrentTick.Next(),
                (ulong)sequence,
                new EntityId(1)));
        }

        engine.AdvanceTicks(1);

        var bareBush = engine.World.GetPlantPatch(position);
        Assert.NotNull(bareBush);
        Assert.Equal(PlantKind.BerryBush, bareBush.Value.Kind);
        Assert.Equal(0, bareBush.Value.Biomass);

        engine.AdvanceTicks(SimulationDefinitions.Foundation.PlantGrowthIntervalTicks - 1);

        Assert.Equal(1, engine.World.GetPlantPatch(position)!.Value.Biomass);
    }

    [Fact]
    public void SaveLoadPreservesVegetationAndUndeliveredWorldChanges()
    {
        var engine = CreateEngine();
        engine.QueueCommand(SimulationCommand.Forage(
            new SimulationTick(1),
            sequence: 1,
            new EntityId(1)));
        engine.AdvanceTicks(1);

        var savedHash = engine.ComputeStateHash();
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);

        Assert.Equal(savedHash, restored.ComputeStateHash());
        Assert.Equal(engine.CreateSnapshot().PlantPatches, restored.CreateSnapshot().PlantPatches);
        Assert.Equal(engine.DrainWorldChanges(), restored.DrainWorldChanges());

        engine.AdvanceTicks(239);
        restored.AdvanceTicks(239);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void DrainingWorldChangesDoesNotChangeAuthoritativeHash()
    {
        var engine = CreateEngine();
        engine.QueueCommand(SimulationCommand.Forage(
            new SimulationTick(1),
            sequence: 1,
            new EntityId(1)));
        engine.AdvanceTicks(1);

        var beforeDrain = engine.ComputeStateHash();
        Assert.NotEmpty(engine.DrainWorldChanges());
        Assert.Equal(beforeDrain, engine.ComputeStateHash());
    }

    private static SimulationEngine CreateEngine() => SimulationEngine.Create(
        new WorldSeed(0x4C4956494E47UL),
        SimulationDefinitions.Foundation,
        initialGoblinCount: 1,
        initialFoodStock: 0);

    private static SimulationEngine MoveToStartOfSummer(SimulationEngine engine)
    {
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        save["currentTick"] = engine.Definitions.Clock.Climate.GetSeasonStartTick(SeasonKind.Summer);
        return SimulationEngine.Load(save.ToJsonString(), engine.Definitions);
    }

    private static int MeasureWaterBody(GeneratedMap map, GridPosition start)
    {
        var visited = new HashSet<GridPosition> { start };
        var queue = new Queue<GridPosition>();
        queue.Enqueue(start);
        while (queue.TryDequeue(out var current))
        {
            foreach (var neighbor in map.GetCardinalNeighbors(current))
            {
                if (visited.Contains(neighbor) ||
                    map.GetCell(neighbor).Terrain is not (TerrainKind.ShallowWater or TerrainKind.DeepWater))
                {
                    continue;
                }

                visited.Add(neighbor);
                queue.Enqueue(neighbor);
            }
        }

        return visited.Count;
    }
}
