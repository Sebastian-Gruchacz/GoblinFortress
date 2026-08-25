using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation;

internal sealed class HumanVillageState
{
    private const int CropGrowthDays = 120;
    private const int FieldYield = 180;
    private const int BaseFoodCapacity = 240;
    private const int StorehouseCapacity = 240;
    private const int StorehouseWoodCost = 24;

    private readonly SortedDictionary<int, HumanCohortState> _cohorts;
    private readonly SortedDictionary<int, HumanFieldState> _fields;

    private HumanVillageState(
        GridPosition anchor, int population, int foodStock, int woodStock, int goodsStock,
        int waterStock, int storehouseCount, bool goblinAttackOrdered, int hostility,
        long lastIntruderSeenTick, int guardHitPoints, int maximumGuardHitPoints,
        IEnumerable<HumanCohortState> cohorts, IEnumerable<HumanFieldState> fields)
    {
        Anchor = anchor;
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
        _cohorts = new(cohorts.ToDictionary(item => item.Id));
        _fields = new(fields.ToDictionary(item => item.Id));
    }

    public GridPosition Anchor { get; }
    public int Population { get; private set; }
    public int FoodStock { get; private set; }
    public int WoodStock { get; private set; }
    public int GoodsStock { get; private set; }
    public int WaterStock { get; private set; }
    public int StorehouseCount { get; private set; }
    public bool GoblinAttackOrdered { get; private set; }
    public int Hostility { get; private set; }
    public long LastIntruderSeenTick { get; private set; }
    public int GuardHitPoints { get; private set; }
    public int MaximumGuardHitPoints { get; }
    public int PlannedFieldCount => Math.Max(1, (Population * CropGrowthDays + FieldYield - 1) / FieldYield);
    public int FoodCapacity => checked(BaseFoodCapacity + StorehouseCount * StorehouseCapacity);

    public static HumanVillageState CreateInitial(WorldMapState world, SimulationDefinitions definitions)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(definitions);
        var positions = FindPositions(world, definitions.HumanVillageActivityRadius);
        if (positions.Count < 7)
        {
            throw new InvalidOperationException("The human village has too few traversable work positions.");
        }

        var initialFieldPositions = positions.Skip(3)
            .Where(position => world.GetPlantPatch(position) is null)
            .Take(4)
            .ToArray();
        if (initialFieldPositions.Length < 4)
        {
            throw new InvalidOperationException("The human village has too few pre-cleared field positions.");
        }

        return new(
            world.Baseline.HumanVillage, 12, 48, 24, 4, 36, 0, false, 0, -1,
            2 * definitions.HumanGuardHealth, 2 * definitions.HumanGuardHealth,
            [
                new(1, HumanCohortRole.Farmers, 4, positions[0], HumanCohortTask.WorkFields, 3, HumanTool.WoodenHoe),
                new(2, HumanCohortRole.Workers, 6, positions[1], HumanCohortTask.DrawWater, 2, HumanTool.WoodenAxe | HumanTool.WoodenBucket),
                new(3, HumanCohortRole.Guards, 2, positions[2], HumanCohortTask.Guard, 2, HumanTool.WoodenSpear),
            ],
            initialFieldPositions.Select((position, index) =>
                new HumanFieldState(index + 1, position, HumanFieldPhase.Sown, 0)));
    }

    public static HumanVillageState Restore(
        WorldMapState world, HumanVillageSaveModel model,
        SimulationDefinitions definitions, SimulationTick currentTick)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(model);
        if (model.Population <= 0 || model.FoodStock < 0 || model.WoodStock < 0 ||
            model.GoodsStock < 0 || model.WaterStock < 0 || model.StorehouseCount < 0 ||
            model.Hostility is < 0 or > 100 || model.LastIntruderSeenTick < -1 ||
            model.LastIntruderSeenTick > currentTick.Value ||
            (model.Hostility == 0) != (model.LastIntruderSeenTick == -1) ||
            model.GuardHitPoints < 0 || model.Cohorts.Count != 3)
        {
            throw new InvalidDataException("The save contains invalid human village state.");
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
        var fields = model.Fields.OrderBy(item => item.Id).Select(item =>
        {
            var position = new GridPosition(item.X, item.Y, item.Z);
            if (item.Id <= 0 || !Enum.IsDefined(item.Phase) || item.GrowthDays is < 0 or > CropGrowthDays ||
                !world.IsSurfaceTraversable(position) ||
                Distance(position, world.Baseline.HumanVillage) > definitions.HumanVillageActivityRadius)
            {
                throw new InvalidDataException("The save contains an invalid human field.");
            }
            return new HumanFieldState(item.Id, position, item.Phase, item.GrowthDays);
        }).ToList();

        if (cohorts.Select(item => item.Id).Distinct().Count() != cohorts.Count ||
            cohorts.Select(item => item.Role).Distinct().Count() != cohorts.Count ||
            cohorts.Sum(item => item.Population) != model.Population ||
            fields.Select(item => item.Id).Distinct().Count() != fields.Count ||
            fields.Select(item => item.Position).Distinct().Count() != fields.Count)
        {
            throw new InvalidDataException("The human village cohorts or fields are inconsistent.");
        }

        var guardPopulation = cohorts.Single(item => item.Role == HumanCohortRole.Guards).Population;
        var maximumGuardHitPoints = 2 * definitions.HumanGuardHealth;
        if (model.GuardHitPoints > maximumGuardHitPoints ||
            guardPopulation != PopulationForHitPoints(model.GuardHitPoints, definitions.HumanGuardHealth))
        {
            throw new InvalidDataException("The human guard health does not match its population.");
        }

        return new(
            world.Baseline.HumanVillage, model.Population, model.FoodStock, model.WoodStock,
            model.GoodsStock, model.WaterStock, model.StorehouseCount, model.GoblinAttackOrdered,
            model.Hostility, model.LastIntruderSeenTick, model.GuardHitPoints,
            maximumGuardHitPoints, cohorts, fields);
    }

    public HumanVillageUpdateResult Update(
        SimulationTick tick, WorldSeed worldSeed, WorldMapState world,
        NavigationPathService navigation,
        SimulationDefinitions definitions, IReadOnlyList<HumanIntruderSnapshot> intruders,
        int detectionRadius)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        IReadOnlyList<WorldChangeEvent> worldChanges = [];
        var calendar = SimulationCalendar.At(tick, definitions.Clock);
        if (calendar.IsDayStart)
        {
            worldChanges = AdvanceDay(tick, calendar.AbsoluteDay, world, definitions);
        }

        var nearby = intruders.Where(item => _cohorts.Values.Any(cohort => cohort.Population > 0 &&
                Distance(item.Position, cohort.Position) <= detectionRadius))
            .OrderBy(item => Distance(item.Position, Anchor)).ThenBy(item => item.Id).ToArray();
        var wasPeaceful = Hostility == 0;
        if (nearby.Length > 0)
        {
            Hostility = Math.Max(Hostility, GoblinAttackOrdered ? 100 : 25);
            LastIntruderSeenTick = tick.Value;
        }

        AssignTasks(nearby);
        if (tick.Value % definitions.HumanCohortMovementIntervalTicks == 0)
        {
            MoveCohorts(
                tick,
                worldSeed,
                world,
                navigation,
                definitions.HumanVillageActivityRadius,
                nearby.FirstOrDefault());
        }
        return new HumanVillageUpdateResult(wasPeaceful && Hostility > 0, worldChanges);
    }

    public void OrderGoblinAttack()
    {
        GoblinAttackOrdered = true;
        Hostility = 100;
        LastIntruderSeenTick = Math.Max(0, LastIntruderSeenTick);
    }

    public HumanVillageSnapshot CreateSnapshot() => new(
        Anchor, Population, FoodStock, WoodStock, GoodsStock, WaterStock, PlannedFieldCount,
        StorehouseCount, FoodCapacity, GoblinAttackOrdered, Hostility, LastIntruderSeenTick,
        GuardHitPoints, MaximumGuardHitPoints, _cohorts.Values.Select(ToSnapshot).ToArray(),
        _fields.Values.Select(item => new HumanFieldSnapshot(item.Id, item.Position, item.Phase, item.GrowthDays)).ToArray());

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
        Cohorts = _cohorts.Values.Select(item => new HumanCohortSaveModel
        {
            Id = item.Id, Role = item.Role, Population = item.Population,
            X = item.Position.X, Y = item.Position.Y, Z = item.Position.Z,
            Task = item.Task, SkillLevel = item.SkillLevel, Tools = item.Tools,
        }).ToList(),
        Fields = _fields.Values.Select(item => new HumanFieldSaveModel
        {
            Id = item.Id, X = item.Position.X, Y = item.Position.Y, Z = item.Position.Z,
            Phase = item.Phase, GrowthDays = item.GrowthDays,
        }).ToList(),
    };

    public HumanCohortSnapshot GetGuardSnapshot() => ToSnapshot(GetCohort(HumanCohortRole.Guards));

    internal IEnumerable<GridPosition> GetLivingCohortPositions() => _cohorts.Values
        .Where(cohort => cohort.Population > 0)
        .Select(cohort => cohort.Position);

    public int ApplyGuardDamage(int damage, int healthPerGuard)
    {
        var guard = GetCohort(HumanCohortRole.Guards);
        var previousPopulation = guard.Population;
        GuardHitPoints = Math.Max(0, GuardHitPoints - damage);
        guard.Population = PopulationForHitPoints(GuardHitPoints, healthPerGuard);
        var deaths = previousPopulation - guard.Population;
        Population -= deaths;
        Hostility = Math.Min(100, Hostility + 10);
        return deaths;
    }

    private IReadOnlyList<WorldChangeEvent> AdvanceDay(
        SimulationTick tick,
        int absoluteDay,
        WorldMapState world,
        SimulationDefinitions definitions)
    {
        FoodStock = Math.Max(0, FoodStock - Population);
        WaterStock = Math.Max(0, WaterStock - Population);
        var workers = GetCohort(HumanCohortRole.Workers);
        WaterStock = Math.Min(Population * 10, WaterStock + workers.Population * (workers.SkillLevel + 1));
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
            if (field.Phase == HumanFieldPhase.Cleared && WaterStock >= 2)
            {
                WaterStock -= 2;
                field.Phase = HumanFieldPhase.Sown;
                field.GrowthDays = 0;
            }
            else if (field.Phase is HumanFieldPhase.Sown or HumanFieldPhase.Growing)
            {
                field.GrowthDays++;
                field.Phase = field.GrowthDays >= CropGrowthDays ? HumanFieldPhase.Ripe : HumanFieldPhase.Growing;
            }
            else if (field.Phase == HumanFieldPhase.Ripe)
            {
                FoodStock = Math.Min(FoodCapacity, FoodStock + FieldYield);
                field.Phase = HumanFieldPhase.Cleared;
                field.GrowthDays = 0;
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
                _fields.Add(id, new(id, fieldPosition, HumanFieldPhase.Cleared, 0));
                WoodStock += workers.Population;
            }
        }

        if (FoodStock >= FoodCapacity * 3 / 4 && WoodStock >= StorehouseWoodCost)
        {
            var reserved = _fields.Values.Select(item => item.Position)
                .Concat(_cohorts.Values.Select(item => item.Position))
                .ToHashSet();
            if (world.TryBuildHumanStorehouse(
                    Anchor,
                    definitions.HumanVillageActivityRadius,
                    reserved,
                    tick,
                    out var storehouseChange))
            {
                WoodStock -= StorehouseWoodCost;
                StorehouseCount++;
                worldChanges.Add(storehouseChange);
            }
        }
        else if (WoodStock >= StorehouseWoodCost + 2 && absoluteDay % 7 == 0)
        {
            WoodStock -= 2;
            GoodsStock++;
        }
        return worldChanges;
    }

    private void AssignTasks(IReadOnlyList<HumanIntruderSnapshot> intruders)
    {
        var guard = GetCohort(HumanCohortRole.Guards);
        var canDefend = GoblinAttackOrdered || guard.Population * (guard.SkillLevel + 1) >= intruders.Count * 2;
        guard.Task = intruders.Count == 0 || canDefend ? HumanCohortTask.Guard : HumanCohortTask.Flee;
        var farmers = GetCohort(HumanCohortRole.Farmers);
        farmers.Task = intruders.Count > 0 ? HumanCohortTask.Flee :
            FoodStock < Population * 3 && !_fields.Values.Any(item => item.Phase == HumanFieldPhase.Ripe)
                ? HumanCohortTask.GatherBerries : HumanCohortTask.WorkFields;
        var workers = GetCohort(HumanCohortRole.Workers);
        workers.Task = intruders.Count > 0 ? HumanCohortTask.Flee :
            WaterStock < Population * 3 ? HumanCohortTask.DrawWater :
            _fields.Count < PlannedFieldCount ? HumanCohortTask.ClearLand :
            FoodStock >= FoodCapacity * 3 / 4 && WoodStock >= StorehouseWoodCost
                ? HumanCohortTask.BuildStorehouse : HumanCohortTask.StayNearVillage;
    }

    private void MoveCohorts(
        SimulationTick tick, WorldSeed worldSeed, WorldMapState world,
        NavigationPathService navigation,
        int activityRadius, HumanIntruderSnapshot intruder)
    {
        var occupied = _cohorts.Values.Select(item => item.Position).ToHashSet();
        foreach (var cohort in _cohorts.Values.Where(item => item.Population > 0))
        {
            occupied.Remove(cohort.Position);
            var target = GetTaskTarget(cohort, world, intruder);
            var cohortRadius = activityRadius + (cohort.Task == HumanCohortTask.GatherBerries ? 4 : 0);
            var route = navigation.FindSurfacePath(cohort.Position, target);
            if (route is { Count: > 0 } && Distance(route[0], Anchor) <= cohortRadius && !occupied.Contains(route[0]))
            {
                cohort.Position = route[0];
            }
            else if (cohort.Position == target)
            {
                var candidates = world.Baseline.GetCardinalNeighbors(cohort.Position).Append(cohort.Position)
                    .Where(item => Distance(item, Anchor) <= cohortRadius && world.IsSurfaceTraversable(item) && !occupied.Contains(item))
                    .OrderBy(item => item.Y).ThenBy(item => item.X).ToArray();
                if (candidates.Length > 0)
                {
                    cohort.Position = candidates[DeterministicRandom.NextInt(
                        worldSeed, RandomDomain.HumanVillage, new EntityId((ulong)cohort.Id), tick, 0, 0, candidates.Length)];
                }
            }
            occupied.Add(cohort.Position);
        }
    }

    private GridPosition GetTaskTarget(HumanCohortState cohort, WorldMapState world, HumanIntruderSnapshot intruder)
    {
        if (cohort.Task == HumanCohortTask.Flee || cohort.Task is HumanCohortTask.DrawWater or HumanCohortTask.BuildStorehouse)
        {
            return Anchor;
        }
        if (cohort.Task == HumanCohortTask.Guard && intruder.Id != EntityId.None)
        {
            return intruder.Position;
        }
        if (cohort.Task == HumanCohortTask.GatherBerries)
        {
            return world.CreatePlantSnapshot().Where(item => item.Kind == PlantKind.BerryBush && item.Biomass > 0)
                .Where(item => Distance(item.Position, Anchor) <= 12)
                .OrderBy(item => Distance(item.Position, cohort.Position)).ThenBy(item => item.Position.Y).ThenBy(item => item.Position.X)
                .Select(item => item.Position).FirstOrDefault(Anchor);
        }
        if (cohort.Task is HumanCohortTask.WorkFields or HumanCohortTask.ClearLand && _fields.Count > 0)
        {
            return _fields.Values.OrderBy(item => Distance(item.Position, cohort.Position)).ThenBy(item => item.Id).First().Position;
        }
        return Anchor;
    }

    private GridPosition? FindNextFieldPosition(WorldMapState world, int radius)
    {
        var occupied = _fields.Values.Select(item => item.Position)
            .Concat(_cohorts.Values.Select(item => item.Position)).Append(Anchor).ToHashSet();
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

    private HumanCohortState GetCohort(HumanCohortRole role) => _cohorts.Values.Single(item => item.Role == role);
    private static HumanCohortSnapshot ToSnapshot(HumanCohortState item) =>
        new(item.Id, item.Role, item.Population, item.Position, item.Task, item.SkillLevel, item.Tools);
    private static int PopulationForHitPoints(int hitPoints, int healthPerGuard) =>
        hitPoints == 0 ? 0 : (hitPoints + healthPerGuard - 1) / healthPerGuard;

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

    private sealed class HumanFieldState(int id, GridPosition position, HumanFieldPhase phase, int growthDays)
    {
        public int Id { get; } = id;
        public GridPosition Position { get; } = position;
        public HumanFieldPhase Phase { get; set; } = phase;
        public int GrowthDays { get; set; } = growthDays;
    }
}

internal readonly record struct HumanIntruderSnapshot(EntityId Id, GridPosition Position);

internal readonly record struct HumanVillageUpdateResult(
    bool Alerted,
    IReadOnlyList<WorldChangeEvent> WorldChanges);
