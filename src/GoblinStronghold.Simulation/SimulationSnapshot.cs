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
    FellTree = 12,
    QuarryBoulder = 13,
    MineRock = 14,
    TendBud = 15,
    HuntAnimal = 16,
    SupplyCrafting = 17,
    Craft = 18,
    ClearConstructionSite = 19,
    CarveRamp = 20,
    CleanBlood = 21,
    LootRaid = 22,
    RecoverRaidCorpse = 23,
    ConsumeRaidCorpse = 24,
}

public readonly record struct GoblinBudSnapshot(
    EntityId Id,
    EntityId ParentId,
    GridPosition Position,
    int RemainingCareTicks,
    int TotalCareTicks)
{
    public EntityId OriginCorpseId { get; init; }

    public GoblinInheritanceImprint OriginImprint { get; init; }
}

public enum GoblinReproductionReadinessKind : byte
{
    Ready = 1,
    InsufficientFood = 2,
    NoMoistSpace = 3,
    NoEligibleParent = 4,
    BudWaitingForCare = 5,
    BudBeingTended = 6,
    InsufficientShelter = 7,
    InsufficientAdultPopulation = 8,
    UnsafeConditions = 9,
    JuvenileCapacityReached = 10,
}

public readonly record struct GoblinReproductionReadinessSnapshot(
    GoblinReproductionReadinessKind Kind,
    int RequiredFood,
    int AvailableFood,
    int SuitableMoistSites,
    int EligibleParents,
    int UntendedBuds);

public readonly record struct TribeNeedsSnapshot(
    int FoodUnits,
    int ExpectedDailyFoodUnits,
    int ShelterCapacity,
    int StorageCapacity,
    int StoredUnits,
    int KnownLooseUnits,
    int SuitableMoistSites,
    int HealthyWorkers,
    int WorkDemand,
    int HumanHostility,
    GoblinReproductionReadinessSnapshot Reproduction);

public enum AnimalKind : byte
{
    MarshHare = 1,
    SwampBoar = 2,
    CaveSpider = 3,
    DeepCrawler = 4,
    MagmaWyrm = 5,
}

public enum AnimalActivity : byte
{
    Roaming = 0,
    Foraging = 1,
    Resting = 2,
    Fleeing = 3,
    Threatening = 4,
}

public enum AnimalSex : byte
{
    Female = 1,
    Male = 2,
}

public readonly record struct AnimalSnapshot(
    ulong Id,
    AnimalKind Kind,
    AnimalSex Sex,
    GridPosition Position,
    AnimalActivity Activity,
    int Health,
    int MaximumHealth,
    int Hunger,
    int Fatigue,
    int MaximumFatigue,
    long AgeTicks,
    long MaturityAgeTicks,
    long MaximumAgeTicks)
{
    public bool IsAdult => AgeTicks >= MaturityAgeTicks;
}

public enum ActorJobStage : byte
{
    None = 0,
    Collecting = 1,
    Delivering = 2,
    ProvisioningFood = 3,
    ProvisioningWater = 4,
    ProvisioningAmmo = 5,
    ProvisioningEquipment = 6,
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

public enum ActorPlanIntentKind : byte
{
    CurrentJob = 0,
    Eat = 1,
    FindFood = 2,
    Drink = 3,
    RefillWater = 4,
    Rest = 5,
    ResumeSuspendedJob = 6,
    NextPublicWork = 7,
}

public readonly record struct ActorPlanEntrySnapshot(
    ActorPlanIntentKind Kind,
    ActorJobKind JobKind,
    int Priority,
    GridPosition Target)
{
    public EntityId WorkOrderId { get; init; }
}

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

public readonly record struct GoblinWorkPreferences(
    int Foraging,
    int Hauling,
    int Building)
{
    public const int Minimum = -2;
    public const int Maximum = 2;

    public bool IsValid =>
        Foraging is >= Minimum and <= Maximum &&
        Hauling is >= Minimum and <= Maximum &&
        Building is >= Minimum and <= Maximum;
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
    Fastidious = 1 << 5,
}

[Flags]
public enum PersonalEquipment : ushort
{
    None = 0,
    RagClothes = 1 << 0,
    PrimitiveWaterskin = 1 << 1,
    BoneKnife = 1 << 2,
    WoodenAxe = 1 << 3,
    PrimitivePickaxe = 1 << 4,
    PrimitiveSling = 1 << 5,
    FightingStick = 1 << 6,
    StoneClub = 1 << 7,
    HideClothes = 1 << 8,
    ReedClothes = 1 << 9,
    ReinforcedPickaxe = 1 << 10,
    WoodenBucket = 1 << 11,
}

public sealed class PersonalFoodContentsSnapshot : IReadOnlyList<FoodKind>,
    IEquatable<PersonalFoodContentsSnapshot>
{
    private readonly FoodKind[] _items;

    public PersonalFoodContentsSnapshot(IEnumerable<FoodKind> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items.ToArray();
    }

    public int Count => _items.Length;

    public FoodKind this[int index] => _items[index];

    public bool Equals(PersonalFoodContentsSnapshot? other) =>
        other is not null && _items.SequenceEqual(other._items);

    public override bool Equals(object? obj) =>
        obj is PersonalFoodContentsSnapshot other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in _items)
        {
            hash.Add(item);
        }
        return hash.ToHashCode();
    }

    public IEnumerator<FoodKind> GetEnumerator() =>
        ((IEnumerable<FoodKind>)_items).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        _items.GetEnumerator();
}

public readonly record struct ActorSnapshot(
    EntityId Id,
    string Name,
    GoblinSkill KnownSkills,
    GoblinTrait KnownTraits,
    PersonalEquipment Equipment,
    GoblinExperienceSnapshot Experience,
    GoblinWorkPreferences WorkPreferences,
    GridPosition Position,
    int Hunger,
    int Fatigue,
    int Health,
    int BleedingTicksRemaining,
    int Thirst,
    int PersonalFood,
    FoodKind PersonalFoodKind,
    PersonalFoodContentsSnapshot PersonalFoodKinds,
    int PersonalWater,
    int PersonalStoneAmmo,
    int AgeDays,
    bool IsJuvenile,
    bool IsElderly,
    int EffectiveMaximumHealth,
    double SenescenceProgress,
    EntityId CarriedStackId,
    ActorJobSnapshot Job,
    IReadOnlyList<ActorPlanEntrySnapshot> Plan)
{
    public EquipmentLoadoutSnapshot Loadout { get; init; } = new([], 0, 0, 0);

    public EntityId CarriedCorpseId { get; init; }

    public ActorTacticalOrderSnapshot TacticalOrder { get; init; }

    public long DispatcherSuspendedUntilTick { get; init; }
}

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
    CraftGoods = 8,
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
    int GrowthDays,
    int WorkProgress);

public readonly record struct HumanCohortSnapshot(
    int Id,
    HumanCohortRole Role,
    int Population,
    GridPosition Position,
    HumanCohortTask Task,
    int SkillLevel,
    HumanTool Tools);

public readonly record struct HumanVillagerSnapshot(
    int Id,
    string Name,
    HumanCohortRole Role,
    GridPosition Position,
    HumanCohortTask Task,
    int SkillLevel,
    HumanTool Tools,
    int Health,
    int MaximumHealth,
    int Fatigue,
    int MaximumFatigue,
    int Hunger,
    int Thirst,
    int MaximumNeed,
    int WorkProgress);

public sealed class HumanVillageSnapshot
{
    internal HumanVillageSnapshot(
        GridPosition anchor,
        int population,
        int foodStock,
        int grainStock,
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
        GridPosition? treeFellingTarget,
        int treeFellingProgress,
        int goodsWorkProgress,
        GridPosition? storehouseSite,
        int storehouseWorkProgress,
        HumanCohortSnapshot[] cohorts,
        HumanVillagerSnapshot[] villagers,
        HumanFieldSnapshot[] fields)
    {
        Anchor = anchor;
        Population = population;
        FoodStock = foodStock;
        GrainStock = grainStock;
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
        TreeFellingTarget = treeFellingTarget;
        TreeFellingProgress = treeFellingProgress;
        GoodsWorkProgress = goodsWorkProgress;
        StorehouseSite = storehouseSite;
        StorehouseWorkProgress = storehouseWorkProgress;
        Cohorts = new ReadOnlyCollection<HumanCohortSnapshot>(cohorts);
        Villagers = new ReadOnlyCollection<HumanVillagerSnapshot>(villagers);
        Fields = new ReadOnlyCollection<HumanFieldSnapshot>(fields);
    }

    public GridPosition Anchor { get; }

    public int Population { get; }

    public int FoodStock { get; }

    public int GrainStock { get; }

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

    public GridPosition? TreeFellingTarget { get; }

    public int TreeFellingProgress { get; }

    public int GoodsWorkProgress { get; }

    public GridPosition? StorehouseSite { get; }

    public int StorehouseWorkProgress { get; }

    public IReadOnlyList<HumanCohortSnapshot> Cohorts { get; }

    public IReadOnlyList<HumanVillagerSnapshot> Villagers { get; }

    public IReadOnlyList<HumanFieldSnapshot> Fields { get; }
}

public enum GoblinRaidPhase : byte
{
    None = 0,
    Preparing = 1,
    Marching = 2,
    Suspended = 3,
    Ready = 4,
    Looting = 5,
    Returning = 6,
}

public sealed class SimulationSnapshot
{
    internal SimulationSnapshot(
        WorldSeed worldSeed,
        SimulationTick tick,
        int foodStock,
        ActorSnapshot[] actors,
        GoblinBudSnapshot[] goblinBuds,
        TribeNeedsSnapshot tribeNeeds,
        AnimalSnapshot[] animals,
        UndergroundFactionSnapshot[] undergroundFactions,
        UndergroundFactionRelationSnapshot[] undergroundFactionRelations,
        CorpseSnapshot[] corpses,
        VillageLootContainerSnapshot[] villageLootContainers,
        ItemStackSnapshot[] itemStacks,
        StorageZoneSnapshot[] storageZones,
        StorageAreaSnapshot[] storageAreas,
        LogisticsNetworkSnapshot[] logisticsNetworks,
        ResourcePrioritySnapshot[] resourcePriorities,
        ResourceInventorySnapshot[] resourceInventory,
        ConstructionSiteSnapshot[] constructionSites,
        CraftingOrderSnapshot[] craftingOrders,
        WorkDesignationSnapshot[] workDesignations,
        PlantPatchSnapshot[] plantPatches,
        WorldObjectSnapshot[] worldObjects,
        BloodStainSnapshot[] bloodStains,
        HumanVillageSnapshot humanVillage,
        GoblinRaidPhase raidPhase,
        GridPosition raidRallyPoint,
        EntityId[] raidPartyIds,
        bool raidRosterConfigured,
        GridPosition raidTarget,
        int raidTargetRadius,
        RaidDirective raidDirectives,
        CellVisibility[] visibility,
        int visibilityLayerCellCount,
        int visibilityNegativeLevelCount,
        ulong worldVersion,
        int mapGeneratorVersion,
        string mapFingerprint,
        string stateHash)
    {
        WorldSeed = worldSeed;
        Tick = tick;
        FoodStock = foodStock;
        Actors = new ReadOnlyCollection<ActorSnapshot>(actors);
        GoblinBuds = new ReadOnlyCollection<GoblinBudSnapshot>(goblinBuds);
        TribeNeeds = tribeNeeds;
        Animals = new ReadOnlyCollection<AnimalSnapshot>(animals);
        UndergroundFactions = new ReadOnlyCollection<UndergroundFactionSnapshot>(
            undergroundFactions);
        UndergroundFactionRelations =
            new ReadOnlyCollection<UndergroundFactionRelationSnapshot>(
                undergroundFactionRelations);
        Corpses = new ReadOnlyCollection<CorpseSnapshot>(corpses);
        VillageLootContainers = new ReadOnlyCollection<VillageLootContainerSnapshot>(
            villageLootContainers);
        ItemStacks = new ReadOnlyCollection<ItemStackSnapshot>(itemStacks);
        StorageZones = new ReadOnlyCollection<StorageZoneSnapshot>(storageZones);
        StorageAreas = new ReadOnlyCollection<StorageAreaSnapshot>(storageAreas);
        LogisticsNetworks = new ReadOnlyCollection<LogisticsNetworkSnapshot>(logisticsNetworks);
        ResourcePriorities = new ReadOnlyCollection<ResourcePrioritySnapshot>(resourcePriorities);
        ResourceInventory = new ReadOnlyCollection<ResourceInventorySnapshot>(resourceInventory);
        ConstructionSites = new ReadOnlyCollection<ConstructionSiteSnapshot>(constructionSites);
        CraftingOrders = new ReadOnlyCollection<CraftingOrderSnapshot>(craftingOrders);
        WorkDesignations = new ReadOnlyCollection<WorkDesignationSnapshot>(workDesignations);
        PlantPatches = new ReadOnlyCollection<PlantPatchSnapshot>(plantPatches);
        WorldObjects = new ReadOnlyCollection<WorldObjectSnapshot>(worldObjects);
        BloodStains = new ReadOnlyCollection<BloodStainSnapshot>(bloodStains);
        HumanVillage = humanVillage;
        RaidPhase = raidPhase;
        RaidRallyPoint = raidRallyPoint;
        RaidPartyIds = new ReadOnlyCollection<EntityId>(raidPartyIds);
        RaidRosterConfigured = raidRosterConfigured;
        RaidPlan = new RaidPlanSnapshot(
            raidRallyPoint,
            raidTarget,
            raidTargetRadius,
            raidDirectives);
        Visibility = new ReadOnlyCollection<CellVisibility>(visibility);
        VisibilityLayerCellCount = visibilityLayerCellCount;
        VisibilityNegativeLevelCount = visibilityNegativeLevelCount;
        WorldVersion = worldVersion;
        MapGeneratorVersion = mapGeneratorVersion;
        MapFingerprint = mapFingerprint;
        StateHash = stateHash;
    }

    public WorldSeed WorldSeed { get; }

    public SimulationTick Tick { get; }

    public int FoodStock { get; }

    public IReadOnlyList<ActorSnapshot> Actors { get; }

    public IReadOnlyList<GoblinBudSnapshot> GoblinBuds { get; }

    public TribeNeedsSnapshot TribeNeeds { get; }

    public IReadOnlyList<AnimalSnapshot> Animals { get; }

    public IReadOnlyList<UndergroundFactionSnapshot> UndergroundFactions { get; }

    public IReadOnlyList<UndergroundFactionRelationSnapshot> UndergroundFactionRelations { get; }

    public IReadOnlyList<CorpseSnapshot> Corpses { get; }

    public IReadOnlyList<VillageLootContainerSnapshot> VillageLootContainers { get; }

    public IReadOnlyList<ItemStackSnapshot> ItemStacks { get; }

    public IReadOnlyList<StorageZoneSnapshot> StorageZones { get; }

    public IReadOnlyList<StorageAreaSnapshot> StorageAreas { get; }

    public IReadOnlyList<LogisticsNetworkSnapshot> LogisticsNetworks { get; }

    public IReadOnlyList<ResourcePrioritySnapshot> ResourcePriorities { get; }

    public IReadOnlyList<ResourceInventorySnapshot> ResourceInventory { get; }

    public IReadOnlyList<ConstructionSiteSnapshot> ConstructionSites { get; }

    public IReadOnlyList<CraftingOrderSnapshot> CraftingOrders { get; }

    public IReadOnlyList<WorkDesignationSnapshot> WorkDesignations { get; }

    public IReadOnlyList<PlantPatchSnapshot> PlantPatches { get; }

    public IReadOnlyList<WorldObjectSnapshot> WorldObjects { get; }

    public IReadOnlyList<BloodStainSnapshot> BloodStains { get; }

    public HumanVillageSnapshot HumanVillage { get; }

    public GoblinRaidPhase RaidPhase { get; }

    public GridPosition RaidRallyPoint { get; }

    public IReadOnlyList<EntityId> RaidPartyIds { get; }

    public bool RaidRosterConfigured { get; }

    public RaidPlanSnapshot RaidPlan { get; }

    public IReadOnlyList<CellVisibility> Visibility { get; }

    public int VisibilityLayerCellCount { get; }

    public int VisibilityNegativeLevelCount { get; }

    public CellVisibility GetVisibility(GridPosition position, int mapWidth) =>
        Visibility[checked(
            ((position.Z <= 0 ? -position.Z : VisibilityNegativeLevelCount + position.Z) *
             VisibilityLayerCellCount) +
            (position.Y * mapWidth) + position.X)];

    public ulong WorldVersion { get; }

    public int MapGeneratorVersion { get; }

    public string MapFingerprint { get; }

    public string StateHash { get; }
}
