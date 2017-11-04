using System;
using System.Collections.Generic;
using ObscureWare.Common;
using ObscureWare.D20Common;
using ObscureWare.ModernD20.BaseCharacterClasses;
using ObscureWare.ModernD20.Engine;

namespace ObscureWare.ModernD20.CharacterCreation
{
    public abstract class BaseCharacterCreator: ICharacterCreator
    {
        protected readonly IRoller _characterRoller;

        private const int MAX_RANDOM_CHARACTER_AGE = 60;
        private const int MIN_RANDOM_CHARACTER_AGE = 16;

        protected BaseCharacterCreator(IRoller characterRoller)
        {
            _characterRoller = characterRoller;
        }

        public Character CreateRandom()
        {
            throw new NotImplementedException();
        }

        public Character CreateSpecificClass(Type characterClass)
        {
            throw new NotImplementedException();
        }

        public Character CreateSpecificSkillSet(params Type[] skillSet)
        {
            throw new NotImplementedException();
        }

        protected abstract IEnumerable<BaseCharacterClass> GetAvailableBaseClasses();

        protected virtual uint GenerateAge()
        {
            return (uint)_characterRoller.CoreGenerator.NextInt(MIN_RANDOM_CHARACTER_AGE, MAX_RANDOM_CHARACTER_AGE);
        }

        /// <summary>
        /// We have only humns here...
        /// </summary>
        /// <returns></returns>
        protected virtual Gender GenerateGender()
        {
            return _characterRoller.Roll(DieEnum.D4) > 2 ? Gender.Male : Gender.Female;
        }

        protected virtual string GenerateFirstName(Gender gender, Guid nationalityId)
        {
            // TODO: real implementation
            switch (gender)
            {
                case Gender.Male:
                    return "Stefan";
                case Gender.Female:
                    return "Jolka";
                default:
                    throw new ArgumentOutOfRangeException(nameof(gender), gender, null);
        }
        }

        protected virtual string GenerateLastName(Gender gender, Guid nationalityId)
        {
            // TODO: real implementation
            switch (gender)
            {
                case Gender.Male:
                    return "Todoicki";
                case Gender.Female:
                    return "Todoicka";
                default:
                    throw new ArgumentOutOfRangeException(nameof(gender), gender, null);
            }
        }

        protected virtual int GenerateStartingHitPoints(BaseCharacterClass charClass)
        {
            return charClass.HitDie.GetMax(); // TODO: or roll?
        }

        protected virtual Guid GenerateNationality()
        {
            throw new NotImplementedException();
        }

        protected virtual int GenerateStartingActionPoints(Character generatedCharacter, BaseCharacterClass charClass)
        {
            return 0;
        }

        protected virtual uint[] GenerateAbilityScoresArray(BaseCharacterClass charClass)
        {
            uint[] array = new uint[] {15, 14, 13, 12, 10, 8};
            array.Shuffle(_characterRoller.CoreGenerator);
            return array;
        }

        protected virtual void AdvanceNextLevel(Character character)
        {
            throw new NotImplementedException();
        }
    }
}