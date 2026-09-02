using GoblinStronghold.Simulation.ContentPacks;
using GoblinStronghold.Simulation.Lighting;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Presentation;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class LightEmitterIndexTests
{
    [Fact]
    public void GoblinDarkVisionBrightensOnlyVisibleCellsOnTheActiveLevel()
    {
        var active = new GridPosition(4, 5, -3);
        var otherLevel = active with { Z = -2 };

        Assert.Equal(
            ActiveLevelDarkVisionPolicy.VisibleCellDarknessMultiplier,
            ActiveLevelDarkVisionPolicy.ResolveDarknessMultiplier(
                active,
                activeLevel: -3,
                CellVisibility.Visible));
        Assert.Equal(
            1f,
            ActiveLevelDarkVisionPolicy.ResolveDarknessMultiplier(
                active,
                activeLevel: -3,
                CellVisibility.Explored));
        Assert.Equal(
            1f,
            ActiveLevelDarkVisionPolicy.ResolveDarknessMultiplier(
                otherLevel,
                activeLevel: -3,
                CellVisibility.Visible));
    }

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
            Assert.InRange(
                definition.Intensity,
                float.Epsilon,
                LightEmitterCatalog.MaximumSupportedIntensity);
            Assert.InRange(definition.FlickerAmount, 0f, 1f);
            Assert.True(LightEmitterActivationPolicy.IsValid(definition));
        });
    }

    [Fact]
    public void WallTorchUsesTheBrighterExtendedRangeProfile()
    {
        var torch = LightEmitterCatalog.Get(LightEmitterCatalog.WallTorchId);
        var index = new LightEmitterIndex();

        Assert.Equal(6.3f, torch.RadiusCells);
        Assert.Equal(1.38f, torch.Intensity);
        index.Upsert(new LightEmitterSnapshot(
            new LightEmitterHandle(torch.Id, 1),
            new GridPosition(4, 4, -1),
            torch.RadiusCells,
            torch.Intensity));
        Assert.Equal(1, index.Count);
    }

    [Fact]
    public void ActivationContractKeepsStaticAndWorkingSourcesDistinct()
    {
        var torch = LightEmitterCatalog.Get(LightEmitterCatalog.WallTorchId);
        var furnace = LightEmitterCatalog.Get(LightEmitterCatalog.SmeltingFurnaceId);

        Assert.True(LightEmitterActivationPolicy.IsStaticallyActive(torch));
        Assert.True(LightEmitterActivationPolicy.IsActive(torch, default));
        Assert.False(LightEmitterActivationPolicy.IsStaticallyActive(furnace));
        Assert.False(LightEmitterActivationPolicy.IsActive(
            furnace,
            new LightEmitterActivationContext(IsWorking: true)));
        Assert.True(LightEmitterActivationPolicy.IsActive(
            furnace,
            new LightEmitterActivationContext(
                IsWorking: true,
                HasWorkOrderFuel: true)));
    }

    [Fact]
    public void PortableAndTraitContractsRequireActorAttachmentAndTheirOwnContext()
    {
        var template = LightEmitterCatalog.Get(LightEmitterCatalog.WallTorchId);
        var lantern = template with
        {
            Activation = new LightEmitterActivation(
                LightEmitterActivityRequirement.WhileCarried,
                LightEmitterFuelRequirement.PortableCharge),
            Attachment = LightEmitterAttachment.Actor,
        };
        var luminousTrait = template with
        {
            Activation = new LightEmitterActivation(
                LightEmitterActivityRequirement.ActorTrait,
                LightEmitterFuelRequirement.None),
            Attachment = LightEmitterAttachment.Actor,
        };

        Assert.True(LightEmitterActivationPolicy.IsValid(lantern));
        Assert.False(LightEmitterActivationPolicy.IsActive(
            lantern,
            new LightEmitterActivationContext(IsCarried: true)));
        Assert.True(LightEmitterActivationPolicy.IsActive(
            lantern,
            new LightEmitterActivationContext(
                IsCarried: true,
                HasPortableCharge: true)));
        Assert.True(LightEmitterActivationPolicy.IsValid(luminousTrait));
        Assert.True(LightEmitterActivationPolicy.IsActive(
            luminousTrait,
            new LightEmitterActivationContext(IsActorTraitActive: true)));
        Assert.False(LightEmitterActivationPolicy.IsValid(
            lantern with { Attachment = LightEmitterAttachment.World }));
    }

    [Fact]
    public void WallBetweenEmitterAndTargetBlocksLightButLitWallRemainsVisible()
    {
        var emitter = CreateEmitter(
            LightEmitterCatalog.LavaId,
            1,
            new GridPosition(1, 2),
            6f);
        var wall = new GridPosition(3, 2);
        IReadOnlySet<GridPosition> blockers = new HashSet<GridPosition> { wall };

        Assert.True(LightOcclusionPolicy.CalculateContribution(emitter, wall, blockers) > 0f);
        Assert.Equal(
            0f,
            LightOcclusionPolicy.CalculateContribution(
                emitter,
                new GridPosition(4, 2),
                blockers));
    }

    [Fact]
    public void DiagonalRayDoesNotLeakThroughBlockedCorner()
    {
        var emitter = CreateEmitter(
            LightEmitterCatalog.LavaId,
            1,
            new GridPosition(1, 1),
            6f);
        IReadOnlySet<GridPosition> blockers = new HashSet<GridPosition>
        {
            new(2, 1),
        };

        Assert.Equal(
            0f,
            LightOcclusionPolicy.CalculateContribution(
                emitter,
                new GridPosition(2, 2),
                blockers));
    }

    [Fact]
    public void OpenPassageDoesNotBlockLight()
    {
        var emitter = CreateEmitter(
            LightEmitterCatalog.LavaId,
            1,
            new GridPosition(1, 2),
            6f);

        Assert.True(LightOcclusionPolicy.CalculateContribution(
            emitter,
            new GridPosition(4, 2),
            new HashSet<GridPosition>()) > 0f);
    }

    [Fact]
    public void SoftShadowRetainsAFullBlockerAndCreatesPenumbraAtItsEdge()
    {
        var emitter = CreateEmitter(
            LightEmitterCatalog.LavaId,
            1,
            new GridPosition(1, 1),
            8f);
        IReadOnlySet<GridPosition> fullWall = Enumerable.Range(0, 7)
            .Select(y => new GridPosition(3, y))
            .ToHashSet();

        Assert.Equal(
            0f,
            LightOcclusionPolicy.CalculateSoftContribution(
                emitter,
                new GridPosition(5, 1),
                fullWall));

        IReadOnlySet<GridPosition> corner = new HashSet<GridPosition> { new(3, 2) };
        var hard = LightOcclusionPolicy.CalculateContribution(
            emitter,
            new GridPosition(4, 4),
            corner);
        var soft = LightOcclusionPolicy.CalculateSoftContribution(
            emitter,
            new GridPosition(4, 4),
            corner);

        Assert.Equal(0f, hard);
        Assert.True(soft > 0f);
        Assert.True(soft < LightOcclusionPolicy.CalculateSoftContribution(
            emitter,
            new GridPosition(4, 4),
            new HashSet<GridPosition>()));
    }

    [Fact]
    public void WallTorchOnlyIlluminatesTheSideItFaces()
    {
        var emitter = CreateEmitter(
            LightEmitterCatalog.WallTorchId,
            1,
            new GridPosition(3, 3),
            6f) with
        {
            Facing = CardinalOrientation.East,
        };
        IReadOnlySet<GridPosition> blockers = new HashSet<GridPosition>();

        Assert.True(LightOcclusionPolicy.CalculateContribution(
            emitter,
            new GridPosition(5, 3),
            blockers) > 0f);
        Assert.Equal(
            0f,
            LightOcclusionPolicy.CalculateContribution(
                emitter,
                new GridPosition(2, 3),
                blockers));
    }

    [Fact]
    public void OpenVerticalPassageProjectsReachableLightToItsUpperEnd()
    {
        var emitter = CreateEmitter(
            LightEmitterCatalog.LavaId,
            1,
            new GridPosition(2, 3, -2),
            6f);
        var passage = new VerticalPassage(
            new GridPosition(4, 3, -1),
            new GridPosition(3, 3, -2),
            VerticalPassageKind.NaturalRamp);

        var projected = AssertProjection(emitter, passage, new HashSet<GridPosition>());

        Assert.Equal(passage.Upper, projected.Position);
        Assert.True(projected.RadiusCells < emitter.RadiusCells);
        Assert.True(projected.Intensity < emitter.Intensity);
        Assert.Null(projected.Facing);
    }

    [Fact]
    public void WallBeforeVerticalPassagePreventsProjection()
    {
        var emitter = CreateEmitter(
            LightEmitterCatalog.LavaId,
            1,
            new GridPosition(1, 3, -2),
            6f);
        var passage = new VerticalPassage(
            new GridPosition(4, 3, -1),
            new GridPosition(3, 3, -2),
            VerticalPassageKind.NaturalRamp);

        Assert.False(VerticalLightPropagationPolicy.TryProjectThrough(
            emitter,
            passage,
            new HashSet<GridPosition> { new(2, 3, -2) },
            out _));
    }

    [Fact]
    public void ProjectedLightCanContinueThroughAConnectedPassageChain()
    {
        var emitter = CreateEmitter(
            LightEmitterCatalog.LavaId,
            1,
            new GridPosition(3, 3, -3),
            8f);
        var lowerPassage = new VerticalPassage(
            new GridPosition(3, 3, -2),
            new GridPosition(3, 3, -3),
            VerticalPassageKind.ExcavatedRamp);
        var upperPassage = new VerticalPassage(
            new GridPosition(3, 3, -1),
            new GridPosition(3, 3, -2),
            VerticalPassageKind.ExcavatedRamp);

        var middle = AssertProjection(emitter, lowerPassage, new HashSet<GridPosition>());
        var upper = AssertProjection(middle, upperPassage, new HashSet<GridPosition>());

        Assert.Equal(upperPassage.Upper, upper.Position);
        Assert.True(upper.Intensity < middle.Intensity);
        Assert.True(upper.RadiusCells < middle.RadiusCells);
    }

    private static LightEmitterSnapshot AssertProjection(
        LightEmitterSnapshot emitter,
        VerticalPassage passage,
        IReadOnlySet<GridPosition> blockers)
    {
        Assert.True(VerticalLightPropagationPolicy.TryProjectThrough(
            emitter,
            passage,
            blockers,
            out var projected));
        return projected;
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
