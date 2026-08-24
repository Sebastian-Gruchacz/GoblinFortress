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
    byte TraversalCost)
{
    public bool IsTraversable => TraversalCost > 0;
}
