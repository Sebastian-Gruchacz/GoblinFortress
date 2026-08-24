namespace GoblinStronghold.Simulation;

public sealed class SimulationDefinitions
{
    public static SimulationDefinitions Foundation { get; } = new(
        id: "foundation-v13",
        ticksPerDay: 240,
        maximumHunger: 10_000,
        hungerPerTick: 25,
        eatThreshold: 5_000,
        foodNutrition: 4_000,
        foodSeekThreshold: 6_500,
        criticalHungerThreshold: 8_000,
        eatWorkTicks: 10,
        maximumHealth: 10_000,
        starvationHungerThreshold: 9_000,
        starvationDamagePerTick: 40,
        baseForageYield: 2,
        forageVariance: 3,
        actorCarryCapacity: 10,
        actorMovementIntervalTicks: 10,
        forageWorkTicks: 30,
        haulHandlingTicks: 10,
        maximumFatigue: 10_000,
        fatiguePerTick: 10,
        restThreshold: 2_500,
        restRecoveryPerTick: 75,
        visionRadius: 4,
        maximumExplorers: 1,
        plantGrowthIntervalTicks: 240,
        plantGrowthPerInterval: 1,
        humanCohortMovementIntervalTicks: 20,
        humanVillageActivityRadius: 8,
        humanDetectionRadius: 5,
        combatIntervalTicks: 10,
        humanGuardHealth: 6_000,
        humanGuardMinimumDamage: 320,
        humanGuardDamageVariance: 180,
        goblinMinimumDamage: 260,
        goblinDamageVariance: 160,
        maximumThirst: 10_000,
        thirstPerTick: 25,
        drinkThreshold: 5_000,
        waterHydration: 4_000,
        dehydrationThirstThreshold: 9_000,
        dehydrationDamagePerTick: 60,
        personalFoodCapacity: 2,
        personalWaterCapacity: 2,
        resupplyWorkTicks: 10);

    public SimulationDefinitions(
        string id,
        int ticksPerDay,
        int maximumHunger,
        int hungerPerTick,
        int eatThreshold,
        int foodNutrition,
        int foodSeekThreshold,
        int criticalHungerThreshold,
        int eatWorkTicks,
        int maximumHealth,
        int starvationHungerThreshold,
        int starvationDamagePerTick,
        int baseForageYield,
        int forageVariance,
        int actorCarryCapacity,
        int actorMovementIntervalTicks,
        int forageWorkTicks,
        int haulHandlingTicks,
        int maximumFatigue,
        int fatiguePerTick,
        int restThreshold,
        int restRecoveryPerTick,
        int visionRadius,
        int maximumExplorers,
        int plantGrowthIntervalTicks,
        int plantGrowthPerInterval,
        int humanCohortMovementIntervalTicks,
        int humanVillageActivityRadius,
        int humanDetectionRadius,
        int combatIntervalTicks,
        int humanGuardHealth,
        int humanGuardMinimumDamage,
        int humanGuardDamageVariance,
        int goblinMinimumDamage,
        int goblinDamageVariance,
        int maximumThirst,
        int thirstPerTick,
        int drinkThreshold,
        int waterHydration,
        int dehydrationThirstThreshold,
        int dehydrationDamagePerTick,
        int personalFoodCapacity,
        int personalWaterCapacity,
        int resupplyWorkTicks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerDay);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumHunger);
        ArgumentOutOfRangeException.ThrowIfNegative(hungerPerTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(eatThreshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(eatThreshold, maximumHunger);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(foodNutrition);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(foodSeekThreshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(foodSeekThreshold, maximumHunger);
        ArgumentOutOfRangeException.ThrowIfLessThan(criticalHungerThreshold, foodSeekThreshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(criticalHungerThreshold, maximumHunger);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(eatWorkTicks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumHealth);
        ArgumentOutOfRangeException.ThrowIfLessThan(starvationHungerThreshold, criticalHungerThreshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(starvationHungerThreshold, maximumHunger);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(starvationDamagePerTick);
        ArgumentOutOfRangeException.ThrowIfNegative(baseForageYield);
        ArgumentOutOfRangeException.ThrowIfNegative(forageVariance);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actorCarryCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actorMovementIntervalTicks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(forageWorkTicks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(haulHandlingTicks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFatigue);
        ArgumentOutOfRangeException.ThrowIfNegative(fatiguePerTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(restThreshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(restThreshold, maximumFatigue);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(restRecoveryPerTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(visionRadius);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumExplorers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(plantGrowthIntervalTicks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(plantGrowthPerInterval);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(humanCohortMovementIntervalTicks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(humanVillageActivityRadius);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(humanDetectionRadius);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(combatIntervalTicks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(humanGuardHealth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(humanGuardMinimumDamage);
        ArgumentOutOfRangeException.ThrowIfNegative(humanGuardDamageVariance);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(goblinMinimumDamage);
        ArgumentOutOfRangeException.ThrowIfNegative(goblinDamageVariance);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumThirst);
        ArgumentOutOfRangeException.ThrowIfNegative(thirstPerTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(drinkThreshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(drinkThreshold, maximumThirst);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(waterHydration);
        ArgumentOutOfRangeException.ThrowIfLessThan(dehydrationThirstThreshold, drinkThreshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dehydrationThirstThreshold, maximumThirst);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dehydrationDamagePerTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(personalFoodCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(personalWaterCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resupplyWorkTicks);

        Id = id;
        TicksPerDay = ticksPerDay;
        MaximumHunger = maximumHunger;
        HungerPerTick = hungerPerTick;
        EatThreshold = eatThreshold;
        FoodNutrition = foodNutrition;
        FoodSeekThreshold = foodSeekThreshold;
        CriticalHungerThreshold = criticalHungerThreshold;
        EatWorkTicks = eatWorkTicks;
        MaximumHealth = maximumHealth;
        StarvationHungerThreshold = starvationHungerThreshold;
        StarvationDamagePerTick = starvationDamagePerTick;
        BaseForageYield = baseForageYield;
        ForageVariance = forageVariance;
        ActorCarryCapacity = actorCarryCapacity;
        ActorMovementIntervalTicks = actorMovementIntervalTicks;
        ForageWorkTicks = forageWorkTicks;
        HaulHandlingTicks = haulHandlingTicks;
        MaximumFatigue = maximumFatigue;
        FatiguePerTick = fatiguePerTick;
        RestThreshold = restThreshold;
        RestRecoveryPerTick = restRecoveryPerTick;
        VisionRadius = visionRadius;
        MaximumExplorers = maximumExplorers;
        PlantGrowthIntervalTicks = plantGrowthIntervalTicks;
        PlantGrowthPerInterval = plantGrowthPerInterval;
        HumanCohortMovementIntervalTicks = humanCohortMovementIntervalTicks;
        HumanVillageActivityRadius = humanVillageActivityRadius;
        HumanDetectionRadius = humanDetectionRadius;
        CombatIntervalTicks = combatIntervalTicks;
        HumanGuardHealth = humanGuardHealth;
        HumanGuardMinimumDamage = humanGuardMinimumDamage;
        HumanGuardDamageVariance = humanGuardDamageVariance;
        GoblinMinimumDamage = goblinMinimumDamage;
        GoblinDamageVariance = goblinDamageVariance;
        MaximumThirst = maximumThirst;
        ThirstPerTick = thirstPerTick;
        DrinkThreshold = drinkThreshold;
        WaterHydration = waterHydration;
        DehydrationThirstThreshold = dehydrationThirstThreshold;
        DehydrationDamagePerTick = dehydrationDamagePerTick;
        PersonalFoodCapacity = personalFoodCapacity;
        PersonalWaterCapacity = personalWaterCapacity;
        ResupplyWorkTicks = resupplyWorkTicks;
    }

    public string Id { get; }

    public int TicksPerDay { get; }

    public int MaximumHunger { get; }

    public int HungerPerTick { get; }

    public int EatThreshold { get; }

    public int FoodNutrition { get; }

    public int FoodSeekThreshold { get; }

    public int CriticalHungerThreshold { get; }

    public int EatWorkTicks { get; }

    public int MaximumHealth { get; }

    public int StarvationHungerThreshold { get; }

    public int StarvationDamagePerTick { get; }

    public int BaseForageYield { get; }

    public int ForageVariance { get; }

    public int ActorCarryCapacity { get; }

    public int ActorMovementIntervalTicks { get; }

    public int ForageWorkTicks { get; }

    public int HaulHandlingTicks { get; }

    public int MaximumFatigue { get; }

    public int FatiguePerTick { get; }

    public int RestThreshold { get; }

    public int RestRecoveryPerTick { get; }

    public int VisionRadius { get; }

    public int MaximumExplorers { get; }

    public int PlantGrowthIntervalTicks { get; }

    public int PlantGrowthPerInterval { get; }

    public int HumanCohortMovementIntervalTicks { get; }

    public int HumanVillageActivityRadius { get; }

    public int HumanDetectionRadius { get; }

    public int CombatIntervalTicks { get; }

    public int HumanGuardHealth { get; }

    public int HumanGuardMinimumDamage { get; }

    public int HumanGuardDamageVariance { get; }

    public int GoblinMinimumDamage { get; }

    public int GoblinDamageVariance { get; }

    public int MaximumThirst { get; }

    public int ThirstPerTick { get; }

    public int DrinkThreshold { get; }

    public int WaterHydration { get; }

    public int DehydrationThirstThreshold { get; }

    public int DehydrationDamagePerTick { get; }

    public int PersonalFoodCapacity { get; }

    public int PersonalWaterCapacity { get; }

    public int ResupplyWorkTicks { get; }
}
