using System;
using ObscureWare.D20Common;
using ObscureWare.ModernD20.Engine;

namespace ObscureWare.ModernD20.EffectBuilders
{
    internal class DisabledSkillEffectBuilder : BaseEffectBuilder
    {
        public DisabledSkillEffectBuilder(Type type, EffectTimeFrameEnum effectTimeFrame):base(effectTimeFrame, EffectStackModeEnum.NonStackable)
        {
            throw new NotImplementedException();
        }

        public override AppliedCharacterEffect GetCharacterEntry(GlobalState state)
        {
            throw new NotImplementedException();
        }
    }
}