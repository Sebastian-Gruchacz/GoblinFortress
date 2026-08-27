using System.Text.Json.Nodes;
using GoblinStronghold.Simulation;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class GoblinAgingTests
{
    [Fact]
    public void FiveYearOldGoblinDeclinesToHeavyFailureWithinTwoSeasons()
    {
        var definitions = SimulationDefinitions.Foundation;
        var engine = SimulationEngine.Create(
            new WorldSeed(0xA6EUL),
            definitions,
            initialGoblinCount: 1,
            initialFoodStock: 20);
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var actor = save["actors"]!.AsArray()[0]!.AsObject();
        var healthyTicks = checked(
            definitions.Clock.Climate.TicksPerYear * definitions.Aging.HealthyYears);
        actor["ageOffsetTicks"] = healthyTicks - 1;
        actor["birthTick"] = null;
        actor["maturesAtTick"] = null;
        actor["health"] = definitions.MaximumHealth;
        engine = SimulationEngine.Load(save.ToJsonString(), definitions);

        var healthy = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.False(healthy.IsElderly);
        Assert.Equal(definitions.MaximumHealth, healthy.EffectiveMaximumHealth);

        engine.AdvanceTicks(2);
        var elderly = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.True(elderly.IsElderly);
        Assert.True(elderly.SenescenceProgress > 0);

        var longestSeason = definitions.Clock.Climate.Seasons.Max(season => season.TotalTicks);
        var agedSave = JsonNode.Parse(engine.Save())!.AsObject();
        agedSave["currentTick"] = checked(engine.CurrentTick.Value + longestSeason *
            definitions.Aging.DeclineMaximumSeasons);
        engine = SimulationEngine.Load(agedSave.ToJsonString(), definitions);
        engine.AdvanceTicks(1);
        var failing = Assert.Single(engine.CreateSnapshot().Actors);
        var terminalHealth = definitions.MaximumHealth *
            definitions.Aging.TerminalHealthPermille / 1_000;
        Assert.Equal(1, failing.SenescenceProgress);
        Assert.Equal(terminalHealth, failing.EffectiveMaximumHealth);
        Assert.True(failing.Health <= terminalHealth);

        var restored = SimulationEngine.Load(engine.Save(), definitions);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        var restoredActor = Assert.Single(restored.CreateSnapshot().Actors);
        Assert.Equal(failing.AgeDays, restoredActor.AgeDays);
        Assert.Equal(failing.EffectiveMaximumHealth, restoredActor.EffectiveMaximumHealth);
        Assert.Equal(failing.SenescenceProgress, restoredActor.SenescenceProgress);
    }

    [Fact]
    public void FoundingAdultsStartBelowSenescenceAge()
    {
        var definitions = SimulationDefinitions.Foundation;
        var engine = SimulationEngine.Create(
            new WorldSeed(0xA6F0UL),
            definitions,
            initialGoblinCount: 8,
            initialFoodStock: 20);

        var maximumHealthyDays = definitions.Clock.Climate.DaysPerYear *
            definitions.Aging.HealthyYears;
        Assert.All(engine.CreateSnapshot().Actors, actor =>
        {
            Assert.InRange(actor.AgeDays,
                definitions.Clock.Climate.DaysPerYear,
                maximumHealthyDays - 1);
            Assert.False(actor.IsElderly);
        });
    }
}
