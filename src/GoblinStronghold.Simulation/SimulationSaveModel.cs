namespace GoblinStronghold.Simulation;

internal sealed class SimulationSaveModel
{
    public int FormatVersion { get; set; }

    public string DefinitionsId { get; set; } = string.Empty;

    public ulong WorldSeed { get; set; }

    public int MapGeneratorVersion { get; set; }

    public int MapWidth { get; set; }

    public int MapHeight { get; set; }

    public string MapFingerprint { get; set; } = string.Empty;

    public long CurrentTick { get; set; }

    public ulong NextEntityId { get; set; }

    public ulong NextEventSequence { get; set; }

    public ulong WorldVersion { get; set; }

    public List<PlantPatchSaveModel> PlantPatches { get; set; } = [];

    public List<ActorSaveModel> Actors { get; set; } = [];

    public List<ItemStackSaveModel> ItemStacks { get; set; } = [];

    public List<StorageZoneSaveModel> StorageZones { get; set; } = [];

    public List<CommandSaveModel> PendingCommands { get; set; } = [];

    public List<EventSaveModel> UndeliveredEvents { get; set; } = [];

    public List<WorldChangeSaveModel> UndeliveredWorldChanges { get; set; } = [];
}

internal sealed class PlantPatchSaveModel
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public Map.PlantKind Kind { get; set; }

    public int Biomass { get; set; }

    public int Capacity { get; set; }
}

internal sealed class ActorSaveModel
{
    public ulong Id { get; set; }

    public int Hunger { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public ulong CarriedStackId { get; set; }
}

internal sealed class ItemStackSaveModel
{
    public ulong Id { get; set; }

    public Resources.ResourceKind Resource { get; set; }

    public int Quantity { get; set; }

    public Resources.ItemLocationKind LocationKind { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public ulong OwnerId { get; set; }
}

internal sealed class StorageZoneSaveModel
{
    public ulong Id { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public Resources.ResourceKind AcceptedResource { get; set; }

    public int Capacity { get; set; }
}

internal sealed class CommandSaveModel
{
    public long ExecuteAt { get; set; }

    public ulong Sequence { get; set; }

    public SimulationCommandKind Kind { get; set; }

    public ulong Subject { get; set; }

    public ulong Target { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public Resources.ResourceKind Resource { get; set; }

    public int Amount { get; set; }
}

internal sealed class EventSaveModel
{
    public ulong Sequence { get; set; }

    public long Tick { get; set; }

    public SimulationEventKind Kind { get; set; }

    public ulong Subject { get; set; }

    public ulong Target { get; set; }

    public int Amount { get; set; }
}

internal sealed class WorldChangeSaveModel
{
    public ulong Version { get; set; }

    public long Tick { get; set; }

    public Map.WorldChangeKind Kind { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public int Amount { get; set; }
}
