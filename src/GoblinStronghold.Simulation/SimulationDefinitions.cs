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
    int MaximumNeedPriority,
    int BackgroundJobCommitment,
    int OrdinaryJobCommitment,
    int OrderedJobCommitment,
    int InterruptHysteresis);

public sealed record GoblinReproductionSettings(
    int FoodCost,
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
    int StoneClubDamageBonus);

public sealed record GoblinAgingSettings(
    int HealthyYears,
    int DeclineMinimumSeasons,
    int DeclineMaximumSeasons,
    int TerminalHealthPermille,
    int InitialMinimumAgeYears,
    int InitialMaximumAgeYearsExclusive);

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
            MaximumNeedPriority: 100,
            BackgroundJobCommitment: 20,
            OrdinaryJobCommitment: 40,
            OrderedJobCommitment: 70,
            InterruptHysteresis: 5));

    public const int FieldCampCapacity = 5;

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
        ActorPlanningSettings? actorPlanning = null)
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
            MaximumNeedPriority: 100,
            BackgroundJobCommitment: 20,
            OrdinaryJobCommitment: 40,
            OrderedJobCommitment: 70,
            InterruptHysteresis: 5);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actorPlanning.QueueCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            actorPlanning.BackgroundPlanningIntervalTicks);
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
            StoneClubDamageBonus: 140);
        Aging = new(
            HealthyYears: 5,
            DeclineMinimumSeasons: 1,
            DeclineMaximumSeasons: 2,
            TerminalHealthPermille: 150,
            InitialMinimumAgeYears: 1,
            InitialMaximumAgeYearsExclusive: 5);
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
