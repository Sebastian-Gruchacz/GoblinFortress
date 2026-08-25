using System.Buffers.Binary;
using System.Security.Cryptography;

namespace GoblinStronghold.Simulation.Map;

public sealed class GeneratedMap
{
    private readonly MapCell[] _cells;
    private readonly CaveCell[] _caveCells;
    private readonly VerticalPassage[] _verticalPassages;
    private readonly string _fingerprint;

    internal GeneratedMap(
        int width,
        int height,
        WorldSeed seed,
        int generatorVersion,
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
        _cells = cells;
        _caveCells = caveCells ?? [];
        _verticalPassages = verticalPassages ?? [];
        GoblinSpawn = goblinSpawn;
        HumanVillage = humanVillage;
        _fingerprint = ComputeFingerprintCore();
    }

    public int Width { get; }

    public int Height { get; }

    public int MinimumTerrainLevel => _cells.Min(cell => Math.Min(cell.FloorLevel, cell.SurfaceLevel));

    public int MaximumTerrainLevel => _cells.Max(cell => cell.SurfaceLevel);

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

    public GridPosition GoblinSpawn { get; }

    public GridPosition HumanVillage { get; }

    public int CellCount => _cells.Length;

    public bool IsWithin(GridPosition position) =>
        position.Z == 0 &&
        position.X >= 0 && position.X < Width &&
        position.Y >= 0 && position.Y < Height;

    public MapCell GetCell(GridPosition position) => _cells[GetIndex(position)];

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

    public bool IsTerrainTraversable(GridPosition position) => position.Z switch
    {
        0 => IsWithin(position) && GetCell(position).IsTraversable,
        < 0 => IsCavePosition(position) && GetCaveCell(position).IsOpen,
        _ => false,
    };

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

    public IEnumerable<GridPosition> GetTerrainNeighbors(GridPosition position)
    {
        if (!IsTerrainTraversable(position))
        {
            yield break;
        }

        foreach (var neighbor in GetCardinalWorldNeighbors(position))
        {
            if (!IsTerrainTraversable(neighbor))
            {
                continue;
            }
            if (position.Z == 0 && !CanTraverseSurfaceEdge(position, neighbor))
            {
                continue;
            }

            yield return neighbor;
        }

        foreach (var passage in _verticalPassages)
        {
            if (passage.Upper == position)
            {
                yield return passage.Lower;
            }
            else if (passage.Lower == position)
            {
                yield return passage.Upper;
            }
        }
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
            Span<byte> caveBuffer = stackalloc byte[2];
            foreach (var caveCell in _caveCells)
            {
                caveBuffer[0] = (byte)caveCell.Rock;
                caveBuffer[1] = (byte)caveCell.Kind;
                hash.AppendData(caveBuffer);
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
}
