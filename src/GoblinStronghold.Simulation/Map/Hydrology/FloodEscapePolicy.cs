namespace GoblinStronghold.Simulation.Map.Hydrology;

internal static class FloodEscapePolicy
{
    public static GridPosition? FindNearestDryPosition(
        WorldMapState world,
        GridPosition origin)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!world.TryGetFluid(origin, out var fluid, out _) ||
            fluid != CellFluidKind.Water)
        {
            return world.IsTerrainTraversable(origin) ? origin : null;
        }

        var passages = world.CreateVerticalPassageSnapshot()
            .SelectMany(passage => new[]
            {
                (From: passage.Upper, To: passage.Lower),
                (From: passage.Lower, To: passage.Upper),
            })
            .GroupBy(edge => edge.From)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => edge.To).ToArray());
        var visited = new HashSet<GridPosition> { origin };
        var frontier = new Queue<GridPosition>();
        frontier.Enqueue(origin);
        while (frontier.TryDequeue(out var current))
        {
            var neighbors = world.GetCardinalWorldNeighbors(current)
                .Concat(passages.GetValueOrDefault(current) ?? [])
                .Distinct()
                .OrderBy(position => position.Z)
                .ThenBy(position => position.Y)
                .ThenBy(position => position.X);
            foreach (var neighbor in neighbors)
            {
                if (!visited.Add(neighbor))
                {
                    continue;
                }
                if (!world.TryGetFluid(neighbor, out var neighborFluid, out _))
                {
                    if (world.IsTerrainTraversable(neighbor))
                    {
                        return neighbor;
                    }
                    continue;
                }
                if (neighborFluid == CellFluidKind.Water)
                {
                    frontier.Enqueue(neighbor);
                }
            }
        }

        return null;
    }
}
