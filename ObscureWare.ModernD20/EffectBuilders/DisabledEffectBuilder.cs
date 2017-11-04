using ObscureWare.D20Common;
using ObscureWare.ModernD20.Engine;

namespace ObscureWare.ModernD20.EffectBuilders
{
    public class DisabledEffectBuilder : BaseEffectBuilder
    {
        public DisabledEffectBuilder() : base(EffectTimeFrameEnum.Restorable, EffectStackModeEnum.NonStackable)
        {
            
        }

        public override AppliedCharacterEffect GetCharacterEntry(GlobalState state)
        {
            throw new System.NotImplementedException();
        }
    }
}