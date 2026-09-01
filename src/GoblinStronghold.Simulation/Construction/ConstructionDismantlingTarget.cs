using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation.Construction;

internal readonly record struct ConstructionDismantlingTarget(
    EntityId EntityId,
    ConstructionKind Construction,
    IReadOnlyList<GridPosition> Footprint)
{
    public int WorkTicks => ConstructionDismantlingPolicy.GetWorkTicks(
        Construction,
        Footprint.Count);
}

internal static class ConstructionDismantlingTargetFactory
{
    public static bool TryCreate(
        WorldObjectSnapshot worldObject,
        out ConstructionDismantlingTarget target)
    {
        ArgumentNullException.ThrowIfNull(worldObject);
        if (worldObject.Owner != WorldObjectOwner.GoblinTribe ||
            !ConstructionDismantlingPolicy.TryGetConstructionKind(
                worldObject.Kind,
                out var construction))
        {
            target = default;
            return false;
        }

        var footprint = worldObject.GetAbsoluteParts()
            .Select(part => part.Position)
            .Distinct()
            .ToArray();
        if (footprint.Length == 0)
        {
            target = default;
            return false;
        }

        target = new ConstructionDismantlingTarget(
            new EntityId(worldObject.Id.Value),
            construction,
            footprint);
        return true;
    }

    public static ConstructionDismantlingTarget CreateStorage(
        EntityId id,
        GridPosition position,
        StorageProviderKind provider,
        ResourceKind acceptedResource) => new(
            id,
            GetStorageConstructionKind(provider, acceptedResource),
            [position]);

    public static IReadOnlySet<GridPosition> GetAccessCells(
        ConstructionDismantlingTarget target,
        Func<GridPosition, bool> isTraversable,
        Func<GridPosition, IEnumerable<GridPosition>> getCardinalNeighbors)
    {
        ArgumentNullException.ThrowIfNull(isTraversable);
        ArgumentNullException.ThrowIfNull(getCardinalNeighbors);
        return target.Footprint
            .SelectMany(position => isTraversable(position)
                ? getCardinalNeighbors(position).Append(position)
                : getCardinalNeighbors(position))
            .Where(isTraversable)
            .ToHashSet();
    }

    private static ConstructionKind GetStorageConstructionKind(
        StorageProviderKind provider,
        ResourceKind acceptedResource) => provider switch
        {
            StorageProviderKind.WaterBarrel => ConstructionKind.WaterBarrel,
            StorageProviderKind.WoodenBox => ConstructionKind.WoodenBox,
            StorageProviderKind.WoodenChest => ConstructionKind.WoodenChest,
            StorageProviderKind.WoodenBulkBin => ConstructionKind.WoodenBulkBin,
            _ => acceptedResource switch
            {
                ResourceKind.Food => ConstructionKind.FoodStorage,
                ResourceKind.Stone => ConstructionKind.StoneStorage,
                ResourceKind.Equipment => ConstructionKind.EquipmentStorage,
                ResourceKind.Materials => ConstructionKind.MaterialsStorage,
                _ => ConstructionKind.WoodStorage,
            },
        };
}
