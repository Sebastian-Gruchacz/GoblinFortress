using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using GoblinStronghold.Simulation.Watchtowers;

namespace GoblinStronghold.Simulation;

public sealed partial class SimulationEngine
{
    private WatchtowerPostSnapshot[] CreateWatchtowerPostSnapshot() =>
        _watchtowerPosts.Values
            .Select(post =>
            {
                var watchtower = GetWatchtower(post.WatchtowerId)!;
                return new WatchtowerPostSnapshot(
                    post.WatchtowerId,
                    WatchtowerDutyPolicy.GetDutyPositions(watchtower)[0],
                    post.GuardIds.ToArray(),
                    post.FoodStorageId);
            })
            .ToArray();

    private void LoadWatchtowerPosts(IEnumerable<WatchtowerPostSaveModel> savedPosts)
    {
        ArgumentNullException.ThrowIfNull(savedPosts);
        foreach (var model in savedPosts)
        {
            var watchtowerId = new WorldObjectId(model.WatchtowerId);
            var storageId = new EntityId(model.FoodStorageId);
            var guardIds = model.GuardIds.Select(id => new EntityId(id)).ToArray();
            var watchtower = GetWatchtower(watchtowerId);
            if (watchtower is null ||
                !_storageZones.TryGetValue(storageId, out var storage) ||
                storage.AcceptedResource != ResourceKind.Food ||
                storage.Position != watchtower.Anchor with { Z = watchtower.Anchor.Z + 1 } ||
                guardIds.Length > WatchtowerDutyPolicy.Capacity ||
                guardIds.Distinct().Count() != guardIds.Length ||
                guardIds.Any(id => !_actors.TryGetValue(id, out var actor) ||
                    actor.Health <= 0 || IsJuvenile(actor)) ||
                !_watchtowerPosts.TryAdd(
                    watchtowerId,
                    new WatchtowerPostState(watchtowerId, storageId, guardIds)))
            {
                throw new InvalidDataException("The save contains an invalid watchtower post.");
            }
        }

        if (_watchtowerPosts.Values.SelectMany(post => post.GuardIds)
            .GroupBy(id => id).Any(group => group.Count() > 1))
        {
            throw new InvalidDataException("A guard is assigned to more than one watchtower.");
        }

        foreach (var watchtower in World.CreateWorldObjectSnapshot().Where(item =>
                     item.Kind == WorldObjectKind.WoodenWatchtower &&
                     item.Owner == WorldObjectOwner.GoblinTribe &&
                     !_watchtowerPosts.ContainsKey(item.Id)))
        {
            var storagePosition = watchtower.Anchor with { Z = watchtower.Anchor.Z + 1 };
            var storage = _storageZones.Values.FirstOrDefault(candidate =>
                    candidate.Position == storagePosition &&
                    candidate.AcceptedResource == ResourceKind.Food) ??
                AllocateStorageZone(
                    storagePosition,
                    ResourceKind.Food,
                    WatchtowerDutyPolicy.FoodStorageCapacity,
                    WatchtowerDutyPolicy.FoodStorageTarget);
            _watchtowerPosts.Add(
                watchtower.Id,
                new WatchtowerPostState(watchtower.Id, storage.Id));
        }
    }

    private bool TryExecuteConfigureWatchtowerGuard(SimulationCommand command)
    {
        var watchtowerId = new WorldObjectId(command.Target.Value);
        if (!_watchtowerPosts.TryGetValue(watchtowerId, out var post) ||
            !_actors.TryGetValue(command.Subject, out var actor) ||
            actor.Health <= 0 || IsJuvenile(actor))
        {
            return false;
        }

        if (command.Amount == 0)
        {
            var removed = post.GuardIds.Remove(actor.Id);
            if (removed && actor.JobKind == ActorJobKind.GuardWatchtower)
            {
                actor.ClearJob();
            }
            return removed;
        }
        if (post.GuardIds.Contains(actor.Id))
        {
            return true;
        }
        if (post.GuardIds.Count >= WatchtowerDutyPolicy.Capacity)
        {
            return false;
        }

        if (_watchtowerPosts.Values.Any(otherPost =>
                otherPost.WatchtowerId != watchtowerId &&
                otherPost.GuardIds.Contains(actor.Id)))
        {
            return false;
        }
        actor.ClearJob();
        actor.ClearSuspendedJob();
        post.GuardIds.Add(actor.Id);
        return true;
    }

    private bool TryPlanWatchtowerDuty(ActorState actor)
    {
        var post = _watchtowerPosts.Values.FirstOrDefault(candidate =>
            candidate.GuardIds.Contains(actor.Id));
        var watchtower = post is null ? null : GetWatchtower(post.WatchtowerId);
        if (post is null || watchtower is null)
        {
            return false;
        }

        var target = ResolveDutyPosition(post, watchtower, actor.Id);
        if (target is null)
        {
            return false;
        }
        var route = FindActorPath(actor, target.Value);
        if (route is null)
        {
            return false;
        }

        actor.JobKind = ActorJobKind.GuardWatchtower;
        actor.JobTarget = target.Value;
        BeginJobLeg(actor, route, int.MaxValue);
        return true;
    }

    private bool TryPlanWatchtowerFoodSupply(
        ActorState actor,
        Dictionary<EntityId, int> sourceReservations,
        Dictionary<EntityId, int> destinationReservations)
    {
        foreach (var post in _watchtowerPosts.Values.OrderBy(post => post.WatchtowerId))
        {
            if (!_storageZones.TryGetValue(post.FoodStorageId, out var storage) ||
                GetStoredQuantity(storage.Id) +
                    destinationReservations.GetValueOrDefault(storage.Id) >=
                    storage.DesiredQuantity)
            {
                continue;
            }

            if (TryPlanHaulCollection(
                    actor,
                    sourceReservations,
                    destinationReservations,
                    requiredDestination: storage.Id))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasWatchtowerDuty(EntityId actorId) =>
        _watchtowerPosts.Values.Any(post => post.GuardIds.Contains(actorId));

    private bool IsWatchtowerFoodStorage(EntityId storageId) =>
        _watchtowerPosts.Values.Any(post => post.FoodStorageId == storageId);

    private IReadOnlySet<GridPosition> GetWatchtowerBedsReservedFor(EntityId actorId)
    {
        var reserved = new HashSet<GridPosition>();
        foreach (var post in _watchtowerPosts.Values)
        {
            var watchtower = GetWatchtower(post.WatchtowerId);
            if (watchtower is null)
            {
                continue;
            }
            var ownBed = ResolveDutyPosition(post, watchtower, actorId);
            foreach (var bed in watchtower.GetAbsoluteParts()
                         .Where(item => item.Part.Kind == WorldObjectPartKind.SleepingMat)
                         .Select(item => item.Position))
            {
                if (bed != ownBed)
                {
                    reserved.Add(bed);
                }
            }
        }
        return reserved;
    }

    private void UpdateWatchtowerDutyJob(ActorState actor)
    {
        var post = _watchtowerPosts.Values.FirstOrDefault(candidate =>
            candidate.GuardIds.Contains(actor.Id));
        var watchtower = post is null ? null : GetWatchtower(post.WatchtowerId);
        if (post is null || watchtower is null ||
            ResolveDutyPosition(post, watchtower, actor.Id) != actor.JobTarget)
        {
            actor.ClearJob();
            return;
        }

        if (actor.JobPhase == ActorJobPhase.Traveling)
        {
            AdvanceTravel(actor);
        }
    }

    private bool IsWatchtowerGuardAtPost(ActorState actor) =>
        _watchtowerPosts.Values.Any(post =>
        {
            var watchtower = GetWatchtower(post.WatchtowerId);
            return watchtower is not null && WatchtowerDutyPolicy.IsGuardAtPost(
                actor.Id, actor.Position, watchtower, post.GuardIds);
        });

    private int ResolveGoblinRangedRange(ActorState actor, int baseRange) =>
        IsWatchtowerGuardAtPost(actor)
            ? checked(baseRange * WatchtowerDutyPolicy.RangedAttackRangeMultiplier)
            : baseRange;

    private static GridPosition? ResolveDutyPosition(
        WatchtowerPostState post,
        WorldObjectSnapshot watchtower,
        EntityId actorId)
    {
        var dutyPositions = WatchtowerDutyPolicy.GetDutyPositions(watchtower);
        var slot = post.GuardIds.TakeWhile(id => id != actorId).Count();
        return slot < dutyPositions.Count && post.GuardIds.Contains(actorId)
            ? dutyPositions[slot]
            : null;
    }

    private WorldObjectSnapshot? GetWatchtower(WorldObjectId id) =>
        World.EnumerateWorldObjects().FirstOrDefault(item =>
            item.Id == id && item.Kind == WorldObjectKind.WoodenWatchtower &&
            item.Owner == WorldObjectOwner.GoblinTribe);

    private sealed class WatchtowerPostState(
        WorldObjectId watchtowerId,
        EntityId foodStorageId,
        IEnumerable<EntityId>? guardIds = null)
    {
        public WorldObjectId WatchtowerId { get; } = watchtowerId;

        public EntityId FoodStorageId { get; } = foodStorageId;

        public SortedSet<EntityId> GuardIds { get; } = guardIds is null
            ? []
            : new SortedSet<EntityId>(guardIds);
    }
}
