using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Civilizations;
using GoblinStronghold.Simulation.Map;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class VisibilityTests
{
    [Theory]
    [InlineData(CellVisibility.Unknown, false)]
    [InlineData(CellVisibility.Explored, true)]
    [InlineData(CellVisibility.Visible, true)]
    public void DiscoveredTerrainIncludesFoggedMemory(
        CellVisibility visibility,
        bool expected)
    {
        Assert.Equal(expected, visibility.IsDiscovered());
    }

    [Fact]
    public void CurrentBuildVisibilityAidIsDisabledForRelease()
    {
        Assert.False(SimulationDebugSettings.ForCurrentBuild.RevealFogFromNonPlayerUnits);
    }

    [Fact]
    public void InitialVisibilityRevealsSpawnButNotDistantVillage()
    {
        var engine = CreateEngine();
        var snapshot = engine.CreateSnapshot();

        Assert.Equal(CellVisibility.Visible, snapshot.GetVisibility(engine.Map.GoblinSpawn, engine.Map.Width));
        Assert.Equal(CellVisibility.Unknown, snapshot.GetVisibility(engine.Map.HumanVillage, engine.Map.Width));
        Assert.InRange(
            snapshot.Visibility.Count(state => state == CellVisibility.Visible),
            1,
            snapshot.Visibility.Count - 1);
    }

    [Fact]
    public void OlderSnapshotTreatsNewlyMaterializedLevelAsUnknown()
    {
        var engine = CreateEngine();
        var snapshot = engine.CreateSnapshot();
        var newLevel = engine.Map.DeepestCaveLevel - 1;

        engine.Map.MaterializeCaveLevel(newLevel);

        var position = engine.Map.GoblinSpawn with { Z = newLevel };
        Assert.Equal(CellVisibility.Unknown, snapshot.GetVisibility(position, engine.Map.Width));
        Assert.Equal(
            CellVisibility.Unknown,
            engine.CreateSnapshot().GetVisibility(position, engine.Map.Width));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            snapshot.GetVisibility(position with { X = -1 }, engine.Map.Width));
    }

    [Fact]
    public void VisibilityReservesStableLayersForMaterializedHillVolume()
    {
        var map = SwampMapGenerator.Generate(
            new WorldSeed(0x48494C4C464F47UL),
            width: 64,
            height: 64);
        var engine = SimulationEngine.Create(
            map.Seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 8);
        var snapshot = engine.CreateSnapshot();
        var expectedLayers = 1 + map.MaterializedNegativeLevelCount +
            map.MaterializedPositiveLevelCount;
        var hillColumn = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width)
                .Select(x => new GridPosition(x, y)))
            .First(position => map.GetColumnCell(position).SurfaceLevel > 0);
        var hillSurface = map.GetTerrainSurfacePosition(hillColumn);

        Assert.Equal(map.CellCount * expectedLayers, snapshot.Visibility.Count);
        Assert.Equal(CellVisibility.Unknown, snapshot.GetVisibility(hillSurface, map.Width));

        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var legacyLength = map.CellCount * (map.CaveLevelCount + 1);
        save["visibility"] = new JsonArray(Enumerable.Range(0, legacyLength)
            .Select(index => JsonValue.Create(index == 0
                ? CellVisibility.Explored
                : CellVisibility.Unknown))
            .ToArray<JsonNode?>());
        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);
        var restoredSnapshot = restored.CreateSnapshot();
        Assert.Equal(
            CellVisibility.Explored,
            restoredSnapshot.GetVisibility(new GridPosition(0, 0), map.Width));
        Assert.Equal(CellVisibility.Unknown, restoredSnapshot.GetVisibility(hillSurface, map.Width));
        Assert.Equal(map.CellCount * expectedLayers, restoredSnapshot.Visibility.Count);
    }

    [Fact]
    public void HilltopObserverRevealsSurfaceWithoutRevealingBuriedRock()
    {
        var map = SwampMapGenerator.Generate(
            new WorldSeed(0x48494C4C464F47UL),
            width: 64,
            height: 64);
        var column = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width)
                .Select(x => new GridPosition(x, y)))
            .First(position => map.GetColumnCell(position).SurfaceLevel > 0);
        var hilltop = map.GetTerrainSurfacePosition(column);
        var buried = hilltop with { Z = hilltop.Z - 1 };
        var engine = SimulationEngine.Create(
            map.Seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 8);
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        save["actors"]![0]!["x"] = hilltop.X;
        save["actors"]![0]!["y"] = hilltop.Y;
        save["actors"]![0]!["z"] = hilltop.Z;
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);

        engine.AdvanceTicks(1);

        Assert.Equal(CellVisibility.Visible, engine.Visibility.Get(hilltop));
        Assert.Equal(CellVisibility.Unknown, engine.Visibility.Get(buried));
    }

    [Fact]
    public void UndergroundAndSurfaceNightUseLimitedDarkVision()
    {
        var perception = CivilizationCatalog.Core
            .Get(CivilizationLegacyRole.PlayerGoblins)
            .Perception!;

        Assert.Equal(
            perception.DayVisionRadius,
            WorldVisibilityPolicy.ResolveGoblinVisionRadius(
                perception,
                new GridPosition(4, 4, 0),
                isSurfaceNight: false));
        Assert.Equal(
            perception.NightVisionRadius,
            WorldVisibilityPolicy.ResolveGoblinVisionRadius(
                perception,
                new GridPosition(4, 4, 0),
                isSurfaceNight: true));
        Assert.Equal(
            perception.NightVisionRadius,
            WorldVisibilityPolicy.ResolveGoblinVisionRadius(
                perception,
                new GridPosition(4, 4, -1),
                isSurfaceNight: false));
    }

    [Fact]
    public void VisiblePassageEndDiscoversButDoesNotLightTheAdjacentLayer()
    {
        var engine = CreateEngine();
        var passage = engine.Map.VerticalPassages.First(item =>
            item.Kind == VerticalPassageKind.CaveMouth);
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        save["actors"]![0]!["x"] = passage.Upper.X;
        save["actors"]![0]!["y"] = passage.Upper.Y;
        save["actors"]![0]!["z"] = passage.Upper.Z;
        var visibility = save["visibility"]!.AsArray();
        for (var index = 0; index < visibility.Count; index++)
        {
            visibility[index] = (int)CellVisibility.Unknown;
        }
        engine = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);

        engine.AdvanceTicks(1);

        Assert.Equal(CellVisibility.Visible, engine.Visibility.Get(passage.Upper));
        Assert.Equal(CellVisibility.Explored, engine.Visibility.Get(passage.Lower));
        Assert.Contains(
            engine.World.GetCardinalWorldNeighbors(passage.Lower),
            neighbor => neighbor.Z == passage.Lower.Z &&
                engine.Visibility.Get(neighbor) == CellVisibility.Explored);
    }

    [Fact]
    public void AdjacentLayerDiscoveryRequiresARealPassageWithAVisibleEnd()
    {
        var passage = new VerticalPassage(
            new GridPosition(3, 3, 0),
            new GridPosition(3, 3, -1),
            VerticalPassageKind.CaveMouth);

        var hidden = WorldVisibilityPolicy.SelectAdjacentLayerDiscoveries(
            [passage],
            _ => CellVisibility.Explored);
        var visible = WorldVisibilityPolicy.SelectAdjacentLayerDiscoveries(
            [passage],
            position => position == passage.Upper
                ? CellVisibility.Visible
                : CellVisibility.Unknown);

        Assert.Empty(hidden);
        Assert.Equal([(passage.Lower, 1)], visible);
        Assert.Empty(WorldVisibilityPolicy.SelectAdjacentLayerDiscoveries(
            [],
            _ => CellVisibility.Visible));
    }

    [Fact]
    public void GoblinBesideAnOpenEdgeUsesItsVisionRadiusOnTheImmediateLowerLayer()
    {
        var observer = new GridPosition(5, 5, 1);
        var openEdge = observer with { X = observer.X + 1 };
        var below = openEdge with { Z = 0 };
        const int visionRadius = 5;

        var discoveries = WorldVisibilityPolicy.SelectEdgeLookDiscoveries(
            [(observer, visionRadius)],
            position => position == observer,
            position => position == openEdge,
            position => position == below);

        Assert.Equal([(below, visionRadius)], discoveries);
        Assert.All(discoveries, discovery =>
            Assert.Equal(observer.Z - 1, discovery.Position.Z));
        Assert.Empty(WorldVisibilityPolicy.SelectEdgeLookDiscoveries(
            [(observer, visionRadius)],
            position => position == observer,
            _ => false,
            _ => true));
    }

    [Fact]
    public void GoblinStructuresRevealTheirConfiguredSurroundingsWithoutActors()
    {
        var seed = new WorldSeed(0x535452554354UL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 0,
            initialFoodStock: 0);
        var hut = engine.CreateSnapshot().WorldObjects.First(worldObject =>
            worldObject.Owner == WorldObjectOwner.GoblinTribe &&
            worldObject.Kind == WorldObjectKind.GoblinHut);

        var snapshot = engine.CreateSnapshot();

        Assert.Equal(
            CellVisibility.Visible,
            snapshot.GetVisibility(hut.Anchor, engine.Map.Width));
        Assert.True(snapshot.Visibility.Count(state => state == CellVisibility.Visible) > 1);
        Assert.Equal(
            CellVisibility.Unknown,
            snapshot.GetVisibility(engine.Map.HumanVillage, engine.Map.Width));
    }

    [Fact]
    public void DebugVisibilityAlsoRevealsAroundNonPlayerUnits()
    {
        var seed = new WorldSeed(0x4445425547UL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            debugSettings: new SimulationDebugSettings(
                RevealFogFromNonPlayerUnits: true));

        var snapshot = engine.CreateSnapshot();

        Assert.Equal(
            CellVisibility.Visible,
            snapshot.GetVisibility(engine.Map.HumanVillage, engine.Map.Width));
        Assert.All(snapshot.HumanVillage.Cohorts.Where(cohort => cohort.Population > 0), cohort =>
            Assert.Equal(
                CellVisibility.Visible,
                snapshot.GetVisibility(cohort.Position, engine.Map.Width)));
    }

    [Fact]
    public void ExplorerExpandsFogAndExploredCellsRemainRemembered()
    {
        var engine = CreateEngine();
        var initialDiscovered = engine.Visibility.DiscoveredCellCount;
        engine.QueueCommand(SimulationCommand.DesignateScouting(
            engine.CurrentTick.Next(),
            sequence: 1,
            new GridPosition(0, 0),
            new GridPosition(engine.Map.Width - 1, engine.Map.Height - 1)));

        engine.AdvanceTicks(200);

        Assert.True(engine.Visibility.DiscoveredCellCount > initialDiscovered);
        Assert.Contains(engine.CreateSnapshot().Visibility, state => state == CellVisibility.Explored);
    }

    [Fact]
    public void ExplorerTraversesSelectedUnknownUndergroundTerrain()
    {
        var engine = CreateEngine();
        var origin = FindUndergroundExplorationOrigin(engine);
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        save["actors"]![0]!["x"] = origin.X;
        save["actors"]![0]!["y"] = origin.Y;
        save["actors"]![0]!["z"] = origin.Z;
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        engine.AdvanceTicks(1);
        var initiallyDiscovered = CountDiscoveredAtLevel(engine, origin.Z);
        engine.QueueCommand(SimulationCommand.DesignateScouting(
            engine.CurrentTick.Next(),
            sequence: 1,
            new GridPosition(0, 0, origin.Z),
            new GridPosition(engine.Map.Width - 1, engine.Map.Height - 1, origin.Z)));

        var receivedExploreJob = false;
        var restoredActiveExplore = false;
        for (var tick = 0; tick < 200; tick++)
        {
            engine.AdvanceTicks(1);
            var isExploring = Assert.Single(engine.CreateSnapshot().Actors).Job.Kind ==
                ActorJobKind.Explore;
            receivedExploreJob |= isExploring;
            if (isExploring && !restoredActiveExplore)
            {
                var restored = SimulationEngine.Load(
                    engine.Save(),
                    SimulationDefinitions.Foundation);
                Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
                engine = restored;
                restoredActiveExplore = true;
            }
            if (CountDiscoveredAtLevel(engine, origin.Z) > initiallyDiscovered)
            {
                break;
            }
        }

        Assert.True(receivedExploreJob);
        Assert.True(restoredActiveExplore);
        Assert.True(CountDiscoveredAtLevel(engine, origin.Z) > initiallyDiscovered);
    }

    [Fact]
    public void GoblinsDoNotEnterUnknownTerrainWithoutScoutingDesignation()
    {
        var engine = CreateEngine();
        var initialDiscovered = engine.Visibility.DiscoveredCellCount;

        engine.AdvanceTicks(200);

        Assert.Equal(initialDiscovered, engine.Visibility.DiscoveredCellCount);
        Assert.DoesNotContain(
            engine.CreateSnapshot().Actors,
            actor => actor.Job.Kind == ActorJobKind.Explore);
    }

    [Fact]
    public void SaveLoadPreservesFogAndExplorationOutcome()
    {
        var engine = CreateEngine();
        engine.AdvanceTicks(73);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);

        Assert.Equal(engine.CreateSnapshot().Visibility, restored.CreateSnapshot().Visibility);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());

        engine.AdvanceTicks(200);
        restored.AdvanceTicks(200);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(engine.CreateSnapshot().Visibility, restored.CreateSnapshot().Visibility);
    }

    [Fact]
    public void LoadRejectsFogWithWrongCellCount()
    {
        var engine = CreateEngine();
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        save["visibility"]!.AsArray().RemoveAt(0);

        var exception = Assert.Throws<InvalidDataException>(() =>
            SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation));
        Assert.Contains("fog-of-war", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SimulationEngine CreateEngine()
    {
        var seed = new WorldSeed(0x464F47UL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        return SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 0);
    }

    private static GridPosition FindUndergroundExplorationOrigin(SimulationEngine engine)
    {
        for (var z = -1; z >= engine.Map.MinimumWorldLevel; z--)
        {
            var remaining = (
                    from y in Enumerable.Range(0, engine.Map.Height)
                    from x in Enumerable.Range(0, engine.Map.Width)
                    let position = new GridPosition(x, y, z)
                    where engine.World.IsTerrainTraversable(position)
                    select position)
                .ToHashSet();
            while (remaining.Count > 0)
            {
                var start = remaining.OrderBy(position => position.Y)
                    .ThenBy(position => position.X)
                    .First();
                var component = new List<GridPosition>();
                var frontier = new Queue<GridPosition>();
                remaining.Remove(start);
                frontier.Enqueue(start);
                while (frontier.TryDequeue(out var current))
                {
                    component.Add(current);
                    foreach (var neighbor in engine.World.GetCardinalWorldNeighbors(current)
                                 .Where(neighbor => neighbor.Z == z && remaining.Remove(neighbor)))
                    {
                        frontier.Enqueue(neighbor);
                    }
                }

                if (component.Any(position =>
                        Math.Abs(position.X - start.X) + Math.Abs(position.Y - start.Y) > 8))
                {
                    return start;
                }
            }
        }

        throw new InvalidOperationException("The generated map has no suitable underground cave.");
    }

    private static int CountDiscoveredAtLevel(SimulationEngine engine, int z) => (
        from y in Enumerable.Range(0, engine.Map.Height)
        from x in Enumerable.Range(0, engine.Map.Width)
        where engine.Visibility.Get(new GridPosition(x, y, z)).IsDiscovered()
        select 1).Count();
}
