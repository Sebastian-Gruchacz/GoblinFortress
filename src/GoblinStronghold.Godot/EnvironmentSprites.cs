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
    private const string AtlasPath = "res://Assets/World/environment-atlas-v1.png";

    public static Texture2D LoadAtlas()
    {
        var image = Image.LoadFromFile(ProjectSettings.GlobalizePath(AtlasPath));
        if (image is null || image.IsEmpty())
        {
            throw new InvalidOperationException($"Cannot load environment atlas: {AtlasPath}");
        }

        return ImageTexture.CreateFromImage(image);
    }

    public static Rect2 GetRegion(Texture2D atlas, EnvironmentSprite sprite)
    {
        var cellWidth = atlas.GetWidth() / (float)Columns;
        var cellHeight = atlas.GetHeight() / (float)Rows;
        var index = (int)sprite;
        return new Rect2(
            index % Columns * cellWidth,
            index / Columns * cellHeight,
            cellWidth,
            cellHeight);
    }
}
