using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Resources;

public enum ResourceKind : byte
{
    Any = 0,
    Food = 1,
    Wood = 2,
    Reeds = 3,
    Stone = 4,
    Bone = 5,
    Vegetation = 6,
}

public enum FoodKind : byte
{
    None = 0,
    DriedRations = 1,
    Berries = 2,
    Mushrooms = 3,
    EdibleRoots = 4,
    Fish = 5,
}

public enum ItemLocationKind : byte
{
    Ground = 1,
    ActorInventory = 2,
    StorageZone = 3,
}

public enum StoragePriority : byte
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3,
}

public enum StorageDeliveryState : byte
{
    Disabled = 0,
    Satisfied = 1,
    InTransit = 2,
    NoAllowedSource = 3,
    NoSurplus = 4,
    DestinationBlocked = 5,
    NoReachableSource = 6,
    NoAvailableHauler = 7,
    AssignedHaulerBusy = 8,
    WaitingForHauler = 9,
}

public readonly record struct StorageDeliveryDiagnostic(
    EntityId ZoneId,
    StorageDeliveryState State,
    int RequestedQuantity,
    int InTransitQuantity,
    int AvailableSourceQuantity,
    int MatchingSourceCount);

public readonly record struct ResourcePrioritySnapshot(
    ResourceKind Resource,
    StoragePriority Priority);

public readonly record struct ResourceInventorySnapshot(
    ResourceKind Resource,
    int StoredQuantity,
    int KnownLooseQuantity,
    int CarriedQuantity);

public readonly record struct ItemLocation
{
    private ItemLocation(ItemLocationKind kind, GridPosition position, EntityId ownerId)
    {
        Kind = kind;
        Position = position;
        OwnerId = ownerId;
    }

    public ItemLocationKind Kind { get; }

    public GridPosition Position { get; }

    public EntityId OwnerId { get; }

    public static ItemLocation OnGround(GridPosition position) =>
        new(ItemLocationKind.Ground, position, EntityId.None);

    public static ItemLocation CarriedBy(EntityId actorId) =>
        new(ItemLocationKind.ActorInventory, default, actorId);

    public static ItemLocation StoredIn(EntityId zoneId, GridPosition position) =>
        new(ItemLocationKind.StorageZone, position, zoneId);
}

public readonly record struct ItemStackSnapshot(
    EntityId Id,
    ResourceKind Resource,
    FoodKind FoodKind,
    int Quantity,
    ItemLocation Location);

public readonly record struct StorageZoneSnapshot(
    EntityId Id,
    GridPosition Position,
    ResourceKind AcceptedResource,
    int Capacity,
    int StoredQuantity,
    int DesiredQuantity,
    EntityId AssignedHaulerId,
    EntityId SourceStorageZoneId,
    StoragePriority Priority,
    int TypeSlotCount,
    int StackCapacity,
    int UsedTypeSlots);
