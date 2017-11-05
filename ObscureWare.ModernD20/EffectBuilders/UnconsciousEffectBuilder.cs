using ObscureWare.D20Common;
using ObscureWare.ModernD20.Engine;

namespace ObscureWare.ModernD20.EffectBuilders
{
    public class UnconsciousEffectBuilder : BaseEffectBuilder
    {
        private readonly uint _lengthInRounds;

        public UnconsciousEffectBuilder(uint lengthInRounds) : base(EffectTimeFrameEnum.Restorable, EffectStackModeEnum.Stackable)
        {
            this._lengthInRounds = lengthInRounds;
        }

        public override AppliedCharacterEffect GetCharacterEntry(GlobalState state)
        {
            throw new System.NotImplementedException();
        }
    }
}