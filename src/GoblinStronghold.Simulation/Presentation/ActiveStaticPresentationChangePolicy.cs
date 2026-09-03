using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Presentation;

public static class ActiveStaticPresentationChangePolicy
{
    public static bool HasChanged(
        SimulationSnapshot previous,
        SimulationSnapshot current,
        int level)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        return previous.WorldVersion != current.WorldVersion ||
            !FilteredSequenceEqual(
                previous.ItemStacks,
                current.ItemStacks,
                level,
                static (item, activeLevel) =>
                    item.Location.Kind == Resources.ItemLocationKind.Ground &&
                    item.Location.Position.Z == activeLevel,
                ItemsEqual) ||
            !FilteredSequenceEqual(
                previous.StorageZones,
                current.StorageZones,
                level,
                static (zone, activeLevel) => zone.Position.Z == activeLevel,
                StorageZonesEqual) ||
            !FilteredContextSequenceEqual(
                previous.StorageAreas,
                current.StorageAreas,
                level,
                static (area, activeLevel) => ContainsLevel(area.Footprint, activeLevel),
                StorageAreasEqual) ||
            !FilteredSequenceEqual(
                previous.ConstructionSites,
                current.ConstructionSites,
                level,
                static (site, activeLevel) =>
                    ContainsLevel(site.Footprint, activeLevel),
                ConstructionSitesEqual) ||
            !FilteredSequenceEqual(
                previous.CraftingOrders,
                current.CraftingOrders,
                level,
                static (order, activeLevel) => order.Workshop.Z == activeLevel,
                CraftingOrdersEqual) ||
            !FilteredSequenceEqual(
                previous.Corpses,
                current.Corpses,
                level,
                static (corpse, activeLevel) => corpse.Position.Z == activeLevel,
                CorpsesEqual) ||
            !FilteredSequenceEqual(
                previous.GoblinBuds,
                current.GoblinBuds,
                level,
                static (bud, activeLevel) => bud.Position.Z == activeLevel,
                BudsEqual) ||
            !FilteredSequenceEqual(
                previous.BloodStains,
                current.BloodStains,
                level,
                static (stain, activeLevel) => stain.Position.Z == activeLevel,
                static (left, right) => left.Position == right.Position &&
                    left.Volume == right.Volume && left.Surface == right.Surface) ||
            !FilteredSequenceEqual(
                previous.SurfaceGrime,
                current.SurfaceGrime,
                level,
                static (stain, activeLevel) => stain.Position.Z == activeLevel,
                static (left, right) => left.Position == right.Position &&
                    left.Volume == right.Volume) ||
            level == 0 && !SequenceEqual(
                previous.HumanVillage.Fields,
                current.HumanVillage.Fields,
                static (left, right) => left.Id == right.Id &&
                    left.Position == right.Position && left.Phase == right.Phase);
    }

    private static bool ItemsEqual(Resources.ItemStackSnapshot left,
        Resources.ItemStackSnapshot right) =>
        left.Id == right.Id && left.Resource == right.Resource &&
        left.FoodKind == right.FoodKind && left.Variant == right.Variant &&
        left.Quantity == right.Quantity && left.Location.Position == right.Location.Position;

    private static bool StorageZonesEqual(Resources.StorageZoneSnapshot left,
        Resources.StorageZoneSnapshot right) =>
        left.Id == right.Id && left.Position == right.Position &&
        left.AcceptedResource == right.AcceptedResource && left.Capacity == right.Capacity &&
        left.StoredQuantity == right.StoredQuantity && left.ProviderKind == right.ProviderKind;

    private static bool StorageAreasEqual(Resources.StorageAreaSnapshot left,
        Resources.StorageAreaSnapshot right,
        int level) =>
        left.Id == right.Id && left.LogisticsNetworkId == right.LogisticsNetworkId &&
        FilteredSequenceEqual(
            left.Footprint,
            right.Footprint,
            level,
            static (position, activeLevel) => position.Z == activeLevel,
            static (first, second) => first == second);

    private static bool ConstructionSitesEqual(
        ConstructionSiteSnapshot left,
        ConstructionSiteSnapshot right) =>
        left.Id == right.Id && left.Anchor == right.Anchor &&
        left.RemainingWorkTicks == right.RemainingWorkTicks &&
        left.TotalWorkTicks == right.TotalWorkTicks &&
        SequenceEqual(left.Footprint, right.Footprint, static (first, second) => first == second) &&
        SequenceEqual(left.Materials, right.Materials, static (first, second) => first == second);

    private static bool CraftingOrdersEqual(CraftingOrderSnapshot left,
        CraftingOrderSnapshot right) =>
        left.Id == right.Id && left.Workshop == right.Workshop &&
        left.RemainingWorkTicks == right.RemainingWorkTicks &&
        left.TotalWorkTicks == right.TotalWorkTicks &&
        SequenceEqual(left.Materials, right.Materials, static (first, second) => first == second);

    private static bool CorpsesEqual(CorpseSnapshot left, CorpseSnapshot right) =>
        left.Id == right.Id && left.Kind == right.Kind && left.Position == right.Position;

    private static bool BudsEqual(GoblinBudSnapshot left, GoblinBudSnapshot right) =>
        left.Id == right.Id && left.Position == right.Position &&
        left.RemainingCareTicks == right.RemainingCareTicks &&
        left.TotalCareTicks == right.TotalCareTicks &&
        left.OriginCorpseId == right.OriginCorpseId;

    private static bool ContainsLevel(IReadOnlyList<GridPosition> positions, int level)
    {
        for (var index = 0; index < positions.Count; index++)
        {
            if (positions[index].Z == level)
            {
                return true;
            }
        }
        return false;
    }

    private static bool SequenceEqual<T>(
        IReadOnlyList<T> left,
        IReadOnlyList<T> right,
        Func<T, T, bool> equals)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!equals(left[index], right[index]))
            {
                return false;
            }
        }
        return true;
    }

    private static bool FilteredSequenceEqual<T, TContext>(
        IReadOnlyList<T> left,
        IReadOnlyList<T> right,
        TContext context,
        Func<T, TContext, bool> include,
        Func<T, T, bool> equals)
    {
        var leftIndex = 0;
        var rightIndex = 0;
        while (true)
        {
            while (leftIndex < left.Count && !include(left[leftIndex], context))
            {
                leftIndex++;
            }
            while (rightIndex < right.Count && !include(right[rightIndex], context))
            {
                rightIndex++;
            }
            if (leftIndex == left.Count || rightIndex == right.Count)
            {
                return leftIndex == left.Count && rightIndex == right.Count;
            }
            if (!equals(left[leftIndex++], right[rightIndex++]))
            {
                return false;
            }
        }
    }

    private static bool FilteredContextSequenceEqual<T, TContext>(
        IReadOnlyList<T> left,
        IReadOnlyList<T> right,
        TContext context,
        Func<T, TContext, bool> include,
        Func<T, T, TContext, bool> equals)
    {
        var leftIndex = 0;
        var rightIndex = 0;
        while (true)
        {
            while (leftIndex < left.Count && !include(left[leftIndex], context))
            {
                leftIndex++;
            }
            while (rightIndex < right.Count && !include(right[rightIndex], context))
            {
                rightIndex++;
            }
            if (leftIndex == left.Count || rightIndex == right.Count)
            {
                return leftIndex == left.Count && rightIndex == right.Count;
            }
            if (!equals(left[leftIndex++], right[rightIndex++], context))
            {
                return false;
            }
        }
    }
}
