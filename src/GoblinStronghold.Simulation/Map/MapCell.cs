namespace GoblinStronghold.Simulation.Map;

public enum TerrainKind : byte
{
    SolidGround = 1,
    Mud = 2,
    ShallowWater = 3,
    DeepWater = 4,
}

public enum TerrainRampDirection : byte
{
    None = 0,
    North = 1,
    East = 2,
    South = 3,
    West = 4,
}

public readonly record struct MapCell(
    TerrainKind Terrain,
    byte Moisture,
    byte Fertility,
    byte TraversalCost,
    sbyte FloorLevel = 0,
    sbyte SurfaceLevel = 0,
    TerrainRampDirection RampDirection = TerrainRampDirection.None)
{
    public bool HasFloorAtSurface => FloorLevel == SurfaceLevel;

    public int WaterDepthLevels => Terrain switch
    {
        TerrainKind.ShallowWater => 0,
        TerrainKind.DeepWater => Math.Max(1, SurfaceLevel - FloorLevel),
        _ => 0,
    };

    public bool IsTraversable => TraversalCost > 0 && HasFloorAtSurface;
}
