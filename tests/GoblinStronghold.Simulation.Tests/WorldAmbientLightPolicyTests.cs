using GoblinStronghold.Simulation.Lighting;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class WorldAmbientLightPolicyTests
{
    [Fact]
    public void SurfaceLightMovesFromWarmMorningThroughClearDayToWarmAfternoon()
    {
        var clock = SimulationDefinitions.Foundation.Clock;
        var season = clock.Climate.Seasons[0];
        var morning = WorldAmbientLightPolicy.ResolveSurface(
            SimulationCalendar.At(new SimulationTick(0), clock));
        var midday = WorldAmbientLightPolicy.ResolveSurface(
            SimulationCalendar.At(new SimulationTick(season.DaylightTicks / 2), clock));
        var afternoon = WorldAmbientLightPolicy.ResolveSurface(
            SimulationCalendar.At(
                new SimulationTick(season.DaylightTicks - 1),
                clock));

        Assert.True(morning.Darkness > midday.Darkness);
        Assert.Equal(0f, midday.Darkness);
        Assert.True(afternoon.Darkness > midday.Darkness);
        Assert.True(morning.Red > morning.Blue);
        Assert.True(afternoon.Red > afternoon.Blue);
    }

    [Fact]
    public void SurfaceNightIsDarkButNotAsDarkAsEnclosedUnderground()
    {
        var clock = SimulationDefinitions.Foundation.Clock;
        var season = clock.Climate.Seasons[0];
        var middleOfNight = new SimulationTick(
            season.DaylightTicks + season.NightTicks / 2);
        var night = WorldAmbientLightPolicy.ResolveSurface(
            SimulationCalendar.At(middleOfNight, clock));

        Assert.True(night.Darkness > 0f);
        Assert.True(night.Darkness < WorldAmbientLightPolicy.Underground.Darkness);
        Assert.True(night.Blue > night.Red);
    }
}
