using System.Collections.Generic;
using ObscureWare.D20Common;
using ObscureWare.ModernD20.BaseCharacterClasses;
using ObscureWare.ModernD20.Engine;

namespace ObscureWare.ModernD20.CharacterCreation
{
    public class HeroCharacterCreator : BaseCharacterCreator
    {
        private const int MAX_RANDOM_CHARACTER_AGE = 50;
        private const int MIN_RANDOM_CHARACTER_AGE = 21;

        public HeroCharacterCreator(IRoller characterRoller) : base(characterRoller)
        {
        }


        protected override IEnumerable<BaseCharacterClass> GetAvailableBaseClasses()
        {
            throw new System.NotImplementedException();
        }

        protected override uint GenerateAge()
        {
            return (uint)_characterRoller.CoreGenerator.NextInt(MIN_RANDOM_CHARACTER_AGE, MAX_RANDOM_CHARACTER_AGE);
        }

        protected override int GenerateStartingActionPoints(Character generatedCharacter, BaseCharacterClass charClass)
        {
            return GlobalOperators.Round(charClass.GetFirstLevelSkillPointsGained(generatedCharacter) * 0.5m);
        }
    }
}