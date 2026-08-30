using GoblinStronghold.Simulation.Localization;
using GoblinStronghold.Simulation.Resources;
using GoblinStronghold.Simulation.Workshops;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class WorkshopCatalogTests
{
    [Fact]
    public void EveryWorkshopHasMaterialAndRecipeLimitsWithoutDisplayText()
    {
        Assert.Equal(Enum.GetValues<WorkshopKind>().Length, WorkshopCatalog.All.Count);
        Assert.All(WorkshopCatalog.All, workshop =>
        {
            Assert.NotEmpty(workshop.ConstructionRequirements);
            Assert.InRange(workshop.WorkSpeedPercent, 1, 1_000);
            Assert.True(workshop.MaximumRecipeLevel > 0);
            Assert.NotEmpty(workshop.ServedMaterialTypes);
        });

        var primitive = WorkshopCatalog.Get(WorkshopKind.PrimitiveWorkshop);
        Assert.All(Enum.GetValues<CraftingRecipeKind>().Where(recipe =>
                recipe < CraftingRecipeKind.SmeltIronBar), recipe =>
            Assert.True(primitive.SupportsRecipe(recipe, recipeLevel: 1)));
        Assert.False(primitive.SupportsRecipe(CraftingRecipeKind.SmeltIronBar, recipeLevel: 1));
    }

    [Fact]
    public void SmeltersServeOnlyTheirConfiguredMaterialOutputs()
    {
        var iron = MaterialCatalog.Get(ResourceVariant.IronBar);
        var copper = MaterialCatalog.Get(ResourceVariant.CopperBar);
        var silver = MaterialCatalog.Get(ResourceVariant.SilverBar);
        var gold = MaterialCatalog.Get(ResourceVariant.GoldBar);

        Assert.True(WorkshopCatalog.Get(WorkshopKind.Bloomery).SupportsProcessing(iron));
        Assert.False(WorkshopCatalog.Get(WorkshopKind.Bloomery).SupportsProcessing(copper));
        Assert.True(WorkshopCatalog.Get(WorkshopKind.SmeltingFurnace)
            .SupportsProcessing(copper));
        Assert.True(WorkshopCatalog.Get(WorkshopKind.CrucibleFurnace)
            .SupportsProcessing(silver));
        Assert.True(WorkshopCatalog.Get(WorkshopKind.CrucibleFurnace)
            .SupportsProcessing(gold));
    }

    [Fact]
    public void PolishAndEnglishTranslationsAreSeparateFromGameplayDefinitions()
    {
        Assert.Equal(["en", "pl"], TranslationCatalog.SupportedLocales);
        Assert.Equal(
            "oak wood",
            TranslationCatalog.Get("en-US", "materials", "names", "oak-wood"));
        Assert.Equal(
            "drewno dębowe",
            TranslationCatalog.Get("pl-PL", "materials", "names", "oak-wood"));
        Assert.Equal(
            "crucible furnace",
            TranslationCatalog.Get("en", "workshops", "names", "crucible-furnace"));
        Assert.Equal(
            "piec tyglowy",
            TranslationCatalog.Get("pl", "workshops", "names", "crucible-furnace"));
        Assert.Equal(
            "drewniana beczka",
            TranslationCatalog.Get("pl", "recipes", "names", "WoodenBarrel"));
    }
}
