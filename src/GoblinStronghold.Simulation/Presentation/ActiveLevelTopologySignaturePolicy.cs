using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Presentation;

public static class ActiveLevelTopologySignaturePolicy
{
    private const ulong Offset = 14_695_981_039_346_656_037UL;
    private const ulong Prime = 1_099_511_628_211UL;

    public static ulong Create(WorldMapState world, int activeLevel)
    {
        ArgumentNullException.ThrowIfNull(world);
        var signature = Add(Offset, unchecked((ulong)activeLevel));
        signature = AddPositions(signature, 1, world.ExcavatedCaveCells, activeLevel);
        signature = AddPositions(signature, 2, world.ExcavatedTerrainRamps, activeLevel);
        signature = AddPositions(signature, 3, world.HarvestedCaveFlora, activeLevel);
        signature = Add(signature, 4);
        foreach (var passage in world.CreateVerticalPassageSnapshot()
                     .Where(passage =>
                         passage.Upper.Z == activeLevel ||
                         passage.Lower.Z == activeLevel)
                     .OrderBy(passage => passage.Upper.Z)
                     .ThenBy(passage => passage.Upper.Y)
                     .ThenBy(passage => passage.Upper.X)
                     .ThenBy(passage => passage.Lower.Z)
                     .ThenBy(passage => passage.Lower.Y)
                     .ThenBy(passage => passage.Lower.X))
        {
            signature = AddPosition(signature, passage.Upper);
            signature = AddPosition(signature, passage.Lower);
            signature = Add(signature, (ulong)passage.Kind);
        }
        return signature;
    }

    private static ulong AddPositions(
        ulong signature,
        ulong category,
        IEnumerable<GridPosition> positions,
        int activeLevel)
    {
        signature = Add(signature, category);
        foreach (var position in positions
                     .Where(position => position.Z == activeLevel)
                     .OrderBy(position => position.Y)
                     .ThenBy(position => position.X))
        {
            signature = AddPosition(signature, position);
        }
        return signature;
    }

    private static ulong AddPosition(ulong signature, GridPosition position)
    {
        signature = Add(signature, unchecked((ulong)position.X));
        signature = Add(signature, unchecked((ulong)position.Y));
        return Add(signature, unchecked((ulong)position.Z));
    }

    private static ulong Add(ulong signature, ulong value) =>
        unchecked((signature ^ value) * Prime);
}
