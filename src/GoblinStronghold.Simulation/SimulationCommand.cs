using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation;

public enum SimulationCommandKind
{
    Forage = 1,
    CreateStorageZone = 2,
    PickUp = 3,
    StoreCarried = 4,
    Move = 5,
}

public readonly record struct SimulationCommand(
    SimulationTick ExecuteAt,
    ulong Sequence,
    SimulationCommandKind Kind,
    EntityId Subject,
    EntityId Target,
    GridPosition Position,
    ResourceKind Resource,
    int Amount)
{
    public static SimulationCommand Forage(
        SimulationTick executeAt,
        ulong sequence,
        EntityId subject,
        int effort = 1) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.Forage,
            subject,
            EntityId.None,
            default,
            ResourceKind.Food,
            effort);

    public static SimulationCommand CreateStorageZone(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition position,
        ResourceKind acceptedResource,
        int capacity) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.CreateStorageZone,
            EntityId.None,
            EntityId.None,
            position,
            acceptedResource,
            capacity);

    public static SimulationCommand PickUp(
        SimulationTick executeAt,
        ulong sequence,
        EntityId actor,
        EntityId itemStack,
        int quantity) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.PickUp,
            actor,
            itemStack,
            default,
            ResourceKind.Any,
            quantity);

    public static SimulationCommand StoreCarried(
        SimulationTick executeAt,
        ulong sequence,
        EntityId actor,
        EntityId storageZone) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.StoreCarried,
            actor,
            storageZone,
            default,
            ResourceKind.Any,
            Amount: 0);

    public static SimulationCommand Move(
        SimulationTick executeAt,
        ulong sequence,
        EntityId actor,
        GridPosition destination) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.Move,
            actor,
            EntityId.None,
            destination,
            ResourceKind.Any,
            Amount: 0);
}

internal readonly record struct CommandKey(SimulationTick Tick, ulong Sequence) : IComparable<CommandKey>
{
    public int CompareTo(CommandKey other)
    {
        var tickComparison = Tick.CompareTo(other.Tick);
        return tickComparison != 0 ? tickComparison : Sequence.CompareTo(other.Sequence);
    }
}
