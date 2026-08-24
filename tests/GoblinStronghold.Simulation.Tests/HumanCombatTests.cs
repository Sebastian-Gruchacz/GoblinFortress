using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class HumanCombatTests
{
    [Fact]
    public void ExplorerAlertsVillageAndTradesDamageWithGuards()
    {
        var engine = CreateEncounterEngine();

        engine.AdvanceTicks(720);

        var snapshot = engine.CreateSnapshot();
        var events = engine.DrainEvents();
        Assert.Equal(100, snapshot.HumanVillage.Hostility);
        Assert.InRange(snapshot.HumanVillage.LastIntruderSeenTick, 0, snapshot.Tick.Value);
        Assert.True(snapshot.HumanVillage.GuardHitPoints < snapshot.HumanVillage.MaximumGuardHitPoints);
        Assert.True(snapshot.Actors.Count < 8);
        Assert.Contains(events, item => item.Kind == SimulationEventKind.HumanVillageAlerted);
        Assert.Contains(events, item => item.Kind == SimulationEventKind.HumanGuardHitGoblin);
        Assert.Contains(events, item => item.Kind == SimulationEventKind.GoblinHitHumanGuard);
        Assert.Contains(events, item => item.Kind == SimulationEventKind.ActorDied);
    }

    [Fact]
    public void AlertedEncounterContinuesIdenticallyAfterSaveLoad()
    {
        var engine = CreateEncounterEngine();
        AdvanceUntilAlerted(engine, maximumTicks: 720);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());

        engine.AdvanceTicks(500);
        restored.AdvanceTicks(500);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(
            engine.CreateSnapshot().HumanVillage.GuardHitPoints,
            restored.CreateSnapshot().HumanVillage.GuardHitPoints);
        Assert.Equal(
            engine.CreateSnapshot().HumanVillage.Population,
            restored.CreateSnapshot().HumanVillage.Population);
    }

    [Fact]
    public void LoadRejectsGuardHealthThatDoesNotMatchPopulation()
    {
        var engine = CreateEncounterEngine();
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        save["humanVillage"]!["guardHitPoints"] = 1;

        var exception = Assert.Throws<InvalidDataException>(() =>
            SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation));

        Assert.Contains("guard health", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void AdvanceUntilAlerted(SimulationEngine engine, int maximumTicks)
    {
        for (var index = 0; index < maximumTicks; index++)
        {
            engine.AdvanceTicks(1);
            if (engine.CreateSnapshot().HumanVillage.Hostility > 0)
            {
                return;
            }
        }

        throw new Xunit.Sdk.XunitException("The explorer did not alert the village in time.");
    }

    private static SimulationEngine CreateEncounterEngine()
    {
        var seed = new WorldSeed(0x474F424C494EUL);
        var map = SwampMapGenerator.Generate(seed, width: 64, height: 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
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
        var food = setup.ItemStacks.Single(stack => stack.Resource == ResourceKind.Food);
        var storage = setup.StorageZones.Single();
        engine.QueueCommand(SimulationCommand.PickUp(
            new SimulationTick(2),
            sequence: 2,
            new EntityId(1),
            food.Id,
            quantity: 5));
        engine.QueueCommand(SimulationCommand.StoreCarried(
            new SimulationTick(3),
            sequence: 3,
            new EntityId(1),
            storage.Id));

        ulong sequence = 4;
        for (var tick = 24; tick <= 720; tick += 24)
        {
            for (ulong actor = 1; actor <= 8; actor++)
            {
                engine.QueueCommand(SimulationCommand.Forage(
                    new SimulationTick(tick + (long)actor),
                    sequence++,
                    new EntityId(actor)));
            }
        }

        return engine;
    }
}
