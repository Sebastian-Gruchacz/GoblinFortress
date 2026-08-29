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
        foreach (var death in result.Deaths)
        {
            Publish(
                SimulationEventKind.HumanDied,
                EntityId.None,
                HumanVillagerEntityId(death.VillagerId),
                1);
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

        var guards = _humanVillage.GetLivingGuardSnapshots()
            .Where(guard => guard.Task == HumanCohortTask.Guard)
            .ToArray();
        if (guards.Length == 0)
        {
            return;
        }

        foreach (var guard in guards)
        {
            var guardTarget = _actors.Values
                .Where(actor => !IsJuvenile(actor) && actor.Health > 0 &&
                    Distance(actor.Position, guard.Position) <= 1)
                .OrderBy(actor => Distance(actor.Position, guard.Position))
                .ThenBy(actor => actor.Id)
                .FirstOrDefault();
            if (guardTarget is null)
            {
                continue;
            }

            var guardDamage = Definitions.HumanGuardMinimumDamage +
                DeterministicRandom.NextInt(
                    WorldSeed,
                    RandomDomain.Combat,
                    HumanVillagerEntityId(guard.Id),
                    CurrentTick,
                    sampleKey: 1,
                    minimumInclusive: 0,
                    maximumExclusive: Definitions.HumanGuardDamageVariance + 1);
            ApplyTraumaDamage(guardTarget, guardDamage);
            Publish(
                SimulationEventKind.HumanGuardHitGoblin,
                HumanVillagerEntityId(guard.Id),
                guardTarget.Id,
                guardDamage);
        }

        foreach (var goblin in _actors.Values.Where(actor =>
                     !IsJuvenile(actor) && actor.Health > 0).OrderBy(actor => actor.Id))
        {
            var livingGuards = _humanVillage.GetLivingGuardSnapshots()
                .Where(guard => guard.Task == HumanCohortTask.Guard)
                .OrderBy(guard => Distance(goblin.Position, guard.Position))
                .ThenBy(guard => guard.Id)
                .ToArray();
            if (livingGuards.Length == 0)
            {
                break;
            }

            var target = livingGuards[0];
            var distance = Distance(goblin.Position, target.Position);
            var hasSling = goblin.Equipment.HasFlag(PersonalEquipment.PrimitiveSling);
            var range = hasSling
                ? Definitions.RangedCombat.SlingRange
                : Definitions.RangedCombat.ThrownStoneRange;
            var isMelee = distance <= 1;
            var isRanged = !isMelee && goblin.PersonalStoneAmmo > 0 && distance <= range;
            if (!isMelee && !isRanged)
            {
                continue;
            }

            if (isRanged)
            {
                goblin.PersonalStoneAmmo--;
            }
            var baseDamage = isMelee
                ? Definitions.GoblinMinimumDamage + GetMeleeEquipmentDamageBonus(goblin.Equipment)
                : hasSling
                    ? Definitions.RangedCombat.SlingDamage
                    : Definitions.RangedCombat.ThrownStoneDamage;
            var goblinDamage = baseDamage +
                DeterministicRandom.NextInt(
                    WorldSeed,
                    RandomDomain.Combat,
                    goblin.Id,
                    CurrentTick,
                    sampleKey: isMelee ? 2UL : 3UL,
                    minimumInclusive: 0,
                    maximumExclusive: isMelee
                        ? Definitions.GoblinDamageVariance + 1
                        : Definitions.RangedCombat.DamageVariance + 1);
            var result = _humanVillage.ApplyGuardDamage(target.Id, goblinDamage);
            if (result.VillagerId == 0)
            {
                continue;
            }
            AddBlood(result.Position, result.Damage);
            Publish(
                SimulationEventKind.GoblinHitHumanGuard,
                goblin.Id,
                HumanVillagerEntityId(result.VillagerId),
                result.Damage);
            if (result.Died)
            {
                Publish(
                    SimulationEventKind.HumanDied,
                    goblin.Id,
                    HumanVillagerEntityId(result.VillagerId),
                    1);
            }
        }
    }

    private static EntityId HumanVillagerEntityId(int villagerId) =>
        new(0x8000000000000000UL | (uint)villagerId);

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
