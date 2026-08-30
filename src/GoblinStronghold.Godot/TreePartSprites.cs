using Godot;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.GodotClient;

internal enum TreePartSprite
{
    StandingTrunk,
    CutStump,
    UndergroundRoots,
    FelledRemains,
}

internal static class TreePartSprites
{
    private const int Columns = 2;
    private const int Rows = 2;
    private const int SourcePadding = 4;
    private const string AtlasPath = "res://Assets/World/tree-parts-atlas-v1.png";

    public static Texture2D LoadAtlas()
        => TextureResources.LoadRequired(AtlasPath, "tree-parts atlas");

    public static Rect2 GetRegion(Texture2D atlas, TreePartSprite sprite)
    {
        var index = (int)sprite;
        var width = atlas.GetWidth() / Columns;
        var height = atlas.GetHeight() / Rows;
        return new Rect2(
            ((index % Columns) * width) + SourcePadding,
            ((index / Columns) * height) + SourcePadding,
            width - (SourcePadding * 2),
            height - (SourcePadding * 2));
    }

    public static Color GetWoodModulate(ResourceVariant variant) =>
        MaterialPaletteColors.For(variant).Highlight.Lerp(Colors.White, 0.28f);

    public static Color GetWoodColor(ResourceVariant variant) =>
        MaterialPaletteColors.For(variant).Midtone;
}
