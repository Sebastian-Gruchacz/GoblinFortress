using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class RaidAutoAssignmentPolicyTests
{
    [Fact]
    public void SelectPrefersArmedThenHealthyRestedAdults()
    {
        var actors = new[]
        {
            Actor(1, PersonalEquipment.StoneClub, health: 50),
            Actor(2, PersonalEquipment.StoneClub, hunger: 20),
            Actor(3, PersonalEquipment.StoneClub),
            Actor(4, PersonalEquipment.PrimitiveSling, ammo: 1),
            Actor(5, PersonalEquipment.PrimitiveSling | PersonalEquipment.StoneClub,
                juvenile: true, ammo: 1),
            Actor(6, PersonalEquipment.PrimitiveSling, health: 0, ammo: 1),
        };

        var selected = RaidAutoAssignmentPolicy.Select(actors, capacity: 4);

        Assert.Equal(
            new[] { new EntityId(4), new EntityId(3), new EntityId(2), new EntityId(1) },
            selected);
    }

    [Fact]
    public void SelectHonorsCapacityAndAllowsAnEmptyResult()
    {
        var actors = new[]
        {
            Actor(1, PersonalEquipment.StoneClub, juvenile: true),
            Actor(2, PersonalEquipment.None, health: 0),
        };

        Assert.Empty(RaidAutoAssignmentPolicy.Select(actors, capacity: 5));
        Assert.Empty(RaidAutoAssignmentPolicy.Select(actors, capacity: 0));
    }

    private static ActorSnapshot Actor(
        ulong id,
        PersonalEquipment equipment,
        int health = 100,
        int hunger = 0,
        bool juvenile = false,
        int ammo = 0) =>
        new()
        {
            Id = new EntityId(id),
            Name = $"Goblin {id}",
            Equipment = equipment,
            Health = health,
            EffectiveMaximumHealth = 100,
            Hunger = hunger,
            IsJuvenile = juvenile,
            PersonalStoneAmmo = ammo,
        };
}
