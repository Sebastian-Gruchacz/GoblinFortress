using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Presentation;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class PlantPresentationPositionPolicyTests
{
    [Fact]
    public void ExplicitUndergroundPositionIsNotProjectedToTerrainSurface()
    {
        var map = SwampMapGenerator.Generate(new WorldSeed(0x504C414E545AUL), 64, 64);
        var position = new GridPosition(12, 18, -3);
        var plant = new PlantPatchSnapshot(position, PlantKind.MushroomCluster, 10, 10);

        Assert.Equal(position, PlantPresentationPositionPolicy.Resolve(map, plant));
    }

    [Fact]
    public void LegacySurfacePositionFollowsGeneratedTerrainHeight()
    {
        var map = SwampMapGenerator.Generate(new WorldSeed(0x504C414E545AUL), 64, 64);
        var surface = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width)
                .Select(x => map.GetTerrainSurfacePosition(new GridPosition(x, y))))
            .First(position => position.Z != 0);
        var legacyPosition = surface with { Z = 0 };
        var plant = new PlantPatchSnapshot(
            legacyPosition,
            PlantKind.BerryBush,
            10,
            10);

        Assert.Equal(surface, PlantPresentationPositionPolicy.Resolve(map, plant));
    }
}
