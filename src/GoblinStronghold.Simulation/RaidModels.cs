using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation;

[Flags]
public enum RaidDirective : ushort
{
    None = 0,
    AttackGuards = 1 << 0,
    AttackAll = 1 << 1,
    LootEquipment = 1 << 2,
    LootSupplies = 1 << 3,
    LootFood = 1 << 4,
    ConsumeCorpses = 1 << 5,
    BudCorpses = 1 << 6,
    BurnBuildings = 1 << 7,
    DemolishBuildings = 1 << 8,
    ContinueWhileTargetsVisible = 1 << 9,
    AutoLaunchWhenReady = 1 << 10,
    RecoverCorpses = 1 << 11,
    BudCorpsesInPlace = 1 << 12,
}

public enum RaidCorpseHandlingMode : byte
{
    None = 0,
    RecoverToCamp = 1,
    RecoverAndBudAtCamp = 2,
    BudInPlace = 3,
}

public readonly record struct RaidPlanSnapshot(
    GridPosition RallyPoint,
    GridPosition Target,
    int TargetRadius,
    RaidDirective Directives)
{
    public bool Has(RaidDirective directive) => Directives.HasFlag(directive);
}

public enum RaidPreparationMode : byte
{
    Automatic = 0,
}

public readonly record struct RaidPreparationProfile(
    RaidPreparationMode Mode,
    int FoodTarget,
    int WaterTarget,
    int StoneAmmoTarget,
    PersonalEquipment PreferredEquipment,
    bool KeepCargoHandsFree);

public static class RaidPreparationPolicy
{
    public static RaidPreparationProfile ResolveAutomatic(
        RaidDirective directives,
        SimulationDefinitions definitions,
        PersonalEquipment equipment)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var expectsExtendedOperation = directives.HasFlag(RaidDirective.LootEquipment) ||
            directives.HasFlag(RaidDirective.LootSupplies) ||
            directives.HasFlag(RaidDirective.LootFood) ||
            directives.HasFlag(RaidDirective.ConsumeCorpses) ||
            directives.HasFlag(RaidDirective.BudCorpses) ||
            directives.HasFlag(RaidDirective.RecoverCorpses) ||
            directives.HasFlag(RaidDirective.BudCorpsesInPlace) ||
            directives.HasFlag(RaidDirective.BurnBuildings) ||
            directives.HasFlag(RaidDirective.DemolishBuildings) ||
            directives.HasFlag(RaidDirective.ContinueWhileTargetsVisible);
        var expectsBroadCombat = directives.HasFlag(RaidDirective.AttackAll) ||
            directives.HasFlag(RaidDirective.ContinueWhileTargetsVisible);
        var ammoCapacity = equipment.HasFlag(PersonalEquipment.PrimitiveSling)
            ? definitions.RangedCombat.SlingAmmoCapacity
            : definitions.RangedCombat.HandAmmoCapacity;
        var preferredEquipment = directives.HasFlag(RaidDirective.DemolishBuildings)
            ? PersonalEquipment.PrimitivePickaxe
            : PersonalEquipment.None;

        return new RaidPreparationProfile(
            RaidPreparationMode.Automatic,
            expectsExtendedOperation
                ? definitions.PersonalFoodCapacity
                : Math.Min(definitions.PersonalFoodCapacity, 1),
            definitions.PersonalWaterCapacity,
            expectsBroadCombat ? ammoCapacity : Math.Min(ammoCapacity, 1),
            preferredEquipment,
            KeepCargoHandsFree: directives.HasFlag(RaidDirective.LootEquipment) ||
                directives.HasFlag(RaidDirective.LootSupplies) ||
                directives.HasFlag(RaidDirective.LootFood) ||
                directives.HasFlag(RaidDirective.RecoverCorpses) ||
                directives.HasFlag(RaidDirective.BudCorpses));
    }
}

public static class RaidAutoAssignmentPolicy
{
    public static IReadOnlyList<EntityId> Select(
        IEnumerable<ActorSnapshot> actors,
        int capacity)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);

        return actors
            .Where(actor => actor.Health > 0 && !actor.IsJuvenile)
            .OrderByDescending(GetArmamentScore)
            .ThenByDescending(actor => actor.Health * 1_000 /
                Math.Max(1, actor.EffectiveMaximumHealth))
            .ThenBy(actor => actor.BleedingTicksRemaining)
            .ThenBy(actor => actor.Hunger + actor.Thirst + actor.Fatigue)
            .ThenBy(actor => actor.Id)
            .Take(capacity)
            .Select(actor => actor.Id)
            .ToArray();
    }

    private static int GetArmamentScore(ActorSnapshot actor)
    {
        var score = 0;
        if (actor.Equipment.HasFlag(PersonalEquipment.PrimitiveSling))
        {
            score += actor.PersonalStoneAmmo > 0 ? 8 : 1;
        }
        if (actor.Equipment.HasFlag(PersonalEquipment.StoneClub))
        {
            score += 4;
        }
        if (actor.Equipment.HasFlag(PersonalEquipment.FightingStick))
        {
            score += 3;
        }
        if (actor.Equipment.HasFlag(PersonalEquipment.BoneKnife))
        {
            score += 2;
        }
        return score;
    }
}
