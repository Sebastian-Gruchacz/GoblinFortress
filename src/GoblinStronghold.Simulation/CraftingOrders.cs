using System.Collections.ObjectModel;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation;

public readonly record struct CraftingMaterialSnapshot(
    ResourceKind Resource,
    ResourceVariant Variant,
    int RequiredQuantity,
    int DeliveredQuantity,
    FoodKind FoodKind = FoodKind.None)
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
        int totalWorkTicks,
        bool isRepeating,
        bool isAutomatic)
    {
        Id = id;
        Recipe = recipe;
        Workshop = workshop;
        Materials = new ReadOnlyCollection<CraftingMaterialSnapshot>(materials.ToArray());
        RemainingWorkTicks = remainingWorkTicks;
        TotalWorkTicks = totalWorkTicks;
        IsRepeating = isRepeating;
        IsAutomatic = isAutomatic;
    }

    public EntityId Id { get; }

    public CraftingRecipeKind Recipe { get; }

    public GridPosition Workshop { get; }

    public IReadOnlyList<CraftingMaterialSnapshot> Materials { get; }

    public int RemainingWorkTicks { get; }

    public int TotalWorkTicks { get; }

    public bool IsRepeating { get; }

    public bool IsAutomatic { get; }

    public bool HasAllMaterials => Materials.All(material => material.MissingQuantity == 0);
}

internal sealed class CraftingOrderState(
    EntityId id,
    CraftingRecipeKind recipe,
    GridPosition workshop,
    IEnumerable<CraftingDeliveredMaterialState> deliveredMaterials,
    int remainingWorkTicks,
    bool isRepeating = false,
    bool isAutomatic = false)
{
    private readonly SortedDictionary<(
        ResourceKind Resource,
        FoodKind FoodKind,
        ResourceVariant Variant), DeliveredMaterial>
        _deliveredMaterials = CreateDeliveredMaterials(deliveredMaterials);

    public EntityId Id { get; } = id;

    public CraftingRecipeKind Recipe { get; } = recipe;

    public GridPosition Workshop { get; } = workshop;

    public int RemainingWorkTicks { get; set; } = remainingWorkTicks;

    public bool IsRepeating { get; } = isRepeating;

    public bool IsAutomatic { get; } = isAutomatic;

    public int TotalWorkTicks => CraftingRecipeCatalog.GetWorkTicks(Recipe);

    public IReadOnlyList<CraftingDeliveredMaterialState> DeliveredMaterials =>
        _deliveredMaterials.Select(material => new CraftingDeliveredMaterialState(
            material.Key.Resource,
            material.Key.FoodKind,
            material.Key.Variant,
            material.Value.Quantity,
            material.Value.FreshUntilTick)).ToArray();

    public int GetDelivered(CraftingMaterialRequirement requirement) =>
        _deliveredMaterials
            .Where(material => requirement.Matches(
                material.Key.Resource,
                material.Key.FoodKind,
                material.Key.Variant))
            .Sum(material => material.Value.Quantity);

    public void Deliver(
        ResourceKind resource,
        FoodKind foodKind,
        ResourceVariant variant,
        int quantity,
        long? freshUntilTick)
    {
        var requirement = CraftingRecipeCatalog.FindMaterial(
                Recipe,
                resource,
                foodKind,
                variant)
            ?? throw new InvalidOperationException(
                "The crafting order cannot accept this material.");
        if (quantity <= 0 || quantity > GetMissing(requirement))
        {
            throw new InvalidOperationException(
                "The crafting delivery exceeds the outstanding requirement.");
        }

        var key = (resource, foodKind, variant);
        var existing = _deliveredMaterials.GetValueOrDefault(key);
        _deliveredMaterials[key] = new DeliveredMaterial(
            checked(existing.Quantity + quantity),
            MinimumFreshUntilTick(existing.FreshUntilTick, freshUntilTick));
    }

    public int GetMissing(CraftingMaterialRequirement requirement) => Math.Max(
        0,
        requirement.Quantity - GetDelivered(requirement));

    public int GetMissing(
        ResourceKind resource,
        FoodKind foodKind,
        ResourceVariant variant) =>
        CraftingRecipeCatalog.FindMaterial(
            Recipe,
            resource,
            foodKind,
            variant) is { } requirement
            ? GetMissing(requirement)
            : 0;

    public bool HasAllMaterials => CraftingRecipeCatalog.Get(Recipe).Materials
        .All(material => GetMissing(material) == 0);

    public void ResetForNextCycle()
    {
        _deliveredMaterials.Clear();
        RemainingWorkTicks = TotalWorkTicks;
    }

    public int SpoilExpiredFood(long currentTick)
    {
        var spoiled = 0;
        foreach (var material in _deliveredMaterials
                     .Where(material =>
                         material.Key.Resource == ResourceKind.Food &&
                         material.Value.FreshUntilTick <= currentTick)
                     .ToArray())
        {
            spoiled = checked(spoiled + material.Value.Quantity);
            _deliveredMaterials.Remove(material.Key);
        }
        return spoiled;
    }

    public CraftingOrderSnapshot ToSnapshot() => new(
        Id,
        Recipe,
        Workshop,
        CraftingRecipeCatalog.Get(Recipe).Materials
            .Select(material => new CraftingMaterialSnapshot(
                material.Resource,
                material.Variant,
                material.Quantity,
                GetDelivered(material),
                material.FoodKind))
            .ToArray(),
        RemainingWorkTicks,
        TotalWorkTicks,
        IsRepeating,
        IsAutomatic);

    private static SortedDictionary<(ResourceKind, FoodKind, ResourceVariant), DeliveredMaterial>
        CreateDeliveredMaterials(IEnumerable<CraftingDeliveredMaterialState> materials)
    {
        var result = new SortedDictionary<
            (ResourceKind, FoodKind, ResourceVariant), DeliveredMaterial>();
        foreach (var material in materials)
        {
            result.Add(
                (material.Resource, material.FoodKind, material.Variant),
                new DeliveredMaterial(material.Quantity, material.FreshUntilTick));
        }
        return result;
    }

    private static long? MinimumFreshUntilTick(long? first, long? second) =>
        first is null ? second : second is null ? first : Math.Min(first.Value, second.Value);

    private readonly record struct DeliveredMaterial(int Quantity, long? FreshUntilTick);
}

internal readonly record struct CraftingDeliveredMaterialState(
    ResourceKind Resource,
    FoodKind FoodKind,
    ResourceVariant Variant,
    int Quantity,
    long? FreshUntilTick = null);
