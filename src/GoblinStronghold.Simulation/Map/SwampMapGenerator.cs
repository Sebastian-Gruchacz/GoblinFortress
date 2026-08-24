namespace GoblinStronghold.Simulation.Map;

public static class SwampMapGenerator
{
    public const int CurrentVersion = 2;
    public const int MinimumDimension = 16;
    public const int MaximumDimension = 2_048;

    public static bool SupportsVersion(int version) => version is 1 or CurrentVersion;

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
                cells[index] = GenerateCell(seed, index);
            }
        }

        var goblinSpawn = new GridPosition(
            Math.Max(2, width / 6),
            DeterministicRandom.NextInt(
                seed,
                RandomDomain.MapGeneration,
                EntityId.None,
                SimulationTick.Zero,
                sampleKey: 10_001,
                minimumInclusive: 2,
                maximumExclusive: height - 2));

        var humanVillage = new GridPosition(
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

    private static MapCell GenerateCell(WorldSeed seed, int index)
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
            >= 86 => new MapCell(TerrainKind.DeepWater, moistureByte, fertility, TraversalCost: 0),
            >= 70 => new MapCell(TerrainKind.ShallowWater, moistureByte, fertility, TraversalCost: 4),
            >= 38 => new MapCell(TerrainKind.Mud, moistureByte, fertility, TraversalCost: 3),
            _ => new MapCell(TerrainKind.SolidGround, moistureByte, fertility, TraversalCost: 1),
        };
    }

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
