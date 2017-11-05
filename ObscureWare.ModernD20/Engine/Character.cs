namespace ObscureWare.ModernD20.Engine
{
    public class Character
    {
        public Character()
        {
            this.Abilities = new AbilitySet(this);
            this.Effects = new EffectsSet(this);
        }

        /// <summary>
        /// Every character has six basic Ability Scores.
        /// </summary>
        public AbilitySet Abilities { get; internal set; }

        public EffectsSet Effects { get; internal set; }

        private readonly int[] _savingThrowBonuses = new int[4]; // as usual, zero-index for version or control sum

        /// <summary>
        /// Current level of character.
        /// </summary>
        public uint Level { get;  }

        /// <summary>
        /// Age of the character
        /// </summary>
        public uint Age { get;  }

        /// <summary>
        /// Returns base saving throw bonus of given save type
        /// </summary>
        /// <param name="throwType"></param>
        /// <returns></returns>
        public int BaseSavingThrowBonus(SavingThrowEnum throwType)
        {
            return this._savingThrowBonuses[(int) throwType];
        }

        public uint MaxHitPoints { get; }

        public int CurrentHitPoints { get;  }

        public bool CanMove()
        {
            if (!this.Abilities.CanMove())
            {
                return false;
            }

            // TODO: check other conditions ?

            return true;
        }

        public bool IsAlive()
        {
            return this.Abilities.IsAlive() && this.CurrentHitPoints > -10;

            // TODO: check other conditions ?
        }

        /// <summary>
        /// Effects of Hit Point Damage
        /// At 0 hit points, a character is disabled.
        /// At from –1 to –9 hit points, a character is dying.
        /// At –10 or lower, a character is dead.
        /// </summary>
        public CharacterStatusEnum CharacterStatus
        {
            get
            {
                if (this.CurrentHitPoints > 0)
                    return CharacterStatusEnum.Normal;
                else if (this.CurrentHitPoints == 0)
                    return CharacterStatusEnum.Disabled; // TODO: not so simple - have to take into account recovery rules
                else if (this.CurrentHitPoints > -10)
                    return CharacterStatusEnum.Dying;
                else
                    return CharacterStatusEnum.Dead;
            }
        }

        #region Effects

        

        #endregion


    }
}