namespace GoblinStronghold.Simulation.Map;

public static class SwampMapGenerator
{
    public const int CurrentVersion = 4;
    public const int MinimumDimension = 16;
    public const int MaximumDimension = 2_048;

    public static bool SupportsVersion(int version) => version is 1 or 2 or 3 or CurrentVersion;

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
                    ? GenerateRegionalCell(seed, x, y, width, height)
                    : GenerateLegacyCell(seed, index, generatorVersion);
            }
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
            CarveSettlementPad(cells, width, height, goblinSpawn, goblin: true);
            CarveSettlementPad(cells, width, height, humanVillage, goblin: false);
        }

        CarveSettlementAccess(cells, width, goblinSpawn, humanVillage);
        SetCell(cells, width, goblinSpawn, CreateMud(moisture: 92, fertility: 72));
        SetCell(cells, width, humanVillage, CreateGround(moisture: 48, fertility: 68));
        SetCell(cells, width, goblinSpawn with { Y = goblinSpawn.Y + 1 }, CreateShallowWater());
        SetCell(cells, width, humanVillage with { Y = humanVillage.Y + 1 }, CreateShallowWater());
        if (generatorVersion >= 4)
        {
            SetCell(cells, width, new GridPosition(width - 1, height - 1), CreateDeepWater());
        }

        var map = new GeneratedMap(
            width,
            height,
            seed,
            generatorVersion,
            cells,
            goblinSpawn,
            humanVillage);
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
        int height)
    {
        var normalizedX = width == 1 ? 0d : x / (double)(width - 1);
        var normalizedY = height == 1 ? 0d : y / (double)(height - 1);
        var terrainNoise = FractalValueNoise(seed, normalizedX, normalizedY, sampleKey: 20_000);
        var moistureNoise = FractalValueNoise(seed, normalizedX, normalizedY, sampleKey: 21_000);
        var riverMeander = (FractalValueNoise(seed, normalizedX, 0.5d, sampleKey: 22_000) - 0.5d) * 0.11d;
        var riverCenterY = 0.82d - (0.64d * normalizedX) + riverMeander;
        var riverHalfWidth = Math.Max(1.5d, Math.Min(width, height) * 0.035d);
        var riverDistance = Math.Abs(normalizedY - riverCenterY) * height;

        if (riverDistance <= riverHalfWidth * 0.48d)
        {
            return CreateDeepWater();
        }

        if (riverDistance <= riverHalfWidth)
        {
            return CreateShallowWater();
        }

        var leftSwamp = Math.Clamp((0.48d - normalizedX) / 0.48d, 0d, 1d);
        var bottomSwamp = Math.Clamp((normalizedY - 0.58d) / 0.42d, 0d, 1d);
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
        GridPosition destination)
    {
        var xStep = start.X <= destination.X ? 1 : -1;
        for (var x = start.X; x != destination.X + xStep; x += xStep)
        {
            SetTraversableIfNeeded(cells, width, new GridPosition(x, start.Y));
        }

        var yStep = start.Y <= destination.Y ? 1 : -1;
        for (var y = start.Y; y != destination.Y + yStep; y += yStep)
        {
            SetTraversableIfNeeded(cells, width, new GridPosition(destination.X, y));
        }
    }

    private static void CarveSettlementPad(
        MapCell[] cells,
        int width,
        int height,
        GridPosition center,
        bool goblin)
    {
        const int padWidth = 8;
        const int padHeight = 9;
        var startX = Math.Clamp(center.X - (padWidth / 2), 0, width - padWidth);
        var startY = Math.Clamp(center.Y - (padHeight / 2), 0, height - padHeight);

        for (var y = startY; y < startY + padHeight; y++)
        {
            for (var x = startX; x < startX + padWidth; x++)
            {
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

    private static void SetTraversableIfNeeded(MapCell[] cells, int width, GridPosition position)
    {
        var index = checked((position.Y * width) + position.X);
        if (!cells[index].IsTraversable)
        {
            cells[index] = CreateMud(moisture: 78, fertility: 55);
        }
    }

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
