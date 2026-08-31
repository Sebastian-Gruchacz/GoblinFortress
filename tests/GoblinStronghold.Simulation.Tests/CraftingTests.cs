using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using GoblinStronghold.Simulation.Workshops;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class CraftingTests
{
    [Fact]
    public void StartingTribeReceivesUpToThreePrimitivePickaxes()
    {
        var largeTribe = SimulationEngine.Create(
            new WorldSeed(0x5049434B415845UL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 5,
            initialFoodStock: 12);
        var smallTribe = SimulationEngine.Create(
            new WorldSeed(0x5049434B415846UL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 2,
            initialFoodStock: 12);

        Assert.Equal(3, largeTribe.CreateSnapshot().Actors.Count(actor =>
            actor.Equipment.HasFlag(PersonalEquipment.PrimitivePickaxe)));
        Assert.All(smallTribe.CreateSnapshot().Actors, actor => Assert.True(
            actor.Equipment.HasFlag(PersonalEquipment.PrimitivePickaxe)));
    }

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
    public void CraftingSavePreservesDeliveredMaterialVariants()
    {
        var definitions = SimulationDefinitions.Foundation;
        var engine = SimulationEngine.Create(
            new WorldSeed(0xC4B0UL),
            definitions,
            initialGoblinCount: 3,
            initialFoodStock: 10,
            initialWoodStock: 4);
        var workshop = engine.Map.GetCardinalNeighbors(engine.Map.GoblinSpawn)
            .First(engine.World.CanBuildPrimitiveWorkshop);
        engine.QueueCommand(SimulationCommand.BuildPrimitiveWorkshop(
            engine.CurrentTick.Next(), engine.NextAvailableCommandSequence, workshop));
        for (var index = 0; index < 5_000 && !engine.World.HasPrimitiveWorkshop(workshop); index++)
        {
            engine.AdvanceTicks(1);
        }

        engine.QueueCommand(SimulationCommand.QueueCraftingRecipe(
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            workshop,
            CraftingRecipeKind.FightingStick));
        engine.AdvanceTicks(1);
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var order = save["craftingOrders"]!.AsArray().Single()!.AsObject();
        order["deliveredMaterials"] = new JsonArray
        {
            new JsonObject
            {
                ["resource"] = (int)ResourceKind.Wood,
                ["variant"] = (int)ResourceVariant.OakWood,
                ["quantity"] = 1,
            },
            new JsonObject
            {
                ["resource"] = (int)ResourceKind.Wood,
                ["variant"] = (int)ResourceVariant.PineWood,
                ["quantity"] = 2,
            },
        };

        var restored = SimulationEngine.Load(save.ToJsonString(), definitions);
        var restoredOrder = Assert.Single(restored.CreateSnapshot().CraftingOrders);
        Assert.Equal(3, Assert.Single(restoredOrder.Materials).DeliveredQuantity);
        var persistedMaterials = JsonNode.Parse(restored.Save())!["craftingOrders"]![0]!
            ["deliveredMaterials"]!.AsArray();
        Assert.Contains(persistedMaterials, material =>
            material!["variant"]!.GetValue<int>() == (int)ResourceVariant.OakWood);
        Assert.Contains(persistedMaterials, material =>
            material!["variant"]!.GetValue<int>() == (int)ResourceVariant.PineWood);
        Assert.Equal(restored.ComputeStateHash(),
            SimulationEngine.Load(restored.Save(), definitions).ComputeStateHash());
    }

    [Fact]
    public void BloomeryConsumesExactOreAndCoalAndProducesIronBar()
    {
        var definitions = SimulationDefinitions.Foundation;
        var engine = SimulationEngine.Create(
            new WorldSeed(0xB1004E2UL),
            definitions,
            initialGoblinCount: 4,
            initialFoodStock: 20,
            initialWoodStock: 0);
        var available = Enumerable.Range(0, engine.Map.Height)
            .SelectMany(y => Enumerable.Range(0, engine.Map.Width)
                .Select(x => new GridPosition(x, y)))
            .Where(engine.World.IsTerrainTraversable)
            .Where(position => engine.Navigation.HasSurfacePath(engine.Map.GoblinSpawn, position))
            .OrderBy(position => Math.Abs(position.X - engine.Map.GoblinSpawn.X) +
                Math.Abs(position.Y - engine.Map.GoblinSpawn.Y))
            .ToArray();
        var storagePosition = available.First(position =>
            position != engine.Map.GoblinSpawn &&
            engine.World.GetWorldObjectsAt(position).Count == 0);
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            storagePosition,
            ResourceKind.Stone,
            capacity: 64));
        engine.AdvanceTicks(1);
        var storage = Assert.Single(engine.CreateSnapshot().StorageZones);
        var furnacePosition = available.First(position =>
            position != storagePosition &&
            engine.World.CanBuildWorkshop(position) &&
            engine.CreateSnapshot().ItemStacks.All(stack =>
                stack.Location.Position != position));

        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var firstActor = save["actors"]!.AsArray()[0]!.AsObject();
        firstActor["equipment"] = firstActor["equipment"]!.GetValue<int>() |
            (int)PersonalEquipment.PrimitivePickaxe;
        var nextId = save["nextEntityId"]!.GetValue<ulong>();
        var stacks = save["itemStacks"]!.AsArray();
        stacks.Add(CreateStack(
            nextId++, ResourceKind.Stone, storagePosition, 12, storage.Id,
            ResourceVariant.Sandstone));
        stacks.Add(CreateStack(
            nextId++, ResourceKind.Ore, storagePosition, 2, storage.Id,
            ResourceVariant.IronOre));
        stacks.Add(CreateStack(
            nextId++, ResourceKind.Coal, storagePosition, 1, storage.Id));
        save["nextEntityId"] = nextId;
        engine = SimulationEngine.Load(save.ToJsonString(), definitions);

        engine.QueueCommand(SimulationCommand.BuildWorkshop(
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            furnacePosition,
            WorkshopKind.Bloomery));
        for (var tick = 0; tick < 5_000 &&
             engine.CreateSnapshot().ConstructionSites.All(site =>
                 site.Materials.All(material => material.DeliveredQuantity == 0)); tick++)
        {
            engine.AdvanceTicks(1);
        }

        var suppliedSite = Assert.Single(engine.CreateSnapshot().ConstructionSites);
        var suppliedMaterial = Assert.Single(suppliedSite.Materials);
        Assert.True(suppliedMaterial.DeliveredQuantity > 0);
        Assert.Equal(ResourceVariant.Sandstone, suppliedMaterial.DeliveredVariant);
        var restoredDuringConstruction = SimulationEngine.Load(engine.Save(), definitions);
        Assert.Equal(engine.ComputeStateHash(), restoredDuringConstruction.ComputeStateHash());
        engine = restoredDuringConstruction;
        for (var tick = 0; tick < 10_000 &&
             !engine.World.HasWorkshop(furnacePosition, WorkshopKind.Bloomery); tick++)
        {
            engine.AdvanceTicks(1);
        }

        Assert.True(engine.World.HasWorkshop(furnacePosition, WorkshopKind.Bloomery));
        var bloomery = Assert.Single(engine.World.GetWorldObjectsAt(furnacePosition),
            worldObject => worldObject.Kind == WorldObjectKind.Bloomery);
        Assert.Equal(ResourceVariant.Sandstone, bloomery.MaterialVariant);
        engine.QueueCommand(SimulationCommand.QueueCraftingRecipe(
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            furnacePosition,
            CraftingRecipeKind.SmeltIronBar));
        for (var tick = 0; tick < 10_000 &&
             (engine.CreateSnapshot().CraftingOrders.Count > 0 || tick == 0); tick++)
        {
            engine.AdvanceTicks(1);
        }

        var snapshot = engine.CreateSnapshot();
        Assert.Empty(snapshot.CraftingOrders);
        Assert.Contains(snapshot.ItemStacks, stack =>
            stack.Resource == ResourceKind.Materials &&
            stack.Variant == ResourceVariant.IronBar &&
            stack.Quantity == 1);
        Assert.DoesNotContain(snapshot.ItemStacks, stack =>
            stack.Variant == ResourceVariant.IronOre);
        Assert.DoesNotContain(snapshot.ItemStacks, stack => stack.Resource == ResourceKind.Coal);
        Assert.Equal(engine.ComputeStateHash(),
            SimulationEngine.Load(engine.Save(), definitions).ComputeStateHash());

        engine = AddStack(
            engine,
            definitions,
            storagePosition,
            ResourceKind.Ore,
            quantity: 4,
            storageZoneId: storage.Id,
            equipSling: false,
            variant: ResourceVariant.IronOre);
        engine = AddStack(
            engine,
            definitions,
            storagePosition,
            ResourceKind.Coal,
            quantity: 2,
            storageZoneId: storage.Id,
            equipSling: false);
        engine.QueueCommand(SimulationCommand.ConfigureRepeatingCraftingRecipe(
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            furnacePosition,
            CraftingRecipeKind.SmeltIronBar,
            enabled: true));
        for (var tick = 0; tick < 20_000 && engine.CreateSnapshot().ItemStacks
                 .Where(stack => stack.Variant == ResourceVariant.IronBar)
                 .Sum(stack => stack.Quantity) < 3; tick++)
        {
            engine.AdvanceTicks(1);
        }

        snapshot = engine.CreateSnapshot();
        var repeating = Assert.Single(snapshot.CraftingOrders);
        Assert.True(repeating.IsRepeating);
        Assert.Equal(CraftingRecipeKind.SmeltIronBar, repeating.Recipe);
        Assert.Equal(3, snapshot.ItemStacks
            .Where(stack => stack.Variant == ResourceVariant.IronBar)
            .Sum(stack => stack.Quantity));
        Assert.All(repeating.Materials, material =>
            Assert.Equal(0, material.DeliveredQuantity));

        engine = SimulationEngine.Load(engine.Save(), definitions);
        Assert.True(Assert.Single(engine.CreateSnapshot().CraftingOrders).IsRepeating);
        engine.QueueCommand(SimulationCommand.ConfigureRepeatingCraftingRecipe(
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            furnacePosition,
            CraftingRecipeKind.SmeltIronBar,
            enabled: false));
        engine.AdvanceTicks(1);
        Assert.Empty(engine.CreateSnapshot().CraftingOrders);
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
            storageZoneId: zone.Id,
            equipSling: false);

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
        Assert.All(snapshot.Actors, actor =>
            Assert.False(actor.Equipment.HasFlag(PersonalEquipment.PrimitiveSling)));
        Assert.Equal(6, carriedAmmo + looseStone);
    }

    [Fact]
    public void BetterMainHandEquipmentReplacesAndStoresPreviousWeapon()
    {
        var definitions = SimulationDefinitions.Foundation;
        var engine = SimulationEngine.Create(
            new WorldSeed(0x55504752414445UL),
            definitions,
            initialGoblinCount: 1,
            initialFoodStock: 0);
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            engine.Map.GoblinSpawn,
            ResourceKind.Equipment,
            capacity: 8));
        engine.AdvanceTicks(1);
        var zone = Assert.Single(engine.CreateSnapshot().StorageZones);
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var actor = save["actors"]!.AsArray()[0]!.AsObject();
        actor["equipment"] = (int)(PersonalEquipment.RagClothes |
            PersonalEquipment.PrimitiveWaterskin |
            PersonalEquipment.FightingStick);
        var nextId = save["nextEntityId"]!.GetValue<ulong>();
        save["itemStacks"]!.AsArray().Add(CreateStack(
            nextId,
            ResourceKind.Equipment,
            zone.Position,
            storageZoneId: zone.Id,
            variant: ResourceVariant.EquipmentStoneClub));
        save["nextEntityId"] = nextId + 1;
        engine = SimulationEngine.Load(save.ToJsonString(), definitions);

        for (var tick = 0; tick < 1_000 &&
             !Assert.Single(engine.CreateSnapshot().Actors).Equipment
                 .HasFlag(PersonalEquipment.StoneClub); tick++)
        {
            engine.AdvanceTicks(1);
        }

        var snapshot = engine.CreateSnapshot();
        var equipped = Assert.Single(snapshot.Actors).Equipment;
        Assert.True(equipped.HasFlag(PersonalEquipment.StoneClub));
        Assert.False(equipped.HasFlag(PersonalEquipment.FightingStick));
        Assert.Single(EquipmentCatalog.GetDefinitions(equipped), item =>
            item.Slot == EquipmentSlot.MainHand);
        Assert.Contains(snapshot.ItemStacks, stack =>
            stack.Resource == ResourceKind.Equipment &&
            stack.Variant == ResourceVariant.EquipmentFightingStick &&
            stack.Location.Kind == ItemLocationKind.StorageZone &&
            stack.Location.OwnerId == zone.Id &&
            stack.Quantity == 1);
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
                ~((int)PersonalEquipment.PrimitiveWaterskin |
                  (int)PersonalEquipment.WoodenAxe |
                  (int)PersonalEquipment.PrimitivePickaxe);
            actor["personalWater"] = 0;
        }
        engine = SimulationEngine.Load(save.ToJsonString(), definitions);
        Assert.DoesNotContain(engine.CreateSnapshot().Actors, actor =>
            actor.Equipment.HasFlag(PersonalEquipment.PrimitiveWaterskin));
        Assert.DoesNotContain(engine.CreateSnapshot().Actors, actor =>
            actor.Equipment.HasFlag(PersonalEquipment.WoodenAxe));
        Assert.DoesNotContain(engine.CreateSnapshot().Actors, actor =>
            actor.Equipment.HasFlag(PersonalEquipment.PrimitivePickaxe));
        engine = AddCraftingMaterials(engine, definitions, engine.Map.GoblinSpawn,
            (ResourceKind.Bone, 1),
            (ResourceKind.Wood, 12),
            (ResourceKind.Stone, 4),
            (ResourceKind.Hide, 3),
            (ResourceKind.Reeds, 8));
        foreach (var recipe in new[]
                 {
                     CraftingRecipeKind.BoneKnife,
                     CraftingRecipeKind.PrimitiveAxe,
                     CraftingRecipeKind.PrimitivePickaxe,
                     CraftingRecipeKind.FightingStick,
                     CraftingRecipeKind.StoneClub,
                     CraftingRecipeKind.HideClothes,
                     CraftingRecipeKind.ReedClothes,
                     CraftingRecipeKind.PrimitiveWaterskin,
                     CraftingRecipeKind.WoodenBucket,
                     CraftingRecipeKind.WoodenBarrel,
                 })
        {
            engine.QueueCommand(SimulationCommand.QueueCraftingRecipe(
                engine.CurrentTick.Next(), engine.NextAvailableCommandSequence, workshop, recipe));
        }

        for (var index = 0; index < 20_000 &&
             (engine.CreateSnapshot() is var current &&
              (current.CraftingOrders.Count > 0 ||
               current.ItemStacks.Any(stack =>
                   stack.Resource == ResourceKind.Equipment &&
                   stack.Location.Kind == ItemLocationKind.Ground) ||
               index == 0)); index++)
        {
            engine.AdvanceTicks(1);
        }

        var snapshot = engine.CreateSnapshot();
        Assert.Empty(snapshot.CraftingOrders);
        Assert.All(snapshot.Actors, actor =>
        {
            Assert.All(
                EquipmentCatalog.GetDefinitions(actor.Equipment).GroupBy(item => item.Slot),
                group => Assert.Single(group));
        });
        foreach (var variant in new[]
                 {
                     ResourceVariant.EquipmentBoneKnife,
                     ResourceVariant.EquipmentWoodenAxe,
                     ResourceVariant.EquipmentPrimitivePickaxe,
                     ResourceVariant.EquipmentFightingStick,
                     ResourceVariant.EquipmentStoneClub,
                     ResourceVariant.EquipmentHideClothes,
                     ResourceVariant.EquipmentReedClothes,
                     ResourceVariant.EquipmentPrimitiveWaterskin,
                     ResourceVariant.EquipmentWoodenBucket,
                 })
        {
            var definition = Assert.IsType<EquipmentItemDefinition>(
                EquipmentCatalog.FindDefinition(variant));
            Assert.True(
                snapshot.Actors.Any(actor => actor.Equipment.HasFlag(definition.Equipment)) ||
                snapshot.ItemStacks.Any(stack =>
                    stack.Resource == ResourceKind.Equipment &&
                    stack.Variant == variant &&
                    stack.Quantity > 0));
        }
        Assert.Contains(snapshot.ItemStacks, stack =>
            stack.Resource == ResourceKind.Equipment &&
            stack.Variant == ResourceVariant.EquipmentWoodenBarrel &&
            stack.Quantity == 1);
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
        EntityId storageZoneId = default,
        bool equipSling = true,
        ResourceVariant variant = ResourceVariant.None)
    {
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var actor = save["actors"]!.AsArray()[0]!.AsObject();
        if (equipSling)
        {
            actor["equipment"] = actor["equipment"]!.GetValue<int>() |
                (int)PersonalEquipment.PrimitiveSling;
        }
        var nextId = save["nextEntityId"]!.GetValue<ulong>();
        save["itemStacks"]!.AsArray().Add(CreateStack(
            nextId,
            resource,
            position,
            quantity,
            storageZoneId,
            variant));
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
        EntityId storageZoneId = default,
        ResourceVariant variant = ResourceVariant.None) => new()
    {
        ["id"] = id,
        ["resource"] = (int)resource,
        ["foodKind"] = (int)FoodKind.None,
        ["variant"] = (int)variant,
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
