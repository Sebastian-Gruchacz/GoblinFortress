using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class CraftingTests
{
    [Fact]
    public void BuiltEquipmentStorageUsesSharedCapacityAcrossGearKinds()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0xE011UL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 2,
            initialFoodStock: 8,
            initialWoodStock: 4);
        var position = engine.Map.GetCardinalNeighbors(engine.Map.GoblinSpawn)
            .First(engine.World.IsTerrainTraversable);
        engine.QueueCommand(SimulationCommand.BuildEquipmentStorage(
            engine.CurrentTick.Next(), engine.NextAvailableCommandSequence, position));

        for (var index = 0; index < 5_000 &&
             engine.CreateSnapshot().StorageZones.All(zone =>
                 zone.AcceptedResource != ResourceKind.Equipment); index++)
        {
            engine.AdvanceTicks(1);
        }

        var storage = Assert.Single(engine.CreateSnapshot().StorageZones, zone =>
            zone.AcceptedResource == ResourceKind.Equipment);
        Assert.Equal(32, storage.Capacity);
        Assert.Equal(32, storage.DesiredQuantity);
        Assert.Equal(1, storage.TypeSlotCount);
        Assert.Equal(32, storage.StackCapacity);
        Assert.False(storage.SeparatesItemTypes);
    }

    [Fact]
    public void BuiltMaterialsStoragePullsLooseCraftingMaterials()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0xA47EUL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 3,
            initialFoodStock: 8,
            initialWoodStock: 4);
        var position = engine.Map.GetCardinalNeighbors(engine.Map.GoblinSpawn)
            .First(engine.World.IsTerrainTraversable);
        engine.QueueCommand(SimulationCommand.BuildMaterialsStorage(
            engine.CurrentTick.Next(), engine.NextAvailableCommandSequence, position));

        for (var index = 0; index < 5_000 &&
             engine.CreateSnapshot().StorageZones.All(zone =>
                 zone.AcceptedResource != ResourceKind.Materials); index++)
        {
            engine.AdvanceTicks(1);
        }

        var storage = Assert.Single(engine.CreateSnapshot().StorageZones, zone =>
            zone.AcceptedResource == ResourceKind.Materials);
        Assert.Equal(64, storage.Capacity);
        Assert.Equal(64, storage.DesiredQuantity);
        Assert.False(storage.SeparatesItemTypes);
    }

    [Fact]
    public void PrimitiveWorkshopConsumesPhysicalMaterialsAndProducesSling()
    {
        var definitions = SimulationDefinitions.Foundation;
        var engine = SimulationEngine.Create(
            new WorldSeed(0xC4A7UL),
            definitions,
            initialGoblinCount: 4,
            initialFoodStock: 12,
            initialWoodStock: 8);
        var workshop = engine.Map.GetCardinalNeighbors(engine.Map.GoblinSpawn)
            .First(engine.World.CanBuildPrimitiveWorkshop);
        engine.QueueCommand(SimulationCommand.BuildPrimitiveWorkshop(
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            workshop));

        for (var index = 0; index < 5_000 &&
             !engine.World.HasPrimitiveWorkshop(workshop); index++)
        {
            engine.AdvanceTicks(1);
        }

        Assert.True(engine.World.HasPrimitiveWorkshop(workshop));
        engine = AddCraftingMaterials(engine, definitions, engine.Map.GoblinSpawn);
        engine.QueueCommand(SimulationCommand.QueuePrimitiveSling(
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            workshop));
        engine.AdvanceTicks(1);
        Assert.Single(engine.CreateSnapshot().CraftingOrders);

        for (var index = 0; index < 5_000 &&
             engine.CreateSnapshot().CraftingOrders.Count > 0; index++)
        {
            engine.AdvanceTicks(1);
        }

        var snapshot = engine.CreateSnapshot();
        Assert.Empty(snapshot.CraftingOrders);
        Assert.Contains(snapshot.ItemStacks, stack =>
            stack.Resource == ResourceKind.Equipment &&
            stack.Variant == ResourceVariant.EquipmentPrimitiveSling &&
            stack.Location.Kind == ItemLocationKind.Ground);
        Assert.DoesNotContain(snapshot.ItemStacks, stack =>
            stack.Resource is ResourceKind.Hide or ResourceKind.Bone);
        Assert.Contains(engine.DrainEvents(), simulationEvent =>
            simulationEvent.Kind == SimulationEventKind.CraftingCompleted);
        var restored = SimulationEngine.Load(engine.Save(), definitions);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void PrimitiveWorkshopIgnoresLooseMaterialsOutsideStorage()
    {
        var definitions = SimulationDefinitions.Foundation;
        var engine = SimulationEngine.Create(
            new WorldSeed(0xC4A9UL),
            definitions,
            initialGoblinCount: 3,
            initialFoodStock: 10,
            initialWoodStock: 8);
        var workshop = engine.Map.GetCardinalNeighbors(engine.Map.GoblinSpawn)
            .First(engine.World.CanBuildPrimitiveWorkshop);
        engine.QueueCommand(SimulationCommand.BuildPrimitiveWorkshop(
            engine.CurrentTick.Next(), engine.NextAvailableCommandSequence, workshop));
        for (var index = 0; index < 5_000 && !engine.World.HasPrimitiveWorkshop(workshop); index++)
        {
            engine.AdvanceTicks(1);
        }

        Assert.True(engine.World.HasPrimitiveWorkshop(workshop));
        engine = AddLooseCraftingMaterials(engine, definitions, engine.Map.GoblinSpawn);
        engine.QueueCommand(SimulationCommand.QueuePrimitiveSling(
            engine.CurrentTick.Next(), engine.NextAvailableCommandSequence, workshop));
        engine.AdvanceTicks(200);

        var order = Assert.Single(engine.CreateSnapshot().CraftingOrders);
        Assert.All(order.Materials, material => Assert.Equal(0, material.DeliveredQuantity));
        Assert.DoesNotContain(engine.CreateSnapshot().Actors, actor =>
            actor.Job.Kind == ActorJobKind.SupplyCrafting);
    }

    [Fact]
    public void GoblinPocketsPhysicalStonesAsPersonalAmmunition()
    {
        var definitions = SimulationDefinitions.Foundation;
        var engine = SimulationEngine.Create(
            new WorldSeed(0xA660UL),
            definitions,
            initialGoblinCount: 2,
            initialFoodStock: 8);
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            engine.Map.GoblinSpawn,
            ResourceKind.Any,
            capacity: 16));
        engine.AdvanceTicks(1);
        var zone = Assert.Single(engine.CreateSnapshot().StorageZones);
        engine = AddStack(
            engine,
            definitions,
            engine.Map.GoblinSpawn,
            ResourceKind.Stone,
            quantity: 6,
            storageZoneId: zone.Id);

        for (var index = 0; index < 1_000 &&
             engine.CreateSnapshot().Actors.Sum(actor => actor.PersonalStoneAmmo) == 0; index++)
        {
            engine.AdvanceTicks(1);
        }

        var snapshot = engine.CreateSnapshot();
        var carriedAmmo = snapshot.Actors.Sum(actor => actor.PersonalStoneAmmo);
        var looseStone = snapshot.ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Stone)
            .Sum(stack => stack.Quantity);
        Assert.True(carriedAmmo > 0);
        Assert.Equal(6, carriedAmmo + looseStone);
    }

    [Fact]
    public void PrimitiveWorkshopCraftsBasicToolsWeaponsClothesAndContainers()
    {
        var definitions = SimulationDefinitions.Foundation;
        var engine = SimulationEngine.Create(
            new WorldSeed(0xC4A8UL),
            definitions,
            initialGoblinCount: 4,
            initialFoodStock: 12,
            initialWoodStock: 8);
        var workshop = engine.Map.GetCardinalNeighbors(engine.Map.GoblinSpawn)
            .First(engine.World.CanBuildPrimitiveWorkshop);
        engine.QueueCommand(SimulationCommand.BuildPrimitiveWorkshop(
            engine.CurrentTick.Next(), engine.NextAvailableCommandSequence, workshop));
        for (var index = 0; index < 5_000 && !engine.World.HasPrimitiveWorkshop(workshop); index++)
        {
            engine.AdvanceTicks(1);
        }

        Assert.True(engine.World.HasPrimitiveWorkshop(workshop));
        var equipmentStorage = engine.Map.GetCardinalNeighbors(engine.Map.GoblinSpawn)
            .First(position => position != workshop && engine.World.IsTerrainTraversable(position));
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            equipmentStorage,
            ResourceKind.Equipment,
            capacity: 32));
        engine.AdvanceTicks(1);
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        foreach (var actor in save["actors"]!.AsArray())
        {
            actor!["equipment"] = actor["equipment"]!.GetValue<int>() &
                ~(int)PersonalEquipment.PrimitiveWaterskin;
            actor["personalWater"] = 0;
        }
        engine = SimulationEngine.Load(save.ToJsonString(), definitions);
        Assert.DoesNotContain(engine.CreateSnapshot().Actors, actor =>
            actor.Equipment.HasFlag(PersonalEquipment.PrimitiveWaterskin));
        engine = AddCraftingMaterials(engine, definitions, engine.Map.GoblinSpawn,
            (ResourceKind.Bone, 1),
            (ResourceKind.Wood, 4),
            (ResourceKind.Stone, 1),
            (ResourceKind.Hide, 3),
            (ResourceKind.Reeds, 3));
        foreach (var recipe in new[]
                 {
                     CraftingRecipeKind.BoneKnife,
                     CraftingRecipeKind.FightingStick,
                     CraftingRecipeKind.StoneClub,
                     CraftingRecipeKind.HideClothes,
                     CraftingRecipeKind.ReedClothes,
                     CraftingRecipeKind.PrimitiveWaterskin,
                 })
        {
            engine.QueueCommand(SimulationCommand.QueueCraftingRecipe(
                engine.CurrentTick.Next(), engine.NextAvailableCommandSequence, workshop, recipe));
        }

        for (var index = 0; index < 20_000 &&
             (engine.CreateSnapshot() is var current &&
              (current.CraftingOrders.Count > 0 ||
               current.ItemStacks.Any(stack => stack.Resource == ResourceKind.Equipment) ||
               index == 0)); index++)
        {
            engine.AdvanceTicks(1);
        }

        var snapshot = engine.CreateSnapshot();
        Assert.Empty(snapshot.CraftingOrders);
        var equipment = snapshot.Actors.Aggregate(
            PersonalEquipment.None,
            (all, actor) => all | actor.Equipment);
        Assert.True(equipment.HasFlag(PersonalEquipment.BoneKnife));
        Assert.True(equipment.HasFlag(PersonalEquipment.FightingStick));
        Assert.True(equipment.HasFlag(PersonalEquipment.StoneClub));
        Assert.True(equipment.HasFlag(PersonalEquipment.HideClothes));
        Assert.True(equipment.HasFlag(PersonalEquipment.ReedClothes));
        Assert.True(equipment.HasFlag(PersonalEquipment.PrimitiveWaterskin));
        Assert.DoesNotContain(snapshot.ItemStacks, stack =>
            stack.Resource == ResourceKind.Equipment &&
            stack.Location.Kind == ItemLocationKind.Ground);
    }

    private static SimulationEngine AddCraftingMaterials(
        SimulationEngine engine,
        SimulationDefinitions definitions,
        GridPosition position,
        params (ResourceKind Resource, int Quantity)[] materials)
    {
        materials = materials.Length == 0
            ? [(ResourceKind.Hide, 1), (ResourceKind.Bone, 1)]
            : materials;
        var requiredStorageKinds = materials
            .Select(material => GetStorageKind(material.Resource))
            .Distinct()
            .ToArray();
        foreach (var storageKind in requiredStorageKinds)
        {
            var snapshot = engine.CreateSnapshot();
            if (snapshot.StorageZones.Any(zone => zone.AcceptedResource == storageKind))
            {
                continue;
            }

            var occupied = snapshot.StorageZones.Select(zone => zone.Position).ToHashSet();
            var storagePosition = Enumerable.Range(0, engine.Map.Height)
                .SelectMany(y => Enumerable.Range(0, engine.Map.Width)
                    .Select(x => new GridPosition(x, y, position.Z)))
                .Where(engine.World.IsTerrainTraversable)
                .Where(candidate => !occupied.Contains(candidate))
                .OrderBy(candidate => Math.Abs(candidate.X - position.X) +
                    Math.Abs(candidate.Y - position.Y))
                .First();
            engine.QueueCommand(SimulationCommand.CreateStorageZone(
                engine.CurrentTick.Next(),
                engine.NextAvailableCommandSequence,
                storagePosition,
                storageKind,
                capacity: 64));
            engine.AdvanceTicks(1);
        }

        var storageByKind = engine.CreateSnapshot().StorageZones
            .Where(zone => requiredStorageKinds.Contains(zone.AcceptedResource))
            .GroupBy(zone => zone.AcceptedResource)
            .ToDictionary(group => group.Key, group => group.First());
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var nextId = save["nextEntityId"]!.GetValue<ulong>();
        var stacks = save["itemStacks"]!.AsArray();
        foreach (var material in materials)
        {
            var materialStorage = storageByKind[GetStorageKind(material.Resource)];
            stacks.Add(CreateStack(
                nextId++,
                material.Resource,
                materialStorage.Position,
                material.Quantity,
                materialStorage.Id));
        }
        save["nextEntityId"] = nextId;
        return SimulationEngine.Load(save.ToJsonString(), definitions);

        static ResourceKind GetStorageKind(ResourceKind resource) => resource is
            ResourceKind.Reeds or ResourceKind.Bone or ResourceKind.Hide
                ? ResourceKind.Materials
                : resource;
    }

    private static SimulationEngine AddStack(
        SimulationEngine engine,
        SimulationDefinitions definitions,
        GridPosition position,
        ResourceKind resource,
        int quantity,
        EntityId storageZoneId = default)
    {
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var actor = save["actors"]!.AsArray()[0]!.AsObject();
        actor["equipment"] = actor["equipment"]!.GetValue<int>() |
            (int)PersonalEquipment.PrimitiveSling;
        var nextId = save["nextEntityId"]!.GetValue<ulong>();
        save["itemStacks"]!.AsArray().Add(CreateStack(
            nextId,
            resource,
            position,
            quantity,
            storageZoneId));
        save["nextEntityId"] = nextId + 1;
        return SimulationEngine.Load(save.ToJsonString(), definitions);
    }

    private static SimulationEngine AddLooseCraftingMaterials(
        SimulationEngine engine,
        SimulationDefinitions definitions,
        GridPosition position)
    {
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var nextId = save["nextEntityId"]!.GetValue<ulong>();
        var stacks = save["itemStacks"]!.AsArray();
        stacks.Add(CreateStack(nextId++, ResourceKind.Hide, position));
        stacks.Add(CreateStack(nextId++, ResourceKind.Bone, position));
        save["nextEntityId"] = nextId;
        return SimulationEngine.Load(save.ToJsonString(), definitions);
    }

    private static JsonObject CreateStack(
        ulong id,
        ResourceKind resource,
        GridPosition position,
        int quantity = 1,
        EntityId storageZoneId = default) => new()
    {
        ["id"] = id,
        ["resource"] = (int)resource,
        ["foodKind"] = (int)FoodKind.None,
        ["variant"] = (int)ResourceVariant.None,
        ["quantity"] = quantity,
        ["locationKind"] = (int)(storageZoneId == EntityId.None
            ? ItemLocationKind.Ground
            : ItemLocationKind.StorageZone),
        ["x"] = position.X,
        ["y"] = position.Y,
        ["z"] = position.Z,
        ["ownerId"] = storageZoneId.Value,
    };
}
