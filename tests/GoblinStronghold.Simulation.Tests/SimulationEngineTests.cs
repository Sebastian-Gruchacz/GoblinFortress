using System.Text.Json.Nodes;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Map;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class SimulationEngineTests
{
    private static readonly SimulationTick FinalTick = new(480);

    [Theory]
    [InlineData(SimulationSpeed.Double)]
    [InlineData(SimulationSpeed.Quadruple)]
    [InlineData(SimulationSpeed.Octuple)]
    [InlineData(SimulationSpeed.Unthrottled)]
    public void ScheduledScenarioHasSameResultAtEverySpeed(SimulationSpeed speed)
    {
        var normal = RunScenario(SimulationSpeed.Normal);
        var accelerated = RunScenario(speed);

        Assert.Equal(normal.Snapshot.StateHash, accelerated.Snapshot.StateHash);
        Assert.Equal(normal.Events, accelerated.Events);
    }

    [Fact]
    public void RepeatedScenarioProducesSameHashAndEvents()
    {
        var first = RunScenario(SimulationSpeed.Unthrottled);
        var second = RunScenario(SimulationSpeed.Unthrottled);

        Assert.Equal(first.Snapshot.StateHash, second.Snapshot.StateHash);
        Assert.Equal(first.Events, second.Events);
    }

    [Fact]
    public void DroppingPresentationSnapshotsDoesNotChangeStateOrLoseEvents()
    {
        var withEverySnapshot = CreateScenario();
        var snapshotCount = 0;
        new SimulationRunner(withEverySnapshot).RunUntil(
            FinalTick,
            SimulationSpeed.Normal,
            snapshotConsumer: _ => snapshotCount++);

        var withoutSnapshots = CreateScenario();
        new SimulationRunner(withoutSnapshots).RunUntil(
            FinalTick,
            SimulationSpeed.Unthrottled,
            unthrottledTickBudget: 97);

        Assert.Equal(FinalTick.Value, snapshotCount);
        Assert.Equal(withEverySnapshot.ComputeStateHash(), withoutSnapshots.ComputeStateHash());
        Assert.Equal(withEverySnapshot.DrainEvents(), withoutSnapshots.DrainEvents());
    }

    [Fact]
    public void SaveLoadPreservesFutureOutcomeAndUndeliveredEvents()
    {
        var uninterrupted = CreateScenario();
        var runner = new SimulationRunner(uninterrupted);
        runner.RunUntil(new SimulationTick(173), SimulationSpeed.Octuple);

        var savedHash = uninterrupted.ComputeStateHash();
        var restored = SimulationEngine.Load(uninterrupted.Save(), SimulationDefinitions.Foundation);

        Assert.Equal(savedHash, restored.ComputeStateHash());

        runner.RunUntil(FinalTick, SimulationSpeed.Normal);
        new SimulationRunner(restored).RunUntil(
            FinalTick,
            SimulationSpeed.Unthrottled,
            unthrottledTickBudget: 61);

        Assert.Equal(uninterrupted.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(uninterrupted.DrainEvents(), restored.DrainEvents());
    }

    [Fact]
    public void SavePinsMapGeneratorVersionAndRejectsUnsupportedVersion()
    {
        var engine = CreateScenario();
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");

        Assert.Equal(
            SwampMapGenerator.CurrentVersion,
            save["mapGeneratorVersion"]?.GetValue<int>());

        save["mapGeneratorVersion"] = SwampMapGenerator.CurrentVersion + 1;

        var exception = Assert.Throws<InvalidDataException>(() =>
            SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation));
        Assert.Contains("map generator version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CommandsOnSameTickExecuteInSequenceOrder()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(123),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 2,
            initialFoodStock: 0);

        engine.QueueCommand(SimulationCommand.Forage(
            new SimulationTick(1),
            sequence: 20,
            new EntityId(2)));
        engine.QueueCommand(SimulationCommand.Forage(
            new SimulationTick(1),
            sequence: 10,
            new EntityId(1)));

        engine.AdvanceTicks(1);
        var gatheredEvents = engine.DrainEvents()
            .Where(simulationEvent => simulationEvent.Kind == SimulationEventKind.FoodGathered)
            .ToArray();

        Assert.Equal(new EntityId(1), gatheredEvents[0].Subject);
        Assert.Equal(new EntityId(2), gatheredEvents[1].Subject);
    }

    [Fact]
    public void CommandsMustTargetFutureTicksAndExistingActors()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(123),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            engine.QueueCommand(SimulationCommand.Forage(
                SimulationTick.Zero,
                sequence: 1,
                new EntityId(1))));

        Assert.Throws<ArgumentException>(() =>
            engine.QueueCommand(SimulationCommand.Forage(
                new SimulationTick(1),
                sequence: 2,
                new EntityId(999))));
    }

    [Fact]
    public void RandomSamplesAreStableAndDomainSeparated()
    {
        var seed = new WorldSeed(123);
        var actor = new EntityId(7);
        var tick = new SimulationTick(11);

        var first = DeterministicRandom.Sample(seed, RandomDomain.Foraging, actor, tick, sampleKey: 2);
        var repeated = DeterministicRandom.Sample(seed, RandomDomain.Foraging, actor, tick, sampleKey: 2);
        var combat = DeterministicRandom.Sample(seed, RandomDomain.Combat, actor, tick, sampleKey: 2);

        Assert.Equal(first, repeated);
        Assert.NotEqual(first, combat);
    }

    private static ScenarioResult RunScenario(SimulationSpeed speed)
    {
        var engine = CreateScenario();
        new SimulationRunner(engine).RunUntil(
            FinalTick,
            speed,
            unthrottledTickBudget: 73);

        return new ScenarioResult(engine.CreateSnapshot(), engine.DrainEvents().ToArray());
    }

    private static SimulationEngine CreateScenario()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0x474F424C494EUL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 4,
            initialFoodStock: 8);

        ulong sequence = 1;
        for (var tick = 20; tick <= FinalTick.Value; tick += 20)
        {
            for (ulong actor = 1; actor <= 4; actor++)
            {
                engine.QueueCommand(SimulationCommand.Forage(
                    new SimulationTick(tick + (long)actor),
                    sequence++,
                    new EntityId(actor)));
            }
        }

        return engine;
    }

    private sealed record ScenarioResult(
        SimulationSnapshot Snapshot,
        SimulationEvent[] Events);
}
