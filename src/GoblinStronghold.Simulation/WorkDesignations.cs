using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation;

public enum WorkDesignationKind : byte
{
    GatherFood = 1,
    GatherBrushwood = 2,
    UprootBerryBush = 3,
    FellTree = 4,
    GatherStone = 5,
    QuarryBoulder = 6,
}

public readonly record struct WorkDesignationSnapshot(
    EntityId Id,
    WorkDesignationKind Kind,
    GridPosition Target,
    EntityId TargetEntityId)
{
    public bool Matches(GridPosition position) => Target == position;
}
