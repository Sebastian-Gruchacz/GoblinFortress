using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class CraftingTests
{
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
        Assert.Contains(snapshot.Actors, actor =>
            actor.Equipment.HasFlag(PersonalEquipment.PrimitiveSling));
        Assert.DoesNotContain(snapshot.ItemStacks, stack =>
            stack.Resource is ResourceKind.Hide or ResourceKind.Bone);
        Assert.Contains(engine.DrainEvents(), simulationEvent =>
            simulationEvent.Kind == SimulationEventKind.CraftingCompleted);
        var restored = SimulationEngine.Load(engine.Save(), definitions);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
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
    public void PrimitiveWorkshopCraftsBasicToolsWeaponsAndClothes()
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
        engine = AddCraftingMaterials(engine, definitions, engine.Map.GoblinSpawn,
            (ResourceKind.Bone, 1),
            (ResourceKind.Wood, 4),
            (ResourceKind.Stone, 1),
            (ResourceKind.Hide, 2),
            (ResourceKind.Reeds, 3));
        foreach (var recipe in new[]
                 {
                     CraftingRecipeKind.BoneKnife,
                     CraftingRecipeKind.FightingStick,
                     CraftingRecipeKind.StoneClub,
                     CraftingRecipeKind.HideClothes,
                     CraftingRecipeKind.ReedClothes,
                 })
        {
            engine.QueueCommand(SimulationCommand.QueueCraftingRecipe(
                engine.CurrentTick.Next(), engine.NextAvailableCommandSequence, workshop, recipe));
        }

        for (var index = 0; index < 20_000 &&
             (engine.CreateSnapshot().CraftingOrders.Count > 0 || index == 0); index++)
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
    }

    private static SimulationEngine AddCraftingMaterials(
        SimulationEngine engine,
        SimulationDefinitions definitions,
        GridPosition position,
        params (ResourceKind Resource, int Quantity)[] materials)
    {
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var nextId = save["nextEntityId"]!.GetValue<ulong>();
        var stacks = save["itemStacks"]!.AsArray();
        materials = materials.Length == 0
            ? [(ResourceKind.Hide, 1), (ResourceKind.Bone, 1)]
            : materials;
        foreach (var material in materials)
        {
            stacks.Add(CreateStack(nextId++, material.Resource, position, material.Quantity));
        }
        save["nextEntityId"] = nextId;
        return SimulationEngine.Load(save.ToJsonString(), definitions);
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
