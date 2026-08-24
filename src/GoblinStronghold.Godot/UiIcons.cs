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
}

internal static class UiIcons
{
    private const int Columns = 4;
    private const int SourceCellSize = 256;
    private const string AtlasPath = "res://Assets/UI/action-icons-v1.png";
    private const string Speed8IconPath = "res://Assets/UI/speed-8x-icon-v1.png";

    public static Texture2D LoadAtlas()
    {
        var image = Image.LoadFromFile(ProjectSettings.GlobalizePath(AtlasPath));
        if (image is null || image.IsEmpty())
        {
            throw new InvalidOperationException($"Cannot load UI icon atlas: {AtlasPath}");
        }

        return ImageTexture.CreateFromImage(image);
    }

    public static AtlasTexture CreateTexture(Texture2D atlas, UiIcon icon) => new()
    {
        Atlas = atlas,
        Region = GetRegion(icon),
        FilterClip = true,
    };

    public static AtlasTexture LoadSpeed8Texture()
    {
        var image = Image.LoadFromFile(ProjectSettings.GlobalizePath(Speed8IconPath));
        if (image is null || image.IsEmpty())
        {
            throw new InvalidOperationException($"Cannot load 8× speed icon: {Speed8IconPath}");
        }

        return new AtlasTexture
        {
            Atlas = ImageTexture.CreateFromImage(image),
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
