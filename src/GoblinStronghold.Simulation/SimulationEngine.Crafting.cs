using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation;

public sealed partial class SimulationEngine
{
    private Dictionary<(
        EntityId OrderId,
        ResourceKind Resource,
        ResourceVariant Variant), int>
        CreateCraftingReservations()
    {
        var reservations = new Dictionary<(EntityId, ResourceKind, ResourceVariant), int>();
        foreach (var actor in _actors.Values.Where(actor =>
                     actor.JobKind == ActorJobKind.SupplyCrafting))
        {
            ItemStackState? stack = null;
            if (actor.JobStage == ActorJobStage.Collecting)
            {
                _itemStacks.TryGetValue(actor.SourceStackId, out stack);
            }
            else if (actor.JobStage == ActorJobStage.Delivering)
            {
                _itemStacks.TryGetValue(actor.CarriedStackId, out stack);
            }
            if (stack is null ||
                !_craftingOrders.TryGetValue(actor.DestinationZoneId, out var order) ||
                CraftingRecipeCatalog.FindMaterial(
                    order.Recipe,
                    stack.Resource,
                    stack.Variant) is not { } requirement)
            {
                continue;
            }

            var key = (actor.DestinationZoneId, requirement.Resource, requirement.Variant);
            reservations[key] = checked(
                reservations.GetValueOrDefault(key) + actor.ReservedQuantity);
        }
        return reservations;
    }

    private bool TryPlanCraftingSupply(
        ActorState actor,
        Dictionary<EntityId, int> sourceReservations,
        Dictionary<(EntityId OrderId, ResourceKind Resource, ResourceVariant Variant), int>
            craftingReservations)
    {
        var candidates = (
                from order in _craftingOrders.Values
                from material in CraftingRecipeCatalog.Get(order.Recipe).Materials
                let key = (order.Id, material.Resource, material.Variant)
                let missing = order.GetMissing(material) -
                    craftingReservations.GetValueOrDefault(key)
                where missing > 0
                from source in _itemStacks.Values
                where material.Matches(source.Resource, source.Variant) &&
                    source.Location.Kind == ItemLocationKind.StorageZone
                let available = source.Quantity - sourceReservations.GetValueOrDefault(source.Id)
                where available > 0
                let estimatedDistance =
                    ManhattanDistance(actor.Position, source.Location.Position) +
                    ManhattanDistance(source.Location.Position, order.Workshop)
                orderby estimatedDistance, order.Id, source.Id
                select new
                {
                    Order = order,
                    Requirement = material,
                    Source = source,
                    Quantity = Math.Min(available, missing),
                })
            .Take(MaximumConstructionRouteCandidatesPerPlanningTick);
        foreach (var candidate in candidates)
        {
            var routeRequest = RequestActorPath(actor, candidate.Source.Location.Position);
            if (routeRequest.Status == NavigationPathRequestStatus.Pending)
            {
                return true;
            }
            if (routeRequest.Status == NavigationPathRequestStatus.Unreachable ||
                routeRequest.Path is not { } route)
            {
                continue;
            }

            actor.JobKind = ActorJobKind.SupplyCrafting;
            actor.JobStage = ActorJobStage.Collecting;
            actor.SourceStackId = candidate.Source.Id;
            actor.DestinationZoneId = candidate.Order.Id;
            actor.ReservedQuantity = Math.Min(
                Definitions.ActorCarryCapacity,
                candidate.Quantity);
            actor.JobTarget = candidate.Source.Location.Position;
            BeginJobLeg(actor, route, Definitions.HaulHandlingTicks);
            sourceReservations[candidate.Source.Id] = checked(
                sourceReservations.GetValueOrDefault(candidate.Source.Id) +
                actor.ReservedQuantity);
            var reservationKey = (
                candidate.Order.Id,
                candidate.Requirement.Resource,
                candidate.Requirement.Variant);
            craftingReservations[reservationKey] = checked(
                craftingReservations.GetValueOrDefault(reservationKey) +
                actor.ReservedQuantity);
            return true;
        }

        return false;
    }

    private bool TryPlanCarriedCraftingDelivery(
        ActorState actor,
        Dictionary<(EntityId OrderId, ResourceKind Resource, ResourceVariant Variant), int>
            craftingReservations)
    {
        if (!_itemStacks.TryGetValue(actor.CarriedStackId, out var carried))
        {
            return false;
        }

        var candidates = _craftingOrders.Values
            .Select(order => new
            {
                Order = order,
                Requirement = CraftingRecipeCatalog.FindMaterial(
                    order.Recipe,
                    carried.Resource,
                    carried.Variant),
            })
            .Where(candidate => candidate.Requirement is not null &&
                candidate.Order.GetMissing(candidate.Requirement) -
                    craftingReservations.GetValueOrDefault((
                        candidate.Order.Id,
                        candidate.Requirement.Resource,
                        candidate.Requirement.Variant)) >= carried.Quantity)
            .Select(candidate => candidate.Order)
            .OrderBy(order => ManhattanDistance(actor.Position, order.Workshop))
            .ThenBy(order => order.Id)
            .Take(MaximumConstructionRouteCandidatesPerPlanningTick);
        foreach (var order in candidates)
        {
            var routeRequest = RequestWorkshopAccessPath(actor, order.Workshop);
            if (routeRequest.Status == NavigationPathRequestStatus.Pending)
            {
                return true;
            }
            if (routeRequest.Status == NavigationPathRequestStatus.Unreachable ||
                routeRequest.Path is not { } route)
            {
                continue;
            }

            actor.JobKind = ActorJobKind.SupplyCrafting;
            actor.JobStage = ActorJobStage.Delivering;
            actor.SourceStackId = EntityId.None;
            actor.DestinationZoneId = order.Id;
            actor.ReservedQuantity = carried.Quantity;
            actor.JobTarget = route.Count == 0 ? actor.Position : route[^1];
            BeginJobLeg(actor, route, Definitions.HaulHandlingTicks);
            var requirement = CraftingRecipeCatalog.FindMaterial(
                order.Recipe,
                carried.Resource,
                carried.Variant)!;
            var key = (order.Id, requirement.Resource, requirement.Variant);
            craftingReservations[key] = checked(
                craftingReservations.GetValueOrDefault(key) + carried.Quantity);
            return true;
        }

        return false;
    }

    private void UpdateCraftingSupplyJob(ActorState actor)
    {
        if (!_craftingOrders.TryGetValue(actor.DestinationZoneId, out var order) ||
            actor.ReservedQuantity <= 0)
        {
            if (actor.JobStage == ActorJobStage.Delivering)
            {
                DropCarriedStack(actor);
            }
            actor.ClearJob();
            return;
        }

        ItemStackState? material = null;
        if (actor.JobStage == ActorJobStage.Collecting)
        {
            if (actor.CarriedStackId != EntityId.None ||
                !_itemStacks.TryGetValue(actor.SourceStackId, out material) ||
                material.Location.Kind is not (
                    ItemLocationKind.Ground or ItemLocationKind.StorageZone) ||
                material.Quantity < actor.ReservedQuantity ||
                material.Location.Position != actor.JobTarget)
            {
                actor.ClearJob();
                return;
            }
        }
        else if (actor.JobStage == ActorJobStage.Delivering)
        {
            if (!_itemStacks.TryGetValue(actor.CarriedStackId, out material) ||
                material.Location != ItemLocation.CarriedBy(actor.Id))
            {
                DropCarriedStack(actor);
                actor.ClearJob();
                return;
            }
        }
        else
        {
            actor.ClearJob();
            return;
        }

        if (order.GetMissing(material.Resource, material.Variant) < actor.ReservedQuantity)
        {
            if (actor.JobStage == ActorJobStage.Delivering)
            {
                DropCarriedStack(actor);
            }
            actor.ClearJob();
            return;
        }

        if (actor.JobPhase == ActorJobPhase.Traveling)
        {
            AdvanceTravel(actor);
        }
        if (actor.JobKind != ActorJobKind.SupplyCrafting ||
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
            CompleteCraftingCollection(actor, order);
        }
        else
        {
            CompleteCraftingDelivery(actor, order);
        }
    }

    private void CompleteCraftingCollection(ActorState actor, CraftingOrderState order)
    {
        var routeRequest = RequestWorkshopAccessPath(actor, order.Workshop);
        if (routeRequest.Status == NavigationPathRequestStatus.Pending)
        {
            actor.RemainingWorkTicks = 1;
            return;
        }
        if (routeRequest.Status == NavigationPathRequestStatus.Unreachable ||
            routeRequest.Path is not { } route)
        {
            actor.ClearJob();
            return;
        }

        var source = _itemStacks[actor.SourceStackId];
        ItemStackState carried;
        if (source.Quantity == actor.ReservedQuantity)
        {
            carried = source;
        }
        else
        {
            source.Quantity -= actor.ReservedQuantity;
            carried = AllocateItemStack(
                source.Resource,
                actor.ReservedQuantity,
                ItemLocation.CarriedBy(actor.Id),
                source.FoodKind,
                source.Variant);
        }

        MoveItemStack(carried, ItemLocation.CarriedBy(actor.Id));
        actor.CarriedStackId = carried.Id;
        actor.SourceStackId = EntityId.None;
        actor.JobStage = ActorJobStage.Delivering;
        actor.JobTarget = route.Count == 0 ? actor.Position : route[^1];
        BeginJobLeg(actor, route, Definitions.HaulHandlingTicks);
        Publish(SimulationEventKind.ItemPickedUp, actor.Id, carried.Id, carried.Quantity);
    }

    private void CompleteCraftingDelivery(ActorState actor, CraftingOrderState order)
    {
        var carried = _itemStacks[actor.CarriedStackId];
        var delivered = carried.Quantity;
        var resource = carried.Resource;
        var variant = carried.Variant;
        RemoveItemStack(carried.Id);
        actor.CarriedStackId = EntityId.None;
        order.Deliver(resource, variant, delivered);
        GainHaulingExperience(actor, Math.Max(1, delivered * 2));
        Publish(SimulationEventKind.CraftingMaterialDelivered, actor.Id, order.Id, delivered);
        actor.ClearJob();
    }

    private bool TryPlanCraftingWork(ActorState actor)
    {
        var reservedOrders = _actors.Values
            .Where(candidate => candidate.JobKind == ActorJobKind.Craft)
            .Select(candidate => candidate.DestinationZoneId)
            .ToHashSet();
        var candidates = _craftingOrders.Values
            .Where(order => order.HasAllMaterials && !reservedOrders.Contains(order.Id))
            .OrderBy(order => ManhattanDistance(actor.Position, order.Workshop))
            .ThenBy(order => order.Id)
            .Take(MaximumConstructionRouteCandidatesPerPlanningTick);
        foreach (var order in candidates)
        {
            var routeRequest = RequestWorkshopAccessPath(actor, order.Workshop);
            if (routeRequest.Status == NavigationPathRequestStatus.Pending)
            {
                return true;
            }
            if (routeRequest.Status == NavigationPathRequestStatus.Unreachable ||
                routeRequest.Path is not { } route)
            {
                continue;
            }

            actor.JobKind = ActorJobKind.Craft;
            actor.DestinationZoneId = order.Id;
            actor.JobTarget = route.Count == 0 ? actor.Position : route[^1];
            BeginJobLeg(actor, route, order.RemainingWorkTicks);
            return true;
        }

        return false;
    }

    private void UpdateCraftingWorkJob(ActorState actor)
    {
        if (!_craftingOrders.TryGetValue(actor.DestinationZoneId, out var order) ||
            !order.HasAllMaterials ||
            !World.HasWorkshop(
                order.Workshop,
                CraftingRecipeCatalog.Get(order.Recipe).Workshop))
        {
            actor.ClearJob();
            return;
        }

        if (actor.JobPhase == ActorJobPhase.Traveling)
        {
            AdvanceTravel(actor);
        }
        if (actor.JobKind != ActorJobKind.Craft || actor.JobPhase != ActorJobPhase.Working)
        {
            return;
        }

        actor.RemainingWorkTicks--;
        order.RemainingWorkTicks--;
        if (order.RemainingWorkTicks > 0)
        {
            return;
        }

        var output = CraftingRecipeCatalog.Get(order.Recipe).Output;
        AllocateItemStack(
            output.Resource,
            output.Quantity,
            ItemLocation.OnGround(actor.Position),
            variant: output.Variant);
        _craftingOrders.Remove(order.Id);
        GainBuildingExperience(actor, 16);
        Publish(SimulationEventKind.CraftingCompleted, actor.Id, order.Id, (int)order.Recipe);
        actor.ClearJob();
    }

    private static PersonalEquipment GetEquipmentForVariant(ResourceVariant variant) =>
        variant switch
        {
            ResourceVariant.EquipmentPrimitiveSling => PersonalEquipment.PrimitiveSling,
            ResourceVariant.EquipmentBoneKnife => PersonalEquipment.BoneKnife,
            ResourceVariant.EquipmentFightingStick => PersonalEquipment.FightingStick,
            ResourceVariant.EquipmentStoneClub => PersonalEquipment.StoneClub,
            ResourceVariant.EquipmentHideClothes => PersonalEquipment.HideClothes,
            ResourceVariant.EquipmentReedClothes => PersonalEquipment.ReedClothes,
            ResourceVariant.EquipmentPrimitiveWaterskin =>
                PersonalEquipment.PrimitiveWaterskin,
            ResourceVariant.EquipmentWoodenAxe => PersonalEquipment.WoodenAxe,
            ResourceVariant.EquipmentReinforcedPickaxe =>
                PersonalEquipment.ReinforcedPickaxe,
            ResourceVariant.EquipmentWoodenBucket => PersonalEquipment.WoodenBucket,
            _ => PersonalEquipment.None,
        };

    private NavigationPathRequestResult RequestWorkshopAccessPath(
        ActorState actor,
        GridPosition workshop)
    {
        var destinations = World.GetCardinalWorldNeighbors(workshop)
            .Where(World.IsTerrainTraversable)
            .ToHashSet();
        return RequestActorPathToNearest(actor, destinations);
    }

    private void ValidateLoadedCraftingSupplyJob(ActorState actor)
    {
        if (!_craftingOrders.TryGetValue(actor.DestinationZoneId, out var order) ||
            actor.ReservedQuantity <= 0 ||
            actor.JobStage is not (ActorJobStage.Collecting or ActorJobStage.Delivering))
        {
            throw new InvalidDataException("The save contains an invalid crafting-supply job.");
        }
        var stackId = actor.JobStage == ActorJobStage.Collecting
            ? actor.SourceStackId
            : actor.CarriedStackId;
        if (!_itemStacks.TryGetValue(stackId, out var stack) ||
            order.GetMissing(stack.Resource, stack.Variant) < actor.ReservedQuantity)
        {
            throw new InvalidDataException("The save contains invalid crafting material demand.");
        }
    }

    private void ValidateLoadedCraftingWorkJob(ActorState actor)
    {
        if (actor.JobStage != ActorJobStage.None ||
            actor.SourceStackId != EntityId.None || actor.CarriedStackId != EntityId.None ||
            actor.ReservedQuantity != 0 ||
            !_craftingOrders.TryGetValue(actor.DestinationZoneId, out var order) ||
            !order.HasAllMaterials ||
            !World.HasWorkshop(
                order.Workshop,
                CraftingRecipeCatalog.Get(order.Recipe).Workshop))
        {
            throw new InvalidDataException("The save contains an invalid crafting job.");
        }
    }
}
