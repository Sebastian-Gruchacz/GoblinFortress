using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation;

public enum WorkDesignationKind : byte
{
    GatherFood = 1,
    GatherBrushwood = 2,
    UprootBerryBush = 3,
    FellTree = 4,
    GatherStone = 5,
    QuarryBoulder = 6,
    MineRock = 7,
    Scout = 8,
    HuntAnimal = 9,
    GatherReeds = 10,
    CarveRampDown = 11,
    CarveRampUp = 12,
    CleanBlood = 13,
    DismantleWorldObject = 14,
    DismantleStorageZone = 15,
    GatherLichen = 16,
    StripFloor = 17,
}

public readonly record struct WorkDesignationSnapshot(
    EntityId Id,
    WorkDesignationKind Kind,
    GridPosition Target,
    EntityId TargetEntityId)
{
    public EntityId OrderId { get; init; } = Id;

    public StoragePriority Priority { get; init; } = StoragePriority.Normal;

    public bool IsSuspended { get; init; }

    public GridPosition? RampDestination { get; init; }

    public bool Matches(GridPosition position) => Target == position;
}
