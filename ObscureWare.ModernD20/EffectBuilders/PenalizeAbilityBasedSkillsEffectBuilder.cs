using System;
using ObscureWare.D20Common;
using ObscureWare.ModernD20.Engine;

namespace ObscureWare.ModernD20.EffectBuilders
{
    public class PenalizeAbilityBasedSkillsEffectBuilder : BaseEffectBuilder
    {
        public PenalizeAbilityBasedSkillsEffectBuilder(AbilityEnum affectedAbility, EffectTimeFrameEnum timeFrame, int penaltyScore) 
            : base(timeFrame, EffectStackModeEnum.Stackable)
        {
            throw new NotImplementedException();
        }

        public override AppliedCharacterEffect GetCharacterEntry(GlobalState state)
        {
            throw new NotImplementedException();
        }
    }
}