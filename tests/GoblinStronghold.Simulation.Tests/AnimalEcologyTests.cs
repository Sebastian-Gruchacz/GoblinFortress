using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class AnimalEcologyTests
{
    [Fact]
    public void InitialAnimalsMoveDeterministicallyAndSurviveSaveLoad()
    {
        var first = SimulationEngine.Create(
            new WorldSeed(0xA11A1UL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 4,
            initialFoodStock: 12);
        var second = SimulationEngine.Create(
            new WorldSeed(0xA11A1UL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 4,
            initialFoodStock: 12);
        var initial = first.CreateSnapshot().Animals;

        Assert.Contains(initial, animal => animal.Kind == AnimalKind.MarshHare);
        Assert.Contains(initial, animal => animal.Kind == AnimalKind.SwampBoar);
        var hare = initial.First(animal => animal.Kind == AnimalKind.MarshHare);
        var boar = initial.First(animal => animal.Kind == AnimalKind.SwampBoar);
        Assert.True(hare.MaximumHealth < boar.MaximumHealth);
        Assert.True(hare.MaximumFatigue < boar.MaximumFatigue);

        first.AdvanceTicks(300);
        second.AdvanceTicks(300);

        Assert.Equal(first.ComputeStateHash(), second.ComputeStateHash());
        Assert.NotEqual(initial, first.CreateSnapshot().Animals);
        var restored = SimulationEngine.Load(first.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(first.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(first.CreateSnapshot().Animals, restored.CreateSnapshot().Animals);
    }

    [Fact]
    public void SwampBoarCanInjureAnIsolatedGoblin()
    {
        var definitions = SimulationDefinitions.Foundation;
        var engine = SimulationEngine.Create(
            new WorldSeed(0xA11A1UL),
            definitions,
            initialGoblinCount: 1,
            initialFoodStock: 4);
        var boar = engine.CreateSnapshot().Animals.First(animal =>
            animal.Kind == AnimalKind.SwampBoar);
        var actorPosition = boar.Position;
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var actor = save["actors"]!.AsArray()[0]!.AsObject();
        actor["x"] = actorPosition.X;
        actor["y"] = actorPosition.Y;
        actor["z"] = actorPosition.Z;
        engine = SimulationEngine.Load(save.ToJsonString(), definitions);
        var healthBefore = engine.CreateSnapshot().Actors[0].Health;

        engine.AdvanceTicks(10);

        Assert.True(engine.CreateSnapshot().Actors[0].Health < healthBefore);
        Assert.True(engine.CreateSnapshot().Actors[0].BleedingTicksRemaining > 0);
        Assert.Contains(engine.DrainEvents(), simulationEvent =>
            simulationEvent.Kind == SimulationEventKind.AnimalHitGoblin);
    }

    [Fact]
    public void DesignatedHareIsPursuedAndProducesPhysicalRawMeat()
    {
        var seed = new WorldSeed(0xA11A1UL);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            SwampMapGenerator.Generate(seed, width: 64, height: 64),
            initialGoblinCount: 4,
            initialFoodStock: 8,
            debugSettings: new SimulationDebugSettings(RevealFogFromNonPlayerUnits: true));
        engine.AdvanceTicks(1);
        var hunter = engine.CreateSnapshot().Actors.First();
        var hare = engine.CreateSnapshot().Animals.First(animal =>
            animal.Kind == AnimalKind.MarshHare &&
            engine.Navigation.FindPath(hunter.Position, animal.Position) is not null);
        engine.QueueCommand(SimulationCommand.DesignateAnimalHunting(
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            hare.Position,
            hare.Position));

        for (var index = 0; index < 5_000 &&
             engine.CreateSnapshot().Animals.Any(animal => animal.Id == hare.Id); index++)
        {
            engine.AdvanceTicks(1);
        }

        var snapshot = engine.CreateSnapshot();
        Assert.DoesNotContain(snapshot.Animals, animal => animal.Id == hare.Id);
        Assert.Contains(snapshot.ItemStacks, stack =>
            stack.Resource == ResourceKind.Food &&
            stack.FoodKind == FoodKind.RawMeat &&
            stack.Location.Kind == ItemLocationKind.Ground);
        Assert.Contains(snapshot.ItemStacks, stack =>
            stack.Resource == ResourceKind.Hide &&
            stack.Location.Kind == ItemLocationKind.Ground);
        Assert.Contains(snapshot.ItemStacks, stack =>
            stack.Resource == ResourceKind.Bone &&
            stack.Location.Kind == ItemLocationKind.Ground);
        Assert.DoesNotContain(snapshot.WorkDesignations, designation =>
            designation.Kind == WorkDesignationKind.HuntAnimal &&
            designation.TargetEntityId.Value == hare.Id);
        Assert.Contains(engine.DrainEvents(), simulationEvent =>
            simulationEvent.Kind == SimulationEventKind.AnimalHunted);
    }

    [Fact]
    public void HareMustRecoverAfterAShortFlight()
    {
        var definitions = SimulationDefinitions.Foundation;
        var engine = SimulationEngine.Create(
            new WorldSeed(0xA11A1UL),
            definitions,
            initialGoblinCount: 1,
            initialFoodStock: 20);
        var hare = engine.CreateSnapshot().Animals.First(animal =>
            animal.Kind == AnimalKind.MarshHare);
        var approach = engine.Map.GetCardinalNeighbors(hare.Position)
            .First(position => engine.World.IsSurfaceTraversable(position));
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        save["actors"]![0]!["x"] = approach.X;
        save["actors"]![0]!["y"] = approach.Y;
        save["actors"]![0]!["z"] = approach.Z;
        engine = SimulationEngine.Load(save.ToJsonString(), definitions);

        var reachedLimit = false;
        var recoveredFromLimit = false;
        for (var update = 0; update < 20; update++)
        {
            engine.AdvanceTicks(SimulationEngine.AnimalUpdateIntervalTicks);
            var current = engine.CreateSnapshot().Animals.Single(animal => animal.Id == hare.Id);
            if (current.Fatigue == current.MaximumFatigue)
            {
                reachedLimit = true;
            }
            else if (reachedLimit && current.Activity == AnimalActivity.Resting &&
                     current.Fatigue < current.MaximumFatigue)
            {
                recoveredFromLimit = true;
                break;
            }
        }

        Assert.True(reachedLimit);
        Assert.True(recoveredFromLimit);
    }

    [Fact]
    public void RangedHunterConsumesAStoneAndLeavesPersistentBlood()
    {
        var seed = new WorldSeed(0xA11A1UL);
        var map = SwampMapGenerator.Generate(seed, width: 64, height: 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 4,
            initialFoodStock: 20,
            debugSettings: new SimulationDebugSettings(RevealFogFromNonPlayerUnits: true));
        engine.AdvanceTicks(1);
        var initialActor = engine.CreateSnapshot().Actors.First();
        var hare = engine.CreateSnapshot().Animals.First(animal =>
            animal.Kind == AnimalKind.MarshHare &&
            engine.Navigation.FindPath(initialActor.Position, animal.Position) is not null);
        var firingPosition = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width)
                .Select(x => map.GetTerrainSurfacePosition(new GridPosition(x, y))))
            .Where(engine.World.IsTerrainTraversable)
            .Where(position => Math.Abs(position.X - hare.Position.X) +
                Math.Abs(position.Y - hare.Position.Y) is >= 2 and <= 3)
            .Where(position => engine.Navigation.FindPath(position, hare.Position) is not null)
            .OrderBy(position => Math.Abs(position.X - hare.Position.X) +
                Math.Abs(position.Y - hare.Position.Y))
            .First();
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        foreach (var actorNode in save["actors"]!.AsArray())
        {
            var actor = actorNode!.AsObject();
            actor["x"] = firingPosition.X;
            actor["y"] = firingPosition.Y;
            actor["z"] = firingPosition.Z;
            actor["equipment"] = actor["equipment"]!.GetValue<int>() |
                (int)PersonalEquipment.PrimitiveSling;
            actor["personalStoneAmmo"] = 1;
            actor["jobKind"] = (int)ActorJobKind.None;
            actor["jobPhase"] = (int)ActorJobPhase.None;
            actor["jobStage"] = (int)ActorJobStage.None;
            actor["jobTargetX"] = 0;
            actor["jobTargetY"] = 0;
            actor["jobTargetZ"] = 0;
            actor["remainingWorkTicks"] = 0;
            actor["sourceStackId"] = 0;
            actor["destinationZoneId"] = 0;
            actor["reservedQuantity"] = 0;
            actor["remainingRoute"] = new JsonArray();
            actor["suspendedJobKind"] = (int)ActorJobKind.None;
            actor["suspendedTargetX"] = 0;
            actor["suspendedTargetY"] = 0;
            actor["suspendedTargetZ"] = 0;
        }
        var savedHare = save["animals"]!.AsArray().Single(animal =>
            animal!["id"]!.GetValue<ulong>() == hare.Id)!.AsObject();
        savedHare["fatigue"] = hare.MaximumFatigue;
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        engine.QueueCommand(SimulationCommand.DesignateAnimalHunting(
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            hare.Position,
            hare.Position));

        for (var index = 0; index < 5_000 &&
             engine.CreateSnapshot().Actors.Sum(actor => actor.PersonalStoneAmmo) == 4; index++)
        {
            engine.AdvanceTicks(1);
        }

        var snapshot = engine.CreateSnapshot();
        Assert.True(snapshot.Actors.Sum(actor => actor.PersonalStoneAmmo) < 4);
        Assert.Contains(snapshot.BloodStains, stain =>
            stain.Position == hare.Position && stain.Volume > 0);

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(snapshot.BloodStains, restored.CreateSnapshot().BloodStains);
    }
}
