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
        var detectionRadius = calendar.IsNight
            ? HumanPerception.NightVisionRadius
            : HumanPerception.DayVisionRadius;
        var result = _humanVillage.Update(
            CurrentTick,
            WorldSeed,
            World,
            Navigation,
            Definitions,
            intruders,
            detectionRadius,
            TrackSurfaceGrime);
        foreach (var worldChange in result.WorldChanges)
        {
            _undeliveredWorldChanges.Add(worldChange);
        }
        foreach (var death in result.Deaths)
        {
            if (_humanVillage.GetVillagerSnapshot(death.VillagerId) is { } villager)
            {
                CreateHumanCorpse(villager);
            }
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

            var guardDamage = HumanCombat.MinimumMeleeDamage +
                DeterministicRandom.NextInt(
                    WorldSeed,
                    RandomDomain.Combat,
                    HumanVillagerEntityId(guard.Id),
                    CurrentTick,
                    sampleKey: 1,
                    minimumInclusive: 0,
                    maximumExclusive: HumanCombat.MeleeDamageVariance + 1);
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
            var isActiveRaider = _raidPhase == GoblinRaidPhase.Marching &&
                _raidPartyIds.Contains(goblin.Id);
            var isAreaAttack = goblin.TacticalOrderKind ==
                ActorTacticalOrderKind.AttackArea;
            var humanTargets = (isActiveRaider
                    ? GetOrderedRaidCombatTargets()
                    : _humanVillage.GetLivingVillagerSnapshots()
                        .Where(villager => villager.Role == HumanCohortRole.Guards &&
                            villager.Task == HumanCohortTask.Guard &&
                            (!isAreaAttack || Distance(
                                villager.Position,
                                goblin.TacticalCenter) <= goblin.TacticalRadius))
                        .OrderBy(villager => isAreaAttack
                            ? villager.Health
                            : Distance(goblin.Position, villager.Position))
                        .ThenBy(villager => isAreaAttack
                            ? Distance(villager.Position, goblin.TacticalCenter)
                            : 0)
                        .ThenBy(villager => villager.Id))
                .ThenBy(villager => villager.Id)
                .ToArray();
            if (humanTargets.Length == 0)
            {
                continue;
            }

            var target = humanTargets[0];
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
                ? GoblinCombat.MinimumMeleeDamage + GetMeleeEquipmentDamageBonus(goblin.Equipment)
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
                        ? GoblinCombat.MeleeDamageVariance + 1
                        : Definitions.RangedCombat.DamageVariance + 1);
            var result = _humanVillage.ApplyVillagerDamage(target.Id, goblinDamage);
            if (result.VillagerId == 0)
            {
                continue;
            }
            AddBlood(result.Position, result.Damage);
            Publish(
                target.Role == HumanCohortRole.Guards
                    ? SimulationEventKind.GoblinHitHumanGuard
                    : SimulationEventKind.GoblinHitHumanCivilian,
                goblin.Id,
                HumanVillagerEntityId(result.VillagerId),
                result.Damage);
            if (result.Died)
            {
                if (_humanVillage.GetVillagerSnapshot(result.VillagerId) is { } villager)
                {
                    CreateHumanCorpse(villager);
                }
                Publish(
                    SimulationEventKind.HumanDied,
                    goblin.Id,
                    HumanVillagerEntityId(result.VillagerId),
                    1);
            }
        }
    }

    private HumanVillagerSnapshot? GetRaidCombatTarget() =>
        GetOrderedRaidCombatTargets()
            .Select(villager => (HumanVillagerSnapshot?)villager)
            .FirstOrDefault();

    private IOrderedEnumerable<HumanVillagerSnapshot> GetOrderedRaidCombatTargets() =>
        _humanVillage.GetLivingVillagerSnapshots()
            .Where(IsRaidCombatTarget)
            .OrderBy(villager => villager.Role == HumanCohortRole.Guards ? 0 : 1)
            .ThenBy(villager => villager.Health)
            .ThenBy(villager => Distance(villager.Position, _raidTarget))
            .ThenBy(villager => villager.Id);

    private bool HasRemainingRaidCombatTargets() =>
        _humanVillage.GetLivingVillagerSnapshots().Any(IsRaidCombatTarget);

    private bool IsRaidCombatTarget(HumanVillagerSnapshot villager)
    {
        if (Distance(villager.Position, _raidTarget) > _raidTargetRadius)
        {
            return false;
        }

        var attacksAll = _raidDirectives.HasFlag(RaidDirective.AttackAll);
        if (villager.Role == HumanCohortRole.Guards)
        {
            return attacksAll || _raidDirectives.HasFlag(RaidDirective.AttackGuards);
        }

        return attacksAll &&
            (villager.Task != HumanCohortTask.Flee ||
             _raidDirectives.HasFlag(RaidDirective.ContinueWhileTargetsVisible));
    }

    private static EntityId HumanVillagerEntityId(int villagerId) =>
        new(0x8000000000000000UL | (uint)villagerId);

    private int GetMeleeEquipmentDamageBonus(PersonalEquipment equipment) =>
        Equipment.EquipmentCombatPolicy.GetBestMeleeDamageBonus(
            equipment,
            Definitions.PrimitiveEquipment);

    private void TryCompleteRaid()
    {
        if (_raidPhase is not (GoblinRaidPhase.Marching or GoblinRaidPhase.Looting or
            GoblinRaidPhase.Returning))
        {
            return;
        }

        _raidPartyIds.RemoveWhere(id =>
            !_actors.TryGetValue(id, out var actor) || actor.Health <= 0);
        var victory = !HasRemainingRaidCombatTargets();
        var defeated = _raidPartyIds.Count == 0;
        if (_raidPhase == GoblinRaidPhase.Looting && !victory && !defeated)
        {
            _raidPhase = GoblinRaidPhase.Marching;
            _humanVillage.OrderGoblinAttack();
            foreach (var actor in GetRaidParty())
            {
                actor.ClearJob();
            }
            return;
        }
        if (_raidPhase == GoblinRaidPhase.Marching && victory)
        {
            _humanVillage.EndGoblinAttack();
            _raidPhase = HasRemainingRaidCorpseLoot() || HasRemainingRaidBuildingLoot() ||
                HasRemainingRaidCorpseConsumption() || HasRemainingRaidCorpseRecovery()
                    ? GoblinRaidPhase.Looting
                    : GoblinRaidPhase.Returning;
            foreach (var actor in GetRaidParty())
            {
                actor.ClearJob();
            }
            return;
        }
        if (_raidPhase == GoblinRaidPhase.Looting &&
            (HasRemainingRaidCorpseLoot() || HasRemainingRaidBuildingLoot() ||
             HasRemainingRaidCorpseConsumption() ||
             HasRemainingRaidCorpseRecovery() ||
             GetRaidParty().Any(actor =>
                actor.CarriedStackId != EntityId.None ||
                actor.CarriedCorpseId != EntityId.None ||
                actor.JobKind is ActorJobKind.LootRaid or ActorJobKind.RecoverRaidCorpse or
                    ActorJobKind.ConsumeRaidCorpse)))
        {
            return;
        }
        if (_raidPhase == GoblinRaidPhase.Looting && victory)
        {
            _raidPhase = GoblinRaidPhase.Returning;
            foreach (var actor in GetRaidParty())
            {
                actor.ClearJob();
            }
            return;
        }
        if (_raidPhase == GoblinRaidPhase.Returning &&
            GetRaidParty().Any(actor => actor.Position != _raidRallyPoint))
        {
            return;
        }
        if (!victory && !defeated)
        {
            return;
        }

        _humanVillage.EndGoblinAttack();
        var survivingRaiders = _raidPartyIds.Count;
        _raidPhase = GoblinRaidPhase.None;
        _raidRallyPoint = default;
        _raidPartyIds.Clear();
        Publish(
            victory ? SimulationEventKind.RaidVictory : SimulationEventKind.RaidDefeated,
            EntityId.None,
            EntityId.None,
            survivingRaiders);
    }

    private static int Distance(GridPosition left, GridPosition right) =>
        Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y) + Math.Abs(left.Z - right.Z);
}
