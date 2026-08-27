using System.Diagnostics;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

if (args.Contains("--profile-startup", StringComparer.OrdinalIgnoreCase))
{
    var pathIndex = Array.FindIndex(args, argument =>
        argument.Equals("--save", StringComparison.OrdinalIgnoreCase));
    var savePath = pathIndex >= 0 && pathIndex + 1 < args.Length
        ? args[pathIndex + 1]
        : null;
    return RunStartupProfile(savePath);
}

if (args.Contains("--benchmark-day", StringComparer.OrdinalIgnoreCase))
{
    return RunFullDayBenchmark();
}

const long finalTick = 720;
var definitions = SimulationDefinitions.Foundation;
var map = SwampMapGenerator.Generate(
    new WorldSeed(0x474F424C494EUL),
    SwampMapGenerator.DefaultDimension,
    SwampMapGenerator.DefaultDimension);

var accelerated = CreateScenario(definitions, map);
var acceleratedRunner = new SimulationRunner(accelerated);
var renderedSnapshots = 0;

acceleratedRunner.RunUntil(
    new SimulationTick(240),
    SimulationSpeed.Octuple,
    snapshotConsumer: _ => renderedSnapshots++);

var midpointHash = accelerated.ComputeStateHash();
var save = accelerated.Save();
accelerated = SimulationEngine.Load(save, definitions);
var reloadedHash = accelerated.ComputeStateHash();

if (!StringComparer.Ordinal.Equals(midpointHash, reloadedHash))
{
    var reloadedSave = accelerated.Save();
    var sharedLength = Math.Min(save.Length, reloadedSave.Length);
    var differenceAt = Enumerable.Range(0, sharedLength)
        .FirstOrDefault(index => save[index] != reloadedSave[index], -1);
    if (differenceAt < 0 && save.Length != reloadedSave.Length)
    {
        differenceAt = sharedLength;
    }
    var contextStart = Math.Max(0, differenceAt - 80);
    var contextLength = Math.Min(240, Math.Max(save.Length, reloadedSave.Length) - contextStart);
    Console.Error.WriteLine(
        $"Save/load changed authoritative state at the midpoint: {midpointHash} != {reloadedHash}.");
    Console.Error.WriteLine($"First serialized difference at {differenceAt}.");
    Console.Error.WriteLine($"Before: {save.Substring(contextStart, Math.Min(contextLength, save.Length - contextStart))}");
    Console.Error.WriteLine($"After:  {reloadedSave.Substring(contextStart, Math.Min(contextLength, reloadedSave.Length - contextStart))}");
    return 1;
}

acceleratedRunner = new SimulationRunner(accelerated);
acceleratedRunner.RunUntil(
    new SimulationTick(finalTick),
    SimulationSpeed.Unthrottled,
    unthrottledTickBudget: 128);

var acceleratedSnapshot = accelerated.CreateSnapshot();
var acceleratedEvents = accelerated.DrainEvents();
var acceleratedWorldChanges = accelerated.DrainWorldChanges();

var normal = CreateScenario(definitions, map);
var normalRunner = new SimulationRunner(normal);
normalRunner.RunUntil(new SimulationTick(finalTick), SimulationSpeed.Normal);

var normalSnapshot = normal.CreateSnapshot();
var normalEvents = normal.DrainEvents();
var normalWorldChanges = normal.DrainWorldChanges();

if (!StringComparer.Ordinal.Equals(acceleratedSnapshot.StateHash, normalSnapshot.StateHash) ||
    !acceleratedEvents.SequenceEqual(normalEvents) ||
    !acceleratedWorldChanges.SequenceEqual(normalWorldChanges))
{
    Console.Error.WriteLine("Simulation result depends on runner speed or save/load.");
    return 2;
}

var metrics = accelerated.GetMetrics();
var structureParts = acceleratedSnapshot.WorldObjects
    .SelectMany(worldObject => worldObject.GetAbsoluteParts())
    .ToArray();

Console.WriteLine("Goblin Stronghold deterministic simulation foundation");
Console.WriteLine($"Seed: {acceleratedSnapshot.WorldSeed}");
Console.WriteLine($"Tick: {acceleratedSnapshot.Tick}");
Console.WriteLine($"Actors: {acceleratedSnapshot.Actors.Count}");
Console.WriteLine($"Food stock: {acceleratedSnapshot.FoodStock}");
Console.WriteLine(
    $"Personal supplies: food {acceleratedSnapshot.Actors.Sum(actor => actor.PersonalFood)}, " +
    $"water {acceleratedSnapshot.Actors.Sum(actor => actor.PersonalWater)}, " +
    $"maximum thirst {acceleratedSnapshot.Actors.Max(actor => actor.Thirst)}");
Console.WriteLine(
    $"Human village: {acceleratedSnapshot.HumanVillage.Population} people, " +
    $"food {acceleratedSnapshot.HumanVillage.FoodStock}, " +
    $"wood {acceleratedSnapshot.HumanVillage.WoodStock}, " +
    $"water {acceleratedSnapshot.HumanVillage.WaterStock}, " +
    $"fields {acceleratedSnapshot.HumanVillage.Fields.Count}/{acceleratedSnapshot.HumanVillage.PlannedFieldCount}, " +
    $"goods {acceleratedSnapshot.HumanVillage.GoodsStock}, " +
    $"hostility {acceleratedSnapshot.HumanVillage.Hostility}, " +
    $"guard health {acceleratedSnapshot.HumanVillage.GuardHitPoints}/" +
    $"{acceleratedSnapshot.HumanVillage.MaximumGuardHitPoints}");
Console.WriteLine($"Physical stacks: {acceleratedSnapshot.ItemStacks.Count}");
Console.WriteLine($"Storage zones: {acceleratedSnapshot.StorageZones.Count}");
Console.WriteLine($"Plant patches: {acceleratedSnapshot.PlantPatches.Count}");
Console.WriteLine(
    $"Food sources: berries {CountSources(PlantKind.BerryBush)}, " +
    $"mushrooms {CountSources(PlantKind.MushroomCluster)}, " +
    $"roots {CountSources(PlantKind.EdibleRoots)}, " +
    $"fish shoals {CountSources(PlantKind.FishShoal)}");
Console.WriteLine($"Generated structures: {acceleratedSnapshot.WorldObjects.Count}");
Console.WriteLine($"World version: {acceleratedSnapshot.WorldVersion}");
Console.WriteLine($"Terrain baseline: {map.Width}x{map.Height}x{map.LevelCount}");
Console.WriteLine(
    $"Generated structure Z extent: {structureParts.Min(item => item.Position.Z)}..{structureParts.Max(item => item.Position.Z)}");
Console.WriteLine($"Map generator version: {map.GeneratorVersion}");
Console.WriteLine($"Goblin spawn: {map.GoblinSpawn}");
Console.WriteLine($"Human village: {map.HumanVillage}");
Console.WriteLine($"Map fingerprint: {map.ComputeFingerprint()}");
Console.WriteLine($"Events delivered: {acceleratedEvents.Count}");
Console.WriteLine($"World changes delivered: {acceleratedWorldChanges.Count}");
Console.WriteLine($"Rendered midpoint snapshots: {renderedSnapshots}");
Console.WriteLine($"Ticks after reload: {metrics.TicksExecuted}");
Console.WriteLine($"State hash: {acceleratedSnapshot.StateHash}");
Console.WriteLine("Normal, accelerated and unthrottled execution agree.");
return 0;

int CountSources(PlantKind kind) =>
    acceleratedSnapshot.PlantPatches.Count(source => source.Kind == kind);

static SimulationEngine CreateScenario(SimulationDefinitions definitions, GeneratedMap map)
{
    var engine = SimulationEngine.Create(
        new WorldSeed(0x474F424C494EUL),
        definitions,
        map,
        initialGoblinCount: 8,
        initialFoodStock: 12);

    engine.QueueCommand(SimulationCommand.CreateStorageZone(
        new SimulationTick(1),
        sequence: 1,
        map.HumanVillage,
        ResourceKind.Food,
        capacity: 20));
    engine.AdvanceTicks(1);

    var setup = engine.CreateSnapshot();
    var initialFood = setup.ItemStacks.Single(stack => stack.Resource == ResourceKind.Food);
    var storage = setup.StorageZones.Single();
    engine.QueueCommand(SimulationCommand.PickUp(
        new SimulationTick(2),
        sequence: 2,
        new EntityId(1),
        initialFood.Id,
        quantity: 5));
    engine.QueueCommand(SimulationCommand.StoreCarried(
        new SimulationTick(3),
        sequence: 3,
        new EntityId(1),
        storage.Id));

    ulong sequence = 4;
    for (var tick = 24; tick <= finalTick; tick += 24)
    {
        for (ulong actor = 1; actor <= 8; actor++)
        {
            engine.QueueCommand(SimulationCommand.Forage(
                new SimulationTick(tick + (long)actor),
                sequence++,
                new EntityId(actor),
                effort: 1));
        }
    }

    return engine;
}

static int RunFullDayBenchmark()
{
    var seed = new WorldSeed(0x474F424C494EUL);
    var definitions = SimulationDefinitions.Foundation;
    var map = SwampMapGenerator.Generate(seed, width: 64, height: 64);
    var engine = SimulationEngine.Create(
        seed,
        definitions,
        map,
        initialGoblinCount: 8,
        initialFoodStock: 16,
        scatterInitialBrushwood: true);
    engine.QueueCommand(SimulationCommand.CreateStorageZone(
        new SimulationTick(1),
        sequence: 1,
        map.GoblinSpawn,
        ResourceKind.Food,
        definitions.Storage.SmallFoodCapacity));

    var ticks = definitions.Clock.Climate.GetSeason(SeasonKind.Spring).TicksPerDay;
    var stopwatch = Stopwatch.StartNew();
    engine.AdvanceTicks(ticks);
    stopwatch.Stop();
    var metrics = engine.GetMetrics();
    var navigation = metrics.Navigation;
    var hitRate = navigation.Requests == 0
        ? 0
        : 100d * navigation.CacheHits / navigation.Requests;
    Console.WriteLine(
        $"Full demo day ({ticks:N0} ticks): {stopwatch.Elapsed.TotalMilliseconds:F1} ms, " +
        $"mean tick {metrics.TotalTickDuration.TotalMilliseconds / metrics.TicksExecuted:F3} ms, " +
        $"last tick {metrics.LastTickDuration.TotalMilliseconds:F3} ms");
    Console.WriteLine(
        $"Paths: {navigation.Requests:N0} requests, {navigation.Searches:N0} searches, " +
        $"{navigation.CacheHits:N0} hits ({hitRate:F1}%), " +
        $"{navigation.CachedRoutes:N0} cached, {navigation.CacheInvalidations:N0} invalidations");
    return 0;
}

static int RunStartupProfile(string? savePath)
{
    var definitions = SimulationDefinitions.Foundation;
    SimulationEngine engine;
    if (savePath is not null)
    {
        var load = Stopwatch.StartNew();
        engine = SimulationEngine.Load(
            File.ReadAllText(savePath),
            definitions,
            SimulationDebugSettings.ForCurrentBuild);
        Console.WriteLine($"Load: {load.Elapsed.TotalMilliseconds:F1} ms");
    }
    else
    {
        var seed = new WorldSeed(0x50524F46494C45UL);
        var generation = Stopwatch.StartNew();
        var map = SwampMapGenerator.Generate(
            seed,
            SwampMapGenerator.DefaultDimension,
            SwampMapGenerator.DefaultDimension);
        Console.WriteLine($"Map generation: {generation.Elapsed.TotalMilliseconds:F1} ms");
        var creation = Stopwatch.StartNew();
        engine = SimulationEngine.Create(
            seed,
            definitions,
            map,
            initialGoblinCount: 8,
            initialFoodStock: 16,
            scatterInitialBrushwood: true,
            debugSettings: SimulationDebugSettings.ForCurrentBuild);
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1),
            sequence: 1,
            map.GoblinSpawn,
            ResourceKind.Food,
            definitions.Storage.SmallFoodCapacity));
        Console.WriteLine($"Engine creation: {creation.Elapsed.TotalMilliseconds:F1} ms");
    }

    engine.CreatePresentationSnapshot();
    var presentationTimes = Enumerable.Range(0, 10)
        .Select(_ =>
        {
            var stopwatch = Stopwatch.StartNew();
            engine.CreatePresentationSnapshot();
            return stopwatch.Elapsed.TotalMilliseconds;
        })
        .ToArray();
    Console.WriteLine(
        $"Presentation snapshot avg/max: {presentationTimes.Average():F1}/" +
        $"{presentationTimes.Max():F1} ms");
    var authoritativeTimes = Enumerable.Range(0, 3)
        .Select(_ =>
        {
            var stopwatch = Stopwatch.StartNew();
            engine.CreateSnapshot();
            return stopwatch.Elapsed.TotalMilliseconds;
        })
        .ToArray();
    Console.WriteLine(
        $"Authoritative snapshot avg/max: {authoritativeTimes.Average():F1}/" +
        $"{authoritativeTimes.Max():F1} ms");

    var tickTimes = new double[100];
    for (var index = 0; index < tickTimes.Length; index++)
    {
        var tick = Stopwatch.StartNew();
        engine.AdvanceTicks(1);
        tickTimes[index] = tick.Elapsed.TotalMilliseconds;
        if (index < 20 || tickTimes[index] >= 10)
        {
            var stages = engine.GetMetrics().LastTickBreakdown;
            var jobs = engine.GetLastActorJobUpdateProfile();
            Console.WriteLine(
                $"Tick {engine.CurrentTick.Value}: {tickTimes[index]:F1} ms • " +
                $"jobs {stages.ActorJobs.TotalMilliseconds:F1}, " +
                $"interrupt {jobs.NeedInterrupts.TotalMilliseconds:F1}, " +
                $"plan {jobs.IdlePlanning.TotalMilliseconds:F1}, " +
                $"active {jobs.ActiveJobs.TotalMilliseconds:F1}, " +
                $"job final {jobs.Finalization.TotalMilliseconds:F1}, " +
                $"visibility {stages.Visibility.TotalMilliseconds:F1}, " +
                $"humans {stages.HumanVillage.TotalMilliseconds:F1}, " +
                $"animals {stages.Animals.TotalMilliseconds:F1}, " +
                $"actors {stages.Actors.TotalMilliseconds:F1}");
        }
    }

    var ordered = tickTimes.Order().ToArray();
    Console.WriteLine(
        $"Ticks avg/p95/max: {tickTimes.Average():F2}/" +
        $"{ordered[(int)(ordered.Length * 0.95)]:F2}/{tickTimes.Max():F2} ms");
    return 0;
}
