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
    Coal = 7,
    Ore = 8,
    Hide = 9,
    Equipment = 10,
    Materials = 11,
    Water = 12,
    Earth = 13,
    Sand = 14,
}

public enum FoodKind : byte
{
    None = 0,
    DriedRations = 1,
    Berries = 2,
    Mushrooms = 3,
    EdibleRoots = 4,
    Fish = 5,
    RawMeat = 6,
    CookedMeat = 7,
    CampSoup = 8,
    Medicine = 9,
}

public enum ResourceVariant : byte
{
    None = 0,
    OakWood = 1,
    ChestnutWood = 2,
    BirchWood = 3,
    WalnutWood = 4,
    AppleWood = 5,
    PineWood = 6,
    Sandstone = 7,
    Granite = 8,
    IronOre = 9,
    EquipmentPrimitiveSling = 10,
    EquipmentBoneKnife = 11,
    EquipmentFightingStick = 12,
    EquipmentStoneClub = 13,
    EquipmentHideClothes = 14,
    EquipmentReedClothes = 15,
    EquipmentPrimitiveWaterskin = 16,
    EquipmentRagClothes = 17,
    EquipmentWoodenAxe = 18,
    EquipmentPrimitivePickaxe = 19,
    EquipmentWoodenHoe = 20,
    EquipmentHumanWoodenAxe = 21,
    EquipmentWoodenBucket = 22,
    EquipmentWoodenSpear = 23,
    Basalt = 24,
    Obsidian = 25,
    CopperOre = 26,
    SilverOre = 27,
    GoldOre = 28,
    Ruby = 29,
    Emerald = 30,
    Diamond = 31,
    EquipmentReinforcedPickaxe = 32,
    EquipmentWoodenBarrel = 33,
    IronBar = 34,
    CopperBar = 35,
    SilverBar = 36,
    GoldBar = 37,
    EquipmentWoodenBox = 38,
    EquipmentWoodenChest = 39,
    EquipmentWoodenBulkBin = 40,
    SpiderVenom = 41,
    SpiderSilk = 42,
    SpiderChitin = 43,
    Lichen = 44,
    Mana = 45,
    EquipmentWoodenHammer = 46,
    Soil = 47,
    EquipmentWoodenShovel = 48,
    Sand = 49,
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

[Flags]
public enum StorageCapability : byte
{
    None = 0,
    SolidGoods = 1 << 0,
    SealedLiquids = 1 << 1,
    All = SolidGoods | SealedLiquids,
}

public enum StorageRequirement : byte
{
    SolidGoods = 1,
    SealedLiquid = 2,
}

public enum StorageProviderKind : byte
{
    OpenPile = 0,
    WaterBarrel = 1,
    WoodenBox = 2,
    WoodenChest = 3,
    WoodenBulkBin = 4,
}

[Flags]
public enum StorageResourceFilter : ushort
{
    None = 0,
    Food = 1 << 0,
    Wood = 1 << 1,
    Reeds = 1 << 2,
    Stone = 1 << 3,
    Bone = 1 << 4,
    Vegetation = 1 << 5,
    Coal = 1 << 6,
    Ore = 1 << 7,
    Hide = 1 << 8,
    Equipment = 1 << 9,
    Materials = 1 << 10,
    Water = 1 << 11,
    Earth = 1 << 12,
    Sand = 1 << 13,
    SolidGoods = Food | Wood | Reeds | Stone | Bone | Coal | Ore | Hide | Equipment | Materials |
        Earth | Sand,
    All = SolidGoods | Vegetation | Water,
}

public readonly record struct StorageSlotPolicy(
    int SlotCount,
    int StackCapacity,
    bool SeparatesItemTypes,
    StorageCapability Capabilities)
{
    public long TotalCapacity => (long)SlotCount * StackCapacity;

    public bool Supports(StorageRequirement requirement) => requirement switch
    {
        StorageRequirement.SolidGoods => Capabilities.HasFlag(StorageCapability.SolidGoods),
        StorageRequirement.SealedLiquid => Capabilities.HasFlag(StorageCapability.SealedLiquids),
        _ => false,
    };
}

[Flags]
public enum MineralStorageFilter : byte
{
    None = 0,
    Sandstone = 1 << 0,
    Granite = 1 << 1,
    Coal = 1 << 2,
    IronOre = 1 << 3,
    All = Sandstone | Granite | Coal | IronOre,
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
    NoAvailableTool = 10,
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
    ResourceVariant Variant,
    int Quantity,
    ItemLocation Location)
{
    public StoragePriority HaulPriority { get; init; } = StoragePriority.Normal;

    public long? FreshUntilTick { get; init; }
}

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
    int UsedTypeSlots,
    MineralStorageFilter MineralFilter,
    bool SeparatesItemTypes,
    StorageCapability Capabilities,
    EntityId LogisticsNetworkId,
    EntityId StorageAreaId,
    StorageProviderKind ProviderKind,
    StorageResourceFilter ResourceFilter);

public readonly record struct StorageAreaSnapshot(
    EntityId Id,
    string Name,
    IReadOnlyList<GridPosition> Footprint,
    EntityId LogisticsNetworkId,
    IReadOnlyList<EntityId> StorageZoneIds,
    int Capacity,
    int StoredQuantity);

public readonly record struct LogisticsNetworkSnapshot(
    EntityId Id,
    string Name,
    bool IsDefault,
    IReadOnlyList<EntityId> AssignedHaulerIds,
    IReadOnlyList<EntityId> SourceStorageZoneIds,
    IReadOnlyList<EntityId> DestinationStorageZoneIds);
