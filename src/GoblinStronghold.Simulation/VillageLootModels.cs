using System.Collections.ObjectModel;
using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation;

public sealed class VillageLootContainerSnapshot
{
    public VillageLootContainerSnapshot(
        WorldObjectId structureId,
        GridPosition position,
        IEnumerable<CorpseItemSnapshot> contents)
    {
        StructureId = structureId;
        Position = position;
        Contents = new ReadOnlyCollection<CorpseItemSnapshot>(contents.ToArray());
    }

    public WorldObjectId StructureId { get; }

    public GridPosition Position { get; }

    public IReadOnlyList<CorpseItemSnapshot> Contents { get; }
}
