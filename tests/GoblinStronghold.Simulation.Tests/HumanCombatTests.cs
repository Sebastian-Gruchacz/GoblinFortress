using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class HumanCombatTests
{
    [Fact]
    public void AccidentalEncounterCanBecomeFightWithoutBecomingRaidOrder()
    {
        var engine = CreateEncounterEngine();

        engine.AdvanceTicks(720);

        var snapshot = engine.CreateSnapshot();
        var events = engine.DrainEvents();
        Assert.False(snapshot.HumanVillage.GoblinAttackOrdered);
        Assert.InRange(snapshot.HumanVillage.LastIntruderSeenTick, 0, snapshot.Tick.Value);
        Assert.True(snapshot.HumanVillage.GuardHitPoints < snapshot.HumanVillage.MaximumGuardHitPoints);
        Assert.Contains(events, item => item.Kind == SimulationEventKind.HumanVillageAlerted);
        Assert.Contains(events, item =>
            item.Kind == SimulationEventKind.HumanGuardHitGoblin &&
            (item.Subject.Value & 0x8000000000000000UL) != 0);
        Assert.Contains(events, item =>
            item.Kind == SimulationEventKind.GoblinHitHumanGuard &&
            (item.Target.Value & 0x8000000000000000UL) != 0);
        Assert.Equal(
            snapshot.HumanVillage.GuardHitPoints,
            snapshot.HumanVillage.Villagers.Where(villager =>
                villager.Role == HumanCohortRole.Guards).Sum(villager => villager.Health));
    }

    [Fact]
    public void ExplicitRaidOrderStagesAndSendsProvisionedParty()
    {
        var engine = CreateEncounterEngine(orderRaid: true);

        engine.AdvanceTicks(1_600);

        var snapshot = engine.CreateSnapshot();
        var events = engine.DrainEvents();
        var departure = Assert.Single(events,
            item => item.Kind == SimulationEventKind.RaidDeparted);
        Assert.Equal(SimulationDefinitions.FieldCampCapacity, departure.Amount);
        Assert.Contains(events, item =>
            item.Kind == SimulationEventKind.GoblinHitHumanGuard &&
            item.Target != EntityId.None);
        Assert.True(
            snapshot.RaidPhase != GoblinRaidPhase.Preparing &&
            snapshot.RaidPhase != GoblinRaidPhase.Ready,
            $"Raid remained staged instead of fighting: {snapshot.RaidPhase}.");
    }

    [Fact]
    public void ActiveRaidPursuesGuardWhoMovedAwayAndNoLongerHasGuardTask()
    {
        var engine = CreateEncounterEngine(orderRaid: true);
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        var targetX = save["raidTargetX"]!.GetValue<int>();
        var targetY = save["raidTargetY"]!.GetValue<int>();
        var targetZ = save["raidTargetZ"]!.GetValue<int>();
        save["raidPhase"] = (int)GoblinRaidPhase.Marching;
        save["raidTargetRadius"] = SimulationEngine.MaximumRaidTargetRadius;
        save["raidDirectives"] = (int)RaidDirective.AttackGuards;
        save["humanVillage"]!["goblinAttackOrdered"] = true;
        save["humanVillage"]!["hostility"] = 100;
        save["humanVillage"]!["lastIntruderSeenTick"] =
            save["currentTick"]!.GetValue<long>();

        var guardCohort = save["humanVillage"]!["cohorts"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(model => model["role"]!.GetValue<int>() ==
                (int)HumanCohortRole.Guards);
        var previousGuardPopulation = guardCohort["population"]!.GetValue<int>();
        guardCohort["population"] = 1;
        var guardX = targetX > 32 ? targetX - 3 : targetX + 3;
        guardCohort["x"] = guardX;
        guardCohort["y"] = targetY;
        guardCohort["z"] = targetZ;
        var guards = save["humanVillage"]!["villagers"]!.AsArray()
            .Select(node => node!.AsObject())
            .Where(model => model["role"]!.GetValue<int>() ==
                (int)HumanCohortRole.Guards)
            .ToArray();
        for (var index = 0; index < guards.Length; index++)
        {
            guards[index]["health"] = index == 0 ? 2_000 : 0;
            guards[index]["task"] = (int)HumanCohortTask.StayNearVillage;
            guards[index]["x"] = guardX;
            guards[index]["y"] = targetY;
            guards[index]["z"] = targetZ;
        }
        save["humanVillage"]!["guardHitPoints"] = 2_000;
        save["humanVillage"]!["population"] =
            save["humanVillage"]!["population"]!.GetValue<int>() -
            previousGuardPopulation + 1;

        var partyIds = save["raidPartyIds"]!.AsArray()
            .Select(node => node!.GetValue<ulong>())
            .ToHashSet();
        foreach (var actor in save["actors"]!.AsArray()
                     .Select(node => node!.AsObject())
                     .Where(model => partyIds.Contains(model["id"]!.GetValue<ulong>())))
        {
            actor["x"] = targetX;
            actor["y"] = targetY;
            actor["z"] = targetZ;
            actor["jobKind"] = (int)ActorJobKind.None;
            actor["jobPhase"] = (int)ActorJobPhase.None;
            actor["hunger"] = SimulationDefinitions.Foundation.FoodSeekThreshold;
            actor["personalFood"] = 1;
            actor["personalFoodKind"] = (int)FoodKind.DriedRations;
            actor["personalFoodKinds"] = new JsonArray((int)FoodKind.DriedRations);
            actor["thirst"] = 0;
            actor["personalWater"] = SimulationDefinitions.Foundation.PersonalWaterCapacity;
            actor["personalStoneAmmo"] = 0;
            actor["fatigue"] = 0;
        }
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);

        engine.AdvanceTicks(SimulationDefinitions.Foundation.CombatIntervalTicks * 8);

        var snapshot = engine.CreateSnapshot();
        Assert.True(
            snapshot.HumanVillage.GuardHitPoints < 2_000,
            $"Phase {snapshot.RaidPhase}; guards " +
            string.Join(", ", snapshot.HumanVillage.Villagers
                .Where(villager => villager.Role == HumanCohortRole.Guards)
                .Select(villager => $"{villager.Position}:{villager.Health}:{villager.Task}")) +
            "; raiders " + string.Join(", ", snapshot.Actors
                .Where(actor => snapshot.RaidPartyIds.Contains(actor.Id))
                .Select(actor => $"{actor.Position}:{actor.Job.Kind}->{actor.Job.Target}")));
        Assert.Contains(engine.DrainEvents(), item =>
            item.Kind == SimulationEventKind.GoblinHitHumanGuard);
    }

    [Fact]
    public void RaidVictoryClearsActiveLifecycleAndReleasesSurvivingPartySelection()
    {
        var engine = CreateEncounterEngine(orderRaid: true);
        var preparing = engine.CreateSnapshot();
        Assert.Equal(GoblinRaidPhase.Preparing, preparing.RaidPhase);
        var party = preparing.RaidPartyIds.ToArray();
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        save["raidPhase"] = (int)GoblinRaidPhase.Marching;
        save["raidTargetRadius"] = SimulationEngine.MaximumRaidTargetRadius;
        save["raidDirectives"] = save["raidDirectives"]!.GetValue<int>() |
            (int)(RaidDirective.ConsumeCorpses | RaidDirective.BudCorpses);
        save["humanVillage"]!["goblinAttackOrdered"] = true;
        save["humanVillage"]!["hostility"] = 100;
        save["humanVillage"]!["lastIntruderSeenTick"] = save["currentTick"]!.GetValue<long>();
        var guard = save["humanVillage"]!["cohorts"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(model => model["role"]!.GetValue<int>() == (int)HumanCohortRole.Guards);
        var guardPopulation = guard["population"]!.GetValue<int>();
        guard["population"] = 0;
        foreach (var villager in save["humanVillage"]!["villagers"]!.AsArray()
                     .Select(node => node!.AsObject())
                     .Where(model => model["role"]!.GetValue<int>() ==
                         (int)HumanCohortRole.Guards))
        {
            villager["health"] = 0;
        }
        save["humanVillage"]!["guardHitPoints"] = 0;
        save["humanVillage"]!["population"] =
            save["humanVillage"]!["population"]!.GetValue<int>() - guardPopulation;
        var corpseId = save["nextEntityId"]!.GetValue<ulong>();
        save["nextEntityId"] = corpseId + 1;
        save["corpses"]!.AsArray().Add(new JsonObject
        {
            ["id"] = corpseId,
            ["kind"] = (int)CorpseKind.Human,
            ["name"] = "Pokonany strażnik",
            ["x"] = save["raidTargetX"]!.GetValue<int>(),
            ["y"] = save["raidTargetY"]!.GetValue<int>(),
            ["z"] = save["raidTargetZ"]!.GetValue<int>(),
            ["createdAtTick"] = save["currentTick"]!.GetValue<long>(),
            ["containedWater"] = 0,
            ["inheritableSkills"] = (int)(GoblinSkill.Hauling | GoblinSkill.Building),
            ["inheritableHaulingExperience"] = 400,
            ["inheritableBuildingExperience"] = 400,
            ["inheritableForagingPreference"] = -1,
            ["inheritableHaulingPreference"] = 1,
            ["inheritableBuildingPreference"] = 2,
            ["contents"] = new JsonArray(new JsonObject
            {
                ["resource"] = (int)ResourceKind.Equipment,
                ["foodKind"] = (int)FoodKind.None,
                ["variant"] = (int)ResourceVariant.EquipmentWoodenSpear,
                ["quantity"] = 1,
                ["unitWeight"] = 3,
            }),
        });
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);

        engine.AdvanceTicks(1);

        Assert.Equal(GoblinRaidPhase.Looting, engine.CreateSnapshot().RaidPhase);
        var lootingRestored = SimulationEngine.Load(
            engine.Save(),
            SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), lootingRestored.ComputeStateHash());
        engine = lootingRestored;
        var observedCarriedCorpse = false;
        var observedPartialConsumption = false;
        var observedCorpseBud = false;
        var observedPollinator = EntityId.None;
        var observedReturn = false;
        for (var tick = 0;
             tick < 20_000 && engine.CreateSnapshot().RaidPhase != GoblinRaidPhase.None;
             tick++)
        {
            engine.AdvanceTicks(1);
            observedReturn |= engine.CreateSnapshot().RaidPhase == GoblinRaidPhase.Returning;
            var partiallyConsumed = engine.CreateSnapshot().Corpses.FirstOrDefault(corpse =>
                corpse.Id.Value == corpseId && corpse.EdiblePortions is > 0 and < 8);
            if (!observedPartialConsumption && partiallyConsumed is not null)
            {
                observedPartialConsumption = true;
                var feastingRestored = SimulationEngine.Load(
                    engine.Save(),
                    SimulationDefinitions.Foundation);
                Assert.Equal(engine.ComputeStateHash(), feastingRestored.ComputeStateHash());
                Assert.Equal(
                    partiallyConsumed.EdiblePortions,
                    feastingRestored.CreateSnapshot().Corpses
                        .Single(corpse => corpse.Id.Value == corpseId).EdiblePortions);
                engine = feastingRestored;
            }
            if (!observedCorpseBud && engine.CreateSnapshot().GoblinBuds.Any(bud =>
                    bud.OriginCorpseId.Value == corpseId))
            {
                observedCorpseBud = true;
                var corpseBud = engine.CreateSnapshot().GoblinBuds.Single(bud =>
                    bud.OriginCorpseId.Value == corpseId);
                observedPollinator = corpseBud.ParentId;
                Assert.NotEqual(EntityId.None, corpseBud.ParentId);
                Assert.Equal(
                    GoblinSkill.Hauling | GoblinSkill.Building,
                    corpseBud.OriginImprint.KnownSkills);
                Assert.Equal(400, corpseBud.OriginImprint.Experience.Building);
                var buddingRestored = SimulationEngine.Load(
                    engine.Save(),
                    SimulationDefinitions.Foundation);
                Assert.Equal(engine.ComputeStateHash(), buddingRestored.ComputeStateHash());
                engine = buddingRestored;
            }
            if (!observedCarriedCorpse && engine.CreateSnapshot().Actors.Any(actor =>
                    actor.CarriedCorpseId.Value == corpseId))
            {
                observedCarriedCorpse = true;
                var carryingRestored = SimulationEngine.Load(
                    engine.Save(),
                    SimulationDefinitions.Foundation);
                Assert.Equal(engine.ComputeStateHash(), carryingRestored.ComputeStateHash());
                engine = carryingRestored;
            }
        }

        var completed = engine.CreateSnapshot();
        Assert.Equal(GoblinRaidPhase.None, completed.RaidPhase);
        Assert.Equal(default, completed.RaidRallyPoint);
        Assert.False(completed.HumanVillage.GoblinAttackOrdered);
        Assert.Empty(completed.RaidPartyIds);
        Assert.True(observedCarriedCorpse);
        Assert.True(observedPartialConsumption);
        Assert.True(observedCorpseBud);
        Assert.Contains(observedPollinator, party);
        Assert.True(observedReturn);
        Assert.DoesNotContain(completed.Corpses, corpse => corpse.Id.Value == corpseId);
        Assert.Equal(
            preparing.Actors.Count + 1,
            completed.Actors.Count + completed.GoblinBuds.Count);
        Assert.All(completed.Actors.Where(actor => party.Contains(actor.Id)), actor =>
            Assert.Equal(preparing.RaidRallyPoint, actor.Position));
        Assert.Contains(completed.ItemStacks, item =>
            item.Location.Kind == ItemLocationKind.Ground &&
            item.Location.Position == preparing.RaidRallyPoint &&
            item.Variant == ResourceVariant.EquipmentWoodenSpear);
        Assert.Equal(0, completed.HumanVillage.FoodStock);
        Assert.Equal(0, completed.HumanVillage.GoodsStock);
        Assert.Equal(24, completed.HumanVillage.WoodStock);
        Assert.DoesNotContain(completed.VillageLootContainers.SelectMany(item => item.Contents),
            item => item.Variant == ResourceVariant.EquipmentWoodenHoe);
        var events = engine.DrainEvents();
        Assert.Equal(8, events.Count(item =>
            item.Kind == SimulationEventKind.CorpseConsumed &&
            item.Target.Value == corpseId));
        var victory = Assert.Single(events, item =>
            item.Kind == SimulationEventKind.RaidVictory);
        Assert.Equal(party.Length, victory.Amount);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.DoesNotContain(
            restored.CreateSnapshot().VillageLootContainers.SelectMany(item => item.Contents),
            item => item.Variant == ResourceVariant.EquipmentWoodenHoe);
    }

    [Theory]
    [InlineData(RaidDirective.RecoverCorpses, false)]
    [InlineData(RaidDirective.BudCorpsesInPlace, true)]
    public void RaidCorpseHandlingModeControlsRecoveryDestination(
        RaidDirective corpseDirective,
        bool expectBudInPlace)
    {
        var engine = CreateEncounterEngine(orderRaid: true);
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        save["raidPhase"] = (int)GoblinRaidPhase.Marching;
        save["raidTargetRadius"] = SimulationEngine.MaximumRaidTargetRadius;
        save["raidDirectives"] = (int)(RaidDirective.AttackGuards | corpseDirective);
        save["humanVillage"]!["goblinAttackOrdered"] = true;
        save["humanVillage"]!["hostility"] = 100;
        save["humanVillage"]!["lastIntruderSeenTick"] = save["currentTick"]!.GetValue<long>();
        var guard = save["humanVillage"]!["cohorts"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(model => model["role"]!.GetValue<int>() == (int)HumanCohortRole.Guards);
        var guardPopulation = guard["population"]!.GetValue<int>();
        guard["population"] = 0;
        foreach (var villager in save["humanVillage"]!["villagers"]!.AsArray()
                     .Select(node => node!.AsObject())
                     .Where(model => model["role"]!.GetValue<int>() ==
                         (int)HumanCohortRole.Guards))
        {
            villager["health"] = 0;
        }
        save["humanVillage"]!["guardHitPoints"] = 0;
        save["humanVillage"]!["population"] =
            save["humanVillage"]!["population"]!.GetValue<int>() - guardPopulation;
        var corpseId = save["nextEntityId"]!.GetValue<ulong>();
        save["nextEntityId"] = corpseId + 1;
        var corpsePosition = new GridPosition(
            save["raidTargetX"]!.GetValue<int>(),
            save["raidTargetY"]!.GetValue<int>(),
            save["raidTargetZ"]!.GetValue<int>());
        save["corpses"]!.AsArray().Add(new JsonObject
        {
            ["id"] = corpseId,
            ["kind"] = (int)CorpseKind.Human,
            ["name"] = "Nosiciel",
            ["x"] = corpsePosition.X,
            ["y"] = corpsePosition.Y,
            ["z"] = corpsePosition.Z,
            ["createdAtTick"] = save["currentTick"]!.GetValue<long>(),
            ["containedWater"] = 0,
            ["ediblePortions"] = 0,
            ["inheritableSkills"] = (int)GoblinSkill.Survival,
            ["contents"] = new JsonArray(),
        });
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        engine.AdvanceTicks(1);

        for (var tick = 0; tick < 10_000; tick++)
        {
            engine.AdvanceTicks(1);
            var current = engine.CreateSnapshot();
            var budCreated = current.GoblinBuds.Any(bud => bud.OriginCorpseId.Value == corpseId);
            var recovered = current.Corpses.Any(corpse =>
                corpse.Id.Value == corpseId && corpse.Position == current.RaidRallyPoint);
            if (budCreated || recovered)
            {
                break;
            }
        }

        var snapshot = engine.CreateSnapshot();
        if (expectBudInPlace)
        {
            var bud = Assert.Single(snapshot.GoblinBuds, bud => bud.OriginCorpseId.Value == corpseId);
            Assert.Equal(corpsePosition, bud.Position);
            Assert.NotEqual(EntityId.None, bud.ParentId);
            Assert.DoesNotContain(snapshot.Corpses, corpse => corpse.Id.Value == corpseId);
        }
        else
        {
            var corpse = Assert.Single(snapshot.Corpses, corpse => corpse.Id.Value == corpseId);
            Assert.Equal(snapshot.RaidRallyPoint, corpse.Position);
            Assert.DoesNotContain(snapshot.GoblinBuds, bud => bud.OriginCorpseId.Value == corpseId);
        }
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void AlertedEncounterContinuesIdenticallyAfterSaveLoad()
    {
        var engine = CreateEncounterEngine();
        AdvanceUntilAlerted(engine, maximumTicks: 720);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());

        engine.AdvanceTicks(500);
        restored.AdvanceTicks(500);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(
            engine.CreateSnapshot().HumanVillage.GuardHitPoints,
            restored.CreateSnapshot().HumanVillage.GuardHitPoints);
        Assert.Equal(
            engine.CreateSnapshot().HumanVillage.Population,
            restored.CreateSnapshot().HumanVillage.Population);
    }

    [Fact]
    public void LoadRejectsGuardHealthThatDoesNotMatchPopulation()
    {
        var engine = CreateEncounterEngine();
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        save["humanVillage"]!["guardHitPoints"] = 1;

        var exception = Assert.Throws<InvalidDataException>(() =>
            SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation));

        Assert.Contains("guard health", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AttackAllRaidDoctrineCanChaseAndStrikeFleeingCivilianInsideTargetArea()
    {
        var engine = CreateEncounterEngine(orderRaid: true);
        var snapshot = engine.CreateSnapshot();
        var living = snapshot.HumanVillage.Villagers.Where(item => item.Health > 0).ToArray();
        var civilians = living.Where(item => item.Role != HumanCohortRole.Guards).ToArray();
        var firingPair = civilians.SelectMany(target =>
                Enumerable.Range(0, engine.Map.Height)
                    .SelectMany(y => Enumerable.Range(0, engine.Map.Width)
                        .Select(x => new GridPosition(x, y)))
                    .Where(engine.World.IsTerrainTraversable)
                    .Where(position => Distance(position, target.Position) == 4)
                    .Where(position => living
                        .OrderBy(candidate => Distance(position, candidate.Position))
                        .ThenBy(candidate => candidate.Role == HumanCohortRole.Guards ? 0 : 1)
                        .ThenBy(candidate => candidate.Id)
                        .First().Id == target.Id)
                    .Select(position => (Target: target, Position: position)))
            .First();
        var combatTick = Enumerable.Range(1,
                engine.Definitions.Clock.Climate.Seasons.Max(item => item.TicksPerDay) * 2)
            .Select(offset => new SimulationTick(snapshot.Tick.Value + offset))
            .First(tick => tick.Value % engine.Definitions.CombatIntervalTicks == 0 &&
                SimulationCalendar.At(tick, engine.Definitions.Clock).IsNight);
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        save["currentTick"] = combatTick.Value - 1;
        save["raidPhase"] = (int)GoblinRaidPhase.Marching;
        save["raidTargetX"] = firingPair.Target.Position.X;
        save["raidTargetY"] = firingPair.Target.Position.Y;
        save["raidTargetZ"] = firingPair.Target.Position.Z;
        save["raidTargetRadius"] = SimulationEngine.MinimumRaidTargetRadius;
        save["raidDirectives"] = (int)(RaidDirective.AttackAll |
            RaidDirective.ContinueWhileTargetsVisible);
        save["humanVillage"]!["goblinAttackOrdered"] = true;
        save["humanVillage"]!["hostility"] = 100;
        save["humanVillage"]!["lastIntruderSeenTick"] = combatTick.Value - 1;
        var targetModel = save["humanVillage"]!["villagers"]!.AsArray()
            .Select(item => item!.AsObject())
            .Single(item => item["id"]!.GetValue<int>() == firingPair.Target.Id);
        targetModel["health"] = 1;
        targetModel["task"] = (int)HumanCohortTask.Flee;
        var raiderId = save["raidPartyIds"]!.AsArray()[0]!.GetValue<ulong>();
        var actor = save["actors"]!.AsArray()
            .Select(item => item!.AsObject())
            .Single(item => item["id"]!.GetValue<ulong>() == raiderId);
        actor["x"] = firingPair.Position.X;
        actor["y"] = firingPair.Position.Y;
        actor["z"] = firingPair.Position.Z;
        actor["equipment"] = actor["equipment"]!.GetValue<int>() |
            (int)PersonalEquipment.PrimitiveSling;
        actor["personalStoneAmmo"] = 1;
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        var guardsBefore = engine.CreateSnapshot().HumanVillage.GuardHitPoints;

        engine.AdvanceTicks(1);

        snapshot = engine.CreateSnapshot();
        Assert.Equal(guardsBefore, snapshot.HumanVillage.GuardHitPoints);
        Assert.Equal(0, snapshot.HumanVillage.Villagers.Single(item =>
            item.Id == firingPair.Target.Id).Health);
        Assert.Contains(engine.DrainEvents(), item =>
            item.Kind == SimulationEventKind.GoblinHitHumanCivilian &&
            item.Target.Value == (0x8000000000000000UL | (uint)firingPair.Target.Id));

        static int Distance(GridPosition left, GridPosition right) =>
            Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y) +
            Math.Abs(left.Z - right.Z);
    }

    [Fact]
    public void AttackAllRaidStopsWhenFleeingVillagersLeaveTargetArea()
    {
        var engine = CreateEncounterEngine(orderRaid: true);
        var snapshot = engine.CreateSnapshot();
        var living = snapshot.HumanVillage.Villagers.Where(item => item.Health > 0).ToArray();
        var safeCenter = Enumerable.Range(0, engine.Map.Height)
            .SelectMany(y => Enumerable.Range(0, engine.Map.Width)
                .Select(x => new GridPosition(x, y)))
            .Where(engine.World.IsTerrainTraversable)
            .Where(position => living.All(villager =>
                Distance(position, villager.Position) >
                SimulationEngine.MinimumRaidTargetRadius + 2))
            .OrderBy(position => living.Min(villager =>
                Distance(position, villager.Position)))
            .First();
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        save["raidPhase"] = (int)GoblinRaidPhase.Marching;
        save["raidTargetX"] = safeCenter.X;
        save["raidTargetY"] = safeCenter.Y;
        save["raidTargetZ"] = safeCenter.Z;
        save["raidTargetRadius"] = SimulationEngine.MinimumRaidTargetRadius;
        save["raidDirectives"] = (int)(RaidDirective.AttackAll |
            RaidDirective.ContinueWhileTargetsVisible);
        save["humanVillage"]!["goblinAttackOrdered"] = true;
        foreach (var villager in save["humanVillage"]!["villagers"]!.AsArray()
                     .Select(item => item!.AsObject())
                     .Where(item => item["health"]!.GetValue<int>() > 0))
        {
            villager["task"] = (int)HumanCohortTask.Flee;
        }
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);

        engine.AdvanceTicks(1);

        snapshot = engine.CreateSnapshot();
        Assert.Equal(GoblinRaidPhase.Returning, snapshot.RaidPhase);
        Assert.False(snapshot.HumanVillage.GoblinAttackOrdered);

        static int Distance(GridPosition left, GridPosition right) =>
            Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y) +
            Math.Abs(left.Z - right.Z);
    }

    [Fact]
    public void ActiveRaidPartyFocusesItsAttacksOnTheSameWoundedGuard()
    {
        var engine = CreateEncounterEngine(orderRaid: true);
        var snapshot = engine.CreateSnapshot();
        var combatTick = Enumerable.Range(1,
                engine.Definitions.Clock.Climate.Seasons.Max(item => item.TicksPerDay) * 2)
            .Select(offset => new SimulationTick(snapshot.Tick.Value + offset))
            .First(tick => tick.Value % engine.Definitions.CombatIntervalTicks == 0 &&
                tick.Value % engine.Definitions.HumanCohortMovementIntervalTicks != 0 &&
                SimulationCalendar.At(tick, engine.Definitions.Clock).IsNight);
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        save["currentTick"] = combatTick.Value - 1;
        save["raidPhase"] = (int)GoblinRaidPhase.Marching;
        save["raidTargetRadius"] = SimulationEngine.MaximumRaidTargetRadius;
        save["raidDirectives"] = (int)RaidDirective.AttackAll;
        save["humanVillage"]!["goblinAttackOrdered"] = true;
        save["humanVillage"]!["hostility"] = 100;
        save["humanVillage"]!["lastIntruderSeenTick"] = combatTick.Value - 1;
        var guards = save["humanVillage"]!["villagers"]!.AsArray()
            .Select(item => item!.AsObject())
            .Where(item => item["role"]!.GetValue<int>() == (int)HumanCohortRole.Guards)
            .OrderBy(item => item["id"]!.GetValue<int>())
            .ToArray();
        var expectedTarget = guards[0];
        expectedTarget["health"] = 3_000;
        foreach (var guard in guards)
        {
            guard["task"] = (int)HumanCohortTask.Guard;
        }
        save["humanVillage"]!["guardHitPoints"] = guards.Sum(item =>
            item["health"]!.GetValue<int>());
        var targetPosition = new GridPosition(
            expectedTarget["x"]!.GetValue<int>(),
            expectedTarget["y"]!.GetValue<int>(),
            expectedTarget["z"]!.GetValue<int>());
        var partyIds = save["raidPartyIds"]!.AsArray()
            .Select(item => item!.GetValue<ulong>())
            .ToHashSet();
        foreach (var actor in save["actors"]!.AsArray()
                     .Select(item => item!.AsObject())
                     .Where(item => partyIds.Contains(item["id"]!.GetValue<ulong>())))
        {
            actor["x"] = targetPosition.X;
            actor["y"] = targetPosition.Y;
            actor["z"] = targetPosition.Z;
            actor["jobKind"] = (int)ActorJobKind.None;
            actor["jobPhase"] = (int)ActorJobPhase.None;
        }
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);

        engine.AdvanceTicks(1);

        var hits = engine.DrainEvents()
            .Where(item => item.Kind == SimulationEventKind.GoblinHitHumanGuard)
            .ToArray();
        Assert.Equal(partyIds.Count, hits.Length);
        Assert.All(hits, hit => Assert.Equal(
            0x8000000000000000UL | (uint)expectedTarget["id"]!.GetValue<int>(),
            hit.Target.Value));
    }

    [Fact]
    public void CiviliansFleeAwayFromRaidersAtNight()
    {
        var engine = CreateEncounterEngine(orderRaid: true);
        var snapshot = engine.CreateSnapshot();
        var occupied = snapshot.HumanVillage.Villagers
            .Where(item => item.Health > 0)
            .Select(item => item.Position)
            .ToHashSet();
        var fleeSetup = snapshot.HumanVillage.Villagers
            .Where(item => item.Health > 0 && item.Role != HumanCohortRole.Guards)
            .SelectMany(civilian => engine.World.GetCardinalWorldNeighbors(civilian.Position)
                .Where(attackerPosition => engine.World.IsTerrainTraversable(attackerPosition))
                .SelectMany(attackerPosition =>
                    engine.World.GetCardinalWorldNeighbors(civilian.Position)
                        .Where(escapePosition =>
                            engine.World.IsTerrainTraversable(escapePosition) &&
                            !occupied.Contains(escapePosition) &&
                            Distance(escapePosition, attackerPosition) >
                            Distance(civilian.Position, attackerPosition))
                        .Select(escapePosition => new
                        {
                            Civilian = civilian,
                            AttackerPosition = attackerPosition,
                        })))
            .OrderBy(item => item.Civilian.Id)
            .First();
        var civilian = fleeSetup.Civilian;
        var movementTick = Enumerable.Range(1,
                engine.Definitions.Clock.Climate.Seasons.Max(item => item.TicksPerDay) * 2)
            .Select(offset => new SimulationTick(snapshot.Tick.Value + offset))
            .First(tick =>
                tick.Value % engine.Definitions.HumanCohortMovementIntervalTicks == 0 &&
                SimulationCalendar.At(tick, engine.Definitions.Clock).IsNight);
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        save["currentTick"] = movementTick.Value - 1;
        save["raidPhase"] = (int)GoblinRaidPhase.Marching;
        save["raidTargetX"] = civilian.Position.X;
        save["raidTargetY"] = civilian.Position.Y;
        save["raidTargetZ"] = civilian.Position.Z;
        save["raidTargetRadius"] = SimulationEngine.MaximumRaidTargetRadius;
        save["raidDirectives"] = (int)(RaidDirective.AttackAll |
            RaidDirective.ContinueWhileTargetsVisible);
        save["humanVillage"]!["goblinAttackOrdered"] = true;
        save["humanVillage"]!["hostility"] = 100;
        save["humanVillage"]!["lastIntruderSeenTick"] = movementTick.Value - 1;
        save["humanVillage"]!["villagers"]!.AsArray()
            .Single(item => item!["id"]!.GetValue<int>() == civilian.Id)!["carriedGrime"] = 6;
        var partyIds = save["raidPartyIds"]!.AsArray()
            .Select(item => item!.GetValue<ulong>())
            .ToHashSet();
        foreach (var actor in save["actors"]!.AsArray()
                     .Select(item => item!.AsObject())
                     .Where(item => partyIds.Contains(item["id"]!.GetValue<ulong>())))
        {
            actor["x"] = fleeSetup.AttackerPosition.X;
            actor["y"] = fleeSetup.AttackerPosition.Y;
            actor["z"] = fleeSetup.AttackerPosition.Z;
            actor["jobKind"] = (int)ActorJobKind.None;
            actor["jobPhase"] = (int)ActorJobPhase.None;
        }
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);

        engine.AdvanceTicks(1);

        var fleeing = engine.CreateSnapshot().HumanVillage.Villagers.Single(item =>
            item.Id == civilian.Id);
        Assert.Equal(HumanCohortTask.Flee, fleeing.Task);
        Assert.InRange(fleeing.CarriedGrime, 0, 5);
        Assert.True(
            Distance(fleeing.Position, fleeSetup.AttackerPosition) >
            Distance(civilian.Position, fleeSetup.AttackerPosition),
            $"Civilian {civilian.Id} remained at {civilian.Position} during a night raid.");

        static int Distance(GridPosition left, GridPosition right) =>
            Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y) +
            Math.Abs(left.Z - right.Z);
    }

    [Fact]
    public void LootingRaidResumesCombatWhenAttackAllTargetsRemain()
    {
        var engine = CreateEncounterEngine(orderRaid: true);
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        save["raidPhase"] = (int)GoblinRaidPhase.Looting;
        save["raidTargetRadius"] = SimulationEngine.MaximumRaidTargetRadius;
        save["raidDirectives"] = (int)RaidDirective.AttackAll;
        save["humanVillage"]!["goblinAttackOrdered"] = false;
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);

        engine.AdvanceTicks(1);

        var snapshot = engine.CreateSnapshot();
        Assert.Equal(GoblinRaidPhase.Marching, snapshot.RaidPhase);
        Assert.True(snapshot.HumanVillage.GoblinAttackOrdered);
        Assert.All(snapshot.Actors.Where(actor => snapshot.RaidPartyIds.Contains(actor.Id)),
            actor => Assert.Equal(ActorJobKind.None, actor.Job.Kind));
    }

    [Fact]
    public void SlingExtendsGoblinAttackRangeAndConsumesPersonalStone()
    {
        var seed = new WorldSeed(0x511A6UL);
        var map = SwampMapGenerator.Generate(seed, width: 64, height: 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 0);
        var guards = engine.CreateSnapshot().HumanVillage.Villagers.Where(villager =>
            villager.Role == HumanCohortRole.Guards && villager.Health > 0).ToArray();
        var guard = guards[0];
        var firingPosition = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width)
                .Select(x => new GridPosition(x, y)))
            .Where(engine.World.IsSurfaceTraversable)
            .Where(position => Math.Abs(position.X - guard.Position.X) +
                Math.Abs(position.Y - guard.Position.Y) is >= 3 and <= 5)
            .OrderBy(position => Math.Abs(position.X - guard.Position.X) +
                Math.Abs(position.Y - guard.Position.Y))
            .First();
        var expectedTarget = guards
            .OrderBy(candidate => Math.Abs(candidate.Position.X - firingPosition.X) +
                Math.Abs(candidate.Position.Y - firingPosition.Y))
            .ThenBy(candidate => candidate.Id)
            .First();
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var targetModel = save["humanVillage"]!["villagers"]!.AsArray()
            .Select(item => item!.AsObject())
            .Single(item => item["id"]!.GetValue<int>() == expectedTarget.Id);
        targetModel["health"] = 1;
        save["humanVillage"]!["guardHitPoints"] =
            save["humanVillage"]!["guardHitPoints"]!.GetValue<int>() -
            expectedTarget.Health + 1;
        var actor = save["actors"]!.AsArray()[0]!.AsObject();
        actor["x"] = firingPosition.X;
        actor["y"] = firingPosition.Y;
        actor["z"] = firingPosition.Z;
        actor["equipment"] = actor["equipment"]!.GetValue<int>() |
            (int)PersonalEquipment.PrimitiveSling;
        actor["personalStoneAmmo"] = 1;
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        var guardHealth = engine.CreateSnapshot().HumanVillage.GuardHitPoints;

        for (var index = 0; index < 200 &&
             engine.CreateSnapshot().Actors[0].PersonalStoneAmmo > 0; index++)
        {
            engine.AdvanceTicks(1);
        }

        var snapshot = engine.CreateSnapshot();
        Assert.Equal(0, snapshot.Actors[0].PersonalStoneAmmo);
        Assert.True(snapshot.HumanVillage.GuardHitPoints < guardHealth);
        Assert.True(snapshot.HumanVillage.Villagers.Single(villager =>
            villager.Id == expectedTarget.Id).Health < expectedTarget.Health);
        Assert.All(snapshot.HumanVillage.Villagers.Where(villager =>
                villager.Role == HumanCohortRole.Guards && villager.Id != expectedTarget.Id),
            untouched => Assert.Equal(
                guards.Single(before => before.Id == untouched.Id).Health,
                untouched.Health));
        Assert.Contains(snapshot.BloodStains, stain =>
            stain.Position == expectedTarget.Position && stain.Volume > 0);
        var corpse = Assert.Single(snapshot.Corpses, corpse =>
            corpse.Kind == CorpseKind.Human && corpse.Name == expectedTarget.Name);
        Assert.True(corpse.InheritanceImprint.KnownSkills.HasFlag(GoblinSkill.Survival));
        Assert.Contains(corpse.Contents, item =>
            item.Variant == ResourceVariant.EquipmentWoodenSpear);
        Assert.Contains(engine.DrainEvents(), simulationEvent =>
            simulationEvent.Kind == SimulationEventKind.GoblinHitHumanGuard &&
            simulationEvent.Target.Value ==
                (0x8000000000000000UL | (uint)expectedTarget.Id));
    }

    private static void AdvanceUntilAlerted(SimulationEngine engine, int maximumTicks)
    {
        for (var index = 0; index < maximumTicks; index++)
        {
            engine.AdvanceTicks(1);
            if (engine.CreateSnapshot().HumanVillage.Hostility > 0)
            {
                return;
            }
        }

        throw new Xunit.Sdk.XunitException("The explorer did not alert the village in time.");
    }

    private static SimulationEngine CreateEncounterEngine(bool orderRaid = false)
    {
        var seed = new WorldSeed(0x474F424C494EUL);
        var map = SwampMapGenerator.Generate(seed, width: 64, height: 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 8,
            initialFoodStock: orderRaid ? 160 : 12,
            initialWoodStock: orderRaid ? 10 : 0);
        if (!orderRaid)
        {
            engine.QueueCommand(SimulationCommand.CreateStorageZone(
                new SimulationTick(1),
                sequence: 1,
                map.HumanVillage,
                ResourceKind.Food,
                capacity: 20));
        }
        if (orderRaid)
        {
            var stagingTarget = new GridPosition(
                (map.GoblinSpawn.X + (2 * map.HumanVillage.X)) / 3,
                (map.GoblinSpawn.Y + (2 * map.HumanVillage.Y)) / 3,
                map.GoblinSpawn.Z);
            var camp = Enumerable.Range(0, map.Height)
                .SelectMany(y => Enumerable.Range(0, map.Width)
                    .Select(x => new GridPosition(x, y)))
                .Where(engine.World.CanBuildGoblinFieldCamp)
                .OrderBy(position => Math.Abs(position.X - stagingTarget.X) +
                    Math.Abs(position.Y - stagingTarget.Y))
                .ThenBy(position => position.Y)
                .ThenBy(position => position.X)
                .First();
            engine.QueueCommand(SimulationCommand.BuildGoblinFieldCamp(
                new SimulationTick(1), sequence: 998, camp));
        }
        engine.AdvanceTicks(1);

        if (orderRaid)
        {
            SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
            var executeAt = engine.CurrentTick.Next();
            engine.QueueCommand(SimulationCommand.ConfigureRaidDirectives(
                executeAt,
                sequence: 999,
                SimulationEngine.DefaultRaidDirectives |
                    RaidDirective.AutoLaunchWhenReady));
            engine.QueueCommand(SimulationCommand.AttackHumanVillage(
                executeAt, sequence: 1_000));
            engine.AdvanceTicks(1);
            return engine;
        }

        var setup = engine.CreateSnapshot();
        var food = setup.ItemStacks.Single(stack => stack.Resource == ResourceKind.Food);
        var storage = setup.StorageZones.Single(zone => zone.Position == map.HumanVillage);
        engine.QueueCommand(SimulationCommand.PickUp(
            new SimulationTick(2),
            sequence: 2,
            new EntityId(1),
            food.Id,
            quantity: 5));
        engine.QueueCommand(SimulationCommand.StoreCarried(
            new SimulationTick(3),
            sequence: 3,
            new EntityId(1),
            storage.Id));

        ulong sequence = 4;
        for (var tick = 24; tick <= 720; tick += 24)
        {
            for (ulong actor = 1; actor <= 8; actor++)
            {
                engine.QueueCommand(SimulationCommand.Forage(
                    new SimulationTick(tick + (long)actor),
                    sequence++,
                    new EntityId(actor)));
            }
        }

        return engine;
    }
}
