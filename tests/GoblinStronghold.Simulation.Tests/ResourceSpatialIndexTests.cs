using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class ResourceSpatialIndexTests
{
    [Fact]
    public void NearbyLookupUsesSectorsAndTracksStackMovementIncrementally()
    {
        var index = new ResourceSpatialIndex(sectorSize: 8);
        var nearbyWood = new EntityId(1);
        var distantWood = new EntityId(2);
        index.UpsertStack(
            distantWood,
            ResourceKind.Wood,
            ItemLocation.OnGround(new GridPosition(40, 3)));
        index.UpsertStack(
            nearbyWood,
            ResourceKind.Wood,
            ItemLocation.OnGround(new GridPosition(3, 2)));
        index.UpsertStack(
            new EntityId(3),
            ResourceKind.Food,
            ItemLocation.OnGround(new GridPosition(1, 1)));
        var populatedVersion = index.Version;

        Assert.Equal(
            [nearbyWood, distantWood],
            index.FindNearestStackIds(ResourceKind.Wood, new GridPosition(0, 0), 8));

        index.UpsertStack(
            nearbyWood,
            ResourceKind.Wood,
            ItemLocation.CarriedBy(new EntityId(20)));

        Assert.True(index.Version > populatedVersion);
        Assert.Equal(
            [distantWood],
            index.FindNearestStackIds(ResourceKind.Wood, new GridPosition(0, 0), 8));
    }

    [Fact]
    public void SnapshotCarriesVersionedStorageLinksForFutureWorkerPlanning()
    {
        var index = new ResourceSpatialIndex();
        var source = new EntityId(10);
        var destination = new EntityId(11);
        index.UpsertStorageNode(
            source,
            new GridPosition(4, 5),
            ResourceKind.Wood,
            EntityId.None);
        index.UpsertStorageNode(
            destination,
            new GridPosition(24, 20),
            ResourceKind.Wood,
            source);

        var snapshot = index.CreateSnapshot();

        Assert.True(snapshot.Version > 0);
        Assert.Equal(ResourceSpatialIndex.DefaultSectorSize, snapshot.SectorSize);
        Assert.Contains(snapshot.StorageNodes, node =>
            node.ZoneId == destination && node.SourceStorageZoneId == source);
    }
}
