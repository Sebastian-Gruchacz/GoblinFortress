using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Map.Generation;
using GoblinStronghold.Simulation.Map.Hydrology;
using GoblinStronghold.Simulation.Resources;
using System.Text.Json.Nodes;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class WorldMapStateTests
{
    [Fact]
    public void CurrentRiverSpillsDownIntoAdjacentSurfaceBasin()
    {
        var seed = new WorldSeed(4_718_262_470_390_704_995UL);
        var map = SwampMapGenerator.Generate(
            LocationGenerationRequest.CreateDefault(seed, 128, 128) with
            {
                RoadMode = RoadGenerationMode.ThroughRoad,
            });
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 8);
        var source = new GridPosition(15, 98, 0);
        var basin = new GridPosition(16, 98, -1);

        Assert.Equal(TerrainKind.DeepWater, map.GetColumnCell(source).Terrain);
        Assert.True(map.IsTerrainSurfacePosition(basin));
        Assert.True(engine.World.TryGetFluid(basin, out var fluid, out var depthLevels));
        Assert.Equal(CellFluidKind.Water, fluid);
        Assert.Equal(1, depthLevels);
        Assert.Contains(basin, engine.World.ConnectedWaterCells);
    }

    [Fact]
    public void InitialNaturalContentRespectsSurfaceSubstrateAndFlooding()
    {
        var seed = new WorldSeed(4_718_262_470_390_704_995UL);
        var map = SwampMapGenerator.Generate(
            LocationGenerationRequest.CreateDefault(seed, 128, 128) with
            {
                RoadMode = RoadGenerationMode.ThroughRoad,
            });
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 8);

        Assert.DoesNotContain(engine.World.CreatePlantSnapshot(), patch =>
            patch.Kind == PlantKind.MushroomCluster &&
            map.GetColumnCell(patch.Position).Terrain == TerrainKind.Sand);
        Assert.DoesNotContain(engine.World.CreatePlantSnapshot(), patch =>
            engine.World.ConnectedWaterCells.Contains(patch.Position));
        Assert.All(
            engine.World.CreateWorldObjectSnapshot().Where(worldObject =>
                worldObject.Owner == WorldObjectOwner.Nature &&
                (worldObject.Kind is WorldObjectKind.Tree or
                    WorldObjectKind.DeadTreeStump or WorldObjectKind.Boulder)),
            worldObject =>
            {
                Assert.True(map.IsTerrainSurfacePosition(worldObject.Anchor));
                Assert.DoesNotContain(worldObject.Anchor, engine.World.ConnectedWaterCells);
            });
        foreach (var position in Enumerable.Range(0, map.Height)
                     .SelectMany(y => Enumerable.Range(0, map.Width)
                         .Select(x => map.GetTerrainSurfacePosition(new GridPosition(x, y))))
                     .Where(position => position.Z < 0))
        {
            Assert.False(engine.World.TryGetCaveFlora(position, out _));
        }
    }

    [Fact]
    public void BreachingLegacyRiverBedFloodsConnectedLevelAndSurvivesLoad()
    {
        SimulationEngine? engine = null;
        GridPosition breach = default;
        for (ulong seedValue = 1; seedValue <= 128 && engine is null; seedValue++)
        {
            var seed = new WorldSeed(seedValue);
            var candidate = SimulationEngine.Create(
                seed,
                SimulationDefinitions.Foundation,
                SwampMapGenerator.Generate(seed, 64, 64, generatorVersion: 17),
                initialGoblinCount: 1,
                initialFoodStock: 8);
            for (var y = 1; y < candidate.Map.Height - 1 && engine is null; y++)
            {
                for (var x = 1; x < candidate.Map.Width - 1; x++)
                {
                    var column = candidate.Map.GetColumnCell(new GridPosition(x, y));
                    var target = new GridPosition(x, y, column.FloorLevel);
                    if (column is { Terrain: TerrainKind.DeepWater, FloorLevel: -1 } &&
                        candidate.World.CanExcavateRock(target))
                    {
                        engine = candidate;
                        breach = target;
                        break;
                    }
                }
            }
        }

        var floodedEngine = engine ?? throw new InvalidOperationException(
            "The deterministic generator samples contained no reachable legacy river bed.");
        Assert.False(floodedEngine.World.TryGetFluid(breach, out _, out _));

        Assert.True(floodedEngine.World.TryExcavateRock(
            breach,
            floodedEngine.CurrentTick,
            out _,
            out _,
            out _));

        Assert.True(floodedEngine.World.TryGetFluid(
            breach,
            out var fluid,
            out var depthLevels));
        Assert.Equal(CellFluidKind.Water, fluid);
        Assert.Equal(1, depthLevels);
        Assert.Contains(breach, floodedEngine.World.ConnectedWaterCells);
        Assert.Contains(
            floodedEngine.World.GetCardinalWorldNeighbors(breach),
            neighbor => floodedEngine.World.TryGetFluid(neighbor, out var neighborFluid, out _) &&
                neighborFluid == CellFluidKind.Water);
        var escape = FloodEscapePolicy.FindNearestDryPosition(
            floodedEngine.World,
            breach);
        Assert.NotNull(escape);
        Assert.True(floodedEngine.World.IsTerrainTraversable(escape.Value));

        var legacyRestored = WorldMapState.Restore(
            floodedEngine.Map,
            floodedEngine.World.Version,
            floodedEngine.World.CreatePlantSnapshot(),
            floodedEngine.World.CreateWorldObjectSnapshot(),
            floodedEngine.World.ExcavatedCaveCells,
            floodedEngine.World.ExcavatedTerrainRamps,
            floodedEngine.World.ExcavatedVerticalPassages,
            floodedEngine.World.HarvestedCaveFlora);
        Assert.False(legacyRestored.ConnectedWaterActivated);
        Assert.False(legacyRestored.TryGetFluid(breach, out _, out _));

        var restored = WorldMapState.Restore(
            floodedEngine.Map,
            floodedEngine.World.Version,
            floodedEngine.World.CreatePlantSnapshot(),
            floodedEngine.World.CreateWorldObjectSnapshot(),
            floodedEngine.World.ExcavatedCaveCells,
            floodedEngine.World.ExcavatedTerrainRamps,
            floodedEngine.World.ExcavatedVerticalPassages,
            floodedEngine.World.HarvestedCaveFlora,
            connectedWaterActivated: true);
        Assert.True(restored.TryGetFluid(breach, out fluid, out depthLevels));
        Assert.Equal(CellFluidKind.Water, fluid);
        Assert.Equal(1, depthLevels);
        Assert.Equal(
            floodedEngine.World.ConnectedWaterCells
                .OrderBy(position => position.Z)
                .ThenBy(position => position.Y)
                .ThenBy(position => position.X),
            restored.ConnectedWaterCells
                .OrderBy(position => position.Z)
                .ThenBy(position => position.Y)
                .ThenBy(position => position.X));
    }

    [Fact]
    public void SharedNearestDestinationTreeServesSeveralStartsFromOneSearch()
    {
        var seed = new WorldSeed(0x7AEEUL);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            SwampMapGenerator.Generate(seed, 64, 64),
            initialGoblinCount: 1,
            initialFoodStock: 8);
        var destination = engine.Map.GoblinSpawn;
        var starts = engine.World.GetCardinalWorldNeighbors(destination)
            .Where(engine.World.IsTerrainTraversable)
            .Take(2)
            .ToArray();
        Assert.Equal(2, starts.Length);
        IReadOnlySet<GridPosition> destinations = new HashSet<GridPosition> { destination };

        foreach (var start in starts)
        {
            NavigationPathRequestResult request;
            do
            {
                request = engine.Navigation.RequestSharedPathToNearest(
                    start,
                    destinations,
                    maximumExpandedNodes: 1);
            }
            while (request.Status == NavigationPathRequestStatus.Pending);

            Assert.Equal(NavigationPathRequestStatus.Complete, request.Status);
            Assert.NotNull(request.Path);
            Assert.Equal(destination, request.Path[^1]);
        }

        var metrics = engine.Navigation.GetMetrics();
        Assert.Equal(1, metrics.Searches);
        Assert.True(metrics.CacheHits > 0);
    }

    [Fact]
    public void AdjacentWalkableFloorsOnSameLevelConnectBothWays()
    {
        var seed = new WorldSeed(1);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            SwampMapGenerator.Generate(seed, 64, 64),
            initialGoblinCount: 1,
            initialFoodStock: 8);
        var surface = new GridPosition(26, 30, -1);
        var cave = new GridPosition(26, 29, -1);

        Assert.True(engine.Map.IsTerrainSurfacePosition(surface));
        Assert.True(engine.World.IsTerrainTraversable(surface));
        Assert.True(engine.World.IsSolidCaveRock(cave));
        Assert.True(engine.World.TryExcavateRock(
            cave,
            engine.CurrentTick,
            out _,
            out _,
            out _));
        Assert.True(engine.World.IsTerrainTraversable(cave));
        Assert.True(engine.World.CanTraverseTerrainEdge(surface, cave));
        Assert.True(engine.World.CanTraverseTerrainEdge(cave, surface));
    }

    [Fact]
    public void ExcavatedTerrainRampStopsConnectingItsLevelsAndSurvivesSaveLoad()
    {
        SimulationEngine? engine = null;
        GridPosition ramp = default;
        GridPosition uphill = default;
        for (ulong seedValue = 1; seedValue <= 64 && engine is null; seedValue++)
        {
            var seed = new WorldSeed(seedValue);
            var candidate = SimulationEngine.Create(
                seed,
                SimulationDefinitions.Foundation,
                SwampMapGenerator.Generate(seed, 64, 64),
                initialGoblinCount: 1,
                initialFoodStock: 8);
            for (var y = 1; y < candidate.Map.Height - 1 && engine is null; y++)
            {
                for (var x = 1; x < candidate.Map.Width - 1; x++)
                {
                    var cell = candidate.Map.GetColumnCell(new GridPosition(x, y));
                    if (cell.RampDirection == TerrainRampDirection.None)
                    {
                        continue;
                    }

                    var position = new GridPosition(x, y, cell.SurfaceLevel);
                    var destination = GetUphillNeighbor(position, cell.RampDirection) with
                    {
                        Z = cell.SurfaceLevel + 1,
                    };
                    if (candidate.Visibility.Get(position) != CellVisibility.Unknown &&
                        candidate.World.CanExcavateRock(position) &&
                        candidate.World.CanTraverseTerrainEdge(position, destination))
                    {
                        engine = candidate;
                        ramp = position;
                        uphill = destination;
                        break;
                    }
                }
            }
        }

        var rampEngine = engine ?? throw new InvalidOperationException(
            "The deterministic generator sample did not contain an excavatable terrain ramp.");
        var topologyVersion = rampEngine.World.TopologyVersion;
        Assert.Contains(ramp, rampEngine.QueryWorkDesignationTargets(
            WorkDesignationKind.MineRock, ramp, ramp));
        rampEngine.ApplyCommandImmediately(SimulationCommand.DesignateRockMining(
            rampEngine.CurrentTick,
            sequence: 1,
            ramp,
            ramp));
        rampEngine.AdvanceTicks(
            SimulationDefinitions.Foundation.ActorPlanning.BackgroundPlanningIntervalTicks);

        Assert.Contains(rampEngine.CreateSnapshot().WorkDesignations, designation =>
            designation.Kind == WorkDesignationKind.MineRock && designation.Target == ramp);
        var designatedRampEngine = SimulationEngine.Load(
            rampEngine.Save(),
            SimulationDefinitions.Foundation);
        Assert.Contains(designatedRampEngine.CreateSnapshot().WorkDesignations, designation =>
            designation.Kind == WorkDesignationKind.MineRock && designation.Target == ramp);
        Assert.True(rampEngine.World.TryExcavateRock(
            ramp,
            rampEngine.CurrentTick,
            out _,
            out var deposit,
            out _));

        Assert.Equal(MineralDepositKind.None, deposit);
        Assert.Contains(ramp, rampEngine.World.ExcavatedTerrainRamps);
        Assert.True(rampEngine.World.IsTerrainTraversable(ramp));
        Assert.False(rampEngine.World.IsTerrainRampIntact(ramp));
        Assert.False(rampEngine.World.CanTraverseTerrainEdge(ramp, uphill));
        Assert.False(rampEngine.World.CanTraverseTerrainEdge(uphill, ramp));
        Assert.True(rampEngine.World.TopologyVersion > topologyVersion);
        rampEngine.AdvanceTicks(
            SimulationDefinitions.Foundation.ActorPlanning.BackgroundPlanningIntervalTicks);
        Assert.DoesNotContain(rampEngine.CreateSnapshot().WorkDesignations, designation =>
            designation.Kind == WorkDesignationKind.MineRock && designation.Target == ramp);

        var restored = SimulationEngine.Load(
            rampEngine.Save(),
            SimulationDefinitions.Foundation);
        Assert.Contains(ramp, restored.World.ExcavatedTerrainRamps);
        Assert.False(restored.World.CanTraverseTerrainEdge(ramp, uphill));
        Assert.Equal(rampEngine.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void ElevatedWalkwayBridgesUnsupportedOpenCell()
    {
        SimulationEngine? engine = null;
        (GridPosition Left, GridPosition Gap, GridPosition Right)? crossing = null;
        for (ulong seedValue = 1; seedValue <= 64 && crossing is null; seedValue++)
        {
            var seed = new WorldSeed(seedValue);
            var candidateEngine = SimulationEngine.Create(
                seed,
                SimulationDefinitions.Foundation,
                SwampMapGenerator.Generate(seed, 64, 64),
                initialGoblinCount: 1,
                initialFoodStock: 8,
                initialWoodStock: 4);
            var actor = Assert.Single(candidateEngine.CreateSnapshot().Actors);
            for (var z = candidateEngine.Map.MinimumWorldLevel;
                 z <= candidateEngine.Map.MaximumWorldLevel && crossing is null;
                 z++)
            {
                for (var y = 1; y < candidateEngine.Map.Height - 1 && crossing is null; y++)
                {
                    for (var x = 1; x < candidateEngine.Map.Width - 1; x++)
                    {
                        var gap = new GridPosition(x, y, z);
                        if (!candidateEngine.Map.TryGetInitialGeometry(gap, out var geometry) ||
                            geometry.IsSolid || geometry.IsSupported ||
                            geometry.Fluid != CellFluidKind.None ||
                            !candidateEngine.World.CanBuildWalkway([gap]))
                        {
                            continue;
                        }

                        foreach (var pair in new[]
                                 {
                                     (Left: gap with { X = x - 1 },
                                      Right: gap with { X = x + 1 }),
                                     (Left: gap with { Y = y - 1 },
                                      Right: gap with { Y = y + 1 }),
                                 })
                        {
                            if (candidateEngine.World.IsTerrainTraversable(pair.Left) &&
                                candidateEngine.World.IsTerrainTraversable(pair.Right) &&
                                candidateEngine.Navigation.FindPath(actor.Position, pair.Left) is not null)
                            {
                                engine = candidateEngine;
                                crossing = (pair.Left, gap, pair.Right);
                                break;
                            }
                        }
                    }
                }
            }
        }

        var bridge = crossing ?? throw new InvalidOperationException(
            "The deterministic generator sample did not contain a reachable ravine crossing.");
        var bridgeEngine = engine ?? throw new InvalidOperationException(
            "The crossing has no owning simulation.");
        Assert.False(bridgeEngine.World.IsTerrainTraversable(bridge.Gap));
        Assert.True(bridgeEngine.World.CanBuildWalkway(
            [bridge.Left, bridge.Gap, bridge.Right]));
        bridgeEngine.QueueCommand(SimulationCommand.BuildWalkway(
            bridgeEngine.CurrentTick.Next(),
            bridgeEngine.NextAvailableCommandSequence,
            bridge.Left,
            bridge.Right));
        bridgeEngine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(bridgeEngine);

        Assert.True(bridgeEngine.World.IsTerrainTraversable(bridge.Gap));
        Assert.True(bridgeEngine.World.CanTraverseTerrainEdge(bridge.Left, bridge.Gap));
        Assert.True(bridgeEngine.World.CanTraverseTerrainEdge(bridge.Gap, bridge.Right));
    }

    private static GridPosition GetUphillNeighbor(
        GridPosition position,
        TerrainRampDirection direction) => direction switch
    {
        TerrainRampDirection.North => position with { Y = position.Y - 1 },
        TerrainRampDirection.East => position with { X = position.X + 1 },
        TerrainRampDirection.South => position with { Y = position.Y + 1 },
        TerrainRampDirection.West => position with { X = position.X - 1 },
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    [Fact]
    public void InitialEcologyContainsDistinctDeterministicFoodSources()
    {
        var seed = new WorldSeed(0x474F424C494EUL);
        var first = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            SwampMapGenerator.Generate(seed, 64, 64),
            initialGoblinCount: 1,
            initialFoodStock: 0);
        var second = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            SwampMapGenerator.Generate(seed, 64, 64),
            initialGoblinCount: 1,
            initialFoodStock: 0);

        var firstSources = first.World.CreatePlantSnapshot();
        Assert.Equal(firstSources, second.World.CreatePlantSnapshot());
        Assert.Contains(firstSources, source => source.Kind == PlantKind.BerryBush);
        Assert.Contains(firstSources, source => source.Kind == PlantKind.MushroomCluster);
        Assert.Contains(firstSources, source => source.Kind == PlantKind.EdibleRoots);
        Assert.Contains(firstSources, source => source.Kind == PlantKind.FishShoal);
        Assert.Contains(firstSources, source => source.Kind == PlantKind.ReedBed);
        Assert.All(firstSources, source =>
        {
            Assert.True(first.Map.IsTerrainSurfacePosition(source.Position));
            Assert.Equal(
                TerrainRampDirection.None,
                first.Map.GetColumnCell(source.Position).RampDirection);
            Assert.Equal(source, first.World.GetPlantPatch(source.Position));
            if (source.Position.Z != 0)
            {
                Assert.Null(first.World.GetPlantPatch(source.Position with { Z = 0 }));
            }
        });
    }

    [Fact]
    public void RegionalMapPlacesForestNearVillageAndDeadwoodInSwamp()
    {
        var seed = new WorldSeed(0x464F52455354UL);
        var map = SwampMapGenerator.Generate(seed, 64, 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 0);

        var objects = engine.World.CreateWorldObjectSnapshot();
        var trees = objects.Where(item => item.Kind == WorldObjectKind.Tree).ToArray();
        var stumps = objects.Where(item => item.Kind == WorldObjectKind.DeadTreeStump).ToArray();

        Assert.NotEmpty(trees);
        Assert.Contains(trees, tree => map.GetColumnCell(tree.Anchor).SurfaceLevel > 0);
        Assert.Contains(trees, tree => tree.Parts.Count(part =>
            part.Kind == WorldObjectPartKind.TreeTrunk) > 1);
        Assert.All(trees, tree =>
        {
            Assert.Equal(WorldObjectOwner.Nature, tree.Owner);
            var trunkParts = tree.Parts
                .Where(part => part.Kind == WorldObjectPartKind.TreeTrunk)
                .OrderBy(part => part.RelativePosition.Z)
                .ToArray();
            Assert.InRange(trunkParts.Length, 1, 3);
            Assert.Equal(Enumerable.Range(0, trunkParts.Length),
                trunkParts.Select(part => part.RelativePosition.Z));
            Assert.Equal(9, tree.Parts.Count(part => part.Kind == WorldObjectPartKind.TreeCrown));
            Assert.All(tree.Parts.Where(part => part.Kind == WorldObjectPartKind.TreeCrown),
                part => Assert.Equal(trunkParts.Length, part.RelativePosition.Z));
            Assert.True(tree.Anchor.X >= map.Width * 0.42);
            Assert.True(tree.Anchor.Y <= map.Height * 0.62);
            Assert.False(engine.World.IsSurfaceTraversable(tree.Anchor));
            Assert.False(engine.World.IsTerrainTraversable(
                map.GetTerrainSurfacePosition(tree.Anchor)));
        });
        Assert.NotEmpty(stumps);
        Assert.All(stumps, stump =>
        {
            Assert.Equal(TerrainKind.Mud, map.GetColumnCell(stump.Anchor).Terrain);
            Assert.True(stump.Anchor.X <= map.Width * 0.42 || stump.Anchor.Y >= map.Height * 0.64);
            Assert.False(engine.World.IsSurfaceTraversable(stump.Anchor));
            Assert.False(engine.World.IsTerrainTraversable(
                map.GetTerrainSurfacePosition(stump.Anchor)));
        });
        var expectedHighestLevel = objects
            .SelectMany(worldObject =>
            {
                var surfaceOffset = worldObject.Anchor.Z == 0 &&
                    (worldObject.Kind is WorldObjectKind.Tree or
                        WorldObjectKind.DeadTreeStump or WorldObjectKind.Boulder)
                    ? map.GetColumnCell(worldObject.Anchor).SurfaceLevel
                    : 0;
                return worldObject.Parts.Select(part =>
                    worldObject.Anchor.Z + surfaceOffset + part.RelativePosition.Z);
            })
            .Append(map.MaximumWorldLevel)
            .Max();
        Assert.Equal(expectedHighestLevel, engine.World.MaximumOccupiedLevel);
        var actor = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.Null(engine.Navigation.FindPath(
            actor.Position,
            map.GetTerrainSurfacePosition(trees[0].Anchor)));
        Assert.Null(engine.Navigation.FindPath(
            actor.Position,
            map.GetTerrainSurfacePosition(stumps[0].Anchor)));
    }

    [Fact]
    public void FreshRegionalMapContainsDeterministicLooseStoneAndBlockingBoulders()
    {
        var seed = new WorldSeed(0x53544F4EUL);
        var map = SwampMapGenerator.Generate(seed, 64, 64);
        var first = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 2,
            initialFoodStock: 0,
            scatterInitialBrushwood: true);
        var second = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            SwampMapGenerator.Generate(seed, 64, 64),
            initialGoblinCount: 2,
            initialFoodStock: 0,
            scatterInitialBrushwood: true);

        var boulders = first.World.CreateWorldObjectSnapshot()
            .Where(item => item.Kind == WorldObjectKind.Boulder)
            .ToArray();
        Assert.NotEmpty(boulders);
        Assert.Equal(
            boulders.Select(item => item.Anchor),
            second.World.CreateWorldObjectSnapshot()
                .Where(item => item.Kind == WorldObjectKind.Boulder)
                .Select(item => item.Anchor));
        Assert.All(boulders, boulder =>
        {
            Assert.Equal(WorldObjectOwner.Nature, boulder.Owner);
            Assert.False(first.World.IsSurfaceTraversable(boulder.Anchor));
            Assert.False(first.World.IsTerrainTraversable(
                map.GetTerrainSurfacePosition(boulder.Anchor)));
            Assert.Contains(boulder.Parts, part => part.Kind == WorldObjectPartKind.Boulder);
        });
        var actor = first.CreateSnapshot().Actors[0];
        Assert.Null(first.Navigation.FindPath(
            actor.Position,
            map.GetTerrainSurfacePosition(boulders[0].Anchor)));

        var looseStone = first.CreateSnapshot().ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Stone &&
                stack.Location.Kind == ItemLocationKind.Ground)
            .ToArray();
        Assert.NotEmpty(looseStone);
        Assert.All(looseStone, stack =>
        {
            Assert.True(map.IsTerrainSurfacePosition(stack.Location.Position));
            Assert.Equal(
                TerrainRampDirection.None,
                map.GetColumnCell(stack.Location.Position).RampDirection);
            Assert.True(first.World.IsTerrainTraversable(stack.Location.Position));
        });
        Assert.Contains(looseStone, stack =>
            Math.Abs(stack.Location.Position.X - map.GoblinSpawn.X) +
            Math.Abs(stack.Location.Position.Y - map.GoblinSpawn.Y) <=
            SimulationDefinitions.Foundation.VisionRadius);
        Assert.Equal(
            looseStone,
            second.CreateSnapshot().ItemStacks.Where(stack =>
                stack.Resource == ResourceKind.Stone &&
                stack.Location.Kind == ItemLocationKind.Ground));
    }

    [Fact]
    public void FishShoalsOnlyOccupyShallowsInLargerConnectedWaterBodies()
    {
        var seed = new WorldSeed(0x474F424C494EUL);
        var map = SwampMapGenerator.Generate(seed, 64, 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 0);

        var shoals = engine.World.CreatePlantSnapshot()
            .Where(source => source.Kind == PlantKind.FishShoal)
            .ToArray();
        Assert.NotEmpty(shoals);
        foreach (var shoal in shoals)
        {
            Assert.Equal(TerrainKind.ShallowWater, map.GetCell(shoal.Position).Terrain);
            Assert.True(MeasureWaterBody(map, shoal.Position) >= 12);
        }
    }

    [Fact]
    public void FishShoalRegrowsFasterBesideADeepRiverChannel()
    {
        var seed = new WorldSeed(0x4649534852495645UL);
        var map = SwampMapGenerator.Generate(seed, 64, 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 0);
        var shoal = engine.World.CreatePlantSnapshot()
            .Where(patch => patch.Kind == PlantKind.FishShoal)
            .First(patch =>
            {
                var neighbors = map.GetCardinalNeighbors(patch.Position).ToArray();
                return neighbors.Any(position =>
                           map.GetCell(position).Terrain == TerrainKind.DeepWater) &&
                    neighbors.Count(position => map.GetCell(position).Terrain is
                        TerrainKind.ShallowWater or TerrainKind.DeepWater) >= 3;
            });
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var savedShoal = save["plantPatches"]!.AsArray().Single(node =>
            node!["x"]!.GetValue<int>() == shoal.Position.X &&
            node["y"]!.GetValue<int>() == shoal.Position.Y &&
            node["z"]!.GetValue<int>() == shoal.Position.Z)!.AsObject();
        savedShoal["biomass"] = 0;
        save["currentTick"] = engine.Definitions.PlantGrowthIntervalTicks - 1L;
        engine = SimulationEngine.Load(save.ToJsonString(), engine.Definitions);

        engine.AdvanceTicks(1);

        var change = Assert.Single(engine.DrainWorldChanges(),
            item => item.Position == shoal.Position &&
                item.Kind == WorldChangeKind.VegetationRegrown);

        Assert.Equal(3, change.Amount);
        Assert.Equal(3, engine.World.GetPlantPatch(shoal.Position)!.Value.Biomass);
    }

    [Fact]
    public void FreshSandboxEcologyContainsADeepEmergencyForagingReserve()
    {
        var seed = new WorldSeed(0x474F424C494EUL);
        var map = SwampMapGenerator.Generate(seed, 64, 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 8,
            initialFoodStock: 16,
            scatterInitialBrushwood: true);

        var snapshot = engine.CreateSnapshot();
        Assert.Equal(8, snapshot.Actors.Count);
        var looseFood = snapshot.ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Food)
            .Sum(stack => stack.Quantity);
        var wildFood = snapshot.PlantPatches.Sum(patch => patch.Biomass);
        Assert.True(looseFood + wildFood >= snapshot.Actors.Count * 6);
    }

    [Fact]
    public void ForagingDepletesLocalVegetationAndPublishesDirtyCell()
    {
        var engine = CreateEngine();
        var position = engine.Map.GoblinSpawn;
        var before = engine.World.GetPlantPatch(position)!.Value;
        var topologyVersion = engine.World.TopologyVersion;

        engine.QueueCommand(SimulationCommand.Forage(
            new SimulationTick(1),
            sequence: 1,
            new EntityId(1)));
        engine.AdvanceTicks(1);

        var after = engine.World.GetPlantPatch(position)!.Value;
        var change = Assert.Single(engine.DrainWorldChanges());
        var gathered = Assert.Single(
            engine.DrainEvents().Where(item => item.Kind == SimulationEventKind.FoodGathered));

        Assert.True(after.Biomass < before.Biomass);
        Assert.Equal(before.Biomass - after.Biomass, gathered.Amount);
        Assert.Equal(WorldChangeKind.VegetationHarvested, change.Kind);
        Assert.Equal(position, change.Position);
        Assert.Equal(-gathered.Amount, change.Amount);
        Assert.Equal(engine.World.Version, change.Version);
        Assert.Equal(topologyVersion, engine.World.TopologyVersion);
    }

    [Fact]
    public void DepletedVegetationRejectsFurtherForagingUntilItRegrows()
    {
        var engine = CreateEngine();
        var position = engine.Map.GoblinSpawn;
        var capacity = engine.World.GetPlantPatch(position)!.Value.Capacity;

        for (var tick = 1; tick <= capacity; tick++)
        {
            engine.QueueCommand(SimulationCommand.Forage(
                new SimulationTick(1),
                sequence: (ulong)tick,
                new EntityId(1)));
        }

        engine.AdvanceTicks(1);
        Assert.Equal(0, engine.World.GetPlantPatch(position)!.Value.Biomass);

        var rejectionTick = 2;
        engine.QueueCommand(SimulationCommand.Forage(
            new SimulationTick(rejectionTick),
            sequence: (ulong)rejectionTick,
            new EntityId(1)));
        engine.AdvanceTicks(1);

        Assert.Contains(
            engine.DrainEvents(),
            item => item.Kind == SimulationEventKind.CommandRejected);
    }

    [Fact]
    public void HarvestedSeasonalFoodDoesNotRegrowAtOrdinaryGrowthIntervals()
    {
        var engine = MoveToStartOfSummer(CreateEngine());
        var position = engine.Map.GoblinSpawn;

        engine.QueueCommand(SimulationCommand.Forage(
            engine.CurrentTick.Next(),
            sequence: 1,
            new EntityId(1)));
        engine.AdvanceTicks(1);
        var harvested = engine.World.GetPlantPatch(position)!.Value;
        engine.DrainEvents();
        engine.DrainWorldChanges();

        engine.AdvanceTicks(
            SimulationDefinitions.Foundation.PlantGrowthIntervalTicks - 1);

        Assert.DoesNotContain(
            engine.DrainWorldChanges(),
            item => item.Kind == WorldChangeKind.VegetationRegrown && item.Position == position);
        Assert.Equal(harvested.Biomass, engine.World.GetPlantPatch(position)!.Value.Biomass);
    }

    [Fact]
    public void HarvestedBerryBushRemainsBareUntilTheNextSummerStarts()
    {
        var engine = MoveToStartOfSummer(CreateEngine());
        var position = engine.Map.GoblinSpawn;
        var capacity = engine.World.GetPlantPatch(position)!.Value.Capacity;
        for (var sequence = 1; sequence <= capacity; sequence++)
        {
            engine.QueueCommand(SimulationCommand.Forage(
                engine.CurrentTick.Next(),
                (ulong)sequence,
                new EntityId(1)));
        }

        engine.AdvanceTicks(1);

        var bareBush = engine.World.GetPlantPatch(position);
        Assert.NotNull(bareBush);
        Assert.Equal(PlantKind.BerryBush, bareBush.Value.Kind);
        Assert.Equal(0, bareBush.Value.Biomass);

        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var nextSummer = checked(
            engine.Definitions.Clock.Climate.TicksPerYear +
            engine.Definitions.Clock.Climate.GetSeasonStartTick(SeasonKind.Summer));
        save["currentTick"] = nextSummer - 1;
        engine = SimulationEngine.Load(save.ToJsonString(), engine.Definitions);
        engine.AdvanceTicks(1);

        Assert.Equal(capacity, engine.World.GetPlantPatch(position)!.Value.Biomass);
        Assert.Contains(engine.DrainWorldChanges(), change =>
            change.Kind == WorldChangeKind.SeasonalFoodChanged &&
            change.Position == position &&
            change.Amount == capacity);
    }

    [Fact]
    public void WildBerriesSpoilWhenWinterBegins()
    {
        var engine = CreateEngine();
        var position = engine.Map.GoblinSpawn;
        Assert.True(engine.World.GetPlantPatch(position)!.Value.Biomass > 0);
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        save["currentTick"] =
            engine.Definitions.Clock.Climate.GetSeasonStartTick(SeasonKind.Winter) - 1;
        engine = SimulationEngine.Load(save.ToJsonString(), engine.Definitions);

        engine.AdvanceTicks(1);

        Assert.Equal(0, engine.World.GetPlantPatch(position)!.Value.Biomass);
        Assert.Contains(engine.DrainWorldChanges(), change =>
            change.Kind == WorldChangeKind.SeasonalFoodChanged &&
            change.Position == position &&
            change.Amount < 0);
    }

    [Fact]
    public void SaveLoadPreservesVegetationAndUndeliveredWorldChanges()
    {
        var engine = CreateEngine();
        engine.QueueCommand(SimulationCommand.Forage(
            new SimulationTick(1),
            sequence: 1,
            new EntityId(1)));
        engine.AdvanceTicks(1);

        var savedHash = engine.ComputeStateHash();
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);

        Assert.Equal(savedHash, restored.ComputeStateHash());
        Assert.Equal(engine.CreateSnapshot().PlantPatches, restored.CreateSnapshot().PlantPatches);
        Assert.Equal(engine.DrainWorldChanges(), restored.DrainWorldChanges());

        engine.AdvanceTicks(239);
        restored.AdvanceTicks(239);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void DrainingWorldChangesDoesNotChangeAuthoritativeHash()
    {
        var engine = CreateEngine();
        engine.QueueCommand(SimulationCommand.Forage(
            new SimulationTick(1),
            sequence: 1,
            new EntityId(1)));
        engine.AdvanceTicks(1);

        var beforeDrain = engine.ComputeStateHash();
        Assert.NotEmpty(engine.DrainWorldChanges());
        Assert.Equal(beforeDrain, engine.ComputeStateHash());
    }

    [Fact]
    public void MultiGoalNavigationFindsTheNearestShelterWithOneSearch()
    {
        var engine = CreateEngine();
        var actor = Assert.Single(engine.CreateSnapshot().Actors);
        var destinations = engine.World.CreateWorldObjectSnapshot()
            .Where(worldObject => worldObject.Kind is
                WorldObjectKind.GoblinHut or WorldObjectKind.GoblinRuin)
            .SelectMany(worldObject => worldObject.GetAbsoluteParts())
            .Where(item => item.Part.Kind is WorldObjectPartKind.Floor or WorldObjectPartKind.Door)
            .Select(item => item.Position)
            .Where(engine.World.IsTerrainTraversable)
            .ToHashSet();
        var expectedLength = destinations
            .Select(destination => engine.World.FindTerrainPath(
                actor.Position,
                destination,
                canOpenDoors: true)?.Count)
            .Where(length => length.HasValue)
            .Min();
        var before = engine.Navigation.GetMetrics();

        var route = engine.Navigation.FindPathToNearest(actor.Position, destinations);

        var after = engine.Navigation.GetMetrics();
        Assert.NotNull(route);
        Assert.Equal(expectedLength, route.Count);
        Assert.Contains(route.Count == 0 ? actor.Position : route[^1], destinations);
        Assert.Equal(before.Searches + 1, after.Searches);
        Assert.True(after.ExpandedNodes > before.ExpandedNodes);
    }

    [Fact]
    public void NearestHarvestablePlantQueryMatchesSnapshotOrderingWithoutMaterializingTheMap()
    {
        var engine = CreateEngine();
        var origin = Assert.Single(engine.CreateSnapshot().Actors).Position;
        var center = engine.Map.HumanVillage;
        const int radius = 12;
        var expected = engine.World.CreatePlantSnapshot()
            .Where(patch => patch.Kind == PlantKind.BerryBush && patch.Biomass > 0)
            .Where(patch => Math.Abs(patch.Position.X - center.X) +
                Math.Abs(patch.Position.Y - center.Y) +
                Math.Abs(patch.Position.Z - center.Z) <= radius)
            .OrderBy(patch => Math.Abs(patch.Position.X - origin.X) +
                Math.Abs(patch.Position.Y - origin.Y) +
                Math.Abs(patch.Position.Z - origin.Z))
            .ThenBy(patch => patch.Position.Y)
            .ThenBy(patch => patch.Position.X)
            .Select(patch => (GridPosition?)patch.Position)
            .FirstOrDefault();

        var actual = engine.World.FindNearestHarvestablePlantPosition(
            origin,
            center,
            radius,
            PlantKind.BerryBush);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BudgetedNavigationResumesOneDeterministicSearchAcrossRequests()
    {
        var engine = CreateEngine();
        var actor = Assert.Single(engine.CreateSnapshot().Actors);
        var candidate =
            (from y in Enumerable.Range(0, engine.Map.Height)
             from x in Enumerable.Range(0, engine.Map.Width)
             let position = engine.Map.GetTerrainSurfacePosition(new GridPosition(x, y))
             where engine.World.IsTerrainTraversable(position)
             orderby Math.Abs(position.X - actor.Position.X) +
                 Math.Abs(position.Y - actor.Position.Y) descending
             let route = engine.World.FindTerrainPath(
                 actor.Position,
                 position,
                 canOpenDoors: true)
             where route is { Count: > 4 }
             select new { Position = position, Route = route }).First();
        var before = engine.Navigation.GetMetrics();

        var request = engine.Navigation.RequestPath(
            actor.Position,
            candidate.Position,
            maximumExpandedNodes: 1);

        Assert.Equal(NavigationPathRequestStatus.Pending, request.Status);
        Assert.Equal(1, engine.Navigation.GetMetrics().PendingSearches);
        for (var slice = 0; slice < engine.Map.CellCount * engine.Map.LevelCount * 2 &&
             request.Status == NavigationPathRequestStatus.Pending; slice++)
        {
            request = engine.Navigation.RequestPath(
                actor.Position,
                candidate.Position,
                maximumExpandedNodes: 1);
        }

        var after = engine.Navigation.GetMetrics();
        Assert.Equal(NavigationPathRequestStatus.Complete, request.Status);
        Assert.Equal(candidate.Route, request.Path);
        Assert.Equal(before.Searches + 1, after.Searches);
        Assert.Equal(0, after.PendingSearches);
        Assert.True(after.ExpandedNodes > before.ExpandedNodes + 1);
    }

    [Fact]
    public void BudgetedNavigationToNearestResumesOneSearchAndCachesItsRoute()
    {
        var engine = CreateEngine();
        var actor = Assert.Single(engine.CreateSnapshot().Actors);
        var candidates =
            (from y in Enumerable.Range(0, engine.Map.Height)
             from x in Enumerable.Range(0, engine.Map.Width)
             let position = engine.Map.GetTerrainSurfacePosition(new GridPosition(x, y))
             where engine.World.IsTerrainTraversable(position)
             let distance = Math.Abs(position.X - actor.Position.X) +
                 Math.Abs(position.Y - actor.Position.Y)
             where distance > 8
             orderby distance descending, position.Y, position.X
             select position)
            .Take(8)
            .ToHashSet();
        Assert.NotEmpty(candidates);
        var expected = engine.Navigation.FindPathToNearest(actor.Position, candidates);
        Assert.NotNull(expected);
        Assert.True(expected.Count > 4);
        var before = engine.Navigation.GetMetrics();

        var request = engine.Navigation.RequestPathToNearest(
            actor.Position,
            candidates,
            maximumExpandedNodes: 1);

        Assert.Equal(NavigationPathRequestStatus.Pending, request.Status);
        Assert.Equal(1, engine.Navigation.GetMetrics().PendingSearches);
        for (var slice = 0; slice < engine.Map.CellCount * engine.Map.LevelCount * 2 &&
             request.Status == NavigationPathRequestStatus.Pending; slice++)
        {
            request = engine.Navigation.RequestPathToNearest(
                actor.Position,
                candidates,
                maximumExpandedNodes: 1);
        }

        var after = engine.Navigation.GetMetrics();
        Assert.Equal(NavigationPathRequestStatus.Complete, request.Status);
        Assert.Equal(expected.Count, request.Path!.Count);
        Assert.Contains(request.Path!.Count == 0 ? actor.Position : request.Path[^1], candidates);
        Assert.Equal(before.Searches + 1, after.Searches);
        Assert.Equal(0, after.PendingSearches);
        Assert.True(after.ExpandedNodes > before.ExpandedNodes + 1);

        var cached = engine.Navigation.RequestPathToNearest(
            actor.Position,
            candidates,
            maximumExpandedNodes: 1);
        var cachedMetrics = engine.Navigation.GetMetrics();
        Assert.Equal(NavigationPathRequestStatus.Complete, cached.Status);
        Assert.Equal(request.Path, cached.Path);
        Assert.Equal(after.Searches, cachedMetrics.Searches);
        Assert.Equal(after.CacheHits + 1, cachedMetrics.CacheHits);
    }

    [Fact]
    public void RoofedGoblinBuildingsRejectTreeCrownSpaceAboveTheirFootprint()
    {
        var engine = CreateEngine();
        var crownCells = engine.World.CreateWorldObjectSnapshot()
            .Where(worldObject => worldObject.Kind == WorldObjectKind.Tree)
            .SelectMany(worldObject => worldObject.GetAbsoluteParts())
            .Where(part => part.Part.Kind == WorldObjectPartKind.TreeCrown &&
                part.Position.Z == 1)
            .Select(part => part.Position)
            .ToHashSet();
        var anchor = crownCells
            .Select(crown => new GridPosition(crown.X, crown.Y, 0))
            .First(candidate =>
                Enumerable.Range(0, 2).SelectMany(y => Enumerable.Range(0, 2)
                        .Select(x => new GridPosition(candidate.X + x, candidate.Y + y, 0)))
                    .All(position => engine.World.IsTerrainTraversable(position) &&
                        engine.World.GetWorldObjectsAt(position).Count == 0));

        Assert.False(engine.World.CanBuildGoblinFieldCamp(anchor));
    }

    private static SimulationEngine CreateEngine() => SimulationEngine.Create(
        new WorldSeed(0x4C4956494E47UL),
        SimulationDefinitions.Foundation,
        initialGoblinCount: 1,
        initialFoodStock: 0);

    private static SimulationEngine MoveToStartOfSummer(SimulationEngine engine)
    {
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        save["currentTick"] = engine.Definitions.Clock.Climate.GetSeasonStartTick(SeasonKind.Summer);
        return SimulationEngine.Load(save.ToJsonString(), engine.Definitions);
    }

    private static int MeasureWaterBody(GeneratedMap map, GridPosition start)
    {
        var visited = new HashSet<GridPosition> { start };
        var queue = new Queue<GridPosition>();
        queue.Enqueue(start);
        while (queue.TryDequeue(out var current))
        {
            foreach (var neighbor in map.GetCardinalNeighbors(current))
            {
                if (visited.Contains(neighbor) ||
                    map.GetCell(neighbor).Terrain is not (TerrainKind.ShallowWater or TerrainKind.DeepWater))
                {
                    continue;
                }

                visited.Add(neighbor);
                queue.Enqueue(neighbor);
            }
        }

        return visited.Count;
    }
}
