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
        => TextureResources.LoadRequired(AtlasPath, "terrain atlas");

    public static Rect2 GetRegion(Texture2D atlas, TerrainSprite sprite)
        => GetRegion(atlas.GetWidth(), atlas.GetHeight(), sprite);

    public static Rect2I GetRegionFromImage(Image atlas, TerrainSprite sprite)
    {
        var region = GetRegion(atlas.GetWidth(), atlas.GetHeight(), sprite);
        return new Rect2I(
            Mathf.RoundToInt(region.Position.X),
            Mathf.RoundToInt(region.Position.Y),
            Mathf.RoundToInt(region.Size.X),
            Mathf.RoundToInt(region.Size.Y));
    }

    private static Rect2 GetRegion(int atlasWidth, int atlasHeight, TerrainSprite sprite)
    {
        var index = (int)sprite;
        var column = index % Columns;
        var row = index / Columns;
        var left = column * atlasWidth / Columns;
        var top = row * atlasHeight / Rows;
        var right = (column + 1) * atlasWidth / Columns;
        var bottom = (row + 1) * atlasHeight / Rows;
        return new Rect2(
            left + SourcePadding,
            top + SourcePadding,
            right - left - (SourcePadding * 2),
            bottom - top - (SourcePadding * 2));
    }
}
