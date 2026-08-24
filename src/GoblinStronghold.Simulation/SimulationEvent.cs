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
}

public readonly record struct SimulationEvent(
    ulong Sequence,
    SimulationTick Tick,
    SimulationEventKind Kind,
    EntityId Subject,
    EntityId Target,
    int Amount);
