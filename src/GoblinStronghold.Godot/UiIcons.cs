using Godot;

namespace GoblinStronghold.GodotClient;

internal enum UiIcon
{
    Pause,
    Play,
    Faster,
    Fastest,
    Build,
    Work,
    Expedition,
    FoodStorage,
    Walkway,
    FieldCamp,
    GatherFood,
    GatherBrushwood,
    ClearOrders,
    Health,
    Hunger,
    Thirst,
    WoodStorage,
    WoodenWall,
    WoodenDoorFrame,
    WoodenDoor,
    UprootBush,
    FellTree,
}

internal static class UiIcons
{
    private const int Columns = 4;
    private const int SourceCellSize = 256;
    private const string AtlasPath = "res://Assets/UI/action-icons-v2.png";
    private const string Speed8IconPath = "res://Assets/UI/speed-8x-icon-v1.png";

    public static Texture2D LoadAtlas()
        => TextureResources.LoadRequired(AtlasPath, "UI icon atlas");

    public static AtlasTexture CreateTexture(Texture2D atlas, UiIcon icon) => new()
    {
        Atlas = atlas,
        Region = GetRegion(icon),
        FilterClip = true,
    };

    public static AtlasTexture LoadSpeed8Texture()
    {
        return new AtlasTexture
        {
            Atlas = TextureResources.LoadRequired(Speed8IconPath, "8× speed icon"),
            Region = new Rect2(64, 410, 1152, 420),
            FilterClip = true,
        };
    }

    public static Rect2 GetRegion(UiIcon icon)
    {
        var index = (int)icon;
        return new Rect2(
            index % Columns * SourceCellSize,
            index / Columns * SourceCellSize,
            SourceCellSize,
            SourceCellSize);
    }
}
