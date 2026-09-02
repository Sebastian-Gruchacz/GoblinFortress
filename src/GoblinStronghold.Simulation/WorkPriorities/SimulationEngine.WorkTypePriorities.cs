using GoblinStronghold.Simulation.Resources;
using GoblinStronghold.Simulation.WorkPriorities;

namespace GoblinStronghold.Simulation;

public sealed partial class SimulationEngine
{
    private readonly SortedDictionary<string, StoragePriority>
        _workTypePriorities = [];

    public IReadOnlyList<WorkTypePrioritySnapshot> GetWorkTypePriorities() =>
        WorkTypePriorityCatalog.All
            .Select(definition => new WorkTypePrioritySnapshot(
                definition.Id,
                GetWorkTypePriority(definition.Id)))
            .ToArray();

    private void InitializeWorkTypePriorities()
    {
        foreach (var definition in WorkTypePriorityCatalog.All)
        {
            _workTypePriorities[definition.Id] = definition.DefaultPriority;
        }
    }

    private StoragePriority GetWorkTypePriority(WorkDesignationKind kind) =>
        GetWorkTypePriority(WorkTypePriorityCatalog.Get(kind).Id);

    private StoragePriority GetWorkTypePriority(string id) =>
        _workTypePriorities.GetValueOrDefault(
            id,
            WorkTypePriorityCatalog.TryGet(id, out var definition)
                ? definition.DefaultPriority
                : StoragePriority.Normal);

    private void LoadWorkTypePriorities(IEnumerable<WorkTypePrioritySaveModel> models)
    {
        InitializeWorkTypePriorities();
        foreach (var model in models)
        {
            if (!WorkTypePriorityCatalog.TryGet(model.Id, out var definition) ||
                !Enum.IsDefined(model.Priority))
            {
                throw new InvalidDataException("The save contains an invalid work-type priority.");
            }

            _workTypePriorities[definition.Id] = model.Priority;
        }
    }

    private bool TryExecuteConfigureWorkTypePriority(SimulationCommand command)
    {
        if (!WorkTypePriorityCatalog.TryGet(command.Text, out var definition))
        {
            return false;
        }

        _workTypePriorities[definition.Id] = (StoragePriority)command.Amount;
        return true;
    }
}
