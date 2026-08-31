using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class WaterLogisticsTests
{
    [Fact]
    public void BarrelPlacementRequestsCraftedBarrelAndCreatesSealedWaterRequester()
    {
        var definitions = SimulationDefinitions.Foundation;
        var engine = SimulationEngine.Create(
            new WorldSeed(0xBA22E1UL),
            definitions,
            initialGoblinCount: 2,
            initialFoodStock: 8,
            initialWoodStock: 4);
        var target = engine.Map.GetCardinalNeighbors(engine.Map.GoblinSpawn)
            .First(engine.World.IsTerrainTraversable);
        engine.QueueCommand(SimulationCommand.PlaceWaterBarrel(
            engine.CurrentTick.Next(), engine.NextAvailableCommandSequence, target));
        engine.AdvanceTicks(1);

        var site = Assert.Single(engine.CreateSnapshot().ConstructionSites);
        var material = Assert.Single(site.Materials);
        Assert.Equal(ResourceKind.Equipment, material.Resource);
        Assert.Equal(ResourceVariant.EquipmentWoodenBarrel, material.Variant);
        Assert.Equal(1, material.RequiredQuantity);

        engine = AddEquipmentStack(
            engine,
            definitions,
            ResourceVariant.EquipmentWoodenBucket,
            engine.Map.GoblinSpawn);
        engine.AdvanceTicks(200);
        Assert.Equal(0, Assert.Single(engine.CreateSnapshot().ConstructionSites)
            .Materials.Single().DeliveredQuantity);

        engine = AddEquipmentStack(
            engine,
            definitions,
            ResourceVariant.EquipmentWoodenBarrel,
            engine.Map.GoblinSpawn);
        for (var tick = 0; tick < 5_000 &&
             engine.CreateSnapshot().StorageZones.All(zone =>
                 zone.AcceptedResource != ResourceKind.Water); tick++)
        {
            engine.AdvanceTicks(1);
        }

        var barrel = Assert.Single(engine.CreateSnapshot().StorageZones, zone =>
            zone.AcceptedResource == ResourceKind.Water);
        Assert.Equal(target, barrel.Position);
        Assert.Equal(32, barrel.Capacity);
        Assert.Equal(32, barrel.DesiredQuantity);
        Assert.Equal(StorageCapability.SealedLiquids, barrel.Capabilities);
        Assert.Equal(
            StorageDeliveryState.NoAvailableTool,
            engine.InspectStorageDelivery(barrel.Id).State);

        var restored = SimulationEngine.Load(engine.Save(), definitions);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void BucketHaulerFillsDeepBarrelAndGoblinRefillsFromIt()
    {
        var definitions = SimulationDefinitions.Foundation;
        var engine = SimulationEngine.Create(
            new WorldSeed(0xD33FBA22E1UL),
            definitions,
            initialGoblinCount: 1,
            initialFoodStock: 8);
        engine = GiveFirstActorBucket(engine, definitions);
        var actor = Assert.Single(engine.CreateSnapshot().Actors);
        var target = (
                from level in Enumerable.Range(1, engine.Map.CaveLevelCount)
                from y in Enumerable.Range(0, engine.Map.Height)
                from x in Enumerable.Range(0, engine.Map.Width)
                let position = new GridPosition(x, y, -level)
                where engine.World.IsTerrainTraversable(position)
                let route = engine.Navigation.FindPath(actor.Position, position)
                where route is not null
                orderby route.Count
                select position)
            .First();
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            target,
            ResourceKind.Water,
            capacity: 32));
        engine.AdvanceTicks(1);
        var barrel = Assert.Single(engine.CreateSnapshot().StorageZones, zone =>
            zone.AcceptedResource == ResourceKind.Water);

        for (var tick = 0; tick < 20_000 &&
             engine.CreateSnapshot().ItemStacks.Where(stack =>
                 stack.Resource == ResourceKind.Water &&
                 stack.Location.Kind == ItemLocationKind.StorageZone &&
                 stack.Location.OwnerId == barrel.Id).Sum(stack => stack.Quantity) == 0; tick++)
        {
            engine.AdvanceTicks(1);
        }

        var delivered = engine.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Water &&
                stack.Location.Kind == ItemLocationKind.StorageZone &&
                stack.Location.OwnerId == barrel.Id)
            .Sum(stack => stack.Quantity);
        Assert.Equal(4, delivered);
        engine.QueueCommand(SimulationCommand.ConfigureStoragePull(
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            barrel.Id,
            delivered));
        engine.AdvanceTicks(1);

        var save = JsonNode.Parse(engine.Save())!.AsObject();
        save["actors"]!.AsArray()[0]!["personalWater"] = 0;
        save["actors"]!.AsArray()[0]!["thirst"] = definitions.DrinkThreshold;
        engine = SimulationEngine.Load(save.ToJsonString(), definitions);
        for (var tick = 0; tick < 1_000 &&
             Assert.Single(engine.CreateSnapshot().Actors).PersonalWater <
                 definitions.PersonalWaterCapacity; tick++)
        {
            engine.AdvanceTicks(1);
        }

        var provisioned = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.Equal(definitions.PersonalWaterCapacity, provisioned.PersonalWater);
        Assert.True(provisioned.Thirst < definitions.DrinkThreshold);
        Assert.Equal(
            delivered - definitions.PersonalWaterCapacity - 1,
            engine.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Water)
            .Sum(stack => stack.Quantity));
        var restored = SimulationEngine.Load(engine.Save(), definitions);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void ConfiguredSourceBarrelRequiresBucketAndTransfersWaterFourUnitsAtATime()
    {
        var definitions = SimulationDefinitions.Foundation;
        var engine = SimulationEngine.Create(
            new WorldSeed(0xBA22E15UL),
            definitions,
            initialGoblinCount: 1,
            initialFoodStock: 8);
        var positions = engine.Map.GetCardinalNeighbors(engine.Map.GoblinSpawn)
            .Prepend(engine.Map.GoblinSpawn)
            .Where(engine.World.IsTerrainTraversable)
            .Take(2)
            .ToArray();
        Assert.Equal(2, positions.Length);
        var executeAt = engine.CurrentTick.Next();
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            executeAt,
            engine.NextAvailableCommandSequence,
            positions[0],
            ResourceKind.Water,
            capacity: 12));
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            executeAt,
            engine.NextAvailableCommandSequence,
            positions[1],
            ResourceKind.Water,
            capacity: 8));
        engine.AdvanceTicks(1);
        var barrels = engine.CreateSnapshot().StorageZones
            .Where(zone => zone.AcceptedResource == ResourceKind.Water)
            .OrderBy(zone => zone.Id)
            .ToArray();
        Assert.Equal(2, barrels.Length);
        var source = barrels[0];
        var destination = barrels[1];
        engine = AddWaterStackToStorage(engine, definitions, source, quantity: 12);

        executeAt = engine.CurrentTick.Next();
        engine.QueueCommand(SimulationCommand.ConfigureStoragePull(
            executeAt,
            engine.NextAvailableCommandSequence,
            source.Id,
            desiredQuantity: 4));
        engine.QueueCommand(SimulationCommand.ConfigureStoragePull(
            executeAt,
            engine.NextAvailableCommandSequence,
            destination.Id,
            desiredQuantity: 8));
        engine.QueueCommand(SimulationCommand.ConfigureStorageSource(
            executeAt,
            engine.NextAvailableCommandSequence,
            destination.Id,
            source.Id));
        engine.AdvanceTicks(1);

        Assert.Equal(
            StorageDeliveryState.NoAvailableTool,
            engine.InspectStorageDelivery(destination.Id).State);

        engine = GiveFirstActorBucket(engine, definitions);
        StorageDeliveryDiagnostic diagnostic = default;
        for (var tick = 0; tick < 1_000; tick++)
        {
            engine.AdvanceTicks(1);
            diagnostic = engine.InspectStorageDelivery(destination.Id);
            if (diagnostic.State == StorageDeliveryState.InTransit)
            {
                break;
            }
        }
        Assert.Equal(StorageDeliveryState.InTransit, diagnostic.State);
        Assert.Equal(4, diagnostic.InTransitQuantity);

        for (var tick = 0; tick < 5_000 &&
             GetStoredWater(engine, destination.Id) < 8; tick++)
        {
            engine.AdvanceTicks(1);
        }

        Assert.Equal(8, GetStoredWater(engine, destination.Id));
        Assert.Equal(4, GetStoredWater(engine, source.Id));
        Assert.Equal(StorageDeliveryState.Satisfied,
            engine.InspectStorageDelivery(destination.Id).State);
        var restored = SimulationEngine.Load(engine.Save(), definitions);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    private static SimulationEngine GiveFirstActorBucket(
        SimulationEngine engine,
        SimulationDefinitions definitions)
    {
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var actor = save["actors"]!.AsArray()[0]!;
        actor["equipment"] = actor["equipment"]!.GetValue<int>() |
            (int)PersonalEquipment.WoodenBucket;
        return SimulationEngine.Load(save.ToJsonString(), definitions);
    }

    private static SimulationEngine AddEquipmentStack(
        SimulationEngine engine,
        SimulationDefinitions definitions,
        ResourceVariant variant,
        GridPosition position)
    {
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var nextId = save["nextEntityId"]!.GetValue<ulong>();
        save["itemStacks"]!.AsArray().Add(new JsonObject
        {
            ["id"] = nextId,
            ["resource"] = (int)ResourceKind.Equipment,
            ["foodKind"] = (int)FoodKind.None,
            ["variant"] = (int)variant,
            ["quantity"] = 1,
            ["locationKind"] = (int)ItemLocationKind.Ground,
            ["x"] = position.X,
            ["y"] = position.Y,
            ["z"] = position.Z,
            ["ownerId"] = 0,
        });
        save["nextEntityId"] = nextId + 1;
        return SimulationEngine.Load(save.ToJsonString(), definitions);
    }

    private static SimulationEngine AddWaterStackToStorage(
        SimulationEngine engine,
        SimulationDefinitions definitions,
        StorageZoneSnapshot zone,
        int quantity)
    {
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var nextId = save["nextEntityId"]!.GetValue<ulong>();
        save["itemStacks"]!.AsArray().Add(new JsonObject
        {
            ["id"] = nextId,
            ["resource"] = (int)ResourceKind.Water,
            ["foodKind"] = (int)FoodKind.None,
            ["variant"] = (int)ResourceVariant.None,
            ["quantity"] = quantity,
            ["locationKind"] = (int)ItemLocationKind.StorageZone,
            ["x"] = zone.Position.X,
            ["y"] = zone.Position.Y,
            ["z"] = zone.Position.Z,
            ["ownerId"] = zone.Id.Value,
        });
        save["nextEntityId"] = nextId + 1;
        return SimulationEngine.Load(save.ToJsonString(), definitions);
    }

    private static int GetStoredWater(SimulationEngine engine, EntityId zoneId) =>
        engine.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Water &&
                stack.Location.Kind == ItemLocationKind.StorageZone &&
                stack.Location.OwnerId == zoneId)
            .Sum(stack => stack.Quantity);
}
