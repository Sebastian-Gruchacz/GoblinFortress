using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class VerticalRampTests
{
    [Fact]
    public void RampCanBeCarvedBelowTheFormerLevelMinusTwoBoundary()
    {
        var seed = new WorldSeed(0x4445455052414D50UL);
        var map = SwampMapGenerator.Generate(seed, width: 48, height: 48);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 30);
        Assert.Equal(-2, map.DeepestCaveLevel);
        var upper =
            (from y in Enumerable.Range(0, map.Height)
             from x in Enumerable.Range(0, map.Width)
             let candidate = new GridPosition(x, y, -2)
             where engine.World.CanCarveRampDown(candidate)
             select candidate).First();
        var lower = upper with { Z = -3 };

        Assert.True(engine.World.TryCarveVerticalRamp(
            upper,
            carveDown: true,
            SimulationTick.Zero,
            out _,
            out _));

        Assert.Contains(lower, engine.World.ExcavatedCaveCells);
        Assert.NotNull(engine.Navigation.FindPath(upper, lower));
        Assert.Equal(-3, map.DeepestCaveLevel);

        var secondUpper = new[]
            {
                lower with { X = lower.X - 1 },
                lower with { X = lower.X + 1 },
                lower with { Y = lower.Y - 1 },
                lower with { Y = lower.Y + 1 },
            }
            .First(engine.World.CanExcavateRock);
        Assert.True(engine.World.TryExcavateRock(
            secondUpper,
            SimulationTick.Zero,
            out _,
            out _,
            out _));
        Assert.True(engine.World.CanCarveRampDown(secondUpper));
        Assert.True(engine.World.TryCarveVerticalRamp(
            secondUpper,
            carveDown: true,
            SimulationTick.Zero,
            out _,
            out _));
        Assert.Equal(-4, map.DeepestCaveLevel);

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(-4, restored.Map.DeepestCaveLevel);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void RampDownCanBeDesignatedAtLevelMinusTenAboveANaturalCave()
    {
        var seed = new WorldSeed(0x4445455043415645UL);
        var map = SwampMapGenerator.Generate(seed, width: 48, height: 48);
        while (map.DeepestCaveLevel > -10)
        {
            map.MaterializeCaveLevel(map.DeepestCaveLevel - 1);
        }
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 30);
        var target =
            (from y in Enumerable.Range(0, map.Height)
             from x in Enumerable.Range(0, map.Width)
             let upper = new GridPosition(x, y, -10)
             let lower = upper with { Z = -11 }
             where map.GetCaveCell(upper).IsOpen &&
                   map.GetCaveCell(upper).Fluid == CellFluidKind.None &&
                   map.GetNextCaveLevelCell(lower).IsOpen &&
                   map.GetNextCaveLevelCell(lower).Fluid == CellFluidKind.None
             select upper).First();
        engine.Visibility.Reveal([target], radius: 1);

        Assert.True(engine.World.CanCarveRampDown(target));
        Assert.Equal(
            new[] { target },
            engine.QueryWorkDesignationTargets(
                WorkDesignationKind.CarveRampDown,
                target,
                target));

        engine.ApplyCommandImmediately(SimulationCommand.DesignateRampDown(
            engine.CurrentTick,
            sequence: 1,
            target));

        var designation = Assert.Single(engine.CreateSnapshot().WorkDesignations);
        Assert.Equal(WorkDesignationKind.CarveRampDown, designation.Kind);
        Assert.Equal(target, designation.Target);
        Assert.True(engine.World.TryCarveVerticalRamp(
            target,
            carveDown: true,
            engine.CurrentTick,
            out _,
            out _));
        Assert.Equal(-11, map.DeepestCaveLevel);
        Assert.Contains(engine.World.ExcavatedVerticalPassages, passage =>
            passage.Upper == target && passage.Lower == target with { Z = -11 });
    }

    [Fact]
    public void GoblinCarvesRampDownAndTheNewRouteSurvivesSaveLoad()
    {
        var seed = new WorldSeed(0x52414D50444F574EUL);
        var map = SwampMapGenerator.Generate(seed, width: 48, height: 48);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 30);
        var actor = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.True(actor.Equipment.HasFlag(PersonalEquipment.PrimitivePickaxe));
        var target =
            (from y in Enumerable.Range(0, map.Height)
             from x in Enumerable.Range(0, map.Width)
             let candidate = new GridPosition(x, y, -1)
             where engine.World.CanCarveRampDown(candidate)
             let route = engine.Navigation.FindPath(actor.Position, candidate)
             where route is not null
             orderby route.Count
             select candidate).First();

        engine.QueueCommand(SimulationCommand.Move(
            new SimulationTick(1), sequence: 1, actor.Id, target));
        for (var tick = 0; tick < 12_000 &&
             Assert.Single(engine.CreateSnapshot().Actors).Position != target; tick++)
        {
            engine.AdvanceTicks(1);
        }
        Assert.Equal(target, Assert.Single(engine.CreateSnapshot().Actors).Position);

        engine.QueueCommand(SimulationCommand.DesignateRampDown(
            engine.CurrentTick.Next(), sequence: 2, target));
        var lower = target with { Z = target.Z - 1 };
        for (var tick = 0; tick < 12_000 &&
             !engine.World.ExcavatedVerticalPassages.Any(passage =>
                 passage.Upper == target && passage.Lower == lower); tick++)
        {
            engine.AdvanceTicks(1);
        }

        Assert.Contains(engine.World.ExcavatedVerticalPassages, passage =>
            passage.Upper == target && passage.Lower == lower &&
            passage.Kind == VerticalPassageKind.ExcavatedRamp);
        Assert.Contains(lower, engine.World.ExcavatedCaveCells);
        Assert.NotNull(engine.Navigation.FindPath(target, lower));
        Assert.Contains(engine.CreateSnapshot().ItemStacks, stack =>
            stack.Resource == ResourceKind.Stone &&
            stack.Location == ItemLocation.OnGround(target));

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.NotNull(restored.Navigation.FindPath(target, lower));
        Assert.Contains(restored.World.ExcavatedVerticalPassages, passage =>
            passage.Upper == target && passage.Lower == lower);
    }

    [Fact]
    public void GoblinCanCarveRampUpFromADeeperCaveFloor()
    {
        var seed = new WorldSeed(0x52414D505550UL);
        var map = SwampMapGenerator.Generate(seed, width: 48, height: 48);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 30);
        var actor = Assert.Single(engine.CreateSnapshot().Actors);
        var target =
            (from level in Enumerable.Range(2, Math.Max(0, map.CaveLevelCount - 1))
             from y in Enumerable.Range(0, map.Height)
             from x in Enumerable.Range(0, map.Width)
             let candidate = new GridPosition(x, y, -level)
             where engine.World.CanCarveRampUp(candidate)
             let route = engine.Navigation.FindPath(actor.Position, candidate)
             where route is not null
             orderby route.Count
             select candidate).First();

        engine.QueueCommand(SimulationCommand.Move(
            new SimulationTick(1), sequence: 1, actor.Id, target));
        for (var tick = 0; tick < 16_000 &&
             Assert.Single(engine.CreateSnapshot().Actors).Position != target; tick++)
        {
            engine.AdvanceTicks(1);
        }
        Assert.Equal(target, Assert.Single(engine.CreateSnapshot().Actors).Position);

        engine.QueueCommand(SimulationCommand.DesignateRampUp(
            engine.CurrentTick.Next(), sequence: 2, target));
        var upper = target with { Z = target.Z + 1 };
        for (var tick = 0; tick < 12_000 &&
             !engine.World.ExcavatedVerticalPassages.Any(passage =>
                 passage.Upper == upper && passage.Lower == target); tick++)
        {
            engine.AdvanceTicks(1);
        }

        Assert.Contains(engine.World.ExcavatedVerticalPassages, passage =>
            passage.Upper == upper && passage.Lower == target);
        Assert.Contains(upper, engine.World.ExcavatedCaveCells);
        Assert.NotNull(engine.Navigation.FindPath(target, upper));
    }
}
