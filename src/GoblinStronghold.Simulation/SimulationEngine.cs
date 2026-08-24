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
    private const int SaveFormatVersion = 15;
    private const int DefaultMapDimension = 32;

    private static readonly JsonSerializerOptions SaveOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly SortedDictionary<EntityId, ActorState> _actors = [];
    private readonly SortedDictionary<EntityId, ItemStackState> _itemStacks = [];
    private readonly SortedDictionary<EntityId, StorageZoneState> _storageZones = [];
    private readonly SortedDictionary<CommandKey, SimulationCommand> _pendingCommands = [];
    private readonly List<SimulationEvent> _undeliveredEvents = [];
    private readonly List<WorldChangeEvent> _undeliveredWorldChanges = [];
    private HumanVillageState _humanVillage;
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
        GeneratedMap map)
    {
        WorldSeed = worldSeed;
        Definitions = definitions;
        World = WorldMapState.CreateInitial(map);
        Visibility = WorldVisibilityState.Create(map);
        _humanVillage = HumanVillageState.CreateInitial(World, definitions);
    }

    public WorldSeed WorldSeed { get; }

    public SimulationDefinitions Definitions { get; }

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
        int? initialHealth = null) =>
        Create(
            worldSeed,
            definitions,
            SwampMapGenerator.Generate(worldSeed, DefaultMapDimension, DefaultMapDimension),
            initialGoblinCount,
            initialFoodStock,
            initialHunger,
            initialHealth);

    public static SimulationEngine Create(
        WorldSeed worldSeed,
        SimulationDefinitions definitions,
        GeneratedMap map,
        int initialGoblinCount,
        int initialFoodStock,
        int initialHunger = 0,
        int? initialHealth = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentOutOfRangeException.ThrowIfNegative(initialGoblinCount);
        ArgumentOutOfRangeException.ThrowIfNegative(initialFoodStock);
        ArgumentOutOfRangeException.ThrowIfNegative(initialHunger);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(initialHunger, definitions.MaximumHunger);
        var actorHealth = initialHealth ?? definitions.MaximumHealth;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actorHealth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(actorHealth, definitions.MaximumHealth);

        if (map.Seed != worldSeed)
        {
            throw new ArgumentException("Map seed must match the simulation world seed.", nameof(map));
        }

        var engine = new SimulationEngine(worldSeed, definitions, map);

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

        engine.UpdateVisibility();

        return engine;
    }

    public static SimulationEngine Load(string saveJson, SimulationDefinitions definitions)
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

        var engine = new SimulationEngine(worldSeed, definitions, map)
        {
            CurrentTick = new SimulationTick(save.CurrentTick),
            _nextEntityId = save.NextEntityId,
            _nextEventSequence = save.NextEventSequence,
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
        engine.LoadStorageZones(save.StorageZones);
        engine.LoadItemStacks(save.ItemStacks);
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
                actor.Position,
                actor.Hunger,
                actor.Fatigue,
                actor.Health,
                actor.Thirst,
                actor.PersonalFood,
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
                    actor.ReservedQuantity)))
            .ToArray();
        var itemStacks = _itemStacks.Values
            .Select(stack => new ItemStackSnapshot(stack.Id, stack.Resource, stack.Quantity, stack.Location))
            .ToArray();
        var storageZones = _storageZones.Values
            .Select(zone => new StorageZoneSnapshot(
                zone.Id,
                zone.Position,
                zone.AcceptedResource,
                zone.Capacity,
                GetStoredQuantity(zone.Id)))
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
            plantPatches,
            worldObjects,
            humanVillage,
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
            WorldSeed = WorldSeed.Value,
            MapGeneratorVersion = Map.GeneratorVersion,
            MapWidth = Map.Width,
            MapHeight = Map.Height,
            MapFingerprint = Map.ComputeFingerprint(),
            CurrentTick = CurrentTick.Value,
            NextEntityId = _nextEntityId,
            NextEventSequence = _nextEventSequence,
            WorldVersion = World.Version,
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
        Append(canonical, WorldSeed.Value);
        Append(canonical, Map.GeneratorVersion);
        Append(canonical, Map.ComputeFingerprint());
        Append(canonical, World.Version);
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
            Append(canonical, actor.Position);
            Append(canonical, actor.Hunger);
            Append(canonical, actor.Fatigue);
            Append(canonical, actor.Health);
            Append(canonical, actor.Thirst);
            Append(canonical, actor.PersonalFood);
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
        }

        Append(canonical, _itemStacks.Count);
        foreach (var stack in _itemStacks.Values)
        {
            Append(canonical, stack.Id.Value);
            Append(canonical, (int)stack.Resource);
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
                actorModel.Hunger < 0 || actorModel.Hunger > Definitions.MaximumHunger ||
                actorModel.Fatigue < 0 || actorModel.Fatigue > Definitions.MaximumFatigue ||
                actorModel.Health <= 0 || actorModel.Health > Definitions.MaximumHealth ||
                actorModel.Thirst < 0 || actorModel.Thirst > Definitions.MaximumThirst ||
                actorModel.PersonalFood < 0 || actorModel.PersonalFood > Definitions.PersonalFoodCapacity ||
                actorModel.PersonalWater < 0 || actorModel.PersonalWater > Definitions.PersonalWaterCapacity ||
                !World.IsSurfaceTraversable(position))
            {
                throw new InvalidDataException("The save contains an invalid actor.");
            }

            var actor = new ActorState(id, position, actorModel.Hunger)
            {
                CarriedStackId = new EntityId(actorModel.CarriedStackId),
                Fatigue = actorModel.Fatigue,
                Health = actorModel.Health,
                Thirst = actorModel.Thirst,
                PersonalFood = actorModel.PersonalFood,
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
                        zoneModel.Capacity)))
            {
                throw new InvalidDataException($"The save contains duplicate storage zone {id}.");
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
                stackModel.Quantity <= 0)
            {
                throw new InvalidDataException("The save contains an invalid item stack.");
            }

            if (!_itemStacks.TryAdd(
                    id,
                    new ItemStackState(id, stackModel.Resource, stackModel.Quantity, location)))
            {
                throw new InvalidDataException($"The save contains duplicate item stack {id}.");
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
            _undeliveredWorldChanges.AddRange(
                World.GrowPlants(CurrentTick, Definitions.PlantGrowthPerInterval));
        }
    }

    private void UpdateVisibility()
    {
        Visibility.Reveal(
            _actors.Values.Select(actor => actor.Position),
            Definitions.VisionRadius);
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
        _ => false,
    };

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
        var stack = FindMergeableGroundStack(ResourceKind.Food, actor.Position)
            ?? AllocateItemStack(ResourceKind.Food, quantity: 0, ItemLocation.OnGround(actor.Position));
        stack.Quantity = checked(stack.Quantity + gathered);
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

        var zone = AllocateStorageZone(command.Position, command.Resource, command.Amount);
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
                ItemLocation.CarriedBy(actor.Id));
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
            !ZoneAccepts(zone, carried.Resource) ||
            GetStoredQuantity(zone.Id) + carried.Quantity > zone.Capacity ||
            !World.HasSurfacePath(actor.Position, zone.Position))
        {
            return false;
        }

        actor.Position = zone.Position;
        actor.CarriedStackId = EntityId.None;
        actor.ClearJob();
        carried.Location = ItemLocation.StoredIn(zone.Id, zone.Position);
        Publish(SimulationEventKind.ItemStored, actor.Id, carried.Id, carried.Quantity);
        return true;
    }

    private void UpdateActors()
    {
        var deadActors = new List<ActorState>();
        foreach (var actor in _actors.Values)
        {
            if (actor.JobKind != ActorJobKind.Rest || actor.JobPhase != ActorJobPhase.Working)
            {
                actor.Fatigue = Math.Min(
                    Definitions.MaximumFatigue,
                    checked(actor.Fatigue + Definitions.FatiguePerTick));
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
                    actor.Hunger = Math.Max(0, actor.Hunger - Definitions.FoodNutrition);
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
            var provisions = FindMergeableGroundStack(ResourceKind.Food, actor.Position)
                ?? AllocateItemStack(ResourceKind.Food, quantity: 0, ItemLocation.OnGround(actor.Position));
            provisions.Quantity = checked(provisions.Quantity + actor.PersonalFood);
            actor.PersonalFood = 0;
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
        actor.Hunger = Math.Max(0, actor.Hunger - Definitions.FoodNutrition);
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
            Health = health ?? Definitions.MaximumHealth,
            PersonalWater = Definitions.PersonalWaterCapacity,
        };
        _actors.Add(id, actor);
        return actor;
    }

    private ItemStackState AllocateItemStack(
        ResourceKind resource,
        int quantity,
        ItemLocation location)
    {
        var id = AllocateEntityId();
        var stack = new ItemStackState(id, resource, quantity, location);
        _itemStacks.Add(id, stack);
        return stack;
    }

    private StorageZoneState AllocateStorageZone(
        GridPosition position,
        ResourceKind acceptedResource,
        int capacity)
    {
        var id = AllocateEntityId();
        var zone = new StorageZoneState(id, position, acceptedResource, capacity);
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
        GridPosition position) =>
        _itemStacks.Values.FirstOrDefault(stack =>
            stack.Resource == resource &&
            stack.Location == ItemLocation.OnGround(position));

    private int GetTotalResourceQuantity(ResourceKind resource) => checked(
        _itemStacks.Values
            .Where(stack => stack.Resource == resource)
            .Sum(stack => stack.Quantity) +
        (resource == ResourceKind.Food
            ? _actors.Values.Sum(actor => actor.PersonalFood)
            : 0));

    private int GetStoredQuantity(EntityId zoneId) =>
        _itemStacks.Values
            .Where(stack =>
                stack.Location.Kind == ItemLocationKind.StorageZone &&
                stack.Location.OwnerId == zoneId)
            .Sum(stack => stack.Quantity);

    private static bool ZoneAccepts(StorageZoneState zone, ResourceKind resource) =>
        zone.AcceptedResource is ResourceKind.Any || zone.AcceptedResource == resource;

    private static bool IsStorableResource(ResourceKind resource) =>
        Enum.IsDefined(resource) && resource != ResourceKind.Any;

    private static ActorSaveModel ToSaveModel(ActorState actor) => new()
    {
        Id = actor.Id.Value,
        Hunger = actor.Hunger,
        Fatigue = actor.Fatigue,
        Health = actor.Health,
        Thirst = actor.Thirst,
        PersonalFood = actor.PersonalFood,
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

        public GridPosition Position { get; set; } = position;

        public int Hunger { get; set; } = hunger;

        public int Fatigue { get; set; }

        public int Health { get; set; }

        public int Thirst { get; set; }

        public int PersonalFood { get; set; }

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
        int quantity,
        ItemLocation location)
    {
        public EntityId Id { get; } = id;

        public ResourceKind Resource { get; } = resource;

        public int Quantity { get; set; } = quantity;

        public ItemLocation Location { get; set; } = location;
    }

    private sealed class StorageZoneState(
        EntityId id,
        GridPosition position,
        ResourceKind acceptedResource,
        int capacity)
    {
        public EntityId Id { get; } = id;

        public GridPosition Position { get; } = position;

        public ResourceKind AcceptedResource { get; } = acceptedResource;

        public int Capacity { get; } = capacity;
    }
}
