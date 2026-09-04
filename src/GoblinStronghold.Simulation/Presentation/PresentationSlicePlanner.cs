using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Presentation;

public readonly record struct PresentationSliceRequest(
    int ActiveLevel,
    PresentationCellBounds VisibleBounds,
    int ChunkSize = LowerLevelExposureIndex.DefaultChunkSize);

public readonly record struct PresentationSliceWorkload(
    int DirectlyExposedColumns,
    int ContinuouslyExposedCells,
    int ExposureRegions,
    int VisibleChunks,
    int VerticalPassages,
    int LightPassages);

public sealed record PresentationSlicePlan(
    PresentationSliceRequest Request,
    LowerLevelExposureIndex Exposure,
    IReadOnlyList<GridPosition> DirectlyExposedCells,
    IReadOnlyList<VerticalPassage> VerticalPassages,
    IReadOnlyList<VerticalPassage> LightPassages,
    IReadOnlyDictionary<GridPosition, GridPosition> OpeningDestinations,
    PresentationSliceWorkload Workload);

public static class PresentationSlicePlanner
{
    public static PresentationSlicePlan Create(
        PresentationSliceRequest request,
        Func<int, int, int> surfaceLevelAt,
        IEnumerable<VerticalPassage> verticalPassages) => Create(
            request,
            surfaceLevelAt,
            verticalPassages,
            _ => true,
            _ => false);

    public static PresentationSlicePlan Create(
        PresentationSliceRequest request,
        Func<int, int, int> surfaceLevelAt,
        IEnumerable<VerticalPassage> verticalPassages,
        Func<GridPosition, bool> isActiveLevelDiscovered) => Create(
            request,
            surfaceLevelAt,
            verticalPassages,
            isActiveLevelDiscovered,
            _ => false);

    public static PresentationSlicePlan Create(
        PresentationSliceRequest request,
        Func<int, int, int> surfaceLevelAt,
        IEnumerable<VerticalPassage> verticalPassages,
        Func<GridPosition, bool> isActiveLevelDiscovered,
        Func<GridPosition, bool> isVerticalViewBlocked)
    {
        ArgumentNullException.ThrowIfNull(surfaceLevelAt);
        ArgumentNullException.ThrowIfNull(verticalPassages);
        ArgumentNullException.ThrowIfNull(isActiveLevelDiscovered);
        ArgumentNullException.ThrowIfNull(isVerticalViewBlocked);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.ChunkSize, 1);
        if (request.VisibleBounds.MaximumX < request.VisibleBounds.MinimumX ||
            request.VisibleBounds.MaximumY < request.VisibleBounds.MinimumY)
        {
            throw new ArgumentException("Visible presentation bounds are inverted.", nameof(request));
        }

        var directlyExposed = CollectDirectExposure(
            request,
            surfaceLevelAt,
            isActiveLevelDiscovered,
            isVerticalViewBlocked);
        var passages = verticalPassages
            .Where(VerticalPassageOpennessPolicy.IsOpen)
            .Where(passage =>
                passage.Upper.Z <= request.ActiveLevel &&
                request.VisibleBounds.Contains(passage.Upper) &&
                request.VisibleBounds.Contains(passage.Lower) &&
                (passage.Upper.Z != request.ActiveLevel ||
                 isActiveLevelDiscovered(passage.Upper)))
            .OrderByDescending(passage => passage.Upper.Z)
            .ThenBy(passage => passage.Upper.Y)
            .ThenBy(passage => passage.Upper.X)
            .ThenBy(passage => passage.Lower.Y)
            .ThenBy(passage => passage.Lower.X)
            .ToArray();
        var lightPassages = passages
            .Concat(directlyExposed.SelectMany(position =>
                Enumerable.Range(position.Z + 1, request.ActiveLevel - position.Z)
                    .Select(level => new VerticalPassage(
                        position with { Z = level },
                        position with { Z = level - 1 },
                        VerticalPassageKind.CaveMouth))))
            .Distinct()
            .ToArray();
        var openings = passages
            .Where(passage => passage.Upper.Z == request.ActiveLevel)
            .GroupBy(passage => passage.Upper)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(passage => passage.Lower.Z)
                    .ThenBy(passage => passage.Lower.Y)
                    .ThenBy(passage => passage.Lower.X)
                    .First().Lower);
        var exposure = LowerLevelExposureIndex.Build(
            request.ActiveLevel,
            directlyExposed,
            passages,
            request.ChunkSize);
        var workload = new PresentationSliceWorkload(
            directlyExposed.Count,
            exposure.Regions.Sum(region => region.Cells.Count),
            exposure.Regions.Count,
            exposure.VisibleChunks.Count,
            passages.Length,
            lightPassages.Length);
        return new PresentationSlicePlan(
            request,
            exposure,
            directlyExposed,
            passages,
            lightPassages,
            openings,
            workload);
    }

    private static IReadOnlyList<GridPosition> CollectDirectExposure(
        PresentationSliceRequest request,
        Func<int, int, int> surfaceLevelAt,
        Func<GridPosition, bool> isActiveLevelDiscovered,
        Func<GridPosition, bool> isVerticalViewBlocked)
    {
        if (request.ActiveLevel < 0)
        {
            return [];
        }

        var result = new List<GridPosition>();
        for (var y = request.VisibleBounds.MinimumY; y < request.VisibleBounds.MaximumY; y++)
        {
            for (var x = request.VisibleBounds.MinimumX; x < request.VisibleBounds.MaximumX; x++)
            {
                var surface = surfaceLevelAt(x, y);
                if (surface >= request.ActiveLevel ||
                    !isActiveLevelDiscovered(new GridPosition(x, y, request.ActiveLevel)))
                {
                    continue;
                }

                var exposedLevel = surface;
                for (var level = request.ActiveLevel; level > surface; level--)
                {
                    if (!isVerticalViewBlocked(new GridPosition(x, y, level)))
                    {
                        continue;
                    }

                    exposedLevel = level;
                    break;
                }

                if (exposedLevel < request.ActiveLevel)
                {
                    result.Add(new GridPosition(x, y, exposedLevel));
                }
            }
        }
        return result;
    }
}
