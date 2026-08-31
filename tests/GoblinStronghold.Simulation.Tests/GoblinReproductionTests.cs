using GoblinStronghold.Simulation;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class GoblinReproductionTests
{
    [Fact]
    public void FoodAndShelterSurplusCreatesPersistentBudAndWeakensParent()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0xB00DUL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 4,
            initialFoodStock: 40);
        var before = engine.CreateSnapshot();

        Assert.Empty(before.GoblinBuds);
        Assert.Equal(GoblinReproductionReadinessKind.Ready,
            before.TribeNeeds.Reproduction.Kind);
        Assert.Equal(24, before.TribeNeeds.Reproduction.RequiredFood);
        Assert.Equal(40, before.TribeNeeds.FoodUnits);
        Assert.Equal(8, before.TribeNeeds.ExpectedDailyFoodUnits);
        Assert.True(before.TribeNeeds.ShelterCapacity >= before.Actors.Count);
        Assert.True(before.TribeNeeds.SuitableMoistSites > 0);

        engine.AdvanceTicks(1);

        var after = engine.CreateSnapshot();
        var bud = Assert.Single(after.GoblinBuds);
        var parentBefore = Assert.Single(before.Actors.Where(actor => actor.Id == bud.ParentId));
        var parentAfter = Assert.Single(after.Actors.Where(actor => actor.Id == bud.ParentId));
        Assert.Equal(36, after.FoodStock);
        Assert.True(parentAfter.Health < parentBefore.Health);
        Assert.True(parentAfter.Hunger > parentBefore.Hunger);
        Assert.True(parentAfter.Fatigue > parentBefore.Fatigue);
        Assert.Equal(GoblinReproductionReadinessKind.BudBeingTended,
            after.TribeNeeds.Reproduction.Kind);

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(after.GoblinBuds, restored.CreateSnapshot().GoblinBuds);
        Assert.Equal(after.TribeNeeds, restored.CreateSnapshot().TribeNeeds);
    }

    [Fact]
    public void CaredForBudBecomesOneNewGoblinAndFurtherGrowthNeedsAnotherSurplus()
    {
        var definitions = SimulationDefinitions.Foundation;
        var engine = SimulationEngine.Create(
            new WorldSeed(0xB00DUL),
            definitions,
            initialGoblinCount: 4,
            initialFoodStock: 26);

        engine.AdvanceTicks(1);
        var bud = Assert.Single(engine.CreateSnapshot().GoblinBuds);
        var parent = Assert.Single(engine.CreateSnapshot().Actors.Where(actor => actor.Id == bud.ParentId));

        engine.AdvanceTicks(999);
        var completed = engine.CreateSnapshot();

        Assert.Equal(5, completed.Actors.Count);
        Assert.Empty(completed.GoblinBuds);
        Assert.Equal(
            GoblinReproductionReadinessKind.JuvenileCapacityReached,
            completed.TribeNeeds.Reproduction.Kind);
        var newborn = completed.Actors.MaxBy(actor => actor.Id.Value);
        Assert.Equal(0, newborn.Experience.Foraging);
        Assert.Equal(0, newborn.Experience.Hauling);
        Assert.Equal(0, newborn.Experience.Building);
        Assert.False(newborn.Equipment.HasFlag(PersonalEquipment.WoodenAxe));
        Assert.False(newborn.Equipment.HasFlag(PersonalEquipment.PrimitivePickaxe));
        Assert.True(newborn.IsJuvenile);
        Assert.True((newborn.KnownSkills & parent.KnownSkills) != GoblinSkill.None);
        Assert.True((newborn.KnownTraits & parent.KnownTraits) != GoblinTrait.None);
        var restoredJuvenile = SimulationEngine.Load(engine.Save(), definitions);
        Assert.Equal(engine.ComputeStateHash(), restoredJuvenile.ComputeStateHash());
        Assert.True(restoredJuvenile.CreateSnapshot().Actors
            .Single(actor => actor.Id == newborn.Id).IsJuvenile);

        var season = SimulationCalendar.At(engine.CurrentTick, definitions.Clock).Season;
        var seasonTicks = definitions.Clock.Climate.GetSeason(season).TotalTicks;
        engine.AdvanceTicks(checked((int)seasonTicks));
        Assert.False(engine.CreateSnapshot().Actors.Single(actor => actor.Id == newborn.Id).IsJuvenile);

        engine.AdvanceTicks(500);
        Assert.Equal(5, engine.CreateSnapshot().Actors.Count);
        Assert.Empty(engine.CreateSnapshot().GoblinBuds);
    }

    [Fact]
    public void PopulationDoesNotGrowWithoutFoodSurplus()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0xB00DUL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 4,
            initialFoodStock: 0);

        engine.AdvanceTicks(300);
        Assert.Equal(4, engine.CreateSnapshot().Actors.Count);
        Assert.Empty(engine.CreateSnapshot().GoblinBuds);

        engine.AdvanceTicks(300);
        Assert.Equal(4, engine.CreateSnapshot().Actors.Count);
        Assert.Empty(engine.CreateSnapshot().GoblinBuds);
        Assert.Equal(
            GoblinReproductionReadinessKind.InsufficientFood,
            engine.CreateSnapshot().TribeNeeds.Reproduction.Kind);
    }

    [Fact]
    public void ReadinessExplainsWhenNoGoblinIsStrongEnoughToBud()
    {
        var definitions = SimulationDefinitions.Foundation;
        var engine = SimulationEngine.Create(
            new WorldSeed(0xB00DUL),
            definitions,
            initialGoblinCount: 4,
            initialFoodStock: 40,
            initialHealth: definitions.MaximumHealth / 2);
        engine.AdvanceTicks(1);

        var readiness = engine.InspectReproductionReadiness();
        Assert.Equal(GoblinReproductionReadinessKind.NoEligibleParent, readiness.Kind);
        Assert.Equal(0, readiness.EligibleParents);
        Assert.True(readiness.SuitableMoistSites > 0);
        Assert.Equal(
            definitions.Reproduction.FoodCost +
            (engine.CreateSnapshot().Actors.Count + 1) * definitions.PersonalFoodCapacity *
            definitions.Reproduction.FoodReserveDays,
            readiness.RequiredFood);
    }
}
