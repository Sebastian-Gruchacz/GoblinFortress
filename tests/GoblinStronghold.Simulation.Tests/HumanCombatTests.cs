using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class HumanCombatTests
{
    [Fact]
    public void AccidentalEncounterCanBecomeFightWithoutBecomingRaidOrder()
    {
        var engine = CreateEncounterEngine();

        engine.AdvanceTicks(720);

        var snapshot = engine.CreateSnapshot();
        var events = engine.DrainEvents();
        Assert.False(snapshot.HumanVillage.GoblinAttackOrdered);
        Assert.InRange(snapshot.HumanVillage.LastIntruderSeenTick, 0, snapshot.Tick.Value);
        Assert.True(snapshot.HumanVillage.GuardHitPoints < snapshot.HumanVillage.MaximumGuardHitPoints);
        Assert.Contains(events, item => item.Kind == SimulationEventKind.HumanVillageAlerted);
        Assert.Contains(events, item => item.Kind == SimulationEventKind.HumanGuardHitGoblin);
        Assert.Contains(events, item => item.Kind == SimulationEventKind.GoblinHitHumanGuard);
    }

    [Fact]
    public void ExplicitRaidOrderStagesAndSendsTribe()
    {
        var engine = CreateEncounterEngine(orderRaid: true);

        engine.AdvanceTicks(1_600);

        var snapshot = engine.CreateSnapshot();
        var events = engine.DrainEvents();
        Assert.False(snapshot.HumanVillage.GoblinAttackOrdered);
        Assert.Equal(GoblinRaidPhase.None, snapshot.RaidPhase);
        Assert.Equal(100, snapshot.HumanVillage.Hostility);
        Assert.Equal(0, snapshot.HumanVillage.GuardHitPoints);
        var departure = Assert.Single(events,
            item => item.Kind == SimulationEventKind.RaidDeparted);
        Assert.Equal(SimulationDefinitions.FieldCampCapacity, departure.Amount);
        Assert.Contains(events, item => item.Kind == SimulationEventKind.RaidVictory);
    }

    [Fact]
    public void RaidVictoryClearsActiveLifecycleAndKeepsSurvivingPartySelection()
    {
        var engine = CreateEncounterEngine(orderRaid: true);
        var preparing = engine.CreateSnapshot();
        Assert.Equal(GoblinRaidPhase.Preparing, preparing.RaidPhase);
        var party = preparing.RaidPartyIds.ToArray();
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        save["raidPhase"] = (int)GoblinRaidPhase.Marching;
        save["humanVillage"]!["goblinAttackOrdered"] = true;
        save["humanVillage"]!["hostility"] = 100;
        save["humanVillage"]!["lastIntruderSeenTick"] = save["currentTick"]!.GetValue<long>();
        var guard = save["humanVillage"]!["cohorts"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(model => model["role"]!.GetValue<int>() == (int)HumanCohortRole.Guards);
        var guardPopulation = guard["population"]!.GetValue<int>();
        guard["population"] = 0;
        save["humanVillage"]!["guardHitPoints"] = 0;
        save["humanVillage"]!["population"] =
            save["humanVillage"]!["population"]!.GetValue<int>() - guardPopulation;
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);

        engine.AdvanceTicks(1);

        var completed = engine.CreateSnapshot();
        Assert.Equal(GoblinRaidPhase.None, completed.RaidPhase);
        Assert.Equal(default, completed.RaidRallyPoint);
        Assert.False(completed.HumanVillage.GoblinAttackOrdered);
        Assert.Equal(party, completed.RaidPartyIds);
        var victory = Assert.Single(engine.DrainEvents(), item =>
            item.Kind == SimulationEventKind.RaidVictory);
        Assert.Equal(party.Length, victory.Amount);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
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

    [Fact]
    public void SlingExtendsGoblinAttackRangeAndConsumesPersonalStone()
    {
        var seed = new WorldSeed(0x511A6UL);
        var map = SwampMapGenerator.Generate(seed, width: 64, height: 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 0);
        var guard = engine.CreateSnapshot().HumanVillage.Cohorts.Single(cohort =>
            cohort.Role == HumanCohortRole.Guards);
        var firingPosition = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width)
                .Select(x => new GridPosition(x, y)))
            .Where(engine.World.IsSurfaceTraversable)
            .Where(position => Math.Abs(position.X - guard.Position.X) +
                Math.Abs(position.Y - guard.Position.Y) is >= 3 and <= 5)
            .OrderBy(position => Math.Abs(position.X - guard.Position.X) +
                Math.Abs(position.Y - guard.Position.Y))
            .First();
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var actor = save["actors"]!.AsArray()[0]!.AsObject();
        actor["x"] = firingPosition.X;
        actor["y"] = firingPosition.Y;
        actor["z"] = firingPosition.Z;
        actor["equipment"] = actor["equipment"]!.GetValue<int>() |
            (int)PersonalEquipment.PrimitiveSling;
        actor["personalStoneAmmo"] = 1;
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        var guardHealth = engine.CreateSnapshot().HumanVillage.GuardHitPoints;

        for (var index = 0; index < 200 &&
             engine.CreateSnapshot().Actors[0].PersonalStoneAmmo > 0; index++)
        {
            engine.AdvanceTicks(1);
        }

        var snapshot = engine.CreateSnapshot();
        Assert.Equal(0, snapshot.Actors[0].PersonalStoneAmmo);
        Assert.True(snapshot.HumanVillage.GuardHitPoints < guardHealth);
        Assert.Contains(engine.DrainEvents(), simulationEvent =>
            simulationEvent.Kind == SimulationEventKind.GoblinHitHumanGuard);
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

    private static SimulationEngine CreateEncounterEngine(bool orderRaid = false)
    {
        var seed = new WorldSeed(0x474F424C494EUL);
        var map = SwampMapGenerator.Generate(seed, width: 64, height: 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 8,
            initialFoodStock: orderRaid ? 160 : 12,
            initialWoodStock: orderRaid ? 10 : 0);
        if (!orderRaid)
        {
            engine.QueueCommand(SimulationCommand.CreateStorageZone(
                new SimulationTick(1),
                sequence: 1,
                map.HumanVillage,
                ResourceKind.Food,
                capacity: 20));
        }
        if (orderRaid)
        {
            var camp = Enumerable.Range(0, map.Height)
                .SelectMany(y => Enumerable.Range(0, map.Width)
                    .Select(x => new GridPosition(x, y)))
                .Where(engine.World.CanBuildGoblinFieldCamp)
                .OrderBy(position => Math.Abs(position.X - map.GoblinSpawn.X) +
                    Math.Abs(position.Y - map.GoblinSpawn.Y))
                .ThenBy(position => position.Y)
                .ThenBy(position => position.X)
                .First();
            engine.QueueCommand(SimulationCommand.BuildGoblinFieldCamp(
                new SimulationTick(1), sequence: 998, camp));
        }
        engine.AdvanceTicks(1);

        if (orderRaid)
        {
            SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
            engine.QueueCommand(SimulationCommand.AttackHumanVillage(
                engine.CurrentTick.Next(), sequence: 999));
            engine.AdvanceTicks(1);
            return engine;
        }

        var setup = engine.CreateSnapshot();
        var food = setup.ItemStacks.Single(stack => stack.Resource == ResourceKind.Food);
        var storage = setup.StorageZones.Single(zone => zone.Position == map.HumanVillage);
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
