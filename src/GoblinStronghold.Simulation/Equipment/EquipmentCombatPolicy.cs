namespace GoblinStronghold.Simulation.Equipment;

public static class EquipmentCombatPolicy
{
    public static int GetBestMeleeDamageBonus(
        PersonalEquipment equipment,
        GoblinPrimitiveEquipmentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var best = 0;
        Consider(PersonalEquipment.BoneKnife, settings.BoneKnifeDamageBonus);
        Consider(PersonalEquipment.FightingStick, settings.FightingStickDamageBonus);
        Consider(PersonalEquipment.StoneClub, settings.StoneClubDamageBonus);
        Consider(PersonalEquipment.WoodenAxe, settings.WoodenAxeDamageBonus);
        Consider(PersonalEquipment.PrimitivePickaxe, settings.PrimitivePickaxeDamageBonus);
        Consider(PersonalEquipment.ReinforcedPickaxe, settings.ReinforcedPickaxeDamageBonus);
        Consider(PersonalEquipment.WoodenHammer, settings.WoodenHammerDamageBonus);
        return best;

        void Consider(PersonalEquipment item, int damageBonus)
        {
            if (equipment.HasFlag(item))
            {
                best = Math.Max(best, damageBonus);
            }
        }
    }
}
