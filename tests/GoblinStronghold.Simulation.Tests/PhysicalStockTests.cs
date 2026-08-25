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
        var stored = Assert.Single(snapshot.ItemStacks.Where(stack =>
            stack.Location.Kind == ItemLocationKind.StorageZone));
        Assert.Equal(FoodKind.DriedRations, stored.FoodKind);
        Assert.Equal(32, stored.Quantity);
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
