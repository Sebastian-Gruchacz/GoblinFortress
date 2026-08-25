using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class SimulationCalendarTests
{
    [Fact]
    public void DemoTemperateProfileKeepsRequestedDemoTiming()
    {
        var climate = SimulationDefinitions.Foundation.Clock.Climate;

        Assert.Equal("demo-temperate", climate.Id);
        Assert.Equal(40, climate.DaysPerYear);
        Assert.Equal(4, climate.Seasons.Count);
        Assert.All(climate.Seasons, season =>
        {
            Assert.Equal(10, season.Days);
            Assert.Equal(7_200, season.DaylightTicks);
            Assert.Equal(4_200, season.NightTicks);
            Assert.Equal(5 * 60, season.DawnMinute);
            Assert.Equal(17 * 60, season.DuskMinute);
        });
    }

    [Theory]
    [InlineData(0, 5, 0, false)]
    [InlineData(7_200, 17, 0, true)]
    [InlineData(11_399, 4, 59, true)]
    public void ClockMapsDayAndNightSegmentsToCivilTime(
        long tick,
        int hour,
        int minute,
        bool isNight)
    {
        var calendar = SimulationCalendar.At(
            new SimulationTick(tick),
            SimulationDefinitions.Foundation.Clock);

        Assert.Equal(hour, calendar.Hour);
        Assert.Equal(minute, calendar.Minute);
        Assert.Equal(isNight, calendar.IsNight);
    }

    [Fact]
    public void CalendarSupportsUnequalSeasonsAndSeasonalDaylightRatios()
    {
        var climate = new ClimateCalendarProfile(
            "test-variable-climate",
            [
                new(SeasonKind.Spring, Days: 2, DaylightTicks: 100, NightTicks: 50,
                    DawnMinute: 6 * 60, DuskMinute: 18 * 60),
                new(SeasonKind.Summer, Days: 3, DaylightTicks: 200, NightTicks: 100,
                    DawnMinute: 4 * 60, DuskMinute: 20 * 60),
            ]);
        var settings = new SimulationClockSettings(climate);
        var summerStart = climate.GetSeasonStartTick(SeasonKind.Summer);
        var summerDusk = SimulationCalendar.At(
            new SimulationTick(summerStart + 200),
            settings);

        Assert.Equal(300, summerStart);
        Assert.Equal(SeasonKind.Summer, summerDusk.Season);
        Assert.Equal(1, summerDusk.DayOfSeason);
        Assert.Equal(20, summerDusk.Hour);
        Assert.True(summerDusk.IsNight);
        Assert.Equal(0.25d, climate.SeasonSpans[0].Length, precision: 10);
        Assert.Equal(0.75d, climate.SeasonSpans[1].Length, precision: 10);
        Assert.Equal(5d / 12d, summerDusk.YearProgress, precision: 10);
    }

    [Fact]
    public void NextDayBoundaryUsesTheCurrentSeasonsDayLength()
    {
        var climate = new ClimateCalendarProfile(
            "test-boundaries",
            [
                new(SeasonKind.Spring, Days: 1, DaylightTicks: 100, NightTicks: 50,
                    DawnMinute: 6 * 60, DuskMinute: 18 * 60),
                new(SeasonKind.Summer, Days: 1, DaylightTicks: 200, NightTicks: 100,
                    DawnMinute: 4 * 60, DuskMinute: 20 * 60),
            ]);
        var settings = new SimulationClockSettings(climate);

        Assert.Equal(new SimulationTick(150),
            SimulationCalendar.NextDayStart(new SimulationTick(20), settings));
        Assert.Equal(new SimulationTick(450),
            SimulationCalendar.NextDayStart(new SimulationTick(200), settings));
    }
}
