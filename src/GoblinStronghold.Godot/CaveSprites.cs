using Godot;
using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.GodotClient;

internal static class CaveSprites
{
    private const int Columns = 2;
    private const int Rows = 2;
    private const string AtlasPath = "res://Assets/World/cave-rock-atlas-v1.png";

    public static Texture2D LoadAtlas()
    {
        var image = Image.LoadFromFile(ProjectSettings.GlobalizePath(AtlasPath));
        if (image is null || image.IsEmpty())
        {
            throw new InvalidOperationException($"Cannot load cave rock atlas: {AtlasPath}");
        }

        return ImageTexture.CreateFromImage(image);
    }

    public static Rect2 GetRegion(Texture2D atlas, RockKind rock, bool wall)
    {
        var column = wall ? 1 : 0;
        var row = rock switch
        {
            RockKind.Sandstone => 0,
            RockKind.Granite => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(rock), rock, null),
        };
        var left = column * atlas.GetWidth() / Columns;
        var top = row * atlas.GetHeight() / Rows;
        var right = (column + 1) * atlas.GetWidth() / Columns;
        var bottom = (row + 1) * atlas.GetHeight() / Rows;
        return new Rect2(left, top, right - left, bottom - top);
    }
}
