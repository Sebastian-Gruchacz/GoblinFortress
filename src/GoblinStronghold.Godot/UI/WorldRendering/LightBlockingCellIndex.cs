using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal sealed class LightBlockingCellIndex
{
    private readonly Dictionary<int, CachedLevel> _levels = [];
    private ulong _topologyVersion = ulong.MaxValue;

    public void Reset()
    {
        _levels.Clear();
        _topologyVersion = ulong.MaxValue;
    }

    public IReadOnlySet<GridPosition> Get(
        SimulationEngine engine,
        IReadOnlyList<WorldObjectSnapshot> worldObjects,
        int level,
        int minimumX,
        int minimumY,
        int maximumX,
        int maximumY)
    {
        if (_topologyVersion != engine.World.TopologyVersion)
        {
            _levels.Clear();
            _topologyVersion = engine.World.TopologyVersion;
        }

        if (_levels.TryGetValue(level, out var cached) &&
            cached.MinimumX == minimumX && cached.MinimumY == minimumY &&
            cached.MaximumX == maximumX && cached.MaximumY == maximumY)
        {
            return cached.Cells;
        }

        var cells = Collect(
            engine,
            worldObjects,
            level,
            minimumX,
            minimumY,
            maximumX,
            maximumY);
        _levels[level] = new CachedLevel(
            minimumX,
            minimumY,
            maximumX,
            maximumY,
            cells);
        return cells;
    }

    public static HashSet<GridPosition> Collect(
        SimulationEngine engine,
        IEnumerable<WorldObjectSnapshot> worldObjects,
        int level,
        int minimumX,
        int minimumY,
        int maximumX,
        int maximumY)
    {
        var result = new HashSet<GridPosition>();
        for (var y = Math.Max(0, minimumY); y < Math.Min(engine.Map.Height, maximumY); y++)
        {
            for (var x = Math.Max(0, minimumX); x < Math.Min(engine.Map.Width, maximumX); x++)
            {
                var position = new GridPosition(x, y, level);
                if (engine.World.IsSolidRock(position))
                {
                    result.Add(position);
                }
            }
        }

        foreach (var (position, part) in worldObjects
                     .SelectMany(worldObject => worldObject.GetAbsoluteParts())
                     .Where(item => item.Position.Z == level &&
                         item.Position.X >= minimumX && item.Position.X < maximumX &&
                         item.Position.Y >= minimumY && item.Position.Y < maximumY &&
                         item.Part.Kind is WorldObjectPartKind.Wall or
                             WorldObjectPartKind.ClosedDoorLeaf))
        {
            result.Add(position);
        }

        return result;
    }

    private sealed record CachedLevel(
        int MinimumX,
        int MinimumY,
        int MaximumX,
        int MaximumY,
        HashSet<GridPosition> Cells);
}
