using Godot;

namespace GoblinStronghold.GodotClient;

internal static class SurfaceGrimeSprites
{
    private const string AtlasPath = "res://Assets/Generated/surface-grime-atlas-v1.png";
    private const int Columns = 4;
    private const int Rows = 4;

    public static Texture2D LoadAtlas() =>
        TextureResources.LoadRequired(AtlasPath, "surface grime atlas");

    public static Rect2 GetRegion(Texture2D atlas, int volume, int variant)
    {
        var column = Math.Abs(variant) % Columns;
        var row = Math.Clamp((volume - 1) / 12, 0, Rows - 1);
        var cellWidth = atlas.GetWidth() / (float)Columns;
        var cellHeight = atlas.GetHeight() / (float)Rows;
        return new Rect2(column * cellWidth, row * cellHeight, cellWidth, cellHeight);
    }
}
