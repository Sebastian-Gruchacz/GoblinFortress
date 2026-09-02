using Godot;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal static class LowerLevelStaticStructurePainter
{
    public static void PaintCell(
        Image target,
        Vector2I origin,
        GridPosition position,
        IReadOnlyList<WorldObjectSnapshot> worldObjects)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(worldObjects);

        foreach (var worldObject in worldObjects)
        {
            foreach (var part in worldObject.GetAbsoluteParts()
                         .Where(item => item.Position == position)
                         .Select(item => item.Part)
                         .OrderBy(item => PaintOrder(item.Kind)))
            {
                PaintPart(target, origin, worldObject, part);
            }
        }
    }

    private static void PaintPart(
        Image target,
        Vector2I origin,
        WorldObjectSnapshot worldObject,
        WorldObjectPartSnapshot part)
    {
        var palette = ResolvePalette(worldObject);
        var size = LowerLevelChunkTextureCache.PixelsPerCell;
        var cell = new Rect2I(origin, new Vector2I(size, size));
        switch (part.Kind)
        {
            case WorldObjectPartKind.Floor:
                target.FillRect(cell, palette.Midtone.Darkened(0.12f));
                DrawFloorJoints(target, origin, palette.Shadow);
                break;
            case WorldObjectPartKind.Walkway:
                target.FillRect(cell.Grow(-1), palette.Midtone);
                DrawLine(target, origin, horizontal: true, palette.Highlight);
                break;
            case WorldObjectPartKind.Wall:
                target.FillRect(cell.Grow(-1), palette.Edge);
                target.FillRect(cell.Grow(-3), palette.Midtone);
                break;
            case WorldObjectPartKind.Door:
            case WorldObjectPartKind.ClosedDoorLeaf:
            case WorldObjectPartKind.OpenDoorLeaf:
            case WorldObjectPartKind.AutomaticallyOpenedDoorLeaf:
                target.FillRect(cell.Grow(-2), palette.Edge);
                DrawLine(
                    target,
                    origin,
                    worldObject.Orientation is CardinalOrientation.East or CardinalOrientation.West,
                    palette.Highlight);
                break;
            case WorldObjectPartKind.Roof:
                target.FillRect(cell, palette.Shadow);
                DrawRoofRidge(target, origin, worldObject.Orientation, palette.Highlight);
                break;
            case WorldObjectPartKind.TreeCrown:
                target.FillRect(cell.Grow(-1), new Color("36542b"));
                break;
            case WorldObjectPartKind.TreeTrunk:
            case WorldObjectPartKind.TreeStump:
            case WorldObjectPartKind.FelledTreeRemains:
                target.FillRect(cell.Grow(-3), new Color("694527"));
                break;
            case WorldObjectPartKind.Boulder:
                target.FillRect(cell.Grow(-2), palette.Edge);
                target.FillRect(cell.Grow(-3), palette.Midtone);
                break;
            case WorldObjectPartKind.WallTorch:
                target.FillRect(new Rect2I(origin + new Vector2I(4, 3), new Vector2I(2, 4)),
                    new Color("f28a24"));
                break;
            case WorldObjectPartKind.PrimitiveWorkshop:
            case WorldObjectPartKind.Bloomery:
            case WorldObjectPartKind.SmeltingFurnace:
            case WorldObjectPartKind.CrucibleFurnace:
                target.FillRect(cell.Grow(-2), palette.Edge);
                target.FillRect(cell.Grow(-3), palette.Midtone);
                break;
            case WorldObjectPartKind.ConstructedRamp:
                target.FillRect(cell.Grow(-1), palette.Midtone);
                DrawRampSteps(target, origin, worldObject.Orientation, palette.Edge);
                break;
            case WorldObjectPartKind.WellRim:
            case WorldObjectPartKind.WellShaft:
                target.FillRect(cell.Grow(-2), palette.Edge);
                target.FillRect(cell.Grow(-4), new Color("17191a"));
                break;
        }
    }

    private static MaterialPaletteColors ResolvePalette(WorldObjectSnapshot worldObject)
    {
        if (worldObject.MaterialVariant != ResourceVariant.None)
        {
            return MaterialPaletteColors.For(worldObject.MaterialVariant);
        }

        return worldObject.Kind switch
        {
            WorldObjectKind.StoneWall or WorldObjectKind.StoneDoorFrame or
                WorldObjectKind.StoneFloor or WorldObjectKind.StoneRamp or
                WorldObjectKind.BasaltWalkway or WorldObjectKind.Boulder =>
                MaterialPaletteColors.For(ResourceVariant.Granite),
            _ => MaterialPaletteColors.For(ResourceVariant.PineWood),
        };
    }

    private static int PaintOrder(WorldObjectPartKind kind) => kind switch
    {
        WorldObjectPartKind.Floor or WorldObjectPartKind.Walkway => 0,
        WorldObjectPartKind.Roof => 2,
        _ => 1,
    };

    private static void DrawFloorJoints(Image target, Vector2I origin, Color color)
    {
        for (var offset = 2; offset < LowerLevelChunkTextureCache.PixelsPerCell; offset += 4)
        {
            target.FillRect(
                new Rect2I(origin + new Vector2I(0, offset), new Vector2I(10, 1)),
                color);
        }
    }

    private static void DrawRoofRidge(
        Image target,
        Vector2I origin,
        CardinalOrientation orientation,
        Color color) => DrawLine(
            target,
            origin,
            orientation is CardinalOrientation.North or CardinalOrientation.South,
            color);

    private static void DrawLine(
        Image target,
        Vector2I origin,
        bool horizontal,
        Color color)
    {
        var rectangle = horizontal
            ? new Rect2I(origin + new Vector2I(1, 4), new Vector2I(8, 1))
            : new Rect2I(origin + new Vector2I(4, 1), new Vector2I(1, 8));
        target.FillRect(rectangle, color);
    }

    private static void DrawRampSteps(
        Image target,
        Vector2I origin,
        CardinalOrientation orientation,
        Color color)
    {
        var horizontal = orientation is CardinalOrientation.North or CardinalOrientation.South;
        for (var offset = 2; offset < 9; offset += 3)
        {
            var rectangle = horizontal
                ? new Rect2I(origin + new Vector2I(1, offset), new Vector2I(8, 1))
                : new Rect2I(origin + new Vector2I(offset, 1), new Vector2I(1, 8));
            target.FillRect(rectangle, color);
        }
    }
}
