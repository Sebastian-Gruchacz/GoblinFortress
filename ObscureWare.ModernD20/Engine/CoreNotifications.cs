using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ObscureWare.D20Common;
using ObscureWare.ModernD20.EffectBuilders;

namespace ObscureWare.ModernD20.Engine
{
    public interface ICoreNotifications
    {
        void ReportCharacterTakingDamage(Character character, DamageInfo damageInfo);

        void ReportCharacterReceivingEffect(Character character, AppliedCharacterEffect effectInfo, BaseEffectBuilder effectBuilder);
        void ReportCharacterLostEffect(Character character, AppliedCharacterEffect effect);

        void ReportCharacterDead(Character character, DeathReason deathReason);

        // void Report

        void ReportCharacterFailedSaveThrow(Character character, SavingThrowEnum savingThrow, uint cleanRoll);
        void ReportCharacterPassedSaveThrow(Character character, SavingThrowEnum savingThrow, uint cleanRoll);


        void ReportCharacterAbilityRestored(Character character, AbilityEnum affectedAbility);
        
    }

    public class DeathReason
    {
    }

    public class DamageInfo
    {
    }
}
