namespace GoblinStronghold.Simulation.Tests;

internal static class SimulationTestSteps
{
    public static void AdvanceUntilConstructionCompletes(
        SimulationEngine engine,
        int maximumTicks = 4_000)
    {
        for (var tick = 0; tick < maximumTicks; tick++)
        {
            if (engine.CreateSnapshot().ConstructionSites.Count == 0)
            {
                return;
            }

            engine.AdvanceTicks(1);
        }

        var snapshot = engine.CreateSnapshot();
        var sites = string.Join(", ", snapshot.ConstructionSites.Select(site =>
            $"{site.Id}/{site.Kind}/work={site.RemainingWorkTicks}/materials=" +
            string.Join("+", site.Materials.Select(material =>
                $"{material.Resource}:{material.DeliveredQuantity}/{material.RequiredQuantity}"))));
        var actors = string.Join(", ", snapshot.Actors.Select(actor =>
            $"{actor.Id}@{actor.Position}/{actor.Job.Kind}/{actor.Job.Phase}/route={actor.Job.RemainingRouteSteps}/" +
            $"hp={actor.Health}/h={actor.Hunger}/t={actor.Thirst}/f={actor.Fatigue}/" +
            $"build={actor.Experience.Building}"));
        throw new InvalidOperationException(
            $"Construction did not complete within {maximumTicks} ticks: {sites}; actors={actors}");
    }
}
