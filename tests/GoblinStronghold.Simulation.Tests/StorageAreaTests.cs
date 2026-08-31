using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class StorageAreaTests
{
    [Fact]
    public void StandaloneStorageCreatesSingletonDefaultArea()
    {
        var engine = CreateEngine();
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1),
            sequence: 1,
            engine.Map.GoblinSpawn,
            ResourceKind.Wood,
            capacity: 12));

        engine.AdvanceTicks(1);

        var snapshot = engine.CreateSnapshot();
        var zone = Assert.Single(snapshot.StorageZones);
        var area = Assert.Single(snapshot.StorageAreas);
        Assert.Equal(zone.Id, area.Id);
        Assert.Equal(zone.StorageAreaId, area.Id);
        Assert.Equal([zone.Position], area.Footprint);
        Assert.Equal([zone.Id], area.StorageZoneIds);
        Assert.Equal(zone.Capacity, area.Capacity);
        Assert.Equal(EntityId.None, area.LogisticsNetworkId);
    }

    [Fact]
    public void ProviderPlacedInsideAreaInheritsNetworkAndSurvivesSaveLoad()
    {
        var engine = CreateEngine();
        engine.QueueCommand(SimulationCommand.CreateLogisticsNetwork(
            new SimulationTick(1), sequence: 1));
        engine.AdvanceTicks(1);
        var network = engine.CreateSnapshot().LogisticsNetworks.Single(item => !item.IsDefault);
        var (start, end) = FindTraversableSquare(engine);

        engine.QueueCommand(SimulationCommand.CreateStorageArea(
            engine.CurrentTick.Next(), sequence: 2, start, end, network.Id));
        engine.AdvanceTicks(1);
        var emptyArea = Assert.Single(engine.CreateSnapshot().StorageAreas);
        Assert.Empty(emptyArea.StorageZoneIds);
        Assert.Equal(0, emptyArea.Capacity);

        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            engine.CurrentTick.Next(),
            sequence: 3,
            emptyArea.Footprint[0],
            ResourceKind.Stone,
            capacity: 20));
        engine.AdvanceTicks(1);

        var snapshot = engine.CreateSnapshot();
        var area = Assert.Single(snapshot.StorageAreas);
        var zone = Assert.Single(snapshot.StorageZones);
        Assert.Equal(area.Id, zone.StorageAreaId);
        Assert.Equal(network.Id, area.LogisticsNetworkId);
        Assert.Equal(network.Id, zone.LogisticsNetworkId);
        Assert.Equal([zone.Id], area.StorageZoneIds);
        Assert.Equal(20, area.Capacity);

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        var restoredArea = Assert.Single(restored.CreateSnapshot().StorageAreas);
        Assert.Equal(area.Footprint, restoredArea.Footprint);
        Assert.Equal(area.StorageZoneIds, restoredArea.StorageZoneIds);
        Assert.Equal(area.LogisticsNetworkId, restoredArea.LogisticsNetworkId);
    }

    [Fact]
    public void StorageAreasCannotOverlap()
    {
        var engine = CreateEngine();
        var (start, end) = FindTraversableSquare(engine);
        engine.QueueCommand(SimulationCommand.CreateStorageArea(
            new SimulationTick(1), sequence: 1, start, end));
        engine.AdvanceTicks(1);
        engine.DrainEvents();

        engine.QueueCommand(SimulationCommand.CreateStorageArea(
            engine.CurrentTick.Next(), sequence: 2, start, start));
        engine.AdvanceTicks(1);

        Assert.Single(engine.CreateSnapshot().StorageAreas);
        Assert.Contains(engine.DrainEvents(), item =>
            item.Kind == SimulationEventKind.CommandRejected &&
            item.Amount == (int)SimulationCommandKind.CreateStorageArea);
    }

    private static SimulationEngine CreateEngine()
    {
        var seed = new WorldSeed(0x53544F5241474541UL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        return SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialWoodStock: 0);
    }

    private static (GridPosition Start, GridPosition End) FindTraversableSquare(
        SimulationEngine engine)
    {
        var z = engine.Map.GoblinSpawn.Z;
        for (var y = 0; y < engine.Map.Height - 1; y++)
        {
            for (var x = 0; x < engine.Map.Width - 1; x++)
            {
                var cells = new[]
                {
                    new GridPosition(x, y, z),
                    new GridPosition(x + 1, y, z),
                    new GridPosition(x, y + 1, z),
                    new GridPosition(x + 1, y + 1, z),
                };
                if (cells.All(engine.World.IsTerrainTraversable))
                {
                    return (cells[0], cells[^1]);
                }
            }
        }

        throw new InvalidOperationException("Generated map has no traversable 2x2 storage area.");
    }
}
