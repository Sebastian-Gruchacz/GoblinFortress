namespace GoblinStronghold.Simulation.Map;

public enum RockKind : byte
{
    Sandstone = 1,
    Granite = 2,
    Basalt = 3,
    Obsidian = 4,
}

public enum MineralDepositKind : byte
{
    None = 0,
    Coal = 1,
    IronOre = 2,
    CopperOre = 3,
    SilverOre = 4,
    GoldOre = 5,
    Ruby = 6,
    Emerald = 7,
    Diamond = 8,
}

public enum CaveCellKind : byte
{
    SolidRock = 1,
    Floor = 2,
    Ramp = 3,
}

public readonly record struct CaveCell(
    RockKind Rock,
    CaveCellKind Kind,
    MineralDepositKind Deposit = MineralDepositKind.None,
    CellFluidKind Fluid = CellFluidKind.None)
{
    public bool IsOpen => Kind is CaveCellKind.Floor or CaveCellKind.Ramp;
}

public enum VerticalPassageKind : byte
{
    CaveMouth = 1,
    NaturalRamp = 2,
    ExcavatedRamp = 3,
}

public readonly record struct VerticalPassage(
    GridPosition Upper,
    GridPosition Lower,
    VerticalPassageKind Kind);
