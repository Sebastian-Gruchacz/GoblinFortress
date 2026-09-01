using GoblinStronghold.Simulation.Animals;
using GoblinStronghold.Simulation.ContentPacks;
using GoblinStronghold.Simulation.Localization;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

[Collection(TranslationCatalogCollection.Name)]
public sealed class AnimalSpeciesCatalogTests
{
    [Fact]
    public void CoreCatalogDefinesEveryLegacyAnimalKindWithStableIdentity()
    {
        Assert.Equal(
            Enum.GetValues<AnimalKind>(),
            AnimalSpeciesCatalog.Core.All.Select(definition => definition.LegacyKind));

        var wyrm = AnimalSpeciesCatalog.Core.Get(AnimalKind.MagmaWyrm);
        Assert.Equal("core:magma-wyrm", wyrm.Id.Value);
        Assert.Same(wyrm, AnimalSpeciesCatalog.Core.Get(wyrm.Id));
        Assert.Equal(2_400, wyrm.Vitals.MaximumHealth);
        Assert.Equal(30, wyrm.Vitals.MaximumFatigue);
        Assert.Equal(AnimalHabitatKind.Cave, wyrm.Habitat.Kind);
        Assert.Equal(16, wyrm.Habitat.MinimumDepthBelowSurface);
        Assert.Equal(AnimalDisposition.Aggressive, wyrm.Behavior.Disposition);
        Assert.Equal("core:aggressive-predator", wyrm.Behavior.ModelId.Value);
        Assert.Equal(100, wyrm.Behavior.Aggression);
        Assert.Equal(300, wyrm.Harvest.ForagingExperience);
        Assert.Equal(5, wyrm.DebugVisionRadius);
        Assert.Equal(AnimalSpawnMode.MaintainEachDepth, wyrm.Spawn.Mode);
        Assert.Equal("core:underground-fauna", wyrm.Visual.AtlasId!.Value.Value);
        Assert.Equal("#f29a3f", wyrm.Visual.Palette["highlight"]);
    }

    [Fact]
    public void EveryCoreAnimalSpeciesHasEnglishAndPolishDisplayNames()
    {
        foreach (var species in AnimalSpeciesCatalog.Core.All)
        {
            var key = species.LegacyKind.ToString();
            Assert.True(TranslationCatalog.TryGet(
                "en", "interface", "animal-kinds", key, out var english));
            Assert.True(TranslationCatalog.TryGet(
                "pl", "interface", "animal-kinds", key, out var polish));
            Assert.False(string.IsNullOrWhiteSpace(english));
            Assert.False(string.IsNullOrWhiteSpace(polish));
        }
    }

    [Fact]
    public void CatalogRejectsMissingOrDuplicatedLegacyAdapters()
    {
        var hare = new AnimalSpeciesDefinition(
            ContentId.Parse("marsh-hare"),
            AnimalKind.MarshHare,
            new(100, 6, 6, 1),
            new(AnimalHabitatKind.FertileGround, 0),
            new(
                ContentId.Parse("core:passive-prey"),
                AnimalDisposition.Passive,
                Aggression: 0,
                DetectionRadius: 5,
                ForageHungerThreshold: 6,
                StarvationHungerThreshold: 24,
                RoamingInterval: 2,
                [new(AnimalEnemySelectorKind.Group, ContentId.Parse("core:goblins"))]),
            new(
                AnimalSpawnMode.InitialSingleLevel,
                Order: 10,
                MinimumDepth: 0,
                MaximumDepth: 0,
                MinimumPopulation: 6,
                MapCellsPerAnimal: 400,
                ScalePopulationWithDepth: false,
                PopulationIncreaseDepth: null,
                PopulationIncrease: 0),
            AnimalEcologyProfile.MarshHare,
            new(0, 0),
            new(3, 1, 1, 12, 120, []),
            DebugVisionRadius: 2,
            new(
                ContentId.Parse("core:procedural-hare"),
                AtlasId: null,
                ContentId.Parse("core:marsh-hare"),
                new Dictionary<string, string> { ["body"] = "#b9aa86" }));

        Assert.Throws<InvalidDataException>(() => new AnimalSpeciesCatalog([hare]));
        Assert.Throws<InvalidDataException>(() => new AnimalSpeciesCatalog(
            AnimalSpeciesCatalog.Core.All.Append(hare)));
    }

    [Fact]
    public void SpeciesDefinitionsOwnCombatAndHarvestDifferences()
    {
        var spider = AnimalSpeciesCatalog.Core.Get(AnimalKind.CaveSpider);
        var wyrm = AnimalSpeciesCatalog.Core.Get(AnimalKind.MagmaWyrm);

        Assert.Equal(445, AnimalAttackPolicy.GetDamage(
            spider, new GridPosition(0, 0, -16)));
        Assert.Equal(1_400, AnimalAttackPolicy.GetDamage(
            wyrm, new GridPosition(0, 0, -16)));
        Assert.Equal(3, spider.Harvest.Byproducts.Count);
        Assert.Contains(spider.Harvest.Byproducts, item =>
            item.Variant == ResourceVariant.SpiderChitin && item.Quantity == 3);
        Assert.Empty(wyrm.Harvest.Byproducts);
        Assert.Equal(
            AnimalEcologyProfile.CaveSpider,
            AnimalSpeciesCatalog.Core.Get(AnimalKind.DeepCrawler).EcologyProfile);
    }
}
