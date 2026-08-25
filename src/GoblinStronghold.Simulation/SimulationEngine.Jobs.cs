using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation;

public sealed partial class SimulationEngine
{
    private const int MaximumConstructionRouteCandidatesPerPlanningTick = 12;

    private void UpdateActorJobs()
    {
        var reservedForageTargets = _actors.Values
            .Where(actor => actor.JobKind is ActorJobKind.Forage or ActorJobKind.ClearVegetation)
            .Select(actor => actor.JobTarget)
            .ToHashSet();
        var reservedFellingDesignations = _actors.Values
            .Where(actor => actor.JobKind is ActorJobKind.FellTree or ActorJobKind.QuarryBoulder)
            .Select(actor => actor.SourceStackId)
            .Where(id => id != EntityId.None)
            .ToHashSet();
        var reservedSourceQuantities = CreateHaulReservations(sourceReservations: true);
        var reservedDestinationQuantities = CreateHaulReservations(sourceReservations: false);
        var reservedConstructionQuantities = CreateConstructionReservations();
        var activeExplorers = _actors.Values.Count(actor => actor.JobKind == ActorJobKind.Explore);
        var raidPartyIds = _raidPhase == GoblinRaidPhase.Preparing
            ? GetRaidParty().Select(actor => actor.Id).ToHashSet()
            : [];

        foreach (var actor in _actors.Values)
        {
            if (actor.JobKind == ActorJobKind.Collapsed)
            {
                UpdateCollapsedJob(actor);
                continue;
            }

            TryInterruptForNeeds(
                actor,
                reservedForageTargets,
                reservedSourceQuantities,
                reservedDestinationQuantities);

            if (actor.JobKind == ActorJobKind.None)
            {
                var needsFood = actor.Hunger >= Definitions.FoodSeekThreshold;
                var needsWater = actor.Thirst >= Definitions.DrinkThreshold && actor.PersonalWater == 0;
                var reserveForExploration =
                    IsBackgroundPlanningTick(actor) &&
                    activeExplorers < Definitions.MaximumExplorers;
                if (_raidPhase == GoblinRaidPhase.Preparing &&
                    raidPartyIds.Contains(actor.Id) &&
                    TryPlanRaidPreparation(
                        actor,
                        reservedSourceQuantities,
                        reservedDestinationQuantities))
                {
                    // The camp prepares one bounded expedition while the rest of the tribe keeps working.
                }
                else if (needsWater && TryPlanWaterResupply(actor))
                {
                    // Water outranks cargo and ordinary work once the carried supply is empty.
                }
                else if (actor.CarriedStackId != EntityId.None)
                {
                    if (!TryPlanCarriedConstructionDelivery(
                            actor,
                            reservedConstructionQuantities))
                    {
                        TryPlanHaulDelivery(actor, reservedDestinationQuantities);
                    }
                }
                else if (needsFood && TryPlanEatJob(actor, reservedSourceQuantities))
                {
                    // A reachable meal outranks fatigue and settlement work.
                }
                else if (needsFood && TryPlanForageJob(
                             actor,
                             reservedForageTargets,
                             requireDesignation: false))
                {
                    // With no prepared food, gathering becomes survival work.
                }
                else if (actor.Fatigue >= Definitions.RestThreshold && TryPlanRestJob(actor))
                {
                    // Survival work outranks gathering once the current job has ended.
                }
                else if (HasAssignedStorageDuty(actor.Id) &&
                         TryPlanHaulCollection(
                             actor,
                             reservedSourceQuantities,
                             reservedDestinationQuantities,
                             assignedDestinationsOnly: true))
                {
                    // A named hauler services assigned stockpiles before public settlement work.
                }
                else if (TryPlanPreferredPublicWork(
                             actor,
                             reservedForageTargets,
                             reservedFellingDesignations,
                             reservedSourceQuantities,
                             reservedDestinationQuantities,
                             reservedConstructionQuantities,
                             allowDesignatedForage: !reserveForExploration))
                {
                    // Public priority dominates preference; preference breaks comparable work apart.
                }
                else if (TryPlanFoodResupply(actor, reservedSourceQuantities))
                {
                    // A small carried ration avoids a trip home for every meal.
                }
                else if (TryPlanWaterResupply(actor))
                {
                    // Primitive containers are refilled at accessible shallow water.
                }
                else if (IsBackgroundPlanningTick(actor) &&
                         activeExplorers < Definitions.MaximumExplorers &&
                         TryPlanExploreJob(actor))
                {
                    activeExplorers++;
                }
                else
                {
                    TryPlanForageJob(
                        actor,
                        reservedForageTargets,
                        requireDesignation: true);
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
                case ActorJobKind.ClearVegetation:
                    UpdateClearVegetationJob(actor);
                    break;
                case ActorJobKind.FellTree:
                    UpdateFellTreeJob(actor);
                    break;
                case ActorJobKind.QuarryBoulder:
                    UpdateQuarryBoulderJob(actor);
                    break;
                case ActorJobKind.SupplyConstruction:
                    UpdateConstructionSupplyJob(actor);
                    break;
                case ActorJobKind.BuildConstruction:
                    UpdateConstructionBuildJob(actor);
                    break;
                case ActorJobKind.Collapsed:
                    UpdateCollapsedJob(actor);
                    break;
            }
        }

        TryLaunchPreparedRaid();
        RemoveExhaustedWorkDesignations();
    }

    private bool TryPlanRaidPreparation(
        ActorState actor,
        Dictionary<EntityId, int> sourceReservations,
        Dictionary<EntityId, int> destinationReservations)
    {
        if (actor.CarriedStackId != EntityId.None)
        {
            TryPlanHaulDelivery(actor, destinationReservations);
            return true;
        }
        var isAtRally = actor.Position == _raidRallyPoint;
        var isInRallyArea = Distance(actor.Position, _raidRallyPoint) <= 4;
        var foodTarget = Definitions.PersonalFoodCapacity;
        var waterTarget = Definitions.PersonalWaterCapacity;
        if (!isInRallyArea && actor.Hunger >= Definitions.FoodSeekThreshold &&
            TryPlanEatJob(actor, sourceReservations))
        {
            return true;
        }
        if (!isInRallyArea && actor.PersonalFood < foodTarget &&
            TryPlanFoodResupply(actor, sourceReservations))
        {
            return true;
        }

        if (actor.Hunger >= Definitions.FoodSeekThreshold &&
            TryPlanEatJob(actor, sourceReservations, isInRallyArea ? _raidRallyPoint : null))
        {
            return true;
        }
        if (actor.PersonalFood < foodTarget &&
            TryPlanFoodResupply(actor, sourceReservations, isInRallyArea ? _raidRallyPoint : null))
        {
            return true;
        }
        if (actor.PersonalWater < waterTarget && TryPlanWaterResupply(actor))
        {
            return true;
        }
        if (actor.Fatigue >= Definitions.RestThreshold && TryPlanRestJob(actor))
        {
            return true;
        }
        var campZone = _storageZones.Values.FirstOrDefault(zone =>
            zone.Position == _raidRallyPoint && zone.AcceptedResource == ResourceKind.Food);
        var raidParty = GetRaidParty();
        var outstandingPartyFood = raidParty.Sum(candidate =>
            Math.Max(0, foodTarget - candidate.PersonalFood) +
            (candidate.Hunger >= Definitions.FoodSeekThreshold ? 1 : 0));
        var requiredCampStock = outstandingPartyFood;
        if (campZone is not null &&
            GetStoredQuantity(campZone.Id) + destinationReservations.GetValueOrDefault(campZone.Id) <
                requiredCampStock &&
            TryPlanHaulCollection(
                actor,
                sourceReservations,
                destinationReservations,
                campZone.Id))
        {
            return true;
        }
        if (!isAtRally)
        {
            var route = Navigation.FindSurfacePath(actor.Position, _raidRallyPoint);
            if (route is { Count: > 0 })
            {
                actor.JobKind = ActorJobKind.Move;
                actor.JobPhase = ActorJobPhase.Traveling;
                actor.JobTarget = _raidRallyPoint;
                actor.RemainingRoute.AddRange(route);
            }
        }
        return true;
    }

    private void TryLaunchPreparedRaid()
    {
        var raidParty = GetRaidParty();
        if (_raidPhase != GoblinRaidPhase.Preparing || raidParty.Count == 0 ||
            raidParty.Any(actor =>
                actor.Position != _raidRallyPoint ||
                actor.JobKind != ActorJobKind.None ||
                actor.CarriedStackId != EntityId.None ||
                actor.PersonalFood < Definitions.PersonalFoodCapacity ||
                actor.PersonalWater < Definitions.PersonalWaterCapacity ||
                actor.Hunger >= Definitions.FoodSeekThreshold ||
                actor.Thirst >= Definitions.DrinkThreshold ||
                actor.Fatigue >= Definitions.RestThreshold))
        {
            return;
        }

        _raidPhase = GoblinRaidPhase.Marching;
        _humanVillage.OrderGoblinAttack();
        foreach (var actor in raidParty)
        {
            var route = Navigation.FindSurfacePath(actor.Position, Map.HumanVillage);
            if (route is not { Count: > 0 })
            {
                continue;
            }
            actor.JobKind = ActorJobKind.Move;
            actor.JobPhase = ActorJobPhase.Traveling;
            actor.JobTarget = Map.HumanVillage;
            actor.RemainingRoute.AddRange(route);
            Publish(SimulationEventKind.MoveOrdered, actor.Id, EntityId.None, route.Count);
        }
        Publish(SimulationEventKind.RaidDeparted, EntityId.None, EntityId.None, raidParty.Count);
    }

    private List<ActorState> GetRaidParty() => _actors.Values
        .OrderBy(actor => actor.Id.Value)
        .Take(SimulationDefinitions.FieldCampCapacity)
        .ToList();

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
                if (!visited.Add(neighbor) || !World.IsSurfaceTraversable(neighbor) ||
                    !Map.CanTraverseSurfaceEdge(current, neighbor))
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

    private bool TryPlanPreferredPublicWork(
        ActorState actor,
        ISet<GridPosition> reservedForageTargets,
        ISet<EntityId> reservedFellingDesignations,
        Dictionary<EntityId, int> sourceReservations,
        Dictionary<EntityId, int> destinationReservations,
        Dictionary<EntityId, int> constructionReservations,
        bool allowDesignatedForage)
    {
        var constructionSupplyPriority = _constructionSites.Values
            .Where(site => site.MissingWood - constructionReservations.GetValueOrDefault(site.Id) > 0)
            .Select(site => site.Priority)
            .DefaultIfEmpty(StoragePriority.Low)
            .Max();
        var constructionWorkPriority = _constructionSites.Values
            .Where(site => site.HasAllMaterials)
            .Select(site => site.Priority)
            .DefaultIfEmpty(StoragePriority.Low)
            .Max();
        var haulingPriority = _storageZones.Values
            .Select(zone => zone.Priority)
            .DefaultIfEmpty(StoragePriority.Low)
            .Max();
        var normal = StoragePriority.Normal;
        var options = new (int Score, int Order, Func<bool> TryPlan)[]
        {
            (Score(constructionSupplyPriority, actor.WorkPreferences.Hauling), 0,
                () => TryPlanConstructionSupply(
                    actor,
                    sourceReservations,
                    constructionReservations)),
            (Score(constructionWorkPriority, actor.WorkPreferences.Building), 1,
                () => TryPlanConstructionWork(actor)),
            (Score(haulingPriority, actor.WorkPreferences.Hauling), 2,
                () => TryPlanHaulCollection(
                    actor,
                    sourceReservations,
                    destinationReservations)),
            (Score(normal, actor.WorkPreferences.Foraging), 3,
                () => TryPlanClearVegetationJob(actor, reservedForageTargets)),
            (Score(normal, actor.WorkPreferences.Foraging), 4,
                () => TryPlanFellTreeJob(actor, reservedFellingDesignations)),
            (Score(normal, actor.WorkPreferences.Foraging), 5,
                () => TryPlanQuarryBoulderJob(actor, reservedFellingDesignations)),
            (Score(normal, actor.WorkPreferences.Foraging), 6,
                () => allowDesignatedForage &&
                    TryPlanForageJob(actor, reservedForageTargets, requireDesignation: true)),
        };
        return options
            .OrderByDescending(option => option.Score)
            .ThenBy(option => option.Order)
            .Any(option => option.TryPlan());

        static int Score(StoragePriority priority, int preference) =>
            checked((int)priority * 10 + preference);
    }

    private IReadOnlyList<ActorPlanEntrySnapshot> CreateActorPlanSnapshot(ActorState actor)
    {
        var entries = new List<(ActorPlanEntrySnapshot Entry, int Order)>();
        if (actor.JobKind != ActorJobKind.None)
        {
            entries.Add((new(
                ActorPlanIntentKind.CurrentJob,
                actor.JobKind,
                GetJobCommitment(actor.JobKind),
                actor.JobTarget), 0));
        }

        var hungerPriority = GetHungerPriority(actor);
        if (hungerPriority > 0 &&
            actor.JobKind != ActorJobKind.Eat &&
            !(actor.JobKind == ActorJobKind.Resupply &&
              actor.JobStage == ActorJobStage.ProvisioningFood))
        {
            entries.Add((new(
                actor.PersonalFood > 0
                    ? ActorPlanIntentKind.Eat
                    : ActorPlanIntentKind.FindFood,
                ActorJobKind.Eat,
                hungerPriority,
                actor.Position), 1));
        }

        var thirstPriority = GetThirstPriority(actor);
        if (thirstPriority > 0 &&
            !(actor.JobKind == ActorJobKind.Resupply &&
              actor.JobStage == ActorJobStage.ProvisioningWater))
        {
            entries.Add((new(
                actor.PersonalWater > 0
                    ? ActorPlanIntentKind.Drink
                    : ActorPlanIntentKind.RefillWater,
                ActorJobKind.Resupply,
                thirstPriority,
                actor.Position), 2));
        }

        var fatiguePriority = GetFatiguePriority(actor);
        if (fatiguePriority > 0 && actor.JobKind != ActorJobKind.Rest)
        {
            entries.Add((new(
                ActorPlanIntentKind.Rest,
                ActorJobKind.Rest,
                fatiguePriority,
                actor.Position), 3));
        }

        if (actor.SuspendedJobKind != ActorJobKind.None)
        {
            entries.Add((new(
                ActorPlanIntentKind.ResumeSuspendedJob,
                actor.SuspendedJobKind,
                GetJobCommitment(actor.SuspendedJobKind),
                actor.SuspendedJobTarget), 4));
        }

        return entries
            .OrderBy(item => item.Entry.Kind == ActorPlanIntentKind.CurrentJob ? 0 : 1)
            .ThenByDescending(item => item.Entry.Priority)
            .ThenBy(item => item.Order)
            .Take(Definitions.ActorPlanning.QueueCapacity)
            .Select(item => item.Entry)
            .ToArray();
    }

    private int GetHungerPriority(ActorState actor) => GetNeedPriority(
        actor.Hunger,
        Definitions.FoodSeekThreshold,
        Definitions.CriticalHungerThreshold);

    private int GetThirstPriority(ActorState actor) => GetNeedPriority(
        actor.Thirst,
        Definitions.DrinkThreshold,
        Definitions.DehydrationThirstThreshold);

    private int GetFatiguePriority(ActorState actor) => GetNeedPriority(
        actor.Fatigue,
        Definitions.RestThreshold,
        Definitions.MaximumFatigue);

    private int GetNeedPriority(int value, int planningThreshold, int criticalThreshold)
    {
        if (value < planningThreshold)
        {
            return 0;
        }
        if (value >= criticalThreshold || criticalThreshold == planningThreshold)
        {
            return Definitions.ActorPlanning.MaximumNeedPriority;
        }

        var range = criticalThreshold - planningThreshold;
        var progress = value - planningThreshold;
        return 1 + (int)((long)progress *
            (Definitions.ActorPlanning.MaximumNeedPriority - 1) / range);
    }

    private int GetJobCommitment(ActorJobKind kind) => kind switch
    {
        ActorJobKind.None => 0,
        ActorJobKind.Explore => Definitions.ActorPlanning.BackgroundJobCommitment,
        ActorJobKind.Move => Definitions.ActorPlanning.OrderedJobCommitment,
        ActorJobKind.Collapsed => Definitions.ActorPlanning.MaximumNeedPriority,
        _ => Definitions.ActorPlanning.OrdinaryJobCommitment,
    };

    private void UpdateMoveJob(ActorState actor)
    {
        if (actor.JobPhase == ActorJobPhase.Traveling)
        {
            AdvanceTravel(actor);
        }
    }

    private void TryInterruptForNeeds(
        ActorState actor,
        ISet<GridPosition> forageTargets,
        Dictionary<EntityId, int> itemReservations,
        Dictionary<EntityId, int> destinationReservations)
    {
        if (actor.JobKind is ActorJobKind.None or ActorJobKind.Collapsed)
        {
            return;
        }

        var interruptPriority = checked(
            GetJobCommitment(actor.JobKind) + Definitions.ActorPlanning.InterruptHysteresis);
        var candidates = new (int Priority, int Order, Func<bool> TryInterrupt)[]
        {
            (GetThirstPriority(actor), 0,
                () => TryInterruptForWater(
                    actor,
                    forageTargets,
                    itemReservations,
                    destinationReservations)),
            (GetHungerPriority(actor), 1,
                () => TryInterruptForHunger(
                    actor,
                    itemReservations,
                    destinationReservations)),
            (GetFatiguePriority(actor), 2,
                () => TryInterruptForFatigue(
                    actor,
                    forageTargets,
                    itemReservations,
                    destinationReservations)),
        };

        foreach (var candidate in candidates
                     .Where(candidate => candidate.Priority > interruptPriority)
                     .OrderByDescending(candidate => candidate.Priority)
                     .ThenBy(candidate => candidate.Order))
        {
            if (candidate.TryInterrupt())
            {
                return;
            }
        }
    }

    private bool TryInterruptForHunger(
        ActorState actor,
        Dictionary<EntityId, int> itemReservations,
        Dictionary<EntityId, int> destinationReservations)
    {
        if (actor.Hunger < Definitions.FoodSeekThreshold ||
            actor.CarriedStackId != EntityId.None ||
            actor.JobKind is ActorJobKind.None or ActorJobKind.Eat or ActorJobKind.Resupply)
        {
            return false;
        }

        var releasedSourceQuantity = actor.JobKind == ActorJobKind.Haul
            ? actor.ReservedQuantity
            : 0;
        var hasReachableMeal = _itemStacks.Values.Any(stack =>
            stack.Resource == ResourceKind.Food &&
            stack.Location.Kind is ItemLocationKind.Ground or ItemLocationKind.StorageZone &&
            stack.Quantity - itemReservations.GetValueOrDefault(stack.Id) +
                (stack.Id == actor.SourceStackId ? releasedSourceQuantity : 0) > 0 &&
            Navigation.HasSurfacePath(actor.Position, stack.Location.Position));
        if (!hasReachableMeal)
        {
            return false;
        }

        if (actor.JobKind == ActorJobKind.Haul)
        {
            ReduceReservation(itemReservations, actor.SourceStackId, actor.ReservedQuantity);
            ReduceReservation(
                destinationReservations,
                actor.DestinationZoneId,
                actor.ReservedQuantity);
        }

        actor.SuspendCurrentJob();
        return true;
    }

    private bool TryInterruptForFatigue(
        ActorState actor,
        ISet<GridPosition> forageTargets,
        Dictionary<EntityId, int> itemReservations,
        Dictionary<EntityId, int> destinationReservations)
    {
        if (actor.Fatigue < Definitions.RestThreshold ||
            actor.CarriedStackId != EntityId.None ||
            actor.JobKind is ActorJobKind.None or ActorJobKind.Rest or ActorJobKind.Eat or
                ActorJobKind.Resupply ||
            !World.CreateWorldObjectSnapshot()
                .Where(worldObject =>
                    (worldObject.Kind is WorldObjectKind.GoblinHut or
                        WorldObjectKind.GoblinFieldCamp) &&
                    worldObject.Owner == WorldObjectOwner.GoblinTribe)
                .SelectMany(worldObject => worldObject.GetAbsoluteParts())
                .Any(item =>
                    item.Position.Z == 0 &&
                    item.Part.Kind is WorldObjectPartKind.Floor or WorldObjectPartKind.Door &&
                    World.IsSurfaceTraversable(item.Position) &&
                    Navigation.HasSurfacePath(actor.Position, item.Position)))
        {
            return false;
        }

        if (actor.JobKind is ActorJobKind.Forage or ActorJobKind.ClearVegetation)
        {
            forageTargets.Remove(actor.JobTarget);
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

        actor.SuspendCurrentJob();
        return true;
    }

    private bool TryInterruptForWater(
        ActorState actor,
        ISet<GridPosition> forageTargets,
        Dictionary<EntityId, int> itemReservations,
        Dictionary<EntityId, int> destinationReservations)
    {
        if (actor.Thirst < Definitions.DrinkThreshold ||
            actor.PersonalWater > 0 ||
            actor.CarriedStackId != EntityId.None ||
            (actor.JobKind == ActorJobKind.Resupply &&
             actor.JobStage == ActorJobStage.ProvisioningWater) ||
            FindNearestShallowWaterPath(actor.Position) is null)
        {
            return false;
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

        actor.SuspendCurrentJob();
        return true;
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
                (worldObject.Kind is WorldObjectKind.GoblinHut or WorldObjectKind.GoblinFieldCamp) &&
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
                Route = Navigation.FindSurfacePath(actor.Position, position),
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
            TryResumeSuspendedJob(actor);
        }
    }

    private void UpdateCollapsedJob(ActorState actor)
    {
        actor.Fatigue = Math.Max(0, actor.Fatigue - Definitions.RestRecoveryPerTick);
        actor.RemainingWorkTicks = GetRestWorkTicks(actor);
        if (actor.Fatigue > 0)
        {
            return;
        }

        actor.ClearJob();
        TryResumeSuspendedJob(actor);
    }

    private bool TryResumeSuspendedJob(ActorState actor)
    {
        var kind = actor.SuspendedJobKind;
        var target = actor.SuspendedJobTarget;
        actor.ClearSuspendedJob();

        if (kind == ActorJobKind.None || !Map.IsWithin(target))
        {
            return false;
        }

        var route = Navigation.FindSurfacePath(actor.Position, target);
        if (route is null)
        {
            return false;
        }

        switch (kind)
        {
            case ActorJobKind.Move:
                if (route.Count == 0)
                {
                    return false;
                }
                actor.JobKind = kind;
                actor.JobTarget = target;
                BeginJobLeg(actor, route, workTicks: 0);
                return true;
            case ActorJobKind.Explore:
                if (route.Count == 0 || Visibility.Get(target) != CellVisibility.Unknown)
                {
                    return false;
                }
                actor.JobKind = kind;
                actor.JobTarget = target;
                BeginJobLeg(actor, route, workTicks: 0);
                return true;
            case ActorJobKind.Forage:
                if (World.GetPlantPatch(target) is not { Biomass: > 0 })
                {
                    return false;
                }
                actor.JobKind = kind;
                actor.JobTarget = target;
                BeginJobLeg(actor, route, Definitions.ForageWorkTicks);
                return true;
            case ActorJobKind.ClearVegetation:
                if (World.GetPlantPatch(target) is not { Kind: PlantKind.BerryBush })
                {
                    return false;
                }
                actor.JobKind = kind;
                actor.JobTarget = target;
                BeginJobLeg(actor, route, GetClearVegetationWorkTicks());
                return true;
            case ActorJobKind.FellTree:
                if (!actor.Equipment.HasFlag(PersonalEquipment.WoodenAxe))
                {
                    return false;
                }
                var designation = _workDesignations.Values
                    .Where(item => item.Kind == WorkDesignationKind.FellTree &&
                        World.GetFellableWood(item.Target) is not null &&
                        AreCardinalNeighbors(target, item.Target))
                    .OrderBy(item => item.Id)
                    .FirstOrDefault();
                if (designation == default)
                {
                    return false;
                }
                actor.JobKind = kind;
                actor.JobTarget = target;
                actor.SourceStackId = designation.Id;
                BeginJobLeg(actor, route, GetFellTreeWorkTicks());
                return true;
            case ActorJobKind.QuarryBoulder:
                if (!actor.Equipment.HasFlag(PersonalEquipment.PrimitivePickaxe))
                {
                    return false;
                }
                var quarryDesignation = _workDesignations.Values
                    .Where(item => item.Kind == WorkDesignationKind.QuarryBoulder &&
                        World.GetQuarriableBoulder(item.Target) is not null &&
                        AreCardinalNeighbors(target, item.Target))
                    .OrderBy(item => item.Id)
                    .FirstOrDefault();
                if (quarryDesignation == default)
                {
                    return false;
                }
                actor.JobKind = kind;
                actor.JobTarget = target;
                actor.SourceStackId = quarryDesignation.Id;
                BeginJobLeg(actor, route, GetQuarryBoulderWorkTicks());
                return true;
            default:
                return false;
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

            if (sourceReservations &&
                actor.JobKind == ActorJobKind.SupplyConstruction &&
                actor.JobStage == ActorJobStage.Collecting)
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
        Dictionary<EntityId, int> itemReservations,
        GridPosition? requiredPosition = null)
    {
        if (actor.PersonalFood >= Definitions.PersonalFoodCapacity)
        {
            return false;
        }

        var best = _itemStacks.Values
            .Where(stack =>
                stack.Resource == ResourceKind.Food &&
                (actor.PersonalFood == 0 || stack.FoodKind == actor.PersonalFoodKind) &&
                stack.Location.Kind is ItemLocationKind.Ground or ItemLocationKind.StorageZone &&
                (requiredPosition is null || stack.Location.Position == requiredPosition.Value) &&
                stack.Quantity - itemReservations.GetValueOrDefault(stack.Id) > 0)
            .Select(stack => new
            {
                Stack = stack,
                Route = Navigation.FindSurfacePath(actor.Position, stack.Location.Position),
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
                if (visited.Add(neighbor) && World.IsSurfaceTraversable(neighbor) &&
                    Map.CanTraverseSurfaceEdge(current, neighbor))
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
            (actor.PersonalFood == 0 || food.FoodKind == actor.PersonalFoodKind) &&
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
            if (actor.PersonalFood == 0)
            {
                actor.PersonalFoodKind = food.FoodKind;
            }
            actor.PersonalFood++;
            Publish(SimulationEventKind.ActorProvisionedFood, actor.Id, food.Id, 1);
            if (food.Quantity == 0)
            {
                RemoveItemStack(food.Id);
                Publish(SimulationEventKind.ItemStackDepleted, actor.Id, food.Id, 0);
            }
        }
        else
        {
            actor.PersonalWater++;
            Publish(SimulationEventKind.ActorCollectedWater, actor.Id, EntityId.None, 1);
        }

        actor.ClearJob();
        TryResumeSuspendedJob(actor);
    }

    private bool TryPlanEatJob(
        ActorState actor,
        Dictionary<EntityId, int> itemReservations,
        GridPosition? requiredPosition = null)
    {
        var best = _itemStacks.Values
            .Where(stack =>
                stack.Resource == ResourceKind.Food &&
                stack.Location.Kind is ItemLocationKind.Ground or ItemLocationKind.StorageZone &&
                (requiredPosition is null || stack.Location.Position == requiredPosition.Value) &&
                stack.Quantity - itemReservations.GetValueOrDefault(stack.Id) > 0)
            .Select(stack => new
            {
                Stack = stack,
                Route = Navigation.FindSurfacePath(actor.Position, stack.Location.Position),
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
        actor.Hunger = Math.Max(0, actor.Hunger - Definitions.Food.GetSatiety(food.FoodKind));
        Publish(SimulationEventKind.ActorAte, actor.Id, food.Id, 1);
        if (food.Quantity == 0)
        {
            RemoveItemStack(food.Id);
            Publish(SimulationEventKind.ItemStackDepleted, actor.Id, food.Id, 0);
        }

        actor.ClearJob();
        TryResumeSuspendedJob(actor);
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

    private bool TryPlanForageJob(
        ActorState actor,
        ISet<GridPosition> reservedTargets,
        bool requireDesignation)
    {
        if (requireDesignation && !_workDesignations.Values.Any(designation =>
                designation.Kind == WorkDesignationKind.GatherFood))
        {
            return false;
        }

        var route = Navigation.FindNearestHarvestablePlantPath(
            actor.Position,
            reservedTargets,
            position =>
                Visibility.Get(position) != CellVisibility.Unknown &&
                (!requireDesignation ||
                 IsWorkDesignated(WorkDesignationKind.GatherFood, position)));
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

    private bool TryPlanClearVegetationJob(
        ActorState actor,
        ISet<GridPosition> reservedTargets)
    {
        if (!_workDesignations.Values.Any(designation =>
                designation.Kind == WorkDesignationKind.UprootBerryBush))
        {
            return false;
        }

        var route = Navigation.FindNearestBerryBushPath(
            actor.Position,
            reservedTargets,
            position =>
                Visibility.Get(position) != CellVisibility.Unknown &&
                IsWorkDesignated(WorkDesignationKind.UprootBerryBush, position));
        if (route is null)
        {
            return false;
        }

        actor.JobKind = ActorJobKind.ClearVegetation;
        actor.JobTarget = route.Count == 0 ? actor.Position : route[^1];
        BeginJobLeg(actor, route, GetClearVegetationWorkTicks());
        reservedTargets.Add(actor.JobTarget);
        return true;
    }

    private void UpdateClearVegetationJob(ActorState actor)
    {
        if (actor.CarriedStackId != EntityId.None ||
            World.GetPlantPatch(actor.JobTarget) is not { Kind: PlantKind.BerryBush })
        {
            actor.ClearJob();
            return;
        }

        if (actor.JobPhase == ActorJobPhase.Traveling)
        {
            AdvanceTravel(actor);
        }

        if (actor.JobPhase != ActorJobPhase.Working)
        {
            return;
        }

        actor.RemainingWorkTicks--;
        if (actor.RemainingWorkTicks > 0)
        {
            return;
        }

        if (World.TryUprootBerryBush(actor.Position, CurrentTick, out var change))
        {
            _undeliveredWorldChanges.Add(change);
            GainBuildingExperience(actor, 15);
        }

        actor.ClearJob();
    }

    private bool TryPlanFellTreeJob(
        ActorState actor,
        ISet<EntityId> reservedDesignations)
    {
        if (!actor.Equipment.HasFlag(PersonalEquipment.WoodenAxe) ||
            !actor.KnownSkills.HasFlag(GoblinSkill.Building))
        {
            return false;
        }

        var best = _workDesignations.Values
            .Where(designation => designation.Kind == WorkDesignationKind.FellTree &&
                !reservedDesignations.Contains(designation.Id) &&
                World.GetFellableWood(designation.Target) is not null)
            .SelectMany(designation => Map.GetCardinalNeighbors(designation.Target)
                .Where(World.IsSurfaceTraversable)
                .Select(position => new
                {
                    Designation = designation,
                    Position = position,
                    Route = Navigation.FindSurfacePath(actor.Position, position),
                }))
            .Where(candidate => candidate.Route is not null)
            .OrderBy(candidate => candidate.Route!.Count)
            .ThenBy(candidate => candidate.Designation.Id)
            .ThenBy(candidate => candidate.Position.Y)
            .ThenBy(candidate => candidate.Position.X)
            .FirstOrDefault();
        if (best is null)
        {
            return false;
        }

        actor.JobKind = ActorJobKind.FellTree;
        actor.JobTarget = best.Position;
        actor.SourceStackId = best.Designation.Id;
        BeginJobLeg(actor, best.Route!, GetFellTreeWorkTicks());
        reservedDesignations.Add(best.Designation.Id);
        return true;
    }

    private void UpdateFellTreeJob(ActorState actor)
    {
        if (!actor.Equipment.HasFlag(PersonalEquipment.WoodenAxe) ||
            !_workDesignations.TryGetValue(actor.SourceStackId, out var designation) ||
            designation.Kind != WorkDesignationKind.FellTree ||
            World.GetFellableWood(designation.Target) is null ||
            !AreCardinalNeighbors(actor.JobTarget, designation.Target))
        {
            actor.ClearJob();
            return;
        }

        if (actor.JobPhase == ActorJobPhase.Traveling)
        {
            AdvanceTravel(actor);
        }

        if (actor.JobPhase != ActorJobPhase.Working)
        {
            return;
        }

        actor.RemainingWorkTicks--;
        if (actor.RemainingWorkTicks > 0)
        {
            return;
        }

        if (World.TryHarvestFellableWood(
                designation.Target,
                CurrentTick,
                out var woodQuantity,
                out var change))
        {
            _undeliveredWorldChanges.Add(change);
            ScatterFelledWood(actor.Position, designation.Target, woodQuantity);
            GainBuildingExperience(actor, Math.Max(10, woodQuantity));
        }

        _workDesignations.Remove(designation.Id);
        Publish(SimulationEventKind.WorkDesignationRemoved, actor.Id, designation.Id, 0);
        actor.ClearJob();
    }

    private void ScatterFelledWood(
        GridPosition workerPosition,
        GridPosition treePosition,
        int woodQuantity)
    {
        var directionX = treePosition.X - workerPosition.X;
        var directionY = treePosition.Y - workerPosition.Y;
        var remaining = woodQuantity;
        for (var section = 0; remaining > 0; section++)
        {
            var candidate = new GridPosition(
                treePosition.X + (directionX * (section + 1)),
                treePosition.Y + (directionY * (section + 1)),
                treePosition.Z);
            var position = World.IsSurfaceTraversable(candidate) ? candidate : workerPosition;
            var quantity = Math.Min(16, remaining);
            var existing = FindMergeableGroundStack(ResourceKind.Wood, position);
            if (existing is null)
            {
                AllocateItemStack(ResourceKind.Wood, quantity, ItemLocation.OnGround(position));
            }
            else
            {
                existing.Quantity = checked(existing.Quantity + quantity);
            }
            remaining -= quantity;
        }
    }

    private bool TryPlanQuarryBoulderJob(
        ActorState actor,
        ISet<EntityId> reservedDesignations)
    {
        if (!actor.Equipment.HasFlag(PersonalEquipment.PrimitivePickaxe) ||
            !actor.KnownSkills.HasFlag(GoblinSkill.Building))
        {
            return false;
        }

        var best = _workDesignations.Values
            .Where(designation => designation.Kind == WorkDesignationKind.QuarryBoulder &&
                !reservedDesignations.Contains(designation.Id) &&
                World.GetQuarriableBoulder(designation.Target) is not null)
            .SelectMany(designation => Map.GetCardinalNeighbors(designation.Target)
                .Where(World.IsSurfaceTraversable)
                .Select(position => new
                {
                    Designation = designation,
                    Position = position,
                    Route = Navigation.FindSurfacePath(actor.Position, position),
                }))
            .Where(candidate => candidate.Route is not null)
            .OrderBy(candidate => candidate.Route!.Count)
            .ThenBy(candidate => candidate.Designation.Id)
            .ThenBy(candidate => candidate.Position.Y)
            .ThenBy(candidate => candidate.Position.X)
            .FirstOrDefault();
        if (best is null)
        {
            return false;
        }

        actor.JobKind = ActorJobKind.QuarryBoulder;
        actor.JobTarget = best.Position;
        actor.SourceStackId = best.Designation.Id;
        BeginJobLeg(actor, best.Route!, GetQuarryBoulderWorkTicks());
        reservedDesignations.Add(best.Designation.Id);
        return true;
    }

    private void UpdateQuarryBoulderJob(ActorState actor)
    {
        if (!actor.Equipment.HasFlag(PersonalEquipment.PrimitivePickaxe) ||
            !_workDesignations.TryGetValue(actor.SourceStackId, out var designation) ||
            designation.Kind != WorkDesignationKind.QuarryBoulder ||
            World.GetQuarriableBoulder(designation.Target) is null ||
            !AreCardinalNeighbors(actor.JobTarget, designation.Target))
        {
            actor.ClearJob();
            return;
        }

        if (actor.JobPhase == ActorJobPhase.Traveling)
        {
            AdvanceTravel(actor);
        }

        if (actor.JobPhase != ActorJobPhase.Working)
        {
            return;
        }

        actor.RemainingWorkTicks--;
        if (actor.RemainingWorkTicks > 0)
        {
            return;
        }

        if (World.TryQuarryBoulder(
                designation.Target,
                CurrentTick,
                out var stoneQuantity,
                out var change))
        {
            _undeliveredWorldChanges.Add(change);
            var existing = FindMergeableGroundStack(ResourceKind.Stone, designation.Target);
            if (existing is null)
            {
                AllocateItemStack(
                    ResourceKind.Stone,
                    stoneQuantity,
                    ItemLocation.OnGround(designation.Target));
            }
            else
            {
                existing.Quantity = checked(existing.Quantity + stoneQuantity);
            }
            GainBuildingExperience(actor, Math.Max(16, stoneQuantity));
        }

        _workDesignations.Remove(designation.Id);
        Publish(SimulationEventKind.WorkDesignationRemoved, actor.Id, designation.Id, 0);
        actor.ClearJob();
    }

    private Dictionary<EntityId, int> CreateConstructionReservations()
    {
        var reservations = new Dictionary<EntityId, int>();
        foreach (var actor in _actors.Values.Where(actor =>
                     actor.JobKind == ActorJobKind.SupplyConstruction))
        {
            reservations[actor.DestinationZoneId] = checked(
                reservations.GetValueOrDefault(actor.DestinationZoneId) + actor.ReservedQuantity);
        }

        return reservations;
    }

    private bool TryPlanConstructionSupply(
        ActorState actor,
        Dictionary<EntityId, int> sourceReservations,
        Dictionary<EntityId, int> constructionReservations)
    {
        HaulPlan? best = null;
        var nearbySourceIds = _resourceSpatialIndex.FindNearestStackIds(
            ResourceKind.Wood,
            actor.Position,
            MaximumConstructionRouteCandidatesPerPlanningTick * 4);
        foreach (var priority in Enum.GetValues<StoragePriority>().OrderDescending())
        {
            var candidates = (
                    from site in _constructionSites.Values
                    where site.Priority == priority
                    let missing = site.MissingWood -
                        constructionReservations.GetValueOrDefault(site.Id)
                    where missing > 0
                    from sourceId in nearbySourceIds
                    let source = _itemStacks[sourceId]
                    let available = source.Quantity - sourceReservations.GetValueOrDefault(source.Id)
                    where available > 0
                    let estimatedDistance =
                        ManhattanDistance(actor.Position, source.Location.Position) +
                        ManhattanDistance(source.Location.Position, site.Anchor)
                    orderby estimatedDistance, site.Id, source.Id
                    select new { Site = site, Source = source, Available = available })
                .Take(MaximumConstructionRouteCandidatesPerPlanningTick);
            foreach (var candidate in candidates)
            {
                var routeToSource = Navigation.FindSurfacePath(
                    actor.Position,
                    candidate.Source.Location.Position);
                var routeToSite = FindConstructionAccessPath(
                    candidate.Source.Location.Position,
                    candidate.Site);
                if (routeToSource is null || routeToSite is null)
                {
                    continue;
                }

                var missing = candidate.Site.MissingWood -
                    constructionReservations.GetValueOrDefault(candidate.Site.Id);
                var quantity = Math.Min(
                    Definitions.ActorCarryCapacity,
                    Math.Min(candidate.Available, missing));
                best = new HaulPlan(
                    candidate.Source.Id,
                    candidate.Site.Id,
                    quantity,
                    routeToSource,
                    StoragePriority.Normal,
                    candidate.Site.Priority,
                    checked(routeToSource.Count + routeToSite.Count));
                break;
            }

            if (best is not null)
            {
                break;
            }
        }

        if (best is null)
        {
            return false;
        }

        var plan = best.Value;
        actor.JobKind = ActorJobKind.SupplyConstruction;
        actor.JobStage = ActorJobStage.Collecting;
        actor.SourceStackId = plan.SourceStackId;
        actor.DestinationZoneId = plan.DestinationZoneId;
        actor.ReservedQuantity = plan.Quantity;
        actor.JobTarget = _itemStacks[plan.SourceStackId].Location.Position;
        BeginJobLeg(actor, plan.Route, Definitions.HaulHandlingTicks);
        sourceReservations[plan.SourceStackId] = checked(
            sourceReservations.GetValueOrDefault(plan.SourceStackId) + plan.Quantity);
        constructionReservations[plan.DestinationZoneId] = checked(
            constructionReservations.GetValueOrDefault(plan.DestinationZoneId) + plan.Quantity);
        return true;
    }

    private bool TryPlanCarriedConstructionDelivery(
        ActorState actor,
        Dictionary<EntityId, int> constructionReservations)
    {
        if (!_itemStacks.TryGetValue(actor.CarriedStackId, out var carried) ||
            carried.Resource != ResourceKind.Wood)
        {
            return false;
        }

        var best = _constructionSites.Values
            .Where(site => site.MissingWood - constructionReservations.GetValueOrDefault(site.Id) >=
                carried.Quantity)
            .Select(site => new
            {
                Site = site,
                Route = FindConstructionAccessPath(actor.Position, site),
            })
            .Where(candidate => candidate.Route is not null)
            .OrderByDescending(candidate => candidate.Site.Priority)
            .ThenBy(candidate => candidate.Route!.Count)
            .ThenBy(candidate => candidate.Site.Id)
            .FirstOrDefault();
        if (best is null)
        {
            return false;
        }

        actor.JobKind = ActorJobKind.SupplyConstruction;
        actor.JobStage = ActorJobStage.Delivering;
        actor.SourceStackId = EntityId.None;
        actor.DestinationZoneId = best.Site.Id;
        actor.ReservedQuantity = carried.Quantity;
        actor.JobTarget = best.Route!.Count == 0 ? actor.Position : best.Route[^1];
        BeginJobLeg(actor, best.Route, Definitions.HaulHandlingTicks);
        constructionReservations[best.Site.Id] = checked(
            constructionReservations.GetValueOrDefault(best.Site.Id) + carried.Quantity);
        return true;
    }

    private void UpdateConstructionSupplyJob(ActorState actor)
    {
        if (!_constructionSites.TryGetValue(actor.DestinationZoneId, out var site) ||
            actor.ReservedQuantity <= 0 ||
            site.MissingWood < actor.ReservedQuantity)
        {
            actor.ClearJob();
            return;
        }

        if (actor.JobStage == ActorJobStage.Collecting)
        {
            if (actor.CarriedStackId != EntityId.None ||
                !_itemStacks.TryGetValue(actor.SourceStackId, out var source) ||
                source.Resource != ResourceKind.Wood ||
                source.Location.Kind is not (ItemLocationKind.Ground or ItemLocationKind.StorageZone) ||
                source.Quantity < actor.ReservedQuantity ||
                source.Location.Position != actor.JobTarget)
            {
                actor.ClearJob();
                return;
            }
        }
        else if (actor.JobStage == ActorJobStage.Delivering)
        {
            if (!_itemStacks.TryGetValue(actor.CarriedStackId, out var carried) ||
                carried.Resource != ResourceKind.Wood ||
                carried.Quantity != actor.ReservedQuantity ||
                carried.Location != ItemLocation.CarriedBy(actor.Id))
            {
                actor.ClearJob();
                return;
            }
        }
        else
        {
            actor.ClearJob();
            return;
        }

        if (actor.JobPhase == ActorJobPhase.Traveling)
        {
            AdvanceTravel(actor);
        }
        if (actor.JobKind != ActorJobKind.SupplyConstruction ||
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
            CompleteConstructionCollection(actor, site);
        }
        else
        {
            CompleteConstructionDelivery(actor, site);
        }
    }

    private void CompleteConstructionCollection(ActorState actor, ConstructionSiteState site)
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
                source.FoodKind);
        }

        MoveItemStack(carried, ItemLocation.CarriedBy(actor.Id));
        actor.CarriedStackId = carried.Id;
        actor.SourceStackId = EntityId.None;
        actor.JobStage = ActorJobStage.Delivering;
        var route = FindConstructionAccessPath(actor.Position, site);
        if (route is null)
        {
            actor.ClearJob();
            return;
        }

        actor.JobTarget = route.Count == 0 ? actor.Position : route[^1];
        BeginJobLeg(actor, route, Definitions.HaulHandlingTicks);
        Publish(SimulationEventKind.ItemPickedUp, actor.Id, carried.Id, carried.Quantity);
    }

    private void CompleteConstructionDelivery(ActorState actor, ConstructionSiteState site)
    {
        var carried = _itemStacks[actor.CarriedStackId];
        var delivered = carried.Quantity;
        RemoveItemStack(carried.Id);
        actor.CarriedStackId = EntityId.None;
        site.DeliveredWood = checked(site.DeliveredWood + delivered);
        GainHaulingExperience(actor, Math.Max(1, delivered * 2));
        Publish(SimulationEventKind.ConstructionMaterialDelivered, actor.Id, site.Id, delivered);
        actor.ClearJob();
    }

    private bool TryPlanConstructionWork(ActorState actor)
    {
        var reservedSites = _actors.Values
            .Where(candidate => candidate.JobKind == ActorJobKind.BuildConstruction)
            .Select(candidate => candidate.DestinationZoneId)
            .ToHashSet();
        var best = _constructionSites.Values
            .Where(site => site.HasAllMaterials &&
                !reservedSites.Contains(site.Id) &&
                CanActorBuild(actor, site))
            .Select(site => new
            {
                Site = site,
                Route = FindConstructionAccessPath(actor.Position, site),
            })
            .Where(candidate => candidate.Route is not null)
            .OrderByDescending(candidate => candidate.Site.Priority)
            .ThenBy(candidate => candidate.Route!.Count)
            .ThenBy(candidate => candidate.Site.Id)
            .FirstOrDefault();
        if (best is null)
        {
            return false;
        }

        actor.JobKind = ActorJobKind.BuildConstruction;
        actor.DestinationZoneId = best.Site.Id;
        actor.JobTarget = best.Route!.Count == 0 ? actor.Position : best.Route[^1];
        BeginJobLeg(actor, best.Route, best.Site.RemainingWorkTicks);
        return true;
    }

    private void UpdateConstructionBuildJob(ActorState actor)
    {
        if (!_constructionSites.TryGetValue(actor.DestinationZoneId, out var site) ||
            !site.HasAllMaterials ||
            !CanActorBuild(actor, site))
        {
            actor.ClearJob();
            return;
        }

        if (actor.JobPhase == ActorJobPhase.Traveling)
        {
            AdvanceTravel(actor);
        }
        if (actor.JobKind != ActorJobKind.BuildConstruction ||
            actor.JobPhase != ActorJobPhase.Working)
        {
            return;
        }

        actor.RemainingWorkTicks--;
        site.RemainingWorkTicks--;
        if (site.RemainingWorkTicks > 0)
        {
            return;
        }

        if (!CompleteConstruction(actor, site))
        {
            site.RemainingWorkTicks = 1;
        }
        actor.ClearJob();
    }

    private bool CanActorBuild(ActorState actor, ConstructionSiteState site) =>
        (actor.KnownSkills & site.Capabilities.RequiredSkills) == site.Capabilities.RequiredSkills &&
        (actor.Equipment & site.Capabilities.RequiredEquipment) ==
            site.Capabilities.RequiredEquipment &&
        GoblinExperienceSnapshot.GetLevel(actor.BuildingExperience) >=
            site.Capabilities.MinimumBuildingLevel;

    private IReadOnlyList<GridPosition>? FindConstructionAccessPath(
        GridPosition start,
        ConstructionSiteState site)
    {
        if (site.Anchor.Z != 0)
        {
            return null;
        }

        if (site.Kind is ConstructionKind.FoodStorage or ConstructionKind.WoodStorage or
            ConstructionKind.StoneStorage or
            ConstructionKind.GoblinFieldCamp)
        {
            return Navigation.FindSurfacePath(start, site.Anchor);
        }

        var footprint = site.GetFootprint();
        var accessPositions = site.Kind is ConstructionKind.WoodenWall or ConstructionKind.WoodenDoor
            ? footprint.SelectMany(Map.GetCardinalNeighbors)
            : footprint.SelectMany(position => Map.GetCardinalNeighbors(position).Append(position));
        return accessPositions
            .Where(World.IsSurfaceTraversable)
            .Distinct()
            .Select(position => new
            {
                Position = position,
                Route = Navigation.FindSurfacePath(start, position),
            })
            .Where(candidate => candidate.Route is not null)
            .OrderBy(candidate => candidate.Route!.Count)
            .ThenBy(candidate => candidate.Position.Y)
            .ThenBy(candidate => candidate.Position.X)
            .Select(candidate => candidate.Route)
            .FirstOrDefault();
    }

    private bool TryPlanHaulCollection(
        ActorState actor,
        Dictionary<EntityId, int> sourceReservations,
        Dictionary<EntityId, int> destinationReservations,
        EntityId? requiredDestination = null,
        bool assignedDestinationsOnly = false)
    {
        HaulPlan? best = null;
        foreach (var source in _itemStacks.Values.Where(stack =>
                     stack.Location.Kind is ItemLocationKind.Ground or ItemLocationKind.StorageZone &&
                     Visibility.Get(stack.Location.Position) != CellVisibility.Unknown))
        {
            var availableSource = GetAvailableSourceQuantity(source, sourceReservations);
            if (availableSource <= 0)
            {
                continue;
            }

            var designationKind = source.Resource switch
            {
                ResourceKind.Wood => WorkDesignationKind.GatherBrushwood,
                ResourceKind.Stone => WorkDesignationKind.GatherStone,
                _ => default,
            };
            var isDesignatedLooseResource = designationKind != default &&
                source.Location.Kind == ItemLocationKind.Ground &&
                IsWorkDesignated(designationKind, source.Id, source.Location.Position);
            var candidateZones = _storageZones.Values.Where(zone =>
                         ZoneAccepts(zone, source.Resource) &&
                         IsHaulerAllowedForZone(actor, zone) &&
                         IsSourceAllowedForZone(source, zone) &&
                         CanStoreStack(zone, source, 1) &&
                         (requiredDestination is null || zone.Id == requiredDestination.Value) &&
                         (!assignedDestinationsOnly || zone.AssignedHaulerId == actor.Id))
                .Where(zone =>
                {
                    var stored = GetStoredQuantity(zone.Id);
                    var reservedDestination = destinationReservations.GetValueOrDefault(zone.Id);
                    return isDesignatedLooseResource ||
                        zone.DesiredQuantity > stored + reservedDestination;
                })
                .ToArray();
            if (candidateZones.Length == 0)
            {
                continue;
            }

            var routeToSource = Navigation.FindSurfacePath(actor.Position, source.Location.Position);
            if (routeToSource is null)
            {
                continue;
            }

            foreach (var zone in candidateZones)
            {
                if (source.Location.Kind == ItemLocationKind.StorageZone &&
                    source.Location.OwnerId == zone.Id)
                {
                    continue;
                }
                var stored = GetStoredQuantity(zone.Id);
                var reservedDestination = destinationReservations.GetValueOrDefault(zone.Id);
                var isPulledByStorage = zone.DesiredQuantity > stored + reservedDestination;
                var destinationLimit = isDesignatedLooseResource
                    ? zone.Capacity
                    : Math.Min(zone.Capacity, zone.DesiredQuantity);
                var availableDestination = Math.Min(
                    destinationLimit - stored - reservedDestination,
                    GetAvailableStorageQuantity(zone, source));
                if (availableDestination <= 0)
                {
                    continue;
                }

                var routeToDestination = Navigation.FindSurfacePath(source.Location.Position, zone.Position);
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
                    GetResourcePriority(source.Resource),
                    zone.Priority,
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

    private bool HasAssignedStorageDuty(EntityId actorId) =>
        _storageZones.Values.Any(zone => zone.AssignedHaulerId == actorId);

    private bool IsBackgroundPlanningTick(ActorState actor)
    {
        var interval = Definitions.ActorMovementIntervalTicks;
        return CurrentTick.Value % interval == (long)(actor.Id.Value % (ulong)interval);
    }

    private void RemoveExhaustedWorkDesignations()
    {
        var plants = World.CreatePlantSnapshot();
        var completed = _workDesignations.Values
            .Where(designation => designation.Kind switch
            {
                WorkDesignationKind.GatherFood => !plants.Any(plant =>
                    plant.Biomass > 0 && designation.Matches(plant.Position)),
                WorkDesignationKind.GatherBrushwood =>
                    !_itemStacks.TryGetValue(designation.TargetEntityId, out var stack) ||
                    stack.Resource != ResourceKind.Wood ||
                    stack.Location.Kind != ItemLocationKind.Ground,
                WorkDesignationKind.GatherStone =>
                    !_itemStacks.TryGetValue(designation.TargetEntityId, out var stone) ||
                    stone.Resource != ResourceKind.Stone ||
                    stone.Location.Kind != ItemLocationKind.Ground,
                WorkDesignationKind.UprootBerryBush => !plants.Any(plant =>
                    plant.Kind == PlantKind.BerryBush && designation.Matches(plant.Position)),
                WorkDesignationKind.FellTree => World.GetFellableWood(designation.Target) is null,
                WorkDesignationKind.QuarryBoulder =>
                    World.GetQuarriableBoulder(designation.Target) is null,
                _ => false,
            })
            .Select(designation => designation.Id)
            .ToArray();
        foreach (var id in completed)
        {
            _workDesignations.Remove(id);
            Publish(SimulationEventKind.WorkDesignationRemoved, EntityId.None, id, 0);
        }
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
                zone.SourceStorageZoneId == EntityId.None &&
                IsHaulerAllowedForZone(actor, zone) &&
                CanStoreStack(zone, carried, carried.Quantity) &&
                zone.Capacity - GetStoredQuantity(zone.Id) -
                    destinationReservations.GetValueOrDefault(zone.Id) >= carried.Quantity)
            .Select(zone => new
            {
                Zone = zone,
                Route = Navigation.FindSurfacePath(actor.Position, zone.Position),
            })
            .Where(candidate => candidate.Route is not null)
            .OrderByDescending(candidate => candidate.Zone.Priority)
            .ThenBy(candidate => candidate.Route!.Count)
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
            !IsHaulerAllowedForZone(actor, zone) ||
            actor.ReservedQuantity <= 0)
        {
            return false;
        }

        if (actor.JobStage == ActorJobStage.Collecting)
        {
            if (actor.CarriedStackId != EntityId.None ||
                !_itemStacks.TryGetValue(actor.SourceStackId, out var source) ||
                source.Location.Kind is not (ItemLocationKind.Ground or
                    ItemLocationKind.StorageZone) ||
                !IsSourceAllowedForZone(source, zone))
            {
                return false;
            }

            var protectedAtSource = source.Location.Kind == ItemLocationKind.StorageZone &&
                _storageZones.TryGetValue(source.Location.OwnerId, out var sourceZone)
                    ? Math.Max(0, sourceZone.DesiredQuantity -
                        (GetStoredQuantity(sourceZone.Id) - source.Quantity))
                    : 0;
            return source.Quantity - protectedAtSource >= actor.ReservedQuantity &&
                CanStoreStack(zone, source, actor.ReservedQuantity);
        }

        return actor.JobStage == ActorJobStage.Delivering &&
            actor.SourceStackId == EntityId.None &&
            actor.CarriedStackId != EntityId.None &&
            _itemStacks.TryGetValue(actor.CarriedStackId, out var carried) &&
            carried.Location == ItemLocation.CarriedBy(actor.Id) &&
            carried.Quantity == actor.ReservedQuantity &&
            CanStoreStack(zone, carried, actor.ReservedQuantity);
    }

    private void CompleteHaulCollection(ActorState actor)
    {
        var source = _itemStacks[actor.SourceStackId];
        ItemStackState carried;
        if (source.Quantity == actor.ReservedQuantity)
        {
            carried = source;
            MoveItemStack(carried, ItemLocation.CarriedBy(actor.Id));
        }
        else
        {
            source.Quantity -= actor.ReservedQuantity;
            carried = AllocateItemStack(
                source.Resource,
                actor.ReservedQuantity,
                ItemLocation.CarriedBy(actor.Id),
                source.FoodKind);
        }

        actor.CarriedStackId = carried.Id;
        if (carried.Resource == ResourceKind.Wood)
        {
            GainForagingExperience(actor, Math.Max(1, carried.Quantity));
        }
        actor.SourceStackId = EntityId.None;
        actor.JobStage = ActorJobStage.Delivering;
        var destination = _storageZones[actor.DestinationZoneId];
        var route = Navigation.FindSurfacePath(actor.Position, destination.Position);
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
            !CanStoreStack(zone, carried, carried.Quantity))
        {
            actor.ClearJob();
            return;
        }

        actor.CarriedStackId = EntityId.None;
        var deliveredQuantity = carried.Quantity;
        var stored = StoreStackInZone(carried, zone);
        GainHaulingExperience(actor, Math.Max(1, deliveredQuantity * 2));
        Publish(SimulationEventKind.ItemStored, actor.Id, stored.Id, deliveredQuantity);
        actor.ClearJob();
    }

    private static bool IsHaulerAllowedForZone(ActorState actor, StorageZoneState zone) =>
        zone.AssignedHaulerId == EntityId.None || zone.AssignedHaulerId == actor.Id;

    private static bool IsSourceAllowedForZone(ItemStackState source, StorageZoneState zone) =>
        zone.SourceStorageZoneId == EntityId.None ||
        (source.Location.Kind == ItemLocationKind.StorageZone &&
         source.Location.OwnerId == zone.SourceStorageZoneId);

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
        if (!AreCardinalNeighbors(actor.Position, next))
        {
            actor.ClearJob();
            return;
        }

        if (!World.IsSurfaceTraversable(next) &&
            World.TryGetWoodenDoorState(next, out var isDoorOpen) &&
            !isDoorOpen)
        {
            _undeliveredWorldChanges.Add(World.OpenWoodenDoorForTravel(next, CurrentTick));
            return;
        }

        if (!World.IsSurfaceTraversable(next))
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
            var foodKind = FoodKindFor(World.GetPlantPatch(actor.Position)!.Value.Kind);
            var stack = FindMergeableGroundStack(ResourceKind.Food, actor.Position, foodKind)
                ?? AllocateItemStack(
                    ResourceKind.Food,
                    quantity: 0,
                    ItemLocation.OnGround(actor.Position),
                    foodKind);
            stack.Quantity = checked(stack.Quantity + gathered);
            GainForagingExperience(actor, Math.Max(1, gathered * 2));
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
            case ActorJobKind.ClearVegetation:
                ValidateLoadedClearVegetationJob(actor);
                break;
            case ActorJobKind.FellTree:
                ValidateLoadedFellTreeJob(actor);
                break;
            case ActorJobKind.QuarryBoulder:
                ValidateLoadedQuarryBoulderJob(actor);
                break;
            case ActorJobKind.SupplyConstruction:
                ValidateLoadedConstructionSupplyJob(actor);
                break;
            case ActorJobKind.BuildConstruction:
                ValidateLoadedConstructionBuildJob(actor);
                break;
            case ActorJobKind.Collapsed:
                ValidateLoadedCollapsedJob(actor);
                break;
            default:
                throw new InvalidDataException("The save contains an unsupported actor job.");
        }

        ValidateLoadedJobExecution(actor);
    }

    private static void ValidateLoadedCollapsedJob(ActorState actor)
    {
        if (actor.JobPhase != ActorJobPhase.Working ||
            actor.JobStage != ActorJobStage.None ||
            actor.Position != actor.JobTarget ||
            actor.CarriedStackId != EntityId.None ||
            actor.SourceStackId != EntityId.None ||
            actor.DestinationZoneId != EntityId.None ||
            actor.ReservedQuantity != 0)
        {
            throw new InvalidDataException("The save contains an invalid collapsed actor job.");
        }
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

    private void ValidateLoadedClearVegetationJob(ActorState actor)
    {
        if (actor.JobStage != ActorJobStage.None ||
            actor.CarriedStackId != EntityId.None ||
            actor.SourceStackId != EntityId.None ||
            actor.DestinationZoneId != EntityId.None ||
            actor.ReservedQuantity != 0 ||
            World.GetPlantPatch(actor.JobTarget) is not { Kind: PlantKind.BerryBush })
        {
            throw new InvalidDataException("The save contains an invalid vegetation-clearing job.");
        }
    }

    private void ValidateLoadedFellTreeJob(ActorState actor)
    {
        if (actor.JobStage != ActorJobStage.None ||
            actor.CarriedStackId != EntityId.None ||
            actor.DestinationZoneId != EntityId.None ||
            actor.ReservedQuantity != 0 ||
            !actor.Equipment.HasFlag(PersonalEquipment.WoodenAxe) ||
            !_workDesignations.TryGetValue(actor.SourceStackId, out var designation) ||
            designation.Kind != WorkDesignationKind.FellTree ||
            World.GetFellableWood(designation.Target) is null ||
            !AreCardinalNeighbors(actor.JobTarget, designation.Target))
        {
            throw new InvalidDataException("The save contains an invalid tree-felling job.");
        }
    }

    private void ValidateLoadedQuarryBoulderJob(ActorState actor)
    {
        if (actor.JobStage != ActorJobStage.None ||
            actor.CarriedStackId != EntityId.None ||
            actor.DestinationZoneId != EntityId.None ||
            actor.ReservedQuantity != 0 ||
            !actor.Equipment.HasFlag(PersonalEquipment.PrimitivePickaxe) ||
            !_workDesignations.TryGetValue(actor.SourceStackId, out var designation) ||
            designation.Kind != WorkDesignationKind.QuarryBoulder ||
            World.GetQuarriableBoulder(designation.Target) is null ||
            !AreCardinalNeighbors(actor.JobTarget, designation.Target))
        {
            throw new InvalidDataException("The save contains an invalid boulder-quarrying job.");
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
                source.Location.Kind is not (ItemLocationKind.Ground or ItemLocationKind.StorageZone) ||
                source.Quantity < actor.ReservedQuantity ||
                actor.JobTarget != source.Location.Position ||
                !CanStoreStack(zone, source, actor.ReservedQuantity))
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
                !CanStoreStack(zone, carried, actor.ReservedQuantity))
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

    private void ValidateLoadedConstructionSupplyJob(ActorState actor)
    {
        if (actor.ReservedQuantity <= 0 ||
            actor.ReservedQuantity > Definitions.ActorCarryCapacity ||
            !_constructionSites.TryGetValue(actor.DestinationZoneId, out var site) ||
            site.MissingWood < actor.ReservedQuantity)
        {
            throw new InvalidDataException("The save contains an invalid construction delivery.");
        }

        if (actor.JobStage == ActorJobStage.Collecting)
        {
            if (actor.CarriedStackId != EntityId.None ||
                !_itemStacks.TryGetValue(actor.SourceStackId, out var source) ||
                source.Resource != ResourceKind.Wood ||
                source.Location.Kind is not (ItemLocationKind.Ground or ItemLocationKind.StorageZone) ||
                source.Quantity < actor.ReservedQuantity ||
                actor.JobTarget != source.Location.Position)
            {
                throw new InvalidDataException("The save contains an invalid construction collection.");
            }
        }
        else if (actor.JobStage == ActorJobStage.Delivering)
        {
            if (actor.SourceStackId != EntityId.None ||
                !_itemStacks.TryGetValue(actor.CarriedStackId, out var carried) ||
                carried.Resource != ResourceKind.Wood ||
                carried.Quantity != actor.ReservedQuantity ||
                carried.Location != ItemLocation.CarriedBy(actor.Id))
            {
                throw new InvalidDataException("The save contains invalid carried construction material.");
            }
        }
        else
        {
            throw new InvalidDataException("The save contains an invalid construction delivery stage.");
        }
    }

    private void ValidateLoadedConstructionBuildJob(ActorState actor)
    {
        if (actor.JobStage != ActorJobStage.None ||
            actor.CarriedStackId != EntityId.None ||
            actor.SourceStackId != EntityId.None ||
            actor.ReservedQuantity != 0 ||
            !_constructionSites.TryGetValue(actor.DestinationZoneId, out var site) ||
            !site.HasAllMaterials ||
            !CanActorBuild(actor, site) ||
            (actor.JobPhase == ActorJobPhase.Working &&
             actor.RemainingWorkTicks != site.RemainingWorkTicks))
        {
            throw new InvalidDataException("The save contains an invalid construction work job.");
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
            ActorJobKind.ClearVegetation => GetClearVegetationWorkTicks(),
            ActorJobKind.FellTree => GetFellTreeWorkTicks(),
            ActorJobKind.QuarryBoulder => GetQuarryBoulderWorkTicks(),
            ActorJobKind.SupplyConstruction => Definitions.HaulHandlingTicks,
            ActorJobKind.BuildConstruction when
                _constructionSites.TryGetValue(actor.DestinationZoneId, out var site) =>
                site.TotalWorkTicks,
            ActorJobKind.Collapsed => GetMaximumRestWorkTicks(),
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
            if (!World.IsSurfaceReachable(position) || !AreCardinalNeighbors(previous, position))
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

        var constructionReservations = CreateConstructionReservations();
        foreach (var reservation in constructionReservations)
        {
            if (!_constructionSites.TryGetValue(reservation.Key, out var site) ||
                reservation.Value > site.MissingWood)
            {
                throw new InvalidDataException("Jobs over-reserve construction material demand.");
            }
        }

        var duplicateBuilders = _actors.Values
            .Where(actor => actor.JobKind == ActorJobKind.BuildConstruction)
            .GroupBy(actor => actor.DestinationZoneId)
            .Any(group => group.Count() > 1);
        if (duplicateBuilders)
        {
            throw new InvalidDataException("Multiple builders reserve one construction site.");
        }
    }

    private static bool IsBetter(HaulPlan candidate, HaulPlan current)
    {
        if (candidate.ResourcePriority != current.ResourcePriority)
        {
            return candidate.ResourcePriority > current.ResourcePriority;
        }

        if (candidate.DestinationPriority != current.DestinationPriority)
        {
            return candidate.DestinationPriority > current.DestinationPriority;
        }

        if (candidate.TotalDistance != current.TotalDistance)
        {
            return candidate.TotalDistance < current.TotalDistance;
        }

        return candidate.SourceStackId.Value < current.SourceStackId.Value ||
            (candidate.SourceStackId == current.SourceStackId &&
             candidate.DestinationZoneId.Value < current.DestinationZoneId.Value);
    }

    private int GetJobWorkTicks(ActorState actor) => actor.JobKind switch
    {
        ActorJobKind.Forage => Definitions.ForageWorkTicks,
        ActorJobKind.Haul => Definitions.HaulHandlingTicks,
        ActorJobKind.Rest => GetRestWorkTicks(actor),
        ActorJobKind.Eat => Definitions.EatWorkTicks,
        ActorJobKind.Resupply => Definitions.ResupplyWorkTicks,
        ActorJobKind.ClearVegetation => GetClearVegetationWorkTicks(),
        ActorJobKind.FellTree => GetFellTreeWorkTicks(),
        ActorJobKind.QuarryBoulder => GetQuarryBoulderWorkTicks(),
        ActorJobKind.SupplyConstruction => Definitions.HaulHandlingTicks,
        ActorJobKind.BuildConstruction when
            _constructionSites.TryGetValue(actor.DestinationZoneId, out var site) =>
            site.RemainingWorkTicks,
        ActorJobKind.Collapsed => GetRestWorkTicks(actor),
        _ => throw new InvalidOperationException("An idle actor cannot begin work."),
    };

    private int GetRestWorkTicks(ActorState actor) =>
        Math.Max(1, (actor.Fatigue + Definitions.RestRecoveryPerTick - 1) /
            Definitions.RestRecoveryPerTick);

    private int GetMaximumRestWorkTicks() =>
        (Definitions.MaximumFatigue + Definitions.RestRecoveryPerTick - 1) /
        Definitions.RestRecoveryPerTick;

    private int GetClearVegetationWorkTicks() => checked(Definitions.ForageWorkTicks * 2);

    private int GetFellTreeWorkTicks() => checked(Definitions.ForageWorkTicks * 4);

    private int GetQuarryBoulderWorkTicks() => checked(Definitions.ForageWorkTicks * 6);

    private bool IsRestLocation(GridPosition position) =>
        World.GetWorldObjectsAt(position).Any(worldObject =>
            (worldObject.Kind is WorldObjectKind.GoblinHut or WorldObjectKind.GoblinFieldCamp) &&
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
        StoragePriority ResourcePriority,
        StoragePriority DestinationPriority,
        int TotalDistance);
}
