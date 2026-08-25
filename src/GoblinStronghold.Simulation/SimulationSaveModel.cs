namespace GoblinStronghold.Simulation;

internal sealed class SimulationSaveModel
{
    public int FormatVersion { get; set; }

    public string DefinitionsId { get; set; } = string.Empty;

    public string ClimateProfileId { get; set; } = string.Empty;

    public ulong WorldSeed { get; set; }

    public int MapGeneratorVersion { get; set; }

    public int MapWidth { get; set; }

    public int MapHeight { get; set; }

    public string MapFingerprint { get; set; } = string.Empty;

    public long CurrentTick { get; set; }

    public ulong NextEntityId { get; set; }

    public ulong NextEventSequence { get; set; }

    public ulong WorldVersion { get; set; }

    public GoblinRaidPhase RaidPhase { get; set; }

    public int RaidRallyX { get; set; }

    public int RaidRallyY { get; set; }

    public int RaidRallyZ { get; set; }

    public List<PlantPatchSaveModel> PlantPatches { get; set; } = [];

    public List<WorldObjectSaveModel> WorldObjects { get; set; } = [];

    public HumanVillageSaveModel HumanVillage { get; set; } = new();

    public List<Map.CellVisibility> Visibility { get; set; } = [];

    public List<ActorSaveModel> Actors { get; set; } = [];

    public List<ItemStackSaveModel> ItemStacks { get; set; } = [];

    public List<StorageZoneSaveModel> StorageZones { get; set; } = [];

    public List<ResourcePrioritySaveModel> ResourcePriorities { get; set; } = [];

    public List<ConstructionSiteSaveModel> ConstructionSites { get; set; } = [];

    public List<WorkDesignationSaveModel> WorkDesignations { get; set; } = [];

    public List<CommandSaveModel> PendingCommands { get; set; } = [];

    public List<EventSaveModel> UndeliveredEvents { get; set; } = [];

    public List<WorldChangeSaveModel> UndeliveredWorldChanges { get; set; } = [];
}

internal sealed class ResourcePrioritySaveModel
{
    public Resources.ResourceKind Resource { get; set; }

    public Resources.StoragePriority Priority { get; set; }
}

internal sealed class HumanVillageSaveModel
{
    public int Population { get; set; }

    public int FoodStock { get; set; }

    public int WoodStock { get; set; }

    public int GoodsStock { get; set; }

    public int WaterStock { get; set; }

    public int StorehouseCount { get; set; }

    public bool GoblinAttackOrdered { get; set; }

    public int Hostility { get; set; }

    public long LastIntruderSeenTick { get; set; }

    public int GuardHitPoints { get; set; }

    public List<HumanCohortSaveModel> Cohorts { get; set; } = [];

    public List<HumanFieldSaveModel> Fields { get; set; } = [];
}

internal sealed class HumanCohortSaveModel
{
    public int Id { get; set; }

    public HumanCohortRole Role { get; set; }

    public int Population { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public HumanCohortTask Task { get; set; }

    public int SkillLevel { get; set; }

    public HumanTool Tools { get; set; }
}

internal sealed class HumanFieldSaveModel
{
    public int Id { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public HumanFieldPhase Phase { get; set; }
    public int GrowthDays { get; set; }
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

internal sealed class WorldObjectSaveModel
{
    public ulong Id { get; set; }

    public Map.WorldObjectKind Kind { get; set; }

    public Map.WorldObjectOwner Owner { get; set; }

    public int AnchorX { get; set; }

    public int AnchorY { get; set; }

    public int AnchorZ { get; set; }

    public Map.CardinalOrientation Orientation { get; set; }

    public List<WorldObjectPartSaveModel> Parts { get; set; } = [];
}

internal sealed class WorldObjectPartSaveModel
{
    public int RelativeX { get; set; }

    public int RelativeY { get; set; }

    public int RelativeZ { get; set; }

    public Map.SpatialOccupancyChannel Channel { get; set; }

    public Map.WorldObjectPartKind Kind { get; set; }
}

internal sealed class ActorSaveModel
{
    public ulong Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public GoblinSkill KnownSkills { get; set; }

    public GoblinTrait KnownTraits { get; set; }

    public PersonalEquipment Equipment { get; set; }

    public int ForagingExperience { get; set; }

    public int HaulingExperience { get; set; }

    public int BuildingExperience { get; set; }

    public int Hunger { get; set; }

    public int Fatigue { get; set; }

    public int Health { get; set; }

    public int Thirst { get; set; }

    public int PersonalFood { get; set; }

    public Resources.FoodKind PersonalFoodKind { get; set; }

    public int PersonalWater { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public ulong CarriedStackId { get; set; }

    public ActorJobKind JobKind { get; set; }

    public ActorJobPhase JobPhase { get; set; }

    public ActorJobStage JobStage { get; set; }

    public int JobTargetX { get; set; }

    public int JobTargetY { get; set; }

    public int JobTargetZ { get; set; }

    public int RemainingWorkTicks { get; set; }

    public ulong SourceStackId { get; set; }

    public ulong DestinationZoneId { get; set; }

    public int ReservedQuantity { get; set; }

    public List<GridPositionSaveModel> RemainingRoute { get; set; } = [];

    public ActorJobKind SuspendedJobKind { get; set; }

    public int SuspendedTargetX { get; set; }

    public int SuspendedTargetY { get; set; }

    public int SuspendedTargetZ { get; set; }
}

internal sealed class GridPositionSaveModel
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }
}

internal sealed class ItemStackSaveModel
{
    public ulong Id { get; set; }

    public Resources.ResourceKind Resource { get; set; }

    public Resources.FoodKind FoodKind { get; set; }

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

    public int DesiredQuantity { get; set; }

    public ulong AssignedHaulerId { get; set; }

    public ulong SourceStorageZoneId { get; set; }

    public Resources.StoragePriority Priority { get; set; }
}

internal sealed class WorkDesignationSaveModel
{
    public ulong Id { get; set; }

    public WorkDesignationKind Kind { get; set; }

    public int TargetX { get; set; }

    public int TargetY { get; set; }

    public int TargetZ { get; set; }

    public ulong TargetEntityId { get; set; }
}

internal sealed class ConstructionSiteSaveModel
{
    public ulong Id { get; set; }

    public ConstructionKind Kind { get; set; }

    public int AnchorX { get; set; }

    public int AnchorY { get; set; }

    public int AnchorZ { get; set; }

    public int EndX { get; set; }

    public int EndY { get; set; }

    public int EndZ { get; set; }

    public int RequiredWood { get; set; }

    public int DeliveredWood { get; set; }

    public int RemainingWorkTicks { get; set; }

    public int TotalWorkTicks { get; set; }

    public GoblinSkill RequiredSkills { get; set; }

    public int MinimumBuildingLevel { get; set; }

    public PersonalEquipment RequiredEquipment { get; set; }
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

    public int EndX { get; set; }

    public int EndY { get; set; }

    public int EndZ { get; set; }

    public ConstructionKind Construction { get; set; }

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
