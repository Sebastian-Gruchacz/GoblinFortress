namespace GoblinStronghold.Simulation.Map;

public enum TerrainKind : byte
{
    SolidGround = 1,
    Mud = 2,
    ShallowWater = 3,
    DeepWater = 4,
}

public readonly record struct MapCell(
    TerrainKind Terrain,
    byte Moisture,
    byte Fertility,
    byte TraversalCost,
    sbyte FloorLevel = 0)
{
    public bool HasFloorAtSurface => FloorLevel == 0;

    public int WaterDepthLevels => Terrain switch
    {
        TerrainKind.ShallowWater => 0,
        TerrainKind.DeepWater => Math.Max(1, -FloorLevel),
        _ => 0,
    };

    public bool IsTraversable => TraversalCost > 0 && HasFloorAtSurface;
}
