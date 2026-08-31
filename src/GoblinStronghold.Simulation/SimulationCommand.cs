using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using GoblinStronghold.Simulation.Workshops;

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
    QueueCraftingOrder = 23,
    SuspendRaidPreparation = 24,
    LaunchRaid = 25,
    ConfigureRaidTarget = 26,
    ConfigureRaidDirectives = 27,
    OrderPatrol = 28,
    OrderAttackArea = 29,
    OrderHuntArea = 30,
    ConfigureCorpseDirectives = 31,
    CreateLogisticsNetwork = 32,
    ConfigureLogisticsHauler = 33,
    ConfigureLogisticsSource = 34,
    ConfigureStorageNetwork = 35,
    CreateStorageArea = 36,
    ConfigureStorageAreaNetwork = 37,
    RenameLogisticsNetwork = 38,
    RenameStorageArea = 39,
    ConfigureStorageFilter = 40,
    DeleteLogisticsNetwork = 41,
    ResizeStorageArea = 42,
    DissolveStorageArea = 43,
    ConfigureStorageFilterResource = 44,
    CancelConstruction = 45,
    DismantleConstruction = 46,
    OrderActorFlee = 47,
    OrderActorSleep = 48,
    SuspendActorDispatcher = 49,
    EquipItem = 50,
    PrioritizeItemHauling = 51,
    OrderItemPickup = 52,
}

public enum DismantleTargetKind : byte
{
    WorldObject = 1,
    StorageZone = 2,
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
    GoblinHut = 13,
    EquipmentStorage = 14,
    MaterialsStorage = 15,
    WaterBarrel = 16,
    BasaltWalkway = 17,
    Bloomery = 18,
    SmeltingFurnace = 19,
    CrucibleFurnace = 20,
    WoodenBox = 21,
    WoodenChest = 22,
    WoodenBulkBin = 23,
    WoodenFloor = 24,
    StoneFloor = 25,
    WoodenRamp = 26,
    StoneRamp = 27,
}

public enum CraftingRecipeKind : byte
{
    PrimitiveSling = 1,
    BoneKnife = 2,
    FightingStick = 3,
    StoneClub = 4,
    HideClothes = 5,
    ReedClothes = 6,
    PrimitiveWaterskin = 7,
    ReinforcedPickaxe = 8,
    WoodenBucket = 9,
    WoodenBarrel = 10,
    SmeltIronBar = 11,
    SmeltCopperBar = 12,
    SmeltSilverBar = 13,
    SmeltGoldBar = 14,
    WoodenBox = 15,
    WoodenChest = 16,
    WoodenBulkBin = 17,
    PrimitiveAxe = 18,
    PrimitivePickaxe = 19,
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
    int Amount,
    string Text = "",
    ResourceVariant MaterialVariant = ResourceVariant.None)
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

    public static IReadOnlyList<GridPosition> GetAreaCells(
        GridPosition first,
        GridPosition second)
    {
        if (first.Z != second.Z)
        {
            throw new ArgumentException("An area construction must remain on one height level.");
        }

        var minimumX = Math.Min(first.X, second.X);
        var maximumX = Math.Max(first.X, second.X);
        var minimumY = Math.Min(first.Y, second.Y);
        var maximumY = Math.Max(first.Y, second.Y);
        return Enumerable.Range(minimumY, checked(maximumY - minimumY + 1))
            .SelectMany(y => Enumerable.Range(minimumX, checked(maximumX - minimumX + 1))
                .Select(x => new GridPosition(x, y, first.Z)))
            .ToArray();
    }

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

    public static SimulationCommand OrderActorFlee(
        SimulationTick executeAt,
        ulong sequence,
        EntityId actor) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.OrderActorFlee,
            actor,
            EntityId.None,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: 0);

    public static SimulationCommand OrderActorSleep(
        SimulationTick executeAt,
        ulong sequence,
        EntityId actor) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.OrderActorSleep,
            actor,
            EntityId.None,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: 0);

    public static SimulationCommand SuspendActorDispatcher(
        SimulationTick executeAt,
        ulong sequence,
        EntityId actor) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.SuspendActorDispatcher,
            actor,
            EntityId.None,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: 0);

    public static SimulationCommand EquipItem(
        SimulationTick executeAt,
        ulong sequence,
        EntityId actor,
        EntityId itemStack) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.EquipItem,
            actor,
            itemStack,
            default,
            default,
            default,
            ResourceKind.Equipment,
            Amount: 1);

    public static SimulationCommand PrioritizeItemHauling(
        SimulationTick executeAt,
        ulong sequence,
        EntityId itemStack) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.PrioritizeItemHauling,
            EntityId.None,
            itemStack,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: (int)StoragePriority.Urgent);

    public static SimulationCommand OrderItemPickup(
        SimulationTick executeAt,
        ulong sequence,
        EntityId actor,
        EntityId itemStack) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.OrderItemPickup,
            actor,
            itemStack,
            default,
            default,
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
        GridPosition end,
        ResourceVariant materialVariant = ResourceVariant.None) =>
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
            Amount: 1,
            MaterialVariant: materialVariant);

    public static SimulationCommand BuildBasaltWalkway(
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
            ConstructionKind.BasaltWalkway,
            ResourceKind.Stone,
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

    public static SimulationCommand BuildEquipmentStorage(
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
            ConstructionKind.EquipmentStorage,
            ResourceKind.Wood,
            Amount: 2);

    public static SimulationCommand BuildMaterialsStorage(
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
            ConstructionKind.MaterialsStorage,
            ResourceKind.Wood,
            Amount: 2);

    public static SimulationCommand PlaceWaterBarrel(
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
            ConstructionKind.WaterBarrel,
            ResourceKind.Equipment,
            Amount: 1);

    public static SimulationCommand PlaceWoodenBox(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition position) =>
        PlaceStorageContainer(
            executeAt, sequence, position, ConstructionKind.WoodenBox);

    public static SimulationCommand PlaceWoodenChest(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition position) =>
        PlaceStorageContainer(
            executeAt, sequence, position, ConstructionKind.WoodenChest);

    public static SimulationCommand PlaceWoodenBulkBin(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition position) =>
        PlaceStorageContainer(
            executeAt, sequence, position, ConstructionKind.WoodenBulkBin);

    private static SimulationCommand PlaceStorageContainer(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition position,
        ConstructionKind construction) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.Build,
            EntityId.None,
            EntityId.None,
            position,
            position,
            construction,
            ResourceKind.Equipment,
            Amount: 1);

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

    public static SimulationCommand BuildGoblinHut(
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
            position with { X = position.X + 2, Y = position.Y + 2 },
            ConstructionKind.GoblinHut,
            ResourceKind.Wood,
            Amount: 8);

    public static SimulationCommand BuildWoodenWall(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition position) =>
        BuildWoodenWall(executeAt, sequence, position, position);

    public static SimulationCommand BuildWoodenWall(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition start,
        GridPosition end,
        ResourceVariant materialVariant = ResourceVariant.None) =>
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
            Amount: 2,
            MaterialVariant: materialVariant);

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
        GridPosition end,
        ResourceVariant materialVariant = ResourceVariant.None) =>
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
            Amount: 2,
            MaterialVariant: materialVariant);

    public static SimulationCommand BuildWoodenFloor(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition first,
        GridPosition second,
        ResourceVariant materialVariant) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.Build,
            EntityId.None,
            EntityId.None,
            first,
            second,
            ConstructionKind.WoodenFloor,
            ResourceKind.Wood,
            Amount: 1,
            MaterialVariant: materialVariant);

    public static SimulationCommand BuildStoneFloor(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition first,
        GridPosition second,
        ResourceVariant materialVariant) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.Build,
            EntityId.None,
            EntityId.None,
            first,
            second,
            ConstructionKind.StoneFloor,
            ResourceKind.Stone,
            Amount: 1,
            MaterialVariant: materialVariant);

    public static SimulationCommand BuildWoodenRamp(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition lower,
        GridPosition upper,
        ResourceVariant materialVariant) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.Build,
            EntityId.None,
            EntityId.None,
            lower,
            upper,
            ConstructionKind.WoodenRamp,
            ResourceKind.Wood,
            Amount: 2,
            MaterialVariant: materialVariant);

    public static SimulationCommand BuildStoneRamp(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition lower,
        GridPosition upper,
        ResourceVariant materialVariant) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.Build,
            EntityId.None,
            EntityId.None,
            lower,
            upper,
            ConstructionKind.StoneRamp,
            ResourceKind.Stone,
            Amount: 3,
            MaterialVariant: materialVariant);

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
        GridPosition position) => BuildWorkshop(
            executeAt,
            sequence,
            position,
            WorkshopKind.PrimitiveWorkshop);

    public static SimulationCommand BuildWorkshop(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition position,
        WorkshopKind workshop)
    {
        var definition = WorkshopCatalog.Get(workshop);
        var construction = workshop switch
        {
            WorkshopKind.PrimitiveWorkshop => ConstructionKind.PrimitiveWorkshop,
            WorkshopKind.Bloomery => ConstructionKind.Bloomery,
            WorkshopKind.SmeltingFurnace => ConstructionKind.SmeltingFurnace,
            WorkshopKind.CrucibleFurnace => ConstructionKind.CrucibleFurnace,
            _ => throw new ArgumentOutOfRangeException(nameof(workshop), workshop, null),
        };
        var resource = workshop == WorkshopKind.PrimitiveWorkshop
            ? ResourceKind.Wood
            : ResourceKind.Stone;
        return new(
            executeAt,
            sequence,
            SimulationCommandKind.Build,
            EntityId.None,
            EntityId.None,
            position,
            position,
            construction,
            resource,
            Amount: definition.ConstructionRequirements.Sum(item => item.Quantity));
    }

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

    public static SimulationCommand CreateLogisticsNetwork(
        SimulationTick executeAt,
        ulong sequence) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.CreateLogisticsNetwork,
            EntityId.None,
            EntityId.None,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: 0);

    public static SimulationCommand ConfigureLogisticsHauler(
        SimulationTick executeAt,
        ulong sequence,
        EntityId logisticsNetwork,
        EntityId actor,
        bool assigned) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.ConfigureLogisticsHauler,
            actor,
            logisticsNetwork,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: assigned ? 1 : 0);

    public static SimulationCommand ConfigureLogisticsSource(
        SimulationTick executeAt,
        ulong sequence,
        EntityId logisticsNetwork,
        EntityId storageZone,
        bool included) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.ConfigureLogisticsSource,
            storageZone,
            logisticsNetwork,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: included ? 1 : 0);

    public static SimulationCommand ConfigureStorageNetwork(
        SimulationTick executeAt,
        ulong sequence,
        EntityId storageZone,
        EntityId logisticsNetwork) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.ConfigureStorageNetwork,
            logisticsNetwork,
            storageZone,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: 0);

    public static SimulationCommand CreateStorageArea(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition start,
        GridPosition end,
        EntityId logisticsNetwork = default) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.CreateStorageArea,
            logisticsNetwork,
            EntityId.None,
            start,
            end,
            default,
            ResourceKind.Any,
            Amount: 0);

    public static SimulationCommand ConfigureStorageAreaNetwork(
        SimulationTick executeAt,
        ulong sequence,
        EntityId storageArea,
        EntityId logisticsNetwork) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.ConfigureStorageAreaNetwork,
            logisticsNetwork,
            storageArea,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: 0);

    public static SimulationCommand RenameLogisticsNetwork(
        SimulationTick executeAt,
        ulong sequence,
        EntityId logisticsNetwork,
        string name) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.RenameLogisticsNetwork,
            EntityId.None,
            logisticsNetwork,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: 0,
            Text: name);

    public static SimulationCommand RenameStorageArea(
        SimulationTick executeAt,
        ulong sequence,
        EntityId storageArea,
        string name) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.RenameStorageArea,
            EntityId.None,
            storageArea,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: 0,
            Text: name);

    public static SimulationCommand ConfigureStorageFilter(
        SimulationTick executeAt,
        ulong sequence,
        EntityId storageZone,
        ResourceKind acceptedResource) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.ConfigureStorageFilter,
            EntityId.None,
            storageZone,
            default,
            default,
            default,
            acceptedResource,
            Amount: 0);

    public static SimulationCommand DeleteLogisticsNetwork(
        SimulationTick executeAt,
        ulong sequence,
        EntityId logisticsNetwork) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.DeleteLogisticsNetwork,
            EntityId.None,
            logisticsNetwork,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: 0);

    public static SimulationCommand ResizeStorageArea(
        SimulationTick executeAt,
        ulong sequence,
        EntityId storageArea,
        GridPosition start,
        GridPosition end) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.ResizeStorageArea,
            EntityId.None,
            storageArea,
            start,
            end,
            default,
            ResourceKind.Any,
            Amount: 0);

    public static SimulationCommand DissolveStorageArea(
        SimulationTick executeAt,
        ulong sequence,
        EntityId storageArea) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.DissolveStorageArea,
            EntityId.None,
            storageArea,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: 0);

    public static SimulationCommand ConfigureStorageFilterResource(
        SimulationTick executeAt,
        ulong sequence,
        EntityId storageZone,
        ResourceKind resource,
        bool included) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.ConfigureStorageFilterResource,
            EntityId.None,
            storageZone,
            default,
            default,
            default,
            resource,
            Amount: included ? 1 : 0);

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
        ulong sequence) => AttackHumanVillage(executeAt, sequence, default);

    public static SimulationCommand AttackHumanVillage(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition rallyPoint) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.AttackHumanVillage,
            EntityId.None,
            EntityId.None,
            rallyPoint,
            rallyPoint,
            default,
            ResourceKind.Any,
            Amount: 0);

    public static SimulationCommand SuspendRaidPreparation(
        SimulationTick executeAt,
        ulong sequence) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.SuspendRaidPreparation,
            EntityId.None,
            EntityId.None,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: 0);

    public static SimulationCommand LaunchRaid(
        SimulationTick executeAt,
        ulong sequence) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.LaunchRaid,
            EntityId.None,
            EntityId.None,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: 0);

    public static SimulationCommand ConfigureRaidTarget(
        SimulationTick executeAt,
        ulong sequence,
        GridPosition target,
        int radius) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.ConfigureRaidTarget,
            EntityId.None,
            EntityId.None,
            target,
            target,
            default,
            ResourceKind.Any,
            Amount: radius);

    public static SimulationCommand ConfigureRaidDirectives(
        SimulationTick executeAt,
        ulong sequence,
        RaidDirective directives) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.ConfigureRaidDirectives,
            EntityId.None,
            EntityId.None,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: (int)directives);

    public static SimulationCommand ConfigureCorpseDirectives(
        SimulationTick executeAt,
        ulong sequence,
        EntityId corpse,
        CorpseDirective directives) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.ConfigureCorpseDirectives,
            EntityId.None,
            corpse,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: (int)directives);

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

    public static SimulationCommand OrderPatrol(
        SimulationTick executeAt,
        ulong sequence,
        EntityId actor,
        GridPosition point,
        bool append) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.OrderPatrol,
            actor,
            EntityId.None,
            point,
            point,
            default,
            ResourceKind.Any,
            Amount: append ? 1 : 0);

    public static SimulationCommand OrderAttackArea(
        SimulationTick executeAt,
        ulong sequence,
        EntityId actor,
        GridPosition center,
        int radius) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.OrderAttackArea,
            actor,
            EntityId.None,
            center,
            center,
            default,
            ResourceKind.Any,
            Amount: radius);

    public static SimulationCommand OrderHuntArea(
        SimulationTick executeAt,
        ulong sequence,
        EntityId actor,
        GridPosition center,
        int radius) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.OrderHuntArea,
            actor,
            EntityId.None,
            center,
            center,
            default,
            ResourceKind.Any,
            Amount: radius);

    public static SimulationCommand CancelConstruction(
        SimulationTick executeAt,
        ulong sequence,
        EntityId constructionSiteId) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.CancelConstruction,
            EntityId.None,
            constructionSiteId,
            default,
            default,
            default,
            ResourceKind.Any,
            Amount: 0);

    public static SimulationCommand DismantleWorldObject(
        SimulationTick executeAt,
        ulong sequence,
        WorldObjectId worldObjectId,
        GridPosition position) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.DismantleConstruction,
            EntityId.None,
            new EntityId(worldObjectId.Value),
            position,
            position,
            default,
            ResourceKind.Any,
            Amount: (int)DismantleTargetKind.WorldObject);

    public static SimulationCommand DismantleStorageZone(
        SimulationTick executeAt,
        ulong sequence,
        EntityId storageZoneId,
        GridPosition position) =>
        new(
            executeAt,
            sequence,
            SimulationCommandKind.DismantleConstruction,
            EntityId.None,
            storageZoneId,
            position,
            position,
            default,
            ResourceKind.Any,
            Amount: (int)DismantleTargetKind.StorageZone);
}

internal readonly record struct CommandKey(SimulationTick Tick, ulong Sequence) : IComparable<CommandKey>
{
    public int CompareTo(CommandKey other)
    {
        var tickComparison = Tick.CompareTo(other.Tick);
        return tickComparison != 0 ? tickComparison : Sequence.CompareTo(other.Sequence);
    }
}
