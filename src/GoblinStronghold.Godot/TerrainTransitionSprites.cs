using Godot;
using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.GodotClient;

internal static class TerrainTransitionSprites
{
    private const int Columns = 16;
    private const int Rows = 4;
    private const int SourcePadding = 1;
    private const string AtlasPath = "res://Assets/Generated/terrain-height-transitions-v1.png";

    public static Texture2D LoadAtlas()
        => TextureResources.LoadRequired(AtlasPath, "terrain transition atlas");

    public static bool Supports(TerrainSprite sprite) => sprite is
        TerrainSprite.Meadow or
        TerrainSprite.DeciduousForestFloor or
        TerrainSprite.ConiferForestFloor or
        TerrainSprite.BogGround;

    public static Rect2 GetRegion(
        Texture2D atlas,
        TerrainSprite sprite,
        TerrainRampDirection rampDirection)
    {
        if (!Supports(sprite))
        {
            throw new ArgumentOutOfRangeException(nameof(sprite), sprite, null);
        }

        var heightMask = rampDirection switch
        {
            TerrainRampDirection.None => 0,
            TerrainRampDirection.North => 1,
            TerrainRampDirection.East => 2,
            TerrainRampDirection.South => 4,
            TerrainRampDirection.West => 8,
            _ => throw new ArgumentOutOfRangeException(nameof(rampDirection), rampDirection, null),
        };
        var row = (int)sprite;
        var left = heightMask * atlas.GetWidth() / Columns;
        var top = row * atlas.GetHeight() / Rows;
        var right = (heightMask + 1) * atlas.GetWidth() / Columns;
        var bottom = (row + 1) * atlas.GetHeight() / Rows;
        return new Rect2(
            left + SourcePadding,
            top + SourcePadding,
            right - left - (SourcePadding * 2),
            bottom - top - (SourcePadding * 2));
    }
}
