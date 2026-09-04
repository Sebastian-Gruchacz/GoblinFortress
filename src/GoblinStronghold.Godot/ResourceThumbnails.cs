using Godot;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.GodotClient;

internal static class ResourceThumbnails
{
    private const int FoodColumns = 3;
    private const int FoodRows = 2;
    private const string FoodAtlasPath = "res://Assets/UI/food-icons-v1.png";
    private const string WoodenHammerIconPath = "res://Assets/UI/wooden-hammer-v1.svg";
    private const string WoodenShovelIconPath = "res://Assets/UI/wooden-shovel-v1.svg";

    public static Texture2D LoadWoodenHammerIcon() =>
        TextureResources.LoadRequired(WoodenHammerIconPath, "wooden hammer icon");

    public static Texture2D LoadWoodenShovelIcon() =>
        TextureResources.LoadRequired(WoodenShovelIconPath, "wooden shovel icon");

    public static Texture2D? TryLoadFoodAtlas()
    {
        if (!ResourceLoader.Exists(FoodAtlasPath, "Texture2D"))
        {
            GD.PushWarning(
                $"Food icon atlas is unavailable; using generic food icons: {FoodAtlasPath}");
            return null;
        }

        try
        {
            var atlas = GD.Load<Texture2D>(FoodAtlasPath);
            if (atlas is null)
            {
                GD.PushWarning(
                    $"Food icon atlas is unavailable; using generic food icons: {FoodAtlasPath}");
            }

            return atlas;
        }
        catch (Exception exception)
        {
            GD.PushWarning(
                $"Food icon atlas could not be loaded; using generic food icons: {exception.Message}");
            return null;
        }
    }

    public static Texture2D Create(
        Texture2D itemAtlas,
        Texture2D treePartAtlas,
        Texture2D? foodAtlas,
        MaterialPaletteTextureCache paletteTextures,
        ResourceKind resource,
        FoodKind foodKind,
        ResourceVariant variant)
    {
        if (resource == ResourceKind.Food && foodKind != FoodKind.None && foodAtlas is not null)
        {
            return CreateFoodTexture(foodAtlas, foodKind);
        }

        if (resource == ResourceKind.Wood)
        {
            var woodRegion = GetWoodRegion(treePartAtlas);
            return variant != ResourceVariant.None && MaterialCatalog.TryGet(variant, out _)
                ? paletteTextures.Get(
                    treePartAtlas,
                    woodRegion,
                    variant,
                    MaterialPaletteTextureProfile.IllustratedTimber)
                : new AtlasTexture
                {
                    Atlas = treePartAtlas,
                    Region = woodRegion,
                    FilterClip = true,
                };
        }

        if (resource == ResourceKind.Coal)
        {
            return paletteTextures.GetCoalIcon();
        }

        if (resource == ResourceKind.Ore && variant != ResourceVariant.None &&
            MaterialCatalog.TryGet(variant, out _))
        {
            return paletteTextures.GetResourceIcon(
                variant,
                MaterialResourceIconShape.Ore);
        }

        if (resource == ResourceKind.Materials && IsMetalBar(variant))
        {
            return paletteTextures.GetResourceIcon(
                variant,
                MaterialResourceIconShape.Ingot);
        }

        if (variant == ResourceVariant.EquipmentWoodenHammer)
        {
            return LoadWoodenHammerIcon();
        }

        if (variant == ResourceVariant.EquipmentWoodenShovel)
        {
            return LoadWoodenShovelIcon();
        }

        var spiderMaterialIcon = variant switch
        {
            ResourceVariant.SpiderVenom => ItemIcon.PrimitiveWaterskin,
            ResourceVariant.SpiderSilk => ItemIcon.Reeds,
            ResourceVariant.SpiderChitin => ItemIcon.Bone,
            ResourceVariant.Lichen => ItemIcon.Reeds,
            ResourceVariant.Mana => ItemIcon.PrimitiveWaterskin,
            _ => (ItemIcon?)null,
        };
        if (spiderMaterialIcon is { } icon)
        {
            return ItemIcons.CreateTexture(itemAtlas, icon);
        }

        if (variant != ResourceVariant.None &&
            resource is (ResourceKind.Stone or ResourceKind.Ore or ResourceKind.Materials or
                ResourceKind.Earth or ResourceKind.Sand) &&
            MaterialCatalog.TryGet(variant, out _))
        {
            return paletteTextures.Get(
                itemAtlas,
                ItemIcons.GetRegion(itemAtlas, ItemIcon.Stone),
                variant,
                MaterialPaletteTextureProfile.CompleteSurface);
        }

        var equipmentIcon = variant switch
        {
            ResourceVariant.EquipmentBoneKnife => ItemIcon.BoneKnife,
            ResourceVariant.EquipmentWoodenAxe or ResourceVariant.EquipmentHumanWoodenAxe =>
                ItemIcon.WoodenAxe,
            ResourceVariant.EquipmentWoodenHoe => ItemIcon.WoodenHoe,
            ResourceVariant.EquipmentWoodenBucket => ItemIcon.WoodenBucket,
            ResourceVariant.EquipmentWoodenSpear => ItemIcon.WoodenSpear,
            ResourceVariant.EquipmentPrimitiveWaterskin => ItemIcon.PrimitiveWaterskin,
            ResourceVariant.EquipmentRagClothes or ResourceVariant.EquipmentHideClothes or
                ResourceVariant.EquipmentReedClothes => ItemIcon.RagClothes,
            _ => ItemIcons.ForResource(resource),
        };
        return ItemIcons.CreateTexture(itemAtlas, equipmentIcon);
    }

    public static bool UsesSpiderMaterialIcon(ResourceVariant variant) => variant is
        ResourceVariant.SpiderVenom or ResourceVariant.SpiderSilk or
        ResourceVariant.SpiderChitin;

    public static bool UsesDedicatedMaterialIcon(
        ResourceKind resource,
        ResourceVariant variant) =>
        resource is ResourceKind.Coal or ResourceKind.Ore ||
        IsMetalBar(variant) ||
        UsesSpiderMaterialIcon(variant);

    private static bool IsMetalBar(ResourceVariant variant) => variant is
        ResourceVariant.IronBar or ResourceVariant.CopperBar or
        ResourceVariant.SilverBar or ResourceVariant.GoldBar;

    private static Rect2 GetWoodRegion(Texture2D treePartAtlas)
    {
        var source = TreePartSprites.GetRegion(treePartAtlas, TreePartSprite.FelledRemains);
        return new Rect2(
            source.Position + source.Size * new Vector2(0.16f, 0.02f),
            source.Size * new Vector2(0.68f, 0.64f));
    }

    private static AtlasTexture CreateFoodTexture(Texture2D atlas, FoodKind foodKind)
    {
        var index = foodKind switch
        {
            FoodKind.CookedMeat => (int)FoodKind.RawMeat - 1,
            FoodKind.CampSoup => (int)FoodKind.Mushrooms - 1,
            FoodKind.Medicine => (int)FoodKind.EdibleRoots - 1,
            _ => (int)foodKind - 1,
        };
        if (index is < 0 or >= FoodColumns * FoodRows)
        {
            throw new ArgumentOutOfRangeException(nameof(foodKind));
        }

        var cellWidth = atlas.GetWidth() / (float)FoodColumns;
        var cellHeight = atlas.GetHeight() / (float)FoodRows;
        return new AtlasTexture
        {
            Atlas = atlas,
            Region = new Rect2(
                index % FoodColumns * cellWidth,
                index / FoodColumns * cellHeight,
                cellWidth,
                cellHeight),
            FilterClip = true,
        };
    }
}
