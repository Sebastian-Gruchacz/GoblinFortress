using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Contamination;

internal readonly record struct SurfaceContaminationArea(
    GridPosition Anchor,
    IReadOnlyCollection<GridPosition> Positions);

internal sealed class SurfaceContaminationAreaIndex
{
    private static readonly IComparer<GridPosition> PositionComparer =
        Comparer<GridPosition>.Create(ComparePositions);
    private readonly Dictionary<GridPosition, int> _areaByPosition = [];
    private readonly Dictionary<int, AreaState> _areas = [];
    private int _nextAreaId = 1;

    public int PositionCount => _areaByPosition.Count;

    public int AreaCount => _areas.Count;

    public bool Add(GridPosition position)
    {
        if (_areaByPosition.ContainsKey(position))
        {
            return false;
        }

        var neighboringAreaIds = GetCardinalNeighbors(position)
            .Select(neighbor => _areaByPosition.GetValueOrDefault(neighbor))
            .Where(areaId => areaId != 0)
            .Distinct()
            .Order()
            .ToArray();
        if (neighboringAreaIds.Length == 0)
        {
            CreateArea([position]);
            return true;
        }

        var destinationId = neighboringAreaIds[0];
        var destination = _areas[destinationId];
        destination.Add(position);
        _areaByPosition.Add(position, destinationId);
        foreach (var sourceId in neighboringAreaIds.Skip(1))
        {
            var source = _areas[sourceId];
            foreach (var sourcePosition in source.Positions)
            {
                destination.Add(sourcePosition);
                _areaByPosition[sourcePosition] = destinationId;
            }
            _areas.Remove(sourceId);
        }

        return true;
    }

    public bool Remove(GridPosition position)
    {
        if (!_areaByPosition.Remove(position, out var areaId))
        {
            return false;
        }

        var previousArea = _areas[areaId];
        previousArea.Remove(position);
        if (previousArea.Positions.Count == 0)
        {
            _areas.Remove(areaId);
            return true;
        }

        var remainingNeighbors = GetCardinalNeighbors(position)
            .Count(previousArea.Positions.Contains);
        if (remainingNeighbors <= 1)
        {
            return true;
        }

        _areas.Remove(areaId);
        foreach (var remainingPosition in previousArea.Positions)
        {
            _areaByPosition.Remove(remainingPosition);
        }

        var unassigned = previousArea.Positions.ToHashSet();
        while (unassigned.Count > 0)
        {
            var origin = unassigned.Min(PositionComparer);
            var component = new List<GridPosition>();
            var pending = new Queue<GridPosition>();
            pending.Enqueue(origin);
            unassigned.Remove(origin);
            while (pending.TryDequeue(out var current))
            {
                component.Add(current);
                foreach (var neighbor in GetCardinalNeighbors(current))
                {
                    if (unassigned.Remove(neighbor))
                    {
                        pending.Enqueue(neighbor);
                    }
                }
            }

            CreateArea(component);
        }

        return true;
    }

    public IEnumerable<SurfaceContaminationArea> EnumerateAreas() =>
        _areas.Values
            .OrderBy(area => area.Anchor, PositionComparer)
            .Select(area => new SurfaceContaminationArea(area.Anchor, area.Positions));

    public void Clear()
    {
        _areaByPosition.Clear();
        _areas.Clear();
        _nextAreaId = 1;
    }

    private void CreateArea(IEnumerable<GridPosition> positions)
    {
        var areaId = _nextAreaId++;
        var area = new AreaState();
        foreach (var position in positions)
        {
            area.Add(position);
            _areaByPosition.Add(position, areaId);
        }
        _areas.Add(areaId, area);
    }

    private static IEnumerable<GridPosition> GetCardinalNeighbors(GridPosition position)
    {
        yield return position with { X = position.X - 1 };
        yield return position with { X = position.X + 1 };
        yield return position with { Y = position.Y - 1 };
        yield return position with { Y = position.Y + 1 };
    }

    private static int ComparePositions(GridPosition left, GridPosition right)
    {
        var zComparison = left.Z.CompareTo(right.Z);
        if (zComparison != 0)
        {
            return zComparison;
        }

        var yComparison = left.Y.CompareTo(right.Y);
        return yComparison != 0 ? yComparison : left.X.CompareTo(right.X);
    }

    private sealed class AreaState
    {
        private long _sumX;
        private long _sumY;
        private long _sumZ;

        public HashSet<GridPosition> Positions { get; } = [];

        public GridPosition Anchor => new(
            DivideRounded(_sumX, Positions.Count),
            DivideRounded(_sumY, Positions.Count),
            DivideRounded(_sumZ, Positions.Count));

        public void Add(GridPosition position)
        {
            if (!Positions.Add(position))
            {
                return;
            }

            _sumX += position.X;
            _sumY += position.Y;
            _sumZ += position.Z;
        }

        public void Remove(GridPosition position)
        {
            if (!Positions.Remove(position))
            {
                return;
            }

            _sumX -= position.X;
            _sumY -= position.Y;
            _sumZ -= position.Z;
        }

        private static int DivideRounded(long total, int count) =>
            checked((int)Math.Round((double)total / count, MidpointRounding.AwayFromZero));
    }
}
