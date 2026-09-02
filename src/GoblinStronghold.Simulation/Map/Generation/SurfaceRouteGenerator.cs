namespace GoblinStronghold.Simulation.Map.Generation;

internal static class SurfaceRouteGenerator
{
    private const ulong ThroughRoadPhaseSampleKey = 41_001;
    private const ulong JunctionPhaseSampleKey = 41_002;

    public static IReadOnlyList<GeneratedSurfaceRoute> Apply(
        MapCell[] cells,
        WorldSeed seed,
        int width,
        int height,
        RoadGenerationMode mode,
        RoadGenerationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(profile);
        if (mode == RoadGenerationMode.Absent)
        {
            return [];
        }

        var throughRoad = CarveThroughRoad(cells, seed, width, height, profile);
        if (mode != RoadGenerationMode.Junction)
        {
            return [throughRoad.Route];
        }

        var branch = CarveJunction(
            cells,
            seed,
            width,
            height,
            profile,
            throughRoad.Centers);
        return [throughRoad.Route, branch];
    }

    private static ThroughRoadResult CarveThroughRoad(
        MapCell[] cells,
        WorldSeed seed,
        int width,
        int height,
        RoadGenerationProfile profile)
    {
        var centers = new int[height];
        var path = new List<GridPosition>();
        var phase = SamplePhase(seed, ThroughRoadPhaseSampleKey);
        var previousX = -1;
        for (var y = 0; y < height; y++)
        {
            var progress = height == 1 ? 0d : (double)y / (height - 1);
            var baseX = Lerp(profile.NorthEntryX, profile.SouthEntryX, progress) * (width - 1);
            var meander = Math.Sin((progress * Math.Tau * 1.7d) + phase) *
                profile.MeanderAmplitude * width;
            var centerX = Math.Clamp((int)Math.Round(baseX + meander), 1, width - 2);
            centers[y] = centerX;
            if (previousX < 0)
            {
                AddPathPoint(path, cells, width, height, centerX, y, profile.HalfWidth);
            }
            else
            {
                AddPathPoint(path, cells, width, height, previousX, y, profile.HalfWidth);
                var horizontalDirection = Math.Sign(centerX - previousX);
                for (var x = previousX + horizontalDirection;
                     horizontalDirection != 0 && x != centerX + horizontalDirection;
                     x += horizontalDirection)
                {
                    AddPathPoint(path, cells, width, height, x, y, profile.HalfWidth);
                }
            }
            previousX = centerX;
        }
        return new ThroughRoadResult(
            centers,
            new GeneratedSurfaceRoute(
                GeneratedSurfaceRouteRole.ThroughRoad,
                SurfaceRouteEndpoint.NorthEdge,
                SurfaceRouteEndpoint.SouthEdge,
                path));
    }

    private static GeneratedSurfaceRoute CarveJunction(
        MapCell[] cells,
        WorldSeed seed,
        int width,
        int height,
        RoadGenerationProfile profile,
        IReadOnlyList<int> throughRoadCenters)
    {
        var junctionY = Math.Clamp(
            (int)Math.Round(profile.JunctionY * (height - 1)),
            1,
            height - 2);
        var startX = throughRoadCenters[junctionY];
        var endX = Math.Clamp(
            (int)Math.Round(profile.JunctionEndX * (width - 1)),
            0,
            width - 1);
        var direction = Math.Sign(endX - startX);
        if (direction == 0)
        {
            throw new InvalidOperationException(
                "The generated road junction does not reach another map edge.");
        }

        var phase = SamplePhase(seed, JunctionPhaseSampleKey);
        var distance = Math.Abs(endX - startX);
        var path = new List<GridPosition>();
        var previousY = junctionY;
        AddPathPoint(path, cells, width, height, startX, junctionY, profile.HalfWidth);
        for (var step = 1; step <= distance; step++)
        {
            var progress = (double)step / distance;
            var x = startX + (step * direction);
            var offset = Math.Sin((progress * Math.Tau) + phase) *
                profile.MeanderAmplitude * height * 0.45d;
            var y = Math.Clamp((int)Math.Round(junctionY + offset), 1, height - 2);
            AddPathPoint(path, cells, width, height, x, previousY, profile.HalfWidth);
            var verticalDirection = Math.Sign(y - previousY);
            for (var connectorY = previousY + verticalDirection;
                 verticalDirection != 0 && connectorY != y + verticalDirection;
                 connectorY += verticalDirection)
            {
                AddPathPoint(path, cells, width, height, x, connectorY, profile.HalfWidth);
            }
            previousY = y;
        }

        return new GeneratedSurfaceRoute(
            GeneratedSurfaceRouteRole.JunctionBranch,
            SurfaceRouteEndpoint.Junction,
            direction > 0 ? SurfaceRouteEndpoint.EastEdge : SurfaceRouteEndpoint.WestEdge,
            path);
    }

    private static void AddPathPoint(
        List<GridPosition> path,
        MapCell[] cells,
        int width,
        int height,
        int x,
        int y,
        int halfWidth)
    {
        CarveSpan(cells, width, height, x, y, halfWidth);
        var position = new GridPosition(
            x,
            y,
            cells[checked((y * width) + x)].SurfaceLevel);
        if (path.Count == 0 || path[^1] != position)
        {
            path.Add(position);
        }
    }

    private static void CarveSpan(
        MapCell[] cells,
        int width,
        int height,
        int centerX,
        int centerY,
        int halfWidth)
    {
        for (var y = centerY - halfWidth; y <= centerY + halfWidth; y++)
        {
            for (var x = centerX - halfWidth; x <= centerX + halfWidth; x++)
            {
                if (x < 0 || x >= width || y < 0 || y >= height)
                {
                    continue;
                }
                CarveCell(cells, width, x, y);
            }
        }
    }

    private static void CarveCell(MapCell[] cells, int width, int x, int y)
    {
        var index = checked((y * width) + x);
        var cell = cells[index];
        if (cell.Terrain is TerrainKind.ShallowWater or TerrainKind.DeepWater)
        {
            cells[index] = cell with
            {
                Terrain = TerrainKind.ShallowWater,
                TraversalCost = 2,
                FloorLevel = cell.SurfaceLevel,
                RampDirection = TerrainRampDirection.None,
                SurfaceRoute = SurfaceRouteKind.Ford,
            };
            return;
        }

        cells[index] = cell with
        {
            Terrain = TerrainKind.SolidGround,
            Moisture = Math.Min(cell.Moisture, (byte)62),
            TraversalCost = 1,
            RampDirection = TerrainRampDirection.None,
            SurfaceRoute = SurfaceRouteKind.Road,
        };
    }

    private static double SamplePhase(WorldSeed seed, ulong sampleKey) =>
        DeterministicRandom.NextInt(
            seed,
            RandomDomain.MapGeneration,
            EntityId.None,
            SimulationTick.Zero,
            sampleKey,
            minimumInclusive: 0,
            maximumExclusive: 6_284) / 1_000d;

    private static double Lerp(double from, double to, double amount) =>
        from + ((to - from) * amount);

    private sealed record ThroughRoadResult(
        int[] Centers,
        GeneratedSurfaceRoute Route);
}
