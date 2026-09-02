using GoblinStronghold.Simulation.ContentPacks;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Map.Generation;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class CaveMacroFeaturePlanTests
{
    [Fact]
    public void PlanKeepsContiguousMultiLevelGeometryAndPassagesTogether()
    {
        var upper = new GridPosition(10, 12, -3);
        var lower = upper with { Z = -4 };
        var passage = new VerticalPassage(
            upper,
            lower,
            VerticalPassageKind.NaturalRamp);

        var plan = CreatePlan(
            CaveMacroFeatureMaterializationPolicy.LayerByLayer,
            new CaveMacroFeatureSlice(-3, [upper], [passage]),
            new CaveMacroFeatureSlice(-4, [lower], [passage]));

        Assert.Equal(-3, plan.HighestLevel);
        Assert.Equal(-4, plan.LowestLevel);
        Assert.Equal(passage, Assert.Single(plan.Slices[0].VerticalPassages));
        Assert.Equal(passage, Assert.Single(plan.Slices[1].VerticalPassages));
    }

    [Fact]
    public void ExposedFeatureRequestsEveryOutstandingSlice()
    {
        var plan = CreatePlan(
            CaveMacroFeatureMaterializationPolicy.CompleteOnExposure,
            Slice(-5),
            Slice(-6),
            Slice(-7));
        var registry = new CaveMacroFeatureMaterializationRegistry();
        registry.Register(plan);

        registry.MarkMaterialized(plan.Handle, -5);

        Assert.Equal([-6, -7], registry.GetLevelsToMaterialize(plan.Handle, -5));
    }

    [Fact]
    public void LayeredFeatureRequestsOnlyTheApproachedSlice()
    {
        var plan = CreatePlan(
            CaveMacroFeatureMaterializationPolicy.LayerByLayer,
            Slice(-5),
            Slice(-6),
            Slice(-7));
        var registry = new CaveMacroFeatureMaterializationRegistry();
        registry.Register(plan);

        Assert.Equal([-6], registry.GetLevelsToMaterialize(plan.Handle, -6));
    }

    [Fact]
    public void RegistryRetainsReservationsUntilTheWholeFeatureIsMaterialized()
    {
        var upper = new GridPosition(4, 8, -9);
        var lower = upper with { Z = -10 };
        var plan = CreatePlan(
            CaveMacroFeatureMaterializationPolicy.LayerByLayer,
            new CaveMacroFeatureSlice(-9, [upper]),
            new CaveMacroFeatureSlice(-10, [lower]));
        var registry = new CaveMacroFeatureMaterializationRegistry();
        registry.Register(plan);

        Assert.False(registry.MarkMaterialized(plan.Handle, -9));
        Assert.True(registry.IsReserved(lower));
        Assert.True(registry.TryGetPlan(plan.Handle, out _));

        Assert.True(registry.MarkMaterialized(plan.Handle, -10));
        Assert.False(registry.IsReserved(lower));
        Assert.False(registry.TryGetPlan(plan.Handle, out _));
    }

    [Fact]
    public void RegistryRejectsOverlappingFeatureReservations()
    {
        var first = CreatePlan(
            CaveMacroFeatureMaterializationPolicy.LayerByLayer,
            Slice(-3),
            Slice(-4));
        var second = CreatePlan(
            CaveMacroFeatureMaterializationPolicy.LayerByLayer,
            instanceId: 2,
            Slice(-3),
            Slice(-4));
        var registry = new CaveMacroFeatureMaterializationRegistry();
        registry.Register(first);

        Assert.Throws<InvalidOperationException>(() => registry.Register(second));
    }

    private static CaveMacroFeaturePlan CreatePlan(
        CaveMacroFeatureMaterializationPolicy policy,
        params CaveMacroFeatureSlice[] slices) =>
        CreatePlan(policy, instanceId: 1, slices);

    private static CaveMacroFeaturePlan CreatePlan(
        CaveMacroFeatureMaterializationPolicy policy,
        ulong instanceId,
        params CaveMacroFeatureSlice[] slices) =>
        new(
            new CaveMacroFeatureHandle(
                ContentId.Parse("core:test-cave-feature"),
                instanceId),
            policy,
            slices);

    private static CaveMacroFeatureSlice Slice(int level) =>
        new(level, [new GridPosition(7, 11, level)]);
}
