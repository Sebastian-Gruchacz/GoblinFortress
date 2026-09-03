using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation;

public sealed partial class SimulationEngine
{
    private void MoveActor(ActorState actor, GridPosition destination)
    {
        PickUpBlood(actor);
        actor.CarriedGrime = TrackSurfaceGrime(
            actor.Position,
            destination,
            actor.CarriedGrime);
        ReportAutonomousCleaning(actor.Position);

        actor.Position = destination;
        if (actor.CarriedCorpseId != EntityId.None &&
            _corpses.TryGetValue(actor.CarriedCorpseId, out var carriedCorpse))
        {
            carriedCorpse.Position = destination;
        }

        DepositBlood(actor, destination);
        ReportAutonomousCleaning(destination);
    }

    private void PickUpBlood(ActorState actor)
    {
        if (!_bloodStains.TryGetValue(actor.Position, out var source) ||
            source.Volume < BloodFootprintSourceThreshold)
        {
            return;
        }

        actor.BloodFootprintSteps = Math.Max(
            actor.BloodFootprintSteps,
            BloodFootprintMaximumSteps);
        source.Volume--;
        source.LastChangedAt = CurrentTick;
    }

    private void DepositBlood(ActorState actor, GridPosition destination)
    {
        if (IsWashingSurface(destination))
        {
            actor.BloodFootprintSteps = 0;
            return;
        }

        if (actor.BloodFootprintSteps <= 0)
        {
            return;
        }

        AddBloodVolume(destination, actor.BloodFootprintSteps, allowSpill: false);
        actor.BloodFootprintSteps--;
    }
}
