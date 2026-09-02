using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Presentation;

[Flags]
public enum PresentationChunkDirtyReason : byte
{
    None = 0,
    InitialContent = 1 << 0,
    Topology = 1 << 1,
    Structures = 1 << 2,
    Fluids = 1 << 3,
    Contamination = 1 << 4,
    StaticLighting = 1 << 5,
    ExposureMask = 1 << 6,
}

public readonly record struct PresentationChunkCacheSnapshot(
    PresentationChunkKey Key,
    bool IsVisible,
    PresentationChunkDirtyReason DirtyReasons,
    ulong Revision)
{
    public bool IsDirty => DirtyReasons != PresentationChunkDirtyReason.None;
}

public sealed class LowerLevelPresentationCacheState
{
    private readonly int _chunkSize;
    private readonly Dictionary<PresentationChunkKey, MutableChunkState> _chunks = [];

    public LowerLevelPresentationCacheState(
        int chunkSize = LowerLevelExposureIndex.DefaultChunkSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);
        _chunkSize = chunkSize;
    }

    public IReadOnlyList<PresentationChunkCacheSnapshot> Snapshot => _chunks.Values
        .Select(state => state.ToSnapshot())
        .OrderByDescending(state => state.Key.Level)
        .ThenBy(state => state.Key.Y)
        .ThenBy(state => state.Key.X)
        .ToArray();

    public void Clear() => _chunks.Clear();

    public void SynchronizeExposure(LowerLevelExposureIndex exposure)
    {
        ArgumentNullException.ThrowIfNull(exposure);
        if (exposure.ChunkSize != _chunkSize)
        {
            throw new ArgumentException(
                "The exposure index and presentation cache must use the same chunk size.",
                nameof(exposure));
        }

        foreach (var state in _chunks.Values)
        {
            state.IsVisible = false;
        }

        foreach (var (key, cells) in exposure.VisibleChunkCells)
        {
            var exposureSignature = CreateExposureSignature(cells);
            if (!_chunks.TryGetValue(key, out var state))
            {
                state = new MutableChunkState(key)
                {
                    DirtyReasons = PresentationChunkDirtyReason.InitialContent,
                    ExposureSignature = exposureSignature,
                };
                _chunks.Add(key, state);
            }
            else if (state.ExposureSignature != exposureSignature)
            {
                state.ExposureSignature = exposureSignature;
                state.DirtyReasons |= PresentationChunkDirtyReason.ExposureMask;
            }
            state.IsVisible = true;
        }
    }

    public void Invalidate(GridPosition position, PresentationChunkDirtyReason reason)
    {
        if (reason == PresentationChunkDirtyReason.None)
        {
            return;
        }

        var key = new PresentationChunkKey(
            position.Z,
            FloorDivide(position.X, _chunkSize),
            FloorDivide(position.Y, _chunkSize));
        if (!_chunks.TryGetValue(key, out var state))
        {
            state = new MutableChunkState(key);
            _chunks.Add(key, state);
        }
        state.DirtyReasons |= reason;
    }

    public bool InvalidateRetained(
        GridPosition position,
        PresentationChunkDirtyReason reason)
    {
        if (reason == PresentationChunkDirtyReason.None)
        {
            return false;
        }

        var key = new PresentationChunkKey(
            position.Z,
            FloorDivide(position.X, _chunkSize),
            FloorDivide(position.Y, _chunkSize));
        if (!_chunks.TryGetValue(key, out var state))
        {
            return false;
        }

        state.DirtyReasons |= reason;
        return true;
    }

    public int InvalidateRetainedArea(
        GridPosition center,
        float radiusCells,
        PresentationChunkDirtyReason reason)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radiusCells);
        if (reason == PresentationChunkDirtyReason.None)
        {
            return 0;
        }

        var count = 0;
        foreach (var state in _chunks.Values.Where(state => state.Key.Level == center.Z))
        {
            var minimumX = state.Key.X * _chunkSize;
            var minimumY = state.Key.Y * _chunkSize;
            var maximumX = minimumX + _chunkSize - 1;
            var maximumY = minimumY + _chunkSize - 1;
            var closestX = Math.Clamp(center.X, minimumX, maximumX);
            var closestY = Math.Clamp(center.Y, minimumY, maximumY);
            var deltaX = center.X - closestX;
            var deltaY = center.Y - closestY;
            if ((deltaX * deltaX) + (deltaY * deltaY) > radiusCells * radiusCells)
            {
                continue;
            }

            state.DirtyReasons |= reason;
            count++;
        }
        return count;
    }

    public void InvalidateAll(PresentationChunkDirtyReason reason)
    {
        if (reason == PresentationChunkDirtyReason.None)
        {
            return;
        }

        foreach (var state in _chunks.Values)
        {
            state.DirtyReasons |= reason;
        }
    }

    public IReadOnlyList<PresentationChunkCacheSnapshot> GetVisibleRebuildCandidates() =>
        Snapshot
            .Where(state => state.IsVisible && state.IsDirty)
            .OrderBy(state => state.Key.Level)
            .ThenBy(state => state.Key.Y)
            .ThenBy(state => state.Key.X)
            .ToArray();

    public void MarkRebuilt(PresentationChunkKey key)
    {
        if (!_chunks.TryGetValue(key, out var state))
        {
            throw new KeyNotFoundException($"Presentation chunk '{key}' is not registered.");
        }

        state.DirtyReasons = PresentationChunkDirtyReason.None;
        state.Revision = checked(state.Revision + 1);
    }

    private static int FloorDivide(int value, int divisor)
    {
        var quotient = value / divisor;
        return value < 0 && value % divisor != 0 ? quotient - 1 : quotient;
    }

    private static ulong CreateExposureSignature(IEnumerable<GridPosition> cells)
    {
        const ulong offset = 14_695_981_039_346_656_037UL;
        const ulong prime = 1_099_511_628_211UL;
        var signature = offset;
        foreach (var position in cells)
        {
            signature = (signature ^ unchecked((uint)position.X)) * prime;
            signature = (signature ^ unchecked((uint)position.Y)) * prime;
            signature = (signature ^ unchecked((uint)position.Z)) * prime;
        }
        return signature;
    }

    private sealed class MutableChunkState(PresentationChunkKey key)
    {
        public PresentationChunkKey Key { get; } = key;
        public bool IsVisible { get; set; }
        public PresentationChunkDirtyReason DirtyReasons { get; set; }
        public ulong Revision { get; set; }
        public ulong ExposureSignature { get; set; }

        public PresentationChunkCacheSnapshot ToSnapshot() => new(
            Key,
            IsVisible,
            DirtyReasons,
            Revision);
    }
}
