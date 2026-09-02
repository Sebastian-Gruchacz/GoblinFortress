using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Presentation;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class LowerLevelPresentationCacheTests
{
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

    private static LowerLevelPresentationObservation Observation(
        ulong topologyVersion,
        IReadOnlyList<PresentationTopologyObservation>? topology = null,
        IReadOnlyList<PresentationStructureObservation>? structures = null,
        IReadOnlyList<PresentationContaminationObservation>? contamination = null) => new(
        topologyVersion,
        topology ?? [],
        structures ?? [],
        contamination ?? []);
}
