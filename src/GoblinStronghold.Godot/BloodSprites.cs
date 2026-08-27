using Godot;

namespace GoblinStronghold.GodotClient;

internal static class BloodSprites
{
    private const string AtlasPath = "res://Assets/World/blood-splatter-atlas-v1.png";
    private const int Columns = 4;
    private const int Rows = 4;

    public static Texture2D LoadAtlas() => GD.Load<Texture2D>(AtlasPath);

    public static Rect2 GetRegion(Texture2D atlas, int volume, int variant)
    {
        var column = Math.Abs(variant) % Columns;
        var row = Math.Clamp((volume - 1) / 16, 0, Rows - 1);
        var cellWidth = atlas.GetWidth() / (float)Columns;
        var cellHeight = atlas.GetHeight() / (float)Rows;
        return new Rect2(column * cellWidth, row * cellHeight, cellWidth, cellHeight);
    }
}
