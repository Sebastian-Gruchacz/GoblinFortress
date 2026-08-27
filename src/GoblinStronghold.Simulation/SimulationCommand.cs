using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation;

public enum SimulationCommandKind
{
    Forage = 1,
    CreateStorageZone = 2,
    PickUp = 3,
    StoreCarried = 4,
    Move = 5,
    Build = 6,
    DesignateWork = 7,
    ClearWorkDesignations = 8,
    ConfigureStoragePull = 9,
    AttackHumanVillage = 10,
    ConfigureStorageHauler = 11,
    ConfigureStorageSource = 12,
    ConfigureStoragePriority = 13,
    ConfigureResourcePriority = 14,
    ToggleWoodenDoor = 15,
    ConfigureConstructionPriority = 16,
    ConfigureStorageMineralFilter = 17,
    ConfigureRaidMember = 18,
    ConfigureWorkPriority = 19,
    ClearWorkDesignationOrder = 20,
    ConfigureWorkSuspension = 21,
    ConfigurePopulationTarget = 22,
    QueueCraftingOrder = 23,
}

public enum ConstructionKind : byte
{
    FoodStorage = 1,
    WoodenWalkway = 2,
    WoodStorage = 3,
    GoblinFieldCamp = 4,
    WoodenWall = 5,
    WoodenDoorFrame = 6,
    WoodenDoor = 7,
    StoneStorage = 8,
    StoneWall = 9,
    StoneDoorFrame = 10,
    WallTorch = 11,
    PrimitiveWorkshop = 12,
}

public enum CraftingRecipeKind : byte
{
    PrimitiveSling = 1,
    BoneKnife = 2,
    FightingStick = 3,
    StoneClub = 4,
    HideClothes = 5,
    ReedClothes = 6,
}

public readonly record struct SimulationCommand(
    SimulationTick ExecuteAt,
    ulong Sequence,
    SimulationCommandKind Kind,
    EntityId Subject,
    EntityId Target,
    GridPosition Position,
    GridPosition EndPosition,
    ConstructionKind Construction,
    ResourceKind Resource,
    int Amount)
{
    public static IReadOnlyList<GridPosition> GetLinearCells(
        GridPosition start,
        GridPosition end)
    {
        if (start.Z != end.Z)
        {
            throw new ArgumentException("A linear construction must remain on one height level.");
        }

        var cells = new List<GridPosition> { start };
        var current = start;
        while (current != end)
        {
            var remainingX = Math.Abs(end.X - current.X);
            var remainingY = Math.Abs(end.Y - current.Y);
            current = remainingX >= remainingY && remainingX > 0
                ? current with { X = current.X + Math.Sign(end.X - current.X) }
                : current with { Y = current.Y + Math.Sign(end.Y - current.Y) };
            cells.Add(current);
        }

        return cells;
    }

    public static IReadOnlyList<GridPosition> GetWalkwayCells(
        GridPosition start,
        GridPosition end) =>
        GetLinearCells(start, end);

    public static SimulationCommand Forage(
        SimulationTick executeAt,
        ulong sequence,
        EntityId subject,
        int effort = 1) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.Forage,
            subject,
            EntityId.None,
            default,
            default,
            default,
            ResourceKind.Food,
            effort);

    public static SimulationCommand CreateStorageZone(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition position,
        ResourceKind acceptedResource,
        int capacity) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.CreateStorageZone,
            EntityId.None,
            EntityId.None,
            position,
            position,
            default,
            acceptedResource,
            capacity);

    public static SimulationCommand PickUp(
        SimulationTick executeAt,
        ulong sequence,
        EntityId actor,
        EntityId itemStack,
        int quantity) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.PickUp,
            actor,
            itemStack,
            default,
            default,
            default,
            ResourceKind.Any,
            quantity);

    public static SimulationCommand StoreCarried(
        SimulationTick executeAt,
        ulong sequence,
        EntityId actor,
        EntityId storageZone) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.StoreCarried,
            actor,
            storageZone,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: 0);

    public static SimulationCommand Move(
        SimulationTick executeAt,
        ulong sequence,
        EntityId actor,
        GridPosition destination) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.Move,
            actor,
            EntityId.None,
            destination,
            destination,
            default,
            ResourceKind.Any,
            Amount: 0);

    public static SimulationCommand BuildFoodStorage(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition position) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.Build,
            EntityId.None,
            EntityId.None,
            position,
            position,
            ConstructionKind.FoodStorage,
            ResourceKind.Wood,
            Amount: 2);

    public static SimulationCommand BuildWalkway(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition start,
        GridPosition end) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.Build,
            EntityId.None,
            EntityId.None,
            start,
            end,
            ConstructionKind.WoodenWalkway,
            ResourceKind.Wood,
            Amount: 1);

    public static SimulationCommand BuildWoodStorage(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition position) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.Build,
            EntityId.None,
            EntityId.None,
            position,
            position,
            ConstructionKind.WoodStorage,
            ResourceKind.Wood,
            Amount: 2);

    public static SimulationCommand BuildStoneStorage(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition position) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.Build,
            EntityId.None,
            EntityId.None,
            position,
            position,
            ConstructionKind.StoneStorage,
            ResourceKind.Wood,
            Amount: 2);

    public static SimulationCommand BuildGoblinFieldCamp(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition position) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.Build,
            EntityId.None,
            EntityId.None,
            position,
            position with { X = position.X + 1, Y = position.Y + 1 },
            ConstructionKind.GoblinFieldCamp,
            ResourceKind.Wood,
            Amount: 6);

    public static SimulationCommand BuildWoodenWall(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition position) =>
        BuildWoodenWall(executeAt, sequence, position, position);

    public static SimulationCommand BuildWoodenWall(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition start,
        GridPosition end) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.Build,
            EntityId.None,
            EntityId.None,
            start,
            end,
            ConstructionKind.WoodenWall,
            ResourceKind.Wood,
            Amount: 2);

    public static SimulationCommand BuildWoodenDoorFrame(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition position) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.Build,
            EntityId.None,
            EntityId.None,
            position,
            position,
            ConstructionKind.WoodenDoorFrame,
            ResourceKind.Wood,
            Amount: 1);

    public static SimulationCommand BuildStoneWall(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition position) =>
        BuildStoneWall(executeAt, sequence, position, position);

    public static SimulationCommand BuildStoneWall(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition start,
        GridPosition end) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.Build,
            EntityId.None,
            EntityId.None,
            start,
            end,
            ConstructionKind.StoneWall,
            ResourceKind.Stone,
            Amount: 2);

    public static SimulationCommand BuildStoneDoorFrame(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition position) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.Build,
            EntityId.None,
            EntityId.None,
            position,
            position,
            ConstructionKind.StoneDoorFrame,
            ResourceKind.Stone,
            Amount: 1);

    public static SimulationCommand BuildWallTorch(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition position) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.Build,
            EntityId.None,
            EntityId.None,
            position,
            position,
            ConstructionKind.WallTorch,
            ResourceKind.Wood,
            Amount: 1);

    public static SimulationCommand BuildWoodenDoor(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition position) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.Build,
            EntityId.None,
            EntityId.None,
            position,
            position,
            ConstructionKind.WoodenDoor,
            ResourceKind.Wood,
            Amount: 1);

    public static SimulationCommand BuildPrimitiveWorkshop(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition position) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.Build,
            EntityId.None,
            EntityId.None,
            position,
            position,
            ConstructionKind.PrimitiveWorkshop,
            ResourceKind.Wood,
            Amount: 4);

    public static SimulationCommand QueuePrimitiveSling(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition workshop) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.QueueCraftingOrder,
            EntityId.None,
            EntityId.None,
            workshop,
            workshop,
            default,
            ResourceKind.Any,
            Amount: (int)CraftingRecipeKind.PrimitiveSling);

    public static SimulationCommand QueueCraftingRecipe(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition workshop,
        CraftingRecipeKind recipe) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.QueueCraftingOrder,
            EntityId.None,
            EntityId.None,
            workshop,
            workshop,
            ConstructionKind.PrimitiveWorkshop,
            ResourceKind.Any,
            Amount: (int)recipe);

    public static SimulationCommand ToggleWoodenDoor(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition position) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.ToggleWoodenDoor,
            EntityId.None,
            EntityId.None,
            position,
            position,
            default,
            ResourceKind.Any,
            Amount: 0);

    public static SimulationCommand DesignateWork(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition start,
        GridPosition end,
        ResourceKind resource) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.DesignateWork,
            EntityId.None,
            EntityId.None,
            start,
            end,
            default,
            resource,
            Amount: 0);

    public static SimulationCommand DesignateTreeFelling(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition start,
        GridPosition end) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.DesignateWork,
            EntityId.None,
            EntityId.None,
            start,
            end,
            default,
            ResourceKind.Any,
            Amount: (int)WorkDesignationKind.FellTree);

    public static SimulationCommand DesignateBoulderQuarrying(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition start,
        GridPosition end) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.DesignateWork,
            EntityId.None,
            EntityId.None,
            start,
            end,
            default,
            ResourceKind.Any,
            Amount: (int)WorkDesignationKind.QuarryBoulder);

    public static SimulationCommand DesignateAnimalHunting(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition start,
        GridPosition end) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.DesignateWork,
            EntityId.None,
            EntityId.None,
            start,
            end,
            default,
            ResourceKind.Any,
            Amount: (int)WorkDesignationKind.HuntAnimal);

    public static SimulationCommand DesignateRockMining(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition start,
        GridPosition end) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.DesignateWork,
            EntityId.None,
            EntityId.None,
            start,
            end,
            default,
            ResourceKind.Any,
            Amount: (int)WorkDesignationKind.MineRock);

    public static SimulationCommand DesignateRampDown(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition position) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.DesignateWork,
            EntityId.None,
            EntityId.None,
            position,
            position,
            default,
            ResourceKind.Any,
            Amount: (int)WorkDesignationKind.CarveRampDown);

    public static SimulationCommand DesignateRampUp(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition position) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.DesignateWork,
            EntityId.None,
            EntityId.None,
            position,
            position,
            default,
            ResourceKind.Any,
            Amount: (int)WorkDesignationKind.CarveRampUp);

    public static SimulationCommand DesignateScouting(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition start,
        GridPosition end) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.DesignateWork,
            EntityId.None,
            EntityId.None,
            start,
            end,
            default,
            ResourceKind.Any,
            Amount: (int)WorkDesignationKind.Scout);

    public static SimulationCommand DesignateBloodCleaning(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition start,
        GridPosition end) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.DesignateWork,
            EntityId.None,
            EntityId.None,
            start,
            end,
            default,
            ResourceKind.Any,
            Amount: (int)WorkDesignationKind.CleanBlood);

    public SimulationCommand WithWorkPriority(StoragePriority priority) =>
        this with { Amount = (Amount & 0xff) | (((int)priority + 1) << 8) };

    public SimulationCommand ReplacingWorkOrder(
        EntityId orderId,
        StoragePriority priority,
        bool isSuspended = false) =>
        this with
        {
            Subject = orderId,
            Amount = (Amount & 0xff) | (((int)priority + 1) << 8) |
                (isSuspended ? 1 << 16 : 0),
        };

    public static SimulationCommand ClearWorkDesignations(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition start,
        GridPosition end) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.ClearWorkDesignations,
            EntityId.None,
            EntityId.None,
            start,
            end,
            default,
            ResourceKind.Any,
            Amount: 0);

    public static SimulationCommand ConfigureWorkPriority(
        SimulationTick executeAt,
        ulong sequence,
        EntityId orderId,
        StoragePriority priority) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.ConfigureWorkPriority,
            EntityId.None,
            orderId,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: (int)priority);

    public static SimulationCommand ClearWorkDesignationOrder(
        SimulationTick executeAt,
        ulong sequence,
        EntityId orderId) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.ClearWorkDesignationOrder,
            EntityId.None,
            orderId,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: 0);

    public static SimulationCommand ConfigureWorkSuspension(
        SimulationTick executeAt,
        ulong sequence,
        EntityId orderId,
        bool isSuspended) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.ConfigureWorkSuspension,
            EntityId.None,
            orderId,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: isSuspended ? 1 : 0);

    public static SimulationCommand ConfigureStoragePull(
        SimulationTick executeAt,
        ulong sequence,
        EntityId storageZone,
        int desiredQuantity) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.ConfigureStoragePull,
            EntityId.None,
            storageZone,
            default,
            default,
            default,
            ResourceKind.Any,
            desiredQuantity);

    public static SimulationCommand ConfigureStorageHauler(
        SimulationTick executeAt,
        ulong sequence,
        EntityId storageZone,
        EntityId actor) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.ConfigureStorageHauler,
            actor,
            storageZone,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: 0);

    public static SimulationCommand ConfigureStorageSource(
        SimulationTick executeAt,
        ulong sequence,
        EntityId storageZone,
        EntityId sourceStorageZone) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.ConfigureStorageSource,
            sourceStorageZone,
            storageZone,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: 0);

    public static SimulationCommand ConfigureStoragePriority(
        SimulationTick executeAt,
        ulong sequence,
        EntityId storageZone,
        StoragePriority priority) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.ConfigureStoragePriority,
            EntityId.None,
            storageZone,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: (int)priority);

    public static SimulationCommand ConfigureStorageMineralFilter(
        SimulationTick executeAt,
        ulong sequence,
        EntityId storageZone,
        MineralStorageFilter filter) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.ConfigureStorageMineralFilter,
            EntityId.None,
            storageZone,
            default,
            default,
            default,
            ResourceKind.Stone,
            Amount: (int)filter);

    public static SimulationCommand ConfigureResourcePriority(
        SimulationTick executeAt,
        ulong sequence,
        ResourceKind resource,
        StoragePriority priority) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.ConfigureResourcePriority,
            EntityId.None,
            EntityId.None,
            default,
            default,
            default,
            resource,
            Amount: (int)priority);

    public static SimulationCommand ConfigureConstructionPriority(
        SimulationTick executeAt,
        ulong sequence,
        EntityId constructionSite,
        StoragePriority priority) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.ConfigureConstructionPriority,
            EntityId.None,
            constructionSite,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: (int)priority);

    public static SimulationCommand AttackHumanVillage(
        SimulationTick executeAt,
        ulong sequence) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.AttackHumanVillage,
            EntityId.None,
            EntityId.None,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: 0);

    public static SimulationCommand ConfigureRaidMember(
        SimulationTick executeAt,
        ulong sequence,
        EntityId actor,
        bool selected) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.ConfigureRaidMember,
            actor,
            EntityId.None,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: selected ? 1 : 0);

    public static SimulationCommand ConfigurePopulationTarget(
        SimulationTick executeAt,
        ulong sequence,
        int populationTarget) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.ConfigurePopulationTarget,
            EntityId.None,
            EntityId.None,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: populationTarget);
}

internal readonly record struct CommandKey(SimulationTick Tick, ulong Sequence) : IComparable<CommandKey>
{
    public int CompareTo(CommandKey other)
    {
        var tickComparison = Tick.CompareTo(other.Tick);
        return tickComparison != 0 ? tickComparison : Sequence.CompareTo(other.Sequence);
    }
}
