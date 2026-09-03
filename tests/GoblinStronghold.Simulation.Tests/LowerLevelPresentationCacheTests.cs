using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Presentation;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class LowerLevelPresentationCacheTests
{
    [Fact]
    public void RetainedViewportBoundsUseAChunkMarginAndClampToTheMap()
    {
        var expanded = RetainedPresentationBoundsPolicy.ExpandToChunks(
            new PresentationCellBounds(17, 18, 31, 35),
            chunkSize: 16,
            mapWidth: 40,
            mapHeight: 40);

        Assert.Equal(new PresentationCellBounds(0, 0, 40, 40), expanded);
        Assert.True(RetainedPresentationBoundsPolicy.Contains(
            expanded,
            new PresentationCellBounds(8, 8, 32, 32)));
        Assert.False(RetainedPresentationBoundsPolicy.Contains(
            expanded,
            new PresentationCellBounds(8, 8, 41, 32)));
    }

    [Fact]
    public void ActiveStaticPresentationIgnoresActorOnlyTickChanges()
    {
        var seed = new WorldSeed(0x535441544943UL);
        var map = SwampMapGenerator.Generate(seed, width: 24, height: 24);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 0);
        var previous = engine.CreatePresentationSnapshot();

        engine.AdvanceTicks(1);
        var current = engine.CreatePresentationSnapshot();

        Assert.False(ActiveStaticPresentationChangePolicy.HasChanged(previous, current, 0));
    }

    [Fact]
    public void ActiveStaticPresentationDetectsStorageChangesOnTheVisibleLevel()
    {
        var seed = new WorldSeed(0x53544F52414745UL);
        var map = SwampMapGenerator.Generate(seed, width: 24, height: 24);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 0);
        var previous = engine.CreatePresentationSnapshot();
        var storagePosition = map.GetCardinalNeighbors(map.GoblinSpawn)
            .First(engine.World.IsTerrainTraversable);

        engine.ApplyCommandImmediately(SimulationCommand.CreateStorageZone(
            engine.CurrentTick,
            sequence: 1,
            storagePosition,
            ResourceKind.Wood,
            capacity: 10));
        var current = engine.CreatePresentationSnapshot();

        Assert.True(ActiveStaticPresentationChangePolicy.HasChanged(previous, current, 0));
    }

    [Fact]
    public void LowerLevelVisualDegradationIsGradualAndBounded()
    {
        Assert.Equal(
            LowerLevelVisualDegradationPolicy.NearestLevelBrightness,
            LowerLevelVisualDegradationPolicy.ResolveBrightness(1));
        Assert.True(
            LowerLevelVisualDegradationPolicy.ResolveBrightness(2) <
            LowerLevelVisualDegradationPolicy.ResolveBrightness(1));
        Assert.Equal(
            LowerLevelVisualDegradationPolicy.MinimumBrightness,
            LowerLevelVisualDegradationPolicy.ResolveBrightness(100));
    }

    [Fact]
    public void SlicePlannerBuildsAStableRequestPlanAndFiltersDistantPassages()
    {
        var bounds = new PresentationCellBounds(0, 0, 16, 16);
        var visible = new VerticalPassage(
            new GridPosition(4, 4, 1),
            new GridPosition(5, 4, 0),
            VerticalPassageKind.NaturalRamp);
        var distant = new VerticalPassage(
            new GridPosition(20, 20, 1),
            new GridPosition(20, 21, 0),
            VerticalPassageKind.NaturalRamp);

        var plan = PresentationSlicePlanner.Create(
            new PresentationSliceRequest(1, bounds),
            (_, _) => 1,
            [visible, distant]);

        Assert.Equal(new PresentationSliceRequest(1, bounds), plan.Request);
        Assert.Equal([visible], plan.VerticalPassages);
        Assert.Equal(visible.Lower, plan.OpeningDestinations[visible.Upper]);
        Assert.True(plan.Exposure.IsContinuouslyExposed(visible.Lower));
        Assert.False(plan.Exposure.IsContinuouslyExposed(distant.Lower));
    }

    [Fact]
    public void SlicePlannerUsesTheRequestedChunkSize()
    {
        var bounds = new PresentationCellBounds(0, 0, 20, 20);

        var plan = PresentationSlicePlanner.Create(
            new PresentationSliceRequest(1, bounds, ChunkSize: 8),
            (_, _) => 0,
            []);

        Assert.Equal(8, plan.Exposure.ChunkSize);
        Assert.Equal(9, plan.Workload.VisibleChunks);
    }

    [Theory]
    [InlineData(1, 0, 1.0)]
    [InlineData(2, 0, 2.0)]
    [InlineData(3, 0, 3.0)]
    [InlineData(2, -1, 3.0)]
    public void LowerRefreshCadenceGrowsLinearlyWithDepth(
        int activeLevel,
        int cachedLevel,
        double expectedSeconds)
    {
        Assert.Equal(
            expectedSeconds,
            LowerLevelRefreshCadencePolicy.GetMinimumIntervalSeconds(
                baseIntervalSeconds: 1d,
                activeLevel,
                cachedLevel));
        Assert.False(LowerLevelRefreshCadencePolicy.IsRebuildDue(
            lastRebuildSeconds: 4d,
            currentSeconds: 4d + expectedSeconds - 0.01d,
            baseIntervalSeconds: 1d,
            activeLevel,
            cachedLevel));
        Assert.True(LowerLevelRefreshCadencePolicy.IsRebuildDue(
            lastRebuildSeconds: 4d,
            currentSeconds: 4d + expectedSeconds,
            baseIntervalSeconds: 1d,
            activeLevel,
            cachedLevel));
    }

    [Fact]
    public void LowerSliceIsClippedToDiscoveredOpeningsOnEveryActiveLevel()
    {
        var bounds = new PresentationCellBounds(0, 0, 8, 8);
        var basin = new GridPosition(3, 3, -1);
        var opening = new VerticalPassage(
            new GridPosition(5, 5, 0),
            new GridPosition(5, 5, -1),
            VerticalPassageKind.CaveMouth);

        var hidden = PresentationSlicePlanner.Create(
            new PresentationSliceRequest(0, bounds),
            (x, y) => x == basin.X && y == basin.Y ? basin.Z : 0,
            [opening],
            _ => false);
        var discovered = PresentationSlicePlanner.Create(
            new PresentationSliceRequest(0, bounds),
            (x, y) => x == basin.X && y == basin.Y ? basin.Z : 0,
            [opening],
            position => position.X is 3 or 5 && position.Y == position.X);

        Assert.Empty(hidden.DirectlyExposedCells);
        Assert.Empty(hidden.VerticalPassages);
        Assert.False(hidden.Exposure.IsContinuouslyExposed(basin));
        Assert.Equal([basin], discovered.DirectlyExposedCells);
        Assert.Equal([opening], discovered.VerticalPassages);
        Assert.True(discovered.Exposure.IsContinuouslyExposed(basin));
        Assert.True(discovered.Exposure.IsContinuouslyExposed(opening.Lower));
    }

    [Fact]
    public void ConstructedFloorClosesDirectViewToLowerSurface()
    {
        var bounds = new PresentationCellBounds(0, 0, 8, 8);
        var covered = new GridPosition(3, 3, 0);
        var lowerSurface = covered with { Z = -1 };

        var plan = PresentationSlicePlanner.Create(
            new PresentationSliceRequest(0, bounds),
            (x, y) => x == covered.X && y == covered.Y ? -1 : 0,
            [],
            _ => true,
            position => position == covered);

        Assert.DoesNotContain(lowerSurface, plan.DirectlyExposedCells);
        Assert.False(plan.Exposure.IsContinuouslyExposed(lowerSurface));
    }

    [Fact]
    public void ConstructedFloorOnVisibleLowerSurfaceDoesNotHideItself()
    {
        var bounds = new PresentationCellBounds(0, 0, 8, 8);
        var lowerSurface = new GridPosition(3, 3, -1);

        var plan = PresentationSlicePlanner.Create(
            new PresentationSliceRequest(0, bounds),
            (x, y) => x == lowerSurface.X && y == lowerSurface.Y ? -1 : 0,
            [],
            _ => true,
            position => position == lowerSurface);

        Assert.Contains(lowerSurface, plan.DirectlyExposedCells);
        Assert.True(plan.Exposure.IsContinuouslyExposed(lowerSurface));
    }

    [Fact]
    public void PositiveSliceShowsTheHighestBlockingFloorFromTheSharedCache()
    {
        var bounds = new PresentationCellBounds(0, 0, 8, 8);
        var naturalSurface = new GridPosition(3, 3, 0);
        var upperFloor = naturalSurface with { Z = 1 };

        var plan = PresentationSlicePlanner.Create(
            new PresentationSliceRequest(2, bounds),
            (x, y) => x == naturalSurface.X && y == naturalSurface.Y ? 0 : 2,
            [],
            _ => true,
            position => position == upperFloor);

        Assert.Equal([upperFloor], plan.DirectlyExposedCells);
        Assert.True(plan.Exposure.IsContinuouslyExposed(upperFloor));
        Assert.False(plan.Exposure.IsContinuouslyExposed(naturalSurface));
        Assert.Contains(
            new PresentationChunkKey(upperFloor.Z, 0, 0),
            plan.Exposure.VisibleChunks);
    }

    [Fact]
    public void SliceWorkloadDistinguishesSingleShaftFromSwissCheeseExposure()
    {
        var bounds = new PresentationCellBounds(0, 0, 32, 32);
        var single = PresentationSlicePlanner.Create(
            new PresentationSliceRequest(1, bounds),
            (x, y) => x == 4 && y == 4 ? 0 : 1,
            []);
        var holes = new HashSet<(int X, int Y)>
        {
            (1, 1),
            (17, 1),
            (1, 17),
            (17, 17),
        };
        var swissCheese = PresentationSlicePlanner.Create(
            new PresentationSliceRequest(2, bounds),
            (x, y) => holes.Contains((x, y)) ? 0 : 2,
            []);

        Assert.Equal(
            new PresentationSliceWorkload(1, 1, 1, 1, 0, 1),
            single.Workload);
        Assert.Equal(
            new PresentationSliceWorkload(4, 8, 8, 8, 0, 8),
            swissCheese.Workload);
        Assert.True(
            swissCheese.Workload.VisibleChunks > single.Workload.VisibleChunks);
    }

    [Fact]
    public void ExposureFollowsOnlyContinuousChainsFromActiveLevel()
    {
        var connectedUpper = new GridPosition(3, 3, 2);
        var connectedMiddle = new GridPosition(4, 3, 1);
        var connectedLower = new GridPosition(4, 4, 0);
        var disconnectedUpper = new GridPosition(10, 10, 1);
        var disconnectedLower = new GridPosition(10, 11, 0);

        var index = LowerLevelExposureIndex.Build(
            activeLevel: 2,
            directlyExposedCells: [],
            verticalPassages:
            [
                new VerticalPassage(
                    connectedUpper,
                    connectedMiddle,
                    VerticalPassageKind.NaturalRamp),
                new VerticalPassage(
                    connectedMiddle,
                    connectedLower,
                    VerticalPassageKind.NaturalRamp),
                new VerticalPassage(
                    disconnectedUpper,
                    disconnectedLower,
                    VerticalPassageKind.NaturalRamp),
            ]);

        Assert.True(index.IsContinuouslyExposed(connectedMiddle));
        Assert.True(index.IsContinuouslyExposed(connectedLower));
        Assert.False(index.IsContinuouslyExposed(disconnectedLower));
    }

    [Fact]
    public void ConnectedCellsFormRegionsAndSplitAcrossBoundedChunks()
    {
        var index = LowerLevelExposureIndex.Build(
            activeLevel: 1,
            directlyExposedCells:
            [
                new GridPosition(15, 4, 0),
                new GridPosition(16, 4, 0),
                new GridPosition(17, 4, 0),
                new GridPosition(30, 20, 0),
            ],
            verticalPassages: [],
            chunkSize: 16);

        Assert.Equal(2, index.Regions.Count);
        var connected = Assert.Single(index.Regions.Where(region => region.Cells.Count == 3));
        Assert.Equal(new PresentationCellBounds(15, 4, 18, 5), connected.Bounds);
        Assert.Equal(
            [new PresentationChunkKey(0, 0, 0), new PresentationChunkKey(0, 1, 0)],
            connected.Chunks);
    }

    [Fact]
    public void DirectlyVisibleFloorRegistersIntermediateVerticalContinuity()
    {
        var intermediate = new GridPosition(6, 6, 1);
        var branch = new GridPosition(7, 6, 0);
        var index = LowerLevelExposureIndex.Build(
            activeLevel: 2,
            directlyExposedCells: [new GridPosition(6, 6, 0)],
            verticalPassages:
            [
                new VerticalPassage(
                    intermediate,
                    branch,
                    VerticalPassageKind.NaturalRamp),
            ]);

        Assert.True(index.IsContinuouslyExposed(intermediate));
        Assert.True(index.IsContinuouslyExposed(branch));
    }

    [Fact]
    public void HiddenDirtyChunkWaitsForExposureBeforeBecomingRebuildCandidate()
    {
        var visible = LowerLevelExposureIndex.Build(
            activeLevel: 1,
            directlyExposedCells: [new GridPosition(2, 2, 0)],
            verticalPassages: []);
        var hidden = LowerLevelExposureIndex.Build(
            activeLevel: 1,
            directlyExposedCells: [],
            verticalPassages: []);
        var cache = new LowerLevelPresentationCacheState();
        cache.SynchronizeExposure(visible);
        var initial = Assert.Single(cache.GetVisibleRebuildCandidates());
        cache.MarkRebuilt(initial.Key);

        cache.SynchronizeExposure(hidden);
        cache.Invalidate(
            new GridPosition(2, 2, 0),
            PresentationChunkDirtyReason.StaticLighting);

        Assert.Empty(cache.GetVisibleRebuildCandidates());

        cache.SynchronizeExposure(visible);
        var exposedAgain = Assert.Single(cache.GetVisibleRebuildCandidates());
        Assert.Equal(PresentationChunkDirtyReason.StaticLighting, exposedAgain.DirtyReasons);
        Assert.Equal(1UL, exposedAgain.Revision);
    }

    [Fact]
    public void ChangedExposureMaskInvalidatesAnOtherwiseRetainedChunk()
    {
        var first = LowerLevelExposureIndex.Build(
            activeLevel: 1,
            directlyExposedCells: [new GridPosition(2, 2, 0)],
            verticalPassages: []);
        var movedWithinSameChunk = LowerLevelExposureIndex.Build(
            activeLevel: 1,
            directlyExposedCells: [new GridPosition(3, 2, 0)],
            verticalPassages: []);
        var cache = new LowerLevelPresentationCacheState();
        cache.SynchronizeExposure(first);
        var initial = Assert.Single(cache.GetVisibleRebuildCandidates());
        cache.MarkRebuilt(initial.Key);

        cache.SynchronizeExposure(movedWithinSameChunk);

        var changed = Assert.Single(cache.GetVisibleRebuildCandidates());
        Assert.Equal(PresentationChunkDirtyReason.ExposureMask, changed.DirtyReasons);
    }

    [Fact]
    public void ChangedExposureAtChunkBoundaryInvalidatesNeighboringVignette()
    {
        var first = LowerLevelExposureIndex.Build(
            activeLevel: 1,
            directlyExposedCells:
            [
                new GridPosition(15, 4, 0),
                new GridPosition(16, 4, 0),
                new GridPosition(17, 4, 0),
            ],
            verticalPassages: []);
        var changedAcrossBoundary = LowerLevelExposureIndex.Build(
            activeLevel: 1,
            directlyExposedCells:
            [
                new GridPosition(15, 4, 0),
                new GridPosition(17, 4, 0),
            ],
            verticalPassages: []);
        var cache = new LowerLevelPresentationCacheState();
        cache.SynchronizeExposure(first);
        foreach (var candidate in cache.GetVisibleRebuildCandidates())
        {
            cache.MarkRebuilt(candidate.Key);
        }

        cache.SynchronizeExposure(changedAcrossBoundary);

        Assert.Equal(2, cache.GetVisibleRebuildCandidates().Count);
        Assert.All(cache.GetVisibleRebuildCandidates(), candidate =>
            Assert.Equal(PresentationChunkDirtyReason.ExposureMask, candidate.DirtyReasons));
    }

    [Fact]
    public void RebuildCandidatesAreOrderedFromDeepestLevelUpward()
    {
        var exposure = LowerLevelExposureIndex.Build(
            activeLevel: 2,
            directlyExposedCells: [new GridPosition(4, 4, 0)],
            verticalPassages: []);
        var cache = new LowerLevelPresentationCacheState();

        cache.SynchronizeExposure(exposure);

        Assert.Equal(
            [0, 1],
            cache.GetVisibleRebuildCandidates().Select(candidate => candidate.Key.Level));
    }

    [Fact]
    public void ChangeTrackerInvalidatesOnlyChangedStructureCells()
    {
        var tracker = new LowerLevelPresentationChangeTracker();
        var position = new GridPosition(18, 7, -2);
        tracker.Synchronize(Observation(
            topologyVersion: 4,
            structures:
            [
                new PresentationStructureObservation(
                    new WorldObjectId(9),
                    11,
                    EmitsStaticLight: false,
                    [position]),
            ]));

        var changes = tracker.Synchronize(Observation(
            topologyVersion: 5,
            structures:
            [
                new PresentationStructureObservation(
                    new WorldObjectId(9),
                    12,
                    EmitsStaticLight: false,
                    [position]),
            ]));

        Assert.False(changes.RequiresFullInvalidation);
        var invalidation = Assert.Single(changes.Invalidations);
        Assert.Equal(position, invalidation.Position);
        Assert.Equal(PresentationChunkDirtyReason.Structures, invalidation.Reason);
    }

    [Fact]
    public void ChangeTrackerAddsStaticLightingReasonForTorchChanges()
    {
        var tracker = new LowerLevelPresentationChangeTracker();
        var position = new GridPosition(3, 5, -1);
        tracker.Synchronize(Observation(topologyVersion: 1));

        var changes = tracker.Synchronize(Observation(
            topologyVersion: 2,
            structures:
            [
                new PresentationStructureObservation(
                    new WorldObjectId(12),
                    31,
                    EmitsStaticLight: true,
                    [position]),
            ]));

        Assert.Equal(
            PresentationChunkDirtyReason.Structures |
            PresentationChunkDirtyReason.StaticLighting,
            Assert.Single(changes.Invalidations).Reason);
    }

    [Fact]
    public void ChangeTrackerReportsContaminationWithoutFullInvalidation()
    {
        var tracker = new LowerLevelPresentationChangeTracker();
        var position = new GridPosition(4, 6, -3);
        tracker.Synchronize(Observation(topologyVersion: 8));

        var changes = tracker.Synchronize(Observation(
            topologyVersion: 8,
            contamination:
            [new PresentationContaminationObservation(position, 3, 7)]));

        Assert.False(changes.RequiresFullInvalidation);
        Assert.Equal(
            PresentationChunkDirtyReason.Contamination,
            Assert.Single(changes.Invalidations).Reason);
    }

    [Fact]
    public void ChangeTrackerInvalidatesVegetationWhenBushBiomassChanges()
    {
        var tracker = new LowerLevelPresentationChangeTracker();
        var position = new GridPosition(7, 9, 1);
        tracker.Synchronize(Observation(
            topologyVersion: 3,
            plants: [new PresentationPlantObservation(
                position,
                PlantKind.BerryBush,
                Biomass: 3,
                Capacity: 3)]));

        var changes = tracker.Synchronize(Observation(
            topologyVersion: 3,
            plants: [new PresentationPlantObservation(
                position,
                PlantKind.BerryBush,
                Biomass: 0,
                Capacity: 3)]));

        Assert.False(changes.RequiresFullInvalidation);
        var invalidation = Assert.Single(changes.Invalidations);
        Assert.Equal(position, invalidation.Position);
        Assert.Equal(PresentationChunkDirtyReason.Vegetation, invalidation.Reason);
    }

    [Fact]
    public void UnknownTopologyVersionChangeFallsBackToFullInvalidation()
    {
        var tracker = new LowerLevelPresentationChangeTracker();
        tracker.Synchronize(Observation(topologyVersion: 20));

        var changes = tracker.Synchronize(Observation(topologyVersion: 21));

        Assert.True(changes.RequiresFullInvalidation);
        Assert.Empty(changes.Invalidations);
    }

    [Fact]
    public void KnownTopologyChangeInvalidatesOnlyItsObservedCells()
    {
        var tracker = new LowerLevelPresentationChangeTracker();
        var upper = new GridPosition(8, 9, -1);
        var lower = new GridPosition(8, 10, -2);
        tracker.Synchronize(Observation(topologyVersion: 3));

        var changes = tracker.Synchronize(Observation(
            topologyVersion: 4,
            topology:
            [new PresentationTopologyObservation(77, [upper, lower])]));

        Assert.False(changes.RequiresFullInvalidation);
        Assert.Equal([lower, upper], changes.Invalidations.Select(item => item.Position));
        Assert.All(changes.Invalidations, item =>
            Assert.Equal(PresentationChunkDirtyReason.Topology, item.Reason));
    }

    [Fact]
    public void RetainedInvalidationDoesNotCreateUnseenChunks()
    {
        var cache = new LowerLevelPresentationCacheState();

        var invalidated = cache.InvalidateRetained(
            new GridPosition(30, 30, -4),
            PresentationChunkDirtyReason.Contamination);

        Assert.False(invalidated);
        Assert.Empty(cache.Snapshot);
    }

    [Fact]
    public void AreaInvalidationReachesAdjacentRetainedChunk()
    {
        var exposure = LowerLevelExposureIndex.Build(
            activeLevel: 1,
            directlyExposedCells:
            [
                new GridPosition(15, 4, 0),
                new GridPosition(16, 4, 0),
            ],
            verticalPassages: []);
        var cache = new LowerLevelPresentationCacheState();
        cache.SynchronizeExposure(exposure);
        foreach (var candidate in cache.GetVisibleRebuildCandidates())
        {
            cache.MarkRebuilt(candidate.Key);
        }

        var count = cache.InvalidateRetainedArea(
            new GridPosition(15, 4, 0),
            radiusCells: 2f,
            PresentationChunkDirtyReason.StaticLighting);

        Assert.Equal(2, count);
        Assert.Equal(2, cache.GetVisibleRebuildCandidates().Count);
        Assert.All(cache.GetVisibleRebuildCandidates(), candidate =>
            Assert.Equal(PresentationChunkDirtyReason.StaticLighting, candidate.DirtyReasons));
    }

    [Fact]
    public void ActorOverlaySelectsOnlyContinuouslyExposedLowerActors()
    {
        var visible = new GridPosition(4, 4, 0);
        var hidden = new GridPosition(8, 8, 0);
        var active = new GridPosition(4, 4, 1);
        var exposure = LowerLevelExposureIndex.Build(
            activeLevel: 1,
            directlyExposedCells: [visible],
            verticalPassages: []);

        var selected = LowerLevelActorOverlayPolicy.SelectVisible(
            [
                new LowerLevelActorMarker(new EntityId(1), visible, 1f, false),
                new LowerLevelActorMarker(new EntityId(2), hidden, 1f, false),
                new LowerLevelActorMarker(new EntityId(3), active, 1f, false),
            ],
            exposure,
            new PresentationCellBounds(0, 0, 12, 12));

        Assert.Equal(new EntityId(1), Assert.Single(selected).Id);
    }

    [Fact]
    public void ActorOverlayOrdersDeepestMarkersFirst()
    {
        var deepest = new GridPosition(3, 3, -2);
        var upper = new GridPosition(3, 3, -1);
        var exposure = LowerLevelExposureIndex.Build(
            activeLevel: 0,
            directlyExposedCells: [],
            verticalPassages:
            [
                new VerticalPassage(
                    new GridPosition(3, 3, 0),
                    upper,
                    VerticalPassageKind.NaturalRamp),
                new VerticalPassage(
                    upper,
                    deepest,
                    VerticalPassageKind.NaturalRamp),
            ]);

        var selected = LowerLevelActorOverlayPolicy.SelectVisible(
            [
                new LowerLevelActorMarker(new EntityId(2), upper, 0.8f, false),
                new LowerLevelActorMarker(new EntityId(1), deepest, 0.6f, true),
            ],
            exposure,
            new PresentationCellBounds(0, 0, 8, 8));

        Assert.Equal([deepest, upper], selected.Select(actor => actor.Position));
    }

    private static LowerLevelPresentationObservation Observation(
        ulong topologyVersion,
        IReadOnlyList<PresentationTopologyObservation>? topology = null,
        IReadOnlyList<PresentationStructureObservation>? structures = null,
        IReadOnlyList<PresentationPlantObservation>? plants = null,
        IReadOnlyList<PresentationContaminationObservation>? contamination = null) => new(
        topologyVersion,
        topology ?? [],
        structures ?? [],
        plants ?? [],
        contamination ?? []);
}
