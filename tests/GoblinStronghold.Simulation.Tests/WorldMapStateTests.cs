using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Map;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class WorldMapStateTests
{
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
    public void FreshSandboxEcologySupportsEmergencyForagingForThreeDays()
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

        engine.AdvanceTicks(3 * SimulationDefinitions.Foundation.TicksPerDay);

        var snapshot = engine.CreateSnapshot();
        Assert.Equal(8, snapshot.Actors.Count);
        Assert.All(snapshot.Actors, actor => Assert.True(actor.Health > 0));
    }

    [Fact]
    public void ForagingDepletesLocalVegetationAndPublishesDirtyCell()
    {
        var engine = CreateEngine();
        var position = engine.Map.GoblinSpawn;
        var before = engine.World.GetPlantPatch(position)!.Value;

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
        var engine = CreateEngine();
        var position = engine.Map.GoblinSpawn;

        engine.QueueCommand(SimulationCommand.Forage(
            new SimulationTick(1),
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
        Assert.Equal(new SimulationTick(240), change.Tick);
        Assert.Equal(1, change.Amount);
    }

    [Fact]
    public void HarvestedBerryBushRemainsInWorldWhileItsFruitRegrows()
    {
        var engine = CreateEngine();
        var position = engine.Map.GoblinSpawn;
        var capacity = engine.World.GetPlantPatch(position)!.Value.Capacity;
        for (var sequence = 1; sequence <= capacity; sequence++)
        {
            engine.QueueCommand(SimulationCommand.Forage(
                new SimulationTick(1),
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
