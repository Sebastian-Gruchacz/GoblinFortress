using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using System.Text.Json.Nodes;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class MoveCommandTests
{
    [Fact]
    public void OrderedGoblinClimbsGeneratedTerrainRampToMaterialHillSurface()
    {
        var seed = new WorldSeed(0x48494C4C434C494DUL);
        var map = SwampMapGenerator.Generate(seed, 96, 96);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 20);
        var transition = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width)
                .Select(x => new GridPosition(x, y)))
            .Where(column => map.GetColumnCell(column).RampDirection != TerrainRampDirection.None)
            .Select(column =>
            {
                var direction = map.GetColumnCell(column).RampDirection;
                var uphillColumn = direction switch
                {
                    TerrainRampDirection.North => column with { Y = column.Y - 1 },
                    TerrainRampDirection.East => column with { X = column.X + 1 },
                    TerrainRampDirection.South => column with { Y = column.Y + 1 },
                    TerrainRampDirection.West => column with { X = column.X - 1 },
                    _ => column,
                };
                return (
                    Lower: map.GetTerrainSurfacePosition(column),
                    Upper: map.GetTerrainSurfacePosition(uphillColumn));
            })
            .First(item => item.Upper.Z > item.Lower.Z &&
                engine.World.IsTerrainTraversable(item.Lower) &&
                engine.World.IsTerrainTraversable(item.Upper));
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        save["actors"]![0]!["x"] = transition.Lower.X;
        save["actors"]![0]!["y"] = transition.Lower.Y;
        save["actors"]![0]!["z"] = transition.Lower.Z;
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        var actor = Assert.Single(engine.CreateSnapshot().Actors);

        engine.QueueCommand(SimulationCommand.Move(
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            actor.Id,
            transition.Upper));
        engine.AdvanceTicks(1);

        actor = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.Equal(ActorJobKind.Move, actor.Job.Kind);
        Assert.Equal(transition.Upper, actor.Job.Target);
        Assert.Equal(1, actor.Job.RemainingRouteSteps);

        engine.AdvanceTicks(SimulationDefinitions.Foundation.ActorMovementIntervalTicks - 1);

        actor = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.Equal(transition.Upper, actor.Position);
        Assert.Equal(ActorJobKind.None, actor.Job.Kind);
        Assert.Equal(CellVisibility.Visible, engine.Visibility.Get(transition.Upper));
    }

    [Fact]
    public void OrderedGoblinTravelsCellByCellAndCompletesAtDestination()
    {
        var engine = CreateEngine();
        var actor = engine.CreateSnapshot().Actors.Single();
        var route = engine.World.FindSurfacePath(actor.Position, engine.Map.HumanVillage)
            ?? throw new InvalidOperationException("Generated settlements are disconnected.");
        var destination = route[2];
        engine.QueueCommand(SimulationCommand.Move(
            new SimulationTick(1),
            sequence: 1,
            actor.Id,
            destination));

        engine.AdvanceTicks(1);

        var ordered = engine.CreateSnapshot().Actors.Single();
        Assert.Equal(ActorJobKind.Move, ordered.Job.Kind);
        Assert.Equal(destination, ordered.Job.Target);
        Assert.Equal(actor.Position, ordered.Position);

        engine.AdvanceTicks(
            (3 * SimulationDefinitions.Foundation.ActorMovementIntervalTicks) - 1);

        var completed = engine.CreateSnapshot().Actors.Single();
        var events = engine.DrainEvents();
        Assert.Equal(destination, completed.Position);
        Assert.Equal(ActorJobKind.None, completed.Job.Kind);
        Assert.Contains(events, item => item.Kind == SimulationEventKind.MoveOrdered);
        Assert.Contains(events, item => item.Kind == SimulationEventKind.MoveCompleted);
    }

    [Fact]
    public void TraversedEdgeBecomesPersistedPersonalKnowledge()
    {
        var engine = CreateEngine();
        var actor = engine.CreateSnapshot().Actors.Single();
        var destination = engine.World.FindSurfacePath(actor.Position, engine.Map.HumanVillage)![0];
        engine.QueueCommand(SimulationCommand.Move(
            new SimulationTick(1),
            sequence: 1,
            actor.Id,
            destination));

        engine.AdvanceTicks(SimulationDefinitions.Foundation.ActorMovementIntervalTicks);

        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var belief = Assert.Single(save["actors"]![0]!["navigationBeliefs"]!.AsArray())!;
        Assert.Equal((int)NavigationBeliefStatus.Passable, belief["status"]!.GetValue<int>());
        Assert.Equal(actor.Id.Value, belief["sourceActorId"]!.GetValue<ulong>());
        Assert.True(belief["isDirectObservation"]!.GetValue<bool>());

        var restored = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void OrderedMoveAvoidsAnEdgeKnownBlockedOnlyByThatGoblin()
    {
        var engine = CreateEngine();
        var square = FindTraversableSquare(engine.World);
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var actor = save["actors"]![0]!.AsObject();
        actor["x"] = square.Start.X;
        actor["y"] = square.Start.Y;
        actor["z"] = square.Start.Z;
        actor["navigationBeliefs"] = new JsonArray(new JsonObject
        {
            ["firstX"] = square.Start.X,
            ["firstY"] = square.Start.Y,
            ["firstZ"] = square.Start.Z,
            ["secondX"] = square.Destination.X,
            ["secondY"] = square.Destination.Y,
            ["secondZ"] = square.Destination.Z,
            ["status"] = (int)NavigationBeliefStatus.Blocked,
            ["observedAt"] = 0,
            ["receivedAt"] = 0,
            ["sourceActorId"] = actor["id"]!.GetValue<ulong>(),
            ["confidence"] = 100,
            ["isDirectObservation"] = true,
        });
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        var goblin = engine.CreateSnapshot().Actors.Single();
        engine.QueueCommand(SimulationCommand.Move(
            new SimulationTick(1), sequence: 1, goblin.Id, square.Destination));

        engine.AdvanceTicks(1);

        Assert.Contains(engine.DrainEvents(), simulationEvent =>
            simulationEvent.Kind == SimulationEventKind.MoveOrdered &&
            simulationEvent.Amount >= 3);
        Assert.Equal(square.Start, engine.CreateSnapshot().Actors.Single().Position);
    }

    [Fact]
    public void OrderedMoveUsesTribalReportWhenGoblinHasNoPersonalObservation()
    {
        var engine = CreateEngine();
        var square = FindTraversableSquare(engine.World);
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var actor = save["actors"]![0]!.AsObject();
        actor["x"] = square.Start.X;
        actor["y"] = square.Start.Y;
        actor["z"] = square.Start.Z;
        save["tribeNavigationBeliefs"] = new JsonArray(new JsonObject
        {
            ["firstX"] = square.Start.X,
            ["firstY"] = square.Start.Y,
            ["firstZ"] = square.Start.Z,
            ["secondX"] = square.Destination.X,
            ["secondY"] = square.Destination.Y,
            ["secondZ"] = square.Destination.Z,
            ["status"] = (int)NavigationBeliefStatus.Blocked,
            ["observedAt"] = 0,
            ["receivedAt"] = 0,
            ["sourceActorId"] = 999UL,
            ["confidence"] = 80,
            ["isDirectObservation"] = false,
        });
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        var goblin = engine.CreateSnapshot().Actors.Single();
        engine.QueueCommand(SimulationCommand.Move(
            new SimulationTick(1), sequence: 1, goblin.Id, square.Destination));

        engine.AdvanceTicks(1);

        Assert.Contains(engine.DrainEvents(), simulationEvent =>
            simulationEvent.Kind == SimulationEventKind.MoveOrdered &&
            simulationEvent.Amount >= 3);
        Assert.Empty(JsonNode.Parse(engine.Save())!["actors"]![0]!["navigationBeliefs"]!
            .AsArray());
    }

    [Fact]
    public void OrderedMoveContinuesIdenticallyAfterSaveLoad()
    {
        var engine = CreateEngine();
        var actor = engine.CreateSnapshot().Actors.Single();
        var route = engine.World.FindSurfacePath(actor.Position, engine.Map.HumanVillage)
            ?? throw new InvalidOperationException("Generated settlements are disconnected.");
        engine.QueueCommand(SimulationCommand.Move(
            new SimulationTick(1),
            sequence: 1,
            actor.Id,
            route[7]));
        engine.AdvanceTicks(17);

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        engine.AdvanceTicks(80);
        restored.AdvanceTicks(80);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void UnknownImpassableDestinationIsRejectedAtExecutionWithoutMovingActor()
    {
        var engine = CreateEngine();
        var actor = engine.CreateSnapshot().Actors.Single();
        var impassable = FindImpassableCell(engine.Map);
        Assert.Equal(CellVisibility.Unknown, engine.Visibility.Get(impassable));
        engine.QueueCommand(SimulationCommand.Move(
            new SimulationTick(1),
            sequence: 1,
            actor.Id,
            impassable));

        engine.AdvanceTicks(1);

        Assert.Equal(actor.Position, engine.CreateSnapshot().Actors.Single().Position);
        Assert.Contains(engine.DrainEvents(), item =>
            item.Kind == SimulationEventKind.CommandRejected &&
            item.Amount == (int)SimulationCommandKind.Move);
    }

    [Fact]
    public void OrderedGoblinDescendsThroughCaveMouthAndSaveLoadPreservesTheRoute()
    {
        var engine = CreateEngine();
        var actor = engine.CreateSnapshot().Actors.Single();
        var destination = engine.Map.VerticalPassages
            .Where(passage => passage.Kind == VerticalPassageKind.CaveMouth)
            .Select(passage => passage.Lower)
            .First(position => engine.Navigation.FindPath(actor.Position, position) is not null);
        var route = engine.Navigation.FindPath(actor.Position, destination)
            ?? throw new InvalidOperationException("Generated cave entrance is unreachable.");
        engine.QueueCommand(SimulationCommand.Move(
            new SimulationTick(1),
            sequence: 1,
            actor.Id,
            destination));
        engine.AdvanceTicks(Math.Max(1, route.Count / 2) *
            SimulationDefinitions.Foundation.ActorMovementIntervalTicks);

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());

        for (var tick = 0; tick < route.Count *
             SimulationDefinitions.Foundation.ActorMovementIntervalTicks &&
             (engine.CreateSnapshot().Actors.Single().Position != destination ||
              restored.CreateSnapshot().Actors.Single().Position != destination); tick++)
        {
            engine.AdvanceTicks(1);
            restored.AdvanceTicks(1);
        }

        Assert.Equal(destination, engine.CreateSnapshot().Actors.Single().Position);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.True(engine.Visibility.Get(destination).IsDiscovered());
    }

    [Fact]
    public void GoblinSuppliesAndBuildsFoodStorageOnFirstCaveLevel()
    {
        var seed = new WorldSeed(0x4341564553544F52UL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 12,
            initialWoodStock: 4);
        var actor = engine.CreateSnapshot().Actors.Single();
        var destination = map.VerticalPassages
            .Where(passage => passage.Kind == VerticalPassageKind.CaveMouth)
            .Select(passage => passage.Lower)
            .First(position => engine.Navigation.FindPath(actor.Position, position) is not null);
        engine.QueueCommand(SimulationCommand.BuildFoodStorage(
            new SimulationTick(1),
            sequence: 1,
            destination));

        for (var tick = 0; tick < 8_000 &&
             engine.CreateSnapshot().StorageZones.All(zone => zone.Position != destination); tick++)
        {
            engine.AdvanceTicks(1);
        }

        var storage = Assert.Single(engine.CreateSnapshot().StorageZones.Where(zone =>
            zone.Position == destination));
        Assert.Equal(
            GoblinStronghold.Simulation.Resources.ResourceKind.Food,
            storage.AcceptedResource);
        Assert.DoesNotContain(engine.CreateSnapshot().ConstructionSites, site =>
            site.Anchor == destination);
    }

    [Fact]
    public void DesignatedCaveWallIsMinedAndPersistsAcrossSaveLoad()
    {
        var engine = CreateEngine();
        var actor = engine.CreateSnapshot().Actors.Single();
        Assert.True(actor.Equipment.HasFlag(PersonalEquipment.PrimitivePickaxe));
        var landing = engine.Map.VerticalPassages
            .Where(passage => passage.Kind == VerticalPassageKind.CaveMouth)
            .Select(passage => passage.Lower)
            .First(position => engine.Navigation.FindPath(actor.Position, position) is not null);
        engine.QueueCommand(SimulationCommand.Move(
            new SimulationTick(1), sequence: 1, actor.Id, landing));
        var route = engine.Navigation.FindPath(actor.Position, landing)!;
        engine.AdvanceTicks((route.Count + 2) *
            SimulationDefinitions.Foundation.ActorMovementIntervalTicks);
        var rock =
            (from y in Enumerable.Range(0, engine.Map.Height)
             from x in Enumerable.Range(0, engine.Map.Width)
             let position = new GridPosition(x, y, landing.Z)
             where engine.World.CanExcavateRock(position) &&
                   engine.Visibility.Get(position).IsDiscovered()
             orderby Math.Abs(position.X - landing.X) + Math.Abs(position.Y - landing.Y)
             select position).First();
        engine.QueueCommand(SimulationCommand.DesignateRockMining(
            engine.CurrentTick.Next(), sequence: 2, rock, rock));

        for (var tick = 0; tick < 5_000 &&
             !engine.World.ExcavatedCaveCells.Contains(rock); tick++)
        {
            engine.AdvanceTicks(1);
        }

        Assert.Contains(rock, engine.World.ExcavatedCaveCells);
        Assert.Contains(engine.CreateSnapshot().ItemStacks, stack =>
            stack.Location.Kind == ItemLocationKind.Ground &&
            stack.Location.Position == rock &&
            stack.Resource == ResourceKind.Stone);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Contains(rock, restored.World.ExcavatedCaveCells);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void StrandedCargoIsDroppedSoTheOnlyMinerCanTakeQueuedMiningWork()
    {
        var engine = CreateEngine();
        var actor = Assert.Single(engine.CreateSnapshot().Actors);
        var landing = engine.Map.VerticalPassages
            .Where(passage => passage.Kind == VerticalPassageKind.CaveMouth)
            .Select(passage => passage.Lower)
            .First(position => engine.Navigation.FindPath(actor.Position, position) is not null);
        var route = engine.Navigation.FindPath(actor.Position, landing)!;
        engine.QueueCommand(SimulationCommand.Move(
            new SimulationTick(1), sequence: 1, actor.Id, landing));
        engine.AdvanceTicks((route.Count + 2) *
            SimulationDefinitions.Foundation.ActorMovementIntervalTicks);
        var rock =
            (from y in Enumerable.Range(0, engine.Map.Height)
             from x in Enumerable.Range(0, engine.Map.Width)
             let position = new GridPosition(x, y, landing.Z)
             where engine.World.CanExcavateRock(position) &&
                   engine.Visibility.Get(position).IsDiscovered()
             orderby Math.Abs(position.X - landing.X) + Math.Abs(position.Y - landing.Y)
             select position).First();
        var cargo = engine.CreateSnapshot().ItemStacks.First(stack =>
            stack.Resource == ResourceKind.Food &&
            stack.Location.Kind == ItemLocationKind.Ground);
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            engine.CurrentTick.Next(), sequence: 2, landing, ResourceKind.Wood, capacity: 64));
        engine.QueueCommand(SimulationCommand.PickUp(
            engine.CurrentTick.Next(), sequence: 3, actor.Id, cargo.Id, quantity: 1));
        engine.QueueCommand(SimulationCommand.DesignateRockMining(
            engine.CurrentTick.Next(), sequence: 4, rock, rock));

        engine.AdvanceTicks(1);

        actor = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.Equal(EntityId.None, actor.CarriedStackId);
        var dropEvent = Assert.Single(engine.DrainEvents(), item =>
            item.Kind == SimulationEventKind.ItemDropped && item.Subject == actor.Id);
        Assert.Contains(engine.CreateSnapshot().ItemStacks, stack =>
            stack.Id == dropEvent.Target &&
            stack.Location == ItemLocation.OnGround(actor.Position));

        engine.AdvanceTicks(1);

        actor = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.Equal(ActorJobKind.MineRock, actor.Job.Kind);
        Assert.NotEqual(EntityId.None, actor.Job.SourceStackId);
    }

    [Fact]
    public void MiningAreaKeepsUnknownCellsQueuedUntilTunnelReachesThem()
    {
        var engine = CreateEngine();
        var actor = engine.CreateSnapshot().Actors.Single();
        var landing = engine.Map.VerticalPassages
            .Where(passage => passage.Kind == VerticalPassageKind.CaveMouth)
            .Select(passage => passage.Lower)
            .First(position => engine.Navigation.FindPath(actor.Position, position) is not null);
        engine.QueueCommand(SimulationCommand.Move(
            new SimulationTick(1), sequence: 1, actor.Id, landing));
        var route = engine.Navigation.FindPath(actor.Position, landing)!;
        engine.AdvanceTicks((route.Count + 2) *
            SimulationDefinitions.Foundation.ActorMovementIntervalTicks);
        var directions = new[]
        {
            new GridPosition(0, -1),
            new GridPosition(1, 0),
            new GridPosition(0, 1),
            new GridPosition(-1, 0),
        };
        var tunnel =
            (from y in Enumerable.Range(0, engine.Map.Height)
             from x in Enumerable.Range(0, engine.Map.Width)
             let first = new GridPosition(x, y, landing.Z)
             where engine.World.CanExcavateRock(first) &&
                   engine.Visibility.Get(first).IsDiscovered()
             from direction in directions
             let second = new GridPosition(
                 first.X + direction.X,
                 first.Y + direction.Y,
                 first.Z)
             where engine.World.IsSolidCaveRock(second) &&
                   !engine.World.CanExcavateRock(second) &&
                   engine.Visibility.Get(second) == CellVisibility.Unknown
             orderby Math.Abs(first.X - landing.X) + Math.Abs(first.Y - landing.Y)
             select (First: first, Second: second)).First();
        engine.QueueCommand(SimulationCommand.DesignateRockMining(
            engine.CurrentTick.Next(), sequence: 2, tunnel.First, tunnel.Second));
        engine.AdvanceTicks(1);

        var designations = engine.CreateSnapshot().WorkDesignations.Where(designation =>
            designation.Kind == WorkDesignationKind.MineRock).ToArray();
        Assert.Equal(2, designations.Length);
        Assert.Contains(designations, designation => designation.Target == tunnel.First);
        Assert.Contains(designations, designation => designation.Target == tunnel.Second);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());

        for (var tick = 0; tick < 6_000 &&
             (!engine.World.ExcavatedCaveCells.Contains(tunnel.Second) ||
              !restored.World.ExcavatedCaveCells.Contains(tunnel.Second)); tick++)
        {
            engine.AdvanceTicks(1);
            restored.AdvanceTicks(1);
        }

        Assert.Contains(tunnel.First, engine.World.ExcavatedCaveCells);
        Assert.Contains(tunnel.Second, engine.World.ExcavatedCaveCells);
        Assert.DoesNotContain(engine.CreateSnapshot().WorkDesignations, designation =>
            designation.Kind == WorkDesignationKind.MineRock);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void MiningAnExposedVeinProducesRockAndItsMineral()
    {
        var seed = new WorldSeed(0x4D494E4552414CUL);
        var map = SwampMapGenerator.Generate(seed, width: 64, height: 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 40);
        var actor = engine.CreateSnapshot().Actors.Single();
        var cardinalOffsets = new[]
        {
            new GridPosition(0, -1),
            new GridPosition(1, 0),
            new GridPosition(0, 1),
            new GridPosition(-1, 0),
        };
        var vein =
            (from level in Enumerable.Range(1, map.CaveLevelCount)
             from y in Enumerable.Range(0, map.Height)
             from x in Enumerable.Range(0, map.Width)
             let rock = new GridPosition(x, y, -level)
             let cell = map.GetCaveCell(rock)
             where cell.Deposit != MineralDepositKind.None && engine.World.CanExcavateRock(rock)
             from offset in cardinalOffsets
             let access = new GridPosition(rock.X + offset.X, rock.Y + offset.Y, rock.Z)
             let route = engine.Navigation.FindPath(actor.Position, access)
             where route is not null
             orderby route.Count
             select (Rock: rock, Access: access, Cell: cell)).First();

        engine.QueueCommand(SimulationCommand.Move(
            new SimulationTick(1), sequence: 1, actor.Id, vein.Access));
        for (var tick = 0; tick < 20_000 &&
             engine.CreateSnapshot().Actors.Single().Position != vein.Access; tick++)
        {
            engine.AdvanceTicks(1);
        }
        Assert.Equal(vein.Access, engine.CreateSnapshot().Actors.Single().Position);

        var exploredSave = JsonNode.Parse(engine.Save())!.AsObject();
        var fresh = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 40);
        var freshSave = JsonNode.Parse(fresh.Save())!.AsObject();
        freshSave["visibility"] = exploredSave["visibility"]!.DeepClone();
        engine = SimulationEngine.Load(freshSave.ToJsonString(), SimulationDefinitions.Foundation);
        actor = engine.CreateSnapshot().Actors.Single();
        Assert.Equal(map.GoblinSpawn, actor.Position);

        engine.QueueCommand(SimulationCommand.DesignateRockMining(
            engine.CurrentTick.Next(), sequence: 1, vein.Rock, vein.Rock));
        engine.AdvanceTicks(1);
        Assert.Contains(engine.CreateSnapshot().WorkDesignations, designation =>
            designation.Kind == WorkDesignationKind.MineRock && designation.Target == vein.Rock);
        for (var tick = 0; tick < 8_000 &&
             !engine.World.ExcavatedCaveCells.Contains(vein.Rock); tick++)
        {
            engine.AdvanceTicks(1);
        }
        Assert.Contains(vein.Rock, engine.World.ExcavatedCaveCells);

        var stacks = engine.CreateSnapshot().ItemStacks.Where(stack =>
            stack.Location.Kind == ItemLocationKind.Ground &&
            stack.Location.Position == vein.Rock).ToArray();
        Assert.Contains(stacks, stack => stack.Resource == ResourceKind.Stone);
        var mineral = Assert.Single(stacks, stack => stack.Resource is ResourceKind.Coal or ResourceKind.Ore);
        if (vein.Cell.Deposit == MineralDepositKind.Coal)
        {
            Assert.Equal(ResourceKind.Coal, mineral.Resource);
            Assert.Equal(ResourceVariant.None, mineral.Variant);
        }
        else
        {
            Assert.Equal(ResourceKind.Ore, mineral.Resource);
            Assert.Equal(ResourceVariant.IronOre, mineral.Variant);
        }

        var extractedQuantity = stacks.Sum(stack => stack.Quantity);
        var surfaceStoragePosition =
            (from passage in map.VerticalPassages
             where passage.Upper.Z == 0
             from position in map.GetCardinalNeighbors(passage.Upper)
             where engine.World.IsSurfaceTraversable(position)
             let route = engine.Navigation.FindPath(vein.Rock, position)
             where route is not null
             orderby route.Count
             select position).First();
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            engine.CurrentTick.Next(),
            sequence: 2,
            surfaceStoragePosition,
            ResourceKind.Stone,
            capacity: 64));
        engine.AdvanceTicks(1);
        var storage = Assert.Single(engine.CreateSnapshot().StorageZones);
        engine.QueueCommand(SimulationCommand.DesignateWork(
            engine.CurrentTick.Next(),
            sequence: 3,
            vein.Rock,
            vein.Rock,
            ResourceKind.Stone));
        for (var tick = 0; tick < 12_000 &&
             engine.CreateSnapshot().StorageZones.Single(zone => zone.Id == storage.Id)
                 .StoredQuantity < extractedQuantity; tick++)
        {
            engine.AdvanceTicks(1);
        }

        var delivered = engine.CreateSnapshot().ItemStacks.Where(stack =>
            stack.Location.Kind == ItemLocationKind.StorageZone &&
            stack.Location.OwnerId == storage.Id).ToArray();
        Assert.Equal(extractedQuantity, delivered.Sum(stack => stack.Quantity));
        Assert.Contains(delivered, stack => stack.Resource == ResourceKind.Stone);
        Assert.Contains(delivered, stack => stack.Resource == mineral.Resource);
        Assert.DoesNotContain(engine.CreateSnapshot().WorkDesignations, designation =>
            designation.Kind == WorkDesignationKind.GatherStone &&
            designation.Target == vein.Rock);

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Contains(restored.CreateSnapshot().ItemStacks, stack =>
            stack.Id == mineral.Id && stack.Resource == mineral.Resource &&
            stack.Variant == mineral.Variant && stack.Quantity == mineral.Quantity &&
            stack.Location.Kind == ItemLocationKind.StorageZone);
    }

    [Fact]
    public void UndergroundWallOrderPullsWorkerAndWoodThroughTheCaveEntrance()
    {
        var seed = new WorldSeed(0x4341564557414C4CUL);
        var map = SwampMapGenerator.Generate(seed, width: 48, height: 48);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 20,
            initialWoodStock: 2);
        var actor = engine.CreateSnapshot().Actors.Single();
        var position =
            (from level in Enumerable.Range(1, map.CaveLevelCount)
             from y in Enumerable.Range(0, map.Height)
             from x in Enumerable.Range(0, map.Width)
             let candidate = new GridPosition(x, y, -level)
             let route = engine.Navigation.FindPath(actor.Position, candidate)
             where route is not null &&
                   route.Count > 0 &&
                   engine.World.CanBuildWoodenBarrier(candidate)
             orderby route.Count
             select candidate).First();

        engine.QueueCommand(SimulationCommand.BuildWoodenWall(
            new SimulationTick(1), sequence: 1, position));
        for (var tick = 0; tick < 12_000 &&
             !engine.World.GetWorldObjectsAt(position).Any(worldObject =>
                 worldObject.Kind == WorldObjectKind.WoodenWall); tick++)
        {
            engine.AdvanceTicks(1);
        }

        Assert.Contains(engine.World.GetWorldObjectsAt(position), worldObject =>
            worldObject.Kind == WorldObjectKind.WoodenWall);
        Assert.False(engine.World.IsTerrainTraversable(position));
        Assert.Contains(engine.CreateSnapshot().Actors, goblin => goblin.Position.Z < 0);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.False(restored.World.IsTerrainTraversable(position));
    }

    private static GridPosition FindImpassableCell(GeneratedMap map)
    {
        for (var y = map.Height - 1; y >= 0; y--)
        {
            for (var x = map.Width - 1; x >= 0; x--)
            {
                var position = new GridPosition(x, y);
                if (!map.GetCell(position).IsTraversable)
                {
                    return position;
                }
            }
        }

        throw new InvalidOperationException("The generated swamp has no impassable cell.");
    }

    private static (GridPosition Start, GridPosition Destination) FindTraversableSquare(
        WorldMapState world)
    {
        for (var y = 0; y < world.Baseline.Height - 1; y++)
        {
            for (var x = 0; x < world.Baseline.Width - 1; x++)
            {
                var upperLeft = new GridPosition(x, y);
                var upperRight = new GridPosition(x + 1, y);
                var lowerLeft = new GridPosition(x, y + 1);
                var lowerRight = new GridPosition(x + 1, y + 1);
                if (world.CanTraverseTerrainEdge(upperLeft, upperRight, canOpenDoors: true) &&
                    world.CanTraverseTerrainEdge(upperLeft, lowerLeft, canOpenDoors: true) &&
                    world.CanTraverseTerrainEdge(lowerLeft, lowerRight, canOpenDoors: true) &&
                    world.CanTraverseTerrainEdge(lowerRight, upperRight, canOpenDoors: true))
                {
                    return (upperLeft, upperRight);
                }
            }
        }

        throw new InvalidOperationException("Generated map has no traversable square.");
    }

    private static SimulationEngine CreateEngine()
    {
        var seed = new WorldSeed(0x4D4F5645UL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        return SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 8);
    }
}
