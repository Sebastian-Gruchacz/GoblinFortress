using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class UndergroundFactionTests
{
    [Fact]
    public void ShallowWorldDoesNotCreateUndergroundFactions()
    {
        var director = UndergroundFactionDirector.Create(new WorldSeed(1), minimumWorldLevel: -2);

        Assert.Empty(director.CreateSnapshot());
        Assert.Empty(director.Relations);
        Assert.False(director.HasFactions);
    }

    [Fact]
    public void DepthBandsAndRelationsAreDeterministic()
    {
        var seed = new WorldSeed(0x554E444552UL);
        var first = UndergroundFactionDirector.Create(seed, minimumWorldLevel: -36);
        var second = UndergroundFactionDirector.Create(seed, minimumWorldLevel: -36);

        Assert.Equal(first.CreateSnapshot(), second.CreateSnapshot());
        Assert.Equal(first.Relations, second.Relations);
        Assert.All(first.CreateSnapshot(), faction =>
        {
            Assert.InRange(faction.BandIndex, 0, 3);
            Assert.Equal(
                UndergroundFactionDirector.FirstFactionLevel -
                    (faction.BandIndex * UndergroundFactionDirector.DepthBandSize),
                faction.TopLevel);
            Assert.InRange(
                faction.FortressLevel,
                faction.BottomLevel,
                faction.TopLevel);
            Assert.False(faction.IsActive);
            Assert.Equal(UndergroundFactionDirective.Dormant, faction.Directive);
        });
    }

    [Fact]
    public void DescentActivatesBandsAndHostileDispatchersFight()
    {
        var director = FindDirectorWithHostileFactions();
        var initial = director.CreateSnapshot();

        director.Advance(deepestGoblinLevel: -5, absoluteDay: 1);
        Assert.All(director.CreateSnapshot(), faction => Assert.False(faction.IsActive));

        director.Advance(deepestGoblinLevel: -36, absoluteDay: 2);
        var activated = director.CreateSnapshot();
        Assert.All(activated, faction => Assert.True(faction.IsActive));
        Assert.Contains(activated, faction =>
            faction.Directive == UndergroundFactionDirective.WageWar &&
            faction.TargetFactionId != 0);

        var populationBeforeConflict = activated.Sum(faction => faction.Population);
        director.Advance(deepestGoblinLevel: -36, absoluteDay: 10);
        var afterConflict = director.CreateSnapshot();

        Assert.True(afterConflict.Sum(faction => faction.Population) < populationBeforeConflict);
        director.Advance(deepestGoblinLevel: -36, absoluteDay: 10);
        Assert.Equal(afterConflict, director.CreateSnapshot());
    }

    private static UndergroundFactionDirector FindDirectorWithHostileFactions()
    {
        for (ulong seedValue = 1; seedValue <= 10_000; seedValue++)
        {
            var director = UndergroundFactionDirector.Create(
                new WorldSeed(seedValue),
                minimumWorldLevel: -36);
            if (director.CreateSnapshot().Count >= 2 && director.Relations.Any(relation =>
                    relation.Kind == UndergroundFactionRelationKind.Hostile &&
                    AreInAdjacentBands(director.CreateSnapshot(), relation)))
            {
                return director;
            }
        }
        throw new InvalidOperationException(
            "The deterministic seed sample did not produce hostile underground factions.");
    }

    private static bool AreInAdjacentBands(
        IReadOnlyList<UndergroundFactionSnapshot> factions,
        UndergroundFactionRelationSnapshot relation)
    {
        var first = factions.Single(faction => faction.Id == relation.FirstFactionId);
        var second = factions.Single(faction => faction.Id == relation.SecondFactionId);
        return Math.Abs(first.BandIndex - second.BandIndex) <= 1;
    }
}
