using System.Collections.ObjectModel;

namespace GoblinStronghold.Simulation.Map;

public enum NavigationBeliefStatus : byte
{
    Passable = 1,
    Blocked = 2,
    Hazardous = 3,
}

public enum NavigationBeliefFreshness : byte
{
    Current = 1,
    Aging = 2,
    Stale = 3,
}

public readonly record struct NavigationEdge
{
    private NavigationEdge(GridPosition first, GridPosition second)
    {
        First = first;
        Second = second;
    }

    public GridPosition First { get; }

    public GridPosition Second { get; }

    public static NavigationEdge Between(GridPosition left, GridPosition right)
    {
        if (!ArePotentialNeighbors(left, right))
        {
            throw new ArgumentException("A navigation belief must describe one local transition.");
        }

        return Compare(left, right) <= 0
            ? new NavigationEdge(left, right)
            : new NavigationEdge(right, left);
    }

    private static bool ArePotentialNeighbors(GridPosition left, GridPosition right)
    {
        var deltaX = Math.Abs(left.X - right.X);
        var deltaY = Math.Abs(left.Y - right.Y);
        var deltaZ = Math.Abs(left.Z - right.Z);
        return deltaX + deltaY <= 1 && deltaZ <= 1 &&
            deltaX + deltaY + deltaZ > 0;
    }

    private static int Compare(GridPosition left, GridPosition right)
    {
        var z = left.Z.CompareTo(right.Z);
        if (z != 0) return z;
        var y = left.Y.CompareTo(right.Y);
        return y != 0 ? y : left.X.CompareTo(right.X);
    }
}

public readonly record struct NavigationBelief(
    NavigationEdge Edge,
    NavigationBeliefStatus Status,
    SimulationTick ObservedAt,
    SimulationTick ReceivedAt,
    EntityId SourceActorId,
    byte Confidence,
    bool IsDirectObservation)
{
    public NavigationBeliefFreshness GetFreshness(
        SimulationTick currentTick,
        long currentDurationTicks,
        long agingDurationTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(currentDurationTicks);
        ArgumentOutOfRangeException.ThrowIfNegative(agingDurationTicks);
        if (currentTick.CompareTo(ObservedAt) < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentTick));
        }

        var age = currentTick.Value - ObservedAt.Value;
        if (age <= currentDurationTicks)
        {
            return NavigationBeliefFreshness.Current;
        }
        return age <= checked(currentDurationTicks + agingDurationTicks)
            ? NavigationBeliefFreshness.Aging
            : NavigationBeliefFreshness.Stale;
    }
}

public sealed class NavigationKnowledgeState
{
    private readonly Dictionary<NavigationEdge, NavigationBelief> _beliefs = [];
    private int _blockedCount;

    public int Count => _beliefs.Count;

    public ulong Version { get; private set; }

    public bool HasBlockedBeliefs => _blockedCount > 0;

    public NavigationBelief Observe(
        EntityId observerId,
        GridPosition from,
        GridPosition to,
        NavigationBeliefStatus status,
        SimulationTick tick,
        byte confidence = 100)
    {
        if (observerId == EntityId.None)
        {
            throw new ArgumentOutOfRangeException(nameof(observerId));
        }
        if (tick.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick));
        }
        ValidateStatusAndConfidence(status, confidence);
        var belief = new NavigationBelief(
            NavigationEdge.Between(from, to),
            status,
            tick,
            tick,
            observerId,
            confidence,
            IsDirectObservation: true);
        StoreIfPreferred(belief);
        return belief;
    }

    public bool ReceiveReport(NavigationBelief reported, SimulationTick receivedAt)
    {
        if (reported.SourceActorId == EntityId.None || reported.ObservedAt.Value < 0 ||
            receivedAt.CompareTo(reported.ObservedAt) < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reported));
        }
        ValidateStatusAndConfidence(reported.Status, reported.Confidence);
        var shared = reported with
        {
            ReceivedAt = receivedAt,
            IsDirectObservation = false,
        };
        return StoreIfPreferred(shared);
    }

    public bool Restore(NavigationBelief belief)
    {
        if (belief.SourceActorId == EntityId.None || belief.ObservedAt.Value < 0 ||
            belief.ReceivedAt.CompareTo(belief.ObservedAt) < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(belief));
        }
        ValidateStatusAndConfidence(belief.Status, belief.Confidence);
        return StoreIfPreferred(belief);
    }

    public bool TryGet(NavigationEdge edge, out NavigationBelief belief) =>
        _beliefs.TryGetValue(edge, out belief);

    public bool AllowsTraversal(GridPosition from, GridPosition to) =>
        !_beliefs.TryGetValue(NavigationEdge.Between(from, to), out var belief) ||
        belief.Status != NavigationBeliefStatus.Blocked;

    public bool AllowsTraversal(
        GridPosition from,
        GridPosition to,
        SimulationTick currentTick,
        long currentDurationTicks,
        long agingDurationTicks) =>
        !_beliefs.TryGetValue(NavigationEdge.Between(from, to), out var belief) ||
        belief.Status != NavigationBeliefStatus.Blocked ||
        belief.GetFreshness(currentTick, currentDurationTicks, agingDurationTicks) ==
            NavigationBeliefFreshness.Stale;

    public bool AllowsTraversal(
        GridPosition from,
        GridPosition to,
        NavigationKnowledgeState fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        var edge = NavigationEdge.Between(from, to);
        return _beliefs.TryGetValue(edge, out var personal)
            ? personal.Status != NavigationBeliefStatus.Blocked
            : fallback.AllowsTraversal(from, to);
    }

    public bool AllowsTraversal(
        GridPosition from,
        GridPosition to,
        NavigationKnowledgeState fallback,
        SimulationTick currentTick,
        long currentDurationTicks,
        long agingDurationTicks)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        var edge = NavigationEdge.Between(from, to);
        if (_beliefs.TryGetValue(edge, out var personal))
        {
            return personal.Status != NavigationBeliefStatus.Blocked ||
                personal.GetFreshness(currentTick, currentDurationTicks, agingDurationTicks) ==
                    NavigationBeliefFreshness.Stale;
        }
        return fallback.AllowsTraversal(
            from,
            to,
            currentTick,
            currentDurationTicks,
            agingDurationTicks);
    }

    public IReadOnlyList<NavigationBelief> CreateSnapshot() =>
        new ReadOnlyCollection<NavigationBelief>(_beliefs.Values
            .OrderBy(belief => belief.Edge.First.Z)
            .ThenBy(belief => belief.Edge.First.Y)
            .ThenBy(belief => belief.Edge.First.X)
            .ThenBy(belief => belief.Edge.Second.Z)
            .ThenBy(belief => belief.Edge.Second.Y)
            .ThenBy(belief => belief.Edge.Second.X)
            .ToArray());

    private bool StoreIfPreferred(NavigationBelief candidate)
    {
        if (_beliefs.TryGetValue(candidate.Edge, out var existing) &&
            ComparePreference(candidate, existing) <= 0)
        {
            return false;
        }

        var wasBlocked = _beliefs.TryGetValue(candidate.Edge, out var previous) &&
            previous.Status == NavigationBeliefStatus.Blocked;
        var changedTraversalPolicy = wasBlocked !=
            (candidate.Status == NavigationBeliefStatus.Blocked);
        if (_beliefs.TryGetValue(candidate.Edge, out var replaced) &&
            replaced.Status == NavigationBeliefStatus.Blocked)
        {
            _blockedCount--;
        }
        _beliefs[candidate.Edge] = candidate;
        if (changedTraversalPolicy)
        {
            Version = checked(Version + 1);
        }
        if (candidate.Status == NavigationBeliefStatus.Blocked)
        {
            _blockedCount++;
        }
        return true;
    }

    private static int ComparePreference(NavigationBelief left, NavigationBelief right)
    {
        var observed = left.ObservedAt.CompareTo(right.ObservedAt);
        if (observed != 0) return observed;
        var direct = left.IsDirectObservation.CompareTo(right.IsDirectObservation);
        if (direct != 0) return direct;
        var confidence = left.Confidence.CompareTo(right.Confidence);
        if (confidence != 0) return confidence;
        var received = left.ReceivedAt.CompareTo(right.ReceivedAt);
        if (received != 0) return received;
        var source = left.SourceActorId.Value.CompareTo(right.SourceActorId.Value);
        if (source != 0) return -source;
        return ((byte)right.Status).CompareTo((byte)left.Status);
    }

    private static void ValidateStatusAndConfidence(
        NavigationBeliefStatus status,
        byte confidence)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }
        if (confidence > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence));
        }
    }
}
