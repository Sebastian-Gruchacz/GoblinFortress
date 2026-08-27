using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation;

public sealed partial class SimulationEngine
{
    private void UpdateHumanVillage()
    {
        var intruders = _actors.Values
            .Select(actor => new HumanIntruderSnapshot(actor.Id, actor.Position))
            .ToArray();
        var calendar = SimulationCalendar.At(CurrentTick, Definitions.Clock);
        var detectionRadius = calendar.IsNight ? 3 : Definitions.HumanDetectionRadius;
        var result = _humanVillage.Update(
            CurrentTick,
            WorldSeed,
            World,
            Navigation,
            Definitions,
            intruders,
            detectionRadius);
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
            .Where(actor => !IsJuvenile(actor) && Distance(actor.Position, guards.Position) <= 1)
            .OrderBy(actor => actor.Id)
            .ToArray();
        var rangedGoblins = _actors.Values
            .Where(actor =>
            {
                if (IsJuvenile(actor))
                {
                    return false;
                }
                var distance = Distance(actor.Position, guards.Position);
                var range = actor.Equipment.HasFlag(PersonalEquipment.PrimitiveSling)
                    ? Definitions.RangedCombat.SlingRange
                    : Definitions.RangedCombat.ThrownStoneRange;
                return distance > 1 && distance <= range && actor.PersonalStoneAmmo > 0;
            })
            .OrderBy(actor => actor.Id)
            .ToArray();
        if (adjacentGoblins.Length == 0 && rangedGoblins.Length == 0)
        {
            return;
        }

        if (adjacentGoblins.Length > 0)
        {
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
            ApplyTraumaDamage(guardTarget, guardDamage);
            Publish(
                SimulationEventKind.HumanGuardHitGoblin,
                EntityId.None,
                guardTarget.Id,
                guardDamage);
        }

        foreach (var goblin in adjacentGoblins)
        {
            var goblinDamage = Definitions.GoblinMinimumDamage +
                GetMeleeEquipmentDamageBonus(goblin.Equipment) +
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
            AddBlood(guards.Position, goblinDamage, Math.Max(1, humanDeaths));
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

        foreach (var goblin in rangedGoblins)
        {
            if (_humanVillage.GetGuardSnapshot().Population == 0)
            {
                break;
            }

            goblin.PersonalStoneAmmo--;
            var hasSling = goblin.Equipment.HasFlag(PersonalEquipment.PrimitiveSling);
            var goblinDamage = (hasSling
                    ? Definitions.RangedCombat.SlingDamage
                    : Definitions.RangedCombat.ThrownStoneDamage) +
                DeterministicRandom.NextInt(
                    WorldSeed,
                    RandomDomain.Combat,
                    goblin.Id,
                    CurrentTick,
                    sampleKey: 3,
                    minimumInclusive: 0,
                    maximumExclusive: Definitions.RangedCombat.DamageVariance + 1);
            var humanDeaths = _humanVillage.ApplyGuardDamage(
                goblinDamage,
                Definitions.HumanGuardHealth);
            AddBlood(guards.Position, goblinDamage, Math.Max(1, humanDeaths));
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
        }
    }

    private int GetMeleeEquipmentDamageBonus(PersonalEquipment equipment)
    {
        if (equipment.HasFlag(PersonalEquipment.StoneClub))
        {
            return Definitions.PrimitiveEquipment.StoneClubDamageBonus;
        }
        if (equipment.HasFlag(PersonalEquipment.FightingStick))
        {
            return Definitions.PrimitiveEquipment.FightingStickDamageBonus;
        }
        return equipment.HasFlag(PersonalEquipment.BoneKnife)
            ? Definitions.PrimitiveEquipment.BoneKnifeDamageBonus
            : 0;
    }

    private void TryCompleteRaid()
    {
        if (_raidPhase != GoblinRaidPhase.Marching)
        {
            return;
        }

        _raidPartyIds.RemoveWhere(id =>
            !_actors.TryGetValue(id, out var actor) || actor.Health <= 0);
        var victory = _humanVillage.GetGuardSnapshot().Population == 0;
        var defeated = _raidPartyIds.Count == 0;
        if (!victory && !defeated)
        {
            return;
        }

        _humanVillage.EndGoblinAttack();
        _raidPhase = GoblinRaidPhase.None;
        _raidRallyPoint = default;
        Publish(
            victory ? SimulationEventKind.RaidVictory : SimulationEventKind.RaidDefeated,
            EntityId.None,
            EntityId.None,
            _raidPartyIds.Count);
    }

    private static int Distance(GridPosition left, GridPosition right) =>
        Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y) + Math.Abs(left.Z - right.Z);
}
