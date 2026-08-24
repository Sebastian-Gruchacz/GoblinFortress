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
    int ReservedQuantity);

public readonly record struct ActorSnapshot(
    EntityId Id,
    GridPosition Position,
    int Hunger,
    int Fatigue,
    int Health,
    int Thirst,
    int PersonalFood,
    int PersonalWater,
    EntityId CarriedStackId,
    ActorJobSnapshot Job);

public enum HumanCohortRole : byte
{
    Farmers = 1,
    Workers = 2,
    Guards = 3,
}

public readonly record struct HumanCohortSnapshot(
    int Id,
    HumanCohortRole Role,
    int Population,
    GridPosition Position);

public sealed class HumanVillageSnapshot
{
    internal HumanVillageSnapshot(
        GridPosition anchor,
        int population,
        int foodStock,
        int woodStock,
        int goodsStock,
        int hostility,
        long lastIntruderSeenTick,
        int guardHitPoints,
        int maximumGuardHitPoints,
        HumanCohortSnapshot[] cohorts)
    {
        Anchor = anchor;
        Population = population;
        FoodStock = foodStock;
        WoodStock = woodStock;
        GoodsStock = goodsStock;
        Hostility = hostility;
        LastIntruderSeenTick = lastIntruderSeenTick;
        GuardHitPoints = guardHitPoints;
        MaximumGuardHitPoints = maximumGuardHitPoints;
        Cohorts = new ReadOnlyCollection<HumanCohortSnapshot>(cohorts);
    }

    public GridPosition Anchor { get; }

    public int Population { get; }

    public int FoodStock { get; }

    public int WoodStock { get; }

    public int GoodsStock { get; }

    public int Hostility { get; }

    public long LastIntruderSeenTick { get; }

    public int GuardHitPoints { get; }

    public int MaximumGuardHitPoints { get; }

    public IReadOnlyList<HumanCohortSnapshot> Cohorts { get; }
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
        PlantPatchSnapshot[] plantPatches,
        WorldObjectSnapshot[] worldObjects,
        HumanVillageSnapshot humanVillage,
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
        PlantPatches = new ReadOnlyCollection<PlantPatchSnapshot>(plantPatches);
        WorldObjects = new ReadOnlyCollection<WorldObjectSnapshot>(worldObjects);
        HumanVillage = humanVillage;
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

    public IReadOnlyList<PlantPatchSnapshot> PlantPatches { get; }

    public IReadOnlyList<WorldObjectSnapshot> WorldObjects { get; }

    public HumanVillageSnapshot HumanVillage { get; }

    public IReadOnlyList<CellVisibility> Visibility { get; }

    public CellVisibility GetVisibility(GridPosition position, int mapWidth) =>
        Visibility[checked((position.Y * mapWidth) + position.X)];

    public ulong WorldVersion { get; }

    public int MapGeneratorVersion { get; }

    public string MapFingerprint { get; }

    public string StateHash { get; }
}
