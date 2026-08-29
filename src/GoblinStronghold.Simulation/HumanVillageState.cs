using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation;

internal sealed class HumanVillageState
{
    private static readonly string[] VillagerNames =
    [
        "Aldona", "Bogdan", "Celina", "Dobromir", "Elwira", "Florian",
        "Grażyna", "Hubert", "Irena", "Jeremi", "Klara", "Lucjan",
    ];

    private readonly SortedDictionary<int, HumanCohortState> _cohorts;
    private readonly SortedDictionary<int, HumanVillagerState> _villagers;
    private readonly SortedDictionary<int, HumanFieldState> _fields;
    private readonly HumanVillageNeedSettings _needs;
    private readonly HumanVillageEconomySettings _economy;
    private readonly IReadOnlyList<GridPosition> _wellAccesses;
    private readonly IReadOnlyList<GridPosition> _goodsWorkshopAccesses;
    private GridPosition? _treeFellingTarget;
    private int _treeFellingProgress;
    private int _goodsWorkProgress;
    private GridPosition? _storehouseSite;
    private int _storehouseWorkProgress;

    private HumanVillageState(
        GridPosition anchor, HumanVillageNeedSettings needs,
        HumanVillageEconomySettings economy,
        IReadOnlyList<GridPosition> wellAccesses,
        IReadOnlyList<GridPosition> goodsWorkshopAccesses,
        int population, int foodStock, int woodStock, int goodsStock,
        int waterStock, int storehouseCount, bool goblinAttackOrdered, int hostility,
        long lastIntruderSeenTick, int guardHitPoints, int maximumGuardHitPoints,
        GridPosition? treeFellingTarget, int treeFellingProgress,
        int goodsWorkProgress,
        GridPosition? storehouseSite, int storehouseWorkProgress,
        IEnumerable<HumanCohortState> cohorts, IEnumerable<HumanVillagerState> villagers,
        IEnumerable<HumanFieldState> fields)
    {
        Anchor = anchor;
        _needs = needs;
        _economy = economy;
        _wellAccesses = wellAccesses;
        _goodsWorkshopAccesses = goodsWorkshopAccesses;
        Population = population;
        FoodStock = foodStock;
        WoodStock = woodStock;
        GoodsStock = goodsStock;
        WaterStock = waterStock;
        StorehouseCount = storehouseCount;
        GoblinAttackOrdered = goblinAttackOrdered;
        Hostility = hostility;
        LastIntruderSeenTick = lastIntruderSeenTick;
        GuardHitPoints = guardHitPoints;
        MaximumGuardHitPoints = maximumGuardHitPoints;
        _treeFellingTarget = treeFellingTarget;
        _treeFellingProgress = treeFellingProgress;
        _goodsWorkProgress = goodsWorkProgress;
        _storehouseSite = storehouseSite;
        _storehouseWorkProgress = storehouseWorkProgress;
        _cohorts = new(cohorts.ToDictionary(item => item.Id));
        _villagers = new(villagers.ToDictionary(item => item.Id));
        _fields = new(fields.ToDictionary(item => item.Id));
    }

    public GridPosition Anchor { get; }
    public int Population { get; private set; }
    public int FoodStock { get; private set; }
    public int WoodStock { get; private set; }
    public int GoodsStock { get; private set; }
    public int WaterStock { get; private set; }
    public int StorehouseCount { get; private set; }

    public bool TryTakeRaidLoot(Resources.ResourceKind resource, int quantity)
    {
        if (quantity <= 0)
        {
            return false;
        }
        return resource switch
        {
            Resources.ResourceKind.Food when FoodStock >= quantity => TakeFood(),
            Resources.ResourceKind.Wood when WoodStock >= quantity => TakeWood(),
            Resources.ResourceKind.Hide or Resources.ResourceKind.Reeds
                when GoodsStock >= quantity => TakeGoods(),
            _ => false,
        };

        bool TakeFood() { FoodStock -= quantity; return true; }
        bool TakeWood() { WoodStock -= quantity; return true; }
        bool TakeGoods() { GoodsStock -= quantity; return true; }
    }
    public bool GoblinAttackOrdered { get; private set; }
    public int Hostility { get; private set; }
    public long LastIntruderSeenTick { get; private set; }
    public int GuardHitPoints { get; private set; }
    public int MaximumGuardHitPoints { get; }
    public int PlannedFieldCount => Math.Max(
        _economy.MinimumFieldCount,
        (Population * _economy.CropGrowthDays + _economy.FieldYield - 1) /
            _economy.FieldYield);
    public int FoodCapacity => checked(
        _economy.BaseFoodCapacity + StorehouseCount * _economy.StorehouseCapacity);
    private int GoodsTarget =>
        (Population + _economy.GoodsPopulationDivisor - 1) /
        _economy.GoodsPopulationDivisor;

    public static HumanVillageState CreateInitial(WorldMapState world, SimulationDefinitions definitions)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(definitions);
        var positions = FindPositions(world, definitions.HumanVillageActivityRadius);
        if (positions.Count < 16)
        {
            throw new InvalidOperationException("The human village has too few traversable work positions.");
        }

        var initialFieldPositions = positions.Skip(12)
            .Where(position => world.GetPlantPatch(position) is null)
            .Take(4)
            .ToArray();
        if (initialFieldPositions.Length < 4)
        {
            throw new InvalidOperationException("The human village has too few pre-cleared field positions.");
        }

        HumanCohortState[] cohorts =
        [
            new(1, HumanCohortRole.Farmers, 4, positions[0], HumanCohortTask.WorkFields, 3, HumanTool.WoodenHoe),
            new(2, HumanCohortRole.Workers, 6, positions[4], HumanCohortTask.DrawWater, 2, HumanTool.WoodenAxe | HumanTool.WoodenBucket),
            new(3, HumanCohortRole.Guards, 2, positions[10], HumanCohortTask.Guard, 2, HumanTool.WoodenSpear),
        ];
        var villagers = CreateInitialVillagers(positions, cohorts, definitions);
        return new(
            world.Baseline.HumanVillage, definitions.HumanVillageNeeds,
            definitions.HumanVillageEconomy,
            FindWellAccesses(world),
            FindStructureAccesses(world, WorldObjectKind.HumanBarn),
            12, 48, 24, 4, 36, 0, false, 0, -1,
            2 * definitions.HumanGuardHealth, 2 * definitions.HumanGuardHealth,
            treeFellingTarget: null, treeFellingProgress: 0, goodsWorkProgress: 0,
            storehouseSite: null, storehouseWorkProgress: 0,
            cohorts,
            villagers,
            initialFieldPositions.Select((position, index) =>
                new HumanFieldState(index + 1, position, HumanFieldPhase.Sown, 0, 0)));
    }

    public static HumanVillageState Restore(
        WorldMapState world, HumanVillageSaveModel model,
        SimulationDefinitions definitions, SimulationTick currentTick)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(model);
        if (model.Population < 0 || model.FoodStock < 0 || model.WoodStock < 0 ||
            model.GoodsStock < 0 || model.WaterStock < 0 || model.StorehouseCount < 0 ||
            model.Hostility is < 0 or > 100 || model.LastIntruderSeenTick < -1 ||
            model.LastIntruderSeenTick > currentTick.Value ||
            (model.Hostility == 0) != (model.LastIntruderSeenTick == -1) ||
            model.GuardHitPoints < 0 || model.Cohorts.Count != 3)
        {
            throw new InvalidDataException("The save contains invalid human village state.");
        }
        var hasAnyTreeCoordinate = model.TreeFellingX is not null ||
            model.TreeFellingY is not null || model.TreeFellingZ is not null;
        var hasAllTreeCoordinates = model.TreeFellingX is not null &&
            model.TreeFellingY is not null && model.TreeFellingZ is not null;
        var treeFellingTarget = hasAllTreeCoordinates
            ? new GridPosition(
                model.TreeFellingX!.Value,
                model.TreeFellingY!.Value,
                model.TreeFellingZ!.Value)
            : (GridPosition?)null;
        if (hasAnyTreeCoordinate != hasAllTreeCoordinates ||
            model.TreeFellingProgress < 0 ||
            model.TreeFellingProgress > definitions.HumanVillageEconomy.TreeFellingWork ||
            treeFellingTarget is { } savedTree && world.GetFellableWood(savedTree) is null)
        {
            throw new InvalidDataException("The save contains invalid human tree-felling work.");
        }
        if (model.GoodsWorkProgress < 0 ||
            model.GoodsWorkProgress > definitions.HumanVillageEconomy.GoodsWorkPerUnit)
        {
            throw new InvalidDataException("The save contains invalid human goods work.");
        }
        var hasAnyStorehouseCoordinate = model.StorehouseSiteX is not null ||
            model.StorehouseSiteY is not null || model.StorehouseSiteZ is not null;
        var hasAllStorehouseCoordinates = model.StorehouseSiteX is not null &&
            model.StorehouseSiteY is not null && model.StorehouseSiteZ is not null;
        var storehouseSite = hasAllStorehouseCoordinates
            ? new GridPosition(
                model.StorehouseSiteX!.Value,
                model.StorehouseSiteY!.Value,
                model.StorehouseSiteZ!.Value)
            : (GridPosition?)null;
        if (hasAnyStorehouseCoordinate != hasAllStorehouseCoordinates ||
            model.StorehouseWorkProgress < 0 ||
            model.StorehouseWorkProgress > definitions.HumanVillageEconomy.StorehouseWork)
        {
            throw new InvalidDataException("The save contains invalid human storehouse work.");
        }
        if (model.StorehouseCount != world.CountWorldObjects(
                WorldObjectKind.HumanStorehouse,
                WorldObjectOwner.HumanVillage))
        {
            throw new InvalidDataException("The human storehouse count does not match physical structures.");
        }

        var cohorts = model.Cohorts.OrderBy(item => item.Id).Select(item =>
        {
            var position = new GridPosition(item.X, item.Y, item.Z);
            if (item.Id <= 0 || !Enum.IsDefined(item.Role) || !Enum.IsDefined(item.Task) ||
                item.Population < 0 || item.SkillLevel is < 1 or > 10 ||
                !world.IsSurfaceTraversable(position) ||
                Distance(position, world.Baseline.HumanVillage) >
                    definitions.HumanVillageActivityRadius + 4)
            {
                throw new InvalidDataException(
                    $"The save contains invalid human cohort {item.Id} at {position} " +
                    $"with task {item.Task}; traversable={world.IsSurfaceTraversable(position)}, " +
                    $"distance={Distance(position, world.Baseline.HumanVillage)}.");
            }
            return new HumanCohortState(item.Id, item.Role, item.Population, position, item.Task, item.SkillLevel, item.Tools);
        }).ToList();
        var villagers = model.Villagers.OrderBy(item => item.Id).Select(item =>
        {
            var position = new GridPosition(item.X, item.Y, item.Z);
            var maximumHealth = GetMaximumHealth(item.Role, definitions);
            if (item.Id <= 0 || !Enum.IsDefined(item.Role) || !Enum.IsDefined(item.Task) ||
                item.SkillLevel is < 1 or > 10 || item.Health is < 0 ||
                item.Health > maximumHealth ||
                item.Fatigue is < 0 || item.Fatigue > definitions.HumanVillageNeeds.MaximumFatigue ||
                item.Hunger is < 0 || item.Hunger > definitions.HumanVillageNeeds.MaximumNeed ||
                item.Thirst is < 0 || item.Thirst > definitions.HumanVillageNeeds.MaximumNeed ||
                item.WorkProgress < 0 ||
                !world.IsSurfaceTraversable(position) ||
                Distance(position, world.Baseline.HumanVillage) >
                    definitions.HumanVillageActivityRadius + 4)
            {
                throw new InvalidDataException(
                    $"The save contains invalid human villager {item.Id} at {position}.");
            }
            return new HumanVillagerState(
                item.Id, item.Role, position, item.Task, item.SkillLevel, item.Tools,
                item.Health, maximumHealth, item.Fatigue, item.Hunger, item.Thirst,
                item.WorkProgress);
        }).ToList();
        var fields = model.Fields.OrderBy(item => item.Id).Select(item =>
        {
            var position = new GridPosition(item.X, item.Y, item.Z);
            if (item.Id <= 0 || !Enum.IsDefined(item.Phase) ||
                item.GrowthDays is < 0 ||
                item.GrowthDays > definitions.HumanVillageEconomy.CropGrowthDays ||
                item.WorkProgress < 0 ||
                item.WorkProgress > definitions.HumanVillageEconomy.FieldWorkPerStage ||
                !world.IsSurfaceTraversable(position) ||
                Distance(position, world.Baseline.HumanVillage) > definitions.HumanVillageActivityRadius)
            {
                throw new InvalidDataException("The save contains an invalid human field.");
            }
            return new HumanFieldState(
                item.Id, position, item.Phase, item.GrowthDays, item.WorkProgress);
        }).ToList();
        if (storehouseSite is { } savedSite &&
            !world.CanBuildHumanStorehouseAt(
                savedSite,
                world.Baseline.HumanVillage,
                definitions.HumanVillageActivityRadius,
                fields.Select(item => item.Position).Append(world.Baseline.HumanVillage).ToHashSet()))
        {
            throw new InvalidDataException("The saved human storehouse site is no longer valid.");
        }

        if (cohorts.Select(item => item.Id).Distinct().Count() != cohorts.Count ||
            cohorts.Select(item => item.Role).Distinct().Count() != cohorts.Count ||
            cohorts.Sum(item => item.Population) != model.Population ||
            villagers.Select(item => item.Id).Distinct().Count() != villagers.Count ||
            villagers.Count(item => item.Health > 0) != model.Population ||
            cohorts.Any(cohort => villagers.Count(villager =>
                villager.Role == cohort.Role && villager.Health > 0) != cohort.Population) ||
            fields.Select(item => item.Id).Distinct().Count() != fields.Count ||
            fields.Select(item => item.Position).Distinct().Count() != fields.Count)
        {
            throw new InvalidDataException("The human village cohorts or fields are inconsistent.");
        }

        var maximumGuardHitPoints = 2 * definitions.HumanGuardHealth;
        if (model.GuardHitPoints > maximumGuardHitPoints ||
            villagers.Where(item => item.Role == HumanCohortRole.Guards)
                .Sum(item => item.Health) != model.GuardHitPoints)
        {
            throw new InvalidDataException("The human guard health does not match its population.");
        }

        return new(
            world.Baseline.HumanVillage, definitions.HumanVillageNeeds,
            definitions.HumanVillageEconomy,
            FindWellAccesses(world),
            FindStructureAccesses(world, WorldObjectKind.HumanBarn),
            model.Population, model.FoodStock, model.WoodStock,
            model.GoodsStock, model.WaterStock, model.StorehouseCount, model.GoblinAttackOrdered,
            model.Hostility, model.LastIntruderSeenTick, model.GuardHitPoints,
            maximumGuardHitPoints, treeFellingTarget, model.TreeFellingProgress,
            model.GoodsWorkProgress,
            storehouseSite, model.StorehouseWorkProgress,
            cohorts, villagers, fields);
    }

    public HumanVillageUpdateResult Update(
        SimulationTick tick, WorldSeed worldSeed, WorldMapState world,
        NavigationPathService navigation,
        SimulationDefinitions definitions, IReadOnlyList<HumanIntruderSnapshot> intruders,
        int detectionRadius)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        var worldChanges = new List<WorldChangeEvent>();
        IReadOnlyList<HumanVillagerDeath> deaths = [];
        var calendar = SimulationCalendar.At(tick, definitions.Clock);
        if (calendar.IsDayStart)
        {
            worldChanges.AddRange(AdvanceDay(
                tick,
                calendar.AbsoluteDay,
                world,
                definitions,
                out deaths));
        }

        var nearby = intruders.Where(item => _villagers.Values.Any(villager =>
                villager.Health > 0 && Distance(item.Position, villager.Position) <= detectionRadius))
            .OrderBy(item => Distance(item.Position, Anchor)).ThenBy(item => item.Id).ToArray();
        var wasPeaceful = Hostility == 0;
        if (nearby.Length > 0)
        {
            Hostility = Math.Max(Hostility, GoblinAttackOrdered ? 100 : 25);
            LastIntruderSeenTick = tick.Value;
        }

        AssignTasks(nearby, world, definitions.HumanVillageActivityRadius);
        AssignVillagerTasks(calendar.IsNight);
        if (tick.Value % definitions.HumanCohortMovementIntervalTicks == 0)
        {
            MoveVillagers(
                tick,
                worldSeed,
                world,
                navigation,
                definitions.HumanVillageActivityRadius,
                definitions.ActorPlanning.MaximumPathExpansionsPerSlice,
                nearby.FirstOrDefault(),
                calendar.IsNight,
                worldChanges);
        }
        return new HumanVillageUpdateResult(
            wasPeaceful && Hostility > 0,
            worldChanges,
            deaths);
    }

    public void OrderGoblinAttack()
    {
        GoblinAttackOrdered = true;
        Hostility = 100;
        LastIntruderSeenTick = Math.Max(0, LastIntruderSeenTick);
    }

    public void EndGoblinAttack()
    {
        GoblinAttackOrdered = false;
    }

    public HumanVillageSnapshot CreateSnapshot() => new(
        Anchor, Population, FoodStock, WoodStock, GoodsStock, WaterStock, PlannedFieldCount,
        StorehouseCount, FoodCapacity, GoblinAttackOrdered, Hostility, LastIntruderSeenTick,
        GuardHitPoints, MaximumGuardHitPoints, _treeFellingTarget, _treeFellingProgress,
        _goodsWorkProgress,
        _storehouseSite, _storehouseWorkProgress,
        _cohorts.Values.Select(ToSnapshot).ToArray(),
        _villagers.Values.Select(ToSnapshot).ToArray(),
        _fields.Values.Select(item => new HumanFieldSnapshot(
            item.Id, item.Position, item.Phase, item.GrowthDays, item.WorkProgress)).ToArray());

    public HumanVillageSaveModel CreateSaveModel() => new()
    {
        Population = Population,
        FoodStock = FoodStock,
        WoodStock = WoodStock,
        GoodsStock = GoodsStock,
        WaterStock = WaterStock,
        StorehouseCount = StorehouseCount,
        GoblinAttackOrdered = GoblinAttackOrdered,
        Hostility = Hostility,
        LastIntruderSeenTick = LastIntruderSeenTick,
        GuardHitPoints = GuardHitPoints,
        TreeFellingX = _treeFellingTarget?.X,
        TreeFellingY = _treeFellingTarget?.Y,
        TreeFellingZ = _treeFellingTarget?.Z,
        TreeFellingProgress = _treeFellingProgress,
        GoodsWorkProgress = _goodsWorkProgress,
        StorehouseSiteX = _storehouseSite?.X,
        StorehouseSiteY = _storehouseSite?.Y,
        StorehouseSiteZ = _storehouseSite?.Z,
        StorehouseWorkProgress = _storehouseWorkProgress,
        Cohorts = _cohorts.Values.Select(item => new HumanCohortSaveModel
        {
            Id = item.Id, Role = item.Role, Population = item.Population,
            X = item.Position.X, Y = item.Position.Y, Z = item.Position.Z,
            Task = item.Task, SkillLevel = item.SkillLevel, Tools = item.Tools,
        }).ToList(),
        Villagers = _villagers.Values.Select(item => new HumanVillagerSaveModel
        {
            Id = item.Id, Role = item.Role,
            X = item.Position.X, Y = item.Position.Y, Z = item.Position.Z,
            Task = item.Task, SkillLevel = item.SkillLevel, Tools = item.Tools,
            Health = item.Health, Fatigue = item.Fatigue,
            Hunger = item.Hunger, Thirst = item.Thirst,
            WorkProgress = item.WorkProgress,
        }).ToList(),
        Fields = _fields.Values.Select(item => new HumanFieldSaveModel
        {
            Id = item.Id, X = item.Position.X, Y = item.Position.Y, Z = item.Position.Z,
            Phase = item.Phase, GrowthDays = item.GrowthDays,
            WorkProgress = item.WorkProgress,
        }).ToList(),
    };

    public HumanCohortSnapshot GetGuardSnapshot() => ToSnapshot(GetCohort(HumanCohortRole.Guards));

    public IReadOnlyList<HumanVillagerSnapshot> GetLivingGuardSnapshots() =>
        _villagers.Values.Where(item =>
                item.Role == HumanCohortRole.Guards && item.Health > 0)
            .OrderBy(item => item.Id)
            .Select(ToSnapshot)
            .ToArray();

    public IReadOnlyList<HumanVillagerSnapshot> GetLivingVillagerSnapshots() =>
        _villagers.Values.Where(item => item.Health > 0)
            .OrderBy(item => item.Id)
            .Select(ToSnapshot)
            .ToArray();

    public HumanVillagerSnapshot? GetVillagerSnapshot(int villagerId) =>
        _villagers.TryGetValue(villagerId, out var villager)
            ? ToSnapshot(villager)
            : null;

    internal IEnumerable<GridPosition> GetLivingCohortPositions() => _villagers.Values
        .Where(villager => villager.Health > 0)
        .Select(villager => villager.Position);

    public HumanVillagerDamageResult ApplyGuardDamage(int villagerId, int damage)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(damage);
        if (!_villagers.TryGetValue(villagerId, out var villager) ||
            villager.Role != HumanCohortRole.Guards || villager.Health <= 0)
        {
            return default;
        }

        return ApplyVillagerDamage(villagerId, damage);
    }

    public HumanVillagerDamageResult ApplyVillagerDamage(int villagerId, int damage)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(damage);
        if (!_villagers.TryGetValue(villagerId, out var villager) || villager.Health <= 0)
        {
            return default;
        }

        var cohort = GetCohort(villager.Role);
        var appliedDamage = Math.Min(villager.Health, damage);
        villager.Health -= appliedDamage;
        var died = villager.Health == 0;
        cohort.Population = _villagers.Values.Count(item =>
            item.Role == villager.Role && item.Health > 0);
        cohort.Position = _villagers.Values.FirstOrDefault(item =>
            item.Role == villager.Role && item.Health > 0)?.Position ?? cohort.Position;
        if (villager.Role == HumanCohortRole.Guards)
        {
            GuardHitPoints = _villagers.Values.Where(item =>
                    item.Role == HumanCohortRole.Guards)
                .Sum(item => item.Health);
        }
        if (died)
        {
            Population--;
        }
        Hostility = Math.Min(100, Hostility + 10);
        return new HumanVillagerDamageResult(
            villager.Id,
            villager.Position,
            appliedDamage,
            died);
    }

    private IReadOnlyList<HumanVillagerDeath> AdvanceVillagerNeeds(int absoluteDay)
    {
        var living = _villagers.Values.Where(item => item.Health > 0).ToArray();
        foreach (var villager in living)
        {
            villager.Hunger = Math.Min(
                _needs.MaximumNeed,
                villager.Hunger + _needs.DailyHungerIncrease);
            villager.Thirst = Math.Min(
                _needs.MaximumNeed,
                villager.Thirst + _needs.DailyThirstIncrease);
        }

        foreach (var villager in living
                     .Where(item => item.Hunger >= _needs.MealRelief)
                     .OrderByDescending(item => item.Hunger)
                     .ThenBy(item => (item.Id + absoluteDay) % Math.Max(1, living.Length)))
        {
            if (FoodStock <= 0)
            {
                break;
            }
            FoodStock--;
            villager.Hunger = Math.Max(0, villager.Hunger - _needs.MealRelief);
        }
        foreach (var villager in living
                     .Where(item => item.Thirst >= _needs.DrinkRelief)
                     .OrderByDescending(item => item.Thirst)
                     .ThenBy(item => (item.Id + absoluteDay) % Math.Max(1, living.Length)))
        {
            if (WaterStock <= 0)
            {
                break;
            }
            WaterStock--;
            villager.Thirst = Math.Max(0, villager.Thirst - _needs.DrinkRelief);
        }

        var deaths = new List<HumanVillagerDeath>();
        foreach (var villager in living)
        {
            var damage = 0;
            if (villager.Hunger >= _needs.MaximumNeed)
            {
                damage += Math.Max(1, villager.MaximumHealth / _needs.HungerDamageDivisor);
            }
            if (villager.Thirst >= _needs.MaximumNeed)
            {
                damage += Math.Max(1, villager.MaximumHealth / _needs.ThirstDamageDivisor);
            }
            if (damage == 0)
            {
                continue;
            }

            villager.Health = Math.Max(0, villager.Health - damage);
            if (villager.Health == 0)
            {
                villager.WorkProgress = 0;
                deaths.Add(new HumanVillagerDeath(villager.Id, villager.Position));
            }
        }

        Population = _villagers.Values.Count(item => item.Health > 0);
        GuardHitPoints = _villagers.Values.Where(item =>
                item.Role == HumanCohortRole.Guards)
            .Sum(item => item.Health);
        SynchronizeCohortsFromVillagers();
        return deaths;
    }

    private IReadOnlyList<WorldChangeEvent> AdvanceDay(
        SimulationTick tick,
        int absoluteDay,
        WorldMapState world,
        SimulationDefinitions definitions,
        out IReadOnlyList<HumanVillagerDeath> deaths)
    {
        deaths = AdvanceVillagerNeeds(absoluteDay);
        if (Population == 0)
        {
            return [];
        }
        var workers = GetCohort(HumanCohortRole.Workers);
        WaterStock = Math.Min(Population * 10, WaterStock + CompleteWaterWork());
        var worldChanges = new List<WorldChangeEvent>();
        var farmers = GetCohort(HumanCohortRole.Farmers);
        if (FoodStock < Population * 3 &&
            world.TryHarvest(farmers.Position, farmers.Population * 2, tick, out var gathered, out var change))
        {
            FoodStock = Math.Min(FoodCapacity, FoodStock + gathered);
            worldChanges.Add(change);
        }

        foreach (var field in _fields.Values)
        {
            if (field.WorkProgress < _economy.FieldWorkPerStage)
            {
                continue;
            }
            if (field.Phase == HumanFieldPhase.Cleared && WaterStock >= 2)
            {
                WaterStock -= 2;
                field.Phase = HumanFieldPhase.Sown;
                field.GrowthDays = 0;
                field.WorkProgress = 0;
            }
            else if (field.Phase is HumanFieldPhase.Sown or HumanFieldPhase.Growing)
            {
                field.GrowthDays++;
                field.Phase = field.GrowthDays >= _economy.CropGrowthDays
                    ? HumanFieldPhase.Ripe
                    : HumanFieldPhase.Growing;
                field.WorkProgress = 0;
            }
            else if (field.Phase == HumanFieldPhase.Ripe)
            {
                FoodStock = Math.Min(FoodCapacity, FoodStock + _economy.FieldYield);
                field.Phase = HumanFieldPhase.Cleared;
                field.GrowthDays = 0;
                field.WorkProgress = 0;
            }
        }

        if (_fields.Count < PlannedFieldCount && absoluteDay % 5 == 0)
        {
            var position = FindNextFieldPosition(world, definitions.HumanVillageActivityRadius);
            if (position is { } fieldPosition)
            {
                if (world.TryUprootBerryBush(fieldPosition, tick, out var clearingChange))
                {
                    worldChanges.Add(clearingChange);
                }

                var id = _fields.Count == 0 ? 1 : _fields.Keys.Max() + 1;
                _fields.Add(id, new(id, fieldPosition, HumanFieldPhase.Cleared, 0, 0));
            }
        }

        return worldChanges;
    }

    private void AssignTasks(
        IReadOnlyList<HumanIntruderSnapshot> intruders,
        WorldMapState world,
        int activityRadius)
    {
        var guard = GetCohort(HumanCohortRole.Guards);
        var canDefend = GoblinAttackOrdered || guard.Population * (guard.SkillLevel + 1) >= intruders.Count * 2;
        guard.Task = intruders.Count == 0 || canDefend ? HumanCohortTask.Guard : HumanCohortTask.Flee;
        var farmers = GetCohort(HumanCohortRole.Farmers);
        farmers.Task = intruders.Count > 0 ? HumanCohortTask.Flee :
            FoodStock < Population * 3 && !_fields.Values.Any(item => item.Phase == HumanFieldPhase.Ripe)
                ? HumanCohortTask.GatherBerries : HumanCohortTask.WorkFields;
        var workers = GetCohort(HumanCohortRole.Workers);
        if (WoodStock < _economy.StorehouseWoodCost)
        {
            EnsureTreeFellingTarget(world);
        }
        if (FoodStock >= FoodCapacity * 3 / 4 &&
            WoodStock >= _economy.StorehouseWoodCost)
        {
            EnsureStorehouseSite(world, activityRadius);
        }
        workers.Task = intruders.Count > 0 ? HumanCohortTask.Flee :
            WaterStock < Population * 3 ? HumanCohortTask.DrawWater :
            _fields.Count < PlannedFieldCount ? HumanCohortTask.ClearLand :
            WoodStock < _economy.StorehouseWoodCost && _treeFellingTarget is not null
                ? HumanCohortTask.ClearLand :
            FoodStock >= FoodCapacity * 3 / 4 &&
            WoodStock >= _economy.StorehouseWoodCost && _storehouseSite is not null
                ? HumanCohortTask.BuildStorehouse :
            GoodsStock < GoodsTarget &&
            WoodStock >= _economy.StorehouseWoodCost + _economy.GoodsWoodCost
                ? HumanCohortTask.CraftGoods : HumanCohortTask.StayNearVillage;
    }

    private void AssignVillagerTasks(bool isNight)
    {
        foreach (var villager in _villagers.Values.Where(item => item.Health > 0))
        {
            var assignedTask = GetCohort(villager.Role).Task;
            villager.Task = isNight || villager.Fatigue >= _needs.RestThreshold ||
                assignedTask == HumanCohortTask.DrawWater &&
                !villager.Tools.HasFlag(HumanTool.WoodenBucket) ||
                assignedTask == HumanCohortTask.WorkFields &&
                !villager.Tools.HasFlag(HumanTool.WoodenHoe) ||
                assignedTask == HumanCohortTask.ClearLand &&
                _treeFellingTarget is not null &&
                !villager.Tools.HasFlag(HumanTool.WoodenAxe) ||
                assignedTask == HumanCohortTask.CraftGoods &&
                !villager.Tools.HasFlag(HumanTool.WoodenAxe)
                    ? HumanCohortTask.StayNearVillage
                    : assignedTask;
        }
    }

    private void MoveVillagers(
        SimulationTick tick, WorldSeed worldSeed, WorldMapState world,
        NavigationPathService navigation,
        int activityRadius,
        int maximumPathExpansions,
        HumanIntruderSnapshot intruder,
        bool isNight,
        ICollection<WorldChangeEvent> worldChanges)
    {
        var occupied = _villagers.Values.Where(item => item.Health > 0)
            .Select(item => item.Position).ToHashSet();
        var livingVillagers = _villagers.Values.Where(item => item.Health > 0).ToArray();
        var pathBudget = Math.Max(1, maximumPathExpansions / Math.Max(1, livingVillagers.Length));
        foreach (var villager in livingVillagers)
        {
            occupied.Remove(villager.Position);
            var target = GetTaskTarget(villager, world, intruder);
            var villagerRadius = activityRadius +
                (villager.Task == HumanCohortTask.GatherBerries ? 4 : 0);
            var request = navigation.RequestPath(
                villager.Position,
                target,
                new NavigationPathContext(
                    OwnerId: 0x48554D4100000000UL | (uint)villager.Id,
                    PersonalKnowledgeVersion: 0,
                    SharedKnowledgeVersion: 0,
                    FreshnessBucket: 0,
                    ConstraintKey: 2),
                (from, to) => from.Z == 0 && to.Z == 0 &&
                    world.Baseline.CanTraverseSurfaceEdge(from, to),
                pathBudget,
                canOpenDoors: false);
            var route = request.Status == NavigationPathRequestStatus.Complete
                ? request.Path
                : null;
            if (route is { Count: > 0 } && Distance(route[0], Anchor) <= villagerRadius &&
                !occupied.Contains(route[0]))
            {
                villager.Position = route[0];
                if (villager.Task != HumanCohortTask.StayNearVillage)
                {
                    villager.Fatigue = Math.Min(
                        _needs.MaximumFatigue,
                        villager.Fatigue + _needs.WorkFatiguePerMove);
                }
            }
            else if (villager.Position == target && CanWanderWhileWorking(villager.Task) &&
                !(villager.Task == HumanCohortTask.ClearLand &&
                    _treeFellingTarget is not null))
            {
                var candidates = world.Baseline.GetCardinalNeighbors(villager.Position)
                    .Append(villager.Position)
                    .Where(item => Distance(item, Anchor) <= villagerRadius &&
                        world.IsSurfaceTraversable(item) && !occupied.Contains(item))
                    .OrderBy(item => item.Y).ThenBy(item => item.X).ToArray();
                if (candidates.Length > 0)
                {
                    villager.Position = candidates[DeterministicRandom.NextInt(
                        worldSeed, RandomDomain.HumanVillage, new EntityId((ulong)villager.Id),
                        tick, 0, 0, candidates.Length)];
                }
            }
            if (villager.Task == HumanCohortTask.StayNearVillage &&
                Distance(villager.Position, Anchor) <= 3)
            {
                villager.Fatigue = Math.Max(
                    0,
                    villager.Fatigue - (isNight
                        ? _needs.NightRestRecoveryPerMove
                        : _needs.DayRestRecoveryPerMove));
            }
            else if (!isNight && villager.Task == HumanCohortTask.DrawWater &&
                villager.Tools.HasFlag(HumanTool.WoodenBucket) &&
                _wellAccesses.Contains(villager.Position))
            {
                villager.WorkProgress = checked(villager.WorkProgress + villager.SkillLevel + 1);
            }
            else if (!isNight && villager.Task == HumanCohortTask.WorkFields &&
                villager.Tools.HasFlag(HumanTool.WoodenHoe))
            {
                var field = _fields.Values.FirstOrDefault(item =>
                    item.Position == villager.Position &&
                    item.WorkProgress < _economy.FieldWorkPerStage);
                if (field is not null)
                {
                    field.WorkProgress = Math.Min(
                        _economy.FieldWorkPerStage,
                        checked(field.WorkProgress + villager.SkillLevel + 1));
                }
            }
            else if (!isNight && villager.Task == HumanCohortTask.ClearLand &&
                villager.Tools.HasFlag(HumanTool.WoodenAxe) &&
                _treeFellingTarget is { } treeTarget &&
                Distance(villager.Position, treeTarget) == 1)
            {
                _treeFellingProgress = Math.Min(
                    _economy.TreeFellingWork,
                    checked(_treeFellingProgress + villager.SkillLevel + 1));
                if (_treeFellingProgress >= _economy.TreeFellingWork &&
                    world.TryHarvestFellableWood(
                        treeTarget, tick, out var woodQuantity, out var change))
                {
                    WoodStock = checked(WoodStock + woodQuantity);
                    worldChanges.Add(change);
                    _treeFellingTarget = null;
                    _treeFellingProgress = 0;
                }
            }
            else if (!isNight && villager.Task == HumanCohortTask.CraftGoods &&
                villager.Tools.HasFlag(HumanTool.WoodenAxe) &&
                _goodsWorkshopAccesses.Contains(villager.Position) &&
                GoodsStock < GoodsTarget &&
                WoodStock >= _economy.StorehouseWoodCost + _economy.GoodsWoodCost)
            {
                _goodsWorkProgress = Math.Min(
                    _economy.GoodsWorkPerUnit,
                    checked(_goodsWorkProgress + villager.SkillLevel + 1));
                if (_goodsWorkProgress >= _economy.GoodsWorkPerUnit)
                {
                    WoodStock -= _economy.GoodsWoodCost;
                    GoodsStock++;
                    _goodsWorkProgress = 0;
                }
            }
            else if (!isNight && villager.Task == HumanCohortTask.BuildStorehouse &&
                _storehouseSite is { } storehouseSite &&
                IsWithinSquare(villager.Position, storehouseSite, size: 3))
            {
                _storehouseWorkProgress = Math.Min(
                    _economy.StorehouseWork,
                    checked(_storehouseWorkProgress + villager.SkillLevel + 1));
                if (_storehouseWorkProgress >= _economy.StorehouseWork)
                {
                    var reserved = _fields.Values.Select(item => item.Position)
                        .Append(Anchor)
                        .ToHashSet();
                    if (world.TryBuildHumanStorehouseAt(
                            storehouseSite,
                            Anchor,
                            activityRadius,
                            reserved,
                            tick,
                            out var storehouseChange))
                    {
                        WoodStock -= _economy.StorehouseWoodCost;
                        StorehouseCount++;
                        worldChanges.Add(storehouseChange);
                        _storehouseSite = null;
                        _storehouseWorkProgress = 0;
                        RelocateVillagersFromBlockedCells(world, activityRadius);
                        return;
                    }
                    else
                    {
                        _storehouseSite = null;
                        _storehouseWorkProgress = 0;
                    }
                }
            }
            occupied.Add(villager.Position);
        }
        SynchronizeCohortsFromVillagers();
    }

    private static bool CanWanderWhileWorking(HumanCohortTask task) => task is
        HumanCohortTask.WorkFields or
        HumanCohortTask.ClearLand or
        HumanCohortTask.GatherBerries or
        HumanCohortTask.Guard;

    private GridPosition GetTaskTarget(
        HumanVillagerState villager,
        WorldMapState world,
        HumanIntruderSnapshot intruder)
    {
        var position = villager.Position;
        var task = villager.Task;
        if (task == HumanCohortTask.DrawWater)
        {
            return _wellAccesses[(villager.Id - 1) % _wellAccesses.Count];
        }
        if (task == HumanCohortTask.CraftGoods)
        {
            return _goodsWorkshopAccesses[(villager.Id - 1) % _goodsWorkshopAccesses.Count];
        }
        if (task == HumanCohortTask.BuildStorehouse && _storehouseSite is { } storehouseSite)
        {
            var offset = (villager.Id - 1) % 9;
            return new GridPosition(
                storehouseSite.X + offset % 3,
                storehouseSite.Y + offset / 3,
                storehouseSite.Z);
        }
        if (task == HumanCohortTask.Flee)
        {
            return Anchor;
        }
        if (task == HumanCohortTask.Guard && intruder.Id != EntityId.None)
        {
            return intruder.Position;
        }
        if (task == HumanCohortTask.GatherBerries)
        {
            return world.FindNearestHarvestablePlantPosition(
                position,
                Anchor,
                radius: 12,
                PlantKind.BerryBush) ?? Anchor;
        }
        if (task == HumanCohortTask.WorkFields)
        {
            var fields = _fields.Values.Where(item =>
                    item.WorkProgress < _economy.FieldWorkPerStage)
                .OrderBy(item => item.Phase == HumanFieldPhase.Ripe ? 0 :
                    item.Phase == HumanFieldPhase.Cleared ? 1 : 2)
                .ThenBy(item => item.Id)
                .ToArray();
            return fields.Length == 0
                ? Anchor
                : fields[(villager.Id - 1) % fields.Length].Position;
        }
        if (task == HumanCohortTask.ClearLand)
        {
            if (_treeFellingTarget is { } treeTarget)
            {
                return world.Baseline.GetCardinalNeighbors(treeTarget)
                    .Where(item => world.IsSurfaceTraversable(item) &&
                        Distance(item, Anchor) <= _economy.TreeSearchRadius)
                    .OrderBy(item => Distance(item, position))
                    .ThenBy(item => item.Y)
                    .ThenBy(item => item.X)
                    .FirstOrDefault(Anchor);
            }
            if (_fields.Count > 0)
            {
                return _fields.Values.OrderBy(item => Distance(item.Position, position))
                    .ThenBy(item => item.Id).First().Position;
            }
        }
        return Anchor;
    }

    private GridPosition? FindNextFieldPosition(WorldMapState world, int radius)
    {
        var occupied = _fields.Values.Select(item => item.Position)
            .Concat(_villagers.Values.Where(item => item.Health > 0)
                .Select(item => item.Position)).Append(Anchor).ToHashSet();
        foreach (var position in FindPositions(world, radius))
        {
            var vegetation = world.GetPlantPatch(position);
            if (!occupied.Contains(position) &&
                vegetation is null or { Kind: PlantKind.BerryBush })
            {
                return position;
            }
        }
        return null;
    }

    private void EnsureTreeFellingTarget(WorldMapState world)
    {
        if (_treeFellingTarget is { } current && world.GetFellableWood(current) is not null)
        {
            return;
        }

        _treeFellingTarget = world.EnumerateWorldObjects()
            .Where(item => (item.Kind is WorldObjectKind.Tree or WorldObjectKind.DeadTreeStump) &&
                Distance(item.Anchor, Anchor) <= _economy.TreeSearchRadius)
            .Where(item => world.Baseline.GetCardinalNeighbors(item.Anchor)
                .Any(position => world.IsSurfaceTraversable(position) &&
                    Distance(position, Anchor) <= _economy.TreeSearchRadius))
            .OrderBy(item => Distance(item.Anchor, Anchor))
            .ThenBy(item => item.Anchor.Y)
            .ThenBy(item => item.Anchor.X)
            .Select(item => (GridPosition?)item.Anchor)
            .FirstOrDefault();
        _treeFellingProgress = 0;
    }

    private void EnsureStorehouseSite(WorldMapState world, int activityRadius)
    {
        var reserved = _fields.Values.Select(item => item.Position)
            .Append(Anchor)
            .ToHashSet();
        if (_storehouseSite is { } current &&
            world.CanBuildHumanStorehouseAt(
                current, Anchor, activityRadius, reserved))
        {
            return;
        }

        _storehouseSite = world.FindHumanStorehousePlacement(
            Anchor, activityRadius, reserved);
        _storehouseWorkProgress = 0;
    }

    private static bool IsWithinSquare(
        GridPosition position,
        GridPosition anchor,
        int size) =>
        position.Z == anchor.Z &&
        position.X >= anchor.X && position.X < anchor.X + size &&
        position.Y >= anchor.Y && position.Y < anchor.Y + size;

    private HumanCohortState GetCohort(HumanCohortRole role) => _cohorts.Values.Single(item => item.Role == role);
    private static HumanCohortSnapshot ToSnapshot(HumanCohortState item) =>
        new(item.Id, item.Role, item.Population, item.Position, item.Task, item.SkillLevel, item.Tools);
    private HumanVillagerSnapshot ToSnapshot(HumanVillagerState item) =>
        new(
            item.Id,
            VillagerNames[(item.Id - 1) % VillagerNames.Length],
            item.Role,
            item.Position,
            item.Task,
            item.SkillLevel,
            item.Tools,
            item.Health,
            item.MaximumHealth,
            item.Fatigue,
            _needs.MaximumFatigue,
            item.Hunger,
            item.Thirst,
            _needs.MaximumNeed,
            item.WorkProgress);
    internal static int GetMaximumHealth(
        HumanCohortRole role,
        SimulationDefinitions definitions) => role == HumanCohortRole.Guards
        ? definitions.HumanGuardHealth
        : Math.Max(1, definitions.HumanGuardHealth / 2);

    internal static HumanTool GetIndividualTools(HumanCohortRole role, int roleIndex) =>
        role switch
        {
            HumanCohortRole.Farmers => HumanTool.WoodenHoe,
            HumanCohortRole.Guards => HumanTool.WoodenSpear,
            HumanCohortRole.Workers when roleIndex % 2 == 0 => HumanTool.WoodenAxe,
            HumanCohortRole.Workers => HumanTool.WoodenBucket,
            _ => HumanTool.None,
        };

    private static IReadOnlyList<HumanVillagerState> CreateInitialVillagers(
        IReadOnlyList<GridPosition> positions,
        IReadOnlyList<HumanCohortState> cohorts,
        SimulationDefinitions definitions)
    {
        var result = new List<HumanVillagerState>(cohorts.Sum(item => item.Population));
        var id = 1;
        var positionIndex = 0;
        foreach (var cohort in cohorts.OrderBy(item => item.Id))
        {
            for (var index = 0; index < cohort.Population; index++)
            {
                result.Add(new HumanVillagerState(
                    id++,
                    cohort.Role,
                    positions[positionIndex++],
                    cohort.Task,
                    cohort.SkillLevel,
                    GetIndividualTools(cohort.Role, index),
                    GetMaximumHealth(cohort.Role, definitions),
                    GetMaximumHealth(cohort.Role, definitions),
                    fatigue: 0,
                    hunger: 0,
                    thirst: 0,
                    workProgress: 0));
            }
        }
        return result;
    }

    private void SynchronizeCohortsFromVillagers()
    {
        foreach (var cohort in _cohorts.Values)
        {
            var living = _villagers.Values.Where(item =>
                item.Role == cohort.Role && item.Health > 0).ToArray();
            cohort.Population = living.Length;
            if (living.Length > 0)
            {
                cohort.Position = living[0].Position;
            }
        }
    }

    private void RelocateVillagersFromBlockedCells(WorldMapState world, int radius)
    {
        var occupied = _villagers.Values.Where(item => item.Health > 0 &&
                world.IsSurfaceTraversable(item.Position))
            .Select(item => item.Position)
            .ToHashSet();
        var available = FindPositions(world, radius)
            .Where(position => !occupied.Contains(position))
            .ToArray();
        var next = 0;
        foreach (var villager in _villagers.Values.Where(item =>
                     item.Health > 0 && !world.IsSurfaceTraversable(item.Position)))
        {
            if (next >= available.Length)
            {
                throw new InvalidOperationException(
                    "The human storehouse blocked every relocation position.");
            }
            villager.Position = available[next++];
            occupied.Add(villager.Position);
        }
        SynchronizeCohortsFromVillagers();
    }

    private static IReadOnlyList<GridPosition> FindPositions(WorldMapState world, int radius)
    {
        var result = new List<GridPosition>();
        var visited = new HashSet<GridPosition> { world.Baseline.HumanVillage };
        var queue = new Queue<GridPosition>();
        queue.Enqueue(world.Baseline.HumanVillage);
        while (queue.TryDequeue(out var current))
        {
            if (world.IsSurfaceTraversable(current)) result.Add(current);
            foreach (var neighbor in world.Baseline.GetCardinalNeighbors(current))
            {
                if (Distance(neighbor, world.Baseline.HumanVillage) <= radius &&
                    world.IsSurfaceTraversable(neighbor) &&
                    world.Baseline.CanTraverseSurfaceEdge(current, neighbor) &&
                    visited.Add(neighbor)) queue.Enqueue(neighbor);
            }
        }
        return result;
    }

    private static int Distance(GridPosition left, GridPosition right) =>
        Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y) + Math.Abs(left.Z - right.Z);

    private int CompleteWaterWork()
    {
        var completed = 0;
        foreach (var villager in _villagers.Values.Where(item =>
                     item.Health > 0 && item.Tools.HasFlag(HumanTool.WoodenBucket)))
        {
            completed = checked(completed + villager.WorkProgress / _economy.WaterWorkPerUnit);
            villager.WorkProgress %= _economy.WaterWorkPerUnit;
        }
        return completed;
    }

    private static IReadOnlyList<GridPosition> FindWellAccesses(WorldMapState world)
    {
        var well = world.EnumerateWorldObjects().Single(item =>
            item.Kind == WorldObjectKind.HumanWell &&
            item.Owner == WorldObjectOwner.HumanVillage);
        var wellCells = well.GetAbsoluteParts()
            .Where(item => item.Part.Channel == SpatialOccupancyChannel.Solid)
            .Select(item => item.Position)
            .ToHashSet();
        var accesses = wellCells
            .SelectMany(world.Baseline.GetCardinalNeighbors)
            .Where(position => !wellCells.Contains(position) &&
                world.IsSurfaceTraversable(position))
            .Distinct()
            .OrderBy(position => position.Y)
            .ThenBy(position => position.X)
            .ToArray();
        if (accesses.Length == 0)
        {
            throw new InvalidOperationException("The human well has no traversable access position.");
        }
        return accesses;
    }

    private static IReadOnlyList<GridPosition> FindStructureAccesses(
        WorldMapState world,
        WorldObjectKind kind)
    {
        var structure = world.EnumerateWorldObjects().Single(item =>
            item.Kind == kind && item.Owner == WorldObjectOwner.HumanVillage);
        var accesses = structure.GetAbsoluteParts()
            .Where(item => item.Part.Kind == WorldObjectPartKind.Floor &&
                world.IsSurfaceTraversable(item.Position))
            .Select(item => item.Position)
            .Distinct()
            .OrderBy(position => position.Y)
            .ThenBy(position => position.X)
            .ToArray();
        if (accesses.Length == 0)
        {
            throw new InvalidOperationException(
                $"The human {kind} has no traversable work position.");
        }
        return accesses;
    }

    private sealed class HumanCohortState(
        int id, HumanCohortRole role, int population, GridPosition position,
        HumanCohortTask task, int skillLevel, HumanTool tools)
    {
        public int Id { get; } = id;
        public HumanCohortRole Role { get; } = role;
        public int Population { get; set; } = population;
        public GridPosition Position { get; set; } = position;
        public HumanCohortTask Task { get; set; } = task;
        public int SkillLevel { get; } = skillLevel;
        public HumanTool Tools { get; } = tools;
    }

    private sealed class HumanVillagerState(
        int id,
        HumanCohortRole role,
        GridPosition position,
        HumanCohortTask task,
        int skillLevel,
        HumanTool tools,
        int health,
        int maximumHealth,
        int fatigue,
        int hunger,
        int thirst,
        int workProgress)
    {
        public int Id { get; } = id;
        public HumanCohortRole Role { get; } = role;
        public GridPosition Position { get; set; } = position;
        public HumanCohortTask Task { get; set; } = task;
        public int SkillLevel { get; } = skillLevel;
        public HumanTool Tools { get; } = tools;
        public int MaximumHealth { get; } = maximumHealth;
        public int Health { get; set; } = health;
        public int Fatigue { get; set; } = fatigue;
        public int Hunger { get; set; } = hunger;
        public int Thirst { get; set; } = thirst;
        public int WorkProgress { get; set; } = workProgress;
    }

    private sealed class HumanFieldState(
        int id,
        GridPosition position,
        HumanFieldPhase phase,
        int growthDays,
        int workProgress)
    {
        public int Id { get; } = id;
        public GridPosition Position { get; } = position;
        public HumanFieldPhase Phase { get; set; } = phase;
        public int GrowthDays { get; set; } = growthDays;
        public int WorkProgress { get; set; } = workProgress;
    }
}

internal readonly record struct HumanIntruderSnapshot(EntityId Id, GridPosition Position);

internal readonly record struct HumanVillagerDamageResult(
    int VillagerId,
    GridPosition Position,
    int Damage,
    bool Died);

internal readonly record struct HumanVillagerDeath(
    int VillagerId,
    GridPosition Position);

internal readonly record struct HumanVillageUpdateResult(
    bool Alerted,
    IReadOnlyList<WorldChangeEvent> WorldChanges,
    IReadOnlyList<HumanVillagerDeath> Deaths);
