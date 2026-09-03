using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Map;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class GeneratedStructureTests
{
    [Fact]
    public void InitialSettlementsContainMinimalAuthoredBuildingSet()
    {
        var snapshot = CreateEngine(new WorldSeed(123)).CreateSnapshot();

        Assert.Equal(
            2,
            snapshot.WorldObjects.Count(item => item.Kind == WorldObjectKind.HumanCottage));
        Assert.Single(snapshot.WorldObjects, item => item.Kind == WorldObjectKind.HumanBarn);
        Assert.Single(snapshot.WorldObjects, item => item.Kind == WorldObjectKind.HumanWell);
        Assert.Single(snapshot.WorldObjects, item => item.Kind == WorldObjectKind.GoblinRuin);
        Assert.Single(snapshot.WorldObjects, item => item.Kind == WorldObjectKind.GoblinCompost);
        Assert.Single(snapshot.WorldObjects, item =>
            item.Kind == WorldObjectKind.PrimitiveWorkshop &&
            item.Owner == WorldObjectOwner.GoblinTribe);
        Assert.Equal(2, snapshot.WorldObjects.Count(item =>
            item.Kind == WorldObjectKind.WallTorch &&
            item.Owner == WorldObjectOwner.GoblinTribe));
        Assert.Equal(4, snapshot.WorldObjects.Count(item =>
            item.Kind == WorldObjectKind.ReedSleepingMat &&
            item.Owner == WorldObjectOwner.GoblinTribe));
        Assert.Single(snapshot.WorldObjects, item =>
            item.Kind == WorldObjectKind.StandingTorch &&
            item.Owner == WorldObjectOwner.GoblinTribe);
        Assert.Single(snapshot.WorldObjects, item =>
            item.Kind == WorldObjectKind.CookingFire &&
            item.Owner == WorldObjectOwner.GoblinTribe);
        Assert.DoesNotContain(snapshot.WorldObjects, item =>
            item.Kind == WorldObjectKind.GoblinHut);
    }

    [Fact]
    public void StarterRuinSleepingBaysBlockSkyExposure()
    {
        var engine = CreateEngine(new WorldSeed(123));
        var sleepingMats = engine.World.CreateWorldObjectSnapshot()
            .Where(item => item.Kind == WorldObjectKind.ReedSleepingMat)
            .Select(item => item.Anchor)
            .ToArray();

        Assert.Equal(4, sleepingMats.Length);
        Assert.All(sleepingMats, position =>
            Assert.False(engine.World.IsOpenToSky(position)));
    }

    [Fact]
    public void HumanVillageStructuresStayOnDryFlatGroundAcrossSeeds()
    {
        for (ulong seed = 0; seed < 64; seed++)
        {
            var engine = CreateEngine(new WorldSeed(seed));
            var structures = engine.World.CreateWorldObjectSnapshot().Where(item =>
                item.Owner == WorldObjectOwner.HumanVillage).ToArray();

            Assert.NotEmpty(structures);
            foreach (var structure in structures)
            {
                var footprint = structure.GetAbsoluteParts()
                    .Select(item => item.Position with { Z = 0 })
                    .Distinct()
                    .ToArray();
                var levels = footprint.Select(position =>
                    engine.Map.GetCell(position).SurfaceLevel).Distinct().ToArray();
                Assert.Single(levels);
                Assert.All(footprint, position =>
                {
                    var cell = engine.Map.GetCell(position);
                    Assert.Equal(TerrainKind.SolidGround, cell.Terrain);
                    Assert.Equal(TerrainRampDirection.None, cell.RampDirection);
                });
            }
        }
    }

    [Fact]
    public void BuildingsOccupyManyColumnsAndSeveralHeightLayers()
    {
        var snapshot = CreateEngine(new WorldSeed(456)).CreateSnapshot();

        foreach (var building in snapshot.WorldObjects.Where(item => item.Kind is
                     WorldObjectKind.GoblinRuin or WorldObjectKind.HumanCottage or
                     WorldObjectKind.HumanBarn))
        {
            var absoluteParts = building.GetAbsoluteParts().ToArray();
            Assert.True(
                absoluteParts.Select(item => (item.Position.X, item.Position.Y)).Distinct().Count() > 1);
            Assert.Contains(absoluteParts, item => item.Position.Z == 0);
            Assert.Contains(absoluteParts, item => item.Position.Z == 1);
        }

        var well = Assert.Single(
            snapshot.WorldObjects,
            item => item.Kind == WorldObjectKind.HumanWell);
        Assert.Contains(well.GetAbsoluteParts(), item => item.Position.Z == -1);
    }

    [Fact]
    public void OccupancyChannelsAllowLayersButRejectNoGeneratedConflicts()
    {
        var snapshot = CreateEngine(new WorldSeed(789)).CreateSnapshot();
        var claims = snapshot.WorldObjects
            .SelectMany(worldObject => worldObject.GetAbsoluteParts().Select(item =>
                (worldObject.Id, item.Position, item.Part.Channel)))
            .ToArray();

        Assert.Equal(
            claims.Length,
            claims.Select(item => (item.Position, item.Channel)).Distinct().Count());
        Assert.Contains(
            claims.GroupBy(item => (item.Position.X, item.Position.Y))
                .Select(group => group.Select(item => item.Position.Z).Distinct().Count()),
            layerCount => layerCount > 1);
    }

    [Fact]
    public void WallsBlockSurfaceNavigationButDoorsRemainTraversable()
    {
        var engine = CreateEngine(new WorldSeed(1_234));
        var cottage = engine.World.CreateWorldObjectSnapshot()
            .First(item => item.Kind == WorldObjectKind.HumanCottage);
        var wall = cottage.GetAbsoluteParts().First(item => item.Part.Kind == WorldObjectPartKind.Wall);
        var door = cottage.GetAbsoluteParts().First(item => item.Part.Kind == WorldObjectPartKind.Door);

        Assert.False(engine.World.IsSurfaceTraversable(wall.Position));
        Assert.True(engine.World.IsSurfaceTraversable(door.Position));
        Assert.Contains(cottage, engine.World.GetWorldObjectsAt(wall.Position));
        Assert.Contains(cottage, engine.World.GetWorldObjectsAt(
            door.Position with { Z = 1 }));
    }

    [Fact]
    public void StructureGenerationIsDeterministicAndSurvivesSaveLoad()
    {
        var first = CreateEngine(new WorldSeed(5_678));
        var second = CreateEngine(new WorldSeed(5_678));

        Assert.Equal(GetSignature(first), GetSignature(second));

        var restored = SimulationEngine.Load(first.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(first.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(GetSignature(first), GetSignature(restored));
    }

    [Fact]
    public void MinimalStructuresFitEverySampledMinimumMap()
    {
        for (ulong seed = 0; seed < 64; seed++)
        {
            var map = SwampMapGenerator.Generate(new WorldSeed(seed), width: 16, height: 16);
            var engine = SimulationEngine.Create(
                new WorldSeed(seed),
                SimulationDefinitions.Foundation,
                map,
                initialGoblinCount: 0,
                initialFoodStock: 0);

            Assert.InRange(
                engine.World.CreateWorldObjectSnapshot().Count(item =>
                    item.Owner is WorldObjectOwner.GoblinTribe or WorldObjectOwner.HumanVillage),
                9,
                15);
        }
    }

    [Fact]
    public void LegacyGeneratorKeepsTheFormerGoblinHuts()
    {
        var seed = new WorldSeed(0x4C4547414359UL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32, generatorVersion: 15);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 0,
            initialFoodStock: 0);

        Assert.InRange(engine.World.CountWorldObjects(
            WorldObjectKind.GoblinHut,
            WorldObjectOwner.GoblinTribe), 2, 3);
        Assert.Equal(0, engine.World.CountWorldObjects(
            WorldObjectKind.GoblinRuin,
            WorldObjectOwner.GoblinTribe));
    }

    private static SimulationEngine CreateEngine(WorldSeed seed) => SimulationEngine.Create(
        seed,
        SimulationDefinitions.Foundation,
        initialGoblinCount: 1,
        initialFoodStock: 0);

    private static string GetSignature(SimulationEngine engine) => string.Join(
        '|',
        engine.World.CreateWorldObjectSnapshot().Select(worldObject => string.Join(
            ';',
            worldObject.Id.Value,
            worldObject.Kind,
            worldObject.Owner,
            worldObject.Anchor,
            worldObject.Orientation,
            string.Join(',', worldObject.Parts.Select(part =>
                $"{part.RelativePosition}:{part.Channel}:{part.Kind}")))));
}
