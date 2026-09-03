using GoblinStronghold.Simulation.Localization;
using GoblinStronghold.Simulation.Resources;
using GoblinStronghold.Simulation.Workshops;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

[Collection(TranslationCatalogCollection.Name)]
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
                CraftingRecipeCatalog.Get(recipe).Workshop == WorkshopKind.PrimitiveWorkshop), recipe =>
            Assert.True(primitive.SupportsRecipe(recipe, recipeLevel: 1)));
        Assert.False(primitive.SupportsRecipe(CraftingRecipeKind.SmeltIronBar, recipeLevel: 1));

        var cookingFire = WorkshopCatalog.Get(WorkshopKind.CookingFire);
        Assert.All(Enum.GetValues<CraftingRecipeKind>().Where(recipe =>
                CraftingRecipeCatalog.Get(recipe).Workshop == WorkshopKind.CookingFire), recipe =>
            Assert.True(cookingFire.SupportsRecipe(recipe, recipeLevel: 1)));
        Assert.False(cookingFire.SupportsRecipe(CraftingRecipeKind.BoneKnife, recipeLevel: 1));

        var fitted = WorkshopCatalog.Get(WorkshopKind.FittedWorkshop);
        Assert.True(fitted.SupportsRecipe(CraftingRecipeKind.ReinforcedPickaxe, recipeLevel: 2));
        Assert.True(fitted.SupportsRecipe(CraftingRecipeKind.WoodenChest, recipeLevel: 2));
        Assert.False(fitted.SupportsRecipe(CraftingRecipeKind.PrimitiveAxe, recipeLevel: 1));
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
        Assert.Equal("en", TranslationCatalog.NormalizeLocale("en-EN"));
        Assert.Equal("pl", TranslationCatalog.NormalizeLocale("pl-PL"));
        Assert.Equal("en", TranslationCatalog.NormalizeLocale("de-DE"));
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
            "palenisko",
            TranslationCatalog.Get("pl", "workshops", "names", "cooking-fire"));
        Assert.Equal(
            "cook meat",
            TranslationCatalog.Get("en", "recipes", "names", "CookRawMeat"));
        Assert.Equal(
            "drewniana beczka",
            TranslationCatalog.Get("pl", "recipes", "names", "WoodenBarrel"));
        Assert.Equal(
            "wooden hammer",
            TranslationCatalog.Get("en", "recipes", "names", "WoodenHammer"));
        Assert.Equal(
            "drewniany młotek",
            TranslationCatalog.Get("pl", "interface", "equipment-names",
                "EquipmentWoodenHammer"));
        Assert.Equal(
            "tool",
            TranslationCatalog.Get("en", "interface", "equipment-slots", "Tool"));
        Assert.Equal(
            "broń biała",
            TranslationCatalog.Get("pl", "interface", "equipment-slots", "MeleeWeapon"));
        Assert.Equal(
            "cloak",
            TranslationCatalog.Get("en", "interface", "equipment-slots", "Cloak"));
        Assert.Equal(
            "nogi",
            TranslationCatalog.Get("pl", "interface", "equipment-slots", "Legs"));
        Assert.Equal(
            "narzędzia na pasie",
            TranslationCatalog.Get("pl", "interface", "equipment-paper-doll", "belt"));
        Assert.Equal(
            "Mana: {0}/{1}",
            TranslationCatalog.Get("en", "interface", "goblin-roster", "mana"));
        Assert.Equal(
            "Odżywienie: {0}/{1}",
            TranslationCatalog.Get("pl", "interface", "goblin-roster", "nutrition"));
        Assert.Equal(
            "Keyboard shortcuts",
            TranslationCatalog.Get("en", "interface", "options", "shortcuts"));
        Assert.Equal(
            "Skróty klawiaturowe",
            TranslationCatalog.Get("pl", "interface", "options", "shortcuts"));
    }
}
