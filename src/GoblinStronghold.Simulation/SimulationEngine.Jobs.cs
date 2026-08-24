using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation;

public sealed partial class SimulationEngine
{
    private void UpdateActorJobs()
    {
        var reservedForageTargets = _actors.Values
            .Where(actor => actor.JobKind == ActorJobKind.Forage)
            .Select(actor => actor.JobTarget)
            .ToHashSet();
        var reservedSourceQuantities = CreateHaulReservations(sourceReservations: true);
        var reservedDestinationQuantities = CreateHaulReservations(sourceReservations: false);
        var activeExplorers = _actors.Values.Count(actor => actor.JobKind == ActorJobKind.Explore);

        foreach (var actor in _actors.Values)
        {
            TryInterruptForWater(
                actor,
                reservedForageTargets,
                reservedSourceQuantities,
                reservedDestinationQuantities);
            TryInterruptForCriticalHunger(
                actor,
                reservedSourceQuantities,
                reservedDestinationQuantities);

            if (actor.JobKind == ActorJobKind.None)
            {
                var needsFood = actor.Hunger >= Definitions.FoodSeekThreshold;
                var needsWater = actor.Thirst >= Definitions.DrinkThreshold && actor.PersonalWater == 0;
                if (needsWater && TryPlanWaterResupply(actor))
                {
                    // Water outranks cargo and ordinary work once the carried supply is empty.
                }
                else if (actor.CarriedStackId != EntityId.None)
                {
                    TryPlanHaulDelivery(actor, reservedDestinationQuantities);
                }
                else if (needsFood && TryPlanEatJob(actor, reservedSourceQuantities))
                {
                    // A reachable meal outranks fatigue and settlement work.
                }
                else if (needsFood && TryPlanForageJob(actor, reservedForageTargets))
                {
                    // With no prepared food, gathering becomes survival work.
                }
                else if (actor.Fatigue >= Definitions.RestThreshold && TryPlanRestJob(actor))
                {
                    // Survival work outranks gathering once the current job has ended.
                }
                else if (TryPlanHaulCollection(
                             actor,
                             reservedSourceQuantities,
                             reservedDestinationQuantities))
                {
                    // Exposed stock is secured before routine gathering.
                }
                else if (TryPlanFoodResupply(actor, reservedSourceQuantities))
                {
                    // A small carried ration avoids a trip home for every meal.
                }
                else if (TryPlanWaterResupply(actor))
                {
                    // Primitive containers are refilled at accessible shallow water.
                }
                else if (activeExplorers < Definitions.MaximumExplorers &&
                         TryPlanExploreJob(actor))
                {
                    activeExplorers++;
                }
                else
                {
                    TryPlanForageJob(actor, reservedForageTargets);
                }
            }

            switch (actor.JobKind)
            {
                case ActorJobKind.Forage:
                    UpdateForageJob(actor);
                    break;
                case ActorJobKind.Haul:
                    UpdateHaulJob(actor);
                    break;
                case ActorJobKind.Rest:
                    UpdateRestJob(actor);
                    break;
                case ActorJobKind.Eat:
                    UpdateEatJob(actor);
                    break;
                case ActorJobKind.Explore:
                    UpdateExploreJob(actor);
                    break;
                case ActorJobKind.Move:
                    UpdateMoveJob(actor);
                    break;
                case ActorJobKind.Resupply:
                    UpdateResupplyJob(actor);
                    break;
            }
        }
    }

    private bool TryPlanExploreJob(ActorState actor)
    {
        if (Definitions.MaximumExplorers == 0)
        {
            return false;
        }

        var visited = new HashSet<GridPosition> { actor.Position };
        var predecessors = new Dictionary<GridPosition, GridPosition>();
        var queue = new Queue<GridPosition>();
        queue.Enqueue(actor.Position);

        while (queue.TryDequeue(out var current))
        {
            if (Visibility.Get(current) == CellVisibility.Unknown)
            {
                var route = new List<GridPosition>();
                while (current != actor.Position)
                {
                    route.Add(current);
                    current = predecessors[current];
                }

                route.Reverse();
                actor.JobKind = ActorJobKind.Explore;
                actor.JobTarget = route[^1];
                actor.JobPhase = ActorJobPhase.Traveling;
                actor.RemainingRoute.AddRange(route);
                return true;
            }

            foreach (var neighbor in Map.GetCardinalNeighbors(current))
            {
                if (!visited.Add(neighbor) || !World.IsSurfaceTraversable(neighbor))
                {
                    continue;
                }

                predecessors[neighbor] = current;
                queue.Enqueue(neighbor);
            }
        }

        return false;
    }

    private void UpdateExploreJob(ActorState actor)
    {
        if (Visibility.Get(actor.JobTarget) != CellVisibility.Unknown)
        {
            actor.ClearJob();
            return;
        }

        if (actor.JobPhase == ActorJobPhase.Traveling)
        {
            AdvanceTravel(actor);
        }
    }

    private void UpdateMoveJob(ActorState actor)
    {
        if (actor.JobPhase == ActorJobPhase.Traveling)
        {
            AdvanceTravel(actor);
        }
    }

    private void TryInterruptForCriticalHunger(
        ActorState actor,
        Dictionary<EntityId, int> itemReservations,
        Dictionary<EntityId, int> destinationReservations)
    {
        if (actor.Hunger < Definitions.CriticalHungerThreshold ||
            actor.CarriedStackId != EntityId.None ||
            (actor.JobKind != ActorJobKind.Rest &&
             actor.JobKind != ActorJobKind.Move &&
             (actor.JobKind != ActorJobKind.Haul || actor.JobStage != ActorJobStage.Collecting)))
        {
            return;
        }

        var releasedSourceQuantity = actor.JobKind == ActorJobKind.Haul
            ? actor.ReservedQuantity
            : 0;
        var hasReachableMeal = _itemStacks.Values.Any(stack =>
            stack.Resource == ResourceKind.Food &&
            stack.Location.Kind is ItemLocationKind.Ground or ItemLocationKind.StorageZone &&
            stack.Quantity - itemReservations.GetValueOrDefault(stack.Id) +
                (stack.Id == actor.SourceStackId ? releasedSourceQuantity : 0) > 0 &&
            World.HasSurfacePath(actor.Position, stack.Location.Position));
        if (!hasReachableMeal)
        {
            return;
        }

        if (actor.JobKind == ActorJobKind.Haul)
        {
            ReduceReservation(itemReservations, actor.SourceStackId, actor.ReservedQuantity);
            ReduceReservation(
                destinationReservations,
                actor.DestinationZoneId,
                actor.ReservedQuantity);
        }

        actor.ClearJob();
    }

    private void TryInterruptForWater(
        ActorState actor,
        ISet<GridPosition> forageTargets,
        Dictionary<EntityId, int> itemReservations,
        Dictionary<EntityId, int> destinationReservations)
    {
        if (actor.Thirst < Definitions.DrinkThreshold ||
            actor.PersonalWater > 0 ||
            (actor.JobKind == ActorJobKind.Resupply &&
             actor.JobStage == ActorJobStage.ProvisioningWater) ||
            FindNearestShallowWaterPath(actor.Position) is null)
        {
            return;
        }

        if (actor.JobKind == ActorJobKind.Forage)
        {
            forageTargets.Remove(actor.JobTarget);
        }
        else if (actor.JobKind == ActorJobKind.Eat ||
                 (actor.JobKind == ActorJobKind.Resupply &&
                  actor.JobStage == ActorJobStage.ProvisioningFood))
        {
            ReduceReservation(itemReservations, actor.SourceStackId, actor.ReservedQuantity);
        }
        else if (actor.JobKind == ActorJobKind.Haul)
        {
            if (actor.JobStage == ActorJobStage.Collecting)
            {
                ReduceReservation(itemReservations, actor.SourceStackId, actor.ReservedQuantity);
            }

            ReduceReservation(
                destinationReservations,
                actor.DestinationZoneId,
                actor.ReservedQuantity);
        }

        actor.ClearJob();
    }

    private static void ReduceReservation(
        IDictionary<EntityId, int> reservations,
        EntityId id,
        int quantity)
    {
        var remaining = reservations[id] - quantity;
        if (remaining == 0)
        {
            reservations.Remove(id);
        }
        else
        {
            reservations[id] = remaining;
        }
    }

    private bool TryPlanRestJob(ActorState actor)
    {
        var best = World.CreateWorldObjectSnapshot()
            .Where(worldObject =>
                worldObject.Kind == WorldObjectKind.GoblinHut &&
                worldObject.Owner == WorldObjectOwner.GoblinTribe)
            .SelectMany(worldObject => worldObject.GetAbsoluteParts())
            .Where(item =>
                item.Position.Z == 0 &&
                item.Part.Kind is WorldObjectPartKind.Floor or WorldObjectPartKind.Door &&
                World.IsSurfaceTraversable(item.Position))
            .Select(item => item.Position)
            .Distinct()
            .Select(position => new
            {
                Position = position,
                Route = World.FindSurfacePath(actor.Position, position),
            })
            .Where(candidate => candidate.Route is not null)
            .OrderBy(candidate => candidate.Route!.Count)
            .ThenBy(candidate => candidate.Position.Y)
            .ThenBy(candidate => candidate.Position.X)
            .FirstOrDefault();
        if (best is null)
        {
            return false;
        }

        actor.JobKind = ActorJobKind.Rest;
        actor.JobTarget = best.Position;
        BeginJobLeg(actor, best.Route!, GetRestWorkTicks(actor));
        return true;
    }

    private void UpdateRestJob(ActorState actor)
    {
        if (actor.CarriedStackId != EntityId.None || !IsRestLocation(actor.JobTarget))
        {
            actor.ClearJob();
            return;
        }

        if (actor.JobPhase == ActorJobPhase.Traveling)
        {
            AdvanceTravel(actor);
        }

        if (actor.JobKind != ActorJobKind.Rest || actor.JobPhase != ActorJobPhase.Working)
        {
            return;
        }

        actor.Fatigue = Math.Max(0, actor.Fatigue - Definitions.RestRecoveryPerTick);
        actor.RemainingWorkTicks = GetRestWorkTicks(actor);
        if (actor.Fatigue == 0)
        {
            actor.ClearJob();
        }
    }

    private Dictionary<EntityId, int> CreateHaulReservations(bool sourceReservations)
    {
        var reservations = new Dictionary<EntityId, int>();
        foreach (var actor in _actors.Values)
        {
            if (sourceReservations &&
                (actor.JobKind == ActorJobKind.Eat ||
                 (actor.JobKind == ActorJobKind.Resupply &&
                  actor.JobStage == ActorJobStage.ProvisioningFood)))
            {
                reservations[actor.SourceStackId] = checked(
                    reservations.GetValueOrDefault(actor.SourceStackId) + actor.ReservedQuantity);
                continue;
            }

            if (actor.JobKind != ActorJobKind.Haul)
            {
                continue;
            }

            var id = sourceReservations ? actor.SourceStackId : actor.DestinationZoneId;
            if (id == EntityId.None ||
                (sourceReservations && actor.JobStage != ActorJobStage.Collecting))
            {
                continue;
            }

            reservations[id] = checked(reservations.GetValueOrDefault(id) + actor.ReservedQuantity);
        }

        return reservations;
    }

    private bool TryPlanFoodResupply(
        ActorState actor,
        Dictionary<EntityId, int> itemReservations)
    {
        if (actor.PersonalFood >= Definitions.PersonalFoodCapacity)
        {
            return false;
        }

        var best = _itemStacks.Values
            .Where(stack =>
                stack.Resource == ResourceKind.Food &&
                stack.Location.Kind is ItemLocationKind.Ground or ItemLocationKind.StorageZone &&
                stack.Quantity - itemReservations.GetValueOrDefault(stack.Id) > 0)
            .Select(stack => new
            {
                Stack = stack,
                Route = World.FindSurfacePath(actor.Position, stack.Location.Position),
            })
            .Where(candidate => candidate.Route is not null)
            .OrderBy(candidate => candidate.Route!.Count)
            .ThenBy(candidate => candidate.Stack.Id)
            .FirstOrDefault();
        if (best is null)
        {
            return false;
        }

        actor.JobKind = ActorJobKind.Resupply;
        actor.JobStage = ActorJobStage.ProvisioningFood;
        actor.SourceStackId = best.Stack.Id;
        actor.ReservedQuantity = 1;
        actor.JobTarget = best.Stack.Location.Position;
        BeginJobLeg(actor, best.Route!, Definitions.ResupplyWorkTicks);
        itemReservations[best.Stack.Id] = checked(
            itemReservations.GetValueOrDefault(best.Stack.Id) + 1);
        return true;
    }

    private bool TryPlanWaterResupply(ActorState actor)
    {
        if (actor.PersonalWater >= Definitions.PersonalWaterCapacity)
        {
            return false;
        }

        var route = FindNearestShallowWaterPath(actor.Position);
        if (route is null)
        {
            return false;
        }

        actor.JobKind = ActorJobKind.Resupply;
        actor.JobStage = ActorJobStage.ProvisioningWater;
        actor.ReservedQuantity = 1;
        actor.JobTarget = route.Count == 0 ? actor.Position : route[^1];
        BeginJobLeg(actor, route, Definitions.ResupplyWorkTicks);
        return true;
    }

    private IReadOnlyList<GridPosition>? FindNearestShallowWaterPath(GridPosition start)
    {
        var visited = new HashSet<GridPosition> { start };
        var predecessors = new Dictionary<GridPosition, GridPosition>();
        var queue = new Queue<GridPosition>();
        queue.Enqueue(start);
        while (queue.TryDequeue(out var current))
        {
            if (Map.GetCell(current).Terrain == TerrainKind.ShallowWater)
            {
                var route = new List<GridPosition>();
                while (current != start)
                {
                    route.Add(current);
                    current = predecessors[current];
                }

                route.Reverse();
                return route;
            }

            foreach (var neighbor in Map.GetCardinalNeighbors(current))
            {
                if (visited.Add(neighbor) && World.IsSurfaceTraversable(neighbor))
                {
                    predecessors[neighbor] = current;
                    queue.Enqueue(neighbor);
                }
            }
        }

        return null;
    }

    private void UpdateResupplyJob(ActorState actor)
    {
        if (!IsResupplyJobValid(actor))
        {
            actor.ClearJob();
            return;
        }

        if (actor.JobPhase == ActorJobPhase.Traveling)
        {
            AdvanceTravel(actor);
        }

        if (actor.JobKind != ActorJobKind.Resupply || actor.JobPhase != ActorJobPhase.Working)
        {
            return;
        }

        actor.RemainingWorkTicks--;
        if (actor.RemainingWorkTicks == 0)
        {
            CompleteResupply(actor);
        }
    }

    private bool IsResupplyJobValid(ActorState actor) => actor.JobStage switch
    {
        ActorJobStage.ProvisioningFood =>
            actor.CarriedStackId == EntityId.None &&
            actor.PersonalFood < Definitions.PersonalFoodCapacity &&
            _itemStacks.TryGetValue(actor.SourceStackId, out var food) &&
            food.Resource == ResourceKind.Food &&
            food.Location.Kind is ItemLocationKind.Ground or ItemLocationKind.StorageZone &&
            food.Location.Position == actor.JobTarget &&
            food.Quantity >= actor.ReservedQuantity,
        ActorJobStage.ProvisioningWater =>
            actor.PersonalWater < Definitions.PersonalWaterCapacity &&
            actor.SourceStackId == EntityId.None &&
            Map.GetCell(actor.JobTarget).Terrain == TerrainKind.ShallowWater,
        _ => false,
    };

    private void CompleteResupply(ActorState actor)
    {
        if (actor.JobStage == ActorJobStage.ProvisioningFood)
        {
            var food = _itemStacks[actor.SourceStackId];
            food.Quantity--;
            actor.PersonalFood++;
            Publish(SimulationEventKind.ActorProvisionedFood, actor.Id, food.Id, 1);
            if (food.Quantity == 0)
            {
                _itemStacks.Remove(food.Id);
                Publish(SimulationEventKind.ItemStackDepleted, actor.Id, food.Id, 0);
            }
        }
        else
        {
            actor.PersonalWater++;
            Publish(SimulationEventKind.ActorCollectedWater, actor.Id, EntityId.None, 1);
        }

        actor.ClearJob();
    }

    private bool TryPlanEatJob(
        ActorState actor,
        Dictionary<EntityId, int> itemReservations)
    {
        var best = _itemStacks.Values
            .Where(stack =>
                stack.Resource == ResourceKind.Food &&
                stack.Location.Kind is ItemLocationKind.Ground or ItemLocationKind.StorageZone &&
                stack.Quantity - itemReservations.GetValueOrDefault(stack.Id) > 0)
            .Select(stack => new
            {
                Stack = stack,
                Route = World.FindSurfacePath(actor.Position, stack.Location.Position),
            })
            .Where(candidate => candidate.Route is not null)
            .OrderBy(candidate => candidate.Route!.Count)
            .ThenBy(candidate => candidate.Stack.Id)
            .FirstOrDefault();
        if (best is null)
        {
            return false;
        }

        actor.JobKind = ActorJobKind.Eat;
        actor.SourceStackId = best.Stack.Id;
        actor.ReservedQuantity = 1;
        actor.JobTarget = best.Stack.Location.Position;
        BeginJobLeg(actor, best.Route!, Definitions.EatWorkTicks);
        itemReservations[best.Stack.Id] = checked(
            itemReservations.GetValueOrDefault(best.Stack.Id) + 1);
        return true;
    }

    private void UpdateEatJob(ActorState actor)
    {
        if (actor.CarriedStackId != EntityId.None ||
            !_itemStacks.TryGetValue(actor.SourceStackId, out var food) ||
            food.Resource != ResourceKind.Food ||
            food.Location.Kind is not (ItemLocationKind.Ground or ItemLocationKind.StorageZone) ||
            food.Location.Position != actor.JobTarget ||
            food.Quantity < actor.ReservedQuantity)
        {
            actor.ClearJob();
            return;
        }

        if (actor.JobPhase == ActorJobPhase.Traveling)
        {
            AdvanceTravel(actor);
        }

        if (actor.JobKind != ActorJobKind.Eat || actor.JobPhase != ActorJobPhase.Working)
        {
            return;
        }

        actor.RemainingWorkTicks--;
        if (actor.RemainingWorkTicks == 0)
        {
            CompleteMeal(actor, food);
        }
    }

    private void CompleteMeal(ActorState actor, ItemStackState food)
    {
        food.Quantity--;
        actor.Hunger = Math.Max(0, actor.Hunger - Definitions.FoodNutrition);
        Publish(SimulationEventKind.ActorAte, actor.Id, food.Id, 1);
        if (food.Quantity == 0)
        {
            _itemStacks.Remove(food.Id);
            Publish(SimulationEventKind.ItemStackDepleted, actor.Id, food.Id, 0);
        }

        actor.ClearJob();
    }

    private void UpdateForageJob(ActorState actor)
    {
        if (actor.CarriedStackId != EntityId.None)
        {
            actor.ClearJob();
            return;
        }

        var patch = World.GetPlantPatch(actor.JobTarget);
        if (patch is null || patch.Value.Biomass == 0)
        {
            actor.ClearJob();
            return;
        }

        if (actor.JobPhase == ActorJobPhase.Traveling)
        {
            AdvanceTravel(actor);
        }

        if (actor.JobPhase == ActorJobPhase.Working)
        {
            AdvanceForageWork(actor);
        }
    }

    private bool TryPlanForageJob(ActorState actor, ISet<GridPosition> reservedTargets)
    {
        var route = World.FindNearestHarvestablePlantPath(actor.Position, reservedTargets);
        if (route is null)
        {
            return false;
        }

        actor.JobKind = ActorJobKind.Forage;
        actor.JobTarget = route.Count == 0 ? actor.Position : route[^1];
        BeginJobLeg(actor, route, Definitions.ForageWorkTicks);
        reservedTargets.Add(actor.JobTarget);
        return true;
    }

    private bool TryPlanHaulCollection(
        ActorState actor,
        Dictionary<EntityId, int> sourceReservations,
        Dictionary<EntityId, int> destinationReservations)
    {
        HaulPlan? best = null;
        foreach (var source in _itemStacks.Values.Where(stack =>
                     stack.Location.Kind == ItemLocationKind.Ground))
        {
            var availableSource = source.Quantity - sourceReservations.GetValueOrDefault(source.Id);
            if (availableSource <= 0)
            {
                continue;
            }

            var routeToSource = World.FindSurfacePath(actor.Position, source.Location.Position);
            if (routeToSource is null)
            {
                continue;
            }

            foreach (var zone in _storageZones.Values.Where(zone => ZoneAccepts(zone, source.Resource)))
            {
                var availableDestination = zone.Capacity -
                    GetStoredQuantity(zone.Id) -
                    destinationReservations.GetValueOrDefault(zone.Id);
                if (availableDestination <= 0)
                {
                    continue;
                }

                var routeToDestination = World.FindSurfacePath(source.Location.Position, zone.Position);
                if (routeToDestination is null)
                {
                    continue;
                }

                var quantity = Math.Min(
                    Definitions.ActorCarryCapacity,
                    Math.Min(availableSource, availableDestination));
                var candidate = new HaulPlan(
                    source.Id,
                    zone.Id,
                    quantity,
                    routeToSource,
                    checked(routeToSource.Count + routeToDestination.Count));
                if (best is null || IsBetter(candidate, best.Value))
                {
                    best = candidate;
                }
            }
        }

        if (best is null)
        {
            return false;
        }

        var plan = best.Value;
        actor.JobKind = ActorJobKind.Haul;
        actor.JobStage = ActorJobStage.Collecting;
        actor.SourceStackId = plan.SourceStackId;
        actor.DestinationZoneId = plan.DestinationZoneId;
        actor.ReservedQuantity = plan.Quantity;
        actor.JobTarget = _itemStacks[plan.SourceStackId].Location.Position;
        BeginJobLeg(actor, plan.Route, Definitions.HaulHandlingTicks);
        sourceReservations[plan.SourceStackId] = checked(
            sourceReservations.GetValueOrDefault(plan.SourceStackId) + plan.Quantity);
        destinationReservations[plan.DestinationZoneId] = checked(
            destinationReservations.GetValueOrDefault(plan.DestinationZoneId) + plan.Quantity);
        return true;
    }

    private bool TryPlanHaulDelivery(
        ActorState actor,
        Dictionary<EntityId, int> destinationReservations)
    {
        if (!_itemStacks.TryGetValue(actor.CarriedStackId, out var carried))
        {
            return false;
        }

        var best = _storageZones.Values
            .Where(zone =>
                ZoneAccepts(zone, carried.Resource) &&
                zone.Capacity - GetStoredQuantity(zone.Id) -
                    destinationReservations.GetValueOrDefault(zone.Id) >= carried.Quantity)
            .Select(zone => new
            {
                Zone = zone,
                Route = World.FindSurfacePath(actor.Position, zone.Position),
            })
            .Where(candidate => candidate.Route is not null)
            .OrderBy(candidate => candidate.Route!.Count)
            .ThenBy(candidate => candidate.Zone.Id)
            .FirstOrDefault();
        if (best is null)
        {
            return false;
        }

        actor.JobKind = ActorJobKind.Haul;
        actor.JobStage = ActorJobStage.Delivering;
        actor.DestinationZoneId = best.Zone.Id;
        actor.ReservedQuantity = carried.Quantity;
        actor.JobTarget = best.Zone.Position;
        BeginJobLeg(actor, best.Route!, Definitions.HaulHandlingTicks);
        destinationReservations[best.Zone.Id] = checked(
            destinationReservations.GetValueOrDefault(best.Zone.Id) + carried.Quantity);
        return true;
    }

    private void UpdateHaulJob(ActorState actor)
    {
        if (!IsHaulJobStillValid(actor))
        {
            actor.ClearJob();
            return;
        }

        if (actor.JobPhase == ActorJobPhase.Traveling)
        {
            AdvanceTravel(actor);
        }

        if (actor.JobKind == ActorJobKind.Haul && actor.JobPhase == ActorJobPhase.Working)
        {
            actor.RemainingWorkTicks--;
            if (actor.RemainingWorkTicks == 0)
            {
                if (actor.JobStage == ActorJobStage.Collecting)
                {
                    CompleteHaulCollection(actor);
                }
                else
                {
                    CompleteHaulDelivery(actor);
                }
            }
        }
    }

    private bool IsHaulJobStillValid(ActorState actor)
    {
        if (!_storageZones.TryGetValue(actor.DestinationZoneId, out var zone) ||
            actor.ReservedQuantity <= 0)
        {
            return false;
        }

        if (actor.JobStage == ActorJobStage.Collecting)
        {
            return actor.CarriedStackId == EntityId.None &&
                _itemStacks.TryGetValue(actor.SourceStackId, out var source) &&
                source.Location.Kind == ItemLocationKind.Ground &&
                source.Quantity >= actor.ReservedQuantity &&
                ZoneAccepts(zone, source.Resource);
        }

        return actor.JobStage == ActorJobStage.Delivering &&
            actor.SourceStackId == EntityId.None &&
            actor.CarriedStackId != EntityId.None &&
            _itemStacks.TryGetValue(actor.CarriedStackId, out var carried) &&
            carried.Location == ItemLocation.CarriedBy(actor.Id) &&
            carried.Quantity == actor.ReservedQuantity &&
            ZoneAccepts(zone, carried.Resource);
    }

    private void CompleteHaulCollection(ActorState actor)
    {
        var source = _itemStacks[actor.SourceStackId];
        ItemStackState carried;
        if (source.Quantity == actor.ReservedQuantity)
        {
            carried = source;
            carried.Location = ItemLocation.CarriedBy(actor.Id);
        }
        else
        {
            source.Quantity -= actor.ReservedQuantity;
            carried = AllocateItemStack(
                source.Resource,
                actor.ReservedQuantity,
                ItemLocation.CarriedBy(actor.Id));
        }

        actor.CarriedStackId = carried.Id;
        actor.SourceStackId = EntityId.None;
        actor.JobStage = ActorJobStage.Delivering;
        var destination = _storageZones[actor.DestinationZoneId];
        var route = World.FindSurfacePath(actor.Position, destination.Position);
        if (route is null)
        {
            actor.ClearJob();
            return;
        }

        actor.JobTarget = destination.Position;
        BeginJobLeg(actor, route, Definitions.HaulHandlingTicks);
        Publish(SimulationEventKind.ItemPickedUp, actor.Id, carried.Id, carried.Quantity);
    }

    private void CompleteHaulDelivery(ActorState actor)
    {
        if (!_itemStacks.TryGetValue(actor.CarriedStackId, out var carried) ||
            !_storageZones.TryGetValue(actor.DestinationZoneId, out var zone) ||
            GetStoredQuantity(zone.Id) + carried.Quantity > zone.Capacity)
        {
            actor.ClearJob();
            return;
        }

        actor.CarriedStackId = EntityId.None;
        carried.Location = ItemLocation.StoredIn(zone.Id, zone.Position);
        Publish(SimulationEventKind.ItemStored, actor.Id, carried.Id, carried.Quantity);
        actor.ClearJob();
    }

    private void BeginJobLeg(
        ActorState actor,
        IReadOnlyList<GridPosition> route,
        int workTicks)
    {
        actor.RemainingRoute.Clear();
        actor.RemainingRoute.AddRange(route);
        actor.RemainingWorkTicks = 0;
        if (route.Count == 0)
        {
            actor.JobPhase = ActorJobPhase.Working;
            actor.RemainingWorkTicks = workTicks;
        }
        else
        {
            actor.JobPhase = ActorJobPhase.Traveling;
        }
    }

    private void AdvanceTravel(ActorState actor)
    {
        if (CurrentTick.Value % Definitions.ActorMovementIntervalTicks != 0)
        {
            return;
        }

        if (actor.RemainingRoute.Count == 0)
        {
            actor.ClearJob();
            return;
        }

        var next = actor.RemainingRoute[0];
        if (!AreCardinalNeighbors(actor.Position, next) || !World.IsSurfaceTraversable(next))
        {
            actor.ClearJob();
            return;
        }

        actor.Position = next;
        actor.RemainingRoute.RemoveAt(0);
        if (actor.RemainingRoute.Count == 0)
        {
            if (actor.JobKind is ActorJobKind.Explore or ActorJobKind.Move)
            {
                if (actor.JobKind == ActorJobKind.Move)
                {
                    Publish(SimulationEventKind.MoveCompleted, actor.Id, EntityId.None, 0);
                }

                actor.ClearJob();
            }
            else
            {
                actor.JobPhase = ActorJobPhase.Working;
                actor.RemainingWorkTicks = GetJobWorkTicks(actor);
            }
        }
    }

    private void AdvanceForageWork(ActorState actor)
    {
        actor.RemainingWorkTicks--;
        if (actor.RemainingWorkTicks > 0)
        {
            return;
        }

        var randomYield = DeterministicRandom.NextInt(
            WorldSeed,
            RandomDomain.Foraging,
            actor.Id,
            CurrentTick,
            sampleKey: 0,
            minimumInclusive: 0,
            maximumExclusive: Definitions.ForageVariance + 1);
        var gathered = Definitions.BaseForageYield + randomYield;
        if (World.TryHarvest(
                actor.Position,
                gathered,
                CurrentTick,
                out gathered,
                out var worldChange))
        {
            _undeliveredWorldChanges.Add(worldChange);
            var stack = FindMergeableGroundStack(ResourceKind.Food, actor.Position)
                ?? AllocateItemStack(
                    ResourceKind.Food,
                    quantity: 0,
                    ItemLocation.OnGround(actor.Position));
            stack.Quantity = checked(stack.Quantity + gathered);
            Publish(SimulationEventKind.FoodGathered, actor.Id, stack.Id, gathered);
        }

        actor.ClearJob();
    }

    private void ValidateLoadedJob(ActorState actor)
    {
        if (!Enum.IsDefined(actor.JobKind) ||
            !Enum.IsDefined(actor.JobPhase) ||
            !Enum.IsDefined(actor.JobStage))
        {
            throw new InvalidDataException("The save contains an invalid actor job.");
        }

        if (actor.JobKind == ActorJobKind.None)
        {
            if (actor.JobPhase != ActorJobPhase.None ||
                actor.JobStage != ActorJobStage.None ||
                actor.RemainingWorkTicks != 0 ||
                actor.RemainingRoute.Count != 0 ||
                actor.JobTarget != default ||
                actor.SourceStackId != EntityId.None ||
                actor.DestinationZoneId != EntityId.None ||
                actor.ReservedQuantity != 0)
            {
                throw new InvalidDataException("The save contains inconsistent idle actor state.");
            }

            return;
        }

        if (!World.IsSurfaceTraversable(actor.JobTarget))
        {
            throw new InvalidDataException("The save contains an invalid job target.");
        }

        switch (actor.JobKind)
        {
            case ActorJobKind.Forage:
                ValidateLoadedForageJob(actor);
                break;
            case ActorJobKind.Haul:
                ValidateLoadedHaulJob(actor);
                break;
            case ActorJobKind.Rest:
                ValidateLoadedRestJob(actor);
                break;
            case ActorJobKind.Eat:
                ValidateLoadedEatJob(actor);
                break;
            case ActorJobKind.Explore:
                ValidateLoadedExploreJob(actor);
                break;
            case ActorJobKind.Move:
                ValidateLoadedMoveJob(actor);
                break;
            case ActorJobKind.Resupply:
                ValidateLoadedResupplyJob(actor);
                break;
            default:
                throw new InvalidDataException("The save contains an unsupported actor job.");
        }

        ValidateLoadedJobExecution(actor);
    }

    private void ValidateLoadedForageJob(ActorState actor)
    {
        if (actor.JobStage != ActorJobStage.None ||
            actor.CarriedStackId != EntityId.None ||
            actor.SourceStackId != EntityId.None ||
            actor.DestinationZoneId != EntityId.None ||
            actor.ReservedQuantity != 0 ||
            World.GetPlantPatch(actor.JobTarget) is null)
        {
            throw new InvalidDataException("The save contains an invalid forage job.");
        }
    }

    private void ValidateLoadedHaulJob(ActorState actor)
    {
        if (actor.ReservedQuantity <= 0 ||
            actor.ReservedQuantity > Definitions.ActorCarryCapacity ||
            !_storageZones.TryGetValue(actor.DestinationZoneId, out var zone))
        {
            throw new InvalidDataException("The save contains an invalid haul reservation.");
        }

        if (actor.JobStage == ActorJobStage.Collecting)
        {
            if (actor.CarriedStackId != EntityId.None ||
                !_itemStacks.TryGetValue(actor.SourceStackId, out var source) ||
                source.Location.Kind != ItemLocationKind.Ground ||
                source.Quantity < actor.ReservedQuantity ||
                actor.JobTarget != source.Location.Position ||
                !ZoneAccepts(zone, source.Resource))
            {
                throw new InvalidDataException("The save contains an invalid haul collection state.");
            }
        }
        else if (actor.JobStage == ActorJobStage.Delivering)
        {
            if (actor.SourceStackId != EntityId.None ||
                !_itemStacks.TryGetValue(actor.CarriedStackId, out var carried) ||
                carried.Location != ItemLocation.CarriedBy(actor.Id) ||
                carried.Quantity != actor.ReservedQuantity ||
                actor.JobTarget != zone.Position ||
                !ZoneAccepts(zone, carried.Resource))
            {
                throw new InvalidDataException("The save contains an invalid haul delivery state.");
            }
        }
        else
        {
            throw new InvalidDataException("The save contains an invalid haul stage.");
        }
    }

    private void ValidateLoadedRestJob(ActorState actor)
    {
        if (actor.JobStage != ActorJobStage.None ||
            actor.CarriedStackId != EntityId.None ||
            actor.SourceStackId != EntityId.None ||
            actor.DestinationZoneId != EntityId.None ||
            actor.ReservedQuantity != 0 ||
            !IsRestLocation(actor.JobTarget))
        {
            throw new InvalidDataException("The save contains an invalid rest job.");
        }
    }

    private void ValidateLoadedEatJob(ActorState actor)
    {
        if (actor.JobStage != ActorJobStage.None ||
            actor.CarriedStackId != EntityId.None ||
            actor.DestinationZoneId != EntityId.None ||
            actor.ReservedQuantity != 1 ||
            !_itemStacks.TryGetValue(actor.SourceStackId, out var food) ||
            food.Resource != ResourceKind.Food ||
            food.Location.Kind is not (ItemLocationKind.Ground or ItemLocationKind.StorageZone) ||
            food.Location.Position != actor.JobTarget)
        {
            throw new InvalidDataException("The save contains an invalid eat job.");
        }
    }

    private void ValidateLoadedExploreJob(ActorState actor)
    {
        if (actor.JobStage != ActorJobStage.None ||
            actor.CarriedStackId != EntityId.None ||
            actor.SourceStackId != EntityId.None ||
            actor.DestinationZoneId != EntityId.None ||
            actor.ReservedQuantity != 0 ||
            actor.JobPhase != ActorJobPhase.Traveling ||
            Visibility.Get(actor.JobTarget) != CellVisibility.Unknown)
        {
            throw new InvalidDataException("The save contains an invalid exploration job.");
        }
    }

    private static void ValidateLoadedMoveJob(ActorState actor)
    {
        if (actor.JobStage != ActorJobStage.None ||
            actor.SourceStackId != EntityId.None ||
            actor.DestinationZoneId != EntityId.None ||
            actor.ReservedQuantity != 0 ||
            actor.JobPhase != ActorJobPhase.Traveling)
        {
            throw new InvalidDataException("The save contains an invalid ordered move job.");
        }
    }

    private void ValidateLoadedResupplyJob(ActorState actor)
    {
        if (actor.DestinationZoneId != EntityId.None ||
            actor.ReservedQuantity != 1 ||
            (actor.JobStage == ActorJobStage.ProvisioningFood &&
             actor.CarriedStackId != EntityId.None) ||
            !IsResupplyJobValid(actor))
        {
            throw new InvalidDataException("The save contains an invalid personal resupply job.");
        }
    }

    private void ValidateLoadedJobExecution(ActorState actor)
    {
        var maximumWorkTicks = actor.JobKind switch
        {
            ActorJobKind.Forage => Definitions.ForageWorkTicks,
            ActorJobKind.Haul => Definitions.HaulHandlingTicks,
            ActorJobKind.Rest => GetMaximumRestWorkTicks(),
            ActorJobKind.Eat => Definitions.EatWorkTicks,
            ActorJobKind.Resupply => Definitions.ResupplyWorkTicks,
            _ => 0,
        };
        if (actor.JobPhase == ActorJobPhase.Working)
        {
            if (actor.Position != actor.JobTarget ||
                actor.RemainingRoute.Count != 0 ||
                actor.RemainingWorkTicks <= 0 ||
                actor.RemainingWorkTicks > maximumWorkTicks)
            {
                throw new InvalidDataException("The save contains invalid job work state.");
            }

            return;
        }

        if (actor.JobPhase != ActorJobPhase.Traveling ||
            actor.RemainingWorkTicks != 0 ||
            actor.RemainingRoute.Count == 0 ||
            actor.RemainingRoute[^1] != actor.JobTarget)
        {
            throw new InvalidDataException("The save contains invalid job travel state.");
        }

        var previous = actor.Position;
        foreach (var position in actor.RemainingRoute)
        {
            if (!World.IsSurfaceTraversable(position) || !AreCardinalNeighbors(previous, position))
            {
                throw new InvalidDataException("The save contains an invalid actor route.");
            }

            previous = position;
        }
    }

    private void ValidateLoadedJobReservations()
    {
        var sourceReservations = CreateHaulReservations(sourceReservations: true);
        foreach (var reservation in sourceReservations)
        {
            if (!_itemStacks.TryGetValue(reservation.Key, out var source) ||
                reservation.Value > source.Quantity)
            {
                throw new InvalidDataException("Jobs over-reserve an item stack.");
            }
        }

        var destinationReservations = CreateHaulReservations(sourceReservations: false);
        foreach (var reservation in destinationReservations)
        {
            if (!_storageZones.TryGetValue(reservation.Key, out var zone) ||
                GetStoredQuantity(zone.Id) + reservation.Value > zone.Capacity)
            {
                throw new InvalidDataException("Haul jobs over-reserve storage capacity.");
            }
        }
    }

    private static bool IsBetter(HaulPlan candidate, HaulPlan current) =>
        candidate.TotalDistance < current.TotalDistance ||
        (candidate.TotalDistance == current.TotalDistance &&
         (candidate.SourceStackId.Value < current.SourceStackId.Value ||
          (candidate.SourceStackId == current.SourceStackId &&
           candidate.DestinationZoneId.Value < current.DestinationZoneId.Value)));

    private int GetJobWorkTicks(ActorState actor) => actor.JobKind switch
    {
        ActorJobKind.Forage => Definitions.ForageWorkTicks,
        ActorJobKind.Haul => Definitions.HaulHandlingTicks,
        ActorJobKind.Rest => GetRestWorkTicks(actor),
        ActorJobKind.Eat => Definitions.EatWorkTicks,
        ActorJobKind.Resupply => Definitions.ResupplyWorkTicks,
        _ => throw new InvalidOperationException("An idle actor cannot begin work."),
    };

    private int GetRestWorkTicks(ActorState actor) =>
        Math.Max(1, (actor.Fatigue + Definitions.RestRecoveryPerTick - 1) /
            Definitions.RestRecoveryPerTick);

    private int GetMaximumRestWorkTicks() =>
        (Definitions.MaximumFatigue + Definitions.RestRecoveryPerTick - 1) /
        Definitions.RestRecoveryPerTick;

    private bool IsRestLocation(GridPosition position) =>
        World.GetWorldObjectsAt(position).Any(worldObject =>
            worldObject.Kind == WorldObjectKind.GoblinHut &&
            worldObject.Owner == WorldObjectOwner.GoblinTribe &&
            worldObject.GetAbsoluteParts().Any(item =>
                item.Position == position &&
                item.Part.Kind is WorldObjectPartKind.Floor or WorldObjectPartKind.Door));

    private int GetReservedItemQuantity(EntityId stackId, ActorState consumingActor)
    {
        var reserved = 0;
        foreach (var actor in _actors.Values)
        {
            if (actor.JobKind == ActorJobKind.Haul &&
                actor.JobStage == ActorJobStage.Collecting &&
                actor.SourceStackId == stackId)
            {
                reserved = checked(reserved + actor.ReservedQuantity);
            }
            else if (actor != consumingActor &&
                     actor.JobKind == ActorJobKind.Eat &&
                     actor.SourceStackId == stackId)
            {
                reserved = checked(reserved + actor.ReservedQuantity);
            }
            else if (actor.JobKind == ActorJobKind.Resupply &&
                     actor.JobStage == ActorJobStage.ProvisioningFood &&
                     actor.SourceStackId == stackId)
            {
                reserved = checked(reserved + actor.ReservedQuantity);
            }
        }

        return reserved;
    }

    private static bool AreCardinalNeighbors(GridPosition first, GridPosition second) =>
        first.Z == second.Z &&
        Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y) == 1;

    private readonly record struct HaulPlan(
        EntityId SourceStackId,
        EntityId DestinationZoneId,
        int Quantity,
        IReadOnlyList<GridPosition> Route,
        int TotalDistance);
}
