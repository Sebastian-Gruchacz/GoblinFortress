using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ObscureWare.ModernD20.BaseCharacterClasses;
using ObscureWare.ModernD20.Engine;

namespace ObscureWare.ModernD20.CharacterCreation
{
    interface ICharacterCreator
    {
        Character CreateRandom();

        Character CreateSpecificClass(Type characterClass);

        Character CreateSpecificSkillSet(params Type[] skillSet);
    }
}
