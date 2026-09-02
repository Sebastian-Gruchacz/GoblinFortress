using GoblinStronghold.Simulation;
using Godot;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal readonly record struct LowerLevelStaticStructurePart(
    WorldObjectSnapshot WorldObject,
    WorldObjectPartSnapshot Part);

internal static class LowerLevelStaticStructurePainter
{
    public static void PaintCell(
        Image target,
        Vector2I origin,
        IReadOnlyList<LowerLevelStaticStructurePart> parts,
        Image environmentAtlas,
        Image itemIconAtlas,
        Image treePartAtlas,
        Image treeCrownAtlas,
        WorldSeed worldSeed,
        int mapWidth)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(environmentAtlas);
        ArgumentNullException.ThrowIfNull(itemIconAtlas);
        ArgumentNullException.ThrowIfNull(treePartAtlas);
        ArgumentNullException.ThrowIfNull(treeCrownAtlas);

        foreach (var item in parts.OrderBy(item => PaintOrder(item.Part.Kind)))
        {
            PaintPart(
                target,
                origin,
                item.WorldObject,
                item.Part,
                environmentAtlas,
                itemIconAtlas,
                treePartAtlas,
                treeCrownAtlas,
                worldSeed,
                mapWidth);
        }
    }

    private static void PaintPart(
        Image target,
        Vector2I origin,
        WorldObjectSnapshot worldObject,
        WorldObjectPartSnapshot part,
        Image environmentAtlas,
        Image itemIconAtlas,
        Image treePartAtlas,
        Image treeCrownAtlas,
        WorldSeed worldSeed,
        int mapWidth)
    {
        var palette = ResolvePalette(worldObject);
        var size = LowerLevelChunkTextureCache.PixelsPerCell;
        var cell = new Rect2I(origin, new Vector2I(size, size));
        if (TryPaintIllustratedGoblinStructure(
                target,
                origin,
                worldObject,
                part,
                environmentAtlas))
        {
            return;
        }
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
                PaintTreeCrown(
                    target,
                    origin,
                    worldObject,
                    part,
                    treeCrownAtlas,
                    worldSeed,
                    mapWidth);
                break;
            case WorldObjectPartKind.TreeTrunk:
            case WorldObjectPartKind.TreeStump:
            case WorldObjectPartKind.FelledTreeRemains:
                PaintTreePart(
                    target,
                    origin,
                    worldObject,
                    part.Kind,
                    treePartAtlas,
                    worldSeed,
                    mapWidth);
                break;
            case WorldObjectPartKind.Boulder:
                PaintBoulder(target, origin, itemIconAtlas);
                break;
            case WorldObjectPartKind.WallTorch:
                target.FillRect(new Rect2I(
                    origin + new Vector2I((size / 2) - 1, size / 3),
                    new Vector2I(2, Math.Max(3, size / 3))),
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

    private static bool TryPaintIllustratedGoblinStructure(
        Image target,
        Vector2I origin,
        WorldObjectSnapshot worldObject,
        WorldObjectPartSnapshot part,
        Image environmentAtlas)
    {
        if (worldObject.Anchor.Z >= 0 ||
            worldObject.Kind is not (WorldObjectKind.GoblinHut or
                WorldObjectKind.GoblinFieldCamp) ||
            part.Kind is not (WorldObjectPartKind.Floor or WorldObjectPartKind.Roof))
        {
            return false;
        }

        var footprint = worldObject.Parts
            .Where(candidate => candidate.Kind == WorldObjectPartKind.Floor)
            .Select(candidate => candidate.RelativePosition)
            .ToArray();
        if (footprint.Length == 0)
        {
            return false;
        }

        var minimumX = footprint.Min(position => position.X);
        var minimumY = footprint.Min(position => position.Y);
        var width = footprint.Max(position => position.X) - minimumX + 1;
        var height = footprint.Max(position => position.Y) - minimumY + 1;
        var column = Math.Clamp(part.RelativePosition.X - minimumX, 0, width - 1);
        var row = Math.Clamp(part.RelativePosition.Y - minimumY, 0, height - 1);
        var sprite = worldObject.Kind == WorldObjectKind.GoblinHut
            ? EnvironmentSprite.GoblinHutRoof
            : EnvironmentSprite.FieldCampRoof;
        var region = EnvironmentSprites.GetRegionFromImage(environmentAtlas, sprite);
        var left = region.Position.X + (column * region.Size.X / width);
        var top = region.Position.Y + (row * region.Size.Y / height);
        var right = region.Position.X + ((column + 1) * region.Size.X / width);
        var bottom = region.Position.Y + ((row + 1) * region.Size.Y / height);
        using var image = ExtractScaled(
            environmentAtlas,
            new Rect2I(left, top, right - left, bottom - top),
            LowerLevelChunkTextureCache.PixelsPerCell);
        target.BlendRect(
            image,
            new Rect2I(Vector2I.Zero, image.GetSize()),
            origin);
        return true;
    }

    private static void PaintBoulder(Image target, Vector2I origin, Image itemIconAtlas)
    {
        using var image = ExtractScaled(
            itemIconAtlas,
            ItemIcons.GetRegionFromImage(itemIconAtlas, ItemIcon.Stone),
            LowerLevelChunkTextureCache.PixelsPerCell);
        target.BlendRect(
            image,
            new Rect2I(Vector2I.Zero, image.GetSize()),
            origin);
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
        var size = LowerLevelChunkTextureCache.PixelsPerCell;
        for (var offset = 3; offset < size; offset += 5)
        {
            target.FillRect(
                new Rect2I(origin + new Vector2I(0, offset), new Vector2I(size, 1)),
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
        var size = LowerLevelChunkTextureCache.PixelsPerCell;
        var center = size / 2;
        var rectangle = horizontal
            ? new Rect2I(origin + new Vector2I(1, center), new Vector2I(size - 2, 1))
            : new Rect2I(origin + new Vector2I(center, 1), new Vector2I(1, size - 2));
        target.FillRect(rectangle, color);
    }

    private static void DrawRampSteps(
        Image target,
        Vector2I origin,
        CardinalOrientation orientation,
        Color color)
    {
        var size = LowerLevelChunkTextureCache.PixelsPerCell;
        var horizontal = orientation is CardinalOrientation.North or CardinalOrientation.South;
        for (var offset = 2; offset < size - 1; offset += 4)
        {
            var rectangle = horizontal
                ? new Rect2I(origin + new Vector2I(1, offset), new Vector2I(size - 2, 1))
                : new Rect2I(origin + new Vector2I(offset, 1), new Vector2I(1, size - 2));
            target.FillRect(rectangle, color);
        }
    }

    private static void PaintTreePart(
        Image target,
        Vector2I origin,
        WorldObjectSnapshot worldObject,
        WorldObjectPartKind kind,
        Image atlas,
        WorldSeed worldSeed,
        int mapWidth)
    {
        var sprite = kind switch
        {
            WorldObjectPartKind.TreeTrunk => TreePartSprite.StandingTrunk,
            WorldObjectPartKind.TreeStump => TreePartSprite.CutStump,
            WorldObjectPartKind.FelledTreeRemains => TreePartSprite.FelledRemains,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        var variant = WoodMaterialPolicy.VariantFor(worldSeed, mapWidth, worldObject.Anchor);
        using var image = ExtractScaled(
            atlas,
            TreePartSprites.GetRegionFromImage(atlas, sprite),
            LowerLevelChunkTextureCache.PixelsPerCell);
        Modulate(image, TreePartSprites.GetWoodModulate(variant));
        target.BlendRect(
            image,
            new Rect2I(Vector2I.Zero, image.GetSize()),
            origin);
    }

    private static void PaintTreeCrown(
        Image target,
        Vector2I origin,
        WorldObjectSnapshot worldObject,
        WorldObjectPartSnapshot part,
        Image atlas,
        WorldSeed worldSeed,
        int mapWidth)
    {
        var variant = WoodMaterialPolicy.VariantFor(worldSeed, mapWidth, worldObject.Anchor);
        var crown = TreeCrownSprites.GetRegionFromImage(atlas, variant);
        var column = Math.Clamp(part.RelativePosition.X + 1, 0, 2);
        var row = Math.Clamp(part.RelativePosition.Y + 1, 0, 2);
        var left = crown.Position.X + (column * crown.Size.X / 3);
        var top = crown.Position.Y + (row * crown.Size.Y / 3);
        var right = crown.Position.X + ((column + 1) * crown.Size.X / 3);
        var bottom = crown.Position.Y + ((row + 1) * crown.Size.Y / 3);
        using var image = ExtractScaled(
            atlas,
            new Rect2I(left, top, right - left, bottom - top),
            LowerLevelChunkTextureCache.PixelsPerCell);
        target.BlendRect(
            image,
            new Rect2I(Vector2I.Zero, image.GetSize()),
            origin);
    }

    private static Image ExtractScaled(Image atlas, Rect2I region, int size)
    {
        var image = atlas.GetRegion(region);
        image.Resize(size, size, Image.Interpolation.Bilinear);
        image.Convert(Image.Format.Rgba8);
        return image;
    }

    private static void Modulate(Image image, Color modulate)
    {
        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = 0; x < image.GetWidth(); x++)
            {
                image.SetPixel(x, y, image.GetPixel(x, y) * modulate);
            }
        }
    }
}
