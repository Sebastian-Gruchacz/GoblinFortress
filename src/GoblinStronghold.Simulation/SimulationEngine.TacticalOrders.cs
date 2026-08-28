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
        var guards = _humanVillage.GetGuardSnapshot();
        if (guards.Population <= 0 ||
            Distance(guards.Position, actor.TacticalCenter) > actor.TacticalRadius)
        {
            CompleteTacticalOrderAndReturn(actor);
            return true;
        }

        var attackRange = actor.PersonalStoneAmmo > 0
            ? actor.Equipment.HasFlag(PersonalEquipment.PrimitiveSling)
                ? Definitions.RangedCombat.SlingRange
                : Definitions.RangedCombat.ThrownStoneRange
            : 1;
        if (Distance(actor.Position, guards.Position) <= attackRange)
        {
            return true;
        }

        var destination = World.GetCardinalWorldNeighbors(guards.Position)
            .Where(World.IsTerrainTraversable)
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
        if (destination is null)
        {
            CompleteTacticalOrderAndReturn(actor);
            return true;
        }

        return BeginTacticalMove(actor, destination.Position, destination.Route!);
    }

    private bool TryPlanHuntAreaOrder(ActorState actor)
    {
        var best = _animals.Values
            .Where(animal =>
                Distance(animal.Position, actor.TacticalCenter) <= actor.TacticalRadius)
            .SelectMany(animal => GetHuntApproachPositions(actor, animal)
                .Select(position => new
                {
                    Animal = animal,
                    Position = position,
                    Route = FindActorPath(actor, position),
                }))
            .Where(candidate => candidate.Route is not null)
            .OrderBy(candidate => candidate.Route!.Count)
            .ThenBy(candidate => candidate.Animal.Id)
            .FirstOrDefault();
        if (best is null)
        {
            CompleteTacticalOrderAndReturn(actor);
            return true;
        }

        actor.TacticalTargetEntityId = new EntityId(best.Animal.Id);
        actor.JobKind = ActorJobKind.HuntAnimal;
        actor.SourceStackId = EntityId.None;
        actor.JobTarget = best.Position;
        BeginJobLeg(actor, best.Route!, GetHuntWorkTicks());
        return true;
    }

    private bool TryBeginTacticalMove(ActorState actor, GridPosition destination)
    {
        var route = FindActorPath(actor, destination);
        if (route is null)
        {
            actor.ClearTacticalOrder();
            return false;
        }
        return BeginTacticalMove(actor, destination, route);
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
        var destination = FindTacticalReturnPosition(actor);
        var route = FindActorPath(actor, destination);
        if (route is { Count: > 0 })
        {
            BeginTacticalMove(actor, destination, route);
        }
    }

    private GridPosition FindTacticalReturnPosition(ActorState actor) =>
        World.CreateWorldObjectSnapshot()
            .Where(worldObject =>
                worldObject.Owner == WorldObjectOwner.GoblinTribe &&
                worldObject.Kind is WorldObjectKind.GoblinHut or
                    WorldObjectKind.GoblinFieldCamp)
            .SelectMany(worldObject => worldObject.GetAbsoluteParts())
            .Where(part => part.Part.Kind == WorldObjectPartKind.Floor &&
                World.IsTerrainTraversable(part.Position))
            .Select(part => new
            {
                part.Position,
                Route = FindActorPath(actor, part.Position),
            })
            .Where(candidate => candidate.Route is not null)
            .OrderBy(candidate => candidate.Route!.Count)
            .Select(candidate => candidate.Position)
            .FirstOrDefault(Map.GoblinSpawn);
}
