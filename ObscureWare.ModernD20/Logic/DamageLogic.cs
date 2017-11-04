using System;
using ObscureWare.D20Common;
using ObscureWare.ModernD20.EffectBuilders;
using ObscureWare.ModernD20.Engine;

namespace ObscureWare.ModernD20.Logic
{
    public class DamageLogic : LogicCore
    {
        private const int NON_LETHAL_DAMAGE_SAVING_THROW_DC = 15;
        private const int MASSIVE_DAMAGE_SAVING_THROW_DC = 15;

        public DamageLogic(ModernD20Library library) : base(library)
        {
            _massiveDamageThresholdEffectId = _library.GlobalDefinitions.FindEffectId(typeof(ImprovedDamageThresholdEffectBuilder));
            _immuneToCriticalDamageEffectId = _library.GlobalDefinitions.FindEffectId(typeof(ImmuneToCriticalDamageEffectBuilder));
            _disabledEffectId = _library.GlobalDefinitions.FindEffectId(typeof(DisabledEffectBuilder));
            _dyingEffectId = _library.GlobalDefinitions.FindEffectId(typeof(DyingEffectBuilder));
        }

        #region Massive Damage

        private readonly Guid _massiveDamageThresholdEffectId;
        private readonly Guid _immuneToCriticalDamageEffectId;

        /// <summary>
        /// A character’s massive damage threshold is equal to the character’s current Constitution score;
        /// it can be increased by taking the Improved Damage Threshold feat
        /// </summary>
        /// <param name="character"></param>
        /// <returns></returns>
        public int GetMassiveDamageThreshold(Character character)
        {
            return character.Abilities.GetAbilityModifier(AbilityEnum.Constitution) +
                   character.Effects.GetAppliedEffectValue(_massiveDamageThresholdEffectId) ?? 0;
        }

        /// <summary>
        /// Any time a character takes damage from a single hit that exceeds the character’s massive damage threshold, that damage is considered massive damage.
        /// </summary>
        /// <param name="character"></param>
        /// <param name="attackScore"></param>
        /// <returns></returns>
        public bool IsAttackMassive(Character character, uint attackScore)
        {
            return GetMassiveDamageThreshold(character) < attackScore;
        }

        /// <summary>
        /// When a character takes massive damage that doesn’t reduce his or her hit points to 0 or lower, the character must make a Fortitude save (DC 15).
        ///     If the character fails the save, the character’s hit point total is immediately reduced to –1.
        ///     If the save succeeds, the character suffers no ill effect beyond the loss of hit points.
        /// </summary>
        /// <param name="character"></param>
        /// <param name="attackScore"></param>
        /// <remarks>Creatures immune to critical hits are also immune to the effects of massive damage.</remarks>
        private void TryApplyMassiveDamage(Character character, uint attackScore)
        {
            bool isImmune = character.Effects.GetAppliedEffectValue(_immuneToCriticalDamageEffectId) != null;

            if (!isImmune && attackScore < character.CurrentHitPoints)
            {
                if (_library.SavingThrowsLogic.TestSavingThrow(SavingThrowEnum.Fortitude, character, MASSIVE_DAMAGE_SAVING_THROW_DC))
                {
                    ApplyNormalDamage(character, attackScore);
                }
                else
                {
                    ForceCharacterHitPointsValue(character, -1); // force Dying status
                }
            }
            else
            {
                ApplyNormalDamage(character, attackScore);
            }
        }

        #endregion

        #region Non-lethal Damage

        /// <summary>
        /// Nonlethal damage is dealt by unarmed attackers and some weapons.
        /// Melee weapons that deal lethal damage can be wielded so as to deal nonlethal damage,
        /// but the attacker takes a –4 penalty on attack rolls for trying to deal nonlethal damage instead of lethal damage.
        /// A ranged weapon that deals lethal damage can’t be made to deal nonlethal damage (unless it is used as an improvised melee weapon).
        /// 
        /// Nonlethal damage does not affect the target’s hit points.
        /// Instead, compare the amount of nonlethal damage from an attack to the target’s massive damage threshold.
        ///     - If the amount is less than the target’s massive damage threshold, the target is unaffected by the attack.
        ///     - If the damage equals or exceeds the target’s massive damage threshold, the target must make a Fortitude save (DC 15).
        ///         + If the target succeeds on the save, the target is dazed for 1 round.
        ///         + If the target fails, he or she is knocked unconscious for 1d4+1 rounds.
        /// </summary>
        /// <param name="character"></param>
        /// <param name="attackScore"></param>
        public void ApplyNonlethalDamage(Character character, uint attackScore) // expand more on attack type etc... or put it higher
        {
            if (IsAttackMassive(character, attackScore))
            {
                if (_library.SavingThrowsLogic.TestSavingThrow(SavingThrowEnum.Fortitude, character, NON_LETHAL_DAMAGE_SAVING_THROW_DC))
                {
                    ApplyEffect(character, new DazedEffectBuilder(1));
                }
                else
                {
                    ApplyEffect(character, new UnconsciousEffectBuilder(_library.FightRoller.Roll(DieEnum.D4) + 1));
                }
            }
        }

        #endregion

        #region Disabled

        private readonly Guid _disabledEffectId;
        
        /*

        When a character’s current hit points drop to exactly 0, the character is disabled.
        The character is not unconscious, but he or she is close to it.
        The character can only take a single move or attack action each turn (but not both, nor can the character take full-round actions). 
        The character can take nonstrenuous move actions without further injuring his or herself, 
        but if the character attacks or perform any other action the GM deems as strenuous, the character takes 1 point of damage after completing the act.
        Unless the activity increased the character’s hit points, the character is now at –1 hit points, and is dying.
        
        Healing that raises the character above 0 hit points makes him or her fully functional again, just as if the character had never been reduced to 0 or lower.
        
        A character can also become disabled when recovering from dying. In this case, it’s a step up along the road to recovery,
        and the character can have fewer than 0 hit points (see Stable Characters and Recovery).
        
        */

        // SG: No implementation yet, dunno how to extract it from top logic... Probably shall move it there anyway
        public void ApplyDisabledState(Character character)
        {
            if (!IsDisabled(character)) // already
            {
                ApplyEffect(character, new DisabledEffectBuilder());
            }
        }

        private bool IsDisabled(Character character)
        {
            return character.Effects.HasEffectApplied(_disabledEffectId);
        }

        #endregion

        #region Dying

        private readonly Guid _dyingEffectId;

        /*

        When a character’s current hit points drop below 0, the character is dying. A dying character has a current hit point total between –1 and –9 inclusive.
        A dying character immediately falls unconscious and can take no actions.
        A dying character loses 1 hit point every round. This continues until the character dies or becomes stable naturally or with help (see below).
        Dead (–10 hit points or lower)
        When a character’s current hit points drop to –10 or lower, he or she is dead. A character can also die if his or her Constitution is reduced to 0.
        
        */

        // SG: No implementation yet, dunno how to extract it from top logic... Probably shall implment on effects updates

        public void ApplyDyingState(Character character)
        {
            if (!IsDying(character)) // already
            {
                ApplyEffect(character, new DyingEffectBuilder());
            }
        }

        private bool IsDying(Character character)
        {
            return character.Effects.HasEffectApplied(_dyingEffectId);
        }

        #endregion

        /// <summary>
        /// 
        /// </summary>
        /// <param name="character"></param>
        /// <param name="attackScore"></param>
        public void ApplyPhysicalDamage(Character character, uint attackScore) // expand more on attack type etc... or put it higher
        {
            if (IsAttackMassive(character, attackScore))
            {
                TryApplyMassiveDamage(character, attackScore);
            }
            else
            {
                ApplyNormalDamage(character, attackScore);
            }
        }





        private void ApplyNormalDamage(Character character, uint attackScore)
        {
            throw new NotImplementedException();

            CheckCharacterCriticalStates(character);
        }

        private void ForceCharacterHitPointsValue(Character character, int i)
        {
            throw new NotImplementedException();

            CheckCharacterCriticalStates(character);
        }

        private void CheckCharacterCriticalStates(Character character)
        {
            if (character.CurrentHitPoints == 0)
            {
                ApplyDisabledState(character);
            }
            else if (character.CurrentHitPoints < 0)
            {
                ApplyDyingState(character);
            }
        }

    }
}
