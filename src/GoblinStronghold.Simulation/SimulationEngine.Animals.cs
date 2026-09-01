using GoblinStronghold.Simulation.Animals;
using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation;

public sealed partial class SimulationEngine
{
    public const int AnimalUpdateIntervalTicks = 10;
    private static IAnimalSpeciesCatalog AnimalSpecies => AnimalSpeciesCatalog.Current;

    private void CreateInitialAnimals()
    {
        if (_animals.Count > 0)
        {
            return;
        }

        foreach (var species in AnimalSpecies.All
                     .Where(species => species.Spawn.Mode is
                         AnimalSpawnMode.InitialSingleLevel or
                         AnimalSpawnMode.InitialEachDepth)
                     .OrderBy(species => species.Spawn.Order))
        {
            foreach (var depth in GetSpawnDepths(species.Spawn))
            {
                CreateInitialAnimals(
                    species.LegacyKind,
                    GetSpawnPopulation(species.Spawn, depth),
                    level: -depth);
            }
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
        var maintained = AnimalSpecies.All
            .Where(species => species.Spawn.Mode == AnimalSpawnMode.MaintainEachDepth)
            .OrderBy(species => species.Spawn.Order)
            .ToArray();
        var minimumDepth = maintained.Min(species => species.Spawn.MinimumDepth);
        for (var depth = minimumDepth; depth <= Map.CaveLevelCount; depth++)
        {
            var level = -depth;
            foreach (var species in maintained.Where(species =>
                         depth >= species.Spawn.MinimumDepth &&
                         depth <= (species.Spawn.MaximumDepth ?? int.MaxValue)))
            {
                var target = GetSpawnPopulation(species.Spawn, depth);
                var current = _animals.Values.Count(animal =>
                    animal.Kind == species.LegacyKind && animal.Position.Z == level);
                CreateInitialAnimals(
                    species.LegacyKind,
                    Math.Max(0, target - current),
                    level);
            }
        }
    }

    private IEnumerable<int> GetSpawnDepths(AnimalSpawnDefinition spawn)
    {
        var maximumDepth = spawn.Mode == AnimalSpawnMode.InitialSingleLevel
            ? spawn.MinimumDepth
            : Math.Min(spawn.MaximumDepth ?? Map.CaveLevelCount, Map.CaveLevelCount);
        for (var depth = spawn.MinimumDepth; depth <= maximumDepth; depth++)
        {
            yield return depth;
        }
    }

    private int GetSpawnPopulation(AnimalSpawnDefinition spawn, int depth)
    {
        var population = spawn.MapCellsPerAnimal == 0
            ? spawn.MinimumPopulation
            : Math.Max(spawn.MinimumPopulation, Map.CellCount / spawn.MapCellsPerAnimal);
        if (spawn.ScalePopulationWithDepth)
        {
            population = checked(population * Math.Max(1, depth));
        }
        if (depth >= (spawn.PopulationIncreaseDepth ?? int.MaxValue))
        {
            population = checked(population + spawn.PopulationIncrease);
        }
        return population;
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
            if (animal.Hunger > AnimalSpecies.Get(animal.Kind)
                    .Behavior.StarvationHungerThreshold)
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

        var species = AnimalSpecies.Get(animal.Kind);
        if (species.Behavior.RoamingInterval > 1 &&
            animal.AgeTicks %
                (AnimalUpdateIntervalTicks * species.Behavior.RoamingInterval) != 0)
        {
            animal.Fatigue = Math.Max(0, animal.Fatigue - 1);
            animal.Activity = AnimalActivity.Resting;
            return;
        }

        var considersGoblinsEnemies =
            AnimalDispositionPolicy.ConsidersGoblinsEnemies(species.Behavior);
        var nearbyActors = _actors.Values
            .Where(actor => considersGoblinsEnemies && actor.Health > 0 &&
                actor.Position.Z == animal.Position.Z)
            .Select(actor => new { Actor = actor, Distance = Distance(actor.Position, animal.Position) })
            .Where(candidate => candidate.Distance <= species.Behavior.DetectionRadius)
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Actor.Id)
            .ToArray();

        if (AnimalDispositionPolicy.ShouldAttack(species.Behavior, nearbyActors.Length))
        {
            var target = nearbyActors[0];
            if (target.Distance <= 1)
            {
                var damage = AnimalAttackPolicy.GetDamage(species, animal.Position);
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

        if (animal.Hunger >= species.Behavior.ForageHungerThreshold &&
            IsAnimalHabitat(animal.Kind, animal.Position))
        {
            animal.Hunger = Math.Max(
                0,
                animal.Hunger - species.Behavior.ForageHungerThreshold);
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
        GridPosition position) => AnimalHabitatPolicy.GetTraversableNeighbors(
            AnimalSpecies.Get(kind),
            Map,
            World,
            position);

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

    private bool IsAnimalHabitat(AnimalKind kind, GridPosition position) =>
        AnimalHabitatPolicy.Accepts(AnimalSpecies.Get(kind), Map, World, position);

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

    private static int MaximumAnimalHealth(AnimalKind kind) =>
        AnimalSpecies.Get(kind).Vitals.MaximumHealth;

    internal static int MaximumAnimalFatigue(AnimalKind kind) =>
        AnimalSpecies.Get(kind).Vitals.MaximumFatigue;

    private static int AnimalMovementFatigue(AnimalKind kind) =>
        AnimalSpecies.Get(kind).Vitals.MovementFatigue;

    private static int AnimalRestRecovery(AnimalKind kind) =>
        AnimalSpecies.Get(kind).Vitals.RestRecovery;

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
