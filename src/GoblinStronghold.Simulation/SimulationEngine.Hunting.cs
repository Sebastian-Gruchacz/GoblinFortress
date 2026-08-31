using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation;

public sealed partial class SimulationEngine
{
    private bool TryPlanHuntAnimalJob(ActorState actor, ISet<EntityId> reservedDesignations)
    {
        var best = _workDesignations.Values
            .Where(designation => designation.Kind == WorkDesignationKind.HuntAnimal &&
                !designation.IsSuspended && !reservedDesignations.Contains(designation.Id) &&
                _animals.ContainsKey(designation.TargetEntityId.Value))
            .SelectMany(designation => GetHuntApproachPositions(
                    actor,
                    _animals[designation.TargetEntityId.Value])
                .Select(position => new
                {
                    Designation = designation,
                    Position = position,
                    Route = FindActorPath(actor, position),
                }))
            .Where(candidate => candidate.Route is not null)
            .OrderBy(candidate => candidate.Route!.Count)
            .ThenBy(candidate => candidate.Designation.Id)
            .FirstOrDefault();
        if (best is null)
        {
            return false;
        }

        actor.JobKind = ActorJobKind.HuntAnimal;
        actor.SourceStackId = best.Designation.Id;
        actor.JobTarget = best.Position;
        BeginJobLeg(actor, best.Route!, GetHuntWorkTicks());
        reservedDesignations.Add(best.Designation.Id);
        return true;
    }

    private void UpdateHuntAnimalJob(ActorState actor)
    {
        var isTacticalHunt = actor.SourceStackId == EntityId.None &&
            actor.TacticalOrderKind == ActorTacticalOrderKind.HuntArea;
        var hasDesignation = _workDesignations.TryGetValue(
            actor.SourceStackId, out var designation);
        var animalId = isTacticalHunt
            ? actor.TacticalTargetEntityId
            : hasDesignation
                ? designation.TargetEntityId
                : EntityId.None;
        if ((!isTacticalHunt &&
             (!hasDesignation || designation.Kind != WorkDesignationKind.HuntAnimal ||
              designation.IsSuspended)) ||
            !_animals.TryGetValue(animalId.Value, out var animal))
        {
            actor.ClearJob();
            return;
        }

        var attackRange = GetHuntAttackRange(actor);
        if (Distance(actor.Position, animal.Position) > attackRange)
        {
            var route = GetHuntApproachPositions(actor, animal)
                .Select(position => new
                {
                    Position = position,
                    Route = FindActorPath(actor, position),
                })
                .Where(candidate => candidate.Route is not null)
                .OrderBy(candidate => candidate.Route!.Count)
                .ThenBy(candidate => candidate.Position.Y)
                .ThenBy(candidate => candidate.Position.X)
                .FirstOrDefault();
            if (route is null)
            {
                actor.ClearJob();
                return;
            }
            if (actor.JobTarget != route.Position || actor.RemainingRoute.Count == 0)
            {
                actor.JobTarget = route.Position;
                BeginJobLeg(actor, route.Route!, GetHuntWorkTicks());
            }
            if (actor.JobPhase == ActorJobPhase.Traveling)
            {
                AdvanceTravel(actor);
            }
            return;
        }

        actor.JobPhase = ActorJobPhase.Working;
        actor.JobTarget = actor.Position;
        actor.RemainingRoute.Clear();
        actor.RemainingWorkTicks--;
        if (actor.RemainingWorkTicks > 0)
        {
            return;
        }

        var usesStone = Distance(actor.Position, animal.Position) > 1 &&
            actor.PersonalStoneAmmo > 0;
        var damage = usesStone
            ? (actor.Equipment.HasFlag(PersonalEquipment.PrimitiveSling)
                    ? Definitions.RangedCombat.SlingDamage
                    : Definitions.RangedCombat.ThrownStoneDamage) +
                DeterministicRandom.NextInt(
                    WorldSeed,
                    RandomDomain.Combat,
                    actor.Id,
                    CurrentTick,
                    sampleKey: 0x48554E54UL,
                    minimumInclusive: 0,
                    maximumExclusive: Definitions.RangedCombat.DamageVariance + 1)
            : animal.Kind switch
            {
                AnimalKind.MarshHare => 120,
                AnimalKind.CaveSpider => 80,
                AnimalKind.DeepCrawler => 55,
                AnimalKind.MagmaWyrm => 35,
                _ => 110,
            };
        if (usesStone)
        {
            actor.PersonalStoneAmmo--;
        }
        animal.Health -= damage;
        AddBlood(animal.Position, damage);
        if (animal.Health > 0)
        {
            actor.RemainingWorkTicks = GetHuntWorkTicks();
            return;
        }

        _animals.Remove(animal.Id);
        var harvest = animal.Kind switch
        {
            AnimalKind.MarshHare => (Meat: 3, Hide: 1, Bone: 1, Experience: 12),
            AnimalKind.SwampBoar => (Meat: 10, Hide: 3, Bone: 4, Experience: 30),
            AnimalKind.CaveSpider => (Meat: 0, Hide: 0, Bone: 0, Experience: 25),
            AnimalKind.DeepCrawler => (Meat: 0, Hide: 3, Bone: 4, Experience: 120),
            AnimalKind.MagmaWyrm => (Meat: 0, Hide: 8, Bone: 10, Experience: 300),
            _ => throw new ArgumentOutOfRangeException(),
        };
        if (harvest.Meat > 0)
        {
            var existing = FindMergeableGroundStack(
                ResourceKind.Food,
                animal.Position,
                FoodKind.RawMeat);
            if (existing is null)
            {
                AllocateItemStack(
                    ResourceKind.Food,
                    harvest.Meat,
                    ItemLocation.OnGround(animal.Position),
                    FoodKind.RawMeat);
            }
            else
            {
                existing.Quantity = checked(existing.Quantity + harvest.Meat);
            }
        }
        if (harvest.Hide > 0)
        {
            DropAnimalMaterial(animal.Position, ResourceKind.Hide, harvest.Hide);
        }
        if (harvest.Bone > 0)
        {
            DropAnimalMaterial(animal.Position, ResourceKind.Bone, harvest.Bone);
        }
        if (animal.Kind == AnimalKind.CaveSpider)
        {
            DropAnimalMaterial(
                animal.Position, ResourceKind.Materials, 1, ResourceVariant.SpiderVenom);
            DropAnimalMaterial(
                animal.Position, ResourceKind.Materials, 2, ResourceVariant.SpiderSilk);
            DropAnimalMaterial(
                animal.Position, ResourceKind.Materials, 3, ResourceVariant.SpiderChitin);
        }
        if (hasDesignation)
        {
            _workDesignations.Remove(designation.Id);
        }
        actor.TacticalTargetEntityId = EntityId.None;
        GainForagingExperience(actor, harvest.Experience);
        Publish(SimulationEventKind.AnimalHunted, actor.Id, EntityId.None, harvest.Meat);
        if (hasDesignation)
        {
            Publish(SimulationEventKind.WorkDesignationRemoved, EntityId.None, designation.Id, 0);
        }
        actor.ClearJob();
    }

    private int GetHuntAttackRange(ActorState actor)
    {
        if (actor.PersonalStoneAmmo <= 0)
        {
            return 1;
        }

        return actor.Equipment.HasFlag(PersonalEquipment.PrimitiveSling)
            ? Definitions.RangedCombat.SlingRange
            : Definitions.RangedCombat.ThrownStoneRange;
    }

    private IEnumerable<GridPosition> GetHuntApproachPositions(
        ActorState actor,
        AnimalState animal)
    {
        var range = GetHuntAttackRange(actor);
        for (var y = Math.Max(0, animal.Position.Y - range);
             y <= Math.Min(Map.Height - 1, animal.Position.Y + range);
             y++)
        {
            for (var x = Math.Max(0, animal.Position.X - range);
                 x <= Math.Min(Map.Width - 1, animal.Position.X + range);
                 x++)
            {
                var position = new GridPosition(x, y, animal.Position.Z);
                var distance = Distance(position, animal.Position);
                if (distance is > 0 && distance <= range && World.IsTerrainTraversable(position))
                {
                    yield return position;
                }
            }
        }
    }

    private void DropAnimalMaterial(
        GridPosition position,
        ResourceKind resource,
        int quantity,
        ResourceVariant variant = ResourceVariant.None)
    {
        var existing = FindMergeableGroundStack(resource, position, variant: variant);
        if (existing is null)
        {
            AllocateItemStack(
                resource,
                quantity,
                ItemLocation.OnGround(position),
                variant: variant);
        }
        else
        {
            existing.Quantity = checked(existing.Quantity + quantity);
        }
    }

    private void ValidateLoadedHuntAnimalJob(ActorState actor)
    {
        var validTacticalHunt = actor.SourceStackId == EntityId.None &&
            actor.TacticalOrderKind == ActorTacticalOrderKind.HuntArea &&
            _animals.ContainsKey(actor.TacticalTargetEntityId.Value);
        var validPublicHunt = _workDesignations.TryGetValue(
                actor.SourceStackId, out var designation) &&
            designation.Kind == WorkDesignationKind.HuntAnimal &&
            _animals.ContainsKey(designation.TargetEntityId.Value);
        if (actor.JobStage != ActorJobStage.None || actor.CarriedStackId != EntityId.None ||
            actor.DestinationZoneId != EntityId.None || actor.ReservedQuantity != 0 ||
            (!validTacticalHunt && !validPublicHunt))
        {
            throw new InvalidDataException("The save contains an invalid animal-hunting job.");
        }
    }

    private void SynchronizeHuntDesignation(AnimalState animal)
    {
        foreach (var designation in _workDesignations.Values
                     .Where(designation => designation.Kind == WorkDesignationKind.HuntAnimal &&
                         designation.TargetEntityId.Value == animal.Id)
                     .ToArray())
        {
            _workDesignations[designation.Id] = designation with { Target = animal.Position };
        }
    }
}
