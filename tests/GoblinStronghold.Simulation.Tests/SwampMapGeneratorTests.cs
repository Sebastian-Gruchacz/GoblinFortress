using GoblinStronghold.Simulation.Map;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class SwampMapGeneratorTests
{
    [Fact]
    public void SameSeedProducesSameMap()
    {
        var first = SwampMapGenerator.Generate(new WorldSeed(123), width: 64, height: 48);
        var second = SwampMapGenerator.Generate(new WorldSeed(123), width: 64, height: 48);

        Assert.Equal(SwampMapGenerator.CurrentVersion, first.GeneratorVersion);
        Assert.Equal(first.ComputeFingerprint(), second.ComputeFingerprint());
        Assert.Equal(first.GoblinSpawn, second.GoblinSpawn);
        Assert.Equal(first.HumanVillage, second.HumanVillage);
    }

    [Fact]
    public void UnsupportedGeneratorVersionIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SwampMapGenerator.Generate(
                new WorldSeed(123),
                width: 32,
                height: 32,
                generatorVersion: SwampMapGenerator.CurrentVersion + 1));
    }

    [Fact]
    public void DifferentSeedsProduceDifferentMaps()
    {
        var first = SwampMapGenerator.Generate(new WorldSeed(123), width: 64, height: 48);
        var second = SwampMapGenerator.Generate(new WorldSeed(124), width: 64, height: 48);

        Assert.NotEqual(first.ComputeFingerprint(), second.ComputeFingerprint());
    }

    [Fact]
    public void GeneratedMapsRemainValidAcrossManySeeds()
    {
        foreach (var size in new[] { SwampMapGenerator.MinimumDimension, 48 })
        {
            for (ulong seed = 0; seed < 256; seed++)
            {
                var map = SwampMapGenerator.Generate(new WorldSeed(seed), width: size, height: size);
                var validation = SwampMapValidator.Validate(map);

                Assert.True(
                    validation.IsValid,
                    $"Size {size}, seed {seed}: {string.Join("; ", validation.Errors)}");
                Assert.True(map.HasTraversablePath(map.GoblinSpawn, map.HumanVillage));
                Assert.Equal(0, map.GoblinSpawn.Z);
                Assert.Equal(0, map.HumanVillage.Z);
            }
        }
    }

    [Fact]
    public void MapContainsEveryFoundationalTerrainType()
    {
        var map = SwampMapGenerator.Generate(new WorldSeed(456), width: 64, height: 64);

        Assert.True(map.CountTerrain(TerrainKind.SolidGround) > 0);
        Assert.True(map.CountTerrain(TerrainKind.Mud) > 0);
        Assert.True(map.CountTerrain(TerrainKind.ShallowWater) > 0);
        Assert.True(map.CountTerrain(TerrainKind.DeepWater) > 0);
    }

    [Fact]
    public void PositionsOutsideSurfaceAreRejected()
    {
        var map = SwampMapGenerator.Generate(new WorldSeed(456), width: 32, height: 32);

        Assert.False(map.IsWithin(new GridPosition(-1, 0)));
        Assert.False(map.IsWithin(new GridPosition(0, -1)));
        Assert.False(map.IsWithin(new GridPosition(map.Width, 0)));
        Assert.False(map.IsWithin(new GridPosition(0, map.Height)));
        Assert.False(map.IsWithin(new GridPosition(0, 0, Z: 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.GetCell(new GridPosition(0, 0, Z: 1)));
    }

    [Theory]
    [InlineData(15, 32)]
    [InlineData(32, 15)]
    [InlineData(2049, 32)]
    [InlineData(32, 2049)]
    public void UnsupportedDimensionsAreRejected(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SwampMapGenerator.Generate(new WorldSeed(1), width, height));
    }
}
