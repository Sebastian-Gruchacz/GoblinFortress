using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Construction;

public static class ConstructionDismantlingPolicy
{
    public const int WorkTimePercent = 30;

    public static int GetWorkTicks(ConstructionKind kind, int footprintCellCount = 1)
    {
        var buildTicks = ConstructionBlueprintDefinitions.Get(kind)
            .GetWorkTicks(Math.Max(1, footprintCellCount));
        return Math.Max(1, checked((buildTicks * WorkTimePercent + 99) / 100));
    }

    public static bool TryGetConstructionKind(
        WorldObjectKind worldObject,
        out ConstructionKind construction)
    {
        construction = worldObject switch
        {
            WorldObjectKind.GoblinHut => ConstructionKind.GoblinHut,
            WorldObjectKind.WoodenWalkway => ConstructionKind.WoodenWalkway,
            WorldObjectKind.GoblinFieldCamp => ConstructionKind.GoblinFieldCamp,
            WorldObjectKind.WoodenWall => ConstructionKind.WoodenWall,
            WorldObjectKind.WoodenDoorFrame => ConstructionKind.WoodenDoorFrame,
            WorldObjectKind.WoodenDoorLeaf => ConstructionKind.WoodenDoor,
            WorldObjectKind.StoneWall => ConstructionKind.StoneWall,
            WorldObjectKind.StoneDoorFrame => ConstructionKind.StoneDoorFrame,
            WorldObjectKind.WallTorch => ConstructionKind.WallTorch,
            WorldObjectKind.PrimitiveWorkshop => ConstructionKind.PrimitiveWorkshop,
            WorldObjectKind.BasaltWalkway => ConstructionKind.BasaltWalkway,
            WorldObjectKind.Bloomery => ConstructionKind.Bloomery,
            WorldObjectKind.SmeltingFurnace => ConstructionKind.SmeltingFurnace,
            WorldObjectKind.CrucibleFurnace => ConstructionKind.CrucibleFurnace,
            WorldObjectKind.WoodenFloor => ConstructionKind.WoodenFloor,
            WorldObjectKind.StoneFloor => ConstructionKind.StoneFloor,
            WorldObjectKind.WoodenRamp => ConstructionKind.WoodenRamp,
            WorldObjectKind.StoneRamp => ConstructionKind.StoneRamp,
            _ => default,
        };
        return construction != default;
    }
}
