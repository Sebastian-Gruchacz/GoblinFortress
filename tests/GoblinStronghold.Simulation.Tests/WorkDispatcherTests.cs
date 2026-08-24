using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class WorkDispatcherTests
{
    [Fact]
    public void SatiatedGoblinsDoNotForageWithoutPlayerDesignation()
    {
        var engine = CreateEngine(goblinCount: 4);

        engine.AdvanceTicks(1);

        Assert.DoesNotContain(
            engine.CreateSnapshot().Actors,
            actor => actor.Job.Kind == ActorJobKind.Forage);
    }

    [Fact]
    public void FoodAreaCreatesDispatcherWorkAndCompletesDeterministically()
    {
        var engine = CreateEngine(goblinCount: 2);
        var spawn = engine.Map.GoblinSpawn;
        engine.QueueCommand(SimulationCommand.DesignateWork(
            new SimulationTick(1),
            sequence: 1,
            spawn,
            spawn,
            ResourceKind.Food));

        engine.AdvanceTicks(1);

        var designation = Assert.Single(engine.CreateSnapshot().WorkDesignations);
        Assert.Equal(WorkDesignationKind.GatherFood, designation.Kind);
        Assert.Equal(spawn, designation.Target);
        Assert.Equal(EntityId.None, designation.TargetEntityId);
        Assert.Contains(engine.CreateSnapshot().Actors, actor =>
            actor.Job.Kind == ActorJobKind.Forage && actor.Job.Target == spawn);

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        engine.AdvanceTicks(80);
        restored.AdvanceTicks(80);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(engine.DrainEvents(), restored.DrainEvents());
    }

    [Fact]
    public void SelectionStoresOnlyConcreteTargetsAndDifferentJobsMayOverlap()
    {
        var engine = CreateEngine(goblinCount: 2, initialWood: 8);
        var spawn = engine.Map.GoblinSpawn;
        engine.QueueCommand(SimulationCommand.DesignateWork(
            new SimulationTick(1), sequence: 1, spawn, spawn, ResourceKind.Food));
        engine.QueueCommand(SimulationCommand.DesignateWork(
            new SimulationTick(1), sequence: 2, spawn, spawn, ResourceKind.Wood));

        engine.AdvanceTicks(1);

        var targets = engine.CreateSnapshot().WorkDesignations;
        Assert.Contains(targets, item =>
            item.Kind == WorkDesignationKind.GatherFood &&
            item.Target == spawn &&
            item.TargetEntityId == EntityId.None);
        Assert.Contains(targets, item =>
            item.Kind == WorkDesignationKind.GatherBrushwood &&
            item.Target == spawn &&
            item.TargetEntityId != EntityId.None);
        Assert.Equal(2, targets.Select(item => item.Kind).Distinct().Count());

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void EmptyCellsInsideSelectionDoNotRemainDesignated()
    {
        var engine = CreateEngine(goblinCount: 1);
        var spawn = engine.Map.GoblinSpawn;
        var end = new GridPosition(
            Math.Min(engine.Map.Width - 1, spawn.X + 5),
            Math.Min(engine.Map.Height - 1, spawn.Y + 5));
        engine.QueueCommand(SimulationCommand.DesignateWork(
            new SimulationTick(1), sequence: 1, spawn, end, ResourceKind.Food));

        engine.AdvanceTicks(1);

        var plants = engine.World.CreatePlantSnapshot()
            .Where(item => item.Biomass > 0)
            .Select(item => item.Position)
            .ToHashSet();
        Assert.All(engine.CreateSnapshot().WorkDesignations,
            designation => Assert.Contains(designation.Target, plants));
        Assert.True(engine.CreateSnapshot().WorkDesignations.Count < 36);
    }

    [Fact]
    public void PreviouslyExploredResourceMayBeDesignatedOutsideCurrentVision()
    {
        var engine = CreateEngine(goblinCount: 1);
        var target = engine.World.CreatePlantSnapshot()
            .First(plant => plant.Biomass > 0 &&
                engine.Visibility.Get(plant.Position) == CellVisibility.Unknown);
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var visibilityIndex = (target.Position.Y * engine.Map.Width) + target.Position.X;
        save["visibility"]!.AsArray()[visibilityIndex] = (int)CellVisibility.Explored;
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        engine.QueueCommand(SimulationCommand.DesignateWork(
            engine.CurrentTick.Next(),
            sequence: 1,
            target.Position,
            target.Position,
            ResourceKind.Food));

        engine.AdvanceTicks(1);

        Assert.Contains(engine.CreateSnapshot().WorkDesignations, designation =>
            designation.Kind == WorkDesignationKind.GatherFood &&
            designation.Target == target.Position);
    }

    [Fact]
    public void UprootAreaRemovesBerryBushPermanentlyAndSurvivesSaveLoad()
    {
        var engine = CreateEngine(goblinCount: 1);
        var spawn = engine.Map.GoblinSpawn;
        Assert.Equal(PlantKind.BerryBush, engine.World.GetPlantPatch(spawn)!.Value.Kind);
        engine.QueueCommand(SimulationCommand.DesignateWork(
            new SimulationTick(1),
            sequence: 1,
            spawn,
            spawn,
            ResourceKind.Vegetation));

        engine.AdvanceTicks(1);

        var designation = Assert.Single(engine.CreateSnapshot().WorkDesignations);
        Assert.Equal(WorkDesignationKind.UprootBerryBush, designation.Kind);
        Assert.Equal(ActorJobKind.ClearVegetation, Assert.Single(engine.CreateSnapshot().Actors).Job.Kind);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);

        engine.AdvanceTicks(80);
        restored.AdvanceTicks(80);

        Assert.Null(engine.World.GetPlantPatch(spawn));
        Assert.DoesNotContain(engine.CreateSnapshot().WorkDesignations,
            item => item.Kind == WorkDesignationKind.UprootBerryBush);
        Assert.Contains(engine.DrainWorldChanges(),
            item => item.Kind == WorldChangeKind.VegetationRemoved && item.Position == spawn);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        engine.AdvanceTicks(SimulationDefinitions.Foundation.PlantGrowthIntervalTicks);
        Assert.Null(engine.World.GetPlantPatch(spawn));
    }

    [Fact]
    public void HungerMayCreateEmergencyForagingOutsideWorkAreas()
    {
        var seed = new WorldSeed(0x48554E475259UL);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialHunger: SimulationDefinitions.Foundation.FoodSeekThreshold);

        engine.AdvanceTicks(1);

        Assert.Equal(ActorJobKind.Forage, Assert.Single(engine.CreateSnapshot().Actors).Job.Kind);
        Assert.Empty(engine.CreateSnapshot().WorkDesignations);
    }

    [Fact]
    public void BrushwoodAreaFeedsWoodStorageEvenWhenPullIsDisabled()
    {
        var engine = CreateEngine(goblinCount: 2, initialWood: 8);
        var spawn = engine.Map.GoblinSpawn;
        var zonePosition = engine.Map.GetCardinalNeighbors(spawn)
            .First(engine.World.IsSurfaceTraversable);
        engine.QueueCommand(SimulationCommand.BuildWoodStorage(
            new SimulationTick(1),
            sequence: 1,
            zonePosition));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
        Assert.Equal(0, Assert.Single(engine.CreateSnapshot().StorageZones).DesiredQuantity);

        engine.QueueCommand(SimulationCommand.DesignateWork(
            engine.CurrentTick.Next(),
            sequence: 2,
            spawn,
            spawn,
            ResourceKind.Wood));
        engine.AdvanceTicks(180);

        var zone = Assert.Single(engine.CreateSnapshot().StorageZones);
        Assert.True(zone.StoredQuantity > 0);
        Assert.DoesNotContain(engine.CreateSnapshot().WorkDesignations, designation =>
            designation.Kind == WorkDesignationKind.GatherBrushwood);
    }

    [Fact]
    public void StoragePullStopsAtConfiguredTargetWithoutDesignation()
    {
        var engine = CreateEngine(goblinCount: 2, initialWood: 10);
        var spawn = engine.Map.GoblinSpawn;
        var zonePosition = engine.Map.GetCardinalNeighbors(spawn)
            .First(engine.World.IsSurfaceTraversable);
        engine.QueueCommand(SimulationCommand.BuildWoodStorage(
            new SimulationTick(1),
            sequence: 1,
            zonePosition));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
        var zone = Assert.Single(engine.CreateSnapshot().StorageZones);
        engine.QueueCommand(SimulationCommand.ConfigureStoragePull(
            engine.CurrentTick.Next(),
            sequence: 2,
            zone.Id,
            desiredQuantity: 3));

        engine.AdvanceTicks(200);

        zone = Assert.Single(engine.CreateSnapshot().StorageZones);
        Assert.Equal(3, zone.StoredQuantity);
        Assert.Equal(3, zone.DesiredQuantity);
    }

    [Fact]
    public void ClearAreaRemovesOverlappingWorkDesignation()
    {
        var engine = CreateEngine(goblinCount: 1);
        var spawn = engine.Map.GoblinSpawn;
        engine.QueueCommand(SimulationCommand.DesignateWork(
            new SimulationTick(1),
            sequence: 1,
            spawn,
            spawn,
            ResourceKind.Food));
        engine.AdvanceTicks(1);
        Assert.Single(engine.CreateSnapshot().WorkDesignations);
        engine.QueueCommand(SimulationCommand.ClearWorkDesignations(
            new SimulationTick(2),
            sequence: 2,
            spawn,
            spawn));

        engine.AdvanceTicks(1);

        Assert.Empty(engine.CreateSnapshot().WorkDesignations);
    }

    private static SimulationEngine CreateEngine(int goblinCount, int initialWood = 0)
    {
        var seed = new WorldSeed(0x574F524BUL);
        return SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            initialGoblinCount: goblinCount,
            initialFoodStock: 0,
            initialWoodStock: initialWood);
    }
}
