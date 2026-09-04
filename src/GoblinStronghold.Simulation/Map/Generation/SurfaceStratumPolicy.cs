namespace GoblinStronghold.Simulation.Map.Generation;

/// <summary>
/// Selects unconsolidated material above the geological rock core. The policy is
/// coordinate-based so initial generation and lazily materialized cave levels agree.
/// </summary>
internal static class SurfaceStratumPolicy
{
    public static LooseMaterialKind Select(
        WorldSeed seed,
        GridPosition position,
        MapCell surface,
        bool isReliefSlope)
    {
        var depth = surface.SurfaceLevel - position.Z;
        if (depth < 1)
        {
            return LooseMaterialKind.None;
        }

        var surfaceSample = Sample(seed, position.X, position.Y, 0, 0x534F494CUL);
        var rockySlope = isReliefSlope && surfaceSample % 100UL < 18UL;
        var coverDepth = rockySlope ? 0 : 1 + (int)((surfaceSample >> 8) & 1UL);
        if (depth <= coverDepth)
        {
            return surface.Terrain == TerrainKind.Sand
                ? LooseMaterialKind.Sand
                : LooseMaterialKind.Soil;
        }

        if (depth <= 10 && IsSandLens(seed, position, depth))
        {
            return LooseMaterialKind.Sand;
        }

        return LooseMaterialKind.None;
    }

    private static bool IsSandLens(WorldSeed seed, GridPosition position, int depth)
    {
        var clusterX = Math.DivRem(position.X, 3, out _);
        var clusterY = Math.DivRem(position.Y, 3, out _);
        var clusterZ = Math.DivRem(position.Z, 2, out _);
        var sample = Sample(seed, clusterX, clusterY, clusterZ, 0x53414E44UL);
        var threshold = depth <= 5 ? 14UL : 5UL;
        return sample % 100UL < threshold;
    }

    private static ulong Sample(
        WorldSeed seed,
        int x,
        int y,
        int z,
        ulong salt)
    {
        var value = seed.Value ^ salt ^
            ((ulong)(uint)x * 0x9E3779B185EBCA87UL) ^
            ((ulong)(uint)y * 0xC2B2AE3D27D4EB4FUL) ^
            ((ulong)(uint)z * 0x165667B19E3779F9UL);
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
