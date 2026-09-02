using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Planning;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class DirectionalRampPlacementPolicyTests
{
    [Fact]
    public void DragFromLowerCellPointsTowardUpperGround()
    {
        var start = new GridPosition(4, 5, -1);
        var end = start with { X = start.X + 1 };
        var expected = new DirectionalRampPlacement(
            start,
            end with { Z = 0 });

        var resolved = DirectionalRampPlacementPolicy.TryResolve(
            start,
            end,
            (lower, upper) => (lower, upper) == (expected.Lower, expected.Upper),
            out var placement);

        Assert.True(resolved);
        Assert.Equal(expected, placement);
    }

    [Fact]
    public void DragFromUpperCellPointsTowardEmptyLowerSpace()
    {
        var start = new GridPosition(9, 8, 1);
        var end = start with { Y = start.Y + 1 };
        var expected = new DirectionalRampPlacement(
            end with { Z = 0 },
            start);

        var resolved = DirectionalRampPlacementPolicy.TryResolve(
            start,
            end,
            (lower, upper) => (lower, upper) == (expected.Lower, expected.Upper),
            out var placement);

        Assert.True(resolved);
        Assert.Equal(expected, placement);
    }

    [Fact]
    public void GestureMustBeOneCardinalCellAndUnambiguous()
    {
        var start = new GridPosition(3, 3, 0);

        Assert.False(DirectionalRampPlacementPolicy.TryResolve(
            start,
            start with { X = 5 },
            (_, _) => true,
            out _));
        Assert.False(DirectionalRampPlacementPolicy.TryResolve(
            start,
            start with { X = 4 },
            (_, _) => true,
            out _));
    }
}
