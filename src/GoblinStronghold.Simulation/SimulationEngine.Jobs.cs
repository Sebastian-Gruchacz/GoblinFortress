using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation;

public sealed partial class SimulationEngine
{
    private const int MaximumConstructionRouteCandidatesPerPlanningTick = 12;
    private const int MaximumHaulRouteCandidatesPerPlanningTick = 1;
    private const int MaximumPublicWorkRouteCandidatesPerPlanningTick = 1;
    private const int MaximumPersonalSupplyRouteCandidates = 8;
    private const int IdleHousekeepingMaximumRouteLength = 8;
    private const int WoodenBucketWaterCapacity = 4;
    private const double JuvenileMaximumHaulUnitWeight = 1.2;
    private const double JuvenileHaulWeightCapacity = 3.0;
    private const int JuvenileHaulFatigueMultiplier = 2;
    private const int SpecialistPublicWorkBonus =
        GoblinWorkPreferences.Maximum - GoblinWorkPreferences.Minimum + 1;
    private const int FastidiousCleaningPreferenceBonus = SpecialistPublicWorkBonus;
    private long[] _lastActorJobStageStopwatchTicks = new long[6];
    private readonly List<ActorPlanningAttemptProfile> _lastPlanningAttempts = [];
    private HashSet<GridPosition>? _shallowWaterSources;
    private int _burstPlannersThisTick;

    public ActorJobUpdateProfile GetLastActorJobUpdateProfile() => new(
        Reproduction: StopwatchTicksToTimeSpan(_lastActorJobStageStopwatchTicks[0]),
        Reservations: StopwatchTicksToTimeSpan(_lastActorJobStageStopwatchTicks[1]),
        NeedInterrupts: StopwatchTicksToTimeSpan(_lastActorJobStageStopwatchTicks[2]),
        IdlePlanning: StopwatchTicksToTimeSpan(_lastActorJobStageStopwatchTicks[3]),
        ActiveJobs: StopwatchTicksToTimeSpan(_lastActorJobStageStopwatchTicks[4]),
        Finalization: StopwatchTicksToTimeSpan(_lastActorJobStageStopwatchTicks[5]));

    public IReadOnlyList<ActorPlanningAttemptProfile> GetLastActorPlanningAttempts() =>
        _lastPlanningAttempts;

    private void UpdateActorJobs()
    {
        _lastPlanningAttempts.Clear();
        _burstPlannersThisTick = 0;
        var stageStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        TryCreateGoblinBud();
        _lastActorJobStageStopwatchTicks[0] =
            System.Diagnostics.Stopwatch.GetTimestamp() - stageStartedAt;
        stageStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var reservedForageTargets = _actors.Values
            .Where(actor => actor.JobKind is ActorJobKind.Forage or ActorJobKind.ClearVegetation)
            .Select(actor => actor.JobTarget)
            .ToHashSet();
        var reservedFellingDesignations = _actors.Values
            .Where(actor => actor.JobKind is ActorJobKind.FellTree or ActorJobKind.QuarryBoulder or
                ActorJobKind.MineRock or ActorJobKind.CarveRamp or ActorJobKind.HuntAnimal or
                ActorJobKind.CleanBlood)
            .Select(actor => actor.SourceStackId)
            .Where(id => id != EntityId.None)
            .ToHashSet();
        var reservedSourceQuantities = CreateHaulReservations(sourceReservations: true);
        var reservedDestinationQuantities = CreateHaulReservations(sourceReservations: false);
        var reservedConstructionQuantities = CreateConstructionReservations();
        var reservedCraftingQuantities = CreateCraftingReservations();
        var activeExplorers = _actors.Values.Count(actor => actor.JobKind == ActorJobKind.Explore);
        var raidPartyIds = _raidPhase is GoblinRaidPhase.Preparing or GoblinRaidPhase.Ready or
            GoblinRaidPhase.Marching or GoblinRaidPhase.Looting or GoblinRaidPhase.Returning
            ? GetRaidParty().Select(actor => actor.Id).ToHashSet()
            : [];
        var fieldCampEvacuees = CreateFieldCampEvacuees();
        _lastActorJobStageStopwatchTicks[1] =
            System.Diagnostics.Stopwatch.GetTimestamp() - stageStartedAt;
        var needInterruptTicks = 0L;
        var idlePlanningTicks = 0L;
        var activeJobTicks = 0L;

        foreach (var actor in _actors.Values)
        {
            if (actor.JobKind == ActorJobKind.Rest && fieldCampEvacuees.Contains(actor.Id))
            {
                actor.ClearJob();
            }
            if (actor.JobKind == ActorJobKind.Collapsed)
            {
                UpdateCollapsedJob(actor);
                continue;
            }

            stageStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            TryInterruptForNeeds(
                actor,
                reservedForageTargets,
                reservedSourceQuantities,
                reservedDestinationQuantities);
            needInterruptTicks += System.Diagnostics.Stopwatch.GetTimestamp() - stageStartedAt;

            stageStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            if (actor.JobKind == ActorJobKind.None)
            {
                var needsFood = actor.Hunger >= Definitions.FoodSeekThreshold &&
                    actor.PersonalFood == 0;
                var needsWater = actor.Thirst >= Definitions.DrinkThreshold && actor.PersonalWater == 0;
                var shouldPlanBackgroundWork = IsBackgroundPlanningTick(actor);
                var reserveForExploration = shouldPlanBackgroundWork &&
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
                else if (needsWater && TryPlanWaterResupply(actor, reservedSourceQuantities))
                {
                    // Water outranks cargo and ordinary work once the carried supply is empty.
                }
                else if (_raidPhase == GoblinRaidPhase.Looting &&
                         raidPartyIds.Contains(actor.Id) &&
                         TryPlanRaidLoot(actor))
                {
                    // Surviving raiders physically recover selected spoils before returning.
                }
                else if (_raidPhase == GoblinRaidPhase.Returning &&
                         raidPartyIds.Contains(actor.Id) &&
                         TryPlanRaidReturn(actor))
                {
                    // Once the aftermath is complete, every survivor returns to the rally camp.
                }
                else if (actor.CarriedStackId != EntityId.None)
                {
                    if (!TryPlanCarriedConstructionDelivery(
                            actor,
                            reservedConstructionQuantities) &&
                        !TryPlanCarriedCraftingDelivery(
                            actor,
                            reservedCraftingQuantities) &&
                        !TryPlanHaulDelivery(actor, reservedDestinationQuantities) &&
                        HasQueuedSpecialistWork(actor))
                    {
                        DropCarriedStack(actor);
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
                else if (fieldCampEvacuees.Contains(actor.Id) &&
                         TryPlanFieldCampDeparture(actor))
                {
                    // Raiders reserve camp beds; excess occupants return to huts or the start area.
                }
                else if (actor.Fatigue >= Definitions.RestThreshold && TryPlanRestJob(actor))
                {
                    // Survival work outranks gathering once the current job has ended.
                }
                else if (IsJuvenile(actor))
                {
                    if (CurrentTick.Value >= actor.DispatcherSuspendedUntilTick &&
                        shouldPlanBackgroundWork)
                    {
                        TryPlanHaulCollection(
                            actor,
                            reservedSourceQuantities,
                            reservedDestinationQuantities);
                    }
                    // Young goblins only help with light transport during their first local season.
                }
                else if (TryPlanCorpseDirective(actor))
                {
                    // A specific carcass order remains available outside the raid lifecycle.
                }
                else if (_raidPhase == GoblinRaidPhase.Marching &&
                         raidPartyIds.Contains(actor.Id) &&
                         TryPlanRaidMarch(actor))
                {
                    // A raider resumes the expedition after satisfying an urgent personal need.
                }
                else if (_raidPhase == GoblinRaidPhase.Ready && raidPartyIds.Contains(actor.Id))
                {
                    // A ready expedition holds its places in camp until explicitly launched.
                }
                else if (TryPlanTacticalOrder(actor))
                {
                    // A persistent personal order resumes after food, water and rest interruptions.
                }
                else if (CurrentTick.Value < actor.DispatcherSuspendedUntilTick)
                {
                    // Personal needs and direct orders remain active while public work is paused.
                }
                else if (!shouldPlanBackgroundWork)
                {
                    // Expensive public planning is staggered between actors instead of repeated every tick.
                }
                else if (TryPlanTendBudJob(actor))
                {
                    // A living bud is finite, already-paid-for work and receives prompt care.
                }
                else if (TryPlanEquipmentResupply(actor, reservedSourceQuantities))
                {
                    // Finished gear remains physical until a goblin collects a missing item.
                }
                else if (actor.PersonalStoneAmmo == 0 &&
                         actor.Equipment.HasFlag(PersonalEquipment.PrimitiveSling) &&
                         TryPlanStoneAmmoResupply(actor, reservedSourceQuantities))
                {
                    // An empty sling is replenished before ordinary settlement work.
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
                else if (GetWorkDesignationPriority(WorkDesignationKind.Scout) >= StoragePriority.High &&
                         activeExplorers < Definitions.MaximumExplorers &&
                         TryPlanExploreJob(actor))
                {
                    activeExplorers++;
                }
                else if (TryPlanPreferredPublicWork(
                             actor,
                             reservedForageTargets,
                             reservedFellingDesignations,
                             reservedSourceQuantities,
                             reservedDestinationQuantities,
                             reservedConstructionQuantities,
                             reservedCraftingQuantities,
                             allowDesignatedForage: !reserveForExploration))
                {
                    // Public priority dominates preference; preference breaks comparable work apart.
                }
                else if (TryPlanFastidiousCleaning(actor, reservedFellingDesignations))
                {
                    // A fastidious goblin may publish one low-priority housekeeping task while idle.
                }
                else if (TryPlanFoodResupply(actor, reservedSourceQuantities))
                {
                    // A small carried ration avoids a trip home for every meal.
                }
                else if (TryPlanWaterResupply(actor, reservedSourceQuantities))
                {
                    // Primitive containers are refilled at accessible shallow water.
                }
                else if (TryPlanStoneAmmoResupply(actor, reservedSourceQuantities))
                {
                    // A few physical stones become personal thrown ammunition.
                }
                else if (TryPlanIdleHousekeeping(
                             actor,
                             reservedFellingDesignations,
                             reservedSourceQuantities,
                             reservedDestinationQuantities))
                {
                    // With no useful work left, tidy a nearby stain or loose stack.
                }
                else if (activeExplorers < Definitions.MaximumExplorers &&
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
            idlePlanningTicks += System.Diagnostics.Stopwatch.GetTimestamp() - stageStartedAt;

            var activeJobKind = actor.JobKind;
            var navigationBeforeActiveJob = Navigation.GetMetrics();
            stageStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            switch (actor.JobKind)
            {
                case ActorJobKind.Forage:
                    UpdateForageJob(actor);
                    break;
                case ActorJobKind.Haul:
                    UpdateHaulJob(actor);
                    break;
                case ActorJobKind.ClearConstructionSite:
                    UpdateConstructionClearanceJob(actor);
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
                case ActorJobKind.MineRock:
                    UpdateMineRockJob(actor);
                    break;
                case ActorJobKind.CarveRamp:
                    UpdateCarveRampJob(actor);
                    break;
                case ActorJobKind.TendBud:
                    UpdateTendBudJob(actor);
                    break;
                case ActorJobKind.HuntAnimal:
                    UpdateHuntAnimalJob(actor);
                    break;
                case ActorJobKind.CleanBlood:
                    UpdateCleanBloodJob(actor);
                    break;
                case ActorJobKind.LootRaid:
                    UpdateRaidLootJob(actor);
                    break;
                case ActorJobKind.RecoverRaidCorpse:
                    UpdateRaidCorpseRecoveryJob(actor);
                    break;
                case ActorJobKind.ConsumeRaidCorpse:
                    UpdateRaidCorpseConsumptionJob(actor);
                    break;
                case ActorJobKind.SupplyConstruction:
                    UpdateConstructionSupplyJob(actor);
                    break;
                case ActorJobKind.BuildConstruction:
                    UpdateConstructionBuildJob(actor);
                    break;
                case ActorJobKind.SupplyCrafting:
                    UpdateCraftingSupplyJob(actor);
                    break;
                case ActorJobKind.Craft:
                    UpdateCraftingWorkJob(actor);
                    break;
                case ActorJobKind.Collapsed:
                    UpdateCollapsedJob(actor);
                    break;
            }
            var activeDuration = System.Diagnostics.Stopwatch.GetTimestamp() - stageStartedAt;
            activeJobTicks += activeDuration;
            var navigationAfterActiveJob = Navigation.GetMetrics();
            if (activeDuration > System.Diagnostics.Stopwatch.Frequency / 1_000 ||
                navigationAfterActiveJob.Searches != navigationBeforeActiveJob.Searches)
            {
                _lastPlanningAttempts.Add(new ActorPlanningAttemptProfile(
                    actor.Id,
                    $"active-{activeJobKind}",
                    StopwatchTicksToTimeSpan(activeDuration),
                    navigationAfterActiveJob.Requests - navigationBeforeActiveJob.Requests,
                    navigationAfterActiveJob.Searches - navigationBeforeActiveJob.Searches,
                    Assigned: false));
            }
        }

        _lastActorJobStageStopwatchTicks[2] = needInterruptTicks;
        _lastActorJobStageStopwatchTicks[3] = idlePlanningTicks;
        _lastActorJobStageStopwatchTicks[4] = activeJobTicks;
        stageStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        FinalizeMatureGoblinBuds();
        TryMarkRaidReady();
        if (CurrentTick.Value % Definitions.ActorPlanning.BackgroundPlanningIntervalTicks == 0)
        {
            RemoveExhaustedWorkDesignations();
        }
        _lastActorJobStageStopwatchTicks[5] =
            System.Diagnostics.Stopwatch.GetTimestamp() - stageStartedAt;
    }

    private bool TryPlanRaidPreparation(
        ActorState actor,
        Dictionary<EntityId, int> sourceReservations,
        Dictionary<EntityId, int> destinationReservations)
    {
        if (actor.CarriedStackId != EntityId.None)
        {
            if (!TryPlanHaulDelivery(actor, destinationReservations))
            {
                DropCarriedStack(actor);
            }
            return true;
        }
        var isAtRally = actor.Position == _raidRallyPoint;
        var isInRallyArea = Distance(actor.Position, _raidRallyPoint) <= 4;
        var preparation = RaidPreparationPolicy.ResolveAutomatic(
            _raidDirectives,
            Definitions,
            actor.Equipment);
        var foodTarget = preparation.FoodTarget;
        var waterTarget = preparation.WaterTarget;
        if (!isInRallyArea && actor.Hunger >= Definitions.FoodSeekThreshold &&
            TryPlanEatJob(actor, sourceReservations))
        {
            return true;
        }
        if (!isInRallyArea && actor.PersonalFood < foodTarget &&
            TryPlanFoodResupply(actor, sourceReservations, desiredQuantity: foodTarget))
        {
            return true;
        }

        if (actor.Hunger >= Definitions.FoodSeekThreshold &&
            TryPlanEatJob(actor, sourceReservations, isInRallyArea ? _raidRallyPoint : null))
        {
            return true;
        }
        if (actor.PersonalFood < foodTarget &&
            TryPlanFoodResupply(
                actor,
                sourceReservations,
                isInRallyArea ? _raidRallyPoint : null,
                foodTarget))
        {
            return true;
        }
        if (actor.PersonalWater < waterTarget &&
            TryPlanWaterResupply(actor, sourceReservations, waterTarget))
        {
            return true;
        }
        if (TryPlanStoneAmmoResupply(actor, sourceReservations, preparation.StoneAmmoTarget))
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
            var route = FindActorPath(actor, _raidRallyPoint);
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

    private void TryMarkRaidReady()
    {
        var raidParty = GetRaidParty();
        if (_raidPhase != GoblinRaidPhase.Preparing || raidParty.Count == 0 ||
            raidParty.Any(actor =>
            {
                var preparation = RaidPreparationPolicy.ResolveAutomatic(
                    _raidDirectives,
                    Definitions,
                    actor.Equipment);
                return actor.Position != _raidRallyPoint ||
                actor.JobKind != ActorJobKind.None ||
                actor.CarriedStackId != EntityId.None ||
                actor.PersonalFood < preparation.FoodTarget ||
                actor.PersonalWater < preparation.WaterTarget ||
                actor.Hunger >= Definitions.FoodSeekThreshold ||
                actor.Thirst >= Definitions.DrinkThreshold ||
                actor.Fatigue >= Definitions.RestThreshold;
            }))
        {
            return;
        }

        _raidPhase = GoblinRaidPhase.Ready;
        if (_raidDirectives.HasFlag(RaidDirective.AutoLaunchWhenReady))
        {
            TryExecuteLaunchRaid();
        }
    }

    private bool TryPlanRaidMarch(ActorState actor)
    {
        var combatTarget = GetRaidCombatTarget();
        if (combatTarget is { } villager)
        {
            var distance = Distance(actor.Position, villager.Position);
            var hasSling = actor.Equipment.HasFlag(PersonalEquipment.PrimitiveSling);
            var rangedRange = hasSling
                ? Definitions.RangedCombat.SlingRange
                : Definitions.RangedCombat.ThrownStoneRange;
            if (distance <= 1 || actor.PersonalStoneAmmo > 0 && distance <= rangedRange)
            {
                return true;
            }

            var approachPositions = World.GetCardinalWorldNeighbors(villager.Position)
                .Where(World.IsTerrainTraversable)
                .ToHashSet();
            var approachRoute = FindActorPathToNearest(actor, approachPositions) ??
                World.GetCardinalWorldNeighbors(actor.Position)
                    .Where(World.IsTerrainTraversable)
                    .OrderBy(position => Distance(position, villager.Position))
                    .ThenBy(position => position.Y)
                    .ThenBy(position => position.X)
                    .Take(1)
                    .ToArray();
            if (approachRoute is null)
            {
                return true;
            }

            var approachDestination = approachRoute.Count == 0
                ? actor.Position
                : approachRoute[^1];
            actor.JobKind = ActorJobKind.Move;
            actor.JobPhase = ActorJobPhase.Traveling;
            actor.JobTarget = approachDestination;
            actor.RemainingRoute.AddRange(approachRoute);
            return true;
        }

        if (actor.Position == _raidTarget)
        {
            return true;
        }

        var request = RequestActorPath(actor, _raidTarget);
        if (request.Status == NavigationPathRequestStatus.Pending)
        {
            return true;
        }
        if (request.Status == NavigationPathRequestStatus.Unreachable ||
            request.Path is not { Count: > 0 } route)
        {
            return true;
        }

        actor.JobKind = ActorJobKind.Move;
        actor.JobPhase = ActorJobPhase.Traveling;
        actor.JobTarget = _raidTarget;
        actor.RemainingRoute.AddRange(route);
        return true;
    }

    private List<ActorState> GetRaidParty() => _raidPartyIds
        .Select(id => _actors.GetValueOrDefault(id))
        .Where(actor => actor is { Health: > 0 })
        .Select(actor => actor!)
        .ToList();

    private bool TryPlanExploreJob(ActorState actor)
    {
        var scoutingTargets = _workDesignations.Values
            .Where(designation => designation.Kind == WorkDesignationKind.Scout &&
                !designation.IsSuspended)
            .Select(designation => designation.Target)
            .Where(position => Visibility.Get(position) == CellVisibility.Unknown)
            .ToHashSet();
        if (Definitions.MaximumExplorers == 0 || scoutingTargets.Count == 0)
        {
            return false;
        }

        var request = RequestActorPathToNearest(
            actor,
            scoutingTargets,
            (_, to) => to.Z >= 0 &&
                (Visibility.Get(to) != CellVisibility.Unknown || scoutingTargets.Contains(to)),
            constraintKey: 1);
        if (request.Status == NavigationPathRequestStatus.Pending)
        {
            return true;
        }
        if (request.Status == NavigationPathRequestStatus.Unreachable ||
            request.Path is not { Count: > 0 } route)
        {
            return false;
        }

        actor.JobKind = ActorJobKind.Explore;
        actor.JobTarget = route[^1];
        actor.JobPhase = ActorJobPhase.Traveling;
        actor.RemainingRoute.AddRange(route);
        return true;
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
        Dictionary<(EntityId OrderId, ResourceKind Resource, ResourceVariant Variant), int>
            craftingReservations,
        bool allowDesignatedForage)
    {
        var constructionSupplyPriority = _constructionSites.Values
            .Where(site => site.MissingQuantity -
                constructionReservations.GetValueOrDefault(site.Id) > 0)
            .Select(site => site.Priority)
            .DefaultIfEmpty(StoragePriority.Low)
            .Max();
        var constructionClearancePriority = _constructionSites.Values
            .Where(HasGroundStackInConstructionFootprint)
            .Select(site => site.Priority)
            .DefaultIfEmpty(StoragePriority.Low)
            .Max();
        var constructionWorkPriority = _constructionSites.Values
            .Where(site => site.HasAllMaterials && !HasGroundStackInConstructionFootprint(site))
            .Select(site => site.Priority)
            .DefaultIfEmpty(StoragePriority.Low)
            .Max();
        var haulingPriority = _storageZones.Values
            .Select(zone => zone.Priority)
            .Concat(_workDesignations.Values
                .Where(designation => designation.Kind is
                    WorkDesignationKind.GatherBrushwood or WorkDesignationKind.GatherStone &&
                    !designation.IsSuspended)
                .Select(designation => designation.Priority))
            .DefaultIfEmpty(StoragePriority.Low)
            .Max();
        var hasCraftingSupply = _craftingOrders.Values.Any(order =>
            CraftingRecipeCatalog.Get(order.Recipe).Materials.Any(material =>
                order.GetMissing(material) - craftingReservations.GetValueOrDefault((
                    order.Id,
                    material.Resource,
                    material.Variant)) > 0));
        var hasCraftingWork = _craftingOrders.Values.Any(order => order.HasAllMaterials);
        var rampPriority = _workDesignations.Values
            .Where(designation => designation.Kind is WorkDesignationKind.CarveRampDown or
                WorkDesignationKind.CarveRampUp && !designation.IsSuspended)
            .Select(designation => designation.Priority)
            .DefaultIfEmpty(StoragePriority.Normal)
            .Max();
        var options = new (string Name, int Score, int Order, Func<bool> TryPlan)[]
        {
            ("construction-clearance", Score(constructionClearancePriority, actor.WorkPreferences.Hauling), 0,
                () => TryPlanConstructionClearance(
                    actor,
                    sourceReservations,
                    destinationReservations)),
            ("construction-supply", Score(constructionSupplyPriority, actor.WorkPreferences.Hauling), 1,
                () => TryPlanConstructionSupply(
                    actor,
                    sourceReservations,
                    constructionReservations)),
            ("construction-work", Score(constructionWorkPriority, actor.WorkPreferences.Building), 2,
                () => TryPlanConstructionWork(actor)),
            ("crafting-supply", Score(hasCraftingSupply ? StoragePriority.Normal : StoragePriority.Low,
                    actor.WorkPreferences.Hauling), 3,
                () => TryPlanCraftingSupply(actor, sourceReservations, craftingReservations)),
            ("crafting-work", Score(hasCraftingWork ? StoragePriority.Normal : StoragePriority.Low,
                    actor.WorkPreferences.Building), 4,
                () => TryPlanCraftingWork(actor)),
            ("hauling", Score(haulingPriority, actor.WorkPreferences.Hauling), 5,
                () => TryPlanHaulCollection(
                    actor,
                    sourceReservations,
                    destinationReservations)),
            ("clear-vegetation", Score(GetWorkDesignationPriority(WorkDesignationKind.UprootBerryBush), actor.WorkPreferences.Foraging), 6,
                () => TryPlanClearVegetationJob(actor, reservedForageTargets)),
            ("fell-tree", Score(GetWorkDesignationPriority(WorkDesignationKind.FellTree),
                    actor.WorkPreferences.Building, specialist: true), 7,
                () => TryPlanFellTreeJob(actor, reservedFellingDesignations)),
            ("quarry-boulder", Score(GetWorkDesignationPriority(WorkDesignationKind.QuarryBoulder),
                    actor.WorkPreferences.Building, specialist: true), 8,
                () => TryPlanQuarryBoulderJob(actor, reservedFellingDesignations)),
            ("mine-rock", Score(GetWorkDesignationPriority(WorkDesignationKind.MineRock),
                    actor.WorkPreferences.Building, specialist: true), 9,
                () => TryPlanMineRockJob(actor, reservedFellingDesignations)),
            ("carve-ramp", Score(rampPriority, actor.WorkPreferences.Building, specialist: true), 10,
                () => TryPlanCarveRampJob(actor, reservedFellingDesignations)),
            ("gather-food", Score(GetWorkDesignationPriority(WorkDesignationKind.GatherFood), actor.WorkPreferences.Foraging), 11,
                () => allowDesignatedForage &&
                    TryPlanForageJob(actor, reservedForageTargets, requireDesignation: true)),
            ("gather-reeds", Score(GetWorkDesignationPriority(WorkDesignationKind.GatherReeds), actor.WorkPreferences.Foraging), 12,
                () => TryPlanForageJob(
                    actor,
                    reservedForageTargets,
                    requireDesignation: true,
                    designationKind: WorkDesignationKind.GatherReeds)),
            ("hunt", Score(GetWorkDesignationPriority(WorkDesignationKind.HuntAnimal),
                    actor.WorkPreferences.Foraging), 13,
                () => TryPlanHuntAnimalJob(actor, reservedFellingDesignations)),
            ("clean-blood", Score(GetWorkDesignationPriority(WorkDesignationKind.CleanBlood),
                    GetCleaningPreference(actor)), 14,
                () => TryPlanCleanBloodJob(actor, reservedFellingDesignations)),
        };
        foreach (var option in options
                     .OrderByDescending(option => option.Score)
                     .ThenBy(option => option.Order))
        {
            var navigationBefore = Navigation.GetMetrics();
            var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            // Incremental path planners can report progress before they have assigned a job.
            // Keep trying fallback categories unless the actor received concrete work.
            var plannerReportedSuccess = option.TryPlan();
            var assigned = plannerReportedSuccess && actor.JobKind != ActorJobKind.None;
            var duration = System.Diagnostics.Stopwatch.GetTimestamp() - startedAt;
            var navigationAfter = Navigation.GetMetrics();
            if (duration > System.Diagnostics.Stopwatch.Frequency / 1_000 ||
                navigationAfter.Searches != navigationBefore.Searches)
            {
                _lastPlanningAttempts.Add(new ActorPlanningAttemptProfile(
                    actor.Id,
                    option.Name,
                    StopwatchTicksToTimeSpan(duration),
                    navigationAfter.Requests - navigationBefore.Requests,
                    navigationAfter.Searches - navigationBefore.Searches,
                    assigned));
            }
            if (assigned)
            {
                return true;
            }
            if (navigationAfter.Searches != navigationBefore.Searches)
            {
                // One new strategic route is enough work for this actor in one simulation tick.
                // A later planning round continues with another category or candidate page.
                return false;
            }
        }
        return false;

        static int Score(
            StoragePriority priority,
            int preference,
            bool specialist = false) => checked(
                (int)priority * 10 + preference +
                (specialist ? SpecialistPublicWorkBonus : 0));
    }

    private StoragePriority GetWorkDesignationPriority(WorkDesignationKind kind) =>
        _workDesignations.Values
            .Where(designation => designation.Kind == kind && !designation.IsSuspended)
            .Select(designation => designation.Priority)
            .DefaultIfEmpty(StoragePriority.Normal)
            .Max();

    private IReadOnlyDictionary<EntityId, ActorPlanEntrySnapshot> CreateFuturePublicWorkPlans()
    {
        var plans = new Dictionary<EntityId, ActorPlanEntrySnapshot>();
        var reserved = _workDesignations.Values
            .Where(designation => _actors.Values.Any(actor =>
                ActorJobMatchesDesignation(actor, designation)))
            .Select(designation => designation.Id)
            .ToHashSet();
        foreach (var actor in _actors.Values
                     .Where(actor => actor.Health > 0 && !IsJuvenile(actor) &&
                         (_raidPhase == GoblinRaidPhase.None || !_raidPartyIds.Contains(actor.Id)))
                     .OrderBy(actor => actor.Id))
        {
            var candidate = _workDesignations.Values
                .Where(designation => !designation.IsSuspended &&
                    !reserved.Contains(designation.Id) &&
                    CanForecastWork(actor, designation))
                .Select(designation => new
                {
                    Designation = designation,
                    Score = checked((int)designation.Priority * 10 +
                        GetForecastWorkPreference(actor, designation.Kind)),
                    Distance = GetForecastDistance(actor.Position, designation.Target),
                })
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Distance)
                .ThenBy(item => item.Designation.Id)
                .FirstOrDefault();
            if (candidate is null)
            {
                continue;
            }

            reserved.Add(candidate.Designation.Id);
            plans.Add(actor.Id, new ActorPlanEntrySnapshot(
                ActorPlanIntentKind.NextPublicWork,
                ToForecastJobKind(candidate.Designation.Kind),
                candidate.Score,
                candidate.Designation.Target)
            {
                WorkOrderId = candidate.Designation.OrderId,
            });
        }
        return plans;
    }

    private bool CanForecastWork(ActorState actor, WorkDesignationSnapshot designation)
    {
        if (designation.Kind == WorkDesignationKind.FellTree &&
            (!actor.Equipment.HasFlag(PersonalEquipment.WoodenAxe) ||
             !actor.KnownSkills.HasFlag(GoblinSkill.Building)))
        {
            return false;
        }
        if (designation.Kind == WorkDesignationKind.QuarryBoulder &&
            (!MiningCapabilityPolicy.HasPickaxe(actor.Equipment) ||
             !actor.KnownSkills.HasFlag(GoblinSkill.Building)))
        {
            return false;
        }
        if (designation.Kind == WorkDesignationKind.MineRock &&
            !CanActorMineRock(actor, designation.Target))
        {
            return false;
        }
        if (designation.Kind is WorkDesignationKind.CarveRampDown or
                WorkDesignationKind.CarveRampUp &&
            !CanActorCarveRamp(actor, designation))
        {
            return false;
        }
        if (designation.Kind is WorkDesignationKind.GatherBrushwood or WorkDesignationKind.GatherStone)
        {
            var resource = designation.Kind == WorkDesignationKind.GatherBrushwood
                ? ResourceKind.Wood
                : ResourceKind.Stone;
            if (!_storageZones.Values.Any(zone =>
                    GetStoredQuantity(zone.Id) < zone.Capacity &&
                    ZoneCategoryAccepts(zone, resource)))
            {
                return false;
            }
        }

        return designation.Kind switch
        {
            WorkDesignationKind.GatherFood =>
                World.GetPlantPatch(designation.Target) is
                    { Kind: not PlantKind.ReedBed, Biomass: > 0 },
            WorkDesignationKind.GatherReeds =>
                World.GetPlantPatch(designation.Target) is
                    { Kind: PlantKind.ReedBed, Biomass: > 0 },
            WorkDesignationKind.GatherBrushwood =>
                _itemStacks.TryGetValue(designation.TargetEntityId, out var wood) &&
                wood.Resource == ResourceKind.Wood &&
                wood.Location.Kind == ItemLocationKind.Ground,
            WorkDesignationKind.GatherStone =>
                _itemStacks.TryGetValue(designation.TargetEntityId, out var stone) &&
                IsMineralResource(stone.Resource) &&
                stone.Location.Kind == ItemLocationKind.Ground,
            WorkDesignationKind.UprootBerryBush =>
                World.GetPlantPatch(designation.Target) is { Kind: PlantKind.BerryBush },
            WorkDesignationKind.FellTree => World.GetFellableWood(designation.Target) is not null,
            WorkDesignationKind.QuarryBoulder =>
                World.GetQuarriableBoulder(designation.Target) is not null,
            WorkDesignationKind.MineRock => CanActorMineRock(actor, designation.Target),
            WorkDesignationKind.CarveRampDown or WorkDesignationKind.CarveRampUp =>
                CanActorCarveRamp(actor, designation),
            WorkDesignationKind.Scout =>
                Visibility.Get(designation.Target) == CellVisibility.Unknown,
            WorkDesignationKind.HuntAnimal =>
                _animals.ContainsKey(designation.TargetEntityId.Value),
            WorkDesignationKind.CleanBlood => HasCleanableBlood(designation.Target),
            _ => false,
        };
    }

    private static int GetForecastWorkPreference(
        ActorState actor,
        WorkDesignationKind kind) => kind switch
    {
        WorkDesignationKind.GatherBrushwood or WorkDesignationKind.GatherStone =>
            actor.WorkPreferences.Hauling,
        WorkDesignationKind.FellTree or WorkDesignationKind.QuarryBoulder or
            WorkDesignationKind.MineRock or WorkDesignationKind.CarveRampDown or
            WorkDesignationKind.CarveRampUp =>
            actor.WorkPreferences.Building + SpecialistPublicWorkBonus,
        WorkDesignationKind.Scout => actor.KnownSkills.HasFlag(GoblinSkill.Scouting) ? 2 : 0,
        WorkDesignationKind.CleanBlood => GetCleaningPreference(actor),
        _ => actor.WorkPreferences.Foraging,
    };

    private static int GetCleaningPreference(ActorState actor) => checked(
        actor.WorkPreferences.Hauling +
        (actor.KnownTraits.HasFlag(GoblinTrait.Fastidious)
            ? FastidiousCleaningPreferenceBonus
            : 0));

    private static int GetForecastDistance(GridPosition first, GridPosition second) =>
        Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y) +
        Math.Abs(first.Z - second.Z) * 8;

    private static ActorJobKind ToForecastJobKind(WorkDesignationKind kind) => kind switch
    {
        WorkDesignationKind.GatherFood or WorkDesignationKind.GatherReeds => ActorJobKind.Forage,
        WorkDesignationKind.GatherBrushwood or WorkDesignationKind.GatherStone => ActorJobKind.Haul,
        WorkDesignationKind.UprootBerryBush => ActorJobKind.ClearVegetation,
        WorkDesignationKind.FellTree => ActorJobKind.FellTree,
        WorkDesignationKind.QuarryBoulder => ActorJobKind.QuarryBoulder,
        WorkDesignationKind.MineRock => ActorJobKind.MineRock,
        WorkDesignationKind.CarveRampDown or WorkDesignationKind.CarveRampUp =>
            ActorJobKind.CarveRamp,
        WorkDesignationKind.Scout => ActorJobKind.Explore,
        WorkDesignationKind.HuntAnimal => ActorJobKind.HuntAnimal,
        WorkDesignationKind.CleanBlood => ActorJobKind.CleanBlood,
        _ => ActorJobKind.None,
    };

    private IReadOnlyList<ActorPlanEntrySnapshot> CreateActorPlanSnapshot(
        ActorState actor,
        ActorPlanEntrySnapshot? futurePublicWork)
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

        if (futurePublicWork is { } future)
        {
            entries.Add((future, 5));
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
            actor.PersonalFood > 0 ||
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
            FindActorPath(actor, stack.Location.Position) is not null);
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
                    item.Part.Kind is WorldObjectPartKind.Floor or WorldObjectPartKind.Door &&
                    World.IsTerrainTraversable(item.Position) &&
                    FindActorPath(actor, item.Position) is not null))
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
            !HasReachableWaterSource(actor, itemReservations))
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
        var shelters = World.CreateWorldObjectSnapshot()
            .Where(worldObject => worldObject.Owner == WorldObjectOwner.GoblinTribe)
            .ToArray();
        var destinations = shelters
            .Where(worldObject => worldObject.Kind == WorldObjectKind.GoblinHut)
            .SelectMany(GetShelterFloorCells)
            .Concat(shelters
                .Where(worldObject => worldObject.Kind == WorldObjectKind.GoblinFieldCamp &&
                    CanReserveFieldCampBed(actor, worldObject))
                .SelectMany(GetShelterFloorCells))
            .Where(item =>
                World.IsTerrainTraversable(item))
            .Distinct()
            .ToHashSet();
        var route = FindActorPathToNearest(actor, destinations);
        if (route is null)
        {
            return false;
        }

        actor.JobKind = ActorJobKind.Rest;
        actor.JobTarget = route.Count == 0 ? actor.Position : route[^1];
        BeginJobLeg(actor, route, GetRestWorkTicks(actor));
        return true;
    }

    private HashSet<EntityId> CreateFieldCampEvacuees()
    {
        var result = new HashSet<EntityId>();
        foreach (var camp in World.CreateWorldObjectSnapshot().Where(worldObject =>
                     worldObject.Kind == WorldObjectKind.GoblinFieldCamp &&
                     worldObject.Owner == WorldObjectOwner.GoblinTribe))
        {
            var floorCells = GetShelterFloorCells(camp).ToHashSet();
            var prioritizedRaiders = IsFieldCampReservedForRaid(camp)
                ? _raidPartyIds.Where(id =>
                        _actors.TryGetValue(id, out var raider) && raider.Health > 0)
                    .ToHashSet()
                : [];
            var availableCivilianBeds = Math.Max(
                0,
                SimulationDefinitions.FieldCampCapacity - prioritizedRaiders.Count);
            var civilianOccupants = _actors.Values
                .Where(candidate => candidate.Health > 0 &&
                    floorCells.Contains(candidate.Position) &&
                    !prioritizedRaiders.Contains(candidate.Id))
                .OrderBy(candidate => candidate.Id)
                .ToArray();
            foreach (var occupant in civilianOccupants.Skip(availableCivilianBeds))
            {
                result.Add(occupant.Id);
            }
        }

        return result;
    }

    private bool TryPlanFieldCampDeparture(ActorState actor)
    {
        var worldObjects = World.CreateWorldObjectSnapshot();
        var destinations = worldObjects
            .Where(worldObject => worldObject.Kind == WorldObjectKind.GoblinHut &&
                worldObject.Owner == WorldObjectOwner.GoblinTribe)
            .SelectMany(GetShelterFloorCells)
            .Where(World.IsTerrainTraversable)
            .Distinct()
            .ToHashSet();
        var route = FindActorPathToNearest(actor, destinations);
        if (route is null)
        {
            var campCells = worldObjects
                .Where(worldObject => worldObject.Kind == WorldObjectKind.GoblinFieldCamp &&
                    worldObject.Owner == WorldObjectOwner.GoblinTribe)
                .SelectMany(GetShelterFloorCells)
                .ToHashSet();
            var startArea = Enumerable.Range(-3, 7)
                .SelectMany(y => Enumerable.Range(-3, 7)
                    .Select(x => new GridPosition(
                        Map.GoblinSpawn.X + x,
                        Map.GoblinSpawn.Y + y,
                        Map.GoblinSpawn.Z)))
                .Where(position => !campCells.Contains(position) &&
                    World.IsTerrainTraversable(position))
                .ToHashSet();
            route = FindActorPathToNearest(actor, startArea);
        }
        if (route is not { Count: > 0 })
        {
            return false;
        }

        actor.JobKind = ActorJobKind.Move;
        actor.JobTarget = route[^1];
        BeginJobLeg(actor, route, workTicks: 0);
        return true;
    }

    private bool CanReserveFieldCampBed(ActorState actor, WorldObjectSnapshot camp)
    {
        var floorCells = GetShelterFloorCells(camp).ToHashSet();
        var prioritizedRaiders = IsFieldCampReservedForRaid(camp)
            ? _raidPartyIds.Where(id =>
                    _actors.TryGetValue(id, out var raider) && raider.Health > 0)
                .ToHashSet()
            : [];
        if (prioritizedRaiders.Contains(actor.Id))
        {
            return true;
        }

        var availableCivilianBeds = Math.Max(
            0,
            SimulationDefinitions.FieldCampCapacity - prioritizedRaiders.Count);
        var civilianReservations = _actors.Values.Count(candidate =>
            candidate.Health > 0 && !prioritizedRaiders.Contains(candidate.Id) &&
            (floorCells.Contains(candidate.Position) ||
             candidate.JobKind == ActorJobKind.Rest && floorCells.Contains(candidate.JobTarget)));
        return civilianReservations < availableCivilianBeds ||
            floorCells.Contains(actor.Position);
    }

    private bool IsFieldCampReservedForRaid(WorldObjectSnapshot camp) =>
        camp.Anchor == _raidRallyPoint &&
        _raidPhase is GoblinRaidPhase.Preparing or GoblinRaidPhase.Ready or
            GoblinRaidPhase.Returning;

    private static IEnumerable<GridPosition> GetShelterFloorCells(
        WorldObjectSnapshot worldObject) => worldObject.GetAbsoluteParts()
        .Where(item => item.Part.Kind is WorldObjectPartKind.Floor or WorldObjectPartKind.Door)
        .Select(item => item.Position)
        .Distinct();

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

        if (kind == ActorJobKind.None || !World.IsTerrainTraversable(target))
        {
            return false;
        }

        var route = FindActorPath(actor, target);
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
            case ActorJobKind.CleanBlood:
                var cleaningDesignation = _workDesignations.Values
                    .Where(item => item.Kind == WorkDesignationKind.CleanBlood &&
                        !item.IsSuspended && item.Target == target && HasCleanableBlood(target))
                    .OrderBy(item => item.Id)
                    .FirstOrDefault();
                if (cleaningDesignation == default)
                {
                    return false;
                }
                actor.JobKind = kind;
                actor.JobTarget = target;
                actor.SourceStackId = cleaningDesignation.Id;
                BeginJobLeg(actor, route, BloodCleaningWorkTicks);
                return true;
            case ActorJobKind.FellTree:
                if (!actor.Equipment.HasFlag(PersonalEquipment.WoodenAxe))
                {
                    return false;
                }
                var designation = _workDesignations.Values
                    .Where(item => item.Kind == WorkDesignationKind.FellTree &&
                        !item.IsSuspended &&
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
                if (!MiningCapabilityPolicy.HasPickaxe(actor.Equipment))
                {
                    return false;
                }
                var quarryDesignation = _workDesignations.Values
                    .Where(item => item.Kind == WorkDesignationKind.QuarryBoulder &&
                        !item.IsSuspended &&
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
            case ActorJobKind.MineRock:
                if (!MiningCapabilityPolicy.HasPickaxe(actor.Equipment))
                {
                    return false;
                }
                var miningDesignation = _workDesignations.Values
                    .Where(item => item.Kind == WorkDesignationKind.MineRock &&
                        !item.IsSuspended &&
                        CanActorMineRock(actor, item.Target) &&
                        AreCardinalNeighbors(target, item.Target))
                    .OrderBy(item => item.Id)
                    .FirstOrDefault();
                if (miningDesignation == default)
                {
                    return false;
                }
                actor.JobKind = kind;
                actor.JobTarget = target;
                actor.SourceStackId = miningDesignation.Id;
                BeginJobLeg(actor, route, GetMineRockWorkTicks(actor));
                return true;
            case ActorJobKind.CarveRamp:
                if (!MiningCapabilityPolicy.HasPickaxe(actor.Equipment))
                {
                    return false;
                }
                var rampDesignation = _workDesignations.Values
                    .Where(item => item.Kind is WorkDesignationKind.CarveRampDown or
                            WorkDesignationKind.CarveRampUp &&
                        !item.IsSuspended &&
                        item.Target == target &&
                        CanActorCarveRamp(actor, item))
                    .OrderBy(item => item.Id)
                    .FirstOrDefault();
                if (rampDesignation == default)
                {
                    return false;
                }
                actor.JobKind = kind;
                actor.JobTarget = target;
                actor.SourceStackId = rampDesignation.Id;
                BeginJobLeg(actor, route, GetCarveRampWorkTicks(actor));
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
                  actor.JobStage is ActorJobStage.ProvisioningFood or
                      ActorJobStage.ProvisioningWater or
                      ActorJobStage.ProvisioningAmmo or
                      ActorJobStage.ProvisioningEquipment) &&
                actor.SourceStackId != EntityId.None))
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

            if (sourceReservations &&
                actor.JobKind == ActorJobKind.SupplyCrafting &&
                actor.JobStage == ActorJobStage.Collecting)
            {
                reservations[actor.SourceStackId] = checked(
                    reservations.GetValueOrDefault(actor.SourceStackId) + actor.ReservedQuantity);
                continue;
            }

            if (actor.JobKind == ActorJobKind.ClearConstructionSite)
            {
                var clearanceReservationId = sourceReservations
                    ? actor.SourceStackId
                    : actor.DestinationZoneId;
                if (clearanceReservationId != EntityId.None &&
                    (!sourceReservations || actor.JobStage == ActorJobStage.Collecting))
                {
                    reservations[clearanceReservationId] = checked(
                        reservations.GetValueOrDefault(clearanceReservationId) +
                        actor.ReservedQuantity);
                }
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
        GridPosition? requiredPosition = null,
        int? desiredQuantity = null)
    {
        var target = desiredQuantity ?? Definitions.PersonalFoodCapacity;
        if (actor.PersonalFood >= target)
        {
            return false;
        }

        var best = FindPersonalSupplySource(
            actor,
            ResourceKind.Food,
            itemReservations,
            requiredPosition);
        if (best is not { } source)
        {
            return false;
        }

        actor.JobKind = ActorJobKind.Resupply;
        actor.JobStage = ActorJobStage.ProvisioningFood;
        actor.SourceStackId = source.Stack.Id;
        actor.ReservedQuantity = 1;
        actor.JobTarget = source.Stack.Location.Position;
        BeginJobLeg(actor, source.Route, Definitions.ResupplyWorkTicks);
        itemReservations[source.Stack.Id] = checked(
            itemReservations.GetValueOrDefault(source.Stack.Id) + 1);
        return true;
    }

    private bool TryPlanWaterResupply(
        ActorState actor,
        Dictionary<EntityId, int> itemReservations,
        int? desiredQuantity = null)
    {
        var target = desiredQuantity ?? Definitions.PersonalWaterCapacity;
        var missing = target - actor.PersonalWater;
        if (missing <= 0)
        {
            return false;
        }

        var stored = FindPersonalSupplySource(
            actor,
            ResourceKind.Water,
            itemReservations,
            requiredPosition: null,
            storageOnly: true);
        var naturalRoute = FindNearestShallowWaterPath(actor.Position);
        if (stored is null && naturalRoute is null)
        {
            return false;
        }

        // Water hauled into a barrel is an intentional mine supply. Prefer it over
        // walking back to a natural source even when the latter happens to be closer.
        var useStored = stored is not null;
        var route = useStored ? stored!.Value.Route : naturalRoute!;
        var projectedThirst = Math.Min(
            Definitions.MaximumThirst,
            checked(actor.Thirst +
                (route.Count + Definitions.ResupplyWorkTicks) * Definitions.ThirstPerTick));
        var requestedQuantity = checked(missing + GetWaterDrinksNeeded(projectedThirst));
        var quantity = useStored
            ? Math.Min(
                requestedQuantity,
                stored!.Value.Stack.Quantity -
                    itemReservations.GetValueOrDefault(stored.Value.Stack.Id))
            : requestedQuantity;

        actor.JobKind = ActorJobKind.Resupply;
        actor.JobStage = ActorJobStage.ProvisioningWater;
        actor.ReservedQuantity = quantity;
        actor.SourceStackId = useStored ? stored!.Value.Stack.Id : EntityId.None;
        actor.JobTarget = useStored
            ? stored!.Value.Stack.Location.Position
            : route.Count == 0 ? actor.Position : route[^1];
        BeginJobLeg(actor, route, Definitions.ResupplyWorkTicks);
        if (useStored)
        {
            itemReservations[actor.SourceStackId] = checked(
                itemReservations.GetValueOrDefault(actor.SourceStackId) + quantity);
        }
        return true;
    }

    private bool HasReachableWaterSource(
        ActorState actor,
        Dictionary<EntityId, int> itemReservations) =>
        FindPersonalSupplySource(
            actor,
            ResourceKind.Water,
            itemReservations,
            requiredPosition: null,
            storageOnly: true) is not null ||
        FindNearestShallowWaterPath(actor.Position) is not null;

    private bool TryPlanStoneAmmoResupply(
        ActorState actor,
        Dictionary<EntityId, int> itemReservations,
        int? desiredQuantity = null)
    {
        var isPreparingForRaid = _raidPhase == GoblinRaidPhase.Preparing &&
            _raidPartyIds.Contains(actor.Id);
        if (!isPreparingForRaid && IsJuvenile(actor))
        {
            return false;
        }

        var target = desiredQuantity ?? GetStoneAmmoCapacity(actor.Equipment);
        var missing = target - actor.PersonalStoneAmmo;
        if (missing <= 0)
        {
            return false;
        }

        var settlementReserve = isPreparingForRaid ? 0 : 1;
        var best = FindPersonalSupplySource(
            actor,
            ResourceKind.Stone,
            itemReservations,
            requiredPosition: null,
            storageOnly: true,
            filter: stack => stack.Quantity -
                itemReservations.GetValueOrDefault(stack.Id) > settlementReserve);
        if (best is not { } source)
        {
            return false;
        }

        var quantity = Math.Min(
            missing,
            source.Stack.Quantity - itemReservations.GetValueOrDefault(source.Stack.Id) -
                settlementReserve);
        actor.JobKind = ActorJobKind.Resupply;
        actor.JobStage = ActorJobStage.ProvisioningAmmo;
        actor.SourceStackId = source.Stack.Id;
        actor.ReservedQuantity = quantity;
        actor.JobTarget = source.Stack.Location.Position;
        BeginJobLeg(actor, source.Route, Definitions.ResupplyWorkTicks);
        itemReservations[source.Stack.Id] = checked(
            itemReservations.GetValueOrDefault(source.Stack.Id) + quantity);
        return true;
    }

    private bool TryPlanEquipmentResupply(
        ActorState actor,
        Dictionary<EntityId, int> itemReservations)
    {
        var best = FindPersonalSupplySource(
            actor,
            ResourceKind.Equipment,
            itemReservations,
            requiredPosition: null,
            storageOnly: true,
            filter: stack =>
            {
                var equipment = GetEquipmentForVariant(stack.Variant);
                return EquipmentCatalog.IsUpgrade(actor.Equipment, equipment);
            });
        if (best is not { } source)
        {
            return false;
        }

        actor.JobKind = ActorJobKind.Resupply;
        actor.JobStage = ActorJobStage.ProvisioningEquipment;
        actor.SourceStackId = source.Stack.Id;
        actor.ReservedQuantity = 1;
        actor.JobTarget = source.Stack.Location.Position;
        BeginJobLeg(actor, source.Route, Definitions.ResupplyWorkTicks);
        itemReservations[source.Stack.Id] = checked(
            itemReservations.GetValueOrDefault(source.Stack.Id) + 1);
        return true;
    }

    private PersonalSupplySource? FindPersonalSupplySource(
        ActorState actor,
        ResourceKind resource,
        Dictionary<EntityId, int> itemReservations,
        GridPosition? requiredPosition,
        bool storageOnly = false,
        Func<ItemStackState, bool>? filter = null)
    {
        IEnumerable<ItemStackState> candidates = requiredPosition is { } position
            ? _itemStacks.Values.Where(stack => stack.Location.Position == position)
            : _resourceSpatialIndex.FindNearestStackIds(
                    resource,
                    actor.Position,
                    MaximumPersonalSupplyRouteCandidates)
                .Select(id => _itemStacks[id]);
        foreach (var stack in candidates.Where(stack =>
                     stack.Resource == resource &&
                     stack.Location.Kind is ItemLocationKind.Ground or ItemLocationKind.StorageZone &&
                     (!storageOnly || stack.Location.Kind == ItemLocationKind.StorageZone) &&
                     (filter is null || filter(stack)) &&
                     stack.Quantity - itemReservations.GetValueOrDefault(stack.Id) > 0))
        {
            var route = FindActorPath(actor, stack.Location.Position);
            if (route is not null)
            {
                return new PersonalSupplySource(stack, route);
            }
        }
        return null;
    }

    private IReadOnlyList<GridPosition>? FindNearestShallowWaterPath(GridPosition start)
    {
        var visited = new HashSet<GridPosition> { start };
        var predecessors = new Dictionary<GridPosition, GridPosition>();
        var queue = new Queue<GridPosition>();
        queue.Enqueue(start);
        while (queue.TryDequeue(out var current))
        {
            if (current.Z == 0 && Map.GetCell(current).Terrain == TerrainKind.ShallowWater)
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

            foreach (var neighbor in World.GetTerrainNeighbors(current, canOpenDoors: true))
            {
                if (visited.Add(neighbor))
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
            actor.CarriedStackId == EntityId.None &&
            actor.ReservedQuantity > 0 &&
            (actor.SourceStackId == EntityId.None
                ? actor.JobTarget.Z == 0 &&
                  Map.GetCell(actor.JobTarget).Terrain == TerrainKind.ShallowWater
                : _itemStacks.TryGetValue(actor.SourceStackId, out var water) &&
                  water.Resource == ResourceKind.Water &&
                  water.Location.Kind == ItemLocationKind.StorageZone &&
                  water.Location.Position == actor.JobTarget &&
                  water.Quantity >= actor.ReservedQuantity),
        ActorJobStage.ProvisioningAmmo =>
            actor.CarriedStackId == EntityId.None &&
            actor.PersonalStoneAmmo + actor.ReservedQuantity <=
                GetStoneAmmoCapacity(actor.Equipment) &&
            _itemStacks.TryGetValue(actor.SourceStackId, out var stones) &&
            stones.Resource == ResourceKind.Stone &&
            stones.Location.Kind is ItemLocationKind.Ground or ItemLocationKind.StorageZone &&
            stones.Location.Position == actor.JobTarget &&
            stones.Quantity >= actor.ReservedQuantity,
        ActorJobStage.ProvisioningEquipment =>
            actor.CarriedStackId == EntityId.None &&
            _itemStacks.TryGetValue(actor.SourceStackId, out var equipment) &&
            equipment.Resource == ResourceKind.Equipment &&
            equipment.Location.Kind == ItemLocationKind.StorageZone &&
            equipment.Location.Position == actor.JobTarget &&
            equipment.Quantity >= 1 &&
            GetEquipmentForVariant(equipment.Variant) is var item &&
            EquipmentCatalog.IsUpgrade(actor.Equipment, item),
        _ => false,
    };

    private void CompleteResupply(ActorState actor)
    {
        if (actor.JobStage == ActorJobStage.ProvisioningFood)
        {
            var food = _itemStacks[actor.SourceStackId];
            food.Quantity--;
            actor.PersonalFoodKinds.Add(food.FoodKind);
            Publish(SimulationEventKind.ActorProvisionedFood, actor.Id, food.Id, 1);
            if (food.Quantity == 0)
            {
                RemoveItemStack(food.Id);
                Publish(SimulationEventKind.ItemStackDepleted, actor.Id, food.Id, 0);
            }
        }
        else if (actor.JobStage == ActorJobStage.ProvisioningWater)
        {
            var sourceId = actor.SourceStackId;
            var drinks = Math.Min(
                actor.ReservedQuantity,
                GetWaterDrinksNeeded(actor.Thirst));
            var carried = Math.Min(
                actor.ReservedQuantity - drinks,
                Definitions.PersonalWaterCapacity - actor.PersonalWater);
            var collected = checked(drinks + carried);
            if (sourceId != EntityId.None)
            {
                var water = _itemStacks[sourceId];
                water.Quantity -= collected;
                if (water.Quantity == 0)
                {
                    RemoveItemStack(water.Id);
                    Publish(SimulationEventKind.ItemStackDepleted, actor.Id, water.Id, 0);
                }
            }
            if (drinks > 0)
            {
                actor.Thirst = Math.Max(
                    0,
                    actor.Thirst - checked(drinks * Definitions.WaterHydration));
                Publish(SimulationEventKind.ActorDrank, actor.Id, sourceId, drinks);
            }
            actor.PersonalWater = checked(actor.PersonalWater + carried);
            Publish(
                SimulationEventKind.ActorCollectedWater,
                actor.Id,
                sourceId,
                collected);
        }
        else if (actor.JobStage == ActorJobStage.ProvisioningEquipment)
        {
            var equipment = _itemStacks[actor.SourceStackId];
            var item = GetEquipmentForVariant(equipment.Variant);
            var replaced = EquipmentCatalog.GetReplacedDefinitions(actor.Equipment, item);
            var storageLocation = equipment.Location;
            foreach (var previous in replaced)
            {
                actor.Equipment &= ~previous.Equipment;
            }
            actor.Equipment |= item;
            equipment.Quantity--;
            Publish(SimulationEventKind.ItemPickedUp, actor.Id, equipment.Id, 1);
            if (equipment.Quantity == 0)
            {
                RemoveItemStack(equipment.Id);
                Publish(SimulationEventKind.ItemStackDepleted, actor.Id, equipment.Id, 0);
            }
            foreach (var previous in replaced)
            {
                var returned = _itemStacks.Values.FirstOrDefault(stack =>
                    stack.Resource == ResourceKind.Equipment &&
                    stack.Variant == previous.Variant &&
                    stack.Location == storageLocation);
                if (returned is null)
                {
                    returned = AllocateItemStack(
                        ResourceKind.Equipment,
                        quantity: 0,
                        location: storageLocation,
                        variant: previous.Variant);
                }
                returned.Quantity++;
                Publish(SimulationEventKind.ItemStored, actor.Id, returned.Id, 1);
            }
        }
        else
        {
            var stones = _itemStacks[actor.SourceStackId];
            stones.Quantity -= actor.ReservedQuantity;
            actor.PersonalStoneAmmo = checked(
                actor.PersonalStoneAmmo + actor.ReservedQuantity);
            Publish(
                SimulationEventKind.ActorCollectedStoneAmmo,
                actor.Id,
                stones.Id,
                actor.ReservedQuantity);
            if (stones.Quantity == 0)
            {
                RemoveItemStack(stones.Id);
                Publish(SimulationEventKind.ItemStackDepleted, actor.Id, stones.Id, 0);
            }
        }

        actor.ClearJob();
        TryResumeSuspendedJob(actor);
    }

    private int GetWaterDrinksNeeded(int thirst) =>
        thirst < Definitions.DrinkThreshold
            ? 0
            : (thirst - Definitions.DrinkThreshold) / Definitions.WaterHydration + 1;

    private bool TryPlanEatJob(
        ActorState actor,
        Dictionary<EntityId, int> itemReservations,
        GridPosition? requiredPosition = null)
    {
        var best = FindPersonalSupplySource(
            actor,
            ResourceKind.Food,
            itemReservations,
            requiredPosition);
        if (best is not { } source)
        {
            return false;
        }

        actor.JobKind = ActorJobKind.Eat;
        actor.SourceStackId = source.Stack.Id;
        actor.ReservedQuantity = 1;
        actor.JobTarget = source.Stack.Location.Position;
        BeginJobLeg(actor, source.Route, Definitions.EatWorkTicks);
        itemReservations[source.Stack.Id] = checked(
            itemReservations.GetValueOrDefault(source.Stack.Id) + 1);
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
        ApplyFoodEffects(actor, food.FoodKind);
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
        bool requireDesignation,
        WorkDesignationKind designationKind = WorkDesignationKind.GatherFood)
    {
        if (requireDesignation && !_workDesignations.Values.Any(designation =>
                designation.Kind == designationKind &&
                !designation.IsSuspended))
        {
            return false;
        }

        var route = Navigation.FindNearestHarvestablePlantPath(
            actor.Position,
            reservedTargets,
            position =>
                Visibility.Get(position) != CellVisibility.Unknown &&
                (designationKind == WorkDesignationKind.GatherReeds
                    ? World.GetPlantPatch(position) is { Kind: PlantKind.ReedBed }
                    : World.GetPlantPatch(position) is not { Kind: PlantKind.ReedBed }) &&
                (!requireDesignation ||
                 IsWorkDesignated(designationKind, position)));
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
                designation.Kind == WorkDesignationKind.UprootBerryBush &&
                !designation.IsSuspended))
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

    private bool TryPlanCleanBloodJob(
        ActorState actor,
        ISet<EntityId> reservedDesignations)
    {
        var best = _workDesignations.Values
            .Where(designation => designation.Kind == WorkDesignationKind.CleanBlood &&
                !designation.IsSuspended &&
                !reservedDesignations.Contains(designation.Id) &&
                HasCleanableBlood(designation.Target))
            .Select(designation => new
            {
                Designation = designation,
                Route = FindActorPath(actor, designation.Target),
            })
            .Where(candidate => candidate.Route is not null)
            .OrderBy(candidate => candidate.Route!.Count)
            .ThenBy(candidate => candidate.Designation.Id)
            .FirstOrDefault();
        if (best is null)
        {
            return false;
        }

        actor.JobKind = ActorJobKind.CleanBlood;
        actor.JobTarget = best.Designation.Target;
        actor.SourceStackId = best.Designation.Id;
        BeginJobLeg(actor, best.Route!, BloodCleaningWorkTicks);
        reservedDesignations.Add(best.Designation.Id);
        return true;
    }

    private bool TryPlanFastidiousCleaning(
        ActorState actor,
        ISet<EntityId> reservedDesignations)
    {
        if (!actor.KnownTraits.HasFlag(GoblinTrait.Fastidious) ||
            (_raidPhase != GoblinRaidPhase.None && _raidPartyIds.Contains(actor.Id)))
        {
            return false;
        }

        var best = _bloodStains.Values
            .Where(stain => stain.Surface == BloodSurfaceKind.ConstructedFloor &&
                stain.Volume > 0 &&
                Visibility.Get(stain.Position).IsDiscovered() &&
                IsGoblinOwnedFloor(stain.Position) &&
                !_workDesignations.Values.Any(designation =>
                    designation.Kind == WorkDesignationKind.CleanBlood &&
                    designation.Target == stain.Position))
            .Select(stain => new
            {
                stain.Position,
                Route = FindActorPath(actor, stain.Position),
            })
            .Where(candidate => candidate.Route is not null)
            .OrderBy(candidate => candidate.Route!.Count)
            .ThenBy(candidate => candidate.Position.Z)
            .ThenBy(candidate => candidate.Position.Y)
            .ThenBy(candidate => candidate.Position.X)
            .FirstOrDefault();
        if (best is null)
        {
            return false;
        }

        var orderId = AllocateEntityId();
        var designationId = AllocateEntityId();
        _workDesignations.Add(
            designationId,
            new WorkDesignationSnapshot(
                designationId,
                WorkDesignationKind.CleanBlood,
                best.Position,
                EntityId.None)
            {
                OrderId = orderId,
                Priority = StoragePriority.Low,
            });
        Publish(
            SimulationEventKind.WorkDesignationCreated,
            actor.Id,
            designationId,
            (int)WorkDesignationKind.CleanBlood);

        actor.JobKind = ActorJobKind.CleanBlood;
        actor.JobTarget = best.Position;
        actor.SourceStackId = designationId;
        BeginJobLeg(actor, best.Route!, BloodCleaningWorkTicks);
        reservedDesignations.Add(designationId);
        return true;
    }

    private bool TryPlanIdleHousekeeping(
        ActorState actor,
        ISet<EntityId> reservedDesignations,
        Dictionary<EntityId, int> sourceReservations,
        Dictionary<EntityId, int> destinationReservations)
    {
        if (_raidPhase != GoblinRaidPhase.None && _raidPartyIds.Contains(actor.Id))
        {
            return false;
        }

        return TryPlanNearbyCleaning(actor, reservedDesignations) ||
            TryPlanHaulCollection(
                actor,
                sourceReservations,
                destinationReservations,
                maximumEstimatedDistance: IdleHousekeepingMaximumRouteLength);
    }

    private bool TryPlanNearbyCleaning(
        ActorState actor,
        ISet<EntityId> reservedDesignations)
    {
        var best = _bloodStains.Values
            .Where(stain => stain.Volume > 0 &&
                ManhattanDistance(actor.Position, stain.Position) <=
                    IdleHousekeepingMaximumRouteLength &&
                Visibility.Get(stain.Position).IsDiscovered() &&
                !_workDesignations.Values.Any(designation =>
                    designation.Kind == WorkDesignationKind.CleanBlood &&
                    designation.Target == stain.Position))
            .Select(stain => new
            {
                stain.Position,
                Route = FindActorPath(actor, stain.Position),
            })
            .Where(candidate => candidate.Route is { Count: <= IdleHousekeepingMaximumRouteLength })
            .OrderBy(candidate => candidate.Route!.Count)
            .ThenBy(candidate => candidate.Position.Z)
            .ThenBy(candidate => candidate.Position.Y)
            .ThenBy(candidate => candidate.Position.X)
            .FirstOrDefault();
        if (best is null)
        {
            return false;
        }

        var orderId = AllocateEntityId();
        var designationId = AllocateEntityId();
        _workDesignations.Add(
            designationId,
            new WorkDesignationSnapshot(
                designationId,
                WorkDesignationKind.CleanBlood,
                best.Position,
                EntityId.None)
            {
                OrderId = orderId,
                Priority = StoragePriority.Low,
            });
        Publish(
            SimulationEventKind.WorkDesignationCreated,
            actor.Id,
            designationId,
            (int)WorkDesignationKind.CleanBlood);

        actor.JobKind = ActorJobKind.CleanBlood;
        actor.JobTarget = best.Position;
        actor.SourceStackId = designationId;
        BeginJobLeg(actor, best.Route!, BloodCleaningWorkTicks);
        reservedDesignations.Add(designationId);
        return true;
    }

    private bool IsGoblinOwnedFloor(GridPosition position) =>
        World.GetWorldObjectsAt(position).Any(worldObject =>
            worldObject.Owner == WorldObjectOwner.GoblinTribe &&
            worldObject.GetAbsoluteParts().Any(part =>
                part.Position == position &&
                part.Part.Kind is WorldObjectPartKind.Floor or WorldObjectPartKind.Walkway));

    private void UpdateCleanBloodJob(ActorState actor)
    {
        if (actor.CarriedStackId != EntityId.None ||
            !_workDesignations.TryGetValue(actor.SourceStackId, out var designation) ||
            designation.Kind != WorkDesignationKind.CleanBlood ||
            designation.IsSuspended ||
            designation.Target != actor.JobTarget ||
            !HasCleanableBlood(actor.JobTarget))
        {
            actor.ClearJob();
            return;
        }

        if (actor.JobPhase == ActorJobPhase.Traveling)
        {
            AdvanceTravel(actor);
        }

        if (actor.JobKind != ActorJobKind.CleanBlood ||
            actor.JobPhase != ActorJobPhase.Working)
        {
            return;
        }

        actor.RemainingWorkTicks--;
        if (actor.RemainingWorkTicks > 0)
        {
            return;
        }

        var cleaned = CleanBlood(actor.Position);
        if (cleaned > 0)
        {
            GainHaulingExperience(actor, Math.Max(1, cleaned / 4));
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
                !designation.IsSuspended &&
                !reservedDesignations.Contains(designation.Id) &&
                World.GetFellableWood(designation.Target) is not null)
            .SelectMany(designation => World.GetCardinalWorldNeighbors(designation.Target)
                .Where(World.IsTerrainTraversable)
                .Select(position => new
                {
                    Designation = designation,
                    Position = position,
                    Route = FindActorPath(actor, position),
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
            designation.IsSuspended ||
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

        var woodyObject = World.GetFellableWood(designation.Target);
        if (woodyObject is not null && World.TryHarvestFellableWood(
                designation.Target,
                CurrentTick,
                out var woodQuantity,
                out var change))
        {
            _undeliveredWorldChanges.Add(change);
            ScatterFelledWood(
                actor.Position,
                designation.Target,
                woodQuantity,
                WoodVariantFor(woodyObject.Anchor));
            GainBuildingExperience(actor, Math.Max(10, woodQuantity));
        }

        _workDesignations.Remove(designation.Id);
        Publish(SimulationEventKind.WorkDesignationRemoved, actor.Id, designation.Id, 0);
        actor.ClearJob();
    }

    private void ScatterFelledWood(
        GridPosition workerPosition,
        GridPosition treePosition,
        int woodQuantity,
        ResourceVariant variant)
    {
        var directionX = treePosition.X - workerPosition.X;
        var directionY = treePosition.Y - workerPosition.Y;
        var remaining = woodQuantity;
        for (var section = 0; remaining > 0; section++)
        {
            var requested = new GridPosition(
                treePosition.X + (directionX * (section + 1)),
                treePosition.Y + (directionY * (section + 1)),
                treePosition.Z);
            var position = World.TryResolveGroundItemPosition(requested, out var resolved)
                ? resolved
                : workerPosition;
            var quantity = Math.Min(16, remaining);
            var existing = FindMergeableGroundStack(
                ResourceKind.Wood,
                position,
                variant: variant);
            if (existing is null)
            {
                AllocateItemStack(
                    ResourceKind.Wood,
                    quantity,
                    ItemLocation.OnGround(position),
                    variant: variant);
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
        if (!MiningCapabilityPolicy.HasPickaxe(actor.Equipment) ||
            !actor.KnownSkills.HasFlag(GoblinSkill.Building))
        {
            return false;
        }

        var best = _workDesignations.Values
            .Where(designation => designation.Kind == WorkDesignationKind.QuarryBoulder &&
                !designation.IsSuspended &&
                !reservedDesignations.Contains(designation.Id) &&
                World.GetQuarriableBoulder(designation.Target) is not null)
            .SelectMany(designation => World.GetCardinalWorldNeighbors(designation.Target)
                .Where(World.IsTerrainTraversable)
                .Select(position => new
                {
                    Designation = designation,
                    Position = position,
                    Route = FindActorPath(actor, position),
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
        if (!MiningCapabilityPolicy.HasPickaxe(actor.Equipment) ||
            !_workDesignations.TryGetValue(actor.SourceStackId, out var designation) ||
            designation.Kind != WorkDesignationKind.QuarryBoulder ||
            designation.IsSuspended ||
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

        var boulder = World.GetQuarriableBoulder(designation.Target);
        if (boulder is not null && World.TryQuarryBoulder(
                designation.Target,
                CurrentTick,
                out var stoneQuantity,
                out var change))
        {
            _undeliveredWorldChanges.Add(change);
            var variant = StoneVariantFor(boulder.Anchor);
            var existing = FindMergeableGroundStack(
                ResourceKind.Stone,
                designation.Target,
                variant: variant);
            if (existing is null)
            {
                AllocateItemStack(
                    ResourceKind.Stone,
                    stoneQuantity,
                    ItemLocation.OnGround(designation.Target),
                    variant: variant);
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

    private bool TryPlanMineRockJob(
        ActorState actor,
        ISet<EntityId> reservedDesignations)
    {
        if (!MiningCapabilityPolicy.HasPickaxe(actor.Equipment) ||
            !actor.KnownSkills.HasFlag(GoblinSkill.Building))
        {
            return false;
        }

        var candidates = _workDesignations.Values
            .Where(designation => designation.Kind == WorkDesignationKind.MineRock &&
                !designation.IsSuspended &&
                !reservedDesignations.Contains(designation.Id) &&
                CanActorMineRock(actor, designation.Target))
            .SelectMany(designation => World.GetCardinalWorldNeighbors(designation.Target)
                .Where(World.IsTerrainTraversable)
                .Select(position => new
                {
                    Designation = designation,
                    Position = position,
                    EstimatedDistance = ManhattanDistance(actor.Position, position),
                }))
            .OrderByDescending(candidate => candidate.Designation.Priority)
            .ThenBy(candidate => candidate.EstimatedDistance)
            .ThenBy(candidate => candidate.Designation.Id)
            .ThenBy(candidate => candidate.Position.Z)
            .ThenBy(candidate => candidate.Position.Y)
            .ThenBy(candidate => candidate.Position.X)
            .ToArray();
        if (candidates.Length == 0)
        {
            return false;
        }

        foreach (var candidate in candidates
                     .Take(MaximumPublicWorkRouteCandidatesPerPlanningTick))
        {
            var route = FindTribePath(actor.Position, candidate.Position);
            if (route is null)
            {
                continue;
            }

            actor.JobKind = ActorJobKind.MineRock;
            actor.JobTarget = candidate.Position;
            actor.SourceStackId = candidate.Designation.Id;
            BeginJobLeg(actor, route, GetMineRockWorkTicks(actor));
            reservedDesignations.Add(candidate.Designation.Id);
            return true;
        }
        return false;
    }

    private void UpdateMineRockJob(ActorState actor)
    {
        if (!MiningCapabilityPolicy.HasPickaxe(actor.Equipment) ||
            !_workDesignations.TryGetValue(actor.SourceStackId, out var designation) ||
            designation.Kind != WorkDesignationKind.MineRock ||
            designation.IsSuspended ||
            !CanActorMineRock(actor, designation.Target) ||
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

        if (World.TryExcavateRock(
                designation.Target,
                CurrentTick,
                out var rock,
                out var deposit,
                out var change))
        {
            _undeliveredWorldChanges.Add(change);
            var quantity = DeterministicRandom.NextInt(
                WorldSeed,
                RandomDomain.Stone,
                actor.Id,
                CurrentTick,
                sampleKey: designation.Id.Value,
                minimumInclusive: 1,
                maximumExclusive: 4);
            var variant = rock switch
            {
                RockKind.Granite => ResourceVariant.Granite,
                RockKind.Basalt => ResourceVariant.Basalt,
                RockKind.Obsidian => ResourceVariant.Obsidian,
                _ => ResourceVariant.Sandstone,
            };
            var outputPosition = World.IsTerrainTraversable(designation.Target)
                ? designation.Target
                : actor.Position;
            var existing = FindMergeableGroundStack(
                ResourceKind.Stone,
                outputPosition,
                variant: variant);
            if (existing is null)
            {
                AllocateItemStack(
                    ResourceKind.Stone,
                    quantity,
                    ItemLocation.OnGround(outputPosition),
                    variant: variant);
            }
            else
            {
                existing.Quantity = checked(existing.Quantity + quantity);
            }
            if (deposit != MineralDepositKind.None)
            {
                var (depositResource, depositVariant) = deposit switch
                {
                    MineralDepositKind.Coal => (ResourceKind.Coal, ResourceVariant.None),
                    MineralDepositKind.IronOre => (ResourceKind.Ore, ResourceVariant.IronOre),
                    MineralDepositKind.CopperOre => (ResourceKind.Ore, ResourceVariant.CopperOre),
                    MineralDepositKind.SilverOre => (ResourceKind.Ore, ResourceVariant.SilverOre),
                    MineralDepositKind.GoldOre => (ResourceKind.Ore, ResourceVariant.GoldOre),
                    MineralDepositKind.Ruby => (ResourceKind.Materials, ResourceVariant.Ruby),
                    MineralDepositKind.Emerald => (ResourceKind.Materials, ResourceVariant.Emerald),
                    MineralDepositKind.Diamond => (ResourceKind.Materials, ResourceVariant.Diamond),
                    _ => throw new ArgumentOutOfRangeException(nameof(deposit)),
                };
                var isGem = deposit is MineralDepositKind.Ruby or MineralDepositKind.Emerald or
                    MineralDepositKind.Diamond;
                var depositQuantity = DeterministicRandom.NextInt(
                    WorldSeed,
                    RandomDomain.Stone,
                    actor.Id,
                    CurrentTick,
                    sampleKey: designation.Id.Value ^ 0x4F52454445504F53UL,
                    minimumInclusive: 1,
                    maximumExclusive: deposit == MineralDepositKind.Coal ? 4 : isGem ? 2 : 3);
                var depositStack = FindMergeableGroundStack(
                    depositResource,
                    outputPosition,
                    variant: depositVariant);
                if (depositStack is null)
                {
                    AllocateItemStack(
                        depositResource,
                        depositQuantity,
                        ItemLocation.OnGround(outputPosition),
                        variant: depositVariant);
                }
                else
                {
                    depositStack.Quantity = checked(depositStack.Quantity + depositQuantity);
                }
            }
            GainBuildingExperience(actor, Math.Max(12, quantity * 2));
        }

        _workDesignations.Remove(designation.Id);
        Publish(SimulationEventKind.WorkDesignationRemoved, actor.Id, designation.Id, 0);
        actor.ClearJob();
    }

    private bool TryPlanCarveRampJob(
        ActorState actor,
        ISet<EntityId> reservedDesignations)
    {
        if (!MiningCapabilityPolicy.HasPickaxe(actor.Equipment) ||
            !actor.KnownSkills.HasFlag(GoblinSkill.Building))
        {
            return false;
        }

        var best = _workDesignations.Values
            .Where(designation => designation.Kind is WorkDesignationKind.CarveRampDown or
                    WorkDesignationKind.CarveRampUp &&
                !designation.IsSuspended &&
                !reservedDesignations.Contains(designation.Id) &&
                CanActorCarveRamp(actor, designation))
            .Select(designation => new
            {
                Designation = designation,
                Route = FindActorPath(actor, designation.Target),
            })
            .Where(candidate => candidate.Route is not null)
            .OrderByDescending(candidate => candidate.Designation.Priority)
            .ThenBy(candidate => candidate.Route!.Count)
            .ThenBy(candidate => candidate.Designation.Id)
            .FirstOrDefault();
        if (best is null)
        {
            return false;
        }

        actor.JobKind = ActorJobKind.CarveRamp;
        actor.JobTarget = best.Designation.Target;
        actor.SourceStackId = best.Designation.Id;
        BeginJobLeg(actor, best.Route!, GetCarveRampWorkTicks(actor));
        reservedDesignations.Add(best.Designation.Id);
        return true;
    }

    private void UpdateCarveRampJob(ActorState actor)
    {
        if (!MiningCapabilityPolicy.HasPickaxe(actor.Equipment) ||
            !_workDesignations.TryGetValue(actor.SourceStackId, out var designation) ||
            designation.Kind is not (WorkDesignationKind.CarveRampDown or
                WorkDesignationKind.CarveRampUp) ||
            designation.IsSuspended ||
            actor.JobTarget != designation.Target ||
            !CanActorCarveRamp(actor, designation))
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

        if (World.TryCarveVerticalRamp(
                designation.Target,
                designation.Kind == WorkDesignationKind.CarveRampDown,
                CurrentTick,
                out var rock,
                out var change))
        {
            _undeliveredWorldChanges.Add(change);
            var quantity = DeterministicRandom.NextInt(
                WorldSeed,
                RandomDomain.Stone,
                actor.Id,
                CurrentTick,
                sampleKey: designation.Id.Value ^ 0x52414D5053544F4EUL,
                minimumInclusive: 6,
                maximumExclusive: 11);
            var variant = rock switch
            {
                RockKind.Granite => ResourceVariant.Granite,
                RockKind.Basalt => ResourceVariant.Basalt,
                RockKind.Obsidian => ResourceVariant.Obsidian,
                _ => ResourceVariant.Sandstone,
            };
            var existing = FindMergeableGroundStack(
                ResourceKind.Stone,
                designation.Target,
                variant: variant);
            if (existing is null)
            {
                AllocateItemStack(
                    ResourceKind.Stone,
                    quantity,
                    ItemLocation.OnGround(designation.Target),
                    variant: variant);
            }
            else
            {
                existing.Quantity = checked(existing.Quantity + quantity);
            }
            GainBuildingExperience(actor, Math.Max(20, quantity * 3));
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

    private bool TryPlanConstructionClearance(
        ActorState actor,
        Dictionary<EntityId, int> sourceReservations,
        Dictionary<EntityId, int> destinationReservations)
    {
        var candidates = _constructionSites.Values
            .SelectMany(site =>
            {
                var footprint = site.GetFootprint();
                return _itemStacks.Values.Where(stack =>
                        stack.Location.Kind == ItemLocationKind.Ground &&
                        footprint.Contains(stack.Location.Position) &&
                        stack.Quantity > 0 &&
                        sourceReservations.GetValueOrDefault(stack.Id) == 0)
                    .Select(stack => new
                    {
                        Site = site,
                        Stack = stack,
                    });
            })
            .OrderByDescending(item => item.Site.Priority)
            .ThenBy(item => ManhattanDistance(actor.Position, item.Stack.Location.Position))
            .ThenBy(item => item.Site.Id)
            .ThenBy(item => item.Stack.Id)
            .Take(MaximumConstructionRouteCandidatesPerPlanningTick);
        foreach (var candidate in candidates)
        {
            var routeRequest = RequestActorPath(actor, candidate.Stack.Location.Position);
            if (routeRequest.Status == NavigationPathRequestStatus.Pending)
            {
                return true;
            }
            if (routeRequest.Status == NavigationPathRequestStatus.Unreachable ||
                routeRequest.Path is not { } route)
            {
                continue;
            }

            var available = candidate.Stack.Quantity -
                sourceReservations.GetValueOrDefault(candidate.Stack.Id);
            var quantity = Math.Min(Definitions.ActorCarryCapacity, available);
            var destination = _storageZones.Values
                .Where(zone =>
                    ZoneAccepts(zone, candidate.Stack) &&
                    CanStoreStack(zone, candidate.Stack, quantity) &&
                    zone.Capacity - GetStoredQuantity(zone.Id) -
                        destinationReservations.GetValueOrDefault(zone.Id) >= quantity)
                .OrderByDescending(zone => zone.Priority)
                .ThenBy(zone => ManhattanDistance(
                    candidate.Stack.Location.Position,
                    zone.Position))
                .ThenBy(zone => zone.Id)
                .FirstOrDefault();
            if (destination is null &&
                FindConstructionClearanceDropPosition(candidate.Stack.Location.Position) is null)
            {
                continue;
            }

            actor.JobKind = ActorJobKind.ClearConstructionSite;
            actor.JobStage = ActorJobStage.Collecting;
            actor.SourceStackId = candidate.Stack.Id;
            actor.DestinationZoneId = destination?.Id ?? EntityId.None;
            actor.ReservedQuantity = quantity;
            actor.JobTarget = candidate.Stack.Location.Position;
            BeginJobLeg(actor, route, Definitions.HaulHandlingTicks);
            sourceReservations[candidate.Stack.Id] = checked(
                sourceReservations.GetValueOrDefault(candidate.Stack.Id) + quantity);
            if (destination is not null)
            {
                destinationReservations[destination.Id] = checked(
                    destinationReservations.GetValueOrDefault(destination.Id) + quantity);
            }
            return true;
        }

        return false;
    }

    private void UpdateConstructionClearanceJob(ActorState actor)
    {
        if (!IsConstructionClearanceJobValid(actor))
        {
            if (actor.CarriedStackId != EntityId.None &&
                TryRedirectConstructionClearanceToGround(actor))
            {
                return;
            }

            if (actor.CarriedStackId != EntityId.None)
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
        if (actor.JobKind != ActorJobKind.ClearConstructionSite ||
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
            CompleteConstructionClearanceCollection(actor);
        }
        else
        {
            CompleteConstructionClearanceDelivery(actor);
        }
    }

    private bool IsConstructionClearanceJobValid(ActorState actor)
    {
        if (actor.ReservedQuantity <= 0 ||
            actor.ReservedQuantity > Definitions.ActorCarryCapacity)
        {
            return false;
        }

        if (actor.JobStage == ActorJobStage.Collecting)
        {
            return actor.CarriedStackId == EntityId.None &&
                _itemStacks.TryGetValue(actor.SourceStackId, out var source) &&
                IsGroundStackBlockingConstruction(source) &&
                source.Quantity >= actor.ReservedQuantity &&
                actor.JobTarget == source.Location.Position;
        }

        if (actor.JobStage != ActorJobStage.Delivering ||
            actor.SourceStackId != EntityId.None ||
            !_itemStacks.TryGetValue(actor.CarriedStackId, out var carried) ||
            carried.Location != ItemLocation.CarriedBy(actor.Id) ||
            carried.Quantity != actor.ReservedQuantity)
        {
            return false;
        }

        if (actor.DestinationZoneId != EntityId.None)
        {
            return _storageZones.TryGetValue(actor.DestinationZoneId, out var zone) &&
                actor.JobTarget == zone.Position &&
                CanStoreStack(zone, carried, carried.Quantity);
        }

        return World.IsTerrainTraversable(actor.JobTarget) &&
            !_constructionSites.Values.Any(site =>
                site.GetFootprint().Contains(actor.JobTarget));
    }

    private void CompleteConstructionClearanceCollection(ActorState actor)
    {
        var source = _itemStacks[actor.SourceStackId];
        var destination = actor.DestinationZoneId != EntityId.None &&
            _storageZones.TryGetValue(actor.DestinationZoneId, out var zone) &&
            CanStoreStack(zone, source, actor.ReservedQuantity)
                ? zone.Position
                : FindConstructionClearanceDropPosition(actor.Position);
        if (destination is null)
        {
            actor.ClearJob();
            return;
        }

        var routeRequest = RequestActorPath(actor, destination.Value);
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
                source.FoodKind,
                source.Variant);
        }

        actor.CarriedStackId = carried.Id;
        actor.SourceStackId = EntityId.None;
        actor.JobStage = ActorJobStage.Delivering;
        Publish(SimulationEventKind.ItemPickedUp, actor.Id, carried.Id, carried.Quantity);
        if (actor.DestinationZoneId != EntityId.None)
        {
            actor.JobTarget = destination.Value;
            BeginJobLeg(actor, route, Definitions.HaulHandlingTicks);
            return;
        }

        actor.DestinationZoneId = EntityId.None;
        actor.JobTarget = destination.Value;
        BeginJobLeg(actor, route, Definitions.HaulHandlingTicks);
    }

    private bool TryRedirectConstructionClearanceToGround(ActorState actor)
    {
        var destination = FindConstructionClearanceDropPosition(actor.Position);
        if (destination is null)
        {
            return false;
        }

        var route = FindActorPath(actor, destination.Value);
        if (route is null)
        {
            return false;
        }

        actor.JobKind = ActorJobKind.ClearConstructionSite;
        actor.JobStage = ActorJobStage.Delivering;
        actor.SourceStackId = EntityId.None;
        actor.DestinationZoneId = EntityId.None;
        actor.JobTarget = destination.Value;
        BeginJobLeg(actor, route, Definitions.HaulHandlingTicks);
        return true;
    }

    private GridPosition? FindConstructionClearanceDropPosition(
        GridPosition origin)
    {
        var blocked = _constructionSites.Values
            .SelectMany(site => site.GetFootprint())
            .ToHashSet();
        var storagePositions = _storageZones.Values
            .Select(zone => zone.Position)
            .ToHashSet();
        for (var distance = 1; distance <= Map.Width + Map.Height; distance++)
        {
            var minimumY = Math.Max(0, origin.Y - distance);
            var maximumY = Math.Min(Map.Height - 1, origin.Y + distance);
            for (var y = minimumY; y <= maximumY; y++)
            {
                var offsetX = distance - Math.Abs(y - origin.Y);
                var left = new GridPosition(origin.X - offsetX, y, origin.Z);
                if (CanDropAt(left))
                {
                    return left;
                }

                var right = new GridPosition(origin.X + offsetX, y, origin.Z);
                if (offsetX > 0 && CanDropAt(right))
                {
                    return right;
                }
            }
        }

        return null;

        bool CanDropAt(GridPosition position) =>
            position.X >= 0 && position.X < Map.Width &&
            !blocked.Contains(position) &&
            !storagePositions.Contains(position) &&
            World.IsTerrainTraversable(position);
    }

    private void CompleteConstructionClearanceDelivery(ActorState actor)
    {
        var carried = _itemStacks[actor.CarriedStackId];
        var quantity = carried.Quantity;
        if (actor.DestinationZoneId != EntityId.None &&
            _storageZones.TryGetValue(actor.DestinationZoneId, out var zone) &&
            CanStoreStack(zone, carried, quantity))
        {
            actor.CarriedStackId = EntityId.None;
            var stored = StoreStackInZone(carried, zone);
            Publish(SimulationEventKind.ItemStored, actor.Id, stored.Id, quantity);
        }
        else
        {
            actor.CarriedStackId = EntityId.None;
            MoveItemStack(carried, ItemLocation.OnGround(actor.Position));
            Publish(SimulationEventKind.ItemDropped, actor.Id, carried.Id, quantity);
        }

        GainHaulingExperience(actor, Math.Max(1, quantity * 2));
        actor.ClearJob();
    }

    private bool TryPlanConstructionSupply(
        ActorState actor,
        Dictionary<EntityId, int> sourceReservations,
        Dictionary<EntityId, int> constructionReservations)
    {
        HaulPlan? best = null;
        foreach (var priority in Enum.GetValues<StoragePriority>().OrderDescending())
        {
            var candidates = (
                    from site in _constructionSites.Values
                    where site.Priority == priority
                    where IsConstructionSequenceReady(site)
                    let missing = site.MissingQuantity -
                        constructionReservations.GetValueOrDefault(site.Id)
                    where missing > 0
                    let nearbySourceIds = _resourceSpatialIndex.FindNearestStackIds(
                        site.RequiredResource,
                        actor.Position,
                        MaximumConstructionRouteCandidatesPerPlanningTick * 4)
                    from sourceId in nearbySourceIds
                    let source = _itemStacks[sourceId]
                    where StackMatchesConstructionMaterial(site, source)
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
                var routeRequest = RequestActorPath(
                    actor,
                    candidate.Source.Location.Position);
                if (routeRequest.Status == NavigationPathRequestStatus.Pending)
                {
                    return true;
                }
                if (routeRequest.Status == NavigationPathRequestStatus.Unreachable ||
                    routeRequest.Path is not { } routeToSource)
                {
                    continue;
                }

                var missing = candidate.Site.MissingQuantity -
                    constructionReservations.GetValueOrDefault(candidate.Site.Id);
                var quantity = Math.Min(
                    Definitions.ActorCarryCapacity,
                    Math.Min(candidate.Available, missing));
                best = new HaulPlan(
                    candidate.Source.Id,
                    candidate.Site.Id,
                    quantity,
                    routeToSource,
                    GetResourcePriority(candidate.Site.RequiredResource),
                    candidate.Site.Priority,
                    checked(routeToSource.Count +
                        ManhattanDistance(candidate.Source.Location.Position, candidate.Site.Anchor)));
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
        if (!_itemStacks.TryGetValue(actor.CarriedStackId, out var carried))
        {
            return false;
        }

        var candidates = _constructionSites.Values
            .Where(site => StackMatchesConstructionMaterial(site, carried) &&
                IsConstructionSequenceReady(site) &&
                site.MissingQuantity - constructionReservations.GetValueOrDefault(site.Id) >=
                    carried.Quantity)
            .OrderByDescending(site => site.Priority)
            .ThenBy(site => ManhattanDistance(actor.Position, site.Anchor))
            .ThenBy(site => site.Id)
            .Take(MaximumConstructionRouteCandidatesPerPlanningTick);
        foreach (var site in candidates)
        {
            var routeRequest = RequestConstructionAccessPath(actor, site);
            if (routeRequest.Status == NavigationPathRequestStatus.Pending)
            {
                return true;
            }
            if (routeRequest.Status == NavigationPathRequestStatus.Unreachable ||
                routeRequest.Path is not { } route)
            {
                continue;
            }

            actor.JobKind = ActorJobKind.SupplyConstruction;
            actor.JobStage = ActorJobStage.Delivering;
            actor.SourceStackId = EntityId.None;
            actor.DestinationZoneId = site.Id;
            actor.ReservedQuantity = carried.Quantity;
            actor.JobTarget = route.Count == 0 ? actor.Position : route[^1];
            BeginJobLeg(actor, route, Definitions.HaulHandlingTicks);
            constructionReservations[site.Id] = checked(
                constructionReservations.GetValueOrDefault(site.Id) + carried.Quantity);
            return true;
        }

        return false;
    }

    private void UpdateConstructionSupplyJob(ActorState actor)
    {
        if (!_constructionSites.TryGetValue(actor.DestinationZoneId, out var site) ||
            actor.ReservedQuantity <= 0 ||
            site.MissingQuantity < actor.ReservedQuantity)
        {
            if (actor.JobStage == ActorJobStage.Delivering)
            {
                DropCarriedStack(actor);
            }
            actor.ClearJob();
            return;
        }

        if (actor.JobStage == ActorJobStage.Collecting)
        {
            if (actor.CarriedStackId != EntityId.None ||
                !_itemStacks.TryGetValue(actor.SourceStackId, out var source) ||
                !StackMatchesConstructionMaterial(site, source) ||
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
                !StackMatchesConstructionMaterial(site, carried) ||
                carried.Quantity != actor.ReservedQuantity ||
                carried.Location != ItemLocation.CarriedBy(actor.Id))
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
        var routeRequest = RequestConstructionAccessPath(actor, site);
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

    private void CompleteConstructionDelivery(ActorState actor, ConstructionSiteState site)
    {
        var carried = _itemStacks[actor.CarriedStackId];
        var delivered = carried.Quantity;
        var resource = carried.Resource;
        var variant = carried.Variant;
        RemoveItemStack(carried.Id);
        actor.CarriedStackId = EntityId.None;
        site.Deliver(resource, variant, delivered);
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
        var candidates = _constructionSites.Values
            .Where(site => site.HasAllMaterials &&
                IsConstructionSequenceReady(site) &&
                !HasGroundStackInConstructionFootprint(site) &&
                !reservedSites.Contains(site.Id) &&
                CanActorBuild(actor, site))
            .OrderByDescending(site => site.Priority)
            .ThenBy(site => ManhattanDistance(actor.Position, site.Anchor))
            .ThenBy(site => site.Id)
            .Take(MaximumConstructionRouteCandidatesPerPlanningTick);
        foreach (var site in candidates)
        {
            var routeRequest = RequestConstructionAccessPath(actor, site);
            if (routeRequest.Status == NavigationPathRequestStatus.Pending)
            {
                return true;
            }
            if (routeRequest.Status == NavigationPathRequestStatus.Unreachable ||
                routeRequest.Path is not { } route)
            {
                continue;
            }

            actor.JobKind = ActorJobKind.BuildConstruction;
            actor.DestinationZoneId = site.Id;
            actor.JobTarget = route.Count == 0 ? actor.Position : route[^1];
            BeginJobLeg(actor, route, site.RemainingWorkTicks);
            return true;
        }

        return false;
    }

    private bool IsConstructionSequenceReady(ConstructionSiteState site) =>
        !_constructionSites.Values.Any(candidate =>
            candidate.OrderId == site.OrderId &&
            candidate.SequenceIndex < site.SequenceIndex);

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
        HasRequiredConstructionEquipment(actor.Equipment, site.Capabilities.RequiredEquipment) &&
        GoblinExperienceSnapshot.GetLevel(actor.BuildingExperience) >=
            site.Capabilities.MinimumBuildingLevel;

    private static bool HasRequiredConstructionEquipment(
        PersonalEquipment equipment,
        PersonalEquipment requiredEquipment) =>
        requiredEquipment == PersonalEquipment.PrimitivePickaxe
            ? MiningCapabilityPolicy.HasPickaxe(equipment)
            : (equipment & requiredEquipment) == requiredEquipment;

    private IReadOnlyList<GridPosition>? FindConstructionAccessPath(
        GridPosition start,
        ConstructionSiteState site,
        ActorState? actor = null)
    {
        if (site.Kind is ConstructionKind.FoodStorage or ConstructionKind.WoodStorage or
            ConstructionKind.StoneStorage or ConstructionKind.EquipmentStorage or
            ConstructionKind.MaterialsStorage or ConstructionKind.WaterBarrel or
            ConstructionKind.GoblinFieldCamp)
        {
            return actor is null
                ? Navigation.FindPath(start, site.Anchor)
                : FindActorPathFrom(actor, start, site.Anchor);
        }

        var footprint = site.GetFootprint();
        var accessPositions = site.Kind is ConstructionKind.WoodenWall or
            ConstructionKind.StoneWall or ConstructionKind.WoodenDoor or
            ConstructionKind.WallTorch or ConstructionKind.PrimitiveWorkshop or
            ConstructionKind.Bloomery or ConstructionKind.SmeltingFurnace or
            ConstructionKind.CrucibleFurnace or
            ConstructionKind.GoblinHut
            ? footprint.SelectMany(World.GetCardinalWorldNeighbors)
            : footprint.SelectMany(position =>
                World.GetCardinalWorldNeighbors(position).Append(position));
        var destinations = accessPositions
            .Where(World.IsTerrainTraversable)
            .Distinct()
            .ToHashSet();
        return actor is null
            ? Navigation.FindPathToNearest(start, destinations)
            : FindActorPathToNearestFrom(actor, start, destinations);
    }

    private NavigationPathRequestResult RequestConstructionAccessPath(
        ActorState actor,
        ConstructionSiteState site)
    {
        if (site.Kind is ConstructionKind.FoodStorage or ConstructionKind.WoodStorage or
            ConstructionKind.StoneStorage or ConstructionKind.EquipmentStorage or
            ConstructionKind.MaterialsStorage or ConstructionKind.WaterBarrel or
            ConstructionKind.GoblinFieldCamp)
        {
            return RequestActorPath(actor, site.Anchor);
        }

        var footprint = site.GetFootprint();
        var accessPositions = site.Kind is ConstructionKind.WoodenWall or
            ConstructionKind.StoneWall or ConstructionKind.WoodenDoor or
            ConstructionKind.WallTorch or ConstructionKind.PrimitiveWorkshop or
            ConstructionKind.Bloomery or ConstructionKind.SmeltingFurnace or
            ConstructionKind.CrucibleFurnace or
            ConstructionKind.GoblinHut
            ? footprint.SelectMany(World.GetCardinalWorldNeighbors)
            : footprint.SelectMany(position =>
                World.GetCardinalWorldNeighbors(position).Append(position));
        var destinations = accessPositions
            .Where(World.IsTerrainTraversable)
            .Distinct()
            .ToHashSet();
        return RequestActorPathToNearest(actor, destinations);
    }

    private bool TryPlanHaulCollection(
        ActorState actor,
        Dictionary<EntityId, int> sourceReservations,
        Dictionary<EntityId, int> destinationReservations,
        EntityId? requiredDestination = null,
        bool assignedDestinationsOnly = false,
        int? maximumEstimatedDistance = null)
    {
        if (TryPlanNaturalWaterHaul(
                actor,
                destinationReservations,
                requiredDestination,
                assignedDestinationsOnly))
        {
            return true;
        }

        var storedQuantities = _itemStacks.Values
            .Where(stack => stack.Location.Kind == ItemLocationKind.StorageZone)
            .GroupBy(stack => stack.Location.OwnerId)
            .ToDictionary(group => group.Key, group => group.Sum(stack => stack.Quantity));
        var storedTypeQuantities = _itemStacks.Values
            .Where(stack => stack.Location.Kind == ItemLocationKind.StorageZone)
            .GroupBy(stack => (stack.Location.OwnerId, Type: GetStorageTypeKey(stack)))
            .ToDictionary(group => group.Key, group => group.Sum(stack => stack.Quantity));
        var usedTypeSlots = storedTypeQuantities.Keys
            .GroupBy(key => key.OwnerId)
            .ToDictionary(group => group.Key, group => group.Count());

        var candidates = new List<HaulCandidate>();
        foreach (var source in _itemStacks.Values.Where(stack =>
                     stack.Location.Kind is ItemLocationKind.Ground or ItemLocationKind.StorageZone &&
                     Visibility.Get(stack.Location.Position) != CellVisibility.Unknown &&
                     CanActorHaulStack(actor, stack) &&
                     (stack.Resource != ResourceKind.Water ||
                      actor.Equipment.HasFlag(PersonalEquipment.WoodenBucket))))
        {
            var protectedAtSource = source.Location.Kind == ItemLocationKind.StorageZone &&
                _storageZones.TryGetValue(source.Location.OwnerId, out var sourceZone)
                    ? Math.Max(0, sourceZone.DesiredQuantity -
                        (GetStored(sourceZone.Id) - source.Quantity))
                    : 0;
            var availableSource = Math.Max(
                0,
                source.Quantity - protectedAtSource -
                    sourceReservations.GetValueOrDefault(source.Id));
            if (availableSource <= 0)
            {
                continue;
            }

            var designationKind = source.Resource switch
            {
                ResourceKind.Wood => WorkDesignationKind.GatherBrushwood,
                ResourceKind.Stone or ResourceKind.Coal or ResourceKind.Ore =>
                    WorkDesignationKind.GatherStone,
                _ => default,
            };
            var isDesignatedLooseResource = designationKind != default &&
                source.Location.Kind == ItemLocationKind.Ground &&
                IsWorkDesignated(designationKind, source.Id, source.Location.Position);
            var isPriorityHaul = source.HaulPriority > StoragePriority.Normal;
            var candidateZones = _storageZones.Values.Where(zone =>
                         ZoneAccepts(zone, source) &&
                         IsHaulerAllowedForZone(actor, zone) &&
                         IsSourceAllowedForZone(source, zone) &&
                         CanStoreIndexed(zone, source, 1) &&
                         (requiredDestination is null || zone.Id == requiredDestination.Value) &&
                         (!assignedDestinationsOnly || IsExplicitHaulingDuty(actor, zone)))
                .Where(zone =>
                {
                    var stored = GetStored(zone.Id);
                    var reservedDestination = destinationReservations.GetValueOrDefault(zone.Id);
                    return isDesignatedLooseResource || isPriorityHaul ||
                        zone.DesiredQuantity > stored + reservedDestination;
                })
                .ToArray();
            if (candidateZones.Length == 0)
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
                var stored = GetStored(zone.Id);
                var reservedDestination = destinationReservations.GetValueOrDefault(zone.Id);
                var isPulledByStorage = zone.DesiredQuantity > stored + reservedDestination;
                var destinationLimit = isDesignatedLooseResource
                    ? zone.Capacity
                    : Math.Min(zone.Capacity, zone.DesiredQuantity);
                var availableDestination = Math.Min(
                    destinationLimit - stored - reservedDestination,
                    GetAvailableStorageIndexed(zone, source));
                if (availableDestination <= 0)
                {
                    continue;
                }

                var carryLimit = source.Resource == ResourceKind.Water
                    ? WoodenBucketWaterCapacity
                    : GetActorHaulQuantityLimit(actor, source);
                var quantity = Math.Min(
                    carryLimit,
                    Math.Min(availableSource, availableDestination));
                var estimatedDistance = checked(
                    ManhattanDistance(actor.Position, source.Location.Position) +
                    ManhattanDistance(source.Location.Position, zone.Position));
                if (maximumEstimatedDistance is { } maximumDistance &&
                    estimatedDistance > maximumDistance)
                {
                    continue;
                }
                candidates.Add(new HaulCandidate(
                    source.Id,
                    zone.Id,
                    quantity,
                    (StoragePriority)Math.Max(
                        (int)GetResourcePriority(source.Resource),
                        (int)source.HaulPriority),
                    zone.Priority,
                    source.Location.Position,
                    zone.Position,
                    estimatedDistance));
            }
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        var orderedCandidates = candidates
            .OrderByDescending(candidate => candidate.ResourcePriority)
            .ThenByDescending(candidate => candidate.DestinationPriority)
            .ThenBy(candidate => candidate.EstimatedDistance)
            .ThenBy(candidate => candidate.SourceStackId)
            .ThenBy(candidate => candidate.DestinationZoneId)
            .ToArray();
        var highestResourcePriority = orderedCandidates[0].ResourcePriority;
        var highestDestinationPriority = orderedCandidates
            .Where(candidate => candidate.ResourcePriority == highestResourcePriority)
            .Max(candidate => candidate.DestinationPriority);
        var priorityCandidates = orderedCandidates
            .Where(candidate =>
                candidate.ResourcePriority == highestResourcePriority &&
                candidate.DestinationPriority == highestDestinationPriority)
            .ToArray();
        var pageCount = (priorityCandidates.Length + MaximumHaulRouteCandidatesPerPlanningTick - 1) /
            MaximumHaulRouteCandidatesPerPlanningTick;
        var planningRound = CurrentTick.Value /
            Definitions.ActorPlanning.BackgroundPlanningIntervalTicks;
        var page = (int)((planningRound + (long)actor.Id.Value) % pageCount);
        foreach (var candidate in priorityCandidates
                     .Skip(page * MaximumHaulRouteCandidatesPerPlanningTick)
                     .Take(MaximumHaulRouteCandidatesPerPlanningTick))
        {
            var routeToSource = FindTribePath(actor.Position, candidate.SourcePosition);
            if (routeToSource is null)
            {
                continue;
            }

            actor.JobKind = ActorJobKind.Haul;
            actor.JobStage = ActorJobStage.Collecting;
            actor.SourceStackId = candidate.SourceStackId;
            actor.DestinationZoneId = candidate.DestinationZoneId;
            actor.ReservedQuantity = candidate.Quantity;
            actor.JobTarget = candidate.SourcePosition;
            BeginJobLeg(actor, routeToSource, Definitions.HaulHandlingTicks);
            sourceReservations[candidate.SourceStackId] = checked(
                sourceReservations.GetValueOrDefault(candidate.SourceStackId) + candidate.Quantity);
            destinationReservations[candidate.DestinationZoneId] = checked(
                destinationReservations.GetValueOrDefault(candidate.DestinationZoneId) +
                candidate.Quantity);
            return true;
        }
        return false;

        int GetStored(EntityId zoneId) => storedQuantities.GetValueOrDefault(zoneId);

        bool CanStoreIndexed(StorageZoneState zone, ItemStackState stack, int quantity)
        {
            if (!zone.SlotPolicy.Supports(GetStorageRequirement(stack)) ||
                GetStored(zone.Id) + quantity > zone.Capacity)
            {
                return false;
            }
            if (!zone.SlotPolicy.SeparatesItemTypes)
            {
                return true;
            }

            var typeKey = GetStorageTypeKey(stack);
            var storedOfKind = storedTypeQuantities.GetValueOrDefault((zone.Id, typeKey));
            return storedOfKind + quantity <= zone.SlotPolicy.StackCapacity &&
                (storedOfKind > 0 ||
                 usedTypeSlots.GetValueOrDefault(zone.Id) < zone.SlotPolicy.SlotCount);
        }

        int GetAvailableStorageIndexed(StorageZoneState zone, ItemStackState stack)
        {
            var totalAvailable = Math.Max(0, zone.Capacity - GetStored(zone.Id));
            if (!zone.SlotPolicy.Supports(GetStorageRequirement(stack)))
            {
                return 0;
            }
            if (!zone.SlotPolicy.SeparatesItemTypes)
            {
                return totalAvailable;
            }

            var typeKey = GetStorageTypeKey(stack);
            var storedOfKind = storedTypeQuantities.GetValueOrDefault((zone.Id, typeKey));
            if (storedOfKind == 0 &&
                usedTypeSlots.GetValueOrDefault(zone.Id) >= zone.SlotPolicy.SlotCount)
            {
                return 0;
            }
            return Math.Min(totalAvailable, zone.SlotPolicy.StackCapacity - storedOfKind);
        }
    }

    private bool TryPlanNaturalWaterHaul(
        ActorState actor,
        Dictionary<EntityId, int> destinationReservations,
        EntityId? requiredDestination,
        bool assignedDestinationsOnly)
    {
        if (IsJuvenile(actor) ||
            !actor.Equipment.HasFlag(PersonalEquipment.WoodenBucket))
        {
            return false;
        }

        var destinations = _storageZones.Values
            .Where(zone => zone.AcceptedResource == ResourceKind.Water &&
                zone.SourceStorageZoneId == EntityId.None &&
                IsHaulerAllowedForZone(actor, zone) &&
                zone.SlotPolicy.Supports(StorageRequirement.SealedLiquid) &&
                (requiredDestination is null || zone.Id == requiredDestination.Value) &&
                (!assignedDestinationsOnly || IsExplicitHaulingDuty(actor, zone)))
            .Select(zone => new
            {
                Zone = zone,
                Missing = Math.Min(zone.Capacity, zone.DesiredQuantity) -
                    GetStoredQuantity(zone.Id) -
                    destinationReservations.GetValueOrDefault(zone.Id),
            })
            .Where(candidate => candidate.Missing > 0)
            .ToArray();
        if (destinations.Length == 0)
        {
            return false;
        }

        var waterSources = GetShallowWaterSources();
        var routeRequest = !actor.NavigationKnowledge.HasBlockedBeliefs &&
            !_tribeNavigationKnowledge.HasBlockedBeliefs
                ? Navigation.RequestSharedPathToNearest(
                    actor.Position,
                    waterSources,
                    Definitions.ActorPlanning.MaximumPathExpansionsPerSlice)
                : RequestActorPathToNearest(actor, waterSources);
        if (routeRequest.Status != NavigationPathRequestStatus.Complete ||
            routeRequest.Path is not { } routeToSource)
        {
            return false;
        }
        var sourcePosition = routeToSource.Count == 0 ? actor.Position : routeToSource[^1];
        var destination = destinations
            .Select(candidate => new
            {
                candidate.Zone,
                candidate.Missing,
                Route = FindActorPathFrom(actor, sourcePosition, candidate.Zone.Position),
            })
            .Where(candidate => candidate.Route is not null)
            .OrderByDescending(candidate => candidate.Zone.Priority)
            .ThenBy(candidate => candidate.Route!.Count)
            .ThenBy(candidate => candidate.Zone.Id)
            .FirstOrDefault();
        if (destination is null)
        {
            return false;
        }

        var quantity = Math.Min(WoodenBucketWaterCapacity, destination.Missing);
        actor.JobKind = ActorJobKind.Haul;
        actor.JobStage = ActorJobStage.Collecting;
        actor.SourceStackId = EntityId.None;
        actor.DestinationZoneId = destination.Zone.Id;
        actor.ReservedQuantity = quantity;
        actor.JobTarget = sourcePosition;
        BeginJobLeg(actor, routeToSource, Definitions.HaulHandlingTicks);
        destinationReservations[destination.Zone.Id] = checked(
            destinationReservations.GetValueOrDefault(destination.Zone.Id) + quantity);
        return true;
    }

    private IReadOnlySet<GridPosition> GetShallowWaterSources()
    {
        if (_shallowWaterSources is not null)
        {
            return _shallowWaterSources;
        }

        _shallowWaterSources = (
            from y in Enumerable.Range(0, Map.Height)
            from x in Enumerable.Range(0, Map.Width)
            let position = new GridPosition(x, y, 0)
            where Map.GetCell(position).Terrain == TerrainKind.ShallowWater
            select position).ToHashSet();
        return _shallowWaterSources;
    }

    private bool HasAssignedStorageDuty(EntityId actorId) =>
        _storageZones.Values.Any(zone =>
            zone.AssignedHaulerId == actorId ||
            zone.LogisticsNetworkId != EntityId.None &&
            _logisticsNetworks[zone.LogisticsNetworkId].AssignedHaulerIds.Contains(actorId));

    private bool IsBackgroundPlanningTick(ActorState actor)
    {
        if (CurrentTick.Value == 1)
        {
            if (_burstPlannersThisTick >=
                Definitions.ActorPlanning.MaximumBurstPlannersPerTick)
            {
                return false;
            }

            _burstPlannersThisTick++;
            return true;
        }

        if (_lastConstructionCommandExecutionTick == CurrentTick.Value)
        {
            return false;
        }

        if (_lastCommandExecutionTick == CurrentTick.Value)
        {
            if (_burstPlannersThisTick >=
                Definitions.ActorPlanning.MaximumBurstPlannersPerTick)
            {
                return false;
            }

            _burstPlannersThisTick++;
            return true;
        }

        var interval = Definitions.ActorPlanning.BackgroundPlanningIntervalTicks;
        return CurrentTick.Value % interval == (long)(actor.Id.Value % (ulong)interval);
    }

    private void RemoveExhaustedWorkDesignations()
    {
        var completed = _workDesignations.Values
            .Where(designation => designation.Kind switch
            {
                WorkDesignationKind.GatherFood =>
                    World.GetPlantPatch(designation.Target) is not
                        { Kind: not PlantKind.ReedBed, Biomass: > 0 },
                WorkDesignationKind.GatherReeds =>
                    World.GetPlantPatch(designation.Target) is not
                        { Kind: PlantKind.ReedBed, Biomass: > 0 },
                WorkDesignationKind.GatherBrushwood =>
                    !_itemStacks.TryGetValue(designation.TargetEntityId, out var stack) ||
                    stack.Resource != ResourceKind.Wood ||
                    stack.Location.Kind != ItemLocationKind.Ground,
                WorkDesignationKind.GatherStone =>
                    !_itemStacks.TryGetValue(designation.TargetEntityId, out var stone) ||
                    !IsMineralResource(stone.Resource) ||
                    stone.Location.Kind != ItemLocationKind.Ground,
                WorkDesignationKind.UprootBerryBush =>
                    World.GetPlantPatch(designation.Target) is not
                        { Kind: PlantKind.BerryBush },
                WorkDesignationKind.FellTree => World.GetFellableWood(designation.Target) is null,
                WorkDesignationKind.QuarryBoulder =>
                    World.GetQuarriableBoulder(designation.Target) is null,
                WorkDesignationKind.MineRock =>
                    Visibility.Get(designation.Target) != CellVisibility.Unknown &&
                    !World.IsSolidRock(designation.Target) &&
                    !World.IsTerrainRampIntact(designation.Target),
                WorkDesignationKind.CarveRampDown =>
                    !World.CanCarveRampDown(designation.Target),
                WorkDesignationKind.CarveRampUp =>
                    !World.CanCarveRampUp(designation.Target),
                WorkDesignationKind.Scout =>
                    Visibility.Get(designation.Target) != CellVisibility.Unknown,
                WorkDesignationKind.HuntAnimal =>
                    !_animals.ContainsKey(designation.TargetEntityId.Value),
                WorkDesignationKind.CleanBlood => !HasCleanableBlood(designation.Target),
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
                Route = FindActorPath(actor, zone.Position),
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
            if (actor.SourceStackId == EntityId.None)
            {
                return actor.CarriedStackId == EntityId.None &&
                    actor.Equipment.HasFlag(PersonalEquipment.WoodenBucket) &&
                    zone.AcceptedResource == ResourceKind.Water &&
                    zone.SlotPolicy.Supports(StorageRequirement.SealedLiquid) &&
                    actor.ReservedQuantity <= WoodenBucketWaterCapacity &&
                    actor.JobTarget.Z == 0 &&
                    Map.GetCell(actor.JobTarget).Terrain == TerrainKind.ShallowWater &&
                    GetStoredQuantity(zone.Id) + actor.ReservedQuantity <= zone.Capacity;
            }

            if (actor.CarriedStackId != EntityId.None ||
                !_itemStacks.TryGetValue(actor.SourceStackId, out var source) ||
                !CanActorHaulStack(actor, source) ||
                actor.ReservedQuantity > GetActorHaulQuantityLimit(actor, source) ||
                source.Location.Kind is not (ItemLocationKind.Ground or
                    ItemLocationKind.StorageZone) ||
                !IsSourceAllowedForZone(source, zone) ||
                (source.Resource == ResourceKind.Water &&
                 !actor.Equipment.HasFlag(PersonalEquipment.WoodenBucket)))
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
            CanActorHaulStack(actor, carried) &&
            actor.ReservedQuantity <= GetActorHaulQuantityLimit(actor, carried) &&
            carried.Location == ItemLocation.CarriedBy(actor.Id) &&
            carried.Quantity == actor.ReservedQuantity &&
            CanStoreStack(zone, carried, actor.ReservedQuantity);
    }

    private void CompleteHaulCollection(ActorState actor)
    {
        if (actor.SourceStackId == EntityId.None)
        {
            var water = AllocateItemStack(
                ResourceKind.Water,
                actor.ReservedQuantity,
                ItemLocation.CarriedBy(actor.Id));
            actor.CarriedStackId = water.Id;
            actor.JobStage = ActorJobStage.Delivering;
            var waterDestination = _storageZones[actor.DestinationZoneId];
            var waterRoute = FindActorPath(actor, waterDestination.Position);
            if (waterRoute is null)
            {
                DropCarriedStack(actor);
                actor.ClearJob();
                return;
            }

            actor.JobTarget = waterDestination.Position;
            BeginJobLeg(actor, waterRoute, Definitions.HaulHandlingTicks);
            Publish(SimulationEventKind.ActorCollectedWater, actor.Id, EntityId.None, water.Quantity);
            return;
        }

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
                source.FoodKind,
                source.Variant);
        }

        actor.CarriedStackId = carried.Id;
        if (carried.Resource == ResourceKind.Wood)
        {
            GainForagingExperience(actor, Math.Max(1, carried.Quantity));
        }
        actor.SourceStackId = EntityId.None;
        actor.JobStage = ActorJobStage.Delivering;
        var destination = _storageZones[actor.DestinationZoneId];
        var route = FindActorPath(actor, destination.Position);
        if (route is null)
        {
            DropCarriedStack(actor);
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
            DropCarriedStack(actor);
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

    private bool HasQueuedSpecialistWork(ActorState actor)
    {
        var canBuild = actor.KnownSkills.HasFlag(GoblinSkill.Building);
        return canBuild && _workDesignations.Values.Any(designation =>
            !designation.IsSuspended && designation.Kind switch
            {
                WorkDesignationKind.FellTree =>
                    actor.Equipment.HasFlag(PersonalEquipment.WoodenAxe) &&
                    World.GetFellableWood(designation.Target) is not null,
                WorkDesignationKind.QuarryBoulder =>
                    MiningCapabilityPolicy.HasPickaxe(actor.Equipment) &&
                    World.GetQuarriableBoulder(designation.Target) is not null,
                WorkDesignationKind.MineRock =>
                    CanActorMineRock(actor, designation.Target),
                WorkDesignationKind.CarveRampDown =>
                    CanActorCarveRamp(actor, designation),
                WorkDesignationKind.CarveRampUp =>
                    CanActorCarveRamp(actor, designation),
                _ => false,
            });
    }

    private bool DropCarriedStack(ActorState actor)
    {
        if (actor.CarriedStackId == EntityId.None ||
            !_itemStacks.TryGetValue(actor.CarriedStackId, out var carried) ||
            carried.Location != ItemLocation.CarriedBy(actor.Id))
        {
            return false;
        }

        actor.CarriedStackId = EntityId.None;
        if (carried.Resource == ResourceKind.Water)
        {
            RemoveItemStack(carried.Id);
            Publish(SimulationEventKind.ItemStackDepleted, actor.Id, carried.Id, 0);
            return true;
        }

        MoveItemStack(carried, ItemLocation.OnGround(actor.Position));
        Publish(SimulationEventKind.ItemDropped, actor.Id, carried.Id, carried.Quantity);
        return true;
    }

    private bool IsHaulerAllowedForZone(ActorState actor, StorageZoneState zone)
    {
        if (zone.AssignedHaulerId != EntityId.None)
        {
            return zone.AssignedHaulerId == actor.Id;
        }

        if (zone.LogisticsNetworkId != EntityId.None)
        {
            return _logisticsNetworks[zone.LogisticsNetworkId]
                .AssignedHaulerIds.Contains(actor.Id);
        }

        return !_logisticsNetworks.Values.Any(network =>
            network.Id != EntityId.None && network.AssignedHaulerIds.Contains(actor.Id));
    }

    private bool CanActorHaulStack(ActorState actor, ItemStackState stack) =>
        !IsJuvenile(actor) ||
        stack.Resource != ResourceKind.Water &&
        GetHaulUnitWeight(stack) <= JuvenileMaximumHaulUnitWeight;

    private int GetActorHaulQuantityLimit(ActorState actor, ItemStackState stack)
    {
        if (!IsJuvenile(actor))
        {
            return Definitions.ActorCarryCapacity;
        }

        return Math.Max(
            1,
            Math.Min(
                Definitions.ActorCarryCapacity,
                (int)Math.Floor(JuvenileHaulWeightCapacity / GetHaulUnitWeight(stack))));
    }

    private static double GetHaulUnitWeight(ItemStackState stack)
    {
        if (MaterialCatalog.TryGet(stack.Resource, stack.Variant, out var material))
        {
            return material.UnitWeight;
        }
        if (stack.Resource == ResourceKind.Equipment &&
            EquipmentCatalog.FindDefinition(stack.Variant) is { } equipment)
        {
            return equipment.Weight;
        }

        return stack.Resource switch
        {
            ResourceKind.Food => 0.5,
            ResourceKind.Wood => 1.0,
            ResourceKind.Reeds => 0.18,
            ResourceKind.Bone => 0.85,
            ResourceKind.Vegetation => 0.25,
            ResourceKind.Coal => 1.3,
            ResourceKind.Stone or ResourceKind.Ore => 3.0,
            ResourceKind.Hide => 0.55,
            ResourceKind.Equipment => 2.0,
            ResourceKind.Materials => 1.0,
            ResourceKind.Water => 1.0,
            _ => 1.0,
        };
    }

    private bool IsExplicitHaulingDuty(ActorState actor, StorageZoneState zone) =>
        zone.AssignedHaulerId == actor.Id ||
        zone.LogisticsNetworkId != EntityId.None &&
        _logisticsNetworks[zone.LogisticsNetworkId].AssignedHaulerIds.Contains(actor.Id);

    private bool IsSourceAllowedForZone(ItemStackState source, StorageZoneState zone)
    {
        if (zone.SourceStorageZoneId != EntityId.None)
        {
            return source.Location.Kind == ItemLocationKind.StorageZone &&
                source.Location.OwnerId == zone.SourceStorageZoneId;
        }
        if (zone.LogisticsNetworkId == EntityId.None ||
            source.Location.Kind == ItemLocationKind.Ground)
        {
            return true;
        }
        return _logisticsNetworks[zone.LogisticsNetworkId]
            .SourceStorageZoneIds.Contains(source.Location.OwnerId);
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
        if (!World.CanTraverseTerrainEdge(actor.Position, next, canOpenDoors: true))
        {
            ObserveNavigationEdge(actor, next, NavigationBeliefStatus.Blocked);
            ReplanOrClearJob(actor);
            return;
        }

        if (!World.IsTerrainTraversable(next) &&
            World.TryGetWoodenDoorState(next, out var isDoorOpen) &&
            !isDoorOpen)
        {
            _undeliveredWorldChanges.Add(World.OpenWoodenDoorForTravel(next, CurrentTick));
            return;
        }

        if (!World.IsTerrainTraversable(next))
        {
            ObserveNavigationEdge(actor, next, NavigationBeliefStatus.Blocked);
            ReplanOrClearJob(actor);
            return;
        }

        ObserveNavigationEdge(actor, next, NavigationBeliefStatus.Passable);
        MoveActor(actor, next);
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

    private IReadOnlyList<GridPosition>? FindActorPath(
        ActorState actor,
        GridPosition destination) =>
        FindActorPathFrom(actor, actor.Position, destination);

    private NavigationPathRequestResult RequestActorPath(
        ActorState actor,
        GridPosition destination)
    {
        var maximumExpandedNodes = Definitions.ActorPlanning.MaximumPathExpansionsPerSlice;
        if (!actor.NavigationKnowledge.HasBlockedBeliefs &&
            !_tribeNavigationKnowledge.HasBlockedBeliefs)
        {
            return Navigation.RequestPath(
                actor.Position,
                destination,
                maximumExpandedNodes);
        }

        var beliefDuration = GetNavigationBeliefDurationTicks();
        var requestTick = CurrentTick;
        return Navigation.RequestPath(
            actor.Position,
            destination,
            new NavigationPathContext(
                actor.Id.Value,
                actor.NavigationKnowledge.Version,
                _tribeNavigationKnowledge.Version,
                requestTick.Value / beliefDuration),
            (from, to) => actor.NavigationKnowledge.AllowsTraversal(
                from,
                to,
                _tribeNavigationKnowledge,
                requestTick,
                beliefDuration,
                beliefDuration),
            maximumExpandedNodes);
    }

    private NavigationPathRequestResult RequestActorPathToNearest(
        ActorState actor,
        IReadOnlySet<GridPosition> destinations,
        Func<GridPosition, GridPosition, bool>? additionalEdgeFilter = null,
        ulong constraintKey = 0)
    {
        var maximumExpandedNodes = Definitions.ActorPlanning.MaximumPathExpansionsPerSlice;
        if (!actor.NavigationKnowledge.HasBlockedBeliefs &&
            !_tribeNavigationKnowledge.HasBlockedBeliefs)
        {
            if (additionalEdgeFilter is not null)
            {
                return Navigation.RequestPathToNearest(
                    actor.Position,
                    destinations,
                    new NavigationPathContext(
                        actor.Id.Value,
                        PersonalKnowledgeVersion: 0,
                        SharedKnowledgeVersion: 0,
                        FreshnessBucket: 0,
                        ConstraintKey: constraintKey),
                    additionalEdgeFilter,
                    maximumExpandedNodes);
            }
            return Navigation.RequestPathToNearest(
                actor.Position,
                destinations,
                maximumExpandedNodes,
                additionalEdgeFilter);
        }

        var beliefDuration = GetNavigationBeliefDurationTicks();
        var requestTick = CurrentTick;
        return Navigation.RequestPathToNearest(
            actor.Position,
            destinations,
            new NavigationPathContext(
                actor.Id.Value,
                actor.NavigationKnowledge.Version,
                _tribeNavigationKnowledge.Version,
                requestTick.Value / beliefDuration,
                constraintKey),
            (from, to) =>
                (additionalEdgeFilter is null || additionalEdgeFilter(from, to)) &&
                actor.NavigationKnowledge.AllowsTraversal(
                    from,
                    to,
                    _tribeNavigationKnowledge,
                    requestTick,
                    beliefDuration,
                    beliefDuration),
            maximumExpandedNodes);
    }

    private IReadOnlyList<GridPosition>? FindActorPathToNearest(
        ActorState actor,
        IReadOnlySet<GridPosition> destinations) =>
        FindActorPathToNearestFrom(actor, actor.Position, destinations);

    private IReadOnlyList<GridPosition>? FindActorPathToNearestFrom(
        ActorState actor,
        GridPosition start,
        IReadOnlySet<GridPosition> destinations)
    {
        if (!actor.NavigationKnowledge.HasBlockedBeliefs &&
            !_tribeNavigationKnowledge.HasBlockedBeliefs)
        {
            return Navigation.FindPathToNearest(start, destinations);
        }

        var beliefDuration = GetNavigationBeliefDurationTicks();
        return Navigation.FindPathToNearest(
            start,
            destinations,
            (from, to) => actor.NavigationKnowledge.AllowsTraversal(
                from,
                to,
                _tribeNavigationKnowledge,
                CurrentTick,
                beliefDuration,
                beliefDuration));
    }

    private IReadOnlyList<GridPosition>? FindActorPathFrom(
        ActorState actor,
        GridPosition start,
        GridPosition destination)
    {
        if (!actor.NavigationKnowledge.HasBlockedBeliefs &&
            !_tribeNavigationKnowledge.HasBlockedBeliefs)
        {
            return Navigation.FindPath(start, destination);
        }

        var beliefDuration = GetNavigationBeliefDurationTicks();
        return Navigation.FindPath(
            start,
            destination,
            new NavigationPathContext(
                actor.Id.Value,
                actor.NavigationKnowledge.Version,
                _tribeNavigationKnowledge.Version,
                CurrentTick.Value / beliefDuration),
            (from, to) => actor.NavigationKnowledge.AllowsTraversal(
                from,
                to,
                _tribeNavigationKnowledge,
                CurrentTick,
                beliefDuration,
                beliefDuration));
    }

    private IReadOnlyList<GridPosition>? FindTribePath(
        GridPosition start,
        GridPosition destination)
    {
        if (!_tribeNavigationKnowledge.HasBlockedBeliefs)
        {
            return Navigation.FindPath(start, destination);
        }

        var beliefDuration = GetNavigationBeliefDurationTicks();
        return Navigation.FindPath(
                start,
                destination,
                new NavigationPathContext(
                    OwnerId: 0,
                    PersonalKnowledgeVersion: 0,
                    SharedKnowledgeVersion: _tribeNavigationKnowledge.Version,
                    FreshnessBucket: CurrentTick.Value / beliefDuration),
                (from, to) => _tribeNavigationKnowledge.AllowsTraversal(
                    from,
                    to,
                    CurrentTick,
                    beliefDuration,
                    beliefDuration));
    }

    private long GetNavigationBeliefDurationTicks() =>
        Definitions.Clock.Climate.GetSeason(
            SimulationCalendar.At(CurrentTick, Definitions.Clock).Season).TicksPerDay;

    private void ObserveNavigationEdge(
        ActorState actor,
        GridPosition destination,
        NavigationBeliefStatus status)
    {
        var belief = actor.NavigationKnowledge.Observe(
            actor.Id,
            actor.Position,
            destination,
            status,
            CurrentTick);
        actor.PendingNavigationReports.Add(belief.Edge);
    }

    private void ReplanOrClearJob(ActorState actor)
    {
        var route = FindActorPath(actor, actor.JobTarget);
        if (route is null || route.Count == 0)
        {
            actor.ClearJob();
            return;
        }

        actor.RemainingRoute.Clear();
        actor.RemainingRoute.AddRange(route);
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
            var plantKind = World.GetPlantPatch(actor.Position)!.Value.Kind;
            var resource = plantKind == PlantKind.ReedBed
                ? ResourceKind.Reeds
                : ResourceKind.Food;
            var foodKind = resource == ResourceKind.Food ? FoodKindFor(plantKind) : FoodKind.None;
            var stack = FindMergeableGroundStack(resource, actor.Position, foodKind)
                ?? AllocateItemStack(resource, quantity: 0, ItemLocation.OnGround(actor.Position), foodKind);
            stack.Quantity = checked(stack.Quantity + gathered);
            GainForagingExperience(actor, Math.Max(1, gathered * 2));
            Publish(resource == ResourceKind.Food
                    ? SimulationEventKind.FoodGathered
                    : SimulationEventKind.ItemDropped,
                actor.Id,
                stack.Id,
                gathered);
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
            actor.ClearJob();
            return;
        }

        if (!World.IsTerrainTraversable(actor.JobTarget))
        {
            actor.ClearJob();
            return;
        }

        try
        {
            switch (actor.JobKind)
            {
                case ActorJobKind.Forage:
                    ValidateLoadedForageJob(actor);
                    break;
                case ActorJobKind.Haul:
                    ValidateLoadedHaulJob(actor);
                    break;
                case ActorJobKind.ClearConstructionSite:
                    ValidateLoadedConstructionClearanceJob(actor);
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
                case ActorJobKind.MineRock:
                    ValidateLoadedMineRockJob(actor);
                    break;
                case ActorJobKind.CarveRamp:
                    ValidateLoadedCarveRampJob(actor);
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
                case ActorJobKind.TendBud:
                    ValidateLoadedTendBudJob(actor);
                    break;
                case ActorJobKind.HuntAnimal:
                    ValidateLoadedHuntAnimalJob(actor);
                    break;
                case ActorJobKind.SupplyCrafting:
                    ValidateLoadedCraftingSupplyJob(actor);
                    break;
                case ActorJobKind.Craft:
                    ValidateLoadedCraftingWorkJob(actor);
                    break;
                case ActorJobKind.CleanBlood:
                    ValidateLoadedCleanBloodJob(actor);
                    break;
                case ActorJobKind.LootRaid:
                    ValidateLoadedRaidLootJob(actor);
                    break;
                case ActorJobKind.RecoverRaidCorpse:
                    ValidateLoadedRaidCorpseRecoveryJob(actor);
                    break;
                case ActorJobKind.ConsumeRaidCorpse:
                    ValidateLoadedRaidCorpseConsumptionJob(actor);
                    break;
                default:
                    throw new InvalidDataException("The save contains an unsupported actor job.");
            }

            ValidateLoadedJobExecution(actor);
        }
        catch (InvalidDataException)
        {
            // Jobs are transient derived state. If newer geometry or rules invalidate one,
            // release its reservations and let the dispatcher plan it again after loading.
            actor.ClearJob();
        }
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

    private void ValidateLoadedRaidLootJob(ActorState actor)
    {
        if (_raidPhase != GoblinRaidPhase.Looting || !_raidPartyIds.Contains(actor.Id) ||
            actor.CarriedCorpseId != EntityId.None || actor.ReservedQuantity != 0)
        {
            throw new InvalidDataException("The save contains an invalid raid-looting job.");
        }

        if (actor.JobStage == ActorJobStage.Collecting)
        {
            var hasCorpse = actor.SourceStackId != EntityId.None &&
                _corpses.TryGetValue(actor.SourceStackId, out var corpse) &&
                corpse.Position == actor.JobTarget && corpse.Contents.Any(IsRaidLootAllowed);
            var hasContainer = actor.SourceStackId == EntityId.None &&
                actor.DestinationZoneId != EntityId.None &&
                CreateVillageLootSnapshot().Any(container =>
                    container.StructureId.Value == actor.DestinationZoneId.Value &&
                    container.Position == actor.JobTarget &&
                    container.Contents.Any(IsRaidLootAllowed));
            if (actor.CarriedStackId != EntityId.None || (!hasCorpse && !hasContainer))
            {
                throw new InvalidDataException("The save contains invalid raid loot collection.");
            }
            return;
        }

        if (actor.JobStage != ActorJobStage.Delivering ||
            actor.SourceStackId != EntityId.None || actor.DestinationZoneId != EntityId.None ||
            actor.CarriedStackId == EntityId.None || actor.JobTarget != _raidRallyPoint)
        {
            throw new InvalidDataException("The save contains invalid raid loot delivery.");
        }
    }

    private void ValidateLoadedRaidCorpseRecoveryJob(ActorState actor)
    {
        if (_raidPhase != GoblinRaidPhase.Looting || !_raidPartyIds.Contains(actor.Id) ||
            actor.CarriedStackId != EntityId.None || actor.DestinationZoneId != EntityId.None ||
            actor.ReservedQuantity != 0)
        {
            throw new InvalidDataException("The save contains an invalid corpse-recovery job.");
        }

        if (actor.JobStage == ActorJobStage.Collecting)
        {
            if (actor.CarriedCorpseId != EntityId.None ||
                !_corpses.TryGetValue(actor.SourceStackId, out var corpse) ||
                corpse.Position != actor.JobTarget)
            {
                throw new InvalidDataException("The save contains invalid corpse collection.");
            }
            return;
        }

        if (actor.JobStage != ActorJobStage.Delivering ||
            actor.SourceStackId != EntityId.None || actor.CarriedCorpseId == EntityId.None ||
            actor.JobTarget != _raidRallyPoint)
        {
            throw new InvalidDataException("The save contains invalid corpse delivery.");
        }
    }

    private void ValidateLoadedRaidCorpseConsumptionJob(ActorState actor)
    {
        if (_raidPhase != GoblinRaidPhase.Looting || !_raidPartyIds.Contains(actor.Id) ||
            actor.JobStage != ActorJobStage.Collecting ||
            actor.CarriedStackId != EntityId.None || actor.CarriedCorpseId != EntityId.None ||
            actor.DestinationZoneId != EntityId.None || actor.ReservedQuantity != 0 ||
            !_corpses.TryGetValue(actor.SourceStackId, out var corpse) ||
            corpse.Kind != CorpseKind.Human || corpse.EdiblePortions <= 0 ||
            corpse.Position != actor.JobTarget)
        {
            throw new InvalidDataException("The save contains an invalid corpse-consumption job.");
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

    private void ValidateLoadedCleanBloodJob(ActorState actor)
    {
        if (actor.JobStage != ActorJobStage.None ||
            actor.CarriedStackId != EntityId.None ||
            actor.DestinationZoneId != EntityId.None ||
            actor.ReservedQuantity != 0 ||
            !_workDesignations.TryGetValue(actor.SourceStackId, out var designation) ||
            designation.Kind != WorkDesignationKind.CleanBlood ||
            designation.Target != actor.JobTarget ||
            !HasCleanableBlood(actor.JobTarget))
        {
            throw new InvalidDataException("The save contains an invalid blood-cleaning job.");
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
            !MiningCapabilityPolicy.HasPickaxe(actor.Equipment) ||
            !_workDesignations.TryGetValue(actor.SourceStackId, out var designation) ||
            designation.Kind != WorkDesignationKind.QuarryBoulder ||
            World.GetQuarriableBoulder(designation.Target) is null ||
            !AreCardinalNeighbors(actor.JobTarget, designation.Target))
        {
            throw new InvalidDataException("The save contains an invalid boulder-quarrying job.");
        }
    }

    private void ValidateLoadedMineRockJob(ActorState actor)
    {
        if (actor.JobStage != ActorJobStage.None ||
            actor.CarriedStackId != EntityId.None ||
            actor.DestinationZoneId != EntityId.None ||
            actor.ReservedQuantity != 0 ||
            !MiningCapabilityPolicy.HasPickaxe(actor.Equipment) ||
            !_workDesignations.TryGetValue(actor.SourceStackId, out var designation) ||
            designation.Kind != WorkDesignationKind.MineRock ||
            !CanActorMineRock(actor, designation.Target) ||
            !AreCardinalNeighbors(actor.JobTarget, designation.Target))
        {
            throw new InvalidDataException("The save contains an invalid rock-mining job.");
        }
    }

    private void ValidateLoadedCarveRampJob(ActorState actor)
    {
        if (actor.JobStage != ActorJobStage.None ||
            actor.CarriedStackId != EntityId.None ||
            actor.DestinationZoneId != EntityId.None ||
            actor.ReservedQuantity != 0 ||
            !MiningCapabilityPolicy.HasPickaxe(actor.Equipment) ||
            !_workDesignations.TryGetValue(actor.SourceStackId, out var designation) ||
            designation.Kind is not (WorkDesignationKind.CarveRampDown or
                WorkDesignationKind.CarveRampUp) ||
            actor.JobTarget != designation.Target ||
            !CanActorCarveRamp(actor, designation))
        {
            throw new InvalidDataException("The save contains an invalid ramp-carving job.");
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
            if (actor.SourceStackId == EntityId.None &&
                actor.CarriedStackId == EntityId.None &&
                actor.Equipment.HasFlag(PersonalEquipment.WoodenBucket) &&
                zone.AcceptedResource == ResourceKind.Water &&
                actor.ReservedQuantity <= WoodenBucketWaterCapacity &&
                actor.JobTarget.Z == 0 &&
                Map.GetCell(actor.JobTarget).Terrain == TerrainKind.ShallowWater)
            {
                return;
            }

            if (actor.CarriedStackId != EntityId.None ||
                !_itemStacks.TryGetValue(actor.SourceStackId, out var source) ||
                source.Location.Kind is not (ItemLocationKind.Ground or ItemLocationKind.StorageZone) ||
                source.Quantity < actor.ReservedQuantity ||
                actor.JobTarget != source.Location.Position ||
                (source.Resource == ResourceKind.Water &&
                 !actor.Equipment.HasFlag(PersonalEquipment.WoodenBucket)) ||
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

    private void ValidateLoadedConstructionClearanceJob(ActorState actor)
    {
        if (actor.ReservedQuantity <= 0 ||
            actor.ReservedQuantity > Definitions.ActorCarryCapacity)
        {
            throw new InvalidDataException("The save contains an invalid site-clearance reservation.");
        }

        if (actor.JobStage == ActorJobStage.Collecting)
        {
            if (actor.CarriedStackId != EntityId.None ||
                !_itemStacks.TryGetValue(actor.SourceStackId, out var source) ||
                !IsGroundStackBlockingConstruction(source) ||
                source.Quantity < actor.ReservedQuantity ||
                actor.JobTarget != source.Location.Position)
            {
                throw new InvalidDataException("The save contains invalid site-clearance collection state.");
            }
            return;
        }

        if (actor.JobStage != ActorJobStage.Delivering ||
            actor.SourceStackId != EntityId.None ||
            !_itemStacks.TryGetValue(actor.CarriedStackId, out var carried) ||
            carried.Location != ItemLocation.CarriedBy(actor.Id) ||
            carried.Quantity != actor.ReservedQuantity)
        {
            throw new InvalidDataException("The save contains invalid site-clearance delivery state.");
        }

        if (actor.DestinationZoneId != EntityId.None)
        {
            if (!_storageZones.TryGetValue(actor.DestinationZoneId, out var zone) ||
                actor.JobTarget != zone.Position ||
                !CanStoreStack(zone, carried, carried.Quantity))
            {
                throw new InvalidDataException("The save contains an invalid site-clearance storage target.");
            }
            return;
        }

        if (!World.IsTerrainTraversable(actor.JobTarget) ||
            _constructionSites.Values.Any(site => site.GetFootprint().Contains(actor.JobTarget)))
        {
            throw new InvalidDataException("The save contains an invalid site-clearance drop target.");
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
            site.MissingQuantity < actor.ReservedQuantity)
        {
            throw new InvalidDataException("The save contains an invalid construction delivery.");
        }

        if (actor.JobStage == ActorJobStage.Collecting)
        {
            if (actor.CarriedStackId != EntityId.None ||
                !_itemStacks.TryGetValue(actor.SourceStackId, out var source) ||
                !StackMatchesConstructionMaterial(site, source) ||
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
                !StackMatchesConstructionMaterial(site, carried) ||
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
            actor.ReservedQuantity <= 0 ||
            (actor.JobStage is ActorJobStage.ProvisioningFood or
                ActorJobStage.ProvisioningWater or ActorJobStage.ProvisioningEquipment &&
             actor.ReservedQuantity != 1) ||
            (actor.JobStage is ActorJobStage.ProvisioningFood or
                 ActorJobStage.ProvisioningAmmo or ActorJobStage.ProvisioningEquipment &&
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
            ActorJobKind.ClearConstructionSite => Definitions.HaulHandlingTicks,
            ActorJobKind.Rest => GetMaximumRestWorkTicks(),
            ActorJobKind.Eat => Definitions.EatWorkTicks,
            ActorJobKind.Resupply => Definitions.ResupplyWorkTicks,
            ActorJobKind.ClearVegetation => GetClearVegetationWorkTicks(),
            ActorJobKind.FellTree => GetFellTreeWorkTicks(),
            ActorJobKind.QuarryBoulder => GetQuarryBoulderWorkTicks(),
            ActorJobKind.MineRock => GetMineRockWorkTicks(actor),
            ActorJobKind.CarveRamp => GetCarveRampWorkTicks(actor),
            ActorJobKind.SupplyConstruction => Definitions.HaulHandlingTicks,
            ActorJobKind.BuildConstruction when
                _constructionSites.TryGetValue(actor.DestinationZoneId, out var site) =>
                site.TotalWorkTicks,
            ActorJobKind.Collapsed => GetMaximumRestWorkTicks(),
            ActorJobKind.TendBud => Definitions.Reproduction.TendWorkTicks,
            ActorJobKind.HuntAnimal => GetHuntWorkTicks(),
            ActorJobKind.SupplyCrafting => Definitions.HaulHandlingTicks,
            ActorJobKind.Craft when
                _craftingOrders.TryGetValue(actor.DestinationZoneId, out var craftingOrder) =>
                craftingOrder.TotalWorkTicks,
            ActorJobKind.CleanBlood => BloodCleaningWorkTicks,
            ActorJobKind.LootRaid => Definitions.HaulHandlingTicks,
            ActorJobKind.RecoverRaidCorpse => Definitions.HaulHandlingTicks,
            ActorJobKind.ConsumeRaidCorpse => Definitions.EatWorkTicks,
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
            if (!World.IsTerrainReachable(position) ||
                !World.CanTraverseTerrainEdge(previous, position, canOpenDoors: true))
            {
                throw new InvalidDataException("The save contains an invalid actor route.");
            }

            previous = position;
        }
    }

    private void ValidateLoadedJobReservations()
    {
        var caredBudIds = _actors.Values
            .Where(actor => actor.JobKind == ActorJobKind.TendBud)
            .Select(actor => actor.SourceStackId)
            .ToArray();
        if (caredBudIds.Distinct().Count() != caredBudIds.Length)
        {
            throw new InvalidDataException("Multiple goblins are assigned to the same living bud.");
        }

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
                reservation.Value > site.MissingQuantity)
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


        var craftingReservations = CreateCraftingReservations();
        foreach (var reservation in craftingReservations)
        {
            if (!_craftingOrders.TryGetValue(reservation.Key.OrderId, out var order) ||
                reservation.Value > order.GetMissing(
                    reservation.Key.Resource,
                    reservation.Key.Variant))
            {
                throw new InvalidDataException("Jobs over-reserve crafting material demand.");
            }
        }
        var duplicateCrafters = _actors.Values
            .Where(actor => actor.JobKind == ActorJobKind.Craft)
            .GroupBy(actor => actor.DestinationZoneId)
            .Any(group => group.Count() > 1);
        if (duplicateCrafters)
        {
            throw new InvalidDataException("Multiple goblins reserve one crafting order.");
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
        ActorJobKind.ClearConstructionSite => Definitions.HaulHandlingTicks,
        ActorJobKind.Rest => GetRestWorkTicks(actor),
        ActorJobKind.Eat => Definitions.EatWorkTicks,
        ActorJobKind.Resupply => Definitions.ResupplyWorkTicks,
        ActorJobKind.ClearVegetation => GetClearVegetationWorkTicks(),
        ActorJobKind.FellTree => GetFellTreeWorkTicks(),
        ActorJobKind.QuarryBoulder => GetQuarryBoulderWorkTicks(),
        ActorJobKind.MineRock => GetMineRockWorkTicks(actor),
        ActorJobKind.CarveRamp => GetCarveRampWorkTicks(actor),
        ActorJobKind.SupplyConstruction => Definitions.HaulHandlingTicks,
        ActorJobKind.BuildConstruction when
            _constructionSites.TryGetValue(actor.DestinationZoneId, out var site) =>
            site.RemainingWorkTicks,
        ActorJobKind.Collapsed => GetRestWorkTicks(actor),
        ActorJobKind.TendBud when _goblinBuds.TryGetValue(actor.SourceStackId, out var bud) =>
            bud.RemainingCareTicks,
        ActorJobKind.HuntAnimal => GetHuntWorkTicks(),
        ActorJobKind.SupplyCrafting => Definitions.HaulHandlingTicks,
        ActorJobKind.Craft when
            _craftingOrders.TryGetValue(actor.DestinationZoneId, out var craftingOrder) =>
            craftingOrder.RemainingWorkTicks,
        ActorJobKind.CleanBlood => BloodCleaningWorkTicks,
        ActorJobKind.LootRaid => Definitions.HaulHandlingTicks,
        ActorJobKind.RecoverRaidCorpse => Definitions.HaulHandlingTicks,
        ActorJobKind.ConsumeRaidCorpse => Definitions.EatWorkTicks,
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

    private int GetMineRockWorkTicks(ActorState actor)
    {
        var rock = _workDesignations.TryGetValue(actor.SourceStackId, out var designation)
            ? Map.IsRockPosition(designation.Target)
                ? Map.GetRockCell(designation.Target).Rock
                : RockKind.Sandstone
            : RockKind.Obsidian;
        var multiplier = MiningCapabilityPolicy.WorkMultiplier(rock);
        return checked(Definitions.ForageWorkTicks * 8 * multiplier);
    }

    private int GetCarveRampWorkTicks(ActorState actor)
    {
        var multiplier = _workDesignations.TryGetValue(actor.SourceStackId, out var designation) &&
            designation.Kind is WorkDesignationKind.CarveRampDown or WorkDesignationKind.CarveRampUp
                ? MiningCapabilityPolicy.WorkMultiplier(World.GetRampExcavationCell(
                    designation.Target,
                    designation.Kind == WorkDesignationKind.CarveRampDown).Rock)
                : MiningCapabilityPolicy.WorkMultiplier(RockKind.Obsidian);
        return checked(Definitions.ForageWorkTicks * 12 * multiplier);
    }

    private bool CanActorMineRock(ActorState actor, GridPosition target) =>
        actor.KnownSkills.HasFlag(GoblinSkill.Building) &&
        World.CanExcavateRock(target) &&
        MiningCapabilityPolicy.CanMine(
            Map.IsRockPosition(target)
                ? Map.GetRockCell(target)
                : new CaveCell(RockKind.Sandstone, CaveCellKind.SolidRock),
            actor.Equipment,
            actor.BuildingExperience);

    private bool CanActorCarveRamp(
        ActorState actor,
        WorkDesignationSnapshot designation)
    {
        var carveDown = designation.Kind == WorkDesignationKind.CarveRampDown;
        return actor.KnownSkills.HasFlag(GoblinSkill.Building) &&
            (carveDown
                ? World.CanCarveRampDown(designation.Target)
                : World.CanCarveRampUp(designation.Target)) &&
            MiningCapabilityPolicy.CanMine(
                World.GetRampExcavationCell(designation.Target, carveDown),
                actor.Equipment,
                actor.BuildingExperience);
    }

    private int GetHuntWorkTicks() => checked(Definitions.ForageWorkTicks * 2);

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

    private readonly record struct HaulCandidate(
        EntityId SourceStackId,
        EntityId DestinationZoneId,
        int Quantity,
        StoragePriority ResourcePriority,
        StoragePriority DestinationPriority,
        GridPosition SourcePosition,
        GridPosition DestinationPosition,
        int EstimatedDistance);

    private readonly record struct PersonalSupplySource(
        ItemStackState Stack,
        IReadOnlyList<GridPosition> Route);
}
