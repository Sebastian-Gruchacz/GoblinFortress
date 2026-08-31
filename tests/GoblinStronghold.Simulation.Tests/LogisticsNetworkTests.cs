using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class LogisticsNetworkTests
{
    [Fact]
    public void NewStrongholdStartsWithDefaultNetworkForAllStorage()
    {
        var engine = CreateEngine(goblinCount: 2, initialWood: 4);
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1),
            sequence: 1,
            engine.Map.GoblinSpawn,
            ResourceKind.Wood,
            capacity: 4));

        engine.AdvanceTicks(1);

        var snapshot = engine.CreateSnapshot();
        var network = Assert.Single(snapshot.LogisticsNetworks);
        var storage = Assert.Single(snapshot.StorageZones);
        Assert.True(network.IsDefault);
        Assert.Equal("Default", network.Name);
        Assert.Empty(network.AssignedHaulerIds);
        Assert.Empty(network.SourceStorageZoneIds);
        Assert.Equal([storage.Id], network.DestinationStorageZoneIds);
        Assert.Equal(EntityId.None, storage.LogisticsNetworkId);
    }

    [Fact]
    public void SpecialistNetworkRoutesOnlyItsHaulerFromItsSourcesAndSurvivesSaveLoad()
    {
        var engine = CreateEngine(goblinCount: 2, initialWood: 10);
        var sourcePosition = engine.Map.GoblinSpawn;
        var destinationPosition = engine.Map.GetCardinalNeighbors(sourcePosition)
            .First(engine.World.IsSurfaceTraversable);
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1), sequence: 1, sourcePosition, ResourceKind.Wood, capacity: 10));
        engine.AdvanceTicks(80);
        var source = Assert.Single(engine.CreateSnapshot().StorageZones);
        Assert.Equal(10, source.StoredQuantity);

        engine.QueueCommand(SimulationCommand.CreateLogisticsNetwork(
            engine.CurrentTick.Next(), sequence: 2));
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            engine.CurrentTick.Next(),
            sequence: 3,
            destinationPosition,
            ResourceKind.Wood,
            capacity: 10));
        engine.AdvanceTicks(1);

        var snapshot = engine.CreateSnapshot();
        var network = snapshot.LogisticsNetworks.Single(item => !item.IsDefault);
        var destination = snapshot.StorageZones.Single(zone => zone.Id != source.Id);
        var specialist = snapshot.Actors.OrderBy(actor => actor.Id).Last();
        engine.QueueCommand(SimulationCommand.ConfigureStoragePull(
            engine.CurrentTick.Next(), sequence: 4, source.Id, desiredQuantity: 2));
        engine.QueueCommand(SimulationCommand.ConfigureLogisticsHauler(
            engine.CurrentTick.Next(), sequence: 5, network.Id, specialist.Id, assigned: true));
        engine.QueueCommand(SimulationCommand.ConfigureLogisticsSource(
            engine.CurrentTick.Next(), sequence: 6, network.Id, source.Id, included: true));
        engine.QueueCommand(SimulationCommand.ConfigureStorageNetwork(
            engine.CurrentTick.Next(), sequence: 7, destination.Id, network.Id));
        engine.DrainEvents();

        engine.AdvanceTicks(200);

        snapshot = engine.CreateSnapshot();
        network = snapshot.LogisticsNetworks.Single(item => item.Id == network.Id);
        source = snapshot.StorageZones.Single(zone => zone.Id == source.Id);
        destination = snapshot.StorageZones.Single(zone => zone.Id == destination.Id);
        Assert.Equal(2, source.StoredQuantity);
        Assert.Equal(8, destination.StoredQuantity);
        Assert.Equal(network.Id, destination.LogisticsNetworkId);
        Assert.Equal([specialist.Id], network.AssignedHaulerIds);
        Assert.Equal([source.Id], network.SourceStorageZoneIds);
        Assert.Equal([destination.Id], network.DestinationStorageZoneIds);
        Assert.All(
            engine.DrainEvents().Where(item =>
                item.Kind is SimulationEventKind.ItemPickedUp or SimulationEventKind.ItemStored),
            item => Assert.Equal(specialist.Id, item.Subject));

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        var restoredNetwork = restored.CreateSnapshot().LogisticsNetworks
            .Single(item => item.Id == network.Id);
        Assert.Equal(network.Name, restoredNetwork.Name);
        Assert.Equal(network.AssignedHaulerIds, restoredNetwork.AssignedHaulerIds);
        Assert.Equal(network.SourceStorageZoneIds, restoredNetwork.SourceStorageZoneIds);
        Assert.Equal(network.DestinationStorageZoneIds, restoredNetwork.DestinationStorageZoneIds);
    }

    private static SimulationEngine CreateEngine(int goblinCount, int initialWood)
    {
        var seed = new WorldSeed(0x4C4F474953544943UL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        return SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: goblinCount,
            initialFoodStock: 0,
            initialWoodStock: initialWood);
    }
}
