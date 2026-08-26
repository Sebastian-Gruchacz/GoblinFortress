using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class HealingTests
{
    [Fact]
    public void HealthyNeedsAllowSlowNaturalRecovery()
    {
        var engine = CreateEngine(initialHealth: 9_000, initialFood: 0, initialHunger: 0);
        var interval = engine.Definitions.HealthRecovery.NaturalIntervalTicks;

        engine.AdvanceTicks(interval - 1);
        Assert.Equal(9_000, Assert.Single(engine.CreateSnapshot().Actors).Health);

        engine.AdvanceTicks(1);
        Assert.Equal(9_001, Assert.Single(engine.CreateSnapshot().Actors).Health);
    }

    [Fact]
    public void SleepingAddsRecoveryOnTopOfNaturalHealing()
    {
        var engine = CreateEngine(initialHealth: 9_000, initialFood: 0, initialHunger: 0);
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        save["actors"]![0]!["fatigue"] = engine.Definitions.RestThreshold;
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);

        ActorSnapshot sleeping = default;
        for (var tick = 0; tick < 2_000; tick++)
        {
            engine.AdvanceTicks(1);
            var actor = Assert.Single(engine.CreateSnapshot().Actors);
            if (actor.Job.Kind == ActorJobKind.Rest && actor.Job.Phase == ActorJobPhase.Working)
            {
                sleeping = actor;
                break;
            }
        }

        Assert.NotEqual(EntityId.None, sleeping.Id);
        var startTick = engine.CurrentTick.Value;
        var duration = 24;
        var expectedRecovery = Enumerable.Range(1, duration).Count(offset =>
                (startTick + offset) % engine.Definitions.HealthRecovery.NaturalIntervalTicks == 0) +
            Enumerable.Range(1, duration).Count(offset =>
                (startTick + offset) %
                engine.Definitions.HealthRecovery.SleepingBonusIntervalTicks == 0);

        engine.AdvanceTicks(duration);

        var recovered = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.Equal(ActorJobKind.Rest, recovered.Job.Kind);
        Assert.Equal(sleeping.Health + expectedRecovery, recovered.Health);
    }

    [Fact]
    public void EatingMedicinalRootsRestoresHealthImmediately()
    {
        var engine = CreateEngine(initialHealth: 9_000, initialFood: 1, initialHunger: 6_500);
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        save["itemStacks"]![0]!["foodKind"] = (int)FoodKind.EdibleRoots;
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);

        for (var tick = 0; tick < engine.Definitions.EatWorkTicks + 2; tick++)
        {
            engine.AdvanceTicks(1);
            if (engine.DrainEvents().Any(item => item.Kind == SimulationEventKind.ActorAte))
            {
                Assert.Equal(
                    9_000 + engine.Definitions.HealthRecovery.MedicinalRootsHealing,
                    Assert.Single(engine.CreateSnapshot().Actors).Health);
                return;
            }
        }

        throw new Xunit.Sdk.XunitException("The goblin did not eat medicinal roots in time.");
    }

    private static SimulationEngine CreateEngine(
        int initialHealth,
        int initialFood,
        int initialHunger)
    {
        var seed = new WorldSeed(0x4845414CUL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        return SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: initialFood,
            initialHunger: initialHunger,
            initialHealth: initialHealth);
    }
}
