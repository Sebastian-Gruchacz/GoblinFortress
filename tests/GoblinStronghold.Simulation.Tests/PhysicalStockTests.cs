using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class PhysicalStockTests
{
    [Fact]
    public void ResourceInventoryDoesNotRevealUnknownGroundGoods()
    {
        var engine = CreateStockScenario(initialFood: 5);
        var stack = Assert.Single(engine.CreateSnapshot().ItemStacks);
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        var visibilityIndex = (stack.Location.Position.Y * engine.Map.Width) +
            stack.Location.Position.X;
        save["visibility"]!.AsArray()[visibilityIndex] = (int)CellVisibility.Unknown;

        var hidden = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);

        var snapshot = hidden.CreateSnapshot();
        var inventory = Assert.Single(snapshot.ResourceInventory, item =>
            item.Resource == ResourceKind.Food);
        Assert.Equal(0, inventory.KnownLooseQuantity);
        Assert.DoesNotContain(
            snapshot.ResourceInventory,
            item => item.Resource == ResourceKind.Vegetation);
        Assert.Equal(5, snapshot.FoodStock);
    }

    [Fact]
    public void SmallFoodStorageLimitsOneFoodKindToOneStackOfThirtyTwo()
    {
        var seed = new WorldSeed(0x534C4F5453UL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 40);
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1),
            sequence: 1,
            map.GoblinSpawn,
            ResourceKind.Food,
            SimulationDefinitions.Foundation.Storage.SmallFoodCapacity));

        engine.AdvanceTicks(160);

        var snapshot = engine.CreateSnapshot();
        var zone = Assert.Single(snapshot.StorageZones);
        Assert.Equal(32, zone.StoredQuantity);
        Assert.Equal(1, zone.UsedTypeSlots);
        Assert.True(zone.SeparatesItemTypes);
        Assert.Equal(StorageCapability.SolidGoods, zone.Capabilities);
        var stored = Assert.Single(snapshot.ItemStacks.Where(stack =>
            stack.Location.Kind == ItemLocationKind.StorageZone));
        Assert.Equal(FoodKind.DriedRations, stored.FoodKind);
        Assert.Equal(32, stored.Quantity);
    }

    [Fact]
    public void DefaultStorageFilterAcceptsDifferentSolidGoodsInOneSharedSlot()
    {
        var seed = new WorldSeed(0x414E59534C4F54UL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 3,
            initialWoodStock: 3);
        var actorId = Assert.Single(engine.CreateSnapshot().Actors).Id;
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1),
            sequence: 1,
            map.HumanVillage,
            ResourceKind.Any,
            capacity: 10));
        engine.AdvanceTicks(1);
        var zone = Assert.Single(engine.CreateSnapshot().StorageZones);

        foreach (var resource in new[] { ResourceKind.Food, ResourceKind.Wood })
        {
            var stack = Assert.Single(engine.CreateSnapshot().ItemStacks, item =>
                item.Resource == resource && item.Location.Kind == ItemLocationKind.Ground);
            engine.QueueCommand(SimulationCommand.PickUp(
                engine.CurrentTick.Next(),
                sequence: resource == ResourceKind.Food ? 2UL : 4UL,
                actorId,
                stack.Id,
                quantity: 2));
            engine.AdvanceTicks(1);
            engine.QueueCommand(SimulationCommand.StoreCarried(
                engine.CurrentTick.Next(),
                sequence: resource == ResourceKind.Food ? 3UL : 5UL,
                actorId,
                zone.Id));
            engine.AdvanceTicks(1);
        }

        zone = Assert.Single(engine.CreateSnapshot().StorageZones);
        Assert.Equal(ResourceKind.Any, zone.AcceptedResource);
        Assert.Equal(4, zone.StoredQuantity);
        Assert.Equal(1, zone.TypeSlotCount);
        Assert.Equal(10, zone.StackCapacity);
        Assert.False(zone.SeparatesItemTypes);
        Assert.Equal(0, zone.UsedTypeSlots);
        Assert.Equal(2, engine.CreateSnapshot().ItemStacks.Count(item =>
            item.Location.Kind == ItemLocationKind.StorageZone));
    }

    [Fact]
    public void OpenAndSealedSlotCapabilitiesDistinguishLiquids()
    {
        var open = new StorageSlotPolicy(
            SlotCount: 1,
            StackCapacity: 32,
            SeparatesItemTypes: false,
            StorageCapability.SolidGoods);
        var barrel = new StorageSlotPolicy(
            SlotCount: 1,
            StackCapacity: 32,
            SeparatesItemTypes: false,
            StorageCapability.SolidGoods | StorageCapability.SealedLiquids);

        Assert.True(open.Supports(StorageRequirement.SolidGoods));
        Assert.False(open.Supports(StorageRequirement.SealedLiquid));
        Assert.True(barrel.Supports(StorageRequirement.SealedLiquid));
    }

    [Fact]
    public void LegacyStorageSaveDerivesEquivalentSlotPolicy()
    {
        var engine = CreateStockScenario(initialFood: 5);
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1),
            sequence: 1,
            engine.Map.HumanVillage,
            ResourceKind.Food,
            SimulationDefinitions.Foundation.Storage.SmallFoodCapacity));
        engine.AdvanceTicks(1);
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        var zoneModel = save["storageZones"]![0]!.AsObject();
        zoneModel.Remove("slotCount");
        zoneModel.Remove("stackCapacity");
        zoneModel.Remove("separatesItemTypes");
        zoneModel.Remove("capabilities");

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        var zone = Assert.Single(restored.CreateSnapshot().StorageZones);
        Assert.Equal(SimulationDefinitions.Foundation.Storage.SmallFoodTypeSlots, zone.TypeSlotCount);
        Assert.Equal(SimulationDefinitions.Foundation.Storage.SmallStackCapacity, zone.StackCapacity);
        Assert.True(zone.SeparatesItemTypes);
    }

    [Fact]
    public void PartialStackCanMoveThroughInventoryIntoStorage()
    {
        var engine = CreateStockScenario(initialFood: 7);
        var initial = engine.CreateSnapshot();
        var groundStack = Assert.Single(initial.ItemStacks);
        var initialInventory = Assert.Single(initial.ResourceInventory, item =>
            item.Resource == ResourceKind.Food);
        Assert.Equal(0, initialInventory.StoredQuantity);
        Assert.Equal(7, initialInventory.KnownLooseQuantity);
        Assert.Equal(0, initialInventory.CarriedQuantity);

        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1),
            sequence: 1,
            engine.Map.HumanVillage,
            ResourceKind.Food,
            capacity: 10));
        engine.AdvanceTicks(1);

        var zone = Assert.Single(engine.CreateSnapshot().StorageZones);
        engine.QueueCommand(SimulationCommand.PickUp(
            new SimulationTick(2),
            sequence: 2,
            new EntityId(1),
            groundStack.Id,
            quantity: 3));
        engine.AdvanceTicks(1);

        var carrying = engine.CreateSnapshot();
        var actor = Assert.Single(carrying.Actors);
        var carried = Assert.Single(carrying.ItemStacks, stack =>
            stack.Location.Kind == ItemLocationKind.ActorInventory);
        var remainder = Assert.Single(carrying.ItemStacks, stack =>
            stack.Location.Kind == ItemLocationKind.Ground);

        Assert.Equal(3, carried.Quantity);
        Assert.Equal(4, remainder.Quantity);
        Assert.Equal(carried.Id, actor.CarriedStackId);
        Assert.Equal(groundStack.Location.Position, actor.Position);
        Assert.Equal(7, carrying.FoodStock);
        var carryingInventory = Assert.Single(carrying.ResourceInventory, item =>
            item.Resource == ResourceKind.Food);
        Assert.Equal(0, carryingInventory.StoredQuantity);
        Assert.Equal(4, carryingInventory.KnownLooseQuantity);
        Assert.Equal(3, carryingInventory.CarriedQuantity);

        engine.QueueCommand(SimulationCommand.StoreCarried(
            new SimulationTick(3),
            sequence: 3,
            actor.Id,
            zone.Id));
        engine.AdvanceTicks(1);

        var stored = engine.CreateSnapshot();
        actor = Assert.Single(stored.Actors);
        zone = Assert.Single(stored.StorageZones);
        carried = Assert.Single(stored.ItemStacks, stack => stack.Id == carried.Id);

        Assert.Equal(EntityId.None, actor.CarriedStackId);
        Assert.Equal(zone.Position, actor.Position);
        Assert.Equal(ItemLocationKind.StorageZone, carried.Location.Kind);
        Assert.Equal(zone.Id, carried.Location.OwnerId);
        Assert.Equal(3, zone.StoredQuantity);
        Assert.Equal(7, stored.FoodStock);
        var storedInventory = Assert.Single(stored.ResourceInventory, item =>
            item.Resource == ResourceKind.Food);
        Assert.Equal(3, storedInventory.StoredQuantity);
        Assert.Equal(4, storedInventory.KnownLooseQuantity);
        Assert.Equal(0, storedInventory.CarriedQuantity);
    }

    [Fact]
    public void SaveLoadPreservesCarriedStackAndPendingStoreCommand()
    {
        var engine = CreateStockScenario(initialFood: 6);
        var groundStack = Assert.Single(engine.CreateSnapshot().ItemStacks);

        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1),
            sequence: 1,
            engine.Map.HumanVillage,
            ResourceKind.Food,
            capacity: 10));
        engine.AdvanceTicks(1);
        var zone = Assert.Single(engine.CreateSnapshot().StorageZones);

        engine.QueueCommand(SimulationCommand.PickUp(
            new SimulationTick(2),
            sequence: 2,
            new EntityId(1),
            groundStack.Id,
            quantity: 4));
        engine.AdvanceTicks(1);
        engine.QueueCommand(SimulationCommand.StoreCarried(
            new SimulationTick(5),
            sequence: 3,
            new EntityId(1),
            zone.Id));

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.NotEqual(EntityId.None, Assert.Single(restored.CreateSnapshot().Actors).CarriedStackId);

        new SimulationRunner(engine).RunUntil(new SimulationTick(20), SimulationSpeed.Normal);
        new SimulationRunner(restored).RunUntil(
            new SimulationTick(20),
            SimulationSpeed.Unthrottled,
            unthrottledTickBudget: 7);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(engine.DrainEvents(), restored.DrainEvents());
        Assert.Equal(4, Assert.Single(restored.CreateSnapshot().StorageZones).StoredQuantity);
    }

    [Fact]
    public void StaleSecondPickupIsRejectedWithoutStoppingSimulation()
    {
        var engine = CreateStockScenario(initialFood: 5);
        var stack = Assert.Single(engine.CreateSnapshot().ItemStacks);

        engine.QueueCommand(SimulationCommand.PickUp(
            new SimulationTick(1),
            sequence: 1,
            new EntityId(1),
            stack.Id,
            quantity: 5));
        engine.QueueCommand(SimulationCommand.PickUp(
            new SimulationTick(1),
            sequence: 2,
            new EntityId(1),
            stack.Id,
            quantity: 5));

        engine.AdvanceTicks(2);

        var events = engine.DrainEvents();
        Assert.Contains(events, simulationEvent => simulationEvent.Kind == SimulationEventKind.ItemPickedUp);
        Assert.Contains(events, simulationEvent => simulationEvent.Kind == SimulationEventKind.CommandRejected);
        Assert.Equal(new SimulationTick(2), engine.CurrentTick);
    }

    [Fact]
    public void ZoneCapacityRejectsOversizedDelivery()
    {
        var engine = CreateStockScenario(initialFood: 5);
        var stack = Assert.Single(engine.CreateSnapshot().ItemStacks);

        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1),
            sequence: 1,
            engine.Map.HumanVillage,
            ResourceKind.Food,
            capacity: 4));
        engine.AdvanceTicks(1);
        var zone = Assert.Single(engine.CreateSnapshot().StorageZones);

        engine.QueueCommand(SimulationCommand.PickUp(
            new SimulationTick(2),
            sequence: 2,
            new EntityId(1),
            stack.Id,
            quantity: 5));
        engine.AdvanceTicks(1);
        engine.QueueCommand(SimulationCommand.StoreCarried(
            new SimulationTick(3),
            sequence: 3,
            new EntityId(1),
            zone.Id));
        engine.AdvanceTicks(1);

        var snapshot = engine.CreateSnapshot();
        Assert.NotEqual(EntityId.None, Assert.Single(snapshot.Actors).CarriedStackId);
        Assert.Equal(0, Assert.Single(snapshot.StorageZones).StoredQuantity);
        Assert.Contains(
            engine.DrainEvents(),
            simulationEvent => simulationEvent.Kind == SimulationEventKind.CommandRejected);
    }

    [Fact]
    public void MapAndPhysicalOwnershipParticipateInStateHash()
    {
        var first = CreateStockScenario(initialFood: 5);
        var second = CreateStockScenario(initialFood: 6);

        Assert.NotEqual(first.ComputeStateHash(), second.ComputeStateHash());
        Assert.Equal(first.Map.ComputeFingerprint(), first.CreateSnapshot().MapFingerprint);
    }

    [Fact]
    public void StoneStorageFilterRejectsExcludedMineralsAndPreservesExistingContents()
    {
        var seed = new WorldSeed(0x46494C544552UL);
        var map = SwampMapGenerator.Generate(seed, width: 48, height: 48);
        var generated = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            scatterInitialBrushwood: true);
        var save = JsonNode.Parse(generated.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        var stoneModels = save["itemStacks"]!.AsArray()
            .Select(node => node!.AsObject())
            .Where(model => model["resource"]!.GetValue<int>() == (int)ResourceKind.Stone)
            .Take(2)
            .ToArray();
        Assert.Equal(2, stoneModels.Length);
        for (var index = 0; index < stoneModels.Length; index++)
        {
            stoneModels[index]["x"] = map.GoblinSpawn.X;
            stoneModels[index]["y"] = map.GoblinSpawn.Y;
            stoneModels[index]["z"] = map.GoblinSpawn.Z;
            stoneModels[index]["quantity"] = 4;
            stoneModels[index]["variant"] = index == 0
                ? (int)ResourceVariant.Sandstone
                : (int)ResourceVariant.Granite;
        }

        var engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        var zonePosition = map.GetCardinalNeighbors(map.GoblinSpawn)
            .First(engine.World.IsSurfaceTraversable);
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1),
            sequence: 1,
            zonePosition,
            ResourceKind.Stone,
            capacity: 16));
        engine.AdvanceTicks(1);
        var zone = Assert.Single(engine.CreateSnapshot().StorageZones);
        engine.QueueCommand(SimulationCommand.ConfigureStorageMineralFilter(
            new SimulationTick(2),
            sequence: 2,
            zone.Id,
            MineralStorageFilter.Sandstone));
        engine.AdvanceTicks(1);

        var stacks = engine.CreateSnapshot().ItemStacks;
        var sandstone = stacks.Single(stack =>
            stack.Id.Value == stoneModels[0]["id"]!.GetValue<ulong>());
        var granite = stacks.Single(stack =>
            stack.Id.Value == stoneModels[1]["id"]!.GetValue<ulong>());
        var actor = Assert.Single(engine.CreateSnapshot().Actors);
        engine.QueueCommand(SimulationCommand.PickUp(
            new SimulationTick(3),
            sequence: 3,
            actor.Id,
            sandstone.Id,
            sandstone.Quantity));
        engine.QueueCommand(SimulationCommand.StoreCarried(
            new SimulationTick(4),
            sequence: 4,
            actor.Id,
            zone.Id));
        engine.AdvanceTicks(2);
        Assert.Equal(4, Assert.Single(engine.CreateSnapshot().StorageZones).StoredQuantity);

        engine.QueueCommand(SimulationCommand.PickUp(
            new SimulationTick(5),
            sequence: 5,
            actor.Id,
            granite.Id,
            granite.Quantity));
        engine.QueueCommand(SimulationCommand.StoreCarried(
            new SimulationTick(6),
            sequence: 6,
            actor.Id,
            zone.Id));
        engine.AdvanceTicks(2);
        Assert.Equal(4, Assert.Single(engine.CreateSnapshot().StorageZones).StoredQuantity);
        Assert.Contains(engine.DrainEvents(), simulationEvent =>
            simulationEvent.Kind == SimulationEventKind.CommandRejected);

        engine.QueueCommand(SimulationCommand.ConfigureStorageMineralFilter(
            new SimulationTick(7),
            sequence: 7,
            zone.Id,
            MineralStorageFilter.None));
        engine.AdvanceTicks(1);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        var restoredZone = Assert.Single(restored.CreateSnapshot().StorageZones);
        Assert.Equal(MineralStorageFilter.None, restoredZone.MineralFilter);
        Assert.Equal(4, restoredZone.StoredQuantity);
        Assert.Contains(restored.CreateSnapshot().ItemStacks, stack =>
            stack.Location.Kind == ItemLocationKind.StorageZone &&
            stack.Variant == ResourceVariant.Sandstone);
    }

    [Fact]
    public void WoodAndStoneVariantsAreDeterministicAndSurviveSaveLoad()
    {
        var seed = new WorldSeed(0x4D4154455249414CUL);
        var map = SwampMapGenerator.Generate(seed, width: 48, height: 48);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            scatterInitialBrushwood: true);

        var resourceStacks = engine.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource is ResourceKind.Wood or ResourceKind.Stone)
            .OrderBy(stack => stack.Id)
            .ToArray();
        Assert.Contains(resourceStacks, stack => stack.Resource == ResourceKind.Wood);
        Assert.Contains(resourceStacks, stack => stack.Resource == ResourceKind.Stone);
        Assert.All(resourceStacks.Where(stack => stack.Resource == ResourceKind.Wood), stack =>
            Assert.InRange(stack.Variant, ResourceVariant.OakWood, ResourceVariant.PineWood));
        Assert.All(resourceStacks.Where(stack => stack.Resource == ResourceKind.Stone), stack =>
            Assert.Contains(stack.Variant, [ResourceVariant.Sandstone, ResourceVariant.Granite]));

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        var restoredVariants = restored.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource is ResourceKind.Wood or ResourceKind.Stone)
            .OrderBy(stack => stack.Id)
            .Select(stack => (stack.Id, stack.Variant));
        Assert.Equal(resourceStacks.Select(stack => (stack.Id, stack.Variant)), restoredVariants);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    private static SimulationEngine CreateStockScenario(int initialFood)
    {
        var seed = new WorldSeed(0x53544F434BUL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        return SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: initialFood);
    }
}
