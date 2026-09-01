using Godot;
using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.GodotClient;

internal static class UndergroundSprites
{
    private const int MineralColumns = 2;
    internal const string FaunaAtlasPath =
        "res://Assets/World/underground-fauna-atlas-v1.png";
    private const string MineralAtlasPath =
        "res://Assets/World/mineral-deposits-atlas-v1.png";

    public static Texture2D LoadFaunaAtlas() => LoadAtlas(FaunaAtlasPath, "underground fauna");

    public static Texture2D LoadMineralAtlas() => LoadAtlas(MineralAtlasPath, "mineral deposits");

    public static Rect2 GetCaveSpiderRegion(Texture2D atlas) =>
        new(0, 0, atlas.GetWidth(), atlas.GetHeight());

    public static Rect2 GetMineralRegion(Texture2D atlas, MineralDepositKind deposit)
    {
        var column = deposit switch
        {
            MineralDepositKind.Coal => 0,
            MineralDepositKind.IronOre => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(deposit), deposit, null),
        };
        var width = atlas.GetWidth() / MineralColumns;
        return new Rect2(column * width, 0, width, atlas.GetHeight());
    }

    private static Texture2D LoadAtlas(string path, string description)
        => TextureResources.LoadRequired(path, $"{description} atlas");
}
