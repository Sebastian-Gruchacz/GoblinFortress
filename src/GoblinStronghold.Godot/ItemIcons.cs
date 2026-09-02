using Godot;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.GodotClient;

internal enum ItemIcon
{
    Food,
    Wood,
    Reeds,
    Stone,
    Bone,
    RagClothes,
    PrimitiveWaterskin,
    BoneKnife,
    WoodenHoe,
    WoodenAxe,
    WoodenBucket,
    WoodenSpear,
    GrainAndFlatbread,
    VillageGoods,
    Cargo,
    Unknown,
}

internal static class ItemIcons
{
    private const int Columns = 4;
    private const int Rows = 4;
    private const string AtlasPath = "res://Assets/UI/item-icons-v1.png";

    public static Texture2D LoadAtlas()
        => TextureResources.LoadRequired(AtlasPath, "item icon atlas");

    public static AtlasTexture CreateTexture(Texture2D atlas, ItemIcon icon) => new()
    {
        Atlas = atlas,
        Region = GetRegion(atlas, icon),
        FilterClip = true,
    };

    public static Rect2 GetRegion(Texture2D atlas, ItemIcon icon)
    {
        var cellWidth = atlas.GetWidth() / (float)Columns;
        var cellHeight = atlas.GetHeight() / (float)Rows;
        var index = (int)icon;
        return new Rect2(
            index % Columns * cellWidth,
            index / Columns * cellHeight,
            cellWidth,
            cellHeight);
    }

    public static Rect2I GetRegionFromImage(Image atlas, ItemIcon icon)
    {
        var cellWidth = atlas.GetWidth() / Columns;
        var cellHeight = atlas.GetHeight() / Rows;
        var index = (int)icon;
        return new Rect2I(
            index % Columns * cellWidth,
            index / Columns * cellHeight,
            cellWidth,
            cellHeight);
    }

    public static ItemIcon ForResource(ResourceKind resource) => resource switch
    {
        ResourceKind.Food => ItemIcon.Food,
        ResourceKind.Wood => ItemIcon.Wood,
        ResourceKind.Reeds => ItemIcon.Reeds,
        ResourceKind.Stone => ItemIcon.Stone,
        ResourceKind.Coal => ItemIcon.Stone,
        ResourceKind.Ore => ItemIcon.Stone,
        ResourceKind.Bone => ItemIcon.Bone,
        ResourceKind.Hide => ItemIcon.RagClothes,
        ResourceKind.Equipment => ItemIcon.Cargo,
        ResourceKind.Water => ItemIcon.WoodenBucket,
        _ => ItemIcon.Unknown,
    };

    public static Color TintForResource(ResourceKind resource) => resource switch
    {
        ResourceKind.Coal => new Color("5f6468"),
        ResourceKind.Ore => new Color("d9783d"),
        ResourceKind.Hide => new Color("b88759"),
        ResourceKind.Water => new Color("68b7d4"),
        _ => Colors.White,
    };
}
