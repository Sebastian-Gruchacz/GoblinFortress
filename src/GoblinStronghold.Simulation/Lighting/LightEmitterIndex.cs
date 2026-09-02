using GoblinStronghold.Simulation.ContentPacks;
using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Lighting;

public readonly record struct LightEmitterHandle(ContentId DefinitionId, ulong InstanceId);

public readonly record struct LightEmitterSnapshot(
    LightEmitterHandle Handle,
    GridPosition Position,
    float RadiusCells,
    float Intensity,
    CardinalOrientation? Facing = null);

public sealed class LightEmitterIndex
{
    public const int DefaultSectorSize = 16;

    private readonly int _sectorSize;
    private readonly Dictionary<LightEmitterHandle, LightEmitterSnapshot> _emitters = [];
    private readonly Dictionary<SectorKey, HashSet<LightEmitterHandle>> _sectors = [];
    private float _maximumRadius;

    public LightEmitterIndex(int sectorSize = DefaultSectorSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sectorSize, 1);
        _sectorSize = sectorSize;
    }

    public ulong Version { get; private set; }

    public int Count => _emitters.Count;

    public void Clear()
    {
        if (_emitters.Count == 0)
        {
            return;
        }

        _emitters.Clear();
        _sectors.Clear();
        _maximumRadius = 0f;
        Version = checked(Version + 1);
    }

    public void Upsert(LightEmitterSnapshot emitter)
    {
        if (emitter.RadiusCells <= 0f ||
            emitter.Intensity is <= 0f or > LightEmitterCatalog.MaximumSupportedIntensity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(emitter),
                $"Light radius and intensity must be positive, and intensity cannot exceed " +
                $"{LightEmitterCatalog.MaximumSupportedIntensity}.");
        }

        if (_emitters.TryGetValue(emitter.Handle, out var existing))
        {
            if (existing == emitter)
            {
                return;
            }

            RemoveFromSector(existing);
        }

        _emitters[emitter.Handle] = emitter;
        var sector = GetSector(emitter.Position);
        if (!_sectors.TryGetValue(sector, out var handles))
        {
            handles = [];
            _sectors.Add(sector, handles);
        }
        handles.Add(emitter.Handle);
        _maximumRadius = Math.Max(_maximumRadius, emitter.RadiusCells);
        Version = checked(Version + 1);
    }

    public bool Remove(LightEmitterHandle handle)
    {
        if (!_emitters.Remove(handle, out var emitter))
        {
            return false;
        }

        RemoveFromSector(emitter);
        if (Math.Abs(emitter.RadiusCells - _maximumRadius) < float.Epsilon)
        {
            _maximumRadius = _emitters.Values
                .Select(candidate => candidate.RadiusCells)
                .DefaultIfEmpty(0f)
                .Max();
        }
        Version = checked(Version + 1);
        return true;
    }

    public IReadOnlyList<LightEmitterSnapshot> Query(
        int level,
        int minimumX,
        int minimumY,
        int maximumX,
        int maximumY)
    {
        if (maximumX <= minimumX || maximumY <= minimumY || _emitters.Count == 0)
        {
            return [];
        }

        var padding = (int)Math.Ceiling(_maximumRadius);
        var minimumSectorX = FloorDivide(minimumX - padding, _sectorSize);
        var minimumSectorY = FloorDivide(minimumY - padding, _sectorSize);
        var maximumSectorX = FloorDivide(maximumX - 1 + padding, _sectorSize);
        var maximumSectorY = FloorDivide(maximumY - 1 + padding, _sectorSize);
        var candidates = new HashSet<LightEmitterHandle>();
        for (var sectorY = minimumSectorY; sectorY <= maximumSectorY; sectorY++)
        {
            for (var sectorX = minimumSectorX; sectorX <= maximumSectorX; sectorX++)
            {
                if (_sectors.TryGetValue(new SectorKey(level, sectorX, sectorY), out var handles))
                {
                    candidates.UnionWith(handles);
                }
            }
        }

        return candidates
            .Select(handle => _emitters[handle])
            .Where(emitter => Intersects(
                emitter,
                minimumX,
                minimumY,
                maximumX,
                maximumY))
            .OrderBy(emitter => emitter.Handle.DefinitionId.Value, StringComparer.Ordinal)
            .ThenBy(emitter => emitter.Handle.InstanceId)
            .ToArray();
    }

    private static bool Intersects(
        LightEmitterSnapshot emitter,
        int minimumX,
        int minimumY,
        int maximumX,
        int maximumY)
    {
        var closestX = Math.Clamp(emitter.Position.X, minimumX, maximumX - 1);
        var closestY = Math.Clamp(emitter.Position.Y, minimumY, maximumY - 1);
        var deltaX = emitter.Position.X - closestX;
        var deltaY = emitter.Position.Y - closestY;
        return (deltaX * deltaX) + (deltaY * deltaY) <=
            emitter.RadiusCells * emitter.RadiusCells;
    }

    private void RemoveFromSector(LightEmitterSnapshot emitter)
    {
        var sector = GetSector(emitter.Position);
        if (!_sectors.TryGetValue(sector, out var handles))
        {
            return;
        }

        handles.Remove(emitter.Handle);
        if (handles.Count == 0)
        {
            _sectors.Remove(sector);
        }
    }

    private SectorKey GetSector(GridPosition position) => new(
        position.Z,
        FloorDivide(position.X, _sectorSize),
        FloorDivide(position.Y, _sectorSize));

    private static int FloorDivide(int value, int divisor)
    {
        var quotient = value / divisor;
        return value < 0 && value % divisor != 0 ? quotient - 1 : quotient;
    }

    private readonly record struct SectorKey(int Level, int X, int Y);
}
