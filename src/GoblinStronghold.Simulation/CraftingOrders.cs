using System.Collections.ObjectModel;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation;

public readonly record struct CraftingMaterialSnapshot(
    ResourceKind Resource,
    ResourceVariant Variant,
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
    IEnumerable<CraftingDeliveredMaterialState> deliveredMaterials,
    int remainingWorkTicks)
{
    private readonly SortedDictionary<(ResourceKind Resource, ResourceVariant Variant), int>
        _deliveredMaterials = CreateDeliveredMaterials(deliveredMaterials);

    public EntityId Id { get; } = id;

    public CraftingRecipeKind Recipe { get; } = recipe;

    public GridPosition Workshop { get; } = workshop;

    public int RemainingWorkTicks { get; set; } = remainingWorkTicks;

    public int TotalWorkTicks => CraftingRecipeCatalog.GetWorkTicks(Recipe);

    public IReadOnlyList<CraftingDeliveredMaterialState> DeliveredMaterials =>
        _deliveredMaterials.Select(material => new CraftingDeliveredMaterialState(
            material.Key.Resource,
            material.Key.Variant,
            material.Value)).ToArray();

    public int GetDelivered(CraftingMaterialRequirement requirement) =>
        _deliveredMaterials
            .Where(material => requirement.Matches(
                material.Key.Resource,
                material.Key.Variant))
            .Sum(material => material.Value);

    public void Deliver(ResourceKind resource, ResourceVariant variant, int quantity)
    {
        var requirement = CraftingRecipeCatalog.FindMaterial(Recipe, resource, variant)
            ?? throw new InvalidOperationException(
                "The crafting order cannot accept this material.");
        if (quantity <= 0 || quantity > GetMissing(requirement))
        {
            throw new InvalidOperationException(
                "The crafting delivery exceeds the outstanding requirement.");
        }

        var key = (resource, variant);
        _deliveredMaterials[key] = checked(
            _deliveredMaterials.GetValueOrDefault(key) + quantity);
    }

    public int GetMissing(CraftingMaterialRequirement requirement) => Math.Max(
        0,
        requirement.Quantity - GetDelivered(requirement));

    public int GetMissing(ResourceKind resource, ResourceVariant variant) =>
        CraftingRecipeCatalog.FindMaterial(Recipe, resource, variant) is { } requirement
            ? GetMissing(requirement)
            : 0;

    public bool HasAllMaterials => CraftingRecipeCatalog.Get(Recipe).Materials
        .All(material => GetMissing(material) == 0);

    public CraftingOrderSnapshot ToSnapshot() => new(
        Id,
        Recipe,
        Workshop,
        CraftingRecipeCatalog.Get(Recipe).Materials
            .Select(material => new CraftingMaterialSnapshot(
                material.Resource,
                material.Variant,
                material.Quantity,
                GetDelivered(material)))
            .ToArray(),
        RemainingWorkTicks,
        TotalWorkTicks);

    private static SortedDictionary<(ResourceKind, ResourceVariant), int>
        CreateDeliveredMaterials(IEnumerable<CraftingDeliveredMaterialState> materials)
    {
        var result = new SortedDictionary<(ResourceKind, ResourceVariant), int>();
        foreach (var material in materials)
        {
            result.Add((material.Resource, material.Variant), material.Quantity);
        }
        return result;
    }
}

internal readonly record struct CraftingDeliveredMaterialState(
    ResourceKind Resource,
    ResourceVariant Variant,
    int Quantity);
