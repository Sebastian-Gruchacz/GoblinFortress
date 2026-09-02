using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation.WorkPriorities;

public sealed record WorkTypePriorityDefinition(
    string Id,
    StoragePriority DefaultPriority,
    IReadOnlyList<WorkDesignationKind> DesignationKinds);

public readonly record struct WorkTypePrioritySnapshot(
    string Id,
    StoragePriority Priority);

public static class WorkTypePriorityCatalog
{
    private static readonly WorkTypePriorityDefinition[] Definitions =
    [
        Define("gathering", WorkDesignationKind.GatherFood, WorkDesignationKind.GatherReeds),
        Define("hauling", WorkDesignationKind.GatherBrushwood, WorkDesignationKind.GatherStone),
        Define("clearing", WorkDesignationKind.UprootBerryBush),
        Define("logging", WorkDesignationKind.FellTree),
        Define("quarrying", WorkDesignationKind.QuarryBoulder),
        Define("mining", WorkDesignationKind.MineRock),
        Define("ramp-carving", WorkDesignationKind.CarveRampDown,
            WorkDesignationKind.CarveRampUp),
        Define("scouting", WorkDesignationKind.Scout),
        Define("hunting", WorkDesignationKind.HuntAnimal),
        Define("cleaning", StoragePriority.Low, WorkDesignationKind.CleanBlood),
        Define("dismantling", WorkDesignationKind.DismantleWorldObject,
            WorkDesignationKind.DismantleStorageZone),
        Define("construction"),
        Define("crafting"),
    ];

    private static readonly IReadOnlyDictionary<string, WorkTypePriorityDefinition> ById =
        Definitions.ToDictionary(definition => definition.Id, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<WorkDesignationKind, WorkTypePriorityDefinition>
        ByKind = Definitions
            .SelectMany(definition => definition.DesignationKinds.Select(kind => (kind, definition)))
            .ToDictionary(item => item.kind, item => item.definition);

    public static IReadOnlyList<WorkTypePriorityDefinition> All => Definitions;

    public static WorkTypePriorityDefinition Get(WorkDesignationKind kind) =>
        ByKind.TryGetValue(kind, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown work type.");

    public static bool TryGet(string id, out WorkTypePriorityDefinition definition)
    {
        if (ById.TryGetValue(id, out var found))
        {
            definition = found;
            return true;
        }

        definition = null!;
        return false;
    }

    private static WorkTypePriorityDefinition Define(
        string id,
        params WorkDesignationKind[] kinds) =>
        Define(id, StoragePriority.Normal, kinds);

    private static WorkTypePriorityDefinition Define(
        string id,
        StoragePriority defaultPriority,
        params WorkDesignationKind[] kinds) =>
        new(id, defaultPriority, kinds);
}
