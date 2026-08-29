using Godot;
using GoblinStronghold.Simulation;

namespace GoblinStronghold.GodotClient;

internal static class SeasonRibbonSprites
{
    private const int Columns = 4;
    private const string RibbonPath = "res://Assets/UI/season-ribbon-v1.png";

    public static Texture2D LoadTexture()
        => TextureResources.LoadRequired(RibbonPath, "season ribbon");

    public static Rect2 GetRegion(Texture2D texture, SeasonKind season)
    {
        var column = season switch
        {
            SeasonKind.Spring => 0,
            SeasonKind.Summer => 1,
            SeasonKind.Autumn => 2,
            SeasonKind.Winter => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(season), season, null),
        };
        var left = column * texture.GetWidth() / Columns;
        var right = (column + 1) * texture.GetWidth() / Columns;
        return new Rect2(left, 0f, right - left, texture.GetHeight());
    }
}
