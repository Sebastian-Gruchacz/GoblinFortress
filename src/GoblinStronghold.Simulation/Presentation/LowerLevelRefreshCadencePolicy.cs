namespace GoblinStronghold.Simulation.Presentation;

public static class LowerLevelRefreshCadencePolicy
{
    public static double GetMinimumIntervalSeconds(
        double baseIntervalSeconds,
        int activeLevel,
        int cachedLevel)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseIntervalSeconds);
        if (cachedLevel >= activeLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cachedLevel),
                "A cached lower level must be below the active level.");
        }

        var depth = checked(activeLevel - cachedLevel);
        return baseIntervalSeconds * depth;
    }

    public static bool IsRebuildDue(
        double lastRebuildSeconds,
        double currentSeconds,
        double baseIntervalSeconds,
        int activeLevel,
        int cachedLevel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lastRebuildSeconds);
        ArgumentOutOfRangeException.ThrowIfNegative(currentSeconds);
        return currentSeconds - lastRebuildSeconds >= GetMinimumIntervalSeconds(
            baseIntervalSeconds,
            activeLevel,
            cachedLevel);
    }
}
