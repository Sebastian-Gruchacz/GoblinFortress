using Godot;
using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.GodotClient;

internal static class CaveSprites
{
    private const int FloorColumns = 2;
    private const int FloorRows = 2;
    private const int WallMaskCount = 16;
    private const int WallColumns = 20;
    private const int WallRows = 2;
    private const string FloorAtlasPath = "res://Assets/World/cave-rock-atlas-v1.png";
    private const string WallAtlasPath = "res://Assets/Generated/cave-walls-v1.png";

    public static Texture2D LoadAtlas()
        => TextureResources.LoadRequired(FloorAtlasPath, "cave rock atlas");

    public static Texture2D LoadWallAtlas()
        => TextureResources.LoadRequired(WallAtlasPath, "cave wall atlas");

    public static Rect2 GetFloorRegion(Texture2D atlas, RockKind rock)
    {
        var row = rock switch
        {
            RockKind.Sandstone => 0,
            RockKind.Granite or RockKind.Basalt or RockKind.Obsidian => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(rock), rock, null),
        };
        var left = 0;
        var top = row * atlas.GetHeight() / FloorRows;
        var right = atlas.GetWidth() / FloorColumns;
        var bottom = (row + 1) * atlas.GetHeight() / FloorRows;
        return new Rect2(left, top, right - left, bottom - top);
    }

    public static Color GetFloorShade(RockKind rock) => rock switch
    {
        RockKind.Sandstone => new Color(0.055f, 0.041f, 0.03f, 0.64f),
        RockKind.Granite => new Color(0.025f, 0.03f, 0.037f, 0.52f),
        RockKind.Basalt => new Color(0.018f, 0.015f, 0.017f, 0.72f),
        RockKind.Obsidian => new Color(0.035f, 0.012f, 0.045f, 0.68f),
        _ => throw new ArgumentOutOfRangeException(nameof(rock), rock, null),
    };

    public static Rect2 GetWallRegion(Texture2D atlas, RockKind rock, int openNeighborMask)
    {
        if (openNeighborMask is < 0 or >= WallMaskCount)
        {
            throw new ArgumentOutOfRangeException(nameof(openNeighborMask));
        }

        var row = rock switch
        {
            RockKind.Sandstone => 0,
            RockKind.Granite or RockKind.Basalt or RockKind.Obsidian => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(rock), rock, null),
        };
        var left = openNeighborMask * atlas.GetWidth() / WallColumns;
        var top = row * atlas.GetHeight() / WallRows;
        var right = (openNeighborMask + 1) * atlas.GetWidth() / WallColumns;
        var bottom = (row + 1) * atlas.GetHeight() / WallRows;
        return new Rect2(left, top, right - left, bottom - top);
    }

    public static Rect2 GetInnerCornerRegion(
        Texture2D atlas,
        RockKind rock,
        CaveInnerCorner corner)
    {
        var cornerIndex = corner switch
        {
            CaveInnerCorner.NorthWest => 0,
            CaveInnerCorner.NorthEast => 1,
            CaveInnerCorner.SouthEast => 2,
            CaveInnerCorner.SouthWest => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(corner), corner, null),
        };
        var row = rock switch
        {
            RockKind.Sandstone => 0,
            RockKind.Granite or RockKind.Basalt or RockKind.Obsidian => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(rock), rock, null),
        };
        var column = WallMaskCount + cornerIndex;
        var left = column * atlas.GetWidth() / WallColumns;
        var top = row * atlas.GetHeight() / WallRows;
        var right = (column + 1) * atlas.GetWidth() / WallColumns;
        var bottom = (row + 1) * atlas.GetHeight() / WallRows;
        return new Rect2(left, top, right - left, bottom - top);
    }
}
