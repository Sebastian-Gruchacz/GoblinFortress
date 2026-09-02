namespace GoblinStronghold.Simulation.Planning;

public enum WorldToolPlacementMode : byte
{
    Point = 1,
    Line = 2,
    Area = 3,
    FixedFootprint = 4,
    InferredConnection = 5,
    DirectionalConnection = 6,
}
