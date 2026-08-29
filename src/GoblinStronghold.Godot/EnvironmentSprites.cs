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
    private const int SourcePadding = 4;
    private const string AtlasPath = "res://Assets/World/environment-atlas-v1.png";

    public static Texture2D LoadAtlas()
        => TextureResources.LoadRequired(AtlasPath, "environment atlas");

    public static Rect2 GetRegion(Texture2D atlas, EnvironmentSprite sprite)
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
