using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Map.Generation;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class CaveFloraGeneratorTests
{
    [Fact]
    public void FirstThreeCaveLevelsReceiveDeterministicFloraOnDryFloors()
    {
        var first = SwampMapGenerator.Generate(
            new WorldSeed(0x43415645464C4F52UL),
            96,
            96);
        var second = SwampMapGenerator.Generate(
            new WorldSeed(0x43415645464C4F52UL),
            96,
            96);
        first.MaterializeCaveLevel(-3);
        second.MaterializeCaveLevel(-3);

        var firstFlora = Collect(first);
        var secondFlora = Collect(second);

        Assert.Equal(firstFlora, secondFlora);
        Assert.All(Enumerable.Range(1, 3), depth =>
            Assert.Contains(firstFlora, flora => flora.Position.Z == -depth));
        Assert.All(Enum.GetValues<CaveFloraKind>(), kind =>
            Assert.Contains(firstFlora, flora => flora.Kind == kind));
        Assert.All(firstFlora, flora => Assert.InRange(flora.Variant, (byte)0, (byte)3));
        Assert.True(firstFlora.Select(flora => flora.Variant).Distinct().Count() > 1);
        Assert.All(firstFlora, flora =>
        {
            var cave = first.GetCaveCell(flora.Position);
            Assert.Equal(CaveCellKind.Floor, cave.Kind);
            Assert.Equal(CellFluidKind.None, cave.Fluid);
        });
    }

    [Fact]
    public void FloraDoesNotExtendBelowConfiguredEarlyCaveLevels()
    {
        var map = SwampMapGenerator.Generate(
            new WorldSeed(0x4341564544454550UL),
            64,
            64);
        map.MaterializeCaveLevel(-3);
        map.MaterializeCaveLevel(-4);
        var position = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width)
                .Select(x => new GridPosition(x, y, -4)))
            .First(cell => map.GetCaveCell(cell).IsOpen);

        Assert.False(CaveFloraGenerator.TryGet(map, position, out _));
    }

    private static CaveFloraPatch[] Collect(GeneratedMap map) =>
        (from depth in Enumerable.Range(1, 3)
         from y in Enumerable.Range(0, map.Height)
         from x in Enumerable.Range(0, map.Width)
         let position = new GridPosition(x, y, -depth)
         where CaveFloraGenerator.TryGet(map, position, out _)
         select Get(map, position)).ToArray();

    private static CaveFloraPatch Get(GeneratedMap map, GridPosition position)
    {
        Assert.True(CaveFloraGenerator.TryGet(map, position, out var flora));
        return flora;
    }
}
