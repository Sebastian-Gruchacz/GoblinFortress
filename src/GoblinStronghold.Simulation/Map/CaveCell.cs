namespace GoblinStronghold.Simulation.Map;

public enum RockKind : byte
{
    Sandstone = 1,
    Granite = 2,
}

public enum CaveCellKind : byte
{
    SolidRock = 1,
    Floor = 2,
    Ramp = 3,
}

public readonly record struct CaveCell(
    RockKind Rock,
    CaveCellKind Kind)
{
    public bool IsOpen => Kind is CaveCellKind.Floor or CaveCellKind.Ramp;
}

public enum VerticalPassageKind : byte
{
    CaveMouth = 1,
    NaturalRamp = 2,
}

public readonly record struct VerticalPassage(
    GridPosition Upper,
    GridPosition Lower,
    VerticalPassageKind Kind);
