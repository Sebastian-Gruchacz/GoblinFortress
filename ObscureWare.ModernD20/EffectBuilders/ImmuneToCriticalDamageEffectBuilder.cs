using ObscureWare.D20Common;
using ObscureWare.ModernD20.Engine;

namespace ObscureWare.ModernD20.EffectBuilders
{
    public class ImmuneToCriticalDamageEffectBuilder : BaseEffectBuilder
    {
        public override AppliedCharacterEffect GetCharacterEntry(GlobalState state)
        {
            throw new System.NotImplementedException();
        }

        public ImmuneToCriticalDamageEffectBuilder() : base(EffectTimeFrameEnum.Permanent, EffectStackModeEnum.Stackable)
        {
        }
    }
}