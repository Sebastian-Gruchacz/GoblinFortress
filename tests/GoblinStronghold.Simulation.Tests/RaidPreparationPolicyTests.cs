using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class RaidPreparationPolicyTests
{
    [Fact]
    public void ShortGuardAttackUsesLightAutomaticLoadout()
    {
        var definitions = SimulationDefinitions.Foundation;

        var profile = RaidPreparationPolicy.ResolveAutomatic(
            RaidDirective.AttackGuards,
            definitions,
            PersonalEquipment.PrimitiveWaterskin);

        Assert.Equal(RaidPreparationMode.Automatic, profile.Mode);
        Assert.Equal(Math.Min(definitions.PersonalFoodCapacity, 1), profile.FoodTarget);
        Assert.Equal(definitions.PersonalWaterCapacity, profile.WaterTarget);
        Assert.Equal(Math.Min(definitions.RangedCombat.HandAmmoCapacity, 1),
            profile.StoneAmmoTarget);
        Assert.False(profile.KeepCargoHandsFree);
    }

    [Fact]
    public void ExtendedDemolitionRaidUsesFullSuppliesAndRecommendsPickaxe()
    {
        var definitions = SimulationDefinitions.Foundation;
        var directives = RaidDirective.AttackNonFleeing |
            RaidDirective.LootSupplies |
            RaidDirective.DemolishBuildings |
            RaidDirective.ContinueWhileTargetsVisible;

        var profile = RaidPreparationPolicy.ResolveAutomatic(
            directives,
            definitions,
            PersonalEquipment.PrimitiveSling);

        Assert.Equal(definitions.PersonalFoodCapacity, profile.FoodTarget);
        Assert.Equal(definitions.PersonalWaterCapacity, profile.WaterTarget);
        Assert.Equal(definitions.RangedCombat.SlingAmmoCapacity, profile.StoneAmmoTarget);
        Assert.Equal(PersonalEquipment.PrimitivePickaxe, profile.PreferredEquipment);
        Assert.True(profile.KeepCargoHandsFree);
    }
}
