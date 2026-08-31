using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class LogisticsManagementTests
{
    [Fact]
    public void PendingRenameCommandsSurviveSaveAndRenameNetworkAndArea()
    {
        var engine = CreateEngine();
        var position = engine.Map.GoblinSpawn;
        engine.QueueCommand(SimulationCommand.CreateLogisticsNetwork(
            new SimulationTick(1), sequence: 1));
        engine.QueueCommand(SimulationCommand.CreateStorageArea(
            new SimulationTick(1), sequence: 2, position, position));
        engine.AdvanceTicks(1);
        var snapshot = engine.CreateSnapshot();
        var network = snapshot.LogisticsNetworks.Single(item => !item.IsDefault);
        var area = Assert.Single(snapshot.StorageAreas);
        engine.QueueCommand(SimulationCommand.RenameLogisticsNetwork(
            engine.CurrentTick.Next(), sequence: 3, network.Id, "Deep Mine"));
        engine.QueueCommand(SimulationCommand.RenameStorageArea(
            engine.CurrentTick.Next(), sequence: 4, area.Id, "Ore Intake"));

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        restored.AdvanceTicks(1);

        snapshot = restored.CreateSnapshot();
        Assert.Equal("Deep Mine", snapshot.LogisticsNetworks.Single(item => item.Id == network.Id).Name);
        Assert.Equal("Ore Intake", Assert.Single(snapshot.StorageAreas).Name);
    }

    [Fact]
    public void ContainerFilterChangesFutureInputWithoutDestroyingExistingContents()
    {
        var engine = CreateEngine();
        var position = engine.Map.GoblinSpawn;
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1), sequence: 1, position, ResourceKind.Any, capacity: 32));
        engine.AdvanceTicks(1);
        var zone = Assert.Single(engine.CreateSnapshot().StorageZones);
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var savedZone = save["storageZones"]!.AsArray()[0]!.AsObject();
        savedZone["providerKind"] = (int)StorageProviderKind.WoodenBox;
        savedZone["slotCount"] = 4;
        savedZone["stackCapacity"] = 8;
        savedZone["separatesItemTypes"] = true;
        savedZone["capabilities"] = (int)StorageCapability.SolidGoods;
        var nextId = save["nextEntityId"]!.GetValue<ulong>();
        save["itemStacks"]!.AsArray().Add(new JsonObject
        {
            ["id"] = nextId,
            ["resource"] = (int)ResourceKind.Wood,
            ["foodKind"] = (int)FoodKind.None,
            ["variant"] = (int)ResourceVariant.OakWood,
            ["quantity"] = 3,
            ["locationKind"] = (int)ItemLocationKind.StorageZone,
            ["x"] = position.X,
            ["y"] = position.Y,
            ["z"] = position.Z,
            ["ownerId"] = zone.Id.Value,
        });
        save["nextEntityId"] = nextId + 1;
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        engine.QueueCommand(SimulationCommand.ConfigureStorageFilter(
            engine.CurrentTick.Next(), sequence: 2, zone.Id, ResourceKind.Food));
        engine.AdvanceTicks(1);

        var snapshot = engine.CreateSnapshot();
        zone = Assert.Single(snapshot.StorageZones);
        Assert.Equal(ResourceKind.Food, zone.AcceptedResource);
        Assert.Equal(3, zone.StoredQuantity);
        Assert.Contains(snapshot.ItemStacks, stack =>
            stack.Location.OwnerId == zone.Id && stack.Resource == ResourceKind.Wood);

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(ResourceKind.Food, Assert.Single(restored.CreateSnapshot().StorageZones)
            .AcceptedResource);
    }

    [Fact]
    public void CompoundContainerFilterAcceptsSelectedCategoriesAndSurvivesSaveLoad()
    {
        var engine = CreateEngine();
        var position = engine.Map.GoblinSpawn;
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1), sequence: 1, position, ResourceKind.Any, capacity: 32));
        engine.AdvanceTicks(1);
        var zone = Assert.Single(engine.CreateSnapshot().StorageZones);
        engine = ConvertToWoodenBox(engine, zone.Id);
        engine = AddItemStack(
            engine,
            ResourceKind.Stone,
            FoodKind.None,
            ResourceVariant.Sandstone,
            quantity: 3,
            ItemLocationKind.StorageZone,
            position,
            zone.Id);
        engine.QueueCommand(SimulationCommand.ConfigureStorageFilter(
            engine.CurrentTick.Next(), sequence: 2, zone.Id, ResourceKind.Food));
        engine.QueueCommand(SimulationCommand.ConfigureStorageFilterResource(
            engine.CurrentTick.Next(), sequence: 3, zone.Id, ResourceKind.Wood, included: true));

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        restored.AdvanceTicks(1);

        zone = Assert.Single(restored.CreateSnapshot().StorageZones);
        Assert.Equal(
            StorageResourceFilter.Food | StorageResourceFilter.Wood,
            zone.ResourceFilter);
        Assert.Equal(ResourceKind.Any, zone.AcceptedResource);
        Assert.Equal(3, zone.StoredQuantity);
        restored = AddItemStack(
            restored,
            ResourceKind.Food,
            FoodKind.Berries,
            ResourceVariant.None,
            quantity: 1,
            ItemLocationKind.Ground,
            position,
            EntityId.None);
        restored = AddItemStack(
            restored,
            ResourceKind.Wood,
            FoodKind.None,
            ResourceVariant.OakWood,
            quantity: 1,
            ItemLocationKind.Ground,
            position,
            EntityId.None);
        restored = AddItemStack(
            restored,
            ResourceKind.Stone,
            FoodKind.None,
            ResourceVariant.Granite,
            quantity: 1,
            ItemLocationKind.Ground,
            position,
            EntityId.None);
        restored.ApplyCommandImmediately(SimulationCommand.ConfigureStoragePull(
            restored.CurrentTick, sequence: 4, zone.Id, desiredQuantity: 10));

        var diagnostic = restored.InspectStorageDelivery(zone.Id);
        Assert.Equal(StorageDeliveryState.WaitingForHauler, diagnostic.State);
        Assert.Equal(2, diagnostic.MatchingSourceCount);
    }

    [Fact]
    public void CompoundContainerFilterCannotDisableItsLastCategory()
    {
        var engine = CreateEngine();
        var position = engine.Map.GoblinSpawn;
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1), sequence: 1, position, ResourceKind.Any, capacity: 32));
        engine.AdvanceTicks(1);
        var zone = Assert.Single(engine.CreateSnapshot().StorageZones);
        engine = ConvertToWoodenBox(engine, zone.Id);
        engine.QueueCommand(SimulationCommand.ConfigureStorageFilter(
            engine.CurrentTick.Next(), sequence: 2, zone.Id, ResourceKind.Food));
        engine.AdvanceTicks(1);
        engine.DrainEvents();
        engine.QueueCommand(SimulationCommand.ConfigureStorageFilterResource(
            engine.CurrentTick.Next(), sequence: 3, zone.Id, ResourceKind.Food, included: false));
        engine.AdvanceTicks(1);

        Assert.Equal(
            StorageResourceFilter.Food,
            Assert.Single(engine.CreateSnapshot().StorageZones).ResourceFilter);
        Assert.Contains(engine.DrainEvents(), item =>
            item.Kind == SimulationEventKind.CommandRejected &&
            item.Amount == (int)SimulationCommandKind.ConfigureStorageFilterResource);
    }

    [Fact]
    public void DefaultNetworkCannotBeRenamed()
    {
        var engine = CreateEngine();
        Assert.Throws<ArgumentException>(() => engine.QueueCommand(
            SimulationCommand.RenameLogisticsNetwork(
                new SimulationTick(1), sequence: 1, EntityId.None, "Something else")));
    }

    [Fact]
    public void DeletingSpecialistNetworkReturnsStoresAndHaulersToDefaultWithoutLosingGoods()
    {
        var engine = CreateEngine();
        var position = engine.Map.GoblinSpawn;
        engine.QueueCommand(SimulationCommand.CreateLogisticsNetwork(
            new SimulationTick(1), sequence: 1));
        engine.QueueCommand(SimulationCommand.CreateStorageArea(
            new SimulationTick(1), sequence: 2, position, position));
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1), sequence: 3, position, ResourceKind.Any, capacity: 16));
        engine.AdvanceTicks(1);
        var snapshot = engine.CreateSnapshot();
        var network = snapshot.LogisticsNetworks.Single(item => !item.IsDefault);
        var area = Assert.Single(snapshot.StorageAreas);
        var zone = Assert.Single(snapshot.StorageZones);
        var actor = snapshot.Actors[0];
        engine.QueueCommand(SimulationCommand.ConfigureStorageAreaNetwork(
            engine.CurrentTick.Next(), sequence: 4, area.Id, network.Id));
        engine.QueueCommand(SimulationCommand.ConfigureLogisticsHauler(
            engine.CurrentTick.Next(), sequence: 5, network.Id, actor.Id, assigned: true));
        engine.QueueCommand(SimulationCommand.ConfigureLogisticsSource(
            engine.CurrentTick.Next(), sequence: 6, network.Id, zone.Id, included: true));
        engine.AdvanceTicks(1);
        engine = AddStoredWood(engine, zone.Id, position, quantity: 3);
        engine.QueueCommand(SimulationCommand.DeleteLogisticsNetwork(
            engine.CurrentTick.Next(), sequence: 7, network.Id));

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        restored.AdvanceTicks(1);

        snapshot = restored.CreateSnapshot();
        Assert.DoesNotContain(snapshot.LogisticsNetworks, item => item.Id == network.Id);
        Assert.Equal(EntityId.None, Assert.Single(snapshot.StorageAreas).LogisticsNetworkId);
        zone = Assert.Single(snapshot.StorageZones);
        Assert.Equal(EntityId.None, zone.LogisticsNetworkId);
        Assert.Equal(3, zone.StoredQuantity);
        Assert.Contains(snapshot.ItemStacks, stack =>
            stack.Location.OwnerId == zone.Id && stack.Resource == ResourceKind.Wood);
        Assert.DoesNotContain(snapshot.LogisticsNetworks, item =>
            !item.IsDefault && item.AssignedHaulerIds.Contains(actor.Id));
        Assert.Contains(restored.DrainEvents(), item =>
            item.Kind == SimulationEventKind.LogisticsNetworkDeleted && item.Target == network.Id);
    }

    [Fact]
    public void StorageAreaCanGrowAndSurvivesSaveLoad()
    {
        var engine = CreateEngine();
        var (start, end) = FindTraversableRectangle(engine, width: 3, height: 2);
        var initialEnd = end with { X = end.X - 1 };
        engine.QueueCommand(SimulationCommand.CreateStorageArea(
            new SimulationTick(1), sequence: 1, start, initialEnd));
        engine.AdvanceTicks(1);
        var area = Assert.Single(engine.CreateSnapshot().StorageAreas);
        engine.QueueCommand(SimulationCommand.ResizeStorageArea(
            engine.CurrentTick.Next(), sequence: 2, area.Id, start, end));
        engine.AdvanceTicks(1);

        area = Assert.Single(engine.CreateSnapshot().StorageAreas);
        Assert.Equal(6, area.Footprint.Count);
        Assert.Contains(end, area.Footprint);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(area.Footprint, Assert.Single(restored.CreateSnapshot().StorageAreas).Footprint);
    }

    [Fact]
    public void StorageAreaResizeCannotExcludeProviderOrOverlapAnotherArea()
    {
        var engine = CreateEngine();
        var (start, end) = FindTraversableRectangle(engine, width: 4, height: 2);
        var firstEnd = end with { X = start.X + 1 };
        var secondStart = start with { X = start.X + 2 };
        var providerPosition = start with { X = start.X + 1 };
        engine.QueueCommand(SimulationCommand.CreateStorageArea(
            new SimulationTick(1), sequence: 1, start, firstEnd));
        engine.QueueCommand(SimulationCommand.CreateStorageArea(
            new SimulationTick(1), sequence: 2, secondStart, end));
        engine.AdvanceTicks(1);
        var first = engine.CreateSnapshot().StorageAreas.OrderBy(area => area.Id).First();
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            engine.CurrentTick.Next(), sequence: 3, providerPosition, ResourceKind.Wood, capacity: 12));
        engine.AdvanceTicks(1);
        engine.DrainEvents();

        engine.QueueCommand(SimulationCommand.ResizeStorageArea(
            engine.CurrentTick.Next(), sequence: 4, first.Id, start, start));
        engine.AdvanceTicks(1);
        Assert.Contains(engine.DrainEvents(), item =>
            item.Kind == SimulationEventKind.CommandRejected &&
            item.Amount == (int)SimulationCommandKind.ResizeStorageArea);
        Assert.Equal(4, engine.CreateSnapshot().StorageAreas.Single(area => area.Id == first.Id)
            .Footprint.Count);

        engine.QueueCommand(SimulationCommand.ResizeStorageArea(
            engine.CurrentTick.Next(), sequence: 5, first.Id, start, secondStart));
        engine.AdvanceTicks(1);
        Assert.Contains(engine.DrainEvents(), item =>
            item.Kind == SimulationEventKind.CommandRejected &&
            item.Amount == (int)SimulationCommandKind.ResizeStorageArea);
        Assert.Equal(4, engine.CreateSnapshot().StorageAreas.Single(area => area.Id == first.Id)
            .Footprint.Count);
    }

    [Fact]
    public void DissolvingAreaKeepsProvidersContentsAndNetworkAsSingletonAreas()
    {
        var engine = CreateEngine();
        engine.QueueCommand(SimulationCommand.CreateLogisticsNetwork(
            new SimulationTick(1), sequence: 1));
        engine.AdvanceTicks(1);
        var network = engine.CreateSnapshot().LogisticsNetworks.Single(item => !item.IsDefault);
        var (start, end) = FindTraversableRectangle(engine, width: 2, height: 1);
        engine.QueueCommand(SimulationCommand.CreateStorageArea(
            engine.CurrentTick.Next(), sequence: 2, start, end, network.Id));
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            engine.CurrentTick.Next(), sequence: 3, start, ResourceKind.Any, capacity: 16));
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            engine.CurrentTick.Next(), sequence: 4, end, ResourceKind.Any, capacity: 16));
        engine.AdvanceTicks(1);
        var snapshot = engine.CreateSnapshot();
        var originalArea = Assert.Single(snapshot.StorageAreas);
        var zones = snapshot.StorageZones.OrderBy(zone => zone.Id).ToArray();
        engine = AddStoredWood(engine, zones[0].Id, zones[0].Position, quantity: 5);
        engine.QueueCommand(SimulationCommand.DissolveStorageArea(
            engine.CurrentTick.Next(), sequence: 5, originalArea.Id));
        engine.AdvanceTicks(1);

        snapshot = engine.CreateSnapshot();
        Assert.DoesNotContain(snapshot.StorageAreas, area => area.Id == originalArea.Id);
        Assert.Equal(2, snapshot.StorageAreas.Count);
        Assert.All(snapshot.StorageAreas, area =>
        {
            Assert.Single(area.Footprint);
            Assert.Equal(network.Id, area.LogisticsNetworkId);
            Assert.Single(area.StorageZoneIds);
        });
        Assert.Equal(5, snapshot.StorageZones.Single(zone => zone.Id == zones[0].Id)
            .StoredQuantity);
        Assert.All(snapshot.StorageZones, zone => Assert.Equal(network.Id, zone.LogisticsNetworkId));
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    private static SimulationEngine CreateEngine()
    {
        var seed = new WorldSeed(0x4D414E4147454D45UL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        return SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 2,
            initialFoodStock: 0,
            initialWoodStock: 0);
    }

    private static SimulationEngine AddStoredWood(
        SimulationEngine engine,
        EntityId storageZoneId,
        GridPosition position,
        int quantity) =>
        AddItemStack(
            engine,
            ResourceKind.Wood,
            FoodKind.None,
            ResourceVariant.OakWood,
            quantity,
            ItemLocationKind.StorageZone,
            position,
            storageZoneId);

    private static SimulationEngine ConvertToWoodenBox(
        SimulationEngine engine,
        EntityId storageZoneId)
    {
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var savedZone = save["storageZones"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(node => node["id"]!.GetValue<ulong>() == storageZoneId.Value);
        savedZone["providerKind"] = (int)StorageProviderKind.WoodenBox;
        savedZone["slotCount"] = 4;
        savedZone["stackCapacity"] = 8;
        savedZone["separatesItemTypes"] = true;
        savedZone["capabilities"] = (int)StorageCapability.SolidGoods;
        return SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
    }

    private static SimulationEngine AddItemStack(
        SimulationEngine engine,
        ResourceKind resource,
        FoodKind foodKind,
        ResourceVariant variant,
        int quantity,
        ItemLocationKind locationKind,
        GridPosition position,
        EntityId ownerId)
    {
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var nextId = save["nextEntityId"]!.GetValue<ulong>();
        save["itemStacks"]!.AsArray().Add(new JsonObject
        {
            ["id"] = nextId,
            ["resource"] = (int)resource,
            ["foodKind"] = (int)foodKind,
            ["variant"] = (int)variant,
            ["quantity"] = quantity,
            ["locationKind"] = (int)locationKind,
            ["x"] = position.X,
            ["y"] = position.Y,
            ["z"] = position.Z,
            ["ownerId"] = ownerId.Value,
        });
        save["nextEntityId"] = nextId + 1;
        return SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
    }

    private static (GridPosition Start, GridPosition End) FindTraversableRectangle(
        SimulationEngine engine,
        int width,
        int height)
    {
        var z = engine.Map.GoblinSpawn.Z;
        for (var y = 0; y <= engine.Map.Height - height; y++)
        {
            for (var x = 0; x <= engine.Map.Width - width; x++)
            {
                var start = new GridPosition(x, y, z);
                var end = new GridPosition(x + width - 1, y + height - 1, z);
                var cells = Enumerable.Range(0, height)
                    .SelectMany(offsetY => Enumerable.Range(0, width)
                        .Select(offsetX => new GridPosition(x + offsetX, y + offsetY, z)));
                if (cells.All(engine.World.IsTerrainTraversable))
                {
                    return (start, end);
                }
            }
        }

        throw new InvalidOperationException("Generated map has no suitable storage rectangle.");
    }
}
