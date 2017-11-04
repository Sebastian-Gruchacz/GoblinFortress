using System;
using ObscureWare.D20Common;
using ObscureWare.ModernD20.Engine;

namespace ObscureWare.ModernD20.EffectBuilders
{
    public class ImprovedDamageThresholdEffectBuilder : BaseEffectBuilder
    {
        public override AppliedCharacterEffect GetCharacterEntry(GlobalState state)
        {
            throw new NotImplementedException();
        }

        public ImprovedDamageThresholdEffectBuilder() : base(EffectTimeFrameEnum.Permanent, EffectStackModeEnum.Stackable)
        {
        }
    }
}
