using ObscureWare.D20Common;
using ObscureWare.ModernD20.Engine;

namespace ObscureWare.ModernD20.EffectBuilders
{
    public class DazedEffectBuilder : BaseEffectBuilder
    {
        private readonly uint _lengthInRounds;

        public DazedEffectBuilder(uint lengthInRounds) : base(EffectTimeFrameEnum.Temporary, EffectStackModeEnum.Stackable)
        {
            this._lengthInRounds = lengthInRounds;
        }

        public override AppliedCharacterEffect GetCharacterEntry(GlobalState state)
        {
            throw new System.NotImplementedException();
        }
    }
}