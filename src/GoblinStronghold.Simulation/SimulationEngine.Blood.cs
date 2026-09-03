using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation;

public enum BloodSurfaceKind : byte
{
    AbsorbentGround = 1,
    ConstructedFloor = 2,
}

public readonly record struct BloodStainSnapshot(
    GridPosition Position,
    int Volume,
    BloodSurfaceKind Surface,
    SimulationTick CreatedAt,
    SimulationTick LastChangedAt);

public sealed partial class SimulationEngine
{
    private const int BloodStainMaximumVolume = 64;
    private const int BloodSpillThreshold = 48;
    private const int BloodAbsorptionIntervalTicks = 600;
    private const int DriedBloodDecayIntervalTicks = 12_000;
    private const int BloodCleaningWorkTicks = 40;
    private const int BloodCleaningVolumePerCycle = 16;
    private const int BloodFootprintSourceThreshold = 8;
    private const int BloodFootprintMaximumSteps = 3;
    private const int BleedingDamageThreshold = 60;
    private const int BleedingPulseIntervalTicks = 20;
    private const int MaximumBleedingTicks = 180;
    private static readonly IComparer<GridPosition> BloodStainPositionComparer =
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
    private readonly SortedDictionary<GridPosition, BloodStainState> _bloodStains =
        new(BloodStainPositionComparer);

    private void AddBlood(GridPosition position, int damage, int severityMultiplier = 1)
    {
        if (damage <= 0 || severityMultiplier <= 0 || !World.IsTerrainReachable(position))
        {
            return;
        }

        var volume = Math.Clamp(
            checked(((damage + 79) / 80) * severityMultiplier),
            1,
            BloodStainMaximumVolume * 4);
        AddBloodVolume(position, volume, allowSpill: true);
    }

    private void AddBloodVolume(GridPosition position, int volume, bool allowSpill)
    {
        if (volume <= 0 || !World.IsTerrainReachable(position))
        {
            return;
        }

        if (!_bloodStains.TryGetValue(position, out var stain))
        {
            stain = new BloodStainState(
                position,
                0,
                ResolveBloodSurface(position),
                CurrentTick,
                CurrentTick);
            _bloodStains.Add(position, stain);
        }

        var combined = checked(stain.Volume + volume);
        stain.Volume = Math.Min(BloodStainMaximumVolume, combined);
        stain.LastChangedAt = CurrentTick;
        RefreshReportedCleaningRegistration(position);
        var overflow = combined - BloodStainMaximumVolume;
        if (!allowSpill || stain.Volume < BloodSpillThreshold || overflow <= 0)
        {
            return;
        }

        foreach (var neighbor in World.GetTerrainNeighbors(position)
                     .OrderBy(candidate => candidate.Y)
                     .ThenBy(candidate => candidate.X)
                     .ThenBy(candidate => candidate.Z))
        {
            AddBloodVolume(neighbor, overflow, allowSpill: false);
            break;
        }
    }

    private void ApplyTraumaDamage(ActorState actor, int damage)
    {
        if (damage <= 0 || actor.Health <= 0)
        {
            return;
        }

        actor.Health = Math.Max(0, actor.Health - damage);
        AddBlood(actor.Position, damage);
        if (damage >= BleedingDamageThreshold && actor.Health > 0)
        {
            actor.BleedingTicksRemaining = Math.Max(
                actor.BleedingTicksRemaining,
                Math.Min(MaximumBleedingTicks, checked(damage * 2)));
        }
    }

    private void UpdateActorBleeding(ActorState actor)
    {
        if (actor.BleedingTicksRemaining <= 0 || actor.Health <= 0)
        {
            actor.BleedingTicksRemaining = 0;
            return;
        }

        if (CurrentTick.Value % BleedingPulseIntervalTicks == 0)
        {
            AddBloodVolume(actor.Position, 1, allowSpill: false);
        }
        actor.BleedingTicksRemaining--;
    }

    private BloodSurfaceKind ResolveBloodSurface(GridPosition position) =>
        World.GetWorldObjectsAt(position)
            .SelectMany(worldObject => worldObject.GetAbsoluteParts())
            .Any(part => part.Position == position &&
                part.Part.Kind is WorldObjectPartKind.Floor or WorldObjectPartKind.Walkway)
            ? BloodSurfaceKind.ConstructedFloor
            : BloodSurfaceKind.AbsorbentGround;

    private bool HasCleanableBloodOnly(GridPosition position) =>
        _bloodStains.TryGetValue(position, out var stain) &&
        stain.Volume > 0;

    private int GetBloodCleaningWorkTicks(GridPosition position) =>
        _bloodStains.TryGetValue(position, out var stain) &&
        stain.Surface == BloodSurfaceKind.ConstructedFloor
            ? BloodCleaningWorkTicks / 2
            : BloodCleaningWorkTicks;

    private int CleanBloodOnly(GridPosition position)
    {
        if (!_bloodStains.TryGetValue(position, out var stain) || stain.Volume <= 0)
        {
            return 0;
        }

        var cleaned = Math.Min(BloodCleaningVolumePerCycle, stain.Volume);
        stain.Volume -= cleaned;
        stain.LastChangedAt = CurrentTick;
        if (stain.Volume == 0)
        {
            _bloodStains.Remove(position);
        }
        RefreshReportedCleaningRegistration(position);

        return cleaned;
    }

    private void UpdateBloodStains()
    {
        foreach (var stain in _bloodStains.Values.ToArray())
        {
            var interval = stain.Surface == BloodSurfaceKind.ConstructedFloor
                ? DriedBloodDecayIntervalTicks
                : BloodAbsorptionIntervalTicks;
            if (CurrentTick.Value == 0 || CurrentTick.Value % interval != 0)
            {
                continue;
            }

            stain.Volume--;
            stain.LastChangedAt = CurrentTick;
            if (stain.Volume <= 0)
            {
                _bloodStains.Remove(stain.Position);
                if (!HasSurfaceGrime(stain.Position))
                {
                    RemoveBloodCleaningDesignations(stain.Position);
                }
            }
            RefreshReportedCleaningRegistration(stain.Position);
        }
    }

    private void RemoveBloodCleaningDesignations(GridPosition position)
    {
        var removed = _workDesignations.Values
            .Where(designation =>
                designation.Kind == WorkDesignationKind.CleanBlood &&
                designation.Target == position)
            .ToArray();
        CancelActorsForDesignations(removed);
        foreach (var designation in removed)
        {
            _workDesignations.Remove(designation.Id);
            Publish(
                SimulationEventKind.WorkDesignationRemoved,
                EntityId.None,
                designation.Id,
                0);
        }
    }

    private BloodStainSnapshot[] CreateBloodStainSnapshot() => _bloodStains.Values
        .Select(stain => stain.ToSnapshot())
        .ToArray();

    private void LoadBloodStains(IEnumerable<BloodStainSaveModel> models)
    {
        foreach (var model in models)
        {
            var position = new GridPosition(model.X, model.Y, model.Z);
            if (!World.IsTerrainReachable(position) ||
                model.Volume is <= 0 or > BloodStainMaximumVolume ||
                !Enum.IsDefined(model.Surface) ||
                model.CreatedAtTick < 0 || model.CreatedAtTick > CurrentTick.Value ||
                model.LastChangedAtTick < model.CreatedAtTick ||
                model.LastChangedAtTick > CurrentTick.Value ||
                _bloodStains.ContainsKey(position))
            {
                throw new InvalidDataException("The save contains an invalid blood stain.");
            }

            _bloodStains.Add(position, new BloodStainState(
                position,
                model.Volume,
                model.Surface,
                new SimulationTick(model.CreatedAtTick),
                new SimulationTick(model.LastChangedAtTick)));
        }
    }

    private sealed class BloodStainState(
        GridPosition position,
        int volume,
        BloodSurfaceKind surface,
        SimulationTick createdAt,
        SimulationTick lastChangedAt)
    {
        public GridPosition Position { get; } = position;

        public int Volume { get; set; } = volume;

        public BloodSurfaceKind Surface { get; } = surface;

        public SimulationTick CreatedAt { get; } = createdAt;

        public SimulationTick LastChangedAt { get; set; } = lastChangedAt;

        public BloodStainSnapshot ToSnapshot() =>
            new(Position, Volume, Surface, CreatedAt, LastChangedAt);
    }
}
