using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation;

public sealed partial class SimulationEngine
{
    public const int AnimalUpdateIntervalTicks = 10;
    private const int HareMaximumHealth = 100;
    private const int HareMaximumFatigue = 6;
    private const int HareMovementFatigue = 6;
    private const int HareRestRecovery = 1;
    private const int BoarMaximumHealth = 500;
    private const int BoarMaximumFatigue = 24;
    private const int BoarMovementFatigue = 1;
    private const int BoarRestRecovery = 4;
    private const int SpiderMaximumHealth = 180;
    private const int SpiderMaximumFatigue = 18;
    private const int SpiderMovementFatigue = 1;
    private const int SpiderRestRecovery = 3;
    private const int DeepCrawlerMaximumHealth = 900;
    private const int MagmaWyrmMaximumHealth = 2_400;
    private const int DeepPredatorMaximumFatigue = 30;

    private void CreateInitialAnimals()
    {
        if (_animals.Count > 0)
        {
            return;
        }

        CreateInitialAnimals(AnimalKind.MarshHare, Math.Max(6, Map.CellCount / 400));
        CreateInitialAnimals(AnimalKind.SwampBoar, Math.Max(3, Map.CellCount / 1_200));
        for (var depth = 1; depth <= Map.CaveLevelCount; depth++)
        {
            CreateInitialAnimals(
                AnimalKind.CaveSpider,
                Math.Max(1, Map.CellCount / 8_000) * depth,
                level: -depth);
        }
    }

    private void CreateInitialAnimals(AnimalKind kind, int count, int level = 0)
    {
        var candidates = Enumerable.Range(0, Map.Height)
            .SelectMany(y => Enumerable.Range(0, Map.Width)
                .Select(x => new GridPosition(x, y, level)))
            .Where(position => IsAnimalHabitat(kind, position) &&
                Map.TryGetInitialGeometry(position, out var geometry) &&
                geometry.Support != CellSupportKind.NaturalRamp &&
                !_actors.Values.Any(actor => actor.Health > 0 && actor.Position == position) &&
                !_animals.Values.Any(animal => animal.Health > 0 && animal.Position == position) &&
                (level < 0 ||
                 Distance(position, Map.GoblinSpawn) > 10 &&
                 Distance(position, Map.HumanVillage) > 7))
            .OrderBy(position => position.Y)
            .ThenBy(position => position.X)
            .ToList();
        for (var index = 0; index < count && candidates.Count > 0; index++)
        {
            var candidateIndex = DeterministicRandom.NextInt(
                WorldSeed,
                RandomDomain.Ecology,
                new EntityId((ulong)kind),
                SimulationTick.Zero,
                (ulong)index,
                0,
                candidates.Count);
            AllocateAnimal(
                kind,
                candidates[candidateIndex],
                ageTicks: GetAnimalMaturityTicks(kind));
            candidates.RemoveAt(candidateIndex);
        }
    }

    private void EnsureDeepPredators()
    {
        for (var depth = 12; depth <= Map.CaveLevelCount; depth++)
        {
            var level = -depth;
            if (depth >= 16 && !_animals.Values.Any(animal =>
                    animal.Kind == AnimalKind.MagmaWyrm && animal.Position.Z == level))
            {
                CreateInitialAnimals(AnimalKind.MagmaWyrm, count: 1, level);
            }

            var crawlerTarget = depth >= 16 ? 2 : 1;
            var crawlers = _animals.Values.Count(animal =>
                animal.Kind == AnimalKind.DeepCrawler && animal.Position.Z == level);
            if (crawlers < crawlerTarget)
            {
                CreateInitialAnimals(
                    AnimalKind.DeepCrawler,
                    crawlerTarget - crawlers,
                    level);
            }
        }
    }

    private void UpdateAnimals()
    {
        EnsureDeepPredators();
        foreach (var animal in _animals.Values.ToArray())
        {
            if (CurrentTick.Value % AnimalUpdateIntervalTicks !=
                (long)(animal.Id % AnimalUpdateIntervalTicks))
            {
                continue;
            }

            animal.AgeTicks = checked(animal.AgeTicks + AnimalUpdateIntervalTicks);
            animal.Hunger++;
            UpdateAnimal(animal);
            SynchronizeHuntDesignation(animal);
            if (animal.Hunger > 24)
            {
                animal.Health--;
            }
            if (animal.Health <= 0)
            {
                _animals.Remove(animal.Id);
                Publish(SimulationEventKind.AnimalDied, EntityId.None, EntityId.None, (int)animal.Kind);
            }
        }

        var calendar = SimulationCalendar.At(CurrentTick, Definitions.Clock);
        if (calendar.TickOfDay == 0 && calendar.AbsoluteDay > 0)
        {
            foreach (var animal in _animals.Values.Where(animal =>
                         animal.AgeTicks >= animal.MaximumAgeTicks))
            {
                animal.Health = Math.Max(0, animal.Health - 1);
            }
        }
        if (CurrentTick.Value % AnimalUpdateIntervalTicks == 0 &&
            calendar.TickOfDay == 0 && calendar.AbsoluteDay > 0)
        {
            foreach (var kind in Enum.GetValues<AnimalKind>())
            {
                var ecology = Definitions.AnimalEcology.Get(kind);
                if (calendar.AbsoluteDay % ecology.BreedingIntervalDays == 0)
                {
                    TryReproduceAnimal(
                        kind,
                        Math.Max(
                            ecology.MinimumPopulationCapacity,
                            Map.CellCount / ecology.MapCellsPerAnimal));
                }
            }
        }
    }

    private void UpdateAnimal(AnimalState animal)
    {
        if (animal.Fatigue >= MaximumAnimalFatigue(animal.Kind) ||
            animal.Activity == AnimalActivity.Resting && animal.Fatigue > 0)
        {
            animal.Fatigue = Math.Max(0, animal.Fatigue - AnimalRestRecovery(animal.Kind));
            animal.Activity = AnimalActivity.Resting;
            return;
        }

        if (animal.Kind == AnimalKind.MarshHare &&
            animal.AgeTicks % (AnimalUpdateIntervalTicks * 2) != 0)
        {
            animal.Fatigue = Math.Max(0, animal.Fatigue - 1);
            animal.Activity = AnimalActivity.Resting;
            return;
        }

        var nearbyActors = _actors.Values
            .Where(actor => actor.Health > 0 && actor.Position.Z == animal.Position.Z)
            .Select(actor => new { Actor = actor, Distance = Distance(actor.Position, animal.Position) })
            .Where(candidate => candidate.Distance <= 5)
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Actor.Id)
            .ToArray();

        if ((animal.Kind == AnimalKind.SwampBoar && nearbyActors.Length == 1) ||
            (animal.Kind is AnimalKind.CaveSpider or AnimalKind.DeepCrawler or
                AnimalKind.MagmaWyrm && nearbyActors.Length > 0))
        {
            var target = nearbyActors[0];
            if (target.Distance <= 1)
            {
                var damage = AnimalCombatPolicy.GetAttackDamage(animal.Kind, animal.Position);
                ApplyTraumaDamage(target.Actor, damage);
                animal.Activity = AnimalActivity.Threatening;
                Publish(SimulationEventKind.AnimalHitGoblin, EntityId.None, target.Actor.Id, damage);
                return;
            }
            MoveAnimal(animal, target.Actor.Position, flee: false);
            animal.Activity = AnimalActivity.Threatening;
            return;
        }

        if (nearbyActors.Length > 0)
        {
            MoveAnimal(animal, nearbyActors[0].Actor.Position, flee: true);
            animal.Activity = AnimalActivity.Fleeing;
            return;
        }

        if (animal.Hunger >= 6 && IsAnimalHabitat(animal.Kind, animal.Position))
        {
            animal.Hunger = Math.Max(0, animal.Hunger - 6);
            animal.Fatigue = Math.Max(0, animal.Fatigue - 1);
            animal.Activity = AnimalActivity.Foraging;
            return;
        }

        var neighbors = GetAnimalTraversableNeighbors(animal.Kind, animal.Position)
            .OrderBy(position => position.Y)
            .ThenBy(position => position.X)
            .ToArray();
        if (neighbors.Length > 0)
        {
            var index = DeterministicRandom.NextInt(
                WorldSeed,
                RandomDomain.Ecology,
                new EntityId(animal.Id),
                CurrentTick,
                sampleKey: 1,
                0,
                neighbors.Length);
            animal.Position = neighbors[index];
            AddMovementFatigue(animal);
        }
        animal.Activity = AnimalActivity.Roaming;
    }

    private void MoveAnimal(AnimalState animal, GridPosition threat, bool flee)
    {
        var destination = GetAnimalTraversableNeighbors(animal.Kind, animal.Position)
            .OrderBy(position => flee ? -Distance(position, threat) : Distance(position, threat))
            .ThenBy(position => position.Y)
            .ThenBy(position => position.X)
            .FirstOrDefault(animal.Position);
        if (destination != animal.Position)
        {
            AddMovementFatigue(animal);
        }
        animal.Position = destination;
    }

    private void TryReproduceAnimal(AnimalKind kind, int capacity)
    {
        var population = _animals.Values.Where(animal => animal.Kind == kind).ToArray();
        if (population.Length < 2 || population.Length >= capacity)
        {
            return;
        }
        var ecology = Definitions.AnimalEcology.Get(kind);
        var adults = population
            .Where(IsAnimalReadyToBreed)
            .OrderBy(animal => animal.Id)
            .ToArray();
        var males = adults.Where(animal => animal.Sex == AnimalSex.Male).ToArray();
        foreach (var mother in adults.Where(animal => animal.Sex == AnimalSex.Female))
        {
            var mate = males.FirstOrDefault(candidate =>
                Distance(candidate.Position, mother.Position) <= ecology.MateSearchRadius);
            if (mate is null)
            {
                continue;
            }
            var position = GetAnimalTraversableNeighbors(kind, mother.Position)
                .FirstOrDefault(candidate =>
                    !_animals.Values.Any(animal =>
                        animal.Position == candidate && animal.Kind == kind),
                    mother.Position);
            if (_animals.Values.Any(animal => animal.Position == position && animal.Kind == kind))
            {
                continue;
            }
            AllocateAnimal(kind, position);
            Publish(SimulationEventKind.AnimalBorn, EntityId.None, EntityId.None, (int)kind);
            return;
        }
    }

    private IEnumerable<GridPosition> GetAnimalTraversableNeighbors(
        AnimalKind kind,
        GridPosition position) => kind is AnimalKind.CaveSpider or AnimalKind.DeepCrawler or
            AnimalKind.MagmaWyrm
        ? World.GetTerrainNeighbors(position).Where(candidate => IsAnimalHabitat(kind, candidate))
        : Map.GetCardinalNeighbors(position).Where(candidate =>
            IsAnimalHabitat(kind, candidate) &&
            World.IsSurfaceTraversable(candidate) &&
            Map.CanTraverseSurfaceEdge(position, candidate));

    private bool IsAnimalReadyToBreed(AnimalState animal) =>
        animal.AgeTicks >= animal.MaturityAgeTicks &&
        animal.AgeTicks < animal.MaximumAgeTicks &&
        animal.Health * 4 >= MaximumAnimalHealth(animal.Kind) * 3 &&
        animal.Hunger <= 12 &&
        animal.Fatigue < MaximumAnimalFatigue(animal.Kind) / 2 &&
        animal.Activity is not (AnimalActivity.Fleeing or AnimalActivity.Threatening) &&
        !_actors.Values.Any(actor =>
            actor.Health > 0 && actor.Position.Z == animal.Position.Z &&
            Distance(actor.Position, animal.Position) <= 6);

    private bool IsAnimalHabitat(AnimalKind kind, GridPosition position)
    {
        if (!Map.IsColumnWithin(position))
        {
            return false;
        }
        if (kind is AnimalKind.CaveSpider or AnimalKind.DeepCrawler or AnimalKind.MagmaWyrm)
        {
            var inhabitsDepth = kind switch
            {
                AnimalKind.DeepCrawler => position.Z <= -12,
                AnimalKind.MagmaWyrm => position.Z <= -16,
                _ => position.Z < 0,
            };
            return inhabitsDepth && Map.IsCavePosition(position) &&
                World.IsTerrainTraversable(position);
        }
        if (position.Z != 0)
        {
            return false;
        }
        var cell = Map.GetCell(position);
        return kind switch
        {
            AnimalKind.MarshHare => cell.IsTraversable && cell.Terrain == TerrainKind.SolidGround &&
                cell.Fertility >= 45,
            AnimalKind.SwampBoar => cell.IsTraversable && cell.Moisture >= 60 &&
                cell.Terrain is TerrainKind.Mud or TerrainKind.ShallowWater,
            _ => false,
        };
    }

    private AnimalState AllocateAnimal(
        AnimalKind kind,
        GridPosition position,
        long ageTicks = 0)
    {
        var id = _nextAnimalId++;
        var sex = id % 2 == 0 ? AnimalSex.Male : AnimalSex.Female;
        var animal = new AnimalState(
            id,
            kind,
            sex,
            position,
            MaximumAnimalHealth(kind),
            GetAnimalMaturityTicks(kind),
            GetAnimalMaximumAgeTicks(kind))
        {
            AgeTicks = ageTicks,
        };
        _animals.Add(id, animal);
        return animal;
    }

    private void LoadAnimals(IEnumerable<AnimalSaveModel> models)
    {
        foreach (var model in models.OrderBy(model => model.Id))
        {
            var position = new GridPosition(model.X, model.Y, model.Z);
            if (model.Id == 0 || !Enum.IsDefined(model.Kind) || !Enum.IsDefined(model.Sex) ||
                !Enum.IsDefined(model.Activity) ||
                !IsAnimalHabitat(model.Kind, position) || model.Health <= 0 ||
                model.Health > MaximumAnimalHealth(model.Kind) || model.Hunger < 0 ||
                model.Fatigue < 0 || model.Fatigue > MaximumAnimalFatigue(model.Kind) ||
                model.AgeTicks < 0 ||
                !_animals.TryAdd(model.Id, new AnimalState(
                    model.Id,
                    model.Kind,
                    model.Sex,
                    position,
                    model.Health,
                    GetAnimalMaturityTicks(model.Kind),
                    GetAnimalMaximumAgeTicks(model.Kind))
                {
                    Activity = model.Activity,
                    Hunger = model.Hunger,
                    Fatigue = model.Fatigue,
                    AgeTicks = model.AgeTicks,
                }))
            {
                throw new InvalidDataException("The save contains an invalid animal.");
            }
        }
        var maximumId = _animals.Count == 0 ? 0 : _animals.Keys.Max();
        if (_nextAnimalId <= maximumId)
        {
            throw new InvalidDataException("The next animal identifier is invalid.");
        }
    }

    private static int MaximumAnimalHealth(AnimalKind kind) => kind switch
    {
        AnimalKind.MarshHare => HareMaximumHealth,
        AnimalKind.SwampBoar => BoarMaximumHealth,
        AnimalKind.CaveSpider => SpiderMaximumHealth,
        AnimalKind.DeepCrawler => DeepCrawlerMaximumHealth,
        AnimalKind.MagmaWyrm => MagmaWyrmMaximumHealth,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal static int MaximumAnimalFatigue(AnimalKind kind) => kind switch
    {
        AnimalKind.MarshHare => HareMaximumFatigue,
        AnimalKind.SwampBoar => BoarMaximumFatigue,
        AnimalKind.CaveSpider => SpiderMaximumFatigue,
        AnimalKind.DeepCrawler or AnimalKind.MagmaWyrm => DeepPredatorMaximumFatigue,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static int AnimalMovementFatigue(AnimalKind kind) => kind switch
    {
        AnimalKind.MarshHare => HareMovementFatigue,
        AnimalKind.SwampBoar => BoarMovementFatigue,
        AnimalKind.CaveSpider => SpiderMovementFatigue,
        AnimalKind.DeepCrawler or AnimalKind.MagmaWyrm => SpiderMovementFatigue,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static int AnimalRestRecovery(AnimalKind kind) => kind switch
    {
        AnimalKind.MarshHare => HareRestRecovery,
        AnimalKind.SwampBoar => BoarRestRecovery,
        AnimalKind.CaveSpider => SpiderRestRecovery,
        AnimalKind.DeepCrawler or AnimalKind.MagmaWyrm => SpiderRestRecovery,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private long GetAnimalMaturityTicks(AnimalKind kind)
    {
        var seasonCount = Definitions.AnimalEcology.Get(kind).MaturitySeasonCount;
        var seasons = Definitions.Clock.Climate.Seasons;
        long ticks = 0;
        for (var index = 0; index < seasonCount; index++)
        {
            ticks = checked(ticks + seasons[index % seasons.Count].TotalTicks);
        }
        return ticks;
    }

    private long GetAnimalMaximumAgeTicks(AnimalKind kind) => checked(
        Definitions.Clock.Climate.TicksPerYear *
        Definitions.AnimalEcology.Get(kind).MaximumAgeYears);

    private static void AddMovementFatigue(AnimalState animal) =>
        animal.Fatigue = Math.Min(
            MaximumAnimalFatigue(animal.Kind),
            checked(animal.Fatigue + AnimalMovementFatigue(animal.Kind)));

    private sealed class AnimalState(
        ulong id,
        AnimalKind kind,
        AnimalSex sex,
        GridPosition position,
        int health,
        long maturityAgeTicks,
        long maximumAgeTicks)
    {
        public ulong Id { get; } = id;
        public AnimalKind Kind { get; } = kind;
        public AnimalSex Sex { get; } = sex;
        public long MaturityAgeTicks { get; } = maturityAgeTicks;
        public long MaximumAgeTicks { get; } = maximumAgeTicks;
        public GridPosition Position { get; set; } = position;
        public AnimalActivity Activity { get; set; }
        public int Health { get; set; } = health;
        public int Hunger { get; set; }
        public int Fatigue { get; set; }
        public long AgeTicks { get; set; }

        public AnimalSnapshot ToSnapshot() =>
            new(
                Id,
                Kind,
                Sex,
                Position,
                Activity,
                Health,
                MaximumAnimalHealth(Kind),
                Hunger,
                Fatigue,
                MaximumAnimalFatigue(Kind),
                AgeTicks,
                MaturityAgeTicks,
                MaximumAgeTicks);

        public AnimalSaveModel ToSaveModel() => new()
        {
            Id = Id,
            Kind = Kind,
            Sex = Sex,
            X = Position.X,
            Y = Position.Y,
            Z = Position.Z,
            Activity = Activity,
            Health = Health,
            Hunger = Hunger,
            Fatigue = Fatigue,
            AgeTicks = AgeTicks,
        };
    }
}
