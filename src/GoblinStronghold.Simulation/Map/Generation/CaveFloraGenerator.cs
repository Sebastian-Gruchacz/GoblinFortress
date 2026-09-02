namespace GoblinStronghold.Simulation.Map.Generation;

public enum CaveFloraKind : byte
{
    GlowcapCluster = 1,
    CaveMoss = 2,
    LichenPatch = 3,
    GnarledCaveTree = 4,
    CaveMushroomCluster = 5,
}

public readonly record struct CaveFloraPatch(
    GridPosition Position,
    CaveFloraKind Kind,
    byte Variant);

public static class CaveFloraGenerator
{
    public const int DeepestFloraLevel = -3;

    public static bool TryGet(
        GeneratedMap map,
        GridPosition position,
        out CaveFloraPatch flora)
    {
        ArgumentNullException.ThrowIfNull(map);
        flora = default;
        if (position.Z is >= 0 or < DeepestFloraLevel ||
            !map.IsCavePosition(position))
        {
            return false;
        }

        var cave = map.GetCaveCell(position);
        if (cave.Kind != CaveCellKind.Floor || cave.Fluid != CellFluidKind.None)
        {
            return false;
        }

        var subject = new EntityId(checked(
            (ulong)((-position.Z - 1) * map.CellCount) +
            (ulong)(position.Y * map.Width + position.X) + 1UL));
        var habitat = ResolveHabitat(map, position);
        var occurrence = DeterministicRandom.NextInt(
            map.Seed,
            RandomDomain.Ecology,
            subject,
            SimulationTick.Zero,
            sampleKey: 0x43415645464C4F52UL,
            minimumInclusive: 0,
            maximumExclusive: 1_000);
        var frequency = position.Z switch
        {
            -1 => 38,
            -2 => 34,
            _ => 30,
        };
        frequency += habitat switch
        {
            < 32 => 76,
            > 86 => -18,
            _ => 0,
        };
        if (occurrence >= frequency)
        {
            return false;
        }

        var kindRoll = DeterministicRandom.NextInt(
            map.Seed,
            RandomDomain.Ecology,
            subject,
            SimulationTick.Zero,
            sampleKey: 0x434156454B494E44UL,
            minimumInclusive: 0,
            maximumExclusive: 100);
        var wallNeighbors = CountWallNeighbors(map, position);
        var kind = wallNeighbors >= 2
            ? kindRoll switch
            {
                < 37 => CaveFloraKind.CaveMoss,
                < 69 => CaveFloraKind.LichenPatch,
                < 84 => CaveFloraKind.CaveMushroomCluster,
                < 96 => CaveFloraKind.GlowcapCluster,
                _ => CaveFloraKind.GnarledCaveTree,
            }
            : position.Z switch
        {
            -1 when kindRoll < 28 => CaveFloraKind.CaveMoss,
            -1 when kindRoll < 53 => CaveFloraKind.LichenPatch,
            -1 when kindRoll < 80 => CaveFloraKind.CaveMushroomCluster,
            -1 when kindRoll < 95 => CaveFloraKind.GlowcapCluster,
            -2 when kindRoll < 28 => CaveFloraKind.CaveMushroomCluster,
            -2 when kindRoll < 56 => CaveFloraKind.GlowcapCluster,
            -2 when kindRoll < 76 => CaveFloraKind.CaveMoss,
            -2 when kindRoll < 93 => CaveFloraKind.LichenPatch,
            -3 when kindRoll < 34 => CaveFloraKind.GlowcapCluster,
            -3 when kindRoll < 59 => CaveFloraKind.CaveMushroomCluster,
            -3 when kindRoll < 76 => CaveFloraKind.LichenPatch,
            -3 when kindRoll < 90 => CaveFloraKind.CaveMoss,
            _ => CaveFloraKind.GnarledCaveTree,
        };
        var variant = (byte)DeterministicRandom.NextInt(
            map.Seed,
            RandomDomain.Ecology,
            subject,
            SimulationTick.Zero,
            sampleKey: 0x4341564556415249UL,
            minimumInclusive: 0,
            maximumExclusive: 4);
        flora = new CaveFloraPatch(position, kind, variant);
        return true;
    }

    private static int ResolveHabitat(GeneratedMap map, GridPosition position)
    {
        const int pocketSize = 6;
        var pocketWidth = (map.Width + pocketSize - 1) / pocketSize;
        var pocketHeight = (map.Height + pocketSize - 1) / pocketSize;
        var pocketX = position.X / pocketSize;
        var pocketY = position.Y / pocketSize;
        var pocketSubject = new EntityId(checked(
            (ulong)((-position.Z - 1) * pocketWidth * pocketHeight) +
            (ulong)(pocketY * pocketWidth + pocketX) + 1UL));
        return DeterministicRandom.NextInt(
            map.Seed,
            RandomDomain.Ecology,
            pocketSubject,
            SimulationTick.Zero,
            sampleKey: 0x4341564548414249UL,
            minimumInclusive: 0,
            maximumExclusive: 100);
    }

    private static int CountWallNeighbors(GeneratedMap map, GridPosition position)
    {
        var result = 0;
        foreach (var offset in CardinalOffsets)
        {
            var x = position.X + offset.X;
            var y = position.Y + offset.Y;
            if (x < 0 || x >= map.Width || y < 0 || y >= map.Height ||
                map.GetCaveCell(new GridPosition(x, y, position.Z)).Kind ==
                CaveCellKind.SolidRock)
            {
                result++;
            }
        }
        return result;
    }

    private static readonly (int X, int Y)[] CardinalOffsets =
        [(0, -1), (1, 0), (0, 1), (-1, 0)];
}
