using Godot;

namespace GoblinStronghold.GodotClient;

internal enum HumanStructureSprite
{
    CottageGround,
    CottageRoof,
    BarnGround,
    BarnRoof,
    StorehouseGround,
    StorehouseRoof,
    WellSurface,
    WellShaft,
}

internal static class HumanStructureSprites
{
    private const int ColumnWidth = 384;
    private const string AtlasPath = "res://Assets/World/human-structures-atlas-v1.png";

    public static Texture2D LoadAtlas()
    {
        var image = Image.LoadFromFile(ProjectSettings.GlobalizePath(AtlasPath));
        if (image is null || image.IsEmpty())
        {
            throw new InvalidOperationException($"Cannot load human structure atlas: {AtlasPath}");
        }

        return ImageTexture.CreateFromImage(image);
    }

    public static Rect2 GetRegion(HumanStructureSprite sprite)
    {
        var index = (int)sprite;
        var column = index % 4;
        var secondRow = index >= 4;
        return new Rect2(
            column * ColumnWidth,
            secondRow ? 415 : 0,
            ColumnWidth,
            secondRow ? 335 : 410);
    }
}
