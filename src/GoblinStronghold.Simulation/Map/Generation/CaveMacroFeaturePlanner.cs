using GoblinStronghold.Simulation.ContentPacks;

namespace GoblinStronghold.Simulation.Map.Generation;

internal static class CaveMacroFeaturePlanner
{
    private static readonly ContentId SlopedCavernId =
        ContentId.Parse("core:sloped-cavern");
    private static readonly ContentId LavaGalleryId =
        ContentId.Parse("core:lava-gallery");

    public static IReadOnlyList<CaveMacroFeatureLayout> Create(
        WorldSeed seed,
        int width,
        int height)
    {
        var shallowCenter = SelectCenter(seed, width, height, sampleKey: 32_000);
        var deepCenter = SelectCenter(seed, width, height, sampleKey: 32_100);
        return
        [
            CreateSlopedCavern(shallowCenter, width, height),
            CreateLavaGallery(deepCenter, width, height),
        ];
    }

    private static CaveMacroFeatureLayout CreateSlopedCavern(
        GridPosition center,
        int width,
        int height)
    {
        const int highestLevel = -4;
        const int lowestLevel = -7;
        var radiusX = Math.Clamp(width / 10, 4, 7);
        var radiusY = Math.Clamp(height / 12, 3, 6);
        var cells = new Dictionary<GridPosition, CaveMacroFeatureCell>();
        var reservedByLevel = new Dictionary<int, HashSet<GridPosition>>();

        for (var level = highestLevel; level >= lowestLevel; level--)
        {
            var depthOffset = -level + highestLevel;
            var sliceCenter = new GridPosition(
                Math.Clamp(center.X + depthOffset, radiusX + 1, width - radiusX - 2),
                Math.Clamp(center.Y + (depthOffset / 2), radiusY + 1, height - radiusY - 2),
                level);
            var reserved = CreateEllipse(sliceCenter, radiusX, radiusY);
            reservedByLevel.Add(level, reserved);
            foreach (var position in reserved)
            {
                cells.Add(position, new CaveMacroFeatureCell(position, CaveCellKind.Floor));
            }
        }

        var passages = CreatePassages(reservedByLevel, cells, center.X, center.Y);
        var slices = reservedByLevel
            .OrderByDescending(pair => pair.Key)
            .Select(pair => new CaveMacroFeatureSlice(
                pair.Key,
                pair.Value,
                passages.Where(passage =>
                    passage.Upper.Z == pair.Key || passage.Lower.Z == pair.Key)))
            .ToArray();
        return new CaveMacroFeatureLayout(
            new CaveMacroFeaturePlan(
                new CaveMacroFeatureHandle(SlopedCavernId, instanceId: 1),
                CaveMacroFeatureMaterializationPolicy.LayerByLayer,
                slices),
            cells.Values);
    }

    private static CaveMacroFeatureLayout CreateLavaGallery(
        GridPosition center,
        int width,
        int height)
    {
        const int highestLevel = -12;
        const int lowestLevel = -15;
        var radiusX = Math.Clamp(width / 12, 4, 6);
        var radiusY = Math.Clamp(height / 14, 3, 5);
        var cells = new Dictionary<GridPosition, CaveMacroFeatureCell>();
        var reservedByLevel = new Dictionary<int, HashSet<GridPosition>>();

        for (var level = highestLevel; level >= lowestLevel; level--)
        {
            var sliceCenter = center with { Z = level };
            var reserved = CreateEllipse(sliceCenter, radiusX, radiusY);
            reservedByLevel.Add(level, reserved);
            foreach (var position in reserved)
            {
                var distanceX = (position.X - center.X) / (double)radiusX;
                var distanceY = (position.Y - center.Y) / (double)radiusY;
                var isLava = level <= -14 &&
                    (distanceX * distanceX) + (distanceY * distanceY) <= 0.42d;
                cells.Add(position, new CaveMacroFeatureCell(
                    position,
                    CaveCellKind.Floor,
                    isLava ? CellFluidKind.Lava : CellFluidKind.None));
            }
        }

        var passages = CreatePassages(reservedByLevel, cells, center.X, center.Y);
        var slices = reservedByLevel
            .OrderByDescending(pair => pair.Key)
            .Select(pair => new CaveMacroFeatureSlice(
                pair.Key,
                pair.Value,
                passages.Where(passage =>
                    passage.Upper.Z == pair.Key || passage.Lower.Z == pair.Key)))
            .ToArray();
        return new CaveMacroFeatureLayout(
            new CaveMacroFeaturePlan(
                new CaveMacroFeatureHandle(LavaGalleryId, instanceId: 1),
                CaveMacroFeatureMaterializationPolicy.CompleteOnExposure,
                slices),
            cells.Values);
    }

    private static VerticalPassage[] CreatePassages(
        IReadOnlyDictionary<int, HashSet<GridPosition>> reservedByLevel,
        Dictionary<GridPosition, CaveMacroFeatureCell> cells,
        int centerX,
        int centerY)
    {
        var levels = reservedByLevel.Keys.OrderByDescending(level => level).ToArray();
        var passages = new List<VerticalPassage>(levels.Length - 1);
        var used = new HashSet<GridPosition>();
        for (var index = 0; index < levels.Length - 1; index++)
        {
            var upperLevel = levels[index];
            var lowerLevel = levels[index + 1];
            var upperCells = reservedByLevel[upperLevel];
            var lowerCells = reservedByLevel[lowerLevel];
            var upper = upperCells
                .Where(position =>
                    lowerCells.Contains(position with { Z = lowerLevel }) &&
                    !used.Contains(position) &&
                    !used.Contains(position with { Z = lowerLevel }))
                .OrderBy(position => Math.Abs(position.X - centerX) + Math.Abs(position.Y - centerY))
                .ThenBy(position => position.Y)
                .ThenBy(position => position.X)
                .First();
            var lower = upper with { Z = lowerLevel };
            used.Add(upper);
            used.Add(lower);
            cells[upper] = new CaveMacroFeatureCell(upper, CaveCellKind.Ramp);
            cells[lower] = new CaveMacroFeatureCell(lower, CaveCellKind.Floor);
            passages.Add(new VerticalPassage(upper, lower, VerticalPassageKind.NaturalRamp));
        }
        return passages.ToArray();
    }

    private static HashSet<GridPosition> CreateEllipse(
        GridPosition center,
        int radiusX,
        int radiusY)
    {
        var result = new HashSet<GridPosition>();
        for (var y = center.Y - radiusY; y <= center.Y + radiusY; y++)
        {
            for (var x = center.X - radiusX; x <= center.X + radiusX; x++)
            {
                var offsetX = (x - center.X) / (double)radiusX;
                var offsetY = (y - center.Y) / (double)radiusY;
                if ((offsetX * offsetX) + (offsetY * offsetY) <= 1d)
                {
                    result.Add(new GridPosition(x, y, center.Z));
                }
            }
        }
        return result;
    }

    private static GridPosition SelectCenter(
        WorldSeed seed,
        int width,
        int height,
        ulong sampleKey)
    {
        const int margin = 6;
        return new GridPosition(
            DeterministicRandom.NextInt(
                seed,
                RandomDomain.MapGeneration,
                EntityId.None,
                SimulationTick.Zero,
                sampleKey,
                margin,
                width - margin),
            DeterministicRandom.NextInt(
                seed,
                RandomDomain.MapGeneration,
                EntityId.None,
                SimulationTick.Zero,
                sampleKey + 1,
                margin,
                height - margin));
    }
}
