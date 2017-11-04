using System;
using ObscureWare.D20Common;
using ObscureWare.ModernD20.Engine;

namespace ObscureWare.ModernD20.EffectBuilders
{
    internal class PenalizeDefenseEffectBuilder : BaseEffectBuilder
    {
        public PenalizeDefenseEffectBuilder(EffectTimeFrameEnum timeFrame, int penaltyRounds, int penaltyScore) : base(timeFrame, EffectStackModeEnum.Stackable)
        {
            throw new NotImplementedException();
        }

        public override AppliedCharacterEffect GetCharacterEntry(GlobalState state)
        {
            throw new NotImplementedException();
        }
    }
}