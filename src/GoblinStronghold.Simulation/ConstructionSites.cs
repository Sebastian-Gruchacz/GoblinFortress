using System.Collections.ObjectModel;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation;

public readonly record struct ConstructionMaterialSnapshot(
    ResourceKind Resource,
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
    int requiredWood,
    int deliveredWood,
    int remainingWorkTicks,
    int totalWorkTicks,
    ConstructionCapabilityRequirements capabilities,
    StoragePriority priority)
{
    public EntityId Id { get; } = id;

    public ConstructionKind Kind { get; } = kind;

    public GridPosition Anchor { get; } = anchor;

    public GridPosition End { get; } = end;

    public int RequiredWood { get; } = requiredWood;

    public int DeliveredWood { get; set; } = deliveredWood;

    public int RemainingWorkTicks { get; set; } = remainingWorkTicks;

    public int TotalWorkTicks { get; } = totalWorkTicks;

    public ConstructionCapabilityRequirements Capabilities { get; } = capabilities;

    public StoragePriority Priority { get; set; } = priority;

    public int MissingWood => Math.Max(0, RequiredWood - DeliveredWood);

    public bool HasAllMaterials => MissingWood == 0;

    public IReadOnlyList<GridPosition> GetFootprint() => Kind switch
    {
        ConstructionKind.WoodenWalkway or ConstructionKind.WoodenWall =>
            SimulationCommand.GetLinearCells(Anchor, End),
        ConstructionKind.GoblinFieldCamp =>
        [
            Anchor,
            Anchor with { X = Anchor.X + 1 },
            Anchor with { Y = Anchor.Y + 1 },
            Anchor with { X = Anchor.X + 1, Y = Anchor.Y + 1 },
        ],
        _ => [Anchor],
    };

    public ConstructionSiteSnapshot ToSnapshot() => new(
        Id,
        Kind,
        Anchor,
        End,
        GetFootprint(),
        [new ConstructionMaterialSnapshot(ResourceKind.Wood, RequiredWood, DeliveredWood)],
        RemainingWorkTicks,
        TotalWorkTicks,
        Capabilities,
        Priority);
}

internal static class ConstructionBlueprintCatalog
{
    public static ConstructionSiteState CreateSite(
        EntityId id,
        ConstructionKind kind,
        GridPosition anchor,
        GridPosition end)
    {
        var segmentCount = kind is ConstructionKind.WoodenWalkway or ConstructionKind.WoodenWall
            ? SimulationCommand.GetLinearCells(anchor, end).Count
            : 1;
        var requiredWood = kind switch
        {
            ConstructionKind.FoodStorage or ConstructionKind.WoodStorage or
                ConstructionKind.StoneStorage => 2,
            ConstructionKind.WoodenWalkway => segmentCount,
            ConstructionKind.GoblinFieldCamp => 6,
            ConstructionKind.WoodenWall => checked(segmentCount * 2),
            ConstructionKind.WoodenDoorFrame => 1,
            ConstructionKind.WoodenDoor => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        var workTicks = kind switch
        {
            ConstructionKind.FoodStorage or ConstructionKind.WoodStorage or
                ConstructionKind.StoneStorage => 40,
            ConstructionKind.WoodenWalkway => checked(segmentCount * 25),
            ConstructionKind.GoblinFieldCamp => 120,
            ConstructionKind.WoodenWall => checked(segmentCount * 45),
            ConstructionKind.WoodenDoorFrame => 30,
            ConstructionKind.WoodenDoor => 35,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        var capabilities = new ConstructionCapabilityRequirements(
            RequiredSkills: GoblinSkill.None,
            MinimumBuildingLevel: 0,
            RequiredEquipment: PersonalEquipment.None);
        return new ConstructionSiteState(
            id,
            kind,
            anchor,
            end,
            requiredWood,
            deliveredWood: 0,
            remainingWorkTicks: workTicks,
            totalWorkTicks: workTicks,
            capabilities,
            StoragePriority.Normal);
    }
}
