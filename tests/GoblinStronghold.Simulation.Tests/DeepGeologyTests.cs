using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Equipment;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class DeepGeologyTests
{
    [Fact]
    public void CurrentGeneratorCarvesAConnectedSlopedCavernAcrossFourLevels()
    {
        var map = MaterializeToDepth(new WorldSeed(0x534C4F5045434156UL), depth: 7);
        var passages = map.VerticalPassages
            .Where(passage =>
                passage.Kind == VerticalPassageKind.NaturalRamp &&
                passage.Upper.Z <= -4 && passage.Lower.Z >= -7)
            .OrderByDescending(passage => passage.Upper.Z)
            .ToArray();

        Assert.Equal(3, passages.Length);
        Assert.Equal([-4, -5, -6], passages.Select(passage => passage.Upper.Z));
        foreach (var passage in passages)
        {
            Assert.Equal(CaveCellKind.Ramp, map.GetCaveCell(passage.Upper).Kind);
            Assert.True(map.GetCaveCell(passage.Lower).IsOpen);
            Assert.Equal(passage.Upper.X, passage.Lower.X);
            Assert.Equal(passage.Upper.Y, passage.Lower.Y);
        }
    }

    [Fact]
    public void ReachingLavaGalleryMaterializesItsWholeMultiLevelLayout()
    {
        var map = MaterializeToDepth(new WorldSeed(0x4C41564147414C4CUL), depth: 12);

        Assert.Equal(15, map.CaveLevelCount);
        var passages = map.VerticalPassages.Where(passage =>
            passage.Kind == VerticalPassageKind.NaturalRamp &&
            passage.Upper.Z <= -12 && passage.Lower.Z >= -15);
        Assert.Equal(3, passages.Count());
        Assert.True(CountLava(map, depth: 14) > 0);
        Assert.True(CountLava(map, depth: 15) > 0);
    }

    [Fact]
    public void VersionFourteenRetainsLayerByLayerCaveMaterialization()
    {
        var map = SwampMapGenerator.Generate(
            new WorldSeed(0x56313443415645UL),
            width: 64,
            height: 64,
            generatorVersion: 14);

        map.MaterializeCaveLevel(-3);

        Assert.Equal(3, map.CaveLevelCount);
        Assert.DoesNotContain(map.VerticalPassages, passage => passage.Upper.Z <= -3);
    }

    [Fact]
    public void EveryDeepMiningLevelContainsNaturalCaves()
    {
        var map = MaterializeToDepth(new WorldSeed(0x43415645524E53UL), depth: 11);

        for (var depth = 3; depth <= 11; depth++)
        {
            var caveFloorCount = ReadLevel(map, depth).Count(cell =>
                cell.Kind == CaveCellKind.Floor && cell.Fluid == CellFluidKind.None);

            Assert.InRange(caveFloorCount, map.CellCount / 50, map.CellCount * 3 / 5);
        }
    }

    [Fact]
    public void MiddleDepthsContainIronWithoutBeingDominatedByCoal()
    {
        var map = MaterializeToDepth(new WorldSeed(0x49524F4E5645494EUL), depth: 10);
        var cells = ReadLevels(map, 6, 10)
            .Where(cell => cell.Kind == CaveCellKind.SolidRock)
            .ToArray();
        var iron = cells.Count(cell => cell.Deposit == MineralDepositKind.IronOre);
        var coal = cells.Count(cell => cell.Deposit == MineralDepositKind.Coal);

        Assert.True(iron > 0);
        Assert.True(coal <= iron * 3, $"iron={iron}, coal={coal}");
    }

    [Fact]
    public void HistoricalExcavationMayOverlapNewNaturalCaveFloor()
    {
        var map = MaterializeToDepth(new WorldSeed(0x4F4C4454554E4E45UL), depth: 3);
        var world = WorldMapState.CreateInitial(map);
        var naturalFloor = FindCell(
            map,
            depth: 3,
            cell => cell.Kind == CaveCellKind.Floor && cell.Fluid == CellFluidKind.None);

        var restored = WorldMapState.Restore(
            map,
            world.Version,
            world.CreatePlantSnapshot(),
            world.CreateWorldObjectSnapshot(),
            excavatedCaveCells: [naturalFloor]);

        Assert.Contains(naturalFloor, restored.ExcavatedCaveCells);
        Assert.True(restored.IsTerrainTraversable(naturalFloor));
    }

    [Fact]
    public void LavaBeginsAtTwelveAndBroadensAtSixteen()
    {
        var map = MaterializeToDepth(new WorldSeed(0x4C41564144454550UL), depth: 16);
        var world = WorldMapState.CreateInitial(map);

        Assert.Equal(0, CountLava(map, depth: 11));
        var earlyLava = Enumerable.Range(12, 4).Sum(depth => CountLava(map, depth));
        var infernalLava = CountLava(map, depth: 16);

        Assert.True(earlyLava > 0);
        Assert.True(infernalLava > CountLava(map, depth: 12));
        var lava = FindCell(map, depth: 16, cell => cell.Fluid == CellFluidKind.Lava);
        Assert.True(map.TryGetInitialGeometry(lava, out var geometry));
        Assert.Equal(CellFluidKind.Lava, geometry.Fluid);
        Assert.False(geometry.IsOccupiable);
        Assert.False(world.CanBuildWalkway([lava]));
        Assert.True(world.CanBuildBasaltWalkway([lava]));

        world.BuildBasaltWalkway([lava], SimulationTick.Zero);

        Assert.True(world.IsTerrainTraversable(lava));
        Assert.Contains(world.CreateWorldObjectSnapshot(), item =>
            item.Kind == WorldObjectKind.BasaltWalkway &&
            item.GetAbsoluteParts().Single().Position == lava);
    }

    [Fact]
    public void BasaltWalkwayBlueprintUsesMinedBasaltAndSkilledBuilder()
    {
        var start = new GridPosition(4, 5, -12);
        var end = start with { X = start.X + 2 };

        var site = ConstructionBlueprintCatalog.CreateSite(
            new EntityId(1),
            ConstructionKind.BasaltWalkway,
            start,
            end);

        Assert.Equal(ResourceKind.Stone, site.RequiredResource);
        Assert.Equal(ResourceVariant.Basalt, site.RequiredVariant);
        Assert.Equal(3, site.RequiredQuantity);
        Assert.Equal(GoblinSkill.Building, site.Capabilities.RequiredSkills);
        Assert.Equal(2, site.Capabilities.MinimumBuildingLevel);
        Assert.Equal(PersonalEquipment.None, site.Capabilities.RequiredEquipment);
        Assert.Equal(ToolFunction.Construction, site.Capabilities.RequiredToolFunction);
        Assert.Equal(1, site.Capabilities.MinimumToolLevel);
    }

    [Fact]
    public void BasaltWalkwayAcrossLavaSurvivesSaveLoad()
    {
        var seed = new WorldSeed(0x424153414C545341UL);
        var map = SwampMapGenerator.Generate(seed, width: 64, height: 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 30);
        while (map.CaveLevelCount < 16)
        {
            map.MaterializeCaveLevel(map.DeepestCaveLevel - 1);
        }
        var lava = FindCell(map, depth: 16, cell => cell.Fluid == CellFluidKind.Lava);

        engine.World.BuildBasaltWalkway([lava], engine.CurrentTick);
        var restored = SimulationEngine.Load(engine.Save(), engine.Definitions);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.True(restored.World.IsTerrainTraversable(lava));
        Assert.Contains(restored.World.CreateWorldObjectSnapshot(), item =>
            item.Kind == WorldObjectKind.BasaltWalkway &&
            item.GetAbsoluteParts().Single().Position == lava);
    }

    [Fact]
    public void RevealedLavaStopsOnlyItsMiningSegmentAndRaisesWarning()
    {
        var seed = new WorldSeed(0x4C4156415741524EUL);
        var map = MaterializeToDepth(seed, depth: 12);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 30);
        var lava =
            (from y in Enumerable.Range(0, map.Height)
             from x in Enumerable.Range(0, map.Width)
             let position = new GridPosition(x, y, -12)
             where map.GetCaveCell(position).Fluid == CellFluidKind.Lava &&
                   engine.World.GetCardinalWorldNeighbors(position)
                       .Any(engine.World.IsTerrainTraversable)
             select position).First();
        var dryRock = FindCell(map, depth: 12, cell =>
            cell.Kind == CaveCellKind.SolidRock && cell.Fluid == CellFluidKind.None);

        engine.ApplyCommandImmediately(SimulationCommand.DesignateRockMining(
            engine.CurrentTick,
            sequence: 1,
            lava,
            lava));
        engine.ApplyCommandImmediately(SimulationCommand.DesignateRockMining(
            engine.CurrentTick,
            sequence: 2,
            dryRock,
            dryRock));
        Assert.Equal(2, engine.CreateSnapshot().WorkDesignations.Count);

        engine.Visibility.Reveal([lava], radius: 1);
        engine.AdvanceTicks(1);

        var remaining = Assert.Single(engine.CreateSnapshot().WorkDesignations);
        Assert.Equal(dryRock, remaining.Target);
        var warning = Assert.Single(engine.DrainEvents().Where(item =>
            item.Kind == SimulationEventKind.MiningHazardDiscovered));
        Assert.Equal((int)CellFluidKind.Lava, warning.Amount);
    }

    [Fact]
    public void DeepLayersContainHardRockPreciousOreAndGemsDeterministically()
    {
        var seed = new WorldSeed(0x47454F4C4F4759UL);
        var first = MaterializeToDepth(seed, depth: 20);
        var second = MaterializeToDepth(seed, depth: 20);
        var deepCells = ReadLevels(first, 12, 20).ToArray();

        Assert.Contains(deepCells, cell => cell.Rock == RockKind.Basalt);
        Assert.Contains(deepCells, cell => cell.Rock == RockKind.Obsidian);
        Assert.Contains(deepCells, cell => cell.Deposit == MineralDepositKind.CopperOre);
        Assert.Contains(deepCells, cell => cell.Deposit == MineralDepositKind.SilverOre);
        Assert.Contains(deepCells, cell => cell.Deposit == MineralDepositKind.GoldOre);
        Assert.Contains(deepCells, cell => cell.Deposit is MineralDepositKind.Ruby or
            MineralDepositKind.Emerald or MineralDepositKind.Diamond);
        Assert.Equal(deepCells, ReadLevels(second, 12, 20));
    }

    [Fact]
    public void HardRockRequiresExperienceAndObsidianRequiresReinforcedPickaxe()
    {
        var basalt = new CaveCell(RockKind.Basalt, CaveCellKind.SolidRock);
        var obsidian = new CaveCell(RockKind.Obsidian, CaveCellKind.SolidRock);

        Assert.False(MiningCapabilityPolicy.CanMine(
            basalt, PersonalEquipment.PrimitivePickaxe, buildingExperience: 0));
        Assert.True(MiningCapabilityPolicy.CanMine(
            basalt, PersonalEquipment.PrimitivePickaxe, buildingExperience: 100));
        Assert.False(MiningCapabilityPolicy.CanMine(
            obsidian, PersonalEquipment.PrimitivePickaxe, buildingExperience: 500));
        Assert.True(MiningCapabilityPolicy.CanMine(
            obsidian, PersonalEquipment.ReinforcedPickaxe, buildingExperience: 200));
        Assert.True(MiningCapabilityPolicy.WorkMultiplier(RockKind.Obsidian) >
            MiningCapabilityPolicy.WorkMultiplier(RockKind.Basalt));
    }

    [Fact]
    public void DeepPredatorsMaterializeWithReachedLevelsAndPersist()
    {
        var seed = new WorldSeed(0x4445455054485245UL);
        var map = SwampMapGenerator.Generate(seed, width: 48, height: 48);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 30);
        var rampOrigin = FindRampOrigin(engine, map.DeepestCaveLevel);

        while (map.CaveLevelCount < 16)
        {
            Assert.True(engine.World.TryCarveVerticalRamp(
                rampOrigin,
                carveDown: true,
                SimulationTick.Zero,
                out _,
                out _));
            var lower = rampOrigin with { Z = rampOrigin.Z - 1 };
            if (map.CaveLevelCount < 16)
            {
                rampOrigin = engine.World.GetCardinalWorldNeighbors(lower)
                    .First(candidate =>
                    {
                        var below = candidate with { Z = candidate.Z - 1 };
                        return engine.World.CanExcavateRock(candidate) &&
                            (map.IsCavePosition(below)
                                ? map.GetCaveCell(below)
                                : map.GetNextCaveLevelCell(below)).Kind == CaveCellKind.SolidRock;
                    });
                Assert.True(engine.World.TryExcavateRock(
                    rampOrigin,
                    SimulationTick.Zero,
                    out _,
                    out _,
                    out _));
            }
        }

        engine.AdvanceTicks(SimulationEngine.AnimalUpdateIntervalTicks);
        var predators = engine.CreateSnapshot().Animals.Where(animal =>
            animal.Kind is AnimalKind.DeepCrawler or AnimalKind.MagmaWyrm).ToArray();
        Assert.Contains(predators, animal =>
            animal.Kind == AnimalKind.DeepCrawler && animal.Position.Z == -12);
        Assert.Contains(predators, animal =>
            animal.Kind == AnimalKind.MagmaWyrm && animal.Position.Z == -16);
        Assert.True(
            AnimalCombatPolicy.GetAttackDamage(AnimalKind.MagmaWyrm, new GridPosition(0, 0, -16)) >
            AnimalCombatPolicy.GetAttackDamage(AnimalKind.DeepCrawler, new GridPosition(0, 0, -12)));

        var restored = SimulationEngine.Load(engine.Save(), engine.Definitions);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(predators.Length, restored.CreateSnapshot().Animals.Count(animal =>
            animal.Kind is AnimalKind.DeepCrawler or AnimalKind.MagmaWyrm));
    }

    private static GridPosition FindRampOrigin(SimulationEngine engine, int level) =>
        (from y in Enumerable.Range(0, engine.Map.Height)
         from x in Enumerable.Range(0, engine.Map.Width)
         let candidate = new GridPosition(x, y, level)
         where engine.World.CanCarveRampDown(candidate)
         select candidate).First();

    private static GeneratedMap MaterializeToDepth(WorldSeed seed, int depth)
    {
        var map = SwampMapGenerator.Generate(seed, width: 64, height: 64);
        while (map.CaveLevelCount < depth)
        {
            map.MaterializeCaveLevel(map.DeepestCaveLevel - 1);
        }
        return map;
    }

    private static JsonObject ToJsonPosition(GridPosition position) => new()
    {
        ["x"] = position.X,
        ["y"] = position.Y,
        ["z"] = position.Z,
    };

    private static int CountLava(GeneratedMap map, int depth) =>
        ReadLevel(map, depth).Count(cell => cell.Fluid == CellFluidKind.Lava);

    private static GridPosition FindCell(
        GeneratedMap map,
        int depth,
        Func<CaveCell, bool> predicate) =>
        (from y in Enumerable.Range(0, map.Height)
         from x in Enumerable.Range(0, map.Width)
         let position = new GridPosition(x, y, -depth)
         where predicate(map.GetCaveCell(position))
         select position).First();

    private static IEnumerable<CaveCell> ReadLevels(
        GeneratedMap map,
        int firstDepth,
        int lastDepth) =>
        Enumerable.Range(firstDepth, lastDepth - firstDepth + 1)
            .SelectMany(depth => ReadLevel(map, depth));

    private static IEnumerable<CaveCell> ReadLevel(GeneratedMap map, int depth) =>
        from y in Enumerable.Range(0, map.Height)
        from x in Enumerable.Range(0, map.Width)
        select map.GetCaveCell(new GridPosition(x, y, -depth));
}
