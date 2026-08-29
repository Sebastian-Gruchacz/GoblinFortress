using GoblinStronghold.Simulation.Map;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class SwampMapGeneratorTests
{
    [Fact]
    public void CurrentDefaultMapHasProminentFoothillsNearGoblinRegion()
    {
        Assert.Equal(96, SwampMapGenerator.DefaultDimension);

        var map = SwampMapGenerator.Generate(
            new WorldSeed(0x474F424C494EUL),
            SwampMapGenerator.DefaultDimension,
            SwampMapGenerator.DefaultDimension);
        var elevated = Positions(map)
            .Where(position => map.GetCell(position).SurfaceLevel > 0)
            .ToArray();
        var nearestToGoblinCamp = elevated.Min(position =>
            Math.Abs(position.X - map.GoblinSpawn.X) +
            Math.Abs(position.Y - map.GoblinSpawn.Y));

        Assert.True(
            elevated.Length >= map.CellCount / 20,
            $"Only {elevated.Length} of {map.CellCount} cells are elevated.");
        Assert.Contains(elevated, position => map.GetCell(position).SurfaceLevel == 2);
        Assert.True(
            nearestToGoblinCamp <= map.Width / 4,
            $"Nearest elevated cell is {nearestToGoblinCamp} steps from goblin spawn.");
    }

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
    public void CurrentGeneratorBreaksUpStraightSwampAndSettlementPadEdges()
    {
        var map = SwampMapGenerator.Generate(new WorldSeed(0x4E4F495345UL), 64, 64);
        var leftSwampEdges = Enumerable.Range(4, 24)
            .Select(y => Enumerable.Range(0, map.Width / 2)
                .Where(x => IsWetTerrain(map.GetCell(new GridPosition(x, y))))
                .DefaultIfEmpty(0)
                .Max())
            .ToArray();
        var humanPadStartX = map.HumanVillage.X - 4;
        var humanPadStartY = map.HumanVillage.Y - 4;
        var humanPad = Enumerable.Range(humanPadStartY, 9)
            .SelectMany(y => Enumerable.Range(humanPadStartX, 8)
                .Select(x => map.GetCell(new GridPosition(x, y))))
            .ToArray();

        Assert.True(
            leftSwampEdges.Distinct().Count() >= 4,
            $"swamp edge positions: {string.Join(", ", leftSwampEdges)}");
        Assert.Contains(humanPad, cell =>
            cell.Terrain != TerrainKind.SolidGround || cell.SurfaceLevel != 0);
    }

    [Fact]
    public void VersionSixRetainsItsHistoricalDeterministicLayout()
    {
        var first = SwampMapGenerator.Generate(
            new WorldSeed(0x5636UL), 64, 64, generatorVersion: 6);
        var second = SwampMapGenerator.Generate(
            new WorldSeed(0x5636UL), 64, 64, generatorVersion: 6);
        var current = SwampMapGenerator.Generate(new WorldSeed(0x5636UL), 64, 64);

        Assert.Equal(first.ComputeFingerprint(), second.ComputeFingerprint());
        Assert.NotEqual(first.ComputeFingerprint(), current.ComputeFingerprint());
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
        var materialRamp = map.GetTerrainSurfacePosition(ramp);
        var materialRampUphill = map.GetTerrainSurfacePosition(rampUphill);
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
        Assert.Equal(1, materialRampUphill.Z - materialRamp.Z);
        Assert.True(map.CanTraverseTerrainSurfaceEdge(materialRamp, materialRampUphill));
        Assert.True(map.CanTraverseTerrainSurfaceEdge(materialRampUphill, materialRamp));
        Assert.Contains(materialRampUphill, map.GetTerrainNeighbors(materialRamp));
        Assert.Equal(
            new[] { materialRampUphill },
            map.FindTerrainPath(materialRamp, materialRampUphill));
        Assert.False(map.CanTraverseSurfaceEdge(cliff.position, cliff.neighbor));
        Assert.False(map.CanTraverseTerrainSurfaceEdge(
            map.GetTerrainSurfacePosition(cliff.position),
            map.GetTerrainSurfacePosition(cliff.neighbor)));
        Assert.True(map.HasTraversablePath(map.GoblinSpawn, map.HumanVillage));
        var materialRoute = map.FindTerrainPath(
            map.GetTerrainSurfacePosition(map.GoblinSpawn),
            map.GetTerrainSurfacePosition(map.HumanVillage));
        Assert.NotNull(materialRoute);
        Assert.All(materialRoute!, position => Assert.True(map.IsTerrainTraversable(position)));
        Assert.Equal(0, map.GetCell(map.GoblinSpawn).SurfaceLevel);
        Assert.Equal(0, map.GetCell(map.HumanVillage).SurfaceLevel);
    }

    [Fact]
    public void CurrentHillsExposeDistinctSurfaceAndRockVolumeCoordinates()
    {
        var map = SwampMapGenerator.Generate(new WorldSeed(0x48494C4C564F4CUL), 96, 96);
        var column = Positions(map)
            .Where(position => map.GetCell(position) is
                { Terrain: TerrainKind.SolidGround, SurfaceLevel: > 0 })
            .OrderByDescending(position => map.GetCell(position).SurfaceLevel)
            .First();
        var surface = map.GetTerrainSurfacePosition(column);
        var lowerRock = column;

        Assert.True(surface.Z > 0);
        Assert.True(map.IsTerrainSurfacePosition(surface));
        Assert.False(map.IsHillRockPosition(surface));
        Assert.True(map.IsHillRockPosition(lowerRock));
        Assert.Equal(CaveCellKind.SolidRock, map.GetHillRockCell(lowerRock).Kind);
        Assert.All(Enumerable.Range(0, surface.Z), z =>
        {
            var rock = column with { Z = z };
            Assert.True(map.IsHillRockPosition(rock));
            Assert.Equal(MineralDepositKind.None, map.GetHillRockCell(rock).Deposit);
        });
        var regenerated = SwampMapGenerator.Generate(map.Seed, map.Width, map.Height);
        Assert.Equal(map.GetHillRockCell(lowerRock), regenerated.GetHillRockCell(lowerRock));

        var historical = SwampMapGenerator.Generate(
            map.Seed,
            map.Width,
            map.Height,
            generatorVersion: 8);
        Assert.Equal(0, historical.MaterializedPositiveLevelCount);
        Assert.False(historical.IsHillRockPosition(lowerRock));
    }

    [Fact]
    public void RaisedMudCoversAContinuousMineableRockMass()
    {
        var map = SwampMapGenerator.Generate(
            new WorldSeed(4882149368200903417UL),
            96,
            96);
        var column = Positions(map)
            .First(position => map.GetCell(position) is
                { Terrain: TerrainKind.Mud, SurfaceLevel: > 0 });
        var surface = map.GetTerrainSurfacePosition(column);

        Assert.True(surface.Z > 0);
        Assert.False(map.IsHillRockPosition(column));
        Assert.True(map.IsHillMassPosition(column));
        Assert.True(map.TryGetInitialGeometry(column, out var geometry));
        Assert.True(geometry.IsSolid);
        Assert.Equal(CaveCellKind.SolidRock, map.GetHillMassCell(column).Kind);
        Assert.Equal(MineralDepositKind.None, map.GetHillMassCell(column).Deposit);
        Assert.Equal(map.GetHillMassCell(column), map.GetRockCell(column));
    }

    [Fact]
    public void InitialGeometryUsesOneContractAboveAndBelowZero()
    {
        var map = SwampMapGenerator.Generate(new WorldSeed(0x554E4946494544UL), 96, 96);
        var column = Positions(map)
            .Where(position => map.GetCell(position) is
                { Terrain: TerrainKind.SolidGround, SurfaceLevel: > 0 })
            .OrderByDescending(position => map.GetCell(position).SurfaceLevel)
            .First();
        var summit = map.GetTerrainSurfacePosition(column);

        Assert.True(map.TryGetInitialGeometry(column, out var hillRock));
        Assert.True(hillRock.IsSolid);
        Assert.NotNull(hillRock.SolidMaterial);

        Assert.True(map.TryGetInitialGeometry(summit, out var summitFloor));
        Assert.False(summitFloor.IsSolid);
        Assert.True(summitFloor.IsSupported);
        Assert.Equal(TerrainKind.SolidGround, summitFloor.Cover);

        var unsupportedAir = summit with { Z = summit.Z - 1 };
        var lowerColumn = Positions(map)
            .First(position =>
                map.GetCell(position).SurfaceLevel < unsupportedAir.Z);
        unsupportedAir = lowerColumn with { Z = unsupportedAir.Z };
        Assert.True(map.TryGetInitialGeometry(unsupportedAir, out var air));
        Assert.False(air.IsSolid);
        Assert.False(air.IsSupported);

        var caveFloor = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width)
                .Select(x => new GridPosition(x, y, -1)))
            .First(position => map.GetCaveCell(position).Kind == CaveCellKind.Floor);
        Assert.True(map.TryGetInitialGeometry(caveFloor, out var underground));
        Assert.False(underground.IsSolid);
        Assert.True(underground.IsSupported);

        Assert.False(map.TryGetInitialGeometry(
            summit with { Z = map.MaximumTerrainLevel + 1 },
            out _));
    }

    [Fact]
    public void InitialGeometryBakesReliefAndWaterBeforeUnderlyingCaves()
    {
        var map = SwampMapGenerator.Generate(new WorldSeed(0x42414B454F524445UL), 96, 96);
        var depression = Positions(map)
            .First(position => map.GetCell(position) is
                { Terrain: not TerrainKind.DeepWater, SurfaceLevel: < 0 });
        var depressionFloor = map.GetTerrainSurfacePosition(depression);

        Assert.True(map.IsCavePosition(depressionFloor));
        Assert.True(map.TryGetInitialGeometry(depressionFloor, out var bakedDepression));
        Assert.False(bakedDepression.IsSolid);
        Assert.True(bakedDepression.IsSupported);
        Assert.Equal(map.GetCell(depression).Terrain, bakedDepression.Cover);

        var deepColumn = Positions(map)
            .Where(position => map.GetCell(position).Terrain == TerrainKind.DeepWater)
            .OrderBy(position => map.GetCell(position).FloorLevel)
            .First();
        var deepTerrain = map.GetCell(deepColumn);
        Assert.True(map.TryGetInitialGeometry(deepColumn, out var waterSurface));
        Assert.Equal(CellFluidKind.Water, waterSurface.Fluid);
        Assert.Equal(deepTerrain.WaterDepthLevels, waterSurface.FluidDepthLevels);

        if (deepTerrain.WaterDepthLevels > 1)
        {
            var submerged = deepColumn with { Z = deepTerrain.SurfaceLevel - 1 };
            Assert.True(map.TryGetInitialGeometry(submerged, out var lowerWater));
            Assert.Equal(CellFluidKind.Water, lowerWater.Fluid);
            Assert.True(lowerWater.IsSupported);
            Assert.Equal(1, lowerWater.FluidDepthLevels);
        }
    }

    [Fact]
    public void InitialSkyExposurePassesOnlyThroughRealVerticalOpenings()
    {
        var map = SwampMapGenerator.Generate(new WorldSeed(0x534B594C49474854UL), 96, 96);
        var caveMouth = map.VerticalPassages.Single(passage =>
            passage.Kind == VerticalPassageKind.CaveMouth);

        Assert.True(map.IsInitiallyOpenToSky(caveMouth.Upper));
        Assert.True(map.IsInitiallyOpenToSky(caveMouth.Lower));

        var coveredCaveFloor = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width)
                .Select(x => new GridPosition(x, y, -1)))
            .First(position =>
                map.GetCaveCell(position).IsOpen && position != caveMouth.Lower &&
                !map.VerticalPassages.Any(passage => passage.Lower == position));
        Assert.False(map.IsInitiallyOpenToSky(coveredCaveFloor));
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
        Assert.True(map.IsTerrainSurfacePosition(map.CaveEntrances.Single()));
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
    public void CurrentCavesContainClusteredCoalAndIronDepositsOnlyInSolidRock()
    {
        var map = SwampMapGenerator.Generate(new WorldSeed(0x5645494E53UL), 64, 64);
        var caveCells = Enumerable.Range(1, map.CaveLevelCount)
            .SelectMany(level => Enumerable.Range(0, map.Height)
                .SelectMany(y => Enumerable.Range(0, map.Width)
                    .Select(x => map.GetCaveCell(new GridPosition(x, y, -level)))))
            .ToArray();

        Assert.Contains(caveCells, cell => cell.Deposit == MineralDepositKind.Coal);
        Assert.Contains(caveCells, cell => cell.Deposit == MineralDepositKind.IronOre);
        Assert.All(caveCells.Where(cell => cell.Deposit != MineralDepositKind.None), cell =>
            Assert.Equal(CaveCellKind.SolidRock, cell.Kind));
        var depositCountsByLevel = Enumerable.Range(1, map.CaveLevelCount)
            .ToDictionary(
                level => level,
                level => Enumerable.Range(0, map.Height)
                    .SelectMany(y => Enumerable.Range(0, map.Width)
                        .Select(x => map.GetCaveCell(new GridPosition(x, y, -level))))
                    .Count(cell => cell.Deposit != MineralDepositKind.None));
        Assert.True(depositCountsByLevel[2] > depositCountsByLevel[1]);

        var offsets = new[]
        {
            new GridPosition(0, -1),
            new GridPosition(1, 0),
            new GridPosition(0, 1),
            new GridPosition(-1, 0),
        };
        var exposedDeposits = Enumerable.Range(1, map.CaveLevelCount)
            .SelectMany(level => Enumerable.Range(0, map.Height)
                .SelectMany(y => Enumerable.Range(0, map.Width)
                    .Select(x => new GridPosition(x, y, -level))))
            .Where(position => map.GetCaveCell(position).Kind == CaveCellKind.SolidRock)
            .Where(position => offsets.Any(offset =>
            {
                var neighbor = new GridPosition(
                    position.X + offset.X,
                    position.Y + offset.Y,
                    position.Z);
                return map.IsCavePosition(neighbor) && map.GetCaveCell(neighbor).IsOpen;
            }))
            .Select(position => map.GetCaveCell(position).Deposit)
            .ToHashSet();
        Assert.Contains(MineralDepositKind.Coal, exposedDeposits);
        Assert.Contains(MineralDepositKind.IronOre, exposedDeposits);

        var historical = SwampMapGenerator.Generate(
            new WorldSeed(0x5645494E53UL),
            64,
            64,
            generatorVersion: 7);
        Assert.DoesNotContain(
            Enumerable.Range(1, historical.CaveLevelCount)
                .SelectMany(level => Enumerable.Range(0, historical.Height)
                    .SelectMany(y => Enumerable.Range(0, historical.Width)
                        .Select(x => historical.GetCaveCell(new GridPosition(x, y, -level))))),
            cell => cell.Deposit != MineralDepositKind.None);
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
