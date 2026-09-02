using GoblinStronghold.Simulation.Civilizations;
using GoblinStronghold.Simulation.Civilizations.Naming;
using GoblinStronghold.Simulation.ContentPacks;
using GoblinStronghold.Simulation.Map;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

[Collection(TranslationCatalogCollection.Name)]
public sealed class CivilizationCatalogTests
{
    [Fact]
    public void CoreCatalogDefinesCurrentCivilizationRoles()
    {
        var catalog = CivilizationCatalog.Core;

        var goblins = catalog.Get(CivilizationLegacyRole.PlayerGoblins);
        var humans = catalog.Get(CivilizationLegacyRole.HumanVillage);
        var dwarves = catalog.Get(CivilizationLegacyRole.DeepDwarfClan);

        Assert.Equal(ContentId.Parse("core:goblin-tribe"), goblins.Id);
        Assert.True(goblins.PlayerControllable);
        Assert.Equal(
            ContentId.Parse("core:goblin-syllables"),
            goblins.Identity.NameGeneratorId);
        Assert.Equal(10_000, goblins.Vitals!.MaximumHealth);
        Assert.Equal(260, goblins.Combat!.MinimumMeleeDamage);
        Assert.Equal(160, goblins.Combat.MeleeDamageVariance);
        Assert.Equal(
            new CivilizationPerceptionDefinition(4, 3, 3),
            goblins.Perception);
        Assert.Equal(
            new CivilizationSpatialBehaviorDefinition(10, 0, 1),
            goblins.SpatialBehavior);
        Assert.Equal(114_000, goblins.Needs!.MaximumHunger);
        Assert.Equal(34_200, goblins.Needs.MaximumThirst);
        Assert.Equal(17_100, goblins.Needs.MaximumFatigue);
        Assert.Equal(5, goblins.Aging!.HealthyYears);
        Assert.Equal(
            [GoblinSkill.Foraging, GoblinSkill.Hauling, GoblinSkill.Survival,
                GoblinSkill.Scouting, GoblinSkill.Building],
            goblins.ActorGeneration!.SkillPool);
        Assert.Equal([3UL, 4UL], goblins.ActorGeneration.SkillSampleKeys);
        Assert.Equal(
            PersonalEquipment.RagClothes | PersonalEquipment.PrimitiveWaterskin,
            goblins.ActorGeneration.GuaranteedEquipment);
        Assert.Equal(ContentId.Parse("core:human-village"), humans.Id);
        Assert.False(humans.PlayerControllable);
        Assert.Equal(
            ContentId.Parse("core:human-frontier-names"),
            humans.Identity.NameGeneratorId);
        Assert.Equal(6_000, humans.Vitals!.MaximumHealth);
        Assert.Equal(320, humans.Combat!.MinimumMeleeDamage);
        Assert.Equal(180, humans.Combat.MeleeDamageVariance);
        Assert.Equal(
            new CivilizationPerceptionDefinition(4, 3, 0),
            humans.Perception);
        Assert.Equal(
            new CivilizationSpatialBehaviorDefinition(20, 8, 0),
            humans.SpatialBehavior);
        Assert.Equal(1_000, humans.PopulationNeeds!.MaximumNeed);
        Assert.Equal(1_000, humans.PopulationNeeds.MaximumFatigue);
        Assert.Null(humans.Aging);
        Assert.Equal(ContentId.Parse("core:cave-dwarf-clan"), dwarves.Id);
        Assert.Equal(
            UndergroundFactionKind.DarkDwarves,
            dwarves.UndergroundGeneration!.LegacyKind);
        Assert.Same(dwarves, catalog.Get(UndergroundFactionKind.DarkDwarves));
    }

    [Fact]
    public void CoreNameGeneratorsAreDeterministicAndPreserveLegacyNames()
    {
        var goblinGenerator = NameGeneratorCatalog.Core.Get(
            ContentId.Parse("core:goblin-syllables"));
        var request = new NameGenerationRequest(
            new WorldSeed(123),
            SubjectId: 7,
            Ordinal: 6,
            new HashSet<string>(StringComparer.Ordinal));
        var first = goblinGenerator.Generate(request);
        Assert.Equal(first, goblinGenerator.Generate(request));
        Assert.Equal(
            $"{first}-7",
            goblinGenerator.Generate(request with
            {
                ExistingNames = new HashSet<string>(StringComparer.Ordinal) { first },
            }));

        var humanGenerator = NameGeneratorCatalog.Core.Get(
            ContentId.Parse("core:human-frontier-names"));
        Assert.Equal("Aldona", humanGenerator.Generate(request with
        {
            SubjectId = 1,
            Ordinal = 0,
        }));
        Assert.Equal("Lucjan", humanGenerator.Generate(request with
        {
            SubjectId = 12,
            Ordinal = 11,
        }));
        Assert.Equal("Celina", humanGenerator.Generate(request with
        {
            SubjectId = 3,
            Ordinal = 1,
            Sex = ActorSex.Female,
        }));
        Assert.Equal("Dobromir", humanGenerator.Generate(request with
        {
            SubjectId = 4,
            Ordinal = 1,
            Sex = ActorSex.Male,
        }));
        Assert.NotEqual(
            goblinGenerator.Generate(request with { Sex = ActorSex.Female }),
            goblinGenerator.Generate(request with { Sex = ActorSex.Male }));
    }

    [Fact]
    public void CivilizationWithUnknownNameGeneratorIsRejected()
    {
        var definitions = CivilizationCatalog.Core.All
            .Select(definition => definition.LegacyRole ==
                    CivilizationLegacyRole.PlayerGoblins
                ? definition with
                {
                    Identity = definition.Identity with
                    {
                        NameGeneratorId = ContentId.Parse("core:missing-generator"),
                    },
                }
                : definition)
            .ToArray();

        Assert.Throws<InvalidDataException>(() =>
            new CivilizationCatalog(definitions));
    }

    [Fact]
    public void CivilizationWithInvalidGoblinNeedsIsRejected()
    {
        var definitions = CivilizationCatalog.Core.All
            .Select(definition => definition.LegacyRole ==
                    CivilizationLegacyRole.PlayerGoblins
                ? definition with
                {
                    Needs = definition.Needs! with
                    {
                        RestThreshold = definition.Needs.MaximumFatigue + 1,
                    },
                }
                : definition)
            .ToArray();

        Assert.Throws<InvalidDataException>(() =>
            new CivilizationCatalog(definitions));
    }

    [Fact]
    public void CivilizationWithInvalidActorGenerationIsRejected()
    {
        var definitions = CivilizationCatalog.Core.All
            .Select(definition => definition.LegacyRole ==
                    CivilizationLegacyRole.PlayerGoblins
                ? definition with
                {
                    ActorGeneration = definition.ActorGeneration! with
                    {
                        SkillPool = [],
                    },
                }
                : definition)
            .ToArray();

        Assert.Throws<InvalidDataException>(() =>
            new CivilizationCatalog(definitions));
    }

    [Fact]
    public void RuntimeUsesActiveActorGenerationProfile()
    {
        var definitions = CivilizationCatalog.Core.All
            .Select(definition => definition.LegacyRole ==
                    CivilizationLegacyRole.PlayerGoblins
                ? definition with
                {
                    ActorGeneration = definition.ActorGeneration! with
                    {
                        SkillPool = [GoblinSkill.Building],
                        SkillSampleKeys = [3],
                        TraitPool = [GoblinTrait.Hardy],
                        TraitSampleKeys = [5],
                        GuaranteedEquipment = PersonalEquipment.HideClothes,
                        OptionalEquipment = PersonalEquipment.BoneKnife,
                        OptionalEquipmentRollMaximumExclusive = 1,
                        OptionalEquipmentSuccessValue = 0,
                        WorkPreferenceMinimum = 2,
                        WorkPreferenceMaximum = 2,
                    },
                }
                : definition)
            .ToArray();

        try
        {
            CivilizationCatalog.Activate(new CivilizationCatalog(definitions));
            var actor = Assert.Single(SimulationEngine.Create(
                new WorldSeed(0x47454E4552415445UL),
                SimulationDefinitions.Foundation,
                initialGoblinCount: 1,
                initialFoodStock: 0).CreateSnapshot().Actors);

            Assert.Equal(GoblinSkill.Building, actor.KnownSkills);
            Assert.Equal(GoblinTrait.Hardy, actor.KnownTraits);
            Assert.True(actor.Equipment.HasFlag(PersonalEquipment.HideClothes));
            Assert.True(actor.Equipment.HasFlag(PersonalEquipment.BoneKnife));
            Assert.False(actor.Equipment.HasFlag(PersonalEquipment.RagClothes));
            Assert.False(actor.Equipment.HasFlag(PersonalEquipment.PrimitiveWaterskin));
            Assert.Equal(new GoblinWorkPreferences(2, 2, 2), actor.WorkPreferences);
        }
        finally
        {
            CivilizationCatalog.ResetToCore();
        }
    }

    [Fact]
    public void CivilizationWithInvalidPopulationNeedsIsRejected()
    {
        var definitions = CivilizationCatalog.Core.All
            .Select(definition => definition.LegacyRole ==
                    CivilizationLegacyRole.HumanVillage
                ? definition with
                {
                    PopulationNeeds = definition.PopulationNeeds! with
                    {
                        DrinkRelief = definition.PopulationNeeds.MaximumNeed + 1,
                    },
                }
                : definition)
            .ToArray();

        Assert.Throws<InvalidDataException>(() =>
            new CivilizationCatalog(definitions));
    }

    [Fact]
    public void CivilizationWithInvalidCombatProfileIsRejected()
    {
        var definitions = CivilizationCatalog.Core.All
            .Select(definition => definition.LegacyRole ==
                    CivilizationLegacyRole.PlayerGoblins
                ? definition with
                {
                    Combat = definition.Combat! with
                    {
                        MeleeDamageVariance = -1,
                    },
                }
                : definition)
            .ToArray();

        Assert.Throws<InvalidDataException>(() =>
            new CivilizationCatalog(definitions));
    }

    [Fact]
    public void CivilizationWithInvalidPerceptionProfileIsRejected()
    {
        var definitions = CivilizationCatalog.Core.All
            .Select(definition => definition.LegacyRole ==
                    CivilizationLegacyRole.HumanVillage
                ? definition with
                {
                    Perception = definition.Perception! with
                    {
                        NightVisionRadius = 0,
                    },
                }
                : definition)
            .ToArray();

        Assert.Throws<InvalidDataException>(() =>
            new CivilizationCatalog(definitions));
    }

    [Fact]
    public void CivilizationWithInvalidSpatialBehaviorIsRejected()
    {
        var definitions = CivilizationCatalog.Core.All
            .Select(definition => definition.LegacyRole ==
                    CivilizationLegacyRole.PlayerGoblins
                ? definition with
                {
                    SpatialBehavior = definition.SpatialBehavior! with
                    {
                        MovementIntervalTicks = 0,
                    },
                }
                : definition)
            .ToArray();

        Assert.Throws<InvalidDataException>(() =>
            new CivilizationCatalog(definitions));
    }

    [Fact]
    public void RuntimeUsesActiveCivilizationHealthAndNeedProfiles()
    {
        var definitions = CivilizationCatalog.Core.All
            .Select(definition => definition.LegacyRole switch
            {
                CivilizationLegacyRole.PlayerGoblins => definition with
                {
                    Vitals = new CivilizationVitalsDefinition(12_000),
                    Combat = new CivilizationCombatDefinition(410, 25),
                    Perception = new CivilizationPerceptionDefinition(7, 5, 4),
                    SpatialBehavior = new CivilizationSpatialBehaviorDefinition(13, 0, 2),
                    Needs = definition.Needs! with
                    {
                        MaximumHunger = 200_000,
                        HungerPerTick = 7,
                    },
                },
                CivilizationLegacyRole.HumanVillage => definition with
                {
                    Vitals = new CivilizationVitalsDefinition(7_000),
                    Combat = new CivilizationCombatDefinition(520, 35),
                    Perception = new CivilizationPerceptionDefinition(8, 4, 0),
                    SpatialBehavior = new CivilizationSpatialBehaviorDefinition(17, 12, 0),
                    PopulationNeeds = definition.PopulationNeeds! with
                    {
                        MaximumNeed = 2_000,
                        MaximumFatigue = 2_500,
                    },
                },
                _ => definition,
            })
            .ToArray();
        var catalog = new CivilizationCatalog(definitions);

        try
        {
            CivilizationCatalog.Activate(catalog);
            var seed = new WorldSeed(0x564954414C53UL);
            var map = SwampMapGenerator.Generate(seed, width: 48, height: 48);
            var engine = SimulationEngine.Create(
                seed,
                SimulationDefinitions.Foundation,
                map,
                initialGoblinCount: 1,
                initialFoodStock: 0,
                initialHunger: 150_000);
            var snapshot = engine.CreateSnapshot();

            Assert.Equal(12_000, Assert.Single(snapshot.Actors).Health);
            Assert.Equal(new CivilizationCombatDefinition(410, 25), engine.GoblinCombat);
            Assert.Equal(new CivilizationCombatDefinition(520, 35), engine.HumanCombat);
            Assert.Equal(
                new CivilizationPerceptionDefinition(7, 5, 4),
                engine.GoblinPerception);
            Assert.Equal(
                new CivilizationPerceptionDefinition(8, 4, 0),
                engine.HumanPerception);
            Assert.Equal(
                new CivilizationSpatialBehaviorDefinition(13, 0, 2),
                engine.GoblinSpatialBehavior);
            Assert.Equal(
                new CivilizationSpatialBehaviorDefinition(17, 12, 0),
                engine.HumanSpatialBehavior);
            Assert.Equal(200_000, engine.MaximumGoblinHunger);
            engine.AdvanceTicks(1);
            Assert.Equal(
                150_007,
                Assert.Single(engine.CreateSnapshot().Actors).Hunger);
            Assert.All(snapshot.HumanVillage.Villagers, villager =>
            {
                Assert.Equal(
                    villager.Role == HumanCohortRole.Guards ? 7_000 : 3_500,
                    villager.MaximumHealth);
                Assert.Equal(2_000, villager.MaximumNeed);
                Assert.Equal(2_500, villager.MaximumFatigue);
            });
        }
        finally
        {
            CivilizationCatalog.ResetToCore();
        }
    }

    [Fact]
    public void CoreUndergroundParametersPreserveLegacyGeometry()
    {
        var generation = CivilizationCatalog.Core
            .Get(CivilizationLegacyRole.DeepDwarfClan)
            .UndergroundGeneration!;

        Assert.Equal(UndergroundFactionDirector.FirstFactionLevel, generation.FirstLevel);
        Assert.Equal(UndergroundFactionDirector.DepthBandSize, generation.DepthBandSize);
    }
}
