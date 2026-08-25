using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Resources;

public readonly record struct ResourceSpatialEntry(
    EntityId StackId,
    ResourceKind Resource,
    GridPosition Position,
    ItemLocationKind LocationKind,
    EntityId StorageZoneId);

public readonly record struct ResourceStorageNode(
    EntityId ZoneId,
    GridPosition Position,
    ResourceKind AcceptedResource,
    EntityId SourceStorageZoneId);

public sealed record ResourceSpatialIndexSnapshot(
    ulong Version,
    int SectorSize,
    IReadOnlyList<ResourceSpatialEntry> Entries,
    IReadOnlyList<ResourceStorageNode> StorageNodes);

public sealed class ResourceSpatialIndex
{
    public const int DefaultSectorSize = 16;

    private readonly int _sectorSize;
    private readonly Dictionary<EntityId, ResourceSpatialEntry> _entries = [];
    private readonly Dictionary<ResourceSector, SortedSet<EntityId>> _sectorEntries = [];
    private readonly Dictionary<EntityId, ResourceStorageNode> _storageNodes = [];

    public ResourceSpatialIndex(int sectorSize = DefaultSectorSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sectorSize);
        _sectorSize = sectorSize;
    }

    public ulong Version { get; private set; }

    public int SectorSize => _sectorSize;

    public void UpsertStack(
        EntityId stackId,
        ResourceKind resource,
        ItemLocation location)
    {
        ArgumentOutOfRangeException.ThrowIfZero(stackId.Value);
        if (!Enum.IsDefined(resource) || resource == ResourceKind.Any)
        {
            throw new ArgumentOutOfRangeException(nameof(resource));
        }

        if (location.Kind == ItemLocationKind.ActorInventory)
        {
            RemoveStack(stackId);
            return;
        }

        var entry = new ResourceSpatialEntry(
            stackId,
            resource,
            location.Position,
            location.Kind,
            location.Kind == ItemLocationKind.StorageZone ? location.OwnerId : EntityId.None);
        if (_entries.TryGetValue(stackId, out var previous) && previous == entry)
        {
            return;
        }

        if (previous.StackId != EntityId.None)
        {
            RemoveFromSector(previous);
        }
        _entries[stackId] = entry;
        GetOrCreateSector(entry.Position).Add(stackId);
        Version = checked(Version + 1);
    }

    public bool RemoveStack(EntityId stackId)
    {
        if (!_entries.Remove(stackId, out var entry))
        {
            return false;
        }

        RemoveFromSector(entry);
        Version = checked(Version + 1);
        return true;
    }

    public void UpsertStorageNode(
        EntityId zoneId,
        GridPosition position,
        ResourceKind acceptedResource,
        EntityId sourceStorageZoneId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(zoneId.Value);
        var node = new ResourceStorageNode(
            zoneId,
            position,
            acceptedResource,
            sourceStorageZoneId);
        if (_storageNodes.GetValueOrDefault(zoneId) == node)
        {
            return;
        }

        _storageNodes[zoneId] = node;
        Version = checked(Version + 1);
    }

    public IReadOnlyList<EntityId> FindNearestStackIds(
        ResourceKind resource,
        GridPosition origin,
        int maximumCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCount);
        var originSector = ToSector(origin);
        var result = new List<EntityId>(maximumCount);
        foreach (var sector in _sectorEntries.Keys
                     .OrderBy(sector => SectorDistance(originSector, sector))
                     .ThenBy(sector => sector.Y)
                     .ThenBy(sector => sector.X))
        {
            foreach (var id in _sectorEntries[sector]
                         .Where(id => _entries[id].Resource == resource)
                         .OrderBy(id => ManhattanDistance(origin, _entries[id].Position))
                         .ThenBy(id => id))
            {
                result.Add(id);
                if (result.Count == maximumCount)
                {
                    return result;
                }
            }
        }

        return result;
    }

    public ResourceSpatialIndexSnapshot CreateSnapshot() => new(
        Version,
        _sectorSize,
        Array.AsReadOnly(_entries.Values.OrderBy(entry => entry.StackId).ToArray()),
        Array.AsReadOnly(_storageNodes.Values.OrderBy(node => node.ZoneId).ToArray()));

    private SortedSet<EntityId> GetOrCreateSector(GridPosition position)
    {
        var sector = ToSector(position);
        if (!_sectorEntries.TryGetValue(sector, out var entries))
        {
            entries = [];
            _sectorEntries.Add(sector, entries);
        }

        return entries;
    }

    private void RemoveFromSector(ResourceSpatialEntry entry)
    {
        var sector = ToSector(entry.Position);
        if (_sectorEntries.TryGetValue(sector, out var entries) &&
            entries.Remove(entry.StackId) && entries.Count == 0)
        {
            _sectorEntries.Remove(sector);
        }
    }

    private ResourceSector ToSector(GridPosition position) => new(
        DivideFloor(position.X, _sectorSize),
        DivideFloor(position.Y, _sectorSize),
        position.Z);

    private static int DivideFloor(int value, int divisor) =>
        value >= 0 ? value / divisor : ((value + 1) / divisor) - 1;

    private static int SectorDistance(ResourceSector left, ResourceSector right) =>
        Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y) + Math.Abs(left.Z - right.Z);

    private static int ManhattanDistance(GridPosition left, GridPosition right) =>
        Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y) + Math.Abs(left.Z - right.Z);

    private readonly record struct ResourceSector(int X, int Y, int Z);
}
