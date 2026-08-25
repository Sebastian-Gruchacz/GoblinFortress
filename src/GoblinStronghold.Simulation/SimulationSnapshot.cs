using System.Collections.ObjectModel;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation;

public enum ActorJobKind : byte
{
    None = 0,
    Forage = 1,
    Haul = 2,
    Rest = 3,
    Eat = 4,
    Explore = 5,
    Move = 6,
    Resupply = 7,
    ClearVegetation = 8,
    SupplyConstruction = 9,
    BuildConstruction = 10,
    Collapsed = 11,
}

public enum ActorJobStage : byte
{
    None = 0,
    Collecting = 1,
    Delivering = 2,
    ProvisioningFood = 3,
    ProvisioningWater = 4,
}

public enum ActorJobPhase : byte
{
    None = 0,
    Traveling = 1,
    Working = 2,
}

public readonly record struct ActorJobSnapshot(
    ActorJobKind Kind,
    ActorJobPhase Phase,
    ActorJobStage Stage,
    GridPosition Target,
    int RemainingWorkTicks,
    EntityId SourceStackId,
    EntityId DestinationZoneId,
    int ReservedQuantity,
    int RemainingRouteSteps,
    ActorJobKind SuspendedKind,
    GridPosition SuspendedTarget);

[Flags]
public enum GoblinSkill : ushort
{
    None = 0,
    Foraging = 1 << 0,
    Hauling = 1 << 1,
    Survival = 1 << 2,
    Scouting = 1 << 3,
    Building = 1 << 4,
}

public readonly record struct GoblinExperienceSnapshot(
    int Foraging,
    int Hauling,
    int Building)
{
    public static int GetLevel(int experience) => 1 + (experience / 100);

    public static int GetProgressToNextLevel(int experience) => experience % 100;
}

[Flags]
public enum GoblinTrait : ushort
{
    None = 0,
    Stubborn = 1 << 0,
    Curious = 1 << 1,
    Hardy = 1 << 2,
    Gluttonous = 1 << 3,
    Nimble = 1 << 4,
}

[Flags]
public enum PersonalEquipment : ushort
{
    None = 0,
    RagClothes = 1 << 0,
    PrimitiveWaterskin = 1 << 1,
    BoneKnife = 1 << 2,
}

public readonly record struct ActorSnapshot(
    EntityId Id,
    string Name,
    GoblinSkill KnownSkills,
    GoblinTrait KnownTraits,
    PersonalEquipment Equipment,
    GoblinExperienceSnapshot Experience,
    GridPosition Position,
    int Hunger,
    int Fatigue,
    int Health,
    int Thirst,
    int PersonalFood,
    FoodKind PersonalFoodKind,
    int PersonalWater,
    EntityId CarriedStackId,
    ActorJobSnapshot Job);

public enum HumanCohortRole : byte
{
    Farmers = 1,
    Workers = 2,
    Guards = 3,
}

public enum HumanCohortTask : byte
{
    StayNearVillage = 0,
    WorkFields = 1,
    DrawWater = 2,
    ClearLand = 3,
    GatherBerries = 4,
    BuildStorehouse = 5,
    Guard = 6,
    Flee = 7,
}

[Flags]
public enum HumanTool : byte
{
    None = 0,
    WoodenHoe = 1 << 0,
    WoodenAxe = 1 << 1,
    WoodenBucket = 1 << 2,
    WoodenSpear = 1 << 3,
}

public enum HumanFieldPhase : byte
{
    Cleared = 1,
    Sown = 2,
    Growing = 3,
    Ripe = 4,
}

public readonly record struct HumanFieldSnapshot(
    int Id,
    GridPosition Position,
    HumanFieldPhase Phase,
    int GrowthDays);

public readonly record struct HumanCohortSnapshot(
    int Id,
    HumanCohortRole Role,
    int Population,
    GridPosition Position,
    HumanCohortTask Task,
    int SkillLevel,
    HumanTool Tools);

public sealed class HumanVillageSnapshot
{
    internal HumanVillageSnapshot(
        GridPosition anchor,
        int population,
        int foodStock,
        int woodStock,
        int goodsStock,
        int waterStock,
        int plannedFieldCount,
        int storehouseCount,
        int foodCapacity,
        bool goblinAttackOrdered,
        int hostility,
        long lastIntruderSeenTick,
        int guardHitPoints,
        int maximumGuardHitPoints,
        HumanCohortSnapshot[] cohorts,
        HumanFieldSnapshot[] fields)
    {
        Anchor = anchor;
        Population = population;
        FoodStock = foodStock;
        WoodStock = woodStock;
        GoodsStock = goodsStock;
        WaterStock = waterStock;
        PlannedFieldCount = plannedFieldCount;
        StorehouseCount = storehouseCount;
        FoodCapacity = foodCapacity;
        GoblinAttackOrdered = goblinAttackOrdered;
        Hostility = hostility;
        LastIntruderSeenTick = lastIntruderSeenTick;
        GuardHitPoints = guardHitPoints;
        MaximumGuardHitPoints = maximumGuardHitPoints;
        Cohorts = new ReadOnlyCollection<HumanCohortSnapshot>(cohorts);
        Fields = new ReadOnlyCollection<HumanFieldSnapshot>(fields);
    }

    public GridPosition Anchor { get; }

    public int Population { get; }

    public int FoodStock { get; }

    public int WoodStock { get; }

    public int GoodsStock { get; }

    public int WaterStock { get; }

    public int PlannedFieldCount { get; }

    public int StorehouseCount { get; }

    public int FoodCapacity { get; }

    public bool GoblinAttackOrdered { get; }

    public int Hostility { get; }

    public long LastIntruderSeenTick { get; }

    public int GuardHitPoints { get; }

    public int MaximumGuardHitPoints { get; }

    public IReadOnlyList<HumanCohortSnapshot> Cohorts { get; }

    public IReadOnlyList<HumanFieldSnapshot> Fields { get; }
}

public enum GoblinRaidPhase : byte
{
    None = 0,
    Preparing = 1,
    Marching = 2,
}

public sealed class SimulationSnapshot
{
    internal SimulationSnapshot(
        WorldSeed worldSeed,
        SimulationTick tick,
        int foodStock,
        ActorSnapshot[] actors,
        ItemStackSnapshot[] itemStacks,
        StorageZoneSnapshot[] storageZones,
        ConstructionSiteSnapshot[] constructionSites,
        WorkDesignationSnapshot[] workDesignations,
        PlantPatchSnapshot[] plantPatches,
        WorldObjectSnapshot[] worldObjects,
        HumanVillageSnapshot humanVillage,
        GoblinRaidPhase raidPhase,
        GridPosition raidRallyPoint,
        CellVisibility[] visibility,
        ulong worldVersion,
        int mapGeneratorVersion,
        string mapFingerprint,
        string stateHash)
    {
        WorldSeed = worldSeed;
        Tick = tick;
        FoodStock = foodStock;
        Actors = new ReadOnlyCollection<ActorSnapshot>(actors);
        ItemStacks = new ReadOnlyCollection<ItemStackSnapshot>(itemStacks);
        StorageZones = new ReadOnlyCollection<StorageZoneSnapshot>(storageZones);
        ConstructionSites = new ReadOnlyCollection<ConstructionSiteSnapshot>(constructionSites);
        WorkDesignations = new ReadOnlyCollection<WorkDesignationSnapshot>(workDesignations);
        PlantPatches = new ReadOnlyCollection<PlantPatchSnapshot>(plantPatches);
        WorldObjects = new ReadOnlyCollection<WorldObjectSnapshot>(worldObjects);
        HumanVillage = humanVillage;
        RaidPhase = raidPhase;
        RaidRallyPoint = raidRallyPoint;
        Visibility = new ReadOnlyCollection<CellVisibility>(visibility);
        WorldVersion = worldVersion;
        MapGeneratorVersion = mapGeneratorVersion;
        MapFingerprint = mapFingerprint;
        StateHash = stateHash;
    }

    public WorldSeed WorldSeed { get; }

    public SimulationTick Tick { get; }

    public int FoodStock { get; }

    public IReadOnlyList<ActorSnapshot> Actors { get; }

    public IReadOnlyList<ItemStackSnapshot> ItemStacks { get; }

    public IReadOnlyList<StorageZoneSnapshot> StorageZones { get; }

    public IReadOnlyList<ConstructionSiteSnapshot> ConstructionSites { get; }

    public IReadOnlyList<WorkDesignationSnapshot> WorkDesignations { get; }

    public IReadOnlyList<PlantPatchSnapshot> PlantPatches { get; }

    public IReadOnlyList<WorldObjectSnapshot> WorldObjects { get; }

    public HumanVillageSnapshot HumanVillage { get; }

    public GoblinRaidPhase RaidPhase { get; }

    public GridPosition RaidRallyPoint { get; }

    public IReadOnlyList<CellVisibility> Visibility { get; }

    public CellVisibility GetVisibility(GridPosition position, int mapWidth) =>
        Visibility[checked((position.Y * mapWidth) + position.X)];

    public ulong WorldVersion { get; }

    public int MapGeneratorVersion { get; }

    public string MapFingerprint { get; }

    public string StateHash { get; }
}
