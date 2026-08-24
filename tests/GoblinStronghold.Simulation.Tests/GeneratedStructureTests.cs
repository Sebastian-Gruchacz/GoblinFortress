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
        Assert.InRange(
            snapshot.WorldObjects.Count(item => item.Kind == WorldObjectKind.GoblinHut),
            2,
            3);
    }

    [Fact]
    public void BuildingsOccupyManyColumnsAndSeveralHeightLayers()
    {
        var snapshot = CreateEngine(new WorldSeed(456)).CreateSnapshot();

        foreach (var building in snapshot.WorldObjects.Where(
                     item => item.Kind != WorldObjectKind.HumanWell))
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

            Assert.InRange(engine.World.WorldObjectCount, 6, 7);
        }
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
