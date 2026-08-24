using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

const long finalTick = 720;
var definitions = SimulationDefinitions.Foundation;
var map = SwampMapGenerator.Generate(new WorldSeed(0x474F424C494EUL), width: 64, height: 64);

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

if (!StringComparer.Ordinal.Equals(midpointHash, accelerated.ComputeStateHash()))
{
    Console.Error.WriteLine("Save/load changed authoritative state at the midpoint.");
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
    $"goods {acceleratedSnapshot.HumanVillage.GoodsStock}, " +
    $"hostility {acceleratedSnapshot.HumanVillage.Hostility}, " +
    $"guard health {acceleratedSnapshot.HumanVillage.GuardHitPoints}/" +
    $"{acceleratedSnapshot.HumanVillage.MaximumGuardHitPoints}");
Console.WriteLine($"Physical stacks: {acceleratedSnapshot.ItemStacks.Count}");
Console.WriteLine($"Storage zones: {acceleratedSnapshot.StorageZones.Count}");
Console.WriteLine($"Plant patches: {acceleratedSnapshot.PlantPatches.Count}");
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
