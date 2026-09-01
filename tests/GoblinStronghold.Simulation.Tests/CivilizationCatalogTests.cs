using GoblinStronghold.Simulation.Civilizations;
using GoblinStronghold.Simulation.ContentPacks;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

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
        Assert.Equal(ContentId.Parse("core:human-village"), humans.Id);
        Assert.False(humans.PlayerControllable);
        Assert.Equal(ContentId.Parse("core:cave-dwarf-clan"), dwarves.Id);
        Assert.Equal(
            UndergroundFactionKind.DarkDwarves,
            dwarves.UndergroundGeneration!.LegacyKind);
        Assert.Same(dwarves, catalog.Get(UndergroundFactionKind.DarkDwarves));
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
