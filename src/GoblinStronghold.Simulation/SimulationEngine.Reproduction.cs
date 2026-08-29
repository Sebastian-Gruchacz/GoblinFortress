using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation;

public sealed partial class SimulationEngine
{
    private bool TryExecuteConfigurePopulationTarget(SimulationCommand command)
    {
        _populationTarget = command.Amount;
        Publish(
            SimulationEventKind.PopulationTargetConfigured,
            EntityId.None,
            EntityId.None,
            _populationTarget);
        return true;
    }

    private void TryCreateGoblinBud()
    {
        var readiness = CreateReproductionReadinessSnapshot();
        if (readiness.Kind != GoblinReproductionReadinessKind.Ready ||
            CurrentTick.Value % Definitions.ActorMovementIntervalTicks != 1)
        {
            return;
        }

        var parent = _actors.Values.FirstOrDefault(IsEligibleBudParent);
        if (parent is null)
        {
            return;
        }

        var site = FindBudSite(parent);
        if (site is null)
        {
            return;
        }

        ConsumeResource(ResourceKind.Food, Definitions.Reproduction.FoodCost);
        var budId = AllocateEntityId();
        _goblinBuds.Add(budId, new GoblinBudState(
            budId,
            parent.Id,
            EntityId.None,
            default,
            site.Value,
            Definitions.Reproduction.TendWorkTicks));
        parent.Health = Math.Max(1, parent.Health - Definitions.Reproduction.ParentHealthCost);
        parent.Hunger = Math.Min(
            Definitions.MaximumHunger,
            parent.Hunger + Definitions.Reproduction.ParentHungerCost);
        parent.Thirst = Math.Min(
            Definitions.MaximumThirst,
            parent.Thirst + Definitions.Reproduction.ParentThirstCost);
        parent.Fatigue = Math.Min(
            Definitions.MaximumFatigue,
            parent.Fatigue + Definitions.Reproduction.ParentFatigueCost);
        Publish(
            SimulationEventKind.GoblinBudCreated,
            parent.Id,
            budId,
            Definitions.Reproduction.FoodCost);
    }

    private bool TryCreateGoblinBudFromCorpse(
        EntityId corpseId,
        EntityId pollinatorId,
        GridPosition position)
    {
        if (pollinatorId == EntityId.None ||
            !_actors.TryGetValue(pollinatorId, out var pollinator) ||
            pollinator.Position != position ||
            !_corpses.TryGetValue(corpseId, out var corpse) ||
            corpse.Kind != CorpseKind.Human || corpse.Position != position ||
            !World.IsTerrainTraversable(position))
        {
            return false;
        }

        _corpses.Remove(corpseId);
        var budId = AllocateEntityId();
        _goblinBuds.Add(budId, new GoblinBudState(
            budId,
            pollinatorId,
            corpseId,
            corpse.InheritanceImprint,
            position,
            Definitions.Reproduction.TendWorkTicks));
        Publish(
            SimulationEventKind.GoblinBudCreated,
            pollinatorId,
            budId,
            0);
        return true;
    }

    private bool IsEligibleBudParent(ActorState actor) =>
        !IsJuvenile(actor) &&
        actor.Health >= Definitions.MaximumHealth * 3 / 4 &&
        actor.Hunger < Definitions.FoodSeekThreshold &&
        actor.Thirst < Definitions.DrinkThreshold &&
        actor.Fatigue < Definitions.RestThreshold / 2 &&
        actor.JobKind == ActorJobKind.None &&
        actor.CarriedStackId == EntityId.None &&
        !_raidPartyIds.Contains(actor.Id) &&
        _goblinBuds.Values.All(bud => bud.ParentId != actor.Id);

    private GridPosition? FindBudSite(ActorState parent)
    {
        var candidates = EnumerateSuitableBudSites(onlyVacant: true)
            .Select(position => new
            {
                Position = position,
                Route = FindActorPath(parent, position),
            })
            .Where(candidate => candidate.Route is not null)
            .OrderBy(candidate => candidate.Route!.Count)
            .ThenBy(candidate => candidate.Position.Y)
            .ThenBy(candidate => candidate.Position.X)
            .FirstOrDefault();
        return candidates?.Position;
    }

    private IEnumerable<GridPosition> EnumerateSuitableBudSites(bool onlyVacant) =>
        World.EnumerateWorldObjects()
            .Where(worldObject =>
                worldObject.Kind == WorldObjectKind.GoblinHut &&
                worldObject.Owner == WorldObjectOwner.GoblinTribe)
            .SelectMany(worldObject => worldObject.GetAbsoluteParts())
            .Where(item =>
                item.Position.Z == 0 &&
                item.Part.Kind is WorldObjectPartKind.Floor or WorldObjectPartKind.Door &&
                World.IsTerrainTraversable(item.Position) &&
                Map.GetCell(item.Position).Moisture >= Definitions.Reproduction.MinimumMoisture &&
                (!onlyVacant || _goblinBuds.Values.All(bud => bud.Position != item.Position)))
            .Select(item => item.Position)
            .Distinct();

    public GoblinReproductionReadinessSnapshot InspectReproductionReadiness() =>
        CreateReproductionReadinessSnapshot();

    private GoblinReproductionReadinessSnapshot CreateReproductionReadinessSnapshot()
    {
        var availableFood = GetAvailableResourceQuantity(ResourceKind.Food);
        var suitableSites = EnumerateSuitableBudSites(onlyVacant: true).Count();
        var eligibleParents = _actors.Values.Count(IsEligibleBudParent);
        var caredBudIds = _actors.Values
            .Where(actor => actor.JobKind == ActorJobKind.TendBud)
            .Select(actor => actor.SourceStackId)
            .ToHashSet();
        var untendedBuds = _goblinBuds.Values.Count(bud => !caredBudIds.Contains(bud.Id));
        var kind = _goblinBuds.Count > 0
            ? untendedBuds > 0
                ? GoblinReproductionReadinessKind.BudWaitingForCare
                : GoblinReproductionReadinessKind.BudBeingTended
            : _actors.Count >= _populationTarget
                ? GoblinReproductionReadinessKind.AtTarget
                : availableFood < Definitions.Reproduction.FoodCost
                    ? GoblinReproductionReadinessKind.InsufficientFood
                    : suitableSites == 0
                        ? GoblinReproductionReadinessKind.NoMoistSpace
                        : eligibleParents == 0
                            ? GoblinReproductionReadinessKind.NoEligibleParent
                            : GoblinReproductionReadinessKind.Ready;
        return new(
            kind,
            Definitions.Reproduction.FoodCost,
            availableFood,
            suitableSites,
            eligibleParents,
            untendedBuds);
    }

    private TribeNeedsSnapshot CreateTribeNeedsSnapshot()
    {
        var storedUnits = _itemStacks.Values
            .Where(stack => stack.Location.Kind == ItemLocationKind.StorageZone)
            .Sum(stack => stack.Quantity);
        var knownLooseUnits = _itemStacks.Values
            .Where(stack =>
                stack.Location.Kind == ItemLocationKind.Ground &&
                Visibility.Get(stack.Location.Position) != CellVisibility.Unknown)
            .Sum(stack => stack.Quantity);
        var hutCapacity = World.EnumerateWorldObjects()
            .Where(worldObject =>
                worldObject.Kind == WorldObjectKind.GoblinHut &&
                worldObject.Owner == WorldObjectOwner.GoblinTribe)
            .SelectMany(worldObject => worldObject.GetAbsoluteParts())
            .Where(item => item.Part.Kind is WorldObjectPartKind.Floor or WorldObjectPartKind.Door)
            .Select(item => item.Position)
            .Distinct()
            .Count();
        var fieldCampCapacity = World.EnumerateWorldObjects().Count(worldObject =>
            worldObject.Kind == WorldObjectKind.GoblinFieldCamp &&
            worldObject.Owner == WorldObjectOwner.GoblinTribe) * SimulationDefinitions.FieldCampCapacity;
        var healthyWorkers = _actors.Values.Count(actor =>
            !IsJuvenile(actor) &&
            actor.Health >= Definitions.MaximumHealth / 2 &&
            actor.Hunger < Definitions.CriticalHungerThreshold &&
            actor.Thirst < Definitions.DehydrationThirstThreshold &&
            actor.JobKind != ActorJobKind.Collapsed);
        return new(
            FoodUnits: GetTotalResourceQuantity(ResourceKind.Food),
            ExpectedDailyFoodUnits: checked(_actors.Count * Definitions.PersonalFoodCapacity),
            ShelterCapacity: checked(hutCapacity + fieldCampCapacity),
            StorageCapacity: _storageZones.Values.Sum(zone => zone.Capacity),
            StoredUnits: storedUnits,
            KnownLooseUnits: knownLooseUnits,
            SuitableMoistSites: EnumerateSuitableBudSites(onlyVacant: true).Count(),
            HealthyWorkers: healthyWorkers,
            WorkDemand: checked(
                _workDesignations.Count + _constructionSites.Count + _goblinBuds.Count),
            HumanHostility: _humanVillage.Hostility,
            Reproduction: CreateReproductionReadinessSnapshot());
    }

    private bool TryPlanTendBudJob(ActorState actor)
    {
        var reservedBudIds = _actors.Values
            .Where(candidate => candidate.JobKind == ActorJobKind.TendBud)
            .Select(candidate => candidate.SourceStackId)
            .ToHashSet();
        var target = _goblinBuds.Values
            .Where(bud => !reservedBudIds.Contains(bud.Id))
            .Select(bud => new
            {
                Bud = bud,
                Route = FindActorPath(actor, bud.Position),
            })
            .Where(candidate => candidate.Route is not null)
            .OrderBy(candidate => candidate.Route!.Count)
            .ThenBy(candidate => candidate.Bud.Id)
            .FirstOrDefault();
        if (target is null)
        {
            return false;
        }

        actor.JobKind = ActorJobKind.TendBud;
        actor.JobTarget = target.Bud.Position;
        actor.SourceStackId = target.Bud.Id;
        BeginJobLeg(actor, target.Route!, target.Bud.RemainingCareTicks);
        return true;
    }

    private void UpdateTendBudJob(ActorState actor)
    {
        if (!_goblinBuds.TryGetValue(actor.SourceStackId, out var bud) ||
            bud.Position != actor.JobTarget)
        {
            actor.ClearJob();
            return;
        }

        if (actor.JobPhase == ActorJobPhase.Traveling)
        {
            AdvanceTravel(actor);
        }
        if (actor.JobKind != ActorJobKind.TendBud || actor.JobPhase != ActorJobPhase.Working)
        {
            return;
        }

        bud.RemainingCareTicks--;
        actor.RemainingWorkTicks--;
        if (bud.RemainingCareTicks > 0)
        {
            return;
        }

        actor.ClearJob();
    }

    private void FinalizeMatureGoblinBuds()
    {
        foreach (var bud in _goblinBuds.Values
                     .Where(bud => bud.RemainingCareTicks <= 0)
                     .ToArray())
        {
            _goblinBuds.Remove(bud.Id);
            var newborn = AllocateActor(
                bud.Position,
                Definitions.EatThreshold / 2,
                Math.Max(1, Definitions.MaximumHealth / 2));
            newborn.Equipment = PersonalEquipment.RagClothes |
                PersonalEquipment.PrimitiveWaterskin;
            newborn.PersonalWater = Math.Min(1, Definitions.PersonalWaterCapacity);
            newborn.BirthTick = CurrentTick.Value;
            newborn.MaturesAtTick = checked(CurrentTick.Value + GetJuvenileDurationTicks());
            newborn.AgeOffsetTicks = 0;
            if (_actors.TryGetValue(bud.ParentId, out var parent))
            {
                ApplyPartialInheritance(newborn, parent);
            }
            if (bud.OriginCorpseId != EntityId.None)
            {
                ApplyPartialInheritance(
                    newborn,
                    bud.OriginImprint,
                    sampleKey: bud.OriginCorpseId.Value,
                    maySkip: true);
            }
            Publish(SimulationEventKind.GoblinBorn, newborn.Id, bud.ParentId, 1);
        }
    }

    private long GetJuvenileDurationTicks()
    {
        var seasons = Definitions.Clock.Climate.Seasons;
        var birthSeason = SimulationCalendar.At(CurrentTick, Definitions.Clock).Season;
        var startIndex = seasons
            .Select((season, index) => (season.Season, index))
            .Single(item => item.Season == birthSeason).index;
        var duration = 0L;
        for (var offset = 0; offset < Definitions.Reproduction.JuvenileSeasonCount; offset++)
        {
            duration = checked(duration + seasons[(startIndex + offset) % seasons.Count].TotalTicks);
        }
        return duration;
    }

    private void ApplyPartialInheritance(ActorState child, ActorState parent)
    {
        ApplyPartialInheritance(
            child,
            new GoblinInheritanceImprint(
                parent.KnownSkills,
                parent.KnownTraits,
                new GoblinExperienceSnapshot(
                    parent.ForagingExperience,
                    parent.HaulingExperience,
                    parent.BuildingExperience),
                parent.WorkPreferences),
            sampleKey: parent.Id.Value,
            maySkip: false);
    }

    private void ApplyPartialInheritance(
        ActorState child,
        GoblinInheritanceImprint source,
        ulong sampleKey,
        bool maySkip)
    {
        var inheritedSkills = SelectInheritedFlag(
            source.KnownSkills,
            child.Id,
            sampleKey: 0x534B494C4CUL ^ sampleKey,
            maySkip);
        child.KnownSkills |= inheritedSkills;
        child.KnownTraits |= SelectInheritedFlag(
            source.KnownTraits,
            child.Id,
            sampleKey: 0x5452414954UL ^ sampleKey,
            maySkip);
        if (inheritedSkills.HasFlag(GoblinSkill.Foraging))
        {
            child.ForagingExperience = Math.Max(
                child.ForagingExperience,
                source.Experience.Foraging / 10);
        }
        if (inheritedSkills.HasFlag(GoblinSkill.Hauling))
        {
            child.HaulingExperience = Math.Max(
                child.HaulingExperience,
                source.Experience.Hauling / 10);
        }
        if (inheritedSkills.HasFlag(GoblinSkill.Building))
        {
            child.BuildingExperience = Math.Max(
                child.BuildingExperience,
                source.Experience.Building / 10);
        }
        if (Convert.ToUInt64(inheritedSkills) != 0)
        {
            child.WorkPreferences = new GoblinWorkPreferences(
                (child.WorkPreferences.Foraging + source.WorkPreferences.Foraging) / 2,
                (child.WorkPreferences.Hauling + source.WorkPreferences.Hauling) / 2,
                (child.WorkPreferences.Building + source.WorkPreferences.Building) / 2);
        }
    }

    private TFlag SelectInheritedFlag<TFlag>(
        TFlag parentFlags,
        EntityId childId,
        ulong sampleKey,
        bool maySkip)
        where TFlag : struct, Enum
    {
        var flags = Enum.GetValues<TFlag>()
            .Where(flag => Convert.ToUInt64(flag) != 0 && parentFlags.HasFlag(flag))
            .ToArray();
        if (flags.Length == 0)
        {
            return default;
        }

        var index = DeterministicRandom.NextInt(
            WorldSeed,
            RandomDomain.GoblinIdentity,
            childId,
            CurrentTick,
            sampleKey,
            minimumInclusive: 0,
            maximumExclusive: flags.Length + (maySkip ? 1 : 0));
        return index == flags.Length ? default : flags[index];
    }

    private void LoadGoblinBuds(IEnumerable<GoblinBudSaveModel> models)
    {
        foreach (var model in models.OrderBy(model => model.Id))
        {
            var id = new EntityId(model.Id);
            var parentId = new EntityId(model.ParentId);
            var originCorpseId = new EntityId(model.OriginCorpseId);
            var originImprint = new GoblinInheritanceImprint(
                model.OriginSkills,
                model.OriginTraits,
                new GoblinExperienceSnapshot(
                    model.OriginForagingExperience,
                    model.OriginHaulingExperience,
                    model.OriginBuildingExperience),
                new GoblinWorkPreferences(
                    model.OriginForagingPreference,
                    model.OriginHaulingPreference,
                    model.OriginBuildingPreference));
            var position = new GridPosition(model.X, model.Y, model.Z);
            if (id == EntityId.None ||
                parentId == EntityId.None && originCorpseId == EntityId.None ||
                !HasOnlyKnownFlags(originImprint.KnownSkills, GoblinSkill.Building) ||
                !HasOnlyKnownFlags(originImprint.KnownTraits, GoblinTrait.Fastidious) ||
                originImprint.Experience.Foraging < 0 ||
                originImprint.Experience.Hauling < 0 ||
                originImprint.Experience.Building < 0 ||
                !originImprint.WorkPreferences.IsValid ||
                model.RemainingCareTicks <= 0 ||
                model.RemainingCareTicks > Definitions.Reproduction.TendWorkTicks ||
                (originCorpseId == EntityId.None
                    ? !IsSuitableBudSite(position)
                    : !World.IsTerrainTraversable(position)) ||
                _goblinBuds.Values.Any(bud => bud.Position == position) ||
                !_goblinBuds.TryAdd(id, new GoblinBudState(
                    id,
                    parentId,
                    originCorpseId,
                    originImprint,
                    position,
                    model.RemainingCareTicks)))
            {
                throw new InvalidDataException("The save contains an invalid goblin bud.");
            }
        }
    }

    private bool IsSuitableBudSite(GridPosition position) =>
        position.Z == 0 &&
        Map.IsWithin(position) &&
        Map.GetCell(position).Moisture >= Definitions.Reproduction.MinimumMoisture &&
        World.GetWorldObjectsAt(position).Any(worldObject =>
            worldObject.Kind == WorldObjectKind.GoblinHut &&
            worldObject.Owner == WorldObjectOwner.GoblinTribe &&
            worldObject.GetAbsoluteParts().Any(item =>
                item.Position == position &&
                item.Part.Kind is WorldObjectPartKind.Floor or WorldObjectPartKind.Door));

    private void ValidateLoadedTendBudJob(ActorState actor)
    {
        if (actor.JobStage != ActorJobStage.None ||
            actor.CarriedStackId != EntityId.None ||
            actor.DestinationZoneId != EntityId.None ||
            actor.ReservedQuantity != 0 ||
            !_goblinBuds.TryGetValue(actor.SourceStackId, out var bud) ||
            bud.Position != actor.JobTarget)
        {
            throw new InvalidDataException("The save contains an invalid bud-care job.");
        }
    }
}
