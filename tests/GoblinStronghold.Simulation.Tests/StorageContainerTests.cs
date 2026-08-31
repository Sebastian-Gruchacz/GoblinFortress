using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class StorageContainerTests
{
    [Theory]
    [InlineData(StorageProviderKind.WoodenBox, ResourceVariant.EquipmentWoodenBox, 32, 4, 8)]
    [InlineData(StorageProviderKind.WoodenChest, ResourceVariant.EquipmentWoodenChest, 64, 8, 8)]
    [InlineData(StorageProviderKind.WoodenBulkBin, ResourceVariant.EquipmentWoodenBulkBin, 64, 1, 64)]
    public void CraftedContainerBecomesPhysicalProviderInsideArea(
        StorageProviderKind providerKind,
        ResourceVariant requiredVariant,
        int capacity,
        int slotCount,
        int stackCapacity)
    {
        var definitions = SimulationDefinitions.Foundation;
        var engine = SimulationEngine.Create(
            new WorldSeed(0x434F4E5441494E45UL + (ulong)providerKind),
            definitions,
            initialGoblinCount: 2,
            initialFoodStock: 8,
            initialWoodStock: 0);
        var target = engine.Map.GetCardinalNeighbors(engine.Map.GoblinSpawn)
            .First(engine.World.IsTerrainTraversable);
        engine.QueueCommand(SimulationCommand.CreateStorageArea(
            engine.CurrentTick.Next(), engine.NextAvailableCommandSequence, target, target));
        engine.AdvanceTicks(1);
        var area = Assert.Single(engine.CreateSnapshot().StorageAreas);

        engine.QueueCommand(CreatePlacement(
            providerKind,
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            target));
        engine.AdvanceTicks(1);
        var site = Assert.Single(engine.CreateSnapshot().ConstructionSites);
        var material = Assert.Single(site.Materials);
        Assert.Equal(ResourceKind.Equipment, material.Resource);
        Assert.Equal(requiredVariant, material.Variant);
        Assert.Equal(1, material.RequiredQuantity);

        engine = AddEquipmentStack(engine, definitions, requiredVariant, engine.Map.GoblinSpawn);
        for (var tick = 0; tick < 5_000 && engine.CreateSnapshot().StorageZones.Count == 0; tick++)
        {
            engine.AdvanceTicks(1);
        }

        var snapshot = engine.CreateSnapshot();
        var provider = Assert.Single(snapshot.StorageZones);
        Assert.Equal(providerKind, provider.ProviderKind);
        Assert.Equal(ResourceKind.Any, provider.AcceptedResource);
        Assert.Equal(capacity, provider.Capacity);
        Assert.Equal(slotCount, provider.TypeSlotCount);
        Assert.Equal(stackCapacity, provider.StackCapacity);
        Assert.True(provider.SeparatesItemTypes);
        Assert.Equal(StorageCapability.SolidGoods, provider.Capabilities);
        Assert.Equal(area.Id, provider.StorageAreaId);
        Assert.Equal([provider.Id], Assert.Single(snapshot.StorageAreas).StorageZoneIds);

        var restored = SimulationEngine.Load(engine.Save(), definitions);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(providerKind, Assert.Single(restored.CreateSnapshot().StorageZones).ProviderKind);
    }

    [Fact]
    public void PrimitiveWorkshopCatalogProvidesAllSolidContainerRecipes()
    {
        Assert.Equal(
            ResourceVariant.EquipmentWoodenBox,
            CraftingRecipeCatalog.Get(CraftingRecipeKind.WoodenBox).Output.Variant);
        Assert.Equal(
            ResourceVariant.EquipmentWoodenChest,
            CraftingRecipeCatalog.Get(CraftingRecipeKind.WoodenChest).Output.Variant);
        Assert.Equal(
            ResourceVariant.EquipmentWoodenBulkBin,
            CraftingRecipeCatalog.Get(CraftingRecipeKind.WoodenBulkBin).Output.Variant);
    }

    private static SimulationCommand CreatePlacement(
        StorageProviderKind providerKind,
        SimulationTick tick,
        ulong sequence,
        GridPosition position) => providerKind switch
    {
        StorageProviderKind.WoodenBox =>
            SimulationCommand.PlaceWoodenBox(tick, sequence, position),
        StorageProviderKind.WoodenChest =>
            SimulationCommand.PlaceWoodenChest(tick, sequence, position),
        StorageProviderKind.WoodenBulkBin =>
            SimulationCommand.PlaceWoodenBulkBin(tick, sequence, position),
        _ => throw new ArgumentOutOfRangeException(nameof(providerKind)),
    };

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
}
