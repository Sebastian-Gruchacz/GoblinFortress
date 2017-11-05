using System;
using ObscureWare.D20Common;
using ObscureWare.ModernD20;
using ObscureWare.ModernD20.EffectBuilders;
using ObscureWare.ModernD20.Engine;

namespace D20Editor
{
    internal class DesignTimeNotifier : ICoreNotifications
    {
        private readonly FormMain _formMain;

        public DesignTimeNotifier(FormMain formMain)
        {
            this._formMain = formMain;
        }

        public void ReportCharacterTakingDamage(Character character, DamageInfo damageInfo)
        {
            throw new NotImplementedException();
        }

        public void ReportCharacterReceivingEffect(Character character, AppliedCharacterEffect effectInfo,
            BaseEffectBuilder effectBuilder)
        {
            throw new NotImplementedException();
        }

        public void ReportCharacterLostEffect(Character character, AppliedCharacterEffect effect)
        {
            throw new NotImplementedException();
        }

        public void ReportCharacterDead(Character character, DeathReason deathReason)
        {
            throw new NotImplementedException();
        }

        public void ReportCharacterFailedSaveThrow(Character character, SavingThrowEnum savingThrow, uint cleanRoll)
        {
            throw new NotImplementedException();
        }

        public void ReportCharacterPassedSaveThrow(Character character, SavingThrowEnum savingThrow, uint cleanRoll)
        {
            throw new NotImplementedException();
        }

        public void ReportCharacterAbilityRestored(Character character, AbilityEnum affectedAbility)
        {
            throw new NotImplementedException();
        }
    }
}