using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Resources;

public static class StoneMaterialPolicy
{
    private const ulong StoneVariantSampleKey = 0x53544F4E54595045UL;

    public static ResourceVariant VariantFor(
        WorldSeed seed,
        int mapWidth,
        GridPosition position)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mapWidth);
        var subject = new EntityId(
            checked((ulong)(position.Y * mapWidth + position.X) + 1));
        return DeterministicRandom.NextInt(
            seed,
            RandomDomain.Stone,
            subject,
            SimulationTick.Zero,
            StoneVariantSampleKey ^ (ulong)(uint)position.Z,
            minimumInclusive: 0,
            maximumExclusive: 2) == 0
            ? ResourceVariant.Sandstone
            : ResourceVariant.Granite;
    }
}
