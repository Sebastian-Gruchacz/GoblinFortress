using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation.Watchtowers;

public readonly record struct WatchtowerPostSnapshot(
    WorldObjectId WatchtowerId,
    GridPosition PlatformPosition,
    IReadOnlyList<EntityId> GuardIds,
    EntityId FoodStorageId);

public static class WatchtowerDutyPolicy
{
    public const int Capacity = 2;
    public const int VisionRangeMultiplier = 2;
    public const int RangedAttackRangeMultiplier = 2;
    public const int FoodStorageCapacity = 12;
    public const int FoodStorageTarget = 6;

    public static bool CanDrawBelowSourceTarget(
        ResourceKind resource,
        bool sourceIsWatchtowerStorage,
        bool destinationIsWatchtowerStorage) =>
        resource == ResourceKind.Food &&
        destinationIsWatchtowerStorage &&
        !sourceIsWatchtowerStorage;

    public static IReadOnlyList<GridPosition> GetDutyPositions(WorldObjectSnapshot watchtower)
    {
        ArgumentNullException.ThrowIfNull(watchtower);
        if (watchtower.Kind != WorldObjectKind.WoodenWatchtower)
        {
            throw new ArgumentException("The world object is not a watchtower.", nameof(watchtower));
        }

        return watchtower.GetAbsoluteParts()
            .Where(item => item.Part.Kind == WorldObjectPartKind.WatchtowerPlatform)
            .Select(item => item.Position)
            .OrderBy(position => position.Y)
            .ThenBy(position => position.X)
            .Take(Capacity)
            .ToArray();
    }

    public static bool IsGuardAtPost(
        EntityId actorId,
        GridPosition actorPosition,
        WorldObjectSnapshot watchtower,
        IReadOnlyCollection<EntityId> assignedGuardIds) =>
        assignedGuardIds.Contains(actorId) &&
        watchtower.GetAbsoluteParts().Any(item =>
            item.Position == actorPosition &&
            item.Part.Kind == WorldObjectPartKind.WatchtowerPlatform);
}
