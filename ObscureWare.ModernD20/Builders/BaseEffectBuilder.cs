using System;
using ObscureWare.D20Common;
using ObscureWare.ModernD20.Engine;

namespace ObscureWare.ModernD20.EffectBuilders
{
    public abstract class BaseEffectBuilder
    {
        public Guid Id { get; private set; }
        public EffectStackModeEnum Stackable { get; set; }
        public EffectTimeFrameEnum EffectTimeFrame { get; }

        protected BaseEffectBuilder(EffectTimeFrameEnum effectTimeFrame, EffectStackModeEnum stackable)
        {
            this.Stackable = stackable;
            this.EffectTimeFrame = effectTimeFrame;

            // TODO: obtain ID and translations from global context
        }

        public abstract AppliedCharacterEffect GetCharacterEntry(GlobalState state);
        
        /// <summary>
        /// Returns translation message to be displayed when effect finishes / is removed.
        /// </summary>
        /// <returns></returns>
        public string GetRemovedMessage()
        {
            throw new NotImplementedException();
        }
    }
}