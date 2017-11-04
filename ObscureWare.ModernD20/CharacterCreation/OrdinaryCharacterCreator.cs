using System.Collections.Generic;
using ObscureWare.D20Common;
using ObscureWare.ModernD20.BaseCharacterClasses;

namespace ObscureWare.ModernD20.CharacterCreation
{
    public class OrdinaryCharacterCreator : BaseCharacterCreator
    {
        public OrdinaryCharacterCreator(IRoller characterRoller) : base(characterRoller)
        {
        }

        protected override IEnumerable<BaseCharacterClass> GetAvailableBaseClasses()
        {
            throw new System.NotImplementedException();
        }
    }
}