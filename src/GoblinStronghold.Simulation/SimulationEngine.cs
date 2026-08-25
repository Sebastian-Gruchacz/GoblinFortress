using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation;

public sealed partial class SimulationEngine
{
    private const int SaveFormatVersion = 26;
    private const int DefaultMapDimension = 32;

    private static readonly JsonSerializerOptions SaveOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly SortedDictionary<EntityId, ActorState> _actors = [];
    private readonly SortedDictionary<EntityId, ItemStackState> _itemStacks = [];
    private readonly SortedDictionary<EntityId, StorageZoneState> _storageZones = [];
    private readonly SortedDictionary<EntityId, ConstructionSiteState> _constructionSites = [];
    private readonly SortedDictionary<EntityId, WorkDesignationSnapshot> _workDesignations = [];
    private readonly SortedDictionary<CommandKey, SimulationCommand> _pendingCommands = [];
    private readonly List<SimulationEvent> _undeliveredEvents = [];
    private readonly List<WorldChangeEvent> _undeliveredWorldChanges = [];
    private HumanVillageState _humanVillage;
    private GoblinRaidPhase _raidPhase;
    private GridPosition _raidRallyPoint;
    private ulong _nextEntityId = 1;
    private ulong _nextEventSequence = 1;
    private long _ticksExecuted;
    private long _commandsExecuted;
    private long _eventsPublished;
    private long _actorUpdates;
    private long _lastTickStopwatchTicks;
    private long _totalTickStopwatchTicks;

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
        Visibility = WorldVisibilityState.Create(map);
        _humanVillage = HumanVillageState.CreateInitial(World, definitions);
    }

    public WorldSeed WorldSeed { get; }

    public SimulationDefinitions Definitions { get; }

    public SimulationDebugSettings DebugSettings { get; }

    public GeneratedMap Map => World.Baseline;

    public WorldMapState World { get; private set; }

    public WorldVisibilityState Visibility { get; private set; }

    public SimulationTick CurrentTick { get; private set; } = SimulationTick.Zero;

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
            SwampMapGenerator.Generate(worldSeed, DefaultMapDimension, DefaultMapDimension),
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
        ArgumentOutOfRangeException.ThrowIfGreaterThan(initialHunger, definitions.MaximumHunger);
        var actorHealth = initialHealth ?? definitions.MaximumHealth;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actorHealth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(actorHealth, definitions.MaximumHealth);

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
            engine.AllocateActor(map.GoblinSpawn, initialHunger, actorHealth);
        }

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

        var save = JsonSerializer.Deserialize<SimulationSaveModel>(saveJson, SaveOptions)
            ?? throw new InvalidDataException("The save does not contain simulation state.");

        ValidateSaveHeader(save, definitions);

        var worldSeed = new WorldSeed(save.WorldSeed);
        GeneratedMap map;
        try
        {
            map = SwampMapGenerator.Generate(
                worldSeed,
                save.MapWidth,
                save.MapHeight,
                save.MapGeneratorVersion);
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

        var engine = new SimulationEngine(
            worldSeed,
            definitions,
            map,
            debugSettings ?? SimulationDebugSettings.Disabled)
        {
            CurrentTick = new SimulationTick(save.CurrentTick),
            _nextEntityId = save.NextEntityId,
            _nextEventSequence = save.NextEventSequence,
            _raidPhase = save.RaidPhase,
            _raidRallyPoint = new GridPosition(save.RaidRallyX, save.RaidRallyY, save.RaidRallyZ),
        };

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
                    part.Kind)))));
        engine.Visibility = WorldVisibilityState.Restore(map, save.Visibility);
        engine._humanVillage = HumanVillageState.Restore(
            engine.World,
            save.HumanVillage,
            definitions,
            engine.CurrentTick);
        engine.ValidateLoadedRaidState();
        engine.LoadStorageZones(save.StorageZones);
        engine.LoadWorkDesignations(save.WorkDesignations);
        engine.LoadItemStacks(save.ItemStacks);
        engine.LoadConstructionSites(save.ConstructionSites);
        engine.ValidateLoadedWorkDesignations();
        engine.LoadActors(save.Actors);
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

    public void AdvanceTicks(int tickCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tickCount);

        for (var index = 0; index < tickCount; index++)
        {
            AdvanceOneTick();
        }
    }

    public SimulationSnapshot CreateSnapshot()
    {
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
                actor.Position,
                actor.Hunger,
                actor.Fatigue,
                actor.Health,
                actor.Thirst,
                actor.PersonalFood,
                actor.PersonalFoodKind,
                actor.PersonalWater,
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
                    actor.SuspendedJobTarget)))
            .ToArray();
        var itemStacks = _itemStacks.Values
            .Select(stack => new ItemStackSnapshot(
                stack.Id, stack.Resource, stack.FoodKind, stack.Quantity, stack.Location))
            .ToArray();
        var storageZones = _storageZones.Values
            .Select(zone => new StorageZoneSnapshot(
                zone.Id,
                zone.Position,
                zone.AcceptedResource,
                zone.Capacity,
                GetStoredQuantity(zone.Id),
                zone.DesiredQuantity,
                UsesSmallFoodSlotRules(zone) ? Definitions.Storage.SmallFoodTypeSlots : 0,
                UsesSmallFoodSlotRules(zone) ? Definitions.Storage.SmallStackCapacity : zone.Capacity,
                GetUsedTypeSlots(zone.Id)))
            .ToArray();
        var workDesignations = _workDesignations.Values.ToArray();
        var constructionSites = _constructionSites.Values
            .Select(site => site.ToSnapshot())
            .ToArray();
        var plantPatches = World.CreatePlantSnapshot().ToArray();
        var worldObjects = World.CreateWorldObjectSnapshot().ToArray();
        var humanVillage = _humanVillage.CreateSnapshot();
        var visibility = Visibility.CreateSnapshot().ToArray();

        return new SimulationSnapshot(
            WorldSeed,
            CurrentTick,
            GetTotalResourceQuantity(ResourceKind.Food),
            actors,
            itemStacks,
            storageZones,
            constructionSites,
            workDesignations,
            plantPatches,
            worldObjects,
            humanVillage,
            _raidPhase,
            _raidRallyPoint,
            visibility,
            World.Version,
            Map.GeneratorVersion,
            Map.ComputeFingerprint(),
            ComputeStateHash());
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
        LastTickDuration: StopwatchTicksToTimeSpan(_lastTickStopwatchTicks),
        TotalTickDuration: StopwatchTicksToTimeSpan(_totalTickStopwatchTicks));

    public string Save()
    {
        var save = new SimulationSaveModel
        {
            FormatVersion = SaveFormatVersion,
            DefinitionsId = Definitions.Id,
            ClimateProfileId = Definitions.Clock.Climate.Id,
            WorldSeed = WorldSeed.Value,
            MapGeneratorVersion = Map.GeneratorVersion,
            MapWidth = Map.Width,
            MapHeight = Map.Height,
            MapFingerprint = Map.ComputeFingerprint(),
            CurrentTick = CurrentTick.Value,
            NextEntityId = _nextEntityId,
            NextEventSequence = _nextEventSequence,
            WorldVersion = World.Version,
            RaidPhase = _raidPhase,
            RaidRallyX = _raidRallyPoint.X,
            RaidRallyY = _raidRallyPoint.Y,
            RaidRallyZ = _raidRallyPoint.Z,
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
                    Parts = worldObject.Parts.Select(part => new WorldObjectPartSaveModel
                    {
                        RelativeX = part.RelativePosition.X,
                        RelativeY = part.RelativePosition.Y,
                        RelativeZ = part.RelativePosition.Z,
                        Channel = part.Channel,
                        Kind = part.Kind,
                    }).ToList(),
                }).ToList(),
            HumanVillage = _humanVillage.CreateSaveModel(),
            Visibility = Visibility.CreateSnapshot().ToList(),
            Actors = _actors.Values.Select(ToSaveModel).ToList(),
            ItemStacks = _itemStacks.Values.Select(ToSaveModel).ToList(),
            StorageZones = _storageZones.Values.Select(ToSaveModel).ToList(),
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
                    RequiredWood = site.RequiredWood,
                    DeliveredWood = site.DeliveredWood,
                    RemainingWorkTicks = site.RemainingWorkTicks,
                    TotalWorkTicks = site.TotalWorkTicks,
                    RequiredSkills = site.Capabilities.RequiredSkills,
                    MinimumBuildingLevel = site.Capabilities.MinimumBuildingLevel,
                    RequiredEquipment = site.Capabilities.RequiredEquipment,
                }).ToList(),
            WorkDesignations = _workDesignations.Values.Select(designation =>
                new WorkDesignationSaveModel
                {
                    Id = designation.Id.Value,
                    Kind = designation.Kind,
                    TargetX = designation.Target.X,
                    TargetY = designation.Target.Y,
                    TargetZ = designation.Target.Z,
                    TargetEntityId = designation.TargetEntityId.Value,
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
        Append(canonical, WorldSeed.Value);
        Append(canonical, Map.GeneratorVersion);
        Append(canonical, Map.ComputeFingerprint());
        Append(canonical, World.Version);
        Append(canonical, (int)_raidPhase);
        Append(canonical, _raidRallyPoint);
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
            Append(canonical, worldObject.Parts.Count);
            foreach (var part in worldObject.Parts)
            {
                Append(canonical, part.RelativePosition);
                Append(canonical, (int)part.Channel);
                Append(canonical, (int)part.Kind);
            }
        }

        var humanVillage = _humanVillage.CreateSnapshot();
        Append(canonical, humanVillage.Anchor);
        Append(canonical, humanVillage.Population);
        Append(canonical, humanVillage.FoodStock);
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
        Append(canonical, humanVillage.Fields.Count);
        foreach (var field in humanVillage.Fields)
        {
            Append(canonical, field.Id);
            Append(canonical, field.Position);
            Append(canonical, (int)field.Phase);
            Append(canonical, field.GrowthDays);
        }

        var visibility = Visibility.CreateSnapshot();
        Append(canonical, visibility.Count);
        foreach (var state in visibility)
        {
            Append(canonical, (int)state);
        }

        Append(canonical, CurrentTick.Value);
        Append(canonical, _nextEntityId);
        Append(canonical, _nextEventSequence);
        Append(canonical, _actors.Count);

        foreach (var actor in _actors.Values)
        {
            Append(canonical, actor.Id.Value);
            Append(canonical, actor.Name);
            Append(canonical, (int)actor.KnownSkills);
            Append(canonical, (int)actor.KnownTraits);
            Append(canonical, (int)actor.Equipment);
            Append(canonical, actor.ForagingExperience);
            Append(canonical, actor.HaulingExperience);
            Append(canonical, actor.BuildingExperience);
            Append(canonical, actor.Position);
            Append(canonical, actor.Hunger);
            Append(canonical, actor.Fatigue);
            Append(canonical, actor.Health);
            Append(canonical, actor.Thirst);
            Append(canonical, actor.PersonalFood);
            Append(canonical, (int)actor.PersonalFoodKind);
            Append(canonical, actor.PersonalWater);
            Append(canonical, actor.CarriedStackId.Value);
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
            Append(canonical, (int)actor.SuspendedJobKind);
            Append(canonical, actor.SuspendedJobTarget);
        }

        Append(canonical, _itemStacks.Count);
        foreach (var stack in _itemStacks.Values)
        {
            Append(canonical, stack.Id.Value);
            Append(canonical, (int)stack.Resource);
            Append(canonical, (int)stack.FoodKind);
            Append(canonical, stack.Quantity);
            Append(canonical, (int)stack.Location.Kind);
            Append(canonical, stack.Location.Position);
            Append(canonical, stack.Location.OwnerId.Value);
        }

        Append(canonical, _storageZones.Count);
        foreach (var zone in _storageZones.Values)
        {
            Append(canonical, zone.Id.Value);
            Append(canonical, zone.Position);
            Append(canonical, (int)zone.AcceptedResource);
            Append(canonical, zone.Capacity);
            Append(canonical, zone.DesiredQuantity);
        }

        Append(canonical, _workDesignations.Count);
        foreach (var designation in _workDesignations.Values)
        {
            Append(canonical, designation.Id.Value);
            Append(canonical, (int)designation.Kind);
            Append(canonical, designation.Target);
            Append(canonical, designation.TargetEntityId.Value);
        }

        Append(canonical, _constructionSites.Count);
        foreach (var site in _constructionSites.Values)
        {
            Append(canonical, site.Id.Value);
            Append(canonical, (int)site.Kind);
            Append(canonical, site.Anchor);
            Append(canonical, site.End);
            Append(canonical, site.RequiredWood);
            Append(canonical, site.DeliveredWood);
            Append(canonical, site.RemainingWorkTicks);
            Append(canonical, site.TotalWorkTicks);
            Append(canonical, (int)site.Capabilities.RequiredSkills);
            Append(canonical, site.Capabilities.MinimumBuildingLevel);
            Append(canonical, (int)site.Capabilities.RequiredEquipment);
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

        if (!SwampMapGenerator.SupportsVersion(save.MapGeneratorVersion))
        {
            throw new InvalidDataException(
                $"Unsupported map generator version {save.MapGeneratorVersion}.");
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
            if (id == EntityId.None ||
                string.IsNullOrWhiteSpace(actorModel.Name) ||
                !HasOnlyKnownFlags(actorModel.KnownSkills, GoblinSkill.Building) ||
                !HasOnlyKnownFlags(actorModel.KnownTraits, GoblinTrait.Nimble) ||
                !HasOnlyKnownFlags(actorModel.Equipment, PersonalEquipment.BoneKnife) ||
                actorModel.ForagingExperience < 0 ||
                actorModel.HaulingExperience < 0 ||
                actorModel.BuildingExperience < 0 ||
                actorModel.Hunger < 0 || actorModel.Hunger > Definitions.MaximumHunger ||
                actorModel.Fatigue < 0 || actorModel.Fatigue > Definitions.MaximumFatigue ||
                actorModel.Health <= 0 || actorModel.Health > Definitions.MaximumHealth ||
                actorModel.Thirst < 0 || actorModel.Thirst > Definitions.MaximumThirst ||
                actorModel.PersonalFood < 0 || actorModel.PersonalFood > Definitions.PersonalFoodCapacity ||
                !IsValidPersonalFood(actorModel.PersonalFood, actorModel.PersonalFoodKind) ||
                actorModel.PersonalWater < 0 || actorModel.PersonalWater > Definitions.PersonalWaterCapacity ||
                !Enum.IsDefined(actorModel.SuspendedJobKind) ||
                (actorModel.SuspendedJobKind == ActorJobKind.None &&
                 (actorModel.SuspendedTargetX != 0 || actorModel.SuspendedTargetY != 0 ||
                  actorModel.SuspendedTargetZ != 0)) ||
                (actorModel.SuspendedJobKind != ActorJobKind.None &&
                 !Map.IsWithin(new GridPosition(
                     actorModel.SuspendedTargetX,
                     actorModel.SuspendedTargetY,
                     actorModel.SuspendedTargetZ))) ||
                !World.IsSurfaceTraversable(position))
            {
                throw new InvalidDataException("The save contains an invalid actor.");
            }

            var actor = new ActorState(id, position, actorModel.Hunger)
            {
                Name = actorModel.Name,
                KnownSkills = actorModel.KnownSkills,
                KnownTraits = actorModel.KnownTraits,
                Equipment = actorModel.Equipment,
                ForagingExperience = actorModel.ForagingExperience,
                HaulingExperience = actorModel.HaulingExperience,
                BuildingExperience = actorModel.BuildingExperience,
                CarriedStackId = new EntityId(actorModel.CarriedStackId),
                Fatigue = actorModel.Fatigue,
                Health = actorModel.Health,
                Thirst = actorModel.Thirst,
                PersonalFood = actorModel.PersonalFood,
                PersonalFoodKind = actorModel.PersonalFoodKind,
                PersonalWater = actorModel.PersonalWater,
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
            };

            actor.RemainingRoute.AddRange(actorModel.RemainingRoute.Select(routePosition =>
                new GridPosition(routePosition.X, routePosition.Y, routePosition.Z)));
            ValidateLoadedJob(actor);

            if (!_actors.TryAdd(id, actor))
            {
                throw new InvalidDataException($"The save contains duplicate actor {id}.");
            }
        }
    }

    private void LoadStorageZones(IEnumerable<StorageZoneSaveModel> zoneModels)
    {
        foreach (var zoneModel in zoneModels.OrderBy(zone => zone.Id))
        {
            var id = new EntityId(zoneModel.Id);
            var position = new GridPosition(zoneModel.X, zoneModel.Y, zoneModel.Z);
            if (id == EntityId.None ||
                !World.IsSurfaceTraversable(position) ||
                zoneModel.Capacity <= 0 ||
                zoneModel.DesiredQuantity < 0 ||
                zoneModel.DesiredQuantity > zoneModel.Capacity ||
                !IsStorableResource(zoneModel.AcceptedResource))
            {
                throw new InvalidDataException("The save contains an invalid storage zone.");
            }

            if (!_storageZones.TryAdd(
                    id,
                    new StorageZoneState(
                        id,
                        position,
                        zoneModel.AcceptedResource,
                        zoneModel.Capacity,
                        zoneModel.DesiredQuantity)))
            {
                throw new InvalidDataException($"The save contains duplicate storage zone {id}.");
            }
        }
    }

    private void LoadWorkDesignations(IEnumerable<WorkDesignationSaveModel> models)
    {
        foreach (var model in models.OrderBy(item => item.Id))
        {
            var id = new EntityId(model.Id);
            var target = new GridPosition(model.TargetX, model.TargetY, model.TargetZ);
            var targetEntityId = new EntityId(model.TargetEntityId);
            if (id == EntityId.None ||
                !Enum.IsDefined(model.Kind) ||
                target.Z != 0 || !Map.IsWithin(target) ||
                (model.Kind is WorkDesignationKind.GatherFood or WorkDesignationKind.UprootBerryBush &&
                 targetEntityId != EntityId.None) ||
                (model.Kind == WorkDesignationKind.GatherBrushwood && targetEntityId == EntityId.None) ||
                !_workDesignations.TryAdd(
                    id,
                    new WorkDesignationSnapshot(id, model.Kind, target, targetEntityId)))
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
            if (id == EntityId.None ||
                !Enum.IsDefined(model.Kind) ||
                !Map.IsWithin(anchor) || !Map.IsWithin(end) ||
                model.RequiredWood <= 0 ||
                model.DeliveredWood < 0 || model.DeliveredWood > model.RequiredWood ||
                model.TotalWorkTicks <= 0 ||
                model.RemainingWorkTicks <= 0 ||
                model.RemainingWorkTicks > model.TotalWorkTicks ||
                !HasOnlyKnownFlags(model.RequiredSkills, GoblinSkill.Building) ||
                model.MinimumBuildingLevel < 0 ||
                !HasOnlyKnownFlags(model.RequiredEquipment, PersonalEquipment.BoneKnife))
            {
                throw new InvalidDataException("The save contains an invalid construction site.");
            }

            var expected = ConstructionBlueprintCatalog.CreateSite(id, model.Kind, anchor, end);
            if (expected.RequiredWood != model.RequiredWood ||
                expected.TotalWorkTicks != model.TotalWorkTicks ||
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
                model.RequiredWood,
                model.DeliveredWood,
                model.RemainingWorkTicks,
                model.TotalWorkTicks,
                expected.Capabilities);
            if (site.GetFootprint().Any(position => !Map.IsWithin(position)) ||
                _constructionSites.Values.Any(other =>
                    other.GetFootprint().Intersect(site.GetFootprint()).Any()) ||
                !_constructionSites.TryAdd(id, site))
            {
                throw new InvalidDataException("The save contains overlapping construction sites.");
            }
        }
    }

    private void LoadItemStacks(IEnumerable<ItemStackSaveModel> stackModels)
    {
        foreach (var stackModel in stackModels.OrderBy(stack => stack.Id))
        {
            var id = new EntityId(stackModel.Id);
            var position = new GridPosition(stackModel.X, stackModel.Y, stackModel.Z);
            var owner = new EntityId(stackModel.OwnerId);
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
                stackModel.Quantity <= 0)
            {
                throw new InvalidDataException("The save contains an invalid item stack.");
            }

            if (!_itemStacks.TryAdd(
                    id,
                    new ItemStackState(
                        id, stackModel.Resource, stackModel.FoodKind, stackModel.Quantity, location)))
            {
                throw new InvalidDataException($"The save contains duplicate item stack {id}.");
            }
        }
    }

    private void ValidateLoadedWorkDesignations()
    {
        foreach (var designation in _workDesignations.Values)
        {
            var valid = designation.Kind switch
            {
                WorkDesignationKind.GatherFood => World.GetPlantPatch(designation.Target) is not null,
                WorkDesignationKind.UprootBerryBush =>
                    World.GetPlantPatch(designation.Target) is { Kind: PlantKind.BerryBush },
                WorkDesignationKind.GatherBrushwood =>
                    _itemStacks.TryGetValue(designation.TargetEntityId, out var stack) &&
                    stack.Resource == ResourceKind.Wood,
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
                    if (!World.IsSurfaceTraversable(stack.Location.Position))
                    {
                        throw new InvalidDataException("A ground stack has an invalid position.");
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
                        zone.Position != stack.Location.Position ||
                        !ZoneAccepts(zone, stack.Resource))
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

        foreach (var zone in _storageZones.Values)
        {
            if (GetStoredQuantity(zone.Id) > zone.Capacity)
            {
                throw new InvalidDataException($"Storage zone {zone.Id} exceeds its capacity.");
            }
        }
    }

    private void ValidateNextEntityId()
    {
        var maximumId = _actors.Keys
            .Concat(_itemStacks.Keys)
            .Concat(_storageZones.Keys)
            .Concat(_workDesignations.Keys)
            .Concat(_constructionSites.Keys)
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
                model.Amount);

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
                !Map.IsWithin(change.Position) ||
                change.Amount == 0 ||
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

        UpdateWorld();
        ExecuteScheduledCommands();
        UpdateActorJobs();
        UpdateHumanVillage();
        ResolveHumanCombat();
        UpdateActors();
        UpdateVisibility();

        _ticksExecuted = checked(_ticksExecuted + 1);
        _lastTickStopwatchTicks = Stopwatch.GetTimestamp() - startedAt;
        _totalTickStopwatchTicks = checked(_totalTickStopwatchTicks + _lastTickStopwatchTicks);
    }

    private void UpdateWorld()
    {
        if (CurrentTick.Value % Definitions.PlantGrowthIntervalTicks == 0)
        {
            var calendar = SimulationCalendar.At(CurrentTick, Definitions.Clock);
            _undeliveredWorldChanges.AddRange(
                World.GrowPlants(CurrentTick, Definitions.PlantGrowthPerInterval, calendar.Season));
        }
    }

    private void UpdateVisibility()
    {
        var calendar = SimulationCalendar.At(CurrentTick, Definitions.Clock);
        var goblinRadius = calendar.IsNight
            ? Math.Max(2, Definitions.VisionRadius - 1)
            : Definitions.VisionRadius;
        var observers = _actors.Values
            .Select(actor => (actor.Position, goblinRadius))
            .ToList();
        if (DebugSettings.RevealFogFromNonPlayerUnits)
        {
            var humanRadius = calendar.IsNight ? 3 : Definitions.VisionRadius;
            observers.AddRange(_humanVillage.GetLivingCohortPositions()
                .Select(position => (position, humanRadius)));
        }

        Visibility.Reveal(observers);
        foreach (var actor in _actors.Values.Where(actor =>
                     actor.JobKind == ActorJobKind.Explore &&
                     Visibility.Get(actor.JobTarget) != CellVisibility.Unknown))
        {
            actor.ClearJob();
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
            if (!TryExecute(first.Value))
            {
                Publish(
                    SimulationEventKind.CommandRejected,
                    first.Value.Subject,
                    first.Value.Target,
                    (int)first.Value.Kind);
            }

            _commandsExecuted = checked(_commandsExecuted + 1);
        }
    }

    private bool TryExecute(SimulationCommand command) => command.Kind switch
    {
        SimulationCommandKind.Forage => TryExecuteForage(command),
        SimulationCommandKind.CreateStorageZone => TryExecuteCreateStorageZone(command),
        SimulationCommandKind.PickUp => TryExecutePickUp(command),
        SimulationCommandKind.StoreCarried => TryExecuteStoreCarried(command),
        SimulationCommandKind.Move => TryExecuteMove(command),
        SimulationCommandKind.Build => TryExecuteBuild(command),
        SimulationCommandKind.DesignateWork => TryExecuteDesignateWork(command),
        SimulationCommandKind.ClearWorkDesignations => TryExecuteClearWork(command),
        SimulationCommandKind.ConfigureStoragePull => TryExecuteConfigureStoragePull(command),
        SimulationCommandKind.AttackHumanVillage => TryExecuteAttackHumanVillage(),
        _ => false,
    };

    private bool TryExecuteAttackHumanVillage()
    {
        if (_humanVillage.GoblinAttackOrdered || _raidPhase != GoblinRaidPhase.None)
        {
            return false;
        }

        var rally = World.CreateWorldObjectSnapshot()
            .Where(item =>
                item.Kind == WorldObjectKind.GoblinFieldCamp &&
                item.Owner == WorldObjectOwner.GoblinTribe)
            .Select(item => item.Anchor)
            .Where(World.IsSurfaceTraversable)
            .Select(position => new
            {
                Position = position,
                Route = World.FindSurfacePath(position, Map.HumanVillage),
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
        var raidParty = _actors.Values
            .OrderBy(actor => actor.Id.Value)
            .Take(SimulationDefinitions.FieldCampCapacity)
            .ToArray();
        foreach (var actor in raidParty)
        {
            if (actor.CarriedStackId == EntityId.None && actor.JobKind != ActorJobKind.Haul)
            {
                actor.ClearJob();
            }
        }
        Publish(SimulationEventKind.RaidPreparationStarted, EntityId.None, EntityId.None, raidParty.Length);
        return true;
    }

    private void ValidateLoadedRaidState()
    {
        if (!Enum.IsDefined(_raidPhase) ||
            (_raidPhase == GoblinRaidPhase.None && _raidRallyPoint != default) ||
            (_raidPhase != GoblinRaidPhase.None &&
             (!Map.IsWithin(_raidRallyPoint) ||
              !World.GetWorldObjectsAt(_raidRallyPoint).Any(item =>
                  item.Kind == WorldObjectKind.GoblinFieldCamp &&
                  item.Owner == WorldObjectOwner.GoblinTribe))) ||
            (_raidPhase == GoblinRaidPhase.Marching && !_humanVillage.GoblinAttackOrdered) ||
            (_raidPhase == GoblinRaidPhase.Preparing && _humanVillage.GoblinAttackOrdered))
        {
            throw new InvalidDataException("The save contains invalid goblin raid state.");
        }
    }

    private bool TryExecuteDesignateWork(SimulationCommand command)
    {
        var kind = command.Resource switch
        {
            ResourceKind.Food => WorkDesignationKind.GatherFood,
            ResourceKind.Wood => WorkDesignationKind.GatherBrushwood,
            ResourceKind.Vegetation => WorkDesignationKind.UprootBerryBush,
            _ => default,
        };
        if (kind == default)
        {
            return false;
        }

        var (minimum, maximum) = NormalizeArea(command.Position, command.EndPosition);
        var targets = kind switch
        {
            WorkDesignationKind.GatherFood => World.CreatePlantSnapshot()
                .Where(plant => plant.Biomass > 0 && IsInside(plant.Position, minimum, maximum) &&
                    Visibility.Get(plant.Position) != CellVisibility.Unknown)
                .Select(plant => (plant.Position, EntityId.None)),
            WorkDesignationKind.GatherBrushwood => _itemStacks.Values
                .Where(stack => stack.Resource == ResourceKind.Wood &&
                    stack.Location.Kind == ItemLocationKind.Ground &&
                    IsInside(stack.Location.Position, minimum, maximum) &&
                    Visibility.Get(stack.Location.Position) != CellVisibility.Unknown)
                .Select(stack => (stack.Location.Position, stack.Id)),
            WorkDesignationKind.UprootBerryBush => World.CreatePlantSnapshot()
                .Where(plant => plant.Kind == PlantKind.BerryBush &&
                    IsInside(plant.Position, minimum, maximum) &&
                    Visibility.Get(plant.Position) != CellVisibility.Unknown)
                .Select(plant => (plant.Position, EntityId.None)),
            _ => [],
        };
        foreach (var (position, targetEntityId) in targets
                     .OrderBy(item => item.Position.Y)
                     .ThenBy(item => item.Position.X)
                     .ThenBy(item => item.Item2))
        {
            if (_workDesignations.Values.Any(item =>
                    item.Kind == kind && item.Target == position && item.TargetEntityId == targetEntityId))
            {
                continue;
            }
            var id = AllocateEntityId();
            _workDesignations.Add(id, new WorkDesignationSnapshot(id, kind, position, targetEntityId));
            Publish(SimulationEventKind.WorkDesignationCreated, EntityId.None, id, (int)kind);
        }
        return true;
    }

    private bool TryExecuteClearWork(SimulationCommand command)
    {
        var (minimum, maximum) = NormalizeArea(command.Position, command.EndPosition);
        var removed = _workDesignations.Values
            .Where(designation => IsInside(designation.Target, minimum, maximum))
            .Select(designation => designation.Id)
            .ToArray();
        foreach (var id in removed)
        {
            _workDesignations.Remove(id);
            Publish(SimulationEventKind.WorkDesignationRemoved, EntityId.None, id, 0);
        }

        CancelJobsInClearedArea(minimum, maximum);
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
        Publish(
            SimulationEventKind.StoragePullConfigured,
            EntityId.None,
            zone.Id,
            command.Amount);
        return true;
    }

    private bool TryExecuteBuild(SimulationCommand command)
    {
        var footprint = GetConstructionFootprint(
            command.Construction,
            command.Position,
            command.EndPosition);
        if (!CanPlaceConstruction(command.Construction, command.Position, footprint) ||
            _constructionSites.Values.Any(site =>
                site.GetFootprint().Intersect(footprint).Any()))
        {
            return false;
        }

        var id = AllocateEntityId();
        var site = ConstructionBlueprintCatalog.CreateSite(
            id,
            command.Construction,
            command.Position,
            command.EndPosition);
        _constructionSites.Add(id, site);
        Publish(SimulationEventKind.ConstructionOrdered, EntityId.None, id, site.RequiredWood);
        return true;
    }

    private IReadOnlyList<GridPosition> GetConstructionFootprint(
        ConstructionKind kind,
        GridPosition anchor,
        GridPosition end) => kind switch
    {
        ConstructionKind.WoodenWalkway => SimulationCommand.GetWalkwayCells(anchor, end),
        ConstructionKind.GoblinFieldCamp =>
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
        IReadOnlyList<GridPosition> footprint)
    {
        if (footprint.Any(position => !Map.IsWithin(position)) ||
            _storageZones.Values.Any(zone => footprint.Contains(zone.Position)))
        {
            return false;
        }

        return kind switch
        {
            ConstructionKind.FoodStorage or ConstructionKind.WoodStorage =>
                World.IsSurfaceTraversable(anchor),
            ConstructionKind.WoodenWalkway => World.CanBuildWalkway(footprint),
            ConstructionKind.GoblinFieldCamp => World.CanBuildGoblinFieldCamp(anchor),
            _ => false,
        };
    }

    private bool CompleteConstruction(ActorState builder, ConstructionSiteState site)
    {
        if (!CanPlaceConstruction(site.Kind, site.Anchor, site.GetFootprint()))
        {
            return false;
        }

        EntityId completedTarget;
        var experience = 10;
        switch (site.Kind)
        {
            case ConstructionKind.FoodStorage:
            case ConstructionKind.WoodStorage:
                var acceptedResource = site.Kind == ConstructionKind.FoodStorage
                    ? ResourceKind.Food
                    : ResourceKind.Wood;
                var capacity = acceptedResource == ResourceKind.Food
                    ? Definitions.Storage.SmallFoodCapacity
                    : 64;
                completedTarget = AllocateStorageZone(site.Anchor, acceptedResource, capacity).Id;
                break;
            case ConstructionKind.WoodenWalkway:
                var cells = site.GetFootprint();
                _undeliveredWorldChanges.Add(World.BuildWalkway(cells, CurrentTick));
                completedTarget = EntityId.None;
                experience = Math.Max(5, cells.Count * 3);
                break;
            case ConstructionKind.GoblinFieldCamp:
                _undeliveredWorldChanges.Add(World.BuildGoblinFieldCamp(site.Anchor, CurrentTick));
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
            default:
                throw new InvalidOperationException("Unsupported construction blueprint.");
        }

        _constructionSites.Remove(site.Id);
        GainBuildingExperience(builder, experience);
        Publish(
            SimulationEventKind.ConstructionCompleted,
            builder.Id,
            completedTarget,
            site.RequiredWood);
        return true;
    }

    private bool TryExecuteMove(SimulationCommand command)
    {
        if (!_actors.TryGetValue(command.Subject, out var actor) ||
            !World.IsSurfaceTraversable(command.Position))
        {
            return false;
        }

        var route = World.FindSurfacePath(actor.Position, command.Position);
        if (route is null)
        {
            return false;
        }

        actor.ClearJob();
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
        if (!World.IsSurfaceTraversable(command.Position) ||
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
            command.Amount <= 0 ||
            command.Amount > source.Quantity ||
            command.Amount > Definitions.ActorCarryCapacity)
        {
            return false;
        }

        var sourcePosition = source.Location.Position;
        if (!World.HasSurfacePath(actor.Position, sourcePosition))
        {
            return false;
        }

        ItemStackState carried;
        if (command.Amount == source.Quantity)
        {
            carried = source;
            carried.Location = ItemLocation.CarriedBy(actor.Id);
        }
        else
        {
            source.Quantity -= command.Amount;
            carried = AllocateItemStack(
                source.Resource,
                command.Amount,
                ItemLocation.CarriedBy(actor.Id),
                source.FoodKind);
        }

        actor.Position = sourcePosition;
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
            !World.HasSurfacePath(actor.Position, zone.Position))
        {
            return false;
        }

        actor.Position = zone.Position;
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
            if (actor.JobKind is not (ActorJobKind.Rest or ActorJobKind.Collapsed) ||
                actor.JobPhase != ActorJobPhase.Working)
            {
                actor.Fatigue = Math.Min(
                    Definitions.MaximumFatigue,
                    checked(actor.Fatigue + Definitions.FatiguePerTick));
            }

            if (actor.Fatigue >= Definitions.MaximumFatigue &&
                actor.JobKind is not (ActorJobKind.Rest or ActorJobKind.Collapsed))
            {
                if (actor.CarriedStackId != EntityId.None &&
                    _itemStacks.TryGetValue(actor.CarriedStackId, out var carried))
                {
                    carried.Location = ItemLocation.OnGround(actor.Position);
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
                Definitions.MaximumHunger,
                checked(actor.Hunger + Definitions.HungerPerTick));
            actor.Thirst = Math.Min(
                Definitions.MaximumThirst,
                checked(actor.Thirst + Definitions.ThirstPerTick));

            if (actor.Thirst >= Definitions.DrinkThreshold && actor.PersonalWater > 0)
            {
                actor.PersonalWater--;
                actor.Thirst = Math.Max(0, actor.Thirst - Definitions.WaterHydration);
                Publish(SimulationEventKind.ActorDrank, actor.Id, EntityId.None, 1);
            }

            if (actor.Hunger >= Definitions.EatThreshold && actor.JobKind != ActorJobKind.Eat)
            {
                if (actor.PersonalFood > 0)
                {
                    actor.PersonalFood--;
                    actor.Hunger = Math.Max(
                        0,
                        actor.Hunger - Definitions.Food.GetSatiety(actor.PersonalFoodKind));
                    if (actor.PersonalFood == 0)
                    {
                        actor.PersonalFoodKind = FoodKind.None;
                    }
                    Publish(SimulationEventKind.ActorAte, actor.Id, EntityId.None, 1);
                }
                else
                {
                    TryFeed(actor);
                }
            }

            if (actor.Hunger >= Definitions.StarvationHungerThreshold)
            {
                actor.Health = Math.Max(0, actor.Health - Definitions.StarvationDamagePerTick);
            }
            if (actor.Thirst >= Definitions.DehydrationThirstThreshold)
            {
                actor.Health = Math.Max(0, actor.Health - Definitions.DehydrationDamagePerTick);
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

    private void RemoveDeadActor(ActorState actor)
    {
        actor.ClearJob();
        if (actor.CarriedStackId != EntityId.None &&
            _itemStacks.TryGetValue(actor.CarriedStackId, out var carried))
        {
            carried.Location = ItemLocation.OnGround(actor.Position);
            actor.CarriedStackId = EntityId.None;
        }
        if (actor.PersonalFood > 0)
        {
            var provisions = FindMergeableGroundStack(
                    ResourceKind.Food,
                    actor.Position,
                    actor.PersonalFoodKind)
                ?? AllocateItemStack(
                    ResourceKind.Food,
                    quantity: 0,
                    ItemLocation.OnGround(actor.Position),
                    actor.PersonalFoodKind);
            provisions.Quantity = checked(provisions.Quantity + actor.PersonalFood);
            actor.PersonalFood = 0;
            actor.PersonalFoodKind = FoodKind.None;
        }

        _actors.Remove(actor.Id);
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
        actor.Hunger = Math.Max(0, actor.Hunger - Definitions.Food.GetSatiety(foodStack.FoodKind));
        if (actor.JobKind == ActorJobKind.Haul &&
            actor.JobStage == ActorJobStage.Delivering &&
            actor.CarriedStackId == foodStack.Id)
        {
            actor.ReservedQuantity = foodStack.Quantity;
        }
        Publish(SimulationEventKind.ActorAte, actor.Id, foodStack.Id, 1);

        if (foodStack.Quantity == 0)
        {
            _itemStacks.Remove(foodStack.Id);
            if (actor.CarriedStackId == foodStack.Id)
            {
                actor.CarriedStackId = EntityId.None;
                actor.ClearJob();
            }

            Publish(SimulationEventKind.ItemStackDepleted, actor.Id, foodStack.Id, 0);
        }

        return true;
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
        int amount)
    {
        var simulationEvent = new SimulationEvent(
            _nextEventSequence,
            CurrentTick,
            kind,
            subject,
            target,
            amount);

        _nextEventSequence = checked(_nextEventSequence + 1);
        _undeliveredEvents.Add(simulationEvent);
        _eventsPublished = checked(_eventsPublished + 1);
    }

    private void ValidateCommandForQueue(SimulationCommand command)
    {
        if (command.ExecuteAt.Value <= CurrentTick.Value)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                $"Commands must target a future tick after {CurrentTick}.");
        }

        if (!Enum.IsDefined(command.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(command), $"Unsupported command kind {command.Kind}.");
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
                if (!World.IsSurfaceTraversable(command.Position) ||
                    !IsStorableResource(command.Resource) ||
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
                if (!Map.IsWithin(command.Position))
                {
                    throw new ArgumentException("Move destination is outside the map.", nameof(command));
                }

                break;
            case SimulationCommandKind.Build:
                if (!Enum.IsDefined(command.Construction) ||
                    command.Resource != ResourceKind.Wood ||
                    !Map.IsWithin(command.Position) ||
                    !Map.IsWithin(command.EndPosition))
                {
                    throw new ArgumentException("Construction command is invalid.", nameof(command));
                }

                if ((command.Construction is ConstructionKind.FoodStorage or ConstructionKind.WoodStorage) &&
                    (command.Position != command.EndPosition || command.Amount != 2))
                {
                    throw new ArgumentException("Food storage construction is invalid.", nameof(command));
                }

                if (command.Construction == ConstructionKind.WoodenWalkway &&
                    (command.Amount != 1 || command.Position.Z != command.EndPosition.Z))
                {
                    throw new ArgumentException("Walkway construction is invalid.", nameof(command));
                }

                if (command.Construction == ConstructionKind.GoblinFieldCamp &&
                    (command.Amount != 6 ||
                     command.EndPosition != command.Position with
                     {
                         X = command.Position.X + 1,
                         Y = command.Position.Y + 1,
                     }))
                {
                    throw new ArgumentException("Field-camp construction is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.DesignateWork:
                if (command.Resource is not (ResourceKind.Food or ResourceKind.Wood or ResourceKind.Vegetation) ||
                    !IsValidArea(command.Position, command.EndPosition))
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
            case SimulationCommandKind.ConfigureStoragePull:
                if (!_storageZones.TryGetValue(command.Target, out var zone) ||
                    command.Amount < 0 || command.Amount > zone.Capacity)
                {
                    throw new ArgumentException("Storage pull command is invalid.", nameof(command));
                }

                break;
            case SimulationCommandKind.AttackHumanVillage:
                if (command.Subject != EntityId.None || command.Target != EntityId.None ||
                    command.Amount != 0)
                {
                    throw new ArgumentException("Human-village attack command is invalid.", nameof(command));
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
        var actor = new ActorState(id, position, hunger)
        {
            Name = CreateGoblinName(id),
            KnownSkills = CreateGoblinSkills(id),
            KnownTraits = CreateGoblinTraits(id),
            Equipment = CreateGoblinEquipment(id),
            Health = health ?? Definitions.MaximumHealth,
            PersonalWater = Definitions.PersonalWaterCapacity,
        };
        _actors.Add(id, actor);
        return actor;
    }

    private ItemStackState AllocateItemStack(
        ResourceKind resource,
        int quantity,
        ItemLocation location,
        FoodKind foodKind = FoodKind.None)
    {
        if (resource == ResourceKind.Food && foodKind == FoodKind.None)
        {
            foodKind = FoodKind.DriedRations;
        }

        var id = AllocateEntityId();
        var stack = new ItemStackState(id, resource, foodKind, quantity, location);
        _itemStacks.Add(id, stack);
        return stack;
    }

    private StorageZoneState AllocateStorageZone(
        GridPosition position,
        ResourceKind acceptedResource,
        int capacity,
        int desiredQuantity = 0)
    {
        var id = AllocateEntityId();
        var zone = new StorageZoneState(
            id,
            position,
            acceptedResource,
            capacity,
            desiredQuantity);
        _storageZones.Add(id, zone);
        return zone;
    }

    private EntityId AllocateEntityId()
    {
        var id = new EntityId(_nextEntityId);
        _nextEntityId = checked(_nextEntityId + 1);
        return id;
    }

    private ItemStackState? FindMergeableGroundStack(
        ResourceKind resource,
        GridPosition position,
        FoodKind foodKind = FoodKind.None) =>
        _itemStacks.Values.FirstOrDefault(stack =>
            stack.Resource == resource &&
            (resource != ResourceKind.Food || stack.FoodKind ==
                (foodKind == FoodKind.None ? FoodKind.DriedRations : foodKind)) &&
            stack.Location == ItemLocation.OnGround(position));

    private int GetTotalResourceQuantity(ResourceKind resource) => checked(
        _itemStacks.Values
            .Where(stack => stack.Resource == resource)
            .Sum(stack => stack.Quantity) +
        (resource == ResourceKind.Food
            ? _actors.Values.Sum(actor => actor.PersonalFood)
            : 0));

    private string CreateGoblinName(EntityId id)
    {
        string[] beginnings = ["Gr", "Kr", "Sn", "Br", "Zg", "Tr", "Gl", "Wrz"];
        string[] endings = ["uk", "ak", "iz", "og", "yn", "ek", "usz", "ag"];
        var beginning = DeterministicRandom.NextInt(
            WorldSeed,
            RandomDomain.GoblinIdentity,
            id,
            SimulationTick.Zero,
            sampleKey: 1,
            minimumInclusive: 0,
            maximumExclusive: beginnings.Length);
        var ending = DeterministicRandom.NextInt(
            WorldSeed,
            RandomDomain.GoblinIdentity,
            id,
            SimulationTick.Zero,
            sampleKey: 2,
            minimumInclusive: 0,
            maximumExclusive: endings.Length);
        var candidate = beginnings[beginning] + endings[ending];
        return _actors.Values.Any(actor => StringComparer.Ordinal.Equals(actor.Name, candidate))
            ? $"{candidate}-{id.Value}"
            : candidate;
    }

    private GoblinSkill CreateGoblinSkills(EntityId id)
    {
        var primary = DeterministicRandom.NextInt(
            WorldSeed,
            RandomDomain.GoblinIdentity,
            id,
            SimulationTick.Zero,
            sampleKey: 3,
            minimumInclusive: 0,
            maximumExclusive: 5);
        var secondary = DeterministicRandom.NextInt(
            WorldSeed,
            RandomDomain.GoblinIdentity,
            id,
            SimulationTick.Zero,
            sampleKey: 4,
            minimumInclusive: 0,
            maximumExclusive: 5);
        return (GoblinSkill)(1 << primary) | (GoblinSkill)(1 << secondary);
    }

    private GoblinTrait CreateGoblinTraits(EntityId id)
    {
        var first = DeterministicRandom.NextInt(
            WorldSeed,
            RandomDomain.GoblinIdentity,
            id,
            SimulationTick.Zero,
            sampleKey: 5,
            minimumInclusive: 0,
            maximumExclusive: 5);
        var second = DeterministicRandom.NextInt(
            WorldSeed,
            RandomDomain.GoblinIdentity,
            id,
            SimulationTick.Zero,
            sampleKey: 6,
            minimumInclusive: 0,
            maximumExclusive: 5);
        return (GoblinTrait)(1 << first) | (GoblinTrait)(1 << second);
    }

    private PersonalEquipment CreateGoblinEquipment(EntityId id)
    {
        var equipment = PersonalEquipment.RagClothes | PersonalEquipment.PrimitiveWaterskin;
        var hasKnife = DeterministicRandom.NextInt(
            WorldSeed,
            RandomDomain.GoblinIdentity,
            id,
            SimulationTick.Zero,
            sampleKey: 7,
            minimumInclusive: 0,
            maximumExclusive: 2) == 1;
        return hasKnife ? equipment | PersonalEquipment.BoneKnife : equipment;
    }

    private void ScatterInitialBrushwood()
    {
        for (var y = 0; y < Map.Height; y++)
        {
            for (var x = 0; x < Map.Width; x++)
            {
                var position = new GridPosition(x, y);
                var cell = Map.GetCell(position);
                if (cell.Terrain is not (TerrainKind.SolidGround or TerrainKind.Mud) ||
                    cell.Fertility < 45 ||
                    !World.IsSurfaceTraversable(position))
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
                AllocateItemStack(ResourceKind.Wood, quantity, ItemLocation.OnGround(position));
            }
        }

        var nearbyBrushwood = _itemStacks.Values
            .Where(stack =>
            stack.Resource == ResourceKind.Wood &&
            stack.Location.Kind == ItemLocationKind.Ground &&
            ManhattanDistance(stack.Location.Position, Map.GoblinSpawn) <= Definitions.VisionRadius)
            .Sum(stack => stack.Quantity);
        if (nearbyBrushwood >= 4)
        {
            return;
        }

        var fallback = Enumerable.Range(0, Map.Height)
            .SelectMany(y => Enumerable.Range(0, Map.Width)
                .Select(x => new GridPosition(x, y)))
            .Where(position =>
                Map.GetCell(position).Terrain is TerrainKind.SolidGround or TerrainKind.Mud &&
                World.IsSurfaceTraversable(position))
            .OrderBy(position => ManhattanDistance(position, Map.GoblinSpawn))
            .ThenBy(position => position.Y)
            .ThenBy(position => position.X)
            .First();
        AllocateItemStack(
            ResourceKind.Wood,
            4 - nearbyBrushwood,
            ItemLocation.OnGround(fallback));
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
        first.Z == 0 && second.Z == 0 && Map.IsWithin(first) && Map.IsWithin(second);

    private static (GridPosition Minimum, GridPosition Maximum) NormalizeArea(
        GridPosition first,
        GridPosition second) =>
        (new GridPosition(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y), first.Z),
         new GridPosition(Math.Max(first.X, second.X), Math.Max(first.Y, second.Y), first.Z));

    private static bool IsInside(
        GridPosition position,
        GridPosition minimum,
        GridPosition maximum) =>
        position.Z == minimum.Z &&
        position.X >= minimum.X && position.X <= maximum.X &&
        position.Y >= minimum.Y && position.Y <= maximum.Y;

    private bool IsWorkDesignated(WorkDesignationKind kind, GridPosition position) =>
        _workDesignations.Values.Any(designation =>
            designation.Kind == kind && designation.Matches(position));

    private bool IsWorkDesignated(
        WorkDesignationKind kind,
        EntityId targetEntityId,
        GridPosition position) =>
        _workDesignations.Values.Any(designation =>
            designation.Kind == kind && designation.Target == position &&
            designation.TargetEntityId == targetEntityId);

    private void CancelJobsInClearedArea(GridPosition minimum, GridPosition maximum)
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
            item.Location.Kind == ItemLocationKind.StorageZone &&
            item.Location.OwnerId == zone.Id);
        if (existing is null)
        {
            stack.Location = ItemLocation.StoredIn(zone.Id, zone.Position);
            return stack;
        }

        existing.Quantity = checked(existing.Quantity + stack.Quantity);
        _itemStacks.Remove(stack.Id);
        return existing;
    }

    private int GetUsedTypeSlots(EntityId zoneId) =>
        _itemStacks.Values
            .Where(stack => stack.Location.Kind == ItemLocationKind.StorageZone &&
                stack.Location.OwnerId == zoneId)
            .Select(stack => stack.Resource == ResourceKind.Food
                ? (int)stack.FoodKind
                : (int)stack.Resource)
            .Distinct()
            .Count();

    private bool CanStoreStack(StorageZoneState zone, ItemStackState stack, int quantity)
    {
        if (!ZoneAccepts(zone, stack.Resource) || GetStoredQuantity(zone.Id) + quantity > zone.Capacity)
        {
            return false;
        }

        if (!UsesSmallFoodSlotRules(zone) || stack.Resource != ResourceKind.Food)
        {
            return true;
        }

        var storedOfKind = _itemStacks.Values
            .Where(item => item.Location.Kind == ItemLocationKind.StorageZone &&
                item.Location.OwnerId == zone.Id && item.FoodKind == stack.FoodKind)
            .Sum(item => item.Quantity);
        var alreadyUsesSlot = storedOfKind > 0;
        return storedOfKind + quantity <= Definitions.Storage.SmallStackCapacity &&
            (alreadyUsesSlot || GetUsedTypeSlots(zone.Id) < Definitions.Storage.SmallFoodTypeSlots);
    }

    private int GetAvailableStorageQuantity(StorageZoneState zone, ItemStackState stack)
    {
        var totalAvailable = Math.Max(0, zone.Capacity - GetStoredQuantity(zone.Id));
        if (!UsesSmallFoodSlotRules(zone) || stack.Resource != ResourceKind.Food)
        {
            return totalAvailable;
        }

        var storedOfKind = _itemStacks.Values
            .Where(item => item.Location.Kind == ItemLocationKind.StorageZone &&
                item.Location.OwnerId == zone.Id && item.FoodKind == stack.FoodKind)
            .Sum(item => item.Quantity);
        if (storedOfKind == 0 && GetUsedTypeSlots(zone.Id) >= Definitions.Storage.SmallFoodTypeSlots)
        {
            return 0;
        }

        return Math.Min(totalAvailable, Definitions.Storage.SmallStackCapacity - storedOfKind);
    }

    private bool UsesSmallFoodSlotRules(StorageZoneState zone) =>
        zone.AcceptedResource == ResourceKind.Food &&
        zone.Capacity == Definitions.Storage.SmallFoodCapacity;

    private static bool IsValidFoodKind(ResourceKind resource, FoodKind foodKind) =>
        resource == ResourceKind.Food
            ? Enum.IsDefined(foodKind) && foodKind != FoodKind.None
            : foodKind == FoodKind.None;

    private static bool IsValidPersonalFood(int quantity, FoodKind foodKind) =>
        quantity == 0
            ? foodKind == FoodKind.None
            : Enum.IsDefined(foodKind) && foodKind != FoodKind.None;

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
         Visibility.Get(stack.Location.Position) == CellVisibility.Visible);

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
                _itemStacks.Remove(stack.Id);
                Publish(SimulationEventKind.ItemStackDepleted, EntityId.None, stack.Id, 0);
            }

            if (remaining == 0)
            {
                return gatheredDirectly;
            }
        }

        throw new InvalidOperationException("Validated construction resources disappeared.");
    }

    private static bool ZoneAccepts(StorageZoneState zone, ResourceKind resource) =>
        zone.AcceptedResource is ResourceKind.Any || zone.AcceptedResource == resource;

    private static bool IsStorableResource(ResourceKind resource) =>
        Enum.IsDefined(resource) && resource != ResourceKind.Any;

    private static ActorSaveModel ToSaveModel(ActorState actor) => new()
    {
        Id = actor.Id.Value,
        Name = actor.Name,
        KnownSkills = actor.KnownSkills,
        KnownTraits = actor.KnownTraits,
        Equipment = actor.Equipment,
        ForagingExperience = actor.ForagingExperience,
        HaulingExperience = actor.HaulingExperience,
        BuildingExperience = actor.BuildingExperience,
        Hunger = actor.Hunger,
        Fatigue = actor.Fatigue,
        Health = actor.Health,
        Thirst = actor.Thirst,
        PersonalFood = actor.PersonalFood,
        PersonalFoodKind = actor.PersonalFoodKind,
        PersonalWater = actor.PersonalWater,
        X = actor.Position.X,
        Y = actor.Position.Y,
        Z = actor.Position.Z,
        CarriedStackId = actor.CarriedStackId.Value,
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
        RemainingRoute = actor.RemainingRoute.Select(position => new GridPositionSaveModel
        {
            X = position.X,
            Y = position.Y,
            Z = position.Z,
        }).ToList(),
    };

    private static ItemStackSaveModel ToSaveModel(ItemStackState stack) => new()
    {
        Id = stack.Id.Value,
        Resource = stack.Resource,
        FoodKind = stack.FoodKind,
        Quantity = stack.Quantity,
        LocationKind = stack.Location.Kind,
        X = stack.Location.Position.X,
        Y = stack.Location.Position.Y,
        Z = stack.Location.Position.Z,
        OwnerId = stack.Location.OwnerId.Value,
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

    private static TimeSpan StopwatchTicksToTimeSpan(long stopwatchTicks) =>
        TimeSpan.FromSeconds((double)stopwatchTicks / Stopwatch.Frequency);

    private sealed class ActorState(EntityId id, GridPosition position, int hunger)
    {
        public EntityId Id { get; } = id;

        public string Name { get; set; } = string.Empty;

        public GoblinSkill KnownSkills { get; set; }

        public GoblinTrait KnownTraits { get; set; }

        public PersonalEquipment Equipment { get; set; }

        public int ForagingExperience { get; set; }

        public int HaulingExperience { get; set; }

        public int BuildingExperience { get; set; }

        public GridPosition Position { get; set; } = position;

        public int Hunger { get; set; } = hunger;

        public int Fatigue { get; set; }

        public int Health { get; set; }

        public int Thirst { get; set; }

        public int PersonalFood { get; set; }

        public FoodKind PersonalFoodKind { get; set; }

        public int PersonalWater { get; set; }

        public EntityId CarriedStackId { get; set; } = EntityId.None;

        public ActorJobKind JobKind { get; set; }

        public ActorJobPhase JobPhase { get; set; }

        public ActorJobStage JobStage { get; set; }

        public GridPosition JobTarget { get; set; }

        public int RemainingWorkTicks { get; set; }

        public EntityId SourceStackId { get; set; }

        public EntityId DestinationZoneId { get; set; }

        public int ReservedQuantity { get; set; }

        public List<GridPosition> RemainingRoute { get; } = [];

        public ActorJobKind SuspendedJobKind { get; set; }

        public GridPosition SuspendedJobTarget { get; set; }

        public void SuspendCurrentJob()
        {
            if (JobKind is ActorJobKind.Move or ActorJobKind.Explore or ActorJobKind.Forage or
                ActorJobKind.ClearVegetation or ActorJobKind.Rest)
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
    }

    private sealed class ItemStackState(
        EntityId id,
        ResourceKind resource,
        FoodKind foodKind,
        int quantity,
        ItemLocation location)
    {
        public EntityId Id { get; } = id;

        public ResourceKind Resource { get; } = resource;

        public FoodKind FoodKind { get; } = foodKind;

        public int Quantity { get; set; } = quantity;

        public ItemLocation Location { get; set; } = location;
    }

    private sealed class StorageZoneState(
        EntityId id,
        GridPosition position,
        ResourceKind acceptedResource,
        int capacity,
        int desiredQuantity = 0)
    {
        public EntityId Id { get; } = id;

        public GridPosition Position { get; } = position;

        public ResourceKind AcceptedResource { get; } = acceptedResource;

        public int Capacity { get; } = capacity;

        public int DesiredQuantity { get; set; } = desiredQuantity;
    }
}
