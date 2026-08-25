using System.Collections.ObjectModel;

namespace GoblinStronghold.Simulation;

public enum SeasonKind : byte
{
    Spring = 0,
    Summer = 1,
    Autumn = 2,
    Winter = 3,
}

public sealed record ClimateSeasonDefinition(
    SeasonKind Season,
    int Days,
    int DaylightTicks,
    int NightTicks,
    int DawnMinute)
{
    private const int SecondsPerDay = 24 * 60 * 60;

    public int TicksPerDay => checked(DaylightTicks + NightTicks);

    public long TotalTicks => checked((long)Days * TicksPerDay);

    public int DuskSecond =>
        ((DawnMinute * 60) + ScaleTicks(DaylightTicks, TicksPerDay, SecondsPerDay)) % SecondsPerDay;

    public int DuskMinute => DuskSecond / 60;

    private static int ScaleTicks(int tick, int ticksInDay, int clockSeconds) =>
        checked((int)((long)tick * clockSeconds / ticksInDay));
}

public readonly record struct ClimateSeasonSpan(
    SeasonKind Season,
    double Start,
    double Length);

public sealed class ClimateCalendarProfile
{
    private readonly ReadOnlyCollection<ClimateSeasonDefinition> _seasons;
    private readonly ReadOnlyCollection<ClimateSeasonSpan> _seasonSpans;

    public ClimateCalendarProfile(
        string id,
        IEnumerable<ClimateSeasonDefinition> seasons)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(seasons);
        var definitions = seasons.ToArray();
        if (definitions.Length == 0 ||
            definitions.Select(item => item.Season).Distinct().Count() != definitions.Length ||
            definitions.Any(item =>
                item.Days <= 0 ||
                item.DaylightTicks <= 0 ||
                item.NightTicks <= 0 ||
                item.DawnMinute is < 0 or >= 24 * 60))
        {
            throw new ArgumentException("The climate contains invalid or duplicate seasons.", nameof(seasons));
        }

        Id = id;
        _seasons = new ReadOnlyCollection<ClimateSeasonDefinition>(definitions);
        DaysPerYear = definitions.Sum(item => item.Days);
        TicksPerYear = definitions.Sum(item => item.TotalTicks);
        _seasonSpans = new ReadOnlyCollection<ClimateSeasonSpan>(CreateSeasonSpans(definitions));
    }

    public string Id { get; }

    public IReadOnlyList<ClimateSeasonDefinition> Seasons => _seasons;

    public IReadOnlyList<ClimateSeasonSpan> SeasonSpans => _seasonSpans;

    public int DaysPerYear { get; }

    public long TicksPerYear { get; }

    public ClimateSeasonDefinition GetSeason(SeasonKind season) =>
        _seasons.FirstOrDefault(item => item.Season == season)
        ?? throw new ArgumentOutOfRangeException(nameof(season), season, "The climate does not define this season.");

    public long GetSeasonStartTick(SeasonKind season, int year = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(year);
        var tick = checked((long)year * TicksPerYear);
        foreach (var definition in _seasons)
        {
            if (definition.Season == season)
            {
                return tick;
            }
            tick = checked(tick + definition.TotalTicks);
        }
        throw new ArgumentOutOfRangeException(nameof(season), season, "The climate does not define this season.");
    }

    private ClimateSeasonSpan[] CreateSeasonSpans(ClimateSeasonDefinition[] definitions)
    {
        var result = new ClimateSeasonSpan[definitions.Length];
        long elapsed = 0;
        for (var index = 0; index < definitions.Length; index++)
        {
            var definition = definitions[index];
            result[index] = new ClimateSeasonSpan(
                definition.Season,
                (double)elapsed / TicksPerYear,
                (double)definition.TotalTicks / TicksPerYear);
            elapsed = checked(elapsed + definition.TotalTicks);
        }
        return result;
    }
}

public static class ClimateCalendarProfiles
{
    public static ClimateCalendarProfile DemoTemperate { get; } = new(
        "demo-temperate",
        [
            new(SeasonKind.Spring, Days: 10, DaylightTicks: 7_200, NightTicks: 4_200,
                DawnMinute: 5 * 60),
            new(SeasonKind.Summer, Days: 10, DaylightTicks: 7_200, NightTicks: 4_200,
                DawnMinute: 5 * 60),
            new(SeasonKind.Autumn, Days: 10, DaylightTicks: 7_200, NightTicks: 4_200,
                DawnMinute: 5 * 60),
            new(SeasonKind.Winter, Days: 10, DaylightTicks: 7_200, NightTicks: 4_200,
                DawnMinute: 5 * 60),
        ]);
}

public sealed record SimulationClockSettings
{
    public SimulationClockSettings(
        ClimateCalendarProfile climate,
        int ticksPerRealSecondAtNormalSpeed = 10)
    {
        ArgumentNullException.ThrowIfNull(climate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerRealSecondAtNormalSpeed);
        Climate = climate;
        TicksPerRealSecondAtNormalSpeed = ticksPerRealSecondAtNormalSpeed;
    }

    public ClimateCalendarProfile Climate { get; }

    public int TicksPerRealSecondAtNormalSpeed { get; }

    public double RealSecondsPerTickAtNormalSpeed =>
        1d / TicksPerRealSecondAtNormalSpeed;

    public double GetRealSecondsPerDayAtNormalSpeed(SeasonKind season) =>
        (double)Climate.GetSeason(season).TicksPerDay / TicksPerRealSecondAtNormalSpeed;
}

public readonly record struct SimulationCalendarSnapshot(
    int AbsoluteDay,
    int DayOfSeason,
    SeasonKind Season,
    int Hour,
    int Minute,
    int Second,
    bool IsNight,
    bool IsDayStart,
    int TickOfDay,
    int TicksInDay,
    int DaylightTicks,
    int NightTicks,
    double DayProgress,
    double SeasonProgress,
    double YearProgress);

public static class SimulationCalendar
{
    public static SimulationCalendarSnapshot At(
        SimulationTick tick,
        SimulationClockSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var climate = settings.Climate;
        var year = tick.Value / climate.TicksPerYear;
        var tickOfYear = tick.Value % climate.TicksPerYear;
        var elapsedSeasonTicks = 0L;
        var elapsedSeasonDays = 0;
        foreach (var season in climate.Seasons)
        {
            if (tickOfYear < elapsedSeasonTicks + season.TotalTicks)
            {
                var tickOfSeason = tickOfYear - elapsedSeasonTicks;
                var dayIndex = (int)(tickOfSeason / season.TicksPerDay);
                var tickOfDay = (int)(tickOfSeason % season.TicksPerDay);
                var isNight = tickOfDay >= season.DaylightTicks;
                var secondOfDay = (season.DawnMinute * 60) + ScaleTicks(
                    tickOfDay,
                    season.TicksPerDay,
                    24 * 60 * 60);
                secondOfDay %= 24 * 60 * 60;
                var dayProgress = (double)tickOfDay / season.TicksPerDay;
                return new SimulationCalendarSnapshot(
                    checked((int)(year * climate.DaysPerYear) + elapsedSeasonDays + dayIndex),
                    dayIndex + 1,
                    season.Season,
                    secondOfDay / (60 * 60),
                    secondOfDay / 60 % 60,
                    secondOfDay % 60,
                    isNight,
                    tickOfDay == 0,
                    tickOfDay,
                    season.TicksPerDay,
                    season.DaylightTicks,
                    season.NightTicks,
                    dayProgress,
                    (dayIndex + dayProgress) / season.Days,
                    (double)tickOfYear / climate.TicksPerYear);
            }

            elapsedSeasonTicks = checked(elapsedSeasonTicks + season.TotalTicks);
            elapsedSeasonDays = checked(elapsedSeasonDays + season.Days);
        }

        throw new InvalidOperationException("The climate calendar could not resolve a valid tick.");
    }

    public static SimulationTick NextDayStart(
        SimulationTick tick,
        SimulationClockSettings settings)
    {
        var calendar = At(tick, settings);
        return new SimulationTick(checked(tick.Value + calendar.TicksInDay - calendar.TickOfDay));
    }

    private static int ScaleTicks(int tick, int ticksInSegment, int clockUnits) =>
        checked((int)((long)tick * clockUnits / ticksInSegment));
}
