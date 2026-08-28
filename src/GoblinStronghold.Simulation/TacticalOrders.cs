using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation;

public enum ActorTacticalOrderKind : byte
{
    None = 0,
    Patrol = 1,
    AttackArea = 2,
    HuntArea = 3,
}

public readonly record struct ActorTacticalOrderSnapshot(
    ActorTacticalOrderKind Kind,
    GridPosition Center,
    int Radius,
    IReadOnlyList<GridPosition> PatrolPoints,
    int PatrolPointIndex);
