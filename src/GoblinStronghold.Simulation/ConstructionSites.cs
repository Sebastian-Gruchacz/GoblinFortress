using System.Collections.ObjectModel;
using GoblinStronghold.Simulation.Construction;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using GoblinStronghold.Simulation.Workshops;

namespace GoblinStronghold.Simulation;

public readonly record struct ConstructionMaterialSnapshot(
    ResourceKind Resource,
    ResourceVariant Variant,
    ResourceVariant DeliveredVariant,
    int RequiredQuantity,
    int DeliveredQuantity)
{
    public int MissingQuantity => Math.Max(0, RequiredQuantity - DeliveredQuantity);
}

public readonly record struct ConstructionCapabilityRequirements(
    GoblinSkill RequiredSkills,
    int MinimumBuildingLevel,
    PersonalEquipment RequiredEquipment);

public enum ConstructionReadinessState : byte
{
    NoAvailableMaterials = 0,
    NoAvailableSupplier = 1,
    NoReachableMaterialSource = 2,
    WaitingForSupplier = 3,
    MaterialsInTransit = 4,
    NoCapableBuilder = 5,
    NoReachableBuilder = 6,
    WaitingForBuilder = 7,
    Building = 8,
    AwaitingSiteClearance = 9,
}

public readonly record struct ConstructionReadinessDiagnostic(
    EntityId SiteId,
    ConstructionReadinessState State,
    int MissingMaterialQuantity,
    int InTransitQuantity,
    int AvailableMaterialQuantity,
    int MatchingSourceCount,
    int CapableBuilderCount);

public sealed class ConstructionSiteSnapshot
{
    internal ConstructionSiteSnapshot(
        EntityId id,
        ConstructionKind kind,
        GridPosition anchor,
        GridPosition end,
        IReadOnlyList<GridPosition> footprint,
        IReadOnlyList<ConstructionMaterialSnapshot> materials,
        int remainingWorkTicks,
        int totalWorkTicks,
        ConstructionCapabilityRequirements capabilities,
        StoragePriority priority)
    {
        Id = id;
        Kind = kind;
        Anchor = anchor;
        End = end;
        Footprint = new ReadOnlyCollection<GridPosition>(footprint.ToArray());
        Materials = new ReadOnlyCollection<ConstructionMaterialSnapshot>(materials.ToArray());
        RemainingWorkTicks = remainingWorkTicks;
        TotalWorkTicks = totalWorkTicks;
        Capabilities = capabilities;
        Priority = priority;
    }

    public EntityId Id { get; }

    public ConstructionKind Kind { get; }

    public GridPosition Anchor { get; }

    public GridPosition End { get; }

    public IReadOnlyList<GridPosition> Footprint { get; }

    public IReadOnlyList<ConstructionMaterialSnapshot> Materials { get; }

    public int RemainingWorkTicks { get; }

    public int TotalWorkTicks { get; }

    public ConstructionCapabilityRequirements Capabilities { get; }

    public StoragePriority Priority { get; }

    public bool HasAllMaterials => Materials.All(material => material.MissingQuantity == 0);
}

internal sealed class ConstructionSiteState(
    EntityId id,
    ConstructionKind kind,
    GridPosition anchor,
    GridPosition end,
    ResourceKind requiredResource,
    ResourceVariant requiredVariant,
    int requiredQuantity,
    int deliveredQuantity,
    ResourceVariant deliveredVariant,
    int remainingWorkTicks,
    int totalWorkTicks,
    ConstructionCapabilityRequirements capabilities,
    StoragePriority priority,
    EntityId orderId,
    int sequenceIndex)
{
    public EntityId Id { get; } = id;

    public ConstructionKind Kind { get; } = kind;

    public GridPosition Anchor { get; } = anchor;

    public GridPosition End { get; } = end;

    public ResourceKind RequiredResource { get; } = requiredResource;

    public ResourceVariant RequiredVariant { get; } = requiredVariant;

    public int RequiredQuantity { get; } = requiredQuantity;

    public int DeliveredQuantity { get; private set; } = deliveredQuantity;

    public ResourceVariant DeliveredVariant { get; private set; } = deliveredVariant;

    public int RemainingWorkTicks { get; set; } = remainingWorkTicks;

    public int TotalWorkTicks { get; } = totalWorkTicks;

    public ConstructionCapabilityRequirements Capabilities { get; } = capabilities;

    public StoragePriority Priority { get; set; } = priority;

    public EntityId OrderId { get; } = orderId;

    public int SequenceIndex { get; } = sequenceIndex;

    public int MissingQuantity => Math.Max(0, RequiredQuantity - DeliveredQuantity);

    public bool HasAllMaterials => MissingQuantity == 0;

    public void Deliver(ResourceKind resource, ResourceVariant variant, int quantity)
    {
        var retainsMaterialIdentity = ConstructionBlueprintCatalog.RetainsMaterialIdentity(Kind);
        if (resource != RequiredResource || quantity <= 0 || quantity > MissingQuantity ||
            RequiredVariant != ResourceVariant.None && variant != RequiredVariant ||
            retainsMaterialIdentity && DeliveredQuantity > 0 && variant != DeliveredVariant)
        {
            throw new InvalidOperationException(
                "The construction site cannot accept this material delivery.");
        }

        if (retainsMaterialIdentity && DeliveredQuantity == 0)
        {
            DeliveredVariant = variant;
        }
        DeliveredQuantity = checked(DeliveredQuantity + quantity);
    }

    public IReadOnlyList<GridPosition> GetFootprint() =>
        ConstructionBlueprintDefinitions.Get(Kind).GetFootprint(Anchor, End);

    public ConstructionSiteSnapshot ToSnapshot() => new(
        Id,
        Kind,
        Anchor,
        End,
        GetFootprint(),
        [new ConstructionMaterialSnapshot(
            RequiredResource,
            RequiredVariant,
            DeliveredVariant,
            RequiredQuantity,
            DeliveredQuantity)],
        RemainingWorkTicks,
        TotalWorkTicks,
        Capabilities,
        Priority);
}

internal static class ConstructionBlueprintCatalog
{
    public static bool RetainsMaterialIdentity(ConstructionKind kind) =>
        ConstructionBlueprintDefinitions.Get(kind).RetainsMaterialIdentity;

    public static ConstructionSiteState CreateSite(
        EntityId id,
        ConstructionKind kind,
        GridPosition anchor,
        GridPosition end,
        EntityId orderId = default,
        int sequenceIndex = 0,
        ResourceVariant requiredVariantOverride = ResourceVariant.None)
    {
        var definition = ConstructionBlueprintDefinitions.Get(kind);
        var requiredVariant = requiredVariantOverride != ResourceVariant.None
            ? requiredVariantOverride
            : definition.RequiredVariant;
        var footprint = definition.GetFootprint(anchor, end);
        var requiredQuantity = definition.GetRequiredQuantity(footprint.Count);
        var workTicks = definition.GetWorkTicks(footprint.Count);
        return new ConstructionSiteState(
            id,
            kind,
            anchor,
            end,
            definition.RequiredResource,
            requiredVariant,
            requiredQuantity,
            deliveredQuantity: 0,
            deliveredVariant: ResourceVariant.None,
            remainingWorkTicks: workTicks,
            totalWorkTicks: workTicks,
            definition.Capabilities,
            StoragePriority.Normal,
            orderId == EntityId.None ? id : orderId,
            sequenceIndex);
    }

    public static bool TryGetWorkshopKind(
        ConstructionKind construction,
        out WorkshopKind workshop)
    {
        var definition = ConstructionBlueprintDefinitions.Get(construction);
        workshop = definition.Workshop ?? default;
        return definition.Workshop is not null;
    }

    public static WorkshopDefinition GetWorkshop(ConstructionKind construction) =>
        TryGetWorkshopKind(construction, out var workshop)
            ? WorkshopCatalog.Get(workshop)
            : throw new ArgumentOutOfRangeException(
                nameof(construction),
                construction,
                "Construction is not a workshop.");
}
