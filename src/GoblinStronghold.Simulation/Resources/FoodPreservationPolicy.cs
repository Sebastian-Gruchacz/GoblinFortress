namespace GoblinStronghold.Simulation.Resources;

public static class FoodPreservationPolicy
{
    public static int GetShelfLifeDays(FoodKind foodKind) => foodKind switch
    {
        FoodKind.Fish or FoodKind.RawMeat => 2,
        FoodKind.Berries or FoodKind.Mushrooms => 4,
        FoodKind.EdibleRoots => 8,
        FoodKind.CampSoup or FoodKind.CookedMeat or FoodKind.Medicine => 30,
        FoodKind.DriedRations => 180,
        _ => throw new ArgumentOutOfRangeException(nameof(foodKind)),
    };

    public static long GetFreshUntilTick(
        FoodKind foodKind,
        SimulationTick currentTick,
        SimulationClockSettings clock)
    {
        var calendar = SimulationCalendar.At(currentTick, clock);
        var ticksPerDay = clock.Climate.GetSeason(calendar.Season).TicksPerDay;
        return checked(currentTick.Value + (long)GetShelfLifeDays(foodKind) * ticksPerDay);
    }
}
