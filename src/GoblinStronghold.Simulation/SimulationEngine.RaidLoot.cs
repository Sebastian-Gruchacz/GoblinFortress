using GoblinStronghold.Simulation.Resources;
using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation;

public sealed partial class SimulationEngine
{
    private bool TryPlanCorpseDirective(ActorState actor)
    {
        if (actor.CarriedCorpseId != EntityId.None)
        {
            if (!_corpses.TryGetValue(actor.CarriedCorpseId, out var carried) ||
                (carried.Directives & (CorpseDirective.RecoverToCamp |
                    CorpseDirective.RecoverAndBudAtCamp)) == 0)
            {
                actor.CarriedCorpseId = EntityId.None;
                return false;
            }

            var site = FindNearestReachableCorpseSite(actor);
            return site is not null && BeginRaidCorpseRecoveryTravel(
                actor,
                site.Value,
                ActorJobStage.Delivering);
        }

        foreach (var corpse in _corpses.Values)
        {
            if (corpse.Contents.Count == 0)
            {
                corpse.Directives &= ~CorpseDirective.LootContents;
            }
            if (corpse.EdiblePortions == 0)
            {
                corpse.Directives &= ~CorpseDirective.Consume;
            }
        }

        var reservedCorpseIds = _actors.Values
            .Where(candidate => candidate.CarriedCorpseId != EntityId.None ||
                candidate.JobKind is ActorJobKind.LootRaid or
                    ActorJobKind.RecoverRaidCorpse or ActorJobKind.ConsumeRaidCorpse)
            .Select(candidate => candidate.CarriedCorpseId != EntityId.None
                ? candidate.CarriedCorpseId
                : candidate.SourceStackId)
            .Where(id => id != EntityId.None)
            .ToHashSet();
        var ordered = _corpses.Values
            .Where(corpse => corpse.Directives != CorpseDirective.None &&
                !reservedCorpseIds.Contains(corpse.Id))
            .Select(corpse => new
            {
                Corpse = corpse,
                Route = FindActorPath(actor, corpse.Position),
            })
            .Where(candidate => candidate.Route is not null)
            .OrderBy(candidate => candidate.Route!.Count)
            .ThenBy(candidate => candidate.Corpse.Id)
            .FirstOrDefault();
        if (ordered is null)
        {
            return false;
        }

        var target = ordered.Corpse;
        actor.SourceStackId = target.Id;
        if (target.Directives.HasFlag(CorpseDirective.LootContents))
        {
            return BeginRaidLootTravel(actor, target.Position, ActorJobStage.Collecting);
        }
        if (target.Directives.HasFlag(CorpseDirective.Consume))
        {
            return BeginCorpseConsumptionTravel(actor, target);
        }

        var handling = target.Directives & CorpseHandlingDirectives;
        if (handling == CorpseDirective.None)
        {
            actor.ClearJob();
            return false;
        }
        if (handling != CorpseDirective.BudInPlace &&
            FindNearestReachableCorpseSite(actor) is null)
        {
            actor.ClearJob();
            return false;
        }
        return BeginRaidCorpseRecoveryTravel(
            actor,
            target.Position,
            ActorJobStage.Collecting);
    }

    private bool BeginCorpseConsumptionTravel(ActorState actor, CorpseState corpse)
    {
        actor.JobKind = ActorJobKind.ConsumeRaidCorpse;
        actor.JobStage = ActorJobStage.Collecting;
        actor.JobTarget = corpse.Position;
        if (actor.Position == corpse.Position)
        {
            actor.JobPhase = ActorJobPhase.Working;
            actor.RemainingWorkTicks = Definitions.EatWorkTicks;
            return true;
        }

        var request = RequestActorPath(actor, corpse.Position);
        if (request.Status == NavigationPathRequestStatus.Pending)
        {
            actor.ClearJob();
            return true;
        }
        if (request.Path is not { Count: > 0 } route)
        {
            actor.ClearJob();
            return false;
        }
        actor.JobPhase = ActorJobPhase.Traveling;
        actor.RemainingRoute.AddRange(route);
        return true;
    }

    private GridPosition? FindNearestReachableCorpseSite(ActorState actor) =>
        World.EnumerateWorldObjects()
            .Where(worldObject => IsCorpseRecoverySite(worldObject.Anchor))
            .Select(worldObject => new
            {
                worldObject.Anchor,
                Route = FindActorPath(actor, worldObject.Anchor),
            })
            .Where(candidate => candidate.Route is not null)
            .OrderBy(candidate => candidate.Route!.Count)
            .ThenBy(candidate => candidate.Anchor.Y)
            .ThenBy(candidate => candidate.Anchor.X)
            .Select(candidate => (GridPosition?)candidate.Anchor)
            .FirstOrDefault();

    private bool IsCorpseRecoverySite(GridPosition position) =>
        World.IsTerrainTraversable(position) &&
        World.GetWorldObjectsAt(position).Any(worldObject =>
            worldObject.Kind is (WorldObjectKind.GoblinFieldCamp or
                WorldObjectKind.GoblinCompost) &&
            worldObject.Owner == WorldObjectOwner.GoblinTribe &&
            worldObject.Anchor == position);

    private bool TryPlanRaidLoot(ActorState actor)
    {
        if (actor.CarriedCorpseId != EntityId.None)
        {
            if (actor.Position == _raidRallyPoint)
            {
                var corpseId = actor.CarriedCorpseId;
                actor.CarriedCorpseId = EntityId.None;
                if (GetRaidCorpseHandlingMode() == RaidCorpseHandlingMode.RecoverAndBudAtCamp)
                {
                    TryCreateGoblinBudFromCorpse(corpseId, actor.Id, actor.Position);
                }
                actor.ClearJob();
                return true;
            }
            return BeginRaidCorpseRecoveryTravel(
                actor,
                _raidRallyPoint,
                ActorJobStage.Delivering);
        }

        if (actor.CarriedStackId != EntityId.None)
        {
            if (actor.Position == _raidRallyPoint)
            {
                DropCarriedStack(actor);
                actor.ClearJob();
                return true;
            }
            return BeginRaidLootTravel(actor, _raidRallyPoint, ActorJobStage.Delivering);
        }

        var corpse = _corpses.Values
            .Where(item => item.Kind == CorpseKind.Human &&
                Distance(item.Position, _raidTarget) <= _raidTargetRadius &&
                item.Contents.Any(IsRaidLootAllowed))
            .OrderBy(item => Distance(actor.Position, item.Position))
            .ThenBy(item => item.Id)
            .FirstOrDefault();
        if (corpse is null)
        {
            var container = CreateVillageLootSnapshot()
                .Where(item => Distance(item.Position, _raidTarget) <= _raidTargetRadius &&
                    item.Contents.Any(IsRaidLootAllowed))
                .OrderBy(item => Distance(actor.Position, item.Position))
                .ThenBy(item => item.StructureId)
                .FirstOrDefault();
            if (container is null)
            {
                return TryPlanRaidCorpseConsumption(actor) ||
                    TryPlanRaidCorpseRecovery(actor);
            }
            actor.DestinationZoneId = new EntityId(container.StructureId.Value);
            return BeginRaidLootTravel(actor, container.Position, ActorJobStage.Collecting);
        }

        actor.SourceStackId = corpse.Id;
        return BeginRaidLootTravel(actor, corpse.Position, ActorJobStage.Collecting);
    }

    private bool TryPlanRaidCorpseConsumption(ActorState actor)
    {
        if (!_raidDirectives.HasFlag(RaidDirective.ConsumeCorpses))
        {
            return false;
        }

        var reservedCorpseIds = _actors.Values
            .Where(item => item.JobKind == ActorJobKind.ConsumeRaidCorpse)
            .Select(item => item.SourceStackId)
            .ToHashSet();
        var corpse = _corpses.Values
            .Where(item => item.Kind == CorpseKind.Human && item.EdiblePortions > 0 &&
                Distance(item.Position, _raidTarget) <= _raidTargetRadius &&
                !item.Contents.Any(IsRaidLootAllowed) &&
                !reservedCorpseIds.Contains(item.Id))
            .OrderBy(item => Distance(actor.Position, item.Position))
            .ThenBy(item => item.Id)
            .FirstOrDefault();
        if (corpse is null)
        {
            return false;
        }

        actor.JobKind = ActorJobKind.ConsumeRaidCorpse;
        actor.JobStage = ActorJobStage.Collecting;
        actor.JobTarget = corpse.Position;
        actor.SourceStackId = corpse.Id;
        if (actor.Position == corpse.Position)
        {
            actor.JobPhase = ActorJobPhase.Working;
            actor.RemainingWorkTicks = Definitions.EatWorkTicks;
            return true;
        }

        var request = RequestActorPath(actor, corpse.Position);
        if (request.Status == NavigationPathRequestStatus.Pending)
        {
            actor.ClearJob();
            return true;
        }
        if (request.Path is not { Count: > 0 } route)
        {
            actor.ClearJob();
            return false;
        }
        actor.JobPhase = ActorJobPhase.Traveling;
        actor.RemainingRoute.AddRange(route);
        return true;
    }

    private void UpdateRaidCorpseConsumptionJob(ActorState actor)
    {
        var isGenericOrder = _corpses.TryGetValue(actor.SourceStackId, out var corpse) &&
            corpse.Directives.HasFlag(CorpseDirective.Consume);
        if ((!isGenericOrder &&
                (_raidPhase != GoblinRaidPhase.Looting || !_raidPartyIds.Contains(actor.Id))) ||
            corpse is null ||
            (!isGenericOrder && corpse.Kind != CorpseKind.Human) ||
            corpse.EdiblePortions <= 0 ||
            corpse.Position != actor.JobTarget)
        {
            actor.ClearJob();
            return;
        }
        if (actor.JobPhase == ActorJobPhase.Traveling)
        {
            AdvanceTravel(actor);
        }
        if (actor.JobKind != ActorJobKind.ConsumeRaidCorpse ||
            actor.JobPhase != ActorJobPhase.Working)
        {
            return;
        }

        actor.RemainingWorkTicks--;
        if (actor.RemainingWorkTicks > 0)
        {
            return;
        }

        corpse.EdiblePortions--;
        if (corpse.EdiblePortions == 0)
        {
            corpse.Directives &= ~CorpseDirective.Consume;
        }
        ApplyFoodEffects(actor, FoodKind.RawMeat);
        Publish(SimulationEventKind.CorpseConsumed, actor.Id, corpse.Id, 1);
        actor.ClearJob();
    }

    private bool TryPlanRaidCorpseRecovery(ActorState actor)
    {
        var handlingMode = GetRaidCorpseHandlingMode();
        if (handlingMode == RaidCorpseHandlingMode.None)
        {
            return false;
        }

        var reservedCorpseIds = _actors.Values
            .Where(item => item.CarriedCorpseId != EntityId.None ||
                item.JobKind == ActorJobKind.RecoverRaidCorpse)
            .Select(item => item.CarriedCorpseId != EntityId.None
                ? item.CarriedCorpseId
                : item.SourceStackId)
            .ToHashSet();
        var corpse = _corpses.Values
            .Where(item =>
                (handlingMode == RaidCorpseHandlingMode.BudInPlace ||
                    item.Position != _raidRallyPoint) &&
                Distance(item.Position, _raidTarget) <= _raidTargetRadius &&
                !item.Contents.Any(IsRaidLootAllowed) &&
                (!_raidDirectives.HasFlag(RaidDirective.ConsumeCorpses) ||
                    item.EdiblePortions == 0) &&
                !reservedCorpseIds.Contains(item.Id))
            .OrderBy(item => Distance(actor.Position, item.Position))
            .ThenBy(item => item.Id)
            .FirstOrDefault();
        if (corpse is null)
        {
            return false;
        }

        actor.SourceStackId = corpse.Id;
        return BeginRaidCorpseRecoveryTravel(
            actor,
            corpse.Position,
            ActorJobStage.Collecting);
    }

    private bool TryPlanRaidReturn(ActorState actor)
    {
        if (actor.Position == _raidRallyPoint)
        {
            return true;
        }

        var request = RequestActorPath(actor, _raidRallyPoint);
        if (request.Status == NavigationPathRequestStatus.Pending ||
            request.Status == NavigationPathRequestStatus.Unreachable ||
            request.Path is not { Count: > 0 } route)
        {
            return true;
        }

        actor.JobKind = ActorJobKind.Move;
        actor.JobPhase = ActorJobPhase.Traveling;
        actor.JobTarget = _raidRallyPoint;
        actor.RemainingRoute.AddRange(route);
        return true;
    }

    private bool BeginRaidCorpseRecoveryTravel(
        ActorState actor,
        GridPosition target,
        ActorJobStage stage)
    {
        actor.JobKind = ActorJobKind.RecoverRaidCorpse;
        actor.JobStage = stage;
        actor.JobTarget = target;
        if (actor.Position == target)
        {
            actor.JobPhase = ActorJobPhase.Working;
            actor.RemainingWorkTicks = Definitions.HaulHandlingTicks;
            return true;
        }

        var request = RequestActorPath(actor, target);
        if (request.Status == NavigationPathRequestStatus.Pending)
        {
            actor.ClearJob();
            return true;
        }
        if (request.Path is not { Count: > 0 } route)
        {
            actor.ClearJob();
            return false;
        }
        actor.JobPhase = ActorJobPhase.Traveling;
        actor.RemainingRoute.AddRange(route);
        return true;
    }

    private void UpdateRaidCorpseRecoveryJob(ActorState actor)
    {
        var orderedCorpseId = actor.CarriedCorpseId != EntityId.None
            ? actor.CarriedCorpseId
            : actor.SourceStackId;
        var isGenericOrder = _corpses.TryGetValue(orderedCorpseId, out var orderedCorpse) &&
            (orderedCorpse.Directives & CorpseHandlingDirectives) != 0;
        if (!isGenericOrder &&
            (_raidPhase != GoblinRaidPhase.Looting || !_raidPartyIds.Contains(actor.Id)))
        {
            actor.ClearJob();
            return;
        }
        if (actor.JobPhase == ActorJobPhase.Traveling)
        {
            AdvanceTravel(actor);
        }
        if (actor.JobKind != ActorJobKind.RecoverRaidCorpse ||
            actor.JobPhase != ActorJobPhase.Working)
        {
            return;
        }

        actor.RemainingWorkTicks--;
        if (actor.RemainingWorkTicks > 0)
        {
            return;
        }

        if (actor.JobStage == ActorJobStage.Collecting)
        {
            if (!_corpses.TryGetValue(actor.SourceStackId, out var corpse) ||
                corpse.Position != actor.Position ||
                (!isGenericOrder &&
                    corpse.Contents.Any(IsRaidLootAllowed)))
            {
                actor.ClearJob();
                return;
            }
            if (isGenericOrder &&
                corpse.Directives.HasFlag(CorpseDirective.BudInPlace))
            {
                TryCreateGoblinBudFromCorpse(corpse.Id, actor.Id, actor.Position);
            }
            else if (!isGenericOrder &&
                GetRaidCorpseHandlingMode() == RaidCorpseHandlingMode.BudInPlace)
            {
                TryCreateGoblinBudFromCorpse(corpse.Id, actor.Id, actor.Position);
            }
            else
            {
                actor.CarriedCorpseId = corpse.Id;
            }
        }
        else
        {
            var corpseId = actor.CarriedCorpseId;
            actor.CarriedCorpseId = EntityId.None;
            if (isGenericOrder && orderedCorpse is not null)
            {
                if (orderedCorpse.Directives.HasFlag(CorpseDirective.RecoverAndBudAtCamp))
                {
                    TryCreateGoblinBudFromCorpse(corpseId, actor.Id, actor.Position);
                }
                else
                {
                    orderedCorpse.Directives &= ~CorpseHandlingDirectives;
                }
            }
            else if (GetRaidCorpseHandlingMode() == RaidCorpseHandlingMode.RecoverAndBudAtCamp)
            {
                TryCreateGoblinBudFromCorpse(corpseId, actor.Id, actor.Position);
            }
        }
        actor.ClearJob();
    }

    private bool BeginRaidLootTravel(
        ActorState actor,
        GridPosition target,
        ActorJobStage stage)
    {
        actor.JobKind = ActorJobKind.LootRaid;
        actor.JobStage = stage;
        actor.JobTarget = target;
        if (actor.Position == target)
        {
            actor.JobPhase = ActorJobPhase.Working;
            actor.RemainingWorkTicks = Definitions.HaulHandlingTicks;
            return true;
        }

        var request = RequestActorPath(actor, target);
        if (request.Status == NavigationPathRequestStatus.Pending)
        {
            actor.ClearJob();
            return true;
        }
        if (request.Path is not { Count: > 0 } route)
        {
            actor.ClearJob();
            return false;
        }
        actor.JobPhase = ActorJobPhase.Traveling;
        actor.RemainingRoute.AddRange(route);
        return true;
    }

    private void UpdateRaidLootJob(ActorState actor)
    {
        var isGenericOrder = actor.SourceStackId != EntityId.None &&
            _corpses.TryGetValue(actor.SourceStackId, out var orderedCorpse) &&
            orderedCorpse.Directives.HasFlag(CorpseDirective.LootContents);
        if (!isGenericOrder &&
            (_raidPhase != GoblinRaidPhase.Looting || !_raidPartyIds.Contains(actor.Id)))
        {
            actor.ClearJob();
            return;
        }
        if (actor.JobPhase == ActorJobPhase.Traveling)
        {
            AdvanceTravel(actor);
        }
        if (actor.JobKind != ActorJobKind.LootRaid || actor.JobPhase != ActorJobPhase.Working)
        {
            return;
        }
        actor.RemainingWorkTicks--;
        if (actor.RemainingWorkTicks > 0)
        {
            return;
        }

        if (actor.JobStage == ActorJobStage.Collecting)
        {
            if (actor.SourceStackId != EntityId.None)
            {
                if (isGenericOrder)
                {
                    CollectOrderedCorpseLoot(actor);
                }
                else
                {
                    CollectRaidCorpseLoot(actor);
                }
            }
            else
            {
                CollectRaidBuildingLoot(actor);
            }
        }
        else
        {
            DropCarriedStack(actor);
            actor.ClearJob();
        }
    }

    private void CollectRaidCorpseLoot(ActorState actor)
    {
        if (!_corpses.TryGetValue(actor.SourceStackId, out var corpse) ||
            corpse.Position != actor.Position)
        {
            actor.ClearJob();
            return;
        }
        var index = corpse.Contents.FindIndex(IsRaidLootAllowed);
        if (index < 0)
        {
            actor.ClearJob();
            return;
        }
        var loot = corpse.Contents[index];
        var quantity = GetRaidLootQuantity(loot);
        corpse.Contents[index] = loot with { Quantity = loot.Quantity - quantity };
        if (corpse.Contents[index].Quantity == 0)
        {
            corpse.Contents.RemoveAt(index);
        }
        GiveRaidLootToActor(actor, loot, quantity);
        actor.SourceStackId = EntityId.None;
        actor.ClearJob();
    }

    private void CollectOrderedCorpseLoot(ActorState actor)
    {
        if (!_corpses.TryGetValue(actor.SourceStackId, out var corpse) ||
            corpse.Position != actor.Position || corpse.Contents.Count == 0)
        {
            actor.ClearJob();
            return;
        }

        var loot = corpse.Contents[0];
        var quantity = GetRaidLootQuantity(loot);
        corpse.Contents[0] = loot with { Quantity = loot.Quantity - quantity };
        if (corpse.Contents[0].Quantity == 0)
        {
            corpse.Contents.RemoveAt(0);
        }
        if (corpse.Contents.Count == 0)
        {
            corpse.Directives &= ~CorpseDirective.LootContents;
        }
        GiveRaidLootToActor(actor, loot, quantity);
        actor.SourceStackId = EntityId.None;
        actor.ClearJob();
    }

    private void CollectRaidBuildingLoot(ActorState actor)
    {
        var structureId = new Map.WorldObjectId(actor.DestinationZoneId.Value);
        var container = CreateVillageLootSnapshot().FirstOrDefault(item =>
            item.StructureId == structureId && item.Position == actor.Position);
        var loot = container?.Contents.FirstOrDefault(IsRaidLootAllowed) ?? default;
        if (container is null || loot.Quantity <= 0)
        {
            actor.ClearJob();
            return;
        }

        var quantity = GetRaidLootQuantity(loot);
        if (loot.Resource == ResourceKind.Equipment)
        {
            if (!_stolenVillageEquipment.Add(loot.Variant))
            {
                actor.ClearJob();
                return;
            }
        }
        else if (!_humanVillage.TryTakeRaidLoot(loot.Resource, quantity))
        {
            actor.ClearJob();
            return;
        }

        GiveRaidLootToActor(actor, loot, quantity);
        actor.DestinationZoneId = EntityId.None;
        actor.ClearJob();
    }

    private int GetRaidLootQuantity(CorpseItemSnapshot loot) => Math.Min(
        loot.Quantity,
        Math.Max(1, Definitions.ActorCarryCapacity / loot.UnitWeight));

    private void GiveRaidLootToActor(
        ActorState actor,
        CorpseItemSnapshot loot,
        int quantity)
    {
        var carried = AllocateItemStack(
            loot.Resource,
            quantity,
            ItemLocation.CarriedBy(actor.Id),
            loot.FoodKind,
            loot.Variant);
        actor.CarriedStackId = carried.Id;
    }

    private bool HasRemainingRaidCorpseLoot() => _corpses.Values.Any(item =>
        item.Kind == CorpseKind.Human &&
        Distance(item.Position, _raidTarget) <= _raidTargetRadius &&
        item.Contents.Any(IsRaidLootAllowed));

    private bool HasRemainingRaidBuildingLoot() => CreateVillageLootSnapshot().Any(container =>
        Distance(container.Position, _raidTarget) <= _raidTargetRadius &&
        container.Contents.Any(IsRaidLootAllowed));

    private bool HasRemainingRaidCorpseRecovery() =>
        GetRaidCorpseHandlingMode() != RaidCorpseHandlingMode.None &&
        (_actors.Values.Any(actor => actor.CarriedCorpseId != EntityId.None) ||
         _corpses.Values.Any(corpse =>
             (GetRaidCorpseHandlingMode() == RaidCorpseHandlingMode.BudInPlace ||
                 corpse.Position != _raidRallyPoint) &&
             !corpse.Contents.Any(IsRaidLootAllowed) &&
             (!_raidDirectives.HasFlag(RaidDirective.ConsumeCorpses) ||
                 corpse.EdiblePortions == 0) &&
             Distance(corpse.Position, _raidTarget) <= _raidTargetRadius));

    private RaidCorpseHandlingMode GetRaidCorpseHandlingMode() =>
        _raidDirectives.HasFlag(RaidDirective.BudCorpsesInPlace)
            ? RaidCorpseHandlingMode.BudInPlace
            : _raidDirectives.HasFlag(RaidDirective.BudCorpses)
                ? RaidCorpseHandlingMode.RecoverAndBudAtCamp
                : _raidDirectives.HasFlag(RaidDirective.RecoverCorpses)
                    ? RaidCorpseHandlingMode.RecoverToCamp
                    : RaidCorpseHandlingMode.None;

    private bool HasRemainingRaidCorpseConsumption() =>
        _raidDirectives.HasFlag(RaidDirective.ConsumeCorpses) &&
        _corpses.Values.Any(corpse =>
            corpse.Kind == CorpseKind.Human && corpse.EdiblePortions > 0 &&
            Distance(corpse.Position, _raidTarget) <= _raidTargetRadius);

    private bool IsRaidLootAllowed(CorpseItemSnapshot item) =>
        (item.Resource == ResourceKind.Equipment &&
            _raidDirectives.HasFlag(RaidDirective.LootEquipment)) ||
        (item.Resource == ResourceKind.Food &&
            _raidDirectives.HasFlag(RaidDirective.LootFood)) ||
        (item.Resource is not (ResourceKind.Equipment or ResourceKind.Food) &&
            _raidDirectives.HasFlag(RaidDirective.LootSupplies));
}
