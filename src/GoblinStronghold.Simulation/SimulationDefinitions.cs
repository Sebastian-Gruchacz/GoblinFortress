using GoblinStronghold.Simulation.Animals;

namespace GoblinStronghold.Simulation;

public sealed record ActorNeedSettings(
    int MaximumHunger,
    int HungerPerTick,
    int EatThreshold,
    int FoodSeekThreshold,
    int CriticalHungerThreshold,
    int StarvationHungerThreshold,
    int StarvationDamagePerTick,
    int MaximumThirst,
    int ThirstPerTick,
    int DrinkThreshold,
    int DehydrationThirstThreshold,
    int DehydrationDamagePerTick,
    int MaximumFatigue,
    int FatiguePerTick,
    int RestThreshold);

public sealed record StorageSettings(int SmallFoodTypeSlots, int SmallStackCapacity)
{
    public int SmallFoodCapacity => checked(SmallFoodTypeSlots * SmallStackCapacity);
}

public sealed record FoodNutritionSettings(
    int DriedRations,
    int Berries,
    int Mushrooms,
    int EdibleRoots,
    int Fish,
    int RawMeat)
{
    public int GetSatiety(Resources.FoodKind kind) => kind switch
    {
        Resources.FoodKind.DriedRations => DriedRations,
        Resources.FoodKind.Berries => Berries,
        Resources.FoodKind.Mushrooms => Mushrooms,
        Resources.FoodKind.EdibleRoots => EdibleRoots,
        Resources.FoodKind.Fish => Fish,
        Resources.FoodKind.RawMeat => RawMeat,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown food kind."),
    };
}

public sealed record HealthRecoverySettings(
    int NaturalIntervalTicks,
    int SleepingBonusIntervalTicks,
    int MedicinalRootsHealing)
{
    public int GetFoodHealing(Resources.FoodKind kind) => kind switch
    {
        Resources.FoodKind.EdibleRoots => MedicinalRootsHealing,
        _ => 0,
    };
}

public sealed record VisionSettings(
    int GoblinDayRadius,
    int GoblinNightRadius,
    int GoblinStructureRadius);

public sealed record ActorPlanningSettings(
    int QueueCapacity,
    int BackgroundPlanningIntervalTicks,
    int MaximumBurstPlannersPerTick,
    int MaximumPathExpansionsPerSlice,
    int MaximumNeedPriority,
    int BackgroundJobCommitment,
    int OrdinaryJobCommitment,
    int OrderedJobCommitment,
    int InterruptHysteresis);

public sealed record GoblinReproductionSettings(
    int FoodCost,
    int FoodReserveDays,
    int MinimumAdultPopulation,
    int AdultsPerJuvenile,
    int MinimumMoisture,
    int TendWorkTicks,
    int JuvenileSeasonCount,
    int ParentHealthCost,
    int ParentHungerCost,
    int ParentThirstCost,
    int ParentFatigueCost);

public sealed record GoblinRangedCombatSettings(
    int HandAmmoCapacity,
    int SlingAmmoCapacity,
    int ThrownStoneRange,
    int SlingRange,
    int ThrownStoneDamage,
    int SlingDamage,
    int DamageVariance);

public sealed record GoblinPrimitiveEquipmentSettings(
    int BoneKnifeDamageBonus,
    int FightingStickDamageBonus,
    int StoneClubDamageBonus,
    int WoodenAxeDamageBonus,
    int PrimitivePickaxeDamageBonus,
    int ReinforcedPickaxeDamageBonus);

public sealed record GoblinAgingSettings(
    int HealthyYears,
    int DeclineMinimumSeasons,
    int DeclineMaximumSeasons,
    int TerminalHealthPermille,
    int InitialMinimumAgeYears,
    int InitialMaximumAgeYearsExclusive);

public sealed record AnimalSpeciesEcologySettings(
    int MaturitySeasonCount,
    int MaximumAgeYears,
    int BreedingIntervalDays,
    int MateSearchRadius,
    int MinimumPopulationCapacity,
    int MapCellsPerAnimal);

public sealed record AnimalEcologySettings(
    AnimalSpeciesEcologySettings MarshHare,
    AnimalSpeciesEcologySettings SwampBoar,
    AnimalSpeciesEcologySettings CaveSpider)
{
    public AnimalSpeciesEcologySettings Get(AnimalKind kind) =>
        Get(AnimalSpeciesCatalog.Current.Get(kind).EcologyProfile);

    public AnimalSpeciesEcologySettings Get(AnimalEcologyProfile profile) => profile switch
    {
        AnimalEcologyProfile.MarshHare => MarshHare,
        AnimalEcologyProfile.SwampBoar => SwampBoar,
        AnimalEcologyProfile.CaveSpider => CaveSpider,
        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };
}

public sealed record FishRegrowthSettings(
    int BaseMultiplier,
    int DeepWaterBonusMultiplier,
    int RiverChannelBonusMultiplier);

public sealed record HumanVillageNeedSettings(
    int MaximumNeed,
    int DailyHungerIncrease,
    int DailyThirstIncrease,
    int MealRelief,
    int DrinkRelief,
    int MaximumFatigue,
    int RestThreshold,
    int WorkFatiguePerMove,
    int DayRestRecoveryPerMove,
    int NightRestRecoveryPerMove,
    int HungerDamageDivisor,
    int ThirstDamageDivisor);

public sealed record HumanVillageEconomySettings(
    int CropGrowthDays,
    int FieldYield,
    int MinimumFieldCount,
    int BaseFoodCapacity,
    int StorehouseCapacity,
    int StorehouseWoodCost,
    int WaterWorkPerUnit,
    int FieldWorkPerStage,
    int TreeFellingWork,
    int TreeSearchRadius,
    int GoodsWorkPerUnit,
    int GoodsWoodCost,
    int GoodsPopulationDivisor,
    int StorehouseWork);

public sealed class SimulationDefinitions
{
    public static SimulationDefinitions Foundation { get; } = new(
        id: "foundation-v37",
        clock: new(ClimateCalendarProfiles.DemoTemperate),
        maximumHunger: 114_000,
        hungerPerTick: 1,
        eatThreshold: 5_700,
        foodNutrition: 5_700,
        foodSeekThreshold: 5_000,
        criticalHungerThreshold: 85_500,
        eatWorkTicks: 10,
        maximumHealth: 10_000,
        starvationHungerThreshold: 104_000,
        starvationDamagePerTick: 1,
        baseForageYield: 2,
        forageVariance: 3,
        actorCarryCapacity: 10,
        actorMovementIntervalTicks: 10,
        forageWorkTicks: 30,
        haulHandlingTicks: 10,
        maximumFatigue: 17_100,
        fatiguePerTick: 1,
        restThreshold: 10_000,
        restRecoveryPerTick: 3,
        visionRadius: 5,
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
        maximumThirst: 34_200,
        thirstPerTick: 1,
        drinkThreshold: 5_700,
        waterHydration: 5_700,
        dehydrationThirstThreshold: 24_200,
        dehydrationDamagePerTick: 1,
        personalFoodCapacity: 2,
        personalWaterCapacity: 2,
        resupplyWorkTicks: 10,
        berriesNutrition: 2_800,
        mushroomsNutrition: 3_400,
        edibleRootsNutrition: 4_200,
        fishNutrition: 4_800,
        naturalHealthRecoveryIntervalTicks: 12,
        sleepingHealthRecoveryBonusIntervalTicks: 8,
        medicinalRootsHealing: 500,
        goblinNightVisionRadius: 3,
        goblinStructureVisionRadius: 3,
        actorPlanning: new(
            QueueCapacity: 4,
            BackgroundPlanningIntervalTicks: 20,
            MaximumBurstPlannersPerTick: 2,
            MaximumPathExpansionsPerSlice: 64,
            MaximumNeedPriority: 100,
            BackgroundJobCommitment: 20,
            OrdinaryJobCommitment: 40,
            OrderedJobCommitment: 70,
            InterruptHysteresis: 5),
        humanVillageNeeds: new(
            MaximumNeed: 1_000,
            DailyHungerIncrease: 100,
            DailyThirstIncrease: 340,
            MealRelief: 500,
            DrinkRelief: 700,
            MaximumFatigue: 1_000,
            RestThreshold: 750,
            WorkFatiguePerMove: 2,
            DayRestRecoveryPerMove: 4,
            NightRestRecoveryPerMove: 5,
            HungerDamageDivisor: 10,
            ThirstDamageDivisor: 3),
        humanVillageEconomy: new(
            CropGrowthDays: 20,
            FieldYield: 180,
            MinimumFieldCount: 4,
            BaseFoodCapacity: 240,
            StorehouseCapacity: 240,
            StorehouseWoodCost: 24,
            WaterWorkPerUnit: 180,
            FieldWorkPerStage: 1_080,
            TreeFellingWork: 480,
            TreeSearchRadius: 8,
            GoodsWorkPerUnit: 360,
            GoodsWoodCost: 2,
            GoodsPopulationDivisor: 3,
            StorehouseWork: 720));

    public const int FieldCampCapacity = 5;
    public const int GoblinHutCapacity = 9;

    public SimulationDefinitions(
        string id,
        SimulationClockSettings clock,
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
        int resupplyWorkTicks,
        int berriesNutrition = 1_800,
        int mushroomsNutrition = 2_200,
        int edibleRootsNutrition = 2_800,
        int fishNutrition = 3_200,
        int naturalHealthRecoveryIntervalTicks = 12,
        int sleepingHealthRecoveryBonusIntervalTicks = 8,
        int medicinalRootsHealing = 500,
        int? goblinNightVisionRadius = null,
        int goblinStructureVisionRadius = 0,
        ActorPlanningSettings? actorPlanning = null,
        HumanVillageNeedSettings? humanVillageNeeds = null,
        HumanVillageEconomySettings? humanVillageEconomy = null,
        AnimalEcologySettings? animalEcology = null,
        FishRegrowthSettings? fishRegrowth = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(clock);
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
        if (goblinNightVisionRadius is not null)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(goblinNightVisionRadius.Value);
        }
        ArgumentOutOfRangeException.ThrowIfNegative(goblinStructureVisionRadius);
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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(berriesNutrition);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mushroomsNutrition);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(edibleRootsNutrition);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fishNutrition);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(naturalHealthRecoveryIntervalTicks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sleepingHealthRecoveryBonusIntervalTicks);
        ArgumentOutOfRangeException.ThrowIfNegative(medicinalRootsHealing);
        actorPlanning ??= new(
            QueueCapacity: 4,
            BackgroundPlanningIntervalTicks: 20,
            MaximumBurstPlannersPerTick: 2,
            MaximumPathExpansionsPerSlice: 64,
            MaximumNeedPriority: 100,
            BackgroundJobCommitment: 20,
            OrdinaryJobCommitment: 40,
            OrderedJobCommitment: 70,
            InterruptHysteresis: 5);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actorPlanning.QueueCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            actorPlanning.BackgroundPlanningIntervalTicks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            actorPlanning.MaximumBurstPlannersPerTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            actorPlanning.MaximumPathExpansionsPerSlice);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actorPlanning.MaximumNeedPriority);
        ArgumentOutOfRangeException.ThrowIfNegative(actorPlanning.BackgroundJobCommitment);
        ArgumentOutOfRangeException.ThrowIfNegative(actorPlanning.OrdinaryJobCommitment);
        ArgumentOutOfRangeException.ThrowIfNegative(actorPlanning.OrderedJobCommitment);
        ArgumentOutOfRangeException.ThrowIfNegative(actorPlanning.InterruptHysteresis);
        if (actorPlanning.BackgroundJobCommitment > actorPlanning.OrdinaryJobCommitment ||
            actorPlanning.OrdinaryJobCommitment > actorPlanning.OrderedJobCommitment)
        {
            throw new ArgumentException(
                "Actor job commitments must be ordered from background through ordinary to ordered work.",
                nameof(actorPlanning));
        }
        if ((long)actorPlanning.OrderedJobCommitment + actorPlanning.InterruptHysteresis >=
            actorPlanning.MaximumNeedPriority)
        {
            throw new ArgumentException(
                "Maximum need priority must be able to interrupt ordered work.",
                nameof(actorPlanning));
        }
        humanVillageNeeds ??= new(
            MaximumNeed: 1_000,
            DailyHungerIncrease: 100,
            DailyThirstIncrease: 340,
            MealRelief: 500,
            DrinkRelief: 700,
            MaximumFatigue: 1_000,
            RestThreshold: 750,
            WorkFatiguePerMove: 2,
            DayRestRecoveryPerMove: 4,
            NightRestRecoveryPerMove: 5,
            HungerDamageDivisor: 10,
            ThirstDamageDivisor: 3);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(humanVillageNeeds.MaximumNeed);
        ArgumentOutOfRangeException.ThrowIfNegative(humanVillageNeeds.DailyHungerIncrease);
        ArgumentOutOfRangeException.ThrowIfNegative(humanVillageNeeds.DailyThirstIncrease);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(humanVillageNeeds.MealRelief);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            humanVillageNeeds.MealRelief,
            humanVillageNeeds.MaximumNeed);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(humanVillageNeeds.DrinkRelief);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            humanVillageNeeds.DrinkRelief,
            humanVillageNeeds.MaximumNeed);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(humanVillageNeeds.MaximumFatigue);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(humanVillageNeeds.RestThreshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            humanVillageNeeds.RestThreshold,
            humanVillageNeeds.MaximumFatigue);
        ArgumentOutOfRangeException.ThrowIfNegative(humanVillageNeeds.WorkFatiguePerMove);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            humanVillageNeeds.DayRestRecoveryPerMove);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            humanVillageNeeds.NightRestRecoveryPerMove);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            humanVillageNeeds.HungerDamageDivisor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            humanVillageNeeds.ThirstDamageDivisor);
        humanVillageEconomy ??= new(
            CropGrowthDays: 20,
            FieldYield: 180,
            MinimumFieldCount: 4,
            BaseFoodCapacity: 240,
            StorehouseCapacity: 240,
            StorehouseWoodCost: 24,
            WaterWorkPerUnit: 180,
            FieldWorkPerStage: 1_080,
            TreeFellingWork: 480,
            TreeSearchRadius: 8,
            GoodsWorkPerUnit: 360,
            GoodsWoodCost: 2,
            GoodsPopulationDivisor: 3,
            StorehouseWork: 720);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(humanVillageEconomy.CropGrowthDays);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(humanVillageEconomy.FieldYield);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(humanVillageEconomy.MinimumFieldCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(humanVillageEconomy.BaseFoodCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(humanVillageEconomy.StorehouseCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(humanVillageEconomy.StorehouseWoodCost);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(humanVillageEconomy.WaterWorkPerUnit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(humanVillageEconomy.FieldWorkPerStage);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(humanVillageEconomy.TreeFellingWork);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(humanVillageEconomy.TreeSearchRadius);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(humanVillageEconomy.GoodsWorkPerUnit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(humanVillageEconomy.GoodsWoodCost);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(humanVillageEconomy.GoodsPopulationDivisor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(humanVillageEconomy.StorehouseWork);
        animalEcology ??= new(
            MarshHare: new(
                MaturitySeasonCount: 1,
                MaximumAgeYears: 3,
                BreedingIntervalDays: 10,
                MateSearchRadius: 12,
                MinimumPopulationCapacity: 10,
                MapCellsPerAnimal: 300),
            SwampBoar: new(
                MaturitySeasonCount: 2,
                MaximumAgeYears: 8,
                BreedingIntervalDays: 20,
                MateSearchRadius: 16,
                MinimumPopulationCapacity: 5,
                MapCellsPerAnimal: 900),
            CaveSpider: new(
                MaturitySeasonCount: 2,
                MaximumAgeYears: 5,
                BreedingIntervalDays: 20,
                MateSearchRadius: 8,
                MinimumPopulationCapacity: 3,
                MapCellsPerAnimal: 2_000));
        foreach (var species in new[]
                 {
                     animalEcology.MarshHare,
                     animalEcology.SwampBoar,
                     animalEcology.CaveSpider,
                 })
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(species.MaturitySeasonCount);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(species.MaximumAgeYears);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(species.BreedingIntervalDays);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(species.MateSearchRadius);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(species.MinimumPopulationCapacity);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(species.MapCellsPerAnimal);
        }
        fishRegrowth ??= new(
            BaseMultiplier: 1,
            DeepWaterBonusMultiplier: 1,
            RiverChannelBonusMultiplier: 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fishRegrowth.BaseMultiplier);
        ArgumentOutOfRangeException.ThrowIfNegative(fishRegrowth.DeepWaterBonusMultiplier);
        ArgumentOutOfRangeException.ThrowIfNegative(fishRegrowth.RiverChannelBonusMultiplier);

        Id = id;
        Clock = clock;
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
        Needs = new(
            MaximumHunger, HungerPerTick, EatThreshold, FoodSeekThreshold,
            CriticalHungerThreshold, StarvationHungerThreshold, StarvationDamagePerTick,
            MaximumThirst, ThirstPerTick, DrinkThreshold,
            DehydrationThirstThreshold, DehydrationDamagePerTick,
            MaximumFatigue, FatiguePerTick, RestThreshold);
        Storage = new(SmallFoodTypeSlots: 3, SmallStackCapacity: 32);
        Food = new(
            DriedRations: FoodNutrition,
            Berries: berriesNutrition,
            Mushrooms: mushroomsNutrition,
            EdibleRoots: edibleRootsNutrition,
            Fish: fishNutrition,
            RawMeat: fishNutrition);
        HealthRecovery = new(
            NaturalIntervalTicks: naturalHealthRecoveryIntervalTicks,
            SleepingBonusIntervalTicks: sleepingHealthRecoveryBonusIntervalTicks,
            MedicinalRootsHealing: medicinalRootsHealing);
        Vision = new(
            GoblinDayRadius: visionRadius,
            GoblinNightRadius: goblinNightVisionRadius ?? Math.Max(2, visionRadius - 1),
            GoblinStructureRadius: goblinStructureVisionRadius);
        ActorPlanning = actorPlanning;
        Reproduction = new(
            FoodCost: 4,
            FoodReserveDays: 2,
            MinimumAdultPopulation: 4,
            AdultsPerJuvenile: 10,
            MinimumMoisture: 55,
            TendWorkTicks: 120,
            JuvenileSeasonCount: 1,
            ParentHealthCost: 1_500,
            ParentHungerCost: 3_000,
            ParentThirstCost: 1_000,
            ParentFatigueCost: 3_000);
        RangedCombat = new(
            HandAmmoCapacity: 3,
            SlingAmmoCapacity: 8,
            ThrownStoneRange: 2,
            SlingRange: 5,
            ThrownStoneDamage: 160,
            SlingDamage: 280,
            DamageVariance: 90);
        PrimitiveEquipment = new(
            BoneKnifeDamageBonus: 30,
            FightingStickDamageBonus: 80,
            StoneClubDamageBonus: 140,
            WoodenAxeDamageBonus: 100,
            PrimitivePickaxeDamageBonus: 110,
            ReinforcedPickaxeDamageBonus: 160);
        Aging = new(
            HealthyYears: 5,
            DeclineMinimumSeasons: 1,
            DeclineMaximumSeasons: 2,
            TerminalHealthPermille: 150,
            InitialMinimumAgeYears: 1,
            InitialMaximumAgeYearsExclusive: 5);
        HumanVillageNeeds = humanVillageNeeds;
        HumanVillageEconomy = humanVillageEconomy;
        AnimalEcology = animalEcology;
        FishRegrowth = fishRegrowth;
    }

    public string Id { get; }

    public SimulationClockSettings Clock { get; }

    public ActorNeedSettings Needs { get; }

    public StorageSettings Storage { get; }

    public FoodNutritionSettings Food { get; }

    public HealthRecoverySettings HealthRecovery { get; }

    public VisionSettings Vision { get; }

    public ActorPlanningSettings ActorPlanning { get; }

    public GoblinReproductionSettings Reproduction { get; }

    public GoblinRangedCombatSettings RangedCombat { get; }

    public GoblinPrimitiveEquipmentSettings PrimitiveEquipment { get; }

    public GoblinAgingSettings Aging { get; }

    public HumanVillageNeedSettings HumanVillageNeeds { get; }

    public HumanVillageEconomySettings HumanVillageEconomy { get; }

    public AnimalEcologySettings AnimalEcology { get; }

    public FishRegrowthSettings FishRegrowth { get; }

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
