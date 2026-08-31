using System.Collections.ObjectModel;
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

    public IReadOnlyList<GridPosition> GetFootprint() => Kind switch
    {
        ConstructionKind.WoodenWalkway or ConstructionKind.BasaltWalkway or
            ConstructionKind.WoodenWall or
            ConstructionKind.StoneWall =>
            SimulationCommand.GetLinearCells(Anchor, End),
        ConstructionKind.WoodenFloor or ConstructionKind.StoneFloor =>
            SimulationCommand.GetAreaCells(Anchor, End),
        ConstructionKind.GoblinFieldCamp =>
        [
            Anchor,
            Anchor with { X = Anchor.X + 1 },
            Anchor with { Y = Anchor.Y + 1 },
            Anchor with { X = Anchor.X + 1, Y = Anchor.Y + 1 },
        ],
        ConstructionKind.GoblinHut => CreateSquareFootprint(Anchor, 3),
        _ => [Anchor],
    };

    private static IReadOnlyList<GridPosition> CreateSquareFootprint(GridPosition anchor, int size) =>
        Enumerable.Range(0, size)
            .SelectMany(y => Enumerable.Range(0, size)
                .Select(x => new GridPosition(anchor.X + x, anchor.Y + y, anchor.Z)))
            .ToArray();

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
    public static bool RetainsMaterialIdentity(ConstructionKind kind) => kind is not (
        ConstructionKind.FoodStorage or ConstructionKind.WoodStorage or
        ConstructionKind.StoneStorage or ConstructionKind.EquipmentStorage or
        ConstructionKind.MaterialsStorage or ConstructionKind.WaterBarrel or
        ConstructionKind.WoodenBox or ConstructionKind.WoodenChest or
        ConstructionKind.WoodenBulkBin);

    public static ConstructionSiteState CreateSite(
        EntityId id,
        ConstructionKind kind,
        GridPosition anchor,
        GridPosition end,
        EntityId orderId = default,
        int sequenceIndex = 0,
        ResourceVariant requiredVariantOverride = ResourceVariant.None)
    {
        var segmentCount = kind is ConstructionKind.WoodenWalkway or
            ConstructionKind.BasaltWalkway or ConstructionKind.WoodenWall or
            ConstructionKind.StoneWall
            ? SimulationCommand.GetLinearCells(anchor, end).Count
            : 1;
        var requiredResource = kind switch
        {
            ConstructionKind.BasaltWalkway or ConstructionKind.StoneWall or
                ConstructionKind.StoneFloor or ConstructionKind.StoneRamp or
                ConstructionKind.StoneDoorFrame or ConstructionKind.Bloomery or
                ConstructionKind.SmeltingFurnace or ConstructionKind.CrucibleFurnace =>
                ResourceKind.Stone,
            ConstructionKind.WaterBarrel or ConstructionKind.WoodenBox or
                ConstructionKind.WoodenChest or ConstructionKind.WoodenBulkBin =>
                ResourceKind.Equipment,
            _ => ResourceKind.Wood,
        };
        var requiredVariant = requiredVariantOverride != ResourceVariant.None
            ? requiredVariantOverride
            : kind switch
        {
            ConstructionKind.WaterBarrel => ResourceVariant.EquipmentWoodenBarrel,
            ConstructionKind.WoodenBox => ResourceVariant.EquipmentWoodenBox,
            ConstructionKind.WoodenChest => ResourceVariant.EquipmentWoodenChest,
            ConstructionKind.WoodenBulkBin => ResourceVariant.EquipmentWoodenBulkBin,
            ConstructionKind.BasaltWalkway => ResourceVariant.Basalt,
            _ => ResourceVariant.None,
        };
        var requiredQuantity = kind switch
        {
            ConstructionKind.FoodStorage or ConstructionKind.WoodStorage or
                ConstructionKind.StoneStorage or ConstructionKind.EquipmentStorage or
                ConstructionKind.MaterialsStorage => 2,
            ConstructionKind.WaterBarrel => 1,
            ConstructionKind.WoodenBox or ConstructionKind.WoodenChest or
                ConstructionKind.WoodenBulkBin => 1,
            ConstructionKind.WoodenWalkway => segmentCount,
            ConstructionKind.BasaltWalkway => segmentCount,
            ConstructionKind.GoblinFieldCamp => 6,
            ConstructionKind.GoblinHut => 8,
            ConstructionKind.WoodenWall => checked(segmentCount * 2),
            ConstructionKind.StoneWall => checked(segmentCount * 2),
            ConstructionKind.WoodenFloor or ConstructionKind.StoneFloor =>
                SimulationCommand.GetAreaCells(anchor, end).Count,
            ConstructionKind.WoodenRamp => 2,
            ConstructionKind.StoneRamp => 3,
            ConstructionKind.WoodenDoorFrame => 1,
            ConstructionKind.StoneDoorFrame => 1,
            ConstructionKind.WoodenDoor => 1,
            ConstructionKind.WallTorch => 1,
            ConstructionKind.PrimitiveWorkshop or ConstructionKind.Bloomery or
                ConstructionKind.SmeltingFurnace or ConstructionKind.CrucibleFurnace =>
                GetWorkshop(kind).ConstructionRequirements.Sum(item => item.Quantity),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        var workTicks = kind switch
        {
            ConstructionKind.FoodStorage or ConstructionKind.WoodStorage or
                ConstructionKind.StoneStorage or ConstructionKind.EquipmentStorage or
                ConstructionKind.MaterialsStorage => 40,
            ConstructionKind.WaterBarrel => 20,
            ConstructionKind.WoodenBox => 15,
            ConstructionKind.WoodenChest => 25,
            ConstructionKind.WoodenBulkBin => 20,
            ConstructionKind.WoodenWalkway => checked(segmentCount * 25),
            ConstructionKind.BasaltWalkway => checked(segmentCount * 45),
            ConstructionKind.GoblinFieldCamp => 120,
            ConstructionKind.GoblinHut => 180,
            ConstructionKind.WoodenWall => checked(segmentCount * 45),
            ConstructionKind.StoneWall => checked(segmentCount * 60),
            ConstructionKind.WoodenFloor => checked(
                SimulationCommand.GetAreaCells(anchor, end).Count * 20),
            ConstructionKind.StoneFloor => checked(
                SimulationCommand.GetAreaCells(anchor, end).Count * 30),
            ConstructionKind.WoodenRamp => 45,
            ConstructionKind.StoneRamp => 65,
            ConstructionKind.WoodenDoorFrame => 30,
            ConstructionKind.StoneDoorFrame => 45,
            ConstructionKind.WoodenDoor => 35,
            ConstructionKind.WallTorch => 20,
            ConstructionKind.PrimitiveWorkshop => 90,
            ConstructionKind.Bloomery => 180,
            ConstructionKind.SmeltingFurnace => 240,
            ConstructionKind.CrucibleFurnace => 300,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        var capabilities = kind is ConstructionKind.Bloomery or
            ConstructionKind.SmeltingFurnace or ConstructionKind.CrucibleFurnace
            ? new ConstructionCapabilityRequirements(
                RequiredSkills: GoblinSkill.Building,
                MinimumBuildingLevel: kind == ConstructionKind.CrucibleFurnace ? 2 : 1,
                RequiredEquipment: PersonalEquipment.PrimitivePickaxe)
            : kind is ConstructionKind.BasaltWalkway or
            ConstructionKind.StoneWall or ConstructionKind.StoneFloor or
            ConstructionKind.StoneDoorFrame or ConstructionKind.StoneRamp
            ? new ConstructionCapabilityRequirements(
                RequiredSkills: GoblinSkill.Building,
                MinimumBuildingLevel: kind == ConstructionKind.BasaltWalkway ? 2 : 0,
                RequiredEquipment: PersonalEquipment.PrimitivePickaxe)
            : new ConstructionCapabilityRequirements(
                RequiredSkills: GoblinSkill.None,
                MinimumBuildingLevel: 0,
                RequiredEquipment: PersonalEquipment.None);
        return new ConstructionSiteState(
            id,
            kind,
            anchor,
            end,
            requiredResource,
            requiredVariant,
            requiredQuantity,
            deliveredQuantity: 0,
            deliveredVariant: ResourceVariant.None,
            remainingWorkTicks: workTicks,
            totalWorkTicks: workTicks,
            capabilities,
            StoragePriority.Normal,
            orderId == EntityId.None ? id : orderId,
            sequenceIndex);
    }

    public static bool TryGetWorkshopKind(
        ConstructionKind construction,
        out WorkshopKind workshop)
    {
        workshop = construction switch
        {
            ConstructionKind.PrimitiveWorkshop => WorkshopKind.PrimitiveWorkshop,
            ConstructionKind.Bloomery => WorkshopKind.Bloomery,
            ConstructionKind.SmeltingFurnace => WorkshopKind.SmeltingFurnace,
            ConstructionKind.CrucibleFurnace => WorkshopKind.CrucibleFurnace,
            _ => default,
        };
        return construction is ConstructionKind.PrimitiveWorkshop or
            ConstructionKind.Bloomery or ConstructionKind.SmeltingFurnace or
            ConstructionKind.CrucibleFurnace;
    }

    public static WorkshopDefinition GetWorkshop(ConstructionKind construction) =>
        TryGetWorkshopKind(construction, out var workshop)
            ? WorkshopCatalog.Get(workshop)
            : throw new ArgumentOutOfRangeException(
                nameof(construction),
                construction,
                "Construction is not a workshop.");
}
