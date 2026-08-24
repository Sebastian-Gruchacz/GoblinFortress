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
    int TypeSlotCount,
    int StackCapacity,
    int UsedTypeSlots);
