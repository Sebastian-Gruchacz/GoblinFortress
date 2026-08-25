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
    public void RegionalGeneratorCreatesDirectedGeographyAndDiagonalRiver()
    {
        var map = SwampMapGenerator.Generate(new WorldSeed(0x524547494F4EUL), 64, 64);

        Assert.True(map.GoblinSpawn.X < map.Width * 0.35);
        Assert.True(map.GoblinSpawn.Y > map.Height * 0.55);
        Assert.True(map.HumanVillage.X > map.Width * 0.65);
        Assert.True(map.HumanVillage.Y < map.Height * 0.35);

        var swampCells = Positions(map)
            .Where(position => position.X < map.Width * 0.3 || position.Y > map.Height * 0.75)
            .Select(map.GetCell)
            .ToArray();
        var upperRightCells = Positions(map)
            .Where(position => position.X > map.Width * 0.62 && position.Y < map.Height * 0.42)
            .Select(map.GetCell)
            .ToArray();
        Assert.True(
            swampCells.Count(IsWetTerrain) > upperRightCells.Count(IsWetTerrain) * 2,
            $"wet swamp={swampCells.Count(IsWetTerrain)}, wet upper-right={upperRightCells.Count(IsWetTerrain)}");

        for (var x = 4; x < map.Width - 4; x += 8)
        {
            var expectedY = (int)Math.Round((0.82 - (0.64 * x / (map.Width - 1d))) * map.Height);
            Assert.Contains(
                Enumerable.Range(Math.Max(0, expectedY - 7), Math.Min(map.Height, expectedY + 8) - Math.Max(0, expectedY - 7)),
                y => map.GetCell(new GridPosition(x, y)).Terrain is
                    TerrainKind.ShallowWater or TerrainKind.DeepWater);
        }
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
            Assert.InRange(cell.FloorLevel, (sbyte)-2, (sbyte)-1);
            Assert.InRange(cell.WaterDepthLevels, 1, 2);
        });
    }

    [Fact]
    public void CurrentGeneratorCreatesHillsDepressionsRampsAndCliffs()
    {
        var map = SwampMapGenerator.Generate(new WorldSeed(0x484549474854UL), 96, 96);
        var positions = Positions(map).ToArray();
        var levels = positions.Select(position => map.GetCell(position).SurfaceLevel).ToHashSet();
        var ramp = positions.First(position =>
            map.GetCell(position).RampDirection != TerrainRampDirection.None);
        var rampUphill = UphillNeighbor(ramp, map.GetCell(ramp).RampDirection);
        var cliff = (
                from position in positions
                from neighbor in map.GetCardinalNeighbors(position)
                let cell = map.GetCell(position)
                let neighborCell = map.GetCell(neighbor)
                where cell.IsTraversable && neighborCell.IsTraversable &&
                    cell.SurfaceLevel != neighborCell.SurfaceLevel &&
                    !map.CanTraverseSurfaceEdge(position, neighbor)
                select (position, neighbor))
            .First();

        Assert.Contains((sbyte)-1, levels);
        Assert.Contains((sbyte)0, levels);
        Assert.Contains((sbyte)1, levels);
        Assert.Contains((sbyte)2, levels);
        Assert.True(map.CanTraverseSurfaceEdge(ramp, rampUphill));
        Assert.True(map.CanTraverseSurfaceEdge(rampUphill, ramp));
        Assert.False(map.CanTraverseSurfaceEdge(cliff.position, cliff.neighbor));
        Assert.True(map.HasTraversablePath(map.GoblinSpawn, map.HumanVillage));
        Assert.Equal(0, map.GetCell(map.GoblinSpawn).SurfaceLevel);
        Assert.Equal(0, map.GetCell(map.HumanVillage).SurfaceLevel);
    }

    [Fact]
    public void CurrentGeneratorCreatesConnectedTwoLevelCaves()
    {
        var map = SwampMapGenerator.Generate(new WorldSeed(0x4341564553UL), 64, 64);
        var deepestFloor = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width)
                .Select(x => new GridPosition(x, y, map.DeepestCaveLevel)))
            .First(position => map.GetCaveCell(position).IsOpen);
        var path = map.FindTerrainPath(map.CaveEntrances.Single(), deepestFloor);

        Assert.Equal(2, map.CaveLevelCount);
        Assert.Equal(-2, map.DeepestCaveLevel);
        Assert.Contains(map.VerticalPassages, passage =>
            passage.Kind == VerticalPassageKind.CaveMouth);
        Assert.Contains(map.VerticalPassages, passage =>
            passage.Kind == VerticalPassageKind.NaturalRamp);
        Assert.Equal(0, map.CaveEntrances.Single().Z);
        Assert.NotNull(path);
        Assert.Contains(path, position => position.Z == -1);
        Assert.Contains(path, position => position.Z == -2);
    }

    [Fact]
    public void CaveRockAndOpenSpaceHaveDistinctTraversalContracts()
    {
        var map = SwampMapGenerator.Generate(new WorldSeed(0x524F434BUL), 64, 64);
        var cavePositions = Enumerable.Range(1, map.CaveLevelCount)
            .SelectMany(level => Enumerable.Range(0, map.Height)
                .SelectMany(y => Enumerable.Range(0, map.Width)
                    .Select(x => new GridPosition(x, y, -level))))
            .ToArray();
        var solidRock = cavePositions.First(position => !map.GetCaveCell(position).IsOpen);
        var floor = cavePositions.First(position => map.GetCaveCell(position).Kind == CaveCellKind.Floor);

        Assert.Contains(cavePositions.Select(position => map.GetCaveCell(position).Rock),
            rock => rock == RockKind.Sandstone);
        Assert.Contains(cavePositions.Select(position => map.GetCaveCell(position).Rock),
            rock => rock == RockKind.Granite);
        Assert.False(map.IsTerrainTraversable(solidRock));
        Assert.True(map.IsTerrainTraversable(floor));
    }

    [Fact]
    public void GeneratorVersionFiveKeepsWorldWithoutMaterializedCaves()
    {
        var map = SwampMapGenerator.Generate(
            new WorldSeed(123),
            width: 32,
            height: 32,
            generatorVersion: 5);

        Assert.Equal(0, map.CaveLevelCount);
        Assert.Empty(map.VerticalPassages);
        Assert.Empty(map.CaveEntrances);
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

    private static IEnumerable<GridPosition> Positions(GeneratedMap map) =>
        Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width).Select(x => new GridPosition(x, y)));

    private static bool IsWetTerrain(MapCell cell) =>
        cell.Terrain is TerrainKind.Mud or TerrainKind.ShallowWater or TerrainKind.DeepWater;

    private static GridPosition UphillNeighbor(
        GridPosition position,
        TerrainRampDirection direction) => direction switch
    {
        TerrainRampDirection.North => position with { Y = position.Y - 1 },
        TerrainRampDirection.East => position with { X = position.X + 1 },
        TerrainRampDirection.South => position with { Y = position.Y + 1 },
        TerrainRampDirection.West => position with { X = position.X - 1 },
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
    };
}
