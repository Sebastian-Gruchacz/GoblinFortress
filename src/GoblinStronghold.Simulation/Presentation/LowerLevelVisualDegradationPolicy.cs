namespace GoblinStronghold.Simulation.Presentation;

public static class LowerLevelVisualDegradationPolicy
{
    public const float NearestLevelBrightness = 0.72f;
    public const float MinimumBrightness = 0.38f;
    public const float BrightnessLossPerLevel = 0.08f;

    public static float ResolveBrightness(int levelDistance)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(levelDistance, 1);
        return Math.Max(
            MinimumBrightness,
            NearestLevelBrightness - ((levelDistance - 1) * BrightnessLossPerLevel));
    }
}
