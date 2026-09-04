namespace GoblinStronghold.Simulation.Map;

public static class VerticalPassageOpennessPolicy
{
    public static bool IsOpen(VerticalPassage passage) => IsOpen(passage.Kind);

    public static bool IsOpen(VerticalPassageKind kind) =>
        kind is not VerticalPassageKind.ExcavatedStairs;
}
