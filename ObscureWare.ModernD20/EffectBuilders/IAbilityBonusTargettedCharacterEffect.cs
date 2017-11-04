using ObscureWare.D20Common;

namespace ObscureWare.ModernD20.EffectBuilders
{
    public interface IAbilityBonusTargettedCharacterEffect
    {
        AbilityEnum AffectedAbility { get; }

        EffectVectorEnum EffectVector { get; }
    }
}