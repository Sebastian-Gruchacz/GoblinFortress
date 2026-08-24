using Godot;

namespace GoblinStronghold.GodotClient;

internal enum TerrainSprite
{
    Meadow,
    DeciduousForestFloor,
    ConiferForestFloor,
    BogGround,
    ClearedField,
    SownField,
    GrowingField,
    RipeField,
    ShallowWaterA,
    ShallowWaterB,
    DeepWaterA,
    DeepWaterB,
    MuddyWaterA,
    MuddyWaterB,
    FishShadowsA,
    FishShadowsB,
}

internal static class TerrainSprites
{
    private const int Columns = 4;
    private const int Rows = 4;
    private const int SourcePadding = 2;
    private const string AtlasPath = "res://Assets/World/terrain-water-atlas-v1.png";

    public static Texture2D LoadAtlas()
    {
        var image = Image.LoadFromFile(ProjectSettings.GlobalizePath(AtlasPath));
        if (image is null || image.IsEmpty())
        {
            throw new InvalidOperationException($"Cannot load terrain atlas: {AtlasPath}");
        }

        return ImageTexture.CreateFromImage(image);
    }

    public static Rect2 GetRegion(Texture2D atlas, TerrainSprite sprite)
    {
        var index = (int)sprite;
        var column = index % Columns;
        var row = index / Columns;
        var left = column * atlas.GetWidth() / Columns;
        var top = row * atlas.GetHeight() / Rows;
        var right = (column + 1) * atlas.GetWidth() / Columns;
        var bottom = (row + 1) * atlas.GetHeight() / Rows;
        return new Rect2(
            left + SourcePadding,
            top + SourcePadding,
            right - left - (SourcePadding * 2),
            bottom - top - (SourcePadding * 2));
    }
}
