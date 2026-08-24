namespace GoblinStronghold.Simulation;

public sealed class SimulationDefinitions
{
    public static SimulationDefinitions Foundation { get; } = new(
        id: "foundation-v2",
        ticksPerDay: 240,
        maximumHunger: 10_000,
        hungerPerTick: 25,
        eatThreshold: 5_000,
        foodNutrition: 4_000,
        baseForageYield: 2,
        forageVariance: 3,
        actorCarryCapacity: 10,
        plantGrowthIntervalTicks: 240,
        plantGrowthPerInterval: 1);

    public SimulationDefinitions(
        string id,
        int ticksPerDay,
        int maximumHunger,
        int hungerPerTick,
        int eatThreshold,
        int foodNutrition,
        int baseForageYield,
        int forageVariance,
        int actorCarryCapacity,
        int plantGrowthIntervalTicks,
        int plantGrowthPerInterval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerDay);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumHunger);
        ArgumentOutOfRangeException.ThrowIfNegative(hungerPerTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(eatThreshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(eatThreshold, maximumHunger);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(foodNutrition);
        ArgumentOutOfRangeException.ThrowIfNegative(baseForageYield);
        ArgumentOutOfRangeException.ThrowIfNegative(forageVariance);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actorCarryCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(plantGrowthIntervalTicks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(plantGrowthPerInterval);

        Id = id;
        TicksPerDay = ticksPerDay;
        MaximumHunger = maximumHunger;
        HungerPerTick = hungerPerTick;
        EatThreshold = eatThreshold;
        FoodNutrition = foodNutrition;
        BaseForageYield = baseForageYield;
        ForageVariance = forageVariance;
        ActorCarryCapacity = actorCarryCapacity;
        PlantGrowthIntervalTicks = plantGrowthIntervalTicks;
        PlantGrowthPerInterval = plantGrowthPerInterval;
    }

    public string Id { get; }

    public int TicksPerDay { get; }

    public int MaximumHunger { get; }

    public int HungerPerTick { get; }

    public int EatThreshold { get; }

    public int FoodNutrition { get; }

    public int BaseForageYield { get; }

    public int ForageVariance { get; }

    public int ActorCarryCapacity { get; }

    public int PlantGrowthIntervalTicks { get; }

    public int PlantGrowthPerInterval { get; }
}
