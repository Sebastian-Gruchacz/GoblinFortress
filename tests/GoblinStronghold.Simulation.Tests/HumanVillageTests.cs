using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using System.Text.Json.Nodes;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class HumanVillageTests
{
    [Fact]
    public void NewVillageExposesRaidLootInPhysicalHumanBuildings()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(84591),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0);

        var snapshot = engine.CreateSnapshot();
        var contents = snapshot.VillageLootContainers.SelectMany(item => item.Contents).ToArray();
        Assert.Equal(snapshot.HumanVillage.FoodStock, contents
            .Where(item => item.Resource == ResourceKind.Food).Sum(item => item.Quantity));
        Assert.Equal(snapshot.HumanVillage.WoodStock, contents
            .Where(item => item.Resource == ResourceKind.Wood).Sum(item => item.Quantity));
        Assert.Contains(contents, item =>
            item.Variant == ResourceVariant.EquipmentWoodenSpear);
        Assert.All(snapshot.VillageLootContainers, container =>
        {
            Assert.Contains(snapshot.WorldObjects, worldObject =>
                worldObject.Id == container.StructureId &&
                worldObject.Owner == WorldObjectOwner.HumanVillage);
            Assert.True(engine.World.IsTerrainTraversable(container.Position));
        });
        Assert.Empty(snapshot.ItemStacks);
    }

    [Fact]
    public void VillageStartsWithMaterializedPeopleAndThreeDispatcherCohorts()
    {
        var engine = CreateEngine();
        var village = engine.CreateSnapshot().HumanVillage;

        Assert.Equal(engine.Map.HumanVillage, village.Anchor);
        Assert.Equal(12, village.Population);
        Assert.Equal(village.Population, village.Cohorts.Sum(cohort => cohort.Population));
        Assert.Equal(village.Population, village.Villagers.Count);
        Assert.Equal(village.Population, village.Villagers.Count(villager => villager.Health > 0));
        Assert.Equal(12, village.GrainStock);
        Assert.Equal(3, village.Villagers.Count(villager =>
            villager.Tools.HasFlag(HumanTool.WoodenBucket)));
        Assert.Equal(village.Villagers.Count, village.Villagers.Select(villager =>
            villager.Position).Distinct().Count());
        Assert.All(village.Villagers, villager =>
        {
            Assert.False(string.IsNullOrWhiteSpace(villager.Name));
            Assert.Equal(villager.MaximumHealth, villager.Health);
            Assert.Equal(0, villager.Fatigue);
            Assert.Equal(0, villager.Hunger);
            Assert.Equal(0, villager.Thirst);
            Assert.Equal(0, villager.WorkProgress);
            Assert.True(engine.World.IsSurfaceTraversable(villager.Position));
        });
        Assert.All(village.Fields, field => Assert.Equal(0, field.WorkProgress));
        Assert.Equal(
            [HumanCohortRole.Farmers, HumanCohortRole.Workers, HumanCohortRole.Guards],
            village.Cohorts.Select(cohort => cohort.Role));
        Assert.All(village.Cohorts, cohort => Assert.True(engine.World.IsSurfaceTraversable(cohort.Position)));
    }

    [Fact]
    public void VillageProducesAndConsumesStocksOncePerDay()
    {
        var engine = CreateEngine();

        engine = JumpToNextDayBoundary(engine);
        var beforeDayBoundary = engine.CreateSnapshot().HumanVillage;
        Assert.Equal(48, beforeDayBoundary.FoodStock);
        Assert.Equal(24, beforeDayBoundary.WoodStock);
        Assert.Equal(4, beforeDayBoundary.GoodsStock);

        engine.AdvanceTicks(1);
        var afterDayBoundary = engine.CreateSnapshot().HumanVillage;
        Assert.Equal(48, afterDayBoundary.FoodStock);
        Assert.Equal(36, afterDayBoundary.WaterStock);
        Assert.Equal(24, afterDayBoundary.WoodStock);
        Assert.Equal(4, afterDayBoundary.GoodsStock);
        Assert.All(afterDayBoundary.Villagers, villager =>
        {
            Assert.Equal(100, villager.Hunger);
            Assert.Equal(340, villager.Thirst);
        });
        Assert.All(afterDayBoundary.Fields, field => Assert.Equal(0, field.GrowthDays));
    }

    [Fact]
    public void FarmerTendsPhysicalFieldAndWorkAdvancesGrowthAtDawn()
    {
        var source = CreateEngine();
        var snapshot = source.CreateSnapshot();
        var farmer = snapshot.HumanVillage.Villagers.First(villager =>
            villager.Tools.HasFlag(HumanTool.WoodenHoe));
        var field = snapshot.HumanVillage.Fields[(farmer.Id - 1) %
            snapshot.HumanVillage.Fields.Count];
        var save = JsonNode.Parse(source.Save())!.AsObject();
        save["currentTick"] = 19;
        var village = save["humanVillage"]!.AsObject();
        var savedFarmer = village["villagers"]!.AsArray().Single(item =>
            item!["id"]!.GetValue<int>() == farmer.Id)!.AsObject();
        savedFarmer["x"] = field.Position.X;
        savedFarmer["y"] = field.Position.Y;
        savedFarmer["z"] = field.Position.Z;
        var savedField = village["fields"]!.AsArray().Single(item =>
            item!["id"]!.GetValue<int>() == field.Id)!.AsObject();
        savedField["workProgress"] =
            source.Definitions.HumanVillageEconomy.FieldWorkPerStage -
            farmer.SkillLevel - 1;
        var engine = SimulationEngine.Load(save.ToJsonString(), source.Definitions);

        engine.AdvanceTicks(1);

        var worked = engine.CreateSnapshot().HumanVillage;
        Assert.Equal(HumanCohortTask.WorkFields,
            worked.Villagers.Single(item => item.Id == farmer.Id).Task);
        Assert.Equal(source.Definitions.HumanVillageEconomy.FieldWorkPerStage,
            worked.Fields.Single(item => item.Id == field.Id).WorkProgress);

        engine = JumpToNextDayBoundary(engine);
        engine.AdvanceTicks(1);

        var afterDawn = engine.CreateSnapshot().HumanVillage;
        var advancedFarmer = afterDawn.Villagers.Single(item => item.Id == farmer.Id);
        var advanced = afterDawn.Fields.Single(item =>
            item.Id == field.Id);
        Assert.Equal(HumanCohortTask.WorkFields, advancedFarmer.Task);
        Assert.Equal(1, advanced.GrowthDays);
        Assert.Equal(0, advanced.WorkProgress);
    }

    [Fact]
    public void FarmerConsumesStoredSeedGrainWhenSowingClearedField()
    {
        var source = CreateEngine();
        var snapshot = source.CreateSnapshot();
        var farmer = snapshot.HumanVillage.Villagers.First(villager =>
            villager.Tools.HasFlag(HumanTool.WoodenHoe));
        var field = snapshot.HumanVillage.Fields[(farmer.Id - 1) %
            snapshot.HumanVillage.Fields.Count];
        var save = JsonNode.Parse(source.Save())!.AsObject();
        save["currentTick"] = 19;
        var village = save["humanVillage"]!.AsObject();
        var savedFarmer = village["villagers"]!.AsArray().Single(item =>
            item!["id"]!.GetValue<int>() == farmer.Id)!.AsObject();
        savedFarmer["x"] = field.Position.X;
        savedFarmer["y"] = field.Position.Y;
        savedFarmer["z"] = field.Position.Z;
        var savedField = village["fields"]!.AsArray().Single(item =>
            item!["id"]!.GetValue<int>() == field.Id)!.AsObject();
        savedField["phase"] = (int)HumanFieldPhase.Cleared;
        savedField["growthDays"] = 0;
        savedField["workProgress"] =
            source.Definitions.HumanVillageEconomy.FieldWorkPerStage -
            farmer.SkillLevel - 1;
        var engine = SimulationEngine.Load(save.ToJsonString(), source.Definitions);

        engine.AdvanceTicks(1);
        engine = JumpToNextDayBoundary(engine);
        var beforeDawn = engine.CreateSnapshot().HumanVillage;
        engine.AdvanceTicks(1);

        var afterDawn = engine.CreateSnapshot().HumanVillage;
        var sown = afterDawn.Fields.Single(item => item.Id == field.Id);
        Assert.Equal(HumanFieldPhase.Sown, sown.Phase);
        Assert.Equal(beforeDawn.GrainStock - 1, afterDawn.GrainStock);
        Assert.Equal(beforeDawn.WaterStock - 2, afterDawn.WaterStock);
        Assert.Equal(0, sown.WorkProgress);
    }

    [Fact]
    public void AxeWorkerFellsPhysicalTreeInsteadOfReceivingDailyWoodGrant()
    {
        var initialSave = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        initialSave["humanVillage"]!["woodStock"] = 0;
        var planned = SimulationEngine.Load(
            initialSave.ToJsonString(),
            SimulationDefinitions.Foundation);
        planned.AdvanceTicks(1);
        var plan = planned.CreateSnapshot().HumanVillage;
        var target = Assert.IsType<GridPosition>(plan.TreeFellingTarget);
        var woodyObject = Assert.Single(planned.World.GetWorldObjectsAt(target), item =>
            item.Kind is WorldObjectKind.Tree or WorldObjectKind.DeadTreeStump);
        var worker = plan.Villagers.First(villager =>
            villager.Tools.HasFlag(HumanTool.WoodenAxe));
        var access = planned.Map.GetCardinalNeighbors(target)
            .Where(planned.World.IsSurfaceTraversable)
            .OrderBy(position => Math.Abs(position.X - worker.Position.X) +
                Math.Abs(position.Y - worker.Position.Y))
            .ThenBy(position => position.Y)
            .ThenBy(position => position.X)
            .First();
        var save = JsonNode.Parse(planned.Save())!.AsObject();
        save["currentTick"] = 19;
        var village = save["humanVillage"]!.AsObject();
        village["treeFellingProgress"] =
            planned.Definitions.HumanVillageEconomy.TreeFellingWork -
            worker.SkillLevel - 1;
        var savedWorker = village["villagers"]!.AsArray().Single(item =>
            item!["id"]!.GetValue<int>() == worker.Id)!.AsObject();
        savedWorker["x"] = access.X;
        savedWorker["y"] = access.Y;
        savedWorker["z"] = access.Z;
        var engine = SimulationEngine.Load(save.ToJsonString(), planned.Definitions);
        var restored = SimulationEngine.Load(engine.Save(), engine.Definitions);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());

        engine.AdvanceTicks(1);

        var after = engine.CreateSnapshot().HumanVillage;
        Assert.True(after.WoodStock > 0);
        Assert.Null(after.TreeFellingTarget);
        Assert.Equal(0, after.TreeFellingProgress);
        if (woodyObject.Kind == WorldObjectKind.Tree)
        {
            Assert.Contains(engine.World.GetWorldObjectsAt(target), item =>
                item.Kind == WorldObjectKind.DeadTreeStump);
        }
        else
        {
            Assert.DoesNotContain(engine.World.GetWorldObjectsAt(target), item =>
                item.Kind == WorldObjectKind.DeadTreeStump);
        }
        Assert.Contains(engine.DrainWorldChanges(), change =>
            change.Position == target &&
            change.Kind is WorldChangeKind.TreeFelled or WorldChangeKind.StumpHarvested);
    }

    [Fact]
    public void AxeWorkerCraftsGoodsInsidePhysicalBarn()
    {
        var source = CreateEngine();
        var snapshot = source.CreateSnapshot();
        var worker = snapshot.HumanVillage.Villagers.First(villager =>
            villager.Tools.HasFlag(HumanTool.WoodenAxe));
        var barn = snapshot.WorldObjects.Single(item =>
            item.Kind == WorldObjectKind.HumanBarn);
        var workPositions = barn.GetAbsoluteParts()
            .Where(item => item.Part.Kind == WorldObjectPartKind.Floor &&
                source.World.IsSurfaceTraversable(item.Position))
            .Select(item => item.Position)
            .Distinct()
            .OrderBy(position => position.Y)
            .ThenBy(position => position.X)
            .ToArray();
        var workPosition = workPositions[(worker.Id - 1) % workPositions.Length];
        var save = JsonNode.Parse(source.Save())!.AsObject();
        save["currentTick"] = 19;
        var village = save["humanVillage"]!.AsObject();
        village["goodsStock"] = 3;
        village["woodStock"] = 26;
        village["goodsWorkProgress"] =
            source.Definitions.HumanVillageEconomy.GoodsWorkPerUnit -
            worker.SkillLevel - 1;
        var savedWorker = village["villagers"]!.AsArray().Single(item =>
            item!["id"]!.GetValue<int>() == worker.Id)!.AsObject();
        savedWorker["x"] = workPosition.X;
        savedWorker["y"] = workPosition.Y;
        savedWorker["z"] = workPosition.Z;
        var engine = SimulationEngine.Load(save.ToJsonString(), source.Definitions);
        var restored = SimulationEngine.Load(engine.Save(), engine.Definitions);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());

        engine.AdvanceTicks(1);

        var after = engine.CreateSnapshot().HumanVillage;
        Assert.Equal(HumanCohortTask.CraftGoods,
            after.Villagers.Single(item => item.Id == worker.Id).Task);
        Assert.Equal(4, after.GoodsStock);
        Assert.Equal(24, after.WoodStock);
        Assert.Equal(0, after.GoodsWorkProgress);
    }

    [Fact]
    public void BucketWorkerDrawsWaterAtPhysicalWellAndWorkSettlesAtDawn()
    {
        var source = CreateEngine();
        var snapshot = source.CreateSnapshot();
        var worker = snapshot.HumanVillage.Villagers.First(villager =>
            villager.Tools.HasFlag(HumanTool.WoodenBucket));
        var well = snapshot.WorldObjects.Single(worldObject =>
            worldObject.Kind == WorldObjectKind.HumanWell);
        var wellCells = well.GetAbsoluteParts()
            .Where(item => item.Part.Channel == SpatialOccupancyChannel.Solid)
            .Select(item => item.Position)
            .ToHashSet();
        var accesses = wellCells.SelectMany(source.Map.GetCardinalNeighbors)
            .Where(position => !wellCells.Contains(position) &&
                source.World.IsSurfaceTraversable(position))
            .Distinct()
            .OrderBy(position => position.Y)
            .ThenBy(position => position.X)
            .ToArray();
        var access = accesses[(worker.Id - 1) % accesses.Length];
        var save = JsonNode.Parse(source.Save())!.AsObject();
        save["currentTick"] = 19;
        var village = save["humanVillage"]!.AsObject();
        village["waterStock"] = 0;
        var savedWorker = village["villagers"]!.AsArray().Single(item =>
            item!["id"]!.GetValue<int>() == worker.Id)!.AsObject();
        savedWorker["x"] = access.X;
        savedWorker["y"] = access.Y;
        savedWorker["z"] = access.Z;
        savedWorker["workProgress"] =
            source.Definitions.HumanVillageEconomy.WaterWorkPerUnit -
            worker.SkillLevel - 1;
        var engine = SimulationEngine.Load(save.ToJsonString(), source.Definitions);

        engine.AdvanceTicks(1);

        var working = engine.CreateSnapshot().HumanVillage.Villagers.Single(item =>
            item.Id == worker.Id);
        Assert.Equal(HumanCohortTask.DrawWater, working.Task);
        Assert.Equal(access, working.Position);
        Assert.Equal(source.Definitions.HumanVillageEconomy.WaterWorkPerUnit,
            working.WorkProgress);
        Assert.All(engine.CreateSnapshot().HumanVillage.Villagers.Where(item =>
                item.Role == HumanCohortRole.Workers &&
                !item.Tools.HasFlag(HumanTool.WoodenBucket)),
            item => Assert.Equal(HumanCohortTask.StayNearVillage, item.Task));

        engine = JumpToNextDayBoundary(engine);
        engine.AdvanceTicks(1);

        var settled = engine.CreateSnapshot().HumanVillage;
        Assert.Equal(1, settled.WaterStock);
        Assert.Equal(worker.SkillLevel + 1,
            settled.Villagers.Single(item => item.Id == worker.Id).WorkProgress);
    }

    [Fact]
    public void MissingRationsHarmAndEventuallyKillSpecificVillagers()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        var village = save["humanVillage"]!.AsObject();
        village["foodStock"] = 0;
        village["waterStock"] = 0;
        var victim = village["villagers"]![0]!.AsObject();
        var victimId = victim["id"]!.GetValue<int>();
        victim["health"] = 1;
        victim["hunger"] = 900;
        victim["thirst"] = 660;
        var engine = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);

        engine = JumpToNextDayBoundary(engine);
        engine.AdvanceTicks(1);

        var after = engine.CreateSnapshot().HumanVillage;
        Assert.Equal(11, after.Population);
        var dead = after.Villagers.Single(villager => villager.Id == victimId);
        Assert.Equal(0, dead.Health);
        Assert.Equal(dead.MaximumNeed, dead.Hunger);
        Assert.Equal(dead.MaximumNeed, dead.Thirst);
        Assert.Equal(after.Population, after.Cohorts.Sum(cohort => cohort.Population));
        Assert.Contains(engine.DrainEvents(), simulationEvent =>
            simulationEvent.Kind == SimulationEventKind.HumanDied &&
            simulationEvent.Target.Value ==
                (0x8000000000000000UL | (uint)victimId));
    }

    [Fact]
    public void TiredVillagerReturnsToVillageAndRestsAtNight()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        save["currentTick"] = 7_199;
        var villager = save["humanVillage"]!["villagers"]![0]!.AsObject();
        var villagerId = villager["id"]!.GetValue<int>();
        villager["fatigue"] = 800;
        var engine = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);

        engine.AdvanceTicks(1);

        var resting = engine.CreateSnapshot().HumanVillage.Villagers.Single(item =>
            item.Id == villagerId);
        Assert.Equal(HumanCohortTask.StayNearVillage, resting.Task);
        Assert.Equal(795, resting.Fatigue);
    }

    [Fact]
    public void StorehouseRequiresAPlannedSiteAndPhysicalWorkerProgress()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        var village = save["humanVillage"]!.AsObject();
        village["foodStock"] = 180;
        village["woodStock"] = 24;
        village["waterStock"] = 120;
        var engine = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);

        engine.AdvanceTicks(1);

        var planned = engine.CreateSnapshot().HumanVillage;
        var site = Assert.IsType<GridPosition>(planned.StorehouseSite);
        Assert.Equal(0, planned.StorehouseCount);
        Assert.Equal(0, planned.StorehouseWorkProgress);
        Assert.DoesNotContain(engine.CreateSnapshot().WorldObjects,
            item => item.Kind == WorldObjectKind.HumanStorehouse);

        var worker = planned.Villagers.First(item =>
            item.Role == HumanCohortRole.Workers && item.Health > 0);
        var offset = (worker.Id - 1) % 9;
        var workPosition = new GridPosition(
            site.X + offset % 3,
            site.Y + offset / 3,
            site.Z);
        save = JsonNode.Parse(engine.Save())!.AsObject();
        save["currentTick"] = 19;
        village = save["humanVillage"]!.AsObject();
        village["storehouseWorkProgress"] =
            engine.Definitions.HumanVillageEconomy.StorehouseWork - worker.SkillLevel - 1;
        var savedWorker = village["villagers"]!.AsArray().Single(item =>
            item!["id"]!.GetValue<int>() == worker.Id)!.AsObject();
        savedWorker["x"] = workPosition.X;
        savedWorker["y"] = workPosition.Y;
        savedWorker["z"] = workPosition.Z;
        engine = SimulationEngine.Load(save.ToJsonString(), engine.Definitions);
        var restored = SimulationEngine.Load(engine.Save(), engine.Definitions);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());

        engine.AdvanceTicks(1);

        var completed = engine.CreateSnapshot();
        Assert.Equal(1, completed.HumanVillage.StorehouseCount);
        Assert.Equal(0, completed.HumanVillage.WoodStock);
        Assert.Null(completed.HumanVillage.StorehouseSite);
        Assert.Equal(0, completed.HumanVillage.StorehouseWorkProgress);
        var storehouse = Assert.Single(completed.WorldObjects,
            item => item.Kind == WorldObjectKind.HumanStorehouse);
        Assert.Equal(site, storehouse.Anchor);
        Assert.Contains(engine.DrainWorldChanges(), change =>
            change.Kind == WorldChangeKind.StructureBuilt && change.Position == site);
    }

    [Fact]
    public void DispatcherClearsEnoughFieldsForPopulationAndCropsNeedHalfAYear()
    {
        var engine = CreateEngine();
        var initial = engine.CreateSnapshot().HumanVillage;
        Assert.Equal(4, initial.Fields.Count);
        Assert.Equal(4, initial.PlannedFieldCount);
        Assert.All(initial.Fields, field => Assert.Null(engine.World.GetPlantPatch(field.Position)));

        engine = AdvanceVillageDays(engine, 19);
        var expanded = engine.CreateSnapshot().HumanVillage;
        Assert.Equal(expanded.PlannedFieldCount, expanded.Fields.Count);
        Assert.Equal(initial.WoodStock, expanded.WoodStock);
        Assert.DoesNotContain(expanded.Fields, field => field.Phase == HumanFieldPhase.Ripe);
        Assert.All(expanded.Fields, field => Assert.Null(engine.World.GetPlantPatch(field.Position)));

        engine = AdvanceVillageDays(engine, 1);
        Assert.Contains(engine.CreateSnapshot().HumanVillage.Fields,
            field => field.Phase == HumanFieldPhase.Ripe);

        engine = AdvanceVillageDays(engine, 1);
        for (var ticks = 0;
             ticks < 12_000 && engine.CreateSnapshot().HumanVillage.StorehouseCount == 0;
             ticks += 20)
        {
            engine.AdvanceTicks(20);
        }
        var harvested = engine.CreateSnapshot();
        Assert.Equal(1, harvested.HumanVillage.StorehouseCount);
        var storehouse = Assert.Single(harvested.WorldObjects,
            item => item.Kind == WorldObjectKind.HumanStorehouse);
        Assert.Equal(WorldObjectOwner.HumanVillage, storehouse.Owner);
        Assert.Equal(9, storehouse.Parts.Count(item => item.Kind == WorldObjectPartKind.Floor));
        Assert.Contains(storehouse.Parts, item => item.Kind == WorldObjectPartKind.Door);
        Assert.Equal(480, harvested.HumanVillage.FoodCapacity);
        var door = storehouse.GetAbsoluteParts()
            .Single(item => item.Part.Kind == WorldObjectPartKind.Door).Position;
        Assert.True(engine.World.HasSurfacePath(engine.Map.HumanVillage, door));

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Single(restored.CreateSnapshot().WorldObjects,
            item => item.Kind == WorldObjectKind.HumanStorehouse);
    }

    [Fact]
    public void CohortsStayTraversableAndNearTheirVillage()
    {
        var engine = CreateEngine();

        engine.AdvanceTicks(800);

        var cohorts = engine.CreateSnapshot().HumanVillage.Cohorts;
        Assert.Equal(cohorts.Count, cohorts.Select(cohort => cohort.Position).Distinct().Count());
        Assert.All(cohorts, cohort =>
        {
            Assert.True(engine.World.IsSurfaceTraversable(cohort.Position));
            Assert.InRange(
                Math.Abs(cohort.Position.X - engine.Map.HumanVillage.X) +
                Math.Abs(cohort.Position.Y - engine.Map.HumanVillage.Y),
                0,
                engine.Definitions.HumanVillageActivityRadius);
        });
        var villagers = engine.CreateSnapshot().HumanVillage.Villagers
            .Where(villager => villager.Health > 0).ToArray();
        Assert.Equal(villagers.Length, villagers.Select(villager => villager.Position)
            .Distinct().Count());
        Assert.All(villagers, villager => Assert.InRange(
            Math.Abs(villager.Position.X - engine.Map.HumanVillage.X) +
            Math.Abs(villager.Position.Y - engine.Map.HumanVillage.Y),
            0,
            engine.Definitions.HumanVillageActivityRadius + 4));
    }

    [Fact]
    public void SaveLoadPreservesVillageAndItsFuture()
    {
        var engine = CreateEngine();
        engine.AdvanceTicks(337);

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(engine.CreateSnapshot().HumanVillage.Cohorts, restored.CreateSnapshot().HumanVillage.Cohorts);
        Assert.Equal(
            engine.CreateSnapshot().HumanVillage.Villagers,
            restored.CreateSnapshot().HumanVillage.Villagers);

        engine.AdvanceTicks(500);
        restored.AdvanceTicks(500);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(
            engine.CreateSnapshot().HumanVillage.Cohorts,
            restored.CreateSnapshot().HumanVillage.Cohorts);
        Assert.Equal(
            engine.CreateSnapshot().HumanVillage.Villagers,
            restored.CreateSnapshot().HumanVillage.Villagers);
    }

    private static SimulationEngine CreateEngine()
    {
        var seed = new WorldSeed(0x48554D414EUL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        return SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 0);
    }

    private static SimulationEngine AdvanceVillageDays(SimulationEngine engine, int days)
    {
        for (var day = 0; day < days; day++)
        {
            var save = JsonNode.Parse(engine.Save())!.AsObject();
            var bucketWorkerIds = engine.CreateSnapshot().HumanVillage.Villagers
                .Where(villager => villager.Health > 0 &&
                    villager.Tools.HasFlag(HumanTool.WoodenBucket))
                .Select(villager => villager.Id)
                .ToHashSet();
            foreach (var villager in save["humanVillage"]!["villagers"]!.AsArray()
                         .Where(item => bucketWorkerIds.Contains(item!["id"]!.GetValue<int>())))
            {
                villager!["workProgress"] =
                    engine.Definitions.HumanVillageEconomy.WaterWorkPerUnit * 6;
            }
            foreach (var field in save["humanVillage"]!["fields"]!.AsArray())
            {
                field!["workProgress"] =
                    engine.Definitions.HumanVillageEconomy.FieldWorkPerStage;
            }
            engine = SimulationEngine.Load(save.ToJsonString(), engine.Definitions);
            engine = JumpToNextDayBoundary(engine);
            engine.AdvanceTicks(1);
        }
        return engine;
    }

    private static SimulationEngine JumpToNextDayBoundary(SimulationEngine engine)
    {
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        var nextBoundary = SimulationCalendar.NextDayStart(
            engine.CurrentTick,
            engine.Definitions.Clock);
        save["currentTick"] = nextBoundary.Value - 1;
        return SimulationEngine.Load(save.ToJsonString(), engine.Definitions);
    }
}
