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
        Assert.Equal(WorkshopKind.FittedWorkshop, barrel.Workshop);
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
    [InlineData(CraftingRecipeKind.ReinforcedPickaxe)]
    [InlineData(CraftingRecipeKind.WoodenBarrel)]
    [InlineData(CraftingRecipeKind.WoodenChest)]
    [InlineData(CraftingRecipeKind.WoodenBulkBin)]
    public void AdvancedToolsAndContainersRequireTheFittedWorkshop(
        CraftingRecipeKind recipeKind)
    {
        var recipe = CraftingRecipeCatalog.Get(recipeKind);

        Assert.Equal(WorkshopKind.FittedWorkshop, recipe.Workshop);
        Assert.Equal(2, recipe.Level);
    }

    [Fact]
    public void LegacyAndStableRecipeIdsResolveToTheSameDefinition()
    {
        var legacy = CraftingRecipeCatalog.Get("wooden-barrel");
        var stable = CraftingRecipeCatalog.Get("core:wooden-barrel");

        Assert.Same(legacy, stable);
        Assert.Equal("core:wooden-barrel", legacy.StableId.Value);
    }

    [Fact]
    public void PrimitiveAxeUsesWoodStoneBindingAndProducesPhysicalAxe()
    {
        var axe = CraftingRecipeCatalog.Get(CraftingRecipeKind.PrimitiveAxe);

        Assert.Equal(WorkshopKind.PrimitiveWorkshop, axe.Workshop);
        Assert.Equal(
            [
                new CraftingMaterialRequirement(ResourceKind.Wood, ResourceVariant.None, 2),
                new CraftingMaterialRequirement(ResourceKind.Stone, ResourceVariant.None, 1),
                new CraftingMaterialRequirement(ResourceKind.Reeds, ResourceVariant.None, 1),
            ],
            axe.Materials);
        Assert.Equal(
            new CraftingOutputDefinition(
                ResourceKind.Equipment,
                ResourceVariant.EquipmentWoodenAxe,
                1),
            axe.Output);
    }

    [Fact]
    public void PrimitivePickaxeUsesWoodStoneBindingAndProducesPhysicalPickaxe()
    {
        var pickaxe = CraftingRecipeCatalog.Get(CraftingRecipeKind.PrimitivePickaxe);

        Assert.Equal(WorkshopKind.PrimitiveWorkshop, pickaxe.Workshop);
        Assert.Equal(
            [
                new CraftingMaterialRequirement(ResourceKind.Wood, ResourceVariant.None, 2),
                new CraftingMaterialRequirement(ResourceKind.Stone, ResourceVariant.None, 2),
                new CraftingMaterialRequirement(ResourceKind.Reeds, ResourceVariant.None, 1),
            ],
            pickaxe.Materials);
        Assert.Equal(
            new CraftingOutputDefinition(
                ResourceKind.Equipment,
                ResourceVariant.EquipmentPrimitivePickaxe,
                1),
            pickaxe.Output);
    }

    [Fact]
    public void CookingFireConsumesRawMeatAndWoodAndProducesCookedMeat()
    {
        var recipe = CraftingRecipeCatalog.Get(CraftingRecipeKind.CookRawMeat);

        Assert.Equal(WorkshopKind.CookingFire, recipe.Workshop);
        Assert.Contains(recipe.Materials, material => material ==
            new CraftingMaterialRequirement(
                ResourceKind.Food,
                ResourceVariant.None,
                2,
                FoodKind.RawMeat));
        Assert.Contains(recipe.Materials, material => material ==
            new CraftingMaterialRequirement(ResourceKind.Wood, ResourceVariant.None, 1));
        Assert.Equal(
            new CraftingOutputDefinition(
                ResourceKind.Food,
                ResourceVariant.None,
                2,
                FoodKind.CookedMeat),
            recipe.Output);
        Assert.Null(CraftingRecipeCatalog.FindMaterial(
            recipe.Kind,
            ResourceKind.Food,
            FoodKind.Berries,
            ResourceVariant.None));
    }

    [Theory]
    [InlineData(CraftingRecipeKind.FishRootSoup, FoodKind.Fish, FoodKind.EdibleRoots,
        FoodKind.CampSoup, 2)]
    [InlineData(CraftingRecipeKind.FishMushroomSoup, FoodKind.Fish, FoodKind.Mushrooms,
        FoodKind.CampSoup, 2)]
    [InlineData(CraftingRecipeKind.MeatRootSoup, FoodKind.RawMeat, FoodKind.EdibleRoots,
        FoodKind.CampSoup, 2)]
    [InlineData(CraftingRecipeKind.MeatMushroomSoup, FoodKind.RawMeat, FoodKind.Mushrooms,
        FoodKind.CampSoup, 2)]
    [InlineData(CraftingRecipeKind.PreserveFishAndMeat, FoodKind.Fish, FoodKind.RawMeat,
        FoodKind.DriedRations, 2)]
    [InlineData(CraftingRecipeKind.BrewRootAndBerryMedicine, FoodKind.EdibleRoots,
        FoodKind.Berries, FoodKind.Medicine, 1)]
    public void PrimitiveCookingRecipesRetainExactIngredientIdentity(
        CraftingRecipeKind recipeKind,
        FoodKind firstIngredient,
        FoodKind secondIngredient,
        FoodKind outputKind,
        int outputQuantity)
    {
        var recipe = CraftingRecipeCatalog.Get(recipeKind);

        Assert.Equal(WorkshopKind.CookingFire, recipe.Workshop);
        Assert.Contains(recipe.Materials, material =>
            material.Resource == ResourceKind.Food && material.FoodKind == firstIngredient);
        Assert.Contains(recipe.Materials, material =>
            material.Resource == ResourceKind.Food && material.FoodKind == secondIngredient);
        Assert.Contains(recipe.Materials, material =>
            material.Resource == ResourceKind.Wood && material.Quantity == 1);
        Assert.Equal(ResourceKind.Food, recipe.Output.Resource);
        Assert.Equal(outputKind, recipe.Output.FoodKind);
        Assert.Equal(outputQuantity, recipe.Output.Quantity);
    }

    [Fact]
    public void ManaRecipeConsumesLichenAndMushroomsWithoutFuel()
    {
        var recipe = CraftingRecipeCatalog.Get(
            CraftingRecipeKind.BrewLichenAndMushroomMana);

        Assert.Equal(WorkshopKind.CookingFire, recipe.Workshop);
        Assert.Contains(recipe.Materials, material =>
            material.Resource == ResourceKind.Materials &&
            material.Variant == ResourceVariant.Lichen &&
            material.Quantity == 1);
        Assert.Contains(recipe.Materials, material =>
            material.Resource == ResourceKind.Food &&
            material.FoodKind == FoodKind.Mushrooms &&
            material.Quantity == 1);
        Assert.DoesNotContain(recipe.Materials, material =>
            material.Resource == ResourceKind.Wood);
        Assert.Equal(ResourceKind.Materials, recipe.Output.Resource);
        Assert.Equal(ResourceVariant.Mana, recipe.Output.Variant);
        Assert.Equal(1, recipe.Output.Quantity);
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
