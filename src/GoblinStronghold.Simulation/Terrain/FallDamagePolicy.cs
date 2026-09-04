namespace GoblinStronghold.Simulation.Terrain;

internal static class FallDamagePolicy
{
    public static int GetDamage(int fallenLevels, int maximumHealth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fallenLevels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumHealth);

        var scaled = checked((long)maximumHealth * fallenLevels * fallenLevels);
        return checked((int)Math.Max(1, (scaled + 19) / 20));
    }
}
