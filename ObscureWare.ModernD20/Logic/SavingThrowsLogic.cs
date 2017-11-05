using System;
using ObscureWare.D20Common;
using ObscureWare.ModernD20.Engine;

namespace ObscureWare.ModernD20.Logic
{
    /// <summary>
    /// Generally, when a hero is subject to an unusual or magical attack, he or she gets a saving throw to avoid or reduce the effect.
    /// </summary>
    public class SavingThrowsLogic : LogicCore
    {
        public SavingThrowsLogic(ModernD20Library library) : base(library)
        {
        }

        /// <summary>
        /// A character’s saving throw bonus is:
        ///     Base save bonus + ability modifier
        /// </summary>
        /// <param name="savingThrow"></param>
        /// <param name="character"></param>
        /// <returns></returns>
        public int SavingThrowBonus(SavingThrowEnum savingThrow, Character character)
        {
            switch (savingThrow)
            {
                case SavingThrowEnum.Fortitude:
                    return character.BaseSavingThrowBonus(savingThrow) + character.Abilities.GetAbilityModifier(AbilityEnum.Constitution);
                case SavingThrowEnum.Reflex:
                    return character.BaseSavingThrowBonus(savingThrow) + character.Abilities.GetAbilityModifier(AbilityEnum.Dexterity);
                case SavingThrowEnum.Will:
                    return character.BaseSavingThrowBonus(savingThrow) + character.Abilities.GetAbilityModifier(AbilityEnum.Wisdom);
                default:
                    throw new ArgumentOutOfRangeException(nameof(savingThrow), savingThrow, null);
            }
        }

        /// <summary>
        /// Like an attack roll, a saving throw is a 1d20 roll plus a bonus based on the hero’s class and level (the hero’s base save bonus) and an ability modifier.
        /// A natural 1 (the d20 comes up 1) on a saving throw is always a failure.
        /// A natural 20 (the d20 comes up 20) is always a success.
        /// </summary>
        /// <param name="savingThrow"></param>
        /// <param name="character"></param>
        /// <param name="testDifficulity"></param>
        /// <returns></returns>
        public bool TestSavingThrow(SavingThrowEnum savingThrow, Character character, uint testDifficulity)
        {
            uint cleanRoll = this._library.FightRoller.Roll(DieEnum.D20);
            if (cleanRoll == 1)
            {
                this._library.CoreNotifications.ReportCharacterFailedSaveThrow(character, savingThrow, cleanRoll);
                return false;
            }
            else if (cleanRoll == 20)
            {
                this._library.CoreNotifications.ReportCharacterPassedSaveThrow(character, savingThrow, cleanRoll);
                return true;
            }

            bool saved = cleanRoll + this.SavingThrowBonus(savingThrow, character) >= testDifficulity;

            if (saved)
            {
                this._library.CoreNotifications.ReportCharacterPassedSaveThrow(character, savingThrow, cleanRoll);
            }
            else
            {
                this._library.CoreNotifications.ReportCharacterFailedSaveThrow(character, savingThrow, cleanRoll);
            }

            return saved;
        }
    }
}
