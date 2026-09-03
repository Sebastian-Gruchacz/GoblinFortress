using GoblinStronghold.Simulation.Resources;
using GoblinStronghold.Simulation.Workshops;

namespace GoblinStronghold.Simulation.Crafting;

internal static class AutomaticCookingPolicy
{
    public static IReadOnlyList<CraftingRecipeDefinition> FindFeasibleRecipes(
        IReadOnlyDictionary<(ResourceKind Resource, FoodKind FoodKind,
            ResourceVariant Variant), int> available)
    {
        ArgumentNullException.ThrowIfNull(available);

        return WorkshopCatalog.Get(WorkshopKind.CookingFire).AvailableRecipes
            .Select(CraftingRecipeCatalog.Get)
            .Where(recipe => CanReserve(recipe, available))
            .OrderBy(recipe => recipe.Kind)
            .ToArray();
    }

    public static bool TryReserve(
        CraftingRecipeDefinition recipe,
        IDictionary<(ResourceKind Resource, FoodKind FoodKind,
            ResourceVariant Variant), int> available)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(available);
        if (!CanReserve(recipe, available))
        {
            return false;
        }

        foreach (var requirement in recipe.Materials)
        {
            var remaining = requirement.Quantity;
            foreach (var key in available.Keys
                         .Where(key => requirement.Matches(
                             key.Resource,
                             key.FoodKind,
                             key.Variant))
                         .OrderBy(key => key.FoodKind)
                         .ThenBy(key => key.Variant)
                         .ToArray())
            {
                var consumed = Math.Min(remaining, available[key]);
                available[key] -= consumed;
                remaining -= consumed;
                if (remaining == 0)
                {
                    break;
                }
            }
        }
        return true;
    }

    private static bool CanReserve(
        CraftingRecipeDefinition recipe,
        IEnumerable<KeyValuePair<(ResourceKind Resource, FoodKind FoodKind,
            ResourceVariant Variant), int>> available) => recipe.Materials.All(requirement =>
        available.Where(pair => requirement.Matches(
                pair.Key.Resource,
                pair.Key.FoodKind,
                pair.Key.Variant))
            .Sum(pair => pair.Value) >= requirement.Quantity);
}
