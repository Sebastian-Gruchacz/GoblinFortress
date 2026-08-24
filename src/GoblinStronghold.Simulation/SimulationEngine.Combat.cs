using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation;

public sealed partial class SimulationEngine
{
    private void UpdateHumanVillage()
    {
        var intruders = _actors.Values
            .Select(actor => new HumanIntruderSnapshot(actor.Id, actor.Position))
            .ToArray();
        var result = _humanVillage.Update(CurrentTick, WorldSeed, World, Definitions, intruders);
        foreach (var worldChange in result.WorldChanges)
        {
            _undeliveredWorldChanges.Add(worldChange);
        }
        if (result.Alerted)
        {
            Publish(
                SimulationEventKind.HumanVillageAlerted,
                EntityId.None,
                EntityId.None,
                _humanVillage.CreateSnapshot().Hostility);
        }
    }

    private void ResolveHumanCombat()
    {
        if (CurrentTick.Value % Definitions.CombatIntervalTicks != 0)
        {
            return;
        }

        var guards = _humanVillage.GetGuardSnapshot();
        if (guards.Population == 0 || guards.Task != HumanCohortTask.Guard)
        {
            return;
        }

        var adjacentGoblins = _actors.Values
            .Where(actor => Distance(actor.Position, guards.Position) <= 1)
            .OrderBy(actor => actor.Id)
            .ToArray();
        if (adjacentGoblins.Length == 0)
        {
            return;
        }

        var guardTarget = adjacentGoblins[0];
        var guardDamagePerFighter = Definitions.HumanGuardMinimumDamage +
            DeterministicRandom.NextInt(
                WorldSeed,
                RandomDomain.Combat,
                guardTarget.Id,
                CurrentTick,
                sampleKey: 1,
                minimumInclusive: 0,
                maximumExclusive: Definitions.HumanGuardDamageVariance + 1);
        var guardDamage = checked(guards.Population * guardDamagePerFighter);
        guardTarget.Health = Math.Max(0, guardTarget.Health - guardDamage);
        Publish(
            SimulationEventKind.HumanGuardHitGoblin,
            EntityId.None,
            guardTarget.Id,
            guardDamage);

        foreach (var goblin in adjacentGoblins)
        {
            var goblinDamage = Definitions.GoblinMinimumDamage +
                DeterministicRandom.NextInt(
                    WorldSeed,
                    RandomDomain.Combat,
                    goblin.Id,
                    CurrentTick,
                    sampleKey: 2,
                    minimumInclusive: 0,
                    maximumExclusive: Definitions.GoblinDamageVariance + 1);
            var humanDeaths = _humanVillage.ApplyGuardDamage(
                goblinDamage,
                Definitions.HumanGuardHealth);
            Publish(
                SimulationEventKind.GoblinHitHumanGuard,
                goblin.Id,
                EntityId.None,
                goblinDamage);
            if (humanDeaths > 0)
            {
                Publish(
                    SimulationEventKind.HumanDied,
                    goblin.Id,
                    EntityId.None,
                    humanDeaths);
            }

            if (_humanVillage.GetGuardSnapshot().Population == 0)
            {
                break;
            }
        }
    }

    private static int Distance(GridPosition left, GridPosition right) =>
        Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y) + Math.Abs(left.Z - right.Z);
}
