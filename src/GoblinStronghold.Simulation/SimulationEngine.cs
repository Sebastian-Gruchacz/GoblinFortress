using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoblinStronghold.Simulation.Civilizations;
using GoblinStronghold.Simulation.Civilizations.Naming;
using GoblinStronghold.Simulation.Civilizations.Polities;
using GoblinStronghold.Simulation.Construction;
using GoblinStronghold.Simulation.ContentPacks;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Map.Generation;
using GoblinStronghold.Simulation.Resources;
using GoblinStronghold.Simulation.Shelter;
using GoblinStronghold.Simulation.Terrain;
using GoblinStronghold.Simulation.Workshops;
using GoblinStronghold.Simulation.Visibility;
using GoblinStronghold.Simulation.WorkPriorities;

namespace GoblinStronghold.Simulation;

public sealed partial class SimulationEngine
{
    private const int SaveFormatVersion = SimulationSaveFormat.CurrentVersion;
    public const int MinimumRaidTargetRadius = 3;
    public const int MaximumRaidTargetRadius = 10;
    public const int DefaultRaidTargetRadius = 6;
    public const RaidDirective DefaultRaidDirectives =
        RaidDirective.AttackGuards |
        RaidDirective.LootEquipment |
        RaidDirective.LootSupplies |
        RaidDirective.LootFood;

    private static readonly JsonSerializerOptions SaveOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly SortedDictionary<EntityId, ActorState> _actors = [];
    private readonly SortedDictionary<EntityId, ItemStackState> _itemStacks = [];
    private readonly SortedDictionary<EntityId, StorageZoneState> _storageZones = [];
    private readonly SortedDictionary<EntityId, StorageAreaState> _storageAreas = [];
    private readonly SortedDictionary<EntityId, LogisticsNetworkState> _logisticsNetworks = [];
    private readonly ResourceSpatialIndex _resourceSpatialIndex = new();
    private readonly SortedDictionary<ResourceKind, StoragePriority> _resourcePriorities = [];
    private readonly SortedDictionary<EntityId, ConstructionSiteState> _constructionSites = [];
    private readonly SortedDictionary<EntityId, CraftingOrderState> _craftingOrders = [];
    private readonly SortedDictionary<EntityId, WorkDesignationSnapshot> _workDesignations = [];
    private readonly SortedDictionary<EntityId, GoblinBudState> _goblinBuds = [];
    private readonly SortedDictionary<EntityId, CorpseState> _corpses = [];
    private readonly SortedSet<ResourceVariant> _stolenVillageEquipment = [];
    private readonly SortedDictionary<ulong, AnimalState> _animals = [];
    private readonly CivilizationDefinition _goblinCivilization;
    private readonly CivilizationDefinition _humanCivilization;
    private readonly CivilizationActorGenerator _goblinActorGenerator;
    private readonly UndergroundFactionDirector _undergroundFactions;
    private readonly NavigationKnowledgeState _tribeNavigationKnowledge = new();
    private readonly SortedDictionary<CommandKey, SimulationCommand> _pendingCommands = [];
    private readonly List<SimulationEvent> _undeliveredEvents = [];
    private readonly List<WorldChangeEvent> _undeliveredWorldChanges = [];
    private HumanVillageState _humanVillage;
    private GoblinRaidPhase _raidPhase;
    private GridPosition _raidRallyPoint;
    private readonly SortedSet<EntityId> _raidPartyIds = [];
    private bool _raidRosterConfigured;
    private GridPosition _raidTarget;
    private int _raidTargetRadius = DefaultRaidTargetRadius;
    private RaidDirective _raidDirectives = DefaultRaidDirectives;
    private ulong _nextAnimalId = 1;
    private ulong _nextEntityId = 1;
    private ulong _nextEventSequence = 1;
    private int _compostNutrients;
    private long _ticksExecuted;
    private long _commandsExecuted;
    private long _eventsPublished;
    private long _actorUpdates;
    private long _lastCommandExecutionTick = -1;
    private long _lastConstructionCommandExecutionTick = -1;
    private long _lastTickStopwatchTicks;
    private long _totalTickStopwatchTicks;
    private long _presentationSnapshotsCreated;
    private long _lastPresentationSnapshotStopwatchTicks;
    private long _totalPresentationSnapshotStopwatchTicks;
    private long[] _lastTickStageStopwatchTicks = new long[10];

    private SimulationEngine(
        WorldSeed worldSeed,
        SimulationDefinitions definitions,
        GeneratedMap map,
        SimulationDebugSettings debugSettings)
    {
        WorldSeed = worldSeed;
        Definitions = definitions;
        DebugSettings = debugSettings;
        World = WorldMapState.CreateInitial(map);
        Navigation = new NavigationPathService(World);
        Visibility = WorldVisibilityState.Create(map);
        _goblinCivilization = CivilizationCatalog.Current.Get(
            CivilizationLegacyRole.PlayerGoblins);
        _humanCivilization = CivilizationCatalog.Current.Get(
            CivilizationLegacyRole.HumanVillage);
        _goblinActorGenerator = new CivilizationActorGenerator(
            worldSeed,
            _goblinCivilization.ActorGeneration ?? throw new InvalidDataException(
                $"Civilization '{_goblinCivilization.Id}' has no actor-generation profile."));
        _undergroundFactions = UndergroundFactionDirector.Create(worldSeed, map.MinimumWorldLevel);
        _humanVillage = HumanVillageState.CreateInitial(
            World,
            definitions,
            HumanVitals,
            HumanPopulationNeeds,
            HumanSpatialBehavior);
        _raidTarget = map.HumanVillage;
        _logisticsNetworks.Add(
            EntityId.None,
            new LogisticsNetworkState(EntityId.None, "Default"));
        foreach (var resource in Enum.GetValues<ResourceKind>().Where(IsStorableResource))
        {
            _resourcePriorities.Add(resource, StoragePriority.Normal);
        }
        InitializeWorkTypePriorities();
    }

    public WorldSeed WorldSeed { get; }

    public SimulationDefinitions Definitions { get; }

    public SimulationDebugSettings DebugSettings { get; }

    public int MaximumGoblinHealth => GoblinVitals.MaximumHealth;

    public CivilizationCombatDefinition GoblinCombat => _goblinCivilization.Combat!;

    public CivilizationPerceptionDefinition GoblinPerception =>
        _goblinCivilization.Perception!;

    public CivilizationSpatialBehaviorDefinition GoblinSpatialBehavior =>
        _goblinCivilization.SpatialBehavior!;

    public int MaximumGoblinHunger => GoblinNeeds.MaximumHunger;

    public int MaximumGoblinThirst => GoblinNeeds.MaximumThirst;

    public int MaximumGoblinFatigue => GoblinNeeds.MaximumFatigue;

    public GeneratedMap Map => World.Baseline;

    public WorldMapState World { get; private set; }

    public NavigationPathService Navigation { get; private set; }

    public WorldVisibilityState Visibility { get; private set; }

    private CivilizationVitalsDefinition GoblinVitals => _goblinCivilization.Vitals!;

    public CivilizationNeedsDefinition GoblinNeeds => _goblinCivilization.Needs!;

    private CivilizationAgingDefinition GoblinAging => _goblinCivilization.Aging!;

    private CivilizationVitalsDefinition HumanVitals => _humanCivilization.Vitals!;

    public CivilizationCombatDefinition HumanCombat => _humanCivilization.Combat!;

    public CivilizationPerceptionDefinition HumanPerception =>
        _humanCivilization.Perception!;

    public CivilizationSpatialBehaviorDefinition HumanSpatialBehavior =>
        _humanCivilization.SpatialBehavior!;

    public CivilizationPopulationNeedsDefinition HumanPopulationNeeds =>
        _humanCivilization.PopulationNeeds!;

    public SimulationTick CurrentTick { get; private set; } = SimulationTick.Zero;

    public ulong NextAvailableCommandSequence => _pendingCommands.Count == 0
        ? 1
        : checked(_pendingCommands.Values.Max(command => command.Sequence) + 1);

    public ResourceSpatialIndexSnapshot CreateResourceSpatialSnapshot() =>
        _resourceSpatialIndex.CreateSnapshot();

    public IReadOnlyList<NavigationBelief> CreateTribeNavigationKnowledgeSnapshot() =>
        _tribeNavigationKnowledge.CreateSnapshot();

    public IReadOnlyList<ResourceInventorySnapshot> CreateResourceInventorySnapshot()
    {
        var totals = _resourcePriorities.Keys.Where(IsInventoryResource).ToDictionary(
            resource => resource,
            _ => (Stored: 0, KnownLoose: 0, Carried: 0));
        foreach (var stack in _itemStacks.Values)
        {
            if (!totals.TryGetValue(stack.Resource, out var total))
            {
                continue;
            }

            total = stack.Location.Kind switch
            {
                ItemLocationKind.StorageZone => total with
                {
                    Stored = checked(total.Stored + stack.Quantity),
                },
                ItemLocationKind.Ground when
                    Visibility.Get(stack.Location.Position) != CellVisibility.Unknown => total with
                {
                    KnownLoose = checked(total.KnownLoose + stack.Quantity),
                },
                ItemLocationKind.ActorInventory => total with
                {
                    Carried = checked(total.Carried + stack.Quantity),
                },
                _ => total,
            };
            totals[stack.Resource] = total;
        }

        return Array.AsReadOnly(totals
            .Select(pair => new ResourceInventorySnapshot(
                pair.Key,
                pair.Value.Stored,
                pair.Value.KnownLoose,
                pair.Value.Carried))
            .ToArray());
    }

    public static SimulationEngine Create(
        WorldSeed worldSeed,
        SimulationDefinitions definitions,
        int initialGoblinCount,
        int initialFoodStock,
        int initialHunger = 0,
        int? initialHealth = null,
        int initialWoodStock = 0,
        bool scatterInitialBrushwood = false,
        SimulationDebugSettings? debugSettings = null) =>
        Create(
            worldSeed,
            definitions,
            SwampMapGenerator.Generate(
                worldSeed,
                SwampMapGenerator.DefaultDimension,
                SwampMapGenerator.DefaultDimension),
            initialGoblinCount,
            initialFoodStock,
            initialHunger,
            initialHealth,
            initialWoodStock,
            scatterInitialBrushwood,
            debugSettings);

    public static SimulationEngine Create(
        WorldSeed worldSeed,
        SimulationDefinitions definitions,
        GeneratedMap map,
        int initialGoblinCount,
        int initialFoodStock,
        int initialHunger = 0,
        int? initialHealth = null,
        int initialWoodStock = 0,
        bool scatterInitialBrushwood = false,
        SimulationDebugSettings? debugSettings = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentOutOfRangeException.ThrowIfNegative(initialGoblinCount);
        ArgumentOutOfRangeException.ThrowIfNegative(initialFoodStock);
        ArgumentOutOfRangeException.ThrowIfNegative(initialWoodStock);
        ArgumentOutOfRangeException.ThrowIfNegative(initialHunger);
        var goblinCivilization = CivilizationCatalog.Current.Get(
            CivilizationLegacyRole.PlayerGoblins);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            initialHunger,
            goblinCivilization.Needs!.MaximumHunger);
        var maximumHealth = goblinCivilization.Vitals!.MaximumHealth;
        var actorHealth = initialHealth ?? maximumHealth;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actorHealth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(actorHealth, maximumHealth);

        if (map.Seed != worldSeed)
        {
            throw new ArgumentException("Map seed must match the simulation world seed.", nameof(map));
        }

        var engine = new SimulationEngine(
            worldSeed,
            definitions,
            map,
            debugSettings ?? SimulationDebugSettings.Disabled);

        for (var index = 0; index < initialGoblinCount; index++)
        {
            var actor = engine.AllocateActor(map.GoblinSpawn, initialHunger, actorHealth);
            if (index == 0)
            {
                actor.Equipment |= PersonalEquipment.WoodenAxe;
                actor.KnownSkills |= GoblinSkill.Building;
            }
        }
        engine.EnsureTribeHasStarterPickaxe();
        engine.CreateInitialAnimals();

        if (initialFoodStock > 0)
        {
            engine.AllocateItemStack(
                ResourceKind.Food,
                initialFoodStock,
                ItemLocation.OnGround(map.GoblinSpawn));
        }

        if (initialWoodStock > 0)
        {
            engine.AllocateItemStack(
                ResourceKind.Wood,
                initialWoodStock,
                ItemLocation.OnGround(map.GoblinSpawn));
        }

        if (scatterInitialBrushwood)
        {
            engine.ScatterInitialBrushwood();
            engine.ScatterInitialStones();
        }

        engine.UpdateVisibility();

        return engine;
    }

    public static SimulationEngine Load(
        string saveJson,
        SimulationDefinitions definitions,
        SimulationDebugSettings? debugSettings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveJson);
        ArgumentNullException.ThrowIfNull(definitions);

        var save = SimulationSaveReader.ReadExact(saveJson, SaveOptions);

        ValidateSaveHeader(save, definitions);

        var worldSeed = new WorldSeed(save.WorldSeed);
        GeneratedMap map;
        try
        {
            map = SwampMapGenerator.Generate(
                new Map.Generation.LocationGenerationRequest(
                    ContentId.Parse(save.MapProfileId),
                    worldSeed,
                    save.MapWidth,
                    save.MapHeight,
                    save.MapGeneratorVersion,
                    save.MapRiverMode,
                    save.MapRoadMode));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("The save contains invalid map parameters.", exception);
        }

        if (!StringComparer.Ordinal.Equals(map.ComputeFingerprint(), save.MapFingerprint))
        {
            throw new InvalidDataException(
                "The saved map fingerprint does not match the current deterministic generator.");
        }

        var savedNegativeLevelCount = map.MaterializedNegativeLevelCount;

        var engine = new SimulationEngine(
            worldSeed,
            definitions,
            map,
            debugSettings ?? SimulationDebugSettings.Disabled)
        {
            CurrentTick = new SimulationTick(save.CurrentTick),
            _nextEntityId = save.NextEntityId,
            _nextEventSequence = save.NextEventSequence,
            _nextAnimalId = save.NextAnimalId == 0 ? 1 : save.NextAnimalId,
            _raidPhase = save.RaidPhase,
            _raidRallyPoint = new GridPosition(save.RaidRallyX, save.RaidRallyY, save.RaidRallyZ),
            _raidTarget = save.RaidTargetRadius == 0
                ? map.HumanVillage
                : new GridPosition(save.RaidTargetX, save.RaidTargetY, save.RaidTargetZ),
            _raidTargetRadius = save.RaidTargetRadius == 0
                ? DefaultRaidTargetRadius
                : save.RaidTargetRadius,
            _raidDirectives = save.RaidDirectives == RaidDirective.None
                ? DefaultRaidDirectives
                : save.RaidDirectives,
            _compostNutrients = save.CompostNutrients,
        };
        foreach (var actorId in save.RaidPartyIds)
        {
            if (!engine._raidPartyIds.Add(new EntityId(actorId)))
            {
                throw new InvalidDataException("The save contains duplicate raid-party members.");
            }
        }
        engine._raidRosterConfigured = save.RaidRosterConfigured ||
            engine._raidPartyIds.Count > 0;

        engine.World = WorldMapState.Restore(
            map,
            save.WorldVersion,
            save.PlantPatches.Select(model => new PlantPatchSnapshot(
                new GridPosition(model.X, model.Y, model.Z),
                model.Kind,
                model.Biomass,
                model.Capacity)),
            save.WorldObjects.Select(model => new WorldObjectSnapshot(
                new WorldObjectId(model.Id),
                model.Kind,
                model.Owner,
                new GridPosition(model.AnchorX, model.AnchorY, model.AnchorZ),
                model.Orientation,
                model.Parts.Select(part => new WorldObjectPartSnapshot(
                    new GridPosition(part.RelativeX, part.RelativeY, part.RelativeZ),
                    part.Channel,
                    part.Kind)),
                model.MaterialVariant)),
            save.ExcavatedCaveCells.Select(model =>
                new GridPosition(model.X, model.Y, model.Z)),
            save.ExcavatedTerrainRamps.Select(model =>
                new GridPosition(model.X, model.Y, model.Z)),
            save.ExcavatedVerticalPassages.Select(model => new VerticalPassage(
                new GridPosition(model.UpperX, model.UpperY, model.UpperZ),
                new GridPosition(model.LowerX, model.LowerY, model.LowerZ),
                model.Kind)),
            save.HarvestedCaveFlora.Select(model =>
                new GridPosition(model.X, model.Y, model.Z)));
        engine.Navigation = new NavigationPathService(engine.World);
        engine.LoadBloodStains(save.BloodStains);
        var surfaceGrime = save.SurfaceGrime.Select(model =>
            new Contamination.SurfaceGrimeSnapshot(
                new GridPosition(model.X, model.Y, model.Z),
                model.Volume,
                new SimulationTick(model.CreatedAtTick),
                new SimulationTick(model.LastChangedAtTick)))
            .ToArray();
        var validSurfaceGrime = surfaceGrime.Where(stain =>
                engine.World.IsTerrainReachable(stain.Position) &&
                engine.IsConstructedCleanableSurface(stain.Position))
            .ToArray();
        if (validSurfaceGrime.Length != surfaceGrime.Length &&
            !save.DiscardMisplacedLegacySurfaceGrime)
        {
            throw new InvalidDataException("The save contains misplaced surface grime.");
        }
        engine._surfaceGrime.Restore(validSurfaceGrime, engine.CurrentTick);
        engine.RebuildAutonomousCleaningAreas();
        engine.Visibility = WorldVisibilityState.Restore(
            map,
            save.Visibility,
            savedNegativeLevelCount);
        engine._humanVillage = HumanVillageState.Restore(
            engine.World,
            save.HumanVillage,
            definitions,
            engine.HumanVitals,
            engine.HumanPopulationNeeds,
            engine.HumanSpatialBehavior,
            engine.CurrentTick);
        engine.ValidateLoadedRaidState();
        engine.LoadResourcePriorities(save.ResourcePriorities);
        engine.LoadWorkTypePriorities(save.WorkTypePriorities);
        engine.LoadStorageZones(save.StorageZones);
        engine.LoadLogisticsNetworks(save.LogisticsNetworks);
        engine.LoadStorageAreas(save.StorageAreas);
        engine.LoadWorkDesignations(save.WorkDesignations);
        engine.LoadItemStacks(save.ItemStacks);
        engine.LoadConstructionSites(save.ConstructionSites);
        engine.LoadCraftingOrders(save.CraftingOrders);
        engine.LoadGoblinBuds(save.GoblinBuds);
        engine.LoadCorpses(save.Corpses);
        foreach (var variant in save.StolenVillageEquipment)
        {
            if (variant is < ResourceVariant.EquipmentPrimitiveSling or
                > ResourceVariant.EquipmentWoodenSpear ||
                !engine._stolenVillageEquipment.Add(variant))
            {
                throw new InvalidDataException("The save contains invalid stolen village equipment.");
            }
        }
        if (save.Animals is null)
        {
            engine.CreateInitialAnimals();
        }
        else
        {
            engine.LoadAnimals(save.Animals);
        }
        engine._undergroundFactions.Restore(save.UndergroundFactions);
        engine.ValidateLoadedWorkDesignations();
        engine.LoadActors(save.Actors);
        engine.LoadTribeNavigationBeliefs(save.TribeNavigationBeliefs);
        engine.RestoreLegacyRaidPartyIfNeeded();
        engine.ValidateLoadedRaidParty();
        engine.ValidateLoadedOwnership();
        engine.ValidateLoadedJobReservations();
        engine.ValidateNextEntityId();
        engine.LoadPendingCommands(save.PendingCommands);
        engine.LoadUndeliveredEvents(save.UndeliveredEvents);
        engine.LoadUndeliveredWorldChanges(save.UndeliveredWorldChanges);

        return engine;
    }

    public void QueueCommand(SimulationCommand command)
    {
        ValidateCommandForQueue(command);

        var key = new CommandKey(command.ExecuteAt, command.Sequence);
        if (!_pendingCommands.TryAdd(key, command))
        {
            throw new InvalidOperationException(
                $"A command with sequence {command.Sequence} is already scheduled for tick {command.ExecuteAt}.");
        }
    }

    public void ApplyCommandImmediately(SimulationCommand command)
    {
        command = command with { ExecuteAt = CurrentTick };
        ValidateCommandForQueue(command, allowCurrentTick: true);
        ExecuteValidatedCommand(command);
    }

    public void AdvanceTicks(int tickCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tickCount);

        for (var index = 0; index < tickCount; index++)
        {
            AdvanceOneTick();
        }
    }

    public SimulationSnapshot CreatePresentationSnapshot()
    {
        var startedAt = Stopwatch.GetTimestamp();
        var snapshot = CreateSnapshot(includeStateHash: false, includePlanningForecasts: false);
        _lastPresentationSnapshotStopwatchTicks = Stopwatch.GetTimestamp() - startedAt;
        _totalPresentationSnapshotStopwatchTicks = checked(
            _totalPresentationSnapshotStopwatchTicks +
            _lastPresentationSnapshotStopwatchTicks);
        _presentationSnapshotsCreated = checked(_presentationSnapshotsCreated + 1);
        return snapshot;
    }

    public SimulationSnapshot CreateSnapshot(
        bool includeStateHash = true,
        bool includePlanningForecasts = true)
    {
        var futurePublicWork = includePlanningForecasts
            ? CreateFuturePublicWorkPlans()
            : new Dictionary<EntityId, ActorPlanEntrySnapshot>();
        var currentDay = SimulationCalendar.At(CurrentTick, Definitions.Clock).AbsoluteDay;
        var actors = _actors.Values
            .Select(actor => new ActorSnapshot(
                actor.Id,
                actor.Name,
                actor.KnownSkills,
                actor.KnownTraits,
                actor.Equipment,
                new GoblinExperienceSnapshot(
                    actor.ForagingExperience,
                    actor.HaulingExperience,
                    actor.BuildingExperience),
                actor.WorkPreferences,
                actor.Position,
                actor.Hunger,
                actor.Fatigue,
                actor.Health,
                actor.BleedingTicksRemaining,
                actor.Thirst,
                actor.PersonalFood,
                actor.PersonalFoodKind,
                new PersonalFoodContentsSnapshot(actor.PersonalFoodKinds),
                actor.PersonalWater,
                actor.PersonalStoneAmmo,
                GetActorAgeDays(actor, currentDay),
                IsJuvenile(actor),
                IsElderly(actor),
                GetEffectiveMaximumHealth(actor),
                GetSenescenceProgress(actor),
                actor.CarriedStackId,
                new ActorJobSnapshot(
                    actor.JobKind,
                    actor.JobPhase,
                    actor.JobStage,
                    actor.JobTarget,
                    actor.RemainingWorkTicks,
                    actor.SourceStackId,
                    actor.DestinationZoneId,
                    actor.ReservedQuantity,
                    actor.RemainingRoute.Count,
                    actor.SuspendedJobKind,
                    actor.SuspendedJobTarget),
                CreateActorPlanSnapshot(
                    actor,
                    futurePublicWork.TryGetValue(actor.Id, out var futureWork)
                        ? futureWork
                        : null))
            {
                Sex = actor.Sex,
                PolityId = CorePolityIds.PlayerTribe,
                Loadout = CreateEquipmentLoadout(actor),
                CarriedCorpseId = actor.CarriedCorpseId,
                TacticalOrder = new ActorTacticalOrderSnapshot(
                    actor.TacticalOrderKind,
                    actor.TacticalCenter,
                    actor.TacticalRadius,
                    actor.PatrolPoints.ToArray(),
                    actor.PatrolPointIndex),
                DispatcherSuspendedUntilTick = actor.DispatcherSuspendedUntilTick,
            })
            .ToArray();
        var itemStacks = _itemStacks.Values
            .Select(stack => new ItemStackSnapshot(
                stack.Id, stack.Resource, stack.FoodKind, stack.Variant, stack.Quantity, stack.Location)
            {
                HaulPriority = stack.HaulPriority,
                FreshUntilTick = stack.FreshUntilTick,
            })
            .ToArray();
        var goblinBuds = _goblinBuds.Values
            .Select(bud => new GoblinBudSnapshot(
                bud.Id,
                bud.ParentId,
                bud.Position,
                bud.RemainingCareTicks,
                Definitions.Reproduction.TendWorkTicks)
            {
                OriginCorpseId = bud.OriginCorpseId,
                OriginImprint = bud.OriginImprint,
            })
            .ToArray();
        var animals = _animals.Values.Select(animal => animal.ToSnapshot()).ToArray();
        var corpses = _corpses.Values.Select(corpse => corpse.ToSnapshot()).ToArray();
        var storageZones = _storageZones.Values
            .Select(zone => new StorageZoneSnapshot(
                zone.Id,
                zone.Position,
                zone.AcceptedResource,
                zone.Capacity,
                GetStoredQuantity(zone.Id),
                zone.DesiredQuantity,
                zone.AssignedHaulerId,
                zone.SourceStorageZoneId,
                zone.Priority,
                zone.SlotPolicy.SlotCount,
                zone.SlotPolicy.StackCapacity,
                zone.SlotPolicy.SeparatesItemTypes ? GetUsedTypeSlots(zone.Id) : 0,
                zone.MineralFilter,
                zone.SlotPolicy.SeparatesItemTypes,
                zone.SlotPolicy.Capabilities,
                zone.LogisticsNetworkId,
                zone.StorageAreaId,
                zone.ProviderKind,
                zone.ResourceFilter))
            .ToArray();
        var storageAreas = _storageAreas.Values
            .Select(area =>
            {
                var zones = _storageZones.Values
                    .Where(zone => zone.StorageAreaId == area.Id)
                    .ToArray();
                return new StorageAreaSnapshot(
                    area.Id,
                    area.Name,
                    area.Footprint.ToArray(),
                    area.LogisticsNetworkId,
                    zones.Select(zone => zone.Id).ToArray(),
                    zones.Sum(zone => zone.Capacity),
                    zones.Sum(zone => GetStoredQuantity(zone.Id)));
            })
            .ToArray();
        var logisticsNetworks = _logisticsNetworks.Values
            .Select(network => new LogisticsNetworkSnapshot(
                network.Id,
                network.Name,
                network.Id == EntityId.None,
                network.AssignedHaulerIds.ToArray(),
                network.SourceStorageZoneIds.ToArray(),
                _storageZones.Values
                    .Where(zone => zone.LogisticsNetworkId == network.Id)
                    .Select(zone => zone.Id)
                    .ToArray()))
            .ToArray();
        var resourcePriorities = _resourcePriorities
            .Select(pair => new ResourcePrioritySnapshot(pair.Key, pair.Value))
            .ToArray();
        var workDesignations = _workDesignations.Values.ToArray();
        var constructionSites = _constructionSites.Values
            .Select(site => site.ToSnapshot())
            .ToArray();
        var craftingOrders = _craftingOrders.Values
            .Select(order => order.ToSnapshot())
            .ToArray();
        var plantPatches = World.CreatePlantSnapshot().ToArray();
        var worldObjects = World.CreateWorldObjectSnapshot().ToArray();
        var bloodStains = CreateBloodStainSnapshot();
        var surfaceGrime = CreateSurfaceGrimeSnapshot();
        var humanVillage = _humanVillage.CreateSnapshot();
        var visibility = Visibility.CreateSnapshot().ToArray();
        var resourceInventory = CreateResourceInventorySnapshot().ToArray();

        return new SimulationSnapshot(
            WorldSeed,
            CurrentTick,
            GetTotalResourceQuantity(ResourceKind.Food),
            actors,
            goblinBuds,
            CreateTribeNeedsSnapshot(),
            animals,
            _undergroundFactions.CreateSnapshot().ToArray(),
            _undergroundFactions.Relations.ToArray(),
            corpses,
            CreateVillageLootSnapshot(),
            itemStacks,
            storageZones,
            storageAreas,
            logisticsNetworks,
            resourcePriorities,
            resourceInventory,
            constructionSites,
            craftingOrders,
            workDesignations,
            plantPatches,
            worldObjects,
            bloodStains,
            surfaceGrime,
            humanVillage,
            _raidPhase,
            _raidRallyPoint,
            _raidPartyIds.ToArray(),
            _raidRosterConfigured,
            _raidTarget,
            _raidTargetRadius,
            _raidDirectives,
            visibility,
            Map.CellCount,
            Map.MaterializedNegativeLevelCount,
            World.Version,
            Map.GeneratorVersion,
            Map.ProfileId,
            Map.RiverMode,
            Map.RoadMode,
            Map.ComputeFingerprint(),
            includeStateHash ? ComputeStateHash() : string.Empty)
        {
            PlayerPolityId = CorePolityIds.PlayerTribe,
        };
    }

    public StorageDeliveryDiagnostic InspectStorageDelivery(EntityId zoneId)
    {
        if (!_storageZones.TryGetValue(zoneId, out var zone))
        {
            throw new ArgumentException($"Storage zone {zoneId} does not exist.", nameof(zoneId));
        }

        var stored = GetStoredQuantity(zone.Id);
        var requested = Math.Max(0, zone.DesiredQuantity - stored);
        if (zone.DesiredQuantity == 0)
        {
            return CreateDiagnostic(StorageDeliveryState.Disabled, requested);
        }
        if (requested == 0)
        {
            return CreateDiagnostic(StorageDeliveryState.Satisfied, requested);
        }

        var destinationReservations = CreateHaulReservations(sourceReservations: false);
        var inTransit = destinationReservations.GetValueOrDefault(zone.Id);
        if (inTransit > 0)
        {
            return CreateDiagnostic(
                StorageDeliveryState.InTransit,
                requested,
                inTransitQuantity: inTransit);
        }

        if (zone.AcceptedResource == ResourceKind.Water &&
            zone.SourceStorageZoneId == EntityId.None)
        {
            var naturalWaterHaulers = _actors.Values
                .Where(actor => IsHaulerAllowedForZone(actor, zone))
                .ToArray();
            if (naturalWaterHaulers.Length == 0)
            {
                return CreateDiagnostic(StorageDeliveryState.NoAvailableHauler, requested);
            }

            var bucketHaulers = naturalWaterHaulers
                .Where(actor => actor.Equipment.HasFlag(PersonalEquipment.WoodenBucket))
                .ToArray();
            if (bucketHaulers.Length == 0)
            {
                return CreateDiagnostic(StorageDeliveryState.NoAvailableTool, requested);
            }

            var hasReachableNaturalWaterPlan = bucketHaulers.Any(actor =>
            {
                var route = FindNearestShallowWaterPath(actor.Position);
                var source = route is null
                    ? (GridPosition?)null
                    : route.Count == 0 ? actor.Position : route[^1];
                return source is { } position &&
                    FindActorPathFrom(actor, position, zone.Position) is not null;
            });
            if (!hasReachableNaturalWaterPlan)
            {
                return CreateDiagnostic(StorageDeliveryState.NoReachableSource, requested);
            }

            if (zone.AssignedHaulerId != EntityId.None &&
                _actors[zone.AssignedHaulerId].JobKind != ActorJobKind.None)
            {
                return CreateDiagnostic(StorageDeliveryState.AssignedHaulerBusy, requested);
            }

            return CreateDiagnostic(
                StorageDeliveryState.WaitingForHauler,
                requested,
                availableQuantity: requested,
                matchingSourceCount: 1);
        }

        var sourceReservations = CreateHaulReservations(sourceReservations: true);
        var matchingSources = _itemStacks.Values
            .Where(source =>
                source.Location.Kind is ItemLocationKind.Ground or ItemLocationKind.StorageZone &&
                source.Location.OwnerId != zone.Id &&
                Visibility.Get(source.Location.Position) != CellVisibility.Unknown &&
                ZoneAccepts(zone, source) &&
                IsSourceAllowedForZone(source, zone))
            .ToArray();
        if (matchingSources.Length == 0)
        {
            return CreateDiagnostic(StorageDeliveryState.NoAllowedSource, requested);
        }

        var availableSources = matchingSources
            .Select(source => new
            {
                Source = source,
                Available = GetAvailableSourceQuantity(source, sourceReservations),
            })
            .Where(candidate => candidate.Available > 0)
            .ToArray();
        var availableQuantity = availableSources.Sum(candidate => candidate.Available);
        if (availableSources.Length == 0)
        {
            return CreateDiagnostic(
                StorageDeliveryState.NoSurplus,
                requested,
                matchingSourceCount: matchingSources.Length);
        }

        var storableSources = availableSources
            .Where(candidate => CanStoreStack(zone, candidate.Source, 1))
            .ToArray();
        if (storableSources.Length == 0)
        {
            return CreateDiagnostic(
                StorageDeliveryState.DestinationBlocked,
                requested,
                availableQuantity: availableQuantity,
                matchingSourceCount: matchingSources.Length);
        }

        var allowedHaulers = _actors.Values
            .Where(actor => IsHaulerAllowedForZone(actor, zone))
            .ToArray();
        if (allowedHaulers.Length == 0)
        {
            return CreateDiagnostic(
                StorageDeliveryState.NoAvailableHauler,
                requested,
                availableQuantity: availableQuantity,
                matchingSourceCount: matchingSources.Length);
        }
        if (zone.AcceptedResource == ResourceKind.Water)
        {
            allowedHaulers = allowedHaulers
                .Where(actor => actor.Equipment.HasFlag(PersonalEquipment.WoodenBucket))
                .ToArray();
            if (allowedHaulers.Length == 0)
            {
                return CreateDiagnostic(
                    StorageDeliveryState.NoAvailableTool,
                    requested,
                    availableQuantity: availableQuantity,
                    matchingSourceCount: matchingSources.Length);
            }
        }

        var hasReachablePlan = storableSources.Any(candidate =>
            allowedHaulers.Any(actor =>
                FindActorPath(actor, candidate.Source.Location.Position) is not null &&
                FindActorPathFrom(
                    actor,
                    candidate.Source.Location.Position,
                    zone.Position) is not null));
        if (!hasReachablePlan)
        {
            return CreateDiagnostic(
                StorageDeliveryState.NoReachableSource,
                requested,
                availableQuantity: availableQuantity,
                matchingSourceCount: matchingSources.Length);
        }

        if (zone.AssignedHaulerId != EntityId.None &&
            _actors[zone.AssignedHaulerId].JobKind != ActorJobKind.None)
        {
            return CreateDiagnostic(
                StorageDeliveryState.AssignedHaulerBusy,
                requested,
                availableQuantity: availableQuantity,
                matchingSourceCount: matchingSources.Length);
        }

        return CreateDiagnostic(
            StorageDeliveryState.WaitingForHauler,
            requested,
            availableQuantity: availableQuantity,
            matchingSourceCount: matchingSources.Length);

        StorageDeliveryDiagnostic CreateDiagnostic(
            StorageDeliveryState state,
            int requestedQuantity,
            int inTransitQuantity = 0,
            int availableQuantity = 0,
            int matchingSourceCount = 0) =>
            new(
                zoneId,
                state,
                requestedQuantity,
                inTransitQuantity,
                availableQuantity,
                matchingSourceCount);
    }

    public ConstructionReadinessDiagnostic InspectConstructionReadiness(
        EntityId siteId,
        bool evaluateReachability = true)
    {
        if (!_constructionSites.TryGetValue(siteId, out var site))
        {
            throw new ArgumentException($"Construction site {siteId} does not exist.", nameof(siteId));
        }

        var constructionReservations = CreateConstructionReservations();
        var inTransit = constructionReservations.GetValueOrDefault(site.Id);
        if (HasGroundStackInConstructionFootprint(site))
        {
            return CreateDiagnostic(ConstructionReadinessState.AwaitingSiteClearance);
        }

        if (site.MissingQuantity > 0)
        {
            if (inTransit > 0)
            {
                return CreateDiagnostic(
                    ConstructionReadinessState.MaterialsInTransit,
                    inTransitQuantity: inTransit);
            }

            var sourceReservations = CreateHaulReservations(sourceReservations: true);
            var matchingSources = _itemStacks.Values
                .Where(stack =>
                    StackMatchesConstructionMaterial(site, stack) &&
                    stack.Location.Kind is ItemLocationKind.Ground or ItemLocationKind.StorageZone)
                .Select(stack => new
                {
                    Stack = stack,
                    Available = GetAvailableSourceQuantity(stack, sourceReservations),
                })
                .ToArray();
            var availableSources = matchingSources
                .Where(candidate => candidate.Available > 0)
                .OrderBy(candidate => ManhattanDistance(candidate.Stack.Location.Position, site.Anchor))
                .ThenBy(candidate => candidate.Stack.Id)
                .ToArray();
            var availableQuantity = availableSources.Sum(candidate => candidate.Available);
            if (availableSources.Length == 0)
            {
                return CreateDiagnostic(
                    ConstructionReadinessState.NoAvailableMaterials,
                    availableQuantity: availableQuantity,
                    matchingSourceCount: matchingSources.Length);
            }

            var suppliers = _actors.Values.ToArray();
            if (suppliers.Length == 0)
            {
                return CreateDiagnostic(
                    ConstructionReadinessState.NoAvailableSupplier,
                    availableQuantity: availableQuantity,
                    matchingSourceCount: matchingSources.Length);
            }

            if (!evaluateReachability)
            {
                return CreateDiagnostic(
                    ConstructionReadinessState.WaitingForSupplier,
                    availableQuantity: availableQuantity,
                    matchingSourceCount: matchingSources.Length);
            }

            var hasReachablePlan = availableSources.Any(source =>
                suppliers.Any(actor =>
                    FindActorPath(actor, source.Stack.Location.Position) is not null &&
                    FindConstructionAccessPath(
                        source.Stack.Location.Position,
                        site,
                        actor) is not null));
            return CreateDiagnostic(
                hasReachablePlan
                    ? ConstructionReadinessState.WaitingForSupplier
                    : ConstructionReadinessState.NoReachableMaterialSource,
                availableQuantity: availableQuantity,
                matchingSourceCount: matchingSources.Length);
        }

        var assignedBuilder = _actors.Values.Any(actor =>
            actor.JobKind == ActorJobKind.BuildConstruction &&
            actor.DestinationZoneId == site.Id);
        if (assignedBuilder)
        {
            return CreateDiagnostic(ConstructionReadinessState.Building);
        }

        var capableBuilders = _actors.Values
            .Where(actor => CanActorBuild(actor, site))
            .ToArray();
        if (capableBuilders.Length == 0)
        {
            return CreateDiagnostic(ConstructionReadinessState.NoCapableBuilder);
        }

        if (!evaluateReachability)
        {
            return CreateDiagnostic(ConstructionReadinessState.WaitingForBuilder);
        }

        var hasReachableBuilder = capableBuilders.Any(actor =>
            FindConstructionAccessPath(actor.Position, site, actor) is not null);
        return CreateDiagnostic(hasReachableBuilder
            ? ConstructionReadinessState.WaitingForBuilder
            : ConstructionReadinessState.NoReachableBuilder);

        ConstructionReadinessDiagnostic CreateDiagnostic(
            ConstructionReadinessState state,
            int inTransitQuantity = 0,
            int availableQuantity = 0,
            int matchingSourceCount = 0) =>
            new(
                siteId,
                state,
                site.MissingQuantity,
                inTransitQuantity,
                availableQuantity,
                matchingSourceCount,
                _actors.Values.Count(actor => CanActorBuild(actor, site)));
    }

    private int GetAvailableSourceQuantity(
        ItemStackState source,
        IReadOnlyDictionary<EntityId, int> sourceReservations)
    {
        var protectedAtSource = source.Location.Kind == ItemLocationKind.StorageZone &&
            _storageZones.TryGetValue(source.Location.OwnerId, out var sourceZone)
                ? Math.Max(0, sourceZone.DesiredQuantity -
                    (GetStoredQuantity(sourceZone.Id) - source.Quantity))
                : 0;
        return Math.Max(
            0,
            source.Quantity - protectedAtSource - sourceReservations.GetValueOrDefault(source.Id));
    }

    public IReadOnlyList<SimulationEvent> DrainEvents()
    {
        if (_undeliveredEvents.Count == 0)
        {
            return Array.Empty<SimulationEvent>();
        }

        var result = _undeliveredEvents.ToArray();
        _undeliveredEvents.Clear();
        return result;
    }

    public IReadOnlyList<WorldChangeEvent> DrainWorldChanges()
    {
        if (_undeliveredWorldChanges.Count == 0)
        {
            return Array.Empty<WorldChangeEvent>();
        }

        var result = _undeliveredWorldChanges.ToArray();
        _undeliveredWorldChanges.Clear();
        return result;
    }

    public SimulationMetrics GetMetrics() => new(
        TicksExecuted: _ticksExecuted,
        CommandsExecuted: _commandsExecuted,
        EventsPublished: _eventsPublished,
        ActorUpdates: _actorUpdates,
        ActiveActors: _actors.Count,
        ItemStacks: _itemStacks.Count,
        StorageZones: _storageZones.Count,
        PlantPatches: World.PlantPatchCount,
        WorldObjects: World.WorldObjectCount,
        PendingCommands: _pendingCommands.Count,
        UndeliveredEvents: _undeliveredEvents.Count,
        UndeliveredWorldChanges: _undeliveredWorldChanges.Count,
        Navigation: Navigation.GetMetrics(),
        PresentationSnapshots: new PresentationSnapshotMetrics(
            _presentationSnapshotsCreated,
            StopwatchTicksToTimeSpan(_lastPresentationSnapshotStopwatchTicks),
            StopwatchTicksToTimeSpan(_totalPresentationSnapshotStopwatchTicks)),
        LastTickDuration: StopwatchTicksToTimeSpan(_lastTickStopwatchTicks),
        TotalTickDuration: StopwatchTicksToTimeSpan(_totalTickStopwatchTicks),
        LastTickBreakdown: new SimulationTickBreakdown(
            World: StopwatchTicksToTimeSpan(_lastTickStageStopwatchTicks[0]),
            Commands: StopwatchTicksToTimeSpan(_lastTickStageStopwatchTicks[1]),
            ActorJobs: StopwatchTicksToTimeSpan(_lastTickStageStopwatchTicks[2]),
            Animals: StopwatchTicksToTimeSpan(_lastTickStageStopwatchTicks[3]),
            Doors: StopwatchTicksToTimeSpan(_lastTickStageStopwatchTicks[4]),
            HumanVillage: StopwatchTicksToTimeSpan(_lastTickStageStopwatchTicks[5]),
            Combat: StopwatchTicksToTimeSpan(_lastTickStageStopwatchTicks[6]),
            Actors: StopwatchTicksToTimeSpan(_lastTickStageStopwatchTicks[7]),
            Raid: StopwatchTicksToTimeSpan(_lastTickStageStopwatchTicks[8]),
            Visibility: StopwatchTicksToTimeSpan(_lastTickStageStopwatchTicks[9])));

    public string Save()
    {
        var save = new SimulationSaveModel
        {
            FormatVersion = SaveFormatVersion,
            DefinitionsId = Definitions.Id,
            ClimateProfileId = Definitions.Clock.Climate.Id,
            PlayerPolityId = CorePolityIds.PlayerTribe.Value,
            WorldSeed = WorldSeed.Value,
            MapGeneratorVersion = Map.GeneratorVersion,
            MapProfileId = Map.ProfileId.Value,
            MapRiverMode = Map.RiverMode,
            MapRoadMode = Map.RoadMode,
            MapWidth = Map.Width,
            MapHeight = Map.Height,
            MapFingerprint = Map.ComputeFingerprint(),
            CurrentTick = CurrentTick.Value,
            CompostNutrients = _compostNutrients,
            NextEntityId = _nextEntityId,
            NextEventSequence = _nextEventSequence,
            WorldVersion = World.Version,
            GoblinBuds = _goblinBuds.Values.Select(bud => new GoblinBudSaveModel
            {
                Id = bud.Id.Value,
                ParentId = bud.ParentId.Value,
                OriginCorpseId = bud.OriginCorpseId.Value,
                OriginSkills = bud.OriginImprint.KnownSkills,
                OriginTraits = bud.OriginImprint.KnownTraits,
                OriginForagingExperience = bud.OriginImprint.Experience.Foraging,
                OriginHaulingExperience = bud.OriginImprint.Experience.Hauling,
                OriginBuildingExperience = bud.OriginImprint.Experience.Building,
                OriginForagingPreference = bud.OriginImprint.WorkPreferences.Foraging,
                OriginHaulingPreference = bud.OriginImprint.WorkPreferences.Hauling,
                OriginBuildingPreference = bud.OriginImprint.WorkPreferences.Building,
                X = bud.Position.X,
                Y = bud.Position.Y,
                Z = bud.Position.Z,
                RemainingCareTicks = bud.RemainingCareTicks,
            }).ToList(),
            NextAnimalId = _nextAnimalId,
            Animals = _animals.Values.Select(animal => animal.ToSaveModel()).ToList(),
            UndergroundFactions = _undergroundFactions.CreateSaveModels().ToList(),
            Corpses = _corpses.Values.Select(corpse => new CorpseSaveModel
            {
                Id = corpse.Id.Value,
                Kind = corpse.Kind,
                Name = corpse.Name,
                X = corpse.Position.X,
                Y = corpse.Position.Y,
                Z = corpse.Position.Z,
                CreatedAtTick = corpse.CreatedAt.Value,
                ContainedWater = corpse.ContainedWater,
                EdiblePortions = corpse.EdiblePortions,
                Directives = corpse.Directives,
                InheritableSkills = corpse.InheritanceImprint.KnownSkills,
                InheritableTraits = corpse.InheritanceImprint.KnownTraits,
                InheritableForagingExperience = corpse.InheritanceImprint.Experience.Foraging,
                InheritableHaulingExperience = corpse.InheritanceImprint.Experience.Hauling,
                InheritableBuildingExperience = corpse.InheritanceImprint.Experience.Building,
                InheritableForagingPreference = corpse.InheritanceImprint.WorkPreferences.Foraging,
                InheritableHaulingPreference = corpse.InheritanceImprint.WorkPreferences.Hauling,
                InheritableBuildingPreference = corpse.InheritanceImprint.WorkPreferences.Building,
                Contents = corpse.Contents.Select(item => new CorpseItemSaveModel
                {
                    Resource = item.Resource,
                    FoodKind = item.FoodKind,
                    Variant = item.Variant,
                    Quantity = item.Quantity,
                    UnitWeight = item.UnitWeight,
                }).ToList(),
            }).ToList(),
            StolenVillageEquipment = _stolenVillageEquipment.ToList(),
            RaidPhase = _raidPhase,
            RaidRallyX = _raidRallyPoint.X,
            RaidRallyY = _raidRallyPoint.Y,
            RaidRallyZ = _raidRallyPoint.Z,
            RaidPartyIds = _raidPartyIds.Select(id => id.Value).ToList(),
            RaidRosterConfigured = _raidRosterConfigured,
            RaidTargetX = _raidTarget.X,
            RaidTargetY = _raidTarget.Y,
            RaidTargetZ = _raidTarget.Z,
            RaidTargetRadius = _raidTargetRadius,
            RaidDirectives = _raidDirectives,
            PlantPatches = World.CreatePlantSnapshot().Select(patch => new PlantPatchSaveModel
            {
                X = patch.Position.X,
                Y = patch.Position.Y,
                Z = patch.Position.Z,
                Kind = patch.Kind,
                Biomass = patch.Biomass,
                Capacity = patch.Capacity,
            }).ToList(),
            WorldObjects = World.CreateWorldObjectSnapshot().Select(worldObject =>
                new WorldObjectSaveModel
                {
                    Id = worldObject.Id.Value,
                    Kind = worldObject.Kind,
                    Owner = worldObject.Owner,
                    AnchorX = worldObject.Anchor.X,
                    AnchorY = worldObject.Anchor.Y,
                    AnchorZ = worldObject.Anchor.Z,
                    Orientation = worldObject.Orientation,
                    MaterialVariant = worldObject.MaterialVariant,
                    Parts = worldObject.Parts.Select(part => new WorldObjectPartSaveModel
                    {
                        RelativeX = part.RelativePosition.X,
                        RelativeY = part.RelativePosition.Y,
                        RelativeZ = part.RelativePosition.Z,
                        Channel = part.Channel,
                        Kind = part.Kind,
                    }).ToList(),
                }).ToList(),
            BloodStains = _bloodStains.Values.Select(stain => new BloodStainSaveModel
            {
                X = stain.Position.X,
                Y = stain.Position.Y,
                Z = stain.Position.Z,
                Volume = stain.Volume,
                Surface = stain.Surface,
                CreatedAtTick = stain.CreatedAt.Value,
                LastChangedAtTick = stain.LastChangedAt.Value,
            }).ToList(),
            SurfaceGrime = CreateSurfaceGrimeSnapshot().Select(stain =>
                new SurfaceGrimeSaveModel
                {
                    X = stain.Position.X,
                    Y = stain.Position.Y,
                    Z = stain.Position.Z,
                    Volume = stain.Volume,
                    CreatedAtTick = stain.CreatedAt.Value,
                    LastChangedAtTick = stain.LastChangedAt.Value,
                }).ToList(),
            ExcavatedCaveCells = World.ExcavatedCaveCells
                .OrderBy(position => position.Z)
                .ThenBy(position => position.Y)
                .ThenBy(position => position.X)
                .Select(position => new GridPositionSaveModel
                {
                    X = position.X,
                    Y = position.Y,
                    Z = position.Z,
                }).ToList(),
            ExcavatedTerrainRamps = World.ExcavatedTerrainRamps
                .OrderBy(position => position.Z)
                .ThenBy(position => position.Y)
                .ThenBy(position => position.X)
                .Select(position => new GridPositionSaveModel
                {
                    X = position.X,
                    Y = position.Y,
                    Z = position.Z,
                }).ToList(),
            ExcavatedVerticalPassages = World.ExcavatedVerticalPassages
                .OrderBy(passage => passage.Upper.Z)
                .ThenBy(passage => passage.Upper.Y)
                .ThenBy(passage => passage.Upper.X)
                .Select(passage => new VerticalPassageSaveModel
                {
                    UpperX = passage.Upper.X,
                    UpperY = passage.Upper.Y,
                    UpperZ = passage.Upper.Z,
                    LowerX = passage.Lower.X,
                    LowerY = passage.Lower.Y,
                    LowerZ = passage.Lower.Z,
                    Kind = passage.Kind,
                }).ToList(),
            HarvestedCaveFlora = World.HarvestedCaveFlora
                .OrderBy(position => position.Z)
                .ThenBy(position => position.Y)
                .ThenBy(position => position.X)
                .Select(position => new GridPositionSaveModel
                {
                    X = position.X,
                    Y = position.Y,
                    Z = position.Z,
                }).ToList(),
            HumanVillage = _humanVillage.CreateSaveModel(),
            Visibility = Visibility.CreateSnapshot().ToList(),
            Actors = _actors.Values.Select(ToSaveModel).ToList(),
            TribeNavigationBeliefs = _tribeNavigationKnowledge.CreateSnapshot()
                .Select(ToSaveModel)
                .ToList(),
            ItemStacks = _itemStacks.Values.Select(ToSaveModel).ToList(),
            StorageZones = _storageZones.Values.Select(ToSaveModel).ToList(),
            StorageAreas = _storageAreas.Values.Select(ToSaveModel).ToList(),
            LogisticsNetworks = _logisticsNetworks.Values.Select(ToSaveModel).ToList(),
            ResourcePriorities = _resourcePriorities.Select(pair => new ResourcePrioritySaveModel
            {
                Resource = pair.Key,
                Priority = pair.Value,
            }).ToList(),
            WorkTypePriorities = WorkTypePriorityCatalog.All.Select(definition =>
                new WorkTypePrioritySaveModel
                {
                    Id = definition.Id,
                    Priority = GetWorkTypePriority(definition.Id),
                }).ToList(),
            ConstructionSites = _constructionSites.Values.Select(site =>
                new ConstructionSiteSaveModel
                {
                    Id = site.Id.Value,
                    Kind = site.Kind,
                    AnchorX = site.Anchor.X,
                    AnchorY = site.Anchor.Y,
                    AnchorZ = site.Anchor.Z,
                    EndX = site.End.X,
                    EndY = site.End.Y,
                    EndZ = site.End.Z,
                    RequiredResource = site.RequiredResource,
                    RequiredVariant = site.RequiredVariant,
                    RequiredWood = site.RequiredQuantity,
                    DeliveredWood = site.DeliveredQuantity,
                    DeliveredVariant = site.DeliveredVariant,
                    RemainingWorkTicks = site.RemainingWorkTicks,
                    TotalWorkTicks = site.TotalWorkTicks,
                    RequiredSkills = site.Capabilities.RequiredSkills,
                    MinimumBuildingLevel = site.Capabilities.MinimumBuildingLevel,
                    RequiredEquipment = site.Capabilities.RequiredEquipment,
                    Priority = site.Priority,
                    OrderId = site.OrderId.Value,
                    SequenceIndex = site.SequenceIndex,
                }).ToList(),
            CraftingOrders = _craftingOrders.Values.Select(order =>
                new CraftingOrderSaveModel
                {
                    Id = order.Id.Value,
                    Recipe = order.Recipe,
                    WorkshopX = order.Workshop.X,
                    WorkshopY = order.Workshop.Y,
                    WorkshopZ = order.Workshop.Z,
                    DeliveredMaterials = order.DeliveredMaterials.Select(material =>
                        new CraftingDeliveredMaterialSaveModel
                        {
                            Resource = material.Resource,
                            FoodKind = material.FoodKind,
                            Variant = material.Variant,
                            Quantity = material.Quantity,
                            FreshUntilTick = material.FreshUntilTick,
                        }).ToList(),
                    RemainingWorkTicks = order.RemainingWorkTicks,
                    IsRepeating = order.IsRepeating,
                    IsAutomatic = order.IsAutomatic,
                }).ToList(),
            WorkDesignations = _workDesignations.Values.Select(designation =>
                new WorkDesignationSaveModel
                {
                    Id = designation.Id.Value,
                    Kind = designation.Kind,
                    TargetX = designation.Target.X,
                    TargetY = designation.Target.Y,
                    TargetZ = designation.Target.Z,
                    HasRampDestination = designation.RampDestination is not null,
                    RampDestinationX = designation.RampDestination?.X ?? 0,
                    RampDestinationY = designation.RampDestination?.Y ?? 0,
                    RampDestinationZ = designation.RampDestination?.Z ?? 0,
                    TargetEntityId = designation.TargetEntityId.Value,
                    OrderId = designation.OrderId.Value,
                    Priority = designation.Priority,
                    IsSuspended = designation.IsSuspended,
                }).ToList(),
            PendingCommands = _pendingCommands.Values.Select(ToSaveModel).ToList(),
            UndeliveredEvents = _undeliveredEvents.Select(ToSaveModel).ToList(),
            UndeliveredWorldChanges = _undeliveredWorldChanges.Select(change =>
                new WorldChangeSaveModel
                {
                    Version = change.Version,
                    Tick = change.Tick.Value,
                    Kind = change.Kind,
                    X = change.Position.X,
                    Y = change.Position.Y,
                    Z = change.Position.Z,
                    Amount = change.Amount,
                }).ToList(),
        };

        return JsonSerializer.Serialize(save, SaveOptions);
    }

    public string ComputeStateHash()
    {
        var canonical = new StringBuilder(512);
        Append(canonical, SaveFormatVersion);
        Append(canonical, Definitions.Id);
        Append(canonical, Definitions.Clock.Climate.Id);
        Append(canonical, CorePolityIds.PlayerTribe.Value);
        Append(canonical, WorldSeed.Value);
        Append(canonical, Map.GeneratorVersion);
        Append(canonical, Map.ProfileId.Value);
        Append(canonical, (int)Map.RiverMode);
        Append(canonical, (int)Map.RoadMode);
        Append(canonical, Map.ComputeFingerprint());
        Append(canonical, World.Version);
        Append(canonical, (int)_raidPhase);
        Append(canonical, _raidRallyPoint);
        Append(canonical, _raidTarget);
        Append(canonical, _raidTargetRadius);
        Append(canonical, (int)_raidDirectives);
        Append(canonical, _raidRosterConfigured ? 1 : 0);
        Append(canonical, _raidPartyIds.Count);
        foreach (var actorId in _raidPartyIds)
        {
            Append(canonical, actorId.Value);
        }
        AppendNavigationKnowledge(canonical, _tribeNavigationKnowledge);
        Append(canonical, _nextAnimalId);
        Append(canonical, _animals.Count);
        foreach (var animal in _animals.Values)
        {
            Append(canonical, animal.Id);
            Append(canonical, (int)animal.Kind);
            Append(canonical, (int)animal.Sex);
            Append(canonical, animal.Position);
            Append(canonical, (int)animal.Activity);
            Append(canonical, animal.Health);
            Append(canonical, animal.Hunger);
            Append(canonical, animal.Fatigue);
            Append(canonical, animal.CarriedGrime);
            Append(canonical, animal.AgeTicks);
        }
        var undergroundFactions = _undergroundFactions.CreateSnapshot();
        Append(canonical, undergroundFactions.Count);
        foreach (var faction in undergroundFactions)
        {
            Append(canonical, faction.Id);
            Append(canonical, faction.PolityId.Value);
            Append(canonical, (int)faction.Kind);
            Append(canonical, faction.BandIndex);
            Append(canonical, faction.TopLevel);
            Append(canonical, faction.BottomLevel);
            Append(canonical, faction.FortressLevel);
            Append(canonical, faction.IsActive ? 1 : 0);
            Append(canonical, faction.Population);
            Append(canonical, faction.Fighters);
            Append(canonical, faction.Provisions);
            Append(canonical, faction.OreStock);
            Append(canonical, faction.Fortification);
            Append(canonical, (int)faction.Directive);
            Append(canonical, faction.TargetFactionId);
            Append(canonical, faction.LastUpdatedDay);
        }
        Append(canonical, _goblinBuds.Count);
        foreach (var bud in _goblinBuds.Values)
        {
            Append(canonical, bud.Id.Value);
            Append(canonical, bud.ParentId.Value);
            Append(canonical, bud.OriginCorpseId.Value);
            Append(canonical, (int)bud.OriginImprint.KnownSkills);
            Append(canonical, (int)bud.OriginImprint.KnownTraits);
            Append(canonical, bud.OriginImprint.Experience.Foraging);
            Append(canonical, bud.OriginImprint.Experience.Hauling);
            Append(canonical, bud.OriginImprint.Experience.Building);
            Append(canonical, bud.OriginImprint.WorkPreferences.Foraging);
            Append(canonical, bud.OriginImprint.WorkPreferences.Hauling);
            Append(canonical, bud.OriginImprint.WorkPreferences.Building);
            Append(canonical, bud.Position);
            Append(canonical, bud.RemainingCareTicks);
        }
        Append(canonical, _corpses.Count);
        foreach (var corpse in _corpses.Values)
        {
            Append(canonical, corpse.Id.Value);
            Append(canonical, (int)corpse.Kind);
            Append(canonical, corpse.Name);
            Append(canonical, corpse.Position);
            Append(canonical, corpse.CreatedAt.Value);
            Append(canonical, corpse.ContainedWater);
            Append(canonical, corpse.EdiblePortions);
            Append(canonical, (int)corpse.Directives);
            Append(canonical, (int)corpse.InheritanceImprint.KnownSkills);
            Append(canonical, (int)corpse.InheritanceImprint.KnownTraits);
            Append(canonical, corpse.InheritanceImprint.Experience.Foraging);
            Append(canonical, corpse.InheritanceImprint.Experience.Hauling);
            Append(canonical, corpse.InheritanceImprint.Experience.Building);
            Append(canonical, corpse.InheritanceImprint.WorkPreferences.Foraging);
            Append(canonical, corpse.InheritanceImprint.WorkPreferences.Hauling);
            Append(canonical, corpse.InheritanceImprint.WorkPreferences.Building);
            Append(canonical, corpse.Contents.Count);
            foreach (var item in corpse.Contents)
            {
                Append(canonical, (int)item.Resource);
                Append(canonical, (int)item.FoodKind);
                Append(canonical, (int)item.Variant);
                Append(canonical, item.Quantity);
                Append(canonical, item.UnitWeight);
            }
        }
        Append(canonical, _stolenVillageEquipment.Count);
        foreach (var variant in _stolenVillageEquipment)
        {
            Append(canonical, (int)variant);
        }
        var plantPatches = World.CreatePlantSnapshot();
        Append(canonical, plantPatches.Count);
        foreach (var patch in plantPatches)
        {
            Append(canonical, patch.Position);
            Append(canonical, (int)patch.Kind);
            Append(canonical, patch.Biomass);
            Append(canonical, patch.Capacity);
        }

        var worldObjects = World.CreateWorldObjectSnapshot();
        Append(canonical, worldObjects.Count);
        foreach (var worldObject in worldObjects)
        {
            Append(canonical, worldObject.Id.Value);
            Append(canonical, (int)worldObject.Kind);
            Append(canonical, (int)worldObject.Owner);
            Append(canonical, worldObject.Anchor);
            Append(canonical, (int)worldObject.Orientation);
            Append(canonical, (int)worldObject.MaterialVariant);
            Append(canonical, worldObject.Parts.Count);
            foreach (var part in worldObject.Parts)
            {
                Append(canonical, part.RelativePosition);
                Append(canonical, (int)part.Channel);
                Append(canonical, (int)part.Kind);
            }
        }

        Append(canonical, _bloodStains.Count);
        foreach (var stain in _bloodStains.Values)
        {
            Append(canonical, stain.Position);
            Append(canonical, stain.Volume);
            Append(canonical, (int)stain.Surface);
            Append(canonical, stain.CreatedAt.Value);
            Append(canonical, stain.LastChangedAt.Value);
        }

        var surfaceGrime = CreateSurfaceGrimeSnapshot();
        Append(canonical, surfaceGrime.Length);
        foreach (var stain in surfaceGrime)
        {
            Append(canonical, stain.Position);
            Append(canonical, stain.Volume);
            Append(canonical, stain.CreatedAt.Value);
            Append(canonical, stain.LastChangedAt.Value);
        }

        var excavatedCaveCells = World.ExcavatedCaveCells
            .OrderBy(position => position.Z)
            .ThenBy(position => position.Y)
            .ThenBy(position => position.X)
            .ToArray();
        Append(canonical, excavatedCaveCells.Length);
        foreach (var position in excavatedCaveCells)
        {
            Append(canonical, position);
        }

        var excavatedTerrainRamps = World.ExcavatedTerrainRamps
            .OrderBy(position => position.Z)
            .ThenBy(position => position.Y)
            .ThenBy(position => position.X)
            .ToArray();
        Append(canonical, excavatedTerrainRamps.Length);
        foreach (var position in excavatedTerrainRamps)
        {
            Append(canonical, position);
        }

        var excavatedPassages = World.ExcavatedVerticalPassages
            .OrderBy(passage => passage.Upper.Z)
            .ThenBy(passage => passage.Upper.Y)
            .ThenBy(passage => passage.Upper.X)
            .ToArray();
        Append(canonical, excavatedPassages.Length);
        foreach (var passage in excavatedPassages)
        {
            Append(canonical, passage.Upper);
            Append(canonical, passage.Lower);
            Append(canonical, (int)passage.Kind);
        }

        var harvestedCaveFlora = World.HarvestedCaveFlora
            .OrderBy(position => position.Z)
            .ThenBy(position => position.Y)
            .ThenBy(position => position.X)
            .ToArray();
        Append(canonical, harvestedCaveFlora.Length);
        foreach (var position in harvestedCaveFlora)
        {
            Append(canonical, position);
        }

        var humanVillage = _humanVillage.CreateSnapshot();
        Append(canonical, humanVillage.PolityId.Value);
        Append(canonical, humanVillage.Anchor);
        Append(canonical, humanVillage.Population);
        Append(canonical, humanVillage.FoodStock);
        Append(canonical, humanVillage.GrainStock);
        Append(canonical, humanVillage.WoodStock);
        Append(canonical, humanVillage.GoodsStock);
        Append(canonical, humanVillage.WaterStock);
        Append(canonical, humanVillage.PlannedFieldCount);
        Append(canonical, humanVillage.StorehouseCount);
        Append(canonical, humanVillage.FoodCapacity);
        Append(canonical, humanVillage.GoblinAttackOrdered ? 1 : 0);
        Append(canonical, humanVillage.Hostility);
        Append(canonical, humanVillage.LastIntruderSeenTick);
        Append(canonical, humanVillage.GuardHitPoints);
        Append(canonical, humanVillage.MaximumGuardHitPoints);
        Append(canonical, humanVillage.TreeFellingTarget is null ? 0 : 1);
        if (humanVillage.TreeFellingTarget is { } treeFellingTarget)
        {
            Append(canonical, treeFellingTarget);
        }
        Append(canonical, humanVillage.TreeFellingProgress);
        Append(canonical, humanVillage.GoodsWorkProgress);
        Append(canonical, humanVillage.StorehouseSite is null ? 0 : 1);
        if (humanVillage.StorehouseSite is { } storehouseSite)
        {
            Append(canonical, storehouseSite);
        }
        Append(canonical, humanVillage.StorehouseWorkProgress);
        Append(canonical, humanVillage.Cohorts.Count);
        foreach (var cohort in humanVillage.Cohorts)
        {
            Append(canonical, cohort.Id);
            Append(canonical, (int)cohort.Role);
            Append(canonical, cohort.Population);
            Append(canonical, cohort.Position);
            Append(canonical, (int)cohort.Task);
            Append(canonical, cohort.SkillLevel);
            Append(canonical, (int)cohort.Tools);
        }
        Append(canonical, humanVillage.Villagers.Count);
        foreach (var villager in humanVillage.Villagers)
        {
            Append(canonical, villager.Id);
            Append(canonical, (int)villager.Sex);
            Append(canonical, (int)villager.Role);
            Append(canonical, villager.Position);
            Append(canonical, (int)villager.Task);
            Append(canonical, villager.SkillLevel);
            Append(canonical, (int)villager.Tools);
            Append(canonical, villager.Health);
            Append(canonical, villager.MaximumHealth);
            Append(canonical, villager.Fatigue);
            Append(canonical, villager.MaximumFatigue);
            Append(canonical, villager.Hunger);
            Append(canonical, villager.Thirst);
            Append(canonical, villager.MaximumNeed);
            Append(canonical, villager.WorkProgress);
            Append(canonical, villager.CarriedGrime);
        }
        Append(canonical, humanVillage.Fields.Count);
        foreach (var field in humanVillage.Fields)
        {
            Append(canonical, field.Id);
            Append(canonical, field.Position);
            Append(canonical, (int)field.Phase);
            Append(canonical, field.GrowthDays);
            Append(canonical, field.WorkProgress);
        }

        var visibility = Visibility.CreateSnapshot();
        Append(canonical, visibility.Count);
        foreach (var state in visibility)
        {
            Append(canonical, (int)state);
        }

        Append(canonical, CurrentTick.Value);
        Append(canonical, _compostNutrients);
        Append(canonical, _nextEntityId);
        Append(canonical, _nextEventSequence);
        Append(canonical, _actors.Count);

        foreach (var actor in _actors.Values)
        {
            Append(canonical, actor.Id.Value);
            Append(canonical, actor.Name);
            Append(canonical, (int)actor.Sex);
            Append(canonical, (int)actor.KnownSkills);
            Append(canonical, (int)actor.KnownTraits);
            Append(canonical, (int)actor.Equipment);
            Append(canonical, actor.ForagingExperience);
            Append(canonical, actor.HaulingExperience);
            Append(canonical, actor.BuildingExperience);
            Append(canonical, actor.WorkPreferences.Foraging);
            Append(canonical, actor.WorkPreferences.Hauling);
            Append(canonical, actor.WorkPreferences.Building);
            Append(canonical, actor.Position);
            Append(canonical, actor.Hunger);
            Append(canonical, actor.Fatigue);
            Append(canonical, actor.Health);
            Append(canonical, actor.BleedingTicksRemaining);
            Append(canonical, actor.Thirst);
            Append(canonical, actor.PersonalFood);
            foreach (var foodKind in actor.PersonalFoodKinds)
            {
                Append(canonical, (int)foodKind);
            }
            foreach (var freshUntilTick in actor.PersonalFoodFreshUntilTicks)
            {
                Append(canonical, freshUntilTick);
            }
            Append(canonical, actor.PersonalWater);
            Append(canonical, actor.PersonalStoneAmmo);
            Append(canonical, actor.BloodFootprintSteps);
            Append(canonical, actor.CarriedGrime);
            Append(canonical, actor.BirthTick ?? -1);
            Append(canonical, actor.MaturesAtTick ?? -1);
            Append(canonical, actor.AgeOffsetTicks);
            Append(canonical, actor.CarriedStackId.Value);
            Append(canonical, actor.CarriedCorpseId.Value);
            Append(canonical, (int)actor.JobKind);
            Append(canonical, (int)actor.JobPhase);
            Append(canonical, (int)actor.JobStage);
            Append(canonical, actor.JobTarget);
            Append(canonical, actor.RemainingWorkTicks);
            Append(canonical, actor.SourceStackId.Value);
            Append(canonical, actor.DestinationZoneId.Value);
            Append(canonical, actor.ReservedQuantity);
            Append(canonical, actor.RemainingRoute.Count);
            foreach (var position in actor.RemainingRoute)
            {
                Append(canonical, position);
            }
            AppendNavigationKnowledge(canonical, actor.NavigationKnowledge);
            Append(canonical, actor.PendingNavigationReports.Count);
            foreach (var edge in OrderNavigationEdges(actor.PendingNavigationReports))
            {
                Append(canonical, edge.First);
                Append(canonical, edge.Second);
            }
            Append(canonical, (int)actor.SuspendedJobKind);
            Append(canonical, actor.SuspendedJobTarget);
            Append(canonical, (int)actor.TacticalOrderKind);
            Append(canonical, actor.TacticalCenter);
            Append(canonical, actor.TacticalRadius);
            Append(canonical, actor.PatrolPointIndex);
            Append(canonical, actor.TacticalTargetEntityId.Value);
            Append(canonical, actor.DispatcherSuspendedUntilTick);
            Append(canonical, actor.PatrolPoints.Count);
            foreach (var point in actor.PatrolPoints)
            {
                Append(canonical, point);
            }
        }

        Append(canonical, _itemStacks.Count);
        foreach (var stack in _itemStacks.Values)
        {
            Append(canonical, stack.Id.Value);
            Append(canonical, (int)stack.Resource);
            Append(canonical, (int)stack.FoodKind);
            Append(canonical, (int)stack.Variant);
            Append(canonical, stack.Quantity);
            Append(canonical, (int)stack.Location.Kind);
            Append(canonical, stack.Location.Position);
            Append(canonical, stack.Location.OwnerId.Value);
            Append(canonical, (int)stack.HaulPriority);
            Append(canonical, stack.FreshUntilTick ?? -1);
        }

        Append(canonical, _resourcePriorities.Count);
        foreach (var pair in _resourcePriorities)
        {
            Append(canonical, (int)pair.Key);
            Append(canonical, (int)pair.Value);
        }

        Append(canonical, _workTypePriorities.Count);
        foreach (var pair in _workTypePriorities)
        {
            Append(canonical, pair.Key);
            Append(canonical, (int)pair.Value);
        }

        Append(canonical, _storageZones.Count);
        foreach (var zone in _storageZones.Values)
        {
            Append(canonical, zone.Id.Value);
            Append(canonical, zone.Position);
            Append(canonical, (int)zone.AcceptedResource);
            Append(canonical, zone.Capacity);
            Append(canonical, zone.DesiredQuantity);
            Append(canonical, zone.AssignedHaulerId.Value);
            Append(canonical, zone.SourceStorageZoneId.Value);
            Append(canonical, (int)zone.Priority);
            Append(canonical, (int)zone.MineralFilter);
            Append(canonical, zone.SlotPolicy.SlotCount);
            Append(canonical, zone.SlotPolicy.StackCapacity);
            Append(canonical, zone.SlotPolicy.SeparatesItemTypes ? 1 : 0);
            Append(canonical, (int)zone.SlotPolicy.Capabilities);
            Append(canonical, zone.LogisticsNetworkId.Value);
            Append(canonical, zone.StorageAreaId.Value);
            Append(canonical, (int)zone.ProviderKind);
            Append(canonical, (int)zone.ResourceFilter);
        }

        Append(canonical, _storageAreas.Count);
        foreach (var area in _storageAreas.Values)
        {
            Append(canonical, area.Id.Value);
            Append(canonical, area.Name);
            Append(canonical, area.LogisticsNetworkId.Value);
            Append(canonical, area.Footprint.Length);
            foreach (var cell in area.Footprint)
            {
                Append(canonical, cell);
            }
        }

        Append(canonical, _logisticsNetworks.Count);
        foreach (var network in _logisticsNetworks.Values)
        {
            Append(canonical, network.Id.Value);
            Append(canonical, network.Name);
            Append(canonical, network.AssignedHaulerIds.Count);
            foreach (var actorId in network.AssignedHaulerIds)
            {
                Append(canonical, actorId.Value);
            }
            Append(canonical, network.SourceStorageZoneIds.Count);
            foreach (var zoneId in network.SourceStorageZoneIds)
            {
                Append(canonical, zoneId.Value);
            }
        }

        Append(canonical, _workDesignations.Count);
        foreach (var designation in _workDesignations.Values)
        {
            Append(canonical, designation.Id.Value);
            Append(canonical, (int)designation.Kind);
            Append(canonical, designation.Target);
            Append(canonical, designation.RampDestination is null ? 0 : 1);
            if (designation.RampDestination is { } rampDestination)
            {
                Append(canonical, rampDestination);
            }
            Append(canonical, designation.TargetEntityId.Value);
            Append(canonical, designation.OrderId.Value);
            Append(canonical, (int)designation.Priority);
            Append(canonical, designation.IsSuspended ? 1 : 0);
        }

        Append(canonical, _constructionSites.Count);
        foreach (var site in _constructionSites.Values)
        {
            Append(canonical, site.Id.Value);
            Append(canonical, (int)site.Kind);
            Append(canonical, site.Anchor);
            Append(canonical, site.End);
            Append(canonical, (int)site.RequiredResource);
            Append(canonical, (int)site.RequiredVariant);
            Append(canonical, site.RequiredQuantity);
            Append(canonical, site.DeliveredQuantity);
            Append(canonical, (int)site.DeliveredVariant);
            Append(canonical, site.RemainingWorkTicks);
            Append(canonical, site.TotalWorkTicks);
            Append(canonical, (int)site.Capabilities.RequiredSkills);
            Append(canonical, site.Capabilities.MinimumBuildingLevel);
            Append(canonical, (int)site.Capabilities.RequiredEquipment);
            Append(canonical, (int)site.Priority);
            Append(canonical, site.OrderId.Value);
            Append(canonical, site.SequenceIndex);
        }

        Append(canonical, _craftingOrders.Count);
        foreach (var order in _craftingOrders.Values)
        {
            Append(canonical, order.Id.Value);
            Append(canonical, (int)order.Recipe);
            Append(canonical, order.Workshop);
            Append(canonical, order.DeliveredMaterials.Count);
            foreach (var material in order.DeliveredMaterials)
            {
                Append(canonical, (int)material.Resource);
                Append(canonical, (int)material.FoodKind);
                Append(canonical, (int)material.Variant);
                Append(canonical, material.Quantity);
                Append(canonical, material.FreshUntilTick ?? -1);
            }
            Append(canonical, order.RemainingWorkTicks);
            Append(canonical, order.IsRepeating ? 1 : 0);
            Append(canonical, order.IsAutomatic ? 1 : 0);
        }

        Append(canonical, _pendingCommands.Count);
        foreach (var command in _pendingCommands.Values)
        {
            Append(canonical, command.ExecuteAt.Value);
            Append(canonical, command.Sequence);
            Append(canonical, (int)command.Kind);
            Append(canonical, command.Subject.Value);
            Append(canonical, command.Target.Value);
            Append(canonical, command.Position);
            Append(canonical, command.EndPosition);
            Append(canonical, (int)command.Construction);
            Append(canonical, (int)command.Resource);
            Append(canonical, command.Amount);
            Append(canonical, command.Text);
            Append(canonical, (int)command.MaterialVariant);
        }

        var bytes = Encoding.UTF8.GetBytes(canonical.ToString());
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static void ValidateSaveHeader(
        SimulationSaveModel save,
        SimulationDefinitions definitions)
    {
        if (save.FormatVersion != SaveFormatVersion)
        {
            throw new InvalidDataException($"Unsupported save format version {save.FormatVersion}.");
        }

        if (save.CompostNutrients < 0)
        {
            throw new InvalidDataException("The save contains an invalid compost nutrient count.");
        }

        if (!StringComparer.Ordinal.Equals(save.DefinitionsId, definitions.Id))
        {
            throw new InvalidDataException(
                $"Save requires definitions '{save.DefinitionsId}', but '{definitions.Id}' were supplied.");
        }

        if (!StringComparer.Ordinal.Equals(
                save.ClimateProfileId,
                definitions.Clock.Climate.Id))
        {
            throw new InvalidDataException(
                $"Save requires climate profile '{save.ClimateProfileId}', but " +
                $"'{definitions.Clock.Climate.Id}' was supplied.");
        }

        if (!PolityId.TryParse(save.PlayerPolityId, out var playerPolityId) ||
            playerPolityId != CorePolityIds.PlayerTribe)
        {
            throw new InvalidDataException(
                $"Save requires unsupported player polity '{save.PlayerPolityId}'.");
        }

        if (!SwampMapGenerator.SupportsSavedVersion(save.MapGeneratorVersion))
        {
            throw new InvalidDataException(
                $"Obsolete or incompatible map generator version " +
                $"{save.MapGeneratorVersion}; supported versions are " +
                $"{SwampMapGenerator.MinimumSaveCompatibleVersion}-" +
                $"{SwampMapGenerator.CurrentVersion}.");
        }

        if (!ContentId.TryParse(save.MapProfileId, out var mapProfileId) ||
            mapProfileId != SwampMapGenerator.DefaultProfileId)
        {
            throw new InvalidDataException(
                $"Save requires unsupported map profile '{save.MapProfileId}'.");
        }

        if (!Enum.IsDefined(save.MapRiverMode))
        {
            throw new InvalidDataException(
                $"Save requires unsupported river mode '{save.MapRiverMode}'.");
        }

        if (!Enum.IsDefined(save.MapRoadMode))
        {
            throw new InvalidDataException(
                $"Save requires unsupported road mode '{save.MapRoadMode}'.");
        }

        if (save.CurrentTick < 0 || save.NextEntityId == 0 || save.NextEventSequence == 0)
        {
            throw new InvalidDataException("The save contains invalid scalar state.");
        }
    }

    private void LoadActors(IEnumerable<ActorSaveModel> actorModels)
    {
        foreach (var actorModel in actorModels.OrderBy(actor => actor.Id))
        {
            var id = new EntityId(actorModel.Id);
            var position = new GridPosition(actorModel.X, actorModel.Y, actorModel.Z);
            var generatedPreferences = _goblinActorGenerator.CreateWorkPreferences(id);
            var workPreferences = new GoblinWorkPreferences(
                actorModel.ForagingPreference ?? generatedPreferences.Foraging,
                actorModel.HaulingPreference ?? generatedPreferences.Hauling,
                actorModel.BuildingPreference ?? generatedPreferences.Building);
            var personalFoodKinds = actorModel.PersonalFoodKinds is null
                ? Enumerable.Repeat(actorModel.PersonalFoodKind, actorModel.PersonalFood).ToArray()
                : actorModel.PersonalFoodKinds.ToArray();
            var personalFoodFreshUntilTicks = actorModel.PersonalFoodFreshUntilTicks is null
                ? personalFoodKinds.Select(kind => FoodPreservationPolicy.GetFreshUntilTick(
                    kind,
                    CurrentTick,
                    Definitions.Clock)).ToArray()
                : actorModel.PersonalFoodFreshUntilTicks.ToArray();
            var tacticalCenter = new GridPosition(
                actorModel.TacticalCenterX,
                actorModel.TacticalCenterY,
                actorModel.TacticalCenterZ);
            var hasValidTacticalOrder = actorModel.TacticalOrderKind switch
            {
                ActorTacticalOrderKind.None => actorModel.TacticalRadius == 0 &&
                    tacticalCenter == default && actorModel.PatrolPoints.Count == 0 &&
                    actorModel.TacticalTargetEntityId == 0,
                ActorTacticalOrderKind.Patrol => actorModel.TacticalRadius == 0 &&
                    tacticalCenter == default && actorModel.PatrolPoints.Count >= 2 &&
                    actorModel.TacticalTargetEntityId == 0,
                ActorTacticalOrderKind.AttackArea =>
                    actorModel.TacticalRadius is >= MinimumRaidTargetRadius and
                        <= MaximumRaidTargetRadius &&
                    IsAddressableMapPosition(tacticalCenter) &&
                    actorModel.PatrolPoints.Count == 0 &&
                    actorModel.TacticalTargetEntityId == 0,
                ActorTacticalOrderKind.HuntArea =>
                    actorModel.TacticalRadius is >= MinimumRaidTargetRadius and
                        <= MaximumRaidTargetRadius &&
                    IsAddressableMapPosition(tacticalCenter) &&
                    actorModel.PatrolPoints.Count == 0,
                _ => false,
            };
            if (id == EntityId.None ||
                string.IsNullOrWhiteSpace(actorModel.Name) ||
                !Enum.IsDefined(actorModel.Sex) ||
                !PolityId.TryParse(actorModel.PolityId, out var actorPolityId) ||
                actorPolityId != CorePolityIds.PlayerTribe ||
                !HasOnlyKnownFlags(actorModel.KnownSkills, GoblinSkill.Building) ||
                !HasOnlyKnownFlags(actorModel.KnownTraits, GoblinTrait.Fastidious) ||
                !HasOnlyKnownFlags(actorModel.Equipment, PersonalEquipment.WoodenBucket) ||
                actorModel.ForagingExperience < 0 ||
                actorModel.HaulingExperience < 0 ||
                actorModel.BuildingExperience < 0 ||
                !workPreferences.IsValid ||
                actorModel.Hunger < 0 || actorModel.Hunger > GoblinNeeds.MaximumHunger ||
                actorModel.Fatigue < 0 || actorModel.Fatigue > GoblinNeeds.MaximumFatigue ||
                actorModel.Health <= 0 || actorModel.Health > GoblinVitals.MaximumHealth ||
                actorModel.Thirst < 0 || actorModel.Thirst > GoblinNeeds.MaximumThirst ||
                actorModel.PersonalFood < 0 || actorModel.PersonalFood > Definitions.PersonalFoodCapacity ||
                personalFoodKinds.Length != actorModel.PersonalFood ||
                personalFoodFreshUntilTicks.Length != actorModel.PersonalFood ||
                personalFoodKinds.Any(kind => !Enum.IsDefined(kind) || kind == FoodKind.None) ||
                personalFoodFreshUntilTicks.Any(tick => tick <= 0) ||
                (personalFoodKinds.Length == 0
                    ? actorModel.PersonalFoodKind != FoodKind.None
                    : actorModel.PersonalFoodKind != personalFoodKinds[0]) ||
                actorModel.PersonalWater < 0 || actorModel.PersonalWater > Definitions.PersonalWaterCapacity ||
                actorModel.PersonalStoneAmmo < 0 ||
                actorModel.PersonalStoneAmmo > GetStoneAmmoCapacity(actorModel.Equipment) ||
                actorModel.BloodFootprintSteps is < 0 or > BloodFootprintMaximumSteps ||
                actorModel.CarriedGrime is < 0 or >
                    Contamination.SurfaceGrimeState.MaximumCarriedAmount ||
                actorModel.BleedingTicksRemaining is < 0 or > MaximumBleedingTicks ||
                actorModel.BirthTick is < 0 ||
                actorModel.MaturesAtTick is < 0 ||
                actorModel.AgeOffsetTicks is < 0 ||
                actorModel.DispatcherSuspendedUntilTick < 0 ||
                (actorModel.BirthTick.HasValue != actorModel.MaturesAtTick.HasValue) ||
                (actorModel.BirthTick.HasValue &&
                 (actorModel.MaturesAtTick <= actorModel.BirthTick ||
                  actorModel.BirthTick > CurrentTick.Value ||
                  actorModel.AgeOffsetTicks is > 0)) ||
                !Enum.IsDefined(actorModel.SuspendedJobKind) ||
                !hasValidTacticalOrder ||
                actorModel.PatrolPointIndex < 0 ||
                actorModel.PatrolPointIndex >= Math.Max(1, actorModel.PatrolPoints.Count) ||
                actorModel.PatrolPoints.Any(point => !World.IsTerrainReachable(
                    new GridPosition(point.X, point.Y, point.Z))) ||
                (actorModel.SuspendedJobKind == ActorJobKind.None &&
                 (actorModel.SuspendedTargetX != 0 || actorModel.SuspendedTargetY != 0 ||
                  actorModel.SuspendedTargetZ != 0)) ||
                (actorModel.SuspendedJobKind != ActorJobKind.None &&
                 !World.IsTerrainReachable(new GridPosition(
                     actorModel.SuspendedTargetX,
                     actorModel.SuspendedTargetY,
                     actorModel.SuspendedTargetZ))) ||
                !World.IsTerrainTraversable(position))
            {
                throw new InvalidDataException("The save contains an invalid actor.");
            }

            var actor = new ActorState(id, position, actorModel.Hunger)
            {
                Name = actorModel.Name,
                Sex = actorModel.Sex,
                KnownSkills = actorModel.KnownSkills,
                KnownTraits = actorModel.KnownTraits,
                Equipment = actorModel.Equipment,
                ForagingExperience = actorModel.ForagingExperience,
                HaulingExperience = actorModel.HaulingExperience,
                BuildingExperience = actorModel.BuildingExperience,
                WorkPreferences = workPreferences,
                CarriedStackId = new EntityId(actorModel.CarriedStackId),
                CarriedCorpseId = new EntityId(actorModel.CarriedCorpseId),
                Fatigue = actorModel.Fatigue,
                Health = actorModel.Health,
                Thirst = actorModel.Thirst,
                PersonalWater = actorModel.PersonalWater,
                PersonalStoneAmmo = actorModel.PersonalStoneAmmo,
                BloodFootprintSteps = actorModel.BloodFootprintSteps,
                CarriedGrime = actorModel.CarriedGrime,
                BleedingTicksRemaining = actorModel.BleedingTicksRemaining,
                BirthTick = actorModel.BirthTick,
                MaturesAtTick = actorModel.MaturesAtTick,
                AgeOffsetTicks = actorModel.AgeOffsetTicks ??
                    (actorModel.BirthTick.HasValue ? 0 : CreateInitialAgeOffsetTicks(id)),
                JobKind = actorModel.JobKind,
                JobPhase = actorModel.JobPhase,
                JobStage = actorModel.JobStage,
                JobTarget = new GridPosition(
                    actorModel.JobTargetX,
                    actorModel.JobTargetY,
                    actorModel.JobTargetZ),
                RemainingWorkTicks = actorModel.RemainingWorkTicks,
                SourceStackId = new EntityId(actorModel.SourceStackId),
                DestinationZoneId = new EntityId(actorModel.DestinationZoneId),
                ReservedQuantity = actorModel.ReservedQuantity,
                SuspendedJobKind = actorModel.SuspendedJobKind,
                SuspendedJobTarget = new GridPosition(
                    actorModel.SuspendedTargetX,
                    actorModel.SuspendedTargetY,
                    actorModel.SuspendedTargetZ),
                TacticalOrderKind = actorModel.TacticalOrderKind,
                TacticalCenter = new GridPosition(
                    actorModel.TacticalCenterX,
                    actorModel.TacticalCenterY,
                    actorModel.TacticalCenterZ),
                TacticalRadius = actorModel.TacticalRadius,
                PatrolPointIndex = actorModel.PatrolPointIndex,
                TacticalTargetEntityId = new EntityId(actorModel.TacticalTargetEntityId),
                DispatcherSuspendedUntilTick = actorModel.DispatcherSuspendedUntilTick,
            };
            actor.PersonalFoodKinds.AddRange(personalFoodKinds);
            actor.PersonalFoodFreshUntilTicks.AddRange(personalFoodFreshUntilTicks);

            actor.RemainingRoute.AddRange(actorModel.RemainingRoute.Select(routePosition =>
                new GridPosition(routePosition.X, routePosition.Y, routePosition.Z)));
            actor.PatrolPoints.AddRange(actorModel.PatrolPoints.Select(point =>
                new GridPosition(point.X, point.Y, point.Z)));
            RestoreNavigationBeliefs(actor.NavigationKnowledge, actorModel.NavigationBeliefs);
            RestorePendingNavigationReports(actor, actorModel.PendingNavigationReports);
            ValidateLoadedJob(actor);

            if (!_actors.TryAdd(id, actor))
            {
                throw new InvalidDataException($"The save contains duplicate actor {id}.");
            }
        }
    }

    private static void RestoreNavigationBeliefs(
        NavigationKnowledgeState knowledge,
        IEnumerable<NavigationBeliefSaveModel>? beliefModels)
    {
        if (beliefModels is null)
        {
            throw new InvalidDataException("The save contains invalid actor navigation knowledge.");
        }

        try
        {
            foreach (var model in beliefModels)
            {
                var edge = NavigationEdge.Between(
                    new GridPosition(model.FirstX, model.FirstY, model.FirstZ),
                    new GridPosition(model.SecondX, model.SecondY, model.SecondZ));
                knowledge.Restore(new NavigationBelief(
                    edge,
                    model.Status,
                    new SimulationTick(model.ObservedAt),
                    new SimulationTick(model.ReceivedAt),
                    new EntityId(model.SourceActorId),
                    model.Confidence,
                    model.IsDirectObservation));
            }
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "The save contains invalid actor navigation knowledge.",
                exception);
        }
    }

    private void LoadTribeNavigationBeliefs(
        IEnumerable<NavigationBeliefSaveModel>? beliefModels)
    {
        RestoreNavigationBeliefs(_tribeNavigationKnowledge, beliefModels);
    }

    private static void RestorePendingNavigationReports(
        ActorState actor,
        IEnumerable<NavigationEdgeSaveModel>? edgeModels)
    {
        if (edgeModels is null)
        {
            throw new InvalidDataException("The save contains invalid pending navigation reports.");
        }

        try
        {
            foreach (var model in edgeModels)
            {
                var edge = NavigationEdge.Between(
                    new GridPosition(model.FirstX, model.FirstY, model.FirstZ),
                    new GridPosition(model.SecondX, model.SecondY, model.SecondZ));
                if (!actor.NavigationKnowledge.TryGet(edge, out var belief) ||
                    belief.SourceActorId != actor.Id || !belief.IsDirectObservation ||
                    !actor.PendingNavigationReports.Add(edge))
                {
                    throw new InvalidDataException(
                        "The save contains an invalid pending navigation report.");
                }
            }
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "The save contains an invalid pending navigation report.",
                exception);
        }
    }

    private void LoadStorageZones(IEnumerable<StorageZoneSaveModel> zoneModels)
    {
        foreach (var zoneModel in zoneModels.OrderBy(zone => zone.Id))
        {
            var id = new EntityId(zoneModel.Id);
            var position = new GridPosition(zoneModel.X, zoneModel.Y, zoneModel.Z);
            var mineralFilter = zoneModel.MineralFilter ?? MineralStorageFilter.All;
            var resourceFilter = zoneModel.ResourceFilter;
            var slotPolicy = zoneModel.SlotCount is null && zoneModel.StackCapacity is null &&
                zoneModel.SeparatesItemTypes is null && zoneModel.Capabilities is null
                    ? CreateDefaultStorageSlotPolicy(zoneModel.AcceptedResource, zoneModel.Capacity)
                    : new StorageSlotPolicy(
                        zoneModel.SlotCount ?? 0,
                        zoneModel.StackCapacity ?? 0,
                        zoneModel.SeparatesItemTypes ?? false,
                        zoneModel.Capabilities ?? StorageCapability.None);
            if (id == EntityId.None ||
                !World.IsTerrainTraversable(position) ||
                zoneModel.Capacity <= 0 ||
                zoneModel.DesiredQuantity < 0 ||
                zoneModel.DesiredQuantity > zoneModel.Capacity ||
                !Enum.IsDefined(zoneModel.Priority) ||
                !Enum.IsDefined(zoneModel.ProviderKind) ||
                !IsValidMineralFilter(mineralFilter) ||
                (zoneModel.AcceptedResource != ResourceKind.Stone &&
                 mineralFilter != MineralStorageFilter.All) ||
                !IsStorageFilterResource(zoneModel.AcceptedResource) ||
                !IsValidStorageResourceFilter(resourceFilter) ||
                GetStorageFilterDisplayResource(resourceFilter) != zoneModel.AcceptedResource ||
                !IsValidStorageSlotPolicy(slotPolicy, zoneModel.Capacity) ||
                !IsValidStorageProvider(
                    zoneModel.ProviderKind,
                    zoneModel.AcceptedResource,
                    resourceFilter,
                    zoneModel.Capacity,
                    slotPolicy))
            {
                throw new InvalidDataException("The save contains an invalid storage zone.");
            }

            var zone = new StorageZoneState(
                id,
                position,
                zoneModel.AcceptedResource,
                zoneModel.Capacity,
                zoneModel.DesiredQuantity,
                new EntityId(zoneModel.AssignedHaulerId),
                new EntityId(zoneModel.SourceStorageZoneId),
                zoneModel.Priority,
                mineralFilter,
                slotPolicy,
                new EntityId(zoneModel.LogisticsNetworkId),
                new EntityId(zoneModel.StorageAreaId),
                zoneModel.ProviderKind,
                resourceFilter);
            if (!_storageZones.TryAdd(id, zone))
            {
                throw new InvalidDataException($"The save contains duplicate storage zone {id}.");
            }
            IndexStorageZone(zone);
        }
    }

    private void LoadLogisticsNetworks(IEnumerable<LogisticsNetworkSaveModel> networkModels)
    {
        _logisticsNetworks.Clear();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in networkModels.OrderBy(network => network.Id))
        {
            var id = new EntityId(model.Id);
            if (!IsValidManagementName(model.Name) || !names.Add(model.Name.Trim()) ||
                model.AssignedHaulerIds.Distinct().Count() != model.AssignedHaulerIds.Count ||
                model.SourceStorageZoneIds.Distinct().Count() != model.SourceStorageZoneIds.Count)
            {
                throw new InvalidDataException("The save contains an invalid logistics network.");
            }

            var network = new LogisticsNetworkState(id, model.Name);
            foreach (var actorId in model.AssignedHaulerIds.Select(value => new EntityId(value)))
            {
                network.AssignedHaulerIds.Add(actorId);
            }
            foreach (var zoneId in model.SourceStorageZoneIds.Select(value => new EntityId(value)))
            {
                network.SourceStorageZoneIds.Add(zoneId);
            }
            if (!_logisticsNetworks.TryAdd(id, network))
            {
                throw new InvalidDataException($"The save contains duplicate logistics network {id}.");
            }
        }

        if (!_logisticsNetworks.TryGetValue(EntityId.None, out var defaultNetwork) ||
            defaultNetwork.Name != "Default" ||
            defaultNetwork.AssignedHaulerIds.Count != 0 ||
            defaultNetwork.SourceStorageZoneIds.Count != 0)
        {
            throw new InvalidDataException("The save does not contain the canonical default logistics network.");
        }
    }

    private void LoadStorageAreas(IEnumerable<StorageAreaSaveModel> areaModels)
    {
        var occupiedCells = new HashSet<GridPosition>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in areaModels.OrderBy(area => area.Id))
        {
            var id = new EntityId(model.Id);
            var networkId = new EntityId(model.LogisticsNetworkId);
            var footprint = model.Footprint
                .Select(cell => new GridPosition(cell.X, cell.Y, cell.Z))
                .OrderBy(cell => cell.Z)
                .ThenBy(cell => cell.Y)
                .ThenBy(cell => cell.X)
                .ToArray();
            var isSingleLevel = footprint.Length > 0 &&
                footprint.All(cell => cell.Z == footprint[0].Z);
            var expectedRectangle = isSingleLevel
                ? EnumerateAreaCells(footprint[0], footprint[^1]).ToArray()
                : [];
            if (id == EntityId.None || !IsValidManagementName(model.Name) ||
                !names.Add(model.Name.Trim()) ||
                !_logisticsNetworks.ContainsKey(networkId) || footprint.Length == 0 ||
                footprint.Length > 256 || footprint.Distinct().Count() != footprint.Length ||
                !isSingleLevel || !footprint.SequenceEqual(expectedRectangle) ||
                footprint.Any(cell => !World.IsTerrainTraversable(cell)) ||
                footprint.Any(cell => !occupiedCells.Add(cell)))
            {
                throw new InvalidDataException("The save contains an invalid storage area.");
            }

            var area = new StorageAreaState(id, model.Name, footprint, networkId);
            if (!_storageAreas.TryAdd(id, area))
            {
                throw new InvalidDataException($"The save contains duplicate storage area {id}.");
            }
        }
    }

    private void LoadResourcePriorities(IEnumerable<ResourcePrioritySaveModel> models)
    {
        var loaded = models.ToArray();
        var expectedResources = Enum.GetValues<ResourceKind>()
            .Where(IsStorableResource)
            .ToArray();
        if (loaded.Length != expectedResources.Length ||
            loaded.Select(model => model.Resource).Distinct().Count() != loaded.Length ||
            loaded.Any(model =>
                !IsStorableResource(model.Resource) ||
                !Enum.IsDefined(model.Priority)))
        {
            throw new InvalidDataException("The save contains invalid resource priorities.");
        }

        _resourcePriorities.Clear();
        foreach (var model in loaded.OrderBy(model => model.Resource))
        {
            _resourcePriorities.Add(model.Resource, model.Priority);
        }
    }

    private void LoadWorkDesignations(IEnumerable<WorkDesignationSaveModel> models)
    {
        var legacyOrders = new Dictionary<WorkDesignationKind, EntityId>();
        foreach (var model in models.OrderBy(item => item.Id))
        {
            var id = new EntityId(model.Id);
            var target = new GridPosition(model.TargetX, model.TargetY, model.TargetZ);
            var rampDestination = model.HasRampDestination
                ? new GridPosition(
                    model.RampDestinationX,
                    model.RampDestinationY,
                    model.RampDestinationZ)
                : (GridPosition?)null;
            var targetEntityId = new EntityId(model.TargetEntityId);
            var orderId = model.OrderId == 0
                ? legacyOrders.GetValueOrDefault(model.Kind, id)
                : new EntityId(model.OrderId);
            legacyOrders.TryAdd(model.Kind, orderId);
            var priority = model.Priority ?? StoragePriority.Normal;
            if (id == EntityId.None ||
                orderId == EntityId.None ||
                !Enum.IsDefined(model.Kind) ||
                !Enum.IsDefined(priority) ||
                !IsAddressableMapPosition(target) ||
                (model.Kind is WorkDesignationKind.GatherFood or WorkDesignationKind.GatherReeds or
                    WorkDesignationKind.GatherLichen or
                    WorkDesignationKind.UprootBerryBush or WorkDesignationKind.FellTree or
                    WorkDesignationKind.QuarryBoulder or WorkDesignationKind.MineRock or
                    WorkDesignationKind.Scout or WorkDesignationKind.CarveRampDown or
                    WorkDesignationKind.CarveRampUp or WorkDesignationKind.CleanBlood &&
                 targetEntityId != EntityId.None) ||
                (rampDestination is not null &&
                 (model.Kind is not (WorkDesignationKind.CarveRampDown or
                     WorkDesignationKind.CarveRampUp) ||
                  !IsValidRampDesignation(model.Kind, target, rampDestination.Value))) ||
                (model.Kind is WorkDesignationKind.GatherBrushwood or WorkDesignationKind.GatherStone or
                    WorkDesignationKind.HuntAnimal or
                    WorkDesignationKind.DismantleWorldObject or
                    WorkDesignationKind.DismantleStorageZone &&
                 targetEntityId == EntityId.None) ||
                !_workDesignations.TryAdd(
                    id,
                    new WorkDesignationSnapshot(id, model.Kind, target, targetEntityId)
                    {
                        OrderId = orderId,
                        Priority = priority,
                        IsSuspended = model.IsSuspended,
                        RampDestination = rampDestination,
                    }))
            {
                throw new InvalidDataException("The save contains an invalid work designation.");
            }
        }
    }

    private void LoadConstructionSites(IEnumerable<ConstructionSiteSaveModel> models)
    {
        foreach (var model in models.OrderBy(item => item.Id))
        {
            var id = new EntityId(model.Id);
            var anchor = new GridPosition(model.AnchorX, model.AnchorY, model.AnchorZ);
            var end = new GridPosition(model.EndX, model.EndY, model.EndZ);
            var requiredResource = model.RequiredResource ?? ResourceKind.Wood;
            var requiredVariant = model.RequiredVariant ?? ResourceVariant.None;
            if (id == EntityId.None ||
                !Enum.IsDefined(model.Kind) ||
                requiredResource is not (ResourceKind.Wood or ResourceKind.Stone or
                    ResourceKind.Equipment) ||
                requiredVariant != ResourceVariant.None &&
                    requiredResource is ResourceKind.Wood or ResourceKind.Stone &&
                    (!MaterialCatalog.TryGet(requiredVariant, out var requiredMaterial) ||
                     requiredMaterial.ResourceKind != requiredResource ||
                     !requiredMaterial.Uses.Contains(MaterialUse.Construction)) ||
                !IsPotentialConstructionPosition(anchor) ||
                !IsPotentialConstructionPosition(end) ||
                model.RequiredWood <= 0 ||
                model.DeliveredWood < 0 || model.DeliveredWood > model.RequiredWood ||
                model.DeliveredWood == 0 &&
                    model.DeliveredVariant != ResourceVariant.None ||
                model.DeliveredWood > 0 &&
                    ConstructionBlueprintCatalog.RetainsMaterialIdentity(model.Kind) &&
                    (!Enum.IsDefined(model.DeliveredVariant) ||
                     !IsValidResourceVariant(
                         requiredResource,
                         model.DeliveredVariant,
                         allowLegacyDefault: false)) ||
                model.DeliveredWood > 0 &&
                    !ConstructionBlueprintCatalog.RetainsMaterialIdentity(model.Kind) &&
                    model.DeliveredVariant != ResourceVariant.None ||
                model.TotalWorkTicks <= 0 ||
                model.RemainingWorkTicks <= 0 ||
                model.RemainingWorkTicks > model.TotalWorkTicks ||
                !HasOnlyKnownFlags(model.RequiredSkills, GoblinSkill.Building) ||
                model.MinimumBuildingLevel < 0 ||
                !HasOnlyKnownFlags(model.RequiredEquipment, PersonalEquipment.WoodenBucket))
            {
                throw new InvalidDataException("The save contains an invalid construction site.");
            }

            var expected = ConstructionBlueprintCatalog.CreateSite(
                id, model.Kind, anchor, end,
                requiredVariantOverride: requiredVariant);
            var priority = model.Priority ?? StoragePriority.Normal;
            var orderId = model.OrderId == 0 ? id : new EntityId(model.OrderId);
            if (expected.RequiredResource != requiredResource ||
                expected.RequiredVariant != requiredVariant ||
                expected.RequiredQuantity != model.RequiredWood ||
                expected.TotalWorkTicks != model.TotalWorkTicks ||
                !Enum.IsDefined(priority) ||
                expected.Capabilities != new ConstructionCapabilityRequirements(
                    model.RequiredSkills,
                    model.MinimumBuildingLevel,
                    model.RequiredEquipment))
            {
                throw new InvalidDataException("The saved construction site does not match its blueprint.");
            }

            var site = new ConstructionSiteState(
                id,
                model.Kind,
                anchor,
                end,
                requiredResource,
                requiredVariant,
                model.RequiredWood,
                model.DeliveredWood,
                model.DeliveredVariant,
                model.RemainingWorkTicks,
                model.TotalWorkTicks,
                expected.Capabilities,
                priority,
                orderId,
                model.SequenceIndex);
            if (ConstructionBlueprintCatalog.RetainsMaterialIdentity(site.Kind) &&
                    site.DeliveredQuantity > 0 &&
                    site.DeliveredVariant == ResourceVariant.None ||
                !ConstructionBlueprintCatalog.RetainsMaterialIdentity(site.Kind) &&
                    site.DeliveredVariant != ResourceVariant.None ||
                site.DeliveredQuantity > 0 &&
                    !ConstructionMaterialMatches(
                        site,
                        site.RequiredResource,
                        site.DeliveredVariant) ||
                !CanPlaceConstruction(site.Kind, site.Anchor, site.End, site.GetFootprint()) ||
                _constructionSites.Values.Any(other =>
                    other.GetFootprint().Intersect(site.GetFootprint()).Any()) ||
                !_constructionSites.TryAdd(id, site))
            {
                throw new InvalidDataException("The save contains overlapping construction sites.");
            }
        }
    }

    private void LoadCraftingOrders(IEnumerable<CraftingOrderSaveModel> models)
    {
        foreach (var model in models.OrderBy(item => item.Id))
        {
            var id = new EntityId(model.Id);
            var workshop = new GridPosition(
                model.WorkshopX,
                model.WorkshopY,
                model.WorkshopZ);
            var deliveredMaterials = model.DeliveredMaterials.Select(material =>
                new CraftingDeliveredMaterialState(
                    material.Resource,
                    material.FoodKind,
                    material.Variant,
                    material.Quantity,
                    material.Resource == ResourceKind.Food
                        ? material.FreshUntilTick ?? FoodPreservationPolicy.GetFreshUntilTick(
                            material.FoodKind,
                            CurrentTick,
                            Definitions.Clock)
                        : null)).ToArray();
            if (id == EntityId.None ||
                !Enum.IsDefined(model.Recipe) ||
                !World.HasWorkshop(
                    workshop,
                    CraftingRecipeCatalog.Get(model.Recipe).Workshop) ||
                !HasValidCraftingDeliveries(model.Recipe, deliveredMaterials) ||
                model.RemainingWorkTicks <= 0 ||
                model.RemainingWorkTicks > CraftingRecipeCatalog.GetWorkTicks(model.Recipe) ||
                !_craftingOrders.TryAdd(
                    id,
                    new CraftingOrderState(
                        id,
                        model.Recipe,
                        workshop,
                        deliveredMaterials,
                        model.RemainingWorkTicks,
                        model.IsRepeating,
                        model.IsAutomatic)))
            {
                throw new InvalidDataException("The save contains an invalid crafting order.");
            }
        }
    }

    private static bool HasValidCraftingDeliveries(
        CraftingRecipeKind recipe,
        IReadOnlyList<CraftingDeliveredMaterialState> deliveredMaterials)
    {
        if (deliveredMaterials.Any(material =>
                !Enum.IsDefined(material.Resource) ||
                !Enum.IsDefined(material.FoodKind) ||
                !Enum.IsDefined(material.Variant) ||
                !IsValidResourceVariant(
                    material.Resource,
                    material.Variant,
                    allowLegacyDefault: true) ||
                material.Quantity <= 0 ||
                CraftingRecipeCatalog.FindMaterial(
                    recipe,
                    material.Resource,
                    material.FoodKind,
                    material.Variant) is null) ||
            deliveredMaterials.Select(material => (
                    material.Resource,
                    material.FoodKind,
                    material.Variant))
                .Distinct().Count() != deliveredMaterials.Count)
        {
            return false;
        }

        return CraftingRecipeCatalog.Get(recipe).Materials.All(requirement =>
            deliveredMaterials.Where(material => requirement.Matches(
                    material.Resource,
                    material.FoodKind,
                    material.Variant))
                .Sum(material => material.Quantity) <= requirement.Quantity);
    }

    private void LoadItemStacks(IEnumerable<ItemStackSaveModel> stackModels)
    {
        foreach (var stackModel in stackModels.OrderBy(stack => stack.Id))
        {
            var id = new EntityId(stackModel.Id);
            var position = new GridPosition(stackModel.X, stackModel.Y, stackModel.Z);
            var owner = new EntityId(stackModel.OwnerId);
            if (stackModel.LocationKind == ItemLocationKind.Ground &&
                World.TryResolveGroundItemPosition(position, out var resolvedPosition))
            {
                position = resolvedPosition;
            }
            var location = stackModel.LocationKind switch
            {
                ItemLocationKind.Ground => ItemLocation.OnGround(position),
                ItemLocationKind.ActorInventory => ItemLocation.CarriedBy(owner),
                ItemLocationKind.StorageZone => ItemLocation.StoredIn(owner, position),
                _ => throw new InvalidDataException("The save contains an invalid item location kind."),
            };

            if (id == EntityId.None ||
                !IsStorableResource(stackModel.Resource) ||
                !IsValidFoodKind(stackModel.Resource, stackModel.FoodKind) ||
                !IsValidResourceVariant(stackModel.Resource, stackModel.Variant, allowLegacyDefault: true) ||
                (stackModel.Resource == ResourceKind.Food &&
                 !FoodUsePolicy.CanBePacked(stackModel.FoodKind) &&
                 stackModel.LocationKind != ItemLocationKind.Ground) ||
                (stackModel.HaulPriority is { } haulPriority && !Enum.IsDefined(haulPriority)) ||
                stackModel.Quantity <= 0)
            {
                throw new InvalidDataException("The save contains an invalid item stack.");
            }

            long? freshUntilTick = stackModel.Resource == ResourceKind.Food
                ? stackModel.FreshUntilTick ?? FoodPreservationPolicy.GetFreshUntilTick(
                    stackModel.FoodKind,
                    CurrentTick,
                    Definitions.Clock)
                : null;
            if (freshUntilTick <= 0)
            {
                throw new InvalidDataException("The save contains an invalid food shelf life.");
            }
            var stack = new ItemStackState(
                id,
                stackModel.Resource,
                stackModel.FoodKind,
                NormalizeResourceVariant(stackModel.Resource, stackModel.Variant),
                stackModel.Quantity,
                location,
                freshUntilTick)
            {
                HaulPriority = stackModel.HaulPriority ?? StoragePriority.Normal,
            };
            if (!_itemStacks.TryAdd(id, stack))
            {
                throw new InvalidDataException($"The save contains duplicate item stack {id}.");
            }
            IndexItemStack(stack);
        }
    }

    private void ValidateLoadedWorkDesignations()
    {
        foreach (var designation in _workDesignations.Values)
        {
            var valid = TerrainModificationCatalog.TryGet(designation.Kind, out var terrain)
                ? TerrainModificationTargetPolicy.CanRetainDesignation(
                    terrain,
                    World,
                    Visibility,
                    designation.Target,
                    designation.RampDestination)
                : designation.Kind switch
            {
                WorkDesignationKind.GatherFood => World.GetPlantPatch(designation.Target) is
                    { Kind: not PlantKind.ReedBed },
                WorkDesignationKind.GatherReeds => World.GetPlantPatch(designation.Target) is
                    { Kind: PlantKind.ReedBed },
                WorkDesignationKind.GatherLichen =>
                    World.TryGetCaveFlora(designation.Target, out var flora) &&
                    flora.Kind == CaveFloraKind.LichenPatch,
                WorkDesignationKind.UprootBerryBush =>
                    World.GetPlantPatch(designation.Target) is { Kind: PlantKind.BerryBush },
                WorkDesignationKind.GatherBrushwood =>
                    _itemStacks.TryGetValue(designation.TargetEntityId, out var stack) &&
                    stack.Resource == ResourceKind.Wood,
                WorkDesignationKind.GatherStone =>
                    _itemStacks.TryGetValue(designation.TargetEntityId, out var stone) &&
                    IsMineralResource(stone.Resource),
                WorkDesignationKind.FellTree => World.GetFellableWood(designation.Target) is not null,
                WorkDesignationKind.QuarryBoulder =>
                    World.GetQuarriableBoulder(designation.Target) is not null,
                WorkDesignationKind.Scout => World.IsTerrainTraversable(designation.Target),
                WorkDesignationKind.HuntAnimal =>
                    _animals.TryGetValue(designation.TargetEntityId.Value, out var animal) &&
                    animal.Position == designation.Target,
                WorkDesignationKind.CleanBlood => HasCleanableSurface(designation.Target),
                WorkDesignationKind.DismantleWorldObject or
                    WorkDesignationKind.DismantleStorageZone =>
                    TryGetDismantlingWorkTicks(designation, out _),
                _ => false,
            };
            if (!valid)
            {
                throw new InvalidDataException("A work designation references a missing target object.");
            }
        }
    }

    private void ValidateLoadedOwnership()
    {
        foreach (var stack in _itemStacks.Values)
        {
            switch (stack.Location.Kind)
            {
                case ItemLocationKind.Ground:
                    if (!World.TryResolveGroundItemPosition(
                            stack.Location.Position,
                            out var resolvedPosition) ||
                        resolvedPosition != stack.Location.Position)
                    {
                        throw new InvalidDataException(
                            $"Ground stack {stack.Id} ({stack.Resource}/{stack.Variant}) has " +
                            $"an invalid position {stack.Location.Position}.");
                    }

                    break;
                case ItemLocationKind.ActorInventory:
                    if (!_actors.TryGetValue(stack.Location.OwnerId, out var actor) ||
                        actor.CarriedStackId != stack.Id)
                    {
                        throw new InvalidDataException("A carried stack does not match its actor.");
                    }

                    break;
                case ItemLocationKind.StorageZone:
                    if (!_storageZones.TryGetValue(stack.Location.OwnerId, out var zone) ||
                        zone.Position != stack.Location.Position)
                    {
                        throw new InvalidDataException("A stored stack does not match its storage zone.");
                    }

                    break;
                default:
                    throw new InvalidDataException("An item stack has an unsupported location.");
            }
        }

        foreach (var actor in _actors.Values)
        {
            if (actor.CarriedStackId == EntityId.None)
            {
                continue;
            }

            if (!_itemStacks.TryGetValue(actor.CarriedStackId, out var stack) ||
                stack.Location != ItemLocation.CarriedBy(actor.Id))
            {
                throw new InvalidDataException("An actor references an invalid carried stack.");
            }
        }

        var carriedCorpses = new HashSet<EntityId>();
        foreach (var actor in _actors.Values.Where(actor =>
                     actor.CarriedCorpseId != EntityId.None))
        {
            if (!carriedCorpses.Add(actor.CarriedCorpseId) ||
                !_corpses.TryGetValue(actor.CarriedCorpseId, out var corpse) ||
                corpse.Position != actor.Position)
            {
                throw new InvalidDataException("An actor references an invalid carried corpse.");
            }
        }

        foreach (var zone in _storageZones.Values)
        {
            if (GetStoredQuantity(zone.Id) > zone.Capacity ||
                GetStorageFilterDisplayResource(zone.ResourceFilter) != zone.AcceptedResource ||
                !IsValidStorageProvider(
                    zone.ProviderKind,
                    zone.AcceptedResource,
                    zone.ResourceFilter,
                    zone.Capacity,
                    zone.SlotPolicy) ||
                !_logisticsNetworks.ContainsKey(zone.LogisticsNetworkId) ||
                !_storageAreas.TryGetValue(zone.StorageAreaId, out var area) ||
                !area.Footprint.Contains(zone.Position) ||
                area.LogisticsNetworkId != zone.LogisticsNetworkId ||
                (zone.AssignedHaulerId != EntityId.None &&
                 !_actors.ContainsKey(zone.AssignedHaulerId)) ||
                (zone.SourceStorageZoneId != EntityId.None &&
                 (!TryGetCompatibleStorageSource(zone, zone.SourceStorageZoneId, out _))))
            {
                throw new InvalidDataException($"Storage zone {zone.Id} has invalid ownership or capacity.");
            }
        }

        var specialistHaulers = new HashSet<EntityId>();
        foreach (var network in _logisticsNetworks.Values.Where(network =>
                     network.Id != EntityId.None))
        {
            if (network.AssignedHaulerIds.Any(actorId =>
                    !_actors.ContainsKey(actorId) || !specialistHaulers.Add(actorId)) ||
                network.SourceStorageZoneIds.Any(zoneId => !_storageZones.ContainsKey(zoneId)))
            {
                throw new InvalidDataException(
                    $"Logistics network {network.Id} has invalid or duplicate assignments.");
            }
        }
    }

    private void ValidateNextEntityId()
    {
        var maximumId = _actors.Keys
            .Concat(_itemStacks.Keys)
            .Concat(_storageZones.Keys)
            .Concat(_storageAreas.Keys)
            .Concat(_logisticsNetworks.Keys.Where(id => id != EntityId.None))
            .Concat(_workDesignations.Keys)
            .Concat(_constructionSites.Keys)
            .Concat(_craftingOrders.Keys)
            .Concat(_goblinBuds.Keys)
            .Concat(_corpses.Keys)
            .Select(id => id.Value)
            .DefaultIfEmpty(0UL)
            .Max();

        if (_nextEntityId <= maximumId)
        {
            throw new InvalidDataException("The next entity identifier is not ahead of existing entities.");
        }
    }

    private void LoadPendingCommands(IEnumerable<CommandSaveModel> commandModels)
    {
        foreach (var model in commandModels)
        {
            var command = new SimulationCommand(
                new SimulationTick(model.ExecuteAt),
                model.Sequence,
                model.Kind,
                new EntityId(model.Subject),
                new EntityId(model.Target),
                new GridPosition(model.X, model.Y, model.Z),
                new GridPosition(model.EndX, model.EndY, model.EndZ),
                model.Construction,
                model.Resource,
                model.Amount,
                model.Text ?? string.Empty,
                model.MaterialVariant);

            ValidateCommandForQueue(command);
            var key = new CommandKey(command.ExecuteAt, command.Sequence);
            if (!_pendingCommands.TryAdd(key, command))
            {
                throw new InvalidDataException($"The save contains duplicate command key {key}.");
            }
        }
    }

    private void LoadUndeliveredEvents(IEnumerable<EventSaveModel> eventModels)
    {
        ulong previousEventSequence = 0;
        foreach (var model in eventModels)
        {
            var simulationEvent = new SimulationEvent(
                model.Sequence,
                new SimulationTick(model.Tick),
                model.Kind,
                new EntityId(model.Subject),
                new EntityId(model.Target),
                model.Amount);

            if (simulationEvent.Sequence <= previousEventSequence ||
                simulationEvent.Sequence >= _nextEventSequence ||
                simulationEvent.Tick.Value < 0 ||
                simulationEvent.Tick.Value > CurrentTick.Value ||
                !Enum.IsDefined(simulationEvent.Kind))
            {
                throw new InvalidDataException("The save contains invalid undelivered events.");
            }

            _undeliveredEvents.Add(simulationEvent);
            previousEventSequence = simulationEvent.Sequence;
        }
    }

    private void LoadUndeliveredWorldChanges(IEnumerable<WorldChangeSaveModel> changeModels)
    {
        ulong previousVersion = 0;
        foreach (var model in changeModels)
        {
            var change = new WorldChangeEvent(
                model.Version,
                new SimulationTick(model.Tick),
                model.Kind,
                new GridPosition(model.X, model.Y, model.Z),
                model.Amount);

            if (change.Version == 0 ||
                change.Version <= previousVersion ||
                change.Version > World.Version ||
                change.Tick.Value < 0 ||
                change.Tick.Value > CurrentTick.Value ||
                !Enum.IsDefined(change.Kind) ||
                !IsAddressableMapPosition(change.Position) ||
                (change.Kind == WorldChangeKind.DoorToggled
                    ? change.Amount is not (0 or 1)
                    : change.Amount == 0) ||
                (change.Kind == WorldChangeKind.VegetationHarvested && change.Amount > 0) ||
                (change.Kind == WorldChangeKind.VegetationRegrown && change.Amount < 0))
            {
                throw new InvalidDataException("The save contains invalid world changes.");
            }

            _undeliveredWorldChanges.Add(change);
            previousVersion = change.Version;
        }
    }

    private void AdvanceOneTick()
    {
        var startedAt = Stopwatch.GetTimestamp();
        CurrentTick = CurrentTick.Next();

        MeasureStage(0, UpdateWorld);
        MeasureStage(1, ExecuteScheduledCommands);
        MeasureStage(2, UpdateActorJobs);
        MeasureStage(3, UpdateAnimals);
        MeasureStage(4, UpdateAutomaticDoors);
        MeasureStage(5, UpdateHumanVillage);
        MeasureStage(6, ResolveHumanCombat);
        MeasureStage(7, UpdateActors);
        MeasureStage(8, TryCompleteRaid);
        MeasureStage(9, UpdateVisibility);

        _ticksExecuted = checked(_ticksExecuted + 1);
        _lastTickStopwatchTicks = Stopwatch.GetTimestamp() - startedAt;
        _totalTickStopwatchTicks = checked(_totalTickStopwatchTicks + _lastTickStopwatchTicks);

        void MeasureStage(int index, Action action)
        {
            var stageStartedAt = Stopwatch.GetTimestamp();
            action();
            _lastTickStageStopwatchTicks[index] = Stopwatch.GetTimestamp() - stageStartedAt;
        }
    }

    private void UpdateWorld()
    {
        UpdateBloodStains();
        var calendar = SimulationCalendar.At(CurrentTick, Definitions.Clock);
        if (calendar.IsDayStart)
        {
            UpdateFoodSpoilage();
        }
        if (calendar.IsDayStart && calendar.DayOfSeason == 1)
        {
            _undeliveredWorldChanges.AddRange(
                World.RefreshSeasonalWildFood(CurrentTick, calendar.Season));
        }
        if (_undergroundFactions.HasFactions)
        {
            var deepestGoblinLevel = _actors.Values
                .Where(actor => actor.Health > 0)
                .Select(actor => actor.Position.Z)
                .DefaultIfEmpty(0)
                .Min();
            _undergroundFactions.Advance(
                deepestGoblinLevel,
                SimulationCalendar.At(CurrentTick, Definitions.Clock).AbsoluteDay);
        }
        if (CurrentTick.Value % Definitions.PlantGrowthIntervalTicks == 0)
        {
            _undeliveredWorldChanges.AddRange(
                World.GrowPlants(
                    CurrentTick,
                    Definitions.PlantGrowthPerInterval,
                    calendar.Season,
                    Definitions.FishRegrowth));
        }
    }

    private void UpdateAutomaticDoors()
    {
        foreach (var position in World.GetAutomaticallyOpenedDoorPositions())
        {
            if (CanCloseWoodenDoor(position))
            {
                _undeliveredWorldChanges.Add(
                    World.CloseAutomaticallyOpenedDoor(position, CurrentTick));
            }
        }
    }

    private void UpdateVisibility()
    {
        var calendar = SimulationCalendar.At(CurrentTick, Definitions.Clock);
        var goblinObservers = _actors.Values
            .Select(actor => (
                actor.Position,
                WorldVisibilityPolicy.ResolveGoblinVisionRadius(
                    GoblinPerception,
                    actor.Position,
                    calendar.IsNight)))
            .ToArray();
        var observers = goblinObservers.ToList();
        var verticalPassages = World.CreateVerticalPassageSnapshot();
        observers.AddRange(GoblinStructureObserverPolicy.SelectObservers(
            World.EnumerateWorldObjects(),
            GoblinPerception.StructureVisionRadius));
        if (DebugSettings.RevealFogFromNonPlayerUnits)
        {
            var humanRadius = calendar.IsNight
                ? HumanPerception.NightVisionRadius
                : HumanPerception.DayVisionRadius;
            observers.AddRange(_humanVillage.GetLivingCohortPositions()
                .Select(position => (position, humanRadius)));
            observers.AddRange(_animals.Values.Select(animal =>
                (animal.Position, AnimalSpecies.Get(animal.Kind).DebugVisionRadius)));
        }

        Visibility.Reveal(observers, World.IsSolidHillRock);
        Visibility.Discover(
            WorldVisibilityPolicy.SelectAdjacentLayerDiscoveries(
                verticalPassages,
                Visibility.Get),
            World.IsSolidHillRock);
        Visibility.Discover(
            WorldVisibilityPolicy.SelectEdgeLookDiscoveries(
                goblinObservers,
                World.IsTerrainTraversable,
                World.IsOpenUnsupportedVolume,
                Map.IsWorldPosition),
            World.IsSolidHillRock);
        RemoveMiningDesignationsBlockedByRevealedFluid();
        foreach (var actor in _actors.Values.Where(actor =>
                     actor.JobKind == ActorJobKind.Explore &&
                     Visibility.Get(actor.JobTarget) != CellVisibility.Unknown))
        {
            actor.ClearJob();
        }
    }

    private void RemoveMiningDesignationsBlockedByRevealedFluid()
    {
        var blocked = _workDesignations.Values
            .Where(designation =>
                designation.Kind == WorkDesignationKind.MineRock &&
                Visibility.Get(designation.Target).IsDiscovered() &&
                !World.IsSolidRock(designation.Target) &&
                !World.IsTerrainRampIntact(designation.Target) &&
                World.TryGetFluid(designation.Target, out _, out _) &&
                World.GetCardinalWorldNeighbors(designation.Target)
                    .Any(World.IsTerrainTraversable))
            .ToArray();
        CancelActorsForDesignations(blocked);
        foreach (var designation in blocked)
        {
            World.TryGetFluid(designation.Target, out var fluid, out _);
            _workDesignations.Remove(designation.Id);
            Publish(
                SimulationEventKind.MiningHazardDiscovered,
                EntityId.None,
                designation.Id,
                (int)fluid);
        }
    }

    private void ExecuteScheduledCommands()
    {
        while (_pendingCommands.Count > 0)
        {
            var first = _pendingCommands.First();
            if (first.Key.Tick != CurrentTick)
            {
                break;
            }

            _pendingCommands.Remove(first.Key);
            ExecuteValidatedCommand(first.Value);
        }

        UpdateAutomaticCooking();
    }

    private void ExecuteValidatedCommand(SimulationCommand command)
    {
        if (!TryExecute(command))
        {
            Publish(
                SimulationEventKind.CommandRejected,
                command.Subject,
                command.Target,
                (int)command.Kind);
        }

        _commandsExecuted = checked(_commandsExecuted + 1);
        _lastCommandExecutionTick = CurrentTick.Value;
    }

    private bool TryExecute(SimulationCommand command) => command.Kind switch
    {
        SimulationCommandKind.Forage => TryExecuteForage(command),
        SimulationCommandKind.CreateStorageZone => TryExecuteCreateStorageZone(command),
        SimulationCommandKind.PickUp => TryExecutePickUp(command),
        SimulationCommandKind.StoreCarried => TryExecuteStoreCarried(command),
        SimulationCommandKind.Move => TryExecuteMove(command),
        SimulationCommandKind.OrderActorFlee => TryExecuteActorFlee(command),
        SimulationCommandKind.OrderActorSleep => TryExecuteActorSleep(command),
        SimulationCommandKind.SuspendActorDispatcher =>
            TryExecuteSuspendActorDispatcher(command),
        SimulationCommandKind.EquipItem => TryExecuteEquipItem(command),
        SimulationCommandKind.PrioritizeItemHauling =>
            TryExecutePrioritizeItemHauling(command),
        SimulationCommandKind.OrderItemPickup => TryExecuteOrderItemPickup(command),
        SimulationCommandKind.Build => TryExecuteBuild(command),
        SimulationCommandKind.DesignateWork => TryExecuteDesignateWork(command),
        SimulationCommandKind.ClearWorkDesignations => TryExecuteClearWork(command),
        SimulationCommandKind.ClearWorkDesignationOrder => TryExecuteClearWorkOrder(command),
        SimulationCommandKind.ConfigureWorkPriority => TryExecuteConfigureWorkPriority(command),
        SimulationCommandKind.ConfigureWorkSuspension =>
            TryExecuteConfigureWorkSuspension(command),
        SimulationCommandKind.ConfigureStoragePull => TryExecuteConfigureStoragePull(command),
        SimulationCommandKind.ConfigureStorageHauler => TryExecuteConfigureStorageHauler(command),
        SimulationCommandKind.ConfigureStorageSource => TryExecuteConfigureStorageSource(command),
        SimulationCommandKind.ConfigureStoragePriority => TryExecuteConfigureStoragePriority(command),
        SimulationCommandKind.ConfigureResourcePriority => TryExecuteConfigureResourcePriority(command),
        SimulationCommandKind.ConfigureWorkTypePriority =>
            TryExecuteConfigureWorkTypePriority(command),
        SimulationCommandKind.ConfigureConstructionPriority =>
            TryExecuteConfigureConstructionPriority(command),
        SimulationCommandKind.ConfigureStorageMineralFilter =>
            TryExecuteConfigureStorageMineralFilter(command),
        SimulationCommandKind.CreateLogisticsNetwork => TryExecuteCreateLogisticsNetwork(),
        SimulationCommandKind.ConfigureLogisticsHauler =>
            TryExecuteConfigureLogisticsHauler(command),
        SimulationCommandKind.ConfigureLogisticsSource =>
            TryExecuteConfigureLogisticsSource(command),
        SimulationCommandKind.ConfigureStorageNetwork =>
            TryExecuteConfigureStorageNetwork(command),
        SimulationCommandKind.CreateStorageArea => TryExecuteCreateStorageArea(command),
        SimulationCommandKind.ConfigureStorageAreaNetwork =>
            TryExecuteConfigureStorageAreaNetwork(command),
        SimulationCommandKind.RenameLogisticsNetwork =>
            TryExecuteRenameLogisticsNetwork(command),
        SimulationCommandKind.RenameStorageArea => TryExecuteRenameStorageArea(command),
        SimulationCommandKind.ConfigureStorageFilter =>
            TryExecuteConfigureStorageFilter(command),
        SimulationCommandKind.DeleteLogisticsNetwork =>
            TryExecuteDeleteLogisticsNetwork(command),
        SimulationCommandKind.ResizeStorageArea => TryExecuteResizeStorageArea(command),
        SimulationCommandKind.DissolveStorageArea => TryExecuteDissolveStorageArea(command),
        SimulationCommandKind.ConfigureStorageFilterResource =>
            TryExecuteConfigureStorageFilterResource(command),
        SimulationCommandKind.CancelConstruction => TryExecuteCancelConstruction(command),
        SimulationCommandKind.DismantleConstruction => TryExecuteDismantleConstruction(command),
        SimulationCommandKind.AttackHumanVillage => TryExecuteAttackHumanVillage(command),
        SimulationCommandKind.ConfigureRaidMember => TryExecuteConfigureRaidMember(command),
        SimulationCommandKind.SuspendRaidPreparation => TryExecuteSuspendRaidPreparation(),
        SimulationCommandKind.LaunchRaid => TryExecuteLaunchRaid(),
        SimulationCommandKind.ConfigureRaidTarget => TryExecuteConfigureRaidTarget(command),
        SimulationCommandKind.ConfigureRaidDirectives => TryExecuteConfigureRaidDirectives(command),
        SimulationCommandKind.ConfigureCorpseDirectives =>
            TryExecuteConfigureCorpseDirectives(command),
        SimulationCommandKind.OrderPatrol => TryExecuteOrderPatrol(command),
        SimulationCommandKind.OrderAttackArea => TryExecuteAreaOrder(
            command, ActorTacticalOrderKind.AttackArea),
        SimulationCommandKind.OrderHuntArea => TryExecuteAreaOrder(
            command, ActorTacticalOrderKind.HuntArea),
        SimulationCommandKind.DesignateHuntArea => TryExecuteDesignateHuntArea(command),
        SimulationCommandKind.ToggleWoodenDoor => TryExecuteToggleWoodenDoor(command),
        SimulationCommandKind.QueueCraftingOrder => TryExecuteQueueCraftingOrder(command),
        SimulationCommandKind.ConfigureRepeatingCraftingOrder =>
            TryExecuteConfigureRepeatingCraftingOrder(command),
        _ => false,
    };

    private bool TryExecuteCancelConstruction(SimulationCommand command)
    {
        if (!_constructionSites.TryGetValue(command.Target, out var selectedSite))
        {
            return false;
        }

        var sites = _constructionSites.Values
            .Where(site => site.OrderId == selectedSite.OrderId)
            .OrderBy(site => site.SequenceIndex)
            .ToArray();
        var siteIds = sites.Select(site => site.Id).ToHashSet();
        foreach (var actor in _actors.Values.Where(actor =>
                     actor.JobKind is ActorJobKind.SupplyConstruction or
                         ActorJobKind.BuildConstruction or ActorJobKind.ClearConstructionSite &&
                     siteIds.Contains(actor.DestinationZoneId)))
        {
            if (actor.CarriedStackId != EntityId.None)
            {
                DropCarriedStack(actor);
            }
            actor.ClearJob();
        }

        foreach (var site in sites)
        {
            if (site.DeliveredQuantity > 0)
            {
                AddGroundStack(
                    site.RequiredResource,
                    site.DeliveredQuantity,
                    site.Anchor,
                    site.DeliveredVariant != ResourceVariant.None
                        ? site.DeliveredVariant
                        : site.RequiredVariant);
            }
            _constructionSites.Remove(site.Id);
        }

        Publish(
            SimulationEventKind.ConstructionCancelled,
            EntityId.None,
            selectedSite.OrderId,
            sites.Length,
            selectedSite.Kind);
        return true;
    }

    private bool TryExecuteDismantleConstruction(SimulationCommand command)
    {
        var targetKind = (DismantleTargetKind)command.Amount;
        if (targetKind == DismantleTargetKind.StorageZone)
        {
            if (!_storageZones.TryGetValue(command.Target, out var zone))
            {
                return false;
            }

            return QueueDismantlingDesignation(
                WorkDesignationKind.DismantleStorageZone,
                zone.Position,
                zone.Id);
        }

        if (targetKind != DismantleTargetKind.WorldObject)
        {
            return false;
        }

        var id = new WorldObjectId(command.Target.Value);
        var worldObject = World.CreateWorldObjectSnapshot()
            .FirstOrDefault(candidate => candidate.Id == id);
        if (worldObject is null || !World.CanDismantleWorldObject(id) ||
            (worldObject.Kind == WorldObjectKind.GoblinFieldCamp &&
             _raidRallyPoint == worldObject.Anchor &&
             _raidPhase != GoblinRaidPhase.None))
        {
            return false;
        }

        return ConstructionDismantlingPolicy.TryGetConstructionKind(
                worldObject.Kind,
                out _) &&
            QueueDismantlingDesignation(
                WorkDesignationKind.DismantleWorldObject,
                worldObject.Anchor,
                command.Target);
    }

    private bool QueueDismantlingDesignation(
        WorkDesignationKind kind,
        GridPosition position,
        EntityId targetId)
    {
        if (_workDesignations.Values.Any(designation =>
                designation.Kind == kind && designation.TargetEntityId == targetId))
        {
            return false;
        }

        var id = AllocateEntityId();
        _workDesignations.Add(id, new WorkDesignationSnapshot(id, kind, position, targetId));
        Publish(SimulationEventKind.WorkDesignationCreated, EntityId.None, id, (int)kind);
        return true;
    }

    private bool TryDismantleStorageZone(EntityId zoneId, bool publishEvent)
    {
        if (!_storageZones.TryGetValue(zoneId, out var zone))
        {
            return false;
        }

        var storedStackIds = _itemStacks.Values
            .Where(stack => stack.Location.Kind == ItemLocationKind.StorageZone &&
                stack.Location.OwnerId == zone.Id)
            .Select(stack => stack.Id)
            .ToHashSet();
        foreach (var actor in _actors.Values.Where(actor =>
                     actor.DestinationZoneId == zone.Id ||
                     storedStackIds.Contains(actor.SourceStackId)))
        {
            if (actor.CarriedStackId != EntityId.None)
            {
                DropCarriedStack(actor);
            }
            actor.ClearJob();
        }
        foreach (var stackId in storedStackIds)
        {
            MoveItemStack(_itemStacks[stackId], ItemLocation.OnGround(zone.Position));
        }

        foreach (var dependent in _storageZones.Values.Where(candidate =>
                     candidate.SourceStorageZoneId == zone.Id))
        {
            dependent.SourceStorageZoneId = EntityId.None;
            IndexStorageZone(dependent);
        }
        foreach (var network in _logisticsNetworks.Values)
        {
            network.SourceStorageZoneIds.Remove(zone.Id);
        }

        _storageZones.Remove(zone.Id);
        _resourceSpatialIndex.RemoveStorageNode(zone.Id);
        if (zone.StorageAreaId == zone.Id &&
            !_storageZones.Values.Any(candidate => candidate.StorageAreaId == zone.StorageAreaId))
        {
            _storageAreas.Remove(zone.StorageAreaId);
        }
        if (publishEvent)
        {
            Publish(
                SimulationEventKind.ConstructionDismantled,
                EntityId.None,
                zone.Id,
                1);
        }
        return true;
    }

    private void CancelCraftingOrdersAt(GridPosition workshop)
    {
        var orders = _craftingOrders.Values
            .Where(order => order.Workshop == workshop)
            .ToArray();
        var orderIds = orders.Select(order => order.Id).ToHashSet();
        foreach (var actor in _actors.Values.Where(actor =>
                     actor.JobKind is ActorJobKind.SupplyCrafting or ActorJobKind.Craft &&
                     orderIds.Contains(actor.DestinationZoneId)))
        {
            if (actor.CarriedStackId != EntityId.None)
            {
                DropCarriedStack(actor);
            }
            actor.ClearJob();
        }
        foreach (var order in orders)
        {
            foreach (var material in order.DeliveredMaterials)
            {
                AddGroundStack(
                    material.Resource,
                    material.Quantity,
                    workshop,
                    material.Variant,
                    material.FoodKind,
                    material.FreshUntilTick);
            }
            _craftingOrders.Remove(order.Id);
        }
    }

    private void AddGroundStack(
        ResourceKind resource,
        int quantity,
        GridPosition position,
        ResourceVariant variant,
        FoodKind foodKind = FoodKind.None,
        long? freshUntilTick = null)
    {
        var existing = FindMergeableGroundStack(
            resource,
            position,
            foodKind,
            variant);
        if (existing is not null)
        {
            existing.Quantity = checked(existing.Quantity + quantity);
            if (existing.FreshUntilTick is { } existingFreshUntil &&
                freshUntilTick is { } incomingFreshUntil)
            {
                existing.FreshUntilTick = Math.Min(existingFreshUntil, incomingFreshUntil);
            }
            return;
        }
        AllocateItemStack(
            resource,
            quantity,
            ItemLocation.OnGround(position),
            foodKind,
            variant,
            freshUntilTick);
    }

    private bool TryExecuteQueueCraftingOrder(SimulationCommand command)
    {
        var recipe = (CraftingRecipeKind)command.Amount;
        if (!Enum.IsDefined(recipe))
        {
            return false;
        }

        CancelAutomaticCraftingOrdersAt(command.Position);
        return TryCreateCraftingOrder(command.Position, recipe, isRepeating: false);
    }

    private bool TryExecuteConfigureRepeatingCraftingOrder(SimulationCommand command)
    {
        var enabled = command.Amount > 0;
        var recipe = (CraftingRecipeKind)Math.Abs(command.Amount);
        if (!Enum.IsDefined(recipe))
        {
            return false;
        }

        if (enabled)
        {
            CancelAutomaticCraftingOrdersAt(command.Position);
        }

        var existing = _craftingOrders.Values.FirstOrDefault(order =>
            order.Workshop == command.Position &&
            order.Recipe == recipe &&
            order.IsRepeating);
        if (!enabled)
        {
            if (existing is not null)
            {
                CancelCraftingOrder(existing);
            }
            return true;
        }
        return existing is not null || TryCreateCraftingOrder(
            command.Position,
            recipe,
            isRepeating: true);
    }

    private bool TryCreateCraftingOrder(
        GridPosition position,
        CraftingRecipeKind recipe,
        bool isRepeating,
        bool isAutomatic = false)
    {
        var recipeDefinition = CraftingRecipeCatalog.Get(recipe);
        var workshop = WorkshopCatalog.Get(recipeDefinition.Workshop);
        if (!workshop.SupportsRecipe(recipe, CraftingRecipeCatalog.GetRecipeLevel(recipe)) ||
            !World.HasWorkshop(position, recipeDefinition.Workshop))
        {
            return false;
        }

        var id = AllocateEntityId();
        var order = new CraftingOrderState(
            id,
            recipe,
            position,
            deliveredMaterials: [],
            CraftingRecipeCatalog.GetWorkTicks(recipe),
            isRepeating,
            isAutomatic);
        _craftingOrders.Add(id, order);
        Publish(SimulationEventKind.CraftingOrdered, EntityId.None, id, (int)recipe);
        return true;
    }

    private void CancelCraftingOrder(CraftingOrderState order)
    {
        foreach (var actor in _actors.Values.Where(actor =>
                     actor.DestinationZoneId == order.Id &&
                     actor.JobKind is ActorJobKind.SupplyCrafting or ActorJobKind.Craft))
        {
            if (actor.CarriedStackId != EntityId.None)
            {
                DropCarriedStack(actor);
            }
            actor.ClearJob();
        }
        foreach (var material in order.DeliveredMaterials)
        {
            AddGroundStack(
                material.Resource,
                material.Quantity,
                order.Workshop,
                material.Variant,
                material.FoodKind,
                material.FreshUntilTick);
        }
        _craftingOrders.Remove(order.Id);
    }

    private bool TryExecuteToggleWoodenDoor(SimulationCommand command)
    {
        if (!World.TryGetWoodenDoorState(command.Position, out var isOpen))
        {
            return false;
        }

        if (isOpen && !CanCloseWoodenDoor(command.Position))
        {
            return false;
        }

        _undeliveredWorldChanges.Add(World.ToggleWoodenDoor(command.Position, CurrentTick));
        return true;
    }

    private bool CanCloseWoodenDoor(GridPosition position) =>
        !_actors.Values.Any(actor =>
            actor.Position == position ||
            actor.RemainingRoute.Count > 0 && actor.RemainingRoute[0] == position) &&
        !_humanVillage.GetLivingCohortPositions().Contains(position) &&
        !_itemStacks.Values.Any(stack =>
            stack.Location.Kind == ItemLocationKind.Ground &&
            stack.Location.Position == position) &&
        !_storageZones.Values.Any(zone => zone.Position == position);

    private bool TryExecuteAttackHumanVillage(SimulationCommand command)
    {
        if (_humanVillage.GoblinAttackOrdered ||
            _raidPhase is not (GoblinRaidPhase.None or GoblinRaidPhase.Suspended))
        {
            return false;
        }

        _raidPartyIds.RemoveWhere(id =>
            !_actors.TryGetValue(id, out var actor) || actor.Health <= 0 || IsJuvenile(actor));
        if (_raidPartyIds.Count == 0 && !_raidRosterConfigured)
        {
            foreach (var actor in _actors.Values
                         .Where(actor => actor.Health > 0 && !IsJuvenile(actor))
                         .OrderBy(actor => actor.Id.Value)
                         .Take(SimulationDefinitions.FieldCampCapacity))
            {
                _raidPartyIds.Add(actor.Id);
            }
        }
        if (_raidPartyIds.Count == 0)
        {
            return false;
        }
        _raidRosterConfigured = true;

        var rally = World.CreateWorldObjectSnapshot()
            .Where(item =>
                item.Kind == WorldObjectKind.GoblinFieldCamp &&
                item.Owner == WorldObjectOwner.GoblinTribe)
            .Select(item => item.Anchor)
            .Where(position => command.Position == default || position == command.Position)
            .Where(World.IsTerrainTraversable)
            .Select(position => new
            {
                Position = position,
                Route = FindTribePath(position, _raidTarget),
            })
            .Where(item => item.Route is not null)
            .OrderBy(item => item.Route!.Count)
            .ThenBy(item => item.Position.Y)
            .ThenBy(item => item.Position.X)
            .FirstOrDefault();
        if (rally is null)
        {
            return false;
        }

        _raidPhase = GoblinRaidPhase.Preparing;
        _raidRallyPoint = rally.Position;
        var raidParty = GetRaidParty();
        foreach (var actor in raidParty)
        {
            if (actor.CarriedStackId == EntityId.None && actor.JobKind != ActorJobKind.Haul)
            {
                actor.ClearJob();
            }
        }
        Publish(SimulationEventKind.RaidPreparationStarted, EntityId.None, EntityId.None, raidParty.Count);
        return true;
    }

    private bool TryExecuteSuspendRaidPreparation()
    {
        if (_raidPhase is not (GoblinRaidPhase.Preparing or GoblinRaidPhase.Ready or
            GoblinRaidPhase.Marching))
        {
            return false;
        }

        var wasMarching = _raidPhase == GoblinRaidPhase.Marching;
        if (wasMarching)
        {
            _humanVillage.EndGoblinAttack();
        }
        _raidPhase = GoblinRaidPhase.Suspended;
        foreach (var actor in GetRaidParty())
        {
            if (wasMarching && actor.JobKind == ActorJobKind.Move &&
                actor.JobTarget == _raidTarget)
            {
                actor.ClearJob();
                actor.ClearSuspendedJob();
                continue;
            }
            if (actor.CarriedStackId == EntityId.None)
            {
                actor.ClearJob();
            }
        }
        return true;
    }

    private bool TryExecuteLaunchRaid()
    {
        if (_raidPhase != GoblinRaidPhase.Ready)
        {
            return false;
        }

        var raidParty = GetRaidParty();
        if (raidParty.Count == 0)
        {
            return false;
        }

        var routes = raidParty
            .Select(actor => (Actor: actor, Route: FindActorPath(actor, _raidTarget)))
            .ToArray();
        if (routes.Any(item => item.Route is not { Count: > 0 }))
        {
            return false;
        }

        _raidPhase = GoblinRaidPhase.Marching;
        _humanVillage.OrderGoblinAttack();
        foreach (var (actor, route) in routes)
        {
            actor.ClearJob();
            actor.JobKind = ActorJobKind.Move;
            actor.JobPhase = ActorJobPhase.Traveling;
            actor.JobTarget = _raidTarget;
            actor.RemainingRoute.AddRange(route!);
            Publish(SimulationEventKind.MoveOrdered, actor.Id, EntityId.None, route!.Count);
        }
        Publish(SimulationEventKind.RaidDeparted, EntityId.None, EntityId.None, raidParty.Count);
        return true;
    }

    private bool TryExecuteConfigureRaidTarget(SimulationCommand command)
    {
        if (_raidPhase == GoblinRaidPhase.Marching ||
            !IsAddressableMapPosition(command.Position) ||
            command.Amount is < MinimumRaidTargetRadius or > MaximumRaidTargetRadius)
        {
            return false;
        }

        _raidTarget = command.Position;
        _raidTargetRadius = command.Amount;
        InvalidateReadyRaidPlan();
        return true;
    }

    private bool TryExecuteConfigureRaidDirectives(SimulationCommand command)
    {
        var directives = (RaidDirective)command.Amount;
        if (!AreValidRaidDirectives(directives))
        {
            return false;
        }

        _raidDirectives = directives;
        InvalidateReadyRaidPlan();
        return true;
    }

    private bool TryExecuteConfigureCorpseDirectives(SimulationCommand command)
    {
        var directives = (CorpseDirective)command.Amount;
        if (!_corpses.TryGetValue(command.Target, out var corpse) ||
            !AreValidCorpseDirectives(directives))
        {
            return false;
        }

        corpse.Directives = directives;
        return true;
    }

    private bool TryExecuteConfigureRaidMember(SimulationCommand command)
    {
        if (_raidPhase == GoblinRaidPhase.Marching ||
            !_actors.TryGetValue(command.Subject, out var actor) ||
            actor.Health <= 0 ||
            IsJuvenile(actor))
        {
            return false;
        }

        _raidRosterConfigured = true;
        if (command.Amount == 0)
        {
            _raidPartyIds.Remove(actor.Id);
            if (_raidPartyIds.Count == 0 && _raidPhase is GoblinRaidPhase.Preparing or
                    GoblinRaidPhase.Ready or GoblinRaidPhase.Suspended)
            {
                var previousRallyPoint = _raidRallyPoint;
                _raidPhase = GoblinRaidPhase.None;
                _raidRallyPoint = default;
                foreach (var candidate in _actors.Values.Where(candidate =>
                             candidate.JobKind == ActorJobKind.Move &&
                             candidate.JobTarget == previousRallyPoint))
                {
                    candidate.ClearJob();
                }
            }
            InvalidateReadyRaidPlan();
            return true;
        }
        if (_raidPartyIds.Contains(actor.Id))
        {
            return true;
        }
        if (_raidPartyIds.Count >= SimulationDefinitions.FieldCampCapacity)
        {
            return false;
        }

        _raidPartyIds.Add(actor.Id);
        InvalidateReadyRaidPlan();
        return true;
    }

    private void InvalidateReadyRaidPlan()
    {
        if (_raidPhase == GoblinRaidPhase.Ready)
        {
            _raidPhase = GoblinRaidPhase.Preparing;
        }
    }

    private void ValidateLoadedRaidState()
    {
        if (!Enum.IsDefined(_raidPhase) ||
            !IsAddressableMapPosition(_raidTarget) ||
            _raidTargetRadius is < MinimumRaidTargetRadius or > MaximumRaidTargetRadius ||
            !AreValidRaidDirectives(_raidDirectives) ||
            (_raidPhase == GoblinRaidPhase.None &&
             (_raidRallyPoint != default || _humanVillage.GoblinAttackOrdered)) ||
            (_raidPhase != GoblinRaidPhase.None &&
             (!World.IsTerrainTraversable(_raidRallyPoint) ||
              !World.GetWorldObjectsAt(_raidRallyPoint).Any(item =>
                  item.Kind == WorldObjectKind.GoblinFieldCamp &&
                  item.Owner == WorldObjectOwner.GoblinTribe))) ||
            (_raidPhase == GoblinRaidPhase.Marching && !_humanVillage.GoblinAttackOrdered) ||
            (_raidPhase != GoblinRaidPhase.Marching && _humanVillage.GoblinAttackOrdered))
        {
            throw new InvalidDataException("The save contains invalid goblin raid state.");
        }
    }

    private static bool AreValidRaidDirectives(RaidDirective directives)
    {
        const RaidDirective all = RaidDirective.AttackGuards |
            RaidDirective.AttackAll |
            RaidDirective.LootEquipment |
            RaidDirective.LootSupplies |
            RaidDirective.LootFood |
            RaidDirective.ConsumeCorpses |
            RaidDirective.BudCorpses |
            RaidDirective.BurnBuildings |
            RaidDirective.DemolishBuildings |
            RaidDirective.ContinueWhileTargetsVisible |
            RaidDirective.AutoLaunchWhenReady |
            RaidDirective.RecoverCorpses |
            RaidDirective.BudCorpsesInPlace;
        var engagement = directives &
            (RaidDirective.AttackGuards | RaidDirective.AttackAll);
        var corpseHandling = directives &
            (RaidDirective.RecoverCorpses | RaidDirective.BudCorpses |
                RaidDirective.BudCorpsesInPlace);
        return (directives & ~all) == 0 &&
            engagement is RaidDirective.AttackGuards or RaidDirective.AttackAll &&
            corpseHandling is RaidDirective.None or RaidDirective.RecoverCorpses or
                RaidDirective.BudCorpses or RaidDirective.BudCorpsesInPlace;
    }

    private void RestoreLegacyRaidPartyIfNeeded()
    {
        if (_raidPhase == GoblinRaidPhase.None || _raidPartyIds.Count > 0)
        {
            return;
        }

        foreach (var actor in _actors.Values
                     .Where(actor => actor.Health > 0 && !IsJuvenile(actor))
                     .OrderBy(actor => actor.Id.Value)
                     .Take(SimulationDefinitions.FieldCampCapacity))
        {
            _raidPartyIds.Add(actor.Id);
        }
        _raidRosterConfigured = _raidPartyIds.Count > 0;
    }

    private void ValidateLoadedRaidParty()
    {
        if (_raidPartyIds.Count > SimulationDefinitions.FieldCampCapacity ||
            (_raidPhase != GoblinRaidPhase.None && _raidPartyIds.Count == 0) ||
            _raidPartyIds.Any(id =>
                !_actors.TryGetValue(id, out var actor) || actor.Health <= 0 || IsJuvenile(actor)))
        {
            throw new InvalidDataException("The save contains an invalid raid party.");
        }
    }

    public IReadOnlyList<GridPosition> QueryWorkDesignationTargets(
        WorkDesignationKind kind,
        GridPosition start,
        GridPosition end)
    {
        if (start.Z != end.Z || !Map.IsColumnWithin(start) || !Map.IsColumnWithin(end))
        {
            return [];
        }

        var (minimum, maximum) = NormalizeArea(start, end);
        return ResolveWorkDesignationTargets(kind, minimum, maximum)
            .Select(target => target.Position)
            .Distinct()
            .OrderBy(position => position.Y)
            .ThenBy(position => position.X)
            .ToArray();
    }

    private IEnumerable<(GridPosition Position, EntityId TargetEntityId)>
        ResolveWorkDesignationTargets(
            WorkDesignationKind kind,
            GridPosition minimum,
            GridPosition maximum)
    {
        if (TerrainModificationCatalog.TryGet(kind, out var terrain))
        {
            return TerrainModificationTargetPolicy.QueryApplicableTargets(
                    terrain,
                    World,
                    Visibility,
                    minimum,
                    maximum)
                .Select(position => (position, EntityId.None));
        }

        return kind switch
        {
            WorkDesignationKind.GatherFood => World.CreatePlantSnapshot()
                .Where(plant => plant.Kind != PlantKind.ReedBed && plant.Biomass > 0 &&
                    IsInside(plant.Position, minimum, maximum) &&
                    Visibility.Get(plant.Position) != CellVisibility.Unknown)
                .Select(plant => (plant.Position, EntityId.None)),
            WorkDesignationKind.GatherReeds => World.CreatePlantSnapshot()
                .Where(plant => plant.Kind == PlantKind.ReedBed && plant.Biomass > 0 &&
                    IsInside(plant.Position, minimum, maximum) &&
                    Visibility.Get(plant.Position) != CellVisibility.Unknown)
                .Select(plant => (plant.Position, EntityId.None)),
            WorkDesignationKind.GatherLichen => EnumerateAreaCells(minimum, maximum)
                .Where(position => Visibility.Get(position) != CellVisibility.Unknown &&
                    World.TryGetCaveFlora(position, out var flora) &&
                    flora.Kind == CaveFloraKind.LichenPatch)
                .Select(position => (position, EntityId.None)),
            WorkDesignationKind.GatherBrushwood => _itemStacks.Values
                .Where(stack => stack.Resource == ResourceKind.Wood &&
                    stack.Location.Kind == ItemLocationKind.Ground &&
                    IsInside(stack.Location.Position, minimum, maximum) &&
                    Visibility.Get(stack.Location.Position) != CellVisibility.Unknown)
                .Select(stack => (stack.Location.Position, stack.Id)),
            WorkDesignationKind.GatherStone => _itemStacks.Values
                .Where(stack => IsMineralResource(stack.Resource) &&
                    stack.Location.Kind == ItemLocationKind.Ground &&
                    IsInside(stack.Location.Position, minimum, maximum) &&
                    Visibility.Get(stack.Location.Position) != CellVisibility.Unknown)
                .Select(stack => (stack.Location.Position, stack.Id)),
            WorkDesignationKind.UprootBerryBush => World.CreatePlantSnapshot()
                .Where(plant => plant.Kind == PlantKind.BerryBush &&
                    IsInside(plant.Position, minimum, maximum) &&
                    Visibility.Get(plant.Position) != CellVisibility.Unknown)
                .Select(plant => (plant.Position, EntityId.None)),
            WorkDesignationKind.FellTree => World.CreateWorldObjectSnapshot()
                .Where(worldObject =>
                    worldObject.Kind is WorldObjectKind.Tree or WorldObjectKind.DeadTreeStump)
                .Select(worldObject => World.GetNaturalObjectSurfacePosition(worldObject))
                .Where(position => IsInside(position, minimum, maximum) &&
                    Visibility.Get(position) != CellVisibility.Unknown)
                .Select(position => (position, EntityId.None)),
            WorkDesignationKind.QuarryBoulder => World.CreateWorldObjectSnapshot()
                .Where(worldObject => worldObject.Kind == WorldObjectKind.Boulder)
                .Select(worldObject => World.GetNaturalObjectSurfacePosition(worldObject))
                .Where(position => IsInside(position, minimum, maximum) &&
                    Visibility.Get(position) != CellVisibility.Unknown)
                .Select(position => (position, EntityId.None)),
            WorkDesignationKind.Scout =>
                (from y in Enumerable.Range(minimum.Y, maximum.Y - minimum.Y + 1)
                 from x in Enumerable.Range(minimum.X, maximum.X - minimum.X + 1)
                 let position = new GridPosition(x, y, minimum.Z)
                 where World.IsTerrainTraversable(position)
                 select (position, EntityId.None)),
            WorkDesignationKind.HuntAnimal => _animals.Values
                .Where(animal => IsInside(animal.Position, minimum, maximum) &&
                    Visibility.Get(animal.Position) != CellVisibility.Unknown)
                .Select(animal => (animal.Position, new EntityId(animal.Id))),
            WorkDesignationKind.CleanBlood => GetCleanableSurfacePositions()
                .Where(position => IsInside(position, minimum, maximum) &&
                    Visibility.Get(position) != CellVisibility.Unknown)
                .Select(position => (position, EntityId.None)),
            _ => [],
        };
    }

    private bool TryExecuteDesignateWork(SimulationCommand command)
    {
        var designationKindCode = command.Amount & 0xff;
        var priorityCode = (command.Amount >> 8) & 0xff;
        var isSuspended = (command.Amount & (1 << 16)) != 0;
        var priority = priorityCode == 0
            ? StoragePriority.Normal
            : (StoragePriority)(priorityCode - 1);
        var kind = designationKindCode switch
        {
            (int)WorkDesignationKind.FellTree => WorkDesignationKind.FellTree,
            (int)WorkDesignationKind.QuarryBoulder => WorkDesignationKind.QuarryBoulder,
            (int)WorkDesignationKind.MineRock => WorkDesignationKind.MineRock,
            (int)WorkDesignationKind.Scout => WorkDesignationKind.Scout,
            (int)WorkDesignationKind.HuntAnimal => WorkDesignationKind.HuntAnimal,
            (int)WorkDesignationKind.CarveRampDown => WorkDesignationKind.CarveRampDown,
            (int)WorkDesignationKind.CarveRampUp => WorkDesignationKind.CarveRampUp,
            (int)WorkDesignationKind.CleanBlood => WorkDesignationKind.CleanBlood,
            (int)WorkDesignationKind.GatherLichen => WorkDesignationKind.GatherLichen,
            _ => command.Resource switch
        {
            ResourceKind.Food => WorkDesignationKind.GatherFood,
            ResourceKind.Reeds => WorkDesignationKind.GatherReeds,
            ResourceKind.Wood => WorkDesignationKind.GatherBrushwood,
            ResourceKind.Stone => WorkDesignationKind.GatherStone,
            ResourceKind.Vegetation => WorkDesignationKind.UprootBerryBush,
            _ => default,
        },
        };
        if (kind == default)
        {
            return false;
        }
        if (command.Subject != EntityId.None &&
            !_workDesignations.Values.Any(designation =>
                designation.OrderId == command.Subject && designation.Kind == kind))
        {
            return false;
        }

        if (kind is WorkDesignationKind.CarveRampDown or WorkDesignationKind.CarveRampUp)
        {
            return TryExecuteDirectionalRampDesignation(
                command,
                kind,
                priority,
                isSuspended);
        }

        var (minimum, maximum) = NormalizeArea(command.Position, command.EndPosition);
        var targets = ResolveWorkDesignationTargets(kind, minimum, maximum);
        var concreteTargets = targets
            .OrderBy(item => ManhattanDistance(command.Position, item.Item1))
            .ThenBy(item => item.Item1.Z)
            .ThenBy(item => item.Item1.Y)
            .ThenBy(item => item.Item1.X)
            .ThenBy(item => item.Item2)
            .Where(item => !_workDesignations.Values.Any(existing =>
                existing.OrderId != command.Subject &&
                existing.Kind == kind && existing.Target == item.Item1 &&
                existing.TargetEntityId == item.Item2))
            .ToArray();
        if (concreteTargets.Length == 0)
        {
            return command.Subject == EntityId.None;
        }

        var orderId = command.Subject == EntityId.None
            ? AllocateEntityId()
            : command.Subject;
        if (command.Subject != EntityId.None)
        {
            RemoveWorkOrder(command.Subject);
        }
        foreach (var (position, targetEntityId) in concreteTargets)
        {
            var id = AllocateEntityId();
            _workDesignations.Add(id, new WorkDesignationSnapshot(id, kind, position, targetEntityId)
            {
                OrderId = orderId,
                Priority = priority,
                IsSuspended = isSuspended,
            });
            Publish(SimulationEventKind.WorkDesignationCreated, EntityId.None, id, (int)kind);
        }
        return true;
    }

    private bool TryExecuteDirectionalRampDesignation(
        SimulationCommand command,
        WorkDesignationKind kind,
        StoragePriority priority,
        bool isSuspended)
    {
        var destination = command.EndPosition == command.Position
            ? (GridPosition?)null
            : command.EndPosition;
        var definition = TerrainModificationCatalog.Get(kind);
        if (!TerrainModificationTargetPolicy.CanRetainDesignation(
                definition,
                World,
                Visibility,
                command.Position,
                destination) ||
            destination is { } rampDestination &&
            (rampDestination.X != command.Position.X ||
             rampDestination.Y != command.Position.Y) &&
            (!Visibility.TryGet(
                 rampDestination with { Z = command.Position.Z },
                 out var destinationVisibility) ||
             destinationVisibility == CellVisibility.Unknown) ||
            _workDesignations.Values.Any(existing =>
                existing.OrderId != command.Subject &&
                existing.Kind == kind &&
                existing.Target == command.Position &&
                existing.RampDestination == destination))
        {
            return false;
        }

        var orderId = command.Subject == EntityId.None
            ? AllocateEntityId()
            : command.Subject;
        if (command.Subject != EntityId.None)
        {
            RemoveWorkOrder(command.Subject);
        }
        var id = AllocateEntityId();
        _workDesignations.Add(id, new WorkDesignationSnapshot(
            id,
            kind,
            command.Position,
            EntityId.None)
        {
            OrderId = orderId,
            Priority = priority,
            IsSuspended = isSuspended,
            RampDestination = destination,
        });
        Publish(SimulationEventKind.WorkDesignationCreated, EntityId.None, id, (int)kind);
        return true;
    }

    private bool TryExecuteClearWork(SimulationCommand command)
    {
        var (minimum, maximum) = NormalizeArea(command.Position, command.EndPosition);
        var removed = _workDesignations.Values
            .Where(designation => IsInside(designation.Target, minimum, maximum))
            .Select(designation => designation.Id)
            .ToArray();
        var removedIds = removed.ToHashSet();
        foreach (var id in removed)
        {
            _workDesignations.Remove(id);
            Publish(SimulationEventKind.WorkDesignationRemoved, EntityId.None, id, 0);
        }

        CancelJobsInClearedArea(minimum, maximum, removedIds);
        CancelTacticalOrdersInArea(minimum, maximum);
        return true;
    }

    private bool TryExecuteClearWorkOrder(SimulationCommand command)
    {
        RemoveWorkOrder(command.Target);
        return true;
    }

    private void RemoveWorkOrder(EntityId orderId)
    {
        var removed = _workDesignations.Values
            .Where(designation => designation.OrderId == orderId)
            .ToArray();
        CancelActorsForDesignations(removed);
        foreach (var designation in removed)
        {
            _workDesignations.Remove(designation.Id);
            Publish(
                SimulationEventKind.WorkDesignationRemoved,
                EntityId.None,
                designation.Id,
                0);
        }
    }

    private void CancelActorsForDesignations(
        IReadOnlyList<WorkDesignationSnapshot> designations)
    {
        foreach (var actor in _actors.Values)
        {
            if (designations.Any(designation =>
                    ActorJobMatchesDesignation(actor, designation)))
            {
                actor.ClearJob();
                continue;
            }
            if (designations.Any(designation =>
                    SuspendedJobMatchesDesignation(actor, designation)))
            {
                actor.ClearSuspendedJob();
            }
        }
    }

    private bool SuspendedJobMatchesDesignation(
        ActorState actor,
        WorkDesignationSnapshot designation) => designation.Kind switch
    {
        WorkDesignationKind.GatherFood or WorkDesignationKind.GatherReeds or
            WorkDesignationKind.GatherLichen =>
            actor.SuspendedJobKind == ActorJobKind.Forage &&
            actor.SuspendedJobTarget == designation.Target,
        WorkDesignationKind.UprootBerryBush =>
            actor.SuspendedJobKind == ActorJobKind.ClearVegetation &&
            actor.SuspendedJobTarget == designation.Target,
        WorkDesignationKind.FellTree =>
            actor.SuspendedJobKind == ActorJobKind.FellTree &&
            AreCardinalNeighbors(actor.SuspendedJobTarget, designation.Target),
        WorkDesignationKind.QuarryBoulder =>
            actor.SuspendedJobKind == ActorJobKind.QuarryBoulder &&
            AreCardinalNeighbors(actor.SuspendedJobTarget, designation.Target),
        WorkDesignationKind.MineRock =>
            actor.SuspendedJobKind == ActorJobKind.MineRock &&
            World.GetCardinalWorldNeighbors(designation.Target)
                .Contains(actor.SuspendedJobTarget),
        WorkDesignationKind.CarveRampDown or WorkDesignationKind.CarveRampUp =>
            actor.SuspendedJobKind == ActorJobKind.CarveRamp &&
            actor.SuspendedJobTarget == designation.Target,
        WorkDesignationKind.Scout =>
            actor.SuspendedJobKind == ActorJobKind.Explore &&
            actor.SuspendedJobTarget == designation.Target,
        WorkDesignationKind.CleanBlood =>
            actor.SuspendedJobKind == ActorJobKind.CleanBlood &&
            actor.SuspendedJobTarget == designation.Target,
        _ => false,
    };

    private static bool ActorJobMatchesDesignation(
        ActorState actor,
        WorkDesignationSnapshot designation) => designation.Kind switch
    {
        WorkDesignationKind.GatherFood or WorkDesignationKind.GatherReeds or
            WorkDesignationKind.GatherLichen =>
            actor.JobKind == ActorJobKind.Forage && actor.JobTarget == designation.Target,
        WorkDesignationKind.GatherBrushwood or WorkDesignationKind.GatherStone =>
            actor.JobKind == ActorJobKind.Haul &&
            actor.CarriedStackId == EntityId.None &&
            actor.SourceStackId == designation.TargetEntityId,
        WorkDesignationKind.UprootBerryBush =>
            actor.JobKind == ActorJobKind.ClearVegetation && actor.JobTarget == designation.Target,
        WorkDesignationKind.FellTree =>
            actor.JobKind == ActorJobKind.FellTree && actor.SourceStackId == designation.Id,
        WorkDesignationKind.QuarryBoulder =>
            actor.JobKind == ActorJobKind.QuarryBoulder && actor.SourceStackId == designation.Id,
        WorkDesignationKind.MineRock =>
            actor.JobKind == ActorJobKind.MineRock && actor.SourceStackId == designation.Id,
        WorkDesignationKind.CarveRampDown or WorkDesignationKind.CarveRampUp =>
            actor.JobKind == ActorJobKind.CarveRamp && actor.SourceStackId == designation.Id,
        WorkDesignationKind.Scout =>
            actor.JobKind == ActorJobKind.Explore && actor.JobTarget == designation.Target,
        WorkDesignationKind.HuntAnimal =>
            actor.JobKind == ActorJobKind.HuntAnimal && actor.SourceStackId == designation.Id,
        WorkDesignationKind.CleanBlood =>
            actor.JobKind == ActorJobKind.CleanBlood && actor.SourceStackId == designation.Id,
        WorkDesignationKind.DismantleWorldObject or
            WorkDesignationKind.DismantleStorageZone =>
            actor.JobKind == ActorJobKind.DismantleConstruction &&
            actor.SourceStackId == designation.Id,
        _ => false,
    };

    private bool TryExecuteConfigureWorkPriority(SimulationCommand command)
    {
        var priority = (StoragePriority)command.Amount;
        var changed = false;
        foreach (var id in _workDesignations.Values
                     .Where(designation => designation.OrderId == command.Target)
                     .Select(designation => designation.Id)
                     .ToArray())
        {
            _workDesignations[id] = _workDesignations[id] with { Priority = priority };
            changed = true;
        }
        if (changed)
        {
            Publish(SimulationEventKind.WorkPriorityConfigured, EntityId.None, command.Target, command.Amount);
        }
        return changed;
    }

    private bool TryExecuteConfigureWorkSuspension(SimulationCommand command)
    {
        var isSuspended = command.Amount == 1;
        var designations = _workDesignations.Values
            .Where(designation => designation.OrderId == command.Target)
            .ToArray();
        if (designations.Length == 0)
        {
            return false;
        }
        if (isSuspended)
        {
            CancelActorsForDesignations(designations);
        }
        foreach (var designation in designations)
        {
            _workDesignations[designation.Id] = designation with
            {
                IsSuspended = isSuspended,
            };
        }
        Publish(
            SimulationEventKind.WorkSuspensionConfigured,
            EntityId.None,
            command.Target,
            command.Amount);
        return true;
    }

    private bool TryExecuteConfigureStoragePull(SimulationCommand command)
    {
        if (!_storageZones.TryGetValue(command.Target, out var zone) ||
            command.Amount < 0 || command.Amount > zone.Capacity)
        {
            return false;
        }

        zone.DesiredQuantity = command.Amount;
        WakeAssignedHauler(zone);
        foreach (var destination in _storageZones.Values.Where(destination =>
                     destination.SourceStorageZoneId == zone.Id))
        {
            WakeAssignedHauler(destination);
        }
        Publish(
            SimulationEventKind.StoragePullConfigured,
            EntityId.None,
            zone.Id,
            command.Amount);
        return true;
    }

    private bool TryExecuteConfigureStorageHauler(SimulationCommand command)
    {
        if (!_storageZones.TryGetValue(command.Target, out var zone) ||
            (command.Subject != EntityId.None && !_actors.ContainsKey(command.Subject)))
        {
            return false;
        }

        zone.AssignedHaulerId = command.Subject;
        WakeAssignedHauler(zone);
        Publish(
            SimulationEventKind.StorageHaulerConfigured,
            command.Subject,
            zone.Id,
            amount: 0);
        return true;
    }

    private bool TryExecuteConfigureStorageSource(SimulationCommand command)
    {
        if (!_storageZones.TryGetValue(command.Target, out var zone) ||
            (command.Subject != EntityId.None &&
             !TryGetCompatibleStorageSource(zone, command.Subject, out _)))
        {
            return false;
        }

        zone.SourceStorageZoneId = command.Subject;
        IndexStorageZone(zone);
        WakeAssignedHauler(zone);
        Publish(
            SimulationEventKind.StorageSourceConfigured,
            command.Subject,
            zone.Id,
            amount: 0);
        return true;
    }

    private bool TryExecuteCreateLogisticsNetwork()
    {
        var id = AllocateEntityId();
        var network = new LogisticsNetworkState(id, $"Network {id.Value}");
        _logisticsNetworks.Add(id, network);
        Publish(
            SimulationEventKind.LogisticsNetworkCreated,
            EntityId.None,
            id,
            amount: 0);
        return true;
    }

    private bool TryExecuteConfigureLogisticsHauler(SimulationCommand command)
    {
        if (command.Target == EntityId.None ||
            !_logisticsNetworks.TryGetValue(command.Target, out var network) ||
            !_actors.ContainsKey(command.Subject))
        {
            return false;
        }

        foreach (var other in _logisticsNetworks.Values.Where(other =>
                     other.Id != EntityId.None && other.Id != network.Id))
        {
            other.AssignedHaulerIds.Remove(command.Subject);
        }
        if (command.Amount == 1)
        {
            network.AssignedHaulerIds.Add(command.Subject);
        }
        else
        {
            network.AssignedHaulerIds.Remove(command.Subject);
        }
        WakeLogisticsNetwork(network);
        Publish(
            SimulationEventKind.LogisticsHaulerConfigured,
            command.Subject,
            network.Id,
            command.Amount);
        return true;
    }

    private bool TryExecuteConfigureLogisticsSource(SimulationCommand command)
    {
        if (command.Target == EntityId.None ||
            !_logisticsNetworks.TryGetValue(command.Target, out var network) ||
            !_storageZones.ContainsKey(command.Subject))
        {
            return false;
        }

        if (command.Amount == 1)
        {
            network.SourceStorageZoneIds.Add(command.Subject);
        }
        else
        {
            network.SourceStorageZoneIds.Remove(command.Subject);
        }
        WakeLogisticsNetwork(network);
        Publish(
            SimulationEventKind.LogisticsSourceConfigured,
            command.Subject,
            network.Id,
            command.Amount);
        return true;
    }

    private bool TryExecuteConfigureStorageNetwork(SimulationCommand command)
    {
        if (!_storageZones.TryGetValue(command.Target, out var zone) ||
            !_logisticsNetworks.ContainsKey(command.Subject))
        {
            return false;
        }

        if (!_storageAreas.TryGetValue(zone.StorageAreaId, out var area))
        {
            return false;
        }

        ConfigureStorageAreaNetwork(area, command.Subject);
        if (_logisticsNetworks.TryGetValue(command.Subject, out var network))
        {
            WakeLogisticsNetwork(network);
        }
        Publish(
            SimulationEventKind.StorageNetworkConfigured,
            command.Subject,
            zone.Id,
            amount: 0);
        return true;
    }

    private bool TryExecuteCreateStorageArea(SimulationCommand command)
    {
        if (!_logisticsNetworks.ContainsKey(command.Subject))
        {
            return false;
        }

        var cells = EnumerateAreaCells(command.Position, command.EndPosition).ToArray();
        if (cells.Length == 0 || cells.Length > 256 ||
            cells.Any(cell => !World.IsTerrainTraversable(cell)) ||
            cells.Any(cell => _storageAreas.Values.Any(area => area.Footprint.Contains(cell))))
        {
            return false;
        }

        var id = AllocateEntityId();
        var area = new StorageAreaState(
            id,
            $"Storage area {id.Value}",
            cells,
            command.Subject);
        _storageAreas.Add(id, area);
        Publish(SimulationEventKind.StorageAreaCreated, command.Subject, id, cells.Length);
        return true;
    }

    private bool TryExecuteConfigureStorageAreaNetwork(SimulationCommand command)
    {
        if (!_storageAreas.TryGetValue(command.Target, out var area) ||
            !_logisticsNetworks.ContainsKey(command.Subject))
        {
            return false;
        }

        ConfigureStorageAreaNetwork(area, command.Subject);
        if (_logisticsNetworks.TryGetValue(command.Subject, out var network))
        {
            WakeLogisticsNetwork(network);
        }
        Publish(
            SimulationEventKind.StorageAreaNetworkConfigured,
            command.Subject,
            area.Id,
            amount: 0);
        return true;
    }

    private void ConfigureStorageAreaNetwork(StorageAreaState area, EntityId networkId)
    {
        area.LogisticsNetworkId = networkId;
        foreach (var member in _storageZones.Values.Where(zone => zone.StorageAreaId == area.Id))
        {
            member.LogisticsNetworkId = networkId;
            WakeAssignedHauler(member);
        }
    }

    private bool TryExecuteRenameLogisticsNetwork(SimulationCommand command)
    {
        if (command.Target == EntityId.None ||
            !_logisticsNetworks.TryGetValue(command.Target, out var network) ||
            _logisticsNetworks.Values.Any(candidate =>
                candidate.Id != command.Target &&
                string.Equals(
                    candidate.Name,
                    command.Text.Trim(),
                    StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        network.Name = command.Text.Trim();
        Publish(SimulationEventKind.LogisticsNetworkRenamed, EntityId.None, network.Id, 0);
        return true;
    }

    private bool TryExecuteRenameStorageArea(SimulationCommand command)
    {
        if (!_storageAreas.TryGetValue(command.Target, out var area) ||
            _storageAreas.Values.Any(candidate =>
                candidate.Id != command.Target &&
                string.Equals(
                    candidate.Name,
                    command.Text.Trim(),
                    StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        area.Name = command.Text.Trim();
        Publish(SimulationEventKind.StorageAreaRenamed, EntityId.None, area.Id, 0);
        return true;
    }

    private bool TryExecuteConfigureStorageFilter(SimulationCommand command)
    {
        if (!_storageZones.TryGetValue(command.Target, out var zone) ||
            zone.ProviderKind is not (StorageProviderKind.WoodenBox or
                StorageProviderKind.WoodenChest or StorageProviderKind.WoodenBulkBin) ||
            !IsSolidContainerFilter(command.Resource))
        {
            return false;
        }

        zone.AcceptedResource = command.Resource;
        zone.ResourceFilter = CreateStorageResourceFilter(command.Resource);
        if (zone.AcceptedResource != ResourceKind.Stone)
        {
            zone.MineralFilter = MineralStorageFilter.All;
        }
        IndexStorageZone(zone);
        WakeAssignedHauler(zone);
        Publish(
            SimulationEventKind.StorageFilterConfigured,
            EntityId.None,
            zone.Id,
            (int)zone.AcceptedResource);
        return true;
    }

    private bool TryExecuteConfigureStorageFilterResource(SimulationCommand command)
    {
        if (!_storageZones.TryGetValue(command.Target, out var zone) ||
            zone.ProviderKind is not (StorageProviderKind.WoodenBox or
                StorageProviderKind.WoodenChest or StorageProviderKind.WoodenBulkBin) ||
            !IsSolidContainerFilter(command.Resource))
        {
            return false;
        }

        var resource = CreateStorageResourceFilter(command.Resource, expandPreset: false);
        var filter = command.Amount == 1
            ? zone.ResourceFilter | resource
            : zone.ResourceFilter & ~resource;
        if (!IsValidSolidContainerResourceFilter(filter))
        {
            return false;
        }

        zone.ResourceFilter = filter;
        zone.AcceptedResource = GetStorageFilterDisplayResource(filter);
        if (zone.AcceptedResource != ResourceKind.Stone)
        {
            zone.MineralFilter = MineralStorageFilter.All;
        }
        IndexStorageZone(zone);
        WakeAssignedHauler(zone);
        Publish(
            SimulationEventKind.StorageFilterResourceConfigured,
            EntityId.None,
            zone.Id,
            (int)zone.ResourceFilter);
        return true;
    }

    private bool TryExecuteDeleteLogisticsNetwork(SimulationCommand command)
    {
        if (command.Target == EntityId.None ||
            !_logisticsNetworks.TryGetValue(command.Target, out var network))
        {
            return false;
        }

        foreach (var actorId in network.AssignedHaulerIds)
        {
            if (_actors.TryGetValue(actorId, out var actor) &&
                actor.JobKind == ActorJobKind.Haul &&
                _storageZones.TryGetValue(actor.DestinationZoneId, out var destination) &&
                destination.LogisticsNetworkId == network.Id)
            {
                actor.ClearJob();
            }
        }
        foreach (var area in _storageAreas.Values.Where(area =>
                     area.LogisticsNetworkId == network.Id))
        {
            ConfigureStorageAreaNetwork(area, EntityId.None);
        }
        foreach (var zone in _storageZones.Values.Where(zone =>
                     zone.LogisticsNetworkId == network.Id))
        {
            zone.LogisticsNetworkId = EntityId.None;
            WakeAssignedHauler(zone);
        }

        _logisticsNetworks.Remove(network.Id);
        Publish(SimulationEventKind.LogisticsNetworkDeleted, EntityId.None, network.Id, 0);
        return true;
    }

    private bool TryExecuteResizeStorageArea(SimulationCommand command)
    {
        if (!_storageAreas.TryGetValue(command.Target, out var area))
        {
            return false;
        }

        var cells = EnumerateAreaCells(command.Position, command.EndPosition).ToArray();
        if (cells.Length == 0 || cells.Length > 256 ||
            (area.Footprint.Length > 0 && cells[0].Z != area.Footprint[0].Z) ||
            cells.Any(cell => !World.IsTerrainTraversable(cell)) ||
            cells.Any(cell => _storageAreas.Values.Any(other =>
                other.Id != area.Id && other.Footprint.Contains(cell))) ||
            _storageZones.Values.Any(zone =>
                zone.StorageAreaId == area.Id && !cells.Contains(zone.Position)))
        {
            return false;
        }

        area.SetFootprint(cells);
        Publish(SimulationEventKind.StorageAreaResized, EntityId.None, area.Id, cells.Length);
        return true;
    }

    private bool TryExecuteDissolveStorageArea(SimulationCommand command)
    {
        if (!_storageAreas.Remove(command.Target, out var area))
        {
            return false;
        }

        var providersByPosition = _storageZones.Values
            .Where(zone => zone.StorageAreaId == area.Id)
            .GroupBy(zone => zone.Position)
            .OrderBy(group => group.Key.Z)
            .ThenBy(group => group.Key.Y)
            .ThenBy(group => group.Key.X)
            .ToArray();
        foreach (var providers in providersByPosition)
        {
            var singletonId = AllocateEntityId();
            var singleton = new StorageAreaState(
                singletonId,
                $"Storage {singletonId.Value}",
                [providers.Key],
                area.LogisticsNetworkId);
            _storageAreas.Add(singleton.Id, singleton);
            foreach (var zone in providers)
            {
                zone.StorageAreaId = singleton.Id;
            }
        }

        Publish(
            SimulationEventKind.StorageAreaDissolved,
            EntityId.None,
            area.Id,
            providersByPosition.Length);
        return true;
    }

    private bool TryExecuteConfigureStoragePriority(SimulationCommand command)
    {
        var priority = (StoragePriority)command.Amount;
        if (!_storageZones.TryGetValue(command.Target, out var zone) ||
            !Enum.IsDefined(priority))
        {
            return false;
        }

        zone.Priority = priority;
        WakeAssignedHauler(zone);
        Publish(
            SimulationEventKind.StoragePriorityConfigured,
            EntityId.None,
            zone.Id,
            command.Amount);
        return true;
    }

    private bool TryExecuteConfigureStorageMineralFilter(SimulationCommand command)
    {
        var filter = (MineralStorageFilter)command.Amount;
        if (!_storageZones.TryGetValue(command.Target, out var zone) ||
            zone.AcceptedResource != ResourceKind.Stone ||
            !IsValidMineralFilter(filter))
        {
            return false;
        }

        zone.MineralFilter = filter;
        WakeAssignedHauler(zone);
        Publish(
            SimulationEventKind.StorageMineralFilterConfigured,
            EntityId.None,
            zone.Id,
            command.Amount);
        return true;
    }

    private bool TryExecuteConfigureResourcePriority(SimulationCommand command)
    {
        var priority = (StoragePriority)command.Amount;
        if (!_resourcePriorities.ContainsKey(command.Resource) ||
            !Enum.IsDefined(priority))
        {
            return false;
        }

        _resourcePriorities[command.Resource] = priority;
        Publish(
            SimulationEventKind.ResourcePriorityConfigured,
            EntityId.None,
            EntityId.None,
            (int)command.Resource);
        return true;
    }

    private bool TryExecuteConfigureConstructionPriority(SimulationCommand command)
    {
        var priority = (StoragePriority)command.Amount;
        if (!_constructionSites.TryGetValue(command.Target, out var site) ||
            !Enum.IsDefined(priority))
        {
            return false;
        }

        site.Priority = priority;
        Publish(
            SimulationEventKind.ConstructionPriorityConfigured,
            EntityId.None,
            site.Id,
            command.Amount);
        return true;
    }

    private void WakeAssignedHauler(StorageZoneState zone)
    {
        if (zone.AssignedHaulerId != EntityId.None &&
            zone.DesiredQuantity > GetStoredQuantity(zone.Id) &&
            _actors.TryGetValue(zone.AssignedHaulerId, out var actor) &&
            actor.JobKind == ActorJobKind.Explore)
        {
            actor.ClearJob();
        }
    }

    private void WakeLogisticsNetwork(LogisticsNetworkState network)
    {
        foreach (var actorId in network.AssignedHaulerIds)
        {
            if (_actors.TryGetValue(actorId, out var actor) &&
                actor.JobKind == ActorJobKind.Explore)
            {
                actor.ClearJob();
            }
        }
    }

    private bool TryExecuteBuild(SimulationCommand command)
    {
        var footprint = GetConstructionFootprint(
            command.Construction,
            command.Position,
            command.EndPosition);
        IReadOnlyList<FloorCoveringPlacement> floorPlacements = [];
        if (command.Construction is ConstructionKind.WoodenFloor or ConstructionKind.StoneFloor)
        {
            var placements = new List<FloorCoveringPlacement>();
            foreach (var position in footprint)
            {
                if (FloorCoveringPlacementPolicy.TryResolve(
                        World,
                        command.Construction,
                        position,
                        out var placement) &&
                    CanPlaceConstruction(
                        placement.Kind,
                        placement.Anchor,
                        placement.End,
                        [placement.Anchor]) &&
                    !_constructionSites.Values.Any(site =>
                        site.GetFootprint().Contains(placement.Anchor)))
                {
                    placements.Add(placement);
                }
            }
            floorPlacements = placements;
            if (floorPlacements.Count == 0)
            {
                return false;
            }
        }
        else if (!CanPlaceConstruction(
                     command.Construction,
                     command.Position,
                     command.EndPosition,
                     footprint) ||
                 _constructionSites.Values.Any(site =>
                     site.GetFootprint().Intersect(footprint).Any()))
        {
            return false;
        }

        if (command.Construction is ConstructionKind.WoodenFloor or ConstructionKind.StoneFloor)
        {
            var orderId = AllocateEntityId();
            for (var index = 0; index < floorPlacements.Count; index++)
            {
                var placement = floorPlacements[index];
                AddConstructionSite(
                    placement.Kind,
                    placement.Anchor,
                    placement.End,
                    orderId,
                    index,
                    command.MaterialVariant);
            }
        }
        else if (command.Construction is ConstructionKind.WoodenWalkway or
            ConstructionKind.BasaltWalkway or
            ConstructionKind.WoodenWall or ConstructionKind.StoneWall)
        {
            var orderId = AllocateEntityId();
            for (var index = 0; index < footprint.Count; index++)
            {
                var position = footprint[index];
                AddConstructionSite(
                    command.Construction,
                    position,
                    position,
                    orderId,
                    index,
                    command.MaterialVariant);
            }
        }
        else
        {
            AddConstructionSite(
                command.Construction,
                command.Position,
                command.EndPosition,
                requiredVariant: command.MaterialVariant);
        }

        _lastConstructionCommandExecutionTick = CurrentTick.Value;
        return true;
    }

    private void AddConstructionSite(
        ConstructionKind kind,
        GridPosition anchor,
        GridPosition end,
        EntityId orderId = default,
        int sequenceIndex = 0,
        ResourceVariant requiredVariant = ResourceVariant.None)
    {
        var id = AllocateEntityId();
        var site = ConstructionBlueprintCatalog.CreateSite(
            id, kind, anchor, end, orderId, sequenceIndex, requiredVariant);
        _constructionSites.Add(id, site);
        Publish(
            SimulationEventKind.ConstructionOrdered,
            EntityId.None,
            id,
            site.RequiredQuantity);
    }

    private IReadOnlyList<GridPosition> GetConstructionFootprint(
        ConstructionKind kind,
        GridPosition anchor,
        GridPosition end) => kind switch
    {
        ConstructionKind.WoodenWalkway or ConstructionKind.BasaltWalkway or
            ConstructionKind.WoodenWall or
            ConstructionKind.StoneWall =>
            SimulationCommand.GetLinearCells(anchor, end),
        ConstructionKind.WoodenFloor or ConstructionKind.StoneFloor =>
            SimulationCommand.GetAreaCells(anchor, end),
        ConstructionKind.GoblinFieldCamp =>
        [
            anchor,
            anchor with { X = anchor.X + 1 },
            anchor with { Y = anchor.Y + 1 },
            anchor with { X = anchor.X + 1, Y = anchor.Y + 1 },
        ],
        ConstructionKind.GoblinHut => Enumerable.Range(0, 3)
            .SelectMany(y => Enumerable.Range(0, 3)
                .Select(x => new GridPosition(anchor.X + x, anchor.Y + y, anchor.Z)))
            .ToArray(),
        ConstructionKind.WoodenWatchtower =>
        [
            anchor,
            anchor with { X = anchor.X + 1 },
            anchor with { Y = anchor.Y + 1 },
            anchor with { X = anchor.X + 1, Y = anchor.Y + 1 },
        ],
        _ => [anchor],
    };

    private bool CanPlaceConstruction(
        ConstructionKind kind,
        GridPosition anchor,
        GridPosition end,
        IReadOnlyList<GridPosition> footprint)
    {
        if (footprint.Any(position => !IsPotentialConstructionPosition(position)) ||
            kind is not (ConstructionKind.WoodenFloor or ConstructionKind.StoneFloor) &&
            _storageZones.Values.Any(zone => footprint.Contains(zone.Position)))
        {
            return false;
        }

        return kind switch
        {
            ConstructionKind.FoodStorage or ConstructionKind.WoodStorage or
                ConstructionKind.StoneStorage or ConstructionKind.EquipmentStorage or
                ConstructionKind.MaterialsStorage or ConstructionKind.WaterBarrel or
                ConstructionKind.WoodenBox or ConstructionKind.WoodenChest or
                ConstructionKind.WoodenBulkBin =>
                anchor.Z == 0
                    ? World.IsSurfaceTraversable(anchor)
                    : World.IsTerrainTraversable(anchor),
            ConstructionKind.WoodenWalkway => World.CanBuildWalkway(footprint),
            ConstructionKind.BasaltWalkway => World.CanBuildBasaltWalkway(footprint),
            ConstructionKind.GoblinFieldCamp => World.CanBuildGoblinFieldCamp(anchor),
            ConstructionKind.GoblinHut => World.CanBuildGoblinHut(anchor),
            ConstructionKind.GoblinCompost => World.CanBuildGoblinCompost(anchor),
            ConstructionKind.WoodenWatchtower => World.CanBuildWoodenWatchtower(anchor),
            ConstructionKind.ReedSleepingMat => World.CanBuildReedSleepingMat(anchor),
            ConstructionKind.WoodenWall => World.CanBuildWoodenWalls(footprint),
            ConstructionKind.StoneWall => World.CanBuildStoneWalls(footprint),
            ConstructionKind.WoodenFloor or ConstructionKind.StoneFloor =>
                World.CanBuildFloors(footprint),
            ConstructionKind.WoodenRamp or ConstructionKind.StoneRamp =>
                World.CanBuildRamp(anchor, end),
            ConstructionKind.WoodenLadder => World.CanBuildWoodenLadder(anchor, end),
            ConstructionKind.WoodenDoorFrame => World.CanBuildWoodenDoorFrame(anchor),
            ConstructionKind.StoneDoorFrame => World.CanBuildStoneDoorFrame(anchor),
            ConstructionKind.WoodenDoor => World.CanBuildWoodenDoor(anchor),
            ConstructionKind.WallTorch => World.CanBuildWallTorch(anchor),
            ConstructionKind.StandingTorch => World.CanBuildStandingTorch(anchor),
            ConstructionKind.PrimitiveWorkshop or ConstructionKind.Bloomery or
                ConstructionKind.SmeltingFurnace or ConstructionKind.CrucibleFurnace or
                ConstructionKind.CookingFire or ConstructionKind.FittedWorkshop =>
                World.CanBuildWorkshop(anchor),
            _ => false,
        };
    }

    private bool IsPotentialConstructionPosition(GridPosition position) =>
        IsAddressableMapPosition(position);

    private bool HasGroundStackInConstructionFootprint(ConstructionSiteState site)
    {
        var footprint = site.GetFootprint();
        return _itemStacks.Values.Any(stack =>
            stack.Location.Kind == ItemLocationKind.Ground &&
            footprint.Contains(stack.Location.Position));
    }

    private bool IsGroundStackBlockingConstruction(ItemStackState stack) =>
        stack.Location.Kind == ItemLocationKind.Ground &&
        _constructionSites.Values.Any(site =>
            site.GetFootprint().Contains(stack.Location.Position));

    private bool CompleteConstruction(ActorState builder, ConstructionSiteState site)
    {
        if (HasGroundStackInConstructionFootprint(site))
        {
            return false;
        }

        if (site.Kind is ConstructionKind.WoodenWall or ConstructionKind.StoneWall or
                ConstructionKind.PrimitiveWorkshop or ConstructionKind.Bloomery or
                ConstructionKind.SmeltingFurnace or ConstructionKind.CrucibleFurnace or
                ConstructionKind.CookingFire or ConstructionKind.FittedWorkshop or
                ConstructionKind.GoblinHut or ConstructionKind.WoodenWatchtower &&
            _actors.Values.Any(actor => site.GetFootprint().Contains(actor.Position)))
        {
            return false;
        }

        if (!CanPlaceConstruction(site.Kind, site.Anchor, site.End, site.GetFootprint()))
        {
            return false;
        }

        EntityId completedTarget;
        var experience = 10;
        switch (site.Kind)
        {
            case ConstructionKind.FoodStorage:
            case ConstructionKind.WoodStorage:
            case ConstructionKind.StoneStorage:
            case ConstructionKind.EquipmentStorage:
            case ConstructionKind.MaterialsStorage:
            case ConstructionKind.WaterBarrel:
            case ConstructionKind.WoodenBox:
            case ConstructionKind.WoodenChest:
            case ConstructionKind.WoodenBulkBin:
                var acceptedResource = site.Kind switch
                {
                    ConstructionKind.FoodStorage => ResourceKind.Food,
                    ConstructionKind.WoodStorage => ResourceKind.Wood,
                    ConstructionKind.StoneStorage => ResourceKind.Stone,
                    ConstructionKind.EquipmentStorage => ResourceKind.Equipment,
                    ConstructionKind.MaterialsStorage => ResourceKind.Materials,
                    ConstructionKind.WaterBarrel => ResourceKind.Water,
                    ConstructionKind.WoodenBox or ConstructionKind.WoodenChest or
                        ConstructionKind.WoodenBulkBin => ResourceKind.Any,
                    _ => throw new InvalidOperationException(),
                };
                var capacity = acceptedResource switch
                {
                    ResourceKind.Food => Definitions.Storage.SmallFoodCapacity,
                    ResourceKind.Equipment => 32,
                    ResourceKind.Materials => 64,
                    ResourceKind.Water => 32,
                    ResourceKind.Any when site.Kind == ConstructionKind.WoodenBox => 32,
                    ResourceKind.Any when site.Kind == ConstructionKind.WoodenChest => 64,
                    ResourceKind.Any when site.Kind == ConstructionKind.WoodenBulkBin => 64,
                    _ => 64,
                };
                var providerKind = site.Kind switch
                {
                    ConstructionKind.WaterBarrel => StorageProviderKind.WaterBarrel,
                    ConstructionKind.WoodenBox => StorageProviderKind.WoodenBox,
                    ConstructionKind.WoodenChest => StorageProviderKind.WoodenChest,
                    ConstructionKind.WoodenBulkBin => StorageProviderKind.WoodenBulkBin,
                    _ => StorageProviderKind.OpenPile,
                };
                completedTarget = AllocateStorageZone(
                    site.Anchor,
                    acceptedResource,
                    capacity,
                    desiredQuantity: capacity,
                    slotPolicy: providerKind == StorageProviderKind.OpenPile
                        ? null
                        : CreateStorageProviderSlotPolicy(providerKind, capacity),
                    providerKind: providerKind).Id;
                break;
            case ConstructionKind.WoodenWalkway:
                var cells = site.GetFootprint();
                _undeliveredWorldChanges.Add(World.BuildWalkway(
                    cells,
                    CurrentTick,
                    site.DeliveredVariant));
                completedTarget = EntityId.None;
                experience = Math.Max(5, cells.Count * 3);
                break;
            case ConstructionKind.BasaltWalkway:
                var basaltCells = site.GetFootprint();
                _undeliveredWorldChanges.Add(World.BuildBasaltWalkway(
                    basaltCells,
                    CurrentTick,
                    site.DeliveredVariant));
                completedTarget = EntityId.None;
                experience = Math.Max(10, basaltCells.Count * 6);
                break;
            case ConstructionKind.GoblinFieldCamp:
                _undeliveredWorldChanges.Add(World.BuildGoblinFieldCamp(
                    site.Anchor,
                    CurrentTick,
                    site.DeliveredVariant));
                const int campProvisionCapacity = 48;
                var campProvisionTarget = Math.Min(
                    campProvisionCapacity,
                    Math.Max(24, _actors.Count * (Definitions.PersonalFoodCapacity + 3)));
                completedTarget = AllocateStorageZone(
                    site.Anchor,
                    ResourceKind.Food,
                    campProvisionCapacity,
                    campProvisionTarget).Id;
                experience = 25;
                break;
            case ConstructionKind.GoblinHut:
                _undeliveredWorldChanges.Add(World.BuildGoblinHut(
                    site.Anchor,
                    CurrentTick,
                    site.DeliveredVariant));
                completedTarget = EntityId.None;
                experience = 35;
                break;
            case ConstructionKind.GoblinCompost:
                _undeliveredWorldChanges.Add(World.BuildGoblinCompost(
                    site.Anchor,
                    CurrentTick));
                completedTarget = EntityId.None;
                experience = 8;
                break;
            case ConstructionKind.WoodenWatchtower:
                _undeliveredWorldChanges.Add(World.BuildWoodenWatchtower(
                    site.Anchor,
                    CurrentTick,
                    site.DeliveredVariant));
                completedTarget = EntityId.None;
                experience = 30;
                break;
            case ConstructionKind.ReedSleepingMat:
                _undeliveredWorldChanges.Add(World.BuildReedSleepingMat(
                    site.Anchor,
                    CurrentTick));
                completedTarget = EntityId.None;
                experience = 6;
                break;
            case ConstructionKind.WoodenWall:
                var wallCells = site.GetFootprint();
                _undeliveredWorldChanges.Add(World.BuildWoodenWalls(
                    wallCells,
                    CurrentTick,
                    site.DeliveredVariant));
                completedTarget = EntityId.None;
                experience = Math.Max(8, wallCells.Count * 12);
                break;
            case ConstructionKind.StoneWall:
                var stoneWallCells = site.GetFootprint();
                _undeliveredWorldChanges.Add(World.BuildStoneWalls(
                    stoneWallCells,
                    CurrentTick,
                    site.DeliveredVariant));
                completedTarget = EntityId.None;
                experience = Math.Max(12, stoneWallCells.Count * 16);
                break;
            case ConstructionKind.WoodenFloor:
            case ConstructionKind.StoneFloor:
                _undeliveredWorldChanges.Add(World.BuildFloor(
                    site.Anchor,
                    CurrentTick,
                    stone: site.Kind == ConstructionKind.StoneFloor,
                    site.DeliveredVariant));
                completedTarget = EntityId.None;
                experience = site.Kind == ConstructionKind.StoneFloor ? 8 : 5;
                break;
            case ConstructionKind.WoodenRamp:
            case ConstructionKind.StoneRamp:
                _undeliveredWorldChanges.Add(World.BuildRamp(
                    site.Anchor,
                    site.End,
                    CurrentTick,
                    stone: site.Kind == ConstructionKind.StoneRamp,
                    site.DeliveredVariant));
                completedTarget = EntityId.None;
                experience = site.Kind == ConstructionKind.StoneRamp ? 14 : 10;
                break;
            case ConstructionKind.WoodenLadder:
                _undeliveredWorldChanges.Add(World.BuildWoodenLadder(
                    site.Anchor,
                    site.End,
                    CurrentTick,
                    site.DeliveredVariant));
                completedTarget = EntityId.None;
                experience = 10;
                break;
            case ConstructionKind.WoodenDoorFrame:
                _undeliveredWorldChanges.Add(World.BuildWoodenDoorFrame(
                    site.Anchor,
                    CurrentTick,
                    site.DeliveredVariant));
                completedTarget = EntityId.None;
                experience = 8;
                break;
            case ConstructionKind.StoneDoorFrame:
                _undeliveredWorldChanges.Add(World.BuildStoneDoorFrame(
                    site.Anchor,
                    CurrentTick,
                    site.DeliveredVariant));
                completedTarget = EntityId.None;
                experience = 12;
                break;
            case ConstructionKind.WoodenDoor:
                _undeliveredWorldChanges.Add(World.BuildWoodenDoor(
                    site.Anchor,
                    CurrentTick,
                    site.DeliveredVariant));
                completedTarget = EntityId.None;
                experience = 8;
                break;
            case ConstructionKind.WallTorch:
                _undeliveredWorldChanges.Add(World.BuildWallTorch(
                    site.Anchor,
                    CurrentTick,
                    site.DeliveredVariant));
                completedTarget = EntityId.None;
                experience = 5;
                break;
            case ConstructionKind.StandingTorch:
                _undeliveredWorldChanges.Add(World.BuildStandingTorch(
                    site.Anchor,
                    CurrentTick,
                    site.DeliveredVariant));
                completedTarget = EntityId.None;
                experience = 5;
                break;
            case ConstructionKind.PrimitiveWorkshop:
            case ConstructionKind.Bloomery:
            case ConstructionKind.SmeltingFurnace:
            case ConstructionKind.CrucibleFurnace:
            case ConstructionKind.CookingFire:
            case ConstructionKind.FittedWorkshop:
                _ = ConstructionBlueprintCatalog.TryGetWorkshopKind(
                    site.Kind,
                    out var workshopKind);
                _undeliveredWorldChanges.Add(
                    World.BuildWorkshop(
                        site.Anchor,
                        workshopKind,
                        site.DeliveredVariant,
                        CurrentTick));
                completedTarget = EntityId.None;
                experience = WorkshopCatalog.Get(workshopKind).Level switch
                {
                    1 => 24,
                    _ => 36,
                };
                break;
            default:
                throw new InvalidOperationException("Unsupported construction blueprint.");
        }

        foreach (var position in site.GetFootprint())
        {
            RefreshAutonomousCleaningRegistration(position);
        }
        _constructionSites.Remove(site.Id);
        GainBuildingExperience(builder, experience);
        Publish(
            SimulationEventKind.ConstructionCompleted,
            builder.Id,
            completedTarget,
            site.RequiredQuantity,
            site.Kind);
        return true;
    }

    private bool TryExecuteMove(SimulationCommand command)
    {
        if (!_actors.TryGetValue(command.Subject, out var actor) ||
            !World.IsTerrainReachable(command.Position))
        {
            return false;
        }

        var route = FindActorPath(actor, command.Position);
        if (route is null)
        {
            return false;
        }

        actor.ClearJob();
        actor.ClearTacticalOrder();
        actor.ClearSuspendedJob();
        Publish(SimulationEventKind.MoveOrdered, actor.Id, EntityId.None, route.Count);
        if (route.Count == 0)
        {
            Publish(SimulationEventKind.MoveCompleted, actor.Id, EntityId.None, 0);
            return true;
        }

        actor.JobKind = ActorJobKind.Move;
        actor.JobPhase = ActorJobPhase.Traveling;
        actor.JobTarget = command.Position;
        actor.RemainingRoute.AddRange(route);
        return true;
    }

    private bool TryExecuteActorFlee(SimulationCommand command) => TryExecuteMove(
        command with
        {
            Kind = SimulationCommandKind.Move,
            Position = Map.GoblinSpawn,
            EndPosition = Map.GoblinSpawn,
        });

    private bool TryExecuteActorSleep(SimulationCommand command)
    {
        if (!_actors.TryGetValue(command.Subject, out var actor) ||
            actor.Health <= 0 || actor.CarriedStackId != EntityId.None)
        {
            return false;
        }

        var route = FindRestRoute(actor);
        if (route is null)
        {
            return false;
        }

        actor.ClearJob();
        actor.ClearSuspendedJob();
        actor.ClearTacticalOrder();
        actor.JobKind = ActorJobKind.Rest;
        actor.JobTarget = route.Count == 0 ? actor.Position : route[^1];
        BeginJobLeg(actor, route, GetRestWorkTicks(actor));

        Publish(SimulationEventKind.ActorOrderedToRest, actor.Id, EntityId.None, 0);
        return true;
    }

    private bool TryExecuteSuspendActorDispatcher(SimulationCommand command)
    {
        if (!_actors.TryGetValue(command.Subject, out var actor) || actor.Health <= 0)
        {
            return false;
        }

        var calendar = SimulationCalendar.At(CurrentTick, Definitions.Clock);
        var ticksPerDay = Definitions.Clock.Climate.GetSeason(calendar.Season).TicksPerDay;
        var gameHourTicks = Math.Max(1, (ticksPerDay + 23) / 24);
        actor.DispatcherSuspendedUntilTick = checked(CurrentTick.Value + gameHourTicks);
        if (actor.TacticalOrderKind == ActorTacticalOrderKind.None &&
            actor.JobKind is not (ActorJobKind.None or ActorJobKind.Move or ActorJobKind.Rest or
                ActorJobKind.Eat or ActorJobKind.Resupply or ActorJobKind.Collapsed or
                ActorJobKind.LootRaid or ActorJobKind.RecoverRaidCorpse or
                ActorJobKind.ConsumeRaidCorpse))
        {
            actor.ClearJob();
            actor.ClearSuspendedJob();
        }
        Publish(
            SimulationEventKind.ActorDispatcherSuspended,
            actor.Id,
            EntityId.None,
            gameHourTicks);
        return true;
    }

    private bool TryExecuteEquipItem(SimulationCommand command)
    {
        if (!_actors.TryGetValue(command.Subject, out var actor) || actor.Health <= 0 ||
            actor.CarriedStackId != EntityId.None ||
            !_itemStacks.TryGetValue(command.Target, out var source) ||
            source.Resource != ResourceKind.Equipment || source.Quantity <= 0 ||
            source.Location.Kind is not (ItemLocationKind.Ground or ItemLocationKind.StorageZone) ||
            EquipmentCatalog.FindDefinition(source.Variant) is not { } selected ||
            actor.Equipment.HasFlag(selected.Equipment) ||
            FindActorPath(actor, source.Location.Position) is null)
        {
            return false;
        }

        var sourceLocation = source.Location;
        var replaced = EquipmentCatalog.GetReplacedDefinitions(
            actor.Equipment,
            selected.Equipment);
        actor.ClearJob();
        actor.ClearSuspendedJob();
        MoveActor(actor, sourceLocation.Position);
        foreach (var previous in replaced)
        {
            actor.Equipment &= ~previous.Equipment;
        }
        actor.Equipment |= selected.Equipment;
        source.Quantity--;
        Publish(SimulationEventKind.ActorEquippedItem, actor.Id, source.Id, 1);
        if (source.Quantity == 0)
        {
            RemoveItemStack(source.Id);
            Publish(SimulationEventKind.ItemStackDepleted, actor.Id, source.Id, 0);
        }

        foreach (var previous in replaced)
        {
            var returned = _itemStacks.Values.FirstOrDefault(stack =>
                stack.Resource == ResourceKind.Equipment &&
                stack.Variant == previous.Variant &&
                stack.Location == sourceLocation);
            returned ??= AllocateItemStack(
                ResourceKind.Equipment,
                quantity: 0,
                location: sourceLocation,
                variant: previous.Variant);
            returned.Quantity++;
            Publish(
                sourceLocation.Kind == ItemLocationKind.StorageZone
                    ? SimulationEventKind.ItemStored
                    : SimulationEventKind.ItemDropped,
                actor.Id,
                returned.Id,
                1);
        }
        return true;
    }

    private bool TryExecutePrioritizeItemHauling(SimulationCommand command)
    {
        if (!_itemStacks.TryGetValue(command.Target, out var source) ||
            source.Location.Kind is not (ItemLocationKind.Ground or ItemLocationKind.StorageZone) ||
            !_storageZones.Values.Any(zone =>
                (source.Location.Kind != ItemLocationKind.StorageZone ||
                 source.Location.OwnerId != zone.Id) &&
                ZoneAccepts(zone, source) && CanStoreStack(zone, source, 1)))
        {
            return false;
        }

        source.HaulPriority = StoragePriority.Urgent;
        Publish(SimulationEventKind.ItemHaulPrioritized, EntityId.None, source.Id, source.Quantity);
        return true;
    }

    private bool TryExecuteOrderItemPickup(SimulationCommand command)
    {
        if (!_actors.TryGetValue(command.Subject, out var actor) || actor.Health <= 0 ||
            actor.CarriedStackId != EntityId.None ||
            !_itemStacks.TryGetValue(command.Target, out var source) ||
            !CanActorHaulStack(actor, source) ||
            source.Location.Kind is not (ItemLocationKind.Ground or ItemLocationKind.StorageZone))
        {
            return false;
        }

        var routeToSource = FindActorPath(actor, source.Location.Position);
        if (routeToSource is null)
        {
            return false;
        }
        var destination = _storageZones.Values
            .Where(zone =>
                (source.Location.Kind != ItemLocationKind.StorageZone ||
                 source.Location.OwnerId != zone.Id) &&
                ZoneAccepts(zone, source) && CanStoreStack(zone, source, 1))
            .Select(zone => new
            {
                Zone = zone,
                Capacity = GetAvailableStorageQuantity(zone, source),
                Route = FindActorPathFrom(actor, source.Location.Position, zone.Position),
            })
            .Where(candidate => candidate.Capacity > 0 && candidate.Route is not null)
            .OrderByDescending(candidate => candidate.Zone.Priority)
            .ThenBy(candidate => candidate.Route!.Count)
            .ThenBy(candidate => candidate.Zone.Id)
            .FirstOrDefault();
        if (destination is null)
        {
            return false;
        }

        actor.ClearJob();
        actor.ClearSuspendedJob();
        actor.JobKind = ActorJobKind.Haul;
        actor.JobStage = ActorJobStage.Collecting;
        actor.SourceStackId = source.Id;
        actor.DestinationZoneId = destination.Zone.Id;
        actor.ReservedQuantity = Math.Min(
            Math.Min(source.Quantity, GetActorHaulQuantityLimit(actor, source)),
            destination.Capacity);
        actor.JobTarget = source.Location.Position;
        BeginJobLeg(actor, routeToSource, Definitions.HaulHandlingTicks);
        Publish(SimulationEventKind.MoveOrdered, actor.Id, source.Id, routeToSource.Count);
        return true;
    }

    private bool TryExecuteOrderPatrol(SimulationCommand command)
    {
        if (!_actors.TryGetValue(command.Subject, out var actor) ||
            !World.IsTerrainReachable(command.Position))
        {
            return false;
        }

        if (command.Amount == 0)
        {
            actor.ClearTacticalOrder();
            actor.TacticalOrderKind = ActorTacticalOrderKind.Patrol;
            actor.PatrolPoints.Add(actor.Position);
            if (command.Position != actor.Position)
            {
                actor.PatrolPoints.Add(command.Position);
            }
            actor.PatrolPointIndex = actor.PatrolPoints.Count > 1 ? 1 : 0;
        }
        else if (actor.TacticalOrderKind == ActorTacticalOrderKind.Patrol &&
                 actor.PatrolPoints.Count < 16 && actor.PatrolPoints[^1] != command.Position)
        {
            actor.PatrolPoints.Add(command.Position);
        }
        else
        {
            return false;
        }

        actor.ClearJob();
        actor.ClearSuspendedJob();
        return true;
    }

    private bool TryExecuteAreaOrder(
        SimulationCommand command,
        ActorTacticalOrderKind kind)
    {
        if (!_actors.TryGetValue(command.Subject, out var actor) ||
            command.Amount is < MinimumRaidTargetRadius or > MaximumRaidTargetRadius)
        {
            return false;
        }

        actor.ClearTacticalOrder();
        actor.TacticalOrderKind = kind;
        actor.TacticalCenter = command.Position;
        actor.TacticalRadius = command.Amount;
        actor.ClearJob();
        actor.ClearSuspendedJob();
        return true;
    }

    private bool TryExecuteForage(SimulationCommand command)
    {
        if (!_actors.TryGetValue(command.Subject, out var actor))
        {
            return false;
        }

        actor.ClearJob();

        var randomYield = DeterministicRandom.NextInt(
            WorldSeed,
            RandomDomain.Foraging,
            command.Subject,
            CurrentTick,
            command.Sequence,
            minimumInclusive: 0,
            maximumExclusive: checked((Definitions.ForageVariance * command.Amount) + 1));

        var gathered = checked((Definitions.BaseForageYield * command.Amount) + randomYield);
        if (!World.TryHarvest(
                actor.Position,
                gathered,
                CurrentTick,
                out gathered,
                out var worldChange))
        {
            return false;
        }

        _undeliveredWorldChanges.Add(worldChange);
        var foodKind = FoodKindFor(World.GetPlantPatch(actor.Position)!.Value.Kind);
        var stack = FindMergeableGroundStack(ResourceKind.Food, actor.Position, foodKind)
            ?? AllocateItemStack(
                ResourceKind.Food, quantity: 0, ItemLocation.OnGround(actor.Position), foodKind);
        stack.Quantity = checked(stack.Quantity + gathered);
        GainForagingExperience(actor, Math.Max(1, gathered * 2));
        Publish(SimulationEventKind.FoodGathered, actor.Id, stack.Id, gathered);
        return true;
    }

    private bool TryExecuteCreateStorageZone(SimulationCommand command)
    {
        if (!World.IsTerrainTraversable(command.Position) ||
            _storageZones.Values.Any(zone => zone.Position == command.Position))
        {
            return false;
        }

        var zone = AllocateStorageZone(
            command.Position,
            command.Resource,
            command.Amount,
            desiredQuantity: command.Amount);
        Publish(SimulationEventKind.StorageZoneCreated, EntityId.None, zone.Id, zone.Capacity);
        return true;
    }

    private bool TryExecutePickUp(SimulationCommand command)
    {
        if (!_actors.TryGetValue(command.Subject, out var actor) ||
            actor.CarriedStackId != EntityId.None ||
            !_itemStacks.TryGetValue(command.Target, out var source) ||
            source.Location.Kind == ItemLocationKind.ActorInventory ||
            (source.Resource == ResourceKind.Food &&
             !FoodUsePolicy.CanBePacked(source.FoodKind)) ||
            command.Amount <= 0 ||
            command.Amount > source.Quantity ||
            command.Amount > Definitions.ActorCarryCapacity)
        {
            return false;
        }

        var sourcePosition = source.Location.Position;
        if (FindActorPath(actor, sourcePosition) is null)
        {
            return false;
        }

        ItemStackState carried;
        if (command.Amount == source.Quantity)
        {
            carried = source;
            MoveItemStack(carried, ItemLocation.CarriedBy(actor.Id));
        }
        else
        {
            source.Quantity -= command.Amount;
            carried = AllocateItemStack(
                source.Resource,
                command.Amount,
                ItemLocation.CarriedBy(actor.Id),
                source.FoodKind,
                source.Variant,
                source.FreshUntilTick);
        }

        MoveActor(actor, sourcePosition);
        actor.CarriedStackId = carried.Id;
        actor.ClearJob();
        Publish(SimulationEventKind.ItemPickedUp, actor.Id, carried.Id, carried.Quantity);
        return true;
    }

    private bool TryExecuteStoreCarried(SimulationCommand command)
    {
        if (!_actors.TryGetValue(command.Subject, out var actor) ||
            actor.CarriedStackId == EntityId.None ||
            !_itemStacks.TryGetValue(actor.CarriedStackId, out var carried) ||
            !_storageZones.TryGetValue(command.Target, out var zone) ||
            !CanStoreStack(zone, carried, carried.Quantity) ||
            FindActorPath(actor, zone.Position) is null)
        {
            return false;
        }

        MoveActor(actor, zone.Position);
        actor.CarriedStackId = EntityId.None;
        actor.ClearJob();
        var stored = StoreStackInZone(carried, zone);
        Publish(SimulationEventKind.ItemStored, actor.Id, stored.Id, carried.Quantity);
        return true;
    }

    private void UpdateActors()
    {
        var deadActors = new List<ActorState>();
        foreach (var actor in _actors.Values)
        {
            TryShareNavigationReports(actor);
            UpdateActorBleeding(actor);
            if (actor.JobKind is not (ActorJobKind.Rest or ActorJobKind.Collapsed) ||
                actor.JobPhase != ActorJobPhase.Working)
            {
                var fatiguePerTick = GoblinNeeds.FatiguePerTick;
                if (IsJuvenile(actor) && actor.JobKind == ActorJobKind.Haul)
                {
                    fatiguePerTick = checked(
                        fatiguePerTick * JuvenileHaulFatigueMultiplier);
                }
                actor.Fatigue = Math.Min(
                    GoblinNeeds.MaximumFatigue,
                    checked(actor.Fatigue + fatiguePerTick));
            }

            if (actor.Fatigue >= GoblinNeeds.MaximumFatigue &&
                actor.JobKind is not (ActorJobKind.Rest or ActorJobKind.Collapsed))
            {
                if (actor.CarriedStackId != EntityId.None &&
                    _itemStacks.TryGetValue(actor.CarriedStackId, out var carried))
                {
                    MoveItemStack(carried, ItemLocation.OnGround(actor.Position));
                    actor.CarriedStackId = EntityId.None;
                }
                actor.SuspendCurrentJob();
                actor.JobKind = ActorJobKind.Collapsed;
                actor.JobPhase = ActorJobPhase.Working;
                actor.JobTarget = actor.Position;
                actor.RemainingWorkTicks = Math.Max(
                    1,
                    (actor.Fatigue + Definitions.RestRecoveryPerTick - 1) /
                    Definitions.RestRecoveryPerTick);
                Publish(SimulationEventKind.ActorCollapsed, actor.Id, EntityId.None, 0);
            }

            actor.Hunger = Math.Min(
                GoblinNeeds.MaximumHunger,
                checked(actor.Hunger + GoblinNeeds.HungerPerTick));
            actor.Thirst = Math.Min(
                GoblinNeeds.MaximumThirst,
                checked(actor.Thirst + GoblinNeeds.ThirstPerTick));

            if (actor.Thirst >= GoblinNeeds.DrinkThreshold && actor.PersonalWater > 0)
            {
                actor.PersonalWater--;
                actor.Thirst = Math.Max(0, actor.Thirst - Definitions.WaterHydration);
                Publish(SimulationEventKind.ActorDrank, actor.Id, EntityId.None, 1);
            }

            if (actor.Hunger >= GoblinNeeds.EatThreshold && actor.JobKind != ActorJobKind.Eat)
            {
                if (actor.PersonalFood > 0)
                {
                    var foodKind = actor.PersonalFoodKinds[0];
                    actor.PersonalFoodKinds.RemoveAt(0);
                    actor.PersonalFoodFreshUntilTicks.RemoveAt(0);
                    ApplyFoodEffects(actor, foodKind);
                    Publish(SimulationEventKind.ActorAte, actor.Id, EntityId.None, 1);
                }
                else
                {
                    TryFeed(actor);
                }
            }

            actor.Health = Math.Min(actor.Health, GetEffectiveMaximumHealth(actor));
            ApplyPassiveHealthRecovery(actor);

            if (actor.Hunger >= GoblinNeeds.StarvationHungerThreshold)
            {
                actor.Health = Math.Max(0, actor.Health - GoblinNeeds.StarvationDamagePerTick);
            }
            if (actor.Thirst >= GoblinNeeds.DehydrationThirstThreshold)
            {
                actor.Health = Math.Max(0, actor.Health - GoblinNeeds.DehydrationDamagePerTick);
            }

            if (actor.Health == 0)
            {
                deadActors.Add(actor);
            }

            _actorUpdates = checked(_actorUpdates + 1);
        }

        foreach (var actor in deadActors)
        {
            RemoveDeadActor(actor);
        }
    }

    private void TryShareNavigationReports(ActorState actor)
    {
        if (actor.PendingNavigationReports.Count == 0 || !IsRestLocation(actor.Position))
        {
            return;
        }

        foreach (var edge in OrderNavigationEdges(actor.PendingNavigationReports))
        {
            if (actor.NavigationKnowledge.TryGet(edge, out var belief))
            {
                _tribeNavigationKnowledge.ReceiveReport(belief, CurrentTick);
            }
        }
        actor.PendingNavigationReports.Clear();
    }

    private void RemoveDeadActor(ActorState actor)
    {
        actor.ClearJob();
        CreateGoblinCorpse(actor);

        _actors.Remove(actor.Id);
        foreach (var zone in _storageZones.Values.Where(zone =>
                     zone.AssignedHaulerId == actor.Id))
        {
            zone.AssignedHaulerId = EntityId.None;
            Publish(
                SimulationEventKind.StorageHaulerConfigured,
                EntityId.None,
                zone.Id,
                amount: 0);
        }
        Publish(SimulationEventKind.ActorDied, actor.Id, EntityId.None, 0);

        var canceledCommands = _pendingCommands
            .Where(pair => pair.Value.Subject == actor.Id)
            .ToArray();
        foreach (var pair in canceledCommands)
        {
            _pendingCommands.Remove(pair.Key);
            Publish(
                SimulationEventKind.CommandRejected,
                actor.Id,
                pair.Value.Target,
                (int)pair.Value.Kind);
        }
    }

    private bool TryFeed(ActorState actor)
    {
        var foodStack = _itemStacks.Values.FirstOrDefault(stack =>
            stack.Resource == ResourceKind.Food &&
            stack.Quantity > GetReservedItemQuantity(stack.Id, actor) &&
            IsAccessibleToActor(stack.Location, actor));

        if (foodStack is null)
        {
            return false;
        }

        foodStack.Quantity--;
        ApplyFoodEffects(actor, foodStack.FoodKind);
        if (actor.JobKind == ActorJobKind.Haul &&
            actor.JobStage == ActorJobStage.Delivering &&
            actor.CarriedStackId == foodStack.Id)
        {
            actor.ReservedQuantity = foodStack.Quantity;
        }
        Publish(SimulationEventKind.ActorAte, actor.Id, foodStack.Id, 1);

        if (foodStack.Quantity == 0)
        {
            RemoveItemStack(foodStack.Id);
            if (actor.CarriedStackId == foodStack.Id)
            {
                actor.CarriedStackId = EntityId.None;
                actor.ClearJob();
            }

            Publish(SimulationEventKind.ItemStackDepleted, actor.Id, foodStack.Id, 0);
        }

        return true;
    }

    private void ApplyPassiveHealthRecovery(ActorState actor)
    {
        var effectiveMaximumHealth = GetEffectiveMaximumHealth(actor);
        if (actor.Health <= 0 || actor.Health >= effectiveMaximumHealth ||
            actor.Hunger >= GoblinNeeds.CriticalHungerThreshold ||
            actor.Thirst >= GoblinNeeds.DehydrationThirstThreshold)
        {
            return;
        }

        if (CurrentTick.Value % Definitions.HealthRecovery.NaturalIntervalTicks == 0)
        {
            HealActor(actor, 1);
        }
        if (actor.JobKind == ActorJobKind.Rest && actor.JobPhase == ActorJobPhase.Working &&
            CurrentTick.Value % Definitions.HealthRecovery.SleepingBonusIntervalTicks == 0)
        {
            HealActor(actor, 1);
        }
    }

    private void ApplyFoodEffects(ActorState actor, FoodKind foodKind)
    {
        actor.Hunger = Math.Max(0, actor.Hunger - Definitions.Food.GetSatiety(foodKind));
        HealActor(actor, Definitions.HealthRecovery.GetFoodHealing(foodKind));
    }

    private void HealActor(ActorState actor, int amount)
    {
        var effectiveMaximumHealth = GetEffectiveMaximumHealth(actor);
        if (amount <= 0 || actor.Health <= 0 || actor.Health >= effectiveMaximumHealth)
        {
            return;
        }

        actor.Health = (int)Math.Min(
            effectiveMaximumHealth,
            checked((long)actor.Health + amount));
    }

    private static bool IsAccessibleToActor(ItemLocation location, ActorState actor) =>
        location.Kind switch
        {
            ItemLocationKind.ActorInventory => location.OwnerId == actor.Id,
            ItemLocationKind.Ground or ItemLocationKind.StorageZone => location.Position == actor.Position,
            _ => false,
        };

    private void Publish(
        SimulationEventKind kind,
        EntityId subject,
        EntityId target,
        int amount,
        ConstructionKind? construction = null)
    {
        var simulationEvent = new SimulationEvent(
            _nextEventSequence,
            CurrentTick,
            kind,
            subject,
            target,
            amount)
        {
            Construction = construction,
        };

        _nextEventSequence = checked(_nextEventSequence + 1);
        _undeliveredEvents.Add(simulationEvent);
        _eventsPublished = checked(_eventsPublished + 1);
    }

    private void ValidateCommandForQueue(
        SimulationCommand command,
        bool allowCurrentTick = false)
    {
        if (command.ExecuteAt.Value < CurrentTick.Value ||
            (!allowCurrentTick && command.ExecuteAt.Value == CurrentTick.Value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                $"Commands must target a future tick after {CurrentTick}.");
        }

        if (!Enum.IsDefined(command.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(command), $"Unsupported command kind {command.Kind}.");
        }

        if (command.Kind is not (SimulationCommandKind.RenameLogisticsNetwork or
                SimulationCommandKind.RenameStorageArea or
                SimulationCommandKind.ConfigureWorkTypePriority) &&
            !string.IsNullOrEmpty(command.Text))
        {
            throw new ArgumentException("This command does not accept text.", nameof(command));
        }

        if (command.Kind != SimulationCommandKind.Build &&
            command.MaterialVariant != ResourceVariant.None)
        {
            throw new ArgumentException("This command does not accept a material.", nameof(command));
        }

        switch (command.Kind)
        {
            case SimulationCommandKind.Forage:
                ValidateActor(command.Subject, command);
                if (command.Amount <= 0 || command.Amount > 100)
                {
                    throw new ArgumentOutOfRangeException(nameof(command), "Forage effort must be between 1 and 100.");
                }

                break;
            case SimulationCommandKind.CreateStorageZone:
                if (!World.IsTerrainTraversable(command.Position) ||
                    !IsStorageFilterResource(command.Resource) ||
                    command.Amount <= 0)
                {
                    throw new ArgumentException("Storage zone command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.PickUp:
                ValidateActor(command.Subject, command);
                if (!_itemStacks.ContainsKey(command.Target) ||
                    command.Amount <= 0 ||
                    command.Amount > Definitions.ActorCarryCapacity)
                {
                    throw new ArgumentException("Pick-up command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.StoreCarried:
                ValidateActor(command.Subject, command);
                if (!_storageZones.ContainsKey(command.Target))
                {
                    throw new ArgumentException("Storage zone does not exist.", nameof(command));
                }

                break;
            case SimulationCommandKind.Move:
                ValidateActor(command.Subject, command);
                if (!IsAddressableMapPosition(command.Position))
                {
                    throw new ArgumentException("Move destination is outside the map.", nameof(command));
                }

                break;
            case SimulationCommandKind.OrderActorFlee:
            case SimulationCommandKind.OrderActorSleep:
            case SimulationCommandKind.SuspendActorDispatcher:
                ValidateActor(command.Subject, command);
                if (command.Target != EntityId.None || command.Resource != ResourceKind.Any ||
                    command.Amount != 0)
                {
                    throw new ArgumentException("Actor context command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.EquipItem:
                ValidateActor(command.Subject, command);
                if (!_itemStacks.TryGetValue(command.Target, out var equipmentStack) ||
                    equipmentStack.Resource != ResourceKind.Equipment ||
                    command.Resource != ResourceKind.Equipment || command.Amount != 1)
                {
                    throw new ArgumentException("Equip-item command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.PrioritizeItemHauling:
                if (!_itemStacks.ContainsKey(command.Target) ||
                    command.Subject != EntityId.None || command.Resource != ResourceKind.Any ||
                    command.Amount != (int)StoragePriority.Urgent)
                {
                    throw new ArgumentException("Item-hauling priority command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.OrderItemPickup:
                ValidateActor(command.Subject, command);
                if (!_itemStacks.ContainsKey(command.Target) ||
                    command.Resource != ResourceKind.Any || command.Amount != 0)
                {
                    throw new ArgumentException("Ordered item pick-up command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.Build:
                if (!Enum.IsDefined(command.Construction) ||
                    command.Resource != ConstructionBlueprintDefinitions
                        .Get(command.Construction).RequiredResource ||
                    !IsPotentialConstructionPosition(command.Position) ||
                    !IsPotentialConstructionPosition(command.EndPosition))
                {
                    throw new ArgumentException("Construction command is invalid.", nameof(command));
                }

                if (command.MaterialVariant != ResourceVariant.None &&
                    (!MaterialCatalog.TryGet(command.MaterialVariant, out var material) ||
                     material.ResourceKind != command.Resource ||
                     !material.Uses.Contains(MaterialUse.Construction)))
                {
                    throw new ArgumentException(
                        "The selected construction material is invalid.", nameof(command));
                }

                if ((command.Construction is ConstructionKind.FoodStorage or ConstructionKind.WoodStorage or
                        ConstructionKind.StoneStorage or ConstructionKind.EquipmentStorage or
                        ConstructionKind.MaterialsStorage) &&
                    (command.Position != command.EndPosition || command.Amount != 2))
                {
                    throw new ArgumentException("Food storage construction is invalid.", nameof(command));
                }

                if (command.Construction == ConstructionKind.WaterBarrel &&
                    (command.Position != command.EndPosition || command.Amount != 1))
                {
                    throw new ArgumentException("Water-barrel placement is invalid.", nameof(command));
                }

                if (command.Construction is ConstructionKind.WoodenBox or
                        ConstructionKind.WoodenChest or ConstructionKind.WoodenBulkBin &&
                    (command.Position != command.EndPosition || command.Amount != 1))
                {
                    throw new ArgumentException(
                        "Storage-container placement is invalid.", nameof(command));
                }

                if ((command.Construction is ConstructionKind.WoodenWalkway or
                        ConstructionKind.BasaltWalkway) &&
                    (command.Amount != 1 || command.Position.Z != command.EndPosition.Z))
                {
                    throw new ArgumentException("Walkway construction is invalid.", nameof(command));
                }

                if (command.Construction == ConstructionKind.GoblinFieldCamp &&
                    (command.Amount != 6 ||
                     command.EndPosition.Z != command.Position.Z ||
                     command.EndPosition != command.Position with
                     {
                         X = command.Position.X + 1,
                         Y = command.Position.Y + 1,
                     }))
                {
                    throw new ArgumentException("Field-camp construction is invalid.", nameof(command));
                }

                if (command.Construction == ConstructionKind.GoblinHut &&
                    (command.Amount != 8 ||
                     command.EndPosition.Z != command.Position.Z ||
                     command.EndPosition != command.Position with
                     {
                         X = command.Position.X + 2,
                         Y = command.Position.Y + 2,
                     }))
                {
                    throw new ArgumentException("Goblin-hut construction is invalid.", nameof(command));
                }

                if (command.Construction == ConstructionKind.GoblinCompost &&
                    (command.Position != command.EndPosition || command.Amount != 2))
                {
                    throw new ArgumentException(
                        "Goblin-compost construction is invalid.",
                        nameof(command));
                }

                if (command.Construction == ConstructionKind.WoodenWatchtower &&
                    (command.Amount != 8 ||
                     command.EndPosition.Z != command.Position.Z ||
                     command.EndPosition != command.Position with
                     {
                         X = command.Position.X + 1,
                         Y = command.Position.Y + 1,
                     }))
                {
                    throw new ArgumentException(
                        "Wooden-watchtower construction is invalid.",
                        nameof(command));
                }

                if (command.Construction == ConstructionKind.ReedSleepingMat &&
                    (command.Position != command.EndPosition || command.Amount != 2))
                {
                    throw new ArgumentException(
                        "Reed-sleeping-mat construction is invalid.",
                        nameof(command));
                }

                if (command.Construction == ConstructionKind.WoodenWall &&
                    (command.Position.Z != command.EndPosition.Z ||
                     command.Amount != 2))
                {
                    throw new ArgumentException("Wooden-wall construction is invalid.", nameof(command));
                }

                if (command.Construction == ConstructionKind.StoneWall &&
                    (command.Position.Z != command.EndPosition.Z ||
                     command.Amount != 2))
                {
                    throw new ArgumentException("Stone-wall construction is invalid.", nameof(command));
                }

                if (command.Construction is ConstructionKind.WoodenFloor or
                        ConstructionKind.StoneFloor &&
                    (command.Amount != 1 ||
                     command.Position.Z != command.EndPosition.Z ||
                     command.MaterialVariant == ResourceVariant.None ||
                     SimulationCommand.GetAreaCells(command.Position, command.EndPosition).Count > 256))
                {
                    throw new ArgumentException("Floor construction is invalid.", nameof(command));
                }

                if (command.Construction is ConstructionKind.WoodenRamp or
                        ConstructionKind.StoneRamp &&
                    (command.Amount != (command.Construction == ConstructionKind.StoneRamp ? 3 : 2) ||
                     command.MaterialVariant == ResourceVariant.None ||
                     command.EndPosition.Z != command.Position.Z + 1 ||
                     Math.Abs(command.EndPosition.X - command.Position.X) +
                         Math.Abs(command.EndPosition.Y - command.Position.Y) != 1))
                {
                    throw new ArgumentException("Ramp construction is invalid.", nameof(command));
                }

                if (command.Construction == ConstructionKind.WoodenLadder &&
                    (command.Amount != 2 ||
                     command.EndPosition.Z != command.Position.Z + 1 ||
                     Math.Abs(command.EndPosition.X - command.Position.X) +
                         Math.Abs(command.EndPosition.Y - command.Position.Y) != 1))
                {
                    throw new ArgumentException(
                        "Wooden-ladder construction is invalid.", nameof(command));
                }

                if (command.Construction == ConstructionKind.WoodenDoorFrame &&
                    (command.Position != command.EndPosition ||
                     command.Amount != 1))
                {
                    throw new ArgumentException("Wooden door-frame construction is invalid.", nameof(command));
                }

                if (command.Construction == ConstructionKind.StoneDoorFrame &&
                    (command.Position != command.EndPosition ||
                     command.Amount != 1))
                {
                    throw new ArgumentException("Stone door-frame construction is invalid.", nameof(command));
                }

                if (command.Construction == ConstructionKind.WoodenDoor &&
                    (command.Position != command.EndPosition ||
                     command.Amount != 1))
                {
                    throw new ArgumentException("Wooden-door construction is invalid.", nameof(command));
                }

                if (command.Construction == ConstructionKind.WallTorch &&
                    (command.Position != command.EndPosition ||
                     command.Amount != 1))
                {
                    throw new ArgumentException("Wall-torch construction is invalid.", nameof(command));
                }

                if (command.Construction == ConstructionKind.StandingTorch &&
                    (command.Position != command.EndPosition ||
                     command.Amount != 1))
                {
                    throw new ArgumentException(
                        "Standing-torch construction is invalid.",
                        nameof(command));
                }

                if (ConstructionBlueprintCatalog.TryGetWorkshopKind(
                        command.Construction,
                        out var workshopKind) &&
                    (command.Position != command.EndPosition ||
                     command.Amount != WorkshopCatalog.Get(workshopKind)
                         .ConstructionRequirements.Sum(item => item.Quantity)))
                {
                    throw new ArgumentException(
                        "Workshop construction is invalid.",
                        nameof(command));
                }

                break;
            case SimulationCommandKind.DesignateWork:
                var designationKindCode = command.Amount & 0xff;
                var designationPriorityCode = (command.Amount >> 8) & 0xff;
                var hasUnknownDesignationFlags = (command.Amount & ~0x1ffff) != 0;
                var designatedKind = designationKindCode switch
                {
                    (int)WorkDesignationKind.FellTree => WorkDesignationKind.FellTree,
                    (int)WorkDesignationKind.QuarryBoulder => WorkDesignationKind.QuarryBoulder,
                    (int)WorkDesignationKind.MineRock => WorkDesignationKind.MineRock,
                    (int)WorkDesignationKind.Scout => WorkDesignationKind.Scout,
                    (int)WorkDesignationKind.HuntAnimal => WorkDesignationKind.HuntAnimal,
                    (int)WorkDesignationKind.CarveRampDown => WorkDesignationKind.CarveRampDown,
                    (int)WorkDesignationKind.CarveRampUp => WorkDesignationKind.CarveRampUp,
                    (int)WorkDesignationKind.CleanBlood => WorkDesignationKind.CleanBlood,
                    (int)WorkDesignationKind.GatherLichen => WorkDesignationKind.GatherLichen,
                    _ => command.Resource switch
                    {
                        ResourceKind.Food => WorkDesignationKind.GatherFood,
                        ResourceKind.Reeds => WorkDesignationKind.GatherReeds,
                        ResourceKind.Wood => WorkDesignationKind.GatherBrushwood,
                        ResourceKind.Stone => WorkDesignationKind.GatherStone,
                        ResourceKind.Vegetation => WorkDesignationKind.UprootBerryBush,
                        _ => default,
                    },
                };
                var isObjectExtraction = command.Resource == ResourceKind.Any &&
                    designationKindCode is (int)WorkDesignationKind.FellTree or
                        (int)WorkDesignationKind.QuarryBoulder or
                        (int)WorkDesignationKind.MineRock or
                        (int)WorkDesignationKind.Scout or
                        (int)WorkDesignationKind.HuntAnimal or
                        (int)WorkDesignationKind.CarveRampDown or
                        (int)WorkDesignationKind.CarveRampUp or
                        (int)WorkDesignationKind.CleanBlood;
                var isLegacyDesignation =
                    command.Resource is ResourceKind.Food or ResourceKind.Reeds or ResourceKind.Wood or
                        ResourceKind.Stone or ResourceKind.Vegetation &&
                    designationKindCode == 0;
                if ((!isObjectExtraction && !isLegacyDesignation) ||
                    hasUnknownDesignationFlags ||
                    (designationPriorityCode != 0 &&
                     !Enum.IsDefined((StoragePriority)(designationPriorityCode - 1))) ||
                    command.Target != EntityId.None ||
                    (command.Subject != EntityId.None && designatedKind == default) ||
                    (designatedKind is WorkDesignationKind.CarveRampDown or
                        WorkDesignationKind.CarveRampUp
                        ? !IsValidRampDesignation(
                            designatedKind,
                            command.Position,
                            command.EndPosition)
                        : !IsValidArea(command.Position, command.EndPosition)))
                {
                    throw new ArgumentException("Work designation command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.ClearWorkDesignations:
                if (command.Resource != ResourceKind.Any ||
                    !IsValidArea(command.Position, command.EndPosition))
                {
                    throw new ArgumentException("Clear-work command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.ClearWorkDesignationOrder:
                if (command.Resource != ResourceKind.Any ||
                    command.Target == EntityId.None || command.Amount != 0)
                {
                    throw new ArgumentException("Clear-work-order command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.ConfigureWorkPriority:
                var workPriority = (StoragePriority)command.Amount;
                if (command.Resource != ResourceKind.Any ||
                    command.Target == EntityId.None || !Enum.IsDefined(workPriority))
                {
                    throw new ArgumentException("Work-priority command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.ConfigureWorkSuspension:
                if (command.Resource != ResourceKind.Any ||
                    command.Target == EntityId.None || command.Amount is not (0 or 1))
                {
                    throw new ArgumentException("Work-suspension command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.ConfigureStoragePull:
                if (!_storageZones.TryGetValue(command.Target, out var zone) ||
                    command.Amount < 0 || command.Amount > zone.Capacity)
                {
                    throw new ArgumentException("Storage pull command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.ConfigureStorageHauler:
                if (!_storageZones.ContainsKey(command.Target) ||
                    (command.Subject != EntityId.None && !_actors.ContainsKey(command.Subject)) ||
                    command.Amount != 0)
                {
                    throw new ArgumentException("Storage hauler command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.ConfigureStorageSource:
                if (!_storageZones.TryGetValue(command.Target, out var destinationZone) ||
                    (command.Subject != EntityId.None &&
                     !TryGetCompatibleStorageSource(destinationZone, command.Subject, out _)) ||
                    command.Amount != 0)
                {
                    throw new ArgumentException("Storage source command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.ConfigureStoragePriority:
                if (!_storageZones.ContainsKey(command.Target) ||
                    !Enum.IsDefined((StoragePriority)command.Amount))
                {
                    throw new ArgumentException("Storage priority command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.ConfigureStorageMineralFilter:
                if (!_storageZones.TryGetValue(command.Target, out var mineralZone) ||
                    mineralZone.AcceptedResource != ResourceKind.Stone ||
                    command.Resource != ResourceKind.Stone ||
                    !IsValidMineralFilter((MineralStorageFilter)command.Amount))
                {
                    throw new ArgumentException("Storage mineral-filter command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.CreateLogisticsNetwork:
                if (command.Subject != EntityId.None || command.Target != EntityId.None ||
                    command.Resource != ResourceKind.Any || command.Amount != 0)
                {
                    throw new ArgumentException(
                        "Create-logistics-network command is invalid.",
                        nameof(command));
                }

                break;
            case SimulationCommandKind.ConfigureLogisticsHauler:
                if (command.Target == EntityId.None ||
                    !_logisticsNetworks.ContainsKey(command.Target) ||
                    !_actors.ContainsKey(command.Subject) ||
                    command.Resource != ResourceKind.Any || command.Amount is not (0 or 1))
                {
                    throw new ArgumentException(
                        "Logistics-hauler command is invalid.",
                        nameof(command));
                }

                break;
            case SimulationCommandKind.ConfigureLogisticsSource:
                if (command.Target == EntityId.None ||
                    !_logisticsNetworks.ContainsKey(command.Target) ||
                    !_storageZones.ContainsKey(command.Subject) ||
                    command.Resource != ResourceKind.Any || command.Amount is not (0 or 1))
                {
                    throw new ArgumentException(
                        "Logistics-source command is invalid.",
                        nameof(command));
                }

                break;
            case SimulationCommandKind.ConfigureStorageNetwork:
                if (!_storageZones.ContainsKey(command.Target) ||
                    !_logisticsNetworks.ContainsKey(command.Subject) ||
                    command.Resource != ResourceKind.Any || command.Amount != 0)
                {
                    throw new ArgumentException(
                        "Storage-network command is invalid.",
                        nameof(command));
                }

                break;
            case SimulationCommandKind.CreateStorageArea:
                if (command.Target != EntityId.None ||
                    !_logisticsNetworks.ContainsKey(command.Subject) ||
                    command.Resource != ResourceKind.Any || command.Amount != 0 ||
                    !IsValidArea(command.Position, command.EndPosition))
                {
                    throw new ArgumentException(
                        "Create-storage-area command is invalid.",
                        nameof(command));
                }

                var storageAreaCells = EnumerateAreaCells(
                    command.Position,
                    command.EndPosition).ToArray();
                if (storageAreaCells.Length > 256 ||
                    storageAreaCells.Any(cell => !World.IsTerrainTraversable(cell)))
                {
                    throw new ArgumentException(
                        "Storage area must contain at most 256 traversable cells.",
                        nameof(command));
                }

                break;
            case SimulationCommandKind.ConfigureStorageAreaNetwork:
                if (!_storageAreas.ContainsKey(command.Target) ||
                    !_logisticsNetworks.ContainsKey(command.Subject) ||
                    command.Resource != ResourceKind.Any || command.Amount != 0)
                {
                    throw new ArgumentException(
                        "Storage-area-network command is invalid.",
                        nameof(command));
                }

                break;
            case SimulationCommandKind.RenameLogisticsNetwork:
                if (command.Subject != EntityId.None || command.Target == EntityId.None ||
                    !_logisticsNetworks.ContainsKey(command.Target) ||
                    command.Resource != ResourceKind.Any || command.Amount != 0 ||
                    !IsValidManagementName(command.Text))
                {
                    throw new ArgumentException(
                        "Rename-logistics-network command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.RenameStorageArea:
                if (command.Subject != EntityId.None ||
                    !_storageAreas.ContainsKey(command.Target) ||
                    command.Resource != ResourceKind.Any || command.Amount != 0 ||
                    !IsValidManagementName(command.Text))
                {
                    throw new ArgumentException(
                        "Rename-storage-area command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.ConfigureStorageFilter:
                if (command.Subject != EntityId.None ||
                    !_storageZones.TryGetValue(command.Target, out var filterZone) ||
                    filterZone.ProviderKind is not (StorageProviderKind.WoodenBox or
                        StorageProviderKind.WoodenChest or StorageProviderKind.WoodenBulkBin) ||
                    !IsSolidContainerFilter(command.Resource) || command.Amount != 0)
                {
                    throw new ArgumentException(
                        "Storage-filter command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.ConfigureStorageFilterResource:
                if (command.Subject != EntityId.None ||
                    !_storageZones.TryGetValue(command.Target, out var resourceFilterZone) ||
                    resourceFilterZone.ProviderKind is not (StorageProviderKind.WoodenBox or
                        StorageProviderKind.WoodenChest or StorageProviderKind.WoodenBulkBin) ||
                    !IsSolidContainerFilter(command.Resource) ||
                    command.Position != default || command.EndPosition != default ||
                    command.Construction != default || command.Amount is not (0 or 1))
                {
                    throw new ArgumentException(
                        "Storage-filter-resource command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.DeleteLogisticsNetwork:
                if (command.Subject != EntityId.None || command.Target == EntityId.None ||
                    !_logisticsNetworks.ContainsKey(command.Target) ||
                    command.Position != default || command.EndPosition != default ||
                    command.Construction != default || command.Resource != ResourceKind.Any ||
                    command.Amount != 0)
                {
                    throw new ArgumentException(
                        "Delete-logistics-network command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.ResizeStorageArea:
                if (command.Subject != EntityId.None ||
                    !_storageAreas.TryGetValue(command.Target, out var resizedArea) ||
                    command.Construction != default || command.Resource != ResourceKind.Any ||
                    command.Amount != 0 || !IsValidArea(command.Position, command.EndPosition) ||
                    (resizedArea.Footprint.Length > 0 &&
                     command.Position.Z != resizedArea.Footprint[0].Z))
                {
                    throw new ArgumentException(
                        "Resize-storage-area command is invalid.", nameof(command));
                }

                var resizedAreaCells = EnumerateAreaCells(
                    command.Position,
                    command.EndPosition).ToArray();
                if (resizedAreaCells.Length > 256 ||
                    resizedAreaCells.Any(cell => !World.IsTerrainTraversable(cell)))
                {
                    throw new ArgumentException(
                        "Storage area must contain at most 256 traversable cells.",
                        nameof(command));
                }

                break;
            case SimulationCommandKind.DissolveStorageArea:
                if (command.Subject != EntityId.None ||
                    !_storageAreas.ContainsKey(command.Target) ||
                    command.Position != default || command.EndPosition != default ||
                    command.Construction != default || command.Resource != ResourceKind.Any ||
                    command.Amount != 0)
                {
                    throw new ArgumentException(
                        "Dissolve-storage-area command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.CancelConstruction:
                if (command.Subject != EntityId.None || command.Target == EntityId.None ||
                    !_constructionSites.ContainsKey(command.Target) ||
                    command.Position != default || command.EndPosition != default ||
                    command.Construction != default || command.Resource != ResourceKind.Any ||
                    command.Amount != 0)
                {
                    throw new ArgumentException(
                        "Cancel-construction command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.DismantleConstruction:
                var dismantleKind = (DismantleTargetKind)command.Amount;
                var dismantleWorldObject = dismantleKind == DismantleTargetKind.WorldObject
                    ? World.CreateWorldObjectSnapshot().FirstOrDefault(candidate =>
                        candidate.Id.Value == command.Target.Value)
                    : null;
                var validDismantleTarget = dismantleKind switch
                {
                    DismantleTargetKind.WorldObject =>
                        dismantleWorldObject is not null &&
                        dismantleWorldObject.Owner == WorldObjectOwner.GoblinTribe &&
                        dismantleWorldObject.Anchor == command.Position,
                    DismantleTargetKind.StorageZone =>
                        _storageZones.TryGetValue(command.Target, out var dismantleZone) &&
                        dismantleZone.Position == command.Position,
                    _ => false,
                };
                if (command.Subject != EntityId.None || command.Target == EntityId.None ||
                    command.Position != command.EndPosition ||
                    command.Construction != default || command.Resource != ResourceKind.Any ||
                    !validDismantleTarget)
                {
                    throw new ArgumentException(
                        "Dismantle-construction command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.ConfigureResourcePriority:
                if (command.Subject != EntityId.None || command.Target != EntityId.None ||
                    !_resourcePriorities.ContainsKey(command.Resource) ||
                    !Enum.IsDefined((StoragePriority)command.Amount))
                {
                    throw new ArgumentException("Resource priority command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.ConfigureWorkTypePriority:
                if (command.Subject != EntityId.None || command.Target != EntityId.None ||
                    command.Position != default || command.EndPosition != default ||
                    command.Construction != default || command.Resource != ResourceKind.Any ||
                    !WorkTypePriorityCatalog.TryGet(command.Text, out _) ||
                    !Enum.IsDefined((StoragePriority)command.Amount))
                {
                    throw new ArgumentException(
                        "Work-type priority command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.AttackHumanVillage:
                if (command.Subject != EntityId.None || command.Target != EntityId.None ||
                    command.Position != command.EndPosition ||
                    command.Construction != default || command.Resource != ResourceKind.Any ||
                    command.Amount != 0 ||
                    (command.Position != default &&
                     !World.CreateWorldObjectSnapshot().Any(item =>
                         item.Kind == WorldObjectKind.GoblinFieldCamp &&
                         item.Owner == WorldObjectOwner.GoblinTribe &&
                         item.Anchor == command.Position)))
                {
                    throw new ArgumentException("Human-village attack command is invalid.", nameof(command));
                }
                break;
            case SimulationCommandKind.ConfigureRaidMember:
                ValidateActor(command.Subject, command);
                if (command.Target != EntityId.None ||
                    command.Position != default || command.EndPosition != default ||
                    command.Resource != ResourceKind.Any || command.Amount is not (0 or 1))
                {
                    throw new ArgumentException("Raid-member command is invalid.", nameof(command));
                }
                break;
            case SimulationCommandKind.SuspendRaidPreparation:
            case SimulationCommandKind.LaunchRaid:
                if (command.Subject != EntityId.None || command.Target != EntityId.None ||
                    command.Position != default || command.EndPosition != default ||
                    command.Construction != default || command.Resource != ResourceKind.Any ||
                    command.Amount != 0)
                {
                    throw new ArgumentException("Raid-control command is invalid.", nameof(command));
                }
                break;
            case SimulationCommandKind.ConfigureRaidTarget:
                if (command.Subject != EntityId.None || command.Target != EntityId.None ||
                    command.Position != command.EndPosition ||
                    !IsAddressableMapPosition(command.Position) ||
                    command.Construction != default || command.Resource != ResourceKind.Any ||
                    command.Amount is < MinimumRaidTargetRadius or > MaximumRaidTargetRadius)
                {
                    throw new ArgumentException("Raid-target command is invalid.", nameof(command));
                }
                break;
            case SimulationCommandKind.ConfigureRaidDirectives:
                if (command.Subject != EntityId.None || command.Target != EntityId.None ||
                    command.Position != default || command.EndPosition != default ||
                    command.Construction != default || command.Resource != ResourceKind.Any ||
                    !AreValidRaidDirectives((RaidDirective)command.Amount))
                {
                    throw new ArgumentException("Raid-directive command is invalid.", nameof(command));
                }
                break;
            case SimulationCommandKind.ConfigureCorpseDirectives:
                if (command.Subject != EntityId.None ||
                    !_corpses.ContainsKey(command.Target) ||
                    command.Position != default || command.EndPosition != default ||
                    command.Construction != default || command.Resource != ResourceKind.Any ||
                    !AreValidCorpseDirectives((CorpseDirective)command.Amount))
                {
                    throw new ArgumentException("Corpse-directive command is invalid.", nameof(command));
                }
                break;
            case SimulationCommandKind.OrderPatrol:
                ValidateActor(command.Subject, command);
                if (command.Target != EntityId.None ||
                    command.Position != command.EndPosition ||
                    command.Construction != default || command.Resource != ResourceKind.Any ||
                    command.Amount is not (0 or 1) ||
                    !World.IsTerrainReachable(command.Position))
                {
                    throw new ArgumentException("Patrol command is invalid.", nameof(command));
                }
                break;
            case SimulationCommandKind.OrderAttackArea:
            case SimulationCommandKind.OrderHuntArea:
                ValidateActor(command.Subject, command);
                if (command.Target != EntityId.None ||
                    command.Position != command.EndPosition ||
                    command.Construction != default || command.Resource != ResourceKind.Any ||
                    command.Amount is < MinimumRaidTargetRadius or > MaximumRaidTargetRadius ||
                    !IsAddressableMapPosition(command.Position))
                {
                    throw new ArgumentException("Tactical-area command is invalid.", nameof(command));
                }
                break;
            case SimulationCommandKind.DesignateHuntArea:
                if (command.Subject != EntityId.None || command.Target != EntityId.None ||
                    command.Position != command.EndPosition ||
                    command.Construction != default || command.Resource != ResourceKind.Any ||
                    command.Amount is < MinimumRaidTargetRadius or > MaximumRaidTargetRadius ||
                    !IsAddressableMapPosition(command.Position))
                {
                    throw new ArgumentException(
                        "Hunt-area designation command is invalid.", nameof(command));
                }
                break;
            case SimulationCommandKind.ToggleWoodenDoor:
                if (command.Subject != EntityId.None ||
                    command.Target != EntityId.None ||
                    command.Position != command.EndPosition ||
                    command.Resource != ResourceKind.Any ||
                    command.Amount != 0)
                {
                    throw new ArgumentException("Wooden-door toggle command is invalid.", nameof(command));
                }
                break;
            case SimulationCommandKind.QueueCraftingOrder:
                if (command.Subject != EntityId.None || command.Target != EntityId.None ||
                    command.Position != command.EndPosition ||
                    command.Resource != ResourceKind.Any ||
                    !Enum.IsDefined((CraftingRecipeKind)command.Amount))
                {
                    throw new ArgumentException("Crafting-order command is invalid.", nameof(command));
                }
                break;
            case SimulationCommandKind.ConfigureRepeatingCraftingOrder:
                if (command.Subject != EntityId.None || command.Target != EntityId.None ||
                    command.Position != command.EndPosition ||
                    command.Resource != ResourceKind.Any || command.Amount == 0 ||
                    !Enum.IsDefined((CraftingRecipeKind)Math.Abs(command.Amount)))
                {
                    throw new ArgumentException(
                        "Repeating crafting-order command is invalid.", nameof(command));
                }
                break;
        }
    }

    private void ValidateActor(EntityId actorId, SimulationCommand command)
    {
        if (actorId == EntityId.None || !_actors.ContainsKey(actorId))
        {
            throw new ArgumentException($"Command subject {actorId} does not exist.", nameof(command));
        }
    }

    private ActorState AllocateActor(
        GridPosition position,
        int hunger = 0,
        int? health = null)
    {
        var id = AllocateEntityId();
        const ActorSex sex = ActorSex.Sexless;
        var actor = new ActorState(id, position, hunger)
        {
            Name = CreateGoblinName(id, sex),
            Sex = sex,
            KnownSkills = _goblinActorGenerator.CreateSkills(id),
            KnownTraits = _goblinActorGenerator.CreateTraits(id),
            Equipment = _goblinActorGenerator.CreateEquipment(id),
            WorkPreferences = _goblinActorGenerator.CreateWorkPreferences(id),
            Health = health ?? GoblinVitals.MaximumHealth,
            PersonalWater = Definitions.PersonalWaterCapacity,
            AgeOffsetTicks = CreateInitialAgeOffsetTicks(id),
        };
        _actors.Add(id, actor);
        return actor;
    }

    private ItemStackState AllocateItemStack(
        ResourceKind resource,
        int quantity,
        ItemLocation location,
        FoodKind foodKind = FoodKind.None,
        ResourceVariant variant = ResourceVariant.None,
        long? freshUntilTick = null)
    {
        if (resource == ResourceKind.Food && foodKind == FoodKind.None)
        {
            foodKind = FoodKind.DriedRations;
        }

        variant = NormalizeResourceVariant(resource, variant);
        freshUntilTick = resource == ResourceKind.Food
            ? freshUntilTick ?? FoodPreservationPolicy.GetFreshUntilTick(
                foodKind,
                CurrentTick,
                Definitions.Clock)
            : null;
        var id = AllocateEntityId();
        var stack = new ItemStackState(
            id,
            resource,
            foodKind,
            variant,
            quantity,
            location,
            freshUntilTick);
        _itemStacks.Add(id, stack);
        IndexItemStack(stack);
        return stack;
    }

    private StorageZoneState AllocateStorageZone(
        GridPosition position,
        ResourceKind acceptedResource,
        int capacity,
        int desiredQuantity = 0,
        EntityId assignedHaulerId = default,
        EntityId sourceStorageZoneId = default,
        StoragePriority priority = StoragePriority.Normal,
        StorageSlotPolicy? slotPolicy = null,
        StorageProviderKind providerKind = StorageProviderKind.OpenPile)
    {
        var id = AllocateEntityId();
        var area = _storageAreas.Values.FirstOrDefault(candidate =>
            candidate.Footprint.Contains(position));
        if (area is null)
        {
            area = new StorageAreaState(
                id,
                $"Storage {id.Value}",
                [position],
                EntityId.None);
            _storageAreas.Add(area.Id, area);
        }
        var zone = new StorageZoneState(
            id,
            position,
            acceptedResource,
            capacity,
            desiredQuantity,
            assignedHaulerId,
            sourceStorageZoneId,
            priority,
            slotPolicy: slotPolicy ?? CreateDefaultStorageSlotPolicy(acceptedResource, capacity),
            logisticsNetworkId: area.LogisticsNetworkId,
            storageAreaId: area.Id,
            providerKind: providerKind,
            resourceFilter: CreateStorageResourceFilter(acceptedResource));
        _storageZones.Add(id, zone);
        IndexStorageZone(zone);
        return zone;
    }

    private void MoveItemStack(ItemStackState stack, ItemLocation location)
    {
        stack.Location = location;
        if (location.Kind == ItemLocationKind.ActorInventory)
        {
            stack.HaulPriority = StoragePriority.Normal;
        }
        IndexItemStack(stack);
    }

    private bool RemoveItemStack(EntityId stackId)
    {
        if (!_itemStacks.Remove(stackId))
        {
            return false;
        }

        _resourceSpatialIndex.RemoveStack(stackId);
        return true;
    }

    private void IndexItemStack(ItemStackState stack) =>
        _resourceSpatialIndex.UpsertStack(stack.Id, stack.Resource, stack.Location);

    private void IndexStorageZone(StorageZoneState zone) =>
        _resourceSpatialIndex.UpsertStorageNode(
            zone.Id,
            zone.Position,
            zone.AcceptedResource,
            zone.SourceStorageZoneId);

    private EntityId AllocateEntityId()
    {
        var id = new EntityId(_nextEntityId);
        _nextEntityId = checked(_nextEntityId + 1);
        return id;
    }

    private ItemStackState? FindMergeableGroundStack(
        ResourceKind resource,
        GridPosition position,
        FoodKind foodKind = FoodKind.None,
        ResourceVariant variant = ResourceVariant.None) =>
        _itemStacks.Values.FirstOrDefault(stack =>
            stack.Resource == resource &&
            (resource != ResourceKind.Food || stack.FoodKind ==
                (foodKind == FoodKind.None ? FoodKind.DriedRations : foodKind)) &&
            stack.Variant == NormalizeResourceVariant(resource, variant) &&
            stack.Location == ItemLocation.OnGround(position));

    private int GetTotalResourceQuantity(ResourceKind resource) => checked(
        _itemStacks.Values
            .Where(stack => stack.Resource == resource)
            .Sum(stack => stack.Quantity) +
        (resource == ResourceKind.Food
            ? _actors.Values.Sum(actor => actor.PersonalFood)
            : 0));

    private string CreateGoblinName(EntityId id, ActorSex sex)
    {
        var definition = CivilizationCatalog.Current.Get(
            CivilizationLegacyRole.PlayerGoblins);
        return NameGeneratorCatalog.Current.Get(definition.Identity.NameGeneratorId).Generate(new(
            WorldSeed,
            id.Value,
            Ordinal: 0,
            _actors.Values.Select(actor => actor.Name)
                .ToHashSet(StringComparer.Ordinal),
            sex));
    }

    private void EnsureTribeHasStarterAxe()
    {
        if (_actors.Count == 0 || _actors.Values.Any(actor =>
                actor.Equipment.HasFlag(PersonalEquipment.WoodenAxe)))
        {
            return;
        }

        var logger = _actors.Values.First();
        logger.Equipment |= PersonalEquipment.WoodenAxe;
        logger.KnownSkills |= GoblinSkill.Building;
    }

    private void EnsureTribeHasStarterPickaxe()
    {
        const int starterPickaxeTarget = 3;
        var missing = Math.Min(starterPickaxeTarget, _actors.Count) -
            _actors.Values.Count(actor =>
                actor.Equipment.HasFlag(PersonalEquipment.PrimitivePickaxe));
        if (missing <= 0)
        {
            return;
        }

        foreach (var miner in _actors.Values
                     .Where(actor =>
                         !actor.Equipment.HasFlag(PersonalEquipment.PrimitivePickaxe))
                     .OrderBy(actor =>
                         actor.Equipment.HasFlag(PersonalEquipment.WoodenAxe) ? 1 : 0)
                     .ThenBy(actor => actor.Id)
                     .Take(missing))
        {
            miner.Equipment |= PersonalEquipment.PrimitivePickaxe;
            miner.KnownSkills |= GoblinSkill.Building;
        }
    }

    private void ScatterInitialBrushwood()
    {
        for (var y = 0; y < Map.Height; y++)
        {
            for (var x = 0; x < Map.Width; x++)
            {
                var column = new GridPosition(x, y);
                var position = Map.GetTerrainSurfacePosition(column);
                var cell = Map.GetCell(column);
                if (cell.Terrain is not (TerrainKind.SolidGround or TerrainKind.Mud) ||
                    cell.Fertility < 45 ||
                    cell.RampDirection != TerrainRampDirection.None ||
                    !World.IsTerrainTraversable(position))
                {
                    continue;
                }

                var subject = new EntityId(checked((ulong)(y * Map.Width + x) + 1));
                var occurrence = DeterministicRandom.NextInt(
                    WorldSeed,
                    RandomDomain.Brushwood,
                    subject,
                    SimulationTick.Zero,
                    sampleKey: 1,
                    minimumInclusive: 0,
                    maximumExclusive: 100);
                if (occurrence >= 3)
                {
                    continue;
                }

                var quantity = DeterministicRandom.NextInt(
                    WorldSeed,
                    RandomDomain.Brushwood,
                    subject,
                    SimulationTick.Zero,
                    sampleKey: 2,
                    minimumInclusive: 1,
                    maximumExclusive: 4);
                AllocateItemStack(
                    ResourceKind.Wood,
                    quantity,
                    ItemLocation.OnGround(position),
                    variant: WoodVariantFor(position));
            }
        }

        var nearbyBrushwood = _itemStacks.Values
            .Where(stack =>
            stack.Resource == ResourceKind.Wood &&
            stack.Location.Kind == ItemLocationKind.Ground &&
            ManhattanDistance(stack.Location.Position, Map.GoblinSpawn) <=
                GoblinPerception.DayVisionRadius)
            .Sum(stack => stack.Quantity);
        if (nearbyBrushwood >= 4)
        {
            return;
        }

        var fallback = Enumerable.Range(0, Map.Height)
            .SelectMany(y => Enumerable.Range(0, Map.Width)
                .Select(x => new GridPosition(x, y)))
            .Select(Map.GetTerrainSurfacePosition)
            .Where(position =>
                Map.GetColumnCell(position).Terrain is TerrainKind.SolidGround or TerrainKind.Mud &&
                Map.GetColumnCell(position).RampDirection == TerrainRampDirection.None &&
                World.IsTerrainTraversable(position))
            .OrderBy(position => ManhattanDistance(position, Map.GoblinSpawn))
            .ThenBy(position => position.Y)
            .ThenBy(position => position.X)
            .First();
        AllocateItemStack(
            ResourceKind.Wood,
            4 - nearbyBrushwood,
            ItemLocation.OnGround(fallback),
            variant: WoodVariantFor(fallback));
    }

    private void ScatterInitialStones()
    {
        for (var y = 0; y < Map.Height; y++)
        {
            for (var x = 0; x < Map.Width; x++)
            {
                var column = new GridPosition(x, y);
                var position = Map.GetTerrainSurfacePosition(column);
                var cell = Map.GetCell(column);
                if (cell.Terrain is not (TerrainKind.SolidGround or TerrainKind.Mud) ||
                    cell.RampDirection != TerrainRampDirection.None ||
                    !World.IsTerrainTraversable(position))
                {
                    continue;
                }

                var subject = new EntityId(checked((ulong)(y * Map.Width + x) + 1));
                var occurrence = DeterministicRandom.NextInt(
                    WorldSeed,
                    RandomDomain.Stone,
                    subject,
                    SimulationTick.Zero,
                    sampleKey: 0x53544F4E45UL,
                    minimumInclusive: 0,
                    maximumExclusive: 100);
                if (occurrence >= 2)
                {
                    continue;
                }

                var quantity = DeterministicRandom.NextInt(
                    WorldSeed,
                    RandomDomain.Stone,
                    subject,
                    SimulationTick.Zero,
                    sampleKey: 0x53544F4E46UL,
                    minimumInclusive: 1,
                    maximumExclusive: 4);
                AllocateItemStack(
                    ResourceKind.Stone,
                    quantity,
                    ItemLocation.OnGround(position),
                    variant: StoneVariantFor(position));
            }
        }

        if (_itemStacks.Values.Any(stack =>
                stack.Resource == ResourceKind.Stone &&
                stack.Location.Kind == ItemLocationKind.Ground &&
                ManhattanDistance(stack.Location.Position, Map.GoblinSpawn) <=
                    GoblinPerception.DayVisionRadius))
        {
            return;
        }

        var fallback = Enumerable.Range(0, Map.Height)
            .SelectMany(y => Enumerable.Range(0, Map.Width)
                .Select(x => new GridPosition(x, y)))
            .Select(Map.GetTerrainSurfacePosition)
            .Where(position =>
                Map.GetColumnCell(position).RampDirection == TerrainRampDirection.None &&
                World.IsTerrainTraversable(position))
            .OrderBy(position => ManhattanDistance(position, Map.GoblinSpawn))
            .ThenBy(position => position.Y)
            .ThenBy(position => position.X)
            .First();
        AllocateItemStack(
            ResourceKind.Stone,
            3,
            ItemLocation.OnGround(fallback),
            variant: StoneVariantFor(fallback));
    }

    private ActorState? FindNearestBuilder(GridPosition position) =>
        _actors.Values
            .OrderBy(actor => ManhattanDistance(actor.Position, position))
            .ThenBy(actor => actor.Id)
            .FirstOrDefault();

    private static void GainForagingExperience(ActorState actor, int amount)
    {
        actor.KnownSkills |= GoblinSkill.Foraging;
        actor.ForagingExperience = checked(actor.ForagingExperience + amount);
    }

    private static void GainHaulingExperience(ActorState actor, int amount)
    {
        actor.KnownSkills |= GoblinSkill.Hauling;
        actor.HaulingExperience = checked(actor.HaulingExperience + amount);
    }

    private static void GainBuildingExperience(ActorState actor, int amount)
    {
        actor.KnownSkills |= GoblinSkill.Building;
        actor.BuildingExperience = checked(actor.BuildingExperience + amount);
    }

    private static int ManhattanDistance(GridPosition first, GridPosition second) =>
        Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y) + Math.Abs(first.Z - second.Z);

    private bool IsValidArea(GridPosition first, GridPosition second) =>
        first.Z == second.Z &&
        IsAddressableMapPosition(first) &&
        IsAddressableMapPosition(second);

    private bool IsValidRampDesignation(
        WorkDesignationKind kind,
        GridPosition origin,
        GridPosition destination) =>
        IsAddressableMapPosition(origin) &&
        (IsAddressableMapPosition(destination) ||
         destination.Z == Map.MinimumWorldLevel - 1 && Map.IsColumnWithin(destination)) &&
        Math.Abs(destination.X - origin.X) + Math.Abs(destination.Y - origin.Y) <= 1 &&
        destination.Z == origin.Z +
            (kind == WorkDesignationKind.CarveRampDown ? -1 : 1);

    private bool IsAddressableMapPosition(GridPosition position) =>
        Map.IsWorldPosition(position);

    private static (GridPosition Minimum, GridPosition Maximum) NormalizeArea(
        GridPosition first,
        GridPosition second) =>
        (new GridPosition(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y), first.Z),
         new GridPosition(Math.Max(first.X, second.X), Math.Max(first.Y, second.Y), first.Z));

    private static IEnumerable<GridPosition> EnumerateAreaCells(
        GridPosition first,
        GridPosition second)
    {
        var (minimum, maximum) = NormalizeArea(first, second);
        for (var y = minimum.Y; y <= maximum.Y; y++)
        {
            for (var x = minimum.X; x <= maximum.X; x++)
            {
                yield return new GridPosition(x, y, minimum.Z);
            }
        }
    }

    private static bool IsInside(
        GridPosition position,
        GridPosition minimum,
        GridPosition maximum) =>
        position.Z == minimum.Z &&
        position.X >= minimum.X && position.X <= maximum.X &&
        position.Y >= minimum.Y && position.Y <= maximum.Y;

    private bool IsWorkDesignated(WorkDesignationKind kind, GridPosition position) =>
        _workDesignations.Values.Any(designation =>
            designation.Kind == kind && !designation.IsSuspended &&
            designation.Matches(position));

    private bool IsWorkDesignated(
        WorkDesignationKind kind,
        EntityId targetEntityId,
        GridPosition position) =>
        _workDesignations.Values.Any(designation =>
            designation.Kind == kind && !designation.IsSuspended &&
            designation.Target == position &&
            designation.TargetEntityId == targetEntityId);

    private void CancelJobsInClearedArea(
        GridPosition minimum,
        GridPosition maximum,
        IReadOnlySet<EntityId> removedDesignationIds)
    {
        foreach (var actor in _actors.Values)
        {
            var targetWasCleared = actor.JobTarget.X >= minimum.X && actor.JobTarget.X <= maximum.X &&
                actor.JobTarget.Y >= minimum.Y && actor.JobTarget.Y <= maximum.Y &&
                actor.JobTarget.Z == minimum.Z;
            var sourceWasCleared = actor.SourceStackId != EntityId.None &&
                _itemStacks.TryGetValue(actor.SourceStackId, out var source) &&
                source.Location.Kind == ItemLocationKind.Ground &&
                source.Location.Position.X >= minimum.X && source.Location.Position.X <= maximum.X &&
                source.Location.Position.Y >= minimum.Y && source.Location.Position.Y <= maximum.Y &&
                source.Location.Position.Z == minimum.Z;
            if ((actor.JobKind is ActorJobKind.Forage or ActorJobKind.ClearVegetation && targetWasCleared) ||
                (actor.JobKind is ActorJobKind.FellTree or ActorJobKind.QuarryBoulder or
                    ActorJobKind.MineRock &&
                 removedDesignationIds.Contains(actor.SourceStackId)) ||
                (actor.JobKind == ActorJobKind.Haul &&
                 actor.JobStage == ActorJobStage.Collecting &&
                 sourceWasCleared))
            {
                actor.ClearJob();
            }
        }
    }

    private static bool HasOnlyKnownFlags<T>(T value, T highestFlag)
        where T : struct, Enum
    {
        var numericValue = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        var allFlags = (Convert.ToUInt64(highestFlag, CultureInfo.InvariantCulture) << 1) - 1;
        return (numericValue & ~allFlags) == 0;
    }

    private int GetStoredQuantity(EntityId zoneId) =>
        _itemStacks.Values
            .Where(stack =>
                stack.Location.Kind == ItemLocationKind.StorageZone &&
                stack.Location.OwnerId == zoneId)
            .Sum(stack => stack.Quantity);

    private ItemStackState StoreStackInZone(ItemStackState stack, StorageZoneState zone)
    {
        var existing = _itemStacks.Values.FirstOrDefault(item =>
            item.Id != stack.Id &&
            item.Resource == stack.Resource &&
            item.FoodKind == stack.FoodKind &&
            item.Variant == stack.Variant &&
            item.Location.Kind == ItemLocationKind.StorageZone &&
            item.Location.OwnerId == zone.Id);
        if (existing is null)
        {
            MoveItemStack(stack, ItemLocation.StoredIn(zone.Id, zone.Position));
            return stack;
        }

        existing.Quantity = checked(existing.Quantity + stack.Quantity);
        if (existing.FreshUntilTick is { } existingFreshUntil &&
            stack.FreshUntilTick is { } incomingFreshUntil)
        {
            existing.FreshUntilTick = Math.Min(existingFreshUntil, incomingFreshUntil);
        }
        RemoveItemStack(stack.Id);
        return existing;
    }

    private int GetUsedTypeSlots(EntityId zoneId) =>
        _itemStacks.Values
            .Where(stack => stack.Location.Kind == ItemLocationKind.StorageZone &&
                stack.Location.OwnerId == zoneId)
            .Select(GetStorageTypeKey)
            .Distinct()
            .Count();

    private bool CanStoreStack(StorageZoneState zone, ItemStackState stack, int quantity)
    {
        if (!ZoneAccepts(zone, stack) ||
            !zone.SlotPolicy.Supports(GetStorageRequirement(stack)) ||
            GetStoredQuantity(zone.Id) + quantity > zone.Capacity)
        {
            return false;
        }

        if (!zone.SlotPolicy.SeparatesItemTypes)
        {
            return true;
        }

        var typeKey = GetStorageTypeKey(stack);
        var storedOfKind = _itemStacks.Values
            .Where(item => item.Location.Kind == ItemLocationKind.StorageZone &&
                item.Location.OwnerId == zone.Id && GetStorageTypeKey(item) == typeKey)
            .Sum(item => item.Quantity);
        var alreadyUsesSlot = storedOfKind > 0;
        return storedOfKind + quantity <= zone.SlotPolicy.StackCapacity &&
            (alreadyUsesSlot || GetUsedTypeSlots(zone.Id) < zone.SlotPolicy.SlotCount);
    }

    private int GetAvailableStorageQuantity(StorageZoneState zone, ItemStackState stack)
    {
        var totalAvailable = Math.Max(0, zone.Capacity - GetStoredQuantity(zone.Id));
        if (!zone.SlotPolicy.Supports(GetStorageRequirement(stack)))
        {
            return 0;
        }
        if (!zone.SlotPolicy.SeparatesItemTypes)
        {
            return totalAvailable;
        }

        var typeKey = GetStorageTypeKey(stack);
        var storedOfKind = _itemStacks.Values
            .Where(item => item.Location.Kind == ItemLocationKind.StorageZone &&
                item.Location.OwnerId == zone.Id && GetStorageTypeKey(item) == typeKey)
            .Sum(item => item.Quantity);
        if (storedOfKind == 0 && GetUsedTypeSlots(zone.Id) >= zone.SlotPolicy.SlotCount)
        {
            return 0;
        }

        return Math.Min(totalAvailable, zone.SlotPolicy.StackCapacity - storedOfKind);
    }

    private static StorageRequirement GetStorageRequirement(ItemStackState stack) =>
        stack.Resource == ResourceKind.Water
            ? StorageRequirement.SealedLiquid
            : StorageRequirement.SolidGoods;

    private static StorageTypeKey GetStorageTypeKey(ItemStackState stack) =>
        new(stack.Resource, stack.FoodKind, stack.Variant);

    private StorageSlotPolicy CreateDefaultStorageSlotPolicy(
        ResourceKind acceptedResource,
        int capacity) =>
        acceptedResource == ResourceKind.Water
            ? new(
                SlotCount: 1,
                StackCapacity: capacity,
                SeparatesItemTypes: false,
                StorageCapability.SealedLiquids)
            : acceptedResource == ResourceKind.Food &&
                capacity == Definitions.Storage.SmallFoodCapacity
            ? new(
                Definitions.Storage.SmallFoodTypeSlots,
                Definitions.Storage.SmallStackCapacity,
                SeparatesItemTypes: true,
                StorageCapability.SolidGoods)
            : new(
                SlotCount: 1,
                StackCapacity: capacity,
                SeparatesItemTypes: false,
                StorageCapability.SolidGoods);

    private StorageSlotPolicy CreateStorageProviderSlotPolicy(
        StorageProviderKind providerKind,
        int capacity) => providerKind switch
    {
        StorageProviderKind.WaterBarrel => new(
            SlotCount: 1,
            StackCapacity: capacity,
            SeparatesItemTypes: false,
            StorageCapability.SealedLiquids),
        StorageProviderKind.WoodenBox => new(
            SlotCount: 4,
            StackCapacity: 8,
            SeparatesItemTypes: true,
            StorageCapability.SolidGoods),
        StorageProviderKind.WoodenChest => new(
            SlotCount: 8,
            StackCapacity: 8,
            SeparatesItemTypes: true,
            StorageCapability.SolidGoods),
        StorageProviderKind.WoodenBulkBin => new(
            SlotCount: 1,
            StackCapacity: 64,
            SeparatesItemTypes: true,
            StorageCapability.SolidGoods),
        _ => CreateDefaultStorageSlotPolicy(ResourceKind.Any, capacity),
    };

    private static bool IsValidStorageSlotPolicy(StorageSlotPolicy policy, int capacity) =>
        policy.SlotCount > 0 &&
        policy.StackCapacity > 0 &&
        policy.TotalCapacity >= capacity &&
        policy.Capabilities != StorageCapability.None &&
        (policy.Capabilities & ~StorageCapability.All) == StorageCapability.None;

    private static bool IsValidStorageProvider(
        StorageProviderKind providerKind,
        ResourceKind acceptedResource,
        StorageResourceFilter resourceFilter,
        int capacity,
        StorageSlotPolicy policy) => providerKind switch
    {
        StorageProviderKind.OpenPile =>
            resourceFilter == CreateStorageResourceFilter(acceptedResource),
        StorageProviderKind.WaterBarrel =>
            acceptedResource == ResourceKind.Water && capacity == 32 &&
            resourceFilter == StorageResourceFilter.Water &&
            policy == new StorageSlotPolicy(1, 32, false, StorageCapability.SealedLiquids),
        StorageProviderKind.WoodenBox =>
            IsValidSolidContainerResourceFilter(resourceFilter) && capacity == 32 &&
            policy == new StorageSlotPolicy(4, 8, true, StorageCapability.SolidGoods),
        StorageProviderKind.WoodenChest =>
            IsValidSolidContainerResourceFilter(resourceFilter) && capacity == 64 &&
            policy == new StorageSlotPolicy(8, 8, true, StorageCapability.SolidGoods),
        StorageProviderKind.WoodenBulkBin =>
            IsValidSolidContainerResourceFilter(resourceFilter) && capacity == 64 &&
            policy == new StorageSlotPolicy(1, 64, true, StorageCapability.SolidGoods),
        _ => false,
    };

    private static bool IsValidFoodKind(ResourceKind resource, FoodKind foodKind) =>
        resource == ResourceKind.Food
            ? Enum.IsDefined(foodKind) && foodKind != FoodKind.None
            : foodKind == FoodKind.None;

    private static bool IsValidResourceVariant(
        ResourceKind resource,
        ResourceVariant variant,
        bool allowLegacyDefault = false) => resource switch
    {
        ResourceKind.Wood => variant is >= ResourceVariant.OakWood and <= ResourceVariant.PineWood ||
            (allowLegacyDefault && variant == ResourceVariant.None),
        ResourceKind.Stone => variant is ResourceVariant.Sandstone or ResourceVariant.Granite or
                ResourceVariant.Basalt or ResourceVariant.Obsidian ||
            (allowLegacyDefault && variant == ResourceVariant.None),
        ResourceKind.Ore => variant is ResourceVariant.IronOre or ResourceVariant.CopperOre or
            ResourceVariant.SilverOre or ResourceVariant.GoldOre,
        ResourceKind.Equipment => variant is >= ResourceVariant.EquipmentPrimitiveSling and
                <= ResourceVariant.EquipmentWoodenSpear or
            ResourceVariant.EquipmentReinforcedPickaxe or
            ResourceVariant.EquipmentWoodenBarrel or
            ResourceVariant.EquipmentWoodenBox or
            ResourceVariant.EquipmentWoodenChest or
            ResourceVariant.EquipmentWoodenBulkBin,
        ResourceKind.Materials => variant is ResourceVariant.None or ResourceVariant.Ruby or
            ResourceVariant.Emerald or ResourceVariant.Diamond or ResourceVariant.IronBar or
            ResourceVariant.CopperBar or ResourceVariant.SilverBar or ResourceVariant.GoldBar or
            ResourceVariant.SpiderVenom or ResourceVariant.SpiderSilk or
            ResourceVariant.SpiderChitin,
        ResourceKind.Water => variant == ResourceVariant.None,
        _ => variant == ResourceVariant.None,
    };

    private static ResourceVariant NormalizeResourceVariant(
        ResourceKind resource,
        ResourceVariant variant) => resource switch
    {
        ResourceKind.Wood when variant == ResourceVariant.None => ResourceVariant.OakWood,
        ResourceKind.Stone when variant == ResourceVariant.None => ResourceVariant.Sandstone,
        _ => variant,
    };

    private ResourceVariant WoodVariantFor(GridPosition position) =>
        WoodMaterialPolicy.VariantFor(WorldSeed, Map.Width, position);

    private ResourceVariant StoneVariantFor(GridPosition position) =>
        StoneMaterialPolicy.VariantFor(WorldSeed, Map.Width, position);

    private EntityId PositionSubject(GridPosition position) => new(
        checked((ulong)(position.Y * Map.Width + position.X) + 1));

    private static FoodKind FoodKindFor(PlantKind kind) => kind switch
    {
        PlantKind.BerryBush => FoodKind.Berries,
        PlantKind.MushroomCluster => FoodKind.Mushrooms,
        PlantKind.EdibleRoots => FoodKind.EdibleRoots,
        PlantKind.FishShoal => FoodKind.Fish,
        _ => FoodKind.DriedRations,
    };

    private int GetAvailableResourceQuantity(ResourceKind resource) =>
        _itemStacks.Values
            .Where(stack =>
                stack.Resource == resource &&
                IsAvailableForConstruction(stack))
            .Sum(stack => stack.Quantity);

    private bool IsAvailableForConstruction(ItemStackState stack) =>
        stack.Location.Kind == ItemLocationKind.StorageZone ||
        (stack.Location.Kind == ItemLocationKind.Ground &&
         Visibility.TryGet(stack.Location.Position, out var visibility) &&
         visibility == CellVisibility.Visible);

    private int ConsumeResource(ResourceKind resource, int quantity)
    {
        var remaining = quantity;
        var gatheredDirectly = 0;
        foreach (var stack in _itemStacks.Values
                     .Where(stack =>
                         stack.Resource == resource &&
                         IsAvailableForConstruction(stack))
                     .OrderBy(stack => stack.Location.Kind == ItemLocationKind.StorageZone ? 0 : 1)
                     .ThenBy(stack => stack.Id)
                     .ToArray())
        {
            var consumed = Math.Min(remaining, stack.Quantity);
            if (stack.Location.Kind == ItemLocationKind.Ground)
            {
                gatheredDirectly = checked(gatheredDirectly + consumed);
            }
            stack.Quantity -= consumed;
            remaining -= consumed;
            if (stack.Quantity == 0)
            {
                RemoveItemStack(stack.Id);
                Publish(SimulationEventKind.ItemStackDepleted, EntityId.None, stack.Id, 0);
            }

            if (remaining == 0)
            {
                return gatheredDirectly;
            }
        }

        throw new InvalidOperationException("Validated construction resources disappeared.");
    }

    private static bool ZoneCategoryAccepts(StorageZoneState zone, ResourceKind resource) =>
        zone.ResourceFilter.HasFlag(CreateStorageResourceFilter(resource, expandPreset: false));

    private static bool IsBasicCraftingMaterial(ResourceKind resource) =>
        resource is ResourceKind.Reeds or ResourceKind.Bone or ResourceKind.Hide;

    private static bool StackMatchesConstructionMaterial(
        ConstructionSiteState site,
        ItemStackState stack) =>
        ConstructionMaterialMatches(site, stack.Resource, stack.Variant);

    private static bool ConstructionMaterialMatches(
        ConstructionSiteState site,
        ResourceKind resource,
        ResourceVariant variant)
    {
        if (resource != site.RequiredResource ||
            site.RequiredVariant != ResourceVariant.None &&
            variant != site.RequiredVariant ||
            ConstructionBlueprintCatalog.RetainsMaterialIdentity(site.Kind) &&
                site.DeliveredQuantity > 0 && variant != site.DeliveredVariant)
        {
            return false;
        }

        if (!ConstructionBlueprintCatalog.TryGetWorkshopKind(site.Kind, out var workshopKind))
        {
            return true;
        }

        MaterialDefinition material;
        try
        {
            material = MaterialCatalog.Get(resource, variant);
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
        return WorkshopCatalog.Get(workshopKind).ConstructionRequirements.Any(requirement =>
            requirement.AllowedMaterialTypes.Contains(material.MaterialType) &&
            material.Strength >= requirement.MinimumStrength &&
            material.Durability >= requirement.MinimumDurability);
    }

    private static bool ZoneAccepts(StorageZoneState zone, ItemStackState stack)
    {
        if (stack.Resource == ResourceKind.Food &&
            !FoodUsePolicy.CanBeStored(stack.FoodKind))
        {
            return false;
        }

        if (!ZoneCategoryAccepts(zone, stack.Resource) ||
            zone.AcceptedResource != ResourceKind.Stone)
        {
            return ZoneCategoryAccepts(zone, stack.Resource);
        }

        var filter = stack.Resource switch
        {
            ResourceKind.Stone when stack.Variant == ResourceVariant.Sandstone =>
                MineralStorageFilter.Sandstone,
            ResourceKind.Stone when stack.Variant == ResourceVariant.Granite =>
                MineralStorageFilter.Granite,
            ResourceKind.Stone when stack.Variant is ResourceVariant.Basalt or
                ResourceVariant.Obsidian => MineralStorageFilter.Granite,
            ResourceKind.Coal => MineralStorageFilter.Coal,
            ResourceKind.Ore when stack.Variant == ResourceVariant.IronOre =>
                MineralStorageFilter.IronOre,
            ResourceKind.Ore when stack.Variant is ResourceVariant.CopperOre or
                ResourceVariant.SilverOre or ResourceVariant.GoldOre =>
                MineralStorageFilter.IronOre,
            _ => MineralStorageFilter.None,
        };
        return filter != MineralStorageFilter.None && zone.MineralFilter.HasFlag(filter);
    }

    private static bool IsValidMineralFilter(MineralStorageFilter filter) =>
        (filter & ~MineralStorageFilter.All) == MineralStorageFilter.None;

    private static bool IsMineralResource(ResourceKind resource) =>
        resource is ResourceKind.Stone or ResourceKind.Coal or ResourceKind.Ore;

    private bool TryGetCompatibleStorageSource(
        StorageZoneState destination,
        EntityId sourceId,
        out StorageZoneState source) =>
        _storageZones.TryGetValue(sourceId, out source!) &&
        source.Id != destination.Id &&
        (source.ResourceFilter & destination.ResourceFilter) != StorageResourceFilter.None;

    private static bool IsStorableResource(ResourceKind resource) =>
        Enum.IsDefined(resource) && resource != ResourceKind.Any;

    private static bool IsStorageFilterResource(ResourceKind resource) =>
        Enum.IsDefined(resource);

    private static bool IsSolidContainerFilter(ResourceKind resource) =>
        IsStorageFilterResource(resource) &&
        resource is not (ResourceKind.Water or ResourceKind.Vegetation);

    private static StorageResourceFilter CreateStorageResourceFilter(
        ResourceKind resource,
        bool expandPreset = true)
    {
        if (resource == ResourceKind.Any)
        {
            return StorageResourceFilter.SolidGoods;
        }
        if (expandPreset && resource == ResourceKind.Materials)
        {
            return StorageResourceFilter.Materials | StorageResourceFilter.Reeds |
                StorageResourceFilter.Bone | StorageResourceFilter.Hide;
        }
        if (expandPreset && resource == ResourceKind.Stone)
        {
            return StorageResourceFilter.Stone | StorageResourceFilter.Coal |
                StorageResourceFilter.Ore;
        }
        if (!IsStorableResource(resource))
        {
            return StorageResourceFilter.None;
        }

        return (StorageResourceFilter)(1 << ((int)resource - 1));
    }

    private static ResourceKind GetStorageFilterDisplayResource(StorageResourceFilter filter)
    {
        if (filter == StorageResourceFilter.SolidGoods)
        {
            return ResourceKind.Any;
        }
        if (filter == CreateStorageResourceFilter(ResourceKind.Materials))
        {
            return ResourceKind.Materials;
        }
        if (filter == CreateStorageResourceFilter(ResourceKind.Stone))
        {
            return ResourceKind.Stone;
        }

        foreach (var resource in Enum.GetValues<ResourceKind>().Where(resource =>
                     resource != ResourceKind.Any))
        {
            if (filter == CreateStorageResourceFilter(resource, expandPreset: false))
            {
                return resource;
            }
        }
        return ResourceKind.Any;
    }

    private static bool IsValidStorageResourceFilter(StorageResourceFilter filter) =>
        filter != StorageResourceFilter.None &&
        (filter & ~StorageResourceFilter.All) == StorageResourceFilter.None;

    private static bool IsValidSolidContainerResourceFilter(StorageResourceFilter filter) =>
        filter != StorageResourceFilter.None &&
        (filter & ~StorageResourceFilter.SolidGoods) == StorageResourceFilter.None;

    private static bool IsValidManagementName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 40 &&
        name.All(character => !char.IsControl(character));

    private static bool IsInventoryResource(ResourceKind resource) =>
        IsStorableResource(resource) && resource != ResourceKind.Vegetation;

    private StoragePriority GetResourcePriority(ResourceKind resource) =>
        _resourcePriorities.GetValueOrDefault(resource, StoragePriority.Normal);

    private int GetStoneAmmoCapacity(PersonalEquipment equipment) =>
        equipment.HasFlag(PersonalEquipment.PrimitiveSling)
            ? Definitions.RangedCombat.SlingAmmoCapacity
            : Definitions.RangedCombat.HandAmmoCapacity;

    private bool IsJuvenile(ActorState actor) =>
        actor.MaturesAtTick is { } maturityTick && CurrentTick.Value < maturityTick;

    private long CreateInitialAgeOffsetTicks(EntityId id)
    {
        var ticksPerYear = Definitions.Clock.Climate.TicksPerYear;
        var minimum = checked((int)(ticksPerYear * GoblinAging.InitialMinimumAgeYears));
        var maximum = checked((int)(ticksPerYear *
            GoblinAging.InitialMaximumAgeYearsExclusive));
        return DeterministicRandom.NextInt(
            WorldSeed,
            RandomDomain.GoblinIdentity,
            id,
            SimulationTick.Zero,
            sampleKey: 0x4147454F46465345UL,
            minimumInclusive: minimum,
            maximumExclusive: maximum);
    }

    private long GetActorAgeTicks(ActorState actor)
    {
        var elapsedTicks = actor.BirthTick is { } birthTick
            ? CurrentTick.Value - birthTick
            : CurrentTick.Value;
        return checked(actor.AgeOffsetTicks + Math.Max(0, elapsedTicks));
    }

    private bool IsElderly(ActorState actor) =>
        GetActorAgeTicks(actor) >= checked(
            Definitions.Clock.Climate.TicksPerYear * GoblinAging.HealthyYears);

    private double GetSenescenceProgress(ActorState actor)
    {
        var healthyTicks = checked(
            Definitions.Clock.Climate.TicksPerYear * GoblinAging.HealthyYears);
        var ageTicks = GetActorAgeTicks(actor);
        if (ageTicks <= healthyTicks)
        {
            return 0;
        }

        var onsetWorldTick = checked(CurrentTick.Value - (ageTicks - healthyTicks));
        var onsetSeason = SimulationCalendar.At(
            new SimulationTick(Math.Max(0, onsetWorldTick)),
            Definitions.Clock).Season;
        var seasonDefinitions = Definitions.Clock.Climate.Seasons;
        var onsetIndex = seasonDefinitions
            .Select((season, index) => (season.Season, Index: index))
            .Single(item => item.Season == onsetSeason).Index;
        var declineSeasons = DeterministicRandom.NextInt(
            WorldSeed,
            RandomDomain.GoblinIdentity,
            actor.Id,
            SimulationTick.Zero,
            sampleKey: 0x4F4C44414745UL,
            minimumInclusive: GoblinAging.DeclineMinimumSeasons,
            maximumExclusive: GoblinAging.DeclineMaximumSeasons + 1);
        var declineTicks = 0L;
        for (var offset = 0; offset < declineSeasons; offset++)
        {
            declineTicks = checked(declineTicks +
                seasonDefinitions[(onsetIndex + offset) % seasonDefinitions.Count].TotalTicks);
        }
        return Math.Clamp((double)(ageTicks - healthyTicks) / declineTicks, 0, 1);
    }

    private int GetEffectiveMaximumHealth(ActorState actor)
    {
        var progress = GetSenescenceProgress(actor);
        var terminalHealth = checked(
            GoblinVitals.MaximumHealth * GoblinAging.TerminalHealthPermille / 1_000);
        return Math.Clamp(
            (int)Math.Round(GoblinVitals.MaximumHealth -
                (GoblinVitals.MaximumHealth - terminalHealth) * progress),
            terminalHealth,
            GoblinVitals.MaximumHealth);
    }

    private int GetActorAgeDays(ActorState actor, int currentAbsoluteDay)
    {
        if (actor.BirthTick is not { } birthTick)
        {
            return checked((int)(GetActorAgeTicks(actor) *
                Definitions.Clock.Climate.DaysPerYear /
                Definitions.Clock.Climate.TicksPerYear));
        }

        var birthDay = SimulationCalendar.At(new SimulationTick(birthTick), Definitions.Clock)
            .AbsoluteDay;
        return Math.Max(0, currentAbsoluteDay - birthDay);
    }

    private static ActorSaveModel ToSaveModel(ActorState actor) => new()
    {
        Id = actor.Id.Value,
        Name = actor.Name,
        Sex = actor.Sex,
        PolityId = CorePolityIds.PlayerTribe.Value,
        KnownSkills = actor.KnownSkills,
        KnownTraits = actor.KnownTraits,
        Equipment = actor.Equipment,
        ForagingExperience = actor.ForagingExperience,
        HaulingExperience = actor.HaulingExperience,
        BuildingExperience = actor.BuildingExperience,
        ForagingPreference = actor.WorkPreferences.Foraging,
        HaulingPreference = actor.WorkPreferences.Hauling,
        BuildingPreference = actor.WorkPreferences.Building,
        Hunger = actor.Hunger,
        Fatigue = actor.Fatigue,
        Health = actor.Health,
        Thirst = actor.Thirst,
        PersonalFood = actor.PersonalFood,
        PersonalFoodKind = actor.PersonalFoodKind,
        PersonalFoodKinds = actor.PersonalFoodKinds.ToList(),
        PersonalFoodFreshUntilTicks = actor.PersonalFoodFreshUntilTicks.ToList(),
        PersonalWater = actor.PersonalWater,
        PersonalStoneAmmo = actor.PersonalStoneAmmo,
        BloodFootprintSteps = actor.BloodFootprintSteps,
        CarriedGrime = actor.CarriedGrime,
        BleedingTicksRemaining = actor.BleedingTicksRemaining,
        BirthTick = actor.BirthTick,
        MaturesAtTick = actor.MaturesAtTick,
        AgeOffsetTicks = actor.AgeOffsetTicks,
        X = actor.Position.X,
        Y = actor.Position.Y,
        Z = actor.Position.Z,
        CarriedStackId = actor.CarriedStackId.Value,
        CarriedCorpseId = actor.CarriedCorpseId.Value,
        JobKind = actor.JobKind,
        JobPhase = actor.JobPhase,
        JobStage = actor.JobStage,
        JobTargetX = actor.JobTarget.X,
        JobTargetY = actor.JobTarget.Y,
        JobTargetZ = actor.JobTarget.Z,
        RemainingWorkTicks = actor.RemainingWorkTicks,
        SourceStackId = actor.SourceStackId.Value,
        DestinationZoneId = actor.DestinationZoneId.Value,
        ReservedQuantity = actor.ReservedQuantity,
        SuspendedJobKind = actor.SuspendedJobKind,
        SuspendedTargetX = actor.SuspendedJobTarget.X,
        SuspendedTargetY = actor.SuspendedJobTarget.Y,
        SuspendedTargetZ = actor.SuspendedJobTarget.Z,
        TacticalOrderKind = actor.TacticalOrderKind,
        TacticalCenterX = actor.TacticalCenter.X,
        TacticalCenterY = actor.TacticalCenter.Y,
        TacticalCenterZ = actor.TacticalCenter.Z,
        TacticalRadius = actor.TacticalRadius,
        PatrolPointIndex = actor.PatrolPointIndex,
        TacticalTargetEntityId = actor.TacticalTargetEntityId.Value,
        DispatcherSuspendedUntilTick = actor.DispatcherSuspendedUntilTick,
        PatrolPoints = actor.PatrolPoints.Select(point => new GridPositionSaveModel
        {
            X = point.X,
            Y = point.Y,
            Z = point.Z,
        }).ToList(),
        RemainingRoute = actor.RemainingRoute.Select(position => new GridPositionSaveModel
        {
            X = position.X,
            Y = position.Y,
            Z = position.Z,
        }).ToList(),
        NavigationBeliefs = actor.NavigationKnowledge.CreateSnapshot()
            .Select(ToSaveModel)
            .ToList(),
        PendingNavigationReports = OrderNavigationEdges(actor.PendingNavigationReports)
            .Select(ToSaveModel)
            .ToList(),
    };

    private static NavigationBeliefSaveModel ToSaveModel(NavigationBelief belief) => new()
    {
        FirstX = belief.Edge.First.X,
        FirstY = belief.Edge.First.Y,
        FirstZ = belief.Edge.First.Z,
        SecondX = belief.Edge.Second.X,
        SecondY = belief.Edge.Second.Y,
        SecondZ = belief.Edge.Second.Z,
        Status = belief.Status,
        ObservedAt = belief.ObservedAt.Value,
        ReceivedAt = belief.ReceivedAt.Value,
        SourceActorId = belief.SourceActorId.Value,
        Confidence = belief.Confidence,
        IsDirectObservation = belief.IsDirectObservation,
    };

    private static NavigationEdgeSaveModel ToSaveModel(NavigationEdge edge) => new()
    {
        FirstX = edge.First.X,
        FirstY = edge.First.Y,
        FirstZ = edge.First.Z,
        SecondX = edge.Second.X,
        SecondY = edge.Second.Y,
        SecondZ = edge.Second.Z,
    };

    private static ItemStackSaveModel ToSaveModel(ItemStackState stack) => new()
    {
        Id = stack.Id.Value,
        Resource = stack.Resource,
        FoodKind = stack.FoodKind,
        Variant = stack.Variant,
        Quantity = stack.Quantity,
        LocationKind = stack.Location.Kind,
        X = stack.Location.Position.X,
        Y = stack.Location.Position.Y,
        Z = stack.Location.Position.Z,
        OwnerId = stack.Location.OwnerId.Value,
        HaulPriority = stack.HaulPriority,
        FreshUntilTick = stack.FreshUntilTick,
    };

    private static StorageZoneSaveModel ToSaveModel(StorageZoneState zone) => new()
    {
        Id = zone.Id.Value,
        X = zone.Position.X,
        Y = zone.Position.Y,
        Z = zone.Position.Z,
        AcceptedResource = zone.AcceptedResource,
        Capacity = zone.Capacity,
        DesiredQuantity = zone.DesiredQuantity,
        AssignedHaulerId = zone.AssignedHaulerId.Value,
        SourceStorageZoneId = zone.SourceStorageZoneId.Value,
        Priority = zone.Priority,
        MineralFilter = zone.MineralFilter,
        SlotCount = zone.SlotPolicy.SlotCount,
        StackCapacity = zone.SlotPolicy.StackCapacity,
        SeparatesItemTypes = zone.SlotPolicy.SeparatesItemTypes,
        Capabilities = zone.SlotPolicy.Capabilities,
        LogisticsNetworkId = zone.LogisticsNetworkId.Value,
        StorageAreaId = zone.StorageAreaId.Value,
        ProviderKind = zone.ProviderKind,
        ResourceFilter = zone.ResourceFilter,
    };

    private static StorageAreaSaveModel ToSaveModel(StorageAreaState area) => new()
    {
        Id = area.Id.Value,
        Name = area.Name,
        LogisticsNetworkId = area.LogisticsNetworkId.Value,
        Footprint = area.Footprint.Select(cell => new GridPositionSaveModel
        {
            X = cell.X,
            Y = cell.Y,
            Z = cell.Z,
        }).ToList(),
    };

    private static LogisticsNetworkSaveModel ToSaveModel(LogisticsNetworkState network) => new()
    {
        Id = network.Id.Value,
        Name = network.Name,
        AssignedHaulerIds = network.AssignedHaulerIds.Select(id => id.Value).ToList(),
        SourceStorageZoneIds = network.SourceStorageZoneIds.Select(id => id.Value).ToList(),
    };

    private static CommandSaveModel ToSaveModel(SimulationCommand command) => new()
    {
        ExecuteAt = command.ExecuteAt.Value,
        Sequence = command.Sequence,
        Kind = command.Kind,
        Subject = command.Subject.Value,
        Target = command.Target.Value,
        X = command.Position.X,
        Y = command.Position.Y,
        Z = command.Position.Z,
        EndX = command.EndPosition.X,
        EndY = command.EndPosition.Y,
        EndZ = command.EndPosition.Z,
        Construction = command.Construction,
        Resource = command.Resource,
        Amount = command.Amount,
        Text = command.Text,
        MaterialVariant = command.MaterialVariant,
    };

    private static EventSaveModel ToSaveModel(SimulationEvent simulationEvent) => new()
    {
        Sequence = simulationEvent.Sequence,
        Tick = simulationEvent.Tick.Value,
        Kind = simulationEvent.Kind,
        Subject = simulationEvent.Subject.Value,
        Target = simulationEvent.Target.Value,
        Amount = simulationEvent.Amount,
    };

    private static void Append(StringBuilder builder, string value)
    {
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
        builder.Append('|');
    }

    private static void Append(StringBuilder builder, int value) =>
        Append(builder, value.ToString(CultureInfo.InvariantCulture));

    private static void Append(StringBuilder builder, long value) =>
        Append(builder, value.ToString(CultureInfo.InvariantCulture));

    private static void Append(StringBuilder builder, ulong value) =>
        Append(builder, value.ToString(CultureInfo.InvariantCulture));

    private static void Append(StringBuilder builder, GridPosition position)
    {
        Append(builder, position.X);
        Append(builder, position.Y);
        Append(builder, position.Z);
    }

    private static void AppendNavigationKnowledge(
        StringBuilder builder,
        NavigationKnowledgeState knowledge)
    {
        var beliefs = knowledge.CreateSnapshot();
        Append(builder, beliefs.Count);
        foreach (var belief in beliefs)
        {
            Append(builder, belief.Edge.First);
            Append(builder, belief.Edge.Second);
            Append(builder, (int)belief.Status);
            Append(builder, belief.ObservedAt.Value);
            Append(builder, belief.ReceivedAt.Value);
            Append(builder, belief.SourceActorId.Value);
            Append(builder, belief.Confidence);
            Append(builder, belief.IsDirectObservation ? 1 : 0);
        }
    }

    private static IOrderedEnumerable<NavigationEdge> OrderNavigationEdges(
        IEnumerable<NavigationEdge> edges) =>
        edges.OrderBy(edge => edge.First.Z)
            .ThenBy(edge => edge.First.Y)
            .ThenBy(edge => edge.First.X)
            .ThenBy(edge => edge.Second.Z)
            .ThenBy(edge => edge.Second.Y)
            .ThenBy(edge => edge.Second.X);

    private static TimeSpan StopwatchTicksToTimeSpan(long stopwatchTicks) =>
        TimeSpan.FromSeconds((double)stopwatchTicks / Stopwatch.Frequency);

    private sealed class ActorState(EntityId id, GridPosition position, int hunger)
    {
        public EntityId Id { get; } = id;

        public string Name { get; set; } = string.Empty;

        public ActorSex Sex { get; set; }

        public GoblinSkill KnownSkills { get; set; }

        public GoblinTrait KnownTraits { get; set; }

        public PersonalEquipment Equipment { get; set; }

        public int ForagingExperience { get; set; }

        public int HaulingExperience { get; set; }

        public int BuildingExperience { get; set; }

        public GoblinWorkPreferences WorkPreferences { get; set; }

        public GridPosition Position { get; set; } = position;

        public int Hunger { get; set; } = hunger;

        public int Fatigue { get; set; }

        public int Health { get; set; }

        public int Thirst { get; set; }

        public List<FoodKind> PersonalFoodKinds { get; } = [];

        public List<long> PersonalFoodFreshUntilTicks { get; } = [];

        public int PersonalFood => PersonalFoodKinds.Count;

        public FoodKind PersonalFoodKind => PersonalFoodKinds.Count == 0
            ? FoodKind.None
            : PersonalFoodKinds[0];

        public int PersonalWater { get; set; }

        public int PersonalStoneAmmo { get; set; }

        public int BloodFootprintSteps { get; set; }

        public int CarriedGrime { get; set; }

        public int BleedingTicksRemaining { get; set; }

        public long? BirthTick { get; set; }

        public long? MaturesAtTick { get; set; }

        public long AgeOffsetTicks { get; set; }

        public EntityId CarriedStackId { get; set; } = EntityId.None;

        public EntityId CarriedCorpseId { get; set; } = EntityId.None;

        public ActorJobKind JobKind { get; set; }

        public ActorJobPhase JobPhase { get; set; }

        public ActorJobStage JobStage { get; set; }

        public GridPosition JobTarget { get; set; }

        public int RemainingWorkTicks { get; set; }

        public EntityId SourceStackId { get; set; }

        public EntityId DestinationZoneId { get; set; }

        public int ReservedQuantity { get; set; }

        public List<GridPosition> RemainingRoute { get; } = [];

        public NavigationKnowledgeState NavigationKnowledge { get; } = new();

        public HashSet<NavigationEdge> PendingNavigationReports { get; } = [];

        public ActorJobKind SuspendedJobKind { get; set; }

        public GridPosition SuspendedJobTarget { get; set; }

        public ActorTacticalOrderKind TacticalOrderKind { get; set; }

        public GridPosition TacticalCenter { get; set; }

        public int TacticalRadius { get; set; }

        public List<GridPosition> PatrolPoints { get; } = [];

        public int PatrolPointIndex { get; set; }

        public EntityId TacticalTargetEntityId { get; set; }

        public long DispatcherSuspendedUntilTick { get; set; }

        public void SuspendCurrentJob()
        {
            if (JobKind is ActorJobKind.Move or ActorJobKind.Explore or ActorJobKind.Forage or
                ActorJobKind.ClearVegetation or ActorJobKind.FellTree or
                ActorJobKind.QuarryBoulder or ActorJobKind.MineRock or ActorJobKind.CarveRamp or
                ActorJobKind.Rest)
            {
                SuspendedJobKind = JobKind;
                SuspendedJobTarget = JobTarget;
            }

            ClearJob();
        }

        public void ClearSuspendedJob()
        {
            SuspendedJobKind = ActorJobKind.None;
            SuspendedJobTarget = default;
        }

        public void ClearJob()
        {
            JobKind = ActorJobKind.None;
            JobPhase = ActorJobPhase.None;
            JobStage = ActorJobStage.None;
            JobTarget = default;
            RemainingWorkTicks = 0;
            SourceStackId = EntityId.None;
            DestinationZoneId = EntityId.None;
            ReservedQuantity = 0;
            RemainingRoute.Clear();
        }

        public void ClearTacticalOrder()
        {
            TacticalOrderKind = ActorTacticalOrderKind.None;
            TacticalCenter = default;
            TacticalRadius = 0;
            PatrolPoints.Clear();
            PatrolPointIndex = 0;
            TacticalTargetEntityId = EntityId.None;
        }
    }

    private sealed class GoblinBudState(
        EntityId id,
        EntityId parentId,
        EntityId originCorpseId,
        GoblinInheritanceImprint originImprint,
        GridPosition position,
        int remainingCareTicks)
    {
        public EntityId Id { get; } = id;

        public EntityId ParentId { get; } = parentId;

        public EntityId OriginCorpseId { get; } = originCorpseId;

        public GoblinInheritanceImprint OriginImprint { get; } = originImprint;

        public GridPosition Position { get; } = position;

        public int RemainingCareTicks { get; set; } = remainingCareTicks;
    }

    private sealed class ItemStackState(
        EntityId id,
        ResourceKind resource,
        FoodKind foodKind,
        ResourceVariant variant,
        int quantity,
        ItemLocation location,
        long? freshUntilTick)
    {
        public EntityId Id { get; } = id;

        public ResourceKind Resource { get; } = resource;

        public FoodKind FoodKind { get; } = foodKind;

        public ResourceVariant Variant { get; } = variant;

        public int Quantity { get; set; } = quantity;

        public ItemLocation Location { get; set; } = location;

        public long? FreshUntilTick { get; set; } = freshUntilTick;

        public StoragePriority HaulPriority { get; set; } = StoragePriority.Normal;
    }

    private sealed class StorageZoneState(
        EntityId id,
        GridPosition position,
        ResourceKind acceptedResource,
        int capacity,
        int desiredQuantity = 0,
        EntityId assignedHaulerId = default,
        EntityId sourceStorageZoneId = default,
        StoragePriority priority = StoragePriority.Normal,
        MineralStorageFilter mineralFilter = MineralStorageFilter.All,
        StorageSlotPolicy slotPolicy = default,
        EntityId logisticsNetworkId = default,
        EntityId storageAreaId = default,
        StorageProviderKind providerKind = StorageProviderKind.OpenPile,
        StorageResourceFilter resourceFilter = StorageResourceFilter.None)
    {
        public EntityId Id { get; } = id;

        public GridPosition Position { get; } = position;

        public ResourceKind AcceptedResource { get; set; } = acceptedResource;

        public int Capacity { get; } = capacity;

        public int DesiredQuantity { get; set; } = desiredQuantity;

        public EntityId AssignedHaulerId { get; set; } = assignedHaulerId;

        public EntityId SourceStorageZoneId { get; set; } = sourceStorageZoneId;

        public StoragePriority Priority { get; set; } = priority;

        public MineralStorageFilter MineralFilter { get; set; } = mineralFilter;

        public StorageSlotPolicy SlotPolicy { get; } = slotPolicy;

        public EntityId LogisticsNetworkId { get; set; } = logisticsNetworkId;

        public EntityId StorageAreaId { get; set; } = storageAreaId;

        public StorageProviderKind ProviderKind { get; } = providerKind;

        public StorageResourceFilter ResourceFilter { get; set; } = resourceFilter ==
            StorageResourceFilter.None
                ? CreateStorageResourceFilter(acceptedResource)
                : resourceFilter;
    }

    private sealed class StorageAreaState(
        EntityId id,
        string name,
        IEnumerable<GridPosition> footprint,
        EntityId logisticsNetworkId)
    {
        public EntityId Id { get; } = id;

        public string Name { get; set; } = name;

        public GridPosition[] Footprint { get; private set; } = SortFootprint(footprint);

        public EntityId LogisticsNetworkId { get; set; } = logisticsNetworkId;

        public void SetFootprint(IEnumerable<GridPosition> cells) =>
            Footprint = SortFootprint(cells);

        private static GridPosition[] SortFootprint(IEnumerable<GridPosition> cells) => cells
            .OrderBy(cell => cell.Z)
            .ThenBy(cell => cell.Y)
            .ThenBy(cell => cell.X)
            .ToArray();
    }

    private sealed class LogisticsNetworkState(EntityId id, string name)
    {
        public EntityId Id { get; } = id;

        public string Name { get; set; } = name;

        public SortedSet<EntityId> AssignedHaulerIds { get; } = [];

        public SortedSet<EntityId> SourceStorageZoneIds { get; } = [];
    }

    private readonly record struct StorageTypeKey(
        ResourceKind Resource,
        FoodKind FoodKind,
        ResourceVariant Variant);
}
