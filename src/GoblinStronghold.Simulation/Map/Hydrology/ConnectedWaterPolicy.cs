namespace GoblinStronghold.Simulation.Map.Hydrology;

/// <summary>
/// Derives water that has entered existing subterranean voids from immutable
/// generated water volumes. The result is rebuilt only after topology changes.
/// </summary>
internal static class ConnectedWaterPolicy
{
    public static HashSet<GridPosition> FindGeneratedSources(GeneratedMap baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        var sources = new HashSet<GridPosition>();
        for (var z = baseline.MinimumWorldLevel; z <= baseline.MaximumWorldLevel; z++)
        {
            for (var y = 0; y < baseline.Height; y++)
            {
                for (var x = 0; x < baseline.Width; x++)
                {
                    var position = new GridPosition(x, y, z);
                    if (IsGeneratedSubsurfaceWater(baseline, position))
                    {
                        sources.Add(position);
                    }
                }
            }
        }

        return sources;
    }

    public static HashSet<GridPosition> Resolve(
        GeneratedMap baseline,
        IReadOnlySet<GridPosition> generatedSources,
        IReadOnlySet<GridPosition> excavatedCells)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(generatedSources);
        ArgumentNullException.ThrowIfNull(excavatedCells);

        var flooded = new HashSet<GridPosition>();
        var visited = generatedSources.ToHashSet();
        var frontier = new Queue<GridPosition>(generatedSources);
        foreach (var position in excavatedCells.Where(position =>
                     IsBreachedWaterBed(baseline, position)))
        {
            if (visited.Add(position))
            {
                flooded.Add(position);
                frontier.Enqueue(position);
            }
        }

        while (frontier.TryDequeue(out var current))
        {
            foreach (var neighbor in DownhillNeighbors(
                         baseline,
                         current,
                         flooded.Contains(current)))
            {
                if (!visited.Add(neighbor) || !IsFloodableVoid(
                        baseline,
                        excavatedCells,
                        neighbor))
                {
                    continue;
                }

                flooded.Add(neighbor);
                frontier.Enqueue(neighbor);
            }
        }

        return flooded;
    }

    private static bool IsGeneratedSubsurfaceWater(
        GeneratedMap baseline,
        GridPosition position) =>
        baseline.TryGetInitialGeometry(position, out var geometry) &&
        geometry.Fluid == CellFluidKind.Water &&
        geometry.FluidDepthLevels > 0;

    private static bool IsBreachedWaterBed(
        GeneratedMap baseline,
        GridPosition position)
    {
        var column = baseline.GetColumnCell(position);
        return column.Terrain == TerrainKind.DeepWater &&
            position.Z == column.FloorLevel;
    }

    private static bool IsFloodableVoid(
        GeneratedMap baseline,
        IReadOnlySet<GridPosition> excavatedCells,
        GridPosition position)
    {
        var column = baseline.GetColumnCell(position);
        if (position.Z > column.SurfaceLevel)
        {
            return false;
        }
        if (position.Z == column.SurfaceLevel)
        {
            return column.IsTraversable &&
                column.Terrain is not (TerrainKind.ShallowWater or TerrainKind.DeepWater);
        }
        if (excavatedCells.Contains(position))
        {
            return true;
        }
        return baseline.TryGetInitialGeometry(position, out var geometry) &&
            !geometry.IsSolid &&
            geometry.Fluid == CellFluidKind.None;
    }

    private static IEnumerable<GridPosition> DownhillNeighbors(
        GeneratedMap baseline,
        GridPosition position,
        bool canSpreadAcrossSurface)
    {
        foreach (var neighbor in CardinalNeighbors(baseline, position))
        {
            var surfaceLevel = baseline.GetColumnCell(neighbor).SurfaceLevel;
            if (baseline.IsTerrainSurfacePosition(position) &&
                !canSpreadAcrossSurface &&
                surfaceLevel == position.Z)
            {
                continue;
            }
            yield return neighbor with { Z = Math.Min(position.Z, surfaceLevel) };
        }
    }

    private static IEnumerable<GridPosition> CardinalNeighbors(
        GeneratedMap baseline,
        GridPosition position)
    {
        if (position.X > 0) yield return position with { X = position.X - 1 };
        if (position.X + 1 < baseline.Width) yield return position with { X = position.X + 1 };
        if (position.Y > 0) yield return position with { Y = position.Y - 1 };
        if (position.Y + 1 < baseline.Height) yield return position with { Y = position.Y + 1 };
    }
}
