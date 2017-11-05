using System.Collections.Generic;
using ObscureWare.D20Common;
using ObscureWare.ModernD20.BaseCharacterClasses;

namespace ObscureWare.ModernD20.CharacterCreation
{
    /// <summary>
    /// Child creator stores any information about destined class, but until it become young adult (12yo) it's being covered by wrapper class
    /// </summary>
    public class ChildCharacterCreator : BaseCharacterCreator
    {
        private const int MAX_RANDOM_CHARACTER_AGE = 11;
        private const int MIN_RANDOM_CHARACTER_AGE = 0;

        public ChildCharacterCreator(IRoller characterRoller) : base(characterRoller)
        {
        }

        protected override IEnumerable<BaseCharacterClass> GetAvailableBaseClasses()
        {
            throw new System.NotImplementedException();
        }

        protected override uint GenerateAge()
        {
            return (uint) this._characterRoller.CoreGenerator.NextInt(MIN_RANDOM_CHARACTER_AGE, MAX_RANDOM_CHARACTER_AGE);
        }

    }
}