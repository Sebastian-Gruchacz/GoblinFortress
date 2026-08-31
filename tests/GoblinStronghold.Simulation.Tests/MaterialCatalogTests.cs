using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class MaterialCatalogTests
{
    [Fact]
    public void CatalogContainsEveryGeneratedNaturalMaterialVariant()
    {
        ResourceVariant[] expected =
        [
            ResourceVariant.OakWood,
            ResourceVariant.ChestnutWood,
            ResourceVariant.BirchWood,
            ResourceVariant.WalnutWood,
            ResourceVariant.AppleWood,
            ResourceVariant.PineWood,
            ResourceVariant.Sandstone,
            ResourceVariant.Granite,
            ResourceVariant.Basalt,
            ResourceVariant.Obsidian,
            ResourceVariant.IronOre,
            ResourceVariant.CopperOre,
            ResourceVariant.SilverOre,
            ResourceVariant.GoldOre,
            ResourceVariant.Ruby,
            ResourceVariant.Emerald,
            ResourceVariant.Diamond,
            ResourceVariant.IronBar,
            ResourceVariant.CopperBar,
            ResourceVariant.SilverBar,
            ResourceVariant.GoldBar,
            ResourceVariant.SpiderVenom,
            ResourceVariant.SpiderSilk,
            ResourceVariant.SpiderChitin,
        ];

        Assert.All(expected, variant =>
        {
            var material = MaterialCatalog.Get(variant);
            Assert.Equal(variant, material.Variant);
            Assert.Equal(3, material.Palette.KeyColors.Count);
        });
    }

    [Fact]
    public void CatalogHasUniqueIdsAndSensiblePhysicalRanges()
    {
        Assert.Equal(
            MaterialCatalog.All.Count,
            MaterialCatalog.All.Select(material => material.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
        Assert.All(MaterialCatalog.All, material =>
        {
            Assert.True(material.UnitWeight > 0);
            Assert.InRange(material.Strength, 0, 100);
            Assert.InRange(material.Hardness, 0, 100);
            Assert.InRange(material.Durability, 0, 100);
            Assert.InRange(material.Flexibility, 0, 100);
            Assert.InRange(material.AcquisitionDifficulty, 0, 100);
            if (material.Uses.Count == 0)
            {
                Assert.Contains(material.MaterialType,
                    new[] { MaterialType.Venom, MaterialType.Silk, MaterialType.Chitin });
            }
            if (material.Occurrence is { } occurrence)
            {
                Assert.True(occurrence.MinimumDepthBelowSurface >= 0);
                Assert.True(
                    occurrence.MaximumDepthBelowSurface is null ||
                    occurrence.MaximumDepthBelowSurface >=
                        occurrence.MinimumDepthBelowSurface);
            }
        });
    }

    [Fact]
    public void DeepMaterialsExposeTheirIntendedDepthBands()
    {
        Assert.Equal(8, MaterialCatalog.Get(ResourceVariant.Basalt)
            .Occurrence!.MinimumDepthBelowSurface);
        Assert.Equal(16, MaterialCatalog.Get(ResourceVariant.Obsidian)
            .Occurrence!.MinimumDepthBelowSurface);
        Assert.Equal(16, MaterialCatalog.Get(ResourceVariant.Diamond)
            .Occurrence!.MinimumDepthBelowSurface);
        Assert.Equal(ResourceKind.Materials,
            MaterialCatalog.Get(ResourceVariant.Diamond).ResourceKind);
    }

    [Fact]
    public void MetalBarsLinkOreAndFuelToTypedSmelters()
    {
        var iron = MaterialCatalog.Get(ResourceVariant.IronBar);
        Assert.Equal(MaterialType.Metal, iron.MaterialType);
        Assert.Equal(MaterialAcquisitionStrategy.Processing, iron.Acquisition.Strategy);
        Assert.Equal(MaterialProcessorKind.Bloomery, iron.Processing!.Processor);
        Assert.Contains(iron.Processing.Inputs,
            input => input == new MaterialIngredient("iron-ore", 2));
        Assert.Contains(iron.Processing.Inputs,
            input => input == new MaterialIngredient("coal", 1));

        var gold = MaterialCatalog.Get(ResourceVariant.GoldBar);
        Assert.Equal(MaterialProcessorKind.CrucibleFurnace, gold.Processing!.Processor);
        Assert.Equal(2, gold.Processing.MinimumProcessorLevel);
    }

    [Fact]
    public void CapabilityLookupSupportsMaterialSubstitution()
    {
        var toolHeads = MaterialCatalog.Supporting(MaterialUse.ToolHead);

        Assert.Contains(toolHeads, material => material.Id == "granite");
        Assert.Contains(toolHeads, material => material.Id == "iron-bar");
        Assert.Contains(toolHeads, material => material.Id == "diamond");
        Assert.DoesNotContain(toolHeads, material => material.Id == "coal");
        Assert.Equal("reeds", MaterialCatalog.Get(ResourceKind.Reeds).Id);
    }

    [Fact]
    public void SpiderByproductsAreFutureMaterialsWithoutCurrentUses()
    {
        ResourceVariant[] variants =
        [
            ResourceVariant.SpiderVenom,
            ResourceVariant.SpiderSilk,
            ResourceVariant.SpiderChitin,
        ];

        Assert.All(variants, variant =>
        {
            var material = MaterialCatalog.Get(variant);
            Assert.Equal(ResourceKind.Materials, material.ResourceKind);
            Assert.Equal(MaterialAcquisitionStrategy.Butchering, material.Acquisition.Strategy);
            Assert.Empty(material.Uses);
        });
    }
}
