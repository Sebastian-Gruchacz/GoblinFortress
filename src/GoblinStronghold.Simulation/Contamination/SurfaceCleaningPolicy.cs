namespace GoblinStronghold.Simulation.Contamination;

internal static class SurfaceCleaningPolicy
{
    internal const int GrimeVolumePerSeverityLevel = 12;
    internal const int ToleratedGrimeLevelCount = 3;
    internal const int AutomaticCleaningMinimumGrimeVolume =
        (GrimeVolumePerSeverityLevel * ToleratedGrimeLevelCount) + 1;

    internal static bool ShouldStartAutonomousCleaning(
        bool hasBlood,
        int grimeVolume) =>
        hasBlood || grimeVolume >= AutomaticCleaningMinimumGrimeVolume;
}
