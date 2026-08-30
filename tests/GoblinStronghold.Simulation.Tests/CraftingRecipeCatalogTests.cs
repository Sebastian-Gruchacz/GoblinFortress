using GoblinStronghold.Simulation.Resources;
using GoblinStronghold.Simulation.Workshops;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class CraftingRecipeCatalogTests
{
    [Fact]
    public void CatalogDefinesEveryRecipeAndItsOwningWorkshop()
    {
        Assert.Equal(Enum.GetValues<CraftingRecipeKind>().Length, CraftingRecipeCatalog.All.Count);

        foreach (var recipeKind in Enum.GetValues<CraftingRecipeKind>())
        {
            var recipe = CraftingRecipeCatalog.Get(recipeKind);
            var workshop = WorkshopCatalog.Get(recipe.Workshop);

            Assert.NotEmpty(recipe.Id);
            Assert.NotEmpty(recipe.Materials);
            Assert.True(recipe.BaseWorkTicks > 0);
            Assert.True(CraftingRecipeCatalog.GetWorkTicks(recipeKind) > 0);
            Assert.True(workshop.SupportsRecipe(recipe.Kind, recipe.Level));
        }
    }

    [Fact]
    public void RecipeOutputAndRequirementsComeFromCatalog()
    {
        var barrel = CraftingRecipeCatalog.Get("wooden-barrel");

        Assert.Equal(CraftingRecipeKind.WoodenBarrel, barrel.Kind);
        Assert.Equal(WorkshopKind.PrimitiveWorkshop, barrel.Workshop);
        Assert.Equal(
            [
                new CraftingMaterialRequirement(
                    ResourceKind.Wood,
                    ResourceVariant.None,
                    3),
                new CraftingMaterialRequirement(
                    ResourceKind.Reeds,
                    ResourceVariant.None,
                    2),
            ],
            barrel.Materials);
        Assert.Equal(
            new CraftingOutputDefinition(
                ResourceKind.Equipment,
                ResourceVariant.EquipmentWoodenBarrel,
                1),
            barrel.Output);
    }

    [Theory]
    [InlineData(CraftingRecipeKind.SmeltIronBar, WorkshopKind.Bloomery,
        ResourceVariant.IronOre, ResourceVariant.IronBar)]
    [InlineData(CraftingRecipeKind.SmeltCopperBar, WorkshopKind.SmeltingFurnace,
        ResourceVariant.CopperOre, ResourceVariant.CopperBar)]
    [InlineData(CraftingRecipeKind.SmeltSilverBar, WorkshopKind.CrucibleFurnace,
        ResourceVariant.SilverOre, ResourceVariant.SilverBar)]
    [InlineData(CraftingRecipeKind.SmeltGoldBar, WorkshopKind.CrucibleFurnace,
        ResourceVariant.GoldOre, ResourceVariant.GoldBar)]
    public void SmeltingRecipesRequireTheExactOreVariant(
        CraftingRecipeKind recipeKind,
        WorkshopKind workshopKind,
        ResourceVariant oreVariant,
        ResourceVariant barVariant)
    {
        var recipe = CraftingRecipeCatalog.Get(recipeKind);

        Assert.Equal(workshopKind, recipe.Workshop);
        Assert.Contains(recipe.Materials, material =>
            material == new CraftingMaterialRequirement(ResourceKind.Ore, oreVariant, 2));
        Assert.Contains(recipe.Materials, material =>
            material == new CraftingMaterialRequirement(
                ResourceKind.Coal,
                ResourceVariant.None,
                1));
        Assert.Equal(
            new CraftingOutputDefinition(ResourceKind.Materials, barVariant, 1),
            recipe.Output);
        Assert.Null(CraftingRecipeCatalog.FindMaterial(
            recipeKind,
            ResourceKind.Ore,
            ResourceVariant.IronOre == oreVariant
                ? ResourceVariant.CopperOre
                : ResourceVariant.IronOre));
    }
}
