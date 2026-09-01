using System.Buffers.Binary;
using System.Security.Cryptography;
using GoblinStronghold.Simulation.ContentPacks;
using GoblinStronghold.Simulation.Map.Generation;

namespace GoblinStronghold.Simulation.Map;

public sealed class GeneratedMap
{
    private readonly MapCell[] _cells;
    private CaveCell[] _caveCells;
    private readonly VerticalPassage[] _verticalPassages;
    private readonly Dictionary<GridPosition, GridPosition> _verticalPassageDestinations;
    private readonly string _fingerprint;
    private readonly int _minimumTerrainLevel;
    private readonly int _maximumTerrainLevel;

    internal GeneratedMap(
        int width,
        int height,
        WorldSeed seed,
        int generatorVersion,
        ContentId profileId,
        RiverGenerationMode riverMode,
        MapCell[] cells,
        GridPosition goblinSpawn,
        GridPosition humanVillage,
        CaveCell[]? caveCells = null,
        VerticalPassage[]? verticalPassages = null)
    {
        Width = width;
        Height = height;
        Seed = seed;
        GeneratorVersion = generatorVersion;
        ProfileId = profileId;
        RiverMode = riverMode;
        _cells = cells;
        _caveCells = caveCells ?? [];
        _verticalPassages = verticalPassages ?? [];
        _verticalPassageDestinations = BuildVerticalPassageIndex(_verticalPassages);
        _minimumTerrainLevel = _cells.Min(cell => Math.Min(cell.FloorLevel, cell.SurfaceLevel));
        _maximumTerrainLevel = _cells.Max(cell => cell.SurfaceLevel);
        GoblinSpawn = goblinSpawn;
        HumanVillage = humanVillage;
        _fingerprint = ComputeFingerprintCore();
    }

    public int Width { get; }

    public int Height { get; }

    public int MinimumTerrainLevel => _minimumTerrainLevel;

    public int MaximumTerrainLevel => _maximumTerrainLevel;

    public int MinimumWorldLevel => Math.Min(MinimumTerrainLevel, DeepestCaveLevel);

    public int MaximumWorldLevel => MaximumTerrainLevel;

    public int MaterializedPositiveLevelCount => GeneratorVersion >= 9
        ? Math.Max(0, MaximumTerrainLevel)
        : 0;

    public int MaterializedNegativeLevelCount => Math.Max(0, -MinimumWorldLevel);

    public int LevelCount => MaximumTerrainLevel - MinimumTerrainLevel + 1;

    public int CaveLevelCount => _caveCells.Length == 0 ? 0 : _caveCells.Length / CellCount;

    public int DeepestCaveLevel => -CaveLevelCount;

    public IReadOnlyList<VerticalPassage> VerticalPassages => _verticalPassages;

    public IReadOnlyList<GridPosition> CaveEntrances => _verticalPassages
        .Where(passage => passage.Kind == VerticalPassageKind.CaveMouth)
        .Select(passage => passage.Upper)
        .ToArray();

    public WorldSeed Seed { get; }

    public int GeneratorVersion { get; }

    public ContentId ProfileId { get; }

    public RiverGenerationMode RiverMode { get; }

    public GridPosition GoblinSpawn { get; }

    public GridPosition HumanVillage { get; }

    public int CellCount => _cells.Length;

    public bool IsWithin(GridPosition position) =>
        position.Z == 0 &&
        position.X >= 0 && position.X < Width &&
        position.Y >= 0 && position.Y < Height;

    public bool IsColumnWithin(GridPosition position) =>
        position.X >= 0 && position.X < Width &&
        position.Y >= 0 && position.Y < Height;

    public bool IsWorldPosition(GridPosition position) =>
        IsColumnWithin(position) &&
        position.Z >= MinimumWorldLevel &&
        position.Z <= MaximumWorldLevel;

    public bool TryGetInitialGeometry(
        GridPosition position,
        out InitialCellGeometry geometry)
    {
        geometry = default;
        if (!IsColumnWithin(position) ||
            position.Z < MinimumWorldLevel || position.Z > MaximumWorldLevel)
        {
            return false;
        }

        var terrain = GetColumnCell(position);
        if (IsTerrainSurfacePosition(position))
        {
            geometry = CreateTerrainGeometry(terrain);
            return true;
        }

        if (terrain.Terrain == TerrainKind.DeepWater &&
            position.Z > terrain.FloorLevel && position.Z < terrain.SurfaceLevel)
        {
            geometry = new InitialCellGeometry(
                CellVolumeKind.Open,
                Support: position.Z == terrain.FloorLevel + 1
                    ? CellSupportKind.NaturalFlat
                    : CellSupportKind.None,
                Fluid: CellFluidKind.Water,
                FluidDepthLevels: position.Z - terrain.FloorLevel);
            return true;
        }

        if (IsHillMassPosition(position))
        {
            geometry = new InitialCellGeometry(
                CellVolumeKind.Solid,
                GetHillMassCell(position).Rock);
            return true;
        }

        if (IsCavePosition(position))
        {
            var cave = GetCaveCell(position);
            geometry = cave.Kind switch
            {
                CaveCellKind.SolidRock => new InitialCellGeometry(
                    CellVolumeKind.Solid,
                    cave.Rock),
                CaveCellKind.Ramp => new InitialCellGeometry(
                    CellVolumeKind.Open,
                    Support: CellSupportKind.NaturalRamp),
                _ => new InitialCellGeometry(
                    CellVolumeKind.Open,
                    Support: CellSupportKind.NaturalFlat,
                    Fluid: cave.Fluid,
                    FluidDepthLevels: cave.Fluid == CellFluidKind.None ? 0 : 1),
            };
            return true;
        }

        geometry = new InitialCellGeometry(CellVolumeKind.Open);
        return true;
    }

    public bool IsInitiallyOpenToSky(GridPosition position)
    {
        if (!TryGetInitialGeometry(position, out var target) || target.IsSolid)
        {
            return false;
        }

        for (var z = position.Z + 1; z <= MaximumWorldLevel; z++)
        {
            var above = position with { Z = z };
            if (!TryGetInitialGeometry(above, out var geometry))
            {
                continue;
            }
            if (geometry.IsSolid ||
                geometry.IsSupported && !HasInitialVerticalOpening(above, above with { Z = z - 1 }))
            {
                return false;
            }
        }

        return true;
    }

    public bool HasInitialVerticalOpening(GridPosition upper, GridPosition lower) =>
        upper.X == lower.X && upper.Y == lower.Y && upper.Z - lower.Z == 1 &&
        _verticalPassages.Any(passage => passage.Upper == upper && passage.Lower == lower);

    public MapCell GetCell(GridPosition position) => _cells[GetIndex(position)];

    public MapCell GetColumnCell(GridPosition position)
    {
        if (!IsColumnWithin(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        return _cells[checked((position.Y * Width) + position.X)];
    }

    public GridPosition GetTerrainSurfacePosition(GridPosition column)
    {
        var cell = GetColumnCell(column);
        return column with { Z = GeneratorVersion >= 9 ? cell.SurfaceLevel : 0 };
    }

    public bool IsTerrainSurfacePosition(GridPosition position) =>
        IsColumnWithin(position) &&
        position.Z == (GeneratorVersion >= 9 ? GetColumnCell(position).SurfaceLevel : 0);

    public bool IsHillRockPosition(GridPosition position)
    {
        if (GeneratorVersion < 9 || !IsColumnWithin(position))
        {
            return false;
        }

        var cell = GetColumnCell(position);
        return cell.Terrain == TerrainKind.SolidGround &&
            position.Z >= 0 && position.Z < cell.SurfaceLevel;
    }

    public bool IsHillMassPosition(GridPosition position)
    {
        if (GeneratorVersion < 9 || !IsColumnWithin(position))
        {
            return false;
        }

        var cell = GetColumnCell(position);
        return cell.Terrain is TerrainKind.SolidGround or TerrainKind.Mud &&
            position.Z >= 0 && position.Z < cell.SurfaceLevel;
    }

    public CaveCell GetHillRockCell(GridPosition position)
    {
        if (!IsHillRockPosition(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        return CreateHillRockCell(position);
    }

    public CaveCell GetHillMassCell(GridPosition position)
    {
        if (!IsHillMassPosition(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        return CreateHillRockCell(position);
    }

    private CaveCell CreateHillRockCell(GridPosition position)
    {
        var sample = Seed.Value ^
            ((ulong)(uint)position.X * 0x9E3779B185EBCA87UL) ^
            ((ulong)(uint)position.Y * 0xC2B2AE3D27D4EB4FUL) ^
            ((ulong)(uint)position.Z * 0x165667B19E3779F9UL);
        var rock = (sample & 0x07UL) < 3UL ? RockKind.Granite : RockKind.Sandstone;
        return new CaveCell(rock, CaveCellKind.SolidRock, MineralDepositKind.None);
    }

    public bool IsRockPosition(GridPosition position) =>
        IsCavePosition(position) || IsHillMassPosition(position);

    public CaveCell GetRockCell(GridPosition position) => position.Z < 0
        ? GetCaveCell(position)
        : GetHillMassCell(position);

    public CaveCell GetCaveCell(GridPosition position)
    {
        if (!IsCavePosition(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        return _caveCells[GetCaveIndex(position)];
    }

    public bool IsCavePosition(GridPosition position) =>
        position.X >= 0 && position.X < Width &&
        position.Y >= 0 && position.Y < Height &&
        position.Z < 0 && position.Z >= -CaveLevelCount;

    internal CaveCell GetNextCaveLevelCell(GridPosition position)
    {
        if (!IsColumnWithin(position) || position.Z != DeepestCaveLevel - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }
        return SwampMapGenerator.GenerateSolidCaveCell(
            Seed,
            Width,
            Height,
            position.X,
            position.Y,
            -position.Z - 1,
            GeneratorVersion);
    }

    internal void MaterializeCaveLevel(int level)
    {
        if (level != DeepestCaveLevel - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        var expanded = new CaveCell[checked(_caveCells.Length + CellCount)];
        Array.Copy(_caveCells, expanded, _caveCells.Length);
        var levelIndex = CaveLevelCount;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                expanded[_caveCells.Length + (y * Width) + x] =
                    SwampMapGenerator.GenerateSolidCaveCell(
                        Seed,
                        Width,
                        Height,
                        x,
                        y,
                        levelIndex,
                        GeneratorVersion);
            }
        }
        _caveCells = expanded;
    }

    public bool IsTerrainTraversable(GridPosition position)
    {
        if (GeneratorVersion < 10)
        {
            return position.Z switch
            {
                0 => IsWithin(position) && GetCell(position).IsTraversable,
                < 0 => IsCavePosition(position) && GetCaveCell(position).IsOpen,
                _ => false,
            };
        }

        return TryGetInitialGeometry(position, out var geometry) && geometry.IsOccupiable;
    }

    public IEnumerable<GridPosition> GetCardinalNeighbors(GridPosition position)
    {
        if (!IsWithin(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        if (position.X > 0)
        {
            yield return position with { X = position.X - 1 };
        }

        if (position.X + 1 < Width)
        {
            yield return position with { X = position.X + 1 };
        }

        if (position.Y > 0)
        {
            yield return position with { Y = position.Y - 1 };
        }

        if (position.Y + 1 < Height)
        {
            yield return position with { Y = position.Y + 1 };
        }
    }

    public bool HasTraversablePath(GridPosition start, GridPosition destination)
    {
        if (!IsWithin(start) || !IsWithin(destination))
        {
            return false;
        }

        if (!GetCell(start).IsTraversable || !GetCell(destination).IsTraversable)
        {
            return false;
        }

        var visited = new bool[_cells.Length];
        var queue = new Queue<GridPosition>();
        visited[GetIndex(start)] = true;
        queue.Enqueue(start);

        while (queue.TryDequeue(out var current))
        {
            if (current == destination)
            {
                return true;
            }

            foreach (var neighbor in GetCardinalNeighbors(current))
            {
                var index = GetIndex(neighbor);
                if (visited[index] || !GetCell(neighbor).IsTraversable ||
                    !CanTraverseSurfaceEdge(current, neighbor))
                {
                    continue;
                }

                visited[index] = true;
                queue.Enqueue(neighbor);
            }
        }

        return false;
    }

    public int CountTerrain(TerrainKind terrain) => _cells.Count(cell => cell.Terrain == terrain);

    public bool CanTraverseSurfaceEdge(GridPosition from, GridPosition to)
    {
        if (!IsWithin(from) || !IsWithin(to) ||
            Math.Abs(from.X - to.X) + Math.Abs(from.Y - to.Y) != 1)
        {
            return false;
        }

        var fromCell = GetCell(from);
        var toCell = GetCell(to);
        var difference = toCell.SurfaceLevel - fromCell.SurfaceLevel;
        if (difference == 0)
        {
            return true;
        }
        if (difference == 1)
        {
            return fromCell.RampDirection == DirectionFrom(from, to);
        }
        if (difference == -1)
        {
            return toCell.RampDirection == DirectionFrom(to, from);
        }

        return false;
    }

    public bool CanTraverseTerrainSurfaceEdge(GridPosition from, GridPosition to)
    {
        if (GeneratorVersion < 10)
        {
            return CanTraverseSurfaceEdge(from, to);
        }
        if (!IsTerrainSurfacePosition(from) || !IsTerrainSurfacePosition(to) ||
            Math.Abs(from.X - to.X) + Math.Abs(from.Y - to.Y) != 1)
        {
            return false;
        }

        var fromCell = GetColumnCell(from);
        var toCell = GetColumnCell(to);
        var difference = to.Z - from.Z;
        if (difference == 0)
        {
            return true;
        }
        if (difference == 1)
        {
            return fromCell.RampDirection == DirectionFrom(from, to);
        }
        if (difference == -1)
        {
            return toCell.RampDirection == DirectionFrom(to, from);
        }

        return false;
    }

    public IEnumerable<GridPosition> GetTerrainNeighbors(GridPosition position)
    {
        if (!IsTerrainTraversable(position))
        {
            yield break;
        }

        var isMaterialSurface = GeneratorVersion >= 10 &&
            IsTerrainSurfacePosition(position);
        foreach (var adjacentColumn in GetCardinalWorldNeighbors(position))
        {
            if (!isMaterialSurface)
            {
                if (IsTerrainTraversable(adjacentColumn) &&
                    (GeneratorVersion >= 10 || position.Z != 0 ||
                     CanTraverseSurfaceEdge(position, adjacentColumn)))
                {
                    yield return adjacentColumn;
                }
                continue;
            }

            var surfaceNeighbor = GetTerrainSurfacePosition(adjacentColumn);
            if (IsTerrainTraversable(surfaceNeighbor) &&
                CanTraverseTerrainSurfaceEdge(position, surfaceNeighbor))
            {
                yield return surfaceNeighbor;
            }

            if (adjacentColumn != surfaceNeighbor &&
                IsTerrainTraversable(adjacentColumn))
            {
                yield return adjacentColumn;
            }
        }

        if (_verticalPassageDestinations.TryGetValue(position, out var passageDestination) &&
            IsTerrainTraversable(passageDestination))
        {
            yield return passageDestination;
        }
    }

    private static Dictionary<GridPosition, GridPosition> BuildVerticalPassageIndex(
        IEnumerable<VerticalPassage> passages)
    {
        var destinations = new Dictionary<GridPosition, GridPosition>();
        foreach (var passage in passages)
        {
            if (!destinations.TryAdd(passage.Upper, passage.Lower) ||
                !destinations.TryAdd(passage.Lower, passage.Upper))
            {
                throw new InvalidDataException("Vertical passages must not overlap.");
            }
        }

        return destinations;
    }

    public IReadOnlyList<GridPosition>? FindTerrainPath(
        GridPosition start,
        GridPosition destination)
    {
        if (!IsTerrainTraversable(start) || !IsTerrainTraversable(destination))
        {
            return null;
        }

        var visited = new HashSet<GridPosition> { start };
        var predecessors = new Dictionary<GridPosition, GridPosition>();
        var queue = new Queue<GridPosition>();
        queue.Enqueue(start);
        while (queue.TryDequeue(out var current))
        {
            if (current == destination)
            {
                var route = new List<GridPosition>();
                while (current != start)
                {
                    route.Add(current);
                    current = predecessors[current];
                }
                route.Reverse();
                return route;
            }

            foreach (var neighbor in GetTerrainNeighbors(current))
            {
                if (visited.Add(neighbor))
                {
                    predecessors[neighbor] = current;
                    queue.Enqueue(neighbor);
                }
            }
        }

        return null;
    }

    public string ComputeFingerprint() => _fingerprint;

    private string ComputeFingerprintCore()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> scalarBuffer = stackalloc byte[20];

        BinaryPrimitives.WriteInt32LittleEndian(scalarBuffer[..4], Width);
        BinaryPrimitives.WriteInt32LittleEndian(scalarBuffer.Slice(4, 4), Height);
        BinaryPrimitives.WriteUInt64LittleEndian(scalarBuffer.Slice(8, 8), Seed.Value);
        BinaryPrimitives.WriteInt32LittleEndian(scalarBuffer.Slice(16, 4), GeneratorVersion);
        hash.AppendData(scalarBuffer);

        AppendPosition(hash, GoblinSpawn);
        AppendPosition(hash, HumanVillage);

        Span<byte> cellBuffer = stackalloc byte[7];
        foreach (var cell in _cells)
        {
            cellBuffer[0] = (byte)cell.Terrain;
            cellBuffer[1] = cell.Moisture;
            cellBuffer[2] = cell.Fertility;
            cellBuffer[3] = cell.TraversalCost;
            cellBuffer[4] = unchecked((byte)cell.FloorLevel);
            cellBuffer[5] = unchecked((byte)cell.SurfaceLevel);
            cellBuffer[6] = (byte)cell.RampDirection;
            hash.AppendData(GeneratorVersion switch
            {
                >= 5 => cellBuffer,
                >= 3 => cellBuffer[..5],
                _ => cellBuffer[..4],
            });
        }
        if (GeneratorVersion >= 6)
        {
            Span<byte> caveBuffer = stackalloc byte[4];
            foreach (var caveCell in _caveCells)
            {
                caveBuffer[0] = (byte)caveCell.Rock;
                caveBuffer[1] = (byte)caveCell.Kind;
                caveBuffer[2] = (byte)caveCell.Deposit;
                caveBuffer[3] = (byte)caveCell.Fluid;
                hash.AppendData(GeneratorVersion >= 14
                    ? caveBuffer
                    : GeneratorVersion >= 8
                        ? caveBuffer[..3]
                        : caveBuffer[..2]);
            }
            Span<byte> passageBuffer = stackalloc byte[1];
            foreach (var passage in _verticalPassages)
            {
                AppendPosition(hash, passage.Upper);
                AppendPosition(hash, passage.Lower);
                passageBuffer[0] = (byte)passage.Kind;
                hash.AppendData(passageBuffer);
            }
        }
        if (GeneratorVersion >= 9)
        {
            Span<byte> hillRockBuffer = stackalloc byte[1];
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    var cell = GetColumnCell(new GridPosition(x, y));
                    for (var z = 0; z < cell.SurfaceLevel; z++)
                    {
                        var position = new GridPosition(x, y, z);
                        if (!IsHillRockPosition(position))
                        {
                            continue;
                        }

                        AppendPosition(hash, position);
                        hillRockBuffer[0] = (byte)GetHillRockCell(position).Rock;
                        hash.AppendData(hillRockBuffer);
                    }
                }
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendPosition(IncrementalHash hash, GridPosition position)
    {
        Span<byte> buffer = stackalloc byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(buffer[..4], position.X);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(4, 4), position.Y);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(8, 4), position.Z);
        hash.AppendData(buffer);
    }

    private int GetIndex(GridPosition position)
    {
        if (!IsWithin(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        return checked((position.Y * Width) + position.X);
    }

    private int GetCaveIndex(GridPosition position) => checked(
        (((-position.Z) - 1) * CellCount) + (position.Y * Width) + position.X);

    private IEnumerable<GridPosition> GetCardinalWorldNeighbors(GridPosition position)
    {
        if (position.X > 0) yield return position with { X = position.X - 1 };
        if (position.X + 1 < Width) yield return position with { X = position.X + 1 };
        if (position.Y > 0) yield return position with { Y = position.Y - 1 };
        if (position.Y + 1 < Height) yield return position with { Y = position.Y + 1 };
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

    private static InitialCellGeometry CreateTerrainGeometry(MapCell terrain)
    {
        var support = terrain.HasFloorAtSurface
            ? terrain.RampDirection == TerrainRampDirection.None
                ? CellSupportKind.NaturalFlat
                : CellSupportKind.NaturalRamp
            : terrain.Terrain == TerrainKind.DeepWater &&
                terrain.SurfaceLevel == terrain.FloorLevel + 1
                    ? CellSupportKind.NaturalFlat
                    : CellSupportKind.None;
        var fluid = terrain.Terrain is TerrainKind.ShallowWater or TerrainKind.DeepWater
            ? CellFluidKind.Water
            : CellFluidKind.None;
        return new InitialCellGeometry(
            CellVolumeKind.Open,
            Support: support,
            Cover: terrain.Terrain,
            Fluid: fluid,
            FluidDepthLevels: terrain.WaterDepthLevels,
            RampDirection: terrain.RampDirection);
    }
}
