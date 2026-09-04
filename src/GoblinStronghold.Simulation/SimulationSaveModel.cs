using GoblinStronghold.Simulation.Equipment;

namespace GoblinStronghold.Simulation;

internal sealed class SimulationSaveModel
{
    public int FormatVersion { get; set; }

    public string DefinitionsId { get; set; } = string.Empty;

    public string ClimateProfileId { get; set; } = string.Empty;

    public string PlayerPolityId { get; set; } = string.Empty;

    public ulong WorldSeed { get; set; }

    public int MapGeneratorVersion { get; set; }

    public string MapProfileId { get; set; } = string.Empty;

    public Map.Generation.RiverGenerationMode MapRiverMode { get; set; }

    public Map.Generation.RoadGenerationMode MapRoadMode { get; set; }

    public int MapWidth { get; set; }

    public int MapHeight { get; set; }

    public string MapFingerprint { get; set; } = string.Empty;

    public long CurrentTick { get; set; }

    public int CompostNutrients { get; set; }

    public ulong NextEntityId { get; set; }

    public ulong NextEventSequence { get; set; }

    public ulong WorldVersion { get; set; }

    public List<GoblinBudSaveModel> GoblinBuds { get; set; } = [];

    public List<CorpseSaveModel> Corpses { get; set; } = [];

    public List<Resources.ResourceVariant> StolenVillageEquipment { get; set; } = [];

    public ulong NextAnimalId { get; set; }

    public List<AnimalSaveModel>? Animals { get; set; }

    public List<UndergroundFactionSaveModel> UndergroundFactions { get; set; } = [];

    public GoblinRaidPhase RaidPhase { get; set; }

    public int RaidRallyX { get; set; }

    public int RaidRallyY { get; set; }

    public int RaidRallyZ { get; set; }

    public List<ulong> RaidPartyIds { get; set; } = [];

    public bool RaidRosterConfigured { get; set; }

    public int RaidTargetX { get; set; }

    public int RaidTargetY { get; set; }

    public int RaidTargetZ { get; set; }

    public int RaidTargetRadius { get; set; }

    public RaidDirective RaidDirectives { get; set; }

    public List<PlantPatchSaveModel> PlantPatches { get; set; } = [];

    public List<WorldObjectSaveModel> WorldObjects { get; set; } = [];

    public List<BloodStainSaveModel> BloodStains { get; set; } = [];

    public List<SurfaceGrimeSaveModel> SurfaceGrime { get; set; } = [];

    public List<GridPositionSaveModel> ReportedCleaningPositions { get; set; } = [];

    public List<GridPositionSaveModel> ExcavatedCaveCells { get; set; } = [];

    public List<GridPositionSaveModel> ExcavatedTerrainRamps { get; set; } = [];

    public List<VerticalPassageSaveModel> ExcavatedVerticalPassages { get; set; } = [];

    public List<GridPositionSaveModel> HarvestedCaveFlora { get; set; } = [];

    public bool ConnectedWaterActivated { get; set; }

    public HumanVillageSaveModel HumanVillage { get; set; } = new();

    public List<Map.CellVisibility> Visibility { get; set; } = [];

    public List<ActorSaveModel> Actors { get; set; } = [];

    public List<NavigationBeliefSaveModel> TribeNavigationBeliefs { get; set; } = [];

    public List<ItemStackSaveModel> ItemStacks { get; set; } = [];

    public List<StorageZoneSaveModel> StorageZones { get; set; } = [];

    public List<StorageAreaSaveModel> StorageAreas { get; set; } = [];

    public List<LogisticsNetworkSaveModel> LogisticsNetworks { get; set; } = [];

    public List<ResourcePrioritySaveModel> ResourcePriorities { get; set; } = [];

    public List<WorkTypePrioritySaveModel> WorkTypePriorities { get; set; } = [];

    public List<ConstructionSiteSaveModel> ConstructionSites { get; set; } = [];

    public List<CraftingOrderSaveModel> CraftingOrders { get; set; } = [];

    public List<WorkDesignationSaveModel> WorkDesignations { get; set; } = [];

    public List<CommandSaveModel> PendingCommands { get; set; } = [];

    public List<EventSaveModel> UndeliveredEvents { get; set; } = [];

    public List<WorldChangeSaveModel> UndeliveredWorldChanges { get; set; } = [];
}

internal sealed class CorpseSaveModel
{
    public ulong Id { get; set; }

    public CorpseKind Kind { get; set; }

    public string Name { get; set; } = string.Empty;

    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public long CreatedAtTick { get; set; }

    public int ContainedWater { get; set; }

    public int? EdiblePortions { get; set; }

    public CorpseDirective Directives { get; set; }

    public GoblinSkill InheritableSkills { get; set; }

    public GoblinTrait InheritableTraits { get; set; }

    public int InheritableForagingExperience { get; set; }

    public int InheritableHaulingExperience { get; set; }

    public int InheritableBuildingExperience { get; set; }

    public int InheritableForagingPreference { get; set; }

    public int InheritableHaulingPreference { get; set; }

    public int InheritableBuildingPreference { get; set; }

    public List<CorpseItemSaveModel> Contents { get; set; } = [];
}

internal sealed class CorpseItemSaveModel
{
    public Resources.ResourceKind Resource { get; set; }

    public Resources.FoodKind FoodKind { get; set; }

    public Resources.ResourceVariant Variant { get; set; }

    public int Quantity { get; set; }

    public int UnitWeight { get; set; }
}

internal sealed class BloodStainSaveModel
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public int Volume { get; set; }

    public BloodSurfaceKind Surface { get; set; }

    public long CreatedAtTick { get; set; }

    public long LastChangedAtTick { get; set; }
}

internal sealed class SurfaceGrimeSaveModel
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public int Volume { get; set; }

    public long CreatedAtTick { get; set; }

    public long LastChangedAtTick { get; set; }
}

internal sealed class VerticalPassageSaveModel
{
    public int UpperX { get; set; }

    public int UpperY { get; set; }

    public int UpperZ { get; set; }

    public int LowerX { get; set; }

    public int LowerY { get; set; }

    public int LowerZ { get; set; }

    public Map.VerticalPassageKind Kind { get; set; }
}

internal sealed class AnimalSaveModel
{
    public ulong Id { get; set; }
    public AnimalKind Kind { get; set; }
    public AnimalSex Sex { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public AnimalActivity Activity { get; set; }
    public int Health { get; set; }
    public int Hunger { get; set; }
    public int Fatigue { get; set; }
    public int CarriedGrime { get; set; }
    public long AgeTicks { get; set; }
}

internal sealed class GoblinBudSaveModel
{
    public ulong Id { get; set; }

    public ulong ParentId { get; set; }

    public ulong OriginCorpseId { get; set; }

    public GoblinSkill OriginSkills { get; set; }

    public GoblinTrait OriginTraits { get; set; }

    public int OriginForagingExperience { get; set; }

    public int OriginHaulingExperience { get; set; }

    public int OriginBuildingExperience { get; set; }

    public int OriginForagingPreference { get; set; }

    public int OriginHaulingPreference { get; set; }

    public int OriginBuildingPreference { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public int RemainingCareTicks { get; set; }
}

internal sealed class ResourcePrioritySaveModel
{
    public Resources.ResourceKind Resource { get; set; }

    public Resources.StoragePriority Priority { get; set; }
}

internal sealed class WorkTypePrioritySaveModel
{
    public string Id { get; set; } = string.Empty;

    public Resources.StoragePriority Priority { get; set; }
}

internal sealed class HumanVillageSaveModel
{
    public string PolityId { get; set; } = string.Empty;

    public int Population { get; set; }

    public int FoodStock { get; set; }

    public int GrainStock { get; set; }

    public int WoodStock { get; set; }

    public int GoodsStock { get; set; }

    public int WaterStock { get; set; }

    public int StorehouseCount { get; set; }

    public bool GoblinAttackOrdered { get; set; }

    public int Hostility { get; set; }

    public long LastIntruderSeenTick { get; set; }

    public int GuardHitPoints { get; set; }

    public int? TreeFellingX { get; set; }

    public int? TreeFellingY { get; set; }

    public int? TreeFellingZ { get; set; }

    public int TreeFellingProgress { get; set; }

    public int GoodsWorkProgress { get; set; }

    public int? StorehouseSiteX { get; set; }

    public int? StorehouseSiteY { get; set; }

    public int? StorehouseSiteZ { get; set; }

    public int StorehouseWorkProgress { get; set; }

    public List<HumanCohortSaveModel> Cohorts { get; set; } = [];

    public List<HumanVillagerSaveModel> Villagers { get; set; } = [];

    public List<HumanFieldSaveModel> Fields { get; set; } = [];
}

internal sealed class HumanVillagerSaveModel
{
    public int Id { get; set; }

    public ActorSex Sex { get; set; }

    public HumanCohortRole Role { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public HumanCohortTask Task { get; set; }

    public int SkillLevel { get; set; }

    public HumanTool Tools { get; set; }

    public int Health { get; set; }

    public int Fatigue { get; set; }

    public int Hunger { get; set; }

    public int Thirst { get; set; }

    public int CarriedGrime { get; set; }

    public int WorkProgress { get; set; }
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
    public int WorkProgress { get; set; }
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

    public Resources.ResourceVariant MaterialVariant { get; set; }

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

    public ActorSex Sex { get; set; }

    public string PolityId { get; set; } = string.Empty;

    public GoblinSkill KnownSkills { get; set; }

    public GoblinTrait KnownTraits { get; set; }

    public PersonalEquipment Equipment { get; set; }

    public int ForagingExperience { get; set; }

    public int HaulingExperience { get; set; }

    public int BuildingExperience { get; set; }

    public int? ForagingPreference { get; set; }

    public int? HaulingPreference { get; set; }

    public int? BuildingPreference { get; set; }

    public int Hunger { get; set; }

    public int Fatigue { get; set; }

    public int Health { get; set; }

    public int Mana { get; set; }

    public int Thirst { get; set; }

    public int PersonalFood { get; set; }

    public Resources.FoodKind PersonalFoodKind { get; set; }

    public List<Resources.FoodKind>? PersonalFoodKinds { get; set; }

    public List<long>? PersonalFoodFreshUntilTicks { get; set; }

    public int PersonalWater { get; set; }

    public int PersonalStoneAmmo { get; set; }

    public int BloodFootprintSteps { get; set; }

    public int CarriedGrime { get; set; }

    public int BleedingTicksRemaining { get; set; }

    public long? BirthTick { get; set; }

    public long? MaturesAtTick { get; set; }

    public long? AgeOffsetTicks { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public ulong CarriedStackId { get; set; }

    public ulong CarriedCorpseId { get; set; }

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

    public ActorTacticalOrderKind TacticalOrderKind { get; set; }

    public int TacticalCenterX { get; set; }

    public int TacticalCenterY { get; set; }

    public int TacticalCenterZ { get; set; }

    public int TacticalRadius { get; set; }

    public int PatrolPointIndex { get; set; }

    public List<GridPositionSaveModel> PatrolPoints { get; set; } = [];

    public ulong TacticalTargetEntityId { get; set; }

    public long DispatcherSuspendedUntilTick { get; set; }

    public List<NavigationBeliefSaveModel> NavigationBeliefs { get; set; } = [];

    public List<NavigationEdgeSaveModel> PendingNavigationReports { get; set; } = [];
}

internal sealed class NavigationEdgeSaveModel
{
    public int FirstX { get; set; }

    public int FirstY { get; set; }

    public int FirstZ { get; set; }

    public int SecondX { get; set; }

    public int SecondY { get; set; }

    public int SecondZ { get; set; }
}

internal sealed class NavigationBeliefSaveModel
{
    public int FirstX { get; set; }

    public int FirstY { get; set; }

    public int FirstZ { get; set; }

    public int SecondX { get; set; }

    public int SecondY { get; set; }

    public int SecondZ { get; set; }

    public Map.NavigationBeliefStatus Status { get; set; }

    public long ObservedAt { get; set; }

    public long ReceivedAt { get; set; }

    public ulong SourceActorId { get; set; }

    public byte Confidence { get; set; }

    public bool IsDirectObservation { get; set; }
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

    public Resources.ResourceVariant Variant { get; set; }

    public int Quantity { get; set; }

    public Resources.ItemLocationKind LocationKind { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public ulong OwnerId { get; set; }

    public Resources.StoragePriority? HaulPriority { get; set; }

    public long? FreshUntilTick { get; set; }
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

    public Resources.MineralStorageFilter? MineralFilter { get; set; }

    public int? SlotCount { get; set; }

    public int? StackCapacity { get; set; }

    public bool? SeparatesItemTypes { get; set; }

    public Resources.StorageCapability? Capabilities { get; set; }

    public ulong LogisticsNetworkId { get; set; }

    public ulong StorageAreaId { get; set; }

    public Resources.StorageProviderKind ProviderKind { get; set; }

    public Resources.StorageResourceFilter ResourceFilter { get; set; }
}

internal sealed class StorageAreaSaveModel
{
    public ulong Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public ulong LogisticsNetworkId { get; set; }

    public List<GridPositionSaveModel> Footprint { get; set; } = [];
}

internal sealed class LogisticsNetworkSaveModel
{
    public ulong Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<ulong> AssignedHaulerIds { get; set; } = [];

    public List<ulong> SourceStorageZoneIds { get; set; } = [];
}

internal sealed class WorkDesignationSaveModel
{
    public ulong Id { get; set; }

    public WorkDesignationKind Kind { get; set; }

    public int TargetX { get; set; }

    public int TargetY { get; set; }

    public int TargetZ { get; set; }

    public bool HasRampDestination { get; set; }

    public int RampDestinationX { get; set; }

    public int RampDestinationY { get; set; }

    public int RampDestinationZ { get; set; }

    public ulong TargetEntityId { get; set; }

    public ulong OrderId { get; set; }

    public Resources.StoragePriority? Priority { get; set; }

    public bool IsSuspended { get; set; }
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

    public Resources.ResourceKind? RequiredResource { get; set; }

    public Resources.ResourceVariant? RequiredVariant { get; set; }

    public int RequiredWood { get; set; }

    public int DeliveredWood { get; set; }

    public Resources.ResourceVariant DeliveredVariant { get; set; }

    public int RemainingWorkTicks { get; set; }

    public int TotalWorkTicks { get; set; }

    public GoblinSkill RequiredSkills { get; set; }

    public int MinimumBuildingLevel { get; set; }

    public PersonalEquipment RequiredEquipment { get; set; }

    public ToolFunction RequiredToolFunction { get; set; }

    public int MinimumToolLevel { get; set; }

    public Resources.StoragePriority? Priority { get; set; }

    public ulong OrderId { get; set; }

    public int SequenceIndex { get; set; }
}

internal sealed class CraftingOrderSaveModel
{
    public ulong Id { get; set; }

    public CraftingRecipeKind Recipe { get; set; }

    public int WorkshopX { get; set; }

    public int WorkshopY { get; set; }

    public int WorkshopZ { get; set; }

    public List<CraftingDeliveredMaterialSaveModel> DeliveredMaterials { get; set; } = [];

    public int RemainingWorkTicks { get; set; }

    public bool IsRepeating { get; set; }

    public bool IsAutomatic { get; set; }
}

internal sealed class CraftingDeliveredMaterialSaveModel
{
    public Resources.ResourceKind Resource { get; set; }

    public Resources.FoodKind FoodKind { get; set; }

    public Resources.ResourceVariant Variant { get; set; }

    public int Quantity { get; set; }

    public long? FreshUntilTick { get; set; }
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

    public string Text { get; set; } = string.Empty;

    public Resources.ResourceVariant MaterialVariant { get; set; }
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
