using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using GoblinStronghold.Simulation.Terrain;
using GoblinStronghold.Simulation.Terrain.Jobs;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class TerrainWorkYieldPolicyTests
{
    [Theory]
    [InlineData(MineralDepositKind.Coal, ResourceKind.Coal, ResourceVariant.None, 3)]
    [InlineData(MineralDepositKind.IronOre, ResourceKind.Ore, ResourceVariant.IronOre, 2)]
    [InlineData(MineralDepositKind.CopperOre, ResourceKind.Ore, ResourceVariant.CopperOre, 2)]
    [InlineData(MineralDepositKind.SilverOre, ResourceKind.Ore, ResourceVariant.SilverOre, 2)]
    [InlineData(MineralDepositKind.GoldOre, ResourceKind.Ore, ResourceVariant.GoldOre, 2)]
    [InlineData(MineralDepositKind.Ruby, ResourceKind.Materials, ResourceVariant.Ruby, 1)]
    [InlineData(MineralDepositKind.Emerald, ResourceKind.Materials, ResourceVariant.Emerald, 1)]
    [InlineData(MineralDepositKind.Diamond, ResourceKind.Materials, ResourceVariant.Diamond, 1)]
    public void MiningMapsEveryDepositAndKeepsItsQuantityRange(
        MineralDepositKind deposit,
        ResourceKind resource,
        ResourceVariant variant,
        int maximumQuantity)
    {
        var yield = Create(WorkDesignationKind.MineRock, RockKind.Granite, deposit);

        Assert.Equal(2, yield.Stacks.Count);
        Assert.Equal(
            new TerrainYieldStack(ResourceKind.Stone, ResourceVariant.Granite,
                yield.Stacks[0].Quantity),
            yield.Stacks[0]);
        Assert.InRange(yield.Stacks[0].Quantity, 1, 3);
        Assert.Equal(resource, yield.Stacks[1].Resource);
        Assert.Equal(variant, yield.Stacks[1].Variant);
        Assert.InRange(yield.Stacks[1].Quantity, 1, maximumQuantity);
        Assert.Equal(Math.Max(12, yield.Stacks[0].Quantity * 2), yield.BuildingExperience);
    }

    [Fact]
    public void RampProducesOnlyStoneWithItsExistingRangeAndExperience()
    {
        var yield = Create(
            WorkDesignationKind.CarveRampDown,
            RockKind.Obsidian,
            MineralDepositKind.None);

        var stack = Assert.Single(yield.Stacks);
        Assert.Equal(ResourceKind.Stone, stack.Resource);
        Assert.Equal(ResourceVariant.Obsidian, stack.Variant);
        Assert.InRange(stack.Quantity, 6, 10);
        Assert.Equal(Math.Max(20, stack.Quantity * 3), yield.BuildingExperience);
    }

    [Theory]
    [InlineData(LooseMaterialKind.Soil, ResourceKind.Earth, ResourceVariant.Soil)]
    [InlineData(LooseMaterialKind.Sand, ResourceKind.Sand, ResourceVariant.Sand)]
    public void ExcavatingLooseStrataProducesItsPhysicalMaterial(
        LooseMaterialKind material,
        ResourceKind resource,
        ResourceVariant variant)
    {
        var yield = TerrainWorkYieldPolicy.Create(
            TerrainModificationCatalog.Get(WorkDesignationKind.MineRock),
            new CaveCell(
                RockKind.Sandstone,
                CaveCellKind.SolidRock,
                MineralDepositKind.None,
                LooseMaterial: material),
            new WorldSeed(123),
            new EntityId(7),
            new SimulationTick(11),
            new EntityId(19));

        var stack = Assert.Single(yield.Stacks);
        Assert.Equal(resource, stack.Resource);
        Assert.Equal(variant, stack.Variant);
        Assert.InRange(stack.Quantity, 1, 3);
        Assert.Equal(Math.Max(12, stack.Quantity * 2), yield.BuildingExperience);
    }

    [Fact]
    public void SameInputsProduceTheSameYield()
    {
        var first = Create(
            WorkDesignationKind.MineRock,
            RockKind.Basalt,
            MineralDepositKind.GoldOre);
        var second = Create(
            WorkDesignationKind.MineRock,
            RockKind.Basalt,
            MineralDepositKind.GoldOre);

        Assert.Equal(first.BuildingExperience, second.BuildingExperience);
        Assert.Equal(first.Stacks, second.Stacks);
    }

    private static TerrainWorkYield Create(
        WorkDesignationKind designation,
        RockKind rock,
        MineralDepositKind deposit) => TerrainWorkYieldPolicy.Create(
            TerrainModificationCatalog.Get(designation),
            rock,
            deposit,
            new WorldSeed(123),
            new EntityId(7),
            new SimulationTick(11),
            new EntityId(19));
}
