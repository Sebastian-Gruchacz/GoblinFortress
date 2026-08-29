using Godot;
using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.GodotClient;

internal static class TerrainSlopeSprites
{
    private const int Columns = 4;
    private const int SourcePadding = 8;
    private const string AtlasPath = "res://Assets/World/terrain-slope-overlays-v1.png";

    public static Texture2D LoadAtlas()
        => TextureResources.LoadRequired(AtlasPath, "terrain slope atlas");

    public static Rect2 GetRegion(Texture2D atlas, TerrainRampDirection direction)
    {
        var column = direction switch
        {
            TerrainRampDirection.North => 0,
            TerrainRampDirection.East => 1,
            TerrainRampDirection.South => 2,
            TerrainRampDirection.West => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
        };
        var left = column * atlas.GetWidth() / Columns;
        var right = (column + 1) * atlas.GetWidth() / Columns;
        return new Rect2(
            left + SourcePadding,
            SourcePadding,
            right - left - (SourcePadding * 2),
            atlas.GetHeight() - (SourcePadding * 2));
    }
}
