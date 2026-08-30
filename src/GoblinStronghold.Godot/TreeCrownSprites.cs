using Godot;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.GodotClient;

internal static class TreeCrownSprites
{
    private const int Columns = 3;
    private const int Rows = 2;
    private const int SourcePadding = 8;
    private const string AtlasPath = "res://Assets/World/tree-crowns-atlas-v1.png";

    public static Texture2D LoadAtlas()
        => TextureResources.LoadRequired(AtlasPath, "tree-crowns atlas");

    public static Rect2 GetRegion(Texture2D atlas, ResourceVariant variant)
    {
        var index = variant is >= ResourceVariant.OakWood and <= ResourceVariant.PineWood
            ? (int)variant - (int)ResourceVariant.OakWood
            : 0;
        var width = atlas.GetWidth() / Columns;
        var height = atlas.GetHeight() / Rows;
        return new Rect2(
            ((index % Columns) * width) + SourcePadding,
            ((index / Columns) * height) + SourcePadding,
            width - (SourcePadding * 2),
            height - (SourcePadding * 2));
    }

    public static Color GetCrownColor(ResourceVariant variant) => variant switch
    {
        ResourceVariant.OakWood => new Color("39753d"),
        ResourceVariant.ChestnutWood => new Color("50783a"),
        ResourceVariant.BirchWood => new Color("70a64b"),
        ResourceVariant.WalnutWood => new Color("365f32"),
        ResourceVariant.AppleWood => new Color("5f8b3b"),
        ResourceVariant.PineWood => new Color("244f35"),
        _ => new Color("39753d"),
    };
}
