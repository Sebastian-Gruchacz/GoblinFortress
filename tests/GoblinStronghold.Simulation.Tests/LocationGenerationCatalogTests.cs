using GoblinStronghold.Simulation.ContentPacks;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Map.Generation;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class LocationGenerationCatalogTests
{
    [Fact]
    public void CoreCatalogDefinesDemoSwampFrontier()
    {
        var profile = LocationGenerationCatalog.Core.Get(
            ContentId.Parse("core:demo-swamp-frontier"));

        Assert.Same(profile, SwampMapGenerator.DefaultProfile);
        Assert.Equal(ContentId.Parse("core:temperate-marsh"), profile.ClimateProfileId);
        Assert.Equal("marsh-frontier", profile.Character);
        Assert.Equal(96, profile.DefaultDimension);
        Assert.Equal(16, profile.MinimumDimension);
        Assert.Equal(2_048, profile.MaximumDimension);
        Assert.Equal(0.48d, profile.River.DeepWaterRatio);
        Assert.Equal(8, profile.GoblinSettlement.PadWidth);
        Assert.Equal(9, profile.HumanSettlement.PadHeight);
    }

    [Fact]
    public void InvalidDimensionRangeIsRejectedBeforeGeneration()
    {
        var profile = SwampMapGenerator.DefaultProfile with
        {
            MinimumDimension = 8,
        };

        Assert.Throws<InvalidDataException>(() =>
            new LocationGenerationCatalog([profile]));
    }
}
