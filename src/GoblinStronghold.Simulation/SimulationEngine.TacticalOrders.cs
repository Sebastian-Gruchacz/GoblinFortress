using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation;

public sealed partial class SimulationEngine
{
    private bool TryPlanTacticalOrder(ActorState actor) => actor.TacticalOrderKind switch
    {
        ActorTacticalOrderKind.Patrol => TryPlanPatrolOrder(actor),
        ActorTacticalOrderKind.AttackArea => TryPlanAttackAreaOrder(actor),
        ActorTacticalOrderKind.HuntArea => TryPlanHuntAreaOrder(actor),
        _ => false,
    };

    private void CancelTacticalOrdersInArea(
        GridPosition minimum,
        GridPosition maximum)
    {
        foreach (var actor in _actors.Values.Where(actor =>
                     actor.TacticalOrderKind != ActorTacticalOrderKind.None &&
                     TacticalOrderIntersectsArea(actor, minimum, maximum)))
        {
            actor.ClearJob();
            actor.ClearSuspendedJob();
            actor.ClearTacticalOrder();
        }
    }

    private static bool TacticalOrderIntersectsArea(
        ActorState actor,
        GridPosition minimum,
        GridPosition maximum) =>
        IsInside(actor.Position, minimum, maximum) ||
        actor.TacticalOrderKind switch
        {
            ActorTacticalOrderKind.Patrol => actor.PatrolPoints.Any(point =>
                IsInside(point, minimum, maximum)),
            ActorTacticalOrderKind.AttackArea or ActorTacticalOrderKind.HuntArea =>
                IsInside(actor.TacticalCenter, minimum, maximum),
            _ => false,
        };

    private bool TryPlanPatrolOrder(ActorState actor)
    {
        if (actor.PatrolPoints.Count < 2)
        {
            actor.ClearTacticalOrder();
            return false;
        }

        actor.PatrolPointIndex %= actor.PatrolPoints.Count;
        if (actor.Position == actor.PatrolPoints[actor.PatrolPointIndex])
        {
            actor.PatrolPointIndex = (actor.PatrolPointIndex + 1) % actor.PatrolPoints.Count;
        }
        return TryBeginTacticalMove(actor, actor.PatrolPoints[actor.PatrolPointIndex]);
    }

    private bool TryPlanAttackAreaOrder(ActorState actor)
    {
        var guard = _humanVillage.GetLivingGuardSnapshots()
            .Where(candidate =>
                candidate.Task == HumanCohortTask.Guard &&
                Distance(candidate.Position, actor.TacticalCenter) <= actor.TacticalRadius)
            .OrderBy(candidate => candidate.Health)
            .ThenBy(candidate => Distance(candidate.Position, actor.TacticalCenter))
            .ThenBy(candidate => candidate.Id)
            .FirstOrDefault();
        if (guard.Id == 0)
        {
            CompleteTacticalOrderAndReturn(actor);
            return true;
        }

        var attackRange = actor.PersonalStoneAmmo > 0
            ? actor.Equipment.HasFlag(PersonalEquipment.PrimitiveSling)
                ? Definitions.RangedCombat.SlingRange
                : Definitions.RangedCombat.ThrownStoneRange
            : 1;
        if (Distance(actor.Position, guard.Position) <= attackRange)
        {
            return true;
        }

        var destinations = World.GetCardinalWorldNeighbors(guard.Position)
            .Where(World.IsTerrainTraversable)
            .ToHashSet();
        var request = RequestActorPathToNearest(actor, destinations);
        if (request.Status == NavigationPathRequestStatus.Pending)
        {
            return true;
        }
        if (request.Status == NavigationPathRequestStatus.Unreachable ||
            request.Path is not { } route)
        {
            CompleteTacticalOrderAndReturn(actor);
            return true;
        }

        var destination = route.Count == 0 ? actor.Position : route[^1];
        return BeginTacticalMove(actor, destination, route);
    }

    private bool TryPlanHuntAreaOrder(ActorState actor)
    {
        var animals = _animals.Values
            .Where(animal =>
                Distance(animal.Position, actor.TacticalCenter) <= actor.TacticalRadius)
            .OrderByDescending(animal =>
                AnimalCombatPolicy.GetAttackDamage(animal.Kind, animal.Position))
            .ThenByDescending(animal => animal.Health)
            .ThenBy(animal => Distance(animal.Position, actor.TacticalCenter))
            .ThenBy(animal => animal.Id)
            .ToArray();
        foreach (var animal in animals)
        {
            var destinations = GetHuntApproachPositions(actor, animal).ToHashSet();
            var route = FindActorPathToNearest(actor, destinations);
            if (route is null)
            {
                continue;
            }

            var destination = route.Count == 0 ? actor.Position : route[^1];
            actor.TacticalTargetEntityId = new EntityId(animal.Id);
            actor.JobKind = ActorJobKind.HuntAnimal;
            actor.SourceStackId = EntityId.None;
            actor.JobTarget = destination;
            BeginJobLeg(actor, route, GetHuntWorkTicks());
            return true;
        }

        CompleteTacticalOrderAndReturn(actor);
        return true;
    }

    private bool TryBeginTacticalMove(ActorState actor, GridPosition destination)
    {
        var request = RequestActorPath(actor, destination);
        if (request.Status == NavigationPathRequestStatus.Pending)
        {
            return true;
        }
        if (request.Status == NavigationPathRequestStatus.Unreachable)
        {
            actor.ClearTacticalOrder();
            return false;
        }
        return BeginTacticalMove(actor, destination, request.Path!);
    }

    private bool BeginTacticalMove(
        ActorState actor,
        GridPosition destination,
        IReadOnlyList<GridPosition> route)
    {
        if (route.Count == 0)
        {
            return true;
        }
        actor.JobKind = ActorJobKind.Move;
        actor.JobPhase = ActorJobPhase.Traveling;
        actor.JobTarget = destination;
        actor.RemainingRoute.AddRange(route);
        Publish(SimulationEventKind.MoveOrdered, actor.Id, EntityId.None, route.Count);
        return true;
    }

    private void CompleteTacticalOrderAndReturn(ActorState actor)
    {
        actor.ClearTacticalOrder();
        var destinations = World.CreateWorldObjectSnapshot()
            .Where(worldObject =>
                worldObject.Owner == WorldObjectOwner.GoblinTribe &&
                worldObject.Kind is WorldObjectKind.GoblinHut or
                    WorldObjectKind.GoblinFieldCamp)
            .SelectMany(worldObject => worldObject.GetAbsoluteParts())
            .Where(part => part.Part.Kind == WorldObjectPartKind.Floor &&
                World.IsTerrainTraversable(part.Position))
            .Select(part => part.Position)
            .ToHashSet();
        var route = FindActorPathToNearest(actor, destinations) ??
            FindActorPath(actor, Map.GoblinSpawn);
        if (route is { Count: > 0 })
        {
            var destination = route[^1];
            BeginTacticalMove(actor, destination, route);
        }
    }
}
