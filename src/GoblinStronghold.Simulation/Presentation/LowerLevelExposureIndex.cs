using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Presentation;

public readonly record struct PresentationCellBounds(
    int MinimumX,
    int MinimumY,
    int MaximumX,
    int MaximumY)
{
    public bool Contains(GridPosition position) =>
        position.X >= MinimumX && position.X < MaximumX &&
        position.Y >= MinimumY && position.Y < MaximumY;
}

public readonly record struct PresentationChunkKey(int Level, int X, int Y);

public sealed record LowerLevelExposureRegion(
    int Level,
    int Sequence,
    PresentationCellBounds Bounds,
    IReadOnlyList<GridPosition> Cells,
    IReadOnlyList<PresentationChunkKey> Chunks);

public sealed class LowerLevelExposureIndex
{
    public const int DefaultChunkSize = 16;

    private static readonly (int X, int Y)[] CardinalOffsets =
        [(0, -1), (1, 0), (0, 1), (-1, 0)];

    private readonly HashSet<GridPosition> _exposedCells;

    private LowerLevelExposureIndex(
        int activeLevel,
        int chunkSize,
        HashSet<GridPosition> exposedCells,
        IReadOnlyList<LowerLevelExposureRegion> regions)
    {
        ActiveLevel = activeLevel;
        ChunkSize = chunkSize;
        _exposedCells = exposedCells;
        Regions = regions;
    }

    public int ActiveLevel { get; }

    public int ChunkSize { get; }

    public IReadOnlyList<LowerLevelExposureRegion> Regions { get; }

    public IReadOnlyCollection<PresentationChunkKey> VisibleChunks => Regions
        .SelectMany(region => region.Chunks)
        .Distinct()
        .OrderByDescending(chunk => chunk.Level)
        .ThenBy(chunk => chunk.Y)
        .ThenBy(chunk => chunk.X)
        .ToArray();

    public IReadOnlyDictionary<PresentationChunkKey, IReadOnlyList<GridPosition>>
        VisibleChunkCells => Regions
            .SelectMany(region => region.Cells)
            .GroupBy(position => new PresentationChunkKey(
                position.Z,
                FloorDivide(position.X, ChunkSize),
                FloorDivide(position.Y, ChunkSize)))
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<GridPosition>)group
                    .Distinct()
                    .OrderBy(position => position.Y)
                    .ThenBy(position => position.X)
                    .ToArray());

    public bool IsContinuouslyExposed(GridPosition position) =>
        position.Z < ActiveLevel && _exposedCells.Contains(position);

    public static LowerLevelExposureIndex Build(
        int activeLevel,
        IEnumerable<GridPosition> directlyExposedCells,
        IEnumerable<VerticalPassage> verticalPassages,
        int chunkSize = DefaultChunkSize)
    {
        ArgumentNullException.ThrowIfNull(directlyExposedCells);
        ArgumentNullException.ThrowIfNull(verticalPassages);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);

        var exposed = new HashSet<GridPosition>();
        foreach (var position in directlyExposedCells.Where(position => position.Z < activeLevel))
        {
            for (var level = position.Z; level < activeLevel; level++)
            {
                exposed.Add(position with { Z = level });
            }
        }
        var passages = verticalPassages
            .Where(passage =>
                passage.Upper.Z <= activeLevel &&
                passage.Lower.Z < passage.Upper.Z)
            .OrderByDescending(passage => passage.Upper.Z)
            .ThenBy(passage => passage.Upper.Y)
            .ThenBy(passage => passage.Upper.X)
            .ThenBy(passage => passage.Lower.Y)
            .ThenBy(passage => passage.Lower.X)
            .ToArray();

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var passage in passages)
            {
                if (passage.Upper.Z != activeLevel && !exposed.Contains(passage.Upper))
                {
                    continue;
                }

                changed |= exposed.Add(passage.Lower);
            }
        }

        return new LowerLevelExposureIndex(
            activeLevel,
            chunkSize,
            exposed,
            BuildRegions(exposed, chunkSize));
    }

    private static IReadOnlyList<LowerLevelExposureRegion> BuildRegions(
        HashSet<GridPosition> exposed,
        int chunkSize)
    {
        var remaining = exposed.ToHashSet();
        var result = new List<LowerLevelExposureRegion>();
        foreach (var level in exposed.Select(position => position.Z).Distinct().OrderByDescending(z => z))
        {
            var sequence = 0;
            var starts = remaining
                .Where(position => position.Z == level)
                .OrderBy(position => position.Y)
                .ThenBy(position => position.X)
                .ToArray();
            foreach (var first in starts)
            {
                if (!remaining.Contains(first))
                {
                    continue;
                }

                var cells = CollectRegion(first, remaining);
                var chunks = cells
                    .Select(position => new PresentationChunkKey(
                        level,
                        FloorDivide(position.X, chunkSize),
                        FloorDivide(position.Y, chunkSize)))
                    .Distinct()
                    .OrderBy(chunk => chunk.Y)
                    .ThenBy(chunk => chunk.X)
                    .ToArray();
                result.Add(new LowerLevelExposureRegion(
                    level,
                    sequence++,
                    new PresentationCellBounds(
                        cells.Min(position => position.X),
                        cells.Min(position => position.Y),
                        cells.Max(position => position.X) + 1,
                        cells.Max(position => position.Y) + 1),
                    cells,
                    chunks));
            }
        }

        return result;
    }

    private static IReadOnlyList<GridPosition> CollectRegion(
        GridPosition first,
        HashSet<GridPosition> remaining)
    {
        var queue = new Queue<GridPosition>();
        var result = new List<GridPosition>();
        remaining.Remove(first);
        queue.Enqueue(first);
        while (queue.TryDequeue(out var position))
        {
            result.Add(position);
            foreach (var offset in CardinalOffsets)
            {
                var neighbor = new GridPosition(
                    position.X + offset.X,
                    position.Y + offset.Y,
                    position.Z);
                if (remaining.Remove(neighbor))
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        return result
            .OrderBy(position => position.Y)
            .ThenBy(position => position.X)
            .ToArray();
    }

    private static int FloorDivide(int value, int divisor)
    {
        var quotient = value / divisor;
        return value < 0 && value % divisor != 0 ? quotient - 1 : quotient;
    }
}
