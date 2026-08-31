using Godot;

namespace GoblinStronghold.GodotClient;

internal enum ConstructionIcon
{
    WoodenBridge,
    BasaltBridge,
    WoodenFloor,
    StoneFloor,
    WoodenRamp,
    StoneRamp,
    WoodenWall,
    StoneWall,
    WoodenDoorFrame,
    StoneDoorFrame,
    WoodenDoor,
    WallTorch,
}

internal static class ConstructionIcons
{
    private const int Columns = 4;
    private const int Rows = 3;
    private const string AtlasPath = "res://Assets/UI/construction-icons-v1.png";

    public static Texture2D LoadAtlas()
        => TextureResources.LoadRequired(AtlasPath, "construction icon atlas");

    public static AtlasTexture CreateTexture(Texture2D atlas, ConstructionIcon icon) => new()
    {
        Atlas = atlas,
        Region = GetRegion(atlas, icon),
        FilterClip = true,
    };

    private static Rect2 GetRegion(Texture2D atlas, ConstructionIcon icon)
    {
        var cellWidth = atlas.GetWidth() / (float)Columns;
        var cellHeight = atlas.GetHeight() / (float)Rows;
        var index = (int)icon;
        return new Rect2(
            index % Columns * cellWidth,
            index / Columns * cellHeight,
            cellWidth,
            cellHeight);
    }
}
