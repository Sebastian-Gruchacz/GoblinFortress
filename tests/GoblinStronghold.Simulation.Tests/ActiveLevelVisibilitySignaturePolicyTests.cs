using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Presentation;

namespace GoblinStronghold.Simulation.Tests;

public sealed class ActiveLevelVisibilitySignaturePolicyTests
{
    [Fact]
    public void SignatureIgnoresVisibilityChangesOutsideRenderedLevelAndBounds()
    {
        var bounds = new PresentationCellBounds(10, 20, 14, 24);
        var visibility = new Dictionary<GridPosition, CellVisibility>();
        CellVisibility GetVisibility(GridPosition position) =>
            visibility.GetValueOrDefault(position);
        var original = ActiveLevelVisibilitySignaturePolicy.Create(
            level: -6,
            bounds,
            GetVisibility);

        visibility[new GridPosition(11, 21, -5)] = CellVisibility.Visible;
        visibility[new GridPosition(30, 30, -6)] = CellVisibility.Visible;

        Assert.Equal(original, ActiveLevelVisibilitySignaturePolicy.Create(
            level: -6,
            bounds,
            GetVisibility));
    }

    [Fact]
    public void SignatureChangesWithVisibilityInsideRenderedArea()
    {
        var bounds = new PresentationCellBounds(10, 20, 14, 24);
        var visibility = new Dictionary<GridPosition, CellVisibility>();
        CellVisibility GetVisibility(GridPosition position) =>
            visibility.GetValueOrDefault(position);
        var original = ActiveLevelVisibilitySignaturePolicy.Create(
            level: -6,
            bounds,
            GetVisibility);

        visibility[new GridPosition(11, 21, -6)] = CellVisibility.Explored;

        Assert.NotEqual(original, ActiveLevelVisibilitySignaturePolicy.Create(
            level: -6,
            bounds,
            GetVisibility));
    }
}
