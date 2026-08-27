using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation;

public sealed partial class SimulationEngine
{
    private Dictionary<(EntityId OrderId, ResourceKind Resource), int>
        CreateCraftingReservations()
    {
        var reservations = new Dictionary<(EntityId, ResourceKind), int>();
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
            if (stack is null)
            {
                continue;
            }

            var key = (actor.DestinationZoneId, stack.Resource);
            reservations[key] = checked(
                reservations.GetValueOrDefault(key) + actor.ReservedQuantity);
        }
        return reservations;
    }

    private bool TryPlanCraftingSupply(
        ActorState actor,
        Dictionary<EntityId, int> sourceReservations,
        Dictionary<(EntityId OrderId, ResourceKind Resource), int> craftingReservations)
    {
        var candidates =
            from order in _craftingOrders.Values
            from material in CraftingRecipeCatalog.GetMaterials(order.Recipe)
            let key = (order.Id, material.Resource)
            let missing = order.GetMissing(material.Resource) -
                craftingReservations.GetValueOrDefault(key)
            where missing > 0
            from source in _itemStacks.Values
            where source.Resource == material.Resource &&
                source.Location.Kind is ItemLocationKind.Ground or ItemLocationKind.StorageZone
            let available = source.Quantity - sourceReservations.GetValueOrDefault(source.Id)
            where available > 0
            let routeToSource = FindActorPath(actor, source.Location.Position)
            let routeToWorkshop = FindWorkshopAccessPath(
                source.Location.Position,
                order.Workshop,
                actor)
            where routeToSource is not null && routeToWorkshop is not null
            orderby routeToSource.Count + routeToWorkshop.Count, order.Id, source.Id
            select new
            {
                Order = order,
                Source = source,
                Route = routeToSource,
                Quantity = Math.Min(available, missing),
            };
        var best = candidates.FirstOrDefault();
        if (best is null)
        {
            return false;
        }

        actor.JobKind = ActorJobKind.SupplyCrafting;
        actor.JobStage = ActorJobStage.Collecting;
        actor.SourceStackId = best.Source.Id;
        actor.DestinationZoneId = best.Order.Id;
        actor.ReservedQuantity = Math.Min(Definitions.ActorCarryCapacity, best.Quantity);
        actor.JobTarget = best.Source.Location.Position;
        BeginJobLeg(actor, best.Route, Definitions.HaulHandlingTicks);
        sourceReservations[best.Source.Id] = checked(
            sourceReservations.GetValueOrDefault(best.Source.Id) + actor.ReservedQuantity);
        var reservationKey = (best.Order.Id, best.Source.Resource);
        craftingReservations[reservationKey] = checked(
            craftingReservations.GetValueOrDefault(reservationKey) + actor.ReservedQuantity);
        return true;
    }

    private bool TryPlanCarriedCraftingDelivery(
        ActorState actor,
        Dictionary<(EntityId OrderId, ResourceKind Resource), int> craftingReservations)
    {
        if (!_itemStacks.TryGetValue(actor.CarriedStackId, out var carried))
        {
            return false;
        }

        var best = _craftingOrders.Values
            .Where(order => order.GetMissing(carried.Resource) -
                craftingReservations.GetValueOrDefault((order.Id, carried.Resource)) >=
                    carried.Quantity)
            .Select(order => new
            {
                Order = order,
                Route = FindWorkshopAccessPath(actor.Position, order.Workshop, actor),
            })
            .Where(candidate => candidate.Route is not null)
            .OrderBy(candidate => candidate.Route!.Count)
            .ThenBy(candidate => candidate.Order.Id)
            .FirstOrDefault();
        if (best is null)
        {
            return false;
        }

        actor.JobKind = ActorJobKind.SupplyCrafting;
        actor.JobStage = ActorJobStage.Delivering;
        actor.SourceStackId = EntityId.None;
        actor.DestinationZoneId = best.Order.Id;
        actor.ReservedQuantity = carried.Quantity;
        actor.JobTarget = best.Route!.Count == 0 ? actor.Position : best.Route[^1];
        BeginJobLeg(actor, best.Route, Definitions.HaulHandlingTicks);
        var key = (best.Order.Id, carried.Resource);
        craftingReservations[key] = checked(
            craftingReservations.GetValueOrDefault(key) + carried.Quantity);
        return true;
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

        if (order.GetMissing(material.Resource) < actor.ReservedQuantity)
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
        var route = FindWorkshopAccessPath(actor.Position, order.Workshop, actor);
        if (route is null)
        {
            DropCarriedStack(actor);
            actor.ClearJob();
            return;
        }
        actor.JobTarget = route.Count == 0 ? actor.Position : route[^1];
        BeginJobLeg(actor, route, Definitions.HaulHandlingTicks);
        Publish(SimulationEventKind.ItemPickedUp, actor.Id, carried.Id, carried.Quantity);
    }

    private void CompleteCraftingDelivery(ActorState actor, CraftingOrderState order)
    {
        var carried = _itemStacks[actor.CarriedStackId];
        var delivered = carried.Quantity;
        var resource = carried.Resource;
        RemoveItemStack(carried.Id);
        actor.CarriedStackId = EntityId.None;
        order.Deliver(resource, delivered);
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
        var best = _craftingOrders.Values
            .Where(order => order.HasAllMaterials &&
                !reservedOrders.Contains(order.Id))
            .Select(order => new
            {
                Order = order,
                Route = FindWorkshopAccessPath(actor.Position, order.Workshop, actor),
            })
            .Where(candidate => candidate.Route is not null)
            .OrderBy(candidate => candidate.Route!.Count)
            .ThenBy(candidate => candidate.Order.Id)
            .FirstOrDefault();
        if (best is null)
        {
            return false;
        }

        actor.JobKind = ActorJobKind.Craft;
        actor.DestinationZoneId = best.Order.Id;
        actor.JobTarget = best.Route!.Count == 0 ? actor.Position : best.Route[^1];
        BeginJobLeg(actor, best.Route, best.Order.RemainingWorkTicks);
        return true;
    }

    private void UpdateCraftingWorkJob(ActorState actor)
    {
        if (!_craftingOrders.TryGetValue(actor.DestinationZoneId, out var order) ||
            !order.HasAllMaterials || !World.HasPrimitiveWorkshop(order.Workshop))
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

        actor.Equipment |= GetCraftedEquipment(order.Recipe);
        _craftingOrders.Remove(order.Id);
        GainBuildingExperience(actor, 16);
        Publish(SimulationEventKind.CraftingCompleted, actor.Id, order.Id, (int)order.Recipe);
        actor.ClearJob();
    }

    private static PersonalEquipment GetCraftedEquipment(CraftingRecipeKind recipe) => recipe switch
    {
        CraftingRecipeKind.PrimitiveSling => PersonalEquipment.PrimitiveSling,
        CraftingRecipeKind.BoneKnife => PersonalEquipment.BoneKnife,
        CraftingRecipeKind.FightingStick => PersonalEquipment.FightingStick,
        CraftingRecipeKind.StoneClub => PersonalEquipment.StoneClub,
        CraftingRecipeKind.HideClothes => PersonalEquipment.HideClothes,
        CraftingRecipeKind.ReedClothes => PersonalEquipment.ReedClothes,
        _ => throw new ArgumentOutOfRangeException(nameof(recipe), recipe, null),
    };

    private IReadOnlyList<GridPosition>? FindWorkshopAccessPath(
        GridPosition start,
        GridPosition workshop,
        ActorState? actor = null) => World.GetCardinalWorldNeighbors(workshop)
        .Where(World.IsTerrainTraversable)
        .Select(position => actor is null
            ? Navigation.FindPath(start, position)
            : FindActorPathFrom(actor, start, position))
        .Where(route => route is not null)
        .OrderBy(route => route!.Count)
        .FirstOrDefault();

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
            order.GetMissing(stack.Resource) < actor.ReservedQuantity)
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
            !order.HasAllMaterials)
        {
            throw new InvalidDataException("The save contains an invalid crafting job.");
        }
    }
}
