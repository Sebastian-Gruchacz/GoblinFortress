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
    public void HistoricalGeneratorVersionRemainsDeterministic()
    {
        var first = SwampMapGenerator.Generate(
            new WorldSeed(123),
            width: 32,
            height: 32,
            generatorVersion: 1);
        var second = SwampMapGenerator.Generate(
            new WorldSeed(123),
            width: 32,
            height: 32,
            generatorVersion: 1);
        var current = SwampMapGenerator.Generate(new WorldSeed(123), width: 32, height: 32);

        Assert.Equal(1, first.GeneratorVersion);
        Assert.Equal(first.ComputeFingerprint(), second.ComputeFingerprint());
        Assert.NotEqual(first.ComputeFingerprint(), current.ComputeFingerprint());
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
    public void ShallowsAreWadeableButDeepWaterDropsToTheLowerLevel()
    {
        var map = SwampMapGenerator.Generate(new WorldSeed(456), width: 64, height: 64);
        var shallows = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width)
                .Select(x => map.GetCell(new GridPosition(x, y))))
            .Where(cell => cell.Terrain == TerrainKind.ShallowWater)
            .ToArray();
        var deepWater = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width)
                .Select(x => map.GetCell(new GridPosition(x, y))))
            .Where(cell => cell.Terrain == TerrainKind.DeepWater)
            .ToArray();

        Assert.NotEmpty(shallows);
        Assert.All(shallows, cell =>
        {
            Assert.True(cell.IsTraversable);
            Assert.True(cell.HasFloorAtSurface);
            Assert.Equal(0, cell.FloorLevel);
            Assert.Equal(0, cell.WaterDepthLevels);
        });
        Assert.NotEmpty(deepWater);
        Assert.All(deepWater, cell =>
        {
            Assert.False(cell.IsTraversable);
            Assert.False(cell.HasFloorAtSurface);
            Assert.Equal(-1, cell.FloorLevel);
            Assert.Equal(1, cell.WaterDepthLevels);
        });
    }

    [Fact]
    public void HistoricalGeneratorKeepsLegacyDeepWaterRepresentation()
    {
        var map = SwampMapGenerator.Generate(
            new WorldSeed(456),
            width: 64,
            height: 64,
            generatorVersion: 2);
        var deepWater = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width)
                .Select(x => map.GetCell(new GridPosition(x, y))))
            .First(cell => cell.Terrain == TerrainKind.DeepWater);

        Assert.Equal(0, deepWater.FloorLevel);
        Assert.False(deepWater.IsTraversable);
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
