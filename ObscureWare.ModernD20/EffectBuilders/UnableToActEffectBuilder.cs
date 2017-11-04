using System;
using ObscureWare.D20Common;
using ObscureWare.ModernD20.Engine;

namespace ObscureWare.ModernD20.EffectBuilders
{
    /// <summary>
    /// Character is not able to perform any actions
    /// </summary>
    internal class UnableToActEffectBuilder : BaseEffectBuilder
    {
        public UnableToActEffectBuilder(EffectTimeFrameEnum timeFrame, int penaltyRounds) : base(timeFrame, EffectStackModeEnum.NonStackable)
        {
            throw new NotImplementedException();
        }

        public override AppliedCharacterEffect GetCharacterEntry(GlobalState state)
        {
            throw new NotImplementedException();
        }
    }
}