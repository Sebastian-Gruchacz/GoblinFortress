using System.Buffers.Binary;
using System.Security.Cryptography;

namespace GoblinStronghold.Simulation.Map;

public sealed class GeneratedMap
{
    private readonly MapCell[] _cells;
    private readonly string _fingerprint;

    internal GeneratedMap(
        int width,
        int height,
        WorldSeed seed,
        int generatorVersion,
        MapCell[] cells,
        GridPosition goblinSpawn,
        GridPosition humanVillage)
    {
        Width = width;
        Height = height;
        Seed = seed;
        GeneratorVersion = generatorVersion;
        _cells = cells;
        GoblinSpawn = goblinSpawn;
        HumanVillage = humanVillage;
        _fingerprint = ComputeFingerprintCore();
    }

    public int Width { get; }

    public int Height { get; }

    public int LevelCount => 1;

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
                if (visited[index] || !GetCell(neighbor).IsTraversable)
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

        Span<byte> cellBuffer = stackalloc byte[5];
        foreach (var cell in _cells)
        {
            cellBuffer[0] = (byte)cell.Terrain;
            cellBuffer[1] = cell.Moisture;
            cellBuffer[2] = cell.Fertility;
            cellBuffer[3] = cell.TraversalCost;
            cellBuffer[4] = unchecked((byte)cell.FloorLevel);
            hash.AppendData(GeneratorVersion >= 3 ? cellBuffer : cellBuffer[..4]);
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
}
