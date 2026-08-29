namespace GoblinStronghold.Simulation;

public enum RandomDomain : ulong
{
    Foraging = 1,
    MapGeneration = 2,
    Ecology = 3,
    Combat = 4,
    KnowledgeTransfer = 5,
    HumanVillage = 6,
    GoblinIdentity = 7,
    Brushwood = 8,
    Stone = 9,
    UndergroundFactions = 10,
}

public static class DeterministicRandom
{
    public static ulong Sample(
        WorldSeed worldSeed,
        RandomDomain domain,
        EntityId subject,
        SimulationTick tick,
        ulong sampleKey)
    {
        if (tick.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick));
        }

        var value = Mix(worldSeed.Value ^ 0x9E3779B97F4A7C15UL);
        value = Mix(value ^ (ulong)domain);
        value = Mix(value ^ subject.Value);
        value = Mix(value ^ (ulong)tick.Value);
        return Mix(value ^ sampleKey);
    }

    public static int NextInt(
        WorldSeed worldSeed,
        RandomDomain domain,
        EntityId subject,
        SimulationTick tick,
        ulong sampleKey,
        int minimumInclusive,
        int maximumExclusive)
    {
        if (minimumInclusive >= maximumExclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumExclusive));
        }

        var range = (ulong)((long)maximumExclusive - minimumInclusive);
        var rejectionLimit = ulong.MaxValue - (ulong.MaxValue % range);
        var attempt = 0UL;
        ulong sample;

        do
        {
            sample = Sample(worldSeed, domain, subject, tick, sampleKey + attempt);
            attempt = checked(attempt + 1);
        }
        while (sample >= rejectionLimit);

        return checked(minimumInclusive + (int)(sample % range));
    }

    private static ulong Mix(ulong value)
    {
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
