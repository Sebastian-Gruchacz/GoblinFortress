using ObscureWare.D20Common;

namespace ObscureWare.ModernD20.EffectBuilders
{
    public interface IAbilityTargettedCharacterEffect
    {
        AbilityEnum AffectedAbility { get; }

        EffectVectorEnum EffectVector { get; }
    }
}