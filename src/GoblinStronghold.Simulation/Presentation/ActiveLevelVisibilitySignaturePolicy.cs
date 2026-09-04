using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Presentation;

public static class ActiveLevelVisibilitySignaturePolicy
{
    public static ulong Create(
        int level,
        PresentationCellBounds bounds,
        Func<GridPosition, CellVisibility> getVisibility)
    {
        ArgumentNullException.ThrowIfNull(getVisibility);
        const ulong offset = 14_695_981_039_346_656_037UL;
        const ulong prime = 1_099_511_628_211UL;
        var signature = offset;
        for (var y = bounds.MinimumY; y < bounds.MaximumY; y++)
        {
            for (var x = bounds.MinimumX; x < bounds.MaximumX; x++)
            {
                signature = (signature ^ (uint)getVisibility(
                    new GridPosition(x, y, level))) * prime;
            }
        }

        return signature;
    }
}
