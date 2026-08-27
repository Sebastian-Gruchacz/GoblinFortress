namespace GoblinStronghold.Simulation.Map;

public enum CellVolumeKind : byte
{
    Open = 0,
    Solid = 1,
}

public enum CellSupportKind : byte
{
    None = 0,
    NaturalFlat = 1,
    NaturalRamp = 2,
}

public enum CellFluidKind : byte
{
    None = 0,
    Water = 1,
    Lava = 2,
}

/// <summary>
/// Initial physical state baked by the map generator at one authoritative XYZ coordinate.
/// It deliberately does not distinguish a surface world from an underground world.
/// </summary>
public readonly record struct InitialCellGeometry(
    CellVolumeKind Volume,
    RockKind? SolidMaterial = null,
    CellSupportKind Support = CellSupportKind.None,
    TerrainKind? Cover = null,
    CellFluidKind Fluid = CellFluidKind.None,
    int FluidDepthLevels = 0,
    TerrainRampDirection RampDirection = TerrainRampDirection.None)
{
    public bool IsSolid => Volume == CellVolumeKind.Solid;

    public bool IsSupported => Support != CellSupportKind.None;

    public bool IsOccupiable => !IsSolid && IsSupported && FluidDepthLevels == 0;
}
