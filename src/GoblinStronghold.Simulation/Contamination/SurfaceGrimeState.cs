using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Contamination;

public readonly record struct SurfaceGrimeSnapshot(
    GridPosition Position,
    int Volume,
    SimulationTick CreatedAt,
    SimulationTick LastChangedAt);

internal sealed class SurfaceGrimeState
{
    internal const int MaximumVolume = 48;
    internal const int MaximumCarriedAmount = 6;
    private const int PickupThreshold = 3;
    private const int CleaningVolumePerCycle = 16;
    private static readonly IComparer<GridPosition> PositionComparer =
        Comparer<GridPosition>.Create((left, right) =>
        {
            var zComparison = left.Z.CompareTo(right.Z);
            if (zComparison != 0)
            {
                return zComparison;
            }

            var yComparison = left.Y.CompareTo(right.Y);
            return yComparison != 0 ? yComparison : left.X.CompareTo(right.X);
        });
    private readonly SortedDictionary<GridPosition, Entry> _entries =
        new(PositionComparer);

    public bool Contains(GridPosition position) =>
        _entries.TryGetValue(position, out var entry) && entry.Volume > 0;

    public int GetVolume(GridPosition position) =>
        _entries.TryGetValue(position, out var entry) ? entry.Volume : 0;

    public IEnumerable<GridPosition> EnumeratePositions() => _entries.Keys;

    public bool Remove(GridPosition position) => _entries.Remove(position);

    public int PickUp(GridPosition position, int carriedAmount, SimulationTick tick)
    {
        if (!_entries.TryGetValue(position, out var entry) || entry.Volume < PickupThreshold)
        {
            return carriedAmount;
        }

        entry.Volume--;
        entry.LastChangedAt = tick;
        if (entry.Volume == 0)
        {
            _entries.Remove(position);
        }

        return Math.Max(carriedAmount, Math.Min(MaximumCarriedAmount, entry.Volume / 2 + 1));
    }

    public void Deposit(GridPosition position, int carriedAmount, SimulationTick tick)
    {
        if (carriedAmount <= 0)
        {
            return;
        }

        if (!_entries.TryGetValue(position, out var entry))
        {
            entry = new Entry(position, 0, tick, tick);
            _entries.Add(position, entry);
        }

        entry.Volume = Math.Min(MaximumVolume, entry.Volume + Math.Max(1, carriedAmount / 2));
        entry.LastChangedAt = tick;
    }

    public int Clean(GridPosition position, SimulationTick tick)
    {
        if (!_entries.TryGetValue(position, out var entry))
        {
            return 0;
        }

        var cleaned = Math.Min(CleaningVolumePerCycle, entry.Volume);
        entry.Volume -= cleaned;
        entry.LastChangedAt = tick;
        if (entry.Volume == 0)
        {
            _entries.Remove(position);
        }

        return cleaned;
    }

    public SurfaceGrimeSnapshot[] CreateSnapshot() => _entries.Values
        .Select(entry => entry.ToSnapshot())
        .ToArray();

    public void Restore(IEnumerable<SurfaceGrimeSnapshot> snapshots, SimulationTick currentTick)
    {
        foreach (var snapshot in snapshots)
        {
            if (snapshot.Volume is <= 0 or > MaximumVolume ||
                snapshot.CreatedAt.Value < 0 || snapshot.CreatedAt.Value > currentTick.Value ||
                snapshot.LastChangedAt.Value < snapshot.CreatedAt.Value ||
                snapshot.LastChangedAt.Value > currentTick.Value ||
                _entries.ContainsKey(snapshot.Position))
            {
                throw new InvalidDataException("The save contains invalid surface grime.");
            }

            _entries.Add(snapshot.Position, new Entry(
                snapshot.Position,
                snapshot.Volume,
                snapshot.CreatedAt,
                snapshot.LastChangedAt));
        }
    }

    private sealed class Entry(
        GridPosition position,
        int volume,
        SimulationTick createdAt,
        SimulationTick lastChangedAt)
    {
        public GridPosition Position { get; } = position;

        public int Volume { get; set; } = volume;

        public SimulationTick CreatedAt { get; } = createdAt;

        public SimulationTick LastChangedAt { get; set; } = lastChangedAt;

        public SurfaceGrimeSnapshot ToSnapshot() =>
            new(Position, Volume, CreatedAt, LastChangedAt);
    }
}
