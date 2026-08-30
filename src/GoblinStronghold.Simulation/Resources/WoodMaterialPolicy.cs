using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Resources;

public static class WoodMaterialPolicy
{
    private const ulong WoodVariantSampleKey = 0x574F4F4454595045UL;
    private static readonly ResourceVariant[] Variants =
    [
        ResourceVariant.OakWood,
        ResourceVariant.ChestnutWood,
        ResourceVariant.BirchWood,
        ResourceVariant.WalnutWood,
        ResourceVariant.AppleWood,
        ResourceVariant.PineWood,
    ];

    public static ResourceVariant VariantFor(
        WorldSeed seed,
        int mapWidth,
        GridPosition position)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mapWidth);
        var subject = new EntityId(
            checked((ulong)(position.Y * mapWidth + position.X) + 1));
        return Variants[DeterministicRandom.NextInt(
            seed,
            RandomDomain.Brushwood,
            subject,
            SimulationTick.Zero,
            WoodVariantSampleKey ^ (ulong)(uint)position.Z,
            minimumInclusive: 0,
            maximumExclusive: Variants.Length)];
    }
}
