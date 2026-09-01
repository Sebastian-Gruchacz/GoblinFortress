using Godot;
using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal static class FloorPatternSprites
{
    private const int Columns = 2;
    private const string AtlasPath =
        "res://Assets/Generated/constructed-floor-patterns-v1.png";

    public static Texture2D LoadAtlas() =>
        TextureResources.LoadRequired(AtlasPath, "constructed floor pattern atlas");

    public static Rect2 GetRegion(Texture2D atlas, WorldObjectKind kind)
    {
        var column = kind switch
        {
            WorldObjectKind.WoodenFloor => 0,
            WorldObjectKind.StoneFloor => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var width = atlas.GetWidth() / Columns;
        return new Rect2(column * width, 0, width, atlas.GetHeight());
    }
}
