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
            .First(plant => plant.Biomass > 0 && plant.Kind != PlantKind.ReedBed &&
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
    public void ReedBedsRequireAndAcceptDedicatedGatherReedsDesignation()
    {
        var engine = CreateEngine(goblinCount: 1);
        var target = engine.World.CreatePlantSnapshot()
            .First(plant => plant.Kind == PlantKind.ReedBed && plant.Biomass > 0);
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
        engine.QueueCommand(SimulationCommand.DesignateWork(
            engine.CurrentTick.Next(),
            sequence: 2,
            target.Position,
            target.Position,
            ResourceKind.Reeds));
        engine.AdvanceTicks(1);

        var designation = Assert.Single(engine.CreateSnapshot().WorkDesignations);
        Assert.Equal(WorkDesignationKind.GatherReeds, designation.Kind);
        Assert.Equal(target.Position, designation.Target);
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
    public void TreeFellingAreaCreatesConcreteJobAndProducesStumpAndWood()
    {
        var engine = CreateEngine(goblinCount: 2);
        var tree = engine.World.CreateWorldObjectSnapshot()
            .Where(worldObject => worldObject.Kind == WorldObjectKind.Tree)
            .Select(worldObject => new
            {
                Tree = worldObject,
                Access = engine.Map.GetCardinalNeighbors(worldObject.Anchor)
                    .Where(engine.World.IsSurfaceTraversable)
                    .Select(position => engine.Navigation.FindSurfacePath(
                        engine.Map.GoblinSpawn,
                        position))
                    .Where(route => route is not null)
                    .OrderBy(route => route!.Count)
                    .FirstOrDefault(),
            })
            .Where(candidate => candidate.Access is not null)
            .OrderBy(candidate => candidate.Access!.Count)
            .First();
        var trunkSections = tree.Tree.Parts.Count(part =>
            part.Kind == WorldObjectPartKind.TreeTrunk);
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var visibilityIndex = (tree.Tree.Anchor.Y * engine.Map.Width) + tree.Tree.Anchor.X;
        save["visibility"]!.AsArray()[visibilityIndex] = (int)CellVisibility.Explored;
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        engine.QueueCommand(SimulationCommand.DesignateTreeFelling(
            engine.CurrentTick.Next(),
            sequence: 1,
            tree.Tree.Anchor,
            tree.Tree.Anchor));

        engine.AdvanceTicks(1);

        var designation = Assert.Single(engine.CreateSnapshot().WorkDesignations);
        Assert.Equal(WorkDesignationKind.FellTree, designation.Kind);
        Assert.Equal(tree.Tree.Anchor, designation.Target);
        var logger = Assert.Single(engine.CreateSnapshot().Actors,
            actor => actor.Job.Kind == ActorJobKind.FellTree);
        Assert.True(logger.Equipment.HasFlag(PersonalEquipment.WoodenAxe));
        Assert.NotEqual(tree.Tree.Anchor, logger.Job.Target);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);

        engine.AdvanceTicks(2_000);
        restored.AdvanceTicks(2_000);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Contains(engine.World.GetWorldObjectsAt(tree.Tree.Anchor),
            worldObject => worldObject.Kind == WorldObjectKind.DeadTreeStump);
        Assert.DoesNotContain(engine.CreateSnapshot().WorkDesignations,
            item => item.Kind == WorkDesignationKind.FellTree);
        Assert.Equal(trunkSections * 16, engine.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Wood)
            .Sum(stack => stack.Quantity));
        Assert.Contains(engine.DrainWorldChanges(), change =>
            change.Kind == WorldChangeKind.TreeFelled &&
            change.Position == tree.Tree.Anchor);
    }

    [Fact]
    public void FellingAreaHarvestsSwampStumpForDeterministicPartialWoodStack()
    {
        var engine = CreateEngine(goblinCount: 2);
        var stump = engine.World.CreateWorldObjectSnapshot()
            .Where(worldObject => worldObject.Kind == WorldObjectKind.DeadTreeStump)
            .Select(worldObject => new
            {
                Stump = worldObject,
                Route = engine.Map.GetCardinalNeighbors(worldObject.Anchor)
                    .Where(engine.World.IsSurfaceTraversable)
                    .Select(position => engine.Navigation.FindSurfacePath(
                        engine.Map.GoblinSpawn,
                        position))
                    .Where(route => route is not null)
                    .OrderBy(route => route!.Count)
                    .FirstOrDefault(),
            })
            .Where(candidate => candidate.Route is not null)
            .OrderBy(candidate => candidate.Route!.Count)
            .First();
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var visibilityIndex = (stump.Stump.Anchor.Y * engine.Map.Width) + stump.Stump.Anchor.X;
        save["visibility"]!.AsArray()[visibilityIndex] = (int)CellVisibility.Explored;
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        engine.QueueCommand(SimulationCommand.DesignateTreeFelling(
            engine.CurrentTick.Next(),
            sequence: 1,
            stump.Stump.Anchor,
            stump.Stump.Anchor));

        engine.AdvanceTicks(1);

        Assert.Equal(WorkDesignationKind.FellTree,
            Assert.Single(engine.CreateSnapshot().WorkDesignations).Kind);
        Assert.Contains(engine.CreateSnapshot().Actors,
            actor => actor.Job.Kind == ActorJobKind.FellTree);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);

        engine.AdvanceTicks(2_000);
        restored.AdvanceTicks(2_000);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.DoesNotContain(engine.World.GetWorldObjectsAt(stump.Stump.Anchor),
            worldObject => worldObject.Kind == WorldObjectKind.DeadTreeStump);
        var recoveredWood = engine.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Wood)
            .Sum(stack => stack.Quantity);
        Assert.InRange(recoveredWood, 8, 16);
        Assert.Contains(engine.DrainWorldChanges(), change =>
            change.Kind == WorldChangeKind.StumpHarvested &&
            change.Position == stump.Stump.Anchor &&
            change.Amount == recoveredWood);
    }

    [Fact]
    public void QuarryAreaRequiresPickaxeAndTurnsBoulderIntoLooseStone()
    {
        var engine = CreateEngine(goblinCount: 2);
        var boulder = engine.World.CreateWorldObjectSnapshot()
            .Where(worldObject => worldObject.Kind == WorldObjectKind.Boulder)
            .Select(worldObject => new
            {
                Boulder = worldObject,
                Route = engine.Map.GetCardinalNeighbors(worldObject.Anchor)
                    .Where(engine.World.IsSurfaceTraversable)
                    .Select(position => engine.Navigation.FindSurfacePath(
                        engine.Map.GoblinSpawn,
                        position))
                    .Where(route => route is not null)
                    .OrderBy(route => route!.Count)
                    .FirstOrDefault(),
            })
            .Where(candidate => candidate.Route is not null)
            .OrderBy(candidate => candidate.Route!.Count)
            .First();
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var visibilityIndex = (boulder.Boulder.Anchor.Y * engine.Map.Width) + boulder.Boulder.Anchor.X;
        save["visibility"]!.AsArray()[visibilityIndex] = (int)CellVisibility.Explored;
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        engine.QueueCommand(SimulationCommand.DesignateBoulderQuarrying(
            engine.CurrentTick.Next(),
            sequence: 1,
            boulder.Boulder.Anchor,
            boulder.Boulder.Anchor));

        engine.AdvanceTicks(1);

        Assert.Equal(WorkDesignationKind.QuarryBoulder,
            Assert.Single(engine.CreateSnapshot().WorkDesignations).Kind);
        var miner = Assert.Single(engine.CreateSnapshot().Actors,
            actor => actor.Job.Kind == ActorJobKind.QuarryBoulder);
        Assert.True(miner.Equipment.HasFlag(PersonalEquipment.PrimitivePickaxe));
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);

        engine.AdvanceTicks(2_500);
        restored.AdvanceTicks(2_500);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.DoesNotContain(engine.World.GetWorldObjectsAt(boulder.Boulder.Anchor),
            worldObject => worldObject.Kind == WorldObjectKind.Boulder);
        var recoveredStone = engine.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Stone)
            .Sum(stack => stack.Quantity);
        Assert.InRange(recoveredStone, 16, 32);
        Assert.Contains(engine.DrainWorldChanges(), change =>
            change.Kind == WorldChangeKind.BoulderQuarried &&
            change.Position == boulder.Boulder.Anchor &&
            change.Amount == recoveredStone);
    }

    [Fact]
    public void DesignatedLooseStoneFeedsStoneStorageWithPullDisabled()
    {
        var seed = new WorldSeed(0x53544F5245UL);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            initialGoblinCount: 2,
            initialFoodStock: 0,
            initialWoodStock: 8,
            scatterInitialBrushwood: true);
        var spawn = engine.Map.GoblinSpawn;
        var zonePosition = engine.Map.GetCardinalNeighbors(spawn)
            .First(engine.World.IsSurfaceTraversable);
        engine.QueueCommand(SimulationCommand.BuildStoneStorage(
            new SimulationTick(1),
            sequence: 1,
            zonePosition));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
        var zone = Assert.Single(engine.CreateSnapshot().StorageZones);
        Assert.Equal(ResourceKind.Stone, zone.AcceptedResource);
        Assert.Equal(zone.Capacity, zone.DesiredQuantity);
        engine.QueueCommand(SimulationCommand.ConfigureStoragePull(
            engine.CurrentTick.Next(),
            sequence: 2,
            zone.Id,
            desiredQuantity: 0));
        engine.AdvanceTicks(1);

        var knownStone = engine.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Stone &&
                stack.Location.Kind == ItemLocationKind.Ground &&
                engine.CreateSnapshot().GetVisibility(
                    stack.Location.Position,
                    engine.Map.Width) != CellVisibility.Unknown)
            .ToArray();
        Assert.NotEmpty(knownStone);
        var minimum = new GridPosition(
            knownStone.Min(stack => stack.Location.Position.X),
            knownStone.Min(stack => stack.Location.Position.Y));
        var maximum = new GridPosition(
            knownStone.Max(stack => stack.Location.Position.X),
            knownStone.Max(stack => stack.Location.Position.Y));
        engine.QueueCommand(SimulationCommand.DesignateWork(
            engine.CurrentTick.Next(),
            sequence: 3,
            minimum,
            maximum,
            ResourceKind.Stone));

        engine.AdvanceTicks(800);

        zone = Assert.Single(engine.CreateSnapshot().StorageZones);
        Assert.True(zone.StoredQuantity > 0);
        Assert.DoesNotContain(engine.CreateSnapshot().WorkDesignations,
            designation => designation.Kind == WorkDesignationKind.GatherStone);
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
        var zone = Assert.Single(engine.CreateSnapshot().StorageZones);
        Assert.Equal(zone.Capacity, zone.DesiredQuantity);
        engine.QueueCommand(SimulationCommand.ConfigureStoragePull(
            engine.CurrentTick.Next(),
            sequence: 2,
            zone.Id,
            desiredQuantity: 0));
        engine.AdvanceTicks(1);

        engine.QueueCommand(SimulationCommand.DesignateWork(
            engine.CurrentTick.Next(),
            sequence: 3,
            spawn,
            spawn,
            ResourceKind.Wood));
        engine.AdvanceTicks(180);

        zone = Assert.Single(engine.CreateSnapshot().StorageZones);
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
    public void AssignedHaulerMovesOnlySurplusBetweenStorageZonesAndSurvivesSaveLoad()
    {
        var engine = CreateEngine(goblinCount: 2, initialWood: 10);
        var sourcePosition = engine.Map.GoblinSpawn;
        var destinationPosition = engine.Map.GetCardinalNeighbors(sourcePosition)
            .First(engine.World.IsSurfaceTraversable);
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1),
            sequence: 1,
            sourcePosition,
            ResourceKind.Wood,
            capacity: 10));
        engine.AdvanceTicks(80);

        var source = Assert.Single(engine.CreateSnapshot().StorageZones);
        Assert.Equal(10, source.StoredQuantity);
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            engine.CurrentTick.Next(),
            sequence: 2,
            destinationPosition,
            ResourceKind.Wood,
            capacity: 10));
        engine.AdvanceTicks(1);
        var snapshot = engine.CreateSnapshot();
        var destination = snapshot.StorageZones.Single(zone => zone.Id != source.Id);
        var assignedHauler = snapshot.Actors.OrderBy(actor => actor.Id).Last();
        engine.QueueCommand(SimulationCommand.ConfigureStoragePull(
            engine.CurrentTick.Next(),
            sequence: 3,
            source.Id,
            desiredQuantity: 2));
        engine.QueueCommand(SimulationCommand.ConfigureStorageHauler(
            engine.CurrentTick.Next(),
            sequence: 4,
            destination.Id,
            assignedHauler.Id));
        engine.DrainEvents();

        engine.AdvanceTicks(200);

        snapshot = engine.CreateSnapshot();
        source = snapshot.StorageZones.Single(zone => zone.Id == source.Id);
        destination = snapshot.StorageZones.Single(zone => zone.Id == destination.Id);
        Assert.Equal(2, source.StoredQuantity);
        Assert.Equal(8, destination.StoredQuantity);
        Assert.Equal(assignedHauler.Id, destination.AssignedHaulerId);
        Assert.All(
            engine.DrainEvents().Where(item =>
                item.Kind is SimulationEventKind.ItemPickedUp or SimulationEventKind.ItemStored),
            item => Assert.Equal(assignedHauler.Id, item.Subject));

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(
            assignedHauler.Id,
            restored.CreateSnapshot().StorageZones
                .Single(zone => zone.Id == destination.Id)
                .AssignedHaulerId);
    }

    [Fact]
    public void LinkedDestinationIgnoresSurplusOutsideItsConfiguredSource()
    {
        var engine = CreateEngine(goblinCount: 2, initialWood: 10);
        var firstSourcePosition = engine.Map.GoblinSpawn;
        var secondSourcePosition = engine.Map.GetCardinalNeighbors(firstSourcePosition)
            .First(engine.World.IsSurfaceTraversable);
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1), sequence: 1, firstSourcePosition, ResourceKind.Wood, capacity: 5));
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1), sequence: 2, secondSourcePosition, ResourceKind.Wood, capacity: 5));
        engine.AdvanceTicks(80);

        var sources = engine.CreateSnapshot().StorageZones;
        Assert.Equal(2, sources.Count);
        Assert.All(sources, source => Assert.Equal(5, source.StoredQuantity));
        var firstSource = sources.Single(source => source.Position == firstSourcePosition);
        var secondSource = sources.Single(source => source.Position == secondSourcePosition);
        var destinationPosition = (
                from y in Enumerable.Range(0, engine.Map.Height)
                from x in Enumerable.Range(0, engine.Map.Width)
                let position = new GridPosition(x, y)
                where position != firstSourcePosition &&
                    position != secondSourcePosition &&
                    engine.World.IsSurfaceTraversable(position)
                orderby Math.Abs(position.X - firstSourcePosition.X) +
                    Math.Abs(position.Y - firstSourcePosition.Y), position.Y, position.X
                select position)
            .First();
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            engine.CurrentTick.Next(),
            sequence: 3,
            destinationPosition,
            ResourceKind.Wood,
            capacity: 10));
        engine.AdvanceTicks(1);
        var destination = engine.CreateSnapshot().StorageZones
            .Single(zone => zone.Position == destinationPosition);
        engine.QueueCommand(SimulationCommand.ConfigureStorageSource(
            engine.CurrentTick.Next(),
            sequence: 4,
            destination.Id,
            firstSource.Id));
        engine.QueueCommand(SimulationCommand.ConfigureStoragePull(
            engine.CurrentTick.Next(),
            sequence: 5,
            secondSource.Id,
            desiredQuantity: 0));

        engine.AdvanceTicks(100);

        var snapshot = engine.CreateSnapshot();
        Assert.Equal(0, snapshot.StorageZones.Single(zone => zone.Id == destination.Id).StoredQuantity);
        Assert.Equal(5, snapshot.StorageZones.Single(zone => zone.Id == secondSource.Id).StoredQuantity);
        engine.QueueCommand(SimulationCommand.ConfigureStoragePull(
            engine.CurrentTick.Next(),
            sequence: 6,
            firstSource.Id,
            desiredQuantity: 0));
        engine.AdvanceTicks(100);

        snapshot = engine.CreateSnapshot();
        destination = snapshot.StorageZones.Single(zone => zone.Id == destination.Id);
        Assert.Equal(5, destination.StoredQuantity);
        Assert.Equal(firstSource.Id, destination.SourceStorageZoneId);
        Assert.Equal(5, snapshot.StorageZones.Single(zone => zone.Id == secondSource.Id).StoredQuantity);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(
            firstSource.Id,
            restored.CreateSnapshot().StorageZones
                .Single(zone => zone.Id == destination.Id)
                .SourceStorageZoneId);
    }

    [Fact]
    public void UrgentStockpileWinsAgainstACloserLowPriorityDestination()
    {
        var engine = CreateEngine(goblinCount: 2, initialWood: 5);
        var sourcePosition = engine.Map.GoblinSpawn;
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1), sequence: 1, sourcePosition, ResourceKind.Wood, capacity: 5));
        engine.AdvanceTicks(80);
        var source = Assert.Single(engine.CreateSnapshot().StorageZones);
        Assert.Equal(5, source.StoredQuantity);

        var destinations = (
                from y in Enumerable.Range(0, engine.Map.Height)
                from x in Enumerable.Range(0, engine.Map.Width)
                let position = new GridPosition(x, y)
                where position != sourcePosition && engine.World.IsSurfaceTraversable(position)
                let route = engine.Navigation.FindSurfacePath(sourcePosition, position)
                where route is { Count: > 0 and <= 12 }
                orderby route.Count, position.Y, position.X
                select (Position: position, Distance: route.Count))
            .ToArray();
        var lowPosition = destinations[0].Position;
        var urgentPosition = destinations.Last(candidate =>
            candidate.Distance > destinations[0].Distance).Position;
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            engine.CurrentTick.Next(), sequence: 2, lowPosition, ResourceKind.Wood, capacity: 5));
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            engine.CurrentTick.Next(), sequence: 3, urgentPosition, ResourceKind.Wood, capacity: 5));
        engine.AdvanceTicks(1);
        var snapshot = engine.CreateSnapshot();
        var low = snapshot.StorageZones.Single(zone => zone.Position == lowPosition);
        var urgent = snapshot.StorageZones.Single(zone => zone.Position == urgentPosition);
        var executeAt = engine.CurrentTick.Next();
        engine.QueueCommand(SimulationCommand.ConfigureStoragePriority(
            executeAt, sequence: 4, low.Id, StoragePriority.Low));
        engine.QueueCommand(SimulationCommand.ConfigureStoragePriority(
            executeAt, sequence: 5, urgent.Id, StoragePriority.Urgent));
        engine.QueueCommand(SimulationCommand.ConfigureStorageSource(
            executeAt, sequence: 6, low.Id, source.Id));
        engine.QueueCommand(SimulationCommand.ConfigureStorageSource(
            executeAt, sequence: 7, urgent.Id, source.Id));
        engine.QueueCommand(SimulationCommand.ConfigureStoragePull(
            executeAt, sequence: 8, source.Id, desiredQuantity: 0));

        engine.AdvanceTicks(1);

        Assert.Contains(engine.CreateSnapshot().Actors, actor =>
            actor.Job.Kind == ActorJobKind.Haul &&
            actor.Job.DestinationZoneId == urgent.Id);
        engine.AdvanceTicks(200);
        snapshot = engine.CreateSnapshot();
        Assert.Equal(0, snapshot.StorageZones.Single(zone => zone.Id == low.Id).StoredQuantity);
        urgent = snapshot.StorageZones.Single(zone => zone.Id == urgent.Id);
        Assert.Equal(5, urgent.StoredQuantity);
        Assert.Equal(StoragePriority.Urgent, urgent.Priority);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(
            StoragePriority.Urgent,
            restored.CreateSnapshot().StorageZones.Single(zone => zone.Id == urgent.Id).Priority);
    }

    [Fact]
    public void GlobalResourcePriorityOutranksLocalDistanceAcrossGoods()
    {
        var engine = CreateEngine(goblinCount: 2, initialWood: 5, initialFood: 9);
        var positions = (
                from y in Enumerable.Range(0, engine.Map.Height)
                from x in Enumerable.Range(0, engine.Map.Width)
                let position = new GridPosition(x, y)
                where engine.World.IsSurfaceTraversable(position) &&
                    engine.Navigation.HasSurfacePath(engine.Map.GoblinSpawn, position)
                orderby Math.Abs(position.X - engine.Map.GoblinSpawn.X) +
                    Math.Abs(position.Y - engine.Map.GoblinSpawn.Y), position.Y, position.X
                select position)
            .Take(4)
            .ToArray();
        Assert.Equal(4, positions.Length);
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1), sequence: 1, positions[0], ResourceKind.Food, capacity: 5));
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1), sequence: 2, positions[1], ResourceKind.Wood, capacity: 5));
        engine.AdvanceTicks(80);
        var snapshot = engine.CreateSnapshot();
        var foodSource = snapshot.StorageZones.Single(zone =>
            zone.AcceptedResource == ResourceKind.Food);
        var woodSource = snapshot.StorageZones.Single(zone =>
            zone.AcceptedResource == ResourceKind.Wood);
        Assert.Equal(5, foodSource.StoredQuantity);
        Assert.Equal(5, woodSource.StoredQuantity);
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            engine.CurrentTick.Next(), sequence: 3, positions[2], ResourceKind.Wood, capacity: 5));
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            engine.CurrentTick.Next(), sequence: 4, positions[3], ResourceKind.Food, capacity: 5));
        engine.AdvanceTicks(1);
        snapshot = engine.CreateSnapshot();
        var assignedHauler = snapshot.Actors.First(actor => actor.Job.Kind == ActorJobKind.None);
        var woodDestination = snapshot.StorageZones.Single(zone => zone.Position == positions[2]);
        var foodDestination = snapshot.StorageZones.Single(zone => zone.Position == positions[3]);
        var executeAt = engine.CurrentTick.Next();
        engine.QueueCommand(SimulationCommand.ConfigureStorageHauler(
            executeAt, sequence: 5, woodDestination.Id, assignedHauler.Id));
        engine.QueueCommand(SimulationCommand.ConfigureStorageHauler(
            executeAt, sequence: 6, foodDestination.Id, assignedHauler.Id));
        engine.QueueCommand(SimulationCommand.ConfigureStorageSource(
            executeAt, sequence: 7, woodDestination.Id, woodSource.Id));
        engine.QueueCommand(SimulationCommand.ConfigureStorageSource(
            executeAt, sequence: 8, foodDestination.Id, foodSource.Id));
        engine.QueueCommand(SimulationCommand.ConfigureStoragePull(
            executeAt, sequence: 9, woodSource.Id, desiredQuantity: 0));
        engine.QueueCommand(SimulationCommand.ConfigureStoragePull(
            executeAt, sequence: 10, foodSource.Id, desiredQuantity: 0));
        engine.QueueCommand(SimulationCommand.ConfigureResourcePriority(
            executeAt, sequence: 11, ResourceKind.Wood, StoragePriority.Low));
        engine.QueueCommand(SimulationCommand.ConfigureResourcePriority(
            executeAt, sequence: 12, ResourceKind.Food, StoragePriority.Urgent));

        engine.AdvanceTicks(1);

        snapshot = engine.CreateSnapshot();
        var hauler = snapshot.Actors.Single(actor => actor.Id == assignedHauler.Id);
        Assert.Equal(ActorJobKind.Haul, hauler.Job.Kind);
        Assert.Equal(foodDestination.Id, hauler.Job.DestinationZoneId);
        Assert.Equal(
            StoragePriority.Urgent,
            snapshot.ResourcePriorities.Single(priority =>
                priority.Resource == ResourceKind.Food).Priority);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(snapshot.ResourcePriorities, restored.CreateSnapshot().ResourcePriorities);
    }

    [Fact]
    public void AssignedDutyWakesExplorerAndOutranksUrgentPublicStockpile()
    {
        var engine = CreateEngine(goblinCount: 2, initialWood: 5);
        var sourcePosition = engine.Map.GoblinSpawn;
        var destinations = (
                from y in Enumerable.Range(0, engine.Map.Height)
                from x in Enumerable.Range(0, engine.Map.Width)
                let position = new GridPosition(x, y)
                where position != sourcePosition &&
                    engine.World.IsSurfaceTraversable(position) &&
                    engine.Navigation.HasSurfacePath(sourcePosition, position)
                orderby Math.Abs(position.X - sourcePosition.X) +
                    Math.Abs(position.Y - sourcePosition.Y), position.Y, position.X
                select position)
            .Take(2)
            .ToArray();
        Assert.Equal(2, destinations.Length);
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1), sequence: 1, sourcePosition, ResourceKind.Wood, capacity: 5));
        engine.QueueCommand(SimulationCommand.DesignateScouting(
            new SimulationTick(1), sequence: 2,
            new GridPosition(0, 0),
            new GridPosition(engine.Map.Width - 1, engine.Map.Height - 1)));
        engine.AdvanceTicks(80);
        var source = Assert.Single(engine.CreateSnapshot().StorageZones);
        Assert.Equal(5, source.StoredQuantity);
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            engine.CurrentTick.Next(), sequence: 2, destinations[0], ResourceKind.Wood, capacity: 5));
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            engine.CurrentTick.Next(), sequence: 3, destinations[1], ResourceKind.Wood, capacity: 5));
        engine.AdvanceTicks(1);
        for (var attempt = 0;
             attempt < SimulationDefinitions.Foundation.ActorMovementIntervalTicks &&
             engine.CreateSnapshot().Actors.All(actor => actor.Job.Kind != ActorJobKind.Explore);
             attempt++)
        {
            engine.AdvanceTicks(1);
        }

        var snapshot = engine.CreateSnapshot();
        var assignedActor = snapshot.Actors.First(actor => actor.Job.Kind == ActorJobKind.Explore);
        var otherActor = snapshot.Actors.Single(actor => actor.Id != assignedActor.Id);
        var assignedDestination = snapshot.StorageZones.Single(zone =>
            zone.Position == destinations[0]);
        var publicDestination = snapshot.StorageZones.Single(zone =>
            zone.Position == destinations[1]);
        var executeAt = engine.CurrentTick.Next();
        engine.QueueCommand(SimulationCommand.Move(
            executeAt, sequence: 4, otherActor.Id, engine.Map.HumanVillage));
        engine.QueueCommand(SimulationCommand.ConfigureStoragePriority(
            executeAt, sequence: 5, assignedDestination.Id, StoragePriority.Low));
        engine.QueueCommand(SimulationCommand.ConfigureStoragePriority(
            executeAt, sequence: 6, publicDestination.Id, StoragePriority.Urgent));
        engine.QueueCommand(SimulationCommand.ConfigureStorageHauler(
            executeAt, sequence: 7, assignedDestination.Id, assignedActor.Id));
        engine.QueueCommand(SimulationCommand.ConfigureStorageSource(
            executeAt, sequence: 8, assignedDestination.Id, source.Id));
        engine.QueueCommand(SimulationCommand.ConfigureStorageSource(
            executeAt, sequence: 9, publicDestination.Id, source.Id));
        engine.QueueCommand(SimulationCommand.ConfigureStoragePull(
            executeAt, sequence: 10, source.Id, desiredQuantity: 0));

        engine.AdvanceTicks(1);

        snapshot = engine.CreateSnapshot();
        assignedActor = snapshot.Actors.Single(actor => actor.Id == assignedActor.Id);
        Assert.Equal(ActorJobKind.Haul, assignedActor.Job.Kind);
        Assert.Equal(assignedDestination.Id, assignedActor.Job.DestinationZoneId);
        Assert.Equal(
            ActorJobKind.Move,
            snapshot.Actors.Single(actor => actor.Id == otherActor.Id).Job.Kind);
    }

    [Fact]
    public void DeliveryDiagnosticExplainsProtectedSurplusAndTracksTheDelivery()
    {
        var engine = CreateEngine(goblinCount: 1, initialWood: 5);
        var sourcePosition = engine.Map.GoblinSpawn;
        var destinationPosition = engine.Map.GetCardinalNeighbors(sourcePosition)
            .First(engine.World.IsSurfaceTraversable);
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1), sequence: 1, sourcePosition, ResourceKind.Wood, capacity: 5));
        engine.AdvanceTicks(80);
        var source = Assert.Single(engine.CreateSnapshot().StorageZones);
        Assert.Equal(5, source.StoredQuantity);

        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            engine.CurrentTick.Next(),
            sequence: 2,
            destinationPosition,
            ResourceKind.Wood,
            capacity: 5));
        engine.AdvanceTicks(1);
        var snapshot = engine.CreateSnapshot();
        var destination = snapshot.StorageZones.Single(zone => zone.Id != source.Id);
        engine.QueueCommand(SimulationCommand.ConfigureStorageSource(
            engine.CurrentTick.Next(), sequence: 3, destination.Id, source.Id));
        engine.AdvanceTicks(1);

        var diagnostic = engine.InspectStorageDelivery(destination.Id);
        Assert.Equal(StorageDeliveryState.NoSurplus, diagnostic.State);
        Assert.Equal(5, diagnostic.RequestedQuantity);
        Assert.Equal(1, diagnostic.MatchingSourceCount);
        Assert.Equal(0, diagnostic.AvailableSourceQuantity);

        var actor = Assert.Single(snapshot.Actors);
        var executeAt = engine.CurrentTick.Next();
        engine.QueueCommand(SimulationCommand.ConfigureStoragePull(
            executeAt, sequence: 4, source.Id, desiredQuantity: 0));
        engine.QueueCommand(SimulationCommand.ConfigureStorageHauler(
            executeAt, sequence: 5, destination.Id, actor.Id));

        for (var attempt = 0; attempt < 20; attempt++)
        {
            engine.AdvanceTicks(1);
            diagnostic = engine.InspectStorageDelivery(destination.Id);
            if (diagnostic.State == StorageDeliveryState.InTransit)
            {
                break;
            }
        }

        Assert.Equal(StorageDeliveryState.InTransit, diagnostic.State);
        Assert.Equal(5, diagnostic.InTransitQuantity);
        engine.AdvanceTicks(200);
        diagnostic = engine.InspectStorageDelivery(destination.Id);
        Assert.Equal(StorageDeliveryState.Satisfied, diagnostic.State);
        Assert.Equal(0, diagnostic.RequestedQuantity);
    }

    [Fact]
    public void DeliveryDiagnosticNamesABusyAssignedHaulerState()
    {
        var engine = CreateEngine(goblinCount: 1, initialWood: 5);
        var sourcePosition = engine.Map.GoblinSpawn;
        var destinationPosition = engine.Map.GetCardinalNeighbors(sourcePosition)
            .First(engine.World.IsSurfaceTraversable);
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1), sequence: 1, sourcePosition, ResourceKind.Wood, capacity: 5));
        engine.AdvanceTicks(80);
        var snapshot = engine.CreateSnapshot();
        var source = Assert.Single(snapshot.StorageZones);
        var actor = Assert.Single(snapshot.Actors);
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            engine.CurrentTick.Next(),
            sequence: 2,
            destinationPosition,
            ResourceKind.Wood,
            capacity: 5));
        engine.AdvanceTicks(1);
        var destination = engine.CreateSnapshot().StorageZones.Single(zone => zone.Id != source.Id);
        var executeAt = engine.CurrentTick.Next();
        engine.QueueCommand(SimulationCommand.Move(
            executeAt, sequence: 3, actor.Id, engine.Map.HumanVillage));
        engine.QueueCommand(SimulationCommand.ConfigureStoragePull(
            executeAt, sequence: 4, source.Id, desiredQuantity: 0));
        engine.QueueCommand(SimulationCommand.ConfigureStorageSource(
            executeAt, sequence: 5, destination.Id, source.Id));
        engine.QueueCommand(SimulationCommand.ConfigureStorageHauler(
            executeAt, sequence: 6, destination.Id, actor.Id));

        engine.AdvanceTicks(1);

        var diagnostic = engine.InspectStorageDelivery(destination.Id);
        Assert.Equal(StorageDeliveryState.AssignedHaulerBusy, diagnostic.State);
        Assert.Equal(5, diagnostic.AvailableSourceQuantity);
        Assert.Equal(ActorJobKind.Move, Assert.Single(engine.CreateSnapshot().Actors).Job.Kind);
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

    [Fact]
    public void PlannerPriorityPersistsAndClearingOrderKeepsOtherWork()
    {
        var engine = CreateEngine(goblinCount: 1, initialWood: 4);
        var spawn = engine.Map.GoblinSpawn;
        engine.QueueCommand(SimulationCommand.DesignateWork(
            new SimulationTick(1), sequence: 1, spawn, spawn, ResourceKind.Food));
        engine.QueueCommand(SimulationCommand.DesignateWork(
            new SimulationTick(1), sequence: 2, spawn, spawn, ResourceKind.Wood));
        engine.AdvanceTicks(1);
        Assert.Contains(engine.CreateSnapshot().WorkDesignations,
            designation => designation.Kind == WorkDesignationKind.GatherFood);
        Assert.Contains(engine.CreateSnapshot().WorkDesignations,
            designation => designation.Kind == WorkDesignationKind.GatherBrushwood);
        var foodOrderId = engine.CreateSnapshot().WorkDesignations
            .First(designation => designation.Kind == WorkDesignationKind.GatherFood)
            .OrderId;

        engine.QueueCommand(SimulationCommand.ConfigureWorkPriority(
            new SimulationTick(2), sequence: 3,
            foodOrderId, StoragePriority.Urgent));
        engine.AdvanceTicks(1);

        Assert.All(engine.CreateSnapshot().WorkDesignations.Where(designation =>
                designation.Kind == WorkDesignationKind.GatherFood),
            designation => Assert.Equal(StoragePriority.Urgent, designation.Priority));
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.All(restored.CreateSnapshot().WorkDesignations.Where(designation =>
                designation.Kind == WorkDesignationKind.GatherFood),
            designation => Assert.Equal(StoragePriority.Urgent, designation.Priority));

        restored.QueueCommand(SimulationCommand.ClearWorkDesignationOrder(
            restored.CurrentTick.Next(), sequence: 4, foodOrderId));
        restored.AdvanceTicks(1);

        Assert.DoesNotContain(restored.CreateSnapshot().WorkDesignations,
            designation => designation.Kind == WorkDesignationKind.GatherFood);
        Assert.Contains(restored.CreateSnapshot().WorkDesignations,
            designation => designation.Kind == WorkDesignationKind.GatherBrushwood);
    }

    [Fact]
    public void ReplacingWorkAreaIsAtomicAndKeepsOrderIdentity()
    {
        var engine = CreateEngine(goblinCount: 1);
        var spawn = engine.Map.GoblinSpawn;
        engine.QueueCommand(SimulationCommand.DesignateWork(
            new SimulationTick(1), sequence: 1, spawn, spawn, ResourceKind.Food));
        engine.AdvanceTicks(1);
        var original = Assert.Single(engine.CreateSnapshot().WorkDesignations);
        var snapshot = engine.CreateSnapshot();
        var foodPositions = snapshot.PlantPatches
            .Where(plant => plant.Biomass > 0)
            .Select(plant => plant.Position)
            .ToHashSet();
        var emptyVisibleCell =
            (from y in Enumerable.Range(0, engine.Map.Height)
             from x in Enumerable.Range(0, engine.Map.Width)
             let position = new GridPosition(x, y)
             where snapshot.GetVisibility(position, engine.Map.Width) != CellVisibility.Unknown &&
                   !foodPositions.Contains(position)
             select position).First();

        engine.QueueCommand(SimulationCommand.DesignateWork(
                new SimulationTick(2), sequence: 2,
                emptyVisibleCell, emptyVisibleCell, ResourceKind.Food)
            .ReplacingWorkOrder(original.OrderId, StoragePriority.Urgent));
        engine.AdvanceTicks(1);

        var afterRejectedReplacement = Assert.Single(engine.CreateSnapshot().WorkDesignations);
        Assert.Equal(original, afterRejectedReplacement);

        engine.QueueCommand(SimulationCommand.DesignateWork(
                new SimulationTick(3), sequence: 3, spawn, spawn, ResourceKind.Food)
            .ReplacingWorkOrder(original.OrderId, StoragePriority.Urgent));
        engine.AdvanceTicks(1);

        var replaced = Assert.Single(engine.CreateSnapshot().WorkDesignations);
        Assert.Equal(original.OrderId, replaced.OrderId);
        Assert.NotEqual(original.Id, replaced.Id);
        Assert.Equal(StoragePriority.Urgent, replaced.Priority);
    }

    [Fact]
    public void SuspendedPlannerOrderStopsWorkAndResumesWithSameTargets()
    {
        var engine = CreateEngine(goblinCount: 1);
        var spawn = engine.Map.GoblinSpawn;
        engine.QueueCommand(SimulationCommand.DesignateWork(
            new SimulationTick(1), sequence: 1, spawn, spawn, ResourceKind.Food));
        engine.AdvanceTicks(1);
        var original = Assert.Single(engine.CreateSnapshot().WorkDesignations);
        Assert.Equal(ActorJobKind.Forage, Assert.Single(engine.CreateSnapshot().Actors).Job.Kind);

        engine.QueueCommand(SimulationCommand.ConfigureWorkSuspension(
            new SimulationTick(2), sequence: 2, original.OrderId, isSuspended: true));
        engine.AdvanceTicks(1);

        var suspended = Assert.Single(engine.CreateSnapshot().WorkDesignations);
        Assert.True(suspended.IsSuspended);
        Assert.Equal(ActorJobKind.None, Assert.Single(engine.CreateSnapshot().Actors).Job.Kind);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.True(Assert.Single(restored.CreateSnapshot().WorkDesignations).IsSuspended);

        restored.QueueCommand(SimulationCommand.ConfigureWorkSuspension(
            restored.CurrentTick.Next(), sequence: 3, original.OrderId, isSuspended: false));
        restored.AdvanceTicks(1);

        var resumed = Assert.Single(restored.CreateSnapshot().WorkDesignations);
        Assert.False(resumed.IsSuspended);
        Assert.Equal(original.Id, resumed.Id);
        Assert.Equal(ActorJobKind.Forage, Assert.Single(restored.CreateSnapshot().Actors).Job.Kind);
    }

    private static SimulationEngine CreateEngine(
        int goblinCount,
        int initialWood = 0,
        int initialFood = 0)
    {
        var seed = new WorldSeed(0x574F524BUL);
        return SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            initialGoblinCount: goblinCount,
            initialFoodStock: initialFood,
            initialWoodStock: initialWood);
    }
}
