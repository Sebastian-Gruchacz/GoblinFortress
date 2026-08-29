using System.Collections.ObjectModel;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation;

public readonly record struct CraftingMaterialSnapshot(
    ResourceKind Resource,
    int RequiredQuantity,
    int DeliveredQuantity)
{
    public int MissingQuantity => Math.Max(0, RequiredQuantity - DeliveredQuantity);
}

public sealed class CraftingOrderSnapshot
{
    internal CraftingOrderSnapshot(
        EntityId id,
        CraftingRecipeKind recipe,
        GridPosition workshop,
        IReadOnlyList<CraftingMaterialSnapshot> materials,
        int remainingWorkTicks,
        int totalWorkTicks)
    {
        Id = id;
        Recipe = recipe;
        Workshop = workshop;
        Materials = new ReadOnlyCollection<CraftingMaterialSnapshot>(materials.ToArray());
        RemainingWorkTicks = remainingWorkTicks;
        TotalWorkTicks = totalWorkTicks;
    }

    public EntityId Id { get; }

    public CraftingRecipeKind Recipe { get; }

    public GridPosition Workshop { get; }

    public IReadOnlyList<CraftingMaterialSnapshot> Materials { get; }

    public int RemainingWorkTicks { get; }

    public int TotalWorkTicks { get; }

    public bool HasAllMaterials => Materials.All(material => material.MissingQuantity == 0);
}

internal sealed class CraftingOrderState(
    EntityId id,
    CraftingRecipeKind recipe,
    GridPosition workshop,
    int deliveredHide,
    int deliveredBone,
    int deliveredWood,
    int deliveredStone,
    int deliveredReeds,
    int remainingWorkTicks)
{
    public EntityId Id { get; } = id;

    public CraftingRecipeKind Recipe { get; } = recipe;

    public GridPosition Workshop { get; } = workshop;

    public int DeliveredHide { get; set; } = deliveredHide;

    public int DeliveredBone { get; set; } = deliveredBone;

    public int DeliveredWood { get; set; } = deliveredWood;

    public int DeliveredStone { get; set; } = deliveredStone;

    public int DeliveredReeds { get; set; } = deliveredReeds;

    public int RemainingWorkTicks { get; set; } = remainingWorkTicks;

    public int TotalWorkTicks => CraftingRecipeCatalog.GetWorkTicks(Recipe);

    public int GetDelivered(ResourceKind resource) => resource switch
    {
        ResourceKind.Hide => DeliveredHide,
        ResourceKind.Bone => DeliveredBone,
        ResourceKind.Wood => DeliveredWood,
        ResourceKind.Stone => DeliveredStone,
        ResourceKind.Reeds => DeliveredReeds,
        _ => 0,
    };

    public void Deliver(ResourceKind resource, int quantity)
    {
        switch (resource)
        {
            case ResourceKind.Hide:
                DeliveredHide = checked(DeliveredHide + quantity);
                break;
            case ResourceKind.Bone:
                DeliveredBone = checked(DeliveredBone + quantity);
                break;
            case ResourceKind.Wood:
                DeliveredWood = checked(DeliveredWood + quantity);
                break;
            case ResourceKind.Stone:
                DeliveredStone = checked(DeliveredStone + quantity);
                break;
            case ResourceKind.Reeds:
                DeliveredReeds = checked(DeliveredReeds + quantity);
                break;
            default:
                throw new InvalidOperationException("The crafting order cannot accept this material.");
        }
    }

    public int GetMissing(ResourceKind resource) => Math.Max(
        0,
        CraftingRecipeCatalog.GetRequiredQuantity(Recipe, resource) - GetDelivered(resource));

    public bool HasAllMaterials => CraftingRecipeCatalog.GetMaterials(Recipe)
        .All(material => GetMissing(material.Resource) == 0);

    public CraftingOrderSnapshot ToSnapshot() => new(
        Id,
        Recipe,
        Workshop,
        CraftingRecipeCatalog.GetMaterials(Recipe)
            .Select(material => new CraftingMaterialSnapshot(
                material.Resource,
                material.Quantity,
                GetDelivered(material.Resource)))
            .ToArray(),
        RemainingWorkTicks,
        TotalWorkTicks);
}

internal static class CraftingRecipeCatalog
{
    private static readonly (ResourceKind Resource, int Quantity)[] PrimitiveSlingMaterials =
    [
        (ResourceKind.Hide, 1),
        (ResourceKind.Bone, 1),
    ];

    private static readonly (ResourceKind Resource, int Quantity)[] BoneKnifeMaterials =
    [
        (ResourceKind.Bone, 1),
    ];

    private static readonly (ResourceKind Resource, int Quantity)[] FightingStickMaterials =
    [
        (ResourceKind.Wood, 3),
    ];

    private static readonly (ResourceKind Resource, int Quantity)[] StoneClubMaterials =
    [
        (ResourceKind.Wood, 1),
        (ResourceKind.Stone, 1),
    ];

    private static readonly (ResourceKind Resource, int Quantity)[] HideClothesMaterials =
    [
        (ResourceKind.Hide, 2),
    ];

    private static readonly (ResourceKind Resource, int Quantity)[] ReedClothesMaterials =
    [
        (ResourceKind.Reeds, 3),
    ];

    private static readonly (ResourceKind Resource, int Quantity)[] PrimitiveWaterskinMaterials =
    [
        (ResourceKind.Hide, 1),
    ];

    public static IReadOnlyList<(ResourceKind Resource, int Quantity)> GetMaterials(
        CraftingRecipeKind recipe) => recipe switch
    {
        CraftingRecipeKind.PrimitiveSling => PrimitiveSlingMaterials,
        CraftingRecipeKind.BoneKnife => BoneKnifeMaterials,
        CraftingRecipeKind.FightingStick => FightingStickMaterials,
        CraftingRecipeKind.StoneClub => StoneClubMaterials,
        CraftingRecipeKind.HideClothes => HideClothesMaterials,
        CraftingRecipeKind.ReedClothes => ReedClothesMaterials,
        CraftingRecipeKind.PrimitiveWaterskin => PrimitiveWaterskinMaterials,
        _ => throw new ArgumentOutOfRangeException(nameof(recipe), recipe, null),
    };

    public static int GetRequiredQuantity(CraftingRecipeKind recipe, ResourceKind resource) =>
        GetMaterials(recipe)
            .Where(material => material.Resource == resource)
            .Select(material => material.Quantity)
            .SingleOrDefault();

    public static int GetWorkTicks(CraftingRecipeKind recipe) => recipe switch
    {
        CraftingRecipeKind.PrimitiveSling => 80,
        CraftingRecipeKind.BoneKnife => 55,
        CraftingRecipeKind.FightingStick => 45,
        CraftingRecipeKind.StoneClub => 70,
        CraftingRecipeKind.HideClothes => 100,
        CraftingRecipeKind.ReedClothes => 80,
        CraftingRecipeKind.PrimitiveWaterskin => 65,
        _ => throw new ArgumentOutOfRangeException(nameof(recipe), recipe, null),
    };
}
