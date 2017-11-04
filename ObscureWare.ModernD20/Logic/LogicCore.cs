using ObscureWare.ModernD20.EffectBuilders;
using ObscureWare.ModernD20.Engine;

namespace ObscureWare.ModernD20.Logic
{
    public abstract class LogicCore
    {
        protected readonly ModernD20Library _library;

        protected LogicCore(ModernD20Library library)
        {
            _library = library;
        }

        public void ApplyEffect(Character character, BaseEffectBuilder effectBuilder)
        {
            var effectInfo = effectBuilder.GetCharacterEntry(_library.GlobalState);
            character.Effects.ApplyEffect(effectInfo);

            _library.CoreNotifications.ReportCharacterReceivingEffect(character, effectInfo, effectBuilder);
        }

        public void RemoveEffect(Character character, AppliedCharacterEffect effect)
        {
            character.Effects.RemoveEffect(effect);

            _library.CoreNotifications.ReportCharacterLostEffect(character, effect);
        }
    }
}
