using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class FieldCampTests
{
    [Fact]
    public void FieldCampIsPhysicalRestShelterWithProvisionDemand()
    {
        var engine = CreateEngine();
        var position = FindCampPosition(engine);
        engine.QueueCommand(SimulationCommand.BuildGoblinFieldCamp(
            new SimulationTick(1), sequence: 1, position));

        engine.AdvanceTicks(1);

        var site = Assert.Single(engine.CreateSnapshot().ConstructionSites);
        Assert.Equal(ConstructionKind.GoblinFieldCamp, site.Kind);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);

        var snapshot = engine.CreateSnapshot();
        var camp = Assert.Single(snapshot.WorldObjects,
            item => item.Kind == WorldObjectKind.GoblinFieldCamp);
        Assert.Equal(position, camp.Anchor);
        Assert.Equal(4, camp.Parts.Count(item => item.Kind == WorldObjectPartKind.Floor));
        Assert.Equal(4, camp.Parts.Count(item => item.Kind == WorldObjectPartKind.Roof));
        var provisions = Assert.Single(snapshot.StorageZones, item => item.Position == position);
        Assert.Equal(ResourceKind.Food, provisions.AcceptedResource);
        Assert.Equal(48, provisions.Capacity);
        Assert.Equal(24, provisions.DesiredQuantity);
        Assert.Equal(4, snapshot.ItemStacks.Where(item => item.Resource == ResourceKind.Wood)
            .Sum(item => item.Quantity));
    }

    [Fact]
    public void FieldCampBlueprintDoesNotRequireNearbyWater()
    {
        var engine = CreateEngine();
        var storagePositions = engine.CreateSnapshot().StorageZones
            .Select(zone => zone.Position)
            .ToHashSet();
        var shallowWater = Enumerable.Range(0, engine.Map.Height)
            .SelectMany(y => Enumerable.Range(0, engine.Map.Width)
                .Select(x => new GridPosition(x, y)))
            .Where(cell => engine.Map.GetCell(cell).Terrain == TerrainKind.ShallowWater)
            .ToArray();
        var position = Enumerable.Range(0, engine.Map.Height)
            .SelectMany(y => Enumerable.Range(0, engine.Map.Width)
                .Select(x => new GridPosition(x, y)))
            .Where(engine.World.CanBuildGoblinFieldCamp)
            .Where(candidate => !storagePositions.Contains(candidate))
            .Where(candidate => !shallowWater.Any(cell =>
                Math.Abs(cell.X - candidate.X) + Math.Abs(cell.Y - candidate.Y) <= 4))
            .OrderBy(candidate => Math.Abs(candidate.X - engine.Map.GoblinSpawn.X) +
                Math.Abs(candidate.Y - engine.Map.GoblinSpawn.Y))
            .First();

        engine.QueueCommand(SimulationCommand.BuildGoblinFieldCamp(
            new SimulationTick(1), sequence: 1, position));
        engine.AdvanceTicks(1);

        Assert.Contains(engine.CreateSnapshot().ConstructionSites,
            site => site.Kind == ConstructionKind.GoblinFieldCamp && site.Anchor == position);
    }

    [Fact]
    public void FieldCampCanBeBuiltUndergroundAndSurvivesSaveLoad()
    {
        var engine = CreateEngine();
        var actor = Assert.Single(engine.CreateSnapshot().Actors);
        var landing = engine.Map.VerticalPassages
            .Where(passage => passage.Kind == VerticalPassageKind.CaveMouth)
            .Select(passage => passage.Lower)
            .First(position => engine.Navigation.FindPath(actor.Position, position) is not null);
        engine.QueueCommand(SimulationCommand.Move(
            new SimulationTick(1), sequence: 1, actor.Id, landing));
        for (var tick = 0; tick < 8_000 &&
             Assert.Single(engine.CreateSnapshot().Actors).Position != landing; tick++)
        {
            engine.AdvanceTicks(1);
        }

        var snapshot = engine.CreateSnapshot();
        var position =
            (from y in Enumerable.Range(0, engine.Map.Height - 1)
             from x in Enumerable.Range(0, engine.Map.Width - 1)
             let candidate = new GridPosition(x, y, landing.Z)
             where engine.World.CanBuildGoblinFieldCamp(candidate)
             where new[]
             {
                 candidate,
                 candidate with { X = x + 1 },
                 candidate with { Y = y + 1 },
                 candidate with { X = x + 1, Y = y + 1 },
             }.All(cell => snapshot.GetVisibility(cell, engine.Map.Width).IsDiscovered())
             orderby Math.Abs(x - landing.X) + Math.Abs(y - landing.Y)
             select candidate).First();
        engine.QueueCommand(SimulationCommand.BuildGoblinFieldCamp(
            engine.CurrentTick.Next(), sequence: 2, position));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine, maximumTicks: 12_000);

        snapshot = engine.CreateSnapshot();
        var camp = Assert.Single(snapshot.WorldObjects, item =>
            item.Kind == WorldObjectKind.GoblinFieldCamp && item.Anchor == position);
        Assert.Equal(4, camp.Parts.Count(item => item.Kind == WorldObjectPartKind.Floor));
        Assert.DoesNotContain(camp.Parts, item => item.Kind == WorldObjectPartKind.Roof);
        Assert.Contains(snapshot.StorageZones, zone => zone.Position == position);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Contains(restored.CreateSnapshot().WorldObjects, item =>
            item.Kind == WorldObjectKind.GoblinFieldCamp && item.Anchor == position);
    }

    [Fact]
    public void RaidRequiresCampThenWaitsForExplicitDepartureAndSurvivesSaveLoad()
    {
        var engine = CreateEngine();
        engine.QueueCommand(SimulationCommand.AttackHumanVillage(
            new SimulationTick(1), sequence: 1));
        engine.AdvanceTicks(1);
        Assert.Equal(GoblinRaidPhase.None, engine.CreateSnapshot().RaidPhase);
        Assert.Contains(engine.DrainEvents(), item =>
            item.Kind == SimulationEventKind.CommandRejected &&
            item.Amount == (int)SimulationCommandKind.AttackHumanVillage);

        var position = FindCampPosition(engine);
        engine.QueueCommand(SimulationCommand.BuildGoblinFieldCamp(
            new SimulationTick(2), sequence: 2, position));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
        engine.QueueCommand(SimulationCommand.AttackHumanVillage(
            engine.CurrentTick.Next(), sequence: 3));
        engine.AdvanceTicks(1);

        var preparing = engine.CreateSnapshot();
        Assert.Equal(GoblinRaidPhase.Preparing, preparing.RaidPhase);
        Assert.Equal(position, preparing.RaidRallyPoint);
        Assert.False(preparing.HumanVillage.GoblinAttackOrdered);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());

        engine.AdvanceTicks(300);
        restored.AdvanceTicks(300);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        var departed = engine.CreateSnapshot();
        Assert.True(departed.RaidPhase == GoblinRaidPhase.Ready,
            $"Raid phase: {departed.RaidPhase}; rally: {departed.RaidRallyPoint}; actor: {Assert.Single(departed.Actors)}");
        Assert.False(departed.HumanVillage.GoblinAttackOrdered);
        Assert.False(departed.RaidPlan.Has(RaidDirective.AutoLaunchWhenReady));
        engine.QueueCommand(SimulationCommand.LaunchRaid(
            engine.CurrentTick.Next(), sequence: 4));
        engine.AdvanceTicks(1);
        Assert.Equal(GoblinRaidPhase.Marching, engine.CreateSnapshot().RaidPhase);
        Assert.True(engine.CreateSnapshot().HumanVillage.GoblinAttackOrdered);
        Assert.Contains(engine.DrainEvents(), item => item.Kind == SimulationEventKind.RaidDeparted);
    }

    [Fact]
    public void MarchingRaidCanBeRecalledAndEditedAgain()
    {
        var engine = CreateEngine();
        var campPosition = FindCampPosition(engine);
        engine.QueueCommand(SimulationCommand.BuildGoblinFieldCamp(
            new SimulationTick(1), sequence: 1, campPosition));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
        engine.QueueCommand(SimulationCommand.AttackHumanVillage(
            engine.CurrentTick.Next(), sequence: 2, campPosition));
        engine.AdvanceTicks(1);
        for (var tick = 0; tick < 2_000 &&
             engine.CreateSnapshot().RaidPhase != GoblinRaidPhase.Ready; tick++)
        {
            engine.AdvanceTicks(1);
        }
        engine.QueueCommand(SimulationCommand.LaunchRaid(
            engine.CurrentTick.Next(), sequence: 3));
        engine.AdvanceTicks(1);
        Assert.Equal(GoblinRaidPhase.Marching, engine.CreateSnapshot().RaidPhase);

        engine.QueueCommand(SimulationCommand.SuspendRaidPreparation(
            engine.CurrentTick.Next(), sequence: 4));
        engine.AdvanceTicks(1);

        var recalled = engine.CreateSnapshot();
        Assert.Equal(GoblinRaidPhase.Suspended, recalled.RaidPhase);
        Assert.False(recalled.HumanVillage.GoblinAttackOrdered);
        var member = Assert.Single(recalled.RaidPartyIds);
        engine.QueueCommand(SimulationCommand.ConfigureRaidMember(
            engine.CurrentTick.Next(), sequence: 5, member, selected: false));
        engine.AdvanceTicks(1);
        Assert.Empty(engine.CreateSnapshot().RaidPartyIds);
    }

    [Fact]
    public void IdleMarchingRaiderResumesRouteTowardActiveRaidObjective()
    {
        var engine = CreateEngine();
        var campPosition = FindCampPosition(engine);
        engine.QueueCommand(SimulationCommand.BuildGoblinFieldCamp(
            new SimulationTick(1), sequence: 1, campPosition));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
        engine.QueueCommand(SimulationCommand.AttackHumanVillage(
            engine.CurrentTick.Next(), sequence: 2, campPosition));
        engine.AdvanceTicks(1);
        for (var tick = 0; tick < 2_000 &&
             engine.CreateSnapshot().RaidPhase != GoblinRaidPhase.Ready; tick++)
        {
            engine.AdvanceTicks(1);
        }
        engine.QueueCommand(SimulationCommand.LaunchRaid(
            engine.CurrentTick.Next(), sequence: 3));
        engine.AdvanceTicks(1);

        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        var actor = save["actors"]![0]!.AsObject();
        actor["jobKind"] = (int)ActorJobKind.None;
        actor["jobPhase"] = (int)ActorJobPhase.None;
        actor["jobTargetX"] = 0;
        actor["jobTargetY"] = 0;
        actor["jobTargetZ"] = 0;
        actor["remainingRoute"] = new JsonArray();
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);

        for (var tick = 0; tick < 100 &&
             Assert.Single(engine.CreateSnapshot().Actors).Job.Kind != ActorJobKind.Move; tick++)
        {
            engine.AdvanceTicks(1);
        }

        var marching = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.Equal(ActorJobKind.Move, marching.Job.Kind);
        var raidPlan = engine.CreateSnapshot().RaidPlan;
        Assert.InRange(
            Math.Abs(marching.Job.Target.X - raidPlan.Target.X) +
            Math.Abs(marching.Job.Target.Y - raidPlan.Target.Y) +
            Math.Abs(marching.Job.Target.Z - raidPlan.Target.Z),
            0,
            raidPlan.TargetRadius);
    }

    [Fact]
    public void ExplicitRaidPartyIsUsedAndSurvivesSaveLoad()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0x5041525459UL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 3,
            initialFoodStock: 40,
            initialWoodStock: 10);
        var campPosition = FindCampPosition(engine);
        engine.QueueCommand(SimulationCommand.BuildGoblinFieldCamp(
            new SimulationTick(1), sequence: 1, campPosition));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);

        var actors = engine.CreateSnapshot().Actors.OrderBy(actor => actor.Id).ToArray();
        var executeAt = engine.CurrentTick.Next();
        engine.QueueCommand(SimulationCommand.ConfigureRaidMember(
            executeAt, sequence: 2, actors[1].Id, selected: true));
        engine.QueueCommand(SimulationCommand.ConfigureRaidMember(
            executeAt, sequence: 3, actors[2].Id, selected: true));
        engine.QueueCommand(SimulationCommand.AttackHumanVillage(executeAt, sequence: 4));
        engine.AdvanceTicks(1);

        var snapshot = engine.CreateSnapshot();
        Assert.True(snapshot.RaidPhase is GoblinRaidPhase.Preparing or GoblinRaidPhase.Ready);
        Assert.Equal([actors[1].Id, actors[2].Id], snapshot.RaidPartyIds);
        Assert.DoesNotContain(actors[0].Id, snapshot.RaidPartyIds);

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(snapshot.RaidPartyIds, restored.CreateSnapshot().RaidPartyIds);
    }

    [Fact]
    public void ExplicitlyEmptyRaidRosterSurvivesSaveLoadAndDoesNotAutoFill()
    {
        var engine = CreateEngine();
        var campPosition = FindCampPosition(engine);
        engine.QueueCommand(SimulationCommand.BuildGoblinFieldCamp(
            new SimulationTick(1), sequence: 1, campPosition));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
        engine.QueueCommand(SimulationCommand.AttackHumanVillage(
            engine.CurrentTick.Next(), sequence: 2, campPosition));
        engine.AdvanceTicks(1);
        var preparing = engine.CreateSnapshot();
        Assert.True(preparing.RaidPhase is GoblinRaidPhase.Preparing or GoblinRaidPhase.Ready);
        var actor = Assert.Single(preparing.Actors);
        Assert.Equal(actor.Id, Assert.Single(preparing.RaidPartyIds));
        var executeAt = engine.CurrentTick.Next();
        engine.QueueCommand(SimulationCommand.ConfigureRaidMember(
            executeAt, sequence: 3, actor.Id, selected: false));
        engine.QueueCommand(SimulationCommand.ConfigureRaidDirectives(
            executeAt,
            sequence: 4,
            RaidDirective.AttackGuards | RaidDirective.LootFood));
        engine.AdvanceTicks(1);

        var configured = engine.CreateSnapshot();
        Assert.True(configured.RaidRosterConfigured);
        Assert.Empty(configured.RaidPartyIds);
        Assert.True(configured.RaidPlan.Has(RaidDirective.LootFood));
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.True(restored.CreateSnapshot().RaidRosterConfigured);
        Assert.Empty(restored.CreateSnapshot().RaidPartyIds);

        restored.QueueCommand(SimulationCommand.AttackHumanVillage(
            restored.CurrentTick.Next(), sequence: 5, campPosition));
        restored.AdvanceTicks(1);

        Assert.Equal(GoblinRaidPhase.None, restored.CreateSnapshot().RaidPhase);
        Assert.Empty(restored.CreateSnapshot().RaidPartyIds);
    }

    [Fact]
    public void RaidCanUseExplicitFieldCampAsRallyPoint()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0x43414D50UL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 20,
            initialWoodStock: 20);
        var firstCamp = FindCampPosition(engine);
        engine.QueueCommand(SimulationCommand.BuildGoblinFieldCamp(
            new SimulationTick(1), sequence: 1, firstCamp));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);

        var selectedCamp = FindCampPosition(engine);
        Assert.NotEqual(firstCamp, selectedCamp);
        engine.QueueCommand(SimulationCommand.BuildGoblinFieldCamp(
            engine.CurrentTick.Next(), sequence: 2, selectedCamp));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);

        engine.QueueCommand(SimulationCommand.AttackHumanVillage(
            engine.CurrentTick.Next(), sequence: 3, selectedCamp));
        engine.AdvanceTicks(1);

        var snapshot = engine.CreateSnapshot();
        Assert.Equal(GoblinRaidPhase.Preparing, snapshot.RaidPhase);
        Assert.Equal(selectedCamp, snapshot.RaidRallyPoint);
    }

    [Fact]
    public void RaidPartyReservesAllCampBedsAndJuvenilesLeaveForStartArea()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0x42454453UL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 7,
            initialFoodStock: 60,
            initialWoodStock: 20);
        var campPosition = FindCampPosition(engine);
        engine.QueueCommand(SimulationCommand.BuildGoblinFieldCamp(
            new SimulationTick(1), sequence: 1, campPosition));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);

        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        var currentTick = save["currentTick"]!.GetValue<long>();
        var actors = save["actors"]!.AsArray()
            .Select(node => node!.AsObject())
            .OrderBy(node => node["id"]!.GetValue<ulong>())
            .ToArray();
        foreach (var actor in actors)
        {
            actor["x"] = campPosition.X;
            actor["y"] = campPosition.Y;
            actor["z"] = campPosition.Z;
            actor["hunger"] = 0;
            actor["thirst"] = 0;
            actor["fatigue"] = 0;
            ClearActorJob(actor);
        }
        foreach (var juvenile in actors.Skip(SimulationDefinitions.FieldCampCapacity))
        {
            juvenile["birthTick"] = currentTick;
            juvenile["maturesAtTick"] = currentTick + 10_000;
            juvenile["ageOffsetTicks"] = 0;
        }
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);

        var snapshotActors = engine.CreateSnapshot().Actors.OrderBy(actor => actor.Id).ToArray();
        var raiders = snapshotActors.Take(SimulationDefinitions.FieldCampCapacity).ToArray();
        var juveniles = snapshotActors.Skip(SimulationDefinitions.FieldCampCapacity).ToArray();
        var executeAt = engine.CurrentTick.Next();
        var sequence = 2UL;
        foreach (var raider in raiders)
        {
            engine.QueueCommand(SimulationCommand.ConfigureRaidMember(
                executeAt, sequence++, raider.Id, selected: true));
        }
        engine.QueueCommand(SimulationCommand.AttackHumanVillage(
            executeAt, sequence, campPosition));
        engine.AdvanceTicks(1);

        var campCells = engine.CreateSnapshot().WorldObjects
            .Single(item => item.Kind == WorldObjectKind.GoblinFieldCamp &&
                item.Anchor == campPosition)
            .GetAbsoluteParts()
            .Where(item => item.Part.Kind == WorldObjectPartKind.Floor)
            .Select(item => item.Position)
            .ToHashSet();
        Assert.All(juveniles, juvenile => Assert.Equal(
            ActorJobKind.Move,
            engine.CreateSnapshot().Actors.Single(actor => actor.Id == juvenile.Id).Job.Kind));

        for (var tick = 0; tick < 2_000 && engine.CreateSnapshot().Actors
                 .Any(actor => juveniles.Any(juvenile => juvenile.Id == actor.Id) &&
                     campCells.Contains(actor.Position)); tick++)
        {
            engine.AdvanceTicks(1);
        }

        var evacuated = engine.CreateSnapshot();
        Assert.True(evacuated.RaidPhase is GoblinRaidPhase.Preparing or GoblinRaidPhase.Ready);
        Assert.Equal(raiders.Select(actor => actor.Id), evacuated.RaidPartyIds);
        Assert.DoesNotContain(evacuated.Actors, actor =>
            juveniles.Any(juvenile => juvenile.Id == actor.Id) && campCells.Contains(actor.Position));
        Assert.True(evacuated.Actors.Count(actor => campCells.Contains(actor.Position)) <=
            SimulationDefinitions.FieldCampCapacity);
    }

    [Fact]
    public void PreparedRaidCanWaitForManualLaunchAndKeepsPlanAcrossSaveLoad()
    {
        var engine = CreateEngine();
        var campPosition = FindCampPosition(engine);
        engine.QueueCommand(SimulationCommand.BuildGoblinFieldCamp(
            new SimulationTick(1), sequence: 1, campPosition));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);

        var target = engine.Map.HumanVillage;
        var directives = SimulationEngine.DefaultRaidDirectives &
            ~RaidDirective.AutoLaunchWhenReady;
        var executeAt = engine.CurrentTick.Next();
        engine.QueueCommand(SimulationCommand.ConfigureRaidTarget(
            executeAt, sequence: 2, target, radius: 4));
        engine.QueueCommand(SimulationCommand.ConfigureRaidDirectives(
            executeAt, sequence: 3, directives));
        engine.QueueCommand(SimulationCommand.AttackHumanVillage(
            executeAt, sequence: 4, campPosition));
        engine.AdvanceTicks(1);

        Assert.Equal(GoblinRaidPhase.Preparing, engine.CreateSnapshot().RaidPhase);
        engine.QueueCommand(SimulationCommand.ConfigureRaidTarget(
            engine.CurrentTick.Next(), sequence: 5, target, radius: 5));
        engine.AdvanceTicks(1);
        Assert.Equal(5, engine.CreateSnapshot().RaidPlan.TargetRadius);

        for (var tick = 0; tick < 2_000 &&
             engine.CreateSnapshot().RaidPhase != GoblinRaidPhase.Ready; tick++)
        {
            engine.AdvanceTicks(1);
        }

        var ready = engine.CreateSnapshot();
        Assert.Equal(GoblinRaidPhase.Ready, ready.RaidPhase);
        Assert.False(ready.HumanVillage.GoblinAttackOrdered);
        Assert.Equal(target, ready.RaidPlan.Target);
        Assert.Equal(5, ready.RaidPlan.TargetRadius);
        Assert.False(ready.RaidPlan.Has(RaidDirective.AutoLaunchWhenReady));

        engine.QueueCommand(SimulationCommand.ConfigureRaidTarget(
            engine.CurrentTick.Next(), sequence: 6, target, radius: 7));
        engine.AdvanceTicks(1);
        var retargeted = engine.CreateSnapshot();
        Assert.Equal(7, retargeted.RaidPlan.TargetRadius);
        Assert.NotEqual(GoblinRaidPhase.Marching, retargeted.RaidPhase);
        Assert.False(retargeted.HumanVillage.GoblinAttackOrdered);
        for (var tick = 0; tick < 2_000 &&
             engine.CreateSnapshot().RaidPhase != GoblinRaidPhase.Ready; tick++)
        {
            engine.AdvanceTicks(1);
        }
        Assert.Equal(GoblinRaidPhase.Ready, engine.CreateSnapshot().RaidPhase);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());

        restored.QueueCommand(SimulationCommand.LaunchRaid(
            restored.CurrentTick.Next(), sequence: 7));
        restored.AdvanceTicks(1);
        Assert.Equal(GoblinRaidPhase.Marching, restored.CreateSnapshot().RaidPhase);
        Assert.True(restored.CreateSnapshot().HumanVillage.GoblinAttackOrdered);
    }

    [Fact]
    public void ConstructedGoblinHutAddsNineShelterPlaces()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0x485554UL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 20,
            initialWoodStock: 20);
        var position = Enumerable.Range(0, engine.Map.Height - 2)
            .SelectMany(y => Enumerable.Range(0, engine.Map.Width - 2)
                .Select(x => new GridPosition(x, y)))
            .Where(engine.World.CanBuildGoblinHut)
            .OrderBy(cell => Math.Abs(cell.X - engine.Map.GoblinSpawn.X) +
                Math.Abs(cell.Y - engine.Map.GoblinSpawn.Y))
            .First();
        var before = engine.CreateSnapshot().TribeNeeds.ShelterCapacity;

        engine.QueueCommand(SimulationCommand.BuildGoblinHut(
            new SimulationTick(1), sequence: 1, position));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine, maximumTicks: 12_000);

        var snapshot = engine.CreateSnapshot();
        var hut = Assert.Single(snapshot.WorldObjects, item =>
            item.Kind == WorldObjectKind.GoblinHut && item.Anchor == position);
        Assert.Equal(9, hut.Parts.Count(part => part.Kind == WorldObjectPartKind.Floor));
        Assert.Equal(before + 9, snapshot.TribeNeeds.ShelterCapacity);
    }

    [Fact]
    public void RaidMemberCanPackDifferentFoodKindFromCamp()
    {
        var engine = CreateEngine();
        var campPosition = FindCampPosition(engine);
        engine.QueueCommand(SimulationCommand.BuildGoblinFieldCamp(
            new SimulationTick(1), sequence: 1, campPosition));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);

        var snapshot = engine.CreateSnapshot();
        var actor = Assert.Single(snapshot.Actors);
        var campStorage = Assert.Single(snapshot.StorageZones, zone => zone.Position == campPosition);
        var foodStack = snapshot.ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Food)
            .OrderBy(stack => stack.Id)
            .First();
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        var actorModel = save["actors"]![0]!.AsObject();
        actorModel["personalFood"] = 1;
        actorModel["personalFoodKind"] = (int)FoodKind.DriedRations;
        actorModel.Remove("personalFoodKinds");
        var stackModel = save["itemStacks"]!.AsArray()
            .Single(item => item!["id"]!.GetValue<ulong>() == foodStack.Id.Value)!.AsObject();
        stackModel["foodKind"] = (int)FoodKind.EdibleRoots;
        stackModel["quantity"] = 10;
        stackModel["locationKind"] = (int)ItemLocationKind.StorageZone;
        stackModel["x"] = campPosition.X;
        stackModel["y"] = campPosition.Y;
        stackModel["z"] = campPosition.Z;
        stackModel["ownerId"] = campStorage.Id.Value;
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);

        var executeAt = engine.CurrentTick.Next();
        engine.QueueCommand(SimulationCommand.ConfigureRaidMember(
            executeAt, sequence: 2, actor.Id, selected: true));
        engine.QueueCommand(SimulationCommand.AttackHumanVillage(executeAt, sequence: 3));
        engine.AdvanceTicks(1);
        for (var tick = 0; tick < 2_000; tick++)
        {
            engine.AdvanceTicks(1);
            if (Assert.Single(engine.CreateSnapshot().Actors).PersonalFood ==
                engine.Definitions.PersonalFoodCapacity)
            {
                break;
            }
        }

        var provisioned = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.Equal(
            [FoodKind.DriedRations, FoodKind.EdibleRoots],
            provisioned.PersonalFoodKinds.ToArray());
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(
            provisioned.PersonalFoodKinds.ToArray(),
            Assert.Single(restored.CreateSnapshot().Actors).PersonalFoodKinds.ToArray());
    }

    private static SimulationEngine CreateEngine() => SimulationEngine.Create(
        new WorldSeed(0x43414D50UL),
        SimulationDefinitions.Foundation,
        initialGoblinCount: 1,
        initialFoodStock: 20,
        initialWoodStock: 10);

    private static GridPosition FindCampPosition(SimulationEngine engine) =>
        Enumerable.Range(0, engine.Map.Height)
            .SelectMany(y => Enumerable.Range(0, engine.Map.Width)
                .Select(x => new GridPosition(x, y)))
            .Where(engine.World.CanBuildGoblinFieldCamp)
            .OrderBy(position => Math.Abs(position.X - engine.Map.GoblinSpawn.X) +
                Math.Abs(position.Y - engine.Map.GoblinSpawn.Y))
            .ThenBy(position => position.Y)
            .ThenBy(position => position.X)
            .First();

    private static void ClearActorJob(JsonObject actor)
    {
        actor["jobKind"] = (int)ActorJobKind.None;
        actor["jobPhase"] = (int)ActorJobPhase.None;
        actor["jobStage"] = (int)ActorJobStage.None;
        actor["jobTargetX"] = 0;
        actor["jobTargetY"] = 0;
        actor["jobTargetZ"] = 0;
        actor["remainingWorkTicks"] = 0;
        actor["sourceStackId"] = 0;
        actor["destinationZoneId"] = 0;
        actor["reservedQuantity"] = 0;
        actor["remainingRoute"] = new JsonArray();
        actor["suspendedJobKind"] = (int)ActorJobKind.None;
        actor["suspendedTargetX"] = 0;
        actor["suspendedTargetY"] = 0;
        actor["suspendedTargetZ"] = 0;
    }
}
