using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Presentation;

public static class ActiveLevelDarkVisionPolicy
{
    public const float VisibleCellDarknessMultiplier = 0.42f;

    public static float ResolveDarknessMultiplier(
        GridPosition position,
        int activeLevel,
        CellVisibility visibility) =>
        position.Z == activeLevel && visibility == CellVisibility.Visible
            ? VisibleCellDarknessMultiplier
            : 1f;
}
