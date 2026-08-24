using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation;

internal sealed class HumanVillageState
{
    private const int InitialPopulation = 12;
    private const int InitialFoodStock = 48;
    private const int InitialWoodStock = 24;
    private const int InitialGoodsStock = 4;

    private readonly SortedDictionary<int, HumanCohortState> _cohorts;

    private HumanVillageState(
        GridPosition anchor,
        int population,
        int foodStock,
        int woodStock,
        int goodsStock,
        int hostility,
        long lastIntruderSeenTick,
        int guardHitPoints,
        int maximumGuardHitPoints,
        IEnumerable<HumanCohortState> cohorts)
    {
        Anchor = anchor;
        Population = population;
        FoodStock = foodStock;
        WoodStock = woodStock;
        GoodsStock = goodsStock;
        Hostility = hostility;
        LastIntruderSeenTick = lastIntruderSeenTick;
        GuardHitPoints = guardHitPoints;
        MaximumGuardHitPoints = maximumGuardHitPoints;
        _cohorts = new SortedDictionary<int, HumanCohortState>(
            cohorts.ToDictionary(cohort => cohort.Id));
    }

    public GridPosition Anchor { get; }

    public int Population { get; private set; }

    public int FoodStock { get; private set; }

    public int WoodStock { get; private set; }

    public int GoodsStock { get; private set; }

    public int Hostility { get; private set; }

    public long LastIntruderSeenTick { get; private set; }

    public int GuardHitPoints { get; private set; }

    public int MaximumGuardHitPoints { get; }

    public static HumanVillageState CreateInitial(
        WorldMapState world,
        SimulationDefinitions definitions)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(definitions);

        var positions = FindInitialPositions(world, count: 3);
        return new HumanVillageState(
            world.Baseline.HumanVillage,
            InitialPopulation,
            InitialFoodStock,
            InitialWoodStock,
            InitialGoodsStock,
            hostility: 0,
            lastIntruderSeenTick: -1,
            guardHitPoints: checked(2 * definitions.HumanGuardHealth),
            maximumGuardHitPoints: checked(2 * definitions.HumanGuardHealth),
            [
                new HumanCohortState(1, HumanCohortRole.Farmers, 4, positions[0]),
                new HumanCohortState(2, HumanCohortRole.Workers, 6, positions[1]),
                new HumanCohortState(3, HumanCohortRole.Guards, 2, positions[2]),
            ]);
    }

    public static HumanVillageState Restore(
        WorldMapState world,
        HumanVillageSaveModel model,
        SimulationDefinitions definitions,
        SimulationTick currentTick)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(definitions);

        if (model.Population <= 0 ||
            model.FoodStock < 0 ||
            model.WoodStock < 0 ||
            model.GoodsStock < 0 ||
            model.Hostility is < 0 or > 100 ||
            model.LastIntruderSeenTick < -1 ||
            model.LastIntruderSeenTick > currentTick.Value ||
            (model.Hostility == 0) != (model.LastIntruderSeenTick == -1) ||
            model.GuardHitPoints < 0 ||
            model.Cohorts.Count != 3)
        {
            throw new InvalidDataException("The save contains invalid human village state.");
        }

        var cohorts = new List<HumanCohortState>(model.Cohorts.Count);
        foreach (var cohort in model.Cohorts.OrderBy(item => item.Id))
        {
            var position = new GridPosition(cohort.X, cohort.Y, cohort.Z);
            if (cohort.Id <= 0 ||
                !Enum.IsDefined(cohort.Role) ||
                cohort.Population < 0 ||
                !world.IsSurfaceTraversable(position) ||
                Distance(position, world.Baseline.HumanVillage) > definitions.HumanVillageActivityRadius)
            {
                throw new InvalidDataException("The save contains an invalid human cohort.");
            }

            cohorts.Add(new HumanCohortState(
                cohort.Id,
                cohort.Role,
                cohort.Population,
                position));
        }

        if (cohorts.Select(cohort => cohort.Id).Distinct().Count() != cohorts.Count ||
            cohorts.Select(cohort => cohort.Role).Distinct().Count() != cohorts.Count ||
            cohorts.Select(cohort => cohort.Position).Distinct().Count() != cohorts.Count ||
            cohorts.Sum(cohort => cohort.Population) != model.Population)
        {
            throw new InvalidDataException("The human village cohorts do not match its population.");
        }

        var guardPopulation = cohorts.Single(cohort => cohort.Role == HumanCohortRole.Guards).Population;
        var maximumGuardHitPoints = checked(2 * definitions.HumanGuardHealth);
        if (model.GuardHitPoints > maximumGuardHitPoints ||
            guardPopulation != PopulationForHitPoints(model.GuardHitPoints, definitions.HumanGuardHealth))
        {
            throw new InvalidDataException("The human guard health does not match its population.");
        }

        return new HumanVillageState(
            world.Baseline.HumanVillage,
            model.Population,
            model.FoodStock,
            model.WoodStock,
            model.GoodsStock,
            model.Hostility,
            model.LastIntruderSeenTick,
            model.GuardHitPoints,
            maximumGuardHitPoints,
            cohorts);
    }

    public bool Update(
        SimulationTick tick,
        WorldSeed worldSeed,
        WorldMapState world,
        SimulationDefinitions definitions,
        IReadOnlyList<HumanIntruderSnapshot> intruders)
    {
        ArgumentNullException.ThrowIfNull(intruders);

        if (tick.Value % definitions.TicksPerDay == 0)
        {
            AdvanceEconomy();
        }

        var guard = GetCohort(HumanCohortRole.Guards);
        var detectedIntruders = guard.Population == 0
            ? []
            : intruders
                .Where(intruder => Distance(intruder.Position, guard.Position) <= definitions.HumanDetectionRadius)
                .OrderBy(intruder => Distance(intruder.Position, guard.Position))
                .ThenBy(intruder => intruder.Id)
                .ToArray();
        var wasPeaceful = Hostility == 0;
        if (detectedIntruders.Length > 0)
        {
            Hostility = Math.Max(Hostility, 25);
            LastIntruderSeenTick = tick.Value;
        }

        var shouldMoveCivilians = tick.Value % definitions.HumanCohortMovementIntervalTicks == 0;
        var shouldMoveGuards = detectedIntruders.Length > 0
            ? tick.Value % definitions.ActorMovementIntervalTicks == 0
            : shouldMoveCivilians;
        if (shouldMoveCivilians || shouldMoveGuards)
        {
            MoveCohorts(
                tick,
                worldSeed,
                world,
                definitions.HumanVillageActivityRadius,
                shouldMoveCivilians,
                shouldMoveGuards,
                detectedIntruders.FirstOrDefault());
        }

        return wasPeaceful && Hostility > 0;
    }

    public HumanVillageSnapshot CreateSnapshot() => new(
        Anchor,
        Population,
        FoodStock,
        WoodStock,
        GoodsStock,
        Hostility,
        LastIntruderSeenTick,
        GuardHitPoints,
        MaximumGuardHitPoints,
        _cohorts.Values.Select(cohort => new HumanCohortSnapshot(
            cohort.Id,
            cohort.Role,
            cohort.Population,
            cohort.Position)).ToArray());

    public HumanVillageSaveModel CreateSaveModel() => new()
    {
        Population = Population,
        FoodStock = FoodStock,
        WoodStock = WoodStock,
        GoodsStock = GoodsStock,
        Hostility = Hostility,
        LastIntruderSeenTick = LastIntruderSeenTick,
        GuardHitPoints = GuardHitPoints,
        Cohorts = _cohorts.Values.Select(cohort => new HumanCohortSaveModel
        {
            Id = cohort.Id,
            Role = cohort.Role,
            Population = cohort.Population,
            X = cohort.Position.X,
            Y = cohort.Position.Y,
            Z = cohort.Position.Z,
        }).ToList(),
    };

    public HumanCohortSnapshot GetGuardSnapshot()
    {
        var guard = GetCohort(HumanCohortRole.Guards);
        return new HumanCohortSnapshot(guard.Id, guard.Role, guard.Population, guard.Position);
    }

    public int ApplyGuardDamage(int damage, int healthPerGuard)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(damage);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(healthPerGuard);

        var guard = GetCohort(HumanCohortRole.Guards);
        var previousPopulation = guard.Population;
        GuardHitPoints = Math.Max(0, GuardHitPoints - damage);
        guard.Population = PopulationForHitPoints(GuardHitPoints, healthPerGuard);
        var deaths = previousPopulation - guard.Population;
        Population -= deaths;
        Hostility = Math.Min(100, checked(Hostility + 10));
        return deaths;
    }

    private void AdvanceEconomy()
    {
        var farmers = GetPopulation(HumanCohortRole.Farmers);
        var workers = GetPopulation(HumanCohortRole.Workers);
        FoodStock = Math.Max(0, checked(FoodStock + (farmers * 4) - Population));
        WoodStock = checked(WoodStock + workers);
        if (WoodStock >= 2)
        {
            WoodStock -= 2;
            GoodsStock = checked(GoodsStock + 1);
        }
    }

    private void MoveCohorts(
        SimulationTick tick,
        WorldSeed worldSeed,
        WorldMapState world,
        int activityRadius,
        bool moveCivilians,
        bool moveGuards,
        HumanIntruderSnapshot target)
    {
        var occupied = _cohorts.Values.Select(cohort => cohort.Position).ToHashSet();
        foreach (var cohort in _cohorts.Values)
        {
            var isGuard = cohort.Role == HumanCohortRole.Guards;
            if (cohort.Population == 0 || (isGuard ? !moveGuards : !moveCivilians))
            {
                continue;
            }

            occupied.Remove(cohort.Position);
            if (isGuard && target.Id != EntityId.None)
            {
                var route = world.FindSurfacePath(cohort.Position, target.Position);
                if (route is { Count: > 0 } &&
                    route[0] is { } step &&
                    Distance(step, Anchor) <= activityRadius &&
                    !occupied.Contains(step))
                {
                    cohort.Position = step;
                    occupied.Add(cohort.Position);
                    continue;
                }
            }

            var candidates = world.Baseline.GetCardinalNeighbors(cohort.Position)
                .Append(cohort.Position)
                .Where(position =>
                    Distance(position, Anchor) <= activityRadius &&
                    world.IsSurfaceTraversable(position) &&
                    !occupied.Contains(position))
                .OrderBy(position => position.Y)
                .ThenBy(position => position.X)
                .ToArray();
            var selected = DeterministicRandom.NextInt(
                worldSeed,
                RandomDomain.HumanVillage,
                new EntityId((ulong)cohort.Id),
                tick,
                sampleKey: 0,
                minimumInclusive: 0,
                maximumExclusive: candidates.Length);
            cohort.Position = candidates[selected];
            occupied.Add(cohort.Position);
        }
    }

    private int GetPopulation(HumanCohortRole role) =>
        GetCohort(role).Population;

    private HumanCohortState GetCohort(HumanCohortRole role) =>
        _cohorts.Values.Single(cohort => cohort.Role == role);

    private static int PopulationForHitPoints(int hitPoints, int healthPerGuard) =>
        hitPoints == 0 ? 0 : checked((hitPoints + healthPerGuard - 1) / healthPerGuard);

    private static IReadOnlyList<GridPosition> FindInitialPositions(WorldMapState world, int count)
    {
        var positions = new List<GridPosition>(count);
        var visited = new HashSet<GridPosition> { world.Baseline.HumanVillage };
        var queue = new Queue<GridPosition>();
        queue.Enqueue(world.Baseline.HumanVillage);
        while (queue.TryDequeue(out var current))
        {
            if (world.IsSurfaceTraversable(current))
            {
                positions.Add(current);
                if (positions.Count == count)
                {
                    return positions;
                }
            }

            foreach (var neighbor in world.Baseline.GetCardinalNeighbors(current))
            {
                if (world.IsSurfaceTraversable(neighbor) && visited.Add(neighbor))
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        throw new InvalidOperationException("The human village has too few traversable cohort positions.");
    }

    private static int Distance(GridPosition left, GridPosition right) =>
        Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y) + Math.Abs(left.Z - right.Z);

    private sealed class HumanCohortState(
        int id,
        HumanCohortRole role,
        int population,
        GridPosition position)
    {
        public int Id { get; } = id;

        public HumanCohortRole Role { get; } = role;

        public int Population { get; set; } = population;

        public GridPosition Position { get; set; } = position;
    }
}

internal readonly record struct HumanIntruderSnapshot(EntityId Id, GridPosition Position);
