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
        ConstructionCapabilityRequirements capabilities)
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
    ConstructionCapabilityRequirements capabilities)
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

    public int MissingWood => Math.Max(0, RequiredWood - DeliveredWood);

    public bool HasAllMaterials => MissingWood == 0;

    public IReadOnlyList<GridPosition> GetFootprint() => Kind switch
    {
        ConstructionKind.WoodenWalkway => SimulationCommand.GetWalkwayCells(Anchor, End),
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
        Capabilities);
}

internal static class ConstructionBlueprintCatalog
{
    public static ConstructionSiteState CreateSite(
        EntityId id,
        ConstructionKind kind,
        GridPosition anchor,
        GridPosition end)
    {
        var segmentCount = kind == ConstructionKind.WoodenWalkway
            ? SimulationCommand.GetWalkwayCells(anchor, end).Count
            : 1;
        var requiredWood = kind switch
        {
            ConstructionKind.FoodStorage or ConstructionKind.WoodStorage => 2,
            ConstructionKind.WoodenWalkway => segmentCount,
            ConstructionKind.GoblinFieldCamp => 6,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        var workTicks = kind switch
        {
            ConstructionKind.FoodStorage or ConstructionKind.WoodStorage => 40,
            ConstructionKind.WoodenWalkway => checked(segmentCount * 25),
            ConstructionKind.GoblinFieldCamp => 120,
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
            capabilities);
    }
}
