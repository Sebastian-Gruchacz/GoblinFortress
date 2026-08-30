namespace GoblinStronghold.Simulation.Map;

public static class SwampMapGenerator
{
    public const int CurrentVersion = 14;
    public const int MinimumInitialCaveLevelCount = 2;
    public const int DefaultDimension = 96;
    public const int MinimumDimension = 16;
    public const int MaximumDimension = 2_048;

    public static bool SupportsVersion(int version) => version is >= 1 and <= CurrentVersion;

    public static GeneratedMap Generate(
        WorldSeed seed,
        int width,
        int height,
        int generatorVersion = CurrentVersion)
    {
        if (!SupportsVersion(generatorVersion))
        {
            throw new ArgumentOutOfRangeException(
                nameof(generatorVersion),
                generatorVersion,
                "The requested map generator version is not supported.");
        }

        ValidateDimensions(width, height);

        var cells = new MapCell[checked(width * height)];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = checked((y * width) + x);
                cells[index] = generatorVersion >= 4
                    ? GenerateRegionalCell(seed, x, y, width, height, generatorVersion)
                    : GenerateLegacyCell(seed, index, generatorVersion);
            }
        }
        if (generatorVersion >= 5)
        {
            ApplyTerrainRelief(cells, seed, width, height, generatorVersion);
        }

        var goblinSpawn = generatorVersion >= 4
            ? CreateRegionalSettlementPosition(seed, width, height, human: false)
            : new GridPosition(
                Math.Max(2, width / 6),
                DeterministicRandom.NextInt(
                    seed,
                    RandomDomain.MapGeneration,
                    EntityId.None,
                    SimulationTick.Zero,
                    sampleKey: 10_001,
                    minimumInclusive: 2,
                    maximumExclusive: height - 2));

        var humanVillage = generatorVersion >= 4
            ? CreateRegionalSettlementPosition(seed, width, height, human: true)
            : new GridPosition(
                Math.Min(width - 3, width - (width / 6) - 1),
                DeterministicRandom.NextInt(
                    seed,
                    RandomDomain.MapGeneration,
                    EntityId.None,
                    SimulationTick.Zero,
                    sampleKey: 10_002,
                    minimumInclusive: 2,
                    maximumExclusive: height - 2));

        if (generatorVersion >= 2)
        {
            CarveSettlementPad(
                cells, seed, width, height, goblinSpawn, goblin: true, generatorVersion);
            CarveSettlementPad(
                cells, seed, width, height, humanVillage, goblin: false, generatorVersion);
        }

        CarveSettlementAccess(
            cells,
            width,
            goblinSpawn,
            humanVillage,
            preserveWater: generatorVersion >= 5);
        SetCell(cells, width, goblinSpawn, CreateMud(moisture: 92, fertility: 72));
        SetCell(cells, width, humanVillage, CreateGround(moisture: 48, fertility: 68));
        SetCell(cells, width, goblinSpawn with { Y = goblinSpawn.Y + 1 }, CreateShallowWater());
        SetCell(cells, width, humanVillage with { Y = humanVillage.Y + 1 }, CreateShallowWater());
        if (generatorVersion >= 4)
        {
            SetCell(cells, width, new GridPosition(width - 1, height - 1), CreateDeepWater());
        }
        if (generatorVersion >= 5)
        {
            AssignTerrainRamps(cells, seed, width, height);
        }
        var caves = generatorVersion >= 6
            ? GenerateCaves(
                cells,
                seed,
                width,
                height,
                goblinSpawn,
                humanVillage,
                generatorVersion)
            : new CaveGenerationResult([], []);

        var map = new GeneratedMap(
            width,
            height,
            seed,
            generatorVersion,
            cells,
            goblinSpawn,
            humanVillage,
            caves.Cells,
            caves.Passages);
        var validation = SwampMapValidator.Validate(map);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"Generated map failed validation: {string.Join("; ", validation.Errors)}");
        }

        return map;
    }

    private static MapCell GenerateLegacyCell(WorldSeed seed, int index, int generatorVersion)
    {
        var subject = new EntityId(checked((ulong)index + 1));
        var moisture = DeterministicRandom.NextInt(
            seed,
            RandomDomain.MapGeneration,
            subject,
            SimulationTick.Zero,
            sampleKey: 1,
            minimumInclusive: 0,
            maximumExclusive: 101);
        var terrainVariation = DeterministicRandom.NextInt(
            seed,
            RandomDomain.MapGeneration,
            subject,
            SimulationTick.Zero,
            sampleKey: 2,
            minimumInclusive: 0,
            maximumExclusive: 101);
        var fertilityVariation = DeterministicRandom.NextInt(
            seed,
            RandomDomain.MapGeneration,
            subject,
            SimulationTick.Zero,
            sampleKey: 3,
            minimumInclusive: 0,
            maximumExclusive: 101);

        var waterScore = ((moisture * 2) + terrainVariation) / 3;
        var fertility = checked((byte)((moisture + fertilityVariation) / 2));
        var moistureByte = checked((byte)moisture);

        return waterScore switch
        {
            >= 86 => new MapCell(
                TerrainKind.DeepWater,
                moistureByte,
                fertility,
                TraversalCost: 0,
                FloorLevel: generatorVersion >= 3 ? (sbyte)-1 : (sbyte)0),
            >= 70 => new MapCell(TerrainKind.ShallowWater, moistureByte, fertility, TraversalCost: 4),
            >= 38 => new MapCell(TerrainKind.Mud, moistureByte, fertility, TraversalCost: 3),
            _ => new MapCell(TerrainKind.SolidGround, moistureByte, fertility, TraversalCost: 1),
        };
    }

    private static MapCell GenerateRegionalCell(
        WorldSeed seed,
        int x,
        int y,
        int width,
        int height,
        int generatorVersion)
    {
        var normalizedX = width == 1 ? 0d : x / (double)(width - 1);
        var normalizedY = height == 1 ? 0d : y / (double)(height - 1);
        var terrainNoise = FractalValueNoise(seed, normalizedX, normalizedY, sampleKey: 20_000);
        var moistureNoise = FractalValueNoise(seed, normalizedX, normalizedY, sampleKey: 21_000);
        var riverMeander =
            (FractalValueNoise(seed, normalizedX, 0.5d, sampleKey: 22_000) - 0.5d) * 0.11d;
        var riverCenterY = 0.82d - (0.64d * normalizedX) + riverMeander;
        var riverHalfWidth = Math.Max(1.5d, Math.Min(width, height) * 0.035d);
        var riverDistance = Math.Abs(normalizedY - riverCenterY) * height;
        var swampSampleX = normalizedX;
        var swampSampleY = normalizedY;
        if (generatorVersion >= 7)
        {
            var boundaryWarpX =
                FractalValueNoise(seed, normalizedX * 0.68d, normalizedY * 0.68d, 29_000) - 0.5d;
            var boundaryWarpY =
                FractalValueNoise(seed, normalizedX * 0.68d, normalizedY * 0.68d, 29_100) - 0.5d;
            swampSampleX += (boundaryWarpX * 0.18d) + ((terrainNoise - 0.5d) * 0.035d);
            swampSampleY += (boundaryWarpY * 0.16d) + ((moistureNoise - 0.5d) * 0.03d);
            var riverBankNoise =
                FractalValueNoise(seed, normalizedX, normalizedY, sampleKey: 29_200) - 0.5d;
            riverDistance += riverBankNoise * riverHalfWidth * 0.9d;
        }

        if (riverDistance <= riverHalfWidth * 0.48d)
        {
            return CreateDeepWater();
        }

        if (riverDistance <= riverHalfWidth)
        {
            return CreateShallowWater();
        }

        var leftSwamp = Math.Clamp((0.48d - swampSampleX) / 0.48d, 0d, 1d);
        var bottomSwamp = Math.Clamp((swampSampleY - 0.58d) / 0.42d, 0d, 1d);
        var swampInfluence = Math.Max(leftSwamp, bottomSwamp);
        var wetness = (swampInfluence * 0.68d) + (moistureNoise * 0.32d);
        var moisture = checked((byte)Math.Clamp(
            (int)Math.Round(35d + (swampInfluence * 48d) + (moistureNoise * 17d)),
            0,
            100));
        var fertility = checked((byte)Math.Clamp(
            (int)Math.Round(46d + (moistureNoise * 30d) + (terrainNoise * 18d)),
            0,
            100));

        if (swampInfluence > 0.62d && wetness > 0.72d && terrainNoise > 0.76d)
        {
            return new MapCell(
                TerrainKind.DeepWater,
                moisture,
                fertility,
                TraversalCost: 0,
                FloorLevel: -1);
        }

        if (swampInfluence > 0.45d && wetness > 0.68d)
        {
            return new MapCell(TerrainKind.ShallowWater, moisture, fertility, TraversalCost: 4);
        }

        if (swampInfluence > 0.18d || wetness > 0.56d)
        {
            return CreateMud(moisture, fertility);
        }

        return CreateGround(moisture, fertility);
    }

    private static GridPosition CreateRegionalSettlementPosition(
        WorldSeed seed,
        int width,
        int height,
        bool human)
    {
        var jitterX = DeterministicRandom.NextInt(
            seed,
            RandomDomain.MapGeneration,
            EntityId.None,
            SimulationTick.Zero,
            sampleKey: human ? 23_001UL : 23_003UL,
            minimumInclusive: -1,
            maximumExclusive: 2);
        var jitterY = DeterministicRandom.NextInt(
            seed,
            RandomDomain.MapGeneration,
            EntityId.None,
            SimulationTick.Zero,
            sampleKey: human ? 23_002UL : 23_004UL,
            minimumInclusive: -1,
            maximumExclusive: 2);
        var normalizedX = human ? 0.82d : 0.16d;
        var normalizedY = human ? 0.2d : 0.76d;
        return new GridPosition(
            Math.Clamp((int)Math.Round((width - 1) * normalizedX) + jitterX, 2, width - 3),
            Math.Clamp((int)Math.Round((height - 1) * normalizedY) + jitterY, 2, height - 3));
    }

    private static double FractalValueNoise(
        WorldSeed seed,
        double x,
        double y,
        ulong sampleKey)
    {
        var value = 0d;
        var amplitude = 1d;
        var amplitudeSum = 0d;
        var frequency = 3d;
        for (var octave = 0; octave < 4; octave++)
        {
            value += ValueNoise(seed, x * frequency, y * frequency, sampleKey + (ulong)octave) * amplitude;
            amplitudeSum += amplitude;
            amplitude *= 0.5d;
            frequency *= 2d;
        }

        return value / amplitudeSum;
    }

    private static double ValueNoise(WorldSeed seed, double x, double y, ulong sampleKey)
    {
        var minimumX = (int)Math.Floor(x);
        var minimumY = (int)Math.Floor(y);
        var blendX = Smooth(x - minimumX);
        var blendY = Smooth(y - minimumY);
        var top = Lerp(
            SampleLattice(seed, minimumX, minimumY, sampleKey),
            SampleLattice(seed, minimumX + 1, minimumY, sampleKey),
            blendX);
        var bottom = Lerp(
            SampleLattice(seed, minimumX, minimumY + 1, sampleKey),
            SampleLattice(seed, minimumX + 1, minimumY + 1, sampleKey),
            blendX);
        return Lerp(top, bottom, blendY);
    }

    private static double SampleLattice(WorldSeed seed, int x, int y, ulong sampleKey)
    {
        var packed = ((ulong)(uint)x << 32) | (uint)y;
        var sample = DeterministicRandom.Sample(
            seed,
            RandomDomain.MapGeneration,
            new EntityId(packed),
            SimulationTick.Zero,
            sampleKey);
        return (sample >> 11) * (1d / (1UL << 53));
    }

    private static double Smooth(double value) => value * value * (3d - (2d * value));

    private static double Lerp(double start, double end, double amount) =>
        start + ((end - start) * amount);

    private static void CarveSettlementAccess(
        MapCell[] cells,
        int width,
        GridPosition start,
        GridPosition destination,
        bool preserveWater = false)
    {
        var xStep = start.X <= destination.X ? 1 : -1;
        for (var x = start.X; x != destination.X + xStep; x += xStep)
        {
            SetTraversableIfNeeded(cells, width, new GridPosition(x, start.Y), preserveWater);
        }

        var yStep = start.Y <= destination.Y ? 1 : -1;
        for (var y = start.Y; y != destination.Y + yStep; y += yStep)
        {
            SetTraversableIfNeeded(cells, width, new GridPosition(destination.X, y), preserveWater);
        }
    }

    private static void CarveSettlementPad(
        MapCell[] cells,
        WorldSeed seed,
        int width,
        int height,
        GridPosition center,
        bool goblin,
        int generatorVersion)
    {
        const int padWidth = 8;
        const int padHeight = 9;
        var startX = Math.Clamp(center.X - (padWidth / 2), 0, width - padWidth);
        var startY = Math.Clamp(center.Y - (padHeight / 2), 0, height - padHeight);

        for (var y = startY; y < startY + padHeight; y++)
        {
            for (var x = startX; x < startX + padWidth; x++)
            {
                if (generatorVersion >= 7 && Math.Min(width, height) >= 32)
                {
                    var radiusX = padWidth * 0.54d;
                    var radiusY = padHeight * 0.54d;
                    var distanceX = (x - center.X) / radiusX;
                    var distanceY = (y - center.Y) / radiusY;
                    var edgeNoise =
                        (FractalValueNoise(
                            seed,
                            x / (double)(width - 1),
                            y / (double)(height - 1),
                            goblin ? 29_300UL : 29_400UL) - 0.5d) * 0.42d;
                    if ((distanceX * distanceX) + (distanceY * distanceY) > 1d + edgeNoise)
                    {
                        continue;
                    }
                }

                SetCell(
                    cells,
                    width,
                    new GridPosition(x, y),
                    goblin
                        ? CreateMud(moisture: 82, fertility: 66)
                        : CreateGround(moisture: 52, fertility: 70));
            }
        }
    }

    private static void SetTraversableIfNeeded(
        MapCell[] cells,
        int width,
        GridPosition position,
        bool preserveWater)
    {
        var index = checked((position.Y * width) + position.X);
        if (cells[index].Terrain == TerrainKind.DeepWater && preserveWater)
        {
            cells[index] = CreateShallowWater();
        }
        else if (!cells[index].IsTraversable)
        {
            cells[index] = CreateMud(moisture: 78, fertility: 55);
        }
        else if (preserveWater && cells[index].Terrain is not TerrainKind.ShallowWater)
        {
            cells[index] = cells[index] with
            {
                FloorLevel = 0,
                SurfaceLevel = 0,
                RampDirection = TerrainRampDirection.None,
            };
        }
    }

    private static void ApplyTerrainRelief(
        MapCell[] cells,
        WorldSeed seed,
        int width,
        int height,
        int generatorVersion)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = checked((y * width) + x);
                var cell = cells[index];
                if (cell.Terrain == TerrainKind.DeepWater)
                {
                    var depthNoise = FractalValueNoise(
                        seed,
                        x / (double)(width - 1),
                        y / (double)(height - 1),
                        sampleKey: 24_000);
                    cells[index] = cell with { FloorLevel = depthNoise > 0.72d ? (sbyte)-2 : (sbyte)-1 };
                    continue;
                }
                if (cell.Terrain == TerrainKind.ShallowWater)
                {
                    continue;
                }

                var normalizedX = x / (double)(width - 1);
                var normalizedY = y / (double)(height - 1);
                var broadRelief = FractalValueNoise(seed, normalizedX, normalizedY, sampleKey: 25_000);
                var ridgeNoise = FractalValueNoise(seed, normalizedX, normalizedY, sampleKey: 26_000);
                var riverCenterY = 0.82d - (0.64d * normalizedX) +
                    ((FractalValueNoise(seed, normalizedX, 0.5d, sampleKey: 22_000) - 0.5d) * 0.11d);
                var riverDistance = Math.Abs(normalizedY - riverCenterY) * height;
                var valleySuppression = Math.Clamp(riverDistance / (Math.Min(width, height) * 0.18d), 0d, 1d);
                var uplandInfluence = Math.Clamp((normalizedX + (1d - normalizedY) - 0.72d) / 0.9d, 0d, 1d);
                var foothillInfluence = generatorVersion >= 11
                    ? RadialInfluence(
                        normalizedX,
                        normalizedY,
                        centerX: 0.22d,
                        centerY: 0.6d,
                        radiusX: 0.16d,
                        radiusY: 0.18d)
                    : 0d;
                var reliefScore = (broadRelief * 0.5d) + (ridgeNoise * 0.2d) +
                    (uplandInfluence * 0.42d) + (foothillInfluence * 0.72d) -
                    ((1d - valleySuppression) * 0.38d);
                var highThreshold = generatorVersion >= 11 ? 0.79d : 0.83d;
                var raisedThreshold = generatorVersion >= 11 ? 0.55d : 0.59d;
                var level = reliefScore >= highThreshold
                    ? (sbyte)2
                    : reliefScore >= raisedThreshold
                        ? (sbyte)1
                        : reliefScore <= 0.19d && riverDistance > 5d
                            ? (sbyte)-1
                            : (sbyte)0;
                cells[index] = cell with
                {
                    FloorLevel = level,
                    SurfaceLevel = level,
                    RampDirection = TerrainRampDirection.None,
                };
            }
        }
    }

    private static double RadialInfluence(
        double x,
        double y,
        double centerX,
        double centerY,
        double radiusX,
        double radiusY)
    {
        var offsetX = (x - centerX) / radiusX;
        var offsetY = (y - centerY) / radiusY;
        var distance = Math.Sqrt((offsetX * offsetX) + (offsetY * offsetY));
        return Smooth(Math.Clamp(1d - distance, 0d, 1d));
    }

    private static void AssignTerrainRamps(
        MapCell[] cells,
        WorldSeed seed,
        int width,
        int height)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = checked((y * width) + x);
                var cell = cells[index];
                if (!cell.IsTraversable ||
                    cell.Terrain is TerrainKind.ShallowWater or TerrainKind.DeepWater)
                {
                    continue;
                }

                var position = new GridPosition(x, y);
                var uphill = CardinalNeighbors(position, width, height)
                    .Where(neighbor =>
                    {
                        var neighborCell = cells[checked((neighbor.Y * width) + neighbor.X)];
                        return neighborCell.IsTraversable &&
                            neighborCell.SurfaceLevel == cell.SurfaceLevel + 1;
                    })
                    .OrderBy(neighbor => DeterministicRandom.Sample(
                        seed,
                        RandomDomain.MapGeneration,
                        new EntityId(checked((ulong)index + 1)),
                        SimulationTick.Zero,
                        sampleKey: checked(27_000UL + (ulong)((neighbor.Y * width) + neighbor.X))))
                    .Select(neighbor => (GridPosition?)neighbor)
                    .FirstOrDefault();
                if (uphill is null ||
                    DeterministicRandom.NextInt(
                        seed,
                        RandomDomain.MapGeneration,
                        new EntityId(checked((ulong)index + 1)),
                        SimulationTick.Zero,
                        sampleKey: 27_999,
                        minimumInclusive: 0,
                        maximumExclusive: 100) >= 72)
                {
                    continue;
                }

                cells[index] = cell with { RampDirection = DirectionFrom(position, uphill.Value) };
            }
        }
    }

    private static IEnumerable<GridPosition> CardinalNeighbors(
        GridPosition position,
        int width,
        int height)
    {
        if (position.Y > 0) yield return position with { Y = position.Y - 1 };
        if (position.X + 1 < width) yield return position with { X = position.X + 1 };
        if (position.Y + 1 < height) yield return position with { Y = position.Y + 1 };
        if (position.X > 0) yield return position with { X = position.X - 1 };
    }

    private static TerrainRampDirection DirectionFrom(GridPosition from, GridPosition to) =>
        (to.X - from.X, to.Y - from.Y) switch
        {
            (0, -1) => TerrainRampDirection.North,
            (1, 0) => TerrainRampDirection.East,
            (0, 1) => TerrainRampDirection.South,
            (-1, 0) => TerrainRampDirection.West,
            _ => TerrainRampDirection.None,
        };

    private static CaveGenerationResult GenerateCaves(
        MapCell[] surfaceCells,
        WorldSeed seed,
        int width,
        int height,
        GridPosition goblinSpawn,
        GridPosition humanVillage,
        int generatorVersion)
    {
        var depthLevels = generatorVersion >= 13
            ? Math.Max(MinimumInitialCaveLevelCount, -surfaceCells.Min(cell => cell.FloorLevel))
            : 2;
        var cellCount = checked(width * height);
        var caveCells = new CaveCell[checked(cellCount * depthLevels)];
        for (var levelIndex = 0; levelIndex < depthLevels; levelIndex++)
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    caveCells[(levelIndex * cellCount) + (y * width) + x] =
                        GenerateSolidCaveCell(
                            seed,
                            width,
                            height,
                            x,
                            y,
                            levelIndex,
                            generatorVersion);
                }
            }
        }

        var entrance = FindCaveEntrance(
            surfaceCells,
            seed,
            width,
            height,
            goblinSpawn,
            humanVillage,
            generatorVersion);
        var horizontalSign = DeterministicRandom.NextInt(
            seed,
            RandomDomain.MapGeneration,
            EntityId.None,
            SimulationTick.Zero,
            sampleKey: 28_100,
            minimumInclusive: 0,
            maximumExclusive: 2) == 0 ? -1 : 1;
        var verticalSign = DeterministicRandom.NextInt(
            seed,
            RandomDomain.MapGeneration,
            EntityId.None,
            SimulationTick.Zero,
            sampleKey: 28_101,
            minimumInclusive: 0,
            maximumExclusive: 2) == 0 ? -1 : 1;
        var spanX = Math.Max(3, Math.Min(8, width / 6));
        var spanY = Math.Max(3, Math.Min(7, height / 7));
        var firstRoom = entrance with { Z = -1 };
        var secondRoom = FindAvailableCavePosition(
            surfaceCells,
            ClampCavePosition(
            firstRoom with
            {
                X = firstRoom.X + (spanX * horizontalSign),
                Y = firstRoom.Y + (2 * verticalSign),
            },
            width,
            height),
            width,
            height,
            generatorVersion);
        var descent = FindAvailableCavePosition(
            surfaceCells,
            ClampCavePosition(
            secondRoom with
            {
                X = secondRoom.X + (spanX * horizontalSign),
                Y = secondRoom.Y + (spanY * verticalSign),
            },
            width,
            height),
            width,
            height,
            generatorVersion);
        CarveCaveChamber(caveCells, width, height, firstRoom, radiusX: 2, radiusY: 2);
        CarveCaveCorridor(
            caveCells,
            surfaceCells,
            width,
            height,
            firstRoom,
            secondRoom,
            generatorVersion);
        CarveCaveChamber(caveCells, width, height, secondRoom, radiusX: 3, radiusY: 2);
        CarveCaveCorridor(
            caveCells,
            surfaceCells,
            width,
            height,
            secondRoom,
            descent,
            generatorVersion);
        CarveCaveChamber(caveCells, width, height, descent, radiusX: 2, radiusY: 3);

        var deepStart = descent with { Z = -2 };
        var deepMiddle = ClampCavePosition(
            deepStart with
            {
                X = deepStart.X - (spanX * horizontalSign),
                Y = deepStart.Y + (spanY * verticalSign),
            },
            width,
            height);
        var deepEnd = ClampCavePosition(
            deepMiddle with
            {
                X = deepMiddle.X - (spanX * horizontalSign),
                Y = deepMiddle.Y + (3 * verticalSign),
            },
            width,
            height);
        CarveCaveChamber(caveCells, width, height, deepStart, radiusX: 2, radiusY: 2);
        CarveCaveCorridor(
            caveCells,
            surfaceCells,
            width,
            height,
            deepStart,
            deepMiddle,
            generatorVersion);
        CarveCaveChamber(caveCells, width, height, deepMiddle, radiusX: 3, radiusY: 2);
        CarveCaveCorridor(
            caveCells,
            surfaceCells,
            width,
            height,
            deepMiddle,
            deepEnd,
            generatorVersion);
        CarveCaveChamber(caveCells, width, height, deepEnd, radiusX: 3, radiusY: 3);

        SetCaveKind(caveCells, width, height, firstRoom, CaveCellKind.Ramp);
        SetCaveKind(caveCells, width, height, descent, CaveCellKind.Ramp);
        SetCaveKind(caveCells, width, height, deepStart, CaveCellKind.Ramp);
        if (generatorVersion >= 8)
        {
            if (generatorVersion < 13)
            {
                AssignMineralDeposits(
                    caveCells,
                    seed,
                    width,
                    height,
                    generatorVersion);
            }
            if (generatorVersion >= 10)
            {
                EnsureExposedMineralDeposit(
                    caveCells,
                    seed,
                    width,
                    height,
                    MineralDepositKind.Coal);
                EnsureExposedMineralDeposit(
                    caveCells,
                    seed,
                    width,
                    height,
                    MineralDepositKind.IronOre);
            }
        }
        return new CaveGenerationResult(
            caveCells,
            [
                new VerticalPassage(entrance, firstRoom, VerticalPassageKind.CaveMouth),
                new VerticalPassage(descent, deepStart, VerticalPassageKind.NaturalRamp),
            ]);
    }

    internal static CaveCell GenerateSolidCaveCell(
        WorldSeed seed,
        int width,
        int height,
        int x,
        int y,
        int levelIndex,
        int generatorVersion)
    {
        var normalizedX = x / (double)(width - 1);
        var normalizedY = y / (double)(height - 1);
        var rockNoise = FractalValueNoise(
            seed,
            normalizedX,
            normalizedY,
            sampleKey: checked(28_000UL + (ulong)levelIndex));
        var depth = levelIndex + 1;
        var rock = generatorVersion >= 14
            ? SelectDeepRock(rockNoise, depth)
            : rockNoise + (levelIndex * 0.12d) >= 0.57d
                ? RockKind.Granite
                : RockKind.Sandstone;
        if (generatorVersion < 13)
        {
            return new CaveCell(rock, CaveCellKind.SolidRock);
        }

        if (generatorVersion >= 14 && depth >= 12 &&
            IsLavaCell(seed, normalizedX, normalizedY, depth))
        {
            return new CaveCell(
                rock,
                CaveCellKind.Floor,
                MineralDepositKind.None,
                CellFluidKind.Lava);
        }

        if (generatorVersion >= 14)
        {
            return new CaveCell(
                rock,
                CaveCellKind.SolidRock,
                SelectDeepDeposit(seed, normalizedX, normalizedY, depth));
        }
        var coalThreshold = levelIndex == 0 ? 0.68d : 0.62d;
        var ironThreshold = levelIndex == 0
            ? 0.76d
            : Math.Max(0.56d, 0.68d - (levelIndex * 0.015d));
        var coalNoise = FractalValueNoise(seed, normalizedX, normalizedY, sampleKey: 29_100UL);
        var ironNoise = FractalValueNoise(seed, normalizedX, normalizedY, sampleKey: 29_200UL);
        var deposit = ironNoise >= ironThreshold && ironNoise - ironThreshold >=
            coalNoise - coalThreshold
            ? MineralDepositKind.IronOre
            : coalNoise >= coalThreshold
                ? MineralDepositKind.Coal
                : MineralDepositKind.None;
        return new CaveCell(rock, CaveCellKind.SolidRock, deposit);
    }

    private static RockKind SelectDeepRock(double noise, int depth) => depth switch
    {
        >= 16 => noise >= 0.48d ? RockKind.Obsidian : RockKind.Basalt,
        >= 12 => noise >= 0.3d ? RockKind.Basalt : RockKind.Granite,
        >= 8 when noise >= 0.76d => RockKind.Basalt,
        _ => noise + (Math.Min(depth - 1, 4) * 0.08d) >= 0.57d
            ? RockKind.Granite
            : RockKind.Sandstone,
    };

    private static bool IsLavaCell(
        WorldSeed seed,
        double x,
        double y,
        int depth)
    {
        var lakeNoise = FractalValueNoise(
            seed,
            x + (depth * 0.013d),
            y - (depth * 0.009d),
            sampleKey: 30_100UL);
        var streamNoise = FractalValueNoise(
            seed,
            x * 0.72d,
            y * 0.72d,
            sampleKey: 30_200UL);
        var lakeThreshold = depth >= 16 ? 0.67d : 0.84d;
        var streamHalfWidth = depth >= 16 ? 0.055d : 0.014d;
        return lakeNoise >= lakeThreshold || Math.Abs(streamNoise - 0.5d) <= streamHalfWidth;
    }

    private static MineralDepositKind SelectDeepDeposit(
        WorldSeed seed,
        double x,
        double y,
        int depth)
    {
        var gemNoise = FractalValueNoise(seed, x, y, sampleKey: 30_900UL);
        if (depth >= 18 && gemNoise >= 0.76d)
        {
            return MineralDepositKind.Diamond;
        }
        if (depth >= 16 && gemNoise >= 0.70d)
        {
            return gemNoise >= 0.73d ? MineralDepositKind.Emerald : MineralDepositKind.Ruby;
        }

        var preciousNoise = FractalValueNoise(seed, x, y, sampleKey: 30_800UL);
        if (depth >= 14 && preciousNoise >= 0.70d)
        {
            return MineralDepositKind.GoldOre;
        }
        if (depth >= 10 && preciousNoise >= 0.64d)
        {
            return MineralDepositKind.SilverOre;
        }
        if (depth >= 6 && preciousNoise >= 0.58d)
        {
            return MineralDepositKind.CopperOre;
        }

        var coalNoise = FractalValueNoise(seed, x, y, sampleKey: 29_100UL);
        var ironNoise = FractalValueNoise(seed, x, y, sampleKey: 29_200UL);
        var ironThreshold = Math.Max(0.54d, 0.7d - (depth * 0.012d));
        return ironNoise >= ironThreshold && ironNoise - ironThreshold >= coalNoise - 0.62d
            ? MineralDepositKind.IronOre
            : coalNoise >= 0.62d
                ? MineralDepositKind.Coal
                : MineralDepositKind.None;
    }

    private static void AssignMineralDeposits(
        CaveCell[] cells,
        WorldSeed seed,
        int width,
        int height,
        int generatorVersion)
    {
        var cellCount = checked(width * height);
        for (var levelIndex = 0; levelIndex < 2; levelIndex++)
        {
            var coalThreshold = levelIndex == 0 ? 0.68d : 0.62d;
            var ironThreshold = levelIndex == 0 ? 0.76d : 0.68d;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = (levelIndex * cellCount) + (y * width) + x;
                    if (cells[index].Kind != CaveCellKind.SolidRock)
                    {
                        continue;
                    }

                    var normalizedX = x / (double)(width - 1);
                    var normalizedY = y / (double)(height - 1);
                    var coalNoise = FractalValueNoise(
                        seed,
                        normalizedX,
                        normalizedY,
                        sampleKey: generatorVersion >= 12
                            ? 29_100UL
                            : checked(29_100UL + (ulong)levelIndex));
                    var ironNoise = FractalValueNoise(
                        seed,
                        normalizedX,
                        normalizedY,
                        sampleKey: generatorVersion >= 12
                            ? 29_200UL
                            : checked(29_200UL + (ulong)levelIndex));
                    var deposit = ironNoise >= ironThreshold && ironNoise - ironThreshold >=
                        coalNoise - coalThreshold
                        ? MineralDepositKind.IronOre
                        : coalNoise >= coalThreshold
                            ? MineralDepositKind.Coal
                            : MineralDepositKind.None;
                    cells[index] = cells[index] with { Deposit = deposit };
                }
            }
        }
    }

    private static void EnsureExposedMineralDeposit(
        CaveCell[] cells,
        WorldSeed seed,
        int width,
        int height,
        MineralDepositKind deposit)
    {
        var cellCount = checked(width * height);
        var exposedRock = (
                from level in Enumerable.Range(1, 2)
                from y in Enumerable.Range(1, height - 2)
                from x in Enumerable.Range(1, width - 2)
                let position = new GridPosition(x, y, -level)
                let index = ((level - 1) * cellCount) + (y * width) + x
                where cells[index].Kind == CaveCellKind.SolidRock &&
                    CardinalNeighbors(position, width, height).Any(neighbor =>
                    {
                        var neighborIndex = ((level - 1) * cellCount) +
                            (neighbor.Y * width) + neighbor.X;
                        return cells[neighborIndex].IsOpen;
                    })
                select (Position: position, Index: index))
            .ToArray();
        if (exposedRock.Any(candidate => cells[candidate.Index].Deposit == deposit))
        {
            return;
        }

        var selected = exposedRock
            .Where(candidate => cells[candidate.Index].Deposit == MineralDepositKind.None)
            .OrderBy(candidate => DeterministicRandom.Sample(
                seed,
                RandomDomain.MapGeneration,
                new EntityId(checked((ulong)candidate.Index + 1)),
                SimulationTick.Zero,
                sampleKey: checked(29_300UL + (ulong)deposit)))
            .FirstOrDefault();
        if (selected != default)
        {
            cells[selected.Index] = cells[selected.Index] with { Deposit = deposit };
        }
    }

    private static GridPosition FindCaveEntrance(
        MapCell[] surfaceCells,
        WorldSeed seed,
        int width,
        int height,
        GridPosition goblinSpawn,
        GridPosition humanVillage,
        int generatorVersion)
    {
        var margin = Math.Min(4, Math.Max(2, (Math.Min(width, height) - 1) / 4));
        var candidates = (
                from y in Enumerable.Range(margin, height - (margin * 2))
                from x in Enumerable.Range(margin, width - (margin * 2))
                let position = new GridPosition(x, y)
                let cell = surfaceCells[(y * width) + x]
                where cell.IsTraversable &&
                    cell.Terrain is TerrainKind.SolidGround or TerrainKind.Mud &&
                    cell.RampDirection == TerrainRampDirection.None &&
                    (generatorVersion < 10 || cell.SurfaceLevel == 0) &&
                    Distance(position, goblinSpawn) >= 4 &&
                    Distance(position, humanVillage) >= 4
                orderby generatorVersion >= 10
                        ? Math.Min(
                            Distance(position, goblinSpawn),
                            Distance(position, humanVillage))
                        : cell.SurfaceLevel descending,
                    DeterministicRandom.Sample(
                        seed,
                        RandomDomain.MapGeneration,
                        new EntityId(checked((ulong)((y * width) + x) + 1)),
                        SimulationTick.Zero,
                        sampleKey: 28_200)
                select position)
            .ToArray();
        return candidates.FirstOrDefault(goblinSpawn);
    }

    private static GridPosition ClampCavePosition(
        GridPosition position,
        int width,
        int height) => position with
    {
        X = Math.Clamp(position.X, 2, width - 3),
        Y = Math.Clamp(position.Y, 2, height - 3),
    };

    private static GridPosition FindAvailableCavePosition(
        MapCell[] surfaceCells,
        GridPosition preferred,
        int width,
        int height,
        int generatorVersion)
    {
        if (!IsCaveColumnBlocked(
                surfaceCells,
                preferred,
                width,
                generatorVersion))
        {
            return preferred;
        }

        return (
                from y in Enumerable.Range(2, height - 4)
                from x in Enumerable.Range(2, width - 4)
                let position = new GridPosition(x, y, preferred.Z)
                where !IsCaveColumnBlocked(
                    surfaceCells,
                    position,
                    width,
                    generatorVersion)
                orderby Distance(position, preferred), y, x
                select position)
            .First();
    }

    private static void CarveCaveChamber(
        CaveCell[] cells,
        int width,
        int height,
        GridPosition center,
        int radiusX,
        int radiusY)
    {
        for (var y = center.Y - radiusY; y <= center.Y + radiusY; y++)
        {
            for (var x = center.X - radiusX; x <= center.X + radiusX; x++)
            {
                var normalizedX = (x - center.X) / (double)radiusX;
                var normalizedY = (y - center.Y) / (double)radiusY;
                if ((normalizedX * normalizedX) + (normalizedY * normalizedY) <= 1.18d)
                {
                    SetCaveKind(
                        cells,
                        width,
                        height,
                        new GridPosition(x, y, center.Z),
                        CaveCellKind.Floor);
                }
            }
        }
    }

    private static void CarveCaveCorridor(
        CaveCell[] cells,
        MapCell[] surfaceCells,
        int width,
        int height,
        GridPosition start,
        GridPosition end,
        int generatorVersion)
    {
        if (generatorVersion >= 10 && start.Z == -1 &&
            DirectCaveCorridorCrossesBlockedColumn(
                surfaceCells,
                width,
                start,
                end,
                generatorVersion))
        {
            CarveCaveCorridorAroundBlockedColumns(
                cells,
                surfaceCells,
                width,
                height,
                start,
                end,
                generatorVersion);
            return;
        }

        var current = start;
        SetCaveKind(cells, width, height, current, CaveCellKind.Floor);
        while (current.X != end.X)
        {
            current = current with { X = current.X + Math.Sign(end.X - current.X) };
            SetCaveKind(cells, width, height, current, CaveCellKind.Floor);
        }
        while (current.Y != end.Y)
        {
            current = current with { Y = current.Y + Math.Sign(end.Y - current.Y) };
            SetCaveKind(cells, width, height, current, CaveCellKind.Floor);
        }
    }

    private static bool DirectCaveCorridorCrossesBlockedColumn(
        MapCell[] surfaceCells,
        int width,
        GridPosition start,
        GridPosition end,
        int generatorVersion)
    {
        var current = start;
        while (current.X != end.X)
        {
            current = current with { X = current.X + Math.Sign(end.X - current.X) };
            if (IsCaveColumnBlocked(surfaceCells, current, width, generatorVersion))
            {
                return true;
            }
        }
        while (current.Y != end.Y)
        {
            current = current with { Y = current.Y + Math.Sign(end.Y - current.Y) };
            if (IsCaveColumnBlocked(surfaceCells, current, width, generatorVersion))
            {
                return true;
            }
        }

        return false;
    }

    private static void CarveCaveCorridorAroundBlockedColumns(
        CaveCell[] cells,
        MapCell[] surfaceCells,
        int width,
        int height,
        GridPosition start,
        GridPosition end,
        int generatorVersion)
    {
        var queue = new Queue<GridPosition>();
        var predecessors = new Dictionary<GridPosition, GridPosition>();
        var visited = new HashSet<GridPosition> { start };
        queue.Enqueue(start);
        while (queue.TryDequeue(out var current) && current != end)
        {
            foreach (var neighbor in CardinalNeighbors(current, width, height))
            {
                if (neighbor.X < 1 || neighbor.X >= width - 1 ||
                    neighbor.Y < 1 || neighbor.Y >= height - 1 ||
                    IsCaveColumnBlocked(
                        surfaceCells,
                        neighbor,
                        width,
                        generatorVersion) ||
                    !visited.Add(neighbor))
                {
                    continue;
                }

                predecessors[neighbor] = current;
                queue.Enqueue(neighbor);
            }
        }

        if (!visited.Contains(end))
        {
            throw new InvalidOperationException("Unable to route the generated cave corridor.");
        }

        var route = new List<GridPosition> { end };
        var step = end;
        while (step != start)
        {
            step = predecessors[step];
            route.Add(step);
        }
        foreach (var position in route)
        {
            SetCaveKind(cells, width, height, position, CaveCellKind.Floor);
        }
    }

    private static bool IsCaveColumnBlocked(
        MapCell[] surfaceCells,
        GridPosition position,
        int width,
        int generatorVersion)
    {
        if (generatorVersion < 10 || position.Z != -1)
        {
            return false;
        }

        var surface = surfaceCells[(position.Y * width) + position.X];
        return surface.Terrain == TerrainKind.DeepWater && surface.FloorLevel <= -2;
    }

    private static void SetCaveKind(
        CaveCell[] cells,
        int width,
        int height,
        GridPosition position,
        CaveCellKind kind)
    {
        if (position.X < 0 || position.X >= width ||
            position.Y < 0 || position.Y >= height || position.Z is > -1 or < -2)
        {
            return;
        }

        var index = checked((((-position.Z) - 1) * width * height) + (position.Y * width) + position.X);
        cells[index] = cells[index] with
        {
            Kind = kind,
            Deposit = MineralDepositKind.None,
            Fluid = CellFluidKind.None,
        };
    }

    private static int Distance(GridPosition left, GridPosition right) =>
        Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y);

    private readonly record struct CaveGenerationResult(
        CaveCell[] Cells,
        VerticalPassage[] Passages);

    private static void SetCell(MapCell[] cells, int width, GridPosition position, MapCell cell) =>
        cells[checked((position.Y * width) + position.X)] = cell;

    private static MapCell CreateGround(byte moisture, byte fertility) =>
        new(TerrainKind.SolidGround, moisture, fertility, TraversalCost: 1);

    private static MapCell CreateMud(byte moisture, byte fertility) =>
        new(TerrainKind.Mud, moisture, fertility, TraversalCost: 3);

    private static MapCell CreateShallowWater() =>
        new(TerrainKind.ShallowWater, Moisture: 100, Fertility: 45, TraversalCost: 4);

    private static MapCell CreateDeepWater() =>
        new(TerrainKind.DeepWater, Moisture: 100, Fertility: 52, TraversalCost: 0, FloorLevel: -1);

    private static void ValidateDimensions(int width, int height)
    {
        if (width < MinimumDimension || width > MaximumDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height < MinimumDimension || height > MaximumDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        _ = checked(width * height);
    }
}
