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

    private void CreateInitialAnimals()
    {
        if (_animals.Count > 0)
        {
            return;
        }

        CreateInitialAnimals(AnimalKind.MarshHare, Math.Max(6, Map.CellCount / 400));
        CreateInitialAnimals(AnimalKind.SwampBoar, Math.Max(3, Map.CellCount / 1_200));
    }

    private void CreateInitialAnimals(AnimalKind kind, int count)
    {
        var candidates = Enumerable.Range(0, Map.Height)
            .SelectMany(y => Enumerable.Range(0, Map.Width)
                .Select(x => new GridPosition(x, y, 0)))
            .Where(position => IsAnimalHabitat(kind, position) &&
                Distance(position, Map.GoblinSpawn) > 10 &&
                Distance(position, Map.HumanVillage) > 7)
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
            AllocateAnimal(kind, candidates[candidateIndex]);
            candidates.RemoveAt(candidateIndex);
        }
    }

    private void UpdateAnimals()
    {
        if (CurrentTick.Value % AnimalUpdateIntervalTicks != 0)
        {
            return;
        }

        foreach (var animal in _animals.Values.ToArray())
        {
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
        if (calendar.TickOfDay == 0 && calendar.AbsoluteDay > 0 && calendar.AbsoluteDay % 10 == 0)
        {
            TryReproduceAnimal(AnimalKind.MarshHare, Math.Max(10, Map.CellCount / 300));
            TryReproduceAnimal(AnimalKind.SwampBoar, Math.Max(5, Map.CellCount / 900));
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
            CurrentTick.Value % (AnimalUpdateIntervalTicks * 2) != 0)
        {
            animal.Fatigue = Math.Max(0, animal.Fatigue - 1);
            animal.Activity = AnimalActivity.Resting;
            return;
        }

        var nearbyActors = _actors.Values
            .Where(actor => actor.Health > 0 && actor.Position.Z == 0)
            .Select(actor => new { Actor = actor, Distance = Distance(actor.Position, animal.Position) })
            .Where(candidate => candidate.Distance <= 5)
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Actor.Id)
            .ToArray();

        if (animal.Kind == AnimalKind.SwampBoar && nearbyActors.Length == 1)
        {
            var target = nearbyActors[0];
            if (target.Distance <= 1)
            {
                const int damage = 90;
                target.Actor.Health = Math.Max(0, target.Actor.Health - damage);
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

        var neighbors = Map.GetCardinalNeighbors(animal.Position)
            .Where(position => IsAnimalHabitat(animal.Kind, position) &&
                World.IsSurfaceTraversable(position) &&
                Map.CanTraverseSurfaceEdge(animal.Position, position))
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
        var destination = Map.GetCardinalNeighbors(animal.Position)
            .Where(position => IsAnimalHabitat(animal.Kind, position) &&
                World.IsSurfaceTraversable(position) &&
                Map.CanTraverseSurfaceEdge(animal.Position, position))
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
        foreach (var parent in population.OrderBy(animal => animal.Id))
        {
            var position = Map.GetCardinalNeighbors(parent.Position)
                .FirstOrDefault(candidate => IsAnimalHabitat(kind, candidate) &&
                    World.IsSurfaceTraversable(candidate), parent.Position);
            if (_animals.Values.Any(animal => animal.Position == position && animal.Kind == kind))
            {
                continue;
            }
            AllocateAnimal(kind, position);
            Publish(SimulationEventKind.AnimalBorn, EntityId.None, EntityId.None, (int)kind);
            return;
        }
    }

    private bool IsAnimalHabitat(AnimalKind kind, GridPosition position)
    {
        if (position.Z != 0 || !Map.IsWithin(position))
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

    private AnimalState AllocateAnimal(AnimalKind kind, GridPosition position)
    {
        var id = _nextAnimalId++;
        var animal = new AnimalState(id, kind, position, MaximumAnimalHealth(kind));
        _animals.Add(id, animal);
        return animal;
    }

    private void LoadAnimals(IEnumerable<AnimalSaveModel> models)
    {
        foreach (var model in models.OrderBy(model => model.Id))
        {
            var position = new GridPosition(model.X, model.Y, model.Z);
            if (model.Id == 0 || !Enum.IsDefined(model.Kind) || !Enum.IsDefined(model.Activity) ||
                !IsAnimalHabitat(model.Kind, position) || model.Health <= 0 ||
                model.Health > MaximumAnimalHealth(model.Kind) || model.Hunger < 0 ||
                model.Fatigue < 0 || model.Fatigue > MaximumAnimalFatigue(model.Kind) ||
                model.AgeTicks < 0 ||
                !_animals.TryAdd(model.Id, new AnimalState(
                    model.Id, model.Kind, position, model.Health)
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
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal static int MaximumAnimalFatigue(AnimalKind kind) => kind switch
    {
        AnimalKind.MarshHare => HareMaximumFatigue,
        AnimalKind.SwampBoar => BoarMaximumFatigue,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static int AnimalMovementFatigue(AnimalKind kind) => kind switch
    {
        AnimalKind.MarshHare => HareMovementFatigue,
        AnimalKind.SwampBoar => BoarMovementFatigue,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static int AnimalRestRecovery(AnimalKind kind) => kind switch
    {
        AnimalKind.MarshHare => HareRestRecovery,
        AnimalKind.SwampBoar => BoarRestRecovery,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static void AddMovementFatigue(AnimalState animal) =>
        animal.Fatigue = Math.Min(
            MaximumAnimalFatigue(animal.Kind),
            checked(animal.Fatigue + AnimalMovementFatigue(animal.Kind)));

    private sealed class AnimalState(ulong id, AnimalKind kind, GridPosition position, int health)
    {
        public ulong Id { get; } = id;
        public AnimalKind Kind { get; } = kind;
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
                Position,
                Activity,
                Health,
                MaximumAnimalHealth(Kind),
                Hunger,
                Fatigue,
                MaximumAnimalFatigue(Kind),
                AgeTicks);

        public AnimalSaveModel ToSaveModel() => new()
        {
            Id = Id,
            Kind = Kind,
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
