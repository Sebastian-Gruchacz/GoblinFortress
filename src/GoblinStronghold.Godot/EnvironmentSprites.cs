using Godot;

namespace GoblinStronghold.GodotClient;

internal enum EnvironmentSprite
{
    TreeTrunk,
    TreeCrown,
    FruitingBerryBush,
    BareBerryBush,
    MushroomCluster,
    EdibleRoots,
    Reeds,
    FishShoal,
    ClearedField,
    SownField,
    GrowingField,
    RipeField,
    GoblinHutGround,
    GoblinHutRoof,
    FieldCampGround,
    FieldCampRoof,
}

internal static class EnvironmentSprites
{
    private const int Columns = 4;
    private const int Rows = 4;
    private const int SmallSpriteSourcePadding = 4;
    private const string AtlasPath = "res://Assets/World/environment-atlas-v1.png";

    public static Texture2D LoadAtlas()
        => TextureResources.LoadRequired(AtlasPath, "environment atlas");

    public static Rect2 GetRegion(Texture2D atlas, EnvironmentSprite sprite)
        => GetRegion(atlas.GetWidth(), atlas.GetHeight(), sprite);

    public static Rect2I GetRegionFromImage(Image atlas, EnvironmentSprite sprite)
    {
        var region = GetRegion(atlas.GetWidth(), atlas.GetHeight(), sprite);
        return new Rect2I(
            Mathf.RoundToInt(region.Position.X),
            Mathf.RoundToInt(region.Position.Y),
            Mathf.RoundToInt(region.Size.X),
            Mathf.RoundToInt(region.Size.Y));
    }

    private static Rect2 GetRegion(
        int atlasWidth,
        int atlasHeight,
        EnvironmentSprite sprite)
    {
        var index = (int)sprite;
        var column = index % Columns;
        var row = index / Columns;
        var left = column * atlasWidth / Columns;
        var top = row * atlasHeight / Rows;
        var right = (column + 1) * atlasWidth / Columns;
        var bottom = (row + 1) * atlasHeight / Rows;
        var sourcePadding = sprite is
            EnvironmentSprite.GoblinHutGround or
            EnvironmentSprite.GoblinHutRoof or
            EnvironmentSprite.FieldCampGround or
            EnvironmentSprite.FieldCampRoof
                ? 0
                : SmallSpriteSourcePadding;
        return new Rect2(
            left + sourcePadding,
            top + sourcePadding,
            right - left - (sourcePadding * 2),
            bottom - top - (sourcePadding * 2));
    }
}
