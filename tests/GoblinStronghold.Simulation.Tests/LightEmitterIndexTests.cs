using GoblinStronghold.Simulation.ContentPacks;
using GoblinStronghold.Simulation.Lighting;
using GoblinStronghold.Simulation.Map;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class LightEmitterIndexTests
{
    [Fact]
    public void QueryReturnsOnlyEmittersWhoseInfluenceReachesRequestedLevelAndArea()
    {
        var index = new LightEmitterIndex(sectorSize: 4);
        var torch = CreateEmitter(LightEmitterCatalog.WallTorchId, 1, new GridPosition(3, 3, -1), 3f);
        var distant = CreateEmitter(LightEmitterCatalog.LavaId, 2, new GridPosition(20, 20, -1), 2f);
        var otherLevel = CreateEmitter(LightEmitterCatalog.LavaId, 3, new GridPosition(3, 3, -2), 2f);
        index.Upsert(torch);
        index.Upsert(distant);
        index.Upsert(otherLevel);

        var result = index.Query(-1, 5, 3, 8, 7);

        Assert.Equal([torch], result);
    }

    [Fact]
    public void MovingAndRemovingEmitterUpdatesItsSectorIncrementally()
    {
        var index = new LightEmitterIndex(sectorSize: 4);
        var initial = CreateEmitter(
            LightEmitterCatalog.WallTorchId,
            8,
            new GridPosition(1, 1),
            2f);
        index.Upsert(initial);
        var populatedVersion = index.Version;

        var moved = initial with { Position = new GridPosition(12, 12) };
        index.Upsert(moved);

        Assert.True(index.Version > populatedVersion);
        Assert.Empty(index.Query(0, 0, 0, 4, 4));
        Assert.Equal([moved], index.Query(0, 10, 10, 14, 14));
        Assert.True(index.Remove(moved.Handle));
        Assert.Empty(index.Query(0, 10, 10, 14, 14));
    }

    [Fact]
    public void CoreCatalogUsesStableUniqueIdsAndValidParameters()
    {
        var definitions = LightEmitterCatalog.All.ToArray();

        Assert.Equal(definitions.Length, definitions.Select(item => item.Id).Distinct().Count());
        Assert.All(definitions, definition =>
        {
            Assert.Equal(ContentId.CoreNamespace, definition.Id.PackageId);
            Assert.True(definition.RadiusCells > 0f);
            Assert.InRange(definition.Intensity, float.Epsilon, 1f);
            Assert.InRange(definition.FlickerAmount, 0f, 1f);
        });
    }

    private static LightEmitterSnapshot CreateEmitter(
        ContentId definitionId,
        ulong instanceId,
        GridPosition position,
        float radius) => new(
        new LightEmitterHandle(definitionId, instanceId),
        position,
        radius,
        1f);
}
