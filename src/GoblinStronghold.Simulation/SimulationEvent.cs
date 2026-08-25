namespace GoblinStronghold.Simulation;

public enum SimulationEventKind
{
    FoodGathered = 1,
    ActorAte = 2,
    StorageZoneCreated = 3,
    ItemPickedUp = 4,
    ItemStored = 5,
    ItemStackDepleted = 6,
    CommandRejected = 7,
    ActorDied = 8,
    HumanVillageAlerted = 9,
    HumanGuardHitGoblin = 10,
    GoblinHitHumanGuard = 11,
    HumanDied = 12,
    MoveOrdered = 13,
    MoveCompleted = 14,
    ActorProvisionedFood = 15,
    ActorCollectedWater = 16,
    ActorDrank = 17,
    ConstructionCompleted = 18,
    WorkDesignationCreated = 19,
    WorkDesignationRemoved = 20,
    StoragePullConfigured = 21,
    RaidPreparationStarted = 22,
    RaidDeparted = 23,
    ConstructionOrdered = 24,
    ConstructionMaterialDelivered = 25,
    ActorCollapsed = 26,
    StorageHaulerConfigured = 27,
    StorageSourceConfigured = 28,
    StoragePriorityConfigured = 29,
    ResourcePriorityConfigured = 30,
}

public readonly record struct SimulationEvent(
    ulong Sequence,
    SimulationTick Tick,
    SimulationEventKind Kind,
    EntityId Subject,
    EntityId Target,
    int Amount);
